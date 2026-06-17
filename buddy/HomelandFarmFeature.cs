using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;

namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        private const bool HomelandFarmLogsEnabled = MasterLogHomelandFarm;
        private const float HomelandFarmDefaultWaterRadius = 30f;
        private const float HomelandFarmMinWaterRadius = 1f;
        private const float HomelandFarmMaxWaterRadius = 80f;
        private const float HomelandFarmRadiusSaveDebounceSeconds = 0.5f; // delay after slider settles before saving
        private const int HomelandFarmBatchLimit = 18;
        private static readonly string[] HomelandFarmCropBoxLinkMembers =
        {
            "cropNetId", "CropNetId", "childCropNetId", "linkedCropNetId", "LinkedCropNetId",
            "plantNetId", "PlantNetId"
        };
        private const float HomelandFarmCropBoxWorldMatchRadius = 0.35f;
        // Slightly wider gate for the injective occupancy backup pass: catches an on-box crop whose
        // position jittered just past the tight match radius, while still well under the 1m crop-box
        // grid spacing so a mature ground plant near the grid cannot claim a box it does not occupy.
        private const float HomelandFarmCropBoxOccupancyMatchRadius = 0.6f;
        // Cast batch = cells per watering action. Read from TableData.TableModes[mode].num where
        // mode = HobbyProtocolManager.TryGetHobbySkillParam(HobbySkillEnum.Water)[0].
        // Skill levels: 1 (default), 3, 6, 9. Match to player's current skill.
        private const int HomelandFarmCastBatchDefault = 9;
        // Crop-box sow put-zone slot: levelObjectNetId = (slot << 32) | planterNetId. Crop boxes use
        // slot 2 (craft raycast put-zone), per HOMELAND_SOW_ALIGNMENT.md.
        private const int HomelandFarmCropBoxCraftPutZoneSlot = 2;
        // Game ToolType ids — equipped via HoldToolCommand / ToolSystem.SetHandhold, not backpack AddHolder.
        private const int HomelandFarmAxeToolTypeId = 1;
        private const int HomelandFarmSprinklerToolTypeId = 2;
        private const int HomelandFarmRodToolTypeId = 3;
        private const int HomelandFarmBirdScannerToolTypeId = 4;
        private const int HomelandFarmNetToolTypeId = 5;
        private const int HomelandFarmPadToolTypeId = 6;
        private const int HomelandFarmHolderSystemHoldTool = 3; // EHolderSystem.HoldTool
        private const float HomelandFarmCommandDelaySeconds = 0.35f;
        private const float HomelandFarmWaterCommandDelaySeconds = 0.1f;
        private const int HomelandFarmFertilizationTypeCrop = 2;
        private const int HomelandFarmActionErrorSuccess = 0;
        private const int HomelandFarmActionErrorBusy = 1;
        private const float HomelandFarmActionCooldownSeconds = 1.5f;
        private const float HomelandFarmHarvestDelaySeconds = 0f;
        private const float HomelandFarmCollectSeedDelaySeconds = 0f;
        private const float HomelandFarmWeedDelaySeconds = 0f;
        // Auto farming (time-scheduled model):
        // - After each sow, rebuild the crop-netId cache once (one radius scan). Between sows we
        //   only re-read stage/hasWeed per cached netId (cheap directed poll, no radius scan).
        // - Sleep is driven by the crops' exact maturity (FarmUtil: mature = sowTime + ripeGrowTime
        //   - growTime). Coarse weeding while far from ripe; aggressive 1s weeding in the final minute.
        private const float HomelandFarmAutoDiscoveryDelaySeconds = 2.5f; // let server register just-sown crops
        private const long HomelandFarmAutoFinalMinuteSeconds = 60L;      // "final minute" threshold
        private const float HomelandFarmAutoFinalWeedIntervalSeconds = 1f;
        private const float HomelandFarmAutoCoarseWeedIntervalSeconds = 30f;
        private const float HomelandFarmAutoEmptyRetrySeconds = 5f;       // cache empty + seeds left, sow found nothing
        // Minimum gap between sow passes. The server takes a moment to register sown crops; sowing
        // again before that re-sows the same boxes → OnBuildSeedResult MaxPlantCountLimit.
        private const float HomelandFarmAutoSowCooldownSeconds = 15f;
        // After a partial harvest (some crops ripe, others still growing) the freed boxes should be
        // re-sown promptly instead of sleeping until the rest ripen. Only do this when the soonest
        // remaining maturity is beyond this many seconds — otherwise the rest ripen so soon it's
        // cheaper to harvest+re-sow them together on the next wake.
        private const long HomelandFarmAutoPostHarvestResowThresholdSeconds = 5L;
        private const int HomelandFarmMaxTotalWaterLevel = 5;
        // Max guest GUID entries in waterGuids/friends; at capacity the server rejects visitor water.
        private const int HomelandFarmMaxVisitorWaterSlots = 5;
        private const int HomelandFarmDefaultPlantWaterMode = 0;
        private const int HomelandFarmMaxSpatialLevelObjectEntries = 1024;
        private const int HomelandFarmMaxAuraFarmEntityInspect = 8192;
        private const int HomelandFarmMaxAuraFarmComponentChecks = 1536;
        private const int HomelandFarmMaxAuraFarmSpatialCandidates = 256;
        private const int HomelandFarmMaxAuraFarmSpatialVerifyCount = 24;
        private const float HomelandFarmAuraSpatialVerifyBudgetSeconds = 2.5f;
        // Hard wall-clock cap on the synchronous AuraEntities collection pass so a large
        // loaded-entity set (~4096) can never freeze the main thread for seconds.
        private const float HomelandFarmAuraSpatialCollectBudgetSeconds = 0.75f;
        private const float HomelandFarmAuraProximityComponentScanBudgetSeconds = 8f;
        private const float HomelandFarmWaterLogProximityBudgetSeconds = 2f;
        // Dense homelands can pack 4000+ entities inside the scan radius. With a 512 inspect cap
        // (distance-sorted), crop boxes that sort behind 512 nearer non-farm entities were never
        // inspected → intermittent undercount. Classification is cheap (~8ms/512 measured), so
        // raise the cap to cover the whole nearby set; the time budget still bounds worst cases.
        private const int HomelandFarmMaxAuraProximityComponentInspect = 8192;
        private const int HomelandFarmMaxRegisteredFarmTargets = 512;
        private const int HomelandFarmHarvestFramePaceBatch = 4;
        private const float HomelandFarmAuraComponentClassResolveRetrySeconds = 30f;

        private enum HomelandFarmWaterMode
        {
            InRadius,
            Own,
            Friends,
            Unwatered
        }

        private enum HomelandFarmStorageSource
        {
            Backpack,
            Warehouse,
            Both
        }

        private sealed class HomelandFarmTarget
        {
            public uint NetId;
            public uint OwnerId;
            public bool IsCropBox;
            public bool NeedsWater;
            public Vector3 Position;
        }

        private sealed class HomelandFarmInventoryItem
        {
            public int StaticId;
            public uint NetId;
            public int Count;
            public string Label;
        }

        private const int HomelandFarmBackpackStorageType = 1;
        private const int HomelandFarmWarehouseStorageType = 2;
        private const int HomelandFarmFertilizerEffectGrowthValue = 0;
        private const int HomelandFarmFertilizerEffectGrowthRate = 1;
        private const int HomelandFarmFertilizerEffectGrowthProduct = 2;
        private const int HomelandFarmPutZoneFlagCropland = 0x800;
        private static readonly string[] HomelandFarmStorageNames = { "Backpack", "Warehouse" };

        private float homelandFarmWaterRadius = HomelandFarmDefaultWaterRadius;
        // Debounced persistence of the radius slider: sliders fire every frame while dragging, so we
        // save to config a short moment after the value settles instead of on every change.
        private float homelandFarmWaterRadiusLastSeen = -1f;
        private bool homelandFarmWaterRadiusSavePending = false;
        private float homelandFarmWaterRadiusSaveAt = 0f;
        private string homelandFarmLastStatus = "homeland_farm.status_idle";
        private object homelandFarmCoroutine = null;
        private object homelandFarmWarmupCoroutine = null;
        private bool homelandFarmWarmupStarted = false;
        private bool homelandFarmWarmupComplete = false;
        private bool homelandFarmSowManagedReflectionAttempted = false;
        private bool homelandFarmComponentRadiusWarned = false;
        private bool homelandFarmInteropZeroLoadLogged = false;

        private sealed class HomelandFarmRegisteredFarmTarget
        {
            public uint NetId;
            public Vector3 LastPosition;
            public bool IsCropBox;
            public float RegisteredAt;
        }

        private readonly Dictionary<uint, HomelandFarmRegisteredFarmTarget> homelandFarmRegisteredFarmTargets =
            new Dictionary<uint, HomelandFarmRegisteredFarmTarget>();
        private float homelandFarmBusyUntil = 0f;

        private HomelandFarmStorageSource homelandFarmSeedStorage = HomelandFarmStorageSource.Both;
        private HomelandFarmStorageSource homelandFarmFertStorage = HomelandFarmStorageSource.Both;
        private readonly List<HomelandFarmInventoryItem> homelandFarmScannedSeeds = new List<HomelandFarmInventoryItem>();
        private readonly List<HomelandFarmInventoryItem> homelandFarmScannedFertilizers = new List<HomelandFarmInventoryItem>();
        private int homelandFarmSelectedSeedIndex = 0;
        private int homelandFarmSelectedFertilizerIndex = 0;
        private float homelandFarmSeedsCacheTime = 0f;
        private float homelandFarmFertilizersCacheTime = 0f;

        // --- Auto farming (capture planters in radius, then loop sow -> weed -> harvest) ---
        private bool homelandFarmAutoCaptured = false;
        private Vector3 homelandFarmAutoCenter = Vector3.zero;
        private float homelandFarmAutoCaptureRadius = 0f;
        private int homelandFarmAutoPlanterCount = 0;
        private int homelandFarmAutoCaptureExcludedOutsideRadius = 0;
        private bool homelandFarmAutoRunning = false;
        private int homelandFarmAutoSowCount = 0;
        // Crop netIds discovered after sow; polled directly each tick (no radius re-scan).
        private readonly List<uint> homelandFarmAutoCropNetIds = new List<uint>();
        // Harvest was already sent for these netIds this auto-farm run. Client PlantItemData often
        // lingers at stage 4 after the server cleared the box — exclude from cache/occupied scans.
        private readonly HashSet<uint> homelandFarmAutoHarvestedNetIds = new HashSet<uint>();
        // Planter boxes sown this generation but not yet visible to the crop scan / occupied check.
        private readonly HashSet<uint> homelandFarmAutoPendingSowBoxNetIds = new HashSet<uint>();
        private readonly Dictionary<uint, ulong> homelandFarmResolvedPutZoneByPlanterNetId = new Dictionary<uint, ulong>();
        // While set, every radius scan (sow slots, weed, harvest) centers on the captured
        // planter zone instead of the live player position, so the player may drift slightly.
        private Vector3? homelandFarmScanCenterOverride = null;
        private readonly Dictionary<uint, int> homelandFarmSyncedManureVisualByCropNetId = new Dictionary<uint, int>();
        private sealed class HomelandFarmPlanterSowAnchor
        {
            public Vector3 WorldPosition;
            public Quaternion WorldRotation;
            public ulong PutZoneId;
        }

        private readonly Dictionary<uint, HomelandFarmPlanterSowAnchor> homelandFarmPlanterSowAnchorByNetId =
            new Dictionary<uint, HomelandFarmPlanterSowAnchor>();
        private readonly Dictionary<ulong, Vector3> homelandFarmPutZoneWorldPositionById = new Dictionary<ulong, Vector3>();
        private readonly Dictionary<ulong, Quaternion> homelandFarmPutZoneWorldRotationById = new Dictionary<ulong, Quaternion>();
        private readonly HashSet<uint> homelandFarmLastScanCropBoxNetIds = new HashSet<uint>();
        // Short-lived reuse for back-to-back manual radius actions (water then weed, etc.).
        private readonly HashSet<uint> homelandFarmLastManualRadiusCollectNetIds = new HashSet<uint>();
        private Vector3 homelandFarmLastManualRadiusCollectCenter = Vector3.zero;
        private float homelandFarmLastManualRadiusCollectRadius = 0f;
        private float homelandFarmLastManualRadiusCollectAt = -999f;
        private uint homelandFarmLastManualRadiusCollectFieldOwner = 0U;
        private const float HomelandFarmManualRadiusCollectReuseSeconds = 12f;
        private const float HomelandFarmManualRadiusCollectCenterTolerance = 3f;
        // Negative-only: never cache component handles (stale IntPtr → native AV on reuse).
        private readonly HashSet<string> homelandFarmAuraComponentMissCache = new HashSet<string>(StringComparer.Ordinal);

        // GenSimpleConfirmOption wire y on homeland crop grid (ReducePrecision).
        private const float HomelandFarmCropSowFieldLocalY = 0.06f;

        private bool homelandFarmReflectionResolved = false;
        private bool homelandFarmManagedReflectionReady = false;
        private bool homelandFarmAuraReflectionReady = false;
        private bool homelandFarmReflectionUnavailable = false;
        private string homelandFarmReflectionUnavailableStatus = string.Empty;
        private bool homelandFarmInteropAssembliesLoaded = false;
        private float homelandFarmNextInteropLoadAttemptAt = 0f;
        private const float HomelandFarmInteropLoadRetryIntervalSeconds = 5f;
        private bool homelandFarmInteropMissingDirLogged = false;
        private float homelandFarmNextReflectionRetryAt = 0f;
        private bool homelandFarmManagedReflectionUnavailable = false;
        private string homelandFarmManagedReflectionUnavailableStatus = string.Empty;
        private bool homelandFarmScannerUnavailableLogged = false;

        private IntPtr homelandFarmAuraCropWaterPlant2Method = IntPtr.Zero;
        private IntPtr homelandFarmAuraCropWaterPlant3Method = IntPtr.Zero;
        private IntPtr homelandFarmAuraCropPickPlantMethod = IntPtr.Zero;
        private IntPtr homelandFarmAuraCropWeedMethod = IntPtr.Zero;
        private IntPtr homelandFarmAuraCropAddManureMethod = IntPtr.Zero;
        private IntPtr homelandFarmAuraCharacterEquipHandholdMethod = IntPtr.Zero;
        private IntPtr homelandFarmAuraToolProtocolSetHandHoldMethod = IntPtr.Zero;
        private IntPtr homelandFarmAuraToolSystemSetHandholdMethod = IntPtr.Zero;
        private IntPtr homelandFarmAuraToolSystemInstanceGetterMethod = IntPtr.Zero;
        private IntPtr homelandFarmAuraCropSeedingMethod = IntPtr.Zero;
        private IntPtr homelandFarmAuraCropPlantPointClass = IntPtr.Zero;
        private IntPtr homelandFarmAuraCropPlantPointPosField = IntPtr.Zero;
        private IntPtr homelandFarmAuraCropPlantPointAngleField = IntPtr.Zero;
        private IntPtr homelandFarmAuraCropPlantPointNetIdField = IntPtr.Zero;
        private IntPtr homelandFarmAuraCropPlantPointListClass = IntPtr.Zero;
        private IntPtr homelandFarmAuraCropPlantPointListAddMethod = IntPtr.Zero;
        private bool homelandFarmAuraCropPlantPointFieldsResolved = false;
        private IntPtr homelandFarmAuraLevelObjectManagerGetLevelObjectMethod = IntPtr.Zero;
        private int homelandFarmAuraLevelObjectManagerGetLevelObjectArgCount = 0;
        private IntPtr homelandFarmAuraEntitiesFieldSystemGetterMethod = IntPtr.Zero;
        private IntPtr homelandFarmAuraFieldComponentSystemGetFieldMethod = IntPtr.Zero;
        private bool homelandFarmAuraSowCraftContextResolved = false;

        private struct HomelandFarmCropFertilizeSnapshot
        {
            public int ManureId;
            public int BreedingPowderId;
            public int GrowthValue;
        }
        private IntPtr homelandFarmAuraPlantWaterPlantMethod = IntPtr.Zero;
        private IntPtr homelandFarmAuraPlantWaterPlant2Method = IntPtr.Zero;
        private IntPtr homelandFarmAuraPlantCollectSeedMethod = IntPtr.Zero;
        private IntPtr homelandFarmAuraDataCenterClass = IntPtr.Zero;
        private IntPtr homelandFarmAuraEntitiesClass = IntPtr.Zero;
        private IntPtr homelandFarmAuraLevelObjectManagerClass = IntPtr.Zero;
        private IntPtr homelandFarmAuraPlantComponentClass = IntPtr.Zero;
        private IntPtr homelandFarmAuraCropBoxComponentClass = IntPtr.Zero;
        private IntPtr homelandFarmAuraCropComponentClass = IntPtr.Zero;
        // Cooldown so an unresolved farm component class (e.g. CropComponent absent in this build)
        // is not re-scanned across ALL loaded assemblies/images on every classify call. Without
        // this, each TryHomelandFarmClassifyFarmNetId re-ran the full image scan (~seconds),
        // letting a single entity blow the inspection budget (symptom: inspected=1/512).
        private float homelandFarmAuraFarmComponentClassRetryAt = 0f;
        // Throttle for the managed component-data reflection probe. When those types are absent in
        // this build (DotnetAssemblies lack CropItemData/DataCenter/...), the resolution never
        // succeeds and would otherwise re-run the full interop-load + miss-cache-clear + type scan
        // on EVERY classify call (~240ms/entity). See HomelandFarmPrefersAuraComponentData.
        private float homelandFarmComponentDataReflectionRetryAt = 0f;
        // Throttle for the "upgrade managed reflection after aura is ready" probe in
        // EnsureHomelandFarmReflectionReady. Without it, every component-data read re-ran the full
        // managed type scan + miss-cache clear (~managed types absent on this build), so a scan that
        // builds 58 targets x 3 reads froze for ~10s. See EnsureHomelandFarmReflectionReady.
        private float homelandFarmManagedUpgradeRetryAt = 0f;
        // The self player GUID is constant for the session. Reading it tries managed login-info
        // reflection (re-scans when managed types are absent) — ~150ms — so memoize it; otherwise
        // every per-target water-state read pays it (58 targets x 150ms = ~9s build loop).
        private Guid homelandFarmCachedSelfGuid = Guid.Empty;
        private bool homelandFarmCachedSelfGuidReadOk = false;
        private bool homelandFarmCachedSelfGuidResolved = false;
        // Short-TTL cache of the player netId. Resolving it tries managed self-player reflection
        // first (re-scans missing types ~150ms on this build), and it is read per-target during a
        // scan. TTL keeps it correct across world changes while making a single scan O(1).
        private uint homelandFarmCachedPlayerNetId = 0U;
        private float homelandFarmCachedPlayerNetIdAt = 0f;
        private const float HomelandFarmPlayerNetIdCacheTtlSeconds = 5f;
        // Sow-all resolves each empty planter via per-box AuraMono GetLevelObject + matrix reads.
        // Doing 30 boxes in one synchronous frame overwhelms the mono runtime and crashes (native
        // AV). The slot scan is a coroutine that yields every N boxes; results land in these fields.
        private const int HomelandFarmSowSlotsPerFrame = 3;
        private List<object> homelandFarmSowSlotPoints;
        private string homelandFarmSowSlotStatus = string.Empty;
        private bool homelandFarmSowSlotOk = false;
        // Set once the manure visual refresh is found structurally unavailable on this build
        // (Entities.PlayVfxAt missing / crops have no CropComponent). Each failed attempt does heavy
        // AuraMono work, so without this fertilize-all lags ~2-3s per batch refreshing cosmetics.
        private bool homelandFarmManureVisualUnavailable = false;
        private IntPtr homelandFarmAuraEntitiesGetComponentsMethod = IntPtr.Zero;
        private readonly Dictionary<IntPtr, IntPtr> homelandFarmAuraComponentListClassByComponentClass = new Dictionary<IntPtr, IntPtr>();
        private readonly Dictionary<IntPtr, IntPtr> homelandFarmAuraInflatedGetComponentsMethodByComponentClass = new Dictionary<IntPtr, IntPtr>();
        private readonly HashSet<IntPtr> homelandFarmAuraGetComponentsFailedComponentClasses = new HashSet<IntPtr>();
        private bool homelandFarmAuraGetComponentsUnavailableLogged = false;
        // EXPERIMENT (Option 4): direct ECS Entities.GetComponents<T> to skip the crashy
        // recursive entity-graph walk (TryEnumerateAuraMonoLoadedEntityObjects). Flip back to
        // false if step4 INVOKE AVs on inflation/invoke on this build.
        private const bool HomelandFarmAllowUnsafeAuraMonoGetComponents = true;
        private IntPtr homelandFarmAuraCropBindEffectEntityMethod = IntPtr.Zero;
        private IntPtr homelandFarmAuraRendererComponentClass = IntPtr.Zero;
        private IntPtr homelandFarmAuraRendererPlayAnimTransformMethod = IntPtr.Zero;
        private IntPtr homelandFarmAuraEntitiesPlayVfxAtMethod = IntPtr.Zero;
        private int homelandFarmAuraEntitiesPlayVfxAtArgCount = 0;

        private IntPtr homelandFarmAuraEntitiesPlayVfxOnMethod = IntPtr.Zero;
        private IntPtr homelandFarmAuraEntitiesCreateLevelEntityMethod = IntPtr.Zero;

        private Type homelandFarmCropProtocolManagerType = null;
        private Type homelandFarmPlantProtocolManagerType = null;
        private Type homelandFarmDataCenterType = null;
        private Type homelandFarmCropItemDataType = null;
        private Type homelandFarmCropBoxItemDataType = null;
        private Type homelandFarmPlantItemDataType = null;
        private Type homelandFarmLevelEntityComponentDataType = null;
        private Type homelandFarmFriendServiceType = null;
        private Type homelandFarmCropPlantPointType = null;
        private Type homelandFarmGrowCropNetworkCommandType = null;
        private Type homelandFarmWaterCropNetworkCommandType = null;
        private bool homelandFarmWaterCropSendUnavailable = false;
        private bool homelandFarmWaterPlantSendUnavailable = false;
        private Type homelandFarmHarvestNetworkCommandType = null;
        private Type homelandFarmWeedingNetworkCommandType = null;
        private Type homelandFarmManuredNetworkCommandType = null;
        private Type homelandFarmAddHolderSystemCommandType = null;
        private Type homelandFarmEHolderSystemType = null;
        private Type homelandFarmHoldToolCommandType = null;
        private Type homelandFarmCancelHolderSystemCommandType = null;
        private Type homelandFarmToolProtocolManagerType = null;
        private MethodInfo homelandFarmToolProtocolSetHandHoldMethod = null;
        private Type homelandFarmToolSystemType = null;
        private Type homelandFarmToolDataModuleType = null;
        private PropertyInfo homelandFarmToolDataModuleInstanceProperty = null;
        private MethodInfo homelandFarmToolSystemSetHandholdMethod = null;
        private MethodInfo homelandFarmToolSystemGetToolMethod = null;
        private bool homelandFarmToolEquipTypesResolved = false;
        private bool homelandFarmNetworkCommandTypesResolved = false;
        private Type homelandFarmWaterPlantNetworkCommandType = null;
        private Type homelandFarmPickPlantCrossedSeedNetworkCommandType = null;
        private Type homelandFarmNetIdType = null;

        private MethodInfo homelandFarmDataCenterTryGetComponentDataMethodDef = null;
        private MethodInfo homelandFarmCropWaterPlantMethod = null;
        private MethodInfo homelandFarmCropPickPlantMethod = null;
        private MethodInfo homelandFarmCropWeedMethod = null;
        private MethodInfo homelandFarmCropPickProductMethod = null;
        private MethodInfo homelandFarmCropSeedingMethod = null;
        private MethodInfo homelandFarmCropAddManureMethod = null;
        private Type homelandFarmCharacterProtocolManagerType = null;
        private MethodInfo homelandFarmCharacterEquipHandholdMethod = null;
        private MethodInfo homelandFarmPlantWaterPlantMethod = null;
        private MethodInfo homelandFarmPlantCollectSeedMethod = null;

        private Type homelandFarmPlayerDataCenterType = null;
        private Type homelandFarmLocalPlayerComponentType = null;
        private Type homelandFarmCropComponentType = null;
        private Type homelandFarmCropBoxComponentType = null;
        private Type homelandFarmPlantComponentType = null;
        private Type homelandFarmEntitiesType = null;
        private Type homelandFarmEntityType = null;
        private Type homelandFarmEntityUtilType = null;
        private Type homelandFarmEcsServiceType = null;

        private MethodInfo homelandFarmEntitiesGetComponentsMethod = null;
        private MethodInfo homelandFarmEntitiesSphereQueryEntitiesMethod = null;
        private MethodInfo homelandFarmEntityUtilGetSelfPlayerMethod = null;
        private MethodInfo homelandFarmEntityUtilGetSelfPlayerEntityMethod = null;
        private MethodInfo homelandFarmEcsServiceTryGetMethodDef = null;
        private MethodInfo homelandFarmFriendServiceGetFriendsMethod = null;
        private MethodInfo homelandFarmCropBoxGetWaterCountMethod = null;
        private MethodInfo homelandFarmSendCommandMethodDef = null;
        private MethodInfo homelandFarmManuredSendCommandMethod = null;
        private object homelandFarmReliableChannelValue = null;
        private MethodInfo homelandFarmCropAddManureInteropMethod = null;
        private MethodInfo homelandFarmCropSeedingInteropMethod = null;

        private bool homelandFarmScannerTypesResolved = false;
        private bool homelandFarmScannerTypesUnavailable = false;
        private string homelandFarmScannerTypesUnavailableStatus = string.Empty;
        private readonly Dictionary<uint, Vector3> homelandFarmAuraLevelObjectPositionCache = new Dictionary<uint, Vector3>();
        private readonly Dictionary<uint, uint> homelandFarmAuraLevelObjectOwnerByNetId = new Dictionary<uint, uint>();
        private float homelandFarmAuraLevelObjectPositionCacheAt = -999f;
        private const float HomelandFarmLevelObjectPositionCacheTtl = 2f;

        private Type homelandFarmBackPackSystemType = null;
        private Type homelandFarmStorageTypeType = null;
        private MethodInfo homelandFarmBackPackCanPutInMethod = null;
        private MethodInfo homelandFarmBackPackGetAllItemMethod = null;
        private Type homelandFarmTableDataType = null;
        private MethodInfo homelandFarmDecodeTypeEntityDataMethod = null;
        private MethodInfo homelandFarmGetEntityMethod = null;
        private MethodInfo homelandFarmGetBackPackNameMethod = null;
        private MethodInfo homelandFarmGetCropfertilizerMethod = null;
        private Type homelandFarmEntityTypeEnumType = null;
        private Type homelandFarmBuildComponentDataType = null;
        private int homelandFarmCropSeedEntityTypeValue = int.MinValue;
        private int homelandFarmCropFertilizerEntityTypeValue = int.MinValue;
        private int homelandFarmSprinklerEntityTypeValue = int.MinValue;
        private bool homelandFarmBackpackReflectionResolved = false;
        private bool homelandFarmBackpackReflectionUnavailable = false;
        private bool homelandFarmInventoryReflectionResolved = false;
        private bool homelandFarmInventoryReflectionUnavailable = false;
        private bool homelandFarmTableDataReflectionResolved = false;
        private float homelandFarmNextRuntimeResolveAt = 0f;
        private const float HomelandFarmRuntimeResolveRetryIntervalSeconds = 0.5f;
        private float homelandFarmNextSceneLoadFinishedProbeAt = 0f;
        private bool homelandFarmCachedSceneLoadFinished = false;
        private const float HomelandFarmSceneLoadFinishedProbeIntervalSeconds = 0.5f;
        private IntPtr homelandFarmAuraClientHelperServiceClass = IntPtr.Zero;
        private IntPtr homelandFarmAuraIsSceneLoadFinishedMethod = IntPtr.Zero;
        // Warmup diagnostics (visible via ModLogger, throttled so they don't spam).
        private bool homelandFarmWarmupStartedLogged = false;
        private bool homelandFarmWarmupReadyLogged = false;
        private int homelandFarmWarmupAttempts = 0;
        private float homelandFarmNextWarmupFailLogAt = 0f;
        private const float HomelandFarmWarmupFailLogIntervalSeconds = 5f;

        private bool EnsureHomelandFarmReflectionReady()
        {
            if (this.homelandFarmManagedReflectionReady || this.homelandFarmAuraReflectionReady)
            {
                // Opportunistically upgrade to managed reflection once aura is already usable, but
                // THROTTLE it: this runs a full type scan + miss-cache clear and is invoked once per
                // component-data read. Re-running it every call froze multi-target scans for seconds
                // when the managed types are absent (the scan never succeeds, so it never stops).
                float upgradeNow = Time.realtimeSinceStartup;
                if (!this.homelandFarmManagedReflectionReady
                    && !this.homelandFarmManagedReflectionUnavailable
                    && upgradeNow >= this.homelandFarmManagedUpgradeRetryAt)
                {
                    this.homelandFarmManagedUpgradeRetryAt = upgradeNow + HomelandFarmAuraComponentClassResolveRetrySeconds;
                    this.TryEnsureHomelandFarmInteropAssembliesLoaded();
                    this.ClearHomelandFarmReflectionMissCaches();
                    if (this.TryEnsureHomelandFarmManagedReflection(out _))
                    {
                        this.homelandFarmManagedReflectionReady = true;
                        this.HomelandFarmLog("Managed reflection ready (upgrade after aura).");
                    }
                }

                return true;
            }

            float now = Time.realtimeSinceStartup;
            if (this.homelandFarmReflectionUnavailable && now < this.homelandFarmNextReflectionRetryAt)
            {
                return false;
            }

            this.TryEnsureHomelandFarmInteropAssembliesLoaded();
            this.ClearHomelandFarmReflectionMissCaches();

            // Prefer managed (DotnetAssemblies/interop) when available: it avoids extra native
            // AuraMono GetAllComponents calls that can AV after a heavy entity scan.
            if (!this.homelandFarmManagedReflectionUnavailable
                && this.TryEnsureHomelandFarmManagedReflection(out string managedStatus))
            {
                this.homelandFarmManagedReflectionReady = true;
                this.homelandFarmReflectionResolved = true;
                this.homelandFarmReflectionUnavailable = false;
                this.homelandFarmReflectionUnavailableStatus = string.Empty;
                this.HomelandFarmLog("Managed reflection ready.");
                return true;
            }

            if (this.TryEnsureHomelandFarmAuraReflection(out string auraStatus))
            {
                this.homelandFarmAuraReflectionReady = true;
                this.homelandFarmReflectionResolved = true;
                this.homelandFarmReflectionUnavailable = false;
                this.homelandFarmReflectionUnavailableStatus = string.Empty;
                this.HomelandFarmLog("Aura reflection ready (MelonLoader/native path).");
                return true;
            }

            string managedStatusFinal = this.homelandFarmManagedReflectionUnavailable
                ? this.homelandFarmManagedReflectionUnavailableStatus
                : string.Empty;
            this.homelandFarmReflectionUnavailable = true;
            this.homelandFarmReflectionUnavailableStatus = string.IsNullOrEmpty(managedStatusFinal)
                ? auraStatus
                : (string.IsNullOrEmpty(auraStatus) ? managedStatusFinal : managedStatusFinal + ". " + auraStatus);
            this.homelandFarmNextReflectionRetryAt = now + 15f;
            this.HomelandFarmLog(this.homelandFarmReflectionUnavailableStatus);
            return false;
        }

        private void TryEnsureHomelandFarmInteropAssembliesLoaded()
        {
            if (this.homelandFarmInteropAssembliesLoaded)
            {
                return;
            }

            float now = Time.realtimeSinceStartup;
            if (now < this.homelandFarmNextInteropLoadAttemptAt)
            {
                return;
            }

            this.homelandFarmNextInteropLoadAttemptAt = now + HomelandFarmInteropLoadRetryIntervalSeconds;

            try
            {
                string dataPath = Application.dataPath;
                if (string.IsNullOrEmpty(dataPath))
                {
                    return;
                }

                string gameDir = Path.GetDirectoryName(dataPath);
                if (string.IsNullOrEmpty(gameDir))
                {
                    return;
                }

#if BEPINEX
                string interopDir = Path.Combine(gameDir, "BepInEx", "interop");
#else
                string interopDir = Path.Combine(gameDir, "MelonLoader", "Il2CppAssemblies");
#endif
                int loaded = 0;
                if (Directory.Exists(interopDir))
                {
                    loaded += this.TryHomelandFarmLoadInteropAssembliesFromDirectory(interopDir);
                }

                string dotnetAssembliesDir = Path.Combine(dataPath, "StreamingAssets", "DotnetAssemblies");
                if (Directory.Exists(dotnetAssembliesDir))
                {
                    loaded += this.TryHomelandFarmLoadInteropAssembliesFromDirectory(dotnetAssembliesDir, "*.bytes");
                }

                if (loaded == 0)
                {
                    if (!Directory.Exists(interopDir))
                    {
                        if (!this.homelandFarmInteropMissingDirLogged)
                        {
                            this.homelandFarmInteropMissingDirLogged = true;
                            this.HomelandFarmLog("Interop directory missing: " + interopDir + " (will retry).");
                        }

                        return;
                    }

                    if (!this.homelandFarmInteropZeroLoadLogged)
                    {
                        this.homelandFarmInteropZeroLoadLogged = true;
                        this.HomelandFarmLog("Interop directory present but 0 game assemblies loaded from " + interopDir + " (will retry).");
                    }

                    return;
                }

                this.homelandFarmInteropAssembliesLoaded = true;
                this.homelandFarmManagedReflectionUnavailable = false;
                this.homelandFarmSowManagedReflectionAttempted = false;
                this.ClearHomelandFarmReflectionMissCaches();
                this.HomelandFarmLog("Loaded " + loaded + " interop assembly(ies) from interop/DotnetAssemblies.");
            }
            catch (Exception ex)
            {
                this.HomelandFarmLog("Interop load exception: " + ex.Message);
            }
        }

        private int TryHomelandFarmLoadInteropAssembliesFromDirectory(string directory, string searchPattern = "*.dll")
        {
            int loaded = 0;
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return loaded;
            }

            foreach (string dllPath in Directory.GetFiles(directory, searchPattern))
            {
                string fileName = Path.GetFileNameWithoutExtension(dllPath) ?? string.Empty;
#if BEPINEX
                bool isInteropDirectory = directory.IndexOf("interop", StringComparison.OrdinalIgnoreCase) >= 0;
                if (isInteropDirectory && string.Equals(searchPattern, "*.dll", StringComparison.Ordinal))
                {
                    if (fileName.StartsWith("System", StringComparison.OrdinalIgnoreCase)
                        || fileName.StartsWith("Unity", StringComparison.OrdinalIgnoreCase)
                        || fileName.StartsWith("Mono", StringComparison.OrdinalIgnoreCase)
                        || fileName.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase)
                        || fileName.StartsWith("Newtonsoft", StringComparison.OrdinalIgnoreCase)
                        || fileName.IndexOf("Harmony", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        continue;
                    }
                }
                else
#endif
                if (fileName.IndexOf("Assembly-CSharp", StringComparison.OrdinalIgnoreCase) < 0
                    && fileName.IndexOf("Client", StringComparison.OrdinalIgnoreCase) < 0
                    && fileName.IndexOf("EcsClient", StringComparison.OrdinalIgnoreCase) < 0
                    && fileName.IndexOf("GameApp", StringComparison.OrdinalIgnoreCase) < 0
                    && fileName.IndexOf("XDT", StringComparison.OrdinalIgnoreCase) < 0
                    && fileName.IndexOf("Il2Cpp", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                try
                {
                    Assembly.LoadFrom(dllPath);
                    loaded++;
                }
                catch
                {
                }
            }

            return loaded;
        }

        private void ClearHomelandFarmReflectionMissCaches()
        {
            this.ClearModReflectionLookupMissCaches();
            this.homelandFarmAuraComponentMissCache.Clear();
            this.homelandFarmLastScanCropBoxNetIds.Clear();
        }

        private static string HomelandFarmAuraComponentCacheKey(uint netId, string dataTypeName)
        {
            return netId.ToString() + "|" + (dataTypeName ?? string.Empty);
        }

        private Type ResolveHomelandFarmManagedType(string shortName, params string[] fullNames)
        {
            if (fullNames != null)
            {
                for (int i = 0; i < fullNames.Length; i++)
                {
                    string fullName = fullNames[i];
                    if (string.IsNullOrEmpty(fullName))
                    {
                        continue;
                    }

                    Type resolved = this.FindLoadedTypeByFullName(fullName)
                        ?? this.FindLoadedType(fullName, shortName);
                    if (resolved != null)
                    {
                        return resolved;
                    }
                }
            }

            Type auraLoaderType = this.TryResolveHomelandFarmManagedTypeViaAuraLoader(shortName, fullNames);
            if (auraLoaderType != null)
            {
                return auraLoaderType;
            }

            return this.FindHomelandFarmRuntimeType(shortName);
        }

        private Type TryResolveHomelandFarmManagedTypeViaAuraLoader(string shortName, string[] fullNames)
        {
            if (fullNames != null)
            {
                for (int i = 0; i < fullNames.Length; i++)
                {
                    string fullName = fullNames[i];
                    if (string.IsNullOrEmpty(fullName))
                    {
                        continue;
                    }

                    int lastDot = fullName.LastIndexOf('.');
                    string namespaceName = lastDot > 0 ? fullName.Substring(0, lastDot) : string.Empty;
                    Type resolved = this.FindTypeByName(fullName, namespaceName, shortName);
                    if (resolved != null)
                    {
                        return resolved;
                    }

                    if (!fullName.StartsWith("Il2Cpp", StringComparison.Ordinal))
                    {
                        resolved = this.FindTypeByName("Il2Cpp" + fullName, namespaceName, shortName);
                        if (resolved != null)
                        {
                            return resolved;
                        }
                    }
                }
            }

            return null;
        }

        internal Type ResolveHomelandFarmCropComponentRuntimeType()
        {
            this.TryEnsureHomelandFarmInteropAssembliesLoaded();

            Type resolved = this.ResolveHomelandFarmManagedType(
                "CropComponent",
                "XDTLevelAndEntity.Gameplay.Component.Homeland.CropComponent",
                "XDTLevelAndEntity.Gameplay.Component.Farm.CropComponent",
                "ScriptsRefactory.LevelAndEntity.Gameplay.Component.Farm.CropComponent");
            if (resolved != null && this.HomelandFarmLooksLikeCropComponentType(resolved))
            {
                return resolved;
            }

            resolved = this.FindLoadedType(
                "Il2CppXDTLevelAndEntity.Gameplay.Component.Homeland.CropComponent",
                "Il2CppXDTLevelAndEntity.Gameplay.Component.Farm.CropComponent",
                "Il2Cpp.XDTLevelAndEntity.Gameplay.Component.Homeland.CropComponent",
                "Il2Cpp.XDTLevelAndEntity.Gameplay.Component.Farm.CropComponent");
            if (resolved != null && this.HomelandFarmLooksLikeCropComponentType(resolved))
            {
                return resolved;
            }

            resolved = this.FindLoadedTypeBySuffix(
                "Gameplay.Component.Homeland.CropComponent",
                "Gameplay.Component.Farm.CropComponent",
                ".Homeland.CropComponent",
                ".CropComponent");
            if (resolved != null && this.HomelandFarmLooksLikeCropComponentType(resolved))
            {
                return resolved;
            }

            resolved = this.FindHomelandFarmRuntimeType(
                "CropComponent",
                "XDTLevelAndEntity.Gameplay.Component.Homeland",
                "XDTLevelAndEntity.Gameplay.Component.Farm",
                "ScriptsRefactory.LevelAndEntity.Gameplay.Component.Farm");
            if (resolved != null && this.HomelandFarmLooksLikeCropComponentType(resolved))
            {
                return resolved;
            }

            return null;
        }

        private bool HomelandFarmLooksLikeCropComponentType(Type candidate)
        {
            if (candidate == null || !string.Equals(candidate.Name, "CropComponent", StringComparison.Ordinal))
            {
                return false;
            }

            string fullName = candidate.FullName ?? string.Empty;
            if (fullName.IndexOf("CropBoxComponent", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            MethodInfo[] methods = candidate.GetMethods(
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.FlattenHierarchy);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo method = methods[i];
                if (method == null)
                {
                    continue;
                }

                if (string.Equals(method.Name, "UpdateManureEffect", StringComparison.Ordinal)
                    || string.Equals(method.Name, "StopManureEffect", StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return fullName.IndexOf("XDTLevelAndEntity", StringComparison.OrdinalIgnoreCase) >= 0
                || fullName.IndexOf("ScriptsRefactory.LevelAndEntity", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool HomelandFarmLooksLikeAuraCropComponentClass(IntPtr candidate)
        {
            if (candidate == IntPtr.Zero)
            {
                return false;
            }

            string displayName = this.GetAuraMonoClassDisplayName(candidate) ?? string.Empty;
            if (displayName.IndexOf("CropBoxComponent", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            if (displayName.IndexOf("CropComponent", StringComparison.OrdinalIgnoreCase) < 0)
            {
                return false;
            }

            if (this.FindAuraMonoMethodOnHierarchy(candidate, "UpdateManureEffect", 0) != IntPtr.Zero)
            {
                return true;
            }

            return displayName.IndexOf("XDTLevelAndEntity", StringComparison.OrdinalIgnoreCase) >= 0
                || displayName.IndexOf("ScriptsRefactory.LevelAndEntity", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string DescribeHomelandFarmAuraClass(IntPtr classPtr)
        {
            if (classPtr == IntPtr.Zero)
            {
                return "missing";
            }

            string displayName = this.GetAuraMonoClassDisplayName(classPtr);
            return string.IsNullOrEmpty(displayName) ? "resolved" : displayName;
        }

        private static readonly string[] HomelandFarmAuraCropComponentFullNames =
        {
            "XDTLevelAndEntity.Gameplay.Component.Homeland.CropComponent",
            "XDTLevelAndEntity.Gameplay.Component.Farm.CropComponent",
            "XDTLevelAndEntity.GamePlay.Component.Homeland.CropComponent",
            "XDTLevelAndEntity.GamePlay.Component.Farm.CropComponent",
            "ScriptsRefactory.LevelAndEntity.Gameplay.Component.Farm.CropComponent",
        };

        private bool TryResolveHomelandFarmAuraCropComponentClass(out IntPtr cropComponentClass)
        {
            cropComponentClass = this.homelandFarmAuraCropComponentClass;
            if (cropComponentClass != IntPtr.Zero)
            {
                return true;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return false;
            }

            for (int i = 0; i < HomelandFarmAuraCropComponentFullNames.Length; i++)
            {
                IntPtr candidate = this.FindAuraMonoClassByFullName(HomelandFarmAuraCropComponentFullNames[i]);
                if (candidate != IntPtr.Zero && this.HomelandFarmLooksLikeAuraCropComponentClass(candidate))
                {
                    cropComponentClass = candidate;
                    break;
                }
            }

            if (cropComponentClass == IntPtr.Zero)
            {
                cropComponentClass = this.TryFindHomelandFarmAuraCropComponentClassCandidate(
                    "XDTLevelAndEntity.Gameplay.Component.Homeland.CropComponent",
                    "XDTLevelAndEntity.Gameplay.Component.Homeland",
                    "CropComponent");
            }

            if (cropComponentClass == IntPtr.Zero)
            {
                cropComponentClass = this.TryFindHomelandFarmAuraCropComponentClassCandidate(
                    "XDTLevelAndEntity.Gameplay.Component.Farm.CropComponent",
                    "XDTLevelAndEntity.Gameplay.Component.Farm",
                    "CropComponent");
            }

            if (cropComponentClass == IntPtr.Zero)
            {
                cropComponentClass = this.TryFindHomelandFarmAuraCropComponentClassCandidate(
                    "ScriptsRefactory.LevelAndEntity.Gameplay.Component.Farm.CropComponent",
                    "ScriptsRefactory.LevelAndEntity.Gameplay.Component.Farm",
                    "CropComponent");
            }

            if (cropComponentClass == IntPtr.Zero)
            {
                cropComponentClass = this.FindHomelandFarmAuraCropComponentClassByScanningAllImages();
            }

            if (cropComponentClass != IntPtr.Zero)
            {
                this.homelandFarmAuraCropComponentClass = cropComponentClass;
                return true;
            }

            return false;
        }

        private IntPtr TryFindHomelandFarmAuraCropComponentClassCandidate(string fullName, string namespaceName, string shortName)
        {
            IntPtr candidate = this.FindHomelandFarmAuraClass(fullName, namespaceName, shortName);
            return this.HomelandFarmLooksLikeAuraCropComponentClass(candidate) ? candidate : IntPtr.Zero;
        }

        private IntPtr FindHomelandFarmAuraCropComponentClassByScanningAllImages()
        {
            return this.FindHomelandFarmAuraClassByScanningAllImages(
                "CropComponent",
                HomelandFarmAuraCropComponentNamespaces,
                this.HomelandFarmLooksLikeAuraCropComponentClass);
        }

        // Universal fallback: walk every loaded mono image and try mono_class_from_name for the
        // given short name under each candidate namespace, validating each hit. Used when the
        // targeted full-name / loaded-assembly lookups miss (e.g. unexpected namespace in a build).
        private IntPtr FindHomelandFarmAuraClassByScanningAllImages(
            string shortName,
            string[] namespaceCandidates,
            Func<IntPtr, bool> validator)
        {
            if (string.IsNullOrEmpty(shortName)
                || namespaceCandidates == null
                || namespaceCandidates.Length == 0
                || !this.EnsureAuraMonoApiReady()
                || auraMonoClassFromName == null)
            {
                return IntPtr.Zero;
            }

            try
            {
                Type monoHostType = Type.GetType("Il2CppMonoGame.MonoHost, Il2CppMonoGame", false);
                if (monoHostType == null)
                {
                    return IntPtr.Zero;
                }

                PropertyInfo currentProperty = monoHostType.GetProperty("Current", BindingFlags.Public | BindingFlags.Static);
                object monoHost = currentProperty != null ? currentProperty.GetValue(null, null) : null;
                if (monoHost == null)
                {
                    return IntPtr.Zero;
                }

                FieldInfo loadedAssembliesField = monoHostType.GetField("_loadedAssemblies", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object loadedAssemblies = loadedAssembliesField != null ? loadedAssembliesField.GetValue(monoHost) : null;
                IEnumerable enumerable = loadedAssemblies as IEnumerable;
                if (enumerable == null)
                {
                    return IntPtr.Zero;
                }

                foreach (object entry in enumerable)
                {
                    if (entry == null)
                    {
                        continue;
                    }

                    Type entryType = entry.GetType();
                    PropertyInfo valueProperty = entryType.GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
                    object value = valueProperty != null ? valueProperty.GetValue(entry, null) : null;
                    if (value == null)
                    {
                        continue;
                    }

                    PropertyInfo imageProperty = value.GetType().GetProperty("Image", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    object image = imageProperty != null ? imageProperty.GetValue(value, null) : null;
                    if (image == null)
                    {
                        continue;
                    }

                    PropertyInfo handleProperty = image.GetType().GetProperty("Handle", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (handleProperty == null)
                    {
                        continue;
                    }

                    object handleValue = handleProperty.GetValue(image, null);
                    if (!(handleValue is IntPtr imageHandle) || imageHandle == IntPtr.Zero)
                    {
                        continue;
                    }

                    for (int i = 0; i < namespaceCandidates.Length; i++)
                    {
                        IntPtr candidate = auraMonoClassFromName(imageHandle, namespaceCandidates[i], shortName);
                        if (candidate != IntPtr.Zero && (validator == null || validator(candidate)))
                        {
                            return candidate;
                        }
                    }
                }
            }
            catch
            {
            }

            return IntPtr.Zero;
        }

        private Type ResolveHomelandFarmCropBoxComponentRuntimeType()
        {
            return this.ResolveHomelandFarmManagedType(
                "CropBoxComponent",
                "XDTLevelAndEntity.Gameplay.Component.Homeland.CropBoxComponent",
                "XDTLevelAndEntity.Gameplay.Component.Farm.CropBoxComponent",
                "ScriptsRefactory.LevelAndEntity.Gameplay.Component.Farm.CropBoxComponent");
        }

        private void OnAuraFarmRuntimeResolverReady()
        {
            this.EnsureHomelandFarmWarmupStarted();
            this.TryEnsureHomelandFarmInteropAssembliesLoaded();
            this.EnsureNoclipVehicleAuraMono(logIfPending: true);
        }

        private bool IsHomelandFarmSceneLoadFinished()
        {
            float now = Time.unscaledTime;
            if (now < this.homelandFarmNextSceneLoadFinishedProbeAt)
            {
                return this.homelandFarmCachedSceneLoadFinished;
            }

            this.homelandFarmNextSceneLoadFinishedProbeAt = now + HomelandFarmSceneLoadFinishedProbeIntervalSeconds;
            this.homelandFarmCachedSceneLoadFinished = this.TryQuerySceneLoadFinished();
            return this.homelandFarmCachedSceneLoadFinished;
        }

        private bool TryQuerySceneLoadFinished()
        {
            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
                {
                    return false;
                }

                if (this.homelandFarmAuraClientHelperServiceClass == IntPtr.Zero)
                {
                    this.homelandFarmAuraClientHelperServiceClass = this.FindAuraMonoClassByFullName("ClientSystem.Helper.ClientHelperService");
                    if (this.homelandFarmAuraClientHelperServiceClass == IntPtr.Zero)
                    {
                        this.homelandFarmAuraClientHelperServiceClass = this.FindAuraMonoClassInImages(
                            "ClientSystem.Helper",
                            "ClientHelperService",
                            new[] { "EcsSystem", "EcsSystem.dll" });
                    }
                }

                if (this.homelandFarmAuraClientHelperServiceClass == IntPtr.Zero)
                {
                    return false;
                }

                if (this.homelandFarmAuraIsSceneLoadFinishedMethod == IntPtr.Zero)
                {
                    this.homelandFarmAuraIsSceneLoadFinishedMethod = this.FindAuraMonoMethodOnHierarchy(
                        this.homelandFarmAuraClientHelperServiceClass,
                        "IsSceneLoadFinished",
                        0);
                }

                if (this.homelandFarmAuraIsSceneLoadFinishedMethod == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr boxed = auraMonoRuntimeInvoke(
                    this.homelandFarmAuraIsSceneLoadFinishedMethod,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    ref exc);
                if (exc != IntPtr.Zero || boxed == IntPtr.Zero)
                {
                    return false;
                }

                return this.TryUnboxMonoBoolean(boxed, out bool finished) && finished;
            }
            catch
            {
                return false;
            }
        }

        internal void UpdateHomelandFarmBackground()
        {
            if (!this.IsHomelandFarmSceneLoadFinished())
            {
                return;
            }

            if (this.auraFarmMethodsReady)
            {
                // Already warmed up. Log the success exactly once so we can see when it happened.
                if (!this.homelandFarmWarmupReadyLogged)
                {
                    this.homelandFarmWarmupReadyLogged = true;
                    ModLogger.Msg($"[HomelandFarm] Warmup SUCCESS: aura/farm runtime resolved after {this.homelandFarmWarmupAttempts} attempt(s).");
                }
                return;
            }

            float now = Time.unscaledTime;
            if (now < this.homelandFarmNextRuntimeResolveAt)
            {
                return;
            }

            this.homelandFarmNextRuntimeResolveAt = now + HomelandFarmRuntimeResolveRetryIntervalSeconds;

            if (!this.homelandFarmWarmupStartedLogged)
            {
                this.homelandFarmWarmupStartedLogged = true;
                ModLogger.Msg("[HomelandFarm] Warmup started: resolving aura/farm runtime methods...");
            }

            this.homelandFarmWarmupAttempts++;
            bool ready = this.ResolveAuraFarmRuntimeMethods();

            if (ready)
            {
                // Success path is logged on the next tick via the branch above (keeps a single
                // source of truth), but emit it here too so it shows up on the resolving frame.
                if (!this.homelandFarmWarmupReadyLogged)
                {
                    this.homelandFarmWarmupReadyLogged = true;
                    ModLogger.Msg($"[HomelandFarm] Warmup SUCCESS: aura/farm runtime resolved after {this.homelandFarmWarmupAttempts} attempt(s).");
                }
                return;
            }

            // Not ready yet — surface WHAT is missing, throttled so the log isn't spammed every 0.5s.
            if (now >= this.homelandFarmNextWarmupFailLogAt)
            {
                this.homelandFarmNextWarmupFailLogAt = now + HomelandFarmWarmupFailLogIntervalSeconds;
                string detail = string.IsNullOrEmpty(this.auraLastError) ? "<no detail>" : this.auraLastError;
                ModLogger.Msg($"[HomelandFarm] Warmup pending (attempt {this.homelandFarmWarmupAttempts}, AuraMono ready={this.auraMonoApiReady}): {detail}");
            }
        }

        internal bool TryHomelandFarmRefreshCropManureVisualFromCropComponentMono(IntPtr cropComponentObj, out string status)
        {
            status = "Manure refresh unavailable.";
            if (cropComponentObj == IntPtr.Zero || !this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return false;
            }

            this.TryHomelandFarmInvokeAuraMonoVoidInstanceMethod(cropComponentObj, out _, "_CheckParent");

            if (!this.TryGetMonoObjectMember(cropComponentObj, "entity", out IntPtr cropEntityObj)
                || cropEntityObj == IntPtr.Zero)
            {
                status = "Crop entity missing.";
                return false;
            }

            uint cropNetId = 0U;
            this.TryGetMonoUInt32Member(cropEntityObj, "netId", out cropNetId);
            if (cropNetId == 0U)
            {
                this.TryGetMonoUInt32Member(cropEntityObj, "NetId", out cropNetId);
            }

            if (cropNetId == 0U)
            {
                status = "Crop netId missing.";
                return false;
            }

            if (!this.TryHomelandFarmTryReadAuraCropComponentManureId(cropComponentObj, out int manureId) || manureId <= 0)
            {
                status = "manureId missing on component.";
                return false;
            }

            return this.TryHomelandFarmRefreshCropManureVisual(cropNetId, manureId, out status);
        }

        private bool TryHomelandFarmTryReadAuraCropComponentManureId(IntPtr cropComponentObj, out int manureId)
        {
            manureId = 0;
            if (cropComponentObj == IntPtr.Zero)
            {
                return false;
            }

            if ((this.TryGetMonoInt32Member(cropComponentObj, "_lastManureId", out manureId) && manureId > 0)
                || (this.TryGetMonoInt32Member(cropComponentObj, "lastManureId", out manureId) && manureId > 0))
            {
                return true;
            }

            IntPtr componentDataObj = IntPtr.Zero;
            if ((!this.TryGetMonoObjectMember(cropComponentObj, "_componentData", out componentDataObj)
                    || componentDataObj == IntPtr.Zero)
                && (!this.TryGetMonoObjectMember(cropComponentObj, "componentData", out componentDataObj)
                    || componentDataObj == IntPtr.Zero))
            {
                return false;
            }

            return (this.TryGetMonoInt32Member(componentDataObj, "manureId", out manureId) && manureId > 0)
                || (this.TryGetMonoInt32Member(componentDataObj, "ManureId", out manureId) && manureId > 0);
        }

        internal bool TryHomelandFarmBindCropManureEffectFromMono(IntPtr cropComponentObj)
        {
            return this.TryHomelandFarmBindCropManureEffectFromMono(cropComponentObj, out _);
        }

        internal bool TryHomelandFarmBindCropManureEffectFromMono(IntPtr cropComponentObj, out string status)
        {
            status = "Bind unavailable.";
            if (cropComponentObj == IntPtr.Zero || !this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return false;
            }

            this.TryHomelandFarmInvokeAuraMonoVoidInstanceMethod(cropComponentObj, out _, "_CheckParent");

            IntPtr manureEntityObj = IntPtr.Zero;
            if (!this.TryGetMonoObjectMember(cropComponentObj, "_manureEntity", out manureEntityObj)
                || manureEntityObj == IntPtr.Zero)
            {
                this.TryGetMonoObjectMember(cropComponentObj, "manureEntity", out manureEntityObj);
            }

            if (manureEntityObj == IntPtr.Zero)
            {
                status = "Manure entity missing.";
                return false;
            }

            if (!this.TryHomelandFarmTryResolveCropBindTransform(
                    cropComponentObj,
                    out IntPtr cropEntityObj,
                    out IntPtr cropTransformObj,
                    out uint cropNetId))
            {
                status = "Crop transform unavailable.";
                return false;
            }

            uint relocateNetId = cropNetId;
            if (this.TryHomelandFarmTryResolveCropBoxComponentBindTransform(
                    cropComponentObj,
                    out _,
                    out _,
                    out uint cropBoxNetId)
                && cropBoxNetId != 0U)
            {
                relocateNetId = cropBoxNetId;
            }

            if (relocateNetId != 0U)
            {
                this.TryHomelandFarmTryRelocateAuraEffectEntityToCrop(relocateNetId, manureEntityObj, cropEntityObj);
            }

            if (this.TryHomelandFarmTryInvokeCropBindEffectEntity(manureEntityObj, cropTransformObj, out status))
            {
                return true;
            }

            if (this.TryHomelandFarmTryInvokeAuraRendererPlayAnim(manureEntityObj, cropTransformObj, out status))
            {
                return true;
            }

            if (this.TryHomelandFarmTryLinkManureRendererToCrop(manureEntityObj, cropEntityObj, out string linkStatus))
            {
                status = linkStatus;
                return true;
            }

            return false;
        }

        // Mirrors CropComponent.BindEffectEntity: effectEntity.GetComponent<RendererComponent>()?.PlayAnim(trans)
        internal bool TryHomelandFarmBindCropManureEffectFromComponent(object cropComponent)
        {
            if (cropComponent == null)
            {
                return false;
            }

            Type cropType = cropComponent.GetType();
            object manureEntity = this.TryHomelandFarmReadInstanceObjectMember(
                cropType,
                cropComponent,
                "_manureEntity",
                "manureEntity",
                "_ManureEntity",
                "ManureEntity");
            if (manureEntity == null)
            {
                return false;
            }

            if (!this.TryGetObjectMember(cropComponent, "entity", out object cropEntity) || cropEntity == null)
            {
                return false;
            }

            if (!this.TryGetObjectMember(cropEntity, "transform", out object cropTransform) || cropTransform == null)
            {
                return false;
            }

            Type rendererComponentType = this.ResolveHomelandFarmManagedType(
                "RendererComponent",
                "XDTLevelAndEntity.Core.World.RendererComponent",
                "ScriptsRefactory.LevelAndEntity.Core.World.RendererComponent");
            if (rendererComponentType == null)
            {
                return false;
            }

            if (!this.TryHomelandFarmGetComponent(manureEntity, rendererComponentType, out object rendererComponent)
                || rendererComponent == null)
            {
                return false;
            }

            return this.TryHomelandFarmInvokeRendererPlayAnim(rendererComponent, cropTransform);
        }

        private object TryHomelandFarmReadInstanceObjectMember(Type type, object target, params string[] memberNames)
        {
            if (type == null || target == null || memberNames == null)
            {
                return null;
            }

            for (int i = 0; i < memberNames.Length; i++)
            {
                try
                {
                    FieldInfo field = type.GetField(
                        memberNames[i],
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (field != null)
                    {
                        object value = field.GetValue(target);
                        if (value != null)
                        {
                            return value;
                        }
                    }
                }
                catch
                {
                }

                if (this.TryGetObjectMember(target, memberNames[i], out object memberValue) && memberValue != null)
                {
                    return memberValue;
                }
            }

            return null;
        }

        private bool TryHomelandFarmInvokeRendererPlayAnim(object rendererComponent, object transform)
        {
            if (rendererComponent == null || transform == null)
            {
                return false;
            }

            try
            {
                Type transformType = transform.GetType();
                MethodInfo playAnim = rendererComponent.GetType().GetMethod(
                    "PlayAnim",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new Type[] { transformType },
                    null);
                if (playAnim == null)
                {
                    playAnim = rendererComponent.GetType().GetMethod(
                        "PlayAnim",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        new Type[] { typeof(Transform) },
                        null);
                }

                if (playAnim == null)
                {
                    return false;
                }

                playAnim.Invoke(rendererComponent, new object[] { transform });
                return true;
            }
            catch
            {
                return false;
            }
        }

        private Type FindHomelandFarmRuntimeType(string shortName, params string[] namespacePrefixes)
        {
            if (string.IsNullOrEmpty(shortName))
            {
                return null;
            }

            List<string> candidates = new List<string>();
            if (namespacePrefixes != null)
            {
                for (int i = 0; i < namespacePrefixes.Length; i++)
                {
                    string prefix = namespacePrefixes[i];
                    if (string.IsNullOrEmpty(prefix))
                    {
                        continue;
                    }

                    candidates.Add(prefix + "." + shortName);
                    candidates.Add("Il2Cpp" + prefix + "." + shortName);
                    candidates.Add("Il2Cpp." + prefix + "." + shortName);
                }
            }

            candidates.Add(shortName);
            Type resolved = this.FindLoadedType(candidates.ToArray());
            if (resolved != null)
            {
                return resolved;
            }

            resolved = this.FindLoadedTypeByFullName("XDTDataAndProtocol." + shortName);
            if (resolved != null)
            {
                return resolved;
            }

            resolved = this.FindLoadedTypeBySuffix("." + shortName, shortName);
            if (resolved != null)
            {
                return resolved;
            }

            return this.FindHomelandFarmRuntimeTypeByShape(shortName);
        }

        private Type FindHomelandFarmRuntimeTypeByShape(string shortName)
        {
            if (string.IsNullOrEmpty(shortName))
            {
                return null;
            }

            string cacheKey = "shape:" + shortName;
            if (this.loadedTypeLookupCache.TryGetValue(cacheKey, out Type cachedType) && cachedType != null)
            {
                return cachedType;
            }

            if (this.loadedTypeMissCacheUntil.TryGetValue(cacheKey, out float missCacheUntil)
                && Time.unscaledTime < missCacheUntil)
            {
                return null;
            }

            try
            {
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    string assemblyName = assembly.GetName().Name ?? string.Empty;
                    if (assemblyName.StartsWith("System", StringComparison.Ordinal)
                        || assemblyName.StartsWith("Microsoft", StringComparison.Ordinal)
                        || assemblyName.StartsWith("Harmony", StringComparison.Ordinal)
                        || assemblyName == "helper")
                    {
                        continue;
                    }

                    Type[] types;
                    try
                    {
                        types = assembly.GetTypes();
                    }
                    catch (ReflectionTypeLoadException ex)
                    {
                        types = ex.Types;
                    }
                    catch
                    {
                        continue;
                    }

                    if (types == null)
                    {
                        continue;
                    }

                    for (int i = 0; i < types.Length; i++)
                    {
                        Type candidate = types[i];
                        if (candidate == null || !string.Equals(candidate.Name, shortName, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        if (this.HomelandFarmTypeShapeMatches(shortName, candidate))
                        {
                            this.loadedTypeLookupCache[cacheKey] = candidate;
                            this.loadedTypeMissCacheUntil.Remove(cacheKey);
                            return candidate;
                        }
                    }
                }
            }
            catch
            {
            }

            this.loadedTypeMissCacheUntil[cacheKey] = Time.unscaledTime + LoadedTypeMissCacheSeconds;
            return null;
        }

        private bool HomelandFarmTypeShapeMatches(string shortName, Type candidate)
        {
            if (candidate == null || string.IsNullOrEmpty(shortName))
            {
                return false;
            }

            switch (shortName)
            {
                case "CropProtocolManager":
                    return candidate.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                        .Any(m => m.Name == "WaterPlant" || m.Name == "PickPlant");
                case "PlantProtocolManager":
                    return candidate.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                        .Any(m => m.Name == "WaterPlant" || m.Name == "SendCollectSeedCommand");
                case "DataCenter":
                    return candidate.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                        .Any(m => m.Name == "TryGetComponentData" && m.IsGenericMethodDefinition);
                case "NetId":
                    return candidate.IsValueType || candidate.IsClass;
                case "CropComponent":
                    return this.HomelandFarmLooksLikeCropComponentType(candidate);
                default:
                    return true;
            }
        }

        private bool TryEnsureHomelandFarmManagedReflection(out string status)
        {
            status = string.Empty;
            if (this.homelandFarmManagedReflectionReady)
            {
                return true;
            }

            if (this.homelandFarmManagedReflectionUnavailable)
            {
                status = string.IsNullOrEmpty(this.homelandFarmManagedReflectionUnavailableStatus)
                    ? "managed homeland farm resolver unavailable"
                    : this.homelandFarmManagedReflectionUnavailableStatus;
                return false;
            }

            this.homelandFarmCropProtocolManagerType = this.ResolveHomelandFarmManagedType(
                "CropProtocolManager",
                "XDTDataAndProtocol.ProtocolService.Plant.CropProtocolManager");
            this.homelandFarmPlantProtocolManagerType = this.ResolveHomelandFarmManagedType(
                "PlantProtocolManager",
                "XDTDataAndProtocol.ProtocolService.Plant.PlantProtocolManager");
            this.homelandFarmDataCenterType = this.ResolveHomelandFarmManagedType(
                "DataCenter",
                "XDTDataAndProtocol.ComponentsData.DataCenter",
                "ScriptsRefactory.DataAndProtocol.ComponentsData.DataCenter");
            this.homelandFarmCropItemDataType = this.ResolveHomelandFarmManagedType(
                "CropItemData",
                "XDTDataAndProtocol.ComponentsData.CropItemData",
                "ScriptsRefactory.DataAndProtocol.ComponentsData.CropItemData");
            this.homelandFarmCropBoxItemDataType = this.ResolveHomelandFarmManagedType(
                "CropBoxItemData",
                "XDTDataAndProtocol.ComponentsData.CropBoxItemData",
                "ScriptsRefactory.DataAndProtocol.ComponentsData.CropBoxItemData");
            this.homelandFarmPlantItemDataType = this.ResolveHomelandFarmManagedType(
                "PlantItemData",
                "XDTDataAndProtocol.ComponentsData.PlantItemData",
                "ScriptsRefactory.DataAndProtocol.ComponentsData.PlantItemData");
            this.homelandFarmLevelEntityComponentDataType = this.ResolveHomelandFarmManagedType(
                "LevelEntityComponentData",
                "XDTDataAndProtocol.ComponentsData.LevelEntityComponentData",
                "ScriptsRefactory.DataAndProtocol.ComponentsData.LevelEntityComponentData");
            this.homelandFarmFriendServiceType = this.ResolveHomelandFarmManagedType(
                "IFriendService",
                "XDTDataAndProtocol.ProtocolService.Social.IFriendService");
            this.homelandFarmCropPlantPointType = this.ResolveHomelandFarmManagedType(
                "CropPlantPoint",
                "XDT.Scene.Shared.Modules.Farm.CropPlantPoint",
                "EcsClient.XDT.Scene.Shared.Modules.Farm.CropPlantPoint");
            this.homelandFarmGrowCropNetworkCommandType = this.ResolveHomelandFarmManagedType(
                "GrowCropNetworkCommand",
                "XDT.Scene.Shared.Modules.Farm.GrowCropNetworkCommand",
                "EcsClient.XDT.Scene.Shared.Modules.Farm.GrowCropNetworkCommand");
            this.homelandFarmWaterCropNetworkCommandType = this.ResolveHomelandFarmManagedType(
                "WaterCropNetworkCommand",
                "XDT.Scene.Shared.Modules.Farm.WaterCropNetworkCommand",
                "EcsClient.XDT.Scene.Shared.Modules.Farm.WaterCropNetworkCommand");
            this.homelandFarmHarvestNetworkCommandType = this.ResolveHomelandFarmManagedType(
                "HarvestNetworkCommand",
                "XDT.Scene.Shared.Modules.Farm.HarvestNetworkCommand",
                "EcsClient.XDT.Scene.Shared.Modules.Farm.HarvestNetworkCommand");
            this.homelandFarmWeedingNetworkCommandType = this.ResolveHomelandFarmManagedType(
                "WeedingNetworkCommand",
                "XDT.Scene.Shared.Modules.Farm.WeedingNetworkCommand",
                "EcsClient.XDT.Scene.Shared.Modules.Farm.WeedingNetworkCommand");
            this.homelandFarmManuredNetworkCommandType = this.ResolveHomelandFarmManagedType(
                "ManuredNetworkCommand",
                "XDT.Scene.Shared.Modules.Farm.ManuredNetworkCommand",
                "EcsClient.XDT.Scene.Shared.Modules.Farm.ManuredNetworkCommand");
            this.homelandFarmWaterPlantNetworkCommandType = this.ResolveHomelandFarmManagedType(
                "WaterPlantNetworkCommand",
                "XDT.Scene.Shared.Modules.Plant.WaterPlantNetworkCommand",
                "EcsClient.XDT.Scene.Shared.Modules.Plant.WaterPlantNetworkCommand");
            this.homelandFarmPickPlantCrossedSeedNetworkCommandType = this.ResolveHomelandFarmManagedType(
                "PickPlantCrossedSeedNetworkCommand",
                "XDT.Scene.Shared.Modules.Plant.PickPlantCrossedSeedNetworkCommand",
                "EcsClient.XDT.Scene.Shared.Modules.Plant.PickPlantCrossedSeedNetworkCommand");
            this.homelandFarmNetIdType = this.ResolveHomelandFarmManagedType(
                "NetId",
                "EcsClient.XDT.Scene.Shared.Data.SharedData.NetId",
                "XDT.Scene.Shared.Data.SharedData.NetId");

            List<string> missingTypes = new List<string>();
            if (this.homelandFarmCropProtocolManagerType == null) missingTypes.Add("CropProtocolManager");
            if (this.homelandFarmPlantProtocolManagerType == null) missingTypes.Add("PlantProtocolManager");
            if (this.homelandFarmDataCenterType == null) missingTypes.Add("DataCenter");
            if (this.homelandFarmCropItemDataType == null) missingTypes.Add("CropItemData");
            if (this.homelandFarmCropBoxItemDataType == null) missingTypes.Add("CropBoxItemData");
            if (this.homelandFarmPlantItemDataType == null) missingTypes.Add("PlantItemData");
            if (this.homelandFarmLevelEntityComponentDataType == null) missingTypes.Add("LevelEntityComponentData");
            if (this.homelandFarmCropPlantPointType == null) missingTypes.Add("CropPlantPoint");
            if (this.homelandFarmNetIdType == null) missingTypes.Add("NetId");

            if (missingTypes.Count > 0)
            {
                status = "missing type(s): " + string.Join(", ", missingTypes.ToArray());
                IntPtr auraCropProtocol = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.Plant.CropProtocolManager");
                IntPtr auraPlantProtocol = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.Plant.PlantProtocolManager");
                this.HomelandFarmLog(status + " aura(cropProtocol=" + (auraCropProtocol != IntPtr.Zero)
                    + ",plantProtocol=" + (auraPlantProtocol != IntPtr.Zero) + ")");
                this.homelandFarmManagedReflectionUnavailable = true;
                this.homelandFarmManagedReflectionUnavailableStatus = status;
                return false;
            }

            this.homelandFarmDataCenterTryGetComponentDataMethodDef = this.ResolveHomelandFarmTryGetComponentDataMethodDef();
            this.homelandFarmCropWaterPlantMethod = this.ResolveHomelandFarmCropWaterPlantMethod();
            this.homelandFarmCropPickPlantMethod = this.GetMethodByNameAndParamCountQuiet(this.homelandFarmCropProtocolManagerType, "PickPlant", 1);
            this.homelandFarmCropWeedMethod = this.GetMethodByNameAndParamCountQuiet(this.homelandFarmCropProtocolManagerType, "CropWeed", 1);
            this.homelandFarmCropPickProductMethod = this.GetMethodByNameAndParamCountQuiet(this.homelandFarmCropProtocolManagerType, "PickCropProductCommand", 1);
            this.homelandFarmCropSeedingMethod = this.ResolveHomelandFarmCropSeedingMethod();
            this.homelandFarmCropAddManureMethod = this.ResolveHomelandFarmListOnlyStaticMethod(this.homelandFarmCropProtocolManagerType, "AddManure");
            this.homelandFarmCharacterProtocolManagerType = this.ResolveHomelandFarmManagedType(
                "CharacterProtocolManager",
                "XDTDataAndProtocol.ProtocolService.GamePlay.Character.CharacterProtocolManager");
            if (this.homelandFarmCharacterProtocolManagerType != null)
            {
                this.homelandFarmCharacterEquipHandholdMethod = this.homelandFarmCharacterProtocolManagerType.GetMethod(
                    "EquipHandhold",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(uint) },
                    null);
            }
            this.homelandFarmPlantWaterPlantMethod = this.GetMethodByNameAndParamCountQuiet(this.homelandFarmPlantProtocolManagerType, "WaterPlant", 3);
            this.homelandFarmPlantCollectSeedMethod = this.GetMethodByNameAndParamCountQuiet(this.homelandFarmPlantProtocolManagerType, "SendCollectSeedCommand", 1);

            List<string> missingMethods = new List<string>();
            if (this.homelandFarmDataCenterTryGetComponentDataMethodDef == null) missingMethods.Add("DataCenter.TryGetComponentData");
            if (this.homelandFarmCropWaterPlantMethod == null) missingMethods.Add("CropProtocolManager.WaterPlant");
            if (this.homelandFarmCropPickPlantMethod == null) missingMethods.Add("CropProtocolManager.PickPlant");
            if (this.homelandFarmCropWeedMethod == null) missingMethods.Add("CropProtocolManager.CropWeed");
            if (this.homelandFarmCropPickProductMethod == null) missingMethods.Add("CropProtocolManager.PickCropProductCommand");
            if (this.homelandFarmCropSeedingMethod == null) missingMethods.Add("CropProtocolManager.CropSeeding");
            if (this.homelandFarmCropAddManureMethod == null) missingMethods.Add("CropProtocolManager.AddManure");
            if (this.homelandFarmPlantWaterPlantMethod == null) missingMethods.Add("PlantProtocolManager.WaterPlant");
            if (this.homelandFarmPlantCollectSeedMethod == null) missingMethods.Add("PlantProtocolManager.SendCollectSeedCommand");

            if (missingMethods.Count > 0)
            {
                status = "missing method(s): " + string.Join(", ", missingMethods.ToArray());
                this.homelandFarmManagedReflectionUnavailable = true;
                this.homelandFarmManagedReflectionUnavailableStatus = status;
                return false;
            }

            return true;
        }

        private bool TryResolveHomelandFarmAuraProtocol(out string status)
        {
            status = string.Empty;
            bool hasCropWater = this.homelandFarmAuraCropWaterPlant2Method != IntPtr.Zero
                || this.homelandFarmAuraCropWaterPlant3Method != IntPtr.Zero;
            bool hasPlantWater = this.homelandFarmAuraPlantWaterPlantMethod != IntPtr.Zero
                || this.homelandFarmAuraPlantWaterPlant2Method != IntPtr.Zero;
            if (hasCropWater && hasPlantWater)
            {
                return true;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                status = "AuraMono protocol API unavailable.";
                return false;
            }

            IntPtr cropProtocolClass = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.Plant.CropProtocolManager");
            if (cropProtocolClass == IntPtr.Zero)
            {
                cropProtocolClass = this.FindHomelandFarmAuraClass(
                    "XDTDataAndProtocol.ProtocolService.Plant.CropProtocolManager",
                    "XDTDataAndProtocol.ProtocolService.Plant",
                    "CropProtocolManager");
            }

            IntPtr plantProtocolClass = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.Plant.PlantProtocolManager");
            if (plantProtocolClass == IntPtr.Zero)
            {
                plantProtocolClass = this.FindHomelandFarmAuraClass(
                    "XDTDataAndProtocol.ProtocolService.Plant.PlantProtocolManager",
                    "XDTDataAndProtocol.ProtocolService.Plant",
                    "PlantProtocolManager");
            }

            if (cropProtocolClass != IntPtr.Zero)
            {
                if (this.homelandFarmAuraCropWaterPlant2Method == IntPtr.Zero)
                {
                    this.homelandFarmAuraCropWaterPlant2Method = this.FindAuraMonoMethodOnHierarchy(cropProtocolClass, "WaterPlant", 2);
                }

                if (this.homelandFarmAuraCropWaterPlant3Method == IntPtr.Zero)
                {
                    this.homelandFarmAuraCropWaterPlant3Method = this.FindAuraMonoMethodOnHierarchy(cropProtocolClass, "WaterPlant", 3);
                }

                if (this.homelandFarmAuraCropPickPlantMethod == IntPtr.Zero)
                {
                    this.homelandFarmAuraCropPickPlantMethod = this.FindAuraMonoMethodOnHierarchy(cropProtocolClass, "PickPlant", 1);
                }

                if (this.homelandFarmAuraCropWeedMethod == IntPtr.Zero)
                {
                    this.homelandFarmAuraCropWeedMethod = this.FindAuraMonoMethodOnHierarchy(cropProtocolClass, "CropWeed", 1);
                }

                if (this.homelandFarmAuraCropAddManureMethod == IntPtr.Zero)
                {
                    this.homelandFarmAuraCropAddManureMethod = this.FindAuraMonoMethodOnHierarchy(cropProtocolClass, "AddManure", 1);
                }

                if (this.homelandFarmAuraCropSeedingMethod == IntPtr.Zero)
                {
                    this.homelandFarmAuraCropSeedingMethod = this.FindAuraMonoMethodOnHierarchy(cropProtocolClass, "CropSeeding", 2);
                }
            }

            IntPtr characterProtocolClass = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.GamePlay.Character.CharacterProtocolManager");
            if (characterProtocolClass == IntPtr.Zero)
            {
                characterProtocolClass = this.FindHomelandFarmAuraClass(
                    "XDTDataAndProtocol.ProtocolService.GamePlay.Character.CharacterProtocolManager",
                    "XDTDataAndProtocol.ProtocolService.GamePlay.Character",
                    "CharacterProtocolManager");
            }

            if (characterProtocolClass != IntPtr.Zero && this.homelandFarmAuraCharacterEquipHandholdMethod == IntPtr.Zero)
            {
                this.homelandFarmAuraCharacterEquipHandholdMethod = this.FindAuraMonoMethodOnHierarchy(characterProtocolClass, "EquipHandhold", 1);
            }

            if (plantProtocolClass != IntPtr.Zero)
            {
                if (this.homelandFarmAuraPlantWaterPlantMethod == IntPtr.Zero)
                {
                    this.homelandFarmAuraPlantWaterPlantMethod = this.FindAuraMonoMethodOnHierarchy(plantProtocolClass, "WaterPlant", 3);
                }

                if (this.homelandFarmAuraPlantWaterPlant2Method == IntPtr.Zero)
                {
                    this.homelandFarmAuraPlantWaterPlant2Method = this.FindAuraMonoMethodOnHierarchy(plantProtocolClass, "WaterPlant", 2);
                }

                if (this.homelandFarmAuraPlantCollectSeedMethod == IntPtr.Zero)
                {
                    this.homelandFarmAuraPlantCollectSeedMethod = this.FindAuraMonoMethodOnHierarchy(plantProtocolClass, "SendCollectSeedCommand", 1);
                }
            }

            hasCropWater = this.homelandFarmAuraCropWaterPlant2Method != IntPtr.Zero
                || this.homelandFarmAuraCropWaterPlant3Method != IntPtr.Zero;
            hasPlantWater = this.homelandFarmAuraPlantWaterPlantMethod != IntPtr.Zero
                || this.homelandFarmAuraPlantWaterPlant2Method != IntPtr.Zero;
            if (!hasCropWater || !hasPlantWater)
            {
                status = "AuraMono WaterPlant unavailable crop2=0x" + this.homelandFarmAuraCropWaterPlant2Method.ToInt64().ToString("X")
                    + " crop3=0x" + this.homelandFarmAuraCropWaterPlant3Method.ToInt64().ToString("X")
                    + " plant3=0x" + this.homelandFarmAuraPlantWaterPlantMethod.ToInt64().ToString("X")
                    + " plant2=0x" + this.homelandFarmAuraPlantWaterPlant2Method.ToInt64().ToString("X");
                return false;
            }

            return true;
        }

        private bool TryEnsureHomelandFarmAuraReflection(out string status)
        {
            status = string.Empty;
            if (!this.TryResolveHomelandFarmAuraProtocol(out status))
            {
                return false;
            }

            this.homelandFarmAuraDataCenterClass = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ComponentsData.DataCenter");
            if (this.homelandFarmAuraDataCenterClass == IntPtr.Zero)
            {
                this.homelandFarmAuraDataCenterClass = this.FindHomelandFarmAuraClass(
                    "XDTDataAndProtocol.ComponentsData.DataCenter",
                    "XDTDataAndProtocol.ComponentsData",
                    "DataCenter");
            }

            this.TryResolveHomelandFarmOptionalManagedDataTypes();
            return true;
        }

        private void TryResolveHomelandFarmOptionalManagedDataTypes()
        {
            if (this.homelandFarmCropItemDataType == null)
            {
                this.homelandFarmCropItemDataType = this.FindHomelandFarmRuntimeType(
                    "CropItemData",
                    "XDTDataAndProtocol.ComponentsData",
                    "ScriptsRefactory.DataAndProtocol.ComponentsData");
            }

            if (this.homelandFarmCropBoxItemDataType == null)
            {
                this.homelandFarmCropBoxItemDataType = this.FindHomelandFarmRuntimeType(
                    "CropBoxItemData",
                    "XDTDataAndProtocol.ComponentsData",
                    "ScriptsRefactory.DataAndProtocol.ComponentsData");
            }

            if (this.homelandFarmPlantItemDataType == null)
            {
                this.homelandFarmPlantItemDataType = this.FindHomelandFarmRuntimeType(
                    "PlantItemData",
                    "XDTDataAndProtocol.ComponentsData",
                    "ScriptsRefactory.DataAndProtocol.ComponentsData");
            }

            if (this.homelandFarmLevelEntityComponentDataType == null)
            {
                this.homelandFarmLevelEntityComponentDataType = this.FindHomelandFarmRuntimeType(
                    "LevelEntityComponentData",
                    "XDTDataAndProtocol.ComponentsData",
                    "ScriptsRefactory.DataAndProtocol.ComponentsData");
            }

            if (this.homelandFarmNetIdType == null)
            {
                this.homelandFarmNetIdType = this.FindHomelandFarmRuntimeType(
                    "NetId",
                    "EcsClient.XDT.Scene.Shared.Data.SharedData",
                    "XDT.Scene.Shared.Data.SharedData");
            }

            if (this.homelandFarmCropBoxItemDataType != null && this.homelandFarmCropBoxGetWaterCountMethod == null)
            {
                this.homelandFarmCropBoxGetWaterCountMethod = this.homelandFarmCropBoxItemDataType.GetMethod(
                    "GetWaterCount",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    Type.EmptyTypes);
            }
        }

        private IntPtr FindHomelandFarmAuraClass(string fullName, string namespaceName, string shortName)
        {
            IntPtr cls = this.FindAuraMonoClassByFullName(fullName);
            if (cls != IntPtr.Zero)
            {
                return cls;
            }

            return this.FindAuraMonoClassAcrossLoadedAssemblies(namespaceName, shortName);
        }

        // Resolve each cached mono class independently; never short-circuit on partial cache hit.
        private bool TryResolveHomelandFarmAuraScanClasses(out string status)
        {
            status = string.Empty;
            string managerStatus = string.Empty;

            if (this.homelandFarmAuraLevelObjectManagerClass == IntPtr.Zero
                || this.homelandFarmAuraEntitiesClass == IntPtr.Zero)
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    status = "AuraMono entity scan unavailable.";
                    return this.homelandFarmAuraLevelObjectManagerClass != IntPtr.Zero
                        || this.homelandFarmAuraEntitiesClass != IntPtr.Zero;
                }
            }

            if (this.homelandFarmAuraLevelObjectManagerClass == IntPtr.Zero)
            {
                if (this.TryResolveAuraMonoLevelObjectManager(out IntPtr managerObj, out IntPtr managerClass, out managerStatus)
                    && managerObj != IntPtr.Zero
                    && managerClass != IntPtr.Zero)
                {
                    this.homelandFarmAuraLevelObjectManagerClass = managerClass;
                }
            }

            if (this.homelandFarmAuraEntitiesClass == IntPtr.Zero)
            {
                this.homelandFarmAuraEntitiesClass = this.FindHomelandFarmAuraClass(
                    "XDTLevelAndEntity.BaseSystem.EntitiesManager.Entities",
                    "XDTLevelAndEntity.BaseSystem.EntitiesManager",
                    "Entities");
                if (this.homelandFarmAuraEntitiesClass == IntPtr.Zero)
                {
                    this.homelandFarmAuraEntitiesClass = this.FindHomelandFarmAuraClass(
                        "ScriptsRefactory.LevelAndEntity.BaseSystem.EntitiesManager.Entities",
                        "ScriptsRefactory.LevelAndEntity.BaseSystem.EntitiesManager",
                        "Entities");
                }
            }

            if (this.homelandFarmAuraLevelObjectManagerClass != IntPtr.Zero || this.homelandFarmAuraEntitiesClass != IntPtr.Zero)
            {
                return true;
            }

            status = string.IsNullOrEmpty(managerStatus) ? "AuraMono entity scan unavailable." : managerStatus;
            return false;
        }

        private bool HomelandFarmUsesAuraReflection()
        {
            return this.TryResolveHomelandFarmAuraProtocol(out _)
                && (!this.homelandFarmManagedReflectionReady || this.homelandFarmAuraReflectionReady);
        }

        private bool HomelandFarmPrefersAuraComponentData()
        {
            if (this.TryEnsureHomelandFarmComponentDataManagedReflection())
            {
                return false;
            }

            return this.homelandFarmAuraReflectionReady
                || (this.homelandFarmManagedReflectionUnavailable && this.TryResolveHomelandFarmAuraProtocol(out _));
        }

        // Resolve only DataCenter + component data types (no protocol methods). Used to avoid
        // native AuraMono GetAllComponents after heavy entity scans when DotnetAssemblies are loaded.
        private bool TryEnsureHomelandFarmComponentDataManagedReflection()
        {
            if (this.homelandFarmDataCenterTryGetComponentDataMethodDef != null
                && this.homelandFarmCropBoxItemDataType != null
                && this.homelandFarmCropItemDataType != null)
            {
                return true;
            }

            // Not (fully) resolved. The resolution below reloads interop, clears reflection miss
            // caches and runs several full type scans — far too costly to repeat per classify when
            // the types simply do not exist in this build. Throttle re-attempts so the common
            // "managed component data unavailable" case stays cheap (callers fall back to AuraMono).
            float nowResolve = Time.realtimeSinceStartup;
            if (nowResolve < this.homelandFarmComponentDataReflectionRetryAt)
            {
                return false;
            }

            this.homelandFarmComponentDataReflectionRetryAt = nowResolve + HomelandFarmAuraComponentClassResolveRetrySeconds;

            this.TryEnsureHomelandFarmInteropAssembliesLoaded();
            this.ClearHomelandFarmReflectionMissCaches();

            if (this.homelandFarmDataCenterType == null)
            {
                this.homelandFarmDataCenterType = this.ResolveHomelandFarmManagedType(
                    "DataCenter",
                    "XDTDataAndProtocol.ComponentsData.DataCenter",
                    "ScriptsRefactory.DataAndProtocol.ComponentsData.DataCenter");
            }

            if (this.homelandFarmCropItemDataType == null)
            {
                this.homelandFarmCropItemDataType = this.ResolveHomelandFarmManagedType(
                    "CropItemData",
                    "XDTDataAndProtocol.ComponentsData.CropItemData",
                    "ScriptsRefactory.DataAndProtocol.ComponentsData.CropItemData");
            }

            if (this.homelandFarmCropBoxItemDataType == null)
            {
                this.homelandFarmCropBoxItemDataType = this.ResolveHomelandFarmManagedType(
                    "CropBoxItemData",
                    "XDTDataAndProtocol.ComponentsData.CropBoxItemData",
                    "ScriptsRefactory.DataAndProtocol.ComponentsData.CropBoxItemData");
            }

            if (this.homelandFarmPlantItemDataType == null)
            {
                this.homelandFarmPlantItemDataType = this.ResolveHomelandFarmManagedType(
                    "PlantItemData",
                    "XDTDataAndProtocol.ComponentsData.PlantItemData",
                    "ScriptsRefactory.DataAndProtocol.ComponentsData.PlantItemData");
            }

            if (this.homelandFarmNetIdType == null)
            {
                this.homelandFarmNetIdType = this.ResolveHomelandFarmManagedType(
                    "NetId",
                    "EcsClient.XDT.Scene.Shared.Data.SharedData.NetId",
                    "XDT.Scene.Shared.Data.SharedData.NetId");
            }

            if (this.homelandFarmDataCenterTryGetComponentDataMethodDef == null && this.homelandFarmDataCenterType != null)
            {
                this.homelandFarmDataCenterTryGetComponentDataMethodDef = this.ResolveHomelandFarmTryGetComponentDataMethodDef();
            }

            return this.homelandFarmDataCenterTryGetComponentDataMethodDef != null
                && this.homelandFarmCropBoxItemDataType != null
                && this.homelandFarmCropItemDataType != null
                && this.homelandFarmNetIdType != null;
        }

        private MethodInfo ResolveHomelandFarmTryGetComponentDataMethodDef()
        {
            if (this.homelandFarmDataCenterType == null || this.homelandFarmNetIdType == null)
            {
                return null;
            }

            foreach (MethodInfo method in this.homelandFarmDataCenterType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (method == null || method.Name != "TryGetComponentData" || !method.IsGenericMethodDefinition)
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 2
                    && string.Equals(parameters[0].ParameterType.Name, "NetId", StringComparison.Ordinal))
                {
                    return method;
                }
            }

            return null;
        }

        private MethodInfo ResolveHomelandFarmCropWaterPlantMethod()
        {
            if (this.homelandFarmCropProtocolManagerType == null)
            {
                return null;
            }

            MethodInfo twoParam = this.GetMethodByNameAndParamCountQuiet(this.homelandFarmCropProtocolManagerType, "WaterPlant", 2);
            if (twoParam != null)
            {
                return twoParam;
            }

            return this.GetMethodByNameAndParamCountQuiet(this.homelandFarmCropProtocolManagerType, "WaterPlant", 3);
        }

        private MethodInfo ResolveHomelandFarmCropSeedingMethod()
        {
            if (this.homelandFarmCropProtocolManagerType == null || this.homelandFarmCropPlantPointType == null)
            {
                return null;
            }

            MethodInfo method = this.GetMethodByNameAndParamCountQuiet(this.homelandFarmCropProtocolManagerType, "CropSeeding", 2);
            if (method == null)
            {
                return null;
            }

            ParameterInfo[] parameters = method.GetParameters();
            if (parameters.Length != 2)
            {
                return null;
            }

            if (parameters[0].ParameterType != typeof(uint))
            {
                return null;
            }

            Type secondParamType = parameters[1].ParameterType;
            if (!secondParamType.IsGenericType || secondParamType.GetGenericTypeDefinition() != typeof(List<>))
            {
                return null;
            }

            Type listElementType = secondParamType.GetGenericArguments()[0];
            if (listElementType != this.homelandFarmCropPlantPointType && !listElementType.IsAssignableFrom(this.homelandFarmCropPlantPointType))
            {
                return null;
            }

            return method;
        }

        private MethodInfo ResolveHomelandFarmListOnlyStaticMethod(Type protocolType, string methodName)
        {
            if (protocolType == null || string.IsNullOrEmpty(methodName))
            {
                return null;
            }

            MethodInfo method = this.GetMethodByNameAndParamCountQuiet(protocolType, methodName, 1);
            if (method == null)
            {
                return null;
            }

            ParameterInfo[] parameters = method.GetParameters();
            if (!this.TryHomelandFarmIsGenericListType(parameters[0].ParameterType))
            {
                return null;
            }

            return method;
        }

        private bool TryHomelandFarmIsGenericListType(Type type)
        {
            if (type == null || !type.IsGenericType)
            {
                return false;
            }

            Type genericDef = type.GetGenericTypeDefinition();
            if (genericDef == typeof(List<>))
            {
                return true;
            }

            string name = genericDef.Name ?? string.Empty;
            return name == "List`1" || name.StartsWith("List", StringComparison.Ordinal);
        }

        private sealed class HomelandFarmAuraComponentData
        {
            public IntPtr Handle;
        }

        private bool TryHomelandFarmGetComponentData(Type componentDataType, uint netId, out object data, out string status, string auraDataTypeName = null)
        {
            data = null;
            status = "Homeland farm component unavailable.";
            if (netId == 0U)
            {
                status = "Homeland farm netId missing.";
                return false;
            }

            if (componentDataType == null && string.IsNullOrEmpty(auraDataTypeName) && !this.HomelandFarmPrefersAuraComponentData())
            {
                status = "Homeland farm component type missing.";
                return false;
            }

            try
            {
                if (!this.EnsureHomelandFarmReflectionReady())
                {
                    status = string.IsNullOrEmpty(this.homelandFarmReflectionUnavailableStatus)
                        ? "Homeland farm reflection unavailable."
                        : this.homelandFarmReflectionUnavailableStatus;
                    return false;
                }

                if (this.HomelandFarmPrefersAuraComponentData())
                {
                    string typeName = componentDataType != null ? componentDataType.Name : auraDataTypeName ?? string.Empty;
                    if (string.IsNullOrEmpty(typeName))
                    {
                        status = "Homeland farm aura component type missing.";
                        return false;
                    }

                    if (this.TryHomelandFarmResolveAuraComponentData(netId, typeName, out IntPtr handle) && handle != IntPtr.Zero)
                    {
                        data = new HomelandFarmAuraComponentData { Handle = handle };
                        status = "Aura component ready.";
                        return true;
                    }

                    status = "Aura component missing for netId " + netId + ".";
                    return false;
                }

                MethodInfo tryGetMethod = this.homelandFarmDataCenterTryGetComponentDataMethodDef.MakeGenericMethod(componentDataType);
                object netIdArg = this.CreateNetCookNetIdArgument(this.homelandFarmNetIdType, netId);
                if (netIdArg == null)
                {
                    status = "Homeland farm NetId argument creation failed.";
                    return false;
                }

                object dataBox = Activator.CreateInstance(componentDataType);
                object[] args = new object[] { netIdArg, dataBox };
                bool found = tryGetMethod.Invoke(null, args) is bool ok && ok;
                if (!found)
                {
                    status = "Homeland farm component missing for netId " + netId + ".";
                    return false;
                }

                data = args[1] ?? dataBox;
                status = "Homeland farm component ready.";
                return data != null;
            }
            catch (Exception ex)
            {
                status = "Homeland farm component exception: " + ex.Message;
                return false;
            }
        }

        private bool TryHomelandFarmReadComponentBool(object data, out bool value, params string[] members)
        {
            value = false;
            if (data is HomelandFarmAuraComponentData auraData && auraData.Handle != IntPtr.Zero)
            {
                for (int i = 0; i < members.Length; i++)
                {
                    if (this.TryGetMonoBoolMember(auraData.Handle, members[i], out value))
                    {
                        return true;
                    }
                }

                return false;
            }

            return this.TryReadBooleanMember(data, members);
        }

        private bool TryHomelandFarmReadComponentInt(object data, out int value, params string[] members)
        {
            value = 0;
            if (data is HomelandFarmAuraComponentData auraData && auraData.Handle != IntPtr.Zero)
            {
                for (int i = 0; i < members.Length; i++)
                {
                    if (this.TryGetMonoInt32Member(auraData.Handle, members[i], out value))
                    {
                        return true;
                    }
                }

                return false;
            }

            for (int i = 0; i < members.Length; i++)
            {
                if (this.TryReadManagedInt32Member(data, members[i], out value))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryHomelandFarmReadComponentUInt(object data, out uint value, params string[] members)
        {
            value = 0U;
            if (data is HomelandFarmAuraComponentData auraData && auraData.Handle != IntPtr.Zero)
            {
                for (int i = 0; i < members.Length; i++)
                {
                    if (this.TryGetMonoUInt32Member(auraData.Handle, members[i], out value) && value != 0U)
                    {
                        return true;
                    }
                }

                return false;
            }

            for (int i = 0; i < members.Length; i++)
            {
                if (this.TryGetUIntMember(data, members[i], out value) && value != 0U)
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryHomelandFarmReadComponentLong(object data, out long value, params string[] members)
        {
            value = 0L;
            if (data is HomelandFarmAuraComponentData auraData && auraData.Handle != IntPtr.Zero)
            {
                for (int i = 0; i < members.Length; i++)
                {
                    // 8-byte field read; reinterpret as signed (sowTime uses -1 as a sentinel).
                    if (this.TryGetMonoUInt64Member(auraData.Handle, members[i], out ulong raw))
                    {
                        value = (long)raw;
                        return true;
                    }
                }

                return false;
            }

            for (int i = 0; i < members.Length; i++)
            {
                if (this.TryGetObjectMember(data, members[i], out object boxed) && boxed != null)
                {
                    try
                    {
                        value = Convert.ToInt64(boxed);
                        return true;
                    }
                    catch
                    {
                    }
                }
            }

            return false;
        }

        private bool TryHomelandFarmResolveAuraComponentData(uint netId, string dataTypeName, out IntPtr dataHandle)
        {
            dataHandle = IntPtr.Zero;
            if (netId == 0U || string.IsNullOrEmpty(dataTypeName))
            {
                return false;
            }

            string cacheKey = HomelandFarmAuraComponentCacheKey(netId, dataTypeName);
            if (this.homelandFarmAuraComponentMissCache.Contains(cacheKey))
            {
                return false;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoObjectGetClass == null)
            {
                return false;
            }

            if (!this.TryGetAuraMonoEntityObjectByNetId(netId, out IntPtr entityObj) || entityObj == IntPtr.Zero)
            {
                this.homelandFarmAuraComponentMissCache.Add(cacheKey);
                return false;
            }

            if (!this.TryHomelandFarmTryGuardAuraEntityBeforeHeavyAccess(entityObj))
            {
                this.homelandFarmAuraComponentMissCache.Add(cacheKey);
                return false;
            }

            if (!this.TryInvokeAuraMonoZeroArg(entityObj, out IntPtr componentsObj, "GetAllComponents") || componentsObj == IntPtr.Zero)
            {
                this.homelandFarmAuraComponentMissCache.Add(cacheKey);
                return false;
            }

            List<IntPtr> components = new List<IntPtr>();
            if (!this.TryEnumerateAuraMonoCollectionItems(componentsObj, components))
            {
                this.homelandFarmAuraComponentMissCache.Add(cacheKey);
                return false;
            }

            string[] componentHints = this.GetHomelandFarmComponentHints(dataTypeName);
            for (int i = 0; i < components.Count; i++)
            {
                IntPtr componentObj = components[i];
                if (componentObj == IntPtr.Zero)
                {
                    continue;
                }

                string className = this.GetAuraMonoClassDisplayName(auraMonoObjectGetClass(componentObj));
                bool componentMatch = false;
                for (int h = 0; h < componentHints.Length; h++)
                {
                    if (!string.IsNullOrEmpty(className)
                        && className.IndexOf(componentHints[h], StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        componentMatch = true;
                        break;
                    }
                }

                if (!componentMatch)
                {
                    continue;
                }

                string[] dataMembers = { "ComponentData", "_componentData", "componentData", "data", "_data", "Data" };
                for (int m = 0; m < dataMembers.Length; m++)
                {
                    if (this.TryGetMonoObjectMember(componentObj, dataMembers[m], out IntPtr nested) && nested != IntPtr.Zero)
                    {
                        dataHandle = nested;
                        return true;
                    }
                }

                dataHandle = componentObj;
                return true;
            }

            this.homelandFarmAuraComponentMissCache.Add(cacheKey);
            return false;
        }

        private bool TryHomelandFarmAuraExtractFarmDataHandles(
            IntPtr entityObj,
            out IntPtr cropItemDataHandle,
            out IntPtr cropBoxItemDataHandle,
            out IntPtr plantItemDataHandle)
        {
            cropItemDataHandle = IntPtr.Zero;
            cropBoxItemDataHandle = IntPtr.Zero;
            plantItemDataHandle = IntPtr.Zero;
            if (entityObj == IntPtr.Zero
                || !this.EnsureAuraMonoApiReady()
                || !this.AttachAuraMonoThread()
                || auraMonoObjectGetClass == null)
            {
                return false;
            }

            if (!this.TryHomelandFarmTryGuardAuraEntityBeforeHeavyAccess(entityObj))
            {
                return false;
            }

            if (!this.TryInvokeAuraMonoZeroArg(entityObj, out IntPtr componentsObj, "GetAllComponents") || componentsObj == IntPtr.Zero)
            {
                return false;
            }

            List<IntPtr> components = new List<IntPtr>();
            if (!this.TryEnumerateAuraMonoCollectionItems(componentsObj, components))
            {
                return false;
            }

            bool hasComponentClasses = this.TryResolveAuraMonoFarmComponentClasses(
                out IntPtr plantComponentClass,
                out IntPtr cropBoxComponentClass,
                out IntPtr cropComponentClass);

            for (int i = 0; i < components.Count; i++)
            {
                IntPtr componentObj = components[i];
                if (componentObj == IntPtr.Zero)
                {
                    continue;
                }

                IntPtr componentClass = auraMonoObjectGetClass(componentObj);
                if (componentClass == IntPtr.Zero)
                {
                    continue;
                }

                IntPtr dataHandle = this.TryHomelandFarmResolveAuraComponentDataHandle(componentObj);
                if (dataHandle == IntPtr.Zero)
                {
                    continue;
                }

                if (hasComponentClasses)
                {
                    if (cropBoxItemDataHandle == IntPtr.Zero
                        && cropBoxComponentClass != IntPtr.Zero
                        && this.IsAuraMonoClassAssignableTo(componentClass, cropBoxComponentClass))
                    {
                        cropBoxItemDataHandle = dataHandle;
                    }

                    if (plantItemDataHandle == IntPtr.Zero
                        && plantComponentClass != IntPtr.Zero
                        && this.IsAuraMonoClassAssignableTo(componentClass, plantComponentClass))
                    {
                        plantItemDataHandle = dataHandle;
                    }

                    if (cropItemDataHandle == IntPtr.Zero
                        && cropComponentClass != IntPtr.Zero
                        && this.IsAuraMonoClassAssignableTo(componentClass, cropComponentClass))
                    {
                        cropItemDataHandle = dataHandle;
                    }
                }

                string className = this.GetAuraMonoClassDisplayName(componentClass);
                if (string.IsNullOrEmpty(className))
                {
                    continue;
                }

                if (cropBoxItemDataHandle == IntPtr.Zero
                    && (className.IndexOf("CropBoxComponent", StringComparison.OrdinalIgnoreCase) >= 0
                        || className.IndexOf("CropBoxItemData", StringComparison.OrdinalIgnoreCase) >= 0
                        || (className.IndexOf("CropBox", StringComparison.OrdinalIgnoreCase) >= 0
                            && className.IndexOf("CropComponent", StringComparison.OrdinalIgnoreCase) < 0)))
                {
                    cropBoxItemDataHandle = dataHandle;
                }

                if (plantItemDataHandle == IntPtr.Zero
                    && (className.IndexOf("PlantComponent", StringComparison.OrdinalIgnoreCase) >= 0
                        || className.IndexOf("PlantItemData", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    plantItemDataHandle = dataHandle;
                }

                if (cropItemDataHandle == IntPtr.Zero
                    && className.IndexOf("CropBoxComponent", StringComparison.OrdinalIgnoreCase) < 0
                    && className.IndexOf("CropBox", StringComparison.OrdinalIgnoreCase) < 0
                    && (className.IndexOf("CropComponent", StringComparison.OrdinalIgnoreCase) >= 0
                        || className.IndexOf("CropItemData", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    cropItemDataHandle = dataHandle;
                }
            }

            return cropItemDataHandle != IntPtr.Zero
                || cropBoxItemDataHandle != IntPtr.Zero
                || plantItemDataHandle != IntPtr.Zero;
        }

        private static object HomelandFarmAuraData(IntPtr handle)
        {
            return new HomelandFarmAuraComponentData { Handle = handle };
        }

        private string[] GetHomelandFarmComponentHints(string dataTypeName)
        {
            switch (dataTypeName)
            {
                case "CropBoxItemData":
                    return new[] { "CropBoxComponent", "CropBoxItemData" };
                case "PlantItemData":
                    return new[] { "PlantComponent", "PlantItemData" };
                case "CropItemData":
                    return new[] { "CropComponent", "CropItemData" };
                case "BuildItemData":
                    return new[] { "BuildComponent", "BuildItemData" };
                case "TransformComponentData":
                    return new[] { "TransformComponent", "ResourceComponent", "TransformComponentData" };
                case "LevelEntityComponentData":
                    return new[] { "LevelEntityComponent", "LevelEntityComponentData" };
                default:
                    return new[] { dataTypeName, dataTypeName.Replace("ItemData", "Component") };
            }
        }

        private bool TryHomelandFarmIsInHomeland(out string status, bool allowVisitingFarmArea = false, bool logDecisions = true)
        {
            status = "Homeland state unavailable.";
            try
            {
                this.TryGetHomelandFarmPlayerNetId(out uint playerNetId, out _);
                if (this.TryHomelandFarmGetSelfPlayInFieldOwnerNetId(out uint fieldOwnerNetId) && fieldOwnerNetId != 0U)
                {
                    if (playerNetId != 0U && fieldOwnerNetId == playerNetId)
                    {
                        status = "Player is on own farm field.";
                        return true;
                    }

                    if (allowVisitingFarmArea)
                    {
                        status = "Visiting farm field owner=" + fieldOwnerNetId + ".";
                        if (logDecisions)
                        {
                            this.HomelandFarmLog("Homeland gate open via inFieldOwnerId (visiting).");
                        }

                        return true;
                    }
                }

                if (this.TryHomelandFarmTryReadInFieldNetIdAura(out uint inFieldNetId, out string inFieldSource) && inFieldNetId != 0U)
                {
                    if (allowVisitingFarmArea)
                    {
                        status = "Farm area via " + inFieldSource + " inFieldNetId=" + inFieldNetId + ".";
                        if (logDecisions)
                        {
                            this.HomelandFarmLog("Homeland gate open via inFieldNetId (visiting).");
                        }

                        return true;
                    }
                }

                if (this.TryHomelandFarmResolveLocalPlayerComponent(out object localPlayerComponent, out string source)
                    && localPlayerComponent != null
                    && this.TryHomelandFarmTryReadInHomeland(localPlayerComponent, out bool inHomeland))
                {
                    if (inHomeland)
                    {
                        status = "Player is in homeland.";
                        return true;
                    }

                    if (allowVisitingFarmArea && this.TryHomelandFarmHasScannableFarmEntities(out string scanSource))
                    {
                        status = "Farm area via " + scanSource + ".";
                        if (logDecisions)
                        {
                            this.HomelandFarmLog("Homeland gate open via farm scan (visiting).");
                        }

                        return true;
                    }

                    status = "homeland_farm.need_homeland";
                    if (logDecisions)
                    {
                        this.HomelandFarmLog("Homeland gate blocked via " + source + ": inHomeland=false.");
                    }

                    return false;
                }

                if (this.TryHomelandFarmTryReadInHomelandAura(out bool auraInHomeland, out string auraSource))
                {
                    if (auraInHomeland)
                    {
                        status = "Player is in homeland.";
                        if (logDecisions)
                        {
                            this.HomelandFarmLog("Homeland gate open via " + auraSource + ".");
                        }

                        return true;
                    }

                    if (allowVisitingFarmArea && this.TryHomelandFarmHasScannableFarmEntities(out string scanSource))
                    {
                        status = "Farm area via " + scanSource + ".";
                        if (logDecisions)
                        {
                            this.HomelandFarmLog("Homeland gate open via farm scan (visiting).");
                        }

                        return true;
                    }

                    status = "homeland_farm.need_homeland";
                    if (logDecisions)
                    {
                        this.HomelandFarmLog("Homeland gate blocked via " + auraSource + ": inHomeland=false.");
                    }

                    return false;
                }

                if (allowVisitingFarmArea && this.TryHomelandFarmHasScannableFarmEntities(out string farmScanSource))
                {
                    status = "Farm area via " + farmScanSource + ".";
                    if (logDecisions)
                    {
                        this.HomelandFarmLog("Homeland gate open via farm scan (visiting).");
                    }

                    return true;
                }

                status = "LocalPlayerComponent unavailable.";
                if (logDecisions)
                {
                    this.HomelandFarmLog("Homeland gate blocked: " + status);
                }

                return false;
            }
            catch (Exception ex)
            {
                status = "Homeland state exception: " + ex.Message;
                if (logDecisions)
                {
                    this.HomelandFarmLog("Homeland gate exception: " + ex.Message);
                }

                return false;
            }
        }

        private bool TryHomelandFarmHasScannableFarmEntities(out string source)
        {
            source = string.Empty;
            if (!this.EnsureHomelandFarmReflectionReady())
            {
                return false;
            }

            this.EnsureHomelandFarmScannerTypes();
            HashSet<uint> netIds = new HashSet<uint>();
            if (!this.TryHomelandFarmCollectFarmEntityNetIds(netIds, out source) || netIds.Count == 0)
            {
                return false;
            }

            source = string.IsNullOrEmpty(source) ? "LevelObjectManager" : source;
            source = source + "(" + netIds.Count + ")";
            return true;
        }

        private bool TryGetHomelandFarmPlayerNetId(out uint netId, out string status)
        {
            netId = 0U;
            status = "Player netId unavailable.";

            // Short-TTL memoization: the managed-first resolution below is ~150ms when managed
            // types are absent, and this is called once per target during a scan.
            if (this.homelandFarmCachedPlayerNetId != 0U
                && Time.realtimeSinceStartup - this.homelandFarmCachedPlayerNetIdAt < HomelandFarmPlayerNetIdCacheTtlSeconds)
            {
                netId = this.homelandFarmCachedPlayerNetId;
                status = "Player netId (cached).";
                return true;
            }

            try
            {
                if (this.TryGetManagedSelfPlayerEntityObject(out object entityObj, out string source) && entityObj != null)
                {
                    if (this.TryHomelandFarmTryReadEntityNetId(entityObj, out netId) && netId != 0U)
                    {
                        status = "Player netId via " + source + ".";
                        this.HomelandFarmCachePlayerNetId(netId);
                        return true;
                    }
                }

                this.EnsureHomelandFarmScannerTypes();
                if (this.homelandFarmEntityUtilGetSelfPlayerEntityMethod != null)
                {
                    object selfEntity = this.homelandFarmEntityUtilGetSelfPlayerEntityMethod.Invoke(null, null);
                    if (selfEntity != null && this.TryHomelandFarmTryReadEntityNetId(selfEntity, out netId) && netId != 0U)
                    {
                        status = "Player netId via EntityUtil.GetSelfPlayerEntity().";
                        this.HomelandFarmCachePlayerNetId(netId);
                        return true;
                    }
                }

                if (this.TryHomelandFarmTryReadPlayerNetIdAura(out netId, out string auraSource) && netId != 0U)
                {
                    status = "Player netId via " + auraSource + ".";
                    this.HomelandFarmCachePlayerNetId(netId);
                    return true;
                }

                status = "Player netId missing.";
                return false;
            }
            catch (Exception ex)
            {
                status = "Player netId exception: " + ex.Message;
                return false;
            }
        }

        private void HomelandFarmCachePlayerNetId(uint netId)
        {
            if (netId == 0U)
            {
                return;
            }

            this.homelandFarmCachedPlayerNetId = netId;
            this.homelandFarmCachedPlayerNetIdAt = Time.realtimeSinceStartup;
        }

        private bool TryGetHomelandFarmFriendNetIds(HashSet<uint> output, out string status)
        {
            status = "Friend service unavailable.";
            if (output == null)
            {
                status = "Friend netId output missing.";
                return false;
            }

            output.Clear();
            try
            {
                if (!this.TryHomelandFarmResolveFriendService(out object friendService, out status))
                {
                    return false;
                }

                if (this.homelandFarmFriendServiceGetFriendsMethod == null)
                {
                    status = "IFriendService.GetFriends unavailable.";
                    return false;
                }

                object friendsArg = this.homelandFarmFriendServiceGetFriendsMethod.GetParameters().Length == 0
                    ? null
                    : Activator.CreateInstance(this.homelandFarmFriendServiceGetFriendsMethod.GetParameters()[0].ParameterType);
                object friendsResult = this.homelandFarmFriendServiceGetFriendsMethod.GetParameters().Length == 0
                    ? this.homelandFarmFriendServiceGetFriendsMethod.Invoke(friendService, null)
                    : this.homelandFarmFriendServiceGetFriendsMethod.Invoke(friendService, new object[] { friendsArg });

                object friendsCollection = friendsArg ?? friendsResult;
                List<object> friendItems = new List<object>(32);
                if (!this.TryEnumerateManagedCollectionItems(friendsCollection, friendItems) && friendsCollection is IEnumerable enumerable)
                {
                    foreach (object item in enumerable)
                    {
                        if (item != null)
                        {
                            friendItems.Add(item);
                        }
                    }
                }

                if (friendItems.Count == 0)
                {
                    status = "No friends resolved.";
                    return true;
                }

                for (int i = 0; i < friendItems.Count; i++)
                {
                    if (this.TryHomelandFarmTryReadFriendPlayerNetId(friendItems[i], out uint friendNetId) && friendNetId != 0U)
                    {
                        output.Add(friendNetId);
                    }
                }

                status = "Resolved " + output.Count + " friend netId(s).";
                this.HomelandFarmLog(status);
                return true;
            }
            catch (Exception ex)
            {
                status = "Friend netId exception: " + ex.Message;
                return false;
            }
        }

        private bool ScanHomelandFarmWaterTargets(out List<HomelandFarmTarget> targets, out string status, bool allowVisitingFarmArea = false, float scanRadiusOverride = 0f)
        {
            targets = new List<HomelandFarmTarget>();
            status = "Homeland farm scan unavailable.";
            if (!this.TryHomelandFarmIsInHomeland(out status, allowVisitingFarmArea))
            {
                return false;
            }

            if (!this.EnsureHomelandFarmReflectionReady())
            {
                status = string.IsNullOrEmpty(this.homelandFarmReflectionUnavailableStatus)
                    ? "Homeland farm reflection unavailable."
                    : this.homelandFarmReflectionUnavailableStatus;
                return false;
            }

            HashSet<uint> netIds = new HashSet<uint>();
            string scanSource = string.Empty;
            bool hasPlayerPos = this.TryGetHomelandFarmPlayerPosition(out Vector3 playerPos);
            float buildRadiusSq = -1f;
            if (hasPlayerPos)
            {
                // For a small InRadius request, scan only the requested radius (+small border)
                // instead of forcing the 30m default — that kept the scan checking hundreds of
                // far entities and froze the game. A wider default is still used for whole-field modes.
                float radius = scanRadiusOverride > 0f
                    ? scanRadiusOverride + 2f
                    : Mathf.Max(this.homelandFarmWaterRadius, HomelandFarmDefaultWaterRadius);
                if (!this.TryHomelandFarmCollectFarmEntityNetIds(
                        netIds,
                        out scanSource,
                        playerPos,
                        radius,
                        useAutoFarmCollectShortcuts: false))
                {
                    status = string.IsNullOrEmpty(scanSource) ? "No homeland farm entities found." : scanSource;
                    return false;
                }

                // For radius requests, drop far netIds before the expensive BuildWaterTarget step
                // (which does several component resolves each). The cheap cached position check
                // keeps build work proportional to the targets actually in range.
                if (scanRadiusOverride > 0f)
                {
                    buildRadiusSq = (scanRadiusOverride + 2f) * (scanRadiusOverride + 2f);
                }
            }
            else if (!this.TryHomelandFarmCollectFarmEntityNetIds(netIds, out scanSource))
            {
                status = string.IsNullOrEmpty(scanSource) ? "No homeland farm entities found." : scanSource;
                return false;
            }

            foreach (uint netId in netIds)
            {
                if (netId == 0U)
                {
                    continue;
                }

                if (buildRadiusSq >= 0f
                    && this.TryHomelandFarmResolveFarmEntityPosition(netId, out Vector3 netIdPos)
                    && netIdPos != Vector3.zero
                    && (netIdPos - playerPos).sqrMagnitude > buildRadiusSq)
                {
                    continue;
                }

                if (!this.TryHomelandFarmTryNormalizeWaterNetId(netId, netIds, out uint waterNetId))
                {
                    continue;
                }

                if (!this.TryHomelandFarmBuildWaterTarget(waterNetId, out HomelandFarmTarget target))
                {
                    continue;
                }

                targets.Add(target);
            }

            status = "Scanned " + targets.Count + " water target(s) via " + scanSource + ".";
            this.HomelandFarmLog(status);
            return targets.Count > 0;
        }

        private void FilterHomelandFarmByRadius(List<HomelandFarmTarget> targets, Vector3 center, float radius)
        {
            if (targets == null || targets.Count == 0)
            {
                return;
            }

            float radiusSq = radius * radius;
            for (int i = targets.Count - 1; i >= 0; i--)
            {
                HomelandFarmTarget target = targets[i];
                if (target == null)
                {
                    targets.RemoveAt(i);
                    continue;
                }

                if (target.Position == Vector3.zero)
                {
                    this.TryHomelandFarmResolveFarmEntityPosition(target.NetId, out target.Position);
                }

                if (target.Position == Vector3.zero)
                {
                    targets.RemoveAt(i);
                    continue;
                }

                if ((target.Position - center).sqrMagnitude > radiusSq)
                {
                    targets.RemoveAt(i);
                }
            }
        }

        private void FilterHomelandFarmOwn(List<HomelandFarmTarget> targets, uint playerNetId)
        {
            if (targets == null || targets.Count == 0 || playerNetId == 0U)
            {
                return;
            }

            bool onOwnField = this.TryHomelandFarmIsOnOwnFarmField(playerNetId);
            for (int i = targets.Count - 1; i >= 0; i--)
            {
                HomelandFarmTarget target = targets[i];
                if (target == null)
                {
                    targets.RemoveAt(i);
                    continue;
                }

                if (target.OwnerId == playerNetId)
                {
                    continue;
                }

                if (target.OwnerId == 0U && onOwnField)
                {
                    continue;
                }

                targets.RemoveAt(i);
            }
        }

        private void FilterHomelandFarmFriends(List<HomelandFarmTarget> targets, HashSet<uint> friendNetIds)
        {
            if (targets == null || targets.Count == 0 || friendNetIds == null || friendNetIds.Count == 0)
            {
                if (targets != null)
                {
                    targets.Clear();
                }

                return;
            }

            for (int i = targets.Count - 1; i >= 0; i--)
            {
                HomelandFarmTarget target = targets[i];
                if (target == null || !friendNetIds.Contains(target.OwnerId))
                {
                    targets.RemoveAt(i);
                }
            }
        }

        private void FilterHomelandFarmUnwatered(List<HomelandFarmTarget> targets)
        {
            if (targets == null || targets.Count == 0)
            {
                return;
            }

            for (int i = targets.Count - 1; i >= 0; i--)
            {
                HomelandFarmTarget target = targets[i];
                if (target == null || !target.NeedsWater)
                {
                    targets.RemoveAt(i);
                }
            }
        }

        // Shared radius scan for crop boxes. Collects crop netIds within the farm radius around
        // the player and keeps those whose CropItemData matches the predicate. When requireOwn is
        // true only the player's own crops are kept (harvest/fertilize); otherwise any owner is
        // allowed (water/weed work on visited fields too).
        private List<uint> ScanHomelandFarmCropsByRadius(
            Func<object, bool> cropPredicate,
            string label,
            bool requireOwn,
            HashSet<uint> preCollectedNetIds = null,
            bool logScanSummary = true,
            bool includePlantData = false,
            bool useAutoFarmCollectShortcuts = false,
            bool useCapturedScanCenter = false)
        {
            List<uint> result = new List<uint>();
            if (cropPredicate == null || !this.EnsureHomelandFarmReflectionReady())
            {
                return result;
            }

            uint playerNetId = 0U;
            bool onOwnField = false;
            uint effectiveOwnerNetId = 0U;
            if (requireOwn)
            {
                this.TryGetHomelandFarmPlayerNetId(out playerNetId, out _);
                if (playerNetId == 0U)
                {
                    this.HomelandFarmLog(label + ": player netId unavailable for own filter.");
                    return result;
                }

                onOwnField = this.TryHomelandFarmIsOnOwnFarmField(playerNetId);
                effectiveOwnerNetId = playerNetId;
                if (this.TryHomelandFarmGetSelfPlayInFieldOwnerNetId(out uint fieldOwnerNetId) && fieldOwnerNetId != 0U)
                {
                    effectiveOwnerNetId = fieldOwnerNetId;
                }
            }

            HashSet<uint> netIds = preCollectedNetIds;
            bool hasPlayerPos = useCapturedScanCenter || useAutoFarmCollectShortcuts
                ? this.TryGetHomelandFarmScanCenter(out Vector3 playerPos)
                : this.TryGetHomelandFarmPlayerPosition(out playerPos);
            float radius = this.homelandFarmWaterRadius;
            if (netIds == null)
            {
                netIds = new HashSet<uint>();
                float collectRadius = radius + 2f;
                if (!useAutoFarmCollectShortcuts
                    && hasPlayerPos
                    && this.TryHomelandFarmTryReuseManualRadiusCollect(playerPos, collectRadius, netIds))
                {
                    this.HomelandFarmLog(label + ": reusing manual radius collect (" + netIds.Count + " netId(s)).");
                }
                else if (hasPlayerPos)
                {
                    this.TryHomelandFarmCollectFarmEntityNetIds(
                        netIds,
                        out _,
                        playerPos,
                        collectRadius,
                        useAutoFarmCollectShortcuts: useAutoFarmCollectShortcuts);
                }
                else
                {
                    this.TryHomelandFarmCollectFarmEntityNetIds(
                        netIds,
                        out _,
                        Vector3.zero,
                        0f,
                        useAutoFarmCollectShortcuts: useAutoFarmCollectShortcuts);
                }
            }

            return this.FilterHomelandFarmCropsFromNetIds(
                netIds,
                cropPredicate,
                label,
                requireOwn,
                playerNetId,
                effectiveOwnerNetId,
                onOwnField,
                hasPlayerPos,
                playerPos,
                radius,
                logScanSummary,
                includePlantData);
        }

        private List<uint> FilterHomelandFarmCropsFromNetIds(
            HashSet<uint> netIds,
            Func<object, bool> cropPredicate,
            string label,
            bool requireOwn,
            uint playerNetId,
            uint effectiveOwnerNetId,
            bool onOwnField,
            bool hasPlayerPos,
            Vector3 playerPos,
            float radius,
            bool logScanSummary = true,
            bool includePlantData = false)
        {
            List<uint> result = new List<uint>();
            if (netIds == null || netIds.Count == 0 || cropPredicate == null)
            {
                if (logScanSummary)
                {
                    this.HomelandFarmLog(label + " (radius " + radius.ToString("F0") + (requireOwn ? ", own" : string.Empty) + "): 0");
                }

                return result;
            }

            float radiusSq = radius * radius;
            HashSet<uint> seenCrops = new HashSet<uint>();
            foreach (uint netId in netIds)
            {
                if (netId == 0U)
                {
                    continue;
                }

                // Fast path: most aura candidates for weed/harvest are already CropItemData entities.
                if (this.TryHomelandFarmGetComponentData(this.homelandFarmCropItemDataType, netId, out object directCropData, out _, "CropItemData")
                    && directCropData != null)
                {
                    if (!seenCrops.Add(netId))
                    {
                        continue;
                    }

                    if (!cropPredicate(directCropData))
                    {
                        continue;
                    }

                    if (requireOwn)
                    {
                        if (!this.TryHomelandFarmTryResolveOwnCropOwnerNetId(
                                netId,
                                netIds,
                                playerNetId,
                                effectiveOwnerNetId,
                                onOwnField,
                                out _))
                        {
                            continue;
                        }
                    }

                    if (hasPlayerPos
                        && this.TryHomelandFarmResolveFarmEntityPosition(netId, out Vector3 cropPos)
                        && cropPos != Vector3.zero
                        && (cropPos - playerPos).sqrMagnitude > radiusSq)
                    {
                        continue;
                    }

                    result.Add(netId);
                    continue;
                }

                // On builds where the growing crop resolves as PlantItemData (CropItemData absent),
                // treat the plant entity as the crop. Scoped via includePlantData so weed/harvest
                // (which read CropItemData-specific fields) are unaffected.
                if (includePlantData
                    && this.TryHomelandFarmGetComponentData(this.homelandFarmPlantItemDataType, netId, out object directPlantData, out _, "PlantItemData")
                    && directPlantData != null)
                {
                    if (!seenCrops.Add(netId))
                    {
                        continue;
                    }

                    if (!cropPredicate(directPlantData))
                    {
                        continue;
                    }

                    if (requireOwn
                        && !this.TryHomelandFarmTryResolveOwnCropOwnerNetId(netId, netIds, playerNetId, effectiveOwnerNetId, onOwnField, out _))
                    {
                        continue;
                    }

                    if (hasPlayerPos
                        && this.TryHomelandFarmResolveFarmEntityPosition(netId, out Vector3 plantPos)
                        && plantPos != Vector3.zero
                        && (plantPos - playerPos).sqrMagnitude > radiusSq)
                    {
                        continue;
                    }

                    result.Add(netId);
                    continue;
                }

                // Fallback path: resolve linked crop entity from crop-box entities.
                if (!this.TryHomelandFarmGetComponentData(this.homelandFarmCropBoxItemDataType, netId, out object cropBoxData, out _, "CropBoxItemData")
                    || cropBoxData == null)
                {
                    continue;
                }

                string[] cropLinkMembers = { "cropNetId", "CropNetId", "childCropNetId", "linkedCropNetId", "LinkedCropNetId" };
                for (int j = 0; j < cropLinkMembers.Length; j++)
                {
                    if (!this.TryHomelandFarmReadComponentUInt(cropBoxData, out uint cropNetId, cropLinkMembers[j]) || cropNetId == 0U)
                    {
                        continue;
                    }

                    if (!seenCrops.Add(cropNetId))
                    {
                        continue;
                    }

                    if (!this.TryHomelandFarmGetComponentData(this.homelandFarmCropItemDataType, cropNetId, out object cropData, out _, "CropItemData")
                        || cropData == null
                        || !cropPredicate(cropData))
                    {
                        continue;
                    }

                    if (requireOwn)
                    {
                        uint ownerId = 0U;
                        if ((!this.TryHomelandFarmTryReadOwnerId(cropNetId, out ownerId) || ownerId == 0U))
                        {
                            this.TryHomelandFarmTryReadOwnerId(netId, out ownerId);
                        }

                        if (ownerId == 0U && onOwnField)
                        {
                            ownerId = playerNetId;
                        }

                        if (ownerId != effectiveOwnerNetId)
                        {
                            continue;
                        }
                    }

                    if (hasPlayerPos
                        && this.TryHomelandFarmResolveFarmEntityPosition(cropNetId, out Vector3 cropPos)
                        && cropPos != Vector3.zero
                        && (cropPos - playerPos).sqrMagnitude > radiusSq)
                    {
                        continue;
                    }

                    result.Add(cropNetId);
                }
            }

            if (logScanSummary)
            {
                this.HomelandFarmLog(label + " (radius " + radius.ToString("F0") + (requireOwn ? ", own" : string.Empty) + "): " + result.Count);
            }

            return result;
        }

        // CropItemData often reports ownerId=0; fall back to the linked crop-box owner (same as water scan).
        private bool TryHomelandFarmTryResolveOwnCropOwnerNetId(
            uint cropNetId,
            HashSet<uint> scanNetIds,
            uint playerNetId,
            uint effectiveOwnerNetId,
            bool onOwnField,
            out uint resolvedOwnerId)
        {
            resolvedOwnerId = 0U;
            if (cropNetId == 0U)
            {
                return false;
            }

            if (!this.TryHomelandFarmTryReadOwnerId(cropNetId, out resolvedOwnerId) || resolvedOwnerId == 0U)
            {
                this.TryHomelandFarmTryReadOwnerId(cropNetId, out resolvedOwnerId);
            }

            if (resolvedOwnerId == 0U
                && scanNetIds != null
                && this.TryHomelandFarmTryNormalizeWaterNetId(cropNetId, scanNetIds, out uint linkedNetId)
                && linkedNetId != 0U
                && linkedNetId != cropNetId)
            {
                this.TryHomelandFarmTryReadOwnerId(linkedNetId, out resolvedOwnerId);
            }

            if (resolvedOwnerId == 0U && onOwnField)
            {
                resolvedOwnerId = playerNetId;
            }

            return resolvedOwnerId == effectiveOwnerNetId;
        }

        private List<uint> ScanHomelandFarmHarvestableCropsByRadius()
        {
            return this.ScanHomelandFarmCropsByRadius(
                cropData => this.TryHomelandFarmReadComponentInt(cropData, out int stage, "stage", "Stage") && stage == 4,
                "Harvestable crops",
                requireOwn: true);
        }

        private List<uint> ScanHomelandFarmCollectablePlantSeedsByRadius()
        {
            List<uint> result = new List<uint>();
            if (!this.EnsureHomelandFarmReflectionReady())
            {
                return result;
            }

            HashSet<uint> netIds = new HashSet<uint>();
            bool hasPlayerPos = this.TryGetHomelandFarmPlayerPosition(out Vector3 playerPos);
            float radius = this.homelandFarmWaterRadius;
            if (hasPlayerPos)
            {
                this.TryHomelandFarmCollectFarmEntityNetIds(
                    netIds,
                    out _,
                    playerPos,
                    radius + 2f,
                    useAutoFarmCollectShortcuts: false);
            }
            else
            {
                this.TryHomelandFarmCollectFarmEntityNetIds(netIds, out _);
            }

            float radiusSq = radius * radius;
            foreach (uint netId in netIds)
            {
                if (!this.TryHomelandFarmGetComponentData(this.homelandFarmPlantItemDataType, netId, out object plantData, out _, "PlantItemData")
                    || plantData == null)
                {
                    continue;
                }

                if (!this.TryHomelandFarmReadComponentBool(plantData, out bool hasCrossedSeed, "hasCrossedSeed", "_hasCrossedSeed", "HasCrossedSeed") || !hasCrossedSeed)
                {
                    continue;
                }

                if (hasPlayerPos
                    && this.TryHomelandFarmResolveFarmEntityPosition(netId, out Vector3 plantPos)
                    && plantPos != Vector3.zero
                    && (plantPos - playerPos).sqrMagnitude > radiusSq)
                {
                    continue;
                }

                result.Add(netId);
            }

            this.HomelandFarmLog("Collectable plant seeds (radius " + radius.ToString("F0") + "): " + result.Count);
            return result;
        }

        private List<uint> ScanHomelandFarmWeedableCropsByRadius()
        {
            return this.ScanHomelandFarmCropsByRadius(
                cropData => this.TryHomelandFarmReadComponentBool(cropData, out bool hasWeed, "hasWeed", "_hasWeed", "HasWeed") && hasWeed,
                "Weedable crops",
                requireOwn: false);
        }

        // Center used by the radius scans. During auto farming this returns the captured
        // planter-zone center so the working set stays fixed even if the player nudges around.
        private bool TryGetHomelandFarmScanCenter(out Vector3 pos)
        {
            if (this.homelandFarmScanCenterOverride.HasValue)
            {
                pos = this.homelandFarmScanCenterOverride.Value;
                return true;
            }

            return this.TryGetHomelandFarmPlayerPosition(out pos);
        }

        private bool TryGetHomelandFarmPlayerPosition(out Vector3 pos)
        {
            pos = Vector3.zero;
            if (this.TryGetLocalPlayerPosition(out pos) && pos != Vector3.zero)
            {
                return true;
            }

            object positionObj;
            if (this.TryGetManagedSelfPlayerEntityObject(out object entityObj, out _) && entityObj != null)
            {
                if (this.TryGetObjectMember(entityObj, "position", out positionObj) && positionObj is Vector3 position && position != Vector3.zero)
                {
                    pos = position;
                    return true;
                }
            }

            this.EnsureHomelandFarmScannerTypes();
            if (this.homelandFarmEntityUtilGetSelfPlayerEntityMethod != null)
            {
                try
                {
                    object selfEntity = this.homelandFarmEntityUtilGetSelfPlayerEntityMethod.Invoke(null, null);
                    if (selfEntity != null
                        && this.TryGetObjectMember(selfEntity, "position", out positionObj)
                        && positionObj is Vector3 entityPos
                        && entityPos != Vector3.zero)
                    {
                        pos = entityPos;
                        return true;
                    }
                }
                catch
                {
                }
            }

            if (this.TryHomelandFarmTryReadPlayerPositionAura(out pos) && pos != Vector3.zero)
            {
                return true;
            }

            return false;
        }

        private unsafe bool TryHomelandFarmTryReadPlayerPositionAura(out Vector3 pos)
        {
            pos = Vector3.zero;
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr entityUtilClass = this.FindHomelandFarmAuraClass(
                "XDTLevelAndEntity.BaseSystem.EntitiesManager.EntityUtil",
                "XDTLevelAndEntity.BaseSystem.EntitiesManager",
                "EntityUtil");
            if (entityUtilClass == IntPtr.Zero)
            {
                return false;
            }

            IntPtr getSelfPlayerEntityMethod = this.FindAuraMonoMethodOnHierarchy(entityUtilClass, "GetSelfPlayerEntity", 0);
            if (getSelfPlayerEntityMethod == IntPtr.Zero)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr entityObj = auraMonoRuntimeInvoke(getSelfPlayerEntityMethod, IntPtr.Zero, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || entityObj == IntPtr.Zero)
            {
                return false;
            }

            return this.TryGetAuraMonoEntityPosition(entityObj, out pos) && pos != Vector3.zero;
        }

        private bool TryHomelandFarmResolveFarmEntityPosition(uint netId, out Vector3 position)
        {
            position = Vector3.zero;
            if (netId == 0U)
            {
                return false;
            }

            if (this.TryHomelandFarmTryGetCachedLevelObjectPosition(netId, out position))
            {
                return true;
            }

            this.TryHomelandFarmCacheAuraLevelObjectPositions(false, allowDictionaryScan: false);
            if (this.TryHomelandFarmTryGetCachedLevelObjectPosition(netId, out position))
            {
                return true;
            }

            if (this.TryHomelandFarmTryGetEntityPositionAura(netId, out position) && position != Vector3.zero)
            {
                return true;
            }

            if (this.TryGetEntityPositionByNetId(netId, out position) && position != Vector3.zero)
            {
                return true;
            }

            if (this.TryGetEntityPositionByNetIdMono(netId, out position) && position != Vector3.zero)
            {
                return true;
            }

            return false;
        }

        private bool TryHomelandFarmTryGetCachedLevelObjectPosition(uint netId, out Vector3 position)
        {
            position = Vector3.zero;
            return this.homelandFarmAuraLevelObjectPositionCache.TryGetValue(netId, out position) && position != Vector3.zero;
        }

        private void TryHomelandFarmRememberLevelObjectPosition(uint netId, object levelObject)
        {
            if (netId == 0U || levelObject == null)
            {
                return;
            }

            if (this.TryResolvePositionFromManagedObject(levelObject, out Vector3 position) && position != Vector3.zero)
            {
                this.homelandFarmAuraLevelObjectPositionCache[netId] = position;
                this.homelandFarmAuraLevelObjectPositionCacheAt = Time.realtimeSinceStartup;
            }
        }

        private void TryHomelandFarmRememberLevelObjectPosition(uint netId, IntPtr levelObjectObj)
        {
            if (netId == 0U || levelObjectObj == IntPtr.Zero)
            {
                return;
            }

            if (this.TryExtractHomePositionMonoObject(levelObjectObj, out Vector3 position) && position != Vector3.zero)
            {
                this.homelandFarmAuraLevelObjectPositionCache[netId] = position;
                this.homelandFarmAuraLevelObjectPositionCacheAt = Time.realtimeSinceStartup;
            }

            string[] ownerMembers = { "ownerNetId", "OwnerNetId", "fieldOwnerNetId", "FieldOwnerNetId", "ownerId", "OwnerId" };
            for (int i = 0; i < ownerMembers.Length; i++)
            {
                if (this.TryGetMonoUInt32Member(levelObjectObj, ownerMembers[i], out uint ownerNetId)
                    && ownerNetId != 0U
                    && ownerNetId != netId)
                {
                    this.homelandFarmAuraLevelObjectOwnerByNetId[netId] = ownerNetId;
                    return;
                }
            }
        }

        private unsafe bool TryHomelandFarmCacheAuraLevelObjectPositions(bool forceRefresh, bool allowDictionaryScan = true)
        {
            float now = Time.realtimeSinceStartup;
            if (!forceRefresh
                && now - this.homelandFarmAuraLevelObjectPositionCacheAt < HomelandFarmLevelObjectPositionCacheTtl
                && this.homelandFarmAuraLevelObjectPositionCache.Count > 0)
            {
                return true;
            }

            // Reuse whatever we already have instead of touching LevelObjectManager._dictionary
            // during water/harvest/sow. Full dictionary enumeration crashes on the crop field in
            // this IL2CPP build; warmup is the only safe time to populate the cache.
            if (!allowDictionaryScan)
            {
                return this.homelandFarmAuraLevelObjectPositionCache.Count > 0;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            if (!this.TryResolveAuraMonoLevelObjectManager(out IntPtr managerObj, out _, out _))
            {
                return false;
            }

            IntPtr dictionaryObj = IntPtr.Zero;
            if ((!this.TryGetMonoObjectMember(managerObj, "_dictionary", out dictionaryObj) || dictionaryObj == IntPtr.Zero)
                && (!this.TryGetMonoObjectMember(managerObj, "dictionary", out dictionaryObj) || dictionaryObj == IntPtr.Zero))
            {
                return false;
            }

            List<IntPtr> entries = new List<IntPtr>();
            if (!this.TryEnumerateAuraMonoCollectionItems(dictionaryObj, entries) || entries.Count <= 0)
            {
                return false;
            }

            this.homelandFarmAuraLevelObjectPositionCache.Clear();
            this.homelandFarmAuraLevelObjectOwnerByNetId.Clear();
            for (int i = 0; i < entries.Count; i++)
            {
                try
                {
                    IntPtr entry = entries[i];
                    if (entry == IntPtr.Zero)
                    {
                        continue;
                    }

                    IntPtr levelObjectObj = IntPtr.Zero;
                    if ((!this.TryGetMonoObjectMember(entry, "Value", out levelObjectObj) || levelObjectObj == IntPtr.Zero)
                        && (!this.TryGetMonoObjectMember(entry, "value", out levelObjectObj) || levelObjectObj == IntPtr.Zero)
                        && (!this.TryGetMonoObjectMember(entry, "_value", out levelObjectObj) || levelObjectObj == IntPtr.Zero))
                    {
                        levelObjectObj = entry;
                    }

                    uint entityNetId = 0U;
                    if (!this.TryHomelandFarmTryGetLevelObjectScanNetId(levelObjectObj, entry, out entityNetId) || entityNetId == 0U)
                    {
                        continue;
                    }

                    this.TryHomelandFarmRememberLevelObjectPosition(entityNetId, levelObjectObj);
                    this.TryHomelandFarmRememberLevelObjectOwnerFromLevelObject(levelObjectObj, entityNetId);
                }
                catch (Exception ex)
                {
                    this.HomelandFarmLog("LevelObject position cache entry failed: " + ex.Message);
                }
            }

            this.homelandFarmAuraLevelObjectPositionCacheAt = now;
            return this.homelandFarmAuraLevelObjectPositionCache.Count > 0;
        }

        private unsafe bool TryHomelandFarmTryGetEntityPositionAura(uint netId, out Vector3 position)
        {
            position = Vector3.zero;
            if (netId == 0U || !this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            if (!this.TryGetAuraMonoEntityObjectByNetId(netId, out IntPtr entityObj) || entityObj == IntPtr.Zero)
            {
                return false;
            }

            return this.TryGetAuraMonoEntityPosition(entityObj, out position) && position != Vector3.zero;
        }

        private bool TryHomelandFarmCollectCropNetIdsForEntity(uint netId, HashSet<uint> output)
        {
            if (output == null || netId == 0U)
            {
                return false;
            }

            int before = output.Count;
            if (this.TryHomelandFarmGetComponentData(this.homelandFarmCropItemDataType, netId, out _, out _, "CropItemData"))
            {
                output.Add(netId);
            }

            if (this.TryHomelandFarmGetComponentData(this.homelandFarmCropBoxItemDataType, netId, out object cropBoxData, out _, "CropBoxItemData")
                && cropBoxData != null)
            {
                string[] cropLinkMembers = { "cropNetId", "CropNetId", "childCropNetId", "linkedCropNetId", "LinkedCropNetId" };
                for (int i = 0; i < cropLinkMembers.Length; i++)
                {
                    if (this.TryHomelandFarmReadComponentUInt(cropBoxData, out uint linkedCropNetId, cropLinkMembers[i]) && linkedCropNetId != 0U)
                    {
                        output.Add(linkedCropNetId);
                    }
                }
            }

            return output.Count > before;
        }

        private int TryHomelandFarmGetSprinklerCellCount()
        {
            // Reads TableData.TableModes[mode].num where mode comes from HandHoldSprinkler.mode
            // (driven by HobbyProtocolManager hobby skill "Water"). Skill levels map to 1/3/6/9 cells.
            // The server rejects the whole water command if it exceeds the player's skill capacity.
            try
            {
                if (this.TryGetManagedSelfPlayerObject(out object playerObj, out _) && playerObj != null)
                {
                    Type sprinklerType = this.FindLoadedType(
                        "HandHoldSprinkler",
                        "Il2CppXDTLevelAndEntity.Gameplay.Component.Equip.HandHoldSprinkler",
                        "XDTLevelAndEntity.Gameplay.Component.Equip.HandHoldSprinkler");

                    if (sprinklerType != null
                        && this.TryInvokeMethodByName(playerObj, "GetComponent", out object sprinkler, new object[] { sprinklerType })
                        && sprinkler != null
                        && this.TryGetObjectMember(sprinkler, "mode", out object modeObj) && modeObj != null)
                    {
                        int modeVal = Convert.ToInt32(modeObj);
                        Type tableDataType = this.FindLoadedType("TableData", "EcsClient.TableData");
                        FieldInfo modesField = tableDataType?.GetField("TableModes",
                            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                        object modesDict = modesField?.GetValue(null);
                        if (modesDict != null)
                        {
                            MethodInfo tryGetValue = modesDict.GetType().GetMethod("TryGetValue");
                            if (tryGetValue != null)
                            {
                                object[] args = new object[] { modeVal, null };
                                bool found = (bool)(tryGetValue.Invoke(modesDict, args) ?? false);
                                if (found && args[1] != null
                                    && this.TryGetObjectMember(args[1], "num", out object numObj) && numObj != null)
                                {
                                    int cellNum = Convert.ToInt32(numObj);
                                    if (cellNum > 0)
                                    {
                                        this.HomelandFarmLog("Sprinkler cell count from TableMode[" + modeVal + "].num=" + cellNum);
                                        return cellNum;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                this.HomelandFarmLog("Sprinkler cell count read failed: " + ex.Message);
            }

            return HomelandFarmCastBatchDefault;
        }

        private bool TryHomelandFarmTryIsHandHoldSprinklerEquipped()
        {
            try
            {
                if (this.TryGetManagedSelfPlayerObject(out object playerObj, out _) && playerObj != null)
                {
                    Type sprinklerType = this.FindLoadedType(
                        "HandHoldSprinkler",
                        "Il2CppXDTLevelAndEntity.Gameplay.Component.Equip.HandHoldSprinkler",
                        "XDTLevelAndEntity.Gameplay.Component.Equip.HandHoldSprinkler");
                    if (sprinklerType != null
                        && this.TryInvokeMethodByName(playerObj, "GetComponent", out object sprinkler, new object[] { sprinklerType })
                        && sprinkler != null)
                    {
                        return true;
                    }
                }
            }
            catch
            {
            }

            if (this.TryHomelandFarmTryGetEquippedHandholdBagNetId(out uint equippedNetId, out int equippedStaticId)
                && equippedNetId != 0U
                && equippedStaticId > 0
                && this.TryHomelandFarmItemMatchesSprinkler(equippedStaticId, 0))
            {
                return true;
            }

            if (this.EnsureAuraMonoApiReady()
                && this.AttachAuraMonoThread()
                && this.TryHomelandFarmTryGetAuraLocalPlayerObject(out IntPtr auraPlayerObj, out _)
                && auraPlayerObj != IntPtr.Zero
                && this.TryGetMonoObjectMember(auraPlayerObj, "equipComponent", out IntPtr equipObj)
                && equipObj != IntPtr.Zero
                && this.TryGetMonoObjectMember(equipObj, "handhold", out IntPtr handholdObj)
                && handholdObj != IntPtr.Zero
                && (this.TryGetMonoInt32Member(handholdObj, "staticId", out int auraStaticId)
                    || this.TryGetMonoInt32Member(handholdObj, "StaticId", out auraStaticId))
                && auraStaticId > 0
                && this.TryHomelandFarmItemMatchesSprinkler(auraStaticId, 0))
            {
                return true;
            }

            // Tools equipped via ToolSystem.SetHandhold (toolId, not a backpack staticId) are not
            // visible through equipComponent.handhold.staticId above, and the managed GetComponent
            // path fails on builds where HandHoldSprinkler's managed type is absent. Detect the
            // HandHoldSprinkler ECS component directly on the player entity via AuraMono — same
            // GetAllComponents path used to classify crop boxes.
            if (this.TryHomelandFarmAuraPlayerHasSprinklerComponent())
            {
                return true;
            }

            return false;
        }

        // Mirrors TryGetFishingRodToolStatus: the equipped handhold tool (sprinkler, rod, net, ...)
        // is reachable via InteractSystem.player.equipComponent.handhold, and the held tool's class
        // name identifies it. This is the reliable cross-build path (HandHoldSprinkler has no managed
        // type and is not an ECS component on the player entity on some builds).
        private bool TryHomelandFarmAuraPlayerHasSprinklerComponent()
        {
            if (!this.EnsureAuraMonoApiReady()
                || !this.AttachAuraMonoThread()
                || auraMonoObjectGetClass == null
                || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr interactObj = this.GetAuraMonoInteractSystemInstance();
            if (interactObj == IntPtr.Zero || this.auraMonoInteractGetPlayerMethodPtr == IntPtr.Zero)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr playerObj = auraMonoRuntimeInvoke(this.auraMonoInteractGetPlayerMethodPtr, interactObj, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || playerObj == IntPtr.Zero)
            {
                return false;
            }

            if (!this.TryInvokeAuraMonoZeroArg(playerObj, out IntPtr equipObj, "get_equipComponent", "GetEquipComponent")
                || equipObj == IntPtr.Zero)
            {
                return false;
            }

            if (!this.TryInvokeAuraMonoZeroArg(equipObj, out IntPtr handholdObj, "get_handhold", "GetHandhold")
                || handholdObj == IntPtr.Zero)
            {
                return false;
            }

            string handholdClassName = this.GetAuraMonoClassDisplayName(auraMonoObjectGetClass(handholdObj));
            return !string.IsNullOrEmpty(handholdClassName)
                && handholdClassName.IndexOf("Sprinkler", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool TryHomelandFarmWaterBatch(
            uint playerNetId,
            List<uint> cropBoxNetIds,
            Dictionary<uint, List<uint>> plantsByOwner,
            out string status)
        {
            status = "Homeland farm water batch unavailable.";
            cropBoxNetIds = cropBoxNetIds ?? new List<uint>(0);
            plantsByOwner = plantsByOwner ?? new Dictionary<uint, List<uint>>();
            if (!this.EnsureHomelandFarmReflectionReady())
            {
                status = this.homelandFarmReflectionUnavailableStatus;
                return false;
            }

            bool anySent = false;
            List<string> errors = new List<string>();

            if (cropBoxNetIds.Count > 0)
            {
                if (!this.TryHomelandFarmInvokeCropWater(playerNetId, cropBoxNetIds, out string cropStatus))
                {
                    errors.Add(cropStatus);
                }
                else
                {
                    anySent = true;
                }
            }

            foreach (KeyValuePair<uint, List<uint>> pair in plantsByOwner)
            {
                if (pair.Key == 0U || pair.Value == null || pair.Value.Count == 0)
                {
                    continue;
                }

                if (!this.TryHomelandFarmInvokePlantWater(pair.Key, pair.Value, HomelandFarmDefaultPlantWaterMode, out string plantStatus))
                {
                    errors.Add(plantStatus);
                }
                else
                {
                    anySent = true;
                }
            }

            if (anySent)
            {
                status = "Water batch sent.";
                return true;
            }

            status = errors.Count > 0 ? string.Join("; ", errors.ToArray()) : "Water batch had no targets.";
            return false;
        }

        private bool TryHomelandFarmHarvestCrop(uint cropNetId, out string status)
        {
            status = "Harvest unavailable.";
            if (cropNetId == 0U || !this.EnsureHomelandFarmReflectionReady())
            {
                status = cropNetId == 0U ? "Crop netId missing." : this.homelandFarmReflectionUnavailableStatus;
                return false;
            }

            if (this.homelandFarmManagedReflectionReady && this.homelandFarmCropPickPlantMethod != null
                && this.TryHomelandFarmInvokeStaticUintProtocol(
                    this.homelandFarmCropPickPlantMethod,
                    cropNetId,
                    this.homelandFarmHarvestNetworkCommandType,
                    command =>
                    {
                        object cmd = command;
                        return this.TrySetFieldValue(this.homelandFarmHarvestNetworkCommandType, ref cmd, "netId", cropNetId);
                    },
                    "Harvest",
                    out status))
            {
                return true;
            }

            if (this.TryResolveHomelandFarmAuraProtocol(out _)
                && this.TryHomelandFarmInvokeAuraUintProtocol(this.homelandFarmAuraCropPickPlantMethod, cropNetId, "Harvest", out status))
            {
                return true;
            }

            return false;
        }

        private bool TryHomelandFarmCollectPlantSeed(uint plantNetId, out string status)
        {
            status = "Collect plant seed unavailable.";
            if (plantNetId == 0U || !this.EnsureHomelandFarmReflectionReady())
            {
                status = plantNetId == 0U ? "Plant netId missing." : this.homelandFarmReflectionUnavailableStatus;
                return false;
            }

            if (this.homelandFarmManagedReflectionReady && this.homelandFarmPlantCollectSeedMethod != null
                && this.TryHomelandFarmInvokeStaticUintProtocol(
                    this.homelandFarmPlantCollectSeedMethod,
                    plantNetId,
                    this.homelandFarmPickPlantCrossedSeedNetworkCommandType,
                    command =>
                    {
                        object cmd = command;
                        return this.TrySetFieldValue(this.homelandFarmPickPlantCrossedSeedNetworkCommandType, ref cmd, "netId", plantNetId)
                            || this.TrySetFieldValue(this.homelandFarmPickPlantCrossedSeedNetworkCommandType, ref cmd, "plantNetId", plantNetId);
                    },
                    "CollectPlantSeed",
                    out status))
            {
                return true;
            }

            if (this.TryResolveHomelandFarmAuraProtocol(out _)
                && this.TryHomelandFarmInvokeAuraUintProtocol(this.homelandFarmAuraPlantCollectSeedMethod, plantNetId, "CollectPlantSeed", out status))
            {
                return true;
            }

            return false;
        }

        private bool TryHomelandFarmWeed(uint cropNetId, out string status)
        {
            status = "Weed unavailable.";
            if (cropNetId == 0U || !this.EnsureHomelandFarmReflectionReady())
            {
                status = cropNetId == 0U ? "Crop netId missing." : this.homelandFarmReflectionUnavailableStatus;
                return false;
            }

            if (this.homelandFarmManagedReflectionReady && this.homelandFarmCropWeedMethod != null
                && this.TryHomelandFarmInvokeStaticUintProtocol(
                    this.homelandFarmCropWeedMethod,
                    cropNetId,
                    this.homelandFarmWeedingNetworkCommandType,
                    command =>
                    {
                        object cmd = command;
                        return this.TrySetFieldValue(this.homelandFarmWeedingNetworkCommandType, ref cmd, "netId", cropNetId);
                    },
                    "Weed",
                    out status))
            {
                return true;
            }

            if (this.TryResolveHomelandFarmAuraProtocol(out _)
                && this.TryHomelandFarmInvokeAuraUintProtocol(this.homelandFarmAuraCropWeedMethod, cropNetId, "Weed", out status))
            {
                return true;
            }

            return false;
        }

        private bool TryHomelandFarmSendFertilizeAddManure(List<uint> cropNetIds, out string status)
        {
            status = "AddManure unavailable.";
            cropNetIds = cropNetIds ?? new List<uint>(0);
            if (cropNetIds.Count == 0U)
            {
                status = "Crop list empty.";
                return false;
            }

            List<string> attemptLog = new List<string>();
            if (this.TryHomelandFarmInvokeAddManureAura(cropNetIds, out string auraStatus))
            {
                status = auraStatus;
                return true;
            }

            attemptLog.Add("aura=" + auraStatus);

            if (this.TryHomelandFarmInvokeCropAddManureInterop(cropNetIds, out string interopStatus))
            {
                status = interopStatus;
                return true;
            }

            attemptLog.Add("interop=" + interopStatus);

            if (this.TryHomelandFarmSendManureCommand(cropNetIds, out string sendStatus))
            {
                status = sendStatus;
                return true;
            }

            attemptLog.Add("send=" + sendStatus);

            if (this.EnsureHomelandFarmReflectionReady() && this.homelandFarmCropAddManureMethod != null)
            {
                try
                {
                    object listArg = this.CreateHomelandFarmUintList(
                        cropNetIds,
                        this.homelandFarmCropAddManureMethod.GetParameters()[0].ParameterType);
                    this.homelandFarmCropAddManureMethod.Invoke(null, new object[] { listArg });
                    status = "AddManure managed ok count=" + cropNetIds.Count + ".";
                    return true;
                }
                catch (Exception ex)
                {
                    attemptLog.Add("managed=" + (ex.InnerException ?? ex).Message);
                }
            }
            else
            {
                attemptLog.Add("managed=CropProtocolManager.AddManure unavailable");
            }

            status = string.Join("; ", attemptLog.ToArray());
            return false;
        }

        private bool TryHomelandFarmReadCropFertilizeSnapshot(uint cropNetId, out HomelandFarmCropFertilizeSnapshot snapshot)
        {
            snapshot = default(HomelandFarmCropFertilizeSnapshot);
            if (cropNetId == 0U
                || !this.EnsureHomelandFarmReflectionReady()
                || !this.TryHomelandFarmGetComponentData(this.homelandFarmCropItemDataType, cropNetId, out object cropData, out _, "CropItemData")
                || cropData == null)
            {
                return false;
            }

            if (!this.TryHomelandFarmReadComponentInt(cropData, out snapshot.ManureId, "manureId", "ManureId"))
            {
                snapshot.ManureId = 0;
            }

            if (!this.TryHomelandFarmReadComponentInt(cropData, out snapshot.BreedingPowderId, "breedingPowderId", "BreedingPowderId"))
            {
                snapshot.BreedingPowderId = 0;
            }

            if (!this.TryHomelandFarmReadComponentInt(cropData, out snapshot.GrowthValue, "growthValue", "GrowthValue"))
            {
                snapshot.GrowthValue = 0;
            }

            return true;
        }

        private Dictionary<uint, HomelandFarmCropFertilizeSnapshot> TryHomelandFarmSnapshotCropFertilizeStates(List<uint> cropNetIds)
        {
            Dictionary<uint, HomelandFarmCropFertilizeSnapshot> snapshots = new Dictionary<uint, HomelandFarmCropFertilizeSnapshot>();
            if (cropNetIds == null)
            {
                return snapshots;
            }

            for (int i = 0; i < cropNetIds.Count; i++)
            {
                uint cropNetId = cropNetIds[i];
                if (cropNetId == 0U || snapshots.ContainsKey(cropNetId))
                {
                    continue;
                }

                if (this.TryHomelandFarmReadCropFertilizeSnapshot(cropNetId, out HomelandFarmCropFertilizeSnapshot snapshot))
                {
                    snapshots[cropNetId] = snapshot;
                }
            }

            return snapshots;
        }

        private int CountHomelandFarmFertilizeApplied(
            List<uint> cropNetIds,
            Dictionary<uint, HomelandFarmCropFertilizeSnapshot> before,
            int fertilizerStaticId,
            HashSet<uint> scanNetIds,
            out string detail)
        {
            detail = string.Empty;
            if (cropNetIds == null || cropNetIds.Count == 0)
            {
                detail = "empty batch";
                return 0;
            }

            before = before ?? new Dictionary<uint, HomelandFarmCropFertilizeSnapshot>();
            int applied = 0;
            System.Text.StringBuilder summary = new System.Text.StringBuilder();
            for (int i = 0; i < cropNetIds.Count; i++)
            {
                uint cropNetId = cropNetIds[i];
                bool changed = false;
                if (before.TryGetValue(cropNetId, out HomelandFarmCropFertilizeSnapshot oldSnapshot)
                    && this.TryHomelandFarmReadCropFertilizeSnapshot(cropNetId, out HomelandFarmCropFertilizeSnapshot newSnapshot))
                {
                    changed = newSnapshot.ManureId != oldSnapshot.ManureId
                        || newSnapshot.BreedingPowderId != oldSnapshot.BreedingPowderId
                        || newSnapshot.GrowthValue != oldSnapshot.GrowthValue;
                }

                if (!changed
                    && !this.IsHomelandFarmCropFertilizable(cropNetId, fertilizerStaticId, scanNetIds, out string rejectReason)
                    && rejectReason.IndexOf("Already fertilized", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    changed = true;
                }

                if (changed)
                {
                    applied++;
                    if (summary.Length > 0)
                    {
                        summary.Append(';');
                    }

                    summary.Append(cropNetId);
                }
            }

            detail = applied > 0 ? "netIds=" + summary : "no crop state change";
            return applied;
        }

        private unsafe bool TryHomelandFarmReadAuraCropFertilizerRowFields(
            IntPtr rowObj,
            out int rowId,
            out int effectType,
            out int decorationId,
            out int feedbackEffect,
            out int actionEffect)
        {
            rowId = 0;
            effectType = 0;
            decorationId = 0;
            feedbackEffect = 0;
            actionEffect = 0;
            if (rowObj == IntPtr.Zero)
            {
                return false;
            }

            if (!this.TryGetMonoInt32Member(rowObj, "id", out rowId))
            {
                this.TryGetMonoInt32Member(rowObj, "Id", out rowId);
            }

            if (!this.TryInvokeAuraMonoZeroArgInt(rowObj, out effectType, "get_effectType", "get_EffectType"))
            {
                this.TryGetMonoInt32Member(rowObj, "_effectType", out effectType);
                this.TryGetMonoInt32Member(rowObj, "effectType", out effectType);
            }

            if (!this.TryGetMonoInt32Member(rowObj, "decorationId", out decorationId))
            {
                this.TryGetMonoInt32Member(rowObj, "DecorationId", out decorationId);
            }

            if (!this.TryInvokeAuraMonoZeroArgInt(rowObj, out feedbackEffect, "get_feedbackEffect", "get_FeedbackEffect"))
            {
                this.TryGetMonoInt32Member(rowObj, "_feedbackEffect", out feedbackEffect);
                this.TryGetMonoInt32Member(rowObj, "feedbackEffect", out feedbackEffect);
            }

            if (!this.TryInvokeAuraMonoZeroArgInt(rowObj, out actionEffect, "get_actionEffect", "get_ActionEffect"))
            {
                this.TryGetMonoInt32Member(rowObj, "_actionEffect", out actionEffect);
                this.TryGetMonoInt32Member(rowObj, "actionEffect", out actionEffect);
            }

            return rowId > 0 || decorationId > 0 || feedbackEffect > 0 || actionEffect > 0 || effectType > 0;
        }

        private bool TryHomelandFarmTryGetEquippedHandholdCropFertilizerRowAura(out IntPtr rowObj, out string source)
        {
            rowObj = IntPtr.Zero;
            source = string.Empty;
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return false;
            }

            if (!this.TryHomelandFarmTryGetAuraLocalPlayerObject(out IntPtr playerObj, out _)
                || playerObj == IntPtr.Zero)
            {
                return false;
            }

            if (!this.TryGetMonoObjectMember(playerObj, "equipComponent", out IntPtr equipObj) || equipObj == IntPtr.Zero)
            {
                return false;
            }

            if (!this.TryGetMonoObjectMember(equipObj, "handhold", out IntPtr handholdObj) || handholdObj == IntPtr.Zero)
            {
                return false;
            }

            if (this.TryGetMonoObjectMember(handholdObj, "cropfertilizer", out rowObj) && rowObj != IntPtr.Zero)
            {
                source = "handhold.cropfertilizer";
                return true;
            }

            return false;
        }

        private unsafe bool TryHomelandFarmTryGetCropFertilizerTableRowAuraMonoObject(int fertilizerStaticId, out IntPtr rowObj)
        {
            rowObj = IntPtr.Zero;
            if (fertilizerStaticId <= 0 || !this.EnsureAuraMonoApiReady() || auraMonoClassFromName == null || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr ecsImage = this.FindAuraMonoImage(new[] { "EcsClient", "EcsClient.dll" });
            if (ecsImage == IntPtr.Zero)
            {
                return false;
            }

            IntPtr tableDataClass = auraMonoClassFromName(ecsImage, string.Empty, "TableData");
            if (tableDataClass == IntPtr.Zero)
            {
                tableDataClass = auraMonoClassFromName(ecsImage, "EcsClient", "TableData");
            }

            if (tableDataClass == IntPtr.Zero)
            {
                return false;
            }

            IntPtr getCropFertilizerMethod = this.FindAuraMonoMethodOnHierarchy(tableDataClass, "GetCropfertilizer", 1);
            if (getCropFertilizerMethod == IntPtr.Zero)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&fertilizerStaticId);
            rowObj = auraMonoRuntimeInvoke(getCropFertilizerMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            return exc == IntPtr.Zero && rowObj != IntPtr.Zero;
        }

        private bool TryHomelandFarmTryGetCropFertilizerVisualInfo(
            int fertilizerStaticId,
            out int effectType,
            out int decorationId,
            out int feedbackEffect,
            out int actionEffect,
            out string source)
        {
            effectType = 0;
            decorationId = 0;
            feedbackEffect = 0;
            actionEffect = 0;
            source = string.Empty;
            if (fertilizerStaticId <= 0)
            {
                return false;
            }

            if (this.TryHomelandFarmTryGetEquippedHandholdCropFertilizerRowAura(out IntPtr handholdRowObj, out string handholdSource)
                && this.TryHomelandFarmReadAuraCropFertilizerRowFields(
                    handholdRowObj,
                    out _,
                    out effectType,
                    out decorationId,
                    out feedbackEffect,
                    out actionEffect))
            {
                source = handholdSource;
                return true;
            }

            if (this.TryHomelandFarmTryGetCropFertilizerTableRowAuraMonoObject(fertilizerStaticId, out IntPtr tableRowObj)
                && this.TryHomelandFarmReadAuraCropFertilizerRowFields(
                    tableRowObj,
                    out _,
                    out effectType,
                    out decorationId,
                    out feedbackEffect,
                    out actionEffect))
            {
                source = "TableData.GetCropfertilizer";
                return true;
            }

            if (this.TryHomelandFarmTryGetCropFertilizerTableRow(fertilizerStaticId, out object row, out effectType, out _)
                && row != null)
            {
                source = "managed TableData.GetCropfertilizer";
                if (!this.TryReadManagedInt32Member(row, "decorationId", out decorationId))
                {
                    this.TryReadManagedInt32Member(row, "DecorationId", out decorationId);
                }

                if (!this.TryReadManagedInt32Member(row, "feedbackEffect", out feedbackEffect))
                {
                    this.TryReadManagedInt32Member(row, "FeedbackEffect", out feedbackEffect);
                }

                if (!this.TryReadManagedInt32Member(row, "actionEffect", out actionEffect))
                {
                    this.TryReadManagedInt32Member(row, "ActionEffect", out actionEffect);
                }

                return true;
            }

            return this.TryHomelandFarmTryGetCropFertilizerTableRowAuraMono(
                fertilizerStaticId,
                out effectType,
                out _,
                out decorationId,
                out feedbackEffect);
        }

        private bool TryHomelandFarmEnsureAuraEntitiesVisualMethods(out string status)
        {
            status = "Entities visual methods unavailable.";
            if (!this.TryHomelandFarmEnsureAuraEntitiesPlayVfxAtMethod(out status))
            {
                return false;
            }

            if (this.homelandFarmAuraEntitiesPlayVfxOnMethod == IntPtr.Zero
                || this.homelandFarmAuraEntitiesCreateLevelEntityMethod == IntPtr.Zero)
            {
                if (!this.TryResolveHomelandFarmAuraScanClasses(out status) || this.homelandFarmAuraEntitiesClass == IntPtr.Zero)
                {
                    return false;
                }

                if (this.homelandFarmAuraEntitiesPlayVfxOnMethod == IntPtr.Zero)
                {
                    this.homelandFarmAuraEntitiesPlayVfxOnMethod = this.FindAuraMonoMethodOnHierarchy(this.homelandFarmAuraEntitiesClass, "PlayVfxOn", 3);
                }

                if (this.homelandFarmAuraEntitiesCreateLevelEntityMethod == IntPtr.Zero)
                {
                    this.homelandFarmAuraEntitiesCreateLevelEntityMethod = this.FindAuraMonoMethodOnHierarchy(this.homelandFarmAuraEntitiesClass, "CreateLevelEntity", 4);
                }
            }

            if (this.homelandFarmAuraEntitiesPlayVfxOnMethod == IntPtr.Zero
                && this.homelandFarmAuraEntitiesCreateLevelEntityMethod == IntPtr.Zero)
            {
                status = "Entities PlayVfxOn/CreateLevelEntity missing.";
                return this.homelandFarmAuraEntitiesPlayVfxAtMethod != IntPtr.Zero;
            }

            status = "Entities visual methods ready.";
            return true;
        }

        private bool TryHomelandFarmEnsureAuraEntitiesPlayVfxAtMethod(out string status)
        {
            status = "Entities.PlayVfxAt unavailable.";
            if (this.homelandFarmAuraEntitiesPlayVfxAtMethod != IntPtr.Zero)
            {
                status = "Entities.PlayVfxAt ready.";
                return true;
            }

            if (!this.TryResolveHomelandFarmAuraScanClasses(out status))
            {
                return false;
            }

            if (this.homelandFarmAuraEntitiesClass == IntPtr.Zero)
            {
                status = "Entities class unavailable.";
                return false;
            }

            this.homelandFarmAuraEntitiesPlayVfxAtMethod = this.FindAuraMonoMethodOnHierarchy(this.homelandFarmAuraEntitiesClass, "PlayVfxAt", 3);
            this.homelandFarmAuraEntitiesPlayVfxAtArgCount = 3;
            if (this.homelandFarmAuraEntitiesPlayVfxAtMethod == IntPtr.Zero)
            {
                this.homelandFarmAuraEntitiesPlayVfxAtMethod = this.FindAuraMonoMethodOnHierarchy(this.homelandFarmAuraEntitiesClass, "PlayVfxAt", 2);
                this.homelandFarmAuraEntitiesPlayVfxAtArgCount = 2;
            }

            if (this.homelandFarmAuraEntitiesPlayVfxAtMethod == IntPtr.Zero)
            {
                status = "Entities.PlayVfxAt method missing.";
                return false;
            }

            status = "Entities.PlayVfxAt ready.";
            return true;
        }

        private bool TryHomelandFarmTryGetCropEntityPositionRotation(uint cropNetId, out Vector3 position, out Quaternion rotation)
        {
            position = Vector3.zero;
            rotation = Quaternion.identity;
            if (cropNetId == 0U)
            {
                return false;
            }

            if (this.TryHomelandFarmTryResolveCropVisualWorldPose(cropNetId, out position, out rotation)
                && position != Vector3.zero)
            {
                return true;
            }

            return this.TryHomelandFarmResolveFarmEntityPosition(cropNetId, out position) && position != Vector3.zero;
        }

        private void TryHomelandFarmRememberPlanterSowAnchor(
            uint planterNetId,
            ulong putZoneId,
            Vector3 worldPosition,
            Quaternion worldRotation)
        {
            if (planterNetId == 0U || worldPosition == Vector3.zero)
            {
                return;
            }

            this.homelandFarmPlanterSowAnchorByNetId[planterNetId] = new HomelandFarmPlanterSowAnchor
            {
                WorldPosition = worldPosition,
                WorldRotation = worldRotation,
                PutZoneId = putZoneId
            };

            if (putZoneId != 0UL)
            {
                this.homelandFarmPutZoneWorldPositionById[putZoneId] = worldPosition;
                this.homelandFarmPutZoneWorldRotationById[putZoneId] = worldRotation;
            }
        }

        private bool TryHomelandFarmTryFindCropBoxNetIdForCrop(uint cropNetId, out uint cropBoxNetId)
        {
            cropBoxNetId = 0U;
            if (cropNetId == 0U)
            {
                return false;
            }

            foreach (KeyValuePair<uint, HomelandFarmPlanterSowAnchor> entry in this.homelandFarmPlanterSowAnchorByNetId)
            {
                uint planterNetId = entry.Key;
                if (planterNetId == 0U)
                {
                    continue;
                }

                if (this.TryHomelandFarmCropBoxHasCrop(planterNetId, out uint linkedCropNetId)
                    && linkedCropNetId == cropNetId)
                {
                    cropBoxNetId = planterNetId;
                    return true;
                }
            }

            if (this.TryHomelandFarmResolveEntityFieldLocalPosition(cropNetId, out Vector3 cropFieldLocal))
            {
                string cropCellKey = HomelandFarmFieldCellKey(cropFieldLocal);
                foreach (uint planterNetId in this.homelandFarmPlanterSowAnchorByNetId.Keys)
                {
                    if (planterNetId == 0U)
                    {
                        continue;
                    }

                    if (this.TryHomelandFarmResolveEntityFieldLocalPosition(planterNetId, out Vector3 boxFieldLocal)
                        && HomelandFarmFieldCellKey(boxFieldLocal) == cropCellKey)
                    {
                        cropBoxNetId = planterNetId;
                        return true;
                    }
                }

                foreach (uint candidateNetId in this.homelandFarmAuraLevelObjectPositionCache.Keys)
                {
                    if (candidateNetId == 0U || candidateNetId == cropNetId)
                    {
                        continue;
                    }

                    if (!this.TryHomelandFarmGetComponentData(
                            this.homelandFarmCropBoxItemDataType,
                            candidateNetId,
                            out _,
                            out _,
                            "CropBoxItemData"))
                    {
                        continue;
                    }

                    if (this.TryHomelandFarmResolveEntityFieldLocalPosition(candidateNetId, out Vector3 boxFieldLocal)
                        && HomelandFarmFieldCellKey(boxFieldLocal) == cropCellKey)
                    {
                        cropBoxNetId = candidateNetId;
                        return true;
                    }
                }
            }

            Vector3 cropPos = Vector3.zero;
            this.TryHomelandFarmResolveFarmEntityPosition(cropNetId, out cropPos);
            float bestDistanceSq = HomelandFarmCropBoxWorldMatchRadius * HomelandFarmCropBoxWorldMatchRadius;
            foreach (KeyValuePair<uint, HomelandFarmPlanterSowAnchor> entry in this.homelandFarmPlanterSowAnchorByNetId)
            {
                if (entry.Key == 0U || entry.Value == null || entry.Value.WorldPosition == Vector3.zero)
                {
                    continue;
                }

                Vector3 delta = entry.Value.WorldPosition - cropPos;
                delta.y = 0f;
                float distanceSq = delta.sqrMagnitude;
                if (cropPos != Vector3.zero && distanceSq <= bestDistanceSq)
                {
                    bestDistanceSq = distanceSq;
                    cropBoxNetId = entry.Key;
                }
            }

            if (cropBoxNetId != 0U)
            {
                return true;
            }

            if (cropPos == Vector3.zero)
            {
                return false;
            }

            foreach (uint candidateNetId in this.homelandFarmAuraLevelObjectPositionCache.Keys)
            {
                if (candidateNetId == 0U || candidateNetId == cropNetId)
                {
                    continue;
                }

                if (!this.TryHomelandFarmGetComponentData(
                        this.homelandFarmCropBoxItemDataType,
                        candidateNetId,
                        out _,
                        out _,
                        "CropBoxItemData"))
                {
                    continue;
                }

                if (!this.TryHomelandFarmResolveFarmEntityPosition(candidateNetId, out Vector3 boxPos) || boxPos == Vector3.zero)
                {
                    continue;
                }

                Vector3 delta = boxPos - cropPos;
                delta.y = 0f;
                if (delta.sqrMagnitude <= HomelandFarmCropBoxWorldMatchRadius * HomelandFarmCropBoxWorldMatchRadius)
                {
                    cropBoxNetId = candidateNetId;
                    return true;
                }
            }

            return false;
        }

        private bool TryHomelandFarmTryResolveCropVisualWorldPose(uint cropNetId, out Vector3 worldPosition, out Quaternion worldRotation)
        {
            worldPosition = Vector3.zero;
            worldRotation = Quaternion.identity;
            if (cropNetId == 0U)
            {
                return false;
            }

            if (this.TryHomelandFarmTryFindCropBoxNetIdForCrop(cropNetId, out uint cropBoxNetId) && cropBoxNetId != 0U)
            {
                if (this.homelandFarmPlanterSowAnchorByNetId.TryGetValue(cropBoxNetId, out HomelandFarmPlanterSowAnchor sowAnchor)
                    && sowAnchor != null
                    && sowAnchor.WorldPosition != Vector3.zero)
                {
                    worldPosition = sowAnchor.WorldPosition;
                    worldRotation = sowAnchor.WorldRotation;
                    return true;
                }

                if (this.TryHomelandFarmResolveCropBoxSowLevelObjectId(cropBoxNetId, out ulong putZoneId)
                    && putZoneId != 0UL)
                {
                    if (this.homelandFarmPutZoneWorldPositionById.TryGetValue(putZoneId, out worldPosition)
                        && worldPosition != Vector3.zero)
                    {
                        this.homelandFarmPutZoneWorldRotationById.TryGetValue(putZoneId, out worldRotation);
                        return true;
                    }

                    if (this.TryHomelandFarmTryInvokeAuraGetLevelObject(putZoneId, out IntPtr putZoneObj, out _)
                        && putZoneObj != IntPtr.Zero
                        && this.TryHomelandFarmTryGetAuraLevelObjectWorldPose(putZoneObj, out worldPosition, out worldRotation)
                        && worldPosition != Vector3.zero)
                    {
                        this.TryHomelandFarmRememberPlanterSowAnchor(cropBoxNetId, putZoneId, worldPosition, worldRotation);
                        return true;
                    }
                }

                if (this.TryHomelandFarmResolveFarmEntityPosition(cropBoxNetId, out worldPosition) && worldPosition != Vector3.zero)
                {
                    return true;
                }
            }

            return this.TryHomelandFarmResolveFarmEntityPosition(cropNetId, out worldPosition) && worldPosition != Vector3.zero;
        }

        private bool TryHomelandFarmTryGetAuraEntityTransform(uint netId, out IntPtr entityObj, out IntPtr transformObj)
        {
            entityObj = IntPtr.Zero;
            transformObj = IntPtr.Zero;
            if (netId == 0U || !this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return false;
            }

            if (!this.TryGetAuraMonoEntityObjectByNetId(netId, out entityObj) || entityObj == IntPtr.Zero)
            {
                return false;
            }

            return this.TryHomelandFarmTryExtractAuraEntityTransform(entityObj, out transformObj);
        }

        private bool TryHomelandFarmTryExtractAuraEntityTransform(IntPtr entityObj, out IntPtr transformObj)
        {
            transformObj = IntPtr.Zero;
            if (entityObj == IntPtr.Zero)
            {
                return false;
            }

            if (this.TryGetMonoObjectMember(entityObj, "transform", out transformObj) && transformObj != IntPtr.Zero)
            {
                return true;
            }

            if (this.TryGetMonoObjectMember(entityObj, "transformComponent", out IntPtr transformComponentObj)
                && transformComponentObj != IntPtr.Zero
                && this.TryGetMonoObjectMember(transformComponentObj, "transform", out transformObj)
                && transformObj != IntPtr.Zero)
            {
                return true;
            }

            if (this.TryGetMonoObjectMember(entityObj, "transformComponent", out transformComponentObj)
                && transformComponentObj != IntPtr.Zero
                && this.TryGetMonoObjectMember(transformComponentObj, "Transform", out transformObj)
                && transformObj != IntPtr.Zero)
            {
                return true;
            }

            return false;
        }

        private bool TryHomelandFarmTryPreferCropBoxBindTransform(
            uint cropNetId,
            ref IntPtr cropEntityObj,
            ref IntPtr cropTransformObj)
        {
            if (cropNetId == 0U
                || cropEntityObj == IntPtr.Zero
                || cropTransformObj == IntPtr.Zero
                || !this.TryHomelandFarmTryFindCropBoxNetIdForCrop(cropNetId, out uint cropBoxNetId)
                || cropBoxNetId == 0U
                || !this.TryHomelandFarmTryGetAuraEntityTransform(cropBoxNetId, out IntPtr boxEntityObj, out IntPtr boxTransformObj)
                || boxTransformObj == IntPtr.Zero)
            {
                return false;
            }

            Vector3 cropPos = Vector3.zero;
            Vector3 visualPos = Vector3.zero;
            this.TryHomelandFarmResolveFarmEntityPosition(cropNetId, out cropPos);
            this.TryHomelandFarmTryResolveCropVisualWorldPose(cropNetId, out visualPos, out _);
            bool useBoxTransform = this.homelandFarmPlanterSowAnchorByNetId.ContainsKey(cropBoxNetId);
            if (!useBoxTransform && visualPos != Vector3.zero && cropPos != Vector3.zero)
            {
                Vector3 delta = visualPos - cropPos;
                delta.y = 0f;
                useBoxTransform = delta.sqrMagnitude > 0.04f;
            }

            if (!useBoxTransform && visualPos != Vector3.zero)
            {
                this.TryHomelandFarmResolveFarmEntityPosition(cropBoxNetId, out Vector3 boxPos);
                if (boxPos != Vector3.zero)
                {
                    Vector3 delta = boxPos - cropPos;
                    delta.y = 0f;
                    useBoxTransform = cropPos == Vector3.zero || delta.sqrMagnitude > 0.04f;
                }
            }

            if (!useBoxTransform)
            {
                return false;
            }

            cropEntityObj = boxEntityObj;
            cropTransformObj = boxTransformObj;
            return true;
        }

        private unsafe bool TryHomelandFarmPlayFertilizerFeedbackVfxAura(uint cropNetId, int feedbackEffectId, out string status)
        {
            status = "Feedback VFX unavailable.";
            if (cropNetId == 0U || feedbackEffectId <= 0)
            {
                status = feedbackEffectId <= 0 ? "Feedback effect id missing." : "Crop netId missing.";
                return false;
            }

            if (!this.TryHomelandFarmEnsureAuraEntitiesPlayVfxAtMethod(out status)
                || !this.EnsureAuraMonoApiReady()
                || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            if (!this.TryHomelandFarmTryGetCropEntityPositionRotation(cropNetId, out Vector3 position, out Quaternion rotation)
                || position == Vector3.zero)
            {
                status = "Crop position unavailable for VFX netId=" + cropNetId + ".";
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            if (this.homelandFarmAuraEntitiesPlayVfxAtArgCount == 2)
            {
                IntPtr* args = stackalloc IntPtr[2];
                args[0] = (IntPtr)(&feedbackEffectId);
                args[1] = (IntPtr)(&position);
                auraMonoRuntimeInvoke(this.homelandFarmAuraEntitiesPlayVfxAtMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            }
            else
            {
                IntPtr* args = stackalloc IntPtr[3];
                args[0] = (IntPtr)(&feedbackEffectId);
                args[1] = (IntPtr)(&position);
                args[2] = (IntPtr)(&rotation);
                auraMonoRuntimeInvoke(this.homelandFarmAuraEntitiesPlayVfxAtMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            }

            if (exc != IntPtr.Zero)
            {
                status = "PlayVfxAt exc=0x" + exc.ToInt64().ToString("X") + " netId=" + cropNetId + ".";
                return false;
            }

            status = "PlayVfxAt ok effect=" + feedbackEffectId + " netId=" + cropNetId + ".";
            return true;
        }

        private bool TryHomelandFarmPlayFertilizerFeedbackVfxManaged(uint cropNetId, int feedbackEffectId, out string status)
        {
            status = "Managed feedback VFX unavailable.";
            if (cropNetId == 0U || feedbackEffectId <= 0)
            {
                return false;
            }

            this.EnsureHomelandFarmScannerTypes();
            if (this.homelandFarmEntitiesType == null)
            {
                return false;
            }

            if (!this.TryHomelandFarmTryGetCropEntityPositionRotation(cropNetId, out Vector3 position, out Quaternion rotation)
                || position == Vector3.zero)
            {
                status = "Crop position unavailable.";
                return false;
            }

            try
            {
                MethodInfo playVfxMethod = this.homelandFarmEntitiesType.GetMethod(
                    "PlayVfxAt",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                    null,
                    new Type[] { typeof(int), typeof(Vector3), typeof(Quaternion) },
                    null);
                if (playVfxMethod == null)
                {
                    playVfxMethod = this.homelandFarmEntitiesType.GetMethod(
                        "PlayVfxAt",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                        null,
                        new Type[] { typeof(int), typeof(Vector3) },
                        null);
                }

                if (playVfxMethod == null)
                {
                    status = "Entities.PlayVfxAt missing.";
                    return false;
                }

                if (playVfxMethod.GetParameters().Length == 2)
                {
                    playVfxMethod.Invoke(null, new object[] { feedbackEffectId, position });
                }
                else
                {
                    playVfxMethod.Invoke(null, new object[] { feedbackEffectId, position, rotation });
                }

                status = "Managed PlayVfxAt ok effect=" + feedbackEffectId + ".";
                return true;
            }
            catch (Exception ex)
            {
                status = "Managed PlayVfxAt failed: " + (ex.InnerException ?? ex).Message;
                return false;
            }
        }

        private unsafe bool TryHomelandFarmPlayFertilizerVfxOnAura(uint cropNetId, int effectId, string socketName, out string status)
        {
            status = "PlayVfxOn unavailable.";
            if (cropNetId == 0U || effectId <= 0 || string.IsNullOrEmpty(socketName))
            {
                return false;
            }

            if (!this.TryHomelandFarmEnsureAuraEntitiesVisualMethods(out status)
                || this.homelandFarmAuraEntitiesPlayVfxOnMethod == IntPtr.Zero
                || !this.EnsureAuraMonoApiReady()
                || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null
                || auraMonoStringNew == null
                || this.auraMonoRootDomain == IntPtr.Zero)
            {
                return false;
            }

            IntPtr socketObj = auraMonoStringNew(this.auraMonoRootDomain, socketName);
            if (socketObj == IntPtr.Zero)
            {
                status = "PlayVfxOn socket alloc failed.";
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[3];
            args[0] = (IntPtr)(&effectId);
            args[1] = (IntPtr)(&cropNetId);
            args[2] = socketObj;
            auraMonoRuntimeInvoke(this.homelandFarmAuraEntitiesPlayVfxOnMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                status = "PlayVfxOn exc=0x" + exc.ToInt64().ToString("X") + ".";
                return false;
            }

            status = "PlayVfxOn ok effect=" + effectId + " socket=" + socketName + ".";
            return true;
        }

        private unsafe bool TryHomelandFarmCreateManureDecorationAura(uint cropNetId, int decorationId, out string status)
        {
            status = "CreateLevelEntity unavailable.";
            if (cropNetId == 0U || decorationId <= 0)
            {
                return false;
            }

            if (!this.TryHomelandFarmEnsureAuraEntitiesVisualMethods(out status)
                || this.homelandFarmAuraEntitiesCreateLevelEntityMethod == IntPtr.Zero
                || !this.EnsureAuraMonoApiReady()
                || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            if (!this.TryHomelandFarmTryGetCropEntityPositionRotation(cropNetId, out Vector3 position, out Quaternion rotation)
                || position == Vector3.zero)
            {
                status = "Crop position unavailable for decoration.";
                return false;
            }

            uint parentId = 0U;
            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[4];
            args[0] = (IntPtr)(&decorationId);
            args[1] = (IntPtr)(&position);
            args[2] = (IntPtr)(&rotation);
            args[3] = (IntPtr)(&parentId);
            IntPtr manureEntityObj = auraMonoRuntimeInvoke(this.homelandFarmAuraEntitiesCreateLevelEntityMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                status = "CreateLevelEntity exc=0x" + exc.ToInt64().ToString("X") + ".";
                return false;
            }

            if (manureEntityObj != IntPtr.Zero
                && this.TryHomelandFarmResolveAuraCropComponent(cropNetId, out IntPtr cropComponentObj, out _)
                && cropComponentObj != IntPtr.Zero)
            {
                this.TryHomelandFarmTrySetAuraCropManureEntity(cropComponentObj, manureEntityObj);
                this.TryHomelandFarmTryBindAuraEffectEntityToCropTransform(cropNetId, cropComponentObj, manureEntityObj, out _);
            }

            status = "CreateLevelEntity ok decorationId=" + decorationId + ".";
            return true;
        }

        private unsafe bool TryHomelandFarmTrySetAuraCropManureEntity(IntPtr cropComponentObj, IntPtr manureEntityObj)
        {
            if (cropComponentObj == IntPtr.Zero || manureEntityObj == IntPtr.Zero
                || auraMonoObjectGetClass == null || auraMonoFieldSetValue == null)
            {
                return false;
            }

            IntPtr cropClass = auraMonoObjectGetClass(cropComponentObj);
            if (cropClass == IntPtr.Zero)
            {
                return false;
            }

            string[] fieldNames = { "_manureEntity", "manureEntity", "_ManureEntity", "ManureEntity" };
            for (int i = 0; i < fieldNames.Length; i++)
            {
                IntPtr field = this.FindAuraMonoFieldOnHierarchy(cropClass, fieldNames[i]);
                if (field != IntPtr.Zero)
                {
                    auraMonoFieldSetValue(cropComponentObj, field, (IntPtr)(&manureEntityObj));
                    return true;
                }
            }

            return false;
        }

        private bool TryHomelandFarmTryBindAuraEffectEntityToCropTransform(
            uint cropNetId,
            IntPtr cropComponentObj,
            IntPtr effectEntityObj,
            out string status)
        {
            status = "Bind unavailable.";
            if (cropNetId == 0U || cropComponentObj == IntPtr.Zero || effectEntityObj == IntPtr.Zero
                || !this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return false;
            }

            this.TryHomelandFarmInvokeAuraMonoVoidInstanceMethod(cropComponentObj, out _, "_CheckParent");

            if (!this.TryHomelandFarmTryResolveCropBindTransform(
                    cropComponentObj,
                    out IntPtr cropEntityObj,
                    out IntPtr cropTransformObj,
                    out uint resolvedCropNetId))
            {
                status = "Crop transform unavailable.";
                return false;
            }

            uint relocateNetId = resolvedCropNetId != 0U ? resolvedCropNetId : cropNetId;
            if (this.TryHomelandFarmTryResolveCropBoxComponentBindTransform(
                    cropComponentObj,
                    out _,
                    out _,
                    out uint cropBoxNetId)
                && cropBoxNetId != 0U)
            {
                relocateNetId = cropBoxNetId;
            }

            this.TryHomelandFarmTryRelocateAuraEffectEntityToCrop(relocateNetId, effectEntityObj, cropEntityObj);

            if (this.TryHomelandFarmTryInvokeCropBindEffectEntity(effectEntityObj, cropTransformObj, out status))
            {
                status = "BindEffectEntity bound netId=" + cropNetId + ".";
                return true;
            }

            if (!this.TryHomelandFarmTryInvokeAuraRendererPlayAnim(effectEntityObj, cropTransformObj, out status))
            {
                if (this.TryHomelandFarmTryLinkManureRendererToCrop(effectEntityObj, cropEntityObj, out string linkStatus))
                {
                    status = linkStatus;
                    return true;
                }

                return false;
            }

            status = "PlayAnim bound netId=" + cropNetId + ".";
            return true;
        }

        private IntPtr TryHomelandFarmResolveAuraRendererComponentClass()
        {
            if (this.homelandFarmAuraRendererComponentClass != IntPtr.Zero)
            {
                return this.homelandFarmAuraRendererComponentClass;
            }

            string[] fullNames =
            {
                "XDTLevelAndEntity.Core.World.RendererComponent",
                "ScriptsRefactory.LevelAndEntity.Core.World.RendererComponent",
            };
            for (int i = 0; i < fullNames.Length; i++)
            {
                IntPtr candidate = this.FindAuraMonoClassByFullName(fullNames[i]);
                if (candidate != IntPtr.Zero)
                {
                    this.homelandFarmAuraRendererComponentClass = candidate;
                    return candidate;
                }
            }

            this.homelandFarmAuraRendererComponentClass = this.FindAuraMonoClassAcrossLoadedAssemblies(
                "XDTLevelAndEntity.Core.World",
                "RendererComponent");
            return this.homelandFarmAuraRendererComponentClass;
        }

        private IntPtr TryHomelandFarmResolveAuraRendererPlayAnimTransformMethod()
        {
            if (this.homelandFarmAuraRendererPlayAnimTransformMethod != IntPtr.Zero)
            {
                return this.homelandFarmAuraRendererPlayAnimTransformMethod;
            }

            IntPtr rendererClass = this.TryHomelandFarmResolveAuraRendererComponentClass();
            if (rendererClass == IntPtr.Zero
                || auraMonoClassGetMethods == null
                || auraMonoMethodGetName == null)
            {
                return IntPtr.Zero;
            }

            List<IntPtr> candidates = new List<IntPtr>();
            IntPtr iter = IntPtr.Zero;
            while (true)
            {
                IntPtr method = auraMonoClassGetMethods(rendererClass, ref iter);
                if (method == IntPtr.Zero)
                {
                    break;
                }

                string methodName = Marshal.PtrToStringAnsi(auraMonoMethodGetName(method)) ?? string.Empty;
                if (!string.Equals(methodName, "PlayAnim", StringComparison.Ordinal)
                    || this.TryGetAuraMonoMethodParamCount(method) != 1)
                {
                    continue;
                }

                candidates.Add(method);
            }

            // Decompiled RendererComponent declares PlayAnim(Transform) before PlayAnim(int).
            if (candidates.Count > 0)
            {
                this.homelandFarmAuraRendererPlayAnimTransformMethod = candidates[0];
            }

            return this.homelandFarmAuraRendererPlayAnimTransformMethod;
        }

        private bool TryHomelandFarmTryResolveCropBoxComponentBindTransform(
            IntPtr cropComponentObj,
            out IntPtr bindEntityObj,
            out IntPtr bindTransformObj,
            out uint bindNetId)
        {
            bindEntityObj = IntPtr.Zero;
            bindTransformObj = IntPtr.Zero;
            bindNetId = 0U;
            if (cropComponentObj == IntPtr.Zero)
            {
                return false;
            }

            IntPtr cropBoxComponentObj = IntPtr.Zero;
            if ((!this.TryGetMonoObjectMember(cropComponentObj, "_cropBoxComponent", out cropBoxComponentObj)
                    || cropBoxComponentObj == IntPtr.Zero)
                && (!this.TryGetMonoObjectMember(cropComponentObj, "cropBoxComponent", out cropBoxComponentObj)
                    || cropBoxComponentObj == IntPtr.Zero))
            {
                return false;
            }

            if (!this.TryGetMonoObjectMember(cropBoxComponentObj, "entity", out bindEntityObj)
                || bindEntityObj == IntPtr.Zero)
            {
                return false;
            }

            this.TryGetMonoUInt32Member(bindEntityObj, "netId", out bindNetId);
            if (bindNetId == 0U)
            {
                this.TryGetMonoUInt32Member(bindEntityObj, "NetId", out bindNetId);
            }

            return this.TryHomelandFarmTryExtractAuraEntityTransform(bindEntityObj, out bindTransformObj)
                && bindTransformObj != IntPtr.Zero;
        }

        private unsafe bool TryHomelandFarmTryInvokeCropBindEffectEntity(
            IntPtr effectEntityObj,
            IntPtr transformObj,
            out string status)
        {
            status = "BindEffectEntity unavailable.";
            if (effectEntityObj == IntPtr.Zero || transformObj == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            if (this.homelandFarmAuraCropBindEffectEntityMethod == IntPtr.Zero)
            {
                if (!this.TryResolveHomelandFarmAuraCropComponentClass(out IntPtr cropComponentClass)
                    || cropComponentClass == IntPtr.Zero)
                {
                    return false;
                }

                this.homelandFarmAuraCropBindEffectEntityMethod = this.FindAuraMonoMethodOnHierarchy(
                    cropComponentClass,
                    "BindEffectEntity",
                    2);
            }

            if (this.homelandFarmAuraCropBindEffectEntityMethod == IntPtr.Zero
                || !this.EnsureAuraMonoApiReady()
                || !this.AttachAuraMonoThread())
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[2];
            args[0] = (IntPtr)(&effectEntityObj);
            args[1] = (IntPtr)(&transformObj);
            auraMonoRuntimeInvoke(this.homelandFarmAuraCropBindEffectEntityMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                status = "BindEffectEntity exc=0x" + exc.ToInt64().ToString("X") + ".";
                return false;
            }

            status = "BindEffectEntity ok.";
            return true;
        }

        private bool TryHomelandFarmTryResolveCropBindTransform(
            IntPtr cropComponentObj,
            out IntPtr cropEntityObj,
            out IntPtr cropTransformObj,
            out uint cropNetId)
        {
            cropEntityObj = IntPtr.Zero;
            cropTransformObj = IntPtr.Zero;
            cropNetId = 0U;
            if (cropComponentObj == IntPtr.Zero)
            {
                return false;
            }

            if (!this.TryGetMonoObjectMember(cropComponentObj, "entity", out cropEntityObj)
                || cropEntityObj == IntPtr.Zero)
            {
                return false;
            }

            this.TryGetMonoUInt32Member(cropEntityObj, "netId", out cropNetId);
            if (cropNetId == 0U)
            {
                this.TryGetMonoUInt32Member(cropEntityObj, "NetId", out cropNetId);
            }

            if (this.TryHomelandFarmTryResolveCropBoxComponentBindTransform(
                    cropComponentObj,
                    out IntPtr cropBoxEntityObj,
                    out IntPtr cropBoxTransformObj,
                    out _)
                && cropBoxTransformObj != IntPtr.Zero)
            {
                cropEntityObj = cropBoxEntityObj;
                cropTransformObj = cropBoxTransformObj;
                return true;
            }

            if (this.TryHomelandFarmTryExtractAuraEntityTransform(cropEntityObj, out cropTransformObj))
            {
                this.TryHomelandFarmTryPreferCropBoxBindTransform(cropNetId, ref cropEntityObj, ref cropTransformObj);
                return true;
            }

            return false;
        }

        private bool TryHomelandFarmTryGetAuraMonoRendererComponent(IntPtr entityObj, out IntPtr rendererComponentObj)
        {
            rendererComponentObj = IntPtr.Zero;
            if (entityObj == IntPtr.Zero || auraMonoObjectGetClass == null || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr rendererClass = this.TryHomelandFarmResolveAuraRendererComponentClass();
            if (rendererClass == IntPtr.Zero)
            {
                return false;
            }

            if (!this.TryInvokeAuraMonoZeroArg(entityObj, out IntPtr componentsObj, "GetAllComponents")
                || componentsObj == IntPtr.Zero)
            {
                return false;
            }

            List<IntPtr> components = new List<IntPtr>();
            if (!this.TryEnumerateAuraMonoCollectionItems(componentsObj, components))
            {
                return false;
            }

            for (int i = 0; i < components.Count && i < 32; i++)
            {
                IntPtr candidate = components[i];
                if (candidate == IntPtr.Zero)
                {
                    continue;
                }

                IntPtr candidateClass = auraMonoObjectGetClass(candidate);
                if (candidateClass != IntPtr.Zero
                    && this.IsAuraMonoClassAssignableTo(candidateClass, rendererClass))
                {
                    rendererComponentObj = candidate;
                    return true;
                }
            }

            return false;
        }

        private unsafe bool TryHomelandFarmTryRelocateAuraEffectEntityToCrop(
            uint cropNetId,
            IntPtr effectEntityObj,
            IntPtr cropEntityObj)
        {
            if (cropNetId == 0U || effectEntityObj == IntPtr.Zero)
            {
                return false;
            }

            Vector3 position = Vector3.zero;
            Quaternion rotation = Quaternion.identity;
            if (!this.TryHomelandFarmTryResolveCropVisualWorldPose(cropNetId, out position, out rotation)
                || position == Vector3.zero)
            {
                if (!this.TryHomelandFarmResolveFarmEntityPosition(cropNetId, out position) || position == Vector3.zero)
                {
                    return false;
                }

                rotation = Quaternion.identity;
                if (cropEntityObj != IntPtr.Zero)
                {
                    IntPtr cropClass = auraMonoObjectGetClass != null ? auraMonoObjectGetClass(cropEntityObj) : IntPtr.Zero;
                    if (cropClass != IntPtr.Zero)
                    {
                        IntPtr getRotationMethod = this.FindAuraMonoMethodOnHierarchy(cropClass, "get_rotation", 0);
                        if (getRotationMethod == IntPtr.Zero)
                        {
                            getRotationMethod = this.FindAuraMonoMethodOnHierarchy(cropClass, "GetRotation", 0);
                        }

                        if (getRotationMethod != IntPtr.Zero && auraMonoRuntimeInvoke != null && auraMonoObjectUnbox != null)
                        {
                            IntPtr exc = IntPtr.Zero;
                            IntPtr boxed = auraMonoRuntimeInvoke(getRotationMethod, cropEntityObj, IntPtr.Zero, ref exc);
                            if (exc == IntPtr.Zero && boxed != IntPtr.Zero)
                            {
                                IntPtr raw = auraMonoObjectUnbox(boxed);
                                if (raw != IntPtr.Zero)
                                {
                                    rotation = *(Quaternion*)raw;
                                }
                            }
                        }
                    }
                }
            }

            Vector3 scale = Vector3.one;
            if (this.TryHomelandFarmTryInvokeAuraRendererUpdateRootHierarchy(
                    effectEntityObj,
                    position,
                    rotation,
                    scale,
                    out _))
            {
                return true;
            }

            return this.TryHomelandFarmTryWriteAuraEntityPosition(effectEntityObj, position, rotation);
        }

        private unsafe bool TryHomelandFarmTryWriteAuraEntityPosition(
            IntPtr entityObj,
            Vector3 position,
            Quaternion rotation)
        {
            if (entityObj == IntPtr.Zero || auraMonoObjectGetClass == null)
            {
                return false;
            }

            IntPtr entityClass = auraMonoObjectGetClass(entityObj);
            if (entityClass == IntPtr.Zero)
            {
                return false;
            }

            bool wrote = false;
            string[] positionFieldNames = { "position", "_position", "<position>k__BackingField" };
            for (int i = 0; i < positionFieldNames.Length; i++)
            {
                IntPtr field = this.FindAuraMonoFieldOnHierarchy(entityClass, positionFieldNames[i]);
                if (field == IntPtr.Zero)
                {
                    continue;
                }

                if (this.TryHomelandFarmTryWriteAuraMonoVector3Field(entityObj, field, position, out _))
                {
                    wrote = true;
                    break;
                }
            }

            string[] rotationFieldNames = { "rotation", "_rotation", "<rotation>k__BackingField" };
            for (int i = 0; i < rotationFieldNames.Length; i++)
            {
                IntPtr field = this.FindAuraMonoFieldOnHierarchy(entityClass, rotationFieldNames[i]);
                if (field == IntPtr.Zero || auraMonoFieldSetValue == null)
                {
                    continue;
                }

                auraMonoFieldSetValue(entityObj, field, (IntPtr)(&rotation));
                wrote = true;
                break;
            }

            return wrote;
        }

        private unsafe bool TryHomelandFarmTryInvokeAuraRendererUpdateRootHierarchy(
            IntPtr entityObj,
            Vector3 position,
            Quaternion rotation,
            Vector3 scale,
            out string status)
        {
            status = "UpdateRootHierarchy unavailable.";
            if (entityObj == IntPtr.Zero
                || !this.TryHomelandFarmTryGetAuraMonoRendererComponent(entityObj, out IntPtr rendererObj)
                || rendererObj == IntPtr.Zero
                || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr rendererClass = auraMonoObjectGetClass(rendererObj);
            IntPtr method = rendererClass != IntPtr.Zero
                ? this.FindAuraMonoMethodOnHierarchy(rendererClass, "UpdateRootHierarchy", 3)
                : IntPtr.Zero;
            if (method == IntPtr.Zero)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[3];
            args[0] = (IntPtr)(&position);
            args[1] = (IntPtr)(&rotation);
            args[2] = (IntPtr)(&scale);
            auraMonoRuntimeInvoke(method, rendererObj, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                status = "UpdateRootHierarchy exc=0x" + exc.ToInt64().ToString("X") + ".";
                return false;
            }

            status = "UpdateRootHierarchy ok.";
            return true;
        }

        private unsafe bool TryHomelandFarmTryLinkManureRendererToCrop(
            IntPtr manureEntityObj,
            IntPtr cropEntityObj,
            out string status)
        {
            status = "Link unavailable.";
            if (manureEntityObj == IntPtr.Zero
                || cropEntityObj == IntPtr.Zero
                || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            if (!this.TryHomelandFarmTryGetAuraMonoRendererComponent(manureEntityObj, out IntPtr manureRendererObj)
                || !this.TryHomelandFarmTryGetAuraMonoRendererComponent(cropEntityObj, out IntPtr cropRendererObj))
            {
                status = "RendererComponent missing for Link.";
                return false;
            }

            IntPtr rendererClass = this.TryHomelandFarmResolveAuraRendererComponentClass();
            IntPtr linkMethod = rendererClass != IntPtr.Zero
                ? this.FindAuraMonoMethodOnHierarchy(rendererClass, "Link", 1)
                : IntPtr.Zero;
            if (linkMethod == IntPtr.Zero)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = cropRendererObj;
            auraMonoRuntimeInvoke(linkMethod, manureRendererObj, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                status = "Link exc=0x" + exc.ToInt64().ToString("X") + ".";
                return false;
            }

            status = "Link ok.";
            return true;
        }

        private unsafe bool TryHomelandFarmTryInvokeAuraRendererPlayAnim(IntPtr entityObj, IntPtr transformObj, out string status)
        {
            status = "PlayAnim unavailable.";
            if (entityObj == IntPtr.Zero || transformObj == IntPtr.Zero
                || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            if (!this.TryHomelandFarmTryGetAuraMonoRendererComponent(entityObj, out IntPtr rendererObj)
                || rendererObj == IntPtr.Zero)
            {
                status = "RendererComponent missing on effect entity.";
                return false;
            }

            IntPtr playAnimMethod = this.TryHomelandFarmResolveAuraRendererPlayAnimTransformMethod();
            if (playAnimMethod == IntPtr.Zero)
            {
                status = "PlayAnim(Transform) missing.";
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = transformObj;
            auraMonoRuntimeInvoke(playAnimMethod, rendererObj, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                status = "PlayAnim exc=0x" + exc.ToInt64().ToString("X") + ".";
                return false;
            }

            status = "PlayAnim ok.";
            return true;
        }

        private bool TryHomelandFarmTrySyncAuraCropManureDecorationTransform(uint cropNetId, out string status)
        {
            status = "Manure sync unavailable.";
            if (cropNetId == 0U)
            {
                status = "Crop netId missing.";
                return false;
            }

            if (!this.TryHomelandFarmResolveAuraCropComponent(cropNetId, out IntPtr cropComponentObj, out string resolveStatus)
                || cropComponentObj == IntPtr.Zero)
            {
                status = resolveStatus;
                return false;
            }

            IntPtr manureEntityObj = IntPtr.Zero;
            if (!this.TryGetMonoObjectMember(cropComponentObj, "_manureEntity", out manureEntityObj)
                || manureEntityObj == IntPtr.Zero)
            {
                this.TryGetMonoObjectMember(cropComponentObj, "manureEntity", out manureEntityObj);
            }

            if (manureEntityObj == IntPtr.Zero)
            {
                status = "Manure entity missing.";
                return false;
            }

            return this.TryHomelandFarmTryBindAuraEffectEntityToCropTransform(
                cropNetId,
                cropComponentObj,
                manureEntityObj,
                out status);
        }

        private bool TryHomelandFarmPlayFertilizerVisualEffects(
            uint cropNetId,
            int feedbackEffect,
            int actionEffect,
            out string detail)
        {
            detail = string.Empty;
            if (cropNetId == 0U)
            {
                return false;
            }

            System.Text.StringBuilder attempts = new System.Text.StringBuilder();
            bool any = false;

            int[] effectIds = { feedbackEffect, actionEffect };
            string[] effectLabels = { "feedback", "action" };
            for (int i = 0; i < effectIds.Length; i++)
            {
                int effectId = effectIds[i];
                if (effectId <= 0)
                {
                    continue;
                }

                if (this.TryHomelandFarmPlayFertilizerFeedbackVfxAura(cropNetId, effectId, out string atStatus))
                {
                    any = true;
                    if (attempts.Length > 0)
                    {
                        attempts.Append(';');
                    }

                    attempts.Append("PlayVfxAt(").Append(effectLabels[i]).Append('=').Append(effectId).Append(")");
                }
                else if (this.TryHomelandFarmPlayFertilizerFeedbackVfxManaged(cropNetId, effectId, out atStatus))
                {
                    any = true;
                    if (attempts.Length > 0)
                    {
                        attempts.Append(';');
                    }

                    attempts.Append("managed PlayVfxAt(").Append(effectLabels[i]).Append('=').Append(effectId).Append(")");
                }

                if (this.TryHomelandFarmPlayFertilizerVfxOnAura(cropNetId, effectId, "vfx_root", out string onStatus))
                {
                    any = true;
                    if (attempts.Length > 0)
                    {
                        attempts.Append(';');
                    }

                    attempts.Append("PlayVfxOn(root,").Append(effectLabels[i]).Append('=').Append(effectId).Append(")");
                }
            }

            detail = any ? attempts.ToString() : "no VFX ids";
            return any;
        }

        private bool TryHomelandFarmPlayFertilizerFeedbackVfx(uint cropNetId, int feedbackEffectId, out string status)
        {
            return this.TryHomelandFarmPlayFertilizerVisualEffects(cropNetId, feedbackEffectId, 0, out status);
        }

        private unsafe bool TryHomelandFarmInvokeAuraMonoVoidInstanceMethod(IntPtr obj, out string status, params string[] methodNames)
        {
            status = "Method unavailable.";
            if (obj == IntPtr.Zero || methodNames == null || methodNames.Length == 0 || auraMonoObjectGetClass == null || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr classPtr = auraMonoObjectGetClass(obj);
            if (classPtr == IntPtr.Zero)
            {
                return false;
            }

            for (int i = 0; i < methodNames.Length; i++)
            {
                string methodName = methodNames[i];
                if (string.IsNullOrEmpty(methodName))
                {
                    continue;
                }

                IntPtr method = this.FindAuraMonoMethodOnHierarchy(classPtr, methodName, 0);
                if (method == IntPtr.Zero)
                {
                    continue;
                }

                IntPtr exc = IntPtr.Zero;
                auraMonoRuntimeInvoke(method, obj, IntPtr.Zero, ref exc);
                if (exc == IntPtr.Zero)
                {
                    status = methodName + " ok.";
                    return true;
                }

                status = methodName + " exc=0x" + exc.ToInt64().ToString("X") + ".";
            }

            return false;
        }

        private bool TryHomelandFarmResolveAuraCropComponent(uint netId, out IntPtr cropComponentObj, out string status)
        {
            cropComponentObj = IntPtr.Zero;
            status = "CropComponent unavailable.";
            if (netId == 0U || !this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoObjectGetClass == null)
            {
                return false;
            }

            if (!this.TryGetAuraMonoEntityObjectByNetId(netId, out IntPtr entityObj) || entityObj == IntPtr.Zero)
            {
                status = "Entity unavailable for netId=" + netId + ".";
                return false;
            }

            if (!this.TryInvokeAuraMonoZeroArg(entityObj, out IntPtr componentsObj, "GetAllComponents") || componentsObj == IntPtr.Zero)
            {
                status = "GetAllComponents unavailable for netId=" + netId + ".";
                return false;
            }

            List<IntPtr> components = new List<IntPtr>();
            if (!this.TryEnumerateAuraMonoCollectionItems(componentsObj, components) || components.Count <= 0)
            {
                status = "No components on netId=" + netId + ".";
                return false;
            }

            if (!this.TryResolveAuraMonoFarmComponentClasses(out _, out _, out IntPtr cropComponentClass))
            {
                cropComponentClass = IntPtr.Zero;
            }

            for (int i = 0; i < components.Count && i < 64; i++)
            {
                IntPtr candidate = components[i];
                if (candidate == IntPtr.Zero)
                {
                    continue;
                }

                IntPtr candidateClass = auraMonoObjectGetClass(candidate);
                if (candidateClass == IntPtr.Zero)
                {
                    continue;
                }

                if (cropComponentClass != IntPtr.Zero && this.IsAuraMonoClassAssignableTo(candidateClass, cropComponentClass))
                {
                    cropComponentObj = candidate;
                    status = "CropComponent ready.";
                    return true;
                }

                string className = this.GetAuraMonoClassDisplayName(candidateClass);
                if (!string.IsNullOrEmpty(className)
                    && className.IndexOf("CropComponent", StringComparison.OrdinalIgnoreCase) >= 0
                    && className.IndexOf("CropBoxComponent", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    cropComponentObj = candidate;
                    status = "CropComponent ready.";
                    return true;
                }
            }

            status = "CropComponent missing on netId=" + netId + ".";
            return false;
        }

        private unsafe bool TryHomelandFarmResetAuraCropLastManureId(IntPtr cropComponentObj)
        {
            if (cropComponentObj == IntPtr.Zero || auraMonoObjectGetClass == null || auraMonoFieldSetValue == null)
            {
                return false;
            }

            IntPtr cropClass = auraMonoObjectGetClass(cropComponentObj);
            if (cropClass == IntPtr.Zero)
            {
                return false;
            }

            string[] fieldNames = { "_lastManureId", "lastManureId", "_LastManureId", "LastManureId" };
            int zero = 0;
            for (int i = 0; i < fieldNames.Length; i++)
            {
                IntPtr field = this.FindAuraMonoFieldOnHierarchy(cropClass, fieldNames[i]);
                if (field != IntPtr.Zero)
                {
                    auraMonoFieldSetValue(cropComponentObj, field, (IntPtr)(&zero));
                    return true;
                }
            }

            return false;
        }

        private unsafe bool TryHomelandFarmSetAuraCropLastManureId(IntPtr cropComponentObj, int manureId)
        {
            if (cropComponentObj == IntPtr.Zero || auraMonoObjectGetClass == null || auraMonoFieldSetValue == null)
            {
                return false;
            }

            IntPtr cropClass = auraMonoObjectGetClass(cropComponentObj);
            if (cropClass == IntPtr.Zero)
            {
                return false;
            }

            string[] fieldNames = { "_lastManureId", "lastManureId", "_LastManureId", "LastManureId" };
            for (int i = 0; i < fieldNames.Length; i++)
            {
                IntPtr field = this.FindAuraMonoFieldOnHierarchy(cropClass, fieldNames[i]);
                if (field != IntPtr.Zero)
                {
                    auraMonoFieldSetValue(cropComponentObj, field, (IntPtr)(&manureId));
                    return true;
                }
            }

            return false;
        }

        private bool TryHomelandFarmRefreshCropManureVisualAura(uint cropNetId, int fertilizerStaticId, out int decorationId, out int feedbackEffect, out string status)
        {
            status = "Aura visual refresh unavailable.";
            decorationId = 0;
            feedbackEffect = 0;
            int actionEffect = 0;
            this.TryHomelandFarmTryGetCropFertilizerVisualInfo(
                fertilizerStaticId,
                out _,
                out decorationId,
                out feedbackEffect,
                out actionEffect,
                out string visualSource);

            if (!this.TryHomelandFarmResolveAuraCropComponent(cropNetId, out IntPtr cropComponentObj, out string resolveStatus)
                || cropComponentObj == IntPtr.Zero)
            {
                status = resolveStatus;
                return false;
            }

            this.TryHomelandFarmInvokeAuraMonoVoidInstanceMethod(cropComponentObj, out _, "StopManureEffect");
            this.TryHomelandFarmResetAuraCropLastManureId(cropComponentObj);

            // Do NOT call UpdateManureEffect here: it spawns decoration at base.entity.position,
            // which is wrong for mod-sown crops. Create at resolved world position and bind instead.
            string createStatus = "decorationId missing.";
            bool decorationCreated = decorationId > 0
                && this.TryHomelandFarmCreateManureDecorationAura(cropNetId, decorationId, out createStatus);

            bool decorationSynced = this.TryHomelandFarmTrySyncAuraCropManureDecorationTransform(cropNetId, out string syncStatus);
            if (!decorationSynced && decorationCreated)
            {
                decorationSynced = this.TryHomelandFarmTrySyncAuraCropManureDecorationTransform(cropNetId, out syncStatus);
            }

            if (fertilizerStaticId > 0)
            {
                this.TryHomelandFarmSetAuraCropLastManureId(cropComponentObj, fertilizerStaticId);
            }

            this.TryHomelandFarmInvokeAuraMonoVoidInstanceMethod(cropComponentObj, out _, "PlayShakeEffect");

            bool vfxPlayed = this.TryHomelandFarmPlayFertilizerVisualEffects(
                cropNetId,
                feedbackEffect,
                actionEffect,
                out string vfxDetail);

            if (decorationCreated || decorationSynced || vfxPlayed)
            {
                status = "Aura netId=" + cropNetId
                    + " src=" + visualSource
                    + " decoration=" + (decorationCreated || decorationSynced ? "ok" : "skip")
                    + " create=" + (decorationCreated ? createStatus : "none")
                    + " sync=" + (decorationSynced ? syncStatus : "none")
                    + " vfx=" + (vfxPlayed ? vfxDetail : "none")
                    + " decorationId=" + decorationId
                    + " feedbackEffect=" + feedbackEffect
                    + " actionEffect=" + actionEffect + ".";
                return true;
            }

            status = "Aura refresh failed netId=" + cropNetId
                + " src=" + visualSource
                + " create=" + createStatus
                + " decorationId=" + decorationId
                + " feedbackEffect=" + feedbackEffect
                + " actionEffect=" + actionEffect + ".";
            return false;
        }

        private bool TryHomelandFarmRefreshCropManureVisualManaged(uint cropNetId, int fertilizerStaticId, out int decorationId, out int feedbackEffect, out string status)
        {
            status = "Managed visual refresh unavailable.";
            decorationId = 0;
            feedbackEffect = 0;
            int actionEffect = 0;
            this.TryHomelandFarmTryGetCropFertilizerVisualInfo(
                fertilizerStaticId,
                out _,
                out decorationId,
                out feedbackEffect,
                out actionEffect,
                out string visualSource);

            if (cropNetId == 0U || !this.EnsureHomelandFarmScannerTypes() || this.homelandFarmCropComponentType == null)
            {
                return false;
            }

            bool decorationCreated = false;
            if (this.EnsureHomelandFarmTableDataReflection() && this.homelandFarmGetEntityMethod != null)
            {
                try
                {
                    object entity = this.homelandFarmGetEntityMethod.GetParameters().Length == 2
                        ? this.homelandFarmGetEntityMethod.Invoke(null, new object[] { cropNetId, true })
                        : this.homelandFarmGetEntityMethod.Invoke(null, new object[] { cropNetId });
                    if (entity != null
                        && this.TryHomelandFarmGetComponent(entity, this.homelandFarmCropComponentType, out object cropComponent)
                        && cropComponent != null)
                    {
                        Type cropType = cropComponent.GetType();
                        this.TryInvokeManagedInstanceMethod(cropType, cropComponent, "StopManureEffect");
                        this.TrySetManagedInstanceField(cropType, cropComponent, 0, "_lastManureId", "lastManureId", "_LastManureId", "LastManureId");
                        decorationCreated = decorationId > 0
                            && this.TryHomelandFarmCreateManureDecorationAura(cropNetId, decorationId, out _);
                        if (fertilizerStaticId > 0)
                        {
                            this.TrySetManagedInstanceField(
                                cropType,
                                cropComponent,
                                fertilizerStaticId,
                                "_lastManureId",
                                "lastManureId",
                                "_LastManureId",
                                "LastManureId");
                        }

                        this.TryInvokeManagedInstanceMethod(cropType, cropComponent, "PlayShakeEffect");
                    }
                }
                catch (Exception ex)
                {
                    status = "Managed refresh exception: " + (ex.InnerException ?? ex).Message;
                    return false;
                }
            }

            bool vfxPlayed = this.TryHomelandFarmPlayFertilizerVisualEffects(
                cropNetId,
                feedbackEffect,
                actionEffect,
                out string vfxDetail);

            if (decorationCreated || vfxPlayed)
            {
                status = "Managed netId=" + cropNetId
                    + " src=" + visualSource
                    + " decoration=" + (decorationCreated ? "ok" : "skip")
                    + " vfx=" + (vfxPlayed ? vfxDetail : "none")
                    + " decorationId=" + decorationId
                    + " feedbackEffect=" + feedbackEffect
                    + " actionEffect=" + actionEffect + ".";
                return true;
            }

            return false;
        }

        private bool TryInvokeManagedInstanceMethod(Type type, object target, string methodName)
        {
            if (type == null || target == null || string.IsNullOrEmpty(methodName))
            {
                return false;
            }

            try
            {
                MethodInfo method = type.GetMethod(
                    methodName,
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    Type.EmptyTypes,
                    null);
                if (method == null)
                {
                    return false;
                }

                method.Invoke(target, null);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TrySetManagedInstanceField(Type type, object target, int value, params string[] fieldNames)
        {
            if (type == null || target == null || fieldNames == null)
            {
                return false;
            }

            for (int i = 0; i < fieldNames.Length; i++)
            {
                try
                {
                    FieldInfo field = type.GetField(
                        fieldNames[i],
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (field == null)
                    {
                        continue;
                    }

                    field.SetValue(target, Convert.ChangeType(value, field.FieldType));
                    return true;
                }
                catch
                {
                }
            }

            return false;
        }

        private bool TryHomelandFarmRefreshCropManureVisual(uint cropNetId, int fertilizerStaticId, out string status)
        {
            status = "Visual refresh unavailable.";
            if (cropNetId == 0U)
            {
                return false;
            }

            this.TryHomelandFarmTryGetCropFertilizerVisualInfo(
                fertilizerStaticId,
                out int effectType,
                out int decorationId,
                out int feedbackEffect,
                out int actionEffect,
                out string visualSource);

            if (this.TryHomelandFarmRefreshCropManureVisualAura(
                    cropNetId,
                    fertilizerStaticId,
                    out decorationId,
                    out feedbackEffect,
                    out string auraStatus))
            {
                status = auraStatus;
                return true;
            }

            if (this.TryHomelandFarmRefreshCropManureVisualManaged(
                    cropNetId,
                    fertilizerStaticId,
                    out decorationId,
                    out feedbackEffect,
                    out string managedStatus))
            {
                status = managedStatus;
                return true;
            }

            status = auraStatus + "; " + managedStatus
                + " src=" + visualSource
                + " effectType=" + effectType
                + " decorationId=" + decorationId
                + " feedbackEffect=" + feedbackEffect
                + " actionEffect=" + actionEffect
                + (decorationId <= 0 && feedbackEffect <= 0 && actionEffect <= 0
                    ? " (no decoration/feedback/action ids)"
                    : string.Empty);
            return false;
        }

        private bool TryHomelandFarmTryGetTableModeCellCount(int mode, out int cellCount)
        {
            cellCount = 0;
            if (mode <= 0)
            {
                return false;
            }

            try
            {
                Type tableDataType = this.FindLoadedType("TableData", "EcsClient.TableData");
                FieldInfo modesField = tableDataType?.GetField(
                    "TableModes",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                object modesDict = modesField?.GetValue(null);
                if (modesDict != null)
                {
                    MethodInfo tryGetValue = modesDict.GetType().GetMethod("TryGetValue");
                    if (tryGetValue != null)
                    {
                        object[] args = new object[] { mode, null };
                        if ((bool)(tryGetValue.Invoke(modesDict, args) ?? false)
                            && args[1] != null
                            && this.TryGetObjectMember(args[1], "num", out object numObj)
                            && numObj != null)
                        {
                            cellCount = Convert.ToInt32(numObj);
                            return cellCount > 0;
                        }
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private int TryHomelandFarmGetFertilizerCastCellCount()
        {
            this.TryHomelandFarmTryGetEquippedFertilizerMode(out int mode);
            if (this.TryHomelandFarmTryGetTableModeCellCount(mode, out int cellCount) && cellCount > 0)
            {
                this.HomelandFarmLog("Fertilizer cast cell count from TableMode[" + mode + "].num=" + cellCount);
                return cellCount;
            }

            // TableData unavailable on this build (managed reflection missing) -> the mode-based
            // lookup fails and we used to return 1 (fertilize one crop at a time). Fall back to the
            // player's hobby/water cell capacity (same cell pattern as the fertilize cast) so
            // fertilize batches like water/sow.
            cellCount = this.TryHomelandFarmGetSprinklerCellCount();
            this.HomelandFarmLog("Fertilizer cast cell count fallback mode=" + mode + " cells=" + cellCount);
            return Math.Max(1, cellCount);
        }

        private List<uint> BuildHomelandFarmFertilizeTargets(
            List<uint> cropCandidates,
            int fertilizerStaticId,
            HashSet<uint> scanNetIds,
            int maxCount)
        {
            List<uint> result = new List<uint>();
            if (cropCandidates == null || cropCandidates.Count == 0 || maxCount <= 0)
            {
                return result;
            }

            HashSet<uint> seenNetIds = new HashSet<uint>();
            List<Vector3> seenPositions = new List<Vector3>();
            for (int i = 0; i < cropCandidates.Count && result.Count < maxCount; i++)
            {
                uint cropNetId = cropCandidates[i];
                if (cropNetId == 0U
                    || !seenNetIds.Add(cropNetId)
                    || !this.IsHomelandFarmCropFertilizable(cropNetId, fertilizerStaticId, scanNetIds, out _))
                {
                    continue;
                }

                if (this.TryHomelandFarmResolveFarmEntityPosition(cropNetId, out Vector3 cropPos) && cropPos != Vector3.zero)
                {
                    bool duplicatePosition = false;
                    for (int p = 0; p < seenPositions.Count; p++)
                    {
                        if ((seenPositions[p] - cropPos).sqrMagnitude <= HomelandFarmCropBoxWorldMatchRadius * HomelandFarmCropBoxWorldMatchRadius)
                        {
                            duplicatePosition = true;
                            break;
                        }
                    }

                    if (duplicatePosition)
                    {
                        seenNetIds.Remove(cropNetId);
                        continue;
                    }

                    seenPositions.Add(cropPos);
                }

                result.Add(cropNetId);
            }

            return result;
        }

        private bool TryHomelandFarmTryGetEquippedFertilizerMode(out int mode)
        {
            mode = 1;
            try
            {
                object playerObj = null;
                if (this.homelandFarmEntityUtilGetSelfPlayerMethod != null)
                {
                    playerObj = this.homelandFarmEntityUtilGetSelfPlayerMethod.Invoke(null, null);
                }

                if (playerObj == null && this.TryGetManagedSelfPlayerObject(out object managedPlayer, out _))
                {
                    playerObj = managedPlayer;
                }

                if (playerObj != null
                    && this.TryGetObjectMember(playerObj, "equipComponent", out object equipObj)
                    && equipObj != null
                    && this.TryGetObjectMember(equipObj, "handhold", out object handholdObj)
                    && handholdObj != null
                    && this.TryReadManagedInt32Member(handholdObj, "mode", out int handholdMode)
                    && handholdMode > 0)
                {
                    mode = handholdMode;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private bool TryHomelandFarmInvokeCropSeedingInterop(uint seedNetId, List<object> plantPoints, out string status)
        {
            status = "CropSeeding interop unavailable.";
            if (seedNetId == 0U || plantPoints == null || plantPoints.Count == 0)
            {
                status = seedNetId == 0U ? "Seed netId missing." : "Plant point list empty.";
                return false;
            }

            this.TryEnsureHomelandFarmInteropAssembliesLoaded();
            if (!this.EnsureHomelandFarmSowManagedReflection())
            {
                return false;
            }

            if (this.homelandFarmCropSeedingInteropMethod == null)
            {
                Type cropProtocolType = this.homelandFarmCropProtocolManagerType
                    ?? this.ResolveHomelandFarmManagedType(
                        "CropProtocolManager",
                        "XDTDataAndProtocol.ProtocolService.Plant.CropProtocolManager");
                if (cropProtocolType != null)
                {
                    this.homelandFarmCropSeedingInteropMethod = this.ResolveHomelandFarmCropSeedingMethod()
                        ?? this.GetMethodByNameAndParamCountQuiet(cropProtocolType, "CropSeeding", 2);
                }

                if (this.homelandFarmCropSeedingInteropMethod == null)
                {
                    status = "CropSeeding interop method missing (CropProtocolManager="
                        + (cropProtocolType != null) + ").";
                    return false;
                }
            }

            try
            {
                ParameterInfo[] parameters = this.homelandFarmCropSeedingInteropMethod.GetParameters();
                Type listType = parameters[1].ParameterType;
                object pointsList = Activator.CreateInstance(listType);
                MethodInfo addMethod = listType.GetMethod("Add", BindingFlags.Instance | BindingFlags.Public);
                if (addMethod == null)
                {
                    status = "CropSeeding interop list Add missing.";
                    return false;
                }

                for (int i = 0; i < plantPoints.Count; i++)
                {
                    object point = this.TryHomelandFarmMaterializeCropPlantPoint(plantPoints[i]);
                    if (point == null)
                    {
                        status = "CropSeeding interop point missing at index " + i + ".";
                        return false;
                    }

                    addMethod.Invoke(pointsList, new object[] { point });
                }

                this.homelandFarmCropSeedingInteropMethod.Invoke(null, new object[] { seedNetId, pointsList });
                status = "CropSeeding interop ok count=" + plantPoints.Count + ".";
                this.HomelandFarmLog(status);
                return true;
            }
            catch (Exception ex)
            {
                status = "CropSeeding interop exception: " + (ex.InnerException ?? ex).Message;
                this.HomelandFarmLog(status);
                return false;
            }
        }

        private bool TryHomelandFarmInvokeCropAddManureInterop(List<uint> cropNetIds, out string status)
        {
            status = "AddManure interop unavailable.";
            if (cropNetIds == null || cropNetIds.Count == 0)
            {
                status = "Crop list empty.";
                return false;
            }

            this.TryEnsureHomelandFarmInteropAssembliesLoaded();
            if (this.homelandFarmCropAddManureInteropMethod == null)
            {
                Type cropProtocolType = this.ResolveHomelandFarmManagedType(
                    "CropProtocolManager",
                    "XDTDataAndProtocol.ProtocolService.Plant.CropProtocolManager");
                if (cropProtocolType != null)
                {
                    this.homelandFarmCropAddManureInteropMethod = this.ResolveHomelandFarmListOnlyStaticMethod(cropProtocolType, "AddManure");
                }

                if (this.homelandFarmCropAddManureInteropMethod == null)
                {
                    status = "AddManure interop method missing (CropProtocolManager="
                        + (cropProtocolType != null) + ").";
                    return false;
                }
            }

            try
            {
                Type listParamType = this.homelandFarmCropAddManureInteropMethod.GetParameters()[0].ParameterType;
                object listArg = this.CreateHomelandFarmUintList(cropNetIds, listParamType);
                this.homelandFarmCropAddManureInteropMethod.Invoke(null, new[] { listArg });
                status = "AddManure interop ok count=" + cropNetIds.Count + ".";
                this.HomelandFarmLog(status);
                return true;
            }
            catch (Exception ex)
            {
                status = "AddManure interop exception: " + (ex.InnerException ?? ex).Message;
                this.HomelandFarmLog(status);
                return false;
            }
        }

        private bool TryHomelandFarmEnsureNetworkCommandTypes(out string status)
        {
            status = string.Empty;
            if (this.homelandFarmNetworkCommandTypesResolved)
            {
                status = "Manured=" + (this.homelandFarmManuredNetworkCommandType != null)
                    + " AddHolder=" + (this.homelandFarmAddHolderSystemCommandType != null)
                    + " SendCommand=" + (this.homelandFarmSendCommandMethodDef != null);
                return this.homelandFarmManuredNetworkCommandType != null
                    && this.homelandFarmSendCommandMethodDef != null;
            }

            this.TryEnsureHomelandFarmInteropAssembliesLoaded();
            if (this.homelandFarmManuredNetworkCommandType == null)
            {
                this.homelandFarmManuredNetworkCommandType = this.ResolveHomelandFarmManagedType(
                    "ManuredNetworkCommand",
                    "XDT.Scene.Shared.Modules.Farm.ManuredNetworkCommand",
                    "EcsClient.XDT.Scene.Shared.Modules.Farm.ManuredNetworkCommand");
            }

            if (this.homelandFarmAddHolderSystemCommandType == null)
            {
                this.homelandFarmAddHolderSystemCommandType = this.ResolveHomelandFarmManagedType(
                    "AddHolderSystemCommand",
                    "EcsClient.XDT.Scene.Shared.Modules.Tools.AddHolderSystemCommand",
                    "XDT.Scene.Shared.Modules.Tools.AddHolderSystemCommand");
            }

            if (this.homelandFarmEHolderSystemType == null)
            {
                this.homelandFarmEHolderSystemType = this.ResolveHomelandFarmManagedType(
                    "EHolderSystem",
                    "EcsClient.XDT.Scene.Shared.Modules.Tools.EHolderSystem",
                    "XDT.Scene.Shared.Modules.Tools.EHolderSystem");
            }

            this.EnsureHomelandFarmSendCommandResolver();
            this.homelandFarmNetworkCommandTypesResolved = true;
            status = "Manured=" + (this.homelandFarmManuredNetworkCommandType != null)
                + " AddHolder=" + (this.homelandFarmAddHolderSystemCommandType != null)
                + " EHolderSystem=" + (this.homelandFarmEHolderSystemType != null)
                + " SendCommand=" + (this.homelandFarmSendCommandMethodDef != null);
            return this.homelandFarmManuredNetworkCommandType != null
                && this.homelandFarmSendCommandMethodDef != null;
        }

        private bool TryHomelandFarmSendAddHolderSystemCommand(uint itemNetId, out string status)
        {
            status = "AddHolder SendCommand unavailable.";
            if (itemNetId == 0U)
            {
                status = "Handhold netId missing.";
                return false;
            }

            if (this.homelandFarmAddHolderSystemCommandType == null)
            {
                this.TryHomelandFarmEnsureNetworkCommandTypes(out _);
            }

            if (this.homelandFarmAddHolderSystemCommandType == null)
            {
                status = "AddHolderSystemCommand type missing.";
                return false;
            }

            object holdItemSystem = 4;
            if (this.homelandFarmEHolderSystemType != null && this.homelandFarmEHolderSystemType.IsEnum)
            {
                try
                {
                    holdItemSystem = Enum.Parse(this.homelandFarmEHolderSystemType, "HoldItem");
                }
                catch
                {
                    holdItemSystem = 4;
                }
            }

            bool sent = this.TryHomelandFarmSendCommand(
                this.homelandFarmAddHolderSystemCommandType,
                command =>
                {
                    object cmd = command;
                    bool ok = this.TrySetFieldValue(this.homelandFarmAddHolderSystemCommandType, ref cmd, "NetId", itemNetId);
                    ok = this.TrySetFieldValue(this.homelandFarmAddHolderSystemCommandType, ref cmd, "System", holdItemSystem) || ok;
                    return ok;
                },
                out status);
            if (sent)
            {
                status = "AddHolder SendCommand ok netId=" + itemNetId + ".";
                this.HomelandFarmLog(status);
            }

            return sent;
        }

        private bool TryHomelandFarmTryGetEquippedHandholdBagNetId(out uint bagNetId, out int staticId)
        {
            bagNetId = 0U;
            staticId = 0;
            try
            {
                this.EnsureHomelandFarmScannerTypes();
                object playerObj = null;
                if (this.homelandFarmEntityUtilGetSelfPlayerMethod != null)
                {
                    playerObj = this.homelandFarmEntityUtilGetSelfPlayerMethod.Invoke(null, null);
                }

                if (playerObj == null && this.TryGetManagedSelfPlayerObject(out object managedPlayer, out _))
                {
                    playerObj = managedPlayer;
                }

                if (playerObj != null
                    && this.TryGetObjectMember(playerObj, "equipComponent", out object equipObj)
                    && equipObj != null
                    && this.TryGetObjectMember(equipObj, "handhold", out object handholdObj)
                    && handholdObj != null)
                {
                    this.TryGetUIntMember(handholdObj, "netId", out bagNetId);
                    if (!this.TryGetManagedInt32Member(handholdObj, "staticId", out staticId))
                    {
                        this.TryGetManagedInt32Member(handholdObj, "StaticId", out staticId);
                    }

                    if (bagNetId != 0U)
                    {
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private IEnumerator HomelandFarmWaitForEquippedHandhold(uint expectedNetId, int expectedStaticId, float timeoutSeconds)
        {
            float deadline = Time.realtimeSinceStartup + Math.Max(0.5f, timeoutSeconds);
            while (Time.realtimeSinceStartup < deadline)
            {
                if (this.TryHomelandFarmTryGetEquippedHandholdBagNetId(out uint equippedNetId, out int equippedStaticId))
                {
                    if (expectedNetId != 0U && equippedNetId == expectedNetId)
                    {
                        this.HomelandFarmLog("Handhold equipped netId=" + equippedNetId + " staticId=" + equippedStaticId);
                        yield break;
                    }

                    if (expectedStaticId > 0 && equippedStaticId == expectedStaticId)
                    {
                        this.HomelandFarmLog("Handhold equipped staticId=" + equippedStaticId + " netId=" + equippedNetId);
                        yield break;
                    }
                }

                yield return new WaitForSecondsRealtime(0.1f);
            }

            this.HomelandFarmLog("Handhold equip wait timed out expectedNetId=" + expectedNetId + " staticId=" + expectedStaticId);
        }

        private bool TryHomelandFarmEnsureToolEquipAuraMethods()
        {
            if (this.homelandFarmAuraToolProtocolSetHandHoldMethod != IntPtr.Zero
                || this.homelandFarmAuraToolSystemSetHandholdMethod != IntPtr.Zero)
            {
                return true;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr toolProtocolClass = this.FindAuraMonoClassByFullName("ToolProtocolManager");
            if (toolProtocolClass == IntPtr.Zero)
            {
                toolProtocolClass = this.FindHomelandFarmAuraClass(
                    "ToolProtocolManager",
                    "XDTDataAndProtocol",
                    "ToolProtocolManager");
            }

            if (toolProtocolClass != IntPtr.Zero && this.homelandFarmAuraToolProtocolSetHandHoldMethod == IntPtr.Zero)
            {
                this.homelandFarmAuraToolProtocolSetHandHoldMethod = this.FindAuraMonoMethodOnHierarchy(
                    toolProtocolClass,
                    "SetHandHold",
                    2);
            }

            IntPtr toolSystemClass = this.FindAuraMonoClassByFullName("XDTGameSystem.GameplaySystem.Tool.ToolSystem");
            if (toolSystemClass == IntPtr.Zero)
            {
                toolSystemClass = this.FindHomelandFarmAuraClass(
                    "XDTGameSystem.GameplaySystem.Tool.ToolSystem",
                    "XDTGameSystem.GameplaySystem.Tool",
                    "ToolSystem");
            }

            if (toolSystemClass != IntPtr.Zero)
            {
                if (this.homelandFarmAuraToolSystemInstanceGetterMethod == IntPtr.Zero)
                {
                    this.homelandFarmAuraToolSystemInstanceGetterMethod = this.FindAuraMonoMethodOnHierarchy(
                        toolSystemClass,
                        "get_Instance",
                        0);
                }

                if (this.homelandFarmAuraToolSystemSetHandholdMethod == IntPtr.Zero)
                {
                    this.homelandFarmAuraToolSystemSetHandholdMethod = this.FindAuraMonoMethodOnHierarchy(
                        toolSystemClass,
                        "SetHandhold",
                        1);
                }
            }

            return this.homelandFarmAuraToolProtocolSetHandHoldMethod != IntPtr.Zero
                || this.homelandFarmAuraToolSystemSetHandholdMethod != IntPtr.Zero;
        }

        private bool TryHomelandFarmEnsureToolEquipTypes()
        {
            if (this.homelandFarmToolEquipTypesResolved)
            {
                return this.HomelandFarmHasToolEquipPathAvailable();
            }

            this.TryEnsureHomelandFarmInteropAssembliesLoaded();

            this.homelandFarmHoldToolCommandType = this.ResolveHomelandFarmManagedType(
                "HoldToolCommand",
                "EcsClient.XDT.Scene.Shared.Modules.Tools.HoldToolCommand",
                "XDT.Scene.Shared.Modules.Tools.HoldToolCommand",
                "Il2CppEcsClient.XDT.Scene.Shared.Modules.Tools.HoldToolCommand");
            this.homelandFarmCancelHolderSystemCommandType = this.ResolveHomelandFarmManagedType(
                "CancelHolderSystemCommand",
                "EcsClient.XDT.Scene.Shared.Modules.Tools.CancelHolderSystemCommand",
                "XDT.Scene.Shared.Modules.Tools.CancelHolderSystemCommand",
                "Il2CppEcsClient.XDT.Scene.Shared.Modules.Tools.CancelHolderSystemCommand");

            this.homelandFarmToolProtocolManagerType = this.ResolveHomelandFarmManagedType(
                "ToolProtocolManager",
                "ToolProtocolManager",
                "XDTDataAndProtocol.ToolProtocolManager");

            if (this.homelandFarmToolProtocolManagerType != null)
            {
                this.homelandFarmToolProtocolSetHandHoldMethod = this.homelandFarmToolProtocolManagerType.GetMethod(
                    "SetHandHold",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(int), typeof(int) },
                    null);
            }

            this.homelandFarmToolSystemType = this.ResolveHomelandFarmManagedType(
                "ToolSystem",
                "XDTGameSystem.GameplaySystem.Tool.ToolSystem",
                "Il2CppXDTGameSystem.GameplaySystem.Tool.ToolSystem");

            if (this.homelandFarmToolSystemType != null)
            {
                this.homelandFarmToolSystemSetHandholdMethod = this.homelandFarmToolSystemType.GetMethod(
                    "SetHandhold",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new[] { typeof(int) },
                    null);
                this.homelandFarmToolSystemGetToolMethod = this.homelandFarmToolSystemType.GetMethod(
                    "GetTool",
                    BindingFlags.Public | BindingFlags.Instance,
                    null,
                    new[] { typeof(int) },
                    null);

                Type dataModuleGenericType = null;
                foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        dataModuleGenericType = assembly.GetType("XDTGame.Core.DataModule`1", false)
                            ?? assembly.GetType("XDFramework.Core.DataModule`1", false);
                        if (dataModuleGenericType != null)
                        {
                            break;
                        }
                    }
                    catch
                    {
                    }
                }

                if (dataModuleGenericType != null)
                {
                    this.homelandFarmToolDataModuleType = dataModuleGenericType.MakeGenericType(this.homelandFarmToolSystemType);
                    this.homelandFarmToolDataModuleInstanceProperty = this.homelandFarmToolDataModuleType.GetProperty(
                        "Instance",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                }
            }

            this.EnsureHomelandFarmSendCommandResolver();
            this.TryHomelandFarmEnsureToolEquipAuraMethods();

            bool available = this.HomelandFarmHasToolEquipPathAvailable();
            if (available)
            {
                this.homelandFarmToolEquipTypesResolved = true;
            }
            else
            {
                this.HomelandFarmLog(
                    "Tool equip paths unresolved holdTool=" + (this.homelandFarmHoldToolCommandType != null)
                    + " setHandHold=" + (this.homelandFarmToolProtocolSetHandHoldMethod != null)
                    + " toolSystem=" + (this.homelandFarmToolSystemSetHandholdMethod != null)
                    + " sendCommand=" + (this.homelandFarmSendCommandMethodDef != null)
                    + " auraSetHandHold=0x" + this.homelandFarmAuraToolProtocolSetHandHoldMethod.ToInt64().ToString("X")
                    + " auraToolSystem=0x" + this.homelandFarmAuraToolSystemSetHandholdMethod.ToInt64().ToString("X"));
            }

            return available;
        }

        private bool HomelandFarmHasToolEquipPathAvailable()
        {
            return this.homelandFarmHoldToolCommandType != null
                || this.homelandFarmCancelHolderSystemCommandType != null
                || this.homelandFarmToolProtocolSetHandHoldMethod != null
                || this.homelandFarmToolSystemSetHandholdMethod != null
                || this.homelandFarmAuraToolProtocolSetHandHoldMethod != IntPtr.Zero
                || this.homelandFarmAuraToolSystemSetHandholdMethod != IntPtr.Zero;
        }

        private bool TryHomelandFarmTryGetToolSystemInstance(out object toolSystemInstance)
        {
            toolSystemInstance = null;
            if (!this.TryHomelandFarmEnsureToolEquipTypes())
            {
                return false;
            }

            try
            {
                if (this.homelandFarmToolDataModuleInstanceProperty != null)
                {
                    toolSystemInstance = this.homelandFarmToolDataModuleInstanceProperty.GetValue(null, null);
                    if (toolSystemInstance != null)
                    {
                        return true;
                    }
                }

                PropertyInfo directInstance = this.homelandFarmToolSystemType?.GetProperty(
                    "Instance",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                if (directInstance != null)
                {
                    toolSystemInstance = directInstance.GetValue(null, null);
                }
            }
            catch
            {
            }

            return toolSystemInstance != null;
        }

        private bool TryHomelandFarmTryResolveToolSkinId(int toolId, out int skinId)
        {
            skinId = 0;
            if (toolId <= 0)
            {
                return false;
            }

            if (!this.TryHomelandFarmTryGetToolSystemInstance(out object toolSystemInstance)
                || toolSystemInstance == null
                || this.homelandFarmToolSystemGetToolMethod == null)
            {
                return false;
            }

            try
            {
                object tool = this.homelandFarmToolSystemGetToolMethod.Invoke(toolSystemInstance, new object[] { toolId });
                if (tool == null)
                {
                    return false;
                }

                if (this.TryGetManagedInt32Member(tool, "skinId", out skinId) && skinId > 0)
                {
                    return true;
                }

                return this.TryGetManagedInt32Member(tool, "SkinId", out skinId) && skinId > 0;
            }
            catch
            {
                return false;
            }
        }

        private bool TryHomelandFarmSendHoldToolCommand(int toolId, int skinId, out string status)
        {
            status = "HoldTool SendCommand unavailable.";
            if (toolId <= 0)
            {
                status = "Tool id missing.";
                return false;
            }

            if (this.homelandFarmToolProtocolSetHandHoldMethod != null)
            {
                try
                {
                    this.homelandFarmToolProtocolSetHandHoldMethod.Invoke(null, new object[] { toolId, skinId });
                    status = "ToolProtocolManager.SetHandHold ok toolId=" + toolId + " skinId=" + skinId + ".";
                    this.HomelandFarmLog(status);
                    return true;
                }
                catch (Exception ex)
                {
                    status = "ToolProtocolManager.SetHandHold exception: " + (ex.InnerException ?? ex).Message;
                }
            }

            if (this.homelandFarmHoldToolCommandType == null)
            {
                this.TryHomelandFarmEnsureToolEquipTypes();
            }

            if (this.homelandFarmHoldToolCommandType == null)
            {
                return false;
            }

            bool sent = this.TryHomelandFarmSendCommand(
                this.homelandFarmHoldToolCommandType,
                command =>
                {
                    object cmd = command;
                    bool ok = this.TrySetFieldValue(this.homelandFarmHoldToolCommandType, ref cmd, "ToolId", toolId);
                    ok = this.TrySetFieldValue(this.homelandFarmHoldToolCommandType, ref cmd, "ToolSkinId", skinId) || ok;
                    return ok;
                },
                out status);
            if (sent)
            {
                status = "HoldTool SendCommand ok toolId=" + toolId + " skinId=" + skinId + ".";
                this.HomelandFarmLog(status);
            }

            return sent;
        }

        private bool TryHomelandFarmSendCancelHandToolCommand(out string status)
        {
            status = "Cancel HoldTool SendCommand unavailable.";
            if (this.homelandFarmToolProtocolSetHandHoldMethod != null)
            {
                try
                {
                    this.homelandFarmToolProtocolSetHandHoldMethod.Invoke(null, new object[] { 0, 0 });
                    status = "ToolProtocolManager.SetHandHold(0) ok.";
                    this.HomelandFarmLog(status);
                    return true;
                }
                catch (Exception ex)
                {
                    status = "ToolProtocolManager.SetHandHold(0) exception: " + (ex.InnerException ?? ex).Message;
                }
            }

            if (this.TryHomelandFarmInvokeAuraToolProtocolCancelHandHold(out string auraProtocolStatus))
            {
                status = auraProtocolStatus;
                return true;
            }

            if (this.homelandFarmCancelHolderSystemCommandType == null)
            {
                this.TryHomelandFarmEnsureToolEquipTypes();
            }

            if (this.homelandFarmCancelHolderSystemCommandType == null)
            {
                return false;
            }

            bool sent = this.TryHomelandFarmSendCommand(
                this.homelandFarmCancelHolderSystemCommandType,
                command =>
                {
                    object cmd = command;
                    return this.TrySetFieldValue(this.homelandFarmCancelHolderSystemCommandType, ref cmd, "System", HomelandFarmHolderSystemHoldTool);
                },
                out status);
            if (sent)
            {
                status = "CancelHolderSystemCommand ok.";
                this.HomelandFarmLog(status);
            }

            return sent;
        }

        private unsafe bool TryHomelandFarmInvokeAuraToolSystemSetHandhold(int toolId, out string status)
        {
            status = "Aura ToolSystem.SetHandhold unavailable.";
            if (toolId < 0)
            {
                status = "Tool id missing.";
                return false;
            }

            if (!this.TryHomelandFarmEnsureToolEquipAuraMethods()
                || this.homelandFarmAuraToolSystemSetHandholdMethod == IntPtr.Zero
                || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr toolSystemObj = IntPtr.Zero;
            if (this.homelandFarmAuraToolSystemInstanceGetterMethod != IntPtr.Zero)
            {
                IntPtr exc = IntPtr.Zero;
                toolSystemObj = auraMonoRuntimeInvoke(
                    this.homelandFarmAuraToolSystemInstanceGetterMethod,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    ref exc);
                if (exc != IntPtr.Zero)
                {
                    status = "Aura ToolSystem.Instance failed exc=0x" + exc.ToInt64().ToString("X") + ".";
                    return false;
                }
            }

            if (toolSystemObj == IntPtr.Zero)
            {
                status = "Aura ToolSystem.Instance unavailable.";
                return false;
            }

            IntPtr invokeExc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&toolId);
            auraMonoRuntimeInvoke(
                this.homelandFarmAuraToolSystemSetHandholdMethod,
                toolSystemObj,
                (IntPtr)args,
                ref invokeExc);
            if (invokeExc != IntPtr.Zero)
            {
                status = "Aura ToolSystem.SetHandhold failed exc=0x" + invokeExc.ToInt64().ToString("X") + ".";
                return false;
            }

            status = "Aura ToolSystem.SetHandhold ok toolId=" + toolId + ".";
            this.HomelandFarmLog(status);
            return true;
        }

        private unsafe bool TryHomelandFarmInvokeAuraToolProtocolSetHandHold(int toolId, int skinId, out string status)
        {
            status = "Aura ToolProtocolManager.SetHandHold unavailable.";
            if (toolId <= 0)
            {
                status = "Tool id missing.";
                return false;
            }

            if (!this.TryHomelandFarmEnsureToolEquipAuraMethods()
                || this.homelandFarmAuraToolProtocolSetHandHoldMethod == IntPtr.Zero
                || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[2];
            args[0] = (IntPtr)(&toolId);
            args[1] = (IntPtr)(&skinId);
            auraMonoRuntimeInvoke(this.homelandFarmAuraToolProtocolSetHandHoldMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                status = "Aura ToolProtocolManager.SetHandHold failed exc=0x" + exc.ToInt64().ToString("X") + ".";
                return false;
            }

            status = "Aura ToolProtocolManager.SetHandHold ok toolId=" + toolId + " skinId=" + skinId + ".";
            this.HomelandFarmLog(status);
            return true;
        }

        private unsafe bool TryHomelandFarmInvokeAuraToolProtocolCancelHandHold(out string status)
        {
            status = "Aura ToolProtocolManager.SetHandHold(0) unavailable.";
            if (!this.TryHomelandFarmEnsureToolEquipAuraMethods()
                || this.homelandFarmAuraToolProtocolSetHandHoldMethod == IntPtr.Zero
                || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            int toolId = 0;
            int skinId = 0;
            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[2];
            args[0] = (IntPtr)(&toolId);
            args[1] = (IntPtr)(&skinId);
            auraMonoRuntimeInvoke(this.homelandFarmAuraToolProtocolSetHandHoldMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                status = "Aura ToolProtocolManager.SetHandHold(0) failed exc=0x" + exc.ToInt64().ToString("X") + ".";
                return false;
            }

            status = "Aura ToolProtocolManager.SetHandHold(0) ok.";
            this.HomelandFarmLog(status);
            return true;
        }

        private bool TryHomelandFarmEquipHandTool(int toolId, out string status)
        {
            status = "Tool equip unavailable.";
            if (toolId <= 0)
            {
                status = "Tool id missing.";
                return false;
            }

            if (!this.TryHomelandFarmEnsureToolEquipTypes())
            {
                return false;
            }

            this.TryHomelandFarmTryResolveToolSkinId(toolId, out int skinId);

            if (this.TryHomelandFarmInvokeAuraToolSystemSetHandhold(toolId, out string auraToolSystemStatus))
            {
                status = auraToolSystemStatus;
                return true;
            }

            if (this.TryHomelandFarmTryGetToolSystemInstance(out object toolSystemInstance)
                && toolSystemInstance != null
                && this.homelandFarmToolSystemSetHandholdMethod != null)
            {
                try
                {
                    this.homelandFarmToolSystemSetHandholdMethod.Invoke(toolSystemInstance, new object[] { toolId });
                    status = "ToolSystem.SetHandhold ok toolId=" + toolId + ".";
                    this.HomelandFarmLog(status);
                    return true;
                }
                catch (Exception ex)
                {
                    status = "ToolSystem.SetHandhold exception: " + (ex.InnerException ?? ex).Message;
                }
            }

            if (this.TryHomelandFarmInvokeAuraToolProtocolSetHandHold(toolId, skinId, out string auraProtocolStatus))
            {
                status = auraProtocolStatus;
                return true;
            }

            return this.TryHomelandFarmSendHoldToolCommand(toolId, skinId, out status);
        }

        private bool TryHomelandFarmUnequipHandTool(out string status)
        {
            status = "Unequip hand tool unavailable.";
            if (!this.TryHomelandFarmEnsureToolEquipTypes())
            {
                return false;
            }

            if (this.TryHomelandFarmInvokeAuraToolSystemSetHandhold(0, out string auraToolSystemStatus))
            {
                status = auraToolSystemStatus;
                return true;
            }

            if (this.TryHomelandFarmTryGetToolSystemInstance(out object toolSystemInstance)
                && toolSystemInstance != null
                && this.homelandFarmToolSystemSetHandholdMethod != null)
            {
                try
                {
                    this.homelandFarmToolSystemSetHandholdMethod.Invoke(toolSystemInstance, new object[] { 0 });
                    status = "ToolSystem.SetHandhold(0) ok.";
                    this.HomelandFarmLog(status);
                    return true;
                }
                catch (Exception ex)
                {
                    status = "ToolSystem.SetHandhold(0) exception: " + (ex.InnerException ?? ex).Message;
                }
            }

            if (this.TryHomelandFarmInvokeAuraToolProtocolCancelHandHold(out string auraProtocolStatus))
            {
                status = auraProtocolStatus;
                return true;
            }

            return this.TryHomelandFarmSendCancelHandToolCommand(out status);
        }

        public bool TryEquipHandTool(int toolId, out string status)
        {
            if (toolId == 0)
            {
                return this.TryHomelandFarmUnequipHandTool(out status);
            }

            return this.TryHomelandFarmEquipHandTool(toolId, out status);
        }

        public void EquipHandTool(int toolId)
        {
            this.TryEquipHandTool(toolId, out _);
        }

        private bool TryIsHandToolEquipped(int toolId)
        {
            if (toolId <= 0)
            {
                return false;
            }

            if (this.TryGetCurrentToolInfo(out int currentToolId, out _, out _) && currentToolId == toolId)
            {
                return true;
            }

            switch (toolId)
            {
                case HomelandFarmSprinklerToolTypeId:
                    return this.TryHomelandFarmTryIsHandHoldSprinklerEquipped();
                case HomelandFarmNetToolTypeId:
                    return this.TryGetInsectNetToolStatus(out bool netEquipped, out _) && netEquipped;
                case HomelandFarmRodToolTypeId:
                    return this.TryGetFishingRodToolStatus(out bool rodEquipped, out _) && rodEquipped;
                case HomelandFarmBirdScannerToolTypeId:
                    return this.TryGetBirdScannerToolStatus(out bool scannerEquipped, out _) && scannerEquipped;
                default:
                    return false;
            }
        }

        public bool TryToggleEquipHandToolHotkey(int toolId, out bool unequipped, out string status)
        {
            unequipped = false;
            status = string.Empty;
            if (toolId <= 0)
            {
                status = "Invalid tool id.";
                return false;
            }

            if (this.TryIsHandToolEquipped(toolId))
            {
                unequipped = true;
                return this.TryEquipHandTool(0, out status);
            }

            return this.TryEquipHandTool(toolId, out status);
        }

        private bool TryHomelandFarmEquipHandhold(uint itemNetId, out string status)
        {
            status = "EquipHandhold unavailable.";
            if (itemNetId == 0U)
            {
                status = "Handhold netId missing.";
                return false;
            }

            if (this.TryHomelandFarmSendAddHolderSystemCommand(itemNetId, out string sendStatus))
            {
                status = sendStatus;
                return true;
            }

            if (this.homelandFarmCharacterEquipHandholdMethod != null)
            {
                try
                {
                    this.homelandFarmCharacterEquipHandholdMethod.Invoke(null, new object[] { itemNetId });
                    status = "EquipHandhold sent netId=" + itemNetId + ".";
                    this.HomelandFarmLog(status);
                    return true;
                }
                catch (Exception ex)
                {
                    status = (ex.InnerException ?? ex).Message;
                }
            }

            if (this.TryHomelandFarmInvokeEquipHandholdAura(itemNetId, out string auraStatus))
            {
                status = auraStatus;
                return true;
            }

            if (!string.IsNullOrEmpty(sendStatus))
            {
                status = sendStatus;
            }

            return false;
        }

        private unsafe bool TryHomelandFarmInvokeEquipHandholdAura(uint itemNetId, out string status)
        {
            status = "Aura EquipHandhold unavailable.";
            if (itemNetId == 0U)
            {
                status = "Handhold netId missing.";
                return false;
            }

            if (!this.TryResolveHomelandFarmAuraProtocol(out status))
            {
                return false;
            }

            if (this.homelandFarmAuraCharacterEquipHandholdMethod == IntPtr.Zero)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&itemNetId);
            auraMonoRuntimeInvoke(this.homelandFarmAuraCharacterEquipHandholdMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                status = "Aura EquipHandhold failed exc=0x" + exc.ToInt64().ToString("X") + ".";
                return false;
            }

            status = "Aura EquipHandhold sent netId=" + itemNetId + ".";
            this.HomelandFarmLog(status);
            return true;
        }

        private bool TryHomelandFarmSow(uint seedNetId, List<object> plantPoints, out string status)
        {
            status = "Sow unavailable.";
            plantPoints = plantPoints ?? new List<object>(0);
            if (seedNetId == 0U || !this.EnsureHomelandFarmReflectionReady())
            {
                status = seedNetId == 0U ? "Seed netId missing." : this.homelandFarmReflectionUnavailableStatus;
                return false;
            }

            if (plantPoints.Count == 0)
            {
                status = "No plant points to sow.";
                return false;
            }

            this.TryEnsureHomelandFarmInteropAssembliesLoaded();
            if (this.TryHomelandFarmInvokeCropSeedingInterop(seedNetId, plantPoints, out status))
            {
                return true;
            }

            // Native-only build: managed CropPlantPoint type is unavailable, so points are data carriers.
            if (plantPoints[0] is HomelandFarmCropPlantPointData)
            {
                return this.TryHomelandFarmSowNative(seedNetId, plantPoints, out status);
            }

            if (!this.EnsureHomelandFarmSowManagedReflection() || this.homelandFarmCropSeedingMethod == null)
            {
                status = "Sow needs managed CropSeeding, unavailable in this build.";
                this.HomelandFarmLog(status);
                return false;
            }

            try
            {
                ParameterInfo[] parameters = this.homelandFarmCropSeedingMethod.GetParameters();
                Type listType = parameters[1].ParameterType;
                object pointsList = Activator.CreateInstance(listType);
                MethodInfo addMethod = listType.GetMethod("Add", BindingFlags.Instance | BindingFlags.Public);
                if (addMethod == null)
                {
                    status = "CropPlantPoint list Add unavailable.";
                    return false;
                }

                for (int i = 0; i < plantPoints.Count; i++)
                {
                    object point = this.TryHomelandFarmMaterializeCropPlantPoint(plantPoints[i]);
                    if (point == null)
                    {
                        status = "CropPlantPoint materialize failed at index " + i + ".";
                        return false;
                    }

                    addMethod.Invoke(pointsList, new object[] { point });
                }

                this.homelandFarmCropSeedingMethod.Invoke(null, new object[] { seedNetId, pointsList });
                status = "Sow sent for " + plantPoints.Count + " point(s).";
                return true;
            }
            catch (Exception ex)
            {
                status = "Sow exception: " + (ex.InnerException ?? ex).Message;
                return false;
            }
        }

        private unsafe bool TryHomelandFarmResolveAuraCropPlantPointMembers(out string status)
        {
            status = string.Empty;
            if (this.homelandFarmAuraCropPlantPointFieldsResolved
                && this.homelandFarmAuraCropPlantPointClass != IntPtr.Zero)
            {
                return true;
            }

            if (auraMonoClassGetFieldFromName == null)
            {
                status = "AuraMono field API unavailable.";
                return false;
            }

            if (this.homelandFarmAuraCropPlantPointClass == IntPtr.Zero)
            {
                this.homelandFarmAuraCropPlantPointClass = this.FindAuraMonoClassByFullName("XDT.Scene.Shared.Modules.Farm.CropPlantPoint");
                if (this.homelandFarmAuraCropPlantPointClass == IntPtr.Zero)
                {
                    this.homelandFarmAuraCropPlantPointClass = this.FindAuraMonoClassByFullName("EcsClient.XDT.Scene.Shared.Modules.Farm.CropPlantPoint");
                }
            }

            if (this.homelandFarmAuraCropPlantPointClass == IntPtr.Zero)
            {
                status = "AuraMono CropPlantPoint class missing.";
                return false;
            }

            IntPtr cls = this.homelandFarmAuraCropPlantPointClass;
            this.homelandFarmAuraCropPlantPointPosField = auraMonoClassGetFieldFromName(cls, "pos");
            if (this.homelandFarmAuraCropPlantPointPosField == IntPtr.Zero)
            {
                this.homelandFarmAuraCropPlantPointPosField = auraMonoClassGetFieldFromName(cls, "Pos");
            }

            this.homelandFarmAuraCropPlantPointAngleField = auraMonoClassGetFieldFromName(cls, "angle");
            if (this.homelandFarmAuraCropPlantPointAngleField == IntPtr.Zero)
            {
                this.homelandFarmAuraCropPlantPointAngleField = auraMonoClassGetFieldFromName(cls, "Angle");
            }

            this.homelandFarmAuraCropPlantPointNetIdField = auraMonoClassGetFieldFromName(cls, "levelObjectNetId");
            if (this.homelandFarmAuraCropPlantPointNetIdField == IntPtr.Zero)
            {
                this.homelandFarmAuraCropPlantPointNetIdField = auraMonoClassGetFieldFromName(cls, "LevelObjectNetId");
            }

            this.homelandFarmAuraCropPlantPointFieldsResolved = true;
            if (this.homelandFarmAuraCropPlantPointPosField == IntPtr.Zero
                || this.homelandFarmAuraCropPlantPointNetIdField == IntPtr.Zero)
            {
                status = "AuraMono CropPlantPoint fields missing pos=" + (this.homelandFarmAuraCropPlantPointPosField != IntPtr.Zero)
                    + " angle=" + (this.homelandFarmAuraCropPlantPointAngleField != IntPtr.Zero)
                    + " levelObjectNetId=" + (this.homelandFarmAuraCropPlantPointNetIdField != IntPtr.Zero) + ".";
                return false;
            }

            return true;
        }

        // Native sow: builds a Mono List<CropPlantPoint> by constructing each struct via object_new +
        // field-setters (no manual struct-layout math), then invokes the native CropSeeding(uint, List).
        private unsafe bool TryHomelandFarmSowNative(uint seedNetId, List<object> plantPoints, out string status)
        {
            status = "Native sow unavailable.";

            if (!this.TryResolveHomelandFarmAuraProtocol(out string protocolStatus))
            {
                status = "Native sow protocol unavailable: " + protocolStatus;
                return false;
            }

            if (this.homelandFarmAuraCropSeedingMethod == IntPtr.Zero)
            {
                status = "Native CropSeeding method unresolved.";
                return false;
            }

            if (!this.EnsureAuraMonoApiReady()
                || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null
                || auraMonoObjectNew == null
                || auraMonoFieldSetValue == null
                || auraMonoObjectGetClass == null
                || auraMonoStringNew == null
                || this.auraMonoTypeGetTypeMethodPtr == IntPtr.Zero
                || this.auraMonoActivatorCreateInstanceMethodPtr == IntPtr.Zero)
            {
                status = "Native sow prerequisites unavailable.";
                return false;
            }

            if (!this.TryHomelandFarmResolveAuraCropPlantPointMembers(out string memberStatus))
            {
                status = memberStatus;
                this.HomelandFarmLog(status);
                return false;
            }

            // Build List<CropPlantPoint> via Type.GetType + Activator.CreateInstance.
            IntPtr listObj = IntPtr.Zero;
            string[] listTypeCandidates = new[]
            {
                "System.Collections.Generic.List`1[[XDT.Scene.Shared.Modules.Farm.CropPlantPoint, EcsClient]]",
                "System.Collections.Generic.List`1[[XDT.Scene.Shared.Modules.Farm.CropPlantPoint, Client]]",
                "System.Collections.Generic.List`1[[EcsClient.XDT.Scene.Shared.Modules.Farm.CropPlantPoint, EcsClient]]"
            };

            IntPtr* typeArgs = stackalloc IntPtr[1];
            IntPtr* createArgs = stackalloc IntPtr[1];
            for (int i = 0; i < listTypeCandidates.Length && listObj == IntPtr.Zero; i++)
            {
                IntPtr typeNameObj = auraMonoStringNew(this.auraMonoRootDomain, listTypeCandidates[i]);
                if (typeNameObj == IntPtr.Zero)
                {
                    continue;
                }

                typeArgs[0] = typeNameObj;
                IntPtr exc = IntPtr.Zero;
                IntPtr typeObj = auraMonoRuntimeInvoke(this.auraMonoTypeGetTypeMethodPtr, IntPtr.Zero, (IntPtr)typeArgs, ref exc);
                if (exc != IntPtr.Zero || typeObj == IntPtr.Zero)
                {
                    continue;
                }

                createArgs[0] = typeObj;
                exc = IntPtr.Zero;
                listObj = auraMonoRuntimeInvoke(this.auraMonoActivatorCreateInstanceMethodPtr, IntPtr.Zero, (IntPtr)createArgs, ref exc);
                if (exc != IntPtr.Zero)
                {
                    listObj = IntPtr.Zero;
                }
            }

            if (listObj == IntPtr.Zero)
            {
                status = "Native List<CropPlantPoint> create failed.";
                this.HomelandFarmLog(status);
                return false;
            }

            IntPtr listClass = auraMonoObjectGetClass(listObj);
            if (this.homelandFarmAuraCropPlantPointListClass == IntPtr.Zero)
            {
                this.homelandFarmAuraCropPlantPointListClass = listClass;
            }

            IntPtr addMethod = this.homelandFarmAuraCropPlantPointListAddMethod;
            if (addMethod == IntPtr.Zero && listClass != IntPtr.Zero)
            {
                addMethod = this.FindAuraMonoMethodOnHierarchy(listClass, "Add", 1);
                this.homelandFarmAuraCropPlantPointListAddMethod = addMethod;
            }

            if (addMethod == IntPtr.Zero)
            {
                status = "Native List<CropPlantPoint>.Add missing.";
                return false;
            }

            bool pointIsValueType = auraMonoClassIsValueType != null
                && auraMonoClassIsValueType(this.homelandFarmAuraCropPlantPointClass) != 0;

            int added = 0;
            IntPtr* addArgs = stackalloc IntPtr[1];
            for (int i = 0; i < plantPoints.Count; i++)
            {
                if (!(plantPoints[i] is HomelandFarmCropPlantPointData data))
                {
                    continue;
                }

                IntPtr pointObj = auraMonoObjectNew(this.auraMonoRootDomain, this.homelandFarmAuraCropPlantPointClass);
                if (pointObj == IntPtr.Zero)
                {
                    status = "CropPlantPoint native alloc failed.";
                    return false;
                }

                Vector3 pos = data.Pos;
                int angle = data.Angle;
                ulong levelObjectNetId = data.LevelObjectNetId;
                auraMonoFieldSetValue(pointObj, this.homelandFarmAuraCropPlantPointNetIdField, (IntPtr)(&levelObjectNetId));

                if (!this.TryHomelandFarmTryWriteAuraMonoVector3Field(
                        pointObj,
                        this.homelandFarmAuraCropPlantPointPosField,
                        pos,
                        out string vectorWriteStatus))
                {
                    status = vectorWriteStatus;
                    this.HomelandFarmLog(status + " planterPos=" + pos);
                    return false;
                }

                if (this.homelandFarmAuraCropPlantPointAngleField != IntPtr.Zero)
                {
                    auraMonoFieldSetValue(pointObj, this.homelandFarmAuraCropPlantPointAngleField, (IntPtr)(&angle));
                }

                IntPtr exc = IntPtr.Zero;
                // CropPlantPoint is a value type: List<T>.Add(T) via mono_runtime_invoke expects a
                // pointer to the UNBOXED struct value, not the boxed object. Passing the boxed
                // MonoObject* makes Add copy the object header as struct data -> garbage
                // pos/angle/levelObjectNetId on the wire -> server InvalidPlantBox.
                if (pointIsValueType && auraMonoObjectUnbox != null)
                {
                    IntPtr raw = auraMonoObjectUnbox(pointObj);
                    addArgs[0] = raw != IntPtr.Zero ? raw : pointObj;
                }
                else
                {
                    addArgs[0] = pointObj;
                }

                auraMonoRuntimeInvoke(addMethod, listObj, (IntPtr)addArgs, ref exc);
                if (exc != IntPtr.Zero)
                {
                    status = "Native CropPlantPoint Add failed exc=0x" + exc.ToInt64().ToString("X") + ".";
                    this.HomelandFarmLog(status);
                    return false;
                }

                added++;
            }

            if (added == 0)
            {
                status = "Native sow: no valid points.";
                return false;
            }

            IntPtr seedExc = IntPtr.Zero;
            uint seed = seedNetId;
            IntPtr* seedArgs = stackalloc IntPtr[2];
            seedArgs[0] = (IntPtr)(&seed);
            seedArgs[1] = listObj;
            auraMonoRuntimeInvoke(this.homelandFarmAuraCropSeedingMethod, IntPtr.Zero, (IntPtr)seedArgs, ref seedExc);
            if (seedExc != IntPtr.Zero)
            {
                status = "Native CropSeeding failed exc=0x" + seedExc.ToInt64().ToString("X") + ".";
                this.HomelandFarmLog(status + " seed=" + seedNetId + " points=" + added);
                return false;
            }

            status = "Native sow sent for " + added + " point(s).";
            this.HomelandFarmLog(status + " seed=" + seedNetId);
            return true;
        }

        private object CreateHomelandFarmUintList(IEnumerable<uint> ids)
        {
            List<uint> values = ids != null ? ids.ToList() : new List<uint>(0);
            Type listType = null;
            if (this.homelandFarmCropWaterPlantMethod != null && this.homelandFarmCropWaterPlantMethod.GetParameters().Length >= 2)
            {
                listType = this.homelandFarmCropWaterPlantMethod.GetParameters()[1].ParameterType;
            }
            else if (this.homelandFarmManuredNetworkCommandType != null)
            {
                FieldInfo cropNetIdsField = this.homelandFarmManuredNetworkCommandType.GetField(
                    "cropNetIds",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (cropNetIdsField != null)
                {
                    listType = cropNetIdsField.FieldType;
                }
            }

            return this.CreateCompatibleUIntList(listType, values);
        }

        private object CreateHomelandFarmUintList(IEnumerable<uint> ids, Type listType)
        {
            List<uint> values = ids != null ? ids.ToList() : new List<uint>(0);
            return this.CreateCompatibleUIntList(listType, values);
        }

        private bool TryHomelandFarmResolveReflection(out string status)
        {
            status = string.Empty;
            if (this.EnsureHomelandFarmReflectionReady())
            {
                status = "Homeland farm reflection ready.";
                return true;
            }

            status = string.IsNullOrEmpty(this.homelandFarmReflectionUnavailableStatus)
                ? "Homeland farm reflection unavailable."
                : this.homelandFarmReflectionUnavailableStatus;
            return false;
        }

        private bool TryHomelandFarmResolveLocalPlayerComponent(out object localPlayerComponent, out string source)
        {
            localPlayerComponent = null;
            source = string.Empty;
            this.EnsureHomelandFarmLocalPlayerComponentType();

            if (this.TryGetManagedSelfPlayerObject(out object selfPlayerObj, out string selfPlayerSource)
                && this.TryHomelandFarmTryAcceptPlayerCandidate(selfPlayerObj, selfPlayerSource, out localPlayerComponent, out source))
            {
                return true;
            }

            if (this.TryGetManagedSelfPlayerEntityObject(out object selfEntityObj, out string selfEntitySource)
                && this.TryHomelandFarmTryAcceptPlayerCandidate(selfEntityObj, selfEntitySource, out localPlayerComponent, out source))
            {
                return true;
            }

            if (this.TryGetManagedViewModuleSelfPlayerObject(out object viewModulePlayerObj, out string viewModuleSource)
                && this.TryHomelandFarmTryAcceptPlayerCandidate(viewModulePlayerObj, viewModuleSource, out localPlayerComponent, out source))
            {
                return true;
            }

            if (this.TryGetManagedInteractSystemObject(out object interactSystemObj, out string interactSource)
                && this.TryGetManagedInteractPlayerObject(interactSystemObj, out object interactPlayerObj, out string interactPlayerSource)
                && this.TryHomelandFarmTryAcceptPlayerCandidate(
                    interactPlayerObj,
                    interactSource + "/" + interactPlayerSource,
                    out localPlayerComponent,
                    out source))
            {
                return true;
            }

            this.EnsureHomelandFarmScannerTypes();
            if (this.homelandFarmEntityUtilGetSelfPlayerMethod != null)
            {
                try
                {
                    object directSelfPlayer = this.homelandFarmEntityUtilGetSelfPlayerMethod.Invoke(null, null);
                    if (this.TryHomelandFarmTryAcceptPlayerCandidate(
                        directSelfPlayer,
                        "EntityUtil.GetSelfPlayer()",
                        out localPlayerComponent,
                        out source))
                    {
                        return true;
                    }
                }
                catch
                {
                }
            }

            this.HomelandFarmLog("LocalPlayer resolution failed after managed fallbacks.");
            return false;
        }

        private void EnsureHomelandFarmLocalPlayerComponentType()
        {
            if (this.homelandFarmLocalPlayerComponentType != null)
            {
                return;
            }

            this.homelandFarmLocalPlayerComponentType = this.FindLoadedType(
                "XDTLevelAndEntity.Gameplay.Component.Player.LocalPlayerComponent",
                "ScriptsRefactory.LevelAndEntity.Gameplay.Component.Player.LocalPlayerComponent",
                "Il2CppXDTLevelAndEntity.Gameplay.Component.Player.LocalPlayerComponent",
                "LocalPlayerComponent");
        }

        private bool TryHomelandFarmTryAcceptPlayerCandidate(object candidate, string candidateSource, out object accepted, out string acceptedSource)
        {
            accepted = null;
            acceptedSource = string.Empty;
            if (candidate == null || string.IsNullOrWhiteSpace(candidateSource))
            {
                return false;
            }

            if (this.homelandFarmLocalPlayerComponentType != null
                && this.homelandFarmLocalPlayerComponentType.IsInstanceOfType(candidate))
            {
                accepted = candidate;
                acceptedSource = candidateSource;
                this.HomelandFarmLog("LocalPlayer via " + acceptedSource + " type=" + candidate.GetType().FullName);
                return true;
            }

            if (this.TryHomelandFarmObjectExposesInHomeland(candidate))
            {
                accepted = candidate;
                acceptedSource = candidateSource;
                this.HomelandFarmLog("LocalPlayer via " + acceptedSource + " (inHomeland field) type=" + candidate.GetType().FullName);
                return true;
            }

            if (this.homelandFarmLocalPlayerComponentType != null
                && this.TryHomelandFarmGetComponent(candidate, this.homelandFarmLocalPlayerComponentType, out object component)
                && component != null)
            {
                accepted = component;
                acceptedSource = candidateSource + ".GetComponent";
                this.HomelandFarmLog("LocalPlayer via " + acceptedSource + " type=" + component.GetType().FullName);
                return true;
            }

            if (this.TryGetManagedPlayerEntityObject(candidate, out object entityObj, out string entitySource)
                && entityObj != null
                && this.homelandFarmLocalPlayerComponentType != null
                && this.TryHomelandFarmGetComponent(entityObj, this.homelandFarmLocalPlayerComponentType, out component)
                && component != null)
            {
                accepted = component;
                acceptedSource = candidateSource + "." + entitySource + ".GetComponent";
                this.HomelandFarmLog("LocalPlayer via " + acceptedSource + " type=" + component.GetType().FullName);
                return true;
            }

            return false;
        }

        private bool TryHomelandFarmObjectExposesInHomeland(object obj)
        {
            if (obj == null)
            {
                return false;
            }

            string[] members = { "inHomeland", "_inHomeland", "InHomeland", "isInHomeland", "IsInHomeland" };
            for (int i = 0; i < members.Length; i++)
            {
                if (this.TryGetObjectMember(obj, members[i], out _))
                {
                    return true;
                }
            }

            return false;
        }

        private unsafe bool TryHomelandFarmTryGetAuraLocalPlayerObject(out IntPtr localPlayerObj, out string source)
        {
            localPlayerObj = IntPtr.Zero;
            source = string.Empty;
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            string[] entityUtilTypeNames =
            {
                "XDTLevelAndEntity.BaseSystem.EntitiesManager.EntityUtil",
                "ScriptsRefactory.LevelAndEntity.BaseSystem.EntitiesManager.EntityUtil",
                "EntityUtil"
            };
            for (int i = 0; i < entityUtilTypeNames.Length && localPlayerObj == IntPtr.Zero; i++)
            {
                IntPtr entityUtilClass = this.FindAuraMonoClassByFullName(entityUtilTypeNames[i]);
                if (entityUtilClass == IntPtr.Zero)
                {
                    entityUtilClass = this.FindAuraMonoClassAcrossLoadedAssemblies(
                        "XDTLevelAndEntity.BaseSystem.EntitiesManager",
                        "EntityUtil");
                }

                if (entityUtilClass == IntPtr.Zero)
                {
                    continue;
                }

                IntPtr getSelfPlayerMethod = this.FindAuraMonoMethodOnHierarchy(entityUtilClass, "GetSelfPlayer", 0);
                if (getSelfPlayerMethod == IntPtr.Zero)
                {
                    continue;
                }

                IntPtr exc = IntPtr.Zero;
                localPlayerObj = auraMonoRuntimeInvoke(getSelfPlayerMethod, IntPtr.Zero, IntPtr.Zero, ref exc);
                if (exc == IntPtr.Zero && localPlayerObj != IntPtr.Zero)
                {
                    source = "Aura EntityUtil.GetSelfPlayer()";
                }
                else
                {
                    localPlayerObj = IntPtr.Zero;
                }
            }

            if (localPlayerObj != IntPtr.Zero)
            {
                return true;
            }

            string[] entityManagerTypeNames =
            {
                "XDTLevelAndEntity.BaseSystem.EntityManager",
                "ScriptsRefactory.LevelAndEntity.BaseSystem.EntityManager",
                "EntityManager"
            };
            for (int i = 0; i < entityManagerTypeNames.Length && localPlayerObj == IntPtr.Zero; i++)
            {
                IntPtr managerClass = this.FindAuraMonoClassByFullName(entityManagerTypeNames[i]);
                if (managerClass == IntPtr.Zero)
                {
                    continue;
                }

                if (this.TryGetAuraMonoStaticObjectField(managerClass, "Instance", out IntPtr managerObj) && managerObj != IntPtr.Zero
                    && this.TryGetMonoObjectMember(managerObj, "selfPlayer", out localPlayerObj)
                    && localPlayerObj != IntPtr.Zero)
                {
                    source = "Aura EntityManager.Instance.selfPlayer";
                }
                else
                {
                    localPlayerObj = IntPtr.Zero;
                }
            }

            return localPlayerObj != IntPtr.Zero;
        }

        private unsafe bool TryHomelandFarmTryReadAuraLocalPlayerUIntField(string[] members, out uint value, out string source)
        {
            value = 0U;
            source = string.Empty;
            if (members == null || members.Length == 0 || !this.TryHomelandFarmTryGetAuraLocalPlayerObject(out IntPtr localPlayerObj, out source))
            {
                return false;
            }

            for (int i = 0; i < members.Length; i++)
            {
                if (this.TryGetMonoUInt32Member(localPlayerObj, members[i], out value) && value != 0U)
                {
                    source = source + "." + members[i];
                    return true;
                }
            }

            return false;
        }

        private unsafe bool TryHomelandFarmTryReadInFieldNetIdAura(out uint inFieldNetId, out string source)
        {
            inFieldNetId = 0U;
            source = string.Empty;
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return false;
            }

            string[] localPlayerMembers = { "inFieldOwnerId", "InFieldOwnerId", "inFieldNetId", "InFieldNetId" };
            if (this.TryHomelandFarmTryReadAuraLocalPlayerUIntField(localPlayerMembers, out inFieldNetId, out source))
            {
                return true;
            }

            if (!this.TryGetHomelandFarmPlayerNetId(out uint playerNetId, out _) || playerNetId == 0U)
            {
                return false;
            }

            if (!this.TryGetAuraMonoEntityObjectByNetId(playerNetId, out IntPtr entityObj) || entityObj == IntPtr.Zero)
            {
                return false;
            }

            for (int i = 0; i < localPlayerMembers.Length; i++)
            {
                if (this.TryGetMonoUInt32Member(entityObj, localPlayerMembers[i], out inFieldNetId) && inFieldNetId != 0U)
                {
                    source = "Aura player entity." + localPlayerMembers[i];
                    return true;
                }
            }

            return false;
        }

        private bool TryHomelandFarmIsOnOwnFarmField(uint playerNetId)
        {
            return playerNetId != 0U
                && this.TryHomelandFarmGetSelfPlayInFieldOwnerNetId(out uint fieldOwnerNetId)
                && fieldOwnerNetId == playerNetId;
        }

        private bool TryHomelandFarmTryQuickAcceptFarmNetId(uint netId, HashSet<uint> output, bool includeLinkedCrops = true)
        {
            if (netId == 0U || output == null)
            {
                return false;
            }

            int before = output.Count;
            try
            {
                if (this.TryHomelandFarmClassifyFarmNetId(netId, out bool isCropBox))
                {
                    output.Add(netId);
                    if (isCropBox)
                    {
                        this.homelandFarmLastScanCropBoxNetIds.Add(netId);
                    }

                    if (includeLinkedCrops)
                    {
                        this.TryHomelandFarmCollectCropNetIdsForEntity(netId, output);
                    }
                }
            }
            catch (Exception ex)
            {
                this.HomelandFarmLog("Farm netId accept failed netId=" + netId + ": " + ex.Message);
            }

            return output.Count > before;
        }

        private int TryHomelandFarmTryAddLevelObjectFarmNetIds(object levelObject, object dictionaryEntry, HashSet<uint> output)
        {
            if (levelObject == null || output == null)
            {
                return 0;
            }

            if (!this.TryHomelandFarmTryGetLevelObjectScanNetId(levelObject, dictionaryEntry, out uint scanNetId) || scanNetId == 0U)
            {
                return 0;
            }

            this.TryHomelandFarmRememberLevelObjectPosition(scanNetId, levelObject);
            this.TryHomelandFarmRememberLevelObjectOwnerFromLevelObject(levelObject, scanNetId);
            return this.TryHomelandFarmTryQuickAcceptFarmNetId(scanNetId, output) ? 1 : 0;
        }

        private unsafe int TryHomelandFarmTryAddLevelObjectFarmNetIds(IntPtr levelObjectObj, IntPtr dictionaryEntry, HashSet<uint> output)
        {
            if (levelObjectObj == IntPtr.Zero || output == null)
            {
                return 0;
            }

            if (!this.TryHomelandFarmTryGetLevelObjectScanNetId(levelObjectObj, dictionaryEntry, out uint scanNetId) || scanNetId == 0U)
            {
                return 0;
            }

            this.TryHomelandFarmRememberLevelObjectPosition(scanNetId, levelObjectObj);
            this.TryHomelandFarmRememberLevelObjectOwnerFromLevelObject(levelObjectObj, scanNetId);
            return this.TryHomelandFarmTryQuickAcceptFarmNetId(scanNetId, output) ? 1 : 0;
        }

        // Resolve each cached mono class independently; never short-circuit on partial cache hit.
        private bool TryResolveAuraMonoFarmComponentClasses(
            out IntPtr plantComponentClass,
            out IntPtr cropBoxComponentClass,
            out IntPtr cropComponentClass)
        {
            plantComponentClass = this.homelandFarmAuraPlantComponentClass;
            cropBoxComponentClass = this.homelandFarmAuraCropBoxComponentClass;
            cropComponentClass = this.homelandFarmAuraCropComponentClass;

            if (plantComponentClass != IntPtr.Zero
                && cropBoxComponentClass != IntPtr.Zero
                && cropComponentClass != IntPtr.Zero)
            {
                return true;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return plantComponentClass != IntPtr.Zero
                    || cropBoxComponentClass != IntPtr.Zero
                    || cropComponentClass != IntPtr.Zero;
            }

            // At least one class is still unresolved here. Resolving it scans every loaded
            // assembly/image (expensive), so throttle the attempt — otherwise classify pays the
            // full scan on every entity when a class simply does not exist in this build.
            float nowResolve = Time.realtimeSinceStartup;
            if (nowResolve < this.homelandFarmAuraFarmComponentClassRetryAt)
            {
                return plantComponentClass != IntPtr.Zero
                    || cropBoxComponentClass != IntPtr.Zero
                    || cropComponentClass != IntPtr.Zero;
            }

            this.homelandFarmAuraFarmComponentClassRetryAt = nowResolve + HomelandFarmAuraComponentClassResolveRetrySeconds;

            // Run the full multi-strategy resolution (and log which classes were found / missing).
            this.HomelandFarmResolveFarmComponentClassesInternal(logResults: true);

            plantComponentClass = this.homelandFarmAuraPlantComponentClass;
            cropBoxComponentClass = this.homelandFarmAuraCropBoxComponentClass;
            cropComponentClass = this.homelandFarmAuraCropComponentClass;
            return plantComponentClass != IntPtr.Zero || cropBoxComponentClass != IntPtr.Zero || cropComponentClass != IntPtr.Zero;
        }

        // Full namespace / full-name candidate lists for each farm component class. Cover both
        // "Gameplay"/"GamePlay" spellings and XDTLevelAndEntity / ScriptsRefactory variants so the
        // class resolves regardless of which build is loaded.
        private static readonly string[] HomelandFarmAuraPlantComponentNamespaces =
        {
            // PlantComponent actually lives in the Homeland namespace (same as CropBox/Crop), not
            // a ".Plant" namespace — confirmed via ilspy dump. Keep .Plant as fallback.
            "XDTLevelAndEntity.Gameplay.Component.Homeland",
            "XDTLevelAndEntity.GamePlay.Component.Homeland",
            "ScriptsRefactory.LevelAndEntity.Gameplay.Component.Homeland",
            "XDTLevelAndEntity.Gameplay.Component.Plant",
            "XDTLevelAndEntity.GamePlay.Component.Plant",
            "ScriptsRefactory.LevelAndEntity.Gameplay.Component.Plant",
            "ScriptsRefactory.LevelAndEntity.GamePlay.Component.Plant",
        };

        private static readonly string[] HomelandFarmAuraPlantComponentFullNames =
        {
            "XDTLevelAndEntity.Gameplay.Component.Homeland.PlantComponent",
            "XDTLevelAndEntity.GamePlay.Component.Homeland.PlantComponent",
            "ScriptsRefactory.LevelAndEntity.Gameplay.Component.Homeland.PlantComponent",
            "XDTLevelAndEntity.Gameplay.Component.Plant.PlantComponent",
            "XDTLevelAndEntity.GamePlay.Component.Plant.PlantComponent",
            "ScriptsRefactory.LevelAndEntity.Gameplay.Component.Plant.PlantComponent",
        };

        private static readonly string[] HomelandFarmAuraCropBoxComponentNamespaces =
        {
            "XDTLevelAndEntity.Gameplay.Component.Homeland",
            "XDTLevelAndEntity.Gameplay.Component.Farm",
            "XDTLevelAndEntity.GamePlay.Component.Homeland",
            "XDTLevelAndEntity.GamePlay.Component.Farm",
            "ScriptsRefactory.LevelAndEntity.Gameplay.Component.Farm",
            "ScriptsRefactory.LevelAndEntity.Gameplay.Component.Homeland",
        };

        private static readonly string[] HomelandFarmAuraCropBoxComponentFullNames =
        {
            "XDTLevelAndEntity.Gameplay.Component.Homeland.CropBoxComponent",
            "XDTLevelAndEntity.Gameplay.Component.Farm.CropBoxComponent",
            "XDTLevelAndEntity.GamePlay.Component.Homeland.CropBoxComponent",
            "XDTLevelAndEntity.GamePlay.Component.Farm.CropBoxComponent",
            "ScriptsRefactory.LevelAndEntity.Gameplay.Component.Farm.CropBoxComponent",
        };

        private static readonly string[] HomelandFarmAuraCropComponentNamespaces =
        {
            "XDTLevelAndEntity.Gameplay.Component.Homeland",
            "XDTLevelAndEntity.Gameplay.Component.Farm",
            "XDTLevelAndEntity.GamePlay.Component.Homeland",
            "XDTLevelAndEntity.GamePlay.Component.Farm",
            "ScriptsRefactory.LevelAndEntity.Gameplay.Component.Farm",
            "ScriptsRefactory.LevelAndEntity.Gameplay.Component.Homeland",
        };

        // Resolve all three farm component classes using every available strategy, then log which
        // were found (with their resolved class name) and which are still missing. Each class is
        // resolved independently and only when still unknown, so warmup converges as assemblies load.
        internal void HomelandFarmResolveFarmComponentClassesInternal(bool logResults)
        {
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                if (logResults)
                {
                    this.HomelandFarmLog("Farm component class warmup skipped: AuraMono API/thread not ready.");
                }

                return;
            }

            if (this.homelandFarmAuraPlantComponentClass == IntPtr.Zero)
            {
                this.homelandFarmAuraPlantComponentClass = this.HomelandFarmResolveAuraComponentClassRobust(
                    HomelandFarmAuraPlantComponentFullNames,
                    "PlantComponent",
                    HomelandFarmAuraPlantComponentNamespaces,
                    candidate => this.HomelandFarmAuraClassDisplayNameContains(candidate, "PlantComponent"));
            }

            if (this.homelandFarmAuraCropBoxComponentClass == IntPtr.Zero)
            {
                this.homelandFarmAuraCropBoxComponentClass = this.HomelandFarmResolveAuraComponentClassRobust(
                    HomelandFarmAuraCropBoxComponentFullNames,
                    "CropBoxComponent",
                    HomelandFarmAuraCropBoxComponentNamespaces,
                    candidate => this.HomelandFarmAuraClassDisplayNameContains(candidate, "CropBoxComponent"));
            }

            if (this.homelandFarmAuraCropComponentClass == IntPtr.Zero)
            {
                // CropComponent has its own validator (must exclude CropBoxComponent); reuse the
                // existing resolver, which already tries full names, candidates and an all-image scan.
                this.TryResolveHomelandFarmAuraCropComponentClass(out _);
            }

            if (logResults)
            {
                this.HomelandFarmLog(
                    "Farm component class warmup: "
                    + "PlantComponent=" + this.DescribeHomelandFarmAuraClass(this.homelandFarmAuraPlantComponentClass)
                    + " | CropBoxComponent=" + this.DescribeHomelandFarmAuraClass(this.homelandFarmAuraCropBoxComponentClass)
                    + " | CropComponent=" + this.DescribeHomelandFarmAuraClass(this.homelandFarmAuraCropComponentClass));
            }
        }

        // Strategy order per class: (1) exact full names via likely images + loaded assemblies,
        // (2) namespace + short name across loaded assemblies, (3) all-images mono_class_from_name
        // scan. Each candidate is validated by name so we never bind the wrong short-name collision.
        private IntPtr HomelandFarmResolveAuraComponentClassRobust(
            string[] fullNames,
            string shortName,
            string[] namespaceCandidates,
            Func<IntPtr, bool> validator)
        {
            if (fullNames != null)
            {
                for (int i = 0; i < fullNames.Length; i++)
                {
                    IntPtr candidate = this.FindAuraMonoClassByFullName(fullNames[i]);
                    if (candidate != IntPtr.Zero && (validator == null || validator(candidate)))
                    {
                        return candidate;
                    }
                }
            }

            if (namespaceCandidates != null && !string.IsNullOrEmpty(shortName))
            {
                for (int i = 0; i < namespaceCandidates.Length; i++)
                {
                    IntPtr candidate = this.FindAuraMonoClassAcrossLoadedAssemblies(namespaceCandidates[i], shortName);
                    if (candidate != IntPtr.Zero && (validator == null || validator(candidate)))
                    {
                        return candidate;
                    }
                }
            }

            return this.FindHomelandFarmAuraClassByScanningAllImages(shortName, namespaceCandidates, validator);
        }

        private bool HomelandFarmAuraClassDisplayNameContains(IntPtr classPtr, string token)
        {
            if (classPtr == IntPtr.Zero || string.IsNullOrEmpty(token))
            {
                return false;
            }

            string displayName = this.GetAuraMonoClassDisplayName(classPtr);
            return !string.IsNullOrEmpty(displayName)
                && displayName.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool LooksLikeAuraMonoFarmComponentClass(
            IntPtr componentClass,
            IntPtr plantComponentClass,
            IntPtr cropBoxComponentClass,
            IntPtr cropComponentClass)
        {
            if (componentClass == IntPtr.Zero)
            {
                return false;
            }

            if (plantComponentClass != IntPtr.Zero && this.IsAuraMonoClassAssignableTo(componentClass, plantComponentClass))
            {
                return true;
            }

            if (cropBoxComponentClass != IntPtr.Zero && this.IsAuraMonoClassAssignableTo(componentClass, cropBoxComponentClass))
            {
                return true;
            }

            if (cropComponentClass != IntPtr.Zero && this.IsAuraMonoClassAssignableTo(componentClass, cropComponentClass))
            {
                return true;
            }

            string componentClassName = this.GetAuraMonoClassDisplayName(componentClass);
            if (string.IsNullOrEmpty(componentClassName))
            {
                return false;
            }

            return componentClassName.IndexOf("PlantComponent", StringComparison.OrdinalIgnoreCase) >= 0
                || componentClassName.IndexOf("CropBoxComponent", StringComparison.OrdinalIgnoreCase) >= 0
                || (componentClassName.IndexOf("CropComponent", StringComparison.OrdinalIgnoreCase) >= 0
                    && componentClassName.IndexOf("CropBoxComponent", StringComparison.OrdinalIgnoreCase) < 0);
        }

        private bool TryHasFarmComponentViaAuraMono(IntPtr entityObj)
        {
            if (entityObj == IntPtr.Zero || auraMonoObjectGetClass == null || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            if (!this.TryResolveAuraMonoFarmComponentClasses(out IntPtr plantComponentClass, out IntPtr cropBoxComponentClass, out IntPtr cropComponentClass))
            {
                return false;
            }

            IntPtr entityClass = auraMonoObjectGetClass(entityObj);
            IntPtr getAlivedMethod = this.FindAuraMonoMethodOnHierarchy(entityClass, "get_alived", 0);
            if (getAlivedMethod != IntPtr.Zero)
            {
                IntPtr excAlive = IntPtr.Zero;
                IntPtr alivedResult = auraMonoRuntimeInvoke(getAlivedMethod, entityObj, IntPtr.Zero, ref excAlive);
                if (alivedResult != IntPtr.Zero && this.TryUnboxMonoBoolean(alivedResult, out bool isAlive) && !isAlive)
                {
                    return false;
                }
            }

            IntPtr getSpawnedMethod = this.FindAuraMonoMethodOnHierarchy(entityClass, "get_spawned", 0);
            if (getSpawnedMethod != IntPtr.Zero)
            {
                IntPtr excSpawned = IntPtr.Zero;
                IntPtr spawnedResult = auraMonoRuntimeInvoke(getSpawnedMethod, entityObj, IntPtr.Zero, ref excSpawned);
                if (spawnedResult != IntPtr.Zero && this.TryUnboxMonoBoolean(spawnedResult, out bool isSpawned) && !isSpawned)
                {
                    return false;
                }
            }

            IntPtr getAllComponentsMethod = this.FindAuraMonoMethodOnHierarchy(entityClass, "GetAllComponents", 0);
            if (getAllComponentsMethod == IntPtr.Zero)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr componentsObj = auraMonoRuntimeInvoke(getAllComponentsMethod, entityObj, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || componentsObj == IntPtr.Zero)
            {
                return false;
            }

            List<IntPtr> components = new List<IntPtr>();
            if (!this.TryEnumerateAuraMonoCollectionItems(componentsObj, components))
            {
                return false;
            }

            for (int i = 0; i < components.Count && i < 64; i++)
            {
                IntPtr componentObj = components[i];
                if (componentObj == IntPtr.Zero)
                {
                    continue;
                }

                IntPtr componentClass = auraMonoObjectGetClass(componentObj);
                if (this.LooksLikeAuraMonoFarmComponentClass(componentClass, plantComponentClass, cropBoxComponentClass, cropComponentClass))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryHomelandFarmCollectFarmNetIdsFromAuraLoadedEntities(
            Vector3 scanCenter,
            float scanRadius,
            HashSet<uint> output,
            out int added,
            out int inspected)
        {
            added = 0;
            inspected = 0;
            if (output == null)
            {
                return false;
            }

            float phaseStartedAt = Time.realtimeSinceStartup;
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                this.HomelandFarmLog("AuraEntities funnel skipped: aura mono API/thread not ready.");
                return false;
            }

            List<IntPtr> entityObjects;
            if (!this.TryEnumerateAuraMonoLoadedEntityObjects(out entityObjects, out string enumStatus)
                || entityObjects == null
                || entityObjects.Count == 0)
            {
                this.HomelandFarmLog("AuraEntities funnel skipped: entity enumeration empty (" + enumStatus + ").");
                return false;
            }

            float enumMs = (Time.realtimeSinceStartup - phaseStartedAt) * 1000f;
            float preSeedStartedAt = Time.realtimeSinceStartup;

            bool spatialScan = scanRadius > 0f && scanCenter != Vector3.zero;
            float radiusSq = spatialScan ? scanRadius * scanRadius : 0f;
            // IMPORTANT: the loaded-entity traversal can surface stale or non-entity
            // objects. Invoking get_alived / get_spawned / GetAllComponents / position
            // unbox on those raw pointers causes a native access violation (game crash).
            // So here we only use the lightweight GetNetId getter on raw pointers, and
            // route every heavy access below through the netId-based path, which only
            // ever touches entities the game still has registered.
            int totalEnumerated = entityObjects.Count;
            int netResolved = 0;
            int noPosition = 0;
            int outOfRadius = 0;
            HashSet<uint> seenAuraEntityNetIds = new HashSet<uint>();
            List<HomelandFarmAuraEntityCandidate> candidates = new List<HomelandFarmAuraEntityCandidate>(entityObjects.Count);
            int preSeeded = 0;
            // Pre-seed from the in-memory level-object position cache. Crop boxes and planters
            // (the usual water/sow targets) live there with known positions, so we can radius-filter
            // them instantly without any native GetEntity/position invokes. This makes the common
            // case fast even when the cheaper spatial queries returned nothing and we fell back here.
            if (spatialScan)
            {
                this.TryHomelandFarmCacheAuraLevelObjectPositions(false, allowDictionaryScan: false);
                if (this.homelandFarmAuraLevelObjectPositionCache.Count > 0)
                {
                    foreach (KeyValuePair<uint, Vector3> cached in this.homelandFarmAuraLevelObjectPositionCache)
                    {
                        uint cachedNetId = cached.Key;
                        if (cachedNetId == 0U || cachedNetId >= 0x80000000U)
                        {
                            continue;
                        }

                        Vector3 cachedPos = cached.Value;
                        if (cachedPos == Vector3.zero)
                        {
                            continue;
                        }

                        float cachedDistanceSq = (cachedPos - scanCenter).sqrMagnitude;
                        if (cachedDistanceSq > radiusSq)
                        {
                            continue;
                        }

                        if (!seenAuraEntityNetIds.Add(cachedNetId))
                        {
                            continue;
                        }

                        candidates.Add(new HomelandFarmAuraEntityCandidate
                        {
                            NetId = cachedNetId,
                            Distance = Mathf.Sqrt(cachedDistanceSq)
                        });
                        preSeeded++;
                    }
                }
            }

            float preSeedMs = (Time.realtimeSinceStartup - preSeedStartedAt) * 1000f;
            // Wall-clock cap so this synchronous pass cannot freeze the frame for seconds even
            // when the loaded-entity set is large and the per-netId position resolve is costly.
            float collectStartedAt = Time.realtimeSinceStartup;
            bool collectBudgetHit = false;
            // For a spatial (radius) scan, walk the WHOLE loaded set and collect every in-radius
            // entity as a candidate (bounded only by the wall-clock budget). We must NOT cap by
            // candidate count here: at a large radius almost everything is "in radius", and a count
            // cap fills up with the first entities in list order — which can starve the actual farm
            // targets (e.g. crop boxes that appear later in the list). Candidates are sorted by
            // distance afterwards and only the closest are verified, so collecting them all is what
            // lets nearby crop boxes survive regardless of radius. For a non-spatial whole-field
            // scan, keep the inspect cap to bound work.
            for (int i = 0;
                i < entityObjects.Count
                    && (spatialScan || netResolved < HomelandFarmMaxAuraFarmEntityInspect);
                i++)
            {
                if (spatialScan
                    && (i & 31) == 31
                    && Time.realtimeSinceStartup - collectStartedAt >= HomelandFarmAuraSpatialCollectBudgetSeconds)
                {
                    collectBudgetHit = true;
                    break;
                }

                IntPtr entityObj = entityObjects[i];
                if (entityObj == IntPtr.Zero)
                {
                    continue;
                }

                if (!this.TryGetAuraMonoEntityNetId(entityObj, out uint candidateNetId) || candidateNetId == 0U)
                {
                    continue;
                }

                // Skip local-only / invalid high-range netIds (mirrors bird-farm guard).
                if (candidateNetId >= 0x80000000U)
                {
                    continue;
                }

                if (!seenAuraEntityNetIds.Add(candidateNetId))
                {
                    continue;
                }

                netResolved++;

                float distance = 0f;
                if (spatialScan)
                {
                    // Resolve position via the netId-based resolver (cache + registered
                    // entity lookup) instead of invoking on the raw entity pointer.
                    if (!this.TryHomelandFarmResolveFarmEntityPosition(candidateNetId, out Vector3 candidatePosition)
                        || candidatePosition == Vector3.zero)
                    {
                        noPosition++;
                        // Keep entities whose position we cannot resolve: crop boxes are
                        // often parented to a field and may not expose a world position.
                        // Give them lowest priority so positioned entities are checked first.
                        distance = float.MaxValue;
                    }
                    else
                    {
                        Vector3 delta = candidatePosition - scanCenter;
                        float distanceSq = delta.sqrMagnitude;
                        if (distanceSq > radiusSq)
                        {
                            outOfRadius++;
                            continue;
                        }

                        distance = Mathf.Sqrt(distanceSq);
                    }
                }

                candidates.Add(new HomelandFarmAuraEntityCandidate
                {
                    NetId = candidateNetId,
                    Distance = distance
                });
            }

            float collectMs = (Time.realtimeSinceStartup - collectStartedAt) * 1000f;
            this.HomelandFarmLog(
                "AuraEntities funnel: total=" + totalEnumerated
                + " net=" + netResolved
                + " noPos=" + noPosition
                + " outRadius=" + outOfRadius
                + " preSeed=" + preSeeded
                + " candidates=" + candidates.Count
                + " | enum=" + enumMs.ToString("F0") + "ms"
                + " preSeed=" + preSeedMs.ToString("F0") + "ms"
                + " collect=" + collectMs.ToString("F0") + "ms"
                + (collectBudgetHit ? " (collect budget hit)" : string.Empty));

            if (candidates.Count == 0)
            {
                return false;
            }

            candidates.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            if (spatialScan && candidates.Count > HomelandFarmMaxAuraFarmSpatialCandidates)
            {
                candidates.RemoveRange(
                    HomelandFarmMaxAuraFarmSpatialCandidates,
                    candidates.Count - HomelandFarmMaxAuraFarmSpatialCandidates);
            }

            int verifyLimit = spatialScan
                ? Mathf.Min(candidates.Count, HomelandFarmMaxAuraFarmSpatialVerifyCount)
                : Mathf.Min(candidates.Count, HomelandFarmMaxAuraFarmComponentChecks);
            float verifyStartedAt = Time.realtimeSinceStartup;
            for (int i = 0; i < verifyLimit; i++)
            {
                if (spatialScan
                    && Time.realtimeSinceStartup - verifyStartedAt >= HomelandFarmAuraSpatialVerifyBudgetSeconds)
                {
                    break;
                }

                HomelandFarmAuraEntityCandidate candidate = candidates[i];
                inspected++;
                try
                {
                    int before = output.Count;
                    this.TryHomelandFarmTryQuickAcceptFarmNetId(candidate.NetId, output, includeLinkedCrops: false);
                    added += output.Count - before;
                }
                catch (Exception ex)
                {
                    this.HomelandFarmLog("Aura entity farm scan failed netId=" + candidate.NetId + ": " + ex.Message);
                }
            }

            this.HomelandFarmLog(
                "AuraEntities verify: checked=" + inspected
                + " added=" + added
                + " verify=" + ((Time.realtimeSinceStartup - verifyStartedAt) * 1000f).ToString("F0") + "ms");
            return added > 0;
        }

        private struct HomelandFarmAuraEntityCandidate
        {
            public uint NetId;
            public float Distance;
        }

        // Loaded-entity enumeration can return stale pointers; never call GetAllComponents on them.
        // Always resolve through Entities.GetEntity(netId) first (see AuraEntities funnel comment).
        private bool TryHomelandFarmTryGuardAuraEntityBeforeHeavyAccess(IntPtr entityObj)
        {
            if (entityObj == IntPtr.Zero || auraMonoObjectGetClass == null || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr entityClass = auraMonoObjectGetClass(entityObj);
            if (entityClass == IntPtr.Zero)
            {
                return false;
            }

            IntPtr getAlivedMethod = this.FindAuraMonoMethodOnHierarchy(entityClass, "get_alived", 0);
            if (getAlivedMethod != IntPtr.Zero)
            {
                IntPtr excAlive = IntPtr.Zero;
                IntPtr alivedResult = auraMonoRuntimeInvoke(getAlivedMethod, entityObj, IntPtr.Zero, ref excAlive);
                if (excAlive != IntPtr.Zero)
                {
                    return false;
                }

                if (alivedResult != IntPtr.Zero && this.TryUnboxMonoBoolean(alivedResult, out bool isAlive) && !isAlive)
                {
                    return false;
                }
            }

            IntPtr getSpawnedMethod = this.FindAuraMonoMethodOnHierarchy(entityClass, "get_spawned", 0);
            if (getSpawnedMethod != IntPtr.Zero)
            {
                IntPtr excSpawned = IntPtr.Zero;
                IntPtr spawnedResult = auraMonoRuntimeInvoke(getSpawnedMethod, entityObj, IntPtr.Zero, ref excSpawned);
                if (excSpawned != IntPtr.Zero)
                {
                    return false;
                }

                if (spawnedResult != IntPtr.Zero && this.TryUnboxMonoBoolean(spawnedResult, out bool isSpawned) && !isSpawned)
                {
                    return false;
                }
            }

            return true;
        }

        private bool TryHomelandFarmTryGetLevelObjectScanNetId(object levelObject, object dictionaryEntry, out uint scanNetId)
        {
            scanNetId = 0U;
            if (levelObject == null)
            {
                return false;
            }

            if (this.TryGetAuraLevelObjectResourceId(levelObject, out uint resourceId) && resourceId != 0U)
            {
                scanNetId = resourceId;
                return true;
            }

            string[] entityMembers = { "occupantNetId", "OccupantNetId", "resourceID", "ResourceID" };
            for (int i = 0; i < entityMembers.Length; i++)
            {
                if (this.TryGetUIntMember(levelObject, entityMembers[i], out scanNetId) && scanNetId != 0U)
                {
                    return true;
                }
            }

            object data = this.TryGetManagedMemberValue(levelObject, "_data");
            if (data != null)
            {
                for (int i = 0; i < entityMembers.Length; i++)
                {
                    if (this.TryGetUIntMember(data, entityMembers[i], out scanNetId) && scanNetId != 0U)
                    {
                        return true;
                    }
                }
            }

            if (this.TryGetAuraLevelObjectNetId(levelObject, out ulong levelObjectNetId)
                && levelObjectNetId != 0UL
                && levelObjectNetId <= uint.MaxValue)
            {
                scanNetId = (uint)levelObjectNetId;
                return true;
            }

            if (this.TryReadManagedUInt64Member(levelObject, "netId", out levelObjectNetId)
                && levelObjectNetId != 0UL
                && levelObjectNetId <= uint.MaxValue)
            {
                scanNetId = (uint)levelObjectNetId;
                return true;
            }

            if (dictionaryEntry != null && this.TryReadManagedUInt64Member(dictionaryEntry, "Key", out levelObjectNetId)
                && levelObjectNetId != 0UL
                && levelObjectNetId <= uint.MaxValue)
            {
                scanNetId = (uint)levelObjectNetId;
                return true;
            }

            if (dictionaryEntry != null && this.TryReadManagedUInt64Member(dictionaryEntry, "key", out levelObjectNetId)
                && levelObjectNetId != 0UL
                && levelObjectNetId <= uint.MaxValue)
            {
                scanNetId = (uint)levelObjectNetId;
                return true;
            }

            return false;
        }

        private unsafe bool TryHomelandFarmTryGetLevelObjectScanNetId(IntPtr levelObjectObj, IntPtr dictionaryEntry, out uint scanNetId)
        {
            scanNetId = 0U;
            if (levelObjectObj == IntPtr.Zero)
            {
                return false;
            }

            string[] entityMembers = { "resourceID", "ResourceID", "occupantNetId", "OccupantNetId" };
            for (int i = 0; i < entityMembers.Length; i++)
            {
                if (this.TryGetMonoUInt32Member(levelObjectObj, entityMembers[i], out scanNetId) && scanNetId != 0U)
                {
                    return true;
                }
            }

            if (this.TryGetMonoObjectMember(levelObjectObj, "_data", out IntPtr dataObj) && dataObj != IntPtr.Zero)
            {
                for (int i = 0; i < entityMembers.Length; i++)
                {
                    if (this.TryGetMonoUInt32Member(dataObj, entityMembers[i], out scanNetId) && scanNetId != 0U)
                    {
                        return true;
                    }
                }
            }

            if (this.TryGetMonoUInt64Member(levelObjectObj, "netId", out ulong levelObjectNetId)
                && levelObjectNetId != 0UL
                && levelObjectNetId <= uint.MaxValue)
            {
                scanNetId = (uint)levelObjectNetId;
                return true;
            }

            if (dictionaryEntry != IntPtr.Zero)
            {
                if (this.TryGetMonoUInt64Member(dictionaryEntry, "Key", out levelObjectNetId)
                    && levelObjectNetId != 0UL
                    && levelObjectNetId <= uint.MaxValue)
                {
                    scanNetId = (uint)levelObjectNetId;
                    return true;
                }

                if (this.TryGetMonoUInt64Member(dictionaryEntry, "key", out levelObjectNetId)
                    && levelObjectNetId != 0UL
                    && levelObjectNetId <= uint.MaxValue)
                {
                    scanNetId = (uint)levelObjectNetId;
                    return true;
                }
            }

            return false;
        }

        private void TryHomelandFarmRememberLevelObjectOwnerFromLevelObject(object levelObject, uint scanNetId)
        {
            if (levelObject == null || scanNetId == 0U)
            {
                return;
            }

            if (this.TryGetAuraLevelObjectOwnerNetId(levelObject, out uint ownerNetId) && ownerNetId != 0U && ownerNetId != scanNetId)
            {
                this.homelandFarmAuraLevelObjectOwnerByNetId[scanNetId] = ownerNetId;
                return;
            }

            if (this.TryGetUIntMember(levelObject, "ownerNetId", out ownerNetId) && ownerNetId != 0U && ownerNetId != scanNetId)
            {
                this.homelandFarmAuraLevelObjectOwnerByNetId[scanNetId] = ownerNetId;
            }
        }

        private void TryHomelandFarmRememberLevelObjectOwnerFromLevelObject(IntPtr levelObjectObj, uint scanNetId)
        {
            if (levelObjectObj == IntPtr.Zero || scanNetId == 0U)
            {
                return;
            }

            string[] ownerMembers = { "ownerNetId", "OwnerNetId", "fieldOwnerNetId", "FieldOwnerNetId" };
            for (int i = 0; i < ownerMembers.Length; i++)
            {
                if (this.TryGetMonoUInt32Member(levelObjectObj, ownerMembers[i], out uint ownerNetId)
                    && ownerNetId != 0U
                    && ownerNetId != scanNetId)
                {
                    this.homelandFarmAuraLevelObjectOwnerByNetId[scanNetId] = ownerNetId;
                    return;
                }
            }
        }

        private bool TryHomelandFarmTryResolveFarmWaterNetId(object levelObject, out uint waterNetId)
        {
            waterNetId = 0U;
            if (levelObject == null)
            {
                return false;
            }

            List<uint> candidates = new List<uint>(8);
            if (this.TryHomelandFarmTryGetLevelObjectScanNetId(levelObject, null, out uint scanNetId) && scanNetId != 0U)
            {
                candidates.Add(scanNetId);
            }

            this.TryHomelandFarmTryCollectLevelObjectNetIdCandidates(levelObject, candidates);

            if (this.TryGetAuraLevelObjectNetId(levelObject, out ulong levelObjectNetId)
                && levelObjectNetId != 0UL
                && levelObjectNetId <= uint.MaxValue)
            {
                uint asUint = (uint)levelObjectNetId;
                if (!candidates.Contains(asUint))
                {
                    candidates.Add(asUint);
                }
            }

            if (this.TryGetAuraLevelObjectOwnerNetId(levelObject, out uint ownerNetId) && ownerNetId != 0U && !candidates.Contains(ownerNetId))
            {
                candidates.Add(ownerNetId);
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                uint candidate = candidates[i];
                if (candidate != 0U && this.TryHomelandFarmHasFarmComponentData(candidate))
                {
                    waterNetId = candidate;
                    return true;
                }
            }

            if (candidates.Count > 0 && candidates[0] != 0U)
            {
                waterNetId = candidates[0];
                return true;
            }

            return false;
        }

        private bool TryHomelandFarmTryResolveFarmWaterNetId(IntPtr levelObjectObj, out uint waterNetId)
        {
            waterNetId = 0U;
            if (levelObjectObj == IntPtr.Zero)
            {
                return false;
            }

            List<uint> candidates = new List<uint>(8);
            if (this.TryHomelandFarmTryGetLevelObjectScanNetId(levelObjectObj, IntPtr.Zero, out uint scanNetId) && scanNetId != 0U)
            {
                candidates.Add(scanNetId);
            }

            this.TryHomelandFarmTryCollectLevelObjectNetIdCandidates(levelObjectObj, candidates);

            if (this.TryGetMonoUInt64Member(levelObjectObj, "netId", out ulong levelObjectNetId)
                && levelObjectNetId != 0UL
                && levelObjectNetId <= uint.MaxValue)
            {
                uint asUint = (uint)levelObjectNetId;
                if (!candidates.Contains(asUint))
                {
                    candidates.Add(asUint);
                }
            }

            string[] ownerMembers = { "ownerNetId", "OwnerNetId" };
            for (int i = 0; i < ownerMembers.Length; i++)
            {
                if (this.TryGetMonoUInt32Member(levelObjectObj, ownerMembers[i], out uint ownerNetId) && ownerNetId != 0U && !candidates.Contains(ownerNetId))
                {
                    candidates.Add(ownerNetId);
                }
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                uint candidate = candidates[i];
                if (candidate != 0U && this.TryHomelandFarmHasFarmComponentData(candidate))
                {
                    waterNetId = candidate;
                    return true;
                }
            }

            if (candidates.Count > 0 && candidates[0] != 0U)
            {
                waterNetId = candidates[0];
                return true;
            }

            return false;
        }

        private void TryHomelandFarmTryCollectLevelObjectNetIdCandidates(object levelObject, List<uint> candidates)
        {
            if (levelObject == null || candidates == null)
            {
                return;
            }

            string[] members = { "ownerNetId", "OwnerNetId", "resourceID", "ResourceID", "occupantNetId", "OccupantNetId" };
            for (int i = 0; i < members.Length; i++)
            {
                if (this.TryGetUIntMember(levelObject, members[i], out uint value) && value != 0U && !candidates.Contains(value))
                {
                    candidates.Add(value);
                }
            }

            object data = this.TryGetManagedMemberValue(levelObject, "_data");
            if (data != null)
            {
                for (int i = 0; i < members.Length; i++)
                {
                    if (this.TryGetUIntMember(data, members[i], out uint value) && value != 0U && !candidates.Contains(value))
                    {
                        candidates.Add(value);
                    }
                }
            }

            if (this.TryReadManagedUInt64Member(levelObject, "netId", out ulong levelObjectNetId)
                && levelObjectNetId != 0UL
                && levelObjectNetId <= uint.MaxValue)
            {
                uint asUint = (uint)levelObjectNetId;
                if (!candidates.Contains(asUint))
                {
                    candidates.Add(asUint);
                }
            }
        }

        private void TryHomelandFarmTryCollectLevelObjectNetIdCandidates(IntPtr levelObjectObj, List<uint> candidates)
        {
            if (levelObjectObj == IntPtr.Zero || candidates == null)
            {
                return;
            }

            string[] members = { "ownerNetId", "OwnerNetId", "resourceID", "ResourceID", "occupantNetId", "OccupantNetId" };
            for (int i = 0; i < members.Length; i++)
            {
                if (this.TryGetMonoUInt32Member(levelObjectObj, members[i], out uint value) && value != 0U && !candidates.Contains(value))
                {
                    candidates.Add(value);
                }
            }

            if (this.TryGetMonoObjectMember(levelObjectObj, "_data", out IntPtr dataObj) && dataObj != IntPtr.Zero)
            {
                for (int i = 0; i < members.Length; i++)
                {
                    if (this.TryGetMonoUInt32Member(dataObj, members[i], out uint value) && value != 0U && !candidates.Contains(value))
                    {
                        candidates.Add(value);
                    }
                }
            }
        }

        private bool TryHomelandFarmTryReadInHomeland(object obj, out bool inHomeland)
        {
            inHomeland = false;
            if (obj == null)
            {
                return false;
            }

            string[] members = { "inHomeland", "_inHomeland", "InHomeland", "isInHomeland", "IsInHomeland" };
            for (int i = 0; i < members.Length; i++)
            {
                if (this.TryGetObjectMember(obj, members[i], out object raw) && raw != null)
                {
                    try
                    {
                        inHomeland = Convert.ToBoolean(raw);
                        return true;
                    }
                    catch
                    {
                    }
                }
            }

            return false;
        }

        private unsafe bool TryHomelandFarmTryReadInHomelandAura(out bool inHomeland, out string source)
        {
            inHomeland = false;
            source = string.Empty;
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            if (!this.TryHomelandFarmTryGetAuraLocalPlayerObject(out IntPtr playerObj, out source))
            {
                return false;
            }

            string[] members = { "inHomeland", "_inHomeland", "InHomeland", "isInHomeland", "IsInHomeland" };
            for (int i = 0; i < members.Length; i++)
            {
                if (this.TryGetMonoBoolMember(playerObj, members[i], out inHomeland))
                {
                    return true;
                }
            }

            return false;
        }

        private unsafe bool TryHomelandFarmTryReadPlayerNetIdAura(out uint netId, out string source)
        {
            netId = 0U;
            source = string.Empty;
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr entityUtilClass = this.FindHomelandFarmAuraClass(
                "XDTLevelAndEntity.BaseSystem.EntitiesManager.EntityUtil",
                "XDTLevelAndEntity.BaseSystem.EntitiesManager",
                "EntityUtil");
            if (entityUtilClass == IntPtr.Zero)
            {
                return false;
            }

            IntPtr getSelfPlayerEntityMethod = this.FindAuraMonoMethodOnHierarchy(entityUtilClass, "GetSelfPlayerEntity", 0);
            if (getSelfPlayerEntityMethod == IntPtr.Zero)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr entityObj = auraMonoRuntimeInvoke(getSelfPlayerEntityMethod, IntPtr.Zero, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || entityObj == IntPtr.Zero)
            {
                return false;
            }

            source = "Aura EntityUtil.GetSelfPlayerEntity()";
            if (this.TryGetMonoUInt32Member(entityObj, "netId", out netId) && netId != 0U)
            {
                return true;
            }

            if (this.TryGetMonoUInt32Member(entityObj, "NetId", out netId) && netId != 0U)
            {
                return true;
            }

            netId = 0U;
            return false;
        }

        private bool TryHomelandFarmGetComponent(object target, Type componentType, out object component)
        {
            component = null;
            if (target == null || componentType == null)
            {
                return false;
            }

            try
            {
                MethodInfo[] methods = target.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                for (int i = 0; i < methods.Length; i++)
                {
                    MethodInfo method = methods[i];
                    if (method == null || !string.Equals(method.Name, "GetComponent", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (method.IsGenericMethodDefinition && method.GetParameters().Length == 0)
                    {
                        MethodInfo closed = method.MakeGenericMethod(componentType);
                        component = closed.Invoke(target, null);
                        if (component != null)
                        {
                            return true;
                        }

                        continue;
                    }

                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length == 1 && typeof(Type).IsAssignableFrom(parameters[0].ParameterType))
                    {
                        component = method.Invoke(target, new object[] { componentType });
                        if (component != null)
                        {
                            return true;
                        }
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private bool EnsureHomelandFarmScannerTypes()
        {
            this.TryEnsureHomelandFarmInteropAssembliesLoaded();
            this.ResolveAuraFarmRuntimeMethods();
            if (this.homelandFarmEntitiesType == null)
            {
                this.homelandFarmEntitiesType = this.FindEntitiesRuntimeType();
            }

            if (this.homelandFarmEntitiesType == null && this.auraEntitiesType != null)
            {
                this.homelandFarmEntitiesType = this.auraEntitiesType;
            }

            if (this.homelandFarmEntitiesType == null)
            {
                this.homelandFarmEntitiesType = this.FindTypeByName(
                    "XDTLevelAndEntity.BaseSystem.EntitiesManager.Entities",
                    "XDTLevelAndEntity.BaseSystem.EntitiesManager",
                    "Entities");
            }

            if (this.homelandFarmEntityType == null)
            {
                this.homelandFarmEntityType = this.FindEntityRuntimeType();
            }

            if (this.homelandFarmEntityUtilType == null)
            {
                this.homelandFarmEntityUtilType = this.ResolveHomelandFarmManagedType(
                    "EntityUtil",
                    "XDTLevelAndEntity.BaseSystem.EntitiesManager.EntityUtil");
            }

            if (this.homelandFarmCropComponentType == null)
            {
                this.homelandFarmCropComponentType = this.ResolveHomelandFarmCropComponentRuntimeType();
            }

            if (this.homelandFarmCropBoxComponentType == null)
            {
                this.homelandFarmCropBoxComponentType = this.ResolveHomelandFarmCropBoxComponentRuntimeType()
                    ?? this.FindTypeByName(
                        "XDTLevelAndEntity.Gameplay.Component.Homeland.CropBoxComponent",
                        "XDTLevelAndEntity.Gameplay.Component.Homeland",
                        "CropBoxComponent")
                    ?? this.FindTypeByName(
                        "XDTLevelAndEntity.Gameplay.Component.Farm.CropBoxComponent",
                        "XDTLevelAndEntity.Gameplay.Component.Farm",
                        "CropBoxComponent");
            }

            if (this.homelandFarmPlantComponentType == null)
            {
                this.homelandFarmPlantComponentType = this.ResolveHomelandFarmManagedType(
                    "PlantComponent",
                    "XDTLevelAndEntity.Gameplay.Component.Homeland.PlantComponent",
                    "ScriptsRefactory.LevelAndEntity.Gameplay.Component.Homeland.PlantComponent");
            }

            if (this.homelandFarmEcsServiceType == null)
            {
                this.homelandFarmEcsServiceType = this.FindLoadedType(
                    "XDTDataAndProtocol.ProtocolService.EcsService",
                    "EcsService")
                    ?? this.FindLoadedEcsServiceType();
            }

            if (this.homelandFarmEntitiesType != null && this.homelandFarmEntitiesGetComponentsMethod == null)
            {
                this.homelandFarmEntitiesGetComponentsMethod = this.homelandFarmEntitiesType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "GetComponents" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1);
                if (this.homelandFarmEntityType != null && this.homelandFarmEntitiesSphereQueryEntitiesMethod == null)
                {
                    Type entityListType = typeof(List<>).MakeGenericType(this.homelandFarmEntityType);
                    this.homelandFarmEntitiesSphereQueryEntitiesMethod = this.homelandFarmEntitiesType.GetMethod(
                        "SphereQueryEntities",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                        null,
                        new Type[] { typeof(Vector3), typeof(float), entityListType },
                        null);
                }
            }

            if (this.homelandFarmEntityUtilType != null)
            {
                if (this.homelandFarmEntityUtilGetSelfPlayerMethod == null)
                {
                    this.homelandFarmEntityUtilGetSelfPlayerMethod = this.GetMethodQuiet(
                        this.homelandFarmEntityUtilType,
                        "GetSelfPlayer",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                        Type.EmptyTypes);
                }

                if (this.homelandFarmEntityUtilGetSelfPlayerEntityMethod == null)
                {
                    this.homelandFarmEntityUtilGetSelfPlayerEntityMethod = this.GetMethodQuiet(
                        this.homelandFarmEntityUtilType,
                        "GetSelfPlayerEntity",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                        Type.EmptyTypes);
                }
            }

            if (this.homelandFarmEcsServiceType != null && this.homelandFarmEcsServiceTryGetMethodDef == null)
            {
                this.homelandFarmEcsServiceTryGetMethodDef = this.homelandFarmEcsServiceType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "TryGet" && m.IsGenericMethodDefinition && m.GetParameters().Length == 2);
            }

            if (this.homelandFarmCropBoxItemDataType != null && this.homelandFarmCropBoxGetWaterCountMethod == null)
            {
                this.homelandFarmCropBoxGetWaterCountMethod = this.homelandFarmCropBoxItemDataType.GetMethod(
                    "GetWaterCount",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    Type.EmptyTypes);
            }

            bool hasManagedScanPath = this.homelandFarmEntitiesGetComponentsMethod != null
                || this.FindLevelObjectManagerRuntimeType() != null;
            bool hasAuraScanPath = this.TryResolveHomelandFarmAuraScanClasses(out _);
            if (hasManagedScanPath || hasAuraScanPath)
            {
                this.homelandFarmScannerTypesResolved = true;
                this.homelandFarmScannerTypesUnavailable = false;
                this.homelandFarmScannerUnavailableLogged = false;
                return true;
            }

            if (!this.homelandFarmScannerUnavailableLogged)
            {
                this.homelandFarmScannerTypesUnavailableStatus = "Entities.GetComponents and LevelObjectManager unavailable.";
                this.HomelandFarmLog(this.homelandFarmScannerTypesUnavailableStatus);
                this.homelandFarmScannerUnavailableLogged = true;
            }

            return false;
        }

        private bool TryHomelandFarmCollectFarmEntityNetIds(HashSet<uint> output, out string source)
        {
            if (this.TryGetHomelandFarmPlayerPosition(out Vector3 playerPos))
            {
                float radius = Mathf.Max(this.homelandFarmWaterRadius, HomelandFarmDefaultWaterRadius);
                return this.TryHomelandFarmCollectFarmEntityNetIds(
                    output,
                    out source,
                    playerPos,
                    radius,
                    useAutoFarmCollectShortcuts: false);
            }

            return this.TryHomelandFarmCollectFarmEntityNetIds(
                output,
                out source,
                Vector3.zero,
                0f,
                useAutoFarmCollectShortcuts: false);
        }

        private bool TryHomelandFarmCollectFarmEntityNetIds(
            HashSet<uint> output,
            out string source,
            Vector3 scanCenter,
            float scanRadius,
            bool useAutoFarmCollectShortcuts = true)
        {
            return this.TryHomelandFarmCollectFarmEntityNetIds(
                output,
                out source,
                scanCenter,
                scanRadius,
                // EXPERIMENT (Option 4): route the default scan path through the AuraMono
                // Entities.GetComponents source so hotkey/capture/auto-farm all exercise it.
                allowUnsafeAuraMonoGetComponents: HomelandFarmAllowUnsafeAuraMonoGetComponents,
                proximityBudgetSeconds: HomelandFarmAuraProximityComponentScanBudgetSeconds,
                allowAuraEntityFunnel: true,
                useAutoFarmCollectShortcuts: useAutoFarmCollectShortcuts);
        }

        private bool TryHomelandFarmCollectFarmEntityNetIds(
            HashSet<uint> output,
            out string source,
            Vector3 scanCenter,
            float scanRadius,
            bool allowUnsafeAuraMonoGetComponents,
            float proximityBudgetSeconds = HomelandFarmAuraProximityComponentScanBudgetSeconds,
            bool allowAuraEntityFunnel = true,
            bool useAutoFarmCollectShortcuts = true)
        {
            source = string.Empty;
            if (output == null)
            {
                return false;
            }

            output.Clear();
            this.homelandFarmLastScanCropBoxNetIds.Clear();
            this.TryEnsureHomelandFarmInteropAssembliesLoaded();
            this.EnsureHomelandFarmScannerTypes();
            this.ResolveAuraFarmRuntimeMethods();
            List<string> sources = new List<string>(8);
            bool spatialScan = scanRadius > 0f && scanCenter != Vector3.zero;

            if (useAutoFarmCollectShortcuts
                && spatialScan
                && this.TryHomelandFarmCollectFarmNetIdsFromRegisteredCache(scanCenter, scanRadius, output, out int registeredAdded)
                && registeredAdded > 0)
            {
                sources.Add("RegisteredCache(" + registeredAdded + ")");
            }

            if (spatialScan
                && this.TryHomelandFarmCollectFarmNetIdsFromInteractSeeds(scanCenter, scanRadius, output, out int interactAdded)
                && interactAdded > 0)
            {
                sources.Add("InteractSeeds(" + interactAdded + ")");
            }

            int before = output.Count;
            // When the direct ECS Entities.GetComponents<T> source (ComponentRadius) succeeds it
            // returns the authoritative crop-box / crop / plant set without walking the entity graph.
            // It is the safe source — record success so we can skip the crash-prone AuraProximity /
            // AuraEntities loaded-entity walk below on EVERY path (hotkey, capture, auto-farm), not
            // just the shortcut path. That walk dereferences arbitrary mono pointers and is what
            // randomly AV-crashes on visiting/streaming fields.
            bool componentRadiusSucceeded = false;
            if (spatialScan
                && (allowUnsafeAuraMonoGetComponents || this.homelandFarmEntitiesGetComponentsMethod != null)
                && this.TryHomelandFarmCollectFarmNetIdsByComponentRadius(
                    scanCenter,
                    scanRadius,
                    output,
                    out int componentRadiusAdded,
                    allowUnsafeAuraMonoGetComponents)
                && componentRadiusAdded > 0)
            {
                componentRadiusSucceeded = true;
                sources.Add("ComponentRadius(" + componentRadiusAdded + ")");
            }

            before = output.Count;
            // For radius scans, try cheap spatial queries first; they usually return nearby
            // farm entities immediately and avoid a full Aura loaded-entity pass.
            if (spatialScan
                && this.TryHomelandFarmCollectSphereQueryFarmNetIds(scanCenter, scanRadius, output, out int sphereAdded)
                && sphereAdded > 0)
            {
                sources.Add("SphereQuery(" + sphereAdded + ")");
            }

            before = output.Count;
            if (spatialScan
                && this.TryHomelandFarmCollectFarmNetIdsFromAuraCylinder(scanCenter, scanRadius, output, out int cylinderAdded)
                && cylinderAdded > 0)
            {
                sources.Add("Cylinder(" + cylinderAdded + ")");
            }

            // Crop boxes / planters are level objects, not entities, so SphereQuery/Cylinder
            // (entity queries) miss them. Radius-filter the in-memory level-object position
            // cache here — it is the cheapest source (no reflection, no native invokes) and
            // catches the common water/sow targets before we ever touch the expensive funnel.
            before = output.Count;
            if (useAutoFarmCollectShortcuts
                && spatialScan
                && this.TryHomelandFarmCollectLevelObjectNetIdsFromPositionCache(scanCenter, scanRadius, output, out int levelCacheAdded)
                && levelCacheAdded > 0)
            {
                sources.Add("LevelObjectCache(" + levelCacheAdded + ")");
            }

            // Crop boxes are live entities — ComponentRadius (GetComponents) and proximity scan
            // above are the fast paths. Skip duplicate ComponentRadius call here.
            before = output.Count;
            // If the direct ECS source already returned the farm components, never run the
            // graph-walking proximity scan — it is redundant and is the native-AV crash source.
            bool runAuraProximity = spatialScan
                && !componentRadiusSucceeded
                && (!useAutoFarmCollectShortcuts || this.homelandFarmLastScanCropBoxNetIds.Count == 0);
            int proximityAdded = 0;
            int proximityInspected = 0;
            bool proximityScanRan = false;
            if (runAuraProximity)
            {
                proximityScanRan = this.TryHomelandFarmCollectFarmNetIdsFromAuraProximityComponentScan(
                    scanCenter,
                    scanRadius,
                    output,
                    out proximityAdded,
                    out proximityInspected,
                    proximityBudgetSeconds);
                if (proximityScanRan && proximityAdded > 0)
                {
                    sources.Add("AuraProximity(" + proximityAdded + "/" + proximityInspected + ")");
                }
            }

            // Aura funnel: auto farm skips when RegisteredCache/proximity already hit. Manual radius
            // scans skip when proximity classified in-radius entities — avoids a second 900+ entity
            // funnel immediately after water (water+weed crash on visiting fields).
            before = output.Count;
            bool skipAuraEntityFunnel = !allowAuraEntityFunnel
                // Direct ECS source already returned the authoritative set — skip the graph walk.
                || componentRadiusSucceeded
                || (useAutoFarmCollectShortcuts
                    && spatialScan
                    && (this.homelandFarmLastScanCropBoxNetIds.Count > 0 || output.Count > 0))
                // Manual radius scans (water/weed/hotkey): once the proximity scan has been attempted,
                // the heavy AuraMono loaded-entity enumeration is already done. Running the AuraEntities
                // funnel afterwards repeats that 900+ entity native pass — the doubled enumeration is
                // what crashes water+weed on other players' fields. Skip it whenever proximity ran,
                // regardless of how many it matched (proximityScanRan only reports added>0).
                || (!useAutoFarmCollectShortcuts
                    && spatialScan
                    && runAuraProximity);
            if (!skipAuraEntityFunnel)
            {
                if (this.TryHomelandFarmCollectFarmNetIdsFromAuraLoadedEntities(
                        scanCenter,
                        scanRadius,
                        output,
                        out int auraEntityAdded,
                        out int auraEntityInspected)
                    && auraEntityAdded > 0)
                {
                    sources.Add("AuraEntities(" + auraEntityAdded + "/" + auraEntityInspected + ")");
                }
            }
            else if (spatialScan)
            {
                sources.Add("AuraEntities(skipped,output=" + output.Count + ",cropBoxes=" + this.homelandFarmLastScanCropBoxNetIds.Count + ")");
            }

            // Radius actions already have fast spatial sources (Aura entities/cylinder/sphere).
            // Skip full-world component scans unless spatial sources found nothing.
            if (!spatialScan || output.Count == 0)
            {
                before = output.Count;
                this.TryHomelandFarmCollectComponentsNetIds(this.homelandFarmCropBoxComponentType, output, "CropBoxComponent");
                this.TryHomelandFarmCollectComponentsNetIds(this.homelandFarmPlantComponentType, output, "PlantComponent");
                this.TryHomelandFarmCollectComponentsNetIds(this.homelandFarmCropComponentType, output, "CropComponent");
                if (output.Count > before)
                {
                    sources.Add("Entities.GetComponents(" + (output.Count - before) + ")");
                }
            }

            if (sources.Count == 0)
            {
                return false;
            }

            if (!useAutoFarmCollectShortcuts && spatialScan && output.Count > 0)
            {
                this.HomelandFarmRememberManualRadiusCollect(output, scanCenter, scanRadius);
            }

            source = string.Join("+", sources.ToArray());
            return output.Count > 0;
        }

        private void HomelandFarmRememberManualRadiusCollect(HashSet<uint> netIds, Vector3 center, float radius)
        {
            this.homelandFarmLastManualRadiusCollectNetIds.Clear();
            if (netIds != null)
            {
                foreach (uint netId in netIds)
                {
                    if (netId != 0U)
                    {
                        this.homelandFarmLastManualRadiusCollectNetIds.Add(netId);
                    }
                }
            }

            this.homelandFarmLastManualRadiusCollectCenter = center;
            this.homelandFarmLastManualRadiusCollectRadius = radius;
            this.homelandFarmLastManualRadiusCollectAt = Time.realtimeSinceStartup;
            // Field owner pins the reuse to a specific plot/instance. Different homeland instances
            // share the SAME local coordinates, so a position-only key wrongly matches your field to
            // someone else's you just visited — reusing their now-unloaded netIds → native AV.
            this.homelandFarmLastManualRadiusCollectFieldOwner =
                this.TryHomelandFarmGetSelfPlayInFieldOwnerNetId(out uint rememberFieldOwner) ? rememberFieldOwner : 0U;
        }

        private bool TryHomelandFarmTryReuseManualRadiusCollect(Vector3 center, float radius, HashSet<uint> output)
        {
            if (output == null
                || this.homelandFarmLastManualRadiusCollectNetIds.Count == 0
                || Time.realtimeSinceStartup - this.homelandFarmLastManualRadiusCollectAt > HomelandFarmManualRadiusCollectReuseSeconds)
            {
                return false;
            }

            // Only reuse on the exact same field/instance. Require a known, matching field owner —
            // if it is unknown (0) or differs, re-scan instead of reusing another plot's stale netIds.
            if (this.homelandFarmLastManualRadiusCollectFieldOwner == 0U
                || !this.TryHomelandFarmGetSelfPlayInFieldOwnerNetId(out uint currentFieldOwner)
                || currentFieldOwner == 0U
                || currentFieldOwner != this.homelandFarmLastManualRadiusCollectFieldOwner)
            {
                return false;
            }

            float centerToleranceSq = HomelandFarmManualRadiusCollectCenterTolerance * HomelandFarmManualRadiusCollectCenterTolerance;
            if ((center - this.homelandFarmLastManualRadiusCollectCenter).sqrMagnitude > centerToleranceSq
                || Mathf.Abs(radius - this.homelandFarmLastManualRadiusCollectRadius) > 2f)
            {
                return false;
            }

            output.UnionWith(this.homelandFarmLastManualRadiusCollectNetIds);
            return output.Count > 0;
        }

        private bool TryHomelandFarmCollectSphereQueryFarmNetIds(Vector3 center, float radius, HashSet<uint> output, out int added)
        {
            added = 0;
            if (output == null || radius <= 0f)
            {
                return false;
            }

            HashSet<uint> sphereNetIds = new HashSet<uint>();
            if (!this.TryHomelandFarmSphereQueryNetIds(center, radius, sphereNetIds) || sphereNetIds.Count == 0)
            {
                return false;
            }

            foreach (uint netId in sphereNetIds)
            {
                if (netId == 0U)
                {
                    continue;
                }

                int before = output.Count;
                try
                {
                    this.TryHomelandFarmTryQuickAcceptFarmNetId(netId, output);
                }
                catch
                {
                }

                added += output.Count - before;
            }

            return added > 0;
        }

        private unsafe bool TryHomelandFarmCollectLevelObjectNetIdsAuraSpatial(HashSet<uint> output, Vector3 center, float radius, out string source)
        {
            source = "Aura LevelObjectManager(spatial)";
            if (output == null || radius <= 0f)
            {
                return false;
            }

            if (!this.TryResolveHomelandFarmAuraScanClasses(out string scanStatus))
            {
                source = scanStatus;
                return false;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                source = "AuraMono API unavailable.";
                return false;
            }

            if (!this.TryResolveAuraMonoLevelObjectManager(out IntPtr managerObj, out _, out string managerStatus)
                || managerObj == IntPtr.Zero)
            {
                source = managerStatus;
                return false;
            }

            IntPtr dictionaryObj = IntPtr.Zero;
            if ((!this.TryGetMonoObjectMember(managerObj, "_dictionary", out dictionaryObj) || dictionaryObj == IntPtr.Zero)
                && (!this.TryGetMonoObjectMember(managerObj, "dictionary", out dictionaryObj) || dictionaryObj == IntPtr.Zero))
            {
                source = "Aura LevelObjectManager dictionary unavailable.";
                return false;
            }

            List<IntPtr> entries = new List<IntPtr>();
            if (!this.TryEnumerateAuraMonoCollectionItems(dictionaryObj, entries) || entries.Count <= 0)
            {
                source = "Aura LevelObjectManager dictionary empty.";
                return false;
            }

            float radiusSq = radius * radius;
            int added = 0;
            int examined = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                IntPtr entry = entries[i];
                if (entry == IntPtr.Zero)
                {
                    continue;
                }

                examined++;
                IntPtr levelObjectObj = IntPtr.Zero;
                if ((!this.TryGetMonoObjectMember(entry, "Value", out levelObjectObj) || levelObjectObj == IntPtr.Zero)
                    && (!this.TryGetMonoObjectMember(entry, "value", out levelObjectObj) || levelObjectObj == IntPtr.Zero)
                    && (!this.TryGetMonoObjectMember(entry, "_value", out levelObjectObj) || levelObjectObj == IntPtr.Zero))
                {
                    levelObjectObj = entry;
                }

                if (!this.TryExtractHomePositionMonoObject(levelObjectObj, out Vector3 position) || position == Vector3.zero)
                {
                    continue;
                }

                if ((position - center).sqrMagnitude > radiusSq)
                {
                    continue;
                }

                try
                {
                    added += this.TryHomelandFarmTryAddLevelObjectFarmNetIds(levelObjectObj, entry, output);
                }
                catch (Exception ex)
                {
                    this.HomelandFarmLog("Spatial level object scan failed: " + ex.Message);
                }

                if (added >= HomelandFarmMaxSpatialLevelObjectEntries)
                {
                    break;
                }
            }

            source = "Aura LevelObjectManager(spatial " + added + "/" + examined + ")";
            return added > 0;
        }

        private unsafe bool TryHomelandFarmCollectFertilizeOwnerNetIdsNearby(
            List<uint> output,
            Vector3 center,
            float radius,
            int fertilizerStaticId,
            HashSet<uint> scanNetIds,
            int maxCount,
            out string source)
        {
            source = "LevelObject.ownerNetId(nearby)";
            if (output == null || radius <= 0f || maxCount <= 0 || fertilizerStaticId <= 0)
            {
                return false;
            }

            HashSet<uint> seen = new HashSet<uint>(output);
            if (!this.TryResolveHomelandFarmAuraScanClasses(out string scanStatus))
            {
                source = scanStatus;
                return false;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                source = "AuraMono API unavailable.";
                return false;
            }

            if (!this.TryResolveAuraMonoLevelObjectManager(out IntPtr managerObj, out _, out string managerStatus)
                || managerObj == IntPtr.Zero)
            {
                source = managerStatus;
                return false;
            }

            IntPtr dictionaryObj = IntPtr.Zero;
            if ((!this.TryGetMonoObjectMember(managerObj, "_dictionary", out dictionaryObj) || dictionaryObj == IntPtr.Zero)
                && (!this.TryGetMonoObjectMember(managerObj, "dictionary", out dictionaryObj) || dictionaryObj == IntPtr.Zero))
            {
                source = "Aura LevelObjectManager dictionary unavailable.";
                return false;
            }

            List<IntPtr> entries = new List<IntPtr>();
            if (!this.TryEnumerateAuraMonoCollectionItems(dictionaryObj, entries) || entries.Count <= 0)
            {
                source = "Aura LevelObjectManager dictionary empty.";
                return false;
            }

            float radiusSq = radius * radius;
            int added = 0;
            string[] ownerMembers = { "ownerNetId", "OwnerNetId" };
            for (int i = 0; i < entries.Count && output.Count < maxCount; i++)
            {
                IntPtr entry = entries[i];
                if (entry == IntPtr.Zero)
                {
                    continue;
                }

                IntPtr levelObjectObj = IntPtr.Zero;
                if ((!this.TryGetMonoObjectMember(entry, "Value", out levelObjectObj) || levelObjectObj == IntPtr.Zero)
                    && (!this.TryGetMonoObjectMember(entry, "value", out levelObjectObj) || levelObjectObj == IntPtr.Zero)
                    && (!this.TryGetMonoObjectMember(entry, "_value", out levelObjectObj) || levelObjectObj == IntPtr.Zero))
                {
                    levelObjectObj = entry;
                }

                if (!this.TryExtractHomePositionMonoObject(levelObjectObj, out Vector3 position) || position == Vector3.zero)
                {
                    continue;
                }

                if ((position - center).sqrMagnitude > radiusSq)
                {
                    continue;
                }

                uint ownerNetId = 0U;
                for (int m = 0; m < ownerMembers.Length && ownerNetId == 0U; m++)
                {
                    this.TryGetMonoUInt32Member(levelObjectObj, ownerMembers[m], out ownerNetId);
                }

                if (ownerNetId == 0U || seen.Contains(ownerNetId))
                {
                    continue;
                }

                if (!this.IsHomelandFarmCropFertilizable(ownerNetId, fertilizerStaticId, scanNetIds, out _))
                {
                    continue;
                }

                seen.Add(ownerNetId);
                output.Add(ownerNetId);
                added++;
            }

            source = "LevelObject.ownerNetId(spatial " + added + ")";
            return added > 0;
        }

        private List<uint> ScanHomelandFarmFertilizeTargetsFromLevelObjects(
            int fertilizerStaticId,
            HashSet<uint> scanNetIds,
            int maxCount)
        {
            List<uint> result = new List<uint>();
            if (maxCount <= 0 || !this.TryGetHomelandFarmPlayerPosition(out Vector3 playerPos))
            {
                return result;
            }

            this.TryHomelandFarmCollectFertilizeOwnerNetIdsNearby(
                result,
                playerPos,
                this.homelandFarmWaterRadius + 2f,
                fertilizerStaticId,
                scanNetIds,
                maxCount,
                out _);
            return result;
        }

        private List<uint> MergeHomelandFarmFertilizeTargets(List<uint> primary, List<uint> fallback, int maxCount)
        {
            List<uint> merged = new List<uint>();
            HashSet<uint> seen = new HashSet<uint>();
            if (primary != null)
            {
                for (int i = 0; i < primary.Count && merged.Count < maxCount; i++)
                {
                    uint netId = primary[i];
                    if (netId == 0U || !seen.Add(netId))
                    {
                        continue;
                    }

                    merged.Add(netId);
                }
            }

            if (fallback != null)
            {
                for (int i = 0; i < fallback.Count && merged.Count < maxCount; i++)
                {
                    uint netId = fallback[i];
                    if (netId == 0U || !seen.Add(netId))
                    {
                        continue;
                    }

                    merged.Add(netId);
                }
            }

            return merged;
        }

        private bool TryHomelandFarmCollectLevelObjectNetIdsNearby(HashSet<uint> output, Vector3 center, float radius, out string source)
        {
            source = "LevelObjectManager(nearby)";
            if (output == null || radius <= 0f)
            {
                return false;
            }

            try
            {
                Type levelObjectManagerType = this.FindLevelObjectManagerRuntimeType();
                if (levelObjectManagerType == null)
                {
                    source = "LevelObjectManager type unavailable.";
                    return false;
                }

                PropertyInfo instanceProperty = levelObjectManagerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                object levelObjectManager = instanceProperty != null ? instanceProperty.GetValue(null, null) : null;
                if (levelObjectManager == null)
                {
                    source = "LevelObjectManager.Instance unavailable.";
                    return false;
                }

                object dictionaryObj = this.TryGetManagedMemberValue(levelObjectManager, "_dictionary")
                    ?? this.TryGetManagedMemberValue(levelObjectManager, "dictionary");
                if (!(dictionaryObj is IEnumerable enumerable))
                {
                    source = "LevelObjectManager dictionary unavailable.";
                    return false;
                }

                float radiusSq = radius * radius;
                int added = 0;
                int examined = 0;
                foreach (object entry in enumerable)
                {
                    if (entry == null)
                    {
                        continue;
                    }

                    examined++;
                    object levelObject = this.TryGetManagedMemberValue(entry, "Value") ?? entry;
                    if (levelObject == null)
                    {
                        continue;
                    }

                    if (!this.TryResolvePositionFromManagedObject(levelObject, out Vector3 position) || position == Vector3.zero)
                    {
                        continue;
                    }

                    if ((position - center).sqrMagnitude > radiusSq)
                    {
                        continue;
                    }

                    added += this.TryHomelandFarmTryAddLevelObjectFarmNetIds(levelObject, entry, output);
                }

                source = "LevelObjectManager(nearby " + added + "/" + examined + ")";
                return added > 0;
            }
            catch (Exception ex)
            {
                source = "LevelObjectManager nearby failed: " + ex.Message;
                return false;
            }
        }

        // Cheap radius scan over the in-memory level-object position cache. Crop boxes and
        // planters (the usual water/sow targets) are level objects, so SphereQueryEntities —
        // which only returns live entities — never surfaces them. This mirrors how AuraFarm
        // reads the game's near-player level-object lists instead of enumerating everything,
        // and lets us avoid the expensive AuraEntities funnel for the common case.
        private bool TryHomelandFarmCollectLevelObjectNetIdsFromPositionCache(Vector3 center, float radius, HashSet<uint> output, out int added)
        {
            added = 0;
            if (output == null || radius <= 0f || center == Vector3.zero)
            {
                return false;
            }

            this.TryHomelandFarmCacheAuraLevelObjectPositions(false, allowDictionaryScan: false);
            if (this.homelandFarmAuraLevelObjectPositionCache.Count == 0)
            {
                return false;
            }

            float radiusSq = radius * radius;
            // Snapshot so quick-accept (which may refresh caches) cannot mutate the dictionary mid-iteration.
            List<KeyValuePair<uint, Vector3>> snapshot = new List<KeyValuePair<uint, Vector3>>(this.homelandFarmAuraLevelObjectPositionCache);
            for (int i = 0; i < snapshot.Count; i++)
            {
                uint netId = snapshot[i].Key;
                if (netId == 0U || netId >= 0x80000000U)
                {
                    continue;
                }

                Vector3 pos = snapshot[i].Value;
                if (pos == Vector3.zero || (pos - center).sqrMagnitude > radiusSq)
                {
                    continue;
                }

                int before = output.Count;
                try
                {
                    this.TryHomelandFarmTryQuickAcceptFarmNetId(netId, output);
                }
                catch (Exception ex)
                {
                    this.HomelandFarmLog("LevelObject cache accept failed netId=" + netId + ": " + ex.Message);
                }

                added += output.Count - before;
            }

            return added > 0;
        }

        private bool TryHomelandFarmCollectFarmNetIdsFromAuraCylinder(Vector3 center, float radius, HashSet<uint> output, out int added)
        {
            added = 0;
            if (output == null || radius <= 0f)
            {
                return false;
            }

            if (this.auraLevelObjectManagerType == null
                || this.auraLevelObjectManagerCylinderOverlapNonAllocMethod == null
                || this.auraCylinderType == null
                || this.auraLevelObjectTagType == null)
            {
                this.TryEnsureHomelandFarmInteropAssembliesLoaded();
                this.ResolveAuraFarmRuntimeMethods();
            }

            if (this.auraLevelObjectManagerType == null
                || this.auraLevelObjectManagerCylinderOverlapNonAllocMethod == null
                || this.auraCylinderType == null
                || this.auraLevelObjectTagType == null)
            {
                return false;
            }

            object levelObjectManager = this.GetAuraLevelObjectManagerInstance();
            if (levelObjectManager == null)
            {
                return false;
            }

            try
            {
                object cylinder = Activator.CreateInstance(this.auraCylinderType);
                if (cylinder == null)
                {
                    return false;
                }

                float height = 6f;
                Vector3 cylinderCenter = center + (height * 0.5f) * Vector3.up;
                this.SetAuraCylinderValue(cylinder, cylinderCenter, radius, height);

                Type levelObjectType = null;
                if (this.auraEntityUtilGetLevelObjectMethod != null)
                {
                    levelObjectType = this.auraEntityUtilGetLevelObjectMethod.ReturnType;
                }
                else if (this.auraEntityHelperGetLevelObjectMethod != null)
                {
                    levelObjectType = this.auraEntityHelperGetLevelObjectMethod.ReturnType;
                }
                else if (this.auraLevelObjectNetIdField != null)
                {
                    levelObjectType = this.auraLevelObjectNetIdField.DeclaringType;
                }
                else if (this.auraLevelObjectNetIdProperty != null)
                {
                    levelObjectType = this.auraLevelObjectNetIdProperty.DeclaringType;
                }

                if (levelObjectType == null)
                {
                    return false;
                }

                object results = Activator.CreateInstance(typeof(List<>).MakeGenericType(levelObjectType));
                if (results == null)
                {
                    return false;
                }

                object interactableTag = Enum.Parse(this.auraLevelObjectTagType, "Interactable");
                LayerMask layerMask = int.MaxValue;
                this.auraLevelObjectManagerCylinderOverlapNonAllocMethod.Invoke(
                    levelObjectManager,
                    new object[] { cylinder, results, layerMask, interactableTag, -1 });

                if (!(results is IEnumerable enumerable))
                {
                    return false;
                }

                foreach (object levelObject in enumerable)
                {
                    if (levelObject == null)
                    {
                        continue;
                    }

                    try
                    {
                        added += this.TryHomelandFarmTryAddLevelObjectFarmNetIds(levelObject, null, output);
                    }
                    catch (Exception ex)
                    {
                        this.HomelandFarmLog("Cylinder level object failed: " + ex.Message);
                    }

                    if (added >= HomelandFarmMaxSpatialLevelObjectEntries)
                    {
                        break;
                    }
                }

                return added > 0;
            }
            catch (Exception ex)
            {
                this.HomelandFarmLog("Cylinder farm scan failed: " + ex.Message);
                return false;
            }
        }

        private bool TryHomelandFarmCollectCropEntityNetIds(HashSet<uint> output, out string source)
        {
            source = string.Empty;
            if (output == null)
            {
                return false;
            }

            this.EnsureHomelandFarmScannerTypes();
            int before = output.Count;
            this.TryHomelandFarmCollectComponentsNetIds(this.homelandFarmCropComponentType, output, "CropComponent");
            this.TryHomelandFarmCollectComponentsNetIds(this.homelandFarmCropBoxComponentType, output, "CropBoxComponent");
            if (output.Count > before)
            {
                source = "Entities.GetComponents(crop)";
                return true;
            }

            return this.TryHomelandFarmCollectLevelObjectNetIds(output, out source)
                || (this.TryResolveHomelandFarmAuraScanClasses(out _)
                    && this.TryHomelandFarmCollectLevelObjectNetIdsAura(output, out source)
                    && output.Count > 0);
        }

        private bool TryHomelandFarmCollectPlantEntityNetIds(HashSet<uint> output, out string source)
        {
            source = string.Empty;
            if (output == null)
            {
                return false;
            }

            this.EnsureHomelandFarmScannerTypes();
            int before = output.Count;
            this.TryHomelandFarmCollectComponentsNetIds(this.homelandFarmPlantComponentType, output, "PlantComponent");
            if (output.Count > before)
            {
                source = "Entities.GetComponents(plant)";
                return true;
            }

            return this.TryHomelandFarmCollectLevelObjectNetIds(output, out source)
                || (this.TryResolveHomelandFarmAuraScanClasses(out _)
                    && this.TryHomelandFarmCollectLevelObjectNetIdsAura(output, out source)
                    && output.Count > 0);
        }

        // Targeted, radius-filtered farm collection via Entities.GetComponents<T>(). Crop boxes are
        // live entities (not level objects), so the cheap level-object cache never surfaces them and
        // the only correct fast source is a component-type query, which returns ONLY farm entities
        // directly instead of enumerating all ~4096 loaded entities. We then resolve positions for
        // just that small farm set and radius-filter, mirroring how AuraFarm queries by intent.
        private bool TryHomelandFarmCollectFarmNetIdsByComponentRadius(
            Vector3 center,
            float radius,
            HashSet<uint> output,
            out int added,
            bool allowUnsafeAuraMonoGetComponents)
        {
            added = 0;
            if (output == null || radius <= 0f || center == Vector3.zero)
            {
                return false;
            }

            this.EnsureHomelandFarmScannerTypes();
            if (this.homelandFarmEntitiesGetComponentsMethod == null)
            {
                if (!allowUnsafeAuraMonoGetComponents)
                {
                    if (!this.homelandFarmComponentRadiusWarned)
                    {
                        this.homelandFarmComponentRadiusWarned = true;
                        this.HomelandFarmLog("ComponentRadius unavailable: managed Entities.GetComponents unavailable. Suppressing further notices.");
                    }

                    return false;
                }

                // EXPERIMENT (Option 4): managed Entities.GetComponents is absent on this build
                // (entitiesType=False). On the unsafe path require only AuraMono readiness — do
                // NOT gate on the managed resolver, which always fails here and used to bail out
                // before the AuraMono GetComponents branch below ever ran.
                if (!this.TryHomelandFarmIsAuraMonoGetComponentsReady(out string auraResolveStatus))
                {
                    this.HomelandFarmLog("ComponentRadius[AuraMono] not ready: " + auraResolveStatus);
                    return false;
                }

                this.HomelandFarmVerboseLog("ComponentRadius[AuraMono] ready, using AuraMono GetComponents (managed absent).");
            }
            else if (!this.TryEnsureHomelandFarmEntitiesGetComponentsReady(out _))
            {
                return false;
            }

            HashSet<uint> cropBoxComponentNetIds = new HashSet<uint>();
            HashSet<uint> componentNetIds = new HashSet<uint>();
            if (this.homelandFarmEntitiesGetComponentsMethod != null)
            {
                this.TryHomelandFarmCollectComponentsNetIds(this.homelandFarmCropBoxComponentType, cropBoxComponentNetIds, "CropBoxComponent(radius)");
                foreach (uint cropBoxNetId in cropBoxComponentNetIds)
                {
                    componentNetIds.Add(cropBoxNetId);
                }

                this.TryHomelandFarmCollectComponentsNetIds(this.homelandFarmCropComponentType, componentNetIds, "CropComponent(radius)");
                this.TryHomelandFarmCollectComponentsNetIds(this.homelandFarmPlantComponentType, componentNetIds, "PlantComponent(radius)");
            }
            else if (allowUnsafeAuraMonoGetComponents && this.TryHomelandFarmIsAuraMonoGetComponentsReady(out _))
            {
                this.TryResolveAuraMonoFarmComponentClasses(
                    out IntPtr plantComponentClass,
                    out IntPtr cropBoxComponentClass,
                    out IntPtr cropComponentClass);
                this.TryHomelandFarmCollectComponentsNetIdsViaAuraMono(cropBoxComponentClass, cropBoxComponentNetIds, "CropBoxComponent(radius)", isCropBox: true);
                foreach (uint cropBoxNetId in cropBoxComponentNetIds)
                {
                    componentNetIds.Add(cropBoxNetId);
                }

                this.TryHomelandFarmCollectComponentsNetIdsViaAuraMono(cropComponentClass, componentNetIds, "CropComponent(radius)", isCropBox: false);
                this.TryHomelandFarmCollectComponentsNetIdsViaAuraMono(plantComponentClass, componentNetIds, "PlantComponent(radius)", isCropBox: false);
            }

            if (componentNetIds.Count == 0)
            {
                this.HomelandFarmLog("ComponentRadius: GetComponents returned 0 farm components"
                    + " (cropBoxType=" + (this.homelandFarmCropBoxComponentType != null)
                    + " cropType=" + (this.homelandFarmCropComponentType != null)
                    + " plantType=" + (this.homelandFarmPlantComponentType != null) + ").");
                return false;
            }

            float radiusSq = radius * radius;
            foreach (uint netId in componentNetIds)
            {
                if (netId == 0U || netId >= 0x80000000U)
                {
                    continue;
                }

                // Keep entities whose world position cannot be resolved (some crop boxes are parented
                // and do not expose a position); the diagnostic/scan stage applies the final radius gate.
                if (this.TryHomelandFarmResolveFarmEntityPosition(netId, out Vector3 pos)
                    && pos != Vector3.zero
                    && (pos - center).sqrMagnitude > radiusSq)
                {
                    continue;
                }

                if (output.Add(netId))
                {
                    added++;
                    bool isCropBox = cropBoxComponentNetIds.Contains(netId);
                    if (isCropBox)
                    {
                        this.homelandFarmLastScanCropBoxNetIds.Add(netId);
                    }

                    this.TryHomelandFarmRegisterDiscoveredFarmTarget(netId, isCropBox);
                }
            }

            return added > 0;
        }

        private bool TryHomelandFarmCollectComponentsNetIds(Type componentType, HashSet<uint> output, string label)
        {
            if (output == null)
            {
                return false;
            }

            if (componentType != null
                && this.TryEnsureHomelandFarmEntitiesGetComponentsReady(out _)
                && this.homelandFarmEntitiesGetComponentsMethod != null)
            {
                try
                {
                    Type listType = typeof(List<>).MakeGenericType(componentType);
                    object componentList = Activator.CreateInstance(listType);
                    object[] args = new object[] { componentList };
                    this.homelandFarmEntitiesGetComponentsMethod.MakeGenericMethod(componentType).Invoke(null, args);
                    object results = args[0] ?? componentList;
                    if (!(results is IEnumerable enumerable))
                    {
                        return false;
                    }

                    int added = 0;
                    foreach (object component in enumerable)
                    {
                        if (component == null)
                        {
                            continue;
                        }

                        if (!this.TryHomelandFarmTryReadComponentNetId(component, out uint netId) || netId == 0U)
                        {
                            continue;
                        }

                        if (output.Add(netId))
                        {
                            added++;
                            if (componentType == this.homelandFarmCropBoxComponentType)
                            {
                                this.homelandFarmLastScanCropBoxNetIds.Add(netId);
                            }

                            this.TryHomelandFarmRegisterDiscoveredFarmTarget(
                                netId,
                                componentType == this.homelandFarmCropBoxComponentType);
                        }
                    }

                    if (added > 0)
                    {
                        this.HomelandFarmLog(label + " scan added " + added + " netId(s).");
                    }

                    return added > 0;
                }
                catch (Exception ex)
                {
                    this.HomelandFarmLog(label + " scan failed: " + ex.Message);
                    return false;
                }
            }

            if (!HomelandFarmAllowUnsafeAuraMonoGetComponents)
            {
                return false;
            }

            bool getCompReady = this.TryHomelandFarmIsAuraMonoGetComponentsReady(out string getCompReadyStatus);
            this.HomelandFarmVerboseLog("ComponentRadius[AuraMono]: " + label + " getCompReady=" + getCompReady + " (" + getCompReadyStatus + ")");
            if (!getCompReady)
            {
                return false;
            }

            if (!this.TryResolveHomelandFarmAuraMonoComponentClassForManagedType(componentType, label, out IntPtr componentClass)
                || componentClass == IntPtr.Zero)
            {
                this.HomelandFarmVerboseLog("ComponentRadius[AuraMono]: " + label + " component class unresolved, skipping.");
                return false;
            }

            bool isCropBox = componentType == this.homelandFarmCropBoxComponentType
                || (!string.IsNullOrEmpty(label) && label.IndexOf("CropBox", StringComparison.OrdinalIgnoreCase) >= 0);
            this.HomelandFarmVerboseLog("ComponentRadius[AuraMono]: GetComponents<" + label + "> START class=0x" + componentClass.ToInt64().ToString("X") + " isCropBox=" + isCropBox);
            int beforeCount = output.Count;
            bool ok = this.TryHomelandFarmCollectComponentsNetIdsViaAuraMono(componentClass, output, label, isCropBox);
            this.HomelandFarmVerboseLog("ComponentRadius[AuraMono]: GetComponents<" + label + "> DONE ok=" + ok + " got " + (output.Count - beforeCount) + " new netId(s).");
            return ok;
        }

        // Shared inflate/invoke gate for any ViewComponent query (farm crops, meteor logic/view, bubble, …).
        // Does NOT require homeland farm component classes — only Entities + generic infra.
        private bool TryAuraMonoEntitiesGetComponentsInfraReady(out string status)
        {
            status = string.Empty;
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                status = "AuraMono API unavailable.";
                return false;
            }

            if (!this.TryResolveHomelandFarmAuraScanClasses(out status) || this.homelandFarmAuraEntitiesClass == IntPtr.Zero)
            {
                status = string.IsNullOrEmpty(status) ? "Aura Entities class unavailable." : status;
                return false;
            }

            if (this.homelandFarmAuraEntitiesGetComponentsMethod == IntPtr.Zero)
            {
                this.homelandFarmAuraEntitiesGetComponentsMethod = this.FindAuraMonoMethodOnHierarchy(
                    this.homelandFarmAuraEntitiesClass,
                    "GetComponents",
                    1);
            }

            if (this.homelandFarmAuraEntitiesGetComponentsMethod == IntPtr.Zero)
            {
                status = "AuraMono Entities.GetComponents unavailable.";
                return false;
            }

            if (auraMonoClassInflateGenericMethod == null || auraMonoClassGetType == null)
            {
                status = "AuraMono generic method inflate unavailable.";
                return false;
            }

            if (!HomelandFarmAllowUnsafeAuraMonoGetComponents)
            {
                status = "AuraMono GetComponents disabled (unsafe on embedded mono).";
                return false;
            }

            status = "AuraMono Entities.GetComponents infra ready.";
            return true;
        }

        private bool TryHomelandFarmIsAuraMonoGetComponentsReady(out string status)
        {
            if (!this.TryAuraMonoEntitiesGetComponentsInfraReady(out status))
            {
                return false;
            }

            if (!this.TryResolveAuraMonoFarmComponentClasses(out IntPtr plantClass, out IntPtr cropBoxClass, out IntPtr cropClass)
                || (plantClass == IntPtr.Zero && cropBoxClass == IntPtr.Zero && cropClass == IntPtr.Zero))
            {
                status = "AuraMono farm component classes unavailable.";
                return false;
            }

            status = "AuraMono Entities.GetComponents ready.";
            return true;
        }

        private bool TryResolveHomelandFarmAuraMonoComponentClassForManagedType(Type componentType, string label, out IntPtr componentClass)
        {
            componentClass = IntPtr.Zero;
            if (!this.TryResolveAuraMonoFarmComponentClasses(out IntPtr plantClass, out IntPtr cropBoxClass, out IntPtr cropClass))
            {
                return false;
            }

            if (componentType == this.homelandFarmCropBoxComponentType)
            {
                componentClass = cropBoxClass;
            }
            else if (componentType == this.homelandFarmPlantComponentType)
            {
                componentClass = plantClass;
            }
            else if (componentType == this.homelandFarmCropComponentType)
            {
                componentClass = cropClass;
            }
            else if (!string.IsNullOrEmpty(label))
            {
                if (label.IndexOf("CropBox", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    componentClass = cropBoxClass;
                }
                else if (label.IndexOf("Plant", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    componentClass = plantClass;
                }
                else if (label.IndexOf("Crop", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    componentClass = cropClass;
                }
            }

            return componentClass != IntPtr.Zero;
        }

        private string[] BuildHomelandFarmAuraMonoListTypeCandidates(IntPtr componentClass)
        {
            if (componentClass == IntPtr.Zero)
            {
                return Array.Empty<string>();
            }

            string displayName = this.GetAuraMonoClassDisplayName(componentClass);
            if (string.IsNullOrEmpty(displayName))
            {
                return Array.Empty<string>();
            }

            return new string[]
            {
                "System.Collections.Generic.List`1[[" + displayName + ", XDTLevelAndEntity]]",
                "System.Collections.Generic.List`1[[" + displayName + ", ScriptsRefactory.LevelAndEntity]]",
                "System.Collections.Generic.List`1[[" + displayName + ", Client]]",
                "System.Collections.Generic.List`1[[" + displayName + ", EcsClient]]",
                "System.Collections.Generic.List`1[[" + displayName + ", Assembly-CSharp]]"
            };
        }

        private unsafe bool TryHomelandFarmCreateAuraMonoComponentList(IntPtr componentClass, out IntPtr listObj, out string status)
        {
            listObj = IntPtr.Zero;
            status = string.Empty;
            if (componentClass == IntPtr.Zero)
            {
                status = "component class missing";
                return false;
            }

            if (this.homelandFarmAuraComponentListClassByComponentClass.TryGetValue(componentClass, out IntPtr cachedListClass)
                && cachedListClass != IntPtr.Zero
                && auraMonoObjectNew != null)
            {
                listObj = auraMonoObjectNew(this.auraMonoRootDomain, cachedListClass);
                if (listObj != IntPtr.Zero)
                {
                    if (auraMonoRuntimeObjectInit != null)
                    {
                        auraMonoRuntimeObjectInit(listObj);
                    }

                    return true;
                }
            }

            if (auraMonoStringNew == null
                || auraMonoRuntimeInvoke == null
                || this.auraMonoTypeGetTypeMethodPtr == IntPtr.Zero
                || this.auraMonoActivatorCreateInstanceMethodPtr == IntPtr.Zero
                || auraMonoObjectGetClass == null)
            {
                status = "AuraMono list prerequisites unavailable";
                return false;
            }

            string[] candidates = this.BuildHomelandFarmAuraMonoListTypeCandidates(componentClass);
            IntPtr* typeArgs = stackalloc IntPtr[1];
            IntPtr* createArgs = stackalloc IntPtr[1];
            for (int i = 0; i < candidates.Length; i++)
            {
                IntPtr typeNameObj = auraMonoStringNew(this.auraMonoRootDomain, candidates[i]);
                if (typeNameObj == IntPtr.Zero)
                {
                    continue;
                }

                typeArgs[0] = typeNameObj;
                IntPtr exc = IntPtr.Zero;
                IntPtr typeObj = auraMonoRuntimeInvoke(this.auraMonoTypeGetTypeMethodPtr, IntPtr.Zero, (IntPtr)typeArgs, ref exc);
                if (exc != IntPtr.Zero || typeObj == IntPtr.Zero)
                {
                    continue;
                }

                createArgs[0] = typeObj;
                exc = IntPtr.Zero;
                listObj = auraMonoRuntimeInvoke(this.auraMonoActivatorCreateInstanceMethodPtr, IntPtr.Zero, (IntPtr)createArgs, ref exc);
                if (exc != IntPtr.Zero || listObj == IntPtr.Zero)
                {
                    listObj = IntPtr.Zero;
                    continue;
                }

                IntPtr listClass = auraMonoObjectGetClass(listObj);
                if (listClass != IntPtr.Zero)
                {
                    this.homelandFarmAuraComponentListClassByComponentClass[componentClass] = listClass;
                }

                return true;
            }

            status = "AuraMono List<T> create failed";
            return false;
        }

        private unsafe bool TryHomelandFarmTryResolveInflatedAuraEntitiesGetComponentsMethod(
            IntPtr componentClass,
            out IntPtr inflatedMethod)
        {
            inflatedMethod = IntPtr.Zero;
            if (componentClass == IntPtr.Zero
                || this.homelandFarmAuraEntitiesGetComponentsMethod == IntPtr.Zero
                || auraMonoClassInflateGenericMethod == null
                || auraMonoClassGetType == null)
            {
                return false;
            }

            if (this.homelandFarmAuraInflatedGetComponentsMethodByComponentClass.TryGetValue(componentClass, out inflatedMethod)
                && inflatedMethod != IntPtr.Zero)
            {
                return true;
            }

            IntPtr componentType = auraMonoClassGetType(componentClass);
            if (componentType == IntPtr.Zero)
            {
                return false;
            }

            // mono_class_inflate_generic_method expects context.method_inst to be a MonoGenericInst*,
            // NOT a raw MonoType*[]. Passing the bare array made inflate read type_argc from the wrong
            // offset (garbage size) and walk out of bounds -> native AV during inflation. Build a real
            // interned MonoGenericInst via mono_metadata_get_generic_inst(argc, MonoType**).
            if (auraMonoMetadataGetGenericInst == null)
            {
                this.HomelandFarmLog("AuraMono inflate: mono_metadata_get_generic_inst export missing; cannot build generic inst safely.");
                return false;
            }

            IntPtr* typeArgs = stackalloc IntPtr[1];
            typeArgs[0] = componentType;
            this.HomelandFarmVerboseLog("AuraMono inflate step3a: building generic inst (argc=1).");
            IntPtr genericInst = auraMonoMetadataGetGenericInst(1, (IntPtr)typeArgs);
            if (genericInst == IntPtr.Zero)
            {
                this.HomelandFarmLog("AuraMono inflate: get_generic_inst returned null.");
                return false;
            }

            MonoGenericContext context = new MonoGenericContext
            {
                class_inst = IntPtr.Zero,
                method_inst = genericInst
            };

            this.HomelandFarmVerboseLog("AuraMono inflate step3b: inst=0x" + genericInst.ToInt64().ToString("X") + ", calling inflate_generic_method (AV risk here).");
            inflatedMethod = auraMonoClassInflateGenericMethod(this.homelandFarmAuraEntitiesGetComponentsMethod, ref context);
            if (inflatedMethod == IntPtr.Zero)
            {
                this.HomelandFarmLog("AuraMono inflate: inflate_generic_method returned null.");
                return false;
            }

            this.HomelandFarmVerboseLog("AuraMono inflate step3c: inflated=0x" + inflatedMethod.ToInt64().ToString("X") + ", compiling (AV risk here).");
            if (auraMonoCompileMethod != null)
            {
                try
                {
                    auraMonoCompileMethod(inflatedMethod);
                }
                catch
                {
                }
            }

            this.HomelandFarmVerboseLog("AuraMono inflate step3d: inflate+compile OK.");

            // Inflated GetComponents<T> must still take exactly 1 parameter (ref list); a
            // mismatched method_inst would AV the process on invoke instead of throwing.
            if (!AuraMonoMethodParamCountIs(inflatedMethod, 1))
            {
                this.HomelandFarmLog("AuraMono inflate: inflated method signature mismatch, refusing to invoke.");
                return false;
            }

            this.homelandFarmAuraInflatedGetComponentsMethodByComponentClass[componentClass] = inflatedMethod;
            return true;
        }

        private unsafe bool TryHomelandFarmCollectComponentsNetIdsViaAuraMono(
            IntPtr componentClass,
            HashSet<uint> output,
            string label,
            bool isCropBox)
        {
            if (componentClass == IntPtr.Zero || output == null || !HomelandFarmAllowUnsafeAuraMonoGetComponents)
            {
                return false;
            }

            if (this.homelandFarmAuraGetComponentsFailedComponentClasses.Contains(componentClass))
            {
                this.HomelandFarmVerboseLog(label + " AuraMono GetComponents: class previously failed, skipping.");
                return false;
            }

            this.HomelandFarmVerboseLog(label + " ViaAuraMono step1: ready check.");
            if (!this.TryHomelandFarmIsAuraMonoGetComponentsReady(out string readyStatus))
            {
                if (!this.homelandFarmAuraGetComponentsUnavailableLogged)
                {
                    this.homelandFarmAuraGetComponentsUnavailableLogged = true;
                    this.HomelandFarmLog("AuraMono GetComponents unavailable: " + readyStatus);
                }

                return false;
            }

            this.HomelandFarmVerboseLog(label + " ViaAuraMono step2: creating List<T>.");
            if (!this.TryHomelandFarmCreateAuraMonoComponentList(componentClass, out IntPtr listObj, out string listStatus)
                || listObj == IntPtr.Zero)
            {
                this.homelandFarmAuraGetComponentsFailedComponentClasses.Add(componentClass);
                this.HomelandFarmLog(label + " AuraMono list create failed: " + listStatus);
                return false;
            }

            this.HomelandFarmVerboseLog(label + " ViaAuraMono step3: inflating generic method.");
            if (!this.TryHomelandFarmTryResolveInflatedAuraEntitiesGetComponentsMethod(componentClass, out IntPtr inflatedGetComponentsMethod)
                || inflatedGetComponentsMethod == IntPtr.Zero)
            {
                this.homelandFarmAuraGetComponentsFailedComponentClasses.Add(componentClass);
                if (!this.homelandFarmAuraGetComponentsUnavailableLogged)
                {
                    this.homelandFarmAuraGetComponentsUnavailableLogged = true;
                    this.HomelandFarmLog("AuraMono GetComponents inflate failed.");
                }

                return false;
            }

            // The managed signature is `static void GetComponents<T>(ref List<T> outList)` — the
            // parameter is BY-REF. mono_runtime_invoke therefore expects params[0] to be a pointer
            // to the list pointer (List**), not the list object itself. Passing the bare object
            // pointer made mono treat the object header as the ref slot address -> native AV.
            this.HomelandFarmVerboseLog(label + " ViaAuraMono step4: INVOKE inflated method=0x" + inflatedGetComponentsMethod.ToInt64().ToString("X") + " list=0x" + listObj.ToInt64().ToString("X") + " (ref param) (AV risk here).");
            IntPtr* listSlot = stackalloc IntPtr[1];
            listSlot[0] = listObj;
            IntPtr* invokeArgs = stackalloc IntPtr[1];
            invokeArgs[0] = (IntPtr)listSlot;
            IntPtr exc = IntPtr.Zero;
            auraMonoRuntimeInvoke(inflatedGetComponentsMethod, IntPtr.Zero, (IntPtr)invokeArgs, ref exc);
            if (exc != IntPtr.Zero)
            {
                this.homelandFarmAuraGetComponentsFailedComponentClasses.Add(componentClass);
                this.HomelandFarmLog(label + " AuraMono GetComponents invoke failed exc=0x" + exc.ToInt64().ToString("X") + ".");
                return false;
            }

            // GetAllComponents fills the same list in place, but a ref param may also reassign it,
            // so read the slot back and enumerate whatever the method left there.
            IntPtr resultList = listSlot[0] != IntPtr.Zero ? listSlot[0] : listObj;
            this.HomelandFarmVerboseLog(label + " ViaAuraMono step5: invoke OK, enumerating list=0x" + resultList.ToInt64().ToString("X") + ".");
            List<IntPtr> components = new List<IntPtr>();
            if (!this.TryEnumerateAuraMonoCollectionItems(resultList, components) || components.Count == 0)
            {
                this.HomelandFarmVerboseLog(label + " ViaAuraMono: list empty (0 components).");
                return false;
            }

            this.HomelandFarmVerboseLog(label + " ViaAuraMono step6: list has " + components.Count + " component(s), reading netIds.");

            int added = 0;
            for (int i = 0; i < components.Count; i++)
            {
                IntPtr componentObj = components[i];
                if (componentObj == IntPtr.Zero)
                {
                    continue;
                }

                if (!this.TryHomelandFarmTryReadAuraMonoComponentNetId(componentObj, out uint netId) || netId == 0U)
                {
                    continue;
                }

                if (output.Add(netId))
                {
                    added++;
                    if (isCropBox)
                    {
                        this.homelandFarmLastScanCropBoxNetIds.Add(netId);
                    }

                    this.TryHomelandFarmRegisterDiscoveredFarmTarget(netId, isCropBox);
                }
            }

            if (added > 0)
            {
                this.HomelandFarmLog(label + " AuraMono scan added " + added + " netId(s).");
            }

            return added > 0;
        }

        // Generic, reusable AuraMono Entities.GetComponents<T>: given a resolved component mono class,
        // returns the live component object pointers directly — WITHOUT the crash-prone recursive
        // entity-graph walk (TryEnumerateAuraMonoLoadedEntityObjects). Shares the exact inflate/invoke
        // infrastructure the homeland-farm path proved out. The returned IntPtrs are valid only in the
        // current synchronous scope; scalarize (netId/fields) before any coroutine yield.
        private unsafe bool TryAuraMonoGetComponentObjects(IntPtr componentClass, out List<IntPtr> components)
        {
            components = null;
            if (componentClass == IntPtr.Zero || !HomelandFarmAllowUnsafeAuraMonoGetComponents)
            {
                return false;
            }

            if (!this.TryAuraMonoEntitiesGetComponentsInfraReady(out _))
            {
                return false;
            }

            if (!this.TryHomelandFarmCreateAuraMonoComponentList(componentClass, out IntPtr listObj, out _)
                || listObj == IntPtr.Zero)
            {
                return false;
            }

            if (!this.TryHomelandFarmTryResolveInflatedAuraEntitiesGetComponentsMethod(componentClass, out IntPtr inflatedMethod)
                || inflatedMethod == IntPtr.Zero)
            {
                return false;
            }

            // GetComponents<T>(ref List<T> outList): the ref parameter needs params[0] = List**.
            IntPtr* listSlot = stackalloc IntPtr[1];
            listSlot[0] = listObj;
            IntPtr* invokeArgs = stackalloc IntPtr[1];
            invokeArgs[0] = (IntPtr)listSlot;
            IntPtr exc = IntPtr.Zero;
            auraMonoRuntimeInvoke(inflatedMethod, IntPtr.Zero, (IntPtr)invokeArgs, ref exc);
            if (exc != IntPtr.Zero)
            {
                return false;
            }

            IntPtr resultList = listSlot[0] != IntPtr.Zero ? listSlot[0] : listObj;
            List<IntPtr> items = new List<IntPtr>();
            if (!this.TryEnumerateAuraMonoCollectionItems(resultList, items) || items.Count == 0)
            {
                return false;
            }

            components = items;
            return true;
        }

        private bool TryHomelandFarmTryReadAuraMonoComponentNetId(IntPtr componentObj, out uint netId)
        {
            netId = 0U;
            if (componentObj == IntPtr.Zero)
            {
                return false;
            }

            if (this.TryGetMonoObjectMember(componentObj, "entity", out IntPtr entityObj) && entityObj != IntPtr.Zero
                && this.TryGetAuraMonoEntityNetId(entityObj, out netId) && netId != 0U)
            {
                return true;
            }

            if (this.TryGetMonoObjectMember(componentObj, "Entity", out entityObj) && entityObj != IntPtr.Zero
                && this.TryGetAuraMonoEntityNetId(entityObj, out netId) && netId != 0U)
            {
                return true;
            }

            return (this.TryGetMonoUInt32Member(componentObj, "netId", out netId) || this.TryGetMonoUInt32Member(componentObj, "NetId", out netId))
                && netId != 0U;
        }

        private unsafe bool TryHomelandFarmCollectLevelObjectNetIdsAuraNearby(HashSet<uint> output, Vector3 center, float radius, out string source)
        {
            return this.TryHomelandFarmCollectLevelObjectNetIdsAuraSpatial(output, center, radius, out source);
        }

        private unsafe bool TryHomelandFarmCollectLevelObjectNetIdsAura(HashSet<uint> output, out string source)
        {
            source = "Aura LevelObjectManager";
            if (output == null)
            {
                return false;
            }

            if (!this.TryResolveHomelandFarmAuraScanClasses(out string scanStatus))
            {
                source = scanStatus;
                return false;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                source = "AuraMono API unavailable.";
                return false;
            }

            if (!this.TryResolveAuraMonoLevelObjectManager(out IntPtr managerObj, out _, out string managerStatus)
                || managerObj == IntPtr.Zero)
            {
                source = managerStatus;
                return false;
            }

            IntPtr dictionaryObj = IntPtr.Zero;
            if ((!this.TryGetMonoObjectMember(managerObj, "_dictionary", out dictionaryObj) || dictionaryObj == IntPtr.Zero)
                && (!this.TryGetMonoObjectMember(managerObj, "dictionary", out dictionaryObj) || dictionaryObj == IntPtr.Zero))
            {
                source = "Aura LevelObjectManager dictionary unavailable.";
                return false;
            }

            List<IntPtr> entries = new List<IntPtr>();
            if (!this.TryEnumerateAuraMonoCollectionItems(dictionaryObj, entries) || entries.Count <= 0)
            {
                source = "Aura LevelObjectManager dictionary empty.";
                return false;
            }

            int added = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                IntPtr entry = entries[i];
                if (entry == IntPtr.Zero)
                {
                    continue;
                }

                IntPtr levelObjectObj = IntPtr.Zero;
                if ((!this.TryGetMonoObjectMember(entry, "Value", out levelObjectObj) || levelObjectObj == IntPtr.Zero)
                    && (!this.TryGetMonoObjectMember(entry, "value", out levelObjectObj) || levelObjectObj == IntPtr.Zero)
                    && (!this.TryGetMonoObjectMember(entry, "_value", out levelObjectObj) || levelObjectObj == IntPtr.Zero))
                {
                    levelObjectObj = entry;
                }

                uint entityNetId = 0U;
                if (!this.TryHomelandFarmTryGetLevelObjectScanNetId(levelObjectObj, entry, out entityNetId) || entityNetId == 0U)
                {
                    continue;
                }

                this.TryHomelandFarmRememberLevelObjectPosition(entityNetId, levelObjectObj);
                this.TryHomelandFarmRememberLevelObjectOwnerFromLevelObject(levelObjectObj, entityNetId);

                if (output.Add(entityNetId))
                {
                    added++;
                }
            }

            source = "Aura LevelObjectManager(" + added + "/" + entries.Count + ")";
            return added > 0;
        }

        private unsafe bool TryHomelandFarmInvokeAuraUintProtocol(IntPtr methodPtr, uint netId, string label, out string status)
        {
            status = label + " unavailable.";
            if (methodPtr == IntPtr.Zero || !this.EnsureAuraMonoApiReady() || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&netId);
            auraMonoRuntimeInvoke(methodPtr, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                status = "Aura " + label + " failed.";
                return false;
            }

            status = "Aura " + label + " sent.";
            return true;
        }

        private unsafe bool TryHomelandFarmInvokeCropWaterAura(uint ownerNetId, List<uint> cropBoxNetIds, out string status)
        {
            status = "Aura crop water unavailable.";
            if (!this.TryResolveHomelandFarmAuraProtocol(out status))
            {
                return false;
            }

            IntPtr methodPtr = this.homelandFarmAuraCropWaterPlant2Method != IntPtr.Zero
                ? this.homelandFarmAuraCropWaterPlant2Method
                : this.homelandFarmAuraCropWaterPlant3Method;
            if (methodPtr == IntPtr.Zero
                || !this.TryCreatePetFeedAuraUIntList(cropBoxNetIds, out IntPtr listObj, out status)
                || listObj == IntPtr.Zero)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            if (this.homelandFarmAuraCropWaterPlant2Method != IntPtr.Zero && methodPtr == this.homelandFarmAuraCropWaterPlant2Method)
            {
                IntPtr* args = stackalloc IntPtr[2];
                args[0] = (IntPtr)(&ownerNetId);
                args[1] = listObj;
                auraMonoRuntimeInvoke(methodPtr, IntPtr.Zero, (IntPtr)args, ref exc);
            }
            else
            {
                int mode = 0;
                IntPtr* args = stackalloc IntPtr[3];
                args[0] = (IntPtr)(&ownerNetId);
                args[1] = listObj;
                args[2] = (IntPtr)(&mode);
                auraMonoRuntimeInvoke(methodPtr, IntPtr.Zero, (IntPtr)args, ref exc);
            }

            if (exc != IntPtr.Zero)
            {
                status = "Aura crop water failed exc=0x" + exc.ToInt64().ToString("X") + ".";
                this.HomelandFarmLog(status + " owner=" + ownerNetId);
                return false;
            }

            status = "Aura crop water sent (" + cropBoxNetIds.Count + ").";
            this.HomelandFarmLog(status + " owner=" + ownerNetId);
            return true;
        }

        private unsafe bool TryHomelandFarmInvokeAddManureAura(List<uint> cropNetIds, out string status)
        {
            status = "Aura AddManure unavailable.";
            if (cropNetIds == null || cropNetIds.Count == 0)
            {
                status = "Crop list empty.";
                return false;
            }

            if (!this.TryResolveHomelandFarmAuraProtocol(out status))
            {
                return false;
            }

            if (this.homelandFarmAuraCropAddManureMethod == IntPtr.Zero
                || !this.TryCreatePetFeedAuraUIntList(cropNetIds, out IntPtr listObj, out status)
                || listObj == IntPtr.Zero)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = listObj;
            auraMonoRuntimeInvoke(this.homelandFarmAuraCropAddManureMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                status = "Aura AddManure failed exc=0x" + exc.ToInt64().ToString("X") + ".";
                return false;
            }

            status = "Aura AddManure sent (" + cropNetIds.Count + ").";
            return true;
        }

        private unsafe bool TryHomelandFarmInvokePlantWaterAura(uint ownerNetId, List<uint> plantNetIds, int mode, out string status)
        {
            status = "Aura plant water unavailable.";
            if (!this.TryResolveHomelandFarmAuraProtocol(out status))
            {
                return false;
            }

            IntPtr methodPtr = this.homelandFarmAuraPlantWaterPlantMethod != IntPtr.Zero
                ? this.homelandFarmAuraPlantWaterPlantMethod
                : this.homelandFarmAuraPlantWaterPlant2Method;
            if (methodPtr == IntPtr.Zero
                || !this.TryCreatePetFeedAuraUIntList(plantNetIds, out IntPtr listObj, out status)
                || listObj == IntPtr.Zero)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            if (methodPtr == this.homelandFarmAuraPlantWaterPlant2Method)
            {
                IntPtr* args = stackalloc IntPtr[2];
                args[0] = (IntPtr)(&ownerNetId);
                args[1] = listObj;
                auraMonoRuntimeInvoke(methodPtr, IntPtr.Zero, (IntPtr)args, ref exc);
            }
            else
            {
                IntPtr* args = stackalloc IntPtr[3];
                args[0] = (IntPtr)(&ownerNetId);
                args[1] = listObj;
                args[2] = (IntPtr)(&mode);
                auraMonoRuntimeInvoke(methodPtr, IntPtr.Zero, (IntPtr)args, ref exc);
            }

            if (exc != IntPtr.Zero)
            {
                status = "Aura plant water failed.";
                return false;
            }

            status = "Aura plant water sent (" + plantNetIds.Count + ") owner=" + ownerNetId + ".";
            return true;
        }

        private bool TryHomelandFarmCollectLevelObjectNetIds(HashSet<uint> output, out string source)
        {
            source = "LevelObjectManager";
            if (output == null)
            {
                return false;
            }

            try
            {
                Type levelObjectManagerType = this.FindLevelObjectManagerRuntimeType();
                if (levelObjectManagerType == null)
                {
                    source = "LevelObjectManager type unavailable.";
                    return false;
                }

                PropertyInfo instanceProperty = levelObjectManagerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                object levelObjectManager = instanceProperty != null ? instanceProperty.GetValue(null, null) : null;
                if (levelObjectManager == null)
                {
                    source = "LevelObjectManager.Instance unavailable.";
                    return false;
                }

                object dictionaryObj = this.TryGetManagedMemberValue(levelObjectManager, "_dictionary")
                    ?? this.TryGetManagedMemberValue(levelObjectManager, "dictionary");
                if (!(dictionaryObj is IEnumerable enumerable))
                {
                    source = "LevelObjectManager dictionary unavailable.";
                    return false;
                }

                List<object> entries = enumerable.Cast<object>().ToList();
                int added = 0;
                for (int i = 0; i < entries.Count; i++)
                {
                    object entry = entries[i];
                    if (entry == null)
                    {
                        continue;
                    }

                    object levelObject = this.TryGetManagedMemberValue(entry, "Value") ?? entry;
                    if (levelObject == null)
                    {
                        continue;
                    }

                    if (!this.TryHomelandFarmTryGetLevelObjectScanNetId(levelObject, entry, out uint entityNetId) || entityNetId == 0U)
                    {
                        continue;
                    }

                    this.TryHomelandFarmRememberLevelObjectPosition(entityNetId, levelObject);
                    this.TryHomelandFarmRememberLevelObjectOwnerFromLevelObject(levelObject, entityNetId);

                    if (output.Add(entityNetId))
                    {
                        added++;
                    }
                }

                source = "LevelObjectManager(" + added + "/" + entries.Count + ")";
                return added > 0;
            }
            catch (Exception ex)
            {
                source = "LevelObjectManager scan failed: " + ex.Message;
                return false;
            }
        }

        private bool TryHomelandFarmClassifyFarmNetId(uint netId, out bool isCropBox)
        {
            isCropBox = false;
            if (netId == 0U)
            {
                return false;
            }

            bool accepted;
            if (this.HomelandFarmPrefersAuraComponentData())
            {
                accepted = this.TryHomelandFarmAuraEntityClassifyFarm(netId, out isCropBox, out bool isPlant, out bool isCrop)
                    && (isCropBox || isPlant || isCrop);
            }
            else if (this.TryHomelandFarmGetComponentData(this.homelandFarmCropBoxItemDataType, netId, out _, out _, "CropBoxItemData"))
            {
                isCropBox = true;
                accepted = true;
            }
            else
            {
                accepted = this.TryHomelandFarmGetComponentData(this.homelandFarmPlantItemDataType, netId, out _, out _, "PlantItemData")
                    || this.TryHomelandFarmGetComponentData(this.homelandFarmCropItemDataType, netId, out _, out _, "CropItemData");
            }

            if (accepted)
            {
                this.TryHomelandFarmRegisterDiscoveredFarmTarget(netId, isCropBox);
            }

            return accepted;
        }

        private void TryHomelandFarmRegisterDiscoveredFarmTarget(uint netId, bool isCropBox)
        {
            if (netId == 0U || netId >= 0x80000000U)
            {
                return;
            }

            Vector3 position = Vector3.zero;
            this.TryHomelandFarmResolveFarmEntityPosition(netId, out position);
            this.homelandFarmRegisteredFarmTargets[netId] = new HomelandFarmRegisteredFarmTarget
            {
                NetId = netId,
                LastPosition = position,
                IsCropBox = isCropBox,
                RegisteredAt = Time.realtimeSinceStartup
            };

            if (this.homelandFarmRegisteredFarmTargets.Count <= HomelandFarmMaxRegisteredFarmTargets)
            {
                return;
            }

            uint oldestNetId = 0U;
            float oldestAt = float.MaxValue;
            foreach (KeyValuePair<uint, HomelandFarmRegisteredFarmTarget> entry in this.homelandFarmRegisteredFarmTargets)
            {
                if (entry.Value.RegisteredAt >= oldestAt)
                {
                    continue;
                }

                oldestAt = entry.Value.RegisteredAt;
                oldestNetId = entry.Key;
            }

            if (oldestNetId != 0U)
            {
                this.homelandFarmRegisteredFarmTargets.Remove(oldestNetId);
            }
        }

        private bool TryHomelandFarmCollectFarmNetIdsFromRegisteredCache(
            Vector3 scanCenter,
            float scanRadius,
            HashSet<uint> output,
            out int added)
        {
            added = 0;
            if (output == null || this.homelandFarmRegisteredFarmTargets.Count <= 0)
            {
                return false;
            }

            bool spatialScan = scanRadius > 0f && scanCenter != Vector3.zero;
            float radiusSq = spatialScan ? scanRadius * scanRadius : 0f;
            foreach (KeyValuePair<uint, HomelandFarmRegisteredFarmTarget> entry in this.homelandFarmRegisteredFarmTargets)
            {
                uint netId = entry.Key;
                if (netId == 0U || netId >= 0x80000000U)
                {
                    continue;
                }

                HomelandFarmRegisteredFarmTarget registered = entry.Value;
                if (spatialScan)
                {
                    // A registered target whose position never resolved (LastPosition == zero) must NOT
                    // be served in a radius scan: it can't be placed, and including it unconditionally
                    // (the old behaviour) floods the result with un-positioned targets that then get
                    // dropped as skippedNoPos AND short-circuits the real entity scan → nothing found.
                    // Capture's world-wide component scan registers many such zero-position boxes.
                    if (registered.LastPosition == Vector3.zero
                        || (registered.LastPosition - scanCenter).sqrMagnitude > radiusSq)
                    {
                        continue;
                    }
                }

                if (output.Add(netId))
                {
                    added++;
                    if (registered.IsCropBox)
                    {
                        this.homelandFarmLastScanCropBoxNetIds.Add(netId);
                    }
                }
            }

            return added > 0;
        }

        private bool TryHomelandFarmCollectFarmNetIdsFromInteractSeeds(
            Vector3 scanCenter,
            float scanRadius,
            HashSet<uint> output,
            out int added)
        {
            added = 0;
            if (output == null)
            {
                return false;
            }

            HashSet<uint> seedNetIds = new HashSet<uint>();
            if (this.TryGetCurrentFocusedLevelObjectNetId(out ulong focusedLevelObjectNetId, out _)
                && focusedLevelObjectNetId != 0UL
                && focusedLevelObjectNetId <= uint.MaxValue)
            {
                seedNetIds.Add((uint)focusedLevelObjectNetId);
            }

            List<ulong> interactLevelObjects = new List<ulong>(8);
            if (this.TryGetCurrentInteractTargetLevelObjects(interactLevelObjects, out _, null))
            {
                for (int i = 0; i < interactLevelObjects.Count; i++)
                {
                    ulong levelObjectNetId = interactLevelObjects[i];
                    if (levelObjectNetId != 0UL && levelObjectNetId <= uint.MaxValue)
                    {
                        seedNetIds.Add((uint)levelObjectNetId);
                    }
                }
            }

            List<ulong> auraInteractLevelObjects = new List<ulong>(8);
            if (this.TryGetCurrentInteractTargetLevelObjectsViaAuraMono(auraInteractLevelObjects, out _, null))
            {
                for (int i = 0; i < auraInteractLevelObjects.Count; i++)
                {
                    ulong levelObjectNetId = auraInteractLevelObjects[i];
                    if (levelObjectNetId != 0UL && levelObjectNetId <= uint.MaxValue)
                    {
                        seedNetIds.Add((uint)levelObjectNetId);
                    }
                }
            }

            if (seedNetIds.Count <= 0)
            {
                return false;
            }

            float radiusSq = scanRadius > 0f && scanCenter != Vector3.zero ? scanRadius * scanRadius : 0f;
            foreach (uint seedNetId in seedNetIds)
            {
                if (radiusSq > 0f
                    && this.TryHomelandFarmResolveFarmEntityPosition(seedNetId, out Vector3 seedPos)
                    && seedPos != Vector3.zero
                    && (seedPos - scanCenter).sqrMagnitude > radiusSq)
                {
                    continue;
                }

                int before = output.Count;
                this.TryHomelandFarmTryQuickAcceptFarmNetId(seedNetId, output, includeLinkedCrops: false);
                added += output.Count - before;
            }

            return added > 0;
        }

        private Type ResolveHomelandFarmScannerComponentType(string shortName, params string[] fullNames)
        {
            Type resolved = null;
            switch (shortName)
            {
                case "CropBoxComponent":
                    resolved = this.homelandFarmCropBoxComponentType ?? this.ResolveHomelandFarmCropBoxComponentRuntimeType();
                    break;
                case "CropComponent":
                    resolved = this.homelandFarmCropComponentType ?? this.ResolveHomelandFarmCropComponentRuntimeType();
                    break;
                case "PlantComponent":
                    resolved = this.homelandFarmPlantComponentType;
                    break;
            }

            if (resolved != null)
            {
                return resolved;
            }

            if (fullNames != null)
            {
                for (int i = 0; i < fullNames.Length; i++)
                {
                    string fullName = fullNames[i];
                    if (string.IsNullOrEmpty(fullName))
                    {
                        continue;
                    }

                    resolved = this.FindLoadedType(fullName, shortName);
                    if (resolved != null)
                    {
                        return resolved;
                    }

                    int lastDot = fullName.LastIndexOf('.');
                    string namespaceName = lastDot > 0 ? fullName.Substring(0, lastDot) : string.Empty;
                    resolved = this.FindTypeByName(fullName, namespaceName, shortName);
                    if (resolved != null)
                    {
                        return resolved;
                    }
                }
            }

            resolved = this.FindLoadedType(shortName);
            if (resolved != null)
            {
                return resolved;
            }

            return this.FindTypeBySignature(shortName, "XDTLevelAndEntity", false, false)
                ?? this.FindTypeBySignature(shortName, null, false, false);
        }

        private void TryHomelandFarmRefreshLastScanCropBoxNetIdsFromRegistry(HashSet<uint> output)
        {
            if (output == null || output.Count == 0)
            {
                return;
            }

            foreach (uint netId in output)
            {
                if (netId == 0U || netId >= 0x80000000U)
                {
                    continue;
                }

                if (this.homelandFarmRegisteredFarmTargets.TryGetValue(netId, out HomelandFarmRegisteredFarmTarget registered)
                    && registered.IsCropBox)
                {
                    this.homelandFarmLastScanCropBoxNetIds.Add(netId);
                }
            }
        }

        private bool TryEnsureHomelandFarmEntitiesGetComponentsReady(out string status)
        {
            status = string.Empty;
            this.TryEnsureHomelandFarmInteropAssembliesLoaded();
            this.ClearModReflectionLookupMissCaches();
            this.ResolveAuraFarmRuntimeMethods();
            this.EnsureHomelandFarmScannerTypes();

            if (this.homelandFarmEntitiesType == null)
            {
                this.homelandFarmEntitiesType = this.auraEntitiesType;
            }

            if (this.homelandFarmEntitiesType == null)
            {
                this.homelandFarmEntitiesType = this.FindEntitiesRuntimeType();
            }

            if (this.homelandFarmEntitiesType == null)
            {
                this.homelandFarmEntitiesType = this.FindLoadedType(
                    "XDTLevelAndEntity.BaseSystem.EntitiesManager.Entities",
                    "ScriptsRefactory.LevelAndEntity.BaseSystem.EntitiesManager.Entities",
                    "Il2Cpp.XDTLevelAndEntity.BaseSystem.EntitiesManager.Entities",
                    "Entities");
            }

            if (this.homelandFarmEntitiesType == null)
            {
                this.homelandFarmEntitiesType = this.FindTypeByName(
                    "XDTLevelAndEntity.BaseSystem.EntitiesManager.Entities",
                    "XDTLevelAndEntity.BaseSystem.EntitiesManager",
                    "Entities")
                    ?? this.FindTypeBySignature("Entities", "XDTLevelAndEntity", false, false)
                    ?? this.FindTypeBySignature("Entities", null, false, false);
            }

            if (this.homelandFarmEntitiesGetComponentsMethod == null && this.homelandFarmEntitiesType != null)
            {
                this.homelandFarmEntitiesGetComponentsMethod = this.homelandFarmEntitiesType
                    .GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    .FirstOrDefault(m => m.Name == "GetComponents" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1);
            }

            if (this.homelandFarmCropBoxComponentType == null)
            {
                this.homelandFarmCropBoxComponentType = this.ResolveHomelandFarmScannerComponentType(
                    "CropBoxComponent",
                    "XDTLevelAndEntity.Gameplay.Component.Homeland.CropBoxComponent",
                    "XDTLevelAndEntity.Gameplay.Component.Farm.CropBoxComponent",
                    "ScriptsRefactory.LevelAndEntity.Gameplay.Component.Farm.CropBoxComponent");
            }

            if (this.homelandFarmCropComponentType == null)
            {
                this.homelandFarmCropComponentType = this.ResolveHomelandFarmScannerComponentType(
                    "CropComponent",
                    "XDTLevelAndEntity.Gameplay.Component.Homeland.CropComponent",
                    "XDTLevelAndEntity.Gameplay.Component.Farm.CropComponent",
                    "ScriptsRefactory.LevelAndEntity.Gameplay.Component.Farm.CropComponent");
            }

            if (this.homelandFarmPlantComponentType == null)
            {
                this.homelandFarmPlantComponentType = this.ResolveHomelandFarmScannerComponentType(
                    "PlantComponent",
                    "XDTLevelAndEntity.Gameplay.Component.Plant.PlantComponent",
                    "ScriptsRefactory.LevelAndEntity.Gameplay.Component.Plant.PlantComponent");
            }

            if (this.homelandFarmEntitiesGetComponentsMethod == null || this.homelandFarmEntitiesType == null)
            {
                status = "Entities.GetComponents unavailable (entitiesType="
                    + (this.homelandFarmEntitiesType != null)
                    + " auraEntitiesType=" + (this.auraEntitiesType != null) + ").";
                return false;
            }

            if (this.homelandFarmCropBoxComponentType == null
                && this.homelandFarmCropComponentType == null
                && this.homelandFarmPlantComponentType == null)
            {
                status = "Farm component runtime types unavailable.";
                return false;
            }

            status = "Entities.GetComponents ready.";
            return true;
        }

        // Mass-cook-style scan: radius-filter entity positions first, then one GetAllComponents per
        // nearby entity (closest first) until budget/target count — avoids the AuraEntities funnel
        // collecting thousands of in-radius netIds and verifying only two under a short budget.
        private bool TryHomelandFarmCollectFarmNetIdsFromAuraProximityComponentScan(
            Vector3 scanCenter,
            float scanRadius,
            HashSet<uint> output,
            out int added,
            out int inspected,
            float budgetSeconds = HomelandFarmAuraProximityComponentScanBudgetSeconds)
        {
            added = 0;
            inspected = 0;
            if (output == null || scanRadius <= 0f || scanCenter == Vector3.zero)
            {
                return false;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return false;
            }

            List<IntPtr> entityObjects;
            if (!this.TryEnumerateAuraMonoLoadedEntityObjects(out entityObjects, out _)
                || entityObjects == null
                || entityObjects.Count == 0)
            {
                return false;
            }

            float radiusSq = scanRadius * scanRadius;
            float collectStartedAt = Time.realtimeSinceStartup;
            List<HomelandFarmAuraEntityCandidate> nearby = new List<HomelandFarmAuraEntityCandidate>(256);
            for (int i = 0; i < entityObjects.Count; i++)
            {
                IntPtr entityObj = entityObjects[i];
                if (entityObj == IntPtr.Zero)
                {
                    continue;
                }

                if (!this.TryGetAuraMonoEntityNetId(entityObj, out uint candidateNetId) || candidateNetId == 0U || candidateNetId >= 0x80000000U)
                {
                    continue;
                }

                if (!this.TryHomelandFarmResolveFarmEntityPosition(candidateNetId, out Vector3 candidatePosition)
                    || candidatePosition == Vector3.zero)
                {
                    continue;
                }

                float distanceSq = (candidatePosition - scanCenter).sqrMagnitude;
                if (distanceSq > radiusSq)
                {
                    continue;
                }

                nearby.Add(new HomelandFarmAuraEntityCandidate
                {
                    NetId = candidateNetId,
                    Distance = Mathf.Sqrt(distanceSq)
                });
            }

            if (nearby.Count == 0)
            {
                return false;
            }

            float collectMs = (Time.realtimeSinceStartup - collectStartedAt) * 1000f;

            nearby.Sort((a, b) => a.Distance.CompareTo(b.Distance));
            int inspectLimit = Mathf.Min(nearby.Count, HomelandFarmMaxAuraProximityComponentInspect);

            // Warm the farm component class cache once BEFORE timing the inspection. The first
            // resolution scans every loaded image and can take seconds; doing it inside the
            // budgeted loop would let entity #0 consume the whole budget (inspected=1/512).
            if (this.HomelandFarmPrefersAuraComponentData())
            {
                this.TryResolveAuraMonoFarmComponentClasses(out _, out _, out _);
            }

            // The radius-filter pass above intentionally scans the whole loaded set (no time cap)
            // so that nearby crop boxes appearing late in list order survive the distance sort.
            // The budget bounds only this verification pass, so give it its own time origin —
            // otherwise the (potentially multi-second) collect pass would consume the entire
            // budget and starve inspection (symptom: nearby=NNNN but inspected=1).
            float inspectStartedAt = Time.realtimeSinceStartup;
            for (int i = 0; i < inspectLimit; i++)
            {
                if (Time.realtimeSinceStartup - inspectStartedAt >= budgetSeconds)
                {
                    break;
                }

                inspected++;
                HomelandFarmAuraEntityCandidate candidate = nearby[i];
                int before = output.Count;
                try
                {
                    if (this.TryHomelandFarmClassifyFarmNetId(candidate.NetId, out bool isCropBox))
                    {
                        output.Add(candidate.NetId);
                        if (isCropBox)
                        {
                            this.homelandFarmLastScanCropBoxNetIds.Add(candidate.NetId);
                        }
                    }
                }
                catch (Exception ex)
                {
                    this.HomelandFarmLog("Proximity farm scan failed netId=" + candidate.NetId + ": " + ex.Message);
                }

                added += output.Count - before;
            }

            if (added > 0)
            {
                this.HomelandFarmLog(
                    "AuraProximity scan: nearby=" + nearby.Count
                    + " inspected=" + inspected
                    + "/" + inspectLimit
                    + " added=" + added
                    + " collectMs=" + collectMs.ToString("F0")
                    + " inspectMs=" + ((Time.realtimeSinceStartup - inspectStartedAt) * 1000f).ToString("F0"));
            }

            return added > 0;
        }

        private bool TryHomelandFarmHasFarmComponentData(uint netId)
        {
            return this.TryHomelandFarmClassifyFarmNetId(netId, out _);
        }

        private static readonly string[] HomelandFarmAnyFarmComponentHints =
        {
            "CropBoxComponent", "CropBoxItemData",
            "PlantComponent", "PlantItemData",
            "CropComponent", "CropItemData"
        };

        private bool TryHomelandFarmAuraEntityClassifyFarm(
            uint netId,
            out bool isCropBox,
            out bool isPlant,
            out bool isCrop)
        {
            isCropBox = false;
            isPlant = false;
            isCrop = false;
            if (netId == 0U
                || !this.EnsureAuraMonoApiReady()
                || !this.AttachAuraMonoThread()
                || auraMonoObjectGetClass == null)
            {
                return false;
            }

            if (!this.TryGetAuraMonoEntityObjectByNetId(netId, out IntPtr entityObj) || entityObj == IntPtr.Zero)
            {
                return false;
            }

            if (!this.TryHomelandFarmTryGuardAuraEntityBeforeHeavyAccess(entityObj))
            {
                return false;
            }

            if (!this.TryInvokeAuraMonoZeroArg(entityObj, out IntPtr componentsObj, "GetAllComponents") || componentsObj == IntPtr.Zero)
            {
                return false;
            }

            List<IntPtr> components = new List<IntPtr>();
            if (!this.TryEnumerateAuraMonoCollectionItems(componentsObj, components))
            {
                return false;
            }

            bool hasComponentClasses = this.TryResolveAuraMonoFarmComponentClasses(
                out IntPtr plantComponentClass,
                out IntPtr cropBoxComponentClass,
                out IntPtr cropComponentClass);

            for (int i = 0; i < components.Count; i++)
            {
                if (components[i] == IntPtr.Zero)
                {
                    continue;
                }

                IntPtr componentClass = auraMonoObjectGetClass(components[i]);
                if (componentClass == IntPtr.Zero)
                {
                    continue;
                }

                if (hasComponentClasses)
                {
                    if (!isCropBox
                        && cropBoxComponentClass != IntPtr.Zero
                        && this.IsAuraMonoClassAssignableTo(componentClass, cropBoxComponentClass))
                    {
                        isCropBox = true;
                    }

                    if (!isPlant
                        && plantComponentClass != IntPtr.Zero
                        && this.IsAuraMonoClassAssignableTo(componentClass, plantComponentClass))
                    {
                        isPlant = true;
                    }

                    if (!isCrop
                        && cropComponentClass != IntPtr.Zero
                        && this.IsAuraMonoClassAssignableTo(componentClass, cropComponentClass))
                    {
                        isCrop = true;
                    }
                }

                string className = this.GetAuraMonoClassDisplayName(componentClass);
                if (string.IsNullOrEmpty(className))
                {
                    continue;
                }

                if (!isCropBox
                    && (className.IndexOf("CropBoxComponent", StringComparison.OrdinalIgnoreCase) >= 0
                        || className.IndexOf("CropBoxItemData", StringComparison.OrdinalIgnoreCase) >= 0
                        || (className.IndexOf("CropBox", StringComparison.OrdinalIgnoreCase) >= 0
                            && className.IndexOf("CropComponent", StringComparison.OrdinalIgnoreCase) < 0)))
                {
                    isCropBox = true;
                }

                if (!isPlant
                    && (className.IndexOf("PlantComponent", StringComparison.OrdinalIgnoreCase) >= 0
                        || className.IndexOf("PlantItemData", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    isPlant = true;
                }

                if (!isCrop
                    && className.IndexOf("CropBoxComponent", StringComparison.OrdinalIgnoreCase) < 0
                    && className.IndexOf("CropBox", StringComparison.OrdinalIgnoreCase) < 0
                    && (className.IndexOf("CropComponent", StringComparison.OrdinalIgnoreCase) >= 0
                        || className.IndexOf("CropItemData", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    isCrop = true;
                }
            }

            return isCropBox || isPlant || isCrop;
        }

        private bool TryHomelandFarmAuraEntityHasAnyFarmComponent(uint netId)
        {
            return this.TryHomelandFarmAuraEntityClassifyFarm(netId, out _, out _, out _);
        }

        private IntPtr TryHomelandFarmResolveAuraComponentDataHandle(IntPtr componentHandle)
        {
            if (componentHandle == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            string[] dataMembers = { "ComponentData", "_componentData", "componentData", "data", "_data", "Data" };
            for (int i = 0; i < dataMembers.Length; i++)
            {
                if (this.TryGetMonoObjectMember(componentHandle, dataMembers[i], out IntPtr nested) && nested != IntPtr.Zero)
                {
                    return nested;
                }
            }

            return componentHandle;
        }

        private bool TryHomelandFarmTryReadComponentListCount(object data, out int count, out bool readOk, params string[] members)
        {
            count = 0;
            readOk = false;
            if (data == null || members == null || members.Length == 0)
            {
                return false;
            }

            if (data is HomelandFarmAuraComponentData auraData && auraData.Handle != IntPtr.Zero)
            {
                IntPtr dataHandle = this.TryHomelandFarmResolveAuraComponentDataHandle(auraData.Handle);
                for (int i = 0; i < members.Length; i++)
                {
                    if (!this.TryGetMonoObjectMember(dataHandle, members[i], out IntPtr listObj) || listObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    List<IntPtr> items = new List<IntPtr>();
                    if (this.TryEnumerateAuraMonoCollectionItems(listObj, items))
                    {
                        count = items.Count;
                        readOk = true;
                        return true;
                    }
                }

                return false;
            }

            for (int i = 0; i < members.Length; i++)
            {
                if (this.TryGetObjectMember(data, members[i], out object listObj) && listObj != null)
                {
                    List<object> items = new List<object>();
                    if (this.TryEnumerateManagedCollectionItems(listObj, items))
                    {
                        count = items.Count;
                        readOk = true;
                        return true;
                    }

                    if (listObj is ICollection collection)
                    {
                        count = collection.Count;
                        readOk = true;
                        return true;
                    }
                }
            }

            return false;
        }

        private static int HomelandFarmComputePlantWaterLevel(bool masterWater, bool weatherWater, int friendWaterCount)
        {
            int level = 0;
            if (masterWater || weatherWater)
            {
                level++;
            }

            return level + Math.Max(0, friendWaterCount);
        }

        private static int HomelandFarmComputeCropTotalWaterLevel(bool ownerWatered, int friendWaterCount)
        {
            return (ownerWatered ? 1 : 0) + Math.Max(0, friendWaterCount);
        }

        private unsafe bool TryUnboxMonoGuid(IntPtr boxed, out Guid value)
        {
            value = Guid.Empty;
            if (boxed == IntPtr.Zero || auraMonoObjectUnbox == null || !this.TryAuraMonoBoxedIsValueType(boxed))
            {
                return false;
            }

            IntPtr raw = auraMonoObjectUnbox(boxed);
            if (raw == IntPtr.Zero)
            {
                return false;
            }

            value = *(Guid*)raw;
            return value != Guid.Empty;
        }

        private void EnsureHomelandFarmPlayerDataCenterType()
        {
            if (this.homelandFarmPlayerDataCenterType != null)
            {
                return;
            }

            this.homelandFarmPlayerDataCenterType = this.FindLoadedType(
                "XDTDataAndProtocol.PlayerDataCenter",
                "ScriptsRefactory.DataAndProtocol.PlayerDataCenter",
                "PlayerDataCenter");
        }

        private bool TryHomelandFarmTryReadGuidFromLoginInfoObject(object loginInfo, out Guid playerGuid)
        {
            playerGuid = Guid.Empty;
            if (loginInfo == null)
            {
                return false;
            }

            if (this.TryGetObjectMember(loginInfo, "PlayerId", out object rawGuid) && rawGuid is Guid guid && guid != Guid.Empty)
            {
                playerGuid = guid;
                return true;
            }

            Type loginInfoType = loginInfo.GetType();
            PropertyInfo playerIdProperty = loginInfoType.GetProperty("PlayerId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            if (playerIdProperty != null)
            {
                try
                {
                    object value = playerIdProperty.GetValue(loginInfo, null);
                    if (value is Guid propertyGuid && propertyGuid != Guid.Empty)
                    {
                        playerGuid = propertyGuid;
                        return true;
                    }
                }
                catch
                {
                }
            }

            return false;
        }

        private bool TryHomelandFarmTryReadSelfPlayerGuidFromLoginInfoManaged(out Guid playerGuid)
        {
            playerGuid = Guid.Empty;
            this.EnsureHomelandFarmPlayerDataCenterType();
            if (this.homelandFarmPlayerDataCenterType == null)
            {
                return false;
            }

            MethodInfo getLoginInfoMethod = this.GetMethodQuiet(
                this.homelandFarmPlayerDataCenterType,
                "GetLoginInfo",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                Type.EmptyTypes);
            if (getLoginInfoMethod == null)
            {
                return false;
            }

            try
            {
                object loginInfo = getLoginInfoMethod.Invoke(null, null);
                return this.TryHomelandFarmTryReadGuidFromLoginInfoObject(loginInfo, out playerGuid);
            }
            catch
            {
                return false;
            }
        }

        private unsafe bool TryHomelandFarmTryReadSelfPlayerGuidFromLoginInfoAura(out Guid playerGuid)
        {
            playerGuid = Guid.Empty;
            if (!this.EnsureAuraMonoApiReady()
                || !this.AttachAuraMonoThread()
                || auraMonoClassFromName == null
                || auraMonoClassGetMethodFromName == null
                || auraMonoRuntimeInvoke == null
                || auraMonoObjectUnbox == null)
            {
                return false;
            }

            IntPtr image = this.FindAuraMonoImage(new string[] { "XDTDataAndProtocol", "XDTDataAndProtocol.dll" });
            IntPtr playerDataCenterClass = image != IntPtr.Zero
                ? auraMonoClassFromName(image, "XDTDataAndProtocol", "PlayerDataCenter")
                : IntPtr.Zero;
            if (playerDataCenterClass == IntPtr.Zero)
            {
                playerDataCenterClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTDataAndProtocol", "PlayerDataCenter");
            }

            if (playerDataCenterClass == IntPtr.Zero)
            {
                return false;
            }

            IntPtr getLoginInfoMethod = auraMonoClassGetMethodFromName(playerDataCenterClass, "GetLoginInfo", 0);
            if (getLoginInfoMethod == IntPtr.Zero)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr loginInfoObj = auraMonoRuntimeInvoke(getLoginInfoMethod, IntPtr.Zero, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || loginInfoObj == IntPtr.Zero)
            {
                return false;
            }

            string[] members = { "PlayerId", "playerId", "<PlayerId>k__BackingField" };
            for (int i = 0; i < members.Length; i++)
            {
                if (this.TryGetMonoObjectMember(loginInfoObj, members[i], out IntPtr boxed) && boxed != IntPtr.Zero && this.TryUnboxMonoGuid(boxed, out playerGuid))
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryHomelandFarmTryReadSelfPlayerGuid(out Guid playerGuid, out bool readOk)
        {
            // Memoized: the GUID never changes within a session, and each resolution attempt can run
            // expensive managed login-info reflection. Only the successful result is cached so a
            // not-yet-available GUID keeps retrying.
            if (this.homelandFarmCachedSelfGuidResolved)
            {
                playerGuid = this.homelandFarmCachedSelfGuid;
                readOk = this.homelandFarmCachedSelfGuidReadOk;
                return readOk;
            }

            bool resolved = this.TryHomelandFarmTryReadSelfPlayerGuidUncached(out playerGuid, out readOk);
            if (resolved && readOk && playerGuid != Guid.Empty)
            {
                this.homelandFarmCachedSelfGuid = playerGuid;
                this.homelandFarmCachedSelfGuidReadOk = true;
                this.homelandFarmCachedSelfGuidResolved = true;
            }

            return resolved;
        }

        private bool TryHomelandFarmTryReadSelfPlayerGuidUncached(out Guid playerGuid, out bool readOk)
        {
            playerGuid = Guid.Empty;
            readOk = false;

            if (this.TryHomelandFarmTryReadSelfPlayerGuidFromLoginInfoManaged(out playerGuid) && playerGuid != Guid.Empty)
            {
                readOk = true;
                return true;
            }

            if (this.TryHomelandFarmTryReadSelfPlayerGuidFromLoginInfoAura(out playerGuid) && playerGuid != Guid.Empty)
            {
                readOk = true;
                return true;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return false;
            }

            if (!this.TryGetHomelandFarmPlayerNetId(out uint playerNetId, out _) || playerNetId == 0U)
            {
                return false;
            }

            if (!this.TryGetAuraMonoEntityObjectByNetId(playerNetId, out IntPtr entityObj) || entityObj == IntPtr.Zero)
            {
                return false;
            }

            string[] members = { "playerGuid", "PlayerGuid", "guid", "Guid", "roleGuid", "RoleGuid", "accountGuid", "AccountGuid" };
            for (int i = 0; i < members.Length; i++)
            {
                if (this.TryGetMonoObjectMember(entityObj, members[i], out IntPtr boxed) && boxed != IntPtr.Zero && this.TryUnboxMonoGuid(boxed, out playerGuid))
                {
                    readOk = playerGuid != Guid.Empty;
                    return readOk;
                }
            }

            return false;
        }

        private bool TryHomelandFarmComponentListContainsGuid(object data, Guid targetGuid, params string[] listMembers)
        {
            if (data == null || targetGuid == Guid.Empty || listMembers == null || listMembers.Length == 0)
            {
                return false;
            }

            if (data is HomelandFarmAuraComponentData auraData && auraData.Handle != IntPtr.Zero)
            {
                IntPtr dataHandle = this.TryHomelandFarmResolveAuraComponentDataHandle(auraData.Handle);
                for (int i = 0; i < listMembers.Length; i++)
                {
                    if (!this.TryGetMonoObjectMember(dataHandle, listMembers[i], out IntPtr listObj) || listObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    List<IntPtr> items = new List<IntPtr>();
                    if (!this.TryEnumerateAuraMonoCollectionItems(listObj, items))
                    {
                        continue;
                    }

                    for (int j = 0; j < items.Count; j++)
                    {
                        if (this.TryUnboxMonoGuid(items[j], out Guid itemGuid) && itemGuid == targetGuid)
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            for (int i = 0; i < listMembers.Length; i++)
            {
                if (!this.TryGetObjectMember(data, listMembers[i], out object listObj) || listObj == null)
                {
                    continue;
                }

                if (listObj is IEnumerable enumerable)
                {
                    foreach (object item in enumerable)
                    {
                        if (item is Guid itemGuid && itemGuid == targetGuid)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private bool TryHomelandFarmTryResolveVisitorWaterState(
            object componentData,
            uint ownerId,
            uint playerNetId,
            bool isCropBox,
            out bool selfWatered,
            out bool selfWateredReadOk,
            out bool canAddVisitorWater)
        {
            return this.TryHomelandFarmTryResolveVisitorWaterState(
                componentData,
                ownerId,
                playerNetId,
                isCropBox,
                Guid.Empty,
                false,
                out selfWatered,
                out selfWateredReadOk,
                out canAddVisitorWater);
        }

        private bool TryHomelandFarmTryResolveVisitorWaterState(
            object componentData,
            uint ownerId,
            uint playerNetId,
            bool isCropBox,
            Guid cachedSelfGuid,
            bool cachedSelfGuidReadOk,
            out bool selfWatered,
            out bool selfWateredReadOk,
            out bool canAddVisitorWater)
        {
            selfWatered = false;
            selfWateredReadOk = false;
            canAddVisitorWater = false;
            bool visiting = playerNetId != 0U && (ownerId == 0U || ownerId != playerNetId);
            if (!visiting)
            {
                return false;
            }

            string[] listMembers = isCropBox
                ? new[] { "waterGuids", "WaterGuids", "_waterGuids" }
                : new[] { "friends", "Friends", "_friends" };

            Guid selfGuid = cachedSelfGuid;
            selfWateredReadOk = cachedSelfGuidReadOk;
            if (!selfWateredReadOk)
            {
                selfWateredReadOk = this.TryHomelandFarmTryReadSelfPlayerGuid(out selfGuid, out bool readOk) && readOk && selfGuid != Guid.Empty;
            }

            if (selfWateredReadOk)
            {
                selfWatered = this.TryHomelandFarmComponentListContainsGuid(componentData, selfGuid, listMembers);
                int friendWaterCount = 0;
                bool friendWaterCountReadOk = false;
                if (isCropBox)
                {
                    this.TryHomelandFarmTryReadComponentListCount(
                        componentData,
                        out friendWaterCount,
                        out friendWaterCountReadOk,
                        "waterGuids",
                        "WaterGuids",
                        "_waterGuids");
                }
                else
                {
                    this.TryHomelandFarmTryReadComponentListCount(
                        componentData,
                        out friendWaterCount,
                        out friendWaterCountReadOk,
                        "friends",
                        "Friends",
                        "_friends");
                }

                bool hasVisitorSlot = !friendWaterCountReadOk
                    || friendWaterCount < HomelandFarmMaxVisitorWaterSlots;
                canAddVisitorWater = !selfWatered && hasVisitorSlot;
                return true;
            }

            return true;
        }

        private bool TryHomelandFarmTryReadCropBoxWaterState(
            object cropBoxData,
            out bool isWet,
            out bool wetReadOk,
            out int friendWaterCount,
            out bool friendWaterCountReadOk)
        {
            isWet = false;
            wetReadOk = false;
            friendWaterCount = 0;
            friendWaterCountReadOk = false;
            if (cropBoxData == null)
            {
                return false;
            }

            wetReadOk = this.TryHomelandFarmReadComponentBool(cropBoxData, out isWet, "isWet", "_isWet", "IsWet");

            if (this.TryHomelandFarmTryReadComponentListCount(
                    cropBoxData,
                    out friendWaterCount,
                    out friendWaterCountReadOk,
                    "waterGuids",
                    "WaterGuids",
                    "_waterGuids"))
            {
            }
            else if (this.homelandFarmCropBoxGetWaterCountMethod != null && !(cropBoxData is HomelandFarmAuraComponentData))
            {
                try
                {
                    object rawCount = this.homelandFarmCropBoxGetWaterCountMethod.Invoke(cropBoxData, null);
                    if (rawCount != null)
                    {
                        friendWaterCount = Convert.ToInt32(rawCount);
                        friendWaterCountReadOk = true;
                    }
                }
                catch
                {
                }
            }
            else if (cropBoxData is HomelandFarmAuraComponentData auraCropBox && auraCropBox.Handle != IntPtr.Zero)
            {
                IntPtr dataHandle = this.TryHomelandFarmResolveAuraComponentDataHandle(auraCropBox.Handle);

                wetReadOk = this.TryGetMonoBoolMember(dataHandle, "isWet", out isWet)
                    || this.TryGetMonoBoolMember(dataHandle, "_isWet", out isWet)
                    || this.TryGetMonoBoolMember(dataHandle, "IsWet", out isWet);

                if (this.TryGetMonoObjectMember(dataHandle, "waterGuids", out IntPtr waterGuidsObj) && waterGuidsObj != IntPtr.Zero)
                {
                    List<IntPtr> items = new List<IntPtr>();
                    if (this.TryEnumerateAuraMonoCollectionItems(waterGuidsObj, items))
                    {
                        friendWaterCount = items.Count;
                        friendWaterCountReadOk = true;
                    }
                }

                if (!friendWaterCountReadOk
                    && this.TryInvokeAuraMonoZeroArg(dataHandle, out IntPtr countObj, "GetWaterCount")
                    && countObj != IntPtr.Zero)
                {
                    friendWaterCountReadOk = this.TryUnboxMonoInt32(countObj, out friendWaterCount)
                        || this.TryGetMonoInt32Member(countObj, "m_Value", out friendWaterCount);
                }
            }

            return wetReadOk || friendWaterCountReadOk;
        }

        private bool TryHomelandFarmTryReadCropBoxNeedsWater(object cropBoxData, uint ownerId, out bool needsWater)
        {
            needsWater = true;
            if (!this.TryHomelandFarmTryReadCropBoxWaterState(
                    cropBoxData,
                    out bool isWet,
                    out bool wetReadOk,
                    out int friendWaterCount,
                    out bool friendWaterCountReadOk))
            {
                needsWater = true;
                return false;
            }

            if (!wetReadOk && !friendWaterCountReadOk)
            {
                needsWater = true;
                return false;
            }

            // Pass the real ownerId so the visitor-water short-circuit only applies when we are
            // actually VISITING. On our OWN field (ownerId == playerNetId) visiting is false, so we
            // fall through to the owner-water logic below (needs water when the box isn't wet) —
            // otherwise own dry crops with 0<friends<max get wrongly skipped as "fully watered".
            this.TryGetHomelandFarmPlayerNetId(out uint playerNetId, out _);
            if (this.TryHomelandFarmTryResolveVisitorWaterState(cropBoxData, ownerId, playerNetId, isCropBox: true, out bool selfWatered, out bool selfWateredReadOk, out bool canAddVisitorWater))
            {
                if (selfWateredReadOk)
                {
                    needsWater = canAddVisitorWater;
                    return true;
                }
            }

            if (wetReadOk && friendWaterCountReadOk && isWet && friendWaterCount > 0)
            {
                if (selfWateredReadOk)
                {
                    needsWater = canAddVisitorWater;
                    return true;
                }

                needsWater = true;
                return true;
            }

            if (wetReadOk && friendWaterCountReadOk)
            {
                needsWater = !isWet || friendWaterCount == 0;
            }
            else if (wetReadOk)
            {
                needsWater = !isWet;
                if (isWet && !friendWaterCountReadOk)
                {
                    needsWater = true;
                }
            }
            else
            {
                needsWater = friendWaterCount == 0;
            }

            return true;
        }

        private bool TryHomelandFarmTryReadPlantWaterState(
            object plantData,
            out bool masterWater,
            out bool masterWaterReadOk,
            out bool weatherWater,
            out bool weatherWaterReadOk,
            out int friendWaterCount,
            out bool friendWaterCountReadOk,
            out int waterLevel,
            out bool waterLevelReadOk,
            out int stage,
            out bool stageReadOk)
        {
            masterWater = false;
            masterWaterReadOk = false;
            weatherWater = false;
            weatherWaterReadOk = false;
            friendWaterCount = 0;
            friendWaterCountReadOk = false;
            waterLevel = 0;
            waterLevelReadOk = false;
            stage = 0;
            stageReadOk = false;
            if (plantData == null)
            {
                return false;
            }

            masterWaterReadOk = this.TryHomelandFarmReadComponentBool(plantData, out masterWater, "masterWater", "_masterWater", "MasterWater");
            weatherWaterReadOk = this.TryHomelandFarmReadComponentBool(plantData, out weatherWater, "weatherWater", "_weatherWater", "WeatherWater");
            stageReadOk = this.TryHomelandFarmReadComponentInt(plantData, out stage, "stage", "_stage", "Stage");
            friendWaterCountReadOk = this.TryHomelandFarmTryReadComponentListCount(
                plantData,
                out friendWaterCount,
                out friendWaterCountReadOk,
                "friends",
                "Friends",
                "_friends");

            if (plantData is HomelandFarmAuraComponentData auraPlant && auraPlant.Handle != IntPtr.Zero)
            {
                IntPtr dataHandle = this.TryHomelandFarmResolveAuraComponentDataHandle(auraPlant.Handle);

                if (!masterWaterReadOk)
                {
                    masterWaterReadOk = this.TryGetMonoBoolMember(dataHandle, "masterWater", out masterWater)
                        || this.TryGetMonoBoolMember(dataHandle, "_masterWater", out masterWater)
                        || this.TryGetMonoBoolMember(dataHandle, "MasterWater", out masterWater);
                }

                if (!weatherWaterReadOk)
                {
                    weatherWaterReadOk = this.TryGetMonoBoolMember(dataHandle, "weatherWater", out weatherWater)
                        || this.TryGetMonoBoolMember(dataHandle, "_weatherWater", out weatherWater)
                        || this.TryGetMonoBoolMember(dataHandle, "WeatherWater", out weatherWater);
                }

                if (!stageReadOk)
                {
                    stageReadOk = this.TryGetMonoInt32Member(dataHandle, "stage", out stage)
                        || this.TryGetMonoInt32Member(dataHandle, "_stage", out stage)
                        || this.TryGetMonoInt32Member(dataHandle, "Stage", out stage);
                }

                if (!friendWaterCountReadOk
                    && this.TryGetMonoObjectMember(dataHandle, "friends", out IntPtr friendsObj)
                    && friendsObj != IntPtr.Zero)
                {
                    List<IntPtr> items = new List<IntPtr>();
                    if (this.TryEnumerateAuraMonoCollectionItems(friendsObj, items))
                    {
                        friendWaterCount = items.Count;
                        friendWaterCountReadOk = true;
                    }
                }
            }

            if (masterWaterReadOk || weatherWaterReadOk || friendWaterCountReadOk)
            {
                waterLevel = HomelandFarmComputePlantWaterLevel(masterWater, weatherWater, friendWaterCount);
                waterLevelReadOk = true;
            }

            return masterWaterReadOk || weatherWaterReadOk || friendWaterCountReadOk || stageReadOk;
        }

        private bool TryHomelandFarmTryReadPlantNeedsWater(object plantData, uint ownerId, out bool needsWater)
        {
            needsWater = true;
            if (!this.TryHomelandFarmTryReadPlantWaterState(
                    plantData,
                    out bool masterWater,
                    out bool masterWaterReadOk,
                    out bool weatherWater,
                    out bool weatherWaterReadOk,
                    out int friendWaterCount,
                    out bool friendWaterCountReadOk,
                    out _,
                    out _,
                    out _,
                    out _))
            {
                needsWater = true;
                return false;
            }

            bool ownerWatered = (masterWaterReadOk && masterWater) || (weatherWaterReadOk && weatherWater);
            this.TryGetHomelandFarmPlayerNetId(out uint playerNetId, out _);
            bool onOwnField = this.TryHomelandFarmIsOnOwnFarmField(playerNetId);
            bool visiting = !onOwnField && playerNetId != 0U && (ownerId == 0U || ownerId != playerNetId);
            if (visiting)
            {
                if (this.TryHomelandFarmTryResolveVisitorWaterState(plantData, ownerId, playerNetId, isCropBox: false, out bool selfWatered, out bool selfWateredReadOk, out bool canAddVisitorWater))
                {
                    if (selfWateredReadOk)
                    {
                        needsWater = canAddVisitorWater;
                        return true;
                    }

                    needsWater = true;
                    return true;
                }
            }

            if (!ownerWatered)
            {
                needsWater = true;
                return true;
            }

            if (this.TryHomelandFarmTryResolveVisitorWaterState(plantData, ownerId, playerNetId, isCropBox: false, out bool selfWateredAfterOwner, out bool selfWateredReadOkAfterOwner, out bool canAddVisitorWaterAfterOwner))
            {
                if (selfWateredReadOkAfterOwner)
                {
                    needsWater = canAddVisitorWaterAfterOwner;
                    return true;
                }

                needsWater = true;
                return true;
            }

            if (selfWateredReadOkAfterOwner && selfWateredAfterOwner)
            {
                needsWater = false;
                return true;
            }

            if (friendWaterCountReadOk && friendWaterCount == 0)
            {
                needsWater = ownerId != 0U && playerNetId != 0U && ownerId != playerNetId;
                return true;
            }

            needsWater = false;
            return true;
        }

        private static string HomelandFarmFormatDiagnosticValue(bool readOk, bool value)
        {
            return readOk ? value.ToString() : "?";
        }

        private static string HomelandFarmFormatDiagnosticValue(bool readOk, int value)
        {
            return readOk ? value.ToString() : "?";
        }

        private static string HomelandFarmFormatDiagnosticCanAddVisitorWater(bool selfWateredReadOk, bool canAddVisitorWater)
        {
            return selfWateredReadOk ? canAddVisitorWater.ToString() : "?";
        }

        private static string HomelandFarmFormatDiagnosticSelfWater(bool onOwnField, bool ownerWateredReadOk, bool ownerWatered, bool visitorSelfWateredReadOk, bool visitorSelfWatered)
        {
            if (onOwnField)
            {
                return HomelandFarmFormatDiagnosticValue(ownerWateredReadOk, ownerWatered);
            }

            return HomelandFarmFormatDiagnosticValue(visitorSelfWateredReadOk, visitorSelfWatered);
        }

        private static string HomelandFarmFormatDiagnosticCanAddVisitorWater(bool onOwnField, bool selfWateredReadOk, bool canAddVisitorWater)
        {
            if (onOwnField)
            {
                return "owner";
            }

            return HomelandFarmFormatDiagnosticCanAddVisitorWater(selfWateredReadOk, canAddVisitorWater);
        }

        private static string HomelandFarmFormatAtMaxWater(bool totalWaterLevelReadOk, int totalWaterLevel)
        {
            return totalWaterLevelReadOk ? (totalWaterLevel >= HomelandFarmMaxTotalWaterLevel).ToString() : "?";
        }

        private bool TryHomelandFarmTryReadLevelObjectEntityNetId(IntPtr levelObjectObj, out uint entityNetId)
        {
            entityNetId = 0U;
            if (levelObjectObj == IntPtr.Zero)
            {
                return false;
            }

            string[] members = { "ownerNetId", "OwnerNetId" };
            for (int i = 0; i < members.Length; i++)
            {
                if (this.TryGetMonoUInt32Member(levelObjectObj, members[i], out entityNetId) && entityNetId != 0U)
                {
                    return true;
                }
            }

            if (this.TryGetMonoObjectMember(levelObjectObj, "_data", out IntPtr dataObj) && dataObj != IntPtr.Zero)
            {
                for (int i = 0; i < members.Length; i++)
                {
                    if (this.TryGetMonoUInt32Member(dataObj, members[i], out entityNetId) && entityNetId != 0U)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool TryHomelandFarmTryReadLevelObjectEntityNetId(object levelObject, out uint entityNetId)
        {
            entityNetId = 0U;
            if (levelObject == null)
            {
                return false;
            }

            if (this.TryGetUIntMember(levelObject, "ownerNetId", out entityNetId) && entityNetId != 0U)
            {
                return true;
            }

            if (this.TryGetUIntMember(levelObject, "OwnerNetId", out entityNetId) && entityNetId != 0U)
            {
                return true;
            }

            object data = this.TryGetManagedMemberValue(levelObject, "_data");
            if (data != null)
            {
                if (this.TryGetUIntMember(data, "ownerNetId", out entityNetId) && entityNetId != 0U)
                {
                    return true;
                }

                if (this.TryGetUIntMember(data, "OwnerNetId", out entityNetId) && entityNetId != 0U)
                {
                    return true;
                }
            }

            return false;
        }

        private void LogHomelandFarmRadiusWaterDiagnostics()
        {
            try
            {
                this.LogHomelandFarmRadiusWaterDiagnosticsCore();
            }
            catch (Exception ex)
            {
                this.HomelandFarmLog("Water diagnostics crashed: " + ex.Message);
                this.homelandFarmLastStatus = "homeland_farm.log_water_failed";
            }
        }

        private void LogHomelandFarmRadiusWaterDiagnosticsCore()
        {
            if (!this.EnsureHomelandFarmReflectionReady())
            {
                this.HomelandFarmLog("Water diagnostics: reflection unavailable.");
                return;
            }

            if (!this.TryHomelandFarmIsInHomeland(out string homelandStatus, allowVisitingFarmArea: true))
            {
                this.HomelandFarmLog("Water diagnostics: " + homelandStatus);
                return;
            }

            if (!this.TryGetHomelandFarmPlayerPosition(out Vector3 playerPos))
            {
                this.HomelandFarmLog("Water diagnostics: player position unavailable.");
                return;
            }

            float radius = this.homelandFarmWaterRadius;
            HashSet<uint> netIds = new HashSet<uint>();
            if (!this.TryHomelandFarmCollectFarmEntityNetIds(
                    netIds,
                    out string scanSource,
                    playerPos,
                    radius,
                    allowUnsafeAuraMonoGetComponents: HomelandFarmAllowUnsafeAuraMonoGetComponents,
                    proximityBudgetSeconds: HomelandFarmWaterLogProximityBudgetSeconds,
                    allowAuraEntityFunnel: false,
                    useAutoFarmCollectShortcuts: false))
            {
                this.HomelandFarmLog("Water diagnostics: no farm entities found.");
                return;
            }

            float radiusSq = radius * radius;
            int cropCount = 0;
            int cropPlantCount = 0;
            int plantCount = 0;
            int skippedNoPosition = 0;
            int skippedOutOfRadius = 0;
            int skippedNoFarmData = 0;
            this.TryGetHomelandFarmPlayerNetId(out uint playerNetId, out _);
            this.TryHomelandFarmTryReadSelfPlayerGuid(out Guid selfPlayerGuid, out bool selfPlayerGuidReadOk);
            bool onOwnField = this.TryHomelandFarmIsOnOwnFarmField(playerNetId);
            this.HomelandFarmLog(
                "=== Water diagnostics radius=" + radius.ToString("F1")
                + "m playerPos=" + playerPos
                + " playerNetId=" + playerNetId
                + " selfGuid=" + (selfPlayerGuidReadOk ? selfPlayerGuid.ToString() : "?")
                + " scanned=" + netIds.Count + " source=" + scanSource + " ===");

            List<string> diagnosticLines = new List<string>(netIds.Count);
            bool useAuraBatchRead = this.HomelandFarmPrefersAuraComponentData()
                && this.EnsureAuraMonoApiReady()
                && this.AttachAuraMonoThread();

            foreach (uint netId in netIds)
            {
                if (netId == 0U)
                {
                    continue;
                }

                Vector3 position = Vector3.zero;
                this.TryHomelandFarmResolveFarmEntityPosition(netId, out position);
                if (position == Vector3.zero)
                {
                    skippedNoPosition++;
                    continue;
                }

                float distance = Vector3.Distance(position, playerPos);
                if (distance * distance > radiusSq)
                {
                    skippedOutOfRadius++;
                    continue;
                }

                if (useAuraBatchRead
                    && this.TryGetAuraMonoEntityObjectByNetId(netId, out IntPtr entityObj)
                    && entityObj != IntPtr.Zero
                    && this.TryHomelandFarmTryGuardAuraEntityBeforeHeavyAccess(entityObj)
                    && this.TryHomelandFarmAuraExtractFarmDataHandles(
                        entityObj,
                        out IntPtr cropItemDataHandle,
                        out IntPtr cropBoxItemDataHandle,
                        out IntPtr plantItemDataHandle))
                {
                    if (cropItemDataHandle != IntPtr.Zero)
                    {
                        cropPlantCount++;
                        object cropPlantData = HomelandFarmAuraData(cropItemDataHandle);
                        this.TryHomelandFarmTryReadOwnerId(netId, out uint cropOwnerId);
                        bool cropStageReadOk = this.TryHomelandFarmReadComponentInt(cropPlantData, out int cropStage, "stage", "_stage", "Stage");
                        bool hasWeedReadOk = this.TryHomelandFarmReadComponentBool(cropPlantData, out bool hasWeed, "hasWeed", "_hasWeed", "HasWeed");
                        bool cropMasterWaterReadOk = this.TryHomelandFarmReadComponentBool(cropPlantData, out bool cropMasterWater, "masterWater", "_masterWater", "MasterWater");
                        bool cropWeatherWaterReadOk = this.TryHomelandFarmReadComponentBool(cropPlantData, out bool cropWeatherWater, "weatherWater", "_weatherWater", "WeatherWater");
                        bool cropManureReadOk = this.TryHomelandFarmReadComponentInt(cropPlantData, out int cropManureId, "manureId", "_manureId", "ManureId");
                        diagnosticLines.Add(
                            "[Diag] cropPlant netId=" + netId
                            + " owner=" + cropOwnerId
                            + " dist=" + distance.ToString("F1") + "m"
                            + " stage=" + HomelandFarmFormatDiagnosticValue(cropStageReadOk, cropStage)
                            + " weedable=" + HomelandFarmFormatDiagnosticValue(hasWeedReadOk, hasWeed)
                            + " ownerWatered=" + HomelandFarmFormatDiagnosticValue(cropMasterWaterReadOk, cropMasterWater)
                            + " weatherWater=" + HomelandFarmFormatDiagnosticValue(cropWeatherWaterReadOk, cropWeatherWater)
                            + " manure=" + HomelandFarmFormatDiagnosticValue(cropManureReadOk, cropManureId));
                        continue;
                    }

                    uint waterNetId = netId;
                    if (cropBoxItemDataHandle == IntPtr.Zero
                        && plantItemDataHandle == IntPtr.Zero
                        && !this.TryHomelandFarmTryNormalizeWaterNetId(netId, netIds, out waterNetId))
                    {
                        skippedNoFarmData++;
                        continue;
                    }

                    this.TryHomelandFarmTryReadOwnerId(waterNetId, out uint ownerId);
                    if (ownerId == 0U && this.TryHomelandFarmGetSelfPlayInFieldOwnerNetId(out uint fieldOwnerNetId) && fieldOwnerNetId != 0U)
                    {
                        ownerId = fieldOwnerNetId;
                    }

                    if (cropBoxItemDataHandle != IntPtr.Zero)
                    {
                        cropCount++;
                        object cropBoxData = HomelandFarmAuraData(cropBoxItemDataHandle);
                        this.TryHomelandFarmTryReadCropBoxWaterState(
                            cropBoxData,
                            out bool ownerWatered,
                            out bool ownerWateredReadOk,
                            out int friendWaterCount,
                            out bool friendWaterCountReadOk);
                        int totalWaterLevel = HomelandFarmComputeCropTotalWaterLevel(ownerWateredReadOk && ownerWatered, friendWaterCountReadOk ? friendWaterCount : 0);
                        this.TryHomelandFarmTryResolveVisitorWaterState(
                            cropBoxData,
                            ownerId,
                            playerNetId,
                            isCropBox: true,
                            selfPlayerGuid,
                            selfPlayerGuidReadOk,
                            out bool selfWatered,
                            out bool selfWateredReadOk,
                            out bool canAddVisitorWater);
                        diagnosticLines.Add(
                            "[Diag] cropBox netId=" + waterNetId
                            + " owner=" + ownerId
                            + " dist=" + distance.ToString("F1") + "m"
                            + " ownerWatered=" + HomelandFarmFormatDiagnosticValue(ownerWateredReadOk, ownerWatered)
                            + " friendWaterCount=" + HomelandFarmFormatDiagnosticValue(friendWaterCountReadOk, friendWaterCount)
                            + " totalWaterLevel=" + totalWaterLevel
                            + " atMaxWater=" + HomelandFarmFormatAtMaxWater(ownerWateredReadOk || friendWaterCountReadOk, totalWaterLevel)
                            + " selfWatered=" + HomelandFarmFormatDiagnosticSelfWater(onOwnField, ownerWateredReadOk, ownerWatered, selfWateredReadOk, selfWatered)
                            + " canAddVisitorWater=" + HomelandFarmFormatDiagnosticCanAddVisitorWater(onOwnField, selfWateredReadOk, canAddVisitorWater));
                        continue;
                    }

                    if (plantItemDataHandle != IntPtr.Zero)
                    {
                        plantCount++;
                        object plantData = HomelandFarmAuraData(plantItemDataHandle);
                        this.TryHomelandFarmTryReadPlantWaterState(
                            plantData,
                            out bool masterWater,
                            out bool masterWaterReadOk,
                            out bool weatherWater,
                            out bool weatherWaterReadOk,
                            out int friendWaterCount,
                            out bool friendWaterCountReadOk,
                            out int waterLevel,
                            out bool waterLevelReadOk,
                            out int stage,
                            out bool stageReadOk);
                        bool plantOwnerWatered = (masterWaterReadOk && masterWater) || (weatherWaterReadOk && weatherWater);
                        bool plantOwnerWateredReadOk = masterWaterReadOk || weatherWaterReadOk;
                        this.TryHomelandFarmTryResolveVisitorWaterState(
                            plantData,
                            ownerId,
                            playerNetId,
                            isCropBox: false,
                            selfPlayerGuid,
                            selfPlayerGuidReadOk,
                            out bool selfWatered,
                            out bool selfWateredReadOk,
                            out bool canAddVisitorWater);
                        diagnosticLines.Add(
                            "[Diag] plant netId=" + waterNetId
                            + " owner=" + ownerId
                            + " dist=" + distance.ToString("F1") + "m"
                            + " stage=" + HomelandFarmFormatDiagnosticValue(stageReadOk, stage)
                            + " ownerWatered=" + HomelandFarmFormatDiagnosticValue(masterWaterReadOk, masterWater)
                            + " weatherWater=" + HomelandFarmFormatDiagnosticValue(weatherWaterReadOk, weatherWater)
                            + " friendWaterCount=" + HomelandFarmFormatDiagnosticValue(friendWaterCountReadOk, friendWaterCount)
                            + " totalWaterLevel=" + HomelandFarmFormatDiagnosticValue(waterLevelReadOk, waterLevel)
                            + " atMaxWater=" + HomelandFarmFormatAtMaxWater(waterLevelReadOk, waterLevel)
                            + " selfWatered=" + HomelandFarmFormatDiagnosticSelfWater(onOwnField, plantOwnerWateredReadOk, plantOwnerWatered, selfWateredReadOk, selfWatered)
                            + " canAddVisitorWater=" + HomelandFarmFormatDiagnosticCanAddVisitorWater(onOwnField, selfWateredReadOk, canAddVisitorWater));
                        continue;
                    }

                    skippedNoFarmData++;
                    continue;
                }

                // Crop entities (CropItemData) are a separate entity from the crop box (CropBoxItemData).
                // hasWeed lives here, and weed/harvest commands target this entity's own netId.
                if (this.TryHomelandFarmGetComponentData(this.homelandFarmCropItemDataType, netId, out object legacyCropPlantData, out _, "CropItemData"))
                {
                    cropPlantCount++;
                    this.TryHomelandFarmTryReadOwnerId(netId, out uint cropOwnerId);
                    bool cropStageReadOk = this.TryHomelandFarmReadComponentInt(legacyCropPlantData, out int cropStage, "stage", "_stage", "Stage");
                    bool hasWeedReadOk = this.TryHomelandFarmReadComponentBool(legacyCropPlantData, out bool hasWeed, "hasWeed", "_hasWeed", "HasWeed");
                    bool cropMasterWaterReadOk = this.TryHomelandFarmReadComponentBool(legacyCropPlantData, out bool cropMasterWater, "masterWater", "_masterWater", "MasterWater");
                    bool cropWeatherWaterReadOk = this.TryHomelandFarmReadComponentBool(legacyCropPlantData, out bool cropWeatherWater, "weatherWater", "_weatherWater", "WeatherWater");
                    bool cropManureReadOk = this.TryHomelandFarmReadComponentInt(legacyCropPlantData, out int cropManureId, "manureId", "_manureId", "ManureId");
                    diagnosticLines.Add(
                        "[Diag] cropPlant netId=" + netId
                        + " owner=" + cropOwnerId
                        + " dist=" + distance.ToString("F1") + "m"
                        + " stage=" + HomelandFarmFormatDiagnosticValue(cropStageReadOk, cropStage)
                        + " weedable=" + HomelandFarmFormatDiagnosticValue(hasWeedReadOk, hasWeed)
                        + " ownerWatered=" + HomelandFarmFormatDiagnosticValue(cropMasterWaterReadOk, cropMasterWater)
                        + " weatherWater=" + HomelandFarmFormatDiagnosticValue(cropWeatherWaterReadOk, cropWeatherWater)
                        + " manure=" + HomelandFarmFormatDiagnosticValue(cropManureReadOk, cropManureId));
                    if (cropManureReadOk && cropManureId > 0
                        && this.TryHomelandFarmRefreshCropManureVisual(netId, cropManureId, out string manureVisualStatus))
                    {
                        diagnosticLines.Add("[Diag] cropPlant manure visual sync: " + manureVisualStatus);
                    }

                    continue;
                }

                if (!this.TryHomelandFarmTryNormalizeWaterNetId(netId, netIds, out uint legacyWaterNetId))
                {
                    skippedNoFarmData++;
                    continue;
                }

                this.TryHomelandFarmTryReadOwnerId(legacyWaterNetId, out uint legacyOwnerId);
                if (legacyOwnerId == 0U && this.TryHomelandFarmGetSelfPlayInFieldOwnerNetId(out uint legacyFieldOwnerNetId) && legacyFieldOwnerNetId != 0U)
                {
                    legacyOwnerId = legacyFieldOwnerNetId;
                }

                if (this.TryHomelandFarmGetComponentData(this.homelandFarmCropBoxItemDataType, legacyWaterNetId, out object legacyCropBoxData, out _, "CropBoxItemData"))
                {
                    cropCount++;
                    this.TryHomelandFarmTryReadCropBoxWaterState(
                        legacyCropBoxData,
                        out bool ownerWatered,
                        out bool ownerWateredReadOk,
                        out int friendWaterCount,
                        out bool friendWaterCountReadOk);
                    int totalWaterLevel = HomelandFarmComputeCropTotalWaterLevel(ownerWateredReadOk && ownerWatered, friendWaterCountReadOk ? friendWaterCount : 0);
                    this.TryHomelandFarmTryResolveVisitorWaterState(
                        legacyCropBoxData,
                        legacyOwnerId,
                        playerNetId,
                        isCropBox: true,
                        selfPlayerGuid,
                        selfPlayerGuidReadOk,
                        out bool selfWatered,
                        out bool selfWateredReadOk,
                        out bool canAddVisitorWater);
                    diagnosticLines.Add(
                        "[Diag] cropBox netId=" + legacyWaterNetId
                        + " owner=" + legacyOwnerId
                        + " dist=" + distance.ToString("F1") + "m"
                        + " ownerWatered=" + HomelandFarmFormatDiagnosticValue(ownerWateredReadOk, ownerWatered)
                        + " friendWaterCount=" + HomelandFarmFormatDiagnosticValue(friendWaterCountReadOk, friendWaterCount)
                        + " totalWaterLevel=" + totalWaterLevel
                        + " atMaxWater=" + HomelandFarmFormatAtMaxWater(ownerWateredReadOk || friendWaterCountReadOk, totalWaterLevel)
                        + " selfWatered=" + HomelandFarmFormatDiagnosticSelfWater(onOwnField, ownerWateredReadOk, ownerWatered, selfWateredReadOk, selfWatered)
                        + " canAddVisitorWater=" + HomelandFarmFormatDiagnosticCanAddVisitorWater(onOwnField, selfWateredReadOk, canAddVisitorWater));
                    continue;
                }

                if (this.TryHomelandFarmGetComponentData(this.homelandFarmPlantItemDataType, legacyWaterNetId, out object legacyPlantData, out _, "PlantItemData"))
                {
                    plantCount++;
                    this.TryHomelandFarmTryReadPlantWaterState(
                        legacyPlantData,
                        out bool masterWater,
                        out bool masterWaterReadOk,
                        out bool weatherWater,
                        out bool weatherWaterReadOk,
                        out int friendWaterCount,
                        out bool friendWaterCountReadOk,
                        out int waterLevel,
                        out bool waterLevelReadOk,
                        out int stage,
                        out bool stageReadOk);
                    bool plantOwnerWatered = (masterWaterReadOk && masterWater) || (weatherWaterReadOk && weatherWater);
                    bool plantOwnerWateredReadOk = masterWaterReadOk || weatherWaterReadOk;
                    this.TryHomelandFarmTryResolveVisitorWaterState(
                        legacyPlantData,
                        legacyOwnerId,
                        playerNetId,
                        isCropBox: false,
                        selfPlayerGuid,
                        selfPlayerGuidReadOk,
                        out bool selfWatered,
                        out bool selfWateredReadOk,
                        out bool canAddVisitorWater);
                    diagnosticLines.Add(
                        "[Diag] plant netId=" + legacyWaterNetId
                        + " owner=" + legacyOwnerId
                        + " dist=" + distance.ToString("F1") + "m"
                        + " stage=" + HomelandFarmFormatDiagnosticValue(stageReadOk, stage)
                        + " ownerWatered=" + HomelandFarmFormatDiagnosticValue(masterWaterReadOk, masterWater)
                        + " weatherWater=" + HomelandFarmFormatDiagnosticValue(weatherWaterReadOk, weatherWater)
                        + " friendWaterCount=" + HomelandFarmFormatDiagnosticValue(friendWaterCountReadOk, friendWaterCount)
                        + " totalWaterLevel=" + HomelandFarmFormatDiagnosticValue(waterLevelReadOk, waterLevel)
                        + " atMaxWater=" + HomelandFarmFormatAtMaxWater(waterLevelReadOk, waterLevel)
                        + " selfWatered=" + HomelandFarmFormatDiagnosticSelfWater(onOwnField, plantOwnerWateredReadOk, plantOwnerWatered, selfWateredReadOk, selfWatered)
                        + " canAddVisitorWater=" + HomelandFarmFormatDiagnosticCanAddVisitorWater(onOwnField, selfWateredReadOk, canAddVisitorWater));
                    continue;
                }

                skippedNoFarmData++;
            }

            for (int i = 0; i < diagnosticLines.Count; i++)
            {
                this.HomelandFarmLog(diagnosticLines[i]);
            }

            this.HomelandFarmLog(
                "=== Water diagnostics summary: cropBoxes=" + cropCount
                + " cropPlants=" + cropPlantCount
                + " plants=" + plantCount
                + " in radius skippedNoPos=" + skippedNoPosition
                + " skippedOutOfRadius=" + skippedOutOfRadius
                + " skippedNoFarmData=" + skippedNoFarmData + " ===");
            this.homelandFarmLastStatus = "homeland_farm.log_water_done";
        }

        private bool TryHomelandFarmTryNormalizeWaterNetId(uint netId, HashSet<uint> scanSet, out uint waterNetId)
        {
            waterNetId = netId;
            if (netId == 0U)
            {
                return false;
            }

            if (this.TryHomelandFarmGetComponentData(this.homelandFarmCropBoxItemDataType, netId, out _, out _, "CropBoxItemData")
                || this.TryHomelandFarmGetComponentData(this.homelandFarmPlantItemDataType, netId, out _, out _, "PlantItemData"))
            {
                return true;
            }

            if (!this.TryHomelandFarmGetComponentData(this.homelandFarmCropItemDataType, netId, out _, out _, "CropItemData"))
            {
                return false;
            }

            if (scanSet != null)
            {
                foreach (uint candidate in scanSet)
                {
                    if (candidate == 0U || candidate == netId)
                    {
                        continue;
                    }

                    if (!this.TryHomelandFarmGetComponentData(this.homelandFarmCropBoxItemDataType, candidate, out object cropBoxData, out _, "CropBoxItemData")
                        || cropBoxData == null)
                    {
                        continue;
                    }

                    string[] cropLinkMembers = { "cropNetId", "CropNetId", "childCropNetId", "linkedCropNetId", "LinkedCropNetId" };
                    for (int i = 0; i < cropLinkMembers.Length; i++)
                    {
                        if (this.TryHomelandFarmReadComponentUInt(cropBoxData, out uint linkedCropNetId, cropLinkMembers[i])
                            && linkedCropNetId == netId)
                        {
                            waterNetId = candidate;
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        private bool TryHomelandFarmBuildWaterTarget(uint netId, out HomelandFarmTarget target)
        {
            target = null;
            if (netId == 0U)
            {
                return false;
            }

            if (!this.TryHomelandFarmTryReadOwnerId(netId, out uint ownerId))
            {
                ownerId = 0U;
            }

            if (ownerId == 0U && this.TryHomelandFarmGetSelfPlayInFieldOwnerNetId(out uint fieldOwnerNetId) && fieldOwnerNetId != 0U)
            {
                ownerId = fieldOwnerNetId;
            }

            Vector3 position = Vector3.zero;
            this.TryHomelandFarmResolveFarmEntityPosition(netId, out position);

            if (this.TryHomelandFarmGetComponentData(this.homelandFarmCropBoxItemDataType, netId, out object cropBoxData, out _, "CropBoxItemData"))
            {
                target = new HomelandFarmTarget
                {
                    NetId = netId,
                    OwnerId = ownerId,
                    IsCropBox = true,
                    NeedsWater = this.TryHomelandFarmTryReadCropBoxNeedsWater(cropBoxData, ownerId, out bool needsWater)
                        ? needsWater
                        : true,
                    Position = position
                };
                return true;
            }

            if (this.TryHomelandFarmGetComponentData(this.homelandFarmPlantItemDataType, netId, out object plantData, out _, "PlantItemData"))
            {
                target = new HomelandFarmTarget
                {
                    NetId = netId,
                    OwnerId = ownerId,
                    IsCropBox = false,
                    NeedsWater = this.TryHomelandFarmTryReadPlantNeedsWater(plantData, ownerId, out bool needsWater)
                        ? needsWater
                        : true,
                    Position = position
                };
                return true;
            }

            return false;
        }

        private bool TryHomelandFarmTryReadOwnerId(uint netId, out uint ownerId)
        {
            ownerId = 0U;
            if (netId == 0U)
            {
                return false;
            }

            if (this.homelandFarmAuraLevelObjectOwnerByNetId.TryGetValue(netId, out ownerId) && ownerId != 0U)
            {
                return true;
            }

            this.TryHomelandFarmCacheAuraLevelObjectPositions(false, allowDictionaryScan: false);
            if (this.homelandFarmAuraLevelObjectOwnerByNetId.TryGetValue(netId, out ownerId) && ownerId != 0U)
            {
                return true;
            }

            if (this.EnsureAuraMonoApiReady()
                && this.TryResolveOwnerIdFromLevelObjectIdMono(netId, out ownerId)
                && ownerId != 0U)
            {
                this.homelandFarmAuraLevelObjectOwnerByNetId[netId] = ownerId;
                return true;
            }

            if (!this.TryHomelandFarmGetComponentData(this.homelandFarmLevelEntityComponentDataType, netId, out object levelEntityData, out _, "LevelEntityComponentData"))
            {
                return false;
            }

            if (levelEntityData is HomelandFarmAuraComponentData auraLevelEntity && auraLevelEntity.Handle != IntPtr.Zero)
            {
                if (this.TryGetMonoUInt32Member(auraLevelEntity.Handle, "ownerId", out ownerId) && ownerId != 0U)
                {
                    return true;
                }

                if (this.TryGetMonoUInt32Member(auraLevelEntity.Handle, "OwnerId", out ownerId) && ownerId != 0U)
                {
                    return true;
                }

                if (this.TryGetMonoUInt32Member(auraLevelEntity.Handle, "ownerNetId", out ownerId) && ownerId != 0U)
                {
                    return true;
                }

                return this.TryGetMonoUInt32Member(auraLevelEntity.Handle, "OwnerNetId", out ownerId) && ownerId != 0U;
            }

            if (this.TryGetUIntMember(levelEntityData, "ownerId", out ownerId) && ownerId != 0U)
            {
                return true;
            }

            if (this.TryGetUIntMember(levelEntityData, "OwnerId", out ownerId) && ownerId != 0U)
            {
                return true;
            }

            if (this.TryGetUIntMember(levelEntityData, "ownerNetId", out ownerId) && ownerId != 0U)
            {
                return true;
            }

            return this.TryGetUIntMember(levelEntityData, "OwnerNetId", out ownerId) && ownerId != 0U;
        }

        private uint TryHomelandFarmResolveWaterBatchOwner(uint ownerId, HomelandFarmWaterMode mode, uint playerNetId)
        {
            if (ownerId != 0U)
            {
                return ownerId;
            }

            if (this.TryHomelandFarmGetSelfPlayInFieldOwnerNetId(out uint fieldOwnerNetId) && fieldOwnerNetId != 0U)
            {
                return fieldOwnerNetId;
            }

            if (playerNetId != 0U)
            {
                return playerNetId;
            }

            return 0U;
        }

        private bool TryHomelandFarmGetSelfPlayInFieldOwnerNetId(out uint fieldOwnerNetId)
        {
            fieldOwnerNetId = 0U;
            if (this.TryGetManagedSelfPlayerObject(out object localPlayer, out _)
                && localPlayer != null
                && this.TryGetUIntMember(localPlayer, "inFieldOwnerId", out fieldOwnerNetId)
                && fieldOwnerNetId != 0U)
            {
                return true;
            }

            if (this.TryGetManagedSelfPlayerObject(out localPlayer, out _)
                && localPlayer != null
                && this.TryGetUIntMember(localPlayer, "InFieldOwnerId", out fieldOwnerNetId)
                && fieldOwnerNetId != 0U)
            {
                return true;
            }

            Type gameplayApiType = this.FindLoadedType(
                "XDTLevelAndEntity.GameplaySystem.GameplayApi",
                "ScriptsRefactory.LevelAndEntity.GameplaySystem.GameplayApi",
                "GameplayApi");
            if (gameplayApiType != null)
            {
                MethodInfo getFieldOwnerMethod = this.GetMethodQuiet(
                    gameplayApiType,
                    "GetSelfPlayInFieldOwnerNetId",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                    Type.EmptyTypes);
                if (getFieldOwnerMethod != null)
                {
                    try
                    {
                        object value = getFieldOwnerMethod.Invoke(null, null);
                        fieldOwnerNetId = value is uint u ? u : Convert.ToUInt32(value);
                        if (fieldOwnerNetId != 0U)
                        {
                            return true;
                        }
                    }
                    catch
                    {
                    }
                }
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return false;
            }

            string[] localPlayerMembers = { "inFieldOwnerId", "InFieldOwnerId", "inFieldNetId", "InFieldNetId" };
            if (this.TryHomelandFarmTryReadAuraLocalPlayerUIntField(localPlayerMembers, out fieldOwnerNetId, out _))
            {
                return true;
            }

            IntPtr gameplayApiClass = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.GameplaySystem.GameplayApi");
            if (gameplayApiClass == IntPtr.Zero)
            {
                gameplayApiClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTLevelAndEntity.GameplaySystem", "GameplayApi");
            }

            if (gameplayApiClass != IntPtr.Zero && auraMonoRuntimeInvoke != null)
            {
                IntPtr getFieldOwnerMethod = this.FindAuraMonoMethodOnHierarchy(gameplayApiClass, "GetSelfPlayInFieldOwnerNetId", 0);
                if (getFieldOwnerMethod != IntPtr.Zero)
                {
                    IntPtr exc = IntPtr.Zero;
                    IntPtr resultObj = auraMonoRuntimeInvoke(getFieldOwnerMethod, IntPtr.Zero, IntPtr.Zero, ref exc);
                    if (exc == IntPtr.Zero && resultObj != IntPtr.Zero && this.TryUnboxAuraUInt32(resultObj, out fieldOwnerNetId) && fieldOwnerNetId != 0U)
                    {
                        return true;
                    }
                }
            }

            if (!this.TryGetHomelandFarmPlayerNetId(out uint playerNetId, out _) || playerNetId == 0U)
            {
                return false;
            }

            if (!this.TryGetAuraMonoEntityObjectByNetId(playerNetId, out IntPtr entityObj) || entityObj == IntPtr.Zero)
            {
                return false;
            }

            for (int i = 0; i < localPlayerMembers.Length; i++)
            {
                if (this.TryGetMonoUInt32Member(entityObj, localPlayerMembers[i], out fieldOwnerNetId) && fieldOwnerNetId != 0U)
                {
                    return true;
                }
            }

            return false;
        }

        private unsafe bool TryUnboxAuraUInt32(IntPtr boxedObj, out uint value)
        {
            value = 0U;
            if (boxedObj == IntPtr.Zero || auraMonoObjectUnbox == null)
            {
                return false;
            }

            IntPtr raw = auraMonoObjectUnbox(boxedObj);
            if (raw == IntPtr.Zero)
            {
                return false;
            }

            value = *(uint*)raw;
            return true;
        }

        private void TryHomelandFarmResolveWaterTargetOwners(List<HomelandFarmTarget> targets, HomelandFarmWaterMode mode, uint playerNetId)
        {
            if (targets == null || targets.Count == 0)
            {
                return;
            }

            uint inferredFieldOwner = 0U;
            for (int i = 0; i < targets.Count; i++)
            {
                HomelandFarmTarget target = targets[i];
                if (target == null || target.OwnerId != 0U)
                {
                    if (target != null && target.OwnerId != 0U && inferredFieldOwner == 0U)
                    {
                        inferredFieldOwner = target.OwnerId;
                    }

                    continue;
                }

                if (this.TryHomelandFarmTryReadOwnerId(target.NetId, out uint ownerId) && ownerId != 0U)
                {
                    target.OwnerId = ownerId;
                    if (inferredFieldOwner == 0U)
                    {
                        inferredFieldOwner = ownerId;
                    }
                }
            }

            for (int i = 0; i < targets.Count; i++)
            {
                HomelandFarmTarget target = targets[i];
                if (target == null || target.OwnerId != 0U)
                {
                    continue;
                }

                uint resolved = this.TryHomelandFarmResolveWaterBatchOwner(0U, mode, playerNetId);
                if (resolved != 0U)
                {
                    target.OwnerId = resolved;
                    continue;
                }

                if (inferredFieldOwner != 0U)
                {
                    target.OwnerId = inferredFieldOwner;
                }
            }
        }

        private bool TryHomelandFarmTryReadComponentNetId(object component, out uint netId)
        {
            netId = 0U;
            if (component == null)
            {
                return false;
            }

            if (this.TryGetObjectMember(component, "entity", out object entityObj) && entityObj != null
                && this.TryHomelandFarmTryReadEntityNetId(entityObj, out netId) && netId != 0U)
            {
                return true;
            }

            if (this.TryGetObjectMember(component, "Entity", out entityObj) && entityObj != null
                && this.TryHomelandFarmTryReadEntityNetId(entityObj, out netId) && netId != 0U)
            {
                return true;
            }

            return this.TryGetUIntMember(component, "netId", out netId) && netId != 0U
                || this.TryGetUIntMember(component, "NetId", out netId) && netId != 0U;
        }

        private bool TryHomelandFarmTryReadEntityNetId(object entity, out uint netId)
        {
            netId = 0U;
            if (entity == null)
            {
                return false;
            }

            string[] members = new string[] { "netId", "NetId", "ownerNetId", "OwnerNetId", "entityNetId", "mNetId", "_netId" };
            for (int i = 0; i < members.Length; i++)
            {
                if (this.TryGetUIntMember(entity, members[i], out netId) && netId != 0U)
                {
                    return true;
                }
            }

            return false;
        }

        private bool TryHomelandFarmSphereQueryNetIds(Vector3 center, float radius, HashSet<uint> output)
        {
            if (output == null || radius <= 0f)
            {
                return false;
            }

            this.EnsureHomelandFarmScannerTypes();
            if (this.auraEntitiesType == null || this.auraEntitiesSphereQueryEntitiesMethod == null)
            {
                this.ResolveAuraFarmRuntimeMethods();
            }

            if (this.auraEntitiesSphereQueryEntitiesMethod != null)
            {
                Type entityType = this.homelandFarmEntityType;
                if (entityType == null && this.auraEntityUtilGetSelfPlayerEntityMethod != null)
                {
                    entityType = this.auraEntityUtilGetSelfPlayerEntityMethod.ReturnType;
                }

                if (entityType != null)
                {
                    try
                    {
                        Type listType = typeof(List<>).MakeGenericType(entityType);
                        object results = Activator.CreateInstance(listType);
                        this.auraEntitiesSphereQueryEntitiesMethod.Invoke(null, new object[] { center, radius, results });
                        if (results is IEnumerable enumerable)
                        {
                            int added = 0;
                            foreach (object entity in enumerable)
                            {
                                if (entity == null)
                                {
                                    continue;
                                }

                                if (this.TryHomelandFarmTryReadEntityNetId(entity, out uint netId) && netId != 0U && output.Add(netId))
                                {
                                    added++;
                                }
                            }

                            if (added > 0)
                            {
                                return true;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        this.HomelandFarmLog("Aura SphereQuery failed: " + ex.Message);
                    }
                }
            }

            if (this.homelandFarmEntitiesSphereQueryEntitiesMethod == null || this.homelandFarmEntityType == null)
            {
                return false;
            }

            try
            {
                Type listType = typeof(List<>).MakeGenericType(this.homelandFarmEntityType);
                object results = Activator.CreateInstance(listType);
                this.homelandFarmEntitiesSphereQueryEntitiesMethod.Invoke(null, new object[] { center, radius, results });
                if (!(results is IEnumerable enumerable))
                {
                    return false;
                }

                int added = 0;
                foreach (object entity in enumerable)
                {
                    if (entity == null)
                    {
                        continue;
                    }

                    if (this.TryHomelandFarmTryReadEntityNetId(entity, out uint netId) && netId != 0U && output.Add(netId))
                    {
                        added++;
                    }
                }

                return added > 0;
            }
            catch (Exception ex)
            {
                this.HomelandFarmLog("SphereQuery failed: " + ex.Message);
                return false;
            }
        }

        private bool TryHomelandFarmResolveFriendService(out object friendService, out string status)
        {
            friendService = null;
            status = "Friend service unavailable.";
            if (this.homelandFarmFriendServiceType == null)
            {
                return false;
            }

            this.EnsureHomelandFarmScannerTypes();
            if (this.homelandFarmEcsServiceTryGetMethodDef != null)
            {
                try
                {
                    MethodInfo tryGetMethod = this.homelandFarmEcsServiceTryGetMethodDef.MakeGenericMethod(this.homelandFarmFriendServiceType);
                    object[] args = new object[] { null, false };
                    if (tryGetMethod.Invoke(null, args) is bool ok && ok && args[0] != null)
                    {
                        friendService = args[0];
                        this.homelandFarmFriendServiceGetFriendsMethod = friendService.GetType().GetMethod(
                            "GetFriends",
                            BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                        status = "IFriendService via EcsService.TryGet.";
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    this.HomelandFarmLog("EcsService.TryGet<IFriendService> failed: " + ex.Message);
                }
            }

            if (this.TryHomelandFarmResolveFriendServiceFromManagers(out friendService, out status))
            {
                this.homelandFarmFriendServiceGetFriendsMethod = friendService.GetType().GetMethod(
                    "GetFriends",
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                return this.homelandFarmFriendServiceGetFriendsMethod != null;
            }

            return false;
        }

        private bool TryHomelandFarmResolveFriendServiceFromManagers(out object friendService, out string status)
        {
            friendService = null;
            status = "Managers friend service unavailable.";
            try
            {
                Type managersType = this.FindLoadedType("XDTGame.Framework.Managers", "Managers");
                if (managersType == null)
                {
                    return false;
                }

                PropertyInfo instanceProperty = managersType.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                object serviceDic = null;
                object managers = instanceProperty != null ? instanceProperty.GetValue(null, null) : null;
                if (managers != null)
                {
                    serviceDic = this.TryGetManagedMemberValue(managers, "_serviceDic")
                        ?? this.TryGetManagedMemberValue(managers, "serviceDic");
                }

                if (!(serviceDic is IEnumerable enumerable))
                {
                    return false;
                }

                foreach (object entry in enumerable)
                {
                    if (entry == null)
                    {
                        continue;
                    }

                    object serviceObj = this.TryGetManagedMemberValue(entry, "Value") ?? entry;
                    if (serviceObj == null)
                    {
                        continue;
                    }

                    Type serviceType = serviceObj.GetType();
                    if (this.homelandFarmFriendServiceType.IsAssignableFrom(serviceType)
                        || (serviceType.FullName ?? string.Empty).IndexOf("FriendService", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        friendService = serviceObj;
                        status = "IFriendService via Managers._serviceDic: " + serviceType.FullName + ".";
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                status = "Managers friend service exception: " + ex.Message;
            }

            return false;
        }

        private bool TryHomelandFarmTryReadFriendPlayerNetId(object friendObj, out uint friendNetId)
        {
            friendNetId = 0U;
            if (friendObj == null)
            {
                return false;
            }

            string[] members = new string[] { "playerNetId", "PlayerNetId", "netId", "NetId", "ownerNetId", "OwnerNetId", "friendNetId", "FriendNetId" };
            for (int i = 0; i < members.Length; i++)
            {
                if (this.TryGetUIntMember(friendObj, members[i], out friendNetId) && friendNetId != 0U)
                {
                    return true;
                }
            }

            if (this.TryGetObjectMember(friendObj, "entity", out object entityObj) && entityObj != null)
            {
                return this.TryHomelandFarmTryReadEntityNetId(entityObj, out friendNetId);
            }

            if (this.TryGetObjectMember(friendObj, "player", out object playerObj) && playerObj != null)
            {
                return this.TryHomelandFarmTryReadEntityNetId(playerObj, out friendNetId)
                    || this.TryGetUIntMember(playerObj, "netId", out friendNetId);
            }

            return false;
        }

        private bool TryHomelandFarmInvokeCropWater(uint ownerNetId, List<uint> cropBoxNetIds, out string status)
        {
            status = "Crop water unavailable.";
            if (this.homelandFarmManagedReflectionReady && this.homelandFarmCropWaterPlantMethod != null)
            {
                try
                {
                    object listArg = this.CreateHomelandFarmUintList(cropBoxNetIds, this.homelandFarmCropWaterPlantMethod.GetParameters()[1].ParameterType);
                    ParameterInfo[] parameters = this.homelandFarmCropWaterPlantMethod.GetParameters();
                    if (parameters.Length == 2)
                    {
                        this.homelandFarmCropWaterPlantMethod.Invoke(null, new object[] { ownerNetId, listArg });
                    }
                    else
                    {
                        this.homelandFarmCropWaterPlantMethod.Invoke(null, new object[] { ownerNetId, listArg, 0 });
                    }

                    status = "Crop water sent (" + cropBoxNetIds.Count + ").";
                    this.HomelandFarmLog("Crop water owner=" + ownerNetId + " count=" + cropBoxNetIds.Count);
                    return true;
                }
                catch (Exception ex)
                {
                    status = (ex.InnerException ?? ex).Message;
                    this.HomelandFarmLog("Crop water invoke error owner=" + ownerNetId + ": " + status);
                }
            }

            if (this.TryHomelandFarmSendWaterCropCommand(ownerNetId, cropBoxNetIds, out string sendStatus))
            {
                status = sendStatus;
                this.HomelandFarmLog("Crop water via managed SendCommand count=" + cropBoxNetIds.Count + " status=" + sendStatus);
                return true;
            }

            if (this.TryHomelandFarmInvokeCropWaterAura(ownerNetId, cropBoxNetIds, out string auraStatus))
            {
                status = auraStatus;
                return true;
            }

            status = string.IsNullOrEmpty(status) ? sendStatus : (status + ". " + sendStatus);
            if (!string.IsNullOrEmpty(auraStatus))
            {
                status = status + ". " + auraStatus;
            }

            return false;
        }

        private bool TryHomelandFarmInvokePlantWater(uint ownerNetId, List<uint> plantNetIds, int mode, out string status)
        {
            status = "Plant water unavailable.";
            if (this.homelandFarmManagedReflectionReady && this.homelandFarmPlantWaterPlantMethod != null)
            {
                try
                {
                    object listArg = this.CreateHomelandFarmUintList(plantNetIds, this.homelandFarmPlantWaterPlantMethod.GetParameters()[1].ParameterType);
                    this.homelandFarmPlantWaterPlantMethod.Invoke(null, new object[] { ownerNetId, listArg, mode });
                    status = "Plant water sent (" + plantNetIds.Count + ") owner=" + ownerNetId + ".";
                    this.HomelandFarmLog(status);
                    return true;
                }
                catch (Exception ex)
                {
                    status = (ex.InnerException ?? ex).Message;
                }
            }

            if (this.TryHomelandFarmSendWaterPlantCommand(ownerNetId, plantNetIds, mode, out string sendStatus))
            {
                status = sendStatus;
                this.HomelandFarmLog("Plant water via managed SendCommand count=" + plantNetIds.Count + " owner=" + ownerNetId + " mode=" + mode + " status=" + sendStatus);
                return true;
            }

            if (this.TryHomelandFarmInvokePlantWaterAura(ownerNetId, plantNetIds, mode, out string auraStatus))
            {
                status = auraStatus;
                return true;
            }

            status = string.IsNullOrEmpty(status) ? sendStatus : (status + ". " + sendStatus);
            if (!string.IsNullOrEmpty(auraStatus))
            {
                status = status + ". " + auraStatus;
            }

            return false;
        }

        private bool TryHomelandFarmInvokeStaticUintProtocol(
            MethodInfo protocolMethod,
            uint netId,
            Type commandType,
            Func<object, bool> populateCommand,
            string label,
            out string status)
        {
            status = label + " unavailable.";
            try
            {
                protocolMethod.Invoke(null, new object[] { netId });
                status = label + " sent.";
                return true;
            }
            catch (Exception ex)
            {
                status = (ex.InnerException ?? ex).Message;
                string sendStatus = "SendCommand fallback unavailable.";
                if (commandType != null && populateCommand != null && this.TryHomelandFarmSendCommand(commandType, populateCommand, out sendStatus))
                {
                    status = sendStatus;
                    return true;
                }

                status = status + ". " + sendStatus;
                return false;
            }
        }

        private bool EnsureHomelandFarmSendCommandResolver()
        {
            if (this.homelandFarmSendCommandMethodDef != null && this.homelandFarmReliableChannelValue != null)
            {
                return true;
            }

            Type webRequestType = this.FindHomelandFarmRuntimeType(
                "WebRequestUtility",
                "XDTDataAndProtocol.ProtocolService");
            if (webRequestType == null)
            {
                return false;
            }

            this.homelandFarmSendCommandMethodDef = webRequestType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
                .FirstOrDefault(m => m.Name == "SendCommand" && m.IsGenericMethodDefinition && m.GetParameters().Length == 3);
            Type channelType = this.FindLoadedType(
                    "XD.GameGerm.Network.ChannelType",
                    "Il2CppXD.GameGerm.Network.ChannelType",
                    "ChannelType")
                ?? this.FindLoadedTypeByFullName("XD.GameGerm.Network.ChannelType")
                ?? this.FindLoadedTypeByFullName("Il2CppXD.GameGerm.Network.ChannelType");
            if (channelType != null)
            {
                try
                {
                    this.homelandFarmReliableChannelValue = Enum.Parse(channelType, "Reliable");
                }
                catch
                {
                }
            }

            if (this.homelandFarmReliableChannelValue == null)
            {
                this.homelandFarmReliableChannelValue = 1;
            }

            return this.homelandFarmSendCommandMethodDef != null;
        }

        private bool TryHomelandFarmSendCommand(Type commandType, Func<object, bool> populateCommand, out string status)
        {
            status = "SendCommand unavailable.";
            if (commandType == null || populateCommand == null || !this.EnsureHomelandFarmSendCommandResolver())
            {
                return false;
            }

            try
            {
                object command = Activator.CreateInstance(commandType);
                if (!populateCommand(command))
                {
                    status = "Command populate failed.";
                    return false;
                }

                MethodInfo sendMethod = this.homelandFarmSendCommandMethodDef.MakeGenericMethod(commandType);
                object result = sendMethod.Invoke(null, new object[] { command, true, this.homelandFarmReliableChannelValue });
                int sendCode = result is int code ? code : -1;
                if (sendCode < 0)
                {
                    status = "SendCommand failed (" + sendCode + ").";
                    return false;
                }

                status = "SendCommand ok.";
                return true;
            }
            catch (Exception ex)
            {
                status = "SendCommand exception: " + (ex.InnerException ?? ex).Message;
                return false;
            }
        }

        private bool TryHomelandFarmSendWaterCropCommand(uint ownerNetId, List<uint> cropBoxNetIds, out string status)
        {
            status = "WaterCrop SendCommand unavailable.";
            if (this.homelandFarmWaterCropSendUnavailable)
            {
                return false;
            }

            if (this.homelandFarmWaterCropNetworkCommandType == null)
            {
                this.homelandFarmWaterCropNetworkCommandType = this.ResolveHomelandFarmManagedType(
                    "WaterCropNetworkCommand",
                    "XDT.Scene.Shared.Modules.Farm.WaterCropNetworkCommand",
                    "EcsClient.XDT.Scene.Shared.Modules.Farm.WaterCropNetworkCommand");
            }

            if (this.homelandFarmWaterCropNetworkCommandType == null)
            {
                this.homelandFarmWaterCropSendUnavailable = true;
                return false;
            }

            return this.TryHomelandFarmSendCommand(
                this.homelandFarmWaterCropNetworkCommandType,
                command =>
                {
                    object cmd = command;
                    bool ok = this.TrySetFieldValue(this.homelandFarmWaterCropNetworkCommandType, ref cmd, "ownerNetId", ownerNetId)
                        || this.TrySetFieldValue(this.homelandFarmWaterCropNetworkCommandType, ref cmd, "fieldOwnerNetId", ownerNetId);
                    FieldInfo netIdsField = this.homelandFarmWaterCropNetworkCommandType.GetField("netIds", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (netIdsField != null)
                    {
                        netIdsField.SetValue(cmd, this.CreateHomelandFarmUintList(cropBoxNetIds, netIdsField.FieldType));
                        ok = true;
                    }

                    return ok;
                },
                out status);
        }

        private bool TryHomelandFarmSendWaterPlantCommand(uint ownerNetId, List<uint> plantNetIds, int mode, out string status)
        {
            status = "WaterPlant SendCommand unavailable.";
            if (this.homelandFarmWaterPlantSendUnavailable)
            {
                return false;
            }

            if (this.homelandFarmWaterPlantNetworkCommandType == null)
            {
                this.homelandFarmWaterPlantNetworkCommandType = this.ResolveHomelandFarmManagedType(
                    "WaterPlantNetworkCommand",
                    "XDT.Scene.Shared.Modules.Plant.WaterPlantNetworkCommand",
                    "EcsClient.XDT.Scene.Shared.Modules.Plant.WaterPlantNetworkCommand");
            }

            if (this.homelandFarmWaterPlantNetworkCommandType == null)
            {
                this.homelandFarmWaterPlantSendUnavailable = true;
                return false;
            }

            return this.TryHomelandFarmSendCommand(
                this.homelandFarmWaterPlantNetworkCommandType,
                command =>
                {
                    object cmd = command;
                    bool ok = this.TrySetFieldValue(this.homelandFarmWaterPlantNetworkCommandType, ref cmd, "fieldOwnerNetId", ownerNetId)
                        || this.TrySetFieldValue(this.homelandFarmWaterPlantNetworkCommandType, ref cmd, "ownerNetId", ownerNetId);
                    FieldInfo netIdsField = this.homelandFarmWaterPlantNetworkCommandType.GetField("plantNetIds", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                        ?? this.homelandFarmWaterPlantNetworkCommandType.GetField("netIds", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (netIdsField != null)
                    {
                        netIdsField.SetValue(cmd, this.CreateHomelandFarmUintList(plantNetIds, netIdsField.FieldType));
                        ok = true;
                    }

                    ok = this.TrySetFieldValue(this.homelandFarmWaterPlantNetworkCommandType, ref cmd, "mode", mode) || ok;
                    return ok;
                },
                out status);
        }

        private bool TryHomelandFarmSendManureCommand(List<uint> cropNetIds, out string status)
        {
            status = "Manure SendCommand unavailable.";
            if (!this.TryHomelandFarmEnsureNetworkCommandTypes(out string typeStatus)
                || this.homelandFarmManuredNetworkCommandType == null)
            {
                status = typeStatus;
                return false;
            }

            if (this.homelandFarmManuredSendCommandMethod == null)
            {
                if (!this.EnsureHomelandFarmSendCommandResolver())
                {
                    status = "SendCommand resolver unavailable.";
                    return false;
                }

                try
                {
                    this.homelandFarmManuredSendCommandMethod = this.homelandFarmSendCommandMethodDef.MakeGenericMethod(
                        this.homelandFarmManuredNetworkCommandType);
                }
                catch (Exception ex)
                {
                    status = "Manure SendCommand generic bind failed: " + ex.Message;
                    this.HomelandFarmLog(status);
                    return false;
                }
            }

            try
            {
                object command = Activator.CreateInstance(this.homelandFarmManuredNetworkCommandType);
                object cmd = command;
                FieldInfo netIdsField = this.homelandFarmManuredNetworkCommandType.GetField(
                    "cropNetIds",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?? this.homelandFarmManuredNetworkCommandType.GetField(
                        "netIds",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (netIdsField == null)
                {
                    status = "Manure SendCommand cropNetIds field missing.";
                    return false;
                }

                object listArg = this.CreateHomelandFarmUintList(cropNetIds, netIdsField.FieldType);
                if (!this.TrySetFieldValue(this.homelandFarmManuredNetworkCommandType, ref cmd, netIdsField.Name, listArg))
                {
                    netIdsField.SetValue(cmd, listArg);
                }

                object result = this.homelandFarmManuredSendCommandMethod.Invoke(
                    null,
                    new object[] { cmd, true, this.homelandFarmReliableChannelValue });
                int sendCode = result is int code ? code : -1;
                if (sendCode < 0)
                {
                    status = "Manure SendCommand failed (" + sendCode + ").";
                    this.HomelandFarmLog(status);
                    return false;
                }

                status = "Manure SendCommand ok count=" + cropNetIds.Count + ".";
                this.HomelandFarmLog(status);
                return true;
            }
            catch (Exception ex)
            {
                status = "Manure SendCommand exception: " + (ex.InnerException ?? ex).Message;
                this.HomelandFarmLog(status);
                return false;
            }
        }

        private bool IsHomelandFarmBusy()
        {
            return this.homelandFarmCoroutine != null || Time.realtimeSinceStartup < this.homelandFarmBusyUntil;
        }

        private void StopHomelandFarmCoroutine()
        {
            if (this.homelandFarmCoroutine == null)
            {
                return;
            }

            try
            {
                ModCoroutines.Stop(this.homelandFarmCoroutine);
            }
            catch
            {
            }

            this.homelandFarmCoroutine = null;
            // Unity does not run finally blocks on a stopped coroutine, so clear auto-farm
            // state here too (otherwise the scan-center override would leak into manual ops).
            this.homelandFarmAutoRunning = false;
            this.homelandFarmScanCenterOverride = null;
            this.ClearHomelandFarmAutoCaches();
            this.homelandFarmBusyUntil = Time.realtimeSinceStartup + HomelandFarmActionCooldownSeconds;
        }

        // Drops every auto-farm-only entity cache. Called when (re)capturing planters and when auto
        // farm stops, so a fresh run never reuses targets registered on a previous run / another
        // player's field (stale RegisteredCache netIds were the source of the immediate Capture crash:
        // classifying an unloaded foreign entity triggers a native AuraMono AV with no log).
        private void ClearHomelandFarmAutoCaches()
        {
            this.homelandFarmAutoCropNetIds.Clear();
            this.homelandFarmAutoHarvestedNetIds.Clear();
            this.homelandFarmAutoPendingSowBoxNetIds.Clear();
            this.homelandFarmRegisteredFarmTargets.Clear();
        }

        // Snapshot the planters (crop boxes) within the current radius around the player,
        // analogous to Mass Cook "Capture Stoves". The captured center pins the auto-farm
        // working zone; the planter count is informational.
        private void CaptureHomelandFarmAutoPlanters()
        {
            if (this.homelandFarmAutoRunning)
            {
                this.AddMenuNotification("Stop auto farm before re-capturing.", new Color(1f, 0.75f, 0.45f));
                return;
            }

            if (!this.TryBeginHomelandFarmAction(silent: false, out _, allowVisitingFarmArea: true))
            {
                return;
            }

            // Capture is synchronous (no coroutine), so release the action cooldown immediately.
            this.homelandFarmBusyUntil = 0f;

            // Drop any auto-farm caches from a previous run / field before scanning. Stale
            // RegisteredCache netIds (e.g. from another player's field we just visited) would
            // otherwise be classified here and crash on an unloaded entity.
            this.ClearHomelandFarmAutoCaches();

            if (!this.TryGetHomelandFarmPlayerPosition(out Vector3 center))
            {
                this.homelandFarmAutoCaptured = false;
                this.homelandFarmLastStatus = "Auto farm: could not read player position.";
                this.AddMenuNotification(this.homelandFarmLastStatus, new Color(1f, 0.55f, 0.45f));
                return;
            }

            float radius = this.homelandFarmWaterRadius;

            // NOTE: do NOT force a dictionary rebuild here. TryHomelandFarmCacheAuraLevelObjectPositions
            // with allowDictionaryScan:true clears the cache and re-enumerates LevelObjectManager._dictionary,
            // which is unsafe outside warmup on this IL2CPP build (crashes / returns a partial set) and made
            // capture find FEWER planters. The warmup-populated cache is the reliable source.
            // useAutoFarmCollectShortcuts:false — capture must scan live entities only, never the
            // RegisteredCache / level-object cache. Reading cached (possibly stale/foreign) netIds is
            // what crashed Capture immediately with no log.
            HashSet<uint> farmNetIds = new HashSet<uint>();
            this.TryHomelandFarmCollectFarmEntityNetIds(farmNetIds, out _, center, radius + 2f, useAutoFarmCollectShortcuts: false);

            // Resolve crop boxes with the same tiered logic the sow slot scan uses, taking the
            // UNION of every tier (not just the first that yields) so nothing is dropped:
            //   1) boxes the scan itself flagged, 2) per-netId classification,
            //   3) full component scan (world-wide), radius-filtered for boxes the spatial
            //      sources missed entirely — the main reason capture undercounted vs sow.
            HashSet<uint> cropBoxNetIds = new HashSet<uint>();
            foreach (uint boxNetId in this.homelandFarmLastScanCropBoxNetIds)
            {
                if (boxNetId != 0U && farmNetIds.Contains(boxNetId))
                {
                    cropBoxNetIds.Add(boxNetId);
                }
            }

            int tier1 = cropBoxNetIds.Count;

            foreach (uint netId in farmNetIds)
            {
                if (netId != 0U
                    && !cropBoxNetIds.Contains(netId)
                    && this.TryHomelandFarmClassifyFarmNetId(netId, out bool isCropBox)
                    && isCropBox)
                {
                    cropBoxNetIds.Add(netId);
                }
            }

            int tier2 = cropBoxNetIds.Count - tier1;

            HashSet<uint> componentBoxes = new HashSet<uint>();
            bool componentScanOk = this.TryHomelandFarmCollectComponentsNetIds(this.homelandFarmCropBoxComponentType, componentBoxes, "CropBoxComponent(capture)");
            float captureRadiusSq = (radius + 2f) * (radius + 2f);
            int excludedOutsideRadius = 0;
            int beforeTier3 = cropBoxNetIds.Count;
            foreach (uint netId in componentBoxes)
            {
                if (netId == 0U || cropBoxNetIds.Contains(netId))
                {
                    continue;
                }

                // Match the crop scan's filter semantics: exclude a box ONLY when its position is
                // known AND provably outside the radius. Crop-box positions live solely in the
                // level-object cache; if that cache is incomplete the position won't resolve, and
                // dropping such boxes (the old behaviour) caused the intermittent undercount.
                // Unknown position → keep it.
                if (this.TryHomelandFarmResolveFarmEntityPosition(netId, out Vector3 boxPos)
                    && boxPos != Vector3.zero
                    && (boxPos - center).sqrMagnitude > captureRadiusSq)
                {
                    excludedOutsideRadius++;
                    continue;
                }

                cropBoxNetIds.Add(netId);
            }

            int tier3 = cropBoxNetIds.Count - beforeTier3;
            int planterCount = cropBoxNetIds.Count;
            this.homelandFarmAutoCaptureExcludedOutsideRadius = excludedOutsideRadius;

            // Compact per-source breakdown so undercounts are diagnosable without enabling logs.
            //   farm = raw farm netIds in radius; t1/t2/t3 = crop boxes from each tier;
            //   comp = component-scan boxes (ok = whether that scan ran); out = excluded by radius.
            string diag = "[farm=" + farmNetIds.Count
                + " t1=" + tier1 + " t2=" + tier2 + " t3=" + tier3
                + " comp=" + componentBoxes.Count + (componentScanOk ? "" : "(off)")
                + " out=" + excludedOutsideRadius + "]";
            this.HomelandFarmLog("Auto capture: planters=" + planterCount + " " + diag);

            this.homelandFarmAutoCenter = center;
            this.homelandFarmAutoCaptureRadius = radius;
            this.homelandFarmAutoPlanterCount = planterCount;
            this.homelandFarmAutoCaptured = true;

            string radiusNote = excludedOutsideRadius > 0
                ? " (" + excludedOutsideRadius + " outside radius)"
                : string.Empty;
            this.homelandFarmLastStatus = "Auto farm: captured " + planterCount + " planter(s)" + radiusNote + ". " + diag;
            this.AddMenuNotification(
                "Auto farm: captured " + planterCount + " planter(s)" + radiusNote,
                excludedOutsideRadius > 0 ? new Color(1f, 0.85f, 0.4f)
                    : (planterCount > 0 ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.75f, 0.45f)));
            this.HomelandFarmLog("Auto capture center=" + center + " radius=" + radius.ToString("F0") + " planters=" + planterCount);
        }

        private void StartHomelandFarmAuto()
        {
            if (!this.homelandFarmAutoCaptured || this.homelandFarmAutoPlanterCount <= 0)
            {
                this.homelandFarmLastStatus = "Auto farm: capture planters first.";
                this.AddMenuNotification(this.homelandFarmLastStatus, new Color(1f, 0.75f, 0.45f));
                return;
            }

            if (!this.TryBeginHomelandFarmAction(silent: false, out _, allowVisitingFarmArea: true))
            {
                return;
            }

            this.HomelandFarmLog("Start auto farm center=" + this.homelandFarmAutoCenter + " radius=" + this.homelandFarmAutoCaptureRadius.ToString("F0"));
            this.homelandFarmLastStatus = "Auto farming...";
            this.homelandFarmCoroutine = ModCoroutines.Start(this.HomelandFarmAutoRoutine());
        }

        private HomelandFarmInventoryItem FindHomelandFarmSeedByStaticId(int staticId)
        {
            if (staticId <= 0)
            {
                return null;
            }

            for (int i = 0; i < this.homelandFarmScannedSeeds.Count; i++)
            {
                HomelandFarmInventoryItem seed = this.homelandFarmScannedSeeds[i];
                if (seed != null && seed.StaticId == staticId && seed.NetId != 0U && seed.Count > 0)
                {
                    return seed;
                }
            }

            return null;
        }

        // Game (server-synced) unix seconds, used for exact crop maturity. The game clock is offset
        // from local UTC (observed ~+6.8h), so the UTC fallback gave wrong (even negative) remaining
        // times. Prefer the game's own GameTimeUtility.GetUnixTime() — via AuraMono first (managed
        // type is absent under BepInEx), then managed, and only UTC as a last resort.
        private bool TryHomelandFarmGetGameUnixTime(out long unix)
        {
            unix = 0L;
            if (this.TryHomelandFarmGetGameUnixTimeAuraMono(out unix) && unix > 0L)
            {
                return true;
            }

            try
            {
                Type t = this.FindLoadedType("GameTimeUtility", "XDTDataAndProtocol.ProtocolService.GameTimeUtility");
                MethodInfo m = t?.GetMethod("GetUnixTime", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                if (m != null)
                {
                    object r = m.Invoke(null, null);
                    if (r != null)
                    {
                        unix = Convert.ToInt64(r);
                        if (unix > 0L)
                        {
                            return true;
                        }
                    }
                }
            }
            catch
            {
            }

            unix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return unix > 0L;
        }

        private unsafe bool TryHomelandFarmGetGameUnixTimeAuraMono(out long unix)
        {
            unix = 0L;
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null || auraMonoObjectUnbox == null)
            {
                return false;
            }

            IntPtr cls = this.FindHomelandFarmAuraClass(
                "XDTDataAndProtocol.ProtocolService.GameTimeUtility",
                "XDTDataAndProtocol.ProtocolService",
                "GameTimeUtility");
            if (cls == IntPtr.Zero)
            {
                return false;
            }

            IntPtr method = this.FindAuraMonoMethodOnHierarchy(cls, "GetUnixTime", 0);
            if (method == IntPtr.Zero)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr boxed = auraMonoRuntimeInvoke(method, IntPtr.Zero, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || boxed == IntPtr.Zero)
            {
                return false;
            }

            IntPtr raw = auraMonoObjectUnbox(boxed);
            if (raw == IntPtr.Zero)
            {
                return false;
            }

            unix = *(long*)raw;
            return unix > 0L;
        }

        // Reads live crop state for a netId: stage (4 = ripe), weed flag, and exact seconds left
        // until ripe (FarmUtil: matureUnix = sowTime + ripeGrowTime - growTime). Returns false if
        // the crop no longer exists (harvested / unloaded). remainingSeconds = long.MaxValue when
        // timing fields are unavailable so callers fall back to coarse polling.
        private bool TryHomelandFarmReadCropState(uint cropNetId, out int stage, out bool hasWeed, out long remainingSeconds)
        {
            stage = 0;
            hasWeed = false;
            remainingSeconds = long.MaxValue;
            if (cropNetId == 0U)
            {
                return false;
            }

            object cropData = null;
            if (!this.TryHomelandFarmGetComponentData(this.homelandFarmCropItemDataType, cropNetId, out cropData, out _, "CropItemData")
                || cropData == null)
            {
                // Crop-box crops on this IL2CPP build are PlantItemData entities, not CropItemData.
                if (!this.TryHomelandFarmGetComponentData(this.homelandFarmPlantItemDataType, cropNetId, out cropData, out _, "PlantItemData")
                    || cropData == null)
                {
                    return false;
                }
            }

            this.TryHomelandFarmReadComponentInt(cropData, out stage, "stage", "Stage");
            this.TryHomelandFarmReadComponentBool(cropData, out hasWeed, "hasWeed", "_hasWeed", "HasWeed");

            bool haveSow = this.TryHomelandFarmReadComponentLong(cropData, out long sowTime, "sowTime", "SowTime");
            bool haveRipe = this.TryHomelandFarmReadComponentLong(cropData, out long ripeGrowTime, "ripeGrowTime", "RipeGrowTime");
            bool haveGrow = this.TryHomelandFarmReadComponentLong(cropData, out long growTime, "growTime", "GrowTime");
            if (haveSow && haveRipe && haveGrow && ripeGrowTime > 0L
                && this.TryHomelandFarmGetGameUnixTime(out long nowUnix) && nowUnix > 0L)
            {
                long matureUnix = sowTime + ripeGrowTime - growTime;
                remainingSeconds = matureUnix - nowUnix;
            }

            return true;
        }

        // Client-side crop/plant entities that must not keep auto farm busy (harvest already sent,
        // or the game marked the crop picked). Without this, stale PlantItemData at stage 4 blocks sow.
        private bool TryHomelandFarmIsDiscardedAutoFarmCropNetId(uint netId)
        {
            if (netId == 0U)
            {
                return false;
            }

            if (this.homelandFarmAutoHarvestedNetIds.Contains(netId))
            {
                return true;
            }

            object cropData = null;
            if (this.TryHomelandFarmGetComponentData(this.homelandFarmCropItemDataType, netId, out cropData, out _, "CropItemData")
                || (this.TryHomelandFarmGetComponentData(this.homelandFarmPlantItemDataType, netId, out cropData, out _, "PlantItemData")
                    && cropData != null))
            {
                return this.TryHomelandFarmReadComponentBool(cropData, out bool isPick, "isPick", "_isPick", "IsPick") && isPick;
            }

            return false;
        }

        private HashSet<uint> HomelandFarmGetCapturedCropBoxNetIds()
        {
            HashSet<uint> cropBoxNetIds = new HashSet<uint>();
            foreach (uint boxNetId in this.homelandFarmLastScanCropBoxNetIds)
            {
                if (boxNetId != 0U)
                {
                    cropBoxNetIds.Add(boxNetId);
                }
            }

            if (cropBoxNetIds.Count > 0 || !this.homelandFarmAutoCaptured)
            {
                return cropBoxNetIds;
            }

            if (!this.TryGetHomelandFarmScanCenter(out Vector3 center))
            {
                return cropBoxNetIds;
            }

            HashSet<uint> farmNetIds = new HashSet<uint>();
            this.TryHomelandFarmCollectFarmEntityNetIds(farmNetIds, out _, center, this.homelandFarmWaterRadius + 2f);
            foreach (uint netId in farmNetIds)
            {
                if (netId != 0U
                    && this.TryHomelandFarmClassifyFarmNetId(netId, out bool isCropBox)
                    && isCropBox)
                {
                    cropBoxNetIds.Add(netId);
                }
            }

            return cropBoxNetIds;
        }

        private bool TryHomelandFarmCollectOccupiedCapturedPlanterNetIds(out HashSet<uint> occupiedBoxes, out HashSet<uint> scanNetIds)
        {
            scanNetIds = new HashSet<uint>();
            if (this.TryGetHomelandFarmScanCenter(out Vector3 center))
            {
                this.TryHomelandFarmCollectFarmEntityNetIds(
                    scanNetIds,
                    out _,
                    center,
                    this.homelandFarmWaterRadius + 2f,
                    useAutoFarmCollectShortcuts: false);
            }

            return this.TryHomelandFarmBuildOccupiedFromScanNetIds(scanNetIds, out occupiedBoxes);
        }

        // Pure occupied-box derivation from an already-collected farm-entity set — no radius scan.
        // Lets a caller that already has the scan (e.g. RebuildHomelandFarmAutoCropCache) avoid
        // repeating the heavy proximity enumeration.
        private bool TryHomelandFarmBuildOccupiedFromScanNetIds(HashSet<uint> scanNetIds, out HashSet<uint> occupiedBoxes)
        {
            occupiedBoxes = new HashSet<uint>();
            HashSet<uint> cropBoxNetIds = this.HomelandFarmGetCapturedCropBoxNetIds();
            if (cropBoxNetIds.Count == 0)
            {
                return false;
            }

            this.TryHomelandFarmBuildOccupiedCropBoxNetIds(cropBoxNetIds, scanNetIds ?? new HashSet<uint>(), occupiedBoxes);
            return true;
        }

        private bool TryHomelandFarmScanHasCropItemDataEntities(HashSet<uint> scanNetIds)
        {
            if (scanNetIds == null || scanNetIds.Count == 0)
            {
                return false;
            }

            foreach (uint netId in scanNetIds)
            {
                if (netId != 0U
                    && this.TryHomelandFarmGetComponentData(this.homelandFarmCropItemDataType, netId, out _, out _, "CropItemData"))
                {
                    return true;
                }
            }

            return false;
        }

        // When the scan has zero CropItemData, sub-ripe PlantItemData-only entities are client ghosts
        // (live growing crops on this build also register CropItemData — see cropPlants in diagnostics).
        private bool TryHomelandFarmIsOrphanPlantOnlyWithoutCropData(uint netId, HashSet<uint> scanNetIds)
        {
            if (netId == 0U
                || scanNetIds == null
                || this.TryHomelandFarmScanHasCropItemDataEntities(scanNetIds)
                || this.TryHomelandFarmGetComponentData(this.homelandFarmCropItemDataType, netId, out _, out _, "CropItemData"))
            {
                return false;
            }

            if (!this.TryHomelandFarmGetComponentData(this.homelandFarmPlantItemDataType, netId, out object plantData, out _, "PlantItemData")
                || plantData == null)
            {
                return false;
            }

            return this.TryHomelandFarmReadComponentInt(plantData, out int stage, "stage", "Stage") && stage < 4;
        }

        private void HomelandFarmPruneAutoPendingSowBoxes(HashSet<uint> occupiedBoxes)
        {
            if (occupiedBoxes == null || occupiedBoxes.Count == 0 || this.homelandFarmAutoPendingSowBoxNetIds.Count == 0)
            {
                return;
            }

            uint[] pending = new uint[this.homelandFarmAutoPendingSowBoxNetIds.Count];
            this.homelandFarmAutoPendingSowBoxNetIds.CopyTo(pending);
            for (int i = 0; i < pending.Length; i++)
            {
                if (occupiedBoxes.Contains(pending[i]))
                {
                    this.homelandFarmAutoPendingSowBoxNetIds.Remove(pending[i]);
                }
            }
        }

        private bool TryHomelandFarmTryReadPlanterNetIdFromSowPoint(object point, out uint planterNetId)
        {
            planterNetId = 0U;
            if (point == null)
            {
                return false;
            }

            if (point is HomelandFarmCropPlantPointData data)
            {
                planterNetId = data.PlanterNetId;
                return planterNetId != 0U;
            }

            if (this.TryGetUIntMember(point, "planterNetId", out planterNetId) && planterNetId != 0U)
            {
                return true;
            }

            if (this.TryGetUIntMember(point, "PlanterNetId", out planterNetId) && planterNetId != 0U)
            {
                return true;
            }

            if ((this.TryReadManagedUInt64Member(point, "levelObjectNetId", out ulong levelObjectNetId)
                    || this.TryReadManagedUInt64Member(point, "LevelObjectNetId", out levelObjectNetId))
                && levelObjectNetId != 0UL)
            {
                planterNetId = HomelandFarmDecodePlanterNetIdFromLevelObjectId(levelObjectNetId);
                return planterNetId != 0U;
            }

            return false;
        }

        private void HomelandFarmRememberAutoSowPendingBoxes(IEnumerable<object> plantPoints)
        {
            if (plantPoints == null)
            {
                return;
            }

            foreach (object point in plantPoints)
            {
                if (this.TryHomelandFarmTryReadPlanterNetIdFromSowPoint(point, out uint planterNetId) && planterNetId != 0U)
                {
                    this.homelandFarmAutoPendingSowBoxNetIds.Add(planterNetId);
                }
            }
        }

        private bool TryHomelandFarmIsLiveCropOnCapturedPlanter(
            uint cropNetId,
            HashSet<uint> occupiedBoxes,
            HashSet<uint> scanNetIds = null)
        {
            if (cropNetId == 0U || occupiedBoxes == null || occupiedBoxes.Count == 0)
            {
                return false;
            }

            if (this.TryHomelandFarmIsDiscardedAutoFarmCropNetId(cropNetId)
                || this.TryHomelandFarmIsOrphanPlantOnlyWithoutCropData(cropNetId, scanNetIds))
            {
                return false;
            }

            return this.TryHomelandFarmTryFindCropBoxNetIdForCrop(cropNetId, out uint boxNetId)
                && boxNetId != 0U
                && occupiedBoxes.Contains(boxNetId);
        }

        // occupiedBoxes/scanNetIds are precomputed by the caller (one shared scan) to avoid repeating
        // the radius enumeration inside the sanitize pass.
        private void HomelandFarmSanitizeAutoCropNetIds(List<uint> netIds, HashSet<uint> occupiedBoxes, HashSet<uint> scanNetIds)
        {
            if (netIds == null || netIds.Count == 0)
            {
                return;
            }

            if (occupiedBoxes == null)
            {
                return;
            }

            if (occupiedBoxes.Count == 0)
            {
                netIds.Clear();
                if (this.homelandFarmAutoPendingSowBoxNetIds.Count > 0)
                {
                    this.HomelandFarmLog("Auto crop cache: waiting for " + this.homelandFarmAutoPendingSowBoxNetIds.Count
                        + " sown crop(s) to register.");
                }
                else
                {
                    this.HomelandFarmLog("Auto crop cache: all captured planters empty - 0 crop(s).");
                }

                return;
            }

            Dictionary<uint, uint> bestNetIdByBox = new Dictionary<uint, uint>();
            for (int i = netIds.Count - 1; i >= 0; i--)
            {
                uint netId = netIds[i];
                if (this.TryHomelandFarmIsDiscardedAutoFarmCropNetId(netId)
                    || this.TryHomelandFarmIsOrphanPlantOnlyWithoutCropData(netId, scanNetIds)
                    || !this.TryHomelandFarmIsLiveCropOnCapturedPlanter(netId, occupiedBoxes, scanNetIds))
                {
                    netIds.RemoveAt(i);
                    continue;
                }

                if (!this.TryHomelandFarmTryFindCropBoxNetIdForCrop(netId, out uint boxNetId) || boxNetId == 0U)
                {
                    netIds.RemoveAt(i);
                    continue;
                }

                if (!bestNetIdByBox.TryGetValue(boxNetId, out uint existingNetId))
                {
                    bestNetIdByBox[boxNetId] = netId;
                    continue;
                }

                bool netIdIsCropData = this.TryHomelandFarmGetComponentData(
                    this.homelandFarmCropItemDataType,
                    netId,
                    out _,
                    out _,
                    "CropItemData");
                bool existingIsCropData = this.TryHomelandFarmGetComponentData(
                    this.homelandFarmCropItemDataType,
                    existingNetId,
                    out _,
                    out _,
                    "CropItemData");
                if (netIdIsCropData && !existingIsCropData)
                {
                    bestNetIdByBox[boxNetId] = netId;
                }
            }

            netIds.Clear();
            foreach (KeyValuePair<uint, uint> entry in bestNetIdByBox)
            {
                if (entry.Value != 0U)
                {
                    netIds.Add(entry.Value);
                }
            }
        }

        // Rebuilds the auto-farm crop cache for the captured zone: a crop scan, then an
        // occupied-planter cross-check + sanitize to keep one live crop netId per occupied box and
        // drop ghosts/orphans. Runs after each sow (and at start), NOT every poll tick — it triggers
        // several radius scans, so it is deliberately infrequent (steady-growth polling is directed).
        private void RebuildHomelandFarmAutoCropCache()
        {
            // includePlantData:true — on this IL2CPP build crop-box crops are PlantItemData entities
            // (CropItemData absent). TryHomelandFarmReadCropState reads either type. Accept stage 0..4
            // (any live crop), NOT just 1..4: a freshly sown crop starts at stage 0
            // for a moment, so a stage>=1 filter found 0 right after sow → the loop thought the zone
            // was empty and re-sowed already-planted boxes → MaxPlantCountLimit. Exclude already-picked
            // crops (isPick) so the cache still empties once everything is harvested → re-sow fires.
            // ONE shared radius scan for the whole rebuild. The crop filter, the occupied-box
            // derivation and the sanitize pass all reuse this single farm-entity set instead of each
            // running its own proximity enumeration (was 3-5 heavy scans per sow → now 1).
            HashSet<uint> farmNetIds = new HashSet<uint>();
            if (this.TryGetHomelandFarmScanCenter(out Vector3 scanCenter))
            {
                this.TryHomelandFarmCollectFarmEntityNetIds(
                    farmNetIds,
                    out _,
                    scanCenter,
                    this.homelandFarmWaterRadius + 2f,
                    useAutoFarmCollectShortcuts: false);
            }

            List<uint> crops = this.ScanHomelandFarmCropsByRadius(
                cropData =>
                {
                    if (this.TryHomelandFarmReadComponentInt(cropData, out int stage, "stage", "Stage") && stage > 4)
                    {
                        return false;
                    }

                    if (this.TryHomelandFarmReadComponentBool(cropData, out bool isPick, "isPick", "_isPick", "IsPick") && isPick)
                    {
                        return false;
                    }

                    return true;
                },
                "Auto crop cache",
                // requireOwn:false — freshly sown crops have an unresolved owner for a while (and the
                // own-field fallback misses when the scan center is the captured point rather than the
                // live player), so requireOwn:true dropped just-sown crops → cache stuck at 0 forever.
                // The captured zone is the player's own farm; harvest is still own-gated server-side.
                requireOwn: false,
                preCollectedNetIds: farmNetIds,
                logScanSummary: false,
                includePlantData: true,
                useAutoFarmCollectShortcuts: false,
                useCapturedScanCenter: true);

            List<uint> sanitized = new List<uint>(crops.Count);
            HashSet<uint> seen = new HashSet<uint>();
            for (int i = 0; i < crops.Count; i++)
            {
                if (crops[i] != 0U && seen.Add(crops[i]))
                {
                    sanitized.Add(crops[i]);
                }
            }

            this.TryHomelandFarmBuildOccupiedFromScanNetIds(farmNetIds, out HashSet<uint> occupiedBoxes);
            this.HomelandFarmPruneAutoPendingSowBoxes(occupiedBoxes);
            this.HomelandFarmSanitizeAutoCropNetIds(sanitized, occupiedBoxes, farmNetIds);

            this.homelandFarmAutoCropNetIds.Clear();
            this.homelandFarmAutoCropNetIds.AddRange(sanitized);

            this.HomelandFarmLog("Auto crop cache rebuilt: " + this.homelandFarmAutoCropNetIds.Count + " crop(s).");

            // Raw timing dump for the first cached crop — diagnoses maturity/unit mismatches
            // (mature = sowTime + ripeGrowTime - growTime, all in game-unix seconds per FarmUtil).
            if (this.homelandFarmAutoCropNetIds.Count > 0
                && this.TryHomelandFarmGetComponentData(this.homelandFarmCropItemDataType, this.homelandFarmAutoCropNetIds[0], out object sampleCrop, out _, "CropItemData")
                && sampleCrop != null)
            {
                this.TryHomelandFarmReadComponentInt(sampleCrop, out int sStage, "stage", "Stage");
                this.TryHomelandFarmReadComponentLong(sampleCrop, out long sSow, "sowTime", "SowTime");
                this.TryHomelandFarmReadComponentLong(sampleCrop, out long sRipe, "ripeGrowTime", "RipeGrowTime");
                this.TryHomelandFarmReadComponentLong(sampleCrop, out long sGrow, "growTime", "GrowTime");
                this.TryHomelandFarmReadComponentInt(sampleCrop, out int sGrowthVal, "growthValue", "GrowthValue");
                this.TryHomelandFarmGetGameUnixTime(out long sNow);
                this.HomelandFarmLog("Auto crop timing netId=" + this.homelandFarmAutoCropNetIds[0]
                    + " stage=" + sStage
                    + " sowTime=" + sSow
                    + " ripeGrowTime=" + sRipe
                    + " growTime=" + sGrow
                    + " growthValue=" + sGrowthVal
                    + " gameUnix=" + sNow
                    + " mature=" + (sSow + sRipe - sGrow)
                    + " remaining=" + (sSow + sRipe - sGrow - sNow) + "s"
                    + " (elapsedSinceSow=" + (sNow - sSow) + "s)");
            }
        }

        private IEnumerator HomelandFarmAutoRoutine()
        {
            yield return null;

            this.homelandFarmAutoRunning = true;
            this.homelandFarmScanCenterOverride = this.homelandFarmAutoCenter;
            this.homelandFarmAutoCropNetIds.Clear();
            this.homelandFarmAutoHarvestedNetIds.Clear();
            this.homelandFarmAutoPendingSowBoxNetIds.Clear();

            int totalSown = 0;
            int totalWeeded = 0;
            int totalHarvested = 0;
            bool seedsExhausted = false;
            int seedStaticId = 0;
            bool needDiscovery = true;   // discover current crops first (so we never sow occupied planters)
            bool discoveryDelay = false; // wait before discovery only after an actual sow
            float nextSowAllowedAt = 0f; // post-sow cooldown (server registration of sown crops)
            // Re-sow safety no longer relies on a generation lock: every sow pass goes through
            // FindEmptyCropPlanterSlotsRoutine, which fills ONLY genuinely-empty boxes — occupied
            // ones are excluded by link + world-position match, and just-sown boxes are excluded via
            // homelandFarmAutoPendingSowBoxNetIds until the server registers their crops. This lets us
            // top up free planters every tick instead of waiting for the whole generation to ripen.

            try
            {
                if (this.homelandFarmScannedSeeds.Count == 0)
                {
                    this.RefreshHomelandFarmSeeds();
                }

                if (this.homelandFarmScannedSeeds.Count > 0)
                {
                    int idx = Mathf.Clamp(this.homelandFarmSelectedSeedIndex, 0, this.homelandFarmScannedSeeds.Count - 1);
                    HomelandFarmInventoryItem selected = this.homelandFarmScannedSeeds[idx];
                    seedStaticId = selected != null ? selected.StaticId : 0;
                }

                if (seedStaticId <= 0)
                {
                    // No seed selected: just harvest whatever is already growing, then stop.
                    seedsExhausted = true;
                    this.HomelandFarmLog("Auto: no seed selected; harvest-only until zone is empty.");
                }

                while (true)
                {
                    // 1. DISCOVERY FIRST — (re)build the crop cache so we know what is actually in the
                    //    zone BEFORE deciding to sow. This is the only radius scan in the loop. Running
                    //    it before sow is what prevents re-sowing already-occupied planters on a restart
                    //    (the sow-slot occupied check is unreliable right after a previous sow). Wait the
                    //    registration delay only when we just sowed.
                    if (needDiscovery)
                    {
                        needDiscovery = false;
                        if (discoveryDelay)
                        {
                            discoveryDelay = false;
                            this.homelandFarmLastStatus = "Auto: scanning new crops...";
                            yield return new WaitForSecondsRealtime(HomelandFarmAutoDiscoveryDelaySeconds);
                        }
                        else
                        {
                            this.homelandFarmLastStatus = "Auto: scanning crops...";
                        }

                        this.RebuildHomelandFarmAutoCropCache();
                    }

                    // 2. Directed poll of cached crops: weed flagged, harvest ripe (drop from cache),
                    //    and track the soonest maturity. Harvest does NOT immediately re-trigger sow —
                    //    we re-sow only once the whole generation is collected (cache empty), so we
                    //    never re-sow boxes the server hasn't freed/registered yet.
                    long minRemaining = long.MaxValue;
                    int weededThisTick = 0;
                    int harvestedThisTick = 0;
                    int prunedThisTick = 0;
                    HashSet<uint> occupiedPlanters = null;
                    HashSet<uint> pollScanNetIds = null;
                    // The occupied-planter cross-check is a full radius scan (proximity over thousands
                    // of entities + component-class warmup). Only run it while we're still waiting for a
                    // just-sown generation to register (pending boxes) — that's when the cache needs
                    // validation. During steady growth the directed per-netId poll (readCropState) is
                    // enough, so we avoid a heavy scan + warmup on every tick.
                    bool haveOccupiedPlanters = false;
                    if (this.homelandFarmAutoPendingSowBoxNetIds.Count > 0)
                    {
                        haveOccupiedPlanters = this.TryHomelandFarmCollectOccupiedCapturedPlanterNetIds(
                            out occupiedPlanters,
                            out pollScanNetIds);
                        if (haveOccupiedPlanters)
                        {
                            this.HomelandFarmPruneAutoPendingSowBoxes(occupiedPlanters);
                        }
                    }

                    if (haveOccupiedPlanters
                        && occupiedPlanters.Count == 0
                        && this.homelandFarmAutoCropNetIds.Count > 0
                        && this.homelandFarmAutoPendingSowBoxNetIds.Count == 0)
                    {
                        prunedThisTick = this.homelandFarmAutoCropNetIds.Count;
                        this.homelandFarmAutoCropNetIds.Clear();
                        this.HomelandFarmLog("Auto poll: all captured planters empty - cleared stale cache.");
                    }

                    for (int i = this.homelandFarmAutoCropNetIds.Count - 1; i >= 0; i--)
                    {
                        uint cropNetId = this.homelandFarmAutoCropNetIds[i];
                        if (this.TryHomelandFarmIsDiscardedAutoFarmCropNetId(cropNetId)
                            || this.TryHomelandFarmIsOrphanPlantOnlyWithoutCropData(cropNetId, pollScanNetIds)
                            || (occupiedPlanters != null
                                && !this.TryHomelandFarmIsLiveCropOnCapturedPlanter(cropNetId, occupiedPlanters, pollScanNetIds)))
                        {
                            this.homelandFarmAutoCropNetIds.RemoveAt(i);
                            prunedThisTick++;
                            continue;
                        }

                        if (!this.TryHomelandFarmReadCropState(cropNetId, out int stage, out bool hasWeed, out long remaining))
                        {
                            // Crop entity gone (harvested elsewhere / unloaded) — drop it.
                            this.homelandFarmAutoCropNetIds.RemoveAt(i);
                            prunedThisTick++;
                            continue;
                        }

                        if (hasWeed && this.TryHomelandFarmWeed(cropNetId, out _))
                        {
                            totalWeeded++;
                            weededThisTick++;
                        }

                        if (stage >= 4)
                        {
                            if (this.TryHomelandFarmHarvestCrop(cropNetId, out _))
                            {
                                totalHarvested++;
                                harvestedThisTick++;
                            }

                            this.homelandFarmAutoHarvestedNetIds.Add(cropNetId);
                            this.homelandFarmAutoCropNetIds.RemoveAt(i);
                            continue;
                        }

                        if (remaining < minRemaining)
                        {
                            minRemaining = remaining;
                        }

                        if (i % HomelandFarmHarvestFramePaceBatch == 0)
                        {
                            yield return null;
                        }
                    }

                    if (harvestedThisTick > 0 && this.homelandFarmAutoCropNetIds.Count == 0)
                    {
                        // Whole tracked generation collected → clear the just-sown guard set.
                        this.homelandFarmAutoPendingSowBoxNetIds.Clear();
                    }

                    if (weededThisTick > 0 || harvestedThisTick > 0 || prunedThisTick > 0)
                    {
                        this.HomelandFarmLog("Auto poll: weeded=" + weededThisTick + " harvested=" + harvestedThisTick
                            + " pruned=" + prunedThisTick + " remaining=" + this.homelandFarmAutoCropNetIds.Count);
                    }

                    // 3. SOW free planters — always top up genuinely-empty boxes, even while other
                    //    crops are still growing. FindEmptyCropPlanterSlotsRoutine fills ONLY free
                    //    boxes (occupied excluded by link + world-position match; just-sown boxes
                    //    excluded via homelandFarmAutoPendingSowBoxNetIds), so this never re-sows an
                    //    un-registered box (MaxPlantCountLimit). Cooldown-gated so the heavy empty-slot
                    //    scan runs at most once per interval, even when nothing was free.
                    if (!seedsExhausted && Time.realtimeSinceStartup >= nextSowAllowedAt)
                    {
                        this.RefreshHomelandFarmSeeds();
                        HomelandFarmInventoryItem seed = this.FindHomelandFarmSeedByStaticId(seedStaticId);
                        if (seed == null)
                        {
                            seedsExhausted = true;
                            this.HomelandFarmLog("Auto: selected seed exhausted.");
                        }
                        else
                        {
                            this.homelandFarmLastStatus = "Auto: sowing " + seed.Label + "...";
                            this.homelandFarmAutoSowCount = 0;
                            IEnumerator sowPass = this.HomelandFarmAutoSowPassRoutine(seed);
                            while (sowPass.MoveNext())
                            {
                                yield return sowPass.Current;
                            }

                            if (this.homelandFarmAutoSowCount > 0)
                            {
                                totalSown += this.homelandFarmAutoSowCount;
                                // Full registration wait: just-sown crops are invisible for a beat.
                                nextSowAllowedAt = Time.realtimeSinceStartup + HomelandFarmAutoSowCooldownSeconds;
                                needDiscovery = true;
                                discoveryDelay = true; // server needs a beat to create the new crops
                                this.HomelandFarmLog("Auto: sowed " + this.homelandFarmAutoSowCount
                                    + " free planter(s); re-discovering.");
                                continue; // pick up the new crops before the next decision
                            }

                            // Nothing was free to sow right now (e.g. boxes just harvested aren't
                            // server-free yet). Use a SHORT retry gap, not the long registration
                            // cooldown, so freed boxes get filled within seconds.
                            nextSowAllowedAt = Time.realtimeSinceStartup + HomelandFarmAutoEmptyRetrySeconds;
                        }
                    }

                    // 4. DONE / WAIT when no tracked crops remain.
                    if (this.homelandFarmAutoCropNetIds.Count == 0)
                    {
                        // Finished only when seeds are gone AND nothing is still registering/growing.
                        if (seedsExhausted && this.homelandFarmAutoPendingSowBoxNetIds.Count == 0)
                        {
                            break;
                        }

                        // Waiting for just-sown crops to register/grow, or for the sow cooldown to
                        // elapse. Re-discover (picks up crops once they register) before deciding again.
                        this.homelandFarmLastStatus = "Auto: waiting for crops...";
                        yield return new WaitForSecondsRealtime(HomelandFarmAutoEmptyRetrySeconds);
                        needDiscovery = true;
                        continue;
                    }

                    // 5. Sleep until the next meaningful moment, driven by exact maturity:
                    //    final minute → weed every second; otherwise coarse weeding cadence.
                    float sleep;
                    if (minRemaining == long.MaxValue)
                    {
                        sleep = HomelandFarmAutoCoarseWeedIntervalSeconds;
                    }
                    else if (minRemaining <= HomelandFarmAutoFinalMinuteSeconds)
                    {
                        sleep = HomelandFarmAutoFinalWeedIntervalSeconds;
                    }
                    else
                    {
                        sleep = Mathf.Min(
                            HomelandFarmAutoCoarseWeedIntervalSeconds,
                            (float)(minRemaining - HomelandFarmAutoFinalMinuteSeconds));
                        sleep = Mathf.Max(sleep, HomelandFarmAutoFinalWeedIntervalSeconds);
                    }

                    // Just harvested some crops while others still grow with > threshold seconds to
                    // ripen: the freed boxes likely aren't server-free yet, so don't sleep all the way
                    // to the next ripe. Wake within the short retry window to re-sow the freed boxes.
                    if (harvestedThisTick > 0
                        && minRemaining != long.MaxValue
                        && minRemaining > HomelandFarmAutoPostHarvestResowThresholdSeconds)
                    {
                        sleep = Mathf.Min(sleep, HomelandFarmAutoEmptyRetrySeconds);
                        needDiscovery = true;
                    }

                    string remainLabel = minRemaining == long.MaxValue
                        ? "?"
                        : Mathf.Max(0, (int)minRemaining) + "s";
                    this.homelandFarmLastStatus = "Auto farming — sown " + totalSown + ", weeded " + totalWeeded
                        + ", harvested " + totalHarvested + ". Next ripe in " + remainLabel + ".";
                    this.HomelandFarmLog("Auto: next ripe in " + remainLabel + " (" + this.homelandFarmAutoCropNetIds.Count
                        + " crop(s) tracked), sleeping " + sleep.ToString("F0") + "s.");
                    yield return new WaitForSecondsRealtime(sleep);
                }

                this.homelandFarmLastStatus = "Auto farm complete — sown " + totalSown + ", weeded " + totalWeeded
                    + ", harvested " + totalHarvested + ".";
                this.AddMenuNotification(this.homelandFarmLastStatus, new Color(0.45f, 1f, 0.55f));
                this.HomelandFarmLog("Auto farm complete sown=" + totalSown + " weeded=" + totalWeeded + " harvested=" + totalHarvested);
            }
            finally
            {
                this.homelandFarmScanCenterOverride = null;
                this.homelandFarmAutoRunning = false;
                this.homelandFarmAutoCropNetIds.Clear();
                this.homelandFarmAutoHarvestedNetIds.Clear();
                this.homelandFarmAutoPendingSowBoxNetIds.Clear();
                this.homelandFarmCoroutine = null;
                this.homelandFarmBusyUntil = Time.realtimeSinceStartup + HomelandFarmActionCooldownSeconds;
            }
        }

        // One sow pass: fills every currently-empty captured planter with the selected seed,
        // up to the available seed count. Mirrors HomelandFarmSowAllRoutine (single radius scan,
        // never re-scan — the server has not marked just-sown slots occupied yet) but without the
        // coroutine bookkeeping, since the auto loop owns homelandFarmCoroutine. Result in
        // homelandFarmAutoSowCount.
        private IEnumerator HomelandFarmAutoSowPassRoutine(HomelandFarmInventoryItem seed)
        {
            this.homelandFarmAutoSowCount = 0;
            if (seed == null || seed.NetId == 0U || seed.Count <= 0)
            {
                yield break;
            }

            int sowBatchSize = Mathf.Clamp(this.TryHomelandFarmGetSprinklerCellCount(), 1, HomelandFarmBatchLimit);
            int remainingSeeds = seed.Count;
            int sowedPoints = 0;

            while (remainingSeeds > 0)
            {
                IEnumerator slotRoutine = this.FindEmptyCropPlanterSlotsRoutine(remainingSeeds, useAutoFarmCollectShortcuts: true);
                while (slotRoutine.MoveNext())
                {
                    yield return slotRoutine.Current;
                }

                List<object> plantPoints = this.homelandFarmSowSlotPoints;
                if (!this.homelandFarmSowSlotOk || plantPoints == null || plantPoints.Count == 0)
                {
                    break;
                }

                this.HomelandFarmRememberAutoSowPendingBoxes(plantPoints);
                yield return null;

                for (int offset = 0; offset < plantPoints.Count && remainingSeeds > 0; offset += sowBatchSize)
                {
                    int batchSize = Math.Min(sowBatchSize, Math.Min(plantPoints.Count - offset, remainingSeeds));
                    List<object> batch = plantPoints.GetRange(offset, batchSize);
                    if (this.TryHomelandFarmSow(seed.NetId, batch, out string sowStatus))
                    {
                        sowedPoints += batch.Count;
                        remainingSeeds -= batch.Count;
                        this.HomelandFarmLog("Auto sow batch ok seedNetId=" + seed.NetId + " count=" + batch.Count + " " + sowStatus);
                    }
                    else
                    {
                        this.HomelandFarmLog("Auto sow batch failed: " + sowStatus);
                        this.homelandFarmAutoSowCount = sowedPoints;
                        yield break;
                    }

                    yield return new WaitForSecondsRealtime(HomelandFarmCommandDelaySeconds);
                }

                // Single radius scan already returns every empty planter; re-scanning re-sows the
                // same not-yet-occupied slots and floods CropSeeding (crash + wasted seeds).
                break;
            }

            this.homelandFarmAutoSowCount = sowedPoints;
        }

        private bool TryBeginHomelandFarmAction(bool silent, out string blockReason, bool allowVisitingFarmArea = false)
        {
            blockReason = string.Empty;
            if (this.homelandFarmCoroutine != null)
            {
                blockReason = "Homeland farm action already running.";
                this.HomelandFarmLog("Action blocked: " + blockReason);
                if (!silent)
                {
                    this.AddMenuNotification(blockReason, new Color(0.45f, 0.88f, 1f));
                }

                return false;
            }

            if (Time.realtimeSinceStartup < this.homelandFarmBusyUntil)
            {
                float remaining = Mathf.Max(0f, this.homelandFarmBusyUntil - Time.realtimeSinceStartup);
                blockReason = "Homeland farm: wait " + remaining.ToString("F1") + "s";
                this.HomelandFarmLog("Action blocked: " + blockReason);
                if (!silent)
                {
                    this.AddMenuNotification(blockReason, new Color(0.45f, 0.88f, 1f));
                }

                return false;
            }

            if (!this.TryHomelandFarmIsInHomeland(out blockReason, allowVisitingFarmArea))
            {
                this.HomelandFarmLog("Action blocked: " + blockReason);
                if (!silent)
                {
                    this.AddMenuNotification(this.FormatHomelandFarmBlockNotification(blockReason), new Color(1f, 0.55f, 0.45f));
                }

                return false;
            }

            if (!this.EnsureHomelandFarmReflectionReady())
            {
                blockReason = string.IsNullOrEmpty(this.homelandFarmReflectionUnavailableStatus)
                    ? "Homeland farm reflection unavailable."
                    : this.homelandFarmReflectionUnavailableStatus;
                this.HomelandFarmLog("Action blocked: " + blockReason);
                if (!silent)
                {
                    this.AddMenuNotification(blockReason, new Color(1f, 0.55f, 0.45f));
                }

                return false;
            }

            this.homelandFarmBusyUntil = Time.realtimeSinceStartup + HomelandFarmActionCooldownSeconds;
            return true;
        }

        private string FormatHomelandFarmBlockNotification(string blockReason)
        {
            if (string.IsNullOrWhiteSpace(blockReason))
            {
                return blockReason;
            }

            return blockReason.StartsWith("homeland_farm.", StringComparison.Ordinal)
                ? this.L(blockReason)
                : blockReason;
        }

        private void StartHomelandFarmWater(HomelandFarmWaterMode mode, bool silent)
        {
            bool allowVisitingFarmArea = mode == HomelandFarmWaterMode.Unwatered
                || mode == HomelandFarmWaterMode.Friends
                || mode == HomelandFarmWaterMode.InRadius;
            if (!this.TryBeginHomelandFarmAction(silent, out _, allowVisitingFarmArea))
            {
                return;
            }

            this.HomelandFarmLog("Start water mode=" + mode + " radius=" + this.homelandFarmWaterRadius);
            this.homelandFarmLastStatus = "Watering homeland farm (" + mode + ")...";
            this.homelandFarmCoroutine = ModCoroutines.Start(this.HomelandFarmWaterRoutine(mode, silent));
        }

        private void StartHomelandFarmHarvestCrops(bool silent)
        {
            if (!this.TryBeginHomelandFarmAction(silent, out _, allowVisitingFarmArea: true))
            {
                return;
            }

            this.HomelandFarmLog("Start harvest crops in radius");
            this.homelandFarmLastStatus = "Harvesting crops in radius...";
            this.homelandFarmCoroutine = ModCoroutines.Start(this.HomelandFarmHarvestCropsRoutine(silent));
        }

        private void StartHomelandFarmCollectPlantSeeds(bool silent)
        {
            if (!this.TryBeginHomelandFarmAction(silent, out _, allowVisitingFarmArea: true))
            {
                return;
            }

            this.HomelandFarmLog("Start collect plant seeds in radius");
            this.homelandFarmLastStatus = "Collecting plant seeds in radius...";
            this.homelandFarmCoroutine = ModCoroutines.Start(this.HomelandFarmCollectPlantSeedsRoutine(silent));
        }

        private void StartHomelandFarmWeedAll(bool silent)
        {
            if (!this.TryBeginHomelandFarmAction(silent, out _, allowVisitingFarmArea: true))
            {
                return;
            }

            this.HomelandFarmLog("Start weed crops in radius");
            this.homelandFarmLastStatus = "Weeding crops in radius...";
            this.homelandFarmCoroutine = ModCoroutines.Start(this.HomelandFarmWeedAllRoutine(silent));
        }

        // Hotkey convenience: water in radius, then weed in radius, as a single action.
        private void StartHomelandFarmWaterAndWeed(bool silent)
        {
            if (!this.TryBeginHomelandFarmAction(silent, out _, allowVisitingFarmArea: true))
            {
                return;
            }

            this.HomelandFarmLog("Start water + weed in radius");
            this.homelandFarmLastStatus = "Water + weed in radius...";
            this.homelandFarmCoroutine = ModCoroutines.Start(this.HomelandFarmWaterAndWeedRoutine(silent));
        }

        private IEnumerator HomelandFarmWaterAndWeedRoutine(bool silent)
        {
            try
            {
                // Drive the two existing routines in sequence. Each manages its own batching; their
                // finally blocks clear homelandFarmCoroutine, but homelandFarmBusyUntil (set by the
                // initial TryBegin and by each routine) guards against overlapping actions meanwhile.
                IEnumerator water = this.HomelandFarmWaterRoutine(HomelandFarmWaterMode.InRadius, silent);
                while (water.MoveNext())
                {
                    yield return water.Current;
                }

                // Let native water commands and the first radius collect settle before weed reuses
                // that collect or runs another proximity pass (back-to-back Aura scans crash).
                yield return null;
                yield return null;
                yield return new WaitForSecondsRealtime(0.35f);

                IEnumerator weed = this.HomelandFarmWeedAllRoutine(silent);
                while (weed.MoveNext())
                {
                    yield return weed.Current;
                }
            }
            finally
            {
                this.homelandFarmCoroutine = null;
                this.homelandFarmBusyUntil = Time.realtimeSinceStartup + HomelandFarmActionCooldownSeconds;
            }
        }

        private IEnumerator HomelandFarmWaterRoutine(HomelandFarmWaterMode mode, bool silent)
        {
            yield return null;

            int cropBoxCount = 0;
            int plantCount = 0;
            int batchCount = 0;
            int failCount = 0;
            string modeLabel = mode.ToString();

            try
            {
                if (mode == HomelandFarmWaterMode.InRadius && !this.TryHomelandFarmTryIsHandHoldSprinklerEquipped())
                {
                    if (!this.TryEquipHandTool(HomelandFarmSprinklerToolTypeId, out string equipStatus))
                    {
                        this.homelandFarmLastStatus = "Equip sprinkler failed: " + equipStatus;
                        this.HomelandFarmLog(this.homelandFarmLastStatus);
                        if (!silent)
                        {
                            this.AddMenuNotification(this.homelandFarmLastStatus, new Color(1f, 0.55f, 0.45f));
                        }

                        yield break;
                    }

                    // The equip action returned success (SetHandhold ok). The equipped-state check is
                    // unreliable on builds where HandHoldSprinkler has no managed type and is not an
                    // ECS component on the player entity, so don't poll/wait for it — proceed after a
                    // single frame to let the command apply. The server rejects water if truly unarmed.
                    yield return null;
                }

                bool allowVisitingFarmArea = mode == HomelandFarmWaterMode.Unwatered
                    || mode == HomelandFarmWaterMode.Friends
                    || mode == HomelandFarmWaterMode.InRadius;
                // InRadius only needs the local radius scanned — keeps the scan fast (no freeze).
                float scanRadiusOverride = mode == HomelandFarmWaterMode.InRadius ? this.homelandFarmWaterRadius : 0f;
                if (!this.ScanHomelandFarmWaterTargets(out List<HomelandFarmTarget> targets, out string scanStatus, allowVisitingFarmArea, scanRadiusOverride))
                {
                    this.homelandFarmLastStatus = scanStatus;
                    if (!silent)
                    {
                        this.AddMenuNotification("Water: " + scanStatus, new Color(1f, 0.55f, 0.45f));
                    }

                    yield break;
                }

                int scannedCount = targets.Count;
                this.TryGetHomelandFarmPlayerNetId(out uint playerNetId, out _);

                switch (mode)
                {
                    case HomelandFarmWaterMode.InRadius:
                        if (this.TryGetHomelandFarmPlayerPosition(out Vector3 playerPos))
                        {
                            this.FilterHomelandFarmByRadius(targets, playerPos, this.homelandFarmWaterRadius);
                            this.HomelandFarmLog("Water after radius filter: " + targets.Count + "/" + scannedCount + " radius=" + this.homelandFarmWaterRadius);
                        }
                        else
                        {
                            this.HomelandFarmLog("Player position unavailable; skipping radius filter for " + targets.Count + " scanned target(s).");
                        }

                        break;
                    case HomelandFarmWaterMode.Own:
                        this.FilterHomelandFarmOwn(targets, playerNetId);
                        break;
                    case HomelandFarmWaterMode.Friends:
                        if (this.TryHomelandFarmGetSelfPlayInFieldOwnerNetId(out uint visitingFieldOwnerNetId)
                            && visitingFieldOwnerNetId != 0U
                            && visitingFieldOwnerNetId != playerNetId)
                        {
                            this.HomelandFarmLog("Water friends: visiting field owner=" + visitingFieldOwnerNetId);
                            for (int ti = 0; ti < targets.Count; ti++)
                            {
                                HomelandFarmTarget visitTarget = targets[ti];
                                if (visitTarget != null && visitTarget.OwnerId == 0U)
                                {
                                    visitTarget.OwnerId = visitingFieldOwnerNetId;
                                }
                            }

                            break;
                        }

                        HashSet<uint> friendNetIds = new HashSet<uint>();
                        if (!this.TryGetHomelandFarmFriendNetIds(friendNetIds, out string friendStatus) || friendNetIds.Count == 0)
                        {
                            this.HomelandFarmLog("Water friends blocked: " + (string.IsNullOrEmpty(friendStatus) ? "Friend service unavailable." : friendStatus));
                            this.homelandFarmLastStatus = string.IsNullOrEmpty(friendStatus)
                                ? "Friend service unavailable."
                                : friendStatus;
                            if (!silent)
                            {
                                this.AddMenuNotification("Water friends: " + this.homelandFarmLastStatus, new Color(1f, 0.55f, 0.45f));
                            }

                            yield break;
                        }

                        this.FilterHomelandFarmFriends(targets, friendNetIds);
                        this.HomelandFarmLog("Water friends filter kept " + targets.Count + "/" + scannedCount);
                        break;
                    case HomelandFarmWaterMode.Unwatered:
                        this.FilterHomelandFarmUnwatered(targets);
                        break;
                }

                this.FilterHomelandFarmUnwatered(targets);
                int afterDryFilter = targets.Count;
                this.HomelandFarmLog("Water after dry filter: " + afterDryFilter + "/" + scannedCount);

                this.TryHomelandFarmResolveWaterTargetOwners(targets, mode, playerNetId);

                this.HomelandFarmLog("Water scan mode=" + modeLabel + " targets=" + targets.Count + " playerNetId=" + playerNetId);

                if (targets.Count == 0)
                {
                    this.homelandFarmLastStatus = "No water targets found (mode: " + modeLabel + ").";
                    if (!silent)
                    {
                        this.AddMenuNotification(this.homelandFarmLastStatus, new Color(0.45f, 0.88f, 1f));
                    }

                    yield break;
                }

                Dictionary<uint, List<uint>> cropBoxesByOwner = new Dictionary<uint, List<uint>>();
                Dictionary<uint, List<uint>> plantsByOwner = new Dictionary<uint, List<uint>>();

                for (int i = 0; i < targets.Count; i++)
                {
                    HomelandFarmTarget target = targets[i];
                    if (target == null)
                    {
                        continue;
                    }

                    if (target.IsCropBox)
                    {
                        if (!cropBoxesByOwner.TryGetValue(target.OwnerId, out List<uint> cropList))
                        {
                            cropList = new List<uint>();
                            cropBoxesByOwner[target.OwnerId] = cropList;
                        }

                        cropList.Add(target.NetId);
                        cropBoxCount++;
                    }
                    else
                    {
                        if (!plantsByOwner.TryGetValue(target.OwnerId, out List<uint> plantList))
                        {
                            plantList = new List<uint>();
                            plantsByOwner[target.OwnerId] = plantList;
                        }

                        plantList.Add(target.NetId);
                        plantCount++;
                    }
                }

                // Batch size must match the player's sprinkler skill (TableMode[HandHoldSprinkler.mode].num,
                // 1/3/6/9 cells). The server rejects the whole command if it exceeds the player's skill.
                int waterBatchSize = this.TryHomelandFarmGetSprinklerCellCount();
                if (waterBatchSize < 1)
                {
                    waterBatchSize = 1;
                }

                if (waterBatchSize > HomelandFarmBatchLimit)
                {
                    waterBatchSize = HomelandFarmBatchLimit;
                }

                this.HomelandFarmLog("Water batch size=" + waterBatchSize + " (TableMode.num)");

                foreach (KeyValuePair<uint, List<uint>> ownerGroup in cropBoxesByOwner)
                {
                    uint ownerId = this.TryHomelandFarmResolveWaterBatchOwner(ownerGroup.Key, mode, playerNetId);
                    if (ownerId == 0U)
                    {
                        failCount++;
                        this.HomelandFarmLog("Water crop batch owner unresolved count=" + ownerGroup.Value.Count);
                        continue;
                    }

                    List<uint> netIds = ownerGroup.Value;
                    for (int offset = 0; offset < netIds.Count; offset += waterBatchSize)
                    {
                        int count = Math.Min(waterBatchSize, netIds.Count - offset);
                        List<uint> batch = netIds.GetRange(offset, count);
                        if (this.TryHomelandFarmWaterBatch(playerNetId, batch, new Dictionary<uint, List<uint>>(), out string cropBatchStatus))
                        {
                            batchCount++;
                            this.HomelandFarmLog("Water crop batch fieldOwner=" + ownerId + " player=" + playerNetId + " count=" + batch.Count + " ok: " + cropBatchStatus);
                        }
                        else
                        {
                            failCount++;
                            this.HomelandFarmLog("Water crop batch fail fieldOwner=" + ownerId + " player=" + playerNetId + " count=" + batch.Count + ": " + cropBatchStatus);
                        }

                        yield return new WaitForSecondsRealtime(HomelandFarmWaterCommandDelaySeconds);
                    }
                }

                foreach (KeyValuePair<uint, List<uint>> ownerGroup in plantsByOwner)
                {
                    uint ownerId = this.TryHomelandFarmResolveWaterBatchOwner(ownerGroup.Key, mode, playerNetId);
                    if (ownerId == 0U)
                    {
                        failCount++;
                        this.HomelandFarmLog("Water plant batch owner unresolved count=" + ownerGroup.Value.Count);
                        continue;
                    }

                    List<uint> netIds = ownerGroup.Value;
                    for (int offset = 0; offset < netIds.Count; offset += waterBatchSize)
                    {
                        int count = Math.Min(waterBatchSize, netIds.Count - offset);
                        List<uint> batch = netIds.GetRange(offset, count);
                        // Vanilla passes actor.entity.netId (watering player's netId) as fieldOwnerNetId
                        Dictionary<uint, List<uint>> batchPlants = new Dictionary<uint, List<uint>> { { playerNetId, batch } };
                        if (this.TryHomelandFarmWaterBatch(0U, new List<uint>(), batchPlants, out string plantBatchStatus))
                        {
                            batchCount++;
                            this.HomelandFarmLog("Water plant batch fieldOwner=" + ownerId + " player=" + playerNetId + " count=" + batch.Count + " ok: " + plantBatchStatus);
                        }
                        else
                        {
                            failCount++;
                            this.HomelandFarmLog("Water plant batch fail fieldOwner=" + ownerId + " count=" + batch.Count + ": " + plantBatchStatus);
                        }

                        yield return new WaitForSecondsRealtime(HomelandFarmWaterCommandDelaySeconds);
                    }
                }

                this.HomelandFarmLog("Water done crops=" + cropBoxCount + " plants=" + plantCount + " batches=" + batchCount + " fails=" + failCount);

                this.homelandFarmLastStatus = "Watered " + cropBoxCount + " crop box(es), " + plantCount + " plant(s) ("
                    + batchCount + " batch(es), mode: " + modeLabel + ").";
                if (!silent)
                {
                    Color notifyColor = failCount > 0
                        ? new Color(1f, 0.75f, 0.45f)
                        : new Color(0.45f, 1f, 0.55f);
                    this.AddMenuNotification("Water: " + cropBoxCount + " crops, " + plantCount + " plants (" + modeLabel + ")", notifyColor);
                }
            }
            finally
            {
                this.homelandFarmCoroutine = null;
                this.homelandFarmBusyUntil = Time.realtimeSinceStartup + HomelandFarmActionCooldownSeconds;
            }
        }

        private IEnumerator HomelandFarmHarvestCropsRoutine(bool silent)
        {
            yield return null;

            try
            {
                List<uint> cropNetIds = this.ScanHomelandFarmHarvestableCropsByRadius();
                if (cropNetIds.Count == 0)
                {
                    this.homelandFarmLastStatus = "No harvestable crops in radius.";
                    if (!silent)
                    {
                        this.AddMenuNotification(this.homelandFarmLastStatus, new Color(0.45f, 0.88f, 1f));
                    }

                    yield break;
                }

                int harvested = 0;
                int failed = 0;
                for (int i = 0; i < cropNetIds.Count; i++)
                {
                    uint cropNetId = cropNetIds[i];
                    if (this.TryHomelandFarmHarvestCrop(cropNetId, out string harvestStatus))
                    {
                        harvested++;
                        this.HomelandFarmLog("Harvest ok netId=" + cropNetId + " " + harvestStatus);
                    }
                    else
                    {
                        failed++;
                        this.HomelandFarmLog("Harvest fail netId=" + cropNetId + " " + harvestStatus);
                    }

                    if (HomelandFarmHarvestDelaySeconds > 0f)
                    {
                        yield return new WaitForSecondsRealtime(HomelandFarmHarvestDelaySeconds);
                    }
                    else if ((i + 1) % HomelandFarmHarvestFramePaceBatch == 0)
                    {
                        // Keep zero-delay harvest responsive and avoid same-frame command spikes.
                        yield return null;
                    }
                }

                this.homelandFarmLastStatus = "Harvested " + harvested + "/" + cropNetIds.Count + " crop(s)"
                    + (failed > 0 ? ", " + failed + " failed" : string.Empty) + ".";
                if (!silent)
                {
                    Color notifyColor = harvested > 0
                        ? new Color(0.45f, 1f, 0.55f)
                        : new Color(1f, 0.55f, 0.45f);
                    this.AddMenuNotification("Harvest: " + harvested + "/" + cropNetIds.Count + " crops", notifyColor);
                }
            }
            finally
            {
                this.homelandFarmCoroutine = null;
                this.homelandFarmBusyUntil = Time.realtimeSinceStartup + HomelandFarmActionCooldownSeconds;
            }
        }

        private IEnumerator HomelandFarmCollectPlantSeedsRoutine(bool silent)
        {
            yield return null;

            try
            {
                List<uint> plantNetIds = this.ScanHomelandFarmCollectablePlantSeedsByRadius();
                if (plantNetIds.Count == 0)
                {
                    this.homelandFarmLastStatus = "No collectable plant seeds in radius.";
                    if (!silent)
                    {
                        this.AddMenuNotification(this.homelandFarmLastStatus, new Color(0.45f, 0.88f, 1f));
                    }

                    yield break;
                }

                int collected = 0;
                int failed = 0;
                for (int i = 0; i < plantNetIds.Count; i++)
                {
                    if (!this.TryHomelandFarmCanPutSeedInBag(plantNetIds[i], out string bagStatus))
                    {
                        this.homelandFarmLastStatus = "Bag full — collected " + collected + "/" + plantNetIds.Count + " plant seed(s).";
                        if (!silent)
                        {
                            this.AddMenuNotification(this.homelandFarmLastStatus, new Color(1f, 0.75f, 0.45f));
                        }

                        yield break;
                    }

                    uint plantNetId = plantNetIds[i];
                    if (this.TryHomelandFarmCollectPlantSeed(plantNetId, out string collectStatus))
                    {
                        collected++;
                        this.HomelandFarmLog("Collect seed ok netId=" + plantNetId + " " + collectStatus);
                    }
                    else
                    {
                        failed++;
                        this.HomelandFarmLog("Collect seed fail netId=" + plantNetId + " " + collectStatus);
                    }

                    if (HomelandFarmCollectSeedDelaySeconds > 0f)
                    {
                        yield return new WaitForSecondsRealtime(HomelandFarmCollectSeedDelaySeconds);
                    }
                    else
                    {
                        yield return null;
                    }
                }

                this.homelandFarmLastStatus = "Collected " + collected + "/" + plantNetIds.Count + " plant seed(s)"
                    + (failed > 0 ? ", " + failed + " failed" : string.Empty) + ".";
                if (!silent)
                {
                    Color notifyColor = collected > 0
                        ? new Color(0.45f, 1f, 0.55f)
                        : new Color(1f, 0.55f, 0.45f);
                    this.AddMenuNotification("Plant seeds: " + collected + "/" + plantNetIds.Count, notifyColor);
                }
            }
            finally
            {
                this.homelandFarmCoroutine = null;
                this.homelandFarmBusyUntil = Time.realtimeSinceStartup + HomelandFarmActionCooldownSeconds;
            }
        }

        private IEnumerator HomelandFarmWeedAllRoutine(bool silent)
        {
            yield return null;
            yield return null;

            try
            {
                List<uint> cropNetIds = this.ScanHomelandFarmWeedableCropsByRadius();
                if (cropNetIds.Count == 0)
                {
                    this.homelandFarmLastStatus = "No weedable crops in radius.";
                    if (!silent)
                    {
                        this.AddMenuNotification(this.homelandFarmLastStatus, new Color(0.45f, 0.88f, 1f));
                    }

                    yield break;
                }

                int weeded = 0;
                int failed = 0;
                for (int i = 0; i < cropNetIds.Count; i++)
                {
                    uint weedNetId = cropNetIds[i];
                    if (this.TryHomelandFarmWeed(weedNetId, out string weedStatus))
                    {
                        weeded++;
                        this.HomelandFarmLog("Weed ok netId=" + weedNetId + " " + weedStatus);
                    }
                    else
                    {
                        failed++;
                        this.HomelandFarmLog("Weed fail netId=" + weedNetId + " " + weedStatus);
                    }

                    if (HomelandFarmWeedDelaySeconds > 0f)
                    {
                        yield return new WaitForSecondsRealtime(HomelandFarmWeedDelaySeconds);
                    }
                    else if ((i + 1) % HomelandFarmHarvestFramePaceBatch == 0)
                    {
                        yield return null;
                    }
                }

                this.homelandFarmLastStatus = "Weeded " + weeded + "/" + cropNetIds.Count + " crop(s)"
                    + (failed > 0 ? ", " + failed + " failed" : string.Empty) + ".";
                if (!silent)
                {
                    Color notifyColor = weeded > 0
                        ? new Color(0.45f, 1f, 0.55f)
                        : new Color(1f, 0.55f, 0.45f);
                    this.AddMenuNotification("Weed: " + weeded + "/" + cropNetIds.Count + " crops", notifyColor);
                }
            }
            finally
            {
                this.homelandFarmCoroutine = null;
                this.homelandFarmBusyUntil = Time.realtimeSinceStartup + HomelandFarmActionCooldownSeconds;
            }
        }

        private bool EnsureHomelandFarmBackpackReflection()
        {
            if (this.homelandFarmBackpackReflectionResolved)
            {
                return !this.homelandFarmBackpackReflectionUnavailable;
            }

            if (this.homelandFarmBackpackReflectionUnavailable)
            {
                return false;
            }

            this.homelandFarmBackPackSystemType = this.FindLoadedTypeByFullName("XDTGameSystem.GameplaySystem.BackPack.BackPackSystem")
                ?? this.FindLoadedType("XDTGameSystem.GameplaySystem.BackPack.BackPackSystem", "BackPackSystem");
            if (this.homelandFarmBackPackSystemType == null)
            {
                this.homelandFarmBackpackReflectionUnavailable = true;
                return false;
            }

            foreach (MethodInfo method in this.homelandFarmBackPackSystemType.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (method == null || method.Name != "CanPutIn")
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 2
                    && (parameters[0].ParameterType == typeof(int) || parameters[0].ParameterType == typeof(uint))
                    && (parameters[1].ParameterType == typeof(int) || parameters[1].ParameterType == typeof(uint)))
                {
                    this.homelandFarmBackPackCanPutInMethod = method;
                    break;
                }
            }

            this.homelandFarmBackpackReflectionResolved = true;
            if (this.homelandFarmBackPackCanPutInMethod == null)
            {
                this.homelandFarmBackpackReflectionUnavailable = true;
                return false;
            }

            return true;
        }

        private object GetHomelandFarmBackPackSystemInstance()
        {
            if (this.homelandFarmBackPackSystemType == null)
            {
                return null;
            }

            try
            {
                if (this.TryGetManagedModule(this.homelandFarmBackPackSystemType, out object instance) && instance != null)
                {
                    return instance;
                }

                return this.TryGetStaticObjectAcrossHierarchy(this.homelandFarmBackPackSystemType, "Instance", "_instance");
            }
            catch
            {
                return null;
            }
        }

        private bool TryHomelandFarmCanPutInBag(int staticId, int count, out string status)
        {
            status = string.Empty;
            if (staticId <= 0 || count <= 0)
            {
                return true;
            }

            if (!this.EnsureHomelandFarmBackpackReflection())
            {
                return true;
            }

            object backPackObj = this.GetHomelandFarmBackPackSystemInstance();
            if (backPackObj == null)
            {
                return true;
            }

            try
            {
                object result = this.homelandFarmBackPackCanPutInMethod.Invoke(backPackObj, new object[] { staticId, count });
                if (result is bool canPut)
                {
                    if (!canPut)
                    {
                        status = "Bag full.";
                    }

                    return canPut;
                }
            }
            catch (Exception ex)
            {
                this.HomelandFarmLog("CanPutIn exception: " + ex.Message);
            }

            return true;
        }

        private bool TryHomelandFarmGetPlantCrossedSeedStaticId(uint plantNetId, out int staticId)
        {
            staticId = 0;
            if (plantNetId == 0U || !this.EnsureHomelandFarmReflectionReady())
            {
                return false;
            }

            if (!this.TryHomelandFarmGetComponentData(this.homelandFarmPlantItemDataType, plantNetId, out object plantData, out _, "PlantItemData")
                || plantData == null)
            {
                return false;
            }

            string[] members = new string[]
            {
                "crossedSeedStaticId", "CrossedSeedStaticId", "seedStaticId", "SeedStaticId",
                "crossSeedStaticId", "CrossSeedStaticId", "staticId", "StaticId"
            };
            for (int i = 0; i < members.Length; i++)
            {
                if (this.TryReadManagedInt32Member(plantData, members[i], out int value) && value > 0)
                {
                    staticId = value;
                    return true;
                }
            }

            return false;
        }

        private bool TryHomelandFarmCanPutSeedInBag(uint plantNetId, out string status)
        {
            status = string.Empty;
            if (!this.EnsureHomelandFarmBackpackReflection())
            {
                return true;
            }

            if (!this.TryHomelandFarmGetPlantCrossedSeedStaticId(plantNetId, out int staticId) || staticId <= 0)
            {
                return true;
            }

            return this.TryHomelandFarmCanPutInBag(staticId, 1, out status);
        }

        private bool EnsureHomelandFarmInventoryReflection()
        {
            if (this.homelandFarmInventoryReflectionResolved)
            {
                return !this.homelandFarmInventoryReflectionUnavailable;
            }

            this.homelandFarmInventoryReflectionResolved = true;
            if (!this.EnsureHomelandFarmBackpackReflection())
            {
                this.homelandFarmInventoryReflectionUnavailable = true;
                return false;
            }

            this.homelandFarmStorageTypeType = this.FindLoadedTypeByFullName("EcsClient.XDT.Scene.Shared.Data.StaticPartial.EStorageType")
                ?? this.FindLoadedType("EcsClient.XDT.Scene.Shared.Data.StaticPartial.EStorageType", "EStorageType")
                ?? this.FindLoadedType("EcsClient.XDT.Scene.Shared.Data.SharedData.EStorageType", "EStorageType");
            if (this.homelandFarmBackPackSystemType == null)
            {
                this.homelandFarmInventoryReflectionUnavailable = true;
                return false;
            }

            if (this.homelandFarmStorageTypeType != null)
            {
                try
                {
                    object storageProbe = Enum.Parse(this.homelandFarmStorageTypeType, "Backpack");
                    this.homelandFarmBackPackGetAllItemMethod = this.homelandFarmBackPackSystemType.GetMethod(
                        "GetAllItem",
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                        null,
                        new[] { storageProbe.GetType() },
                        null);
                }
                catch
                {
                }
            }

            if (this.homelandFarmBackPackGetAllItemMethod == null)
            {
                this.homelandFarmBackPackGetAllItemMethod = this.homelandFarmBackPackSystemType.GetMethod(
                    "GetAllItem",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    Type.EmptyTypes,
                    null);
            }

            if (this.homelandFarmBackPackGetAllItemMethod == null)
            {
                this.homelandFarmInventoryReflectionUnavailable = true;
                return false;
            }

            return true;
        }

        private bool EnsureHomelandFarmTableDataReflection()
        {
            if (this.homelandFarmTableDataReflectionResolved)
            {
                return this.homelandFarmTableDataType != null;
            }

            this.homelandFarmTableDataReflectionResolved = true;
            this.homelandFarmTableDataType = this.FindLoadedTypeByFullName("EcsClient.TableData")
                ?? this.FindLoadedType("EcsClient.TableData", "TableData")
                ?? this.FindLoadedTypeByFullName("TableData");
            if (this.homelandFarmTableDataType == null)
            {
                return false;
            }

            foreach (MethodInfo method in this.homelandFarmTableDataType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (method == null)
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (method.Name == "DecodeTypeEntityData" && parameters.Length == 1
                    && (parameters[0].ParameterType == typeof(int) || parameters[0].ParameterType == typeof(uint)))
                {
                    this.homelandFarmDecodeTypeEntityDataMethod = method;
                }
                else if (method.Name == "GetEntity" && parameters.Length == 2
                    && (parameters[0].ParameterType == typeof(int) || parameters[0].ParameterType == typeof(uint))
                    && parameters[1].ParameterType == typeof(bool))
                {
                    this.homelandFarmGetEntityMethod = method;
                }
                else if (method.Name == "GetEntity" && parameters.Length == 1
                    && (parameters[0].ParameterType == typeof(int) || parameters[0].ParameterType == typeof(uint))
                    && this.homelandFarmGetEntityMethod == null)
                {
                    this.homelandFarmGetEntityMethod = method;
                }
                else if (method.Name == "GetCropfertilizer" && parameters.Length == 1
                    && (parameters[0].ParameterType == typeof(int) || parameters[0].ParameterType == typeof(uint)))
                {
                    this.homelandFarmGetCropfertilizerMethod = method;
                }
                else if (method.Name == "GetBackPackName" && parameters.Length == 3
                    && (parameters[0].ParameterType == typeof(int) || parameters[0].ParameterType == typeof(uint)))
                {
                    this.homelandFarmGetBackPackNameMethod = method;
                }
            }

            return true;
        }

        private bool TryResolveHomelandFarmEntityTypeValue(string enumName, ref int cachedValue)
        {
            if (cachedValue != int.MinValue)
            {
                return true;
            }

            if (this.homelandFarmEntityTypeEnumType == null)
            {
                this.homelandFarmEntityTypeEnumType = this.FindLoadedType(
                    "EcsClient.XDT.Scene.Shared.Data.SharedData.EntityType",
                    "XDT.Scene.Shared.Data.SharedData.EntityType",
                    "EntityType");
            }

            if (this.homelandFarmEntityTypeEnumType != null)
            {
                try
                {
                    cachedValue = Convert.ToInt32(Enum.Parse(this.homelandFarmEntityTypeEnumType, enumName, ignoreCase: true));
                    return true;
                }
                catch
                {
                }
            }

            this.ResolveAuraFarmRuntimeMethodsViaMono();
            if (this.EnsureAuraMonoApiReady() && this.AttachAuraMonoThread())
            {
                IntPtr entityTypeClass = this.FindAuraMonoClassByFullName("EcsClient.XDT.Scene.Shared.Data.SharedData.EntityType");
                if (entityTypeClass == IntPtr.Zero)
                {
                    entityTypeClass = this.FindAuraMonoClassAcrossLoadedAssemblies("EcsClient.XDT.Scene.Shared.Data.SharedData", "EntityType");
                }

                string[] names = { enumName, char.ToUpper(enumName[0]) + enumName.Substring(1) };
                if (entityTypeClass != IntPtr.Zero && this.TryReadAuraMonoStaticIntField(entityTypeClass, names, out int auraValue))
                {
                    cachedValue = auraValue;
                    return true;
                }
            }

            return false;
        }

        private bool TryResolveHomelandFarmCropSeedEntityType(out int entityType)
        {
            entityType = this.homelandFarmCropSeedEntityTypeValue;
            return this.TryResolveHomelandFarmEntityTypeValue("cropseed", ref this.homelandFarmCropSeedEntityTypeValue)
                && this.homelandFarmCropSeedEntityTypeValue != int.MinValue;
        }

        private bool TryResolveHomelandFarmCropFertilizerEntityType(out int entityType)
        {
            entityType = this.homelandFarmCropFertilizerEntityTypeValue;
            return this.TryResolveHomelandFarmEntityTypeValue("cropfertilizer", ref this.homelandFarmCropFertilizerEntityTypeValue)
                && this.homelandFarmCropFertilizerEntityTypeValue != int.MinValue;
        }

        private bool TryResolveHomelandFarmSprinklerEntityType(out int entityType)
        {
            entityType = this.homelandFarmSprinklerEntityTypeValue;
            return this.TryResolveHomelandFarmEntityTypeValue("sprinkler", ref this.homelandFarmSprinklerEntityTypeValue)
                && this.homelandFarmSprinklerEntityTypeValue != int.MinValue;
        }

        private bool TryHomelandFarmGetEntityTypeForStaticId(int staticId, out int entityType)
        {
            entityType = 0;
            if (staticId <= 0)
            {
                return false;
            }

            if (!this.EnsureHomelandFarmTableDataReflection() || this.homelandFarmDecodeTypeEntityDataMethod == null)
            {
                return false;
            }

            try
            {
                object decoded = this.homelandFarmDecodeTypeEntityDataMethod.Invoke(null, new object[] { staticId });
                if (decoded == null)
                {
                    return false;
                }

                if (this.TryReadManagedInt32Member(decoded, "id", out entityType) && entityType != 0)
                {
                    return true;
                }

                return this.TryReadManagedInt32Member(decoded, "Id", out entityType) && entityType != 0;
            }
            catch
            {
                return false;
            }
        }

        private bool TryHomelandFarmItemMatchesEntityType(int staticId, int itemEntityType, int targetEntityType)
        {
            if (targetEntityType == int.MinValue)
            {
                return false;
            }

            if (itemEntityType == targetEntityType)
            {
                return true;
            }

            return this.TryHomelandFarmGetEntityTypeForStaticId(staticId, out int decodedType) && decodedType == targetEntityType;
        }

        private bool TryHomelandFarmItemMatchesCropSeed(int staticId, int itemEntityType)
        {
            if (!this.TryResolveHomelandFarmCropSeedEntityType(out int cropSeedType))
            {
                cropSeedType = int.MinValue;
            }

            return this.TryHomelandFarmItemMatchesEntityType(staticId, itemEntityType, cropSeedType);
        }

        private bool TryHomelandFarmItemMatchesCropFertilizer(int staticId, int itemEntityType)
        {
            if (!this.TryResolveHomelandFarmCropFertilizerEntityType(out int fertilizerType))
            {
                fertilizerType = int.MinValue;
            }

            return this.TryHomelandFarmItemMatchesEntityType(staticId, itemEntityType, fertilizerType);
        }

        private bool TryHomelandFarmItemMatchesSprinkler(int staticId, int itemEntityType)
        {
            if (!this.TryResolveHomelandFarmSprinklerEntityType(out int sprinklerType))
            {
                sprinklerType = int.MinValue;
            }

            return this.TryHomelandFarmItemMatchesEntityType(staticId, itemEntityType, sprinklerType);
        }

        private string TryHomelandFarmGetItemLabel(int staticId)
        {
            if (staticId <= 0)
            {
                return "item";
            }

            if (this.TryHomelandFarmResolveBackpackItemDisplayName(IntPtr.Zero, null, staticId, 0, 0U, out string displayName))
            {
                return displayName;
            }

            return "Item " + staticId;
        }

        // Same name pipeline as crop seeds (PetFeed backpack scan order).
        private bool TryHomelandFarmResolveBackpackItemDisplayName(
            IntPtr auraItemObj,
            object managedItem,
            int staticId,
            int step,
            uint netId,
            out string displayName)
        {
            displayName = string.Empty;
            if (staticId <= 0)
            {
                return false;
            }

            // Match the Bag / Warehouse and Auto Sell tabs: resolve the canonical
            // item name from the static-id game table first (TableData.GetBackPackName).
            // The PetFeed-style paths below stay as fallbacks. Without this, fertilizer
            // and seed labels came from the live AuraMono item Name + pet-food
            // normalization, so they diverged from every other tab.
            if (this.TryGetResolvedFoodNameFromStaticId(staticId, out string tableDisplayName)
                && !this.IsPoorBagItemDisplayName(tableDisplayName, staticId))
            {
                displayName = tableDisplayName;
                return true;
            }

            if (auraItemObj != IntPtr.Zero)
            {
                string auraName = this.ReadPetFeedBackpackItemNameAuraMono(auraItemObj);
                if (!string.IsNullOrWhiteSpace(auraName) && !this.IsPoorBagItemDisplayName(auraName, staticId))
                {
                    displayName = auraName;
                    return true;
                }
            }

            if (this.TryResolvePetFeedFoodNameFromBackpackItemTypeManaged(staticId, step, netId, out string backpackName)
                && !this.IsPoorBagItemDisplayName(backpackName, staticId))
            {
                displayName = backpackName;
                return true;
            }

            if (this.TryHomelandFarmTryGetBackPackNameAuraMono(staticId, step, netId, out string auraBackpackName)
                && !this.IsPoorBagItemDisplayName(auraBackpackName, staticId))
            {
                displayName = auraBackpackName;
                return true;
            }

            if (this.TryResolvePetFeedFoodNameFromEntityTableManaged(staticId, out string entityName)
                && !this.IsPoorBagItemDisplayName(entityName, staticId))
            {
                displayName = entityName;
                return true;
            }

            if (managedItem != null
                && this.TryReadPetFeedFoodNameFromManagedObject(managedItem, out string objectName)
                && !this.IsPoorBagItemDisplayName(objectName, staticId))
            {
                displayName = objectName;
                return true;
            }

            if (auraItemObj != IntPtr.Zero
                && this.TryGetMonoStringMember(auraItemObj, "icon", out string icon)
                && !string.IsNullOrWhiteSpace(icon)
                && !int.TryParse(icon, out _))
            {
                string resolved = this.ResolveBagItemDisplayName(icon, staticId);
                if (!this.IsPoorBagItemDisplayName(resolved, staticId))
                {
                    displayName = resolved;
                    return true;
                }
            }

            if (managedItem != null)
            {
                string descriptor = this.GetManagedBackpackItemDescriptor(managedItem);
                string matchKey = this.ExtractAutoSellMatchKeyFromDescriptor(descriptor);
                if (string.IsNullOrWhiteSpace(matchKey))
                {
                    matchKey = this.NormalizeAutoSellMatchKey(descriptor);
                }

                if (!string.IsNullOrWhiteSpace(matchKey))
                {
                    string resolved = this.ResolveBagItemDisplayName(matchKey, staticId);
                    if (!this.IsPoorBagItemDisplayName(resolved, staticId))
                    {
                        displayName = resolved;
                        return true;
                    }
                }
            }

            if (this.TryHomelandFarmTryGetEntityTableNameAuraMono(staticId, out string auraEntityName)
                && !this.IsPoorBagItemDisplayName(auraEntityName, staticId))
            {
                displayName = auraEntityName;
                return true;
            }

            if (this.TryGetResolvedFoodNameFromStaticId(staticId, out string tableName)
                && !this.IsPoorBagItemDisplayName(tableName, staticId))
            {
                displayName = tableName;
                return true;
            }

            if (this.TryGetRadarStaticIdIconKey(staticId, out string spriteKey) && !string.IsNullOrWhiteSpace(spriteKey))
            {
                string spriteLabel = this.GetAutoSellItemDisplayName(spriteKey);
                if (!this.IsPoorBagItemDisplayName(spriteLabel, staticId))
                {
                    displayName = spriteLabel;
                    return true;
                }
            }

            displayName = string.Empty;
            return false;
        }

        private string TryHomelandFarmFormatManagedInventoryItemLabel(object item, int staticId, int count, uint netId)
        {
            int step = 0;
            if (item != null)
            {
                if (!this.TryGetManagedInt32Member(item, "step", out step))
                {
                    this.TryGetManagedInt32Member(item, "Step", out step);
                }
            }

            string displayName = this.TryHomelandFarmResolveBackpackItemDisplayName(IntPtr.Zero, item, staticId, step, netId, out string resolvedName)
                ? resolvedName
                : this.TryHomelandFarmGetItemLabel(staticId);

            return displayName + " x" + count;
        }

        private string TryHomelandFarmFormatAuraInventoryItemLabel(IntPtr itemObj, int staticId, int count, uint netId)
        {
            int step = this.GetDirectBackpackItemStep(itemObj);
            string displayName = this.TryHomelandFarmResolveBackpackItemDisplayName(itemObj, null, staticId, step, netId, out string resolvedName)
                ? resolvedName
                : this.TryHomelandFarmGetItemLabel(staticId);

            return displayName + " x" + count;
        }

        private IEnumerable<int> GetHomelandFarmStorageTypeValues(HomelandFarmStorageSource source)
        {
            switch (source)
            {
                case HomelandFarmStorageSource.Backpack:
                    yield return HomelandFarmBackpackStorageType;
                    break;
                case HomelandFarmStorageSource.Warehouse:
                    yield return HomelandFarmWarehouseStorageType;
                    break;
                default:
                    yield return HomelandFarmBackpackStorageType;
                    yield return HomelandFarmWarehouseStorageType;
                    break;
            }
        }

        private object TryHomelandFarmGetStorageObject(int storageValue)
        {
            if (this.homelandFarmStorageTypeType != null && this.homelandFarmStorageTypeType.IsEnum)
            {
                try
                {
                    string storageName = storageValue == HomelandFarmWarehouseStorageType ? "Warehouse" : "Backpack";
                    return Enum.Parse(this.homelandFarmStorageTypeType, storageName);
                }
                catch
                {
                    return Enum.ToObject(this.homelandFarmStorageTypeType, storageValue);
                }
            }

            return storageValue;
        }

        private bool TryCollectHomelandFarmInventoryItemsManaged(
            HomelandFarmStorageSource source,
            Func<int, int, bool> accept,
            List<HomelandFarmInventoryItem> output,
            HashSet<uint> seenNetIds)
        {
            if (output == null || seenNetIds == null || accept == null || !this.EnsureHomelandFarmInventoryReflection())
            {
                return false;
            }

            object backPackObj = this.GetHomelandFarmBackPackSystemInstance();
            if (backPackObj == null || this.homelandFarmBackPackGetAllItemMethod == null)
            {
                return false;
            }

            bool added = false;
            foreach (int storageValue in this.GetHomelandFarmStorageTypeValues(source))
            {
                object storageArg = this.TryHomelandFarmGetStorageObject(storageValue);
                object itemListObj;
                try
                {
                    ParameterInfo[] parameters = this.homelandFarmBackPackGetAllItemMethod.GetParameters();
                    itemListObj = parameters.Length == 1
                        ? this.homelandFarmBackPackGetAllItemMethod.Invoke(backPackObj, new[] { storageArg })
                        : this.homelandFarmBackPackGetAllItemMethod.Invoke(backPackObj, null);
                }
                catch
                {
                    continue;
                }

                if (itemListObj == null)
                {
                    continue;
                }

                List<object> items = new List<object>();
                if (!this.TryEnumerateManagedCollectionItems(itemListObj, items) && itemListObj is IEnumerable enumerable)
                {
                    foreach (object item in enumerable)
                    {
                        if (item != null)
                        {
                            items.Add(item);
                        }
                    }
                }

                for (int i = 0; i < items.Count; i++)
                {
                    object item = items[i];
                    if (item == null)
                    {
                        continue;
                    }

                    if (!this.TryReadIntFromMember(item, "staticId", out int staticId))
                    {
                        this.TryReadIntFromMember(item, "StaticId", out staticId);
                    }

                    if (!this.TryReadUIntFromMember(item, "netId", out uint netId))
                    {
                        this.TryReadUIntFromMember(item, "NetId", out netId);
                    }

                    if (!this.TryReadIntFromMember(item, "count", out int count))
                    {
                        this.TryReadIntFromMember(item, "Count", out count);
                    }

                    if (!this.TryReadIntFromMember(item, "entityType", out int itemEntityType))
                    {
                        this.TryReadIntFromMember(item, "EntityType", out itemEntityType);
                    }

                    if (staticId <= 0 || netId == 0U || count <= 0 || !seenNetIds.Add(netId))
                    {
                        continue;
                    }

                    if (!accept(staticId, itemEntityType))
                    {
                        continue;
                    }

                    output.Add(new HomelandFarmInventoryItem
                    {
                        StaticId = staticId,
                        NetId = netId,
                        Count = count,
                        Label = this.TryHomelandFarmFormatManagedInventoryItemLabel(item, staticId, count, netId)
                    });
                    added = true;
                }
            }

            return added;
        }

        private unsafe bool TryCollectHomelandFarmInventoryItemsAura(
            HomelandFarmStorageSource source,
            Func<int, int, bool> accept,
            List<HomelandFarmInventoryItem> output,
            HashSet<uint> seenNetIds)
        {
            if (output == null || seenNetIds == null || accept == null)
            {
                return false;
            }

            if (!this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.BackPack.BackPackSystem", out IntPtr backPackSystemObj)
                || backPackSystemObj == IntPtr.Zero
                || !this.EnsureAuraMonoApiReady()
                || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr backPackClass = auraMonoObjectGetClass(backPackSystemObj);
            IntPtr getAllItemMethodWithStorage = this.FindAuraMonoMethodOnHierarchy(backPackClass, "GetAllItem", 1);
            IntPtr getAllItemMethodNoArgs = this.FindAuraMonoMethodOnHierarchy(backPackClass, "GetAllItem", 0);
            if (getAllItemMethodWithStorage == IntPtr.Zero && getAllItemMethodNoArgs == IntPtr.Zero)
            {
                return false;
            }

            bool added = false;
            foreach (int storageValue in this.GetHomelandFarmStorageTypeValues(source))
            {
                IntPtr exc = IntPtr.Zero;
                IntPtr itemsObj;
                if (getAllItemMethodWithStorage != IntPtr.Zero)
                {
                    IntPtr* args = stackalloc IntPtr[1];
                    args[0] = (IntPtr)(&storageValue);
                    itemsObj = auraMonoRuntimeInvoke(getAllItemMethodWithStorage, backPackSystemObj, (IntPtr)args, ref exc);
                }
                else
                {
                    itemsObj = auraMonoRuntimeInvoke(getAllItemMethodNoArgs, backPackSystemObj, IntPtr.Zero, ref exc);
                }

                if (exc != IntPtr.Zero || itemsObj == IntPtr.Zero)
                {
                    continue;
                }

                List<IntPtr> items = new List<IntPtr>();
                if (!this.TryEnumerateAuraMonoCollectionItems(itemsObj, items))
                {
                    continue;
                }

                for (int i = 0; i < items.Count; i++)
                {
                    IntPtr itemObj = items[i];
                    if (itemObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    if (!this.TryGetDirectBackpackItemStaticId(itemObj, out int staticId)
                        || !this.TryGetDirectBackpackItemNetId(itemObj, out uint netId)
                        || !this.TryGetDirectBackpackItemCount(itemObj, out int count)
                        || staticId <= 0
                        || netId == 0U
                        || count <= 0
                        || !seenNetIds.Add(netId))
                    {
                        continue;
                    }

                    int itemEntityType = 0;
                    this.TryGetDirectBackpackItemEntityType(itemObj, out itemEntityType);
                    if (!accept(staticId, itemEntityType))
                    {
                        continue;
                    }

                    output.Add(new HomelandFarmInventoryItem
                    {
                        StaticId = staticId,
                        NetId = netId,
                        Count = count,
                        Label = this.TryHomelandFarmFormatAuraInventoryItemLabel(itemObj, staticId, count, netId)
                    });
                    added = true;
                }
            }

            return added;
        }

        private List<HomelandFarmInventoryItem> ScanHomelandFarmCropSeeds(HomelandFarmStorageSource source)
        {
            List<HomelandFarmInventoryItem> results = new List<HomelandFarmInventoryItem>();
            HashSet<uint> seenNetIds = new HashSet<uint>();
            bool accept(int staticId, int itemEntityType) => this.TryHomelandFarmItemMatchesCropSeed(staticId, itemEntityType);

            if (!this.TryCollectHomelandFarmInventoryItemsManaged(source, accept, results, seenNetIds))
            {
                this.TryCollectHomelandFarmInventoryItemsAura(source, accept, results, seenNetIds);
            }

            results.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));
            this.homelandFarmSeedsCacheTime = Time.realtimeSinceStartup;
            this.HomelandFarmLog("Scanned crop seeds: " + results.Count + " (" + source + ").");
            return results;
        }

        private List<HomelandFarmInventoryItem> ScanHomelandFarmFertilizers(HomelandFarmStorageSource source)
        {
            List<HomelandFarmInventoryItem> results = new List<HomelandFarmInventoryItem>();
            HashSet<uint> seenNetIds = new HashSet<uint>();
            bool accept(int staticId, int itemEntityType) => this.TryHomelandFarmItemMatchesCropFertilizer(staticId, itemEntityType);

            if (!this.TryCollectHomelandFarmInventoryItemsManaged(source, accept, results, seenNetIds))
            {
                this.TryCollectHomelandFarmInventoryItemsAura(source, accept, results, seenNetIds);
            }

            results.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));
            this.homelandFarmFertilizersCacheTime = Time.realtimeSinceStartup;
            this.HomelandFarmLog("Scanned fertilizers: " + results.Count + " (" + source + ").");
            return results;
        }

        private List<HomelandFarmInventoryItem> ScanHomelandFarmSprinklers(HomelandFarmStorageSource source)
        {
            List<HomelandFarmInventoryItem> results = new List<HomelandFarmInventoryItem>();
            HashSet<uint> seenNetIds = new HashSet<uint>();
            bool accept(int staticId, int itemEntityType) => this.TryHomelandFarmItemMatchesSprinkler(staticId, itemEntityType);

            if (!this.TryCollectHomelandFarmInventoryItemsManaged(source, accept, results, seenNetIds))
            {
                this.TryCollectHomelandFarmInventoryItemsAura(source, accept, results, seenNetIds);
            }

            results.Sort((a, b) => string.Compare(a.Label, b.Label, StringComparison.OrdinalIgnoreCase));
            this.HomelandFarmLog("Scanned sprinklers: " + results.Count + " (" + source + ").");
            return results;
        }

        private void RefreshHomelandFarmSeeds()
        {
            this.homelandFarmScannedSeeds.Clear();
            this.homelandFarmScannedSeeds.AddRange(this.ScanHomelandFarmCropSeeds(this.homelandFarmSeedStorage));
            if (this.homelandFarmSelectedSeedIndex >= this.homelandFarmScannedSeeds.Count)
            {
                this.homelandFarmSelectedSeedIndex = Math.Max(0, this.homelandFarmScannedSeeds.Count - 1);
            }
        }

        private void RefreshHomelandFarmFertilizers()
        {
            this.homelandFarmScannedFertilizers.Clear();
            this.homelandFarmScannedFertilizers.AddRange(this.ScanHomelandFarmFertilizers(this.homelandFarmFertStorage));
            if (this.homelandFarmSelectedFertilizerIndex >= this.homelandFarmScannedFertilizers.Count)
            {
                this.homelandFarmSelectedFertilizerIndex = Math.Max(0, this.homelandFarmScannedFertilizers.Count - 1);
            }
        }

        // The Aura/native reflection path satisfies EnsureHomelandFarmReflectionReady without ever
        // resolving the MANAGED CropPlantPoint type / CropSeeding method (water/harvest/weed use the
        // native invoke path instead). Sow, however, must build a managed List<CropPlantPoint> for the
        // seeding call, so resolve those managed members lazily and independently here.
        private bool EnsureHomelandFarmSowManagedReflection()
        {
            if (this.homelandFarmCropPlantPointType != null && this.homelandFarmCropSeedingMethod != null)
            {
                return true;
            }

            this.TryEnsureHomelandFarmInteropAssembliesLoaded();

            if (this.homelandFarmCropPlantPointType == null)
            {
                this.homelandFarmCropPlantPointType = this.ResolveHomelandFarmManagedType(
                    "CropPlantPoint",
                    "XDT.Scene.Shared.Modules.Farm.CropPlantPoint",
                    "EcsClient.XDT.Scene.Shared.Modules.Farm.CropPlantPoint");
            }

            if (this.homelandFarmCropProtocolManagerType == null)
            {
                this.homelandFarmCropProtocolManagerType = this.ResolveHomelandFarmManagedType(
                    "CropProtocolManager",
                    "XDTDataAndProtocol.ProtocolService.Plant.CropProtocolManager");
            }

            if (this.homelandFarmCropSeedingMethod == null)
            {
                this.homelandFarmCropSeedingMethod = this.ResolveHomelandFarmCropSeedingMethod();
            }

            bool ready = this.homelandFarmCropPlantPointType != null && this.homelandFarmCropSeedingMethod != null;
            if (!ready && !this.homelandFarmSowManagedReflectionAttempted)
            {
                this.homelandFarmSowManagedReflectionAttempted = true;
                this.HomelandFarmLog("Sow managed reflection: cropPlantPoint=" + (this.homelandFarmCropPlantPointType != null)
                    + " cropProtocol=" + (this.homelandFarmCropProtocolManagerType != null)
                    + " seedingMethod=" + (this.homelandFarmCropSeedingMethod != null) + ".");
            }

            return ready;
        }

        private object TryHomelandFarmMaterializeCropPlantPoint(object point)
        {
            if (point == null)
            {
                return null;
            }

            if (point is HomelandFarmCropPlantPointData data)
            {
                return this.CreateHomelandFarmCropPlantPoint(data.Pos, data.Angle, data.LevelObjectNetId, data.PlanterNetId);
            }

            return point;
        }

        // Plain data carrier used when the managed CropPlantPoint type is unavailable (native-only
        // builds). TryHomelandFarmSow detects these and constructs the native struct list instead.
        private sealed class HomelandFarmCropPlantPointData
        {
            public Vector3 Pos;
            public int Angle;
            public ulong LevelObjectNetId;
            public uint PlanterNetId;
        }

        private object CreateHomelandFarmCropPlantPoint(Vector3 pos, int angle, ulong levelObjectNetId)
        {
            return this.CreateHomelandFarmCropPlantPoint(pos, angle, levelObjectNetId, 0U);
        }

        private object CreateHomelandFarmCropPlantPoint(Vector3 pos, int angle, ulong levelObjectNetId, uint planterNetId)
        {
            pos = HomelandFarmNormalizeCropSowFieldLocalPos(pos);
            if (this.homelandFarmCropPlantPointType == null)
            {
                this.EnsureHomelandFarmSowManagedReflection();
            }

            if (this.homelandFarmCropPlantPointType == null)
            {
                // Managed type unavailable: return a data carrier so sow can use the native path.
                return new HomelandFarmCropPlantPointData
                {
                    Pos = pos,
                    Angle = angle,
                    LevelObjectNetId = levelObjectNetId,
                    PlanterNetId = planterNetId
                };
            }

            try
            {
                object point = Activator.CreateInstance(this.homelandFarmCropPlantPointType);
                if (point == null)
                {
                    this.HomelandFarmLog("CropPlantPoint create: Activator returned null for type "
                        + this.homelandFarmCropPlantPointType.FullName + ".");
                    return null;
                }

                object pointRef = point;
                bool setPos = this.TrySetFieldValue(this.homelandFarmCropPlantPointType, ref pointRef, "pos", pos);
                bool setAngle = this.TrySetFieldValue(this.homelandFarmCropPlantPointType, ref pointRef, "angle", angle);
                bool setNetId = this.TrySetFieldValue(this.homelandFarmCropPlantPointType, ref pointRef, "levelObjectNetId", levelObjectNetId);
                bool setPlanter = planterNetId != 0U
                    && (this.TrySetFieldValue(this.homelandFarmCropPlantPointType, ref pointRef, "planterNetId", planterNetId)
                        || this.TrySetFieldValue(this.homelandFarmCropPlantPointType, ref pointRef, "PlanterNetId", planterNetId));
                if (!setPos || !setNetId)
                {
                    this.HomelandFarmLog("CropPlantPoint create: field set pos=" + setPos
                        + " angle=" + setAngle + " levelObjectNetId=" + setNetId
                        + " planterNetId=" + setPlanter
                        + " on " + this.homelandFarmCropPlantPointType.FullName + ".");
                }

                return pointRef ?? point;
            }
            catch (Exception ex)
            {
                this.HomelandFarmLog("CropPlantPoint create threw: " + ex.GetType().Name + ": " + ex.Message);
                return null;
            }
        }

        private bool TryHomelandFarmTryReadPlanterAngle(uint netId, out int angle)
        {
            angle = 0;
            if (this.TryHomelandFarmGetComponentData(this.homelandFarmLevelEntityComponentDataType, netId, out object levelEntityData, out _, "LevelEntityComponentData")
                && levelEntityData != null)
            {
                if (this.TryReadManagedInt32Member(levelEntityData, "angle", out int dataAngle))
                {
                    angle = dataAngle;
                    return true;
                }

                if (this.TryReadManagedInt32Member(levelEntityData, "Angle", out dataAngle))
                {
                    angle = dataAngle;
                    return true;
                }

                if (this.TryGetObjectMember(levelEntityData, "rotation", out object rotationObj) && rotationObj != null)
                {
                    try
                    {
                        Vector3 euler = this.TryGetVector3FromObject(rotationObj);
                        angle = Mathf.RoundToInt(euler.y);
                        return true;
                    }
                    catch
                    {
                    }
                }
            }

            return false;
        }

        private Vector3 TryGetVector3FromObject(object value)
        {
            if (value is Vector3 vector3)
            {
                return vector3;
            }

            if (value != null && value.GetType().Name.IndexOf("Vector3", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                float x = 0f;
                float y = 0f;
                float z = 0f;
                this.TryReadFloatMember(value, "x", out x);
                this.TryReadFloatMember(value, "y", out y);
                this.TryReadFloatMember(value, "z", out z);
                return new Vector3(x, y, z);
            }

            return Vector3.zero;
        }

        private bool TryHomelandFarmTryReadAuraDataFirstUlong(IntPtr dataHandle, out ulong value, params string[] listMembers)
        {
            value = 0UL;
            if (dataHandle == IntPtr.Zero || listMembers == null || listMembers.Length == 0
                || !this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return false;
            }

            IntPtr resolved = this.TryHomelandFarmResolveAuraComponentDataHandle(dataHandle);
            if (resolved == IntPtr.Zero)
            {
                resolved = dataHandle;
            }

            for (int i = 0; i < listMembers.Length; i++)
            {
                if (!this.TryGetMonoObjectMember(resolved, listMembers[i], out IntPtr listObj) || listObj == IntPtr.Zero)
                {
                    continue;
                }

                List<IntPtr> items = new List<IntPtr>();
                if (!this.TryEnumerateAuraMonoCollectionItems(listObj, items) || items.Count <= 0)
                {
                    continue;
                }

                for (int j = 0; j < items.Count; j++)
                {
                    ulong itemValue = this.TryReadMonoUnsignedIntegral(items[j]);
                    if (itemValue != 0UL)
                    {
                        value = itemValue;
                        return true;
                    }
                }
            }

            return false;
        }

        private static ulong TryHomelandFarmEncodeLevelObjectId(uint ownerNetId, int slot)
        {
            if (ownerNetId == 0U)
            {
                return 0UL;
            }

            return (ulong)ownerNetId | ((ulong)(uint)slot << 32);
        }

        private static uint HomelandFarmDecodePlanterNetIdFromLevelObjectId(ulong levelObjectNetId)
        {
            return (uint)(levelObjectNetId & 0xFFFFFFFFUL);
        }

        private static bool HomelandFarmPutZoneFlagsIncludeCropland(int flags)
        {
            return (flags & HomelandFarmPutZoneFlagCropland) != 0;
        }

        // Crop-box sow zones use PutZoneFlags.Normal (0) on the box entity; Cropland is for field
        // tiles. IsPutable() accepts Normal zones for any seed mask (PutZoneExtension.cs).
        private static int HomelandFarmScoreSowPutZoneFlags(int flags, bool flagsReadOk)
        {
            if (!flagsReadOk)
            {
                return 1;
            }

            if (flags == 0)
            {
                return 10;
            }

            if (HomelandFarmPutZoneFlagsIncludeCropland(flags))
            {
                return 20;
            }

            if ((flags & 1) != 0)
            {
                return 5;
            }

            return -1;
        }

        private static bool HomelandFarmIsPreferredSowPutZoneSlot(int candidateSlot, int bestSlot)
        {
            if (bestSlot < 0)
            {
                return true;
            }

            // SeedBagCommand → BoxArg.zoneElement.putZoneId from craft raycast uses slot 2 on crop boxes.
            if (candidateSlot == 2)
            {
                return bestSlot != 2;
            }

            if (bestSlot == 2)
            {
                return false;
            }

            return candidateSlot < bestSlot;
        }

        private static bool HomelandFarmTryUpdateBestSowPutZoneCandidate(
            ulong candidateNetId,
            int score,
            ref int bestScore,
            ref ulong bestNetId,
            ref int bestSlot)
        {
            if (candidateNetId == 0UL || score < 0)
            {
                return false;
            }

            int candidateSlot = HomelandFarmDecodeLevelObjectSlot(candidateNetId);
            if (bestNetId == 0UL
                || score > bestScore
                || (score == bestScore
                    && candidateSlot >= 0
                    && HomelandFarmIsPreferredSowPutZoneSlot(candidateSlot, bestSlot)))
            {
                bestScore = score;
                bestNetId = candidateNetId;
                bestSlot = candidateSlot;
                return true;
            }

            return false;
        }

        private static int HomelandFarmDecodeLevelObjectSlot(ulong levelObjectNetId)
        {
            return (int)(levelObjectNetId >> 32);
        }

        private bool TryHomelandFarmTryReadManagedLevelObjectPutZoneFlags(object levelObject, out int flags)
        {
            flags = 0;
            if (levelObject == null)
            {
                return false;
            }

            object config = this.TryGetManagedMemberValue(levelObject, "config");
            if (config == null)
            {
                object data = this.TryGetManagedMemberValue(levelObject, "_data");
                if (data != null)
                {
                    config = this.TryGetManagedMemberValue(data, "config");
                }
            }

            if (config == null)
            {
                return false;
            }

            Type configType = config.GetType();
            MethodInfo getFlagsMethod = configType.GetMethod("GetPutZoneFlags", BindingFlags.Public | BindingFlags.Instance);
            if (getFlagsMethod != null)
            {
                try
                {
                    object raw = getFlagsMethod.Invoke(config, null);
                    if (raw != null)
                    {
                        flags = Convert.ToInt32(raw);
                        return true;
                    }
                }
                catch
                {
                }
            }

            FieldInfo flagField = configType.GetField("flag", BindingFlags.Public | BindingFlags.Instance);
            if (flagField != null)
            {
                try
                {
                    object raw = flagField.GetValue(config);
                    if (raw != null)
                    {
                        flags = Convert.ToInt32(raw);
                        return true;
                    }
                }
                catch
                {
                }
            }

            return false;
        }

        private unsafe bool TryHomelandFarmTryReadAuraLevelObjectPutZoneFlags(IntPtr levelObjectObj, out int flags)
        {
            flags = 0;
            if (levelObjectObj == IntPtr.Zero)
            {
                return false;
            }

            if (this.TryGetMonoObjectMember(levelObjectObj, "config", out IntPtr configObj)
                && configObj != IntPtr.Zero
                && (this.TryGetMonoInt32Member(configObj, "flag", out flags)
                    || this.TryGetMonoInt32Member(configObj, "Flag", out flags)))
            {
                return true;
            }

            if (this.TryGetMonoObjectMember(levelObjectObj, "_data", out IntPtr dataObj)
                && dataObj != IntPtr.Zero
                && this.TryGetMonoObjectMember(dataObj, "config", out configObj)
                && configObj != IntPtr.Zero
                && (this.TryGetMonoInt32Member(configObj, "flag", out flags)
                    || this.TryGetMonoInt32Member(configObj, "Flag", out flags)))
            {
                return true;
            }

            return false;
        }

        private bool TryHomelandFarmTryReadLevelObjectOwnerNetIdManaged(object levelObject, ulong dictionaryKey, out uint ownerNetId)
        {
            ownerNetId = 0U;
            if (levelObject != null
                && (this.TryGetUIntMember(levelObject, "ownerNetId", out ownerNetId)
                    || this.TryGetUIntMember(levelObject, "OwnerNetId", out ownerNetId))
                && ownerNetId != 0U)
            {
                return true;
            }

            if (dictionaryKey != 0UL)
            {
                ownerNetId = (uint)(dictionaryKey & 0xFFFFFFFFUL);
                return ownerNetId != 0U;
            }

            return false;
        }

        private unsafe bool TryHomelandFarmTryReadAuraLevelObjectOwnerNetId(IntPtr levelObjectObj, ulong dictionaryKey, out uint ownerNetId)
        {
            ownerNetId = 0U;
            if (levelObjectObj != IntPtr.Zero)
            {
                if (this.TryGetMonoUInt32Member(levelObjectObj, "ownerNetId", out ownerNetId) && ownerNetId != 0U)
                {
                    return true;
                }

                if (this.TryGetMonoUInt32Member(levelObjectObj, "OwnerNetId", out ownerNetId) && ownerNetId != 0U)
                {
                    return true;
                }

                if (this.TryGetMonoObjectMember(levelObjectObj, "_data", out IntPtr dataObj)
                    && dataObj != IntPtr.Zero
                    && this.TryGetMonoUInt32Member(dataObj, "ownerNetId", out ownerNetId)
                    && ownerNetId != 0U)
                {
                    return true;
                }
            }

            if (dictionaryKey != 0UL)
            {
                ownerNetId = (uint)(dictionaryKey & 0xFFFFFFFFUL);
                return ownerNetId != 0U;
            }

            return false;
        }

        private unsafe bool TryHomelandFarmTryReadAuraDictionaryLevelObjectEntry(
            IntPtr entry,
            out ulong dictionaryKey,
            out IntPtr levelObjectObj)
        {
            dictionaryKey = 0UL;
            levelObjectObj = IntPtr.Zero;
            if (entry == IntPtr.Zero)
            {
                return false;
            }

            this.TryReadManagedUInt64Member(entry, "Key", out dictionaryKey);
            if (dictionaryKey == 0UL)
            {
                this.TryReadManagedUInt64Member(entry, "key", out dictionaryKey);
            }

            if ((!this.TryGetMonoObjectMember(entry, "Value", out levelObjectObj) || levelObjectObj == IntPtr.Zero)
                && (!this.TryGetMonoObjectMember(entry, "value", out levelObjectObj) || levelObjectObj == IntPtr.Zero)
                && (!this.TryGetMonoObjectMember(entry, "_value", out levelObjectObj) || levelObjectObj == IntPtr.Zero))
            {
                levelObjectObj = entry;
            }

            return levelObjectObj != IntPtr.Zero;
        }

        private bool TryHomelandFarmTryReadManagedDictionaryLevelObjectEntry(
            object entry,
            out ulong dictionaryKey,
            out object levelObject)
        {
            dictionaryKey = 0UL;
            levelObject = null;
            if (entry == null)
            {
                return false;
            }

            if (!this.TryReadManagedUInt64Member(entry, "Key", out dictionaryKey) || dictionaryKey == 0UL)
            {
                this.TryReadManagedUInt64Member(entry, "key", out dictionaryKey);
            }

            levelObject = this.TryGetManagedMemberValue(entry, "Value")
                ?? this.TryGetManagedMemberValue(entry, "value")
                ?? entry;
            return levelObject != null;
        }

        private unsafe bool TryHomelandFarmFindPlanterSowPutZoneAura(uint planterNetId, out ulong levelObjectNetId, out int slot)
        {
            levelObjectNetId = 0UL;
            slot = -1;
            if (planterNetId == 0U)
            {
                return false;
            }

            if (!this.TryResolveHomelandFarmAuraScanClasses(out _)
                || !this.EnsureAuraMonoApiReady()
                || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            if (!this.TryResolveAuraMonoLevelObjectManager(out IntPtr managerObj, out _, out _)
                || managerObj == IntPtr.Zero)
            {
                return false;
            }

            IntPtr dictionaryObj = IntPtr.Zero;
            if ((!this.TryGetMonoObjectMember(managerObj, "_dictionary", out dictionaryObj) || dictionaryObj == IntPtr.Zero)
                && (!this.TryGetMonoObjectMember(managerObj, "dictionary", out dictionaryObj) || dictionaryObj == IntPtr.Zero))
            {
                return false;
            }

            List<IntPtr> entries = new List<IntPtr>();
            if (!this.TryEnumerateAuraMonoCollectionItems(dictionaryObj, entries) || entries.Count <= 0)
            {
                return false;
            }

            int bestScore = -1;
            ulong bestNetId = 0UL;
            int bestSlot = -1;
            for (int i = 0; i < entries.Count; i++)
            {
                if (!this.TryHomelandFarmTryReadAuraDictionaryLevelObjectEntry(entries[i], out ulong dictionaryKey, out IntPtr levelObjectObj))
                {
                    continue;
                }

                if (!this.TryHomelandFarmTryReadAuraLevelObjectOwnerNetId(levelObjectObj, dictionaryKey, out uint ownerNetId)
                    || ownerNetId != planterNetId)
                {
                    continue;
                }

                bool flagsReadOk = this.TryHomelandFarmTryReadAuraLevelObjectPutZoneFlags(levelObjectObj, out int flags);
                int score = HomelandFarmScoreSowPutZoneFlags(flags, flagsReadOk);
                ulong candidateNetId = dictionaryKey;
                if (candidateNetId == 0UL && this.TryGetAuraLevelObjectNetId(levelObjectObj, out ulong netIdFromObject))
                {
                    candidateNetId = netIdFromObject;
                }

                HomelandFarmTryUpdateBestSowPutZoneCandidate(candidateNetId, score, ref bestScore, ref bestNetId, ref bestSlot);
            }

            if (bestNetId == 0UL)
            {
                return false;
            }

            levelObjectNetId = bestNetId;
            slot = bestSlot;
            return true;
        }

        private bool TryHomelandFarmFindPlanterSowPutZoneManaged(uint planterNetId, out ulong levelObjectNetId, out int slot)
        {
            levelObjectNetId = 0UL;
            slot = -1;
            if (planterNetId == 0U)
            {
                return false;
            }

            try
            {
                Type levelObjectManagerType = this.FindLevelObjectManagerRuntimeType();
                if (levelObjectManagerType == null)
                {
                    return false;
                }

                PropertyInfo instanceProperty = levelObjectManagerType.GetProperty(
                    "Instance",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                object levelObjectManager = instanceProperty != null ? instanceProperty.GetValue(null, null) : null;
                if (levelObjectManager == null)
                {
                    return false;
                }

                object dictionaryObj = this.TryGetManagedMemberValue(levelObjectManager, "_dictionary")
                    ?? this.TryGetManagedMemberValue(levelObjectManager, "dictionary");
                if (!(dictionaryObj is IEnumerable enumerable))
                {
                    return false;
                }

                int bestScore = -1;
                ulong bestNetId = 0UL;
                int bestSlot = -1;
                foreach (object entry in enumerable)
                {
                    if (!this.TryHomelandFarmTryReadManagedDictionaryLevelObjectEntry(entry, out ulong dictionaryKey, out object levelObject)
                        || levelObject == null)
                    {
                        continue;
                    }

                    if (!this.TryHomelandFarmTryReadLevelObjectOwnerNetIdManaged(levelObject, dictionaryKey, out uint ownerNetId)
                        || ownerNetId != planterNetId)
                    {
                        continue;
                    }

                    bool flagsReadOk = this.TryHomelandFarmTryReadManagedLevelObjectPutZoneFlags(levelObject, out int flags);
                    int score = HomelandFarmScoreSowPutZoneFlags(flags, flagsReadOk);
                    ulong candidateNetId = dictionaryKey;
                    if (candidateNetId == 0UL && this.TryGetAuraLevelObjectNetId(levelObject, out ulong netIdFromObject))
                    {
                        candidateNetId = netIdFromObject;
                    }

                    HomelandFarmTryUpdateBestSowPutZoneCandidate(candidateNetId, score, ref bestScore, ref bestNetId, ref bestSlot);
                }

                if (bestNetId == 0UL)
                {
                    return false;
                }

                levelObjectNetId = bestNetId;
                slot = bestSlot;
                return true;
            }
            catch
            {
            }

            return false;
        }

        private bool TryHomelandFarmTryGetCraftFieldNetId(out uint fieldNetId)
        {
            fieldNetId = 0U;
            if (this.TryGetManagedSelfPlayerObject(out object localPlayer, out _)
                && localPlayer != null)
            {
                if ((this.TryGetUIntMember(localPlayer, "inFieldNetId", out fieldNetId)
                        || this.TryGetUIntMember(localPlayer, "InFieldNetId", out fieldNetId))
                    && fieldNetId != 0U)
                {
                    return true;
                }
            }

            if (this.EnsureAuraMonoApiReady() && this.AttachAuraMonoThread())
            {
                string[] fieldNetIdMembers = { "inFieldNetId", "InFieldNetId" };
                if (this.TryHomelandFarmTryReadAuraLocalPlayerUIntField(fieldNetIdMembers, out fieldNetId, out _)
                    && fieldNetId != 0U)
                {
                    return true;
                }

                if (this.TryGetHomelandFarmPlayerNetId(out uint playerNetId, out _)
                    && playerNetId != 0U
                    && this.TryGetAuraMonoEntityObjectByNetId(playerNetId, out IntPtr entityObj)
                    && entityObj != IntPtr.Zero)
                {
                    for (int i = 0; i < fieldNetIdMembers.Length; i++)
                    {
                        if (this.TryGetMonoUInt32Member(entityObj, fieldNetIdMembers[i], out fieldNetId) && fieldNetId != 0U)
                        {
                            return true;
                        }
                    }
                }
            }

            return this.TryHomelandFarmGetSelfPlayInFieldOwnerNetId(out fieldNetId) && fieldNetId != 0U;
        }

        private bool TryHomelandFarmEnsureAuraSowCraftContext(out string status)
        {
            status = string.Empty;
            if (this.homelandFarmAuraSowCraftContextResolved)
            {
                return this.homelandFarmAuraLevelObjectManagerGetLevelObjectMethod != IntPtr.Zero;
            }

            this.homelandFarmAuraSowCraftContextResolved = true;
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                status = "AuraMono unavailable.";
                return false;
            }

            if (!this.TryResolveAuraMonoLevelObjectManager(out IntPtr managerObj, out IntPtr managerClass, out status)
                || managerClass == IntPtr.Zero)
            {
                return false;
            }

            this.homelandFarmAuraLevelObjectManagerGetLevelObjectMethod = this.FindAuraMonoMethodOnHierarchy(
                managerClass,
                "GetLevelObject",
                2);
            this.homelandFarmAuraLevelObjectManagerGetLevelObjectArgCount = this.homelandFarmAuraLevelObjectManagerGetLevelObjectMethod != IntPtr.Zero ? 2 : 0;
            if (this.homelandFarmAuraLevelObjectManagerGetLevelObjectMethod == IntPtr.Zero)
            {
                status = "LevelObjectManager.GetLevelObject(uint,int) missing.";
                return false;
            }

            IntPtr entitiesClass = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.BaseSystem.EntitiesManager.Entities");
            if (entitiesClass == IntPtr.Zero)
            {
                entitiesClass = this.FindAuraMonoClassAcrossLoadedAssemblies(
                    "XDTLevelAndEntity.BaseSystem.EntitiesManager",
                    "Entities");
            }

            if (entitiesClass != IntPtr.Zero)
            {
                this.homelandFarmAuraEntitiesFieldSystemGetterMethod = this.FindAuraMonoMethodOnHierarchy(
                    entitiesClass,
                    "get_fieldSystem",
                    0);
            }

            IntPtr fieldSystemClass = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.GameplaySystem.CraftingSystem.FieldComponentSystem");
            if (fieldSystemClass == IntPtr.Zero)
            {
                fieldSystemClass = this.FindAuraMonoClassAcrossLoadedAssemblies(
                    "XDTLevelAndEntity.GameplaySystem.CraftingSystem",
                    "FieldComponentSystem");
            }

            if (fieldSystemClass != IntPtr.Zero)
            {
                this.homelandFarmAuraFieldComponentSystemGetFieldMethod = this.FindAuraMonoMethodOnHierarchy(
                    fieldSystemClass,
                    "GetField",
                    1);
            }

            return this.homelandFarmAuraLevelObjectManagerGetLevelObjectMethod != IntPtr.Zero;
        }

        private bool TryHomelandFarmTryLookupAuraLevelObjectByNetId(ulong levelObjectNetId, out IntPtr levelObjectObj, out string status)
        {
            levelObjectObj = IntPtr.Zero;
            status = "LevelObject dictionary lookup unavailable.";
            if (levelObjectNetId == 0UL)
            {
                status = "LevelObject netId missing.";
                return false;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return false;
            }

            if (!this.TryResolveAuraMonoLevelObjectManager(out IntPtr managerObj, out _, out status)
                || managerObj == IntPtr.Zero)
            {
                return false;
            }

            IntPtr dictionaryObj = IntPtr.Zero;
            if ((!this.TryGetMonoObjectMember(managerObj, "_dictionary", out dictionaryObj) || dictionaryObj == IntPtr.Zero)
                && (!this.TryGetMonoObjectMember(managerObj, "dictionary", out dictionaryObj) || dictionaryObj == IntPtr.Zero))
            {
                return false;
            }

            List<IntPtr> entries = new List<IntPtr>();
            if (!this.TryEnumerateAuraMonoCollectionItems(dictionaryObj, entries) || entries.Count <= 0)
            {
                return false;
            }

            for (int i = 0; i < entries.Count; i++)
            {
                if (!this.TryHomelandFarmTryReadAuraDictionaryLevelObjectEntry(
                        entries[i],
                        out ulong dictionaryKey,
                        out IntPtr candidateObj)
                    || candidateObj == IntPtr.Zero
                    || dictionaryKey != levelObjectNetId)
                {
                    continue;
                }

                levelObjectObj = candidateObj;
                status = "Dictionary lookup ok netId=" + levelObjectNetId + ".";
                return true;
            }

            status = "Dictionary miss netId=" + levelObjectNetId + ".";
            return false;
        }

        private unsafe bool TryHomelandFarmTryInvokeAuraGetLevelObject(ulong levelObjectNetId, out IntPtr levelObjectObj, out string status)
        {
            levelObjectObj = IntPtr.Zero;
            status = "GetLevelObject unavailable.";
            if (levelObjectNetId == 0UL)
            {
                status = "LevelObject netId missing.";
                return false;
            }

            if (this.TryHomelandFarmTryLookupAuraLevelObjectByNetId(levelObjectNetId, out levelObjectObj, out status)
                && levelObjectObj != IntPtr.Zero)
            {
                return true;
            }

            if (!this.TryHomelandFarmEnsureAuraSowCraftContext(out status)
                || this.homelandFarmAuraLevelObjectManagerGetLevelObjectMethod == IntPtr.Zero
                || this.homelandFarmAuraLevelObjectManagerGetLevelObjectArgCount != 2
                || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            if (!this.TryResolveAuraMonoLevelObjectManager(out IntPtr managerObj, out _, out status)
                || managerObj == IntPtr.Zero)
            {
                return false;
            }

            uint ownerId = (uint)(levelObjectNetId & 0xFFFFFFFFUL);
            int levelObjectId = HomelandFarmDecodeLevelObjectSlot(levelObjectNetId);
            if (ownerId == 0U || levelObjectId < 0)
            {
                status = "LevelObject id decode failed netId=" + levelObjectNetId + ".";
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[2];
            args[0] = (IntPtr)(&ownerId);
            args[1] = (IntPtr)(&levelObjectId);
            levelObjectObj = auraMonoRuntimeInvoke(
                this.homelandFarmAuraLevelObjectManagerGetLevelObjectMethod,
                managerObj,
                (IntPtr)args,
                ref exc);
            if (exc != IntPtr.Zero || levelObjectObj == IntPtr.Zero)
            {
                status = "GetLevelObject(uint,int) failed netId=" + levelObjectNetId + ".";
                return false;
            }

            status = "GetLevelObject(uint,int) ok netId=" + levelObjectNetId + ".";
            return true;
        }

        private unsafe bool TryHomelandFarmTryGetAuraLevelObjectWorldPose(
            IntPtr levelObjectObj,
            out Vector3 worldPosition,
            out Quaternion worldRotation)
        {
            worldPosition = Vector3.zero;
            worldRotation = Quaternion.identity;
            if (levelObjectObj == IntPtr.Zero || auraMonoObjectGetClass == null || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr classPtr = auraMonoObjectGetClass(levelObjectObj);
            if (classPtr != IntPtr.Zero)
            {
                foreach (string methodName in new[] { "get_position", "GetPosition" })
                {
                    IntPtr methodPtr = this.FindAuraMonoMethodOnHierarchy(classPtr, methodName, 0);
                    if (methodPtr == IntPtr.Zero)
                    {
                        continue;
                    }

                    IntPtr exc = IntPtr.Zero;
                    IntPtr boxed = auraMonoRuntimeInvoke(methodPtr, levelObjectObj, IntPtr.Zero, ref exc);
                    if (exc == IntPtr.Zero && boxed != IntPtr.Zero && auraMonoObjectUnbox != null)
                    {
                        IntPtr raw = auraMonoObjectUnbox(boxed);
                        if (raw != IntPtr.Zero)
                        {
                            worldPosition = *(Vector3*)raw;
                            break;
                        }
                    }
                }

                foreach (string methodName in new[] { "get_rotation", "GetRotation" })
                {
                    IntPtr methodPtr = this.FindAuraMonoMethodOnHierarchy(classPtr, methodName, 0);
                    if (methodPtr == IntPtr.Zero)
                    {
                        continue;
                    }

                    IntPtr exc = IntPtr.Zero;
                    IntPtr boxed = auraMonoRuntimeInvoke(methodPtr, levelObjectObj, IntPtr.Zero, ref exc);
                    if (exc == IntPtr.Zero && boxed != IntPtr.Zero && auraMonoObjectUnbox != null)
                    {
                        IntPtr raw = auraMonoObjectUnbox(boxed);
                        if (raw != IntPtr.Zero)
                        {
                            worldRotation = *(Quaternion*)raw;
                            break;
                        }
                    }
                }
            }

            if (worldPosition == Vector3.zero
                && this.TryGetMonoObjectMember(levelObjectObj, "_hierarchy", out IntPtr hierarchyObj)
                && hierarchyObj != IntPtr.Zero)
            {
                this.TryGetMonoVector3Member(hierarchyObj, "position", out worldPosition);
                if (!this.TryGetMonoObjectMember(hierarchyObj, "rotation", out IntPtr rotObj) || rotObj == IntPtr.Zero)
                {
                    this.TryGetMonoVector3Member(hierarchyObj, "eulerAngles", out Vector3 euler);
                    if (euler != Vector3.zero)
                    {
                        worldRotation = Quaternion.Euler(euler);
                    }
                }
                else if (auraMonoObjectUnbox != null)
                {
                    IntPtr rawRot = auraMonoObjectUnbox(rotObj);
                    if (rawRot != IntPtr.Zero)
                    {
                        worldRotation = *(Quaternion*)rawRot;
                    }
                }
            }

            return worldPosition != Vector3.zero;
        }

        private unsafe bool TryHomelandFarmTryGetFieldCraftMatricesAura(
            uint fieldNetId,
            out Matrix4x4 localToWorld,
            out Matrix4x4 worldToLocal)
        {
            localToWorld = Matrix4x4.identity;
            worldToLocal = Matrix4x4.identity;
            if (fieldNetId == 0U
                || !this.TryHomelandFarmEnsureAuraSowCraftContext(out _)
                || this.homelandFarmAuraEntitiesFieldSystemGetterMethod == IntPtr.Zero
                || this.homelandFarmAuraFieldComponentSystemGetFieldMethod == IntPtr.Zero
                || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr fieldSystemObj = auraMonoRuntimeInvoke(
                this.homelandFarmAuraEntitiesFieldSystemGetterMethod,
                IntPtr.Zero,
                IntPtr.Zero,
                ref exc);
            if (exc != IntPtr.Zero || fieldSystemObj == IntPtr.Zero)
            {
                return false;
            }

            exc = IntPtr.Zero;
            IntPtr* getFieldArgs = stackalloc IntPtr[1];
            getFieldArgs[0] = (IntPtr)(&fieldNetId);
            IntPtr fieldComponentObj = auraMonoRuntimeInvoke(
                this.homelandFarmAuraFieldComponentSystemGetFieldMethod,
                fieldSystemObj,
                (IntPtr)getFieldArgs,
                ref exc);
            if (exc != IntPtr.Zero || fieldComponentObj == IntPtr.Zero)
            {
                return false;
            }

            IntPtr buildWorldObj = IntPtr.Zero;
            if (!this.TryGetMonoObjectMember(fieldComponentObj, "buildWorld", out buildWorldObj)
                || buildWorldObj == IntPtr.Zero)
            {
                this.TryGetMonoObjectMember(fieldComponentObj, "_buildWorld", out buildWorldObj);
            }

            if (buildWorldObj == IntPtr.Zero)
            {
                IntPtr fieldClass = auraMonoObjectGetClass(fieldComponentObj);
                IntPtr getBuildWorldMethod = fieldClass != IntPtr.Zero
                    ? this.FindAuraMonoMethodOnHierarchy(fieldClass, "get_buildWorld", 0)
                    : IntPtr.Zero;
                if (getBuildWorldMethod != IntPtr.Zero)
                {
                    exc = IntPtr.Zero;
                    buildWorldObj = auraMonoRuntimeInvoke(getBuildWorldMethod, fieldComponentObj, IntPtr.Zero, ref exc);
                }
            }

            if (buildWorldObj == IntPtr.Zero)
            {
                return false;
            }

            if (!this.TryGetMonoMatrix4x4Member(buildWorldObj, "worldToLocal", out worldToLocal))
            {
                this.TryGetMonoMatrix4x4Member(buildWorldObj, "_worldToLocal", out worldToLocal);
            }

            if (!this.TryGetMonoMatrix4x4Member(buildWorldObj, "localToWorld", out localToWorld))
            {
                this.TryGetMonoMatrix4x4Member(buildWorldObj, "_localToWorld", out localToWorld);
            }

            if (worldToLocal == Matrix4x4.identity && localToWorld != Matrix4x4.identity)
            {
                worldToLocal = localToWorld.inverse;
            }

            return worldToLocal != Matrix4x4.identity || localToWorld != Matrix4x4.identity;
        }

        private bool TryHomelandFarmTryGetFieldCraftMatrices(
            uint fieldNetId,
            out Matrix4x4 localToWorld,
            out Matrix4x4 worldToLocal)
        {
            localToWorld = Matrix4x4.identity;
            worldToLocal = Matrix4x4.identity;
            if (fieldNetId == 0U)
            {
                return false;
            }

            if (this.TryHomelandFarmTryGetFieldCraftMatricesAura(fieldNetId, out localToWorld, out worldToLocal))
            {
                return true;
            }

            if (this.TryHomelandFarmTryGetFieldWorldToLocalMatrix(fieldNetId, out worldToLocal))
            {
                if (worldToLocal != Matrix4x4.identity)
                {
                    localToWorld = worldToLocal.inverse;
                }

                return true;
            }

            return false;
        }

        // Matches BuildSingle.GenConfirmOption: worldToLocal * root world pose, then ReducePrecision.
        private bool TryHomelandFarmTryConvertWorldPoseToFieldLocalSow(
            uint fieldNetId,
            Vector3 worldPosition,
            Quaternion worldRotation,
            out Vector3 fieldLocalPos,
            out int angleY)
        {
            fieldLocalPos = Vector3.zero;
            angleY = 0;
            if (fieldNetId == 0U || worldPosition == Vector3.zero)
            {
                return false;
            }

            if (!this.TryHomelandFarmTryGetFieldCraftMatrices(fieldNetId, out Matrix4x4 localToWorld, out Matrix4x4 worldToLocal))
            {
                return false;
            }

            fieldLocalPos = HomelandFarmReduceCraftPrecision(worldToLocal.MultiplyPoint(worldPosition));
            Quaternion fieldLocalRotation = Quaternion.Inverse(localToWorld.rotation) * worldRotation;
            angleY = HomelandFarmQuantizeFieldLocalSowAngleY(fieldLocalRotation);
            return fieldLocalPos != Vector3.zero;
        }

        private unsafe bool TryHomelandFarmTryGetAuraPutZoneRectMatrix(IntPtr levelObjectObj, out Matrix4x4 rectMatrix)
        {
            rectMatrix = Matrix4x4.identity;
            if (levelObjectObj == IntPtr.Zero)
            {
                return false;
            }

            if (this.TryGetMonoObjectMember(levelObjectObj, "_collision", out IntPtr collisionObj)
                && collisionObj != IntPtr.Zero)
            {
                if (this.TryGetMonoMatrix4x4Member(collisionObj, "rectMatrix", out rectMatrix)
                    || this.TryGetMonoMatrix4x4Member(collisionObj, "_rectMatrix", out rectMatrix))
                {
                    return rectMatrix != Matrix4x4.identity;
                }
            }

            IntPtr classPtr = auraMonoObjectGetClass != null ? auraMonoObjectGetClass(levelObjectObj) : IntPtr.Zero;
            if (classPtr == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr getRectMatrixMethod = this.FindAuraMonoMethodOnHierarchy(classPtr, "get_rectMatrix", 0);
            if (getRectMatrixMethod == IntPtr.Zero)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr boxed = auraMonoRuntimeInvoke(getRectMatrixMethod, levelObjectObj, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || boxed == IntPtr.Zero || auraMonoObjectUnbox == null)
            {
                return false;
            }

            IntPtr raw = auraMonoObjectUnbox(boxed);
            if (raw == IntPtr.Zero)
            {
                return false;
            }

            rectMatrix = Marshal.PtrToStructure<Matrix4x4>(raw);
            return rectMatrix != Matrix4x4.identity;
        }

        // CraftMode_Multiple OnEnterPlacing: camera yaw + 180 before alignment refines preview rotation.
        private unsafe bool TryHomelandFarmTryResolveSowPreviewWorldRotation(out Quaternion worldRotation)
        {
            worldRotation = Quaternion.identity;
            if (!this.TryHomelandFarmTryGetAuraLocalPlayerObject(out IntPtr playerObj, out _)
                || playerObj == IntPtr.Zero)
            {
                return false;
            }

            if (!this.TryGetMonoObjectMember(playerObj, "cameraComponent", out IntPtr cameraComponentObj)
                || cameraComponentObj == IntPtr.Zero)
            {
                return false;
            }

            IntPtr cameraTransformObj = IntPtr.Zero;
            if (!this.TryGetMonoObjectMember(cameraComponentObj, "cameraTransform", out cameraTransformObj)
                || cameraTransformObj == IntPtr.Zero)
            {
                this.TryGetMonoObjectMember(cameraComponentObj, "_cameraTransform", out cameraTransformObj);
            }

            if (cameraTransformObj == IntPtr.Zero)
            {
                return false;
            }

            if (!this.TryGetMonoVector3Member(cameraTransformObj, "eulerAngles", out Vector3 cameraEuler))
            {
                return false;
            }

            worldRotation = Quaternion.Euler(0f, cameraEuler.y + 180f, 0f);
            return true;
        }

        private bool TryHomelandFarmTryResolveSowSeedRootWorldPose(
            uint planterNetId,
            ulong putZoneId,
            out Vector3 worldPosition,
            out Quaternion worldRotation,
            out string status)
        {
            worldPosition = Vector3.zero;
            worldRotation = Quaternion.identity;
            status = "Sow seed root world pose unavailable.";
            if (planterNetId == 0U || putZoneId == 0UL)
            {
                return false;
            }

            // Prefer the CACHED entity world position. The previous primary path called
            // GetLevelObject(putZoneId) + read rectMatrix per box, holding raw mono pointers across
            // invokes; with mono_gc_disable unavailable on this build a GC mid-read randomly AVs
            // (the sow-all crash). The crop-box entity sits at the put-zone cell, so worldToLocal +
            // grid-snap + y-normalize downstream yield the same field-local cell without that call.
            if (this.TryHomelandFarmResolveFarmEntityPosition(planterNetId, out Vector3 entityWorldPos)
                && entityWorldPos != Vector3.zero)
            {
                worldPosition = entityWorldPos;
            }
            else if (this.TryHomelandFarmTryInvokeAuraGetLevelObject(putZoneId, out IntPtr putZoneObj, out status)
                && putZoneObj != IntPtr.Zero)
            {
                // Rare fallback (entity position unavailable): read the put-zone rect.
                if (this.TryHomelandFarmTryGetAuraPutZoneRectMatrix(putZoneObj, out Matrix4x4 rectMatrix))
                {
                    worldPosition = rectMatrix.MultiplyPoint(Vector3.zero);
                }
                else if (this.TryHomelandFarmTryGetAuraLevelObjectWorldPose(putZoneObj, out Vector3 putZonePos, out _)
                    && putZonePos != Vector3.zero)
                {
                    worldPosition = putZonePos;
                }
            }

            if (worldPosition == Vector3.zero)
            {
                status = "PutZone/entity world position unavailable planter=" + planterNetId + ".";
                return false;
            }

            this.TryHomelandFarmTryResolveSowPreviewWorldRotation(out worldRotation);

            status = "Seed root world pos=" + worldPosition + ".";
            return true;
        }

        // GenSimpleConfirmOption: worldToLocal * element root world position, then ReducePrecision.
        private bool TryHomelandFarmTryResolveSowFieldLocalPositionFromEntityWorld(
            uint netId,
            Vector3 worldPos,
            out Vector3 fieldLocalPos,
            out string status)
        {
            fieldLocalPos = Vector3.zero;
            status = "Craft field-local position unavailable.";
            if (netId == 0U || worldPos == Vector3.zero)
            {
                return false;
            }

            if (!this.TryHomelandFarmTryGetCraftFieldNetId(out uint fieldNetId) || fieldNetId == 0U)
            {
                this.TryHomelandFarmTryReadOwnerId(netId, out fieldNetId);
            }

            if (fieldNetId == 0U
                || !this.TryHomelandFarmTryGetFieldCraftMatrices(fieldNetId, out _, out Matrix4x4 worldToLocal))
            {
                status = "Field craft matrices unavailable fieldNetId=" + fieldNetId + ".";
                return false;
            }

            fieldLocalPos = HomelandFarmReduceCraftPrecision(worldToLocal.MultiplyPoint(worldPos));
            if (fieldLocalPos == Vector3.zero)
            {
                status = "Field-local position zero after craft conversion.";
                return false;
            }

            status = "Craft field-local pos=" + fieldLocalPos + " fieldNetId=" + fieldNetId + ".";
            return true;
        }

        // Approximates GenSimpleConfirmOption: putZone rect world pos, camera preview rot, field-local Y normalize.
        private bool TryHomelandFarmTryResolveSowPointFromCraftPutZone(
            uint planterNetId,
            ulong putZoneId,
            out Vector3 fieldLocalPos,
            out int angleY,
            out string status)
        {
            fieldLocalPos = Vector3.zero;
            angleY = 0;
            status = "Craft putZone sow pose unavailable.";
            if (planterNetId == 0U || putZoneId == 0UL)
            {
                return false;
            }

            if (!this.TryHomelandFarmTryResolveSowSeedRootWorldPose(
                    planterNetId,
                    putZoneId,
                    out Vector3 worldPos,
                    out Quaternion worldRot,
                    out status))
            {
                return false;
            }

            if (!this.TryHomelandFarmTryGetCraftFieldNetId(out uint fieldNetId) || fieldNetId == 0U)
            {
                this.TryHomelandFarmTryReadOwnerId(planterNetId, out fieldNetId);
            }

            if (fieldNetId == 0U
                || !this.TryHomelandFarmTryConvertWorldPoseToFieldLocalSow(
                    fieldNetId,
                    worldPos,
                    worldRot,
                    out fieldLocalPos,
                    out angleY))
            {
                status = "Field-local sow pose conversion failed fieldNetId=" + fieldNetId + ".";
                return false;
            }

            fieldLocalPos = HomelandFarmNormalizeCropSowFieldLocalPos(fieldLocalPos);

            this.TryHomelandFarmRememberPlanterSowAnchor(planterNetId, putZoneId, worldPos, worldRot);
            status = "Craft putZone pos=" + fieldLocalPos + " angle=" + angleY + " fieldNetId=" + fieldNetId + ".";
            return fieldLocalPos != Vector3.zero;
        }

        private bool TryHomelandFarmTryProbeValidatedSowPutZone(
            uint planterNetId,
            int slot,
            out ulong levelObjectNetId,
            out string status)
        {
            levelObjectNetId = 0UL;
            status = string.Empty;
            ulong candidate = TryHomelandFarmEncodeLevelObjectId(planterNetId, slot);
            if (candidate == 0UL)
            {
                return false;
            }

            if (!this.TryHomelandFarmTryInvokeAuraGetLevelObject(candidate, out IntPtr levelObjectObj, out status)
                || levelObjectObj == IntPtr.Zero)
            {
                return false;
            }

            if (!this.TryHomelandFarmTryReadAuraLevelObjectOwnerNetId(levelObjectObj, candidate, out uint ownerNetId)
                || ownerNetId != planterNetId)
            {
                status = "PutZone owner mismatch slot=" + slot + ".";
                return false;
            }

            bool flagsReadOk = this.TryHomelandFarmTryReadAuraLevelObjectPutZoneFlags(levelObjectObj, out int flags);
            if (HomelandFarmScoreSowPutZoneFlags(flags, flagsReadOk) < 0)
            {
                status = "PutZone flags rejected slot=" + slot + ".";
                return false;
            }

            levelObjectNetId = candidate;
            status = "validated slot=" + slot;
            return true;
        }

        // CropSeeding.levelObjectNetId must be the put-zone LevelObject on the crop box entity
        // (SeedBagCommand → OptionCreate.BoxArg.zoneElement.putZoneId), NOT
        // BuildItemData.linkLogicParentNetIds (field grid cell where the box sits).
        private bool TryHomelandFarmResolveCropBoxSowLevelObjectId(uint planterNetId, out ulong levelObjectNetId)
        {
            levelObjectNetId = 0UL;
            if (planterNetId == 0U)
            {
                return false;
            }

            if (this.homelandFarmResolvedPutZoneByPlanterNetId.TryGetValue(planterNetId, out ulong cachedPutZone)
                && cachedPutZone != 0UL)
            {
                levelObjectNetId = cachedPutZone;
                return true;
            }

            // Fast, crash-safe path FIRST: the crop-box put-zone is deterministically
            // encode(planter, slot=2). The caller already confirmed this is an owned crop box, so we
            // do NOT call GetLevelObject to validate here — that native call holds a raw mono pointer
            // and randomly AVs across a sow-all batch (mono_gc_disable is unavailable on this build).
            // If the put-zone were wrong the server simply rejects with InvalidPlantBox (no crash).
            ulong fastPutZone = TryHomelandFarmEncodeLevelObjectId(planterNetId, HomelandFarmCropBoxCraftPutZoneSlot);
            if (fastPutZone != 0UL)
            {
                levelObjectNetId = fastPutZone;
                this.homelandFarmResolvedPutZoneByPlanterNetId[planterNetId] = levelObjectNetId;
                return true;
            }

            int slot = -1;
            if (this.TryHomelandFarmFindPlanterSowPutZoneAura(planterNetId, out levelObjectNetId, out slot)
                && levelObjectNetId != 0UL
                && this.TryHomelandFarmValidateSowPutZoneLevelObject(levelObjectNetId))
            {
                this.homelandFarmResolvedPutZoneByPlanterNetId[planterNetId] = levelObjectNetId;
                return true;
            }

            levelObjectNetId = 0UL;
            if (this.TryHomelandFarmFindPlanterSowPutZoneManaged(planterNetId, out levelObjectNetId, out slot)
                && levelObjectNetId != 0UL
                && this.TryHomelandFarmValidateSowPutZoneLevelObject(levelObjectNetId))
            {
                this.homelandFarmResolvedPutZoneByPlanterNetId[planterNetId] = levelObjectNetId;
                return true;
            }

            levelObjectNetId = 0UL;
            int[] slotsToTry = { 1, 0, 2, 3, 4, 5, 6, 7, 8 };
            int bestScore = -1;
            ulong bestNetId = 0UL;
            int bestSlot = -1;
            for (int i = 0; i < slotsToTry.Length; i++)
            {
                if (!this.TryHomelandFarmTryProbeValidatedSowPutZone(
                        planterNetId,
                        slotsToTry[i],
                        out ulong candidate,
                        out _))
                {
                    continue;
                }

                if (!this.TryHomelandFarmTryInvokeAuraGetLevelObject(candidate, out IntPtr levelObjectObj, out _)
                    || levelObjectObj == IntPtr.Zero)
                {
                    continue;
                }

                bool flagsReadOk = this.TryHomelandFarmTryReadAuraLevelObjectPutZoneFlags(levelObjectObj, out int flags);
                int score = HomelandFarmScoreSowPutZoneFlags(flags, flagsReadOk);
                HomelandFarmTryUpdateBestSowPutZoneCandidate(candidate, score, ref bestScore, ref bestNetId, ref bestSlot);
            }

            if (bestNetId != 0UL)
            {
                levelObjectNetId = bestNetId;
                this.homelandFarmResolvedPutZoneByPlanterNetId[planterNetId] = levelObjectNetId;
                return true;
            }

            object managedLevelObject = null;
            for (int i = 0; i < slotsToTry.Length; i++)
            {
                ulong candidate = TryHomelandFarmEncodeLevelObjectId(planterNetId, slotsToTry[i]);
                if (candidate == 0UL)
                {
                    continue;
                }

                managedLevelObject = this.TryGetAuraLevelObject(candidate);
                if (managedLevelObject == null)
                {
                    continue;
                }

                if (!this.TryHomelandFarmTryReadLevelObjectOwnerNetIdManaged(managedLevelObject, candidate, out uint ownerNetId)
                    || ownerNetId != planterNetId)
                {
                    continue;
                }

                bool flagsReadOk = this.TryHomelandFarmTryReadManagedLevelObjectPutZoneFlags(managedLevelObject, out int flags);
                int score = HomelandFarmScoreSowPutZoneFlags(flags, flagsReadOk);
                HomelandFarmTryUpdateBestSowPutZoneCandidate(candidate, score, ref bestScore, ref bestNetId, ref bestSlot);
            }

            if (bestNetId != 0UL)
            {
                levelObjectNetId = bestNetId;
                this.homelandFarmResolvedPutZoneByPlanterNetId[planterNetId] = levelObjectNetId;
                return true;
            }

            return false;
        }

        private bool TryHomelandFarmValidateSowPutZoneLevelObject(ulong levelObjectNetId)
        {
            if (levelObjectNetId == 0UL)
            {
                return false;
            }

            if (this.TryHomelandFarmTryInvokeAuraGetLevelObject(levelObjectNetId, out IntPtr levelObjectObj, out _)
                && levelObjectObj != IntPtr.Zero)
            {
                return true;
            }

            return this.TryGetAuraLevelObject(levelObjectNetId) != null;
        }

        private bool TryHomelandFarmTryGetFieldWorldToLocalMatrix(uint fieldNetId, out Matrix4x4 worldToLocal)
        {
            worldToLocal = Matrix4x4.identity;
            if (fieldNetId == 0U)
            {
                return false;
            }

            try
            {
                Type entitiesType = this.FindLoadedType(
                    "XDTLevelAndEntity.BaseSystem.EntitiesManager.Entities",
                    "Entities");
                if (entitiesType != null)
                {
                    PropertyInfo fieldSystemProperty = entitiesType.GetProperty(
                        "fieldSystem",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    object fieldSystem = fieldSystemProperty != null ? fieldSystemProperty.GetValue(null, null) : null;
                    if (fieldSystem == null)
                    {
                        FieldInfo fieldSystemField = entitiesType.GetField(
                            "fieldSystem",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                        fieldSystem = fieldSystemField?.GetValue(null);
                    }

                    if (fieldSystem != null)
                    {
                        MethodInfo getFieldMethod = fieldSystem.GetType().GetMethod(
                            "GetField",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                            null,
                            new[] { typeof(uint) },
                            null);
                        object fieldComponent = getFieldMethod?.Invoke(fieldSystem, new object[] { fieldNetId });
                        if (fieldComponent != null)
                        {
                            object buildWorld = this.TryGetManagedMemberValue(fieldComponent, "buildWorld");
                            if (buildWorld != null
                                && this.TryGetObjectMember(buildWorld, "worldToLocal", out object matrixObj)
                                && matrixObj is Matrix4x4 matrix)
                            {
                                worldToLocal = matrix;
                                return true;
                            }
                        }
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        private bool TryGetMonoMatrix4x4Member(IntPtr obj, string memberName, out Matrix4x4 value)
        {
            value = Matrix4x4.identity;
            if (obj == IntPtr.Zero || string.IsNullOrEmpty(memberName))
            {
                return false;
            }

            if (this.TryGetMonoObjectMember(obj, memberName, out IntPtr boxed) && boxed != IntPtr.Zero && auraMonoObjectUnbox != null)
            {
                IntPtr raw = auraMonoObjectUnbox(boxed);
                if (raw != IntPtr.Zero)
                {
                    value = Marshal.PtrToStructure<Matrix4x4>(raw);
                    return true;
                }
            }

            return false;
        }

        private unsafe bool TryHomelandFarmTryWriteAuraMonoVector3Field(
            IntPtr targetObj,
            IntPtr fieldPtr,
            Vector3 value,
            out string status)
        {
            status = string.Empty;
            if (targetObj == IntPtr.Zero || fieldPtr == IntPtr.Zero || auraMonoFieldSetValue == null)
            {
                status = "AuraMono Vector3 field write unavailable.";
                return false;
            }

            auraMonoFieldSetValue(targetObj, fieldPtr, (IntPtr)(&value));
            if (auraMonoFieldGetValue != null)
            {
                Vector3 readBack = Vector3.zero;
                auraMonoFieldGetValue(targetObj, fieldPtr, (IntPtr)(&readBack));
                if ((readBack - value).sqrMagnitude <= 0.0001f)
                {
                    return true;
                }
            }

            IntPtr vector3Class = this.FindAuraMonoClassByFullName("UnityEngine.Vector3");
            if (vector3Class == IntPtr.Zero)
            {
                vector3Class = this.FindAuraMonoClassAcrossLoadedAssemblies("UnityEngine", "Vector3");
            }

            if (vector3Class == IntPtr.Zero || auraMonoClassGetFieldFromName == null || auraMonoObjectNew == null)
            {
                status = "CropPlantPoint pos field write failed (Vector3 class missing).";
                return false;
            }

            IntPtr xField = auraMonoClassGetFieldFromName(vector3Class, "x");
            IntPtr yField = auraMonoClassGetFieldFromName(vector3Class, "y");
            IntPtr zField = auraMonoClassGetFieldFromName(vector3Class, "z");
            if (xField == IntPtr.Zero || yField == IntPtr.Zero || zField == IntPtr.Zero)
            {
                status = "CropPlantPoint pos field write failed (Vector3 fields missing).";
                return false;
            }

            IntPtr vectorObj = auraMonoObjectNew(this.auraMonoRootDomain, vector3Class);
            if (vectorObj == IntPtr.Zero)
            {
                status = "CropPlantPoint pos field write failed (Vector3 alloc).";
                return false;
            }

            float x = value.x;
            float y = value.y;
            float z = value.z;
            auraMonoFieldSetValue(vectorObj, xField, (IntPtr)(&x));
            auraMonoFieldSetValue(vectorObj, yField, (IntPtr)(&y));
            auraMonoFieldSetValue(vectorObj, zField, (IntPtr)(&z));

            if (auraMonoObjectUnbox != null)
            {
                IntPtr rawVector = auraMonoObjectUnbox(vectorObj);
                if (rawVector != IntPtr.Zero)
                {
                    auraMonoFieldSetValue(targetObj, fieldPtr, rawVector);
                    if (auraMonoFieldGetValue != null)
                    {
                        Vector3 readBack = Vector3.zero;
                        auraMonoFieldGetValue(targetObj, fieldPtr, (IntPtr)(&readBack));
                        if ((readBack - value).sqrMagnitude <= 0.0001f)
                        {
                            return true;
                        }
                    }
                }
            }

            status = "CropPlantPoint pos field write failed (readback mismatch).";
            return false;
        }

        private bool TryHomelandFarmTryResolvePutZoneNetId(uint netId, out ulong putZoneNetId)
        {
            putZoneNetId = 0UL;
            if (netId == 0U)
            {
                return false;
            }

            string[] linkMembers = { "linkLogicParentNetIds", "LinkLogicParentNetIds", "linkLogicParentNetId", "putZoneId", "PutZoneId" };

            if (this.EnsureAuraMonoApiReady()
                && this.AttachAuraMonoThread()
                && this.TryHomelandFarmResolveAuraComponentData(netId, "BuildItemData", out IntPtr auraBuildDataHandle)
                && this.TryHomelandFarmTryReadAuraDataFirstUlong(auraBuildDataHandle, out putZoneNetId, linkMembers))
            {
                return true;
            }

            if (this.homelandFarmBuildComponentDataType == null)
            {
                this.homelandFarmBuildComponentDataType = this.FindLoadedType(
                    "XDTDataAndProtocol.ComponentsData.BuildItemData",
                    "BuildItemData",
                    "BuildComponentData",
                    "BuildComponent");
            }

            if (this.homelandFarmBuildComponentDataType != null
                && this.TryHomelandFarmGetComponentData(this.homelandFarmBuildComponentDataType, netId, out object buildData, out _, "BuildItemData")
                && buildData != null)
            {
                if (buildData is HomelandFarmAuraComponentData auraBuildData
                    && auraBuildData.Handle != IntPtr.Zero
                    && this.TryHomelandFarmTryReadAuraDataFirstUlong(auraBuildData.Handle, out putZoneNetId, linkMembers))
                {
                    return true;
                }

                for (int i = 0; i < linkMembers.Length; i++)
                {
                    if (!this.TryGetObjectMember(buildData, linkMembers[i], out object linksObj) || linksObj == null)
                    {
                        continue;
                    }

                    if (linksObj is IEnumerable enumerable)
                    {
                        foreach (object entry in enumerable)
                        {
                            if (entry == null)
                            {
                                continue;
                            }

                            if (entry is ulong ulongValue && ulongValue != 0UL)
                            {
                                putZoneNetId = ulongValue;
                                return true;
                            }

                            if (entry is uint uintValue && uintValue != 0U)
                            {
                                putZoneNetId = uintValue;
                                return true;
                            }

                            if (entry is int intValue && intValue > 0)
                            {
                                putZoneNetId = (ulong)intValue;
                                return true;
                            }
                        }
                    }
                    else if (this.TryReadManagedUInt64Member(linksObj, "value", out ulong singleValue) && singleValue != 0UL)
                    {
                        putZoneNetId = singleValue;
                        return true;
                    }
                    else if (this.TryGetUIntMember(linksObj, "value", out uint singleUint) && singleUint != 0U)
                    {
                        putZoneNetId = singleUint;
                        return true;
                    }
                }
            }

            if (this.TryHomelandFarmResolvePlanterPutZoneId(netId, out putZoneNetId) && putZoneNetId != 0UL)
            {
                return true;
            }

            return false;
        }

        private void TryHomelandFarmBuildOccupiedCropBoxNetIds(
            HashSet<uint> cropBoxNetIds,
            HashSet<uint> scanNetIds,
            HashSet<uint> occupiedOut)
        {
            if (occupiedOut == null || cropBoxNetIds == null)
            {
                return;
            }

            occupiedOut.Clear();
            foreach (uint boxNetId in cropBoxNetIds)
            {
                if (boxNetId != 0U && this.TryHomelandFarmCropBoxHasCrop(boxNetId, out _))
                {
                    occupiedOut.Add(boxNetId);
                }
            }

            foreach (uint pendingBoxNetId in this.homelandFarmAutoPendingSowBoxNetIds)
            {
                if (pendingBoxNetId != 0U && cropBoxNetIds.Contains(pendingBoxNetId))
                {
                    occupiedOut.Add(pendingBoxNetId);
                }
            }

            // Also run the position-match pass on AuraMono builds: CropBoxItemData link fields are
            // empty here even when a crop is growing, so TryHomelandFarmCropBoxHasCrop above misses
            // occupied boxes (occupied=0) and sow then targets planted boxes -> server InvalidPlantBox.
            if (scanNetIds != null && scanNetIds.Count > 0)
            {
                this.TryHomelandFarmMarkOccupiedCropBoxesByEntityPosition(cropBoxNetIds, scanNetIds, occupiedOut);
            }
        }

        // Best-effort: resolve sow put-zone anchors for every box so plant→box matching can use the
        // same world/field-local poses as CropSeeding (entity positions alone are unreliable here).
        private void TryHomelandFarmWarmPlanterSowAnchorsForCropBoxes(
            HashSet<uint> cropBoxNetIds,
            Dictionary<string, uint> sowFieldCellToBoxOut = null)
        {
            if (cropBoxNetIds == null || cropBoxNetIds.Count == 0)
            {
                return;
            }

            foreach (uint boxNetId in cropBoxNetIds)
            {
                if (boxNetId == 0U)
                {
                    continue;
                }

                if (!this.TryHomelandFarmResolveBoxFieldPlacement(boxNetId, out _, out Vector3 sowFieldLocal, out _))
                {
                    continue;
                }

                if (sowFieldCellToBoxOut != null && sowFieldLocal != Vector3.zero)
                {
                    sowFieldCellToBoxOut[HomelandFarmFieldCellKey(sowFieldLocal)] = boxNetId;
                }
            }
        }

        private bool TryHomelandFarmIsFarmCropOrPlantNetId(uint netId)
        {
            if (netId == 0U)
            {
                return false;
            }

            if (this.HomelandFarmPrefersAuraComponentData())
            {
                return this.TryHomelandFarmAuraEntityClassifyFarm(netId, out bool isCropBox, out bool isPlant, out bool isCrop)
                    && !isCropBox
                    && (isPlant || isCrop);
            }

            return this.TryHomelandFarmGetComponentData(this.homelandFarmCropItemDataType, netId, out _, out _, "CropItemData")
                || this.TryHomelandFarmGetComponentData(this.homelandFarmPlantItemDataType, netId, out _, out _, "PlantItemData");
        }

        // One crop's claim on a box during injective occupancy assignment.
        private struct HomelandFarmCropOccupancyCandidate
        {
            public uint ExactBoxNetId;
            public int Tier;
            public Vector3 CropPos;
            public bool HasPos;
            public bool Resolved;
        }

        // Crop plants are separate entities from crop boxes; link fields on CropBoxItemData are often
        // empty even when a crop is growing. Match CropItemData / PlantItemData entities to boxes by
        // sow field cell, planter anchors, or world proximity.
        private void TryHomelandFarmMarkOccupiedCropBoxesByEntityPosition(
            HashSet<uint> cropBoxNetIds,
            HashSet<uint> scanNetIds,
            HashSet<uint> occupiedOut)
        {
            if (occupiedOut == null || cropBoxNetIds == null || scanNetIds == null || cropBoxNetIds.Count == 0)
            {
                return;
            }

            Dictionary<string, uint> boxBySowFieldCell = new Dictionary<string, uint>(cropBoxNetIds.Count);
            this.TryHomelandFarmWarmPlanterSowAnchorsForCropBoxes(cropBoxNetIds, boxBySowFieldCell);

            Dictionary<string, uint> boxByFieldCell = new Dictionary<string, uint>(cropBoxNetIds.Count);
            Dictionary<uint, Vector3> boxAnchorWorldPos = new Dictionary<uint, Vector3>(cropBoxNetIds.Count);
            Dictionary<uint, Vector3> boxWorldPos = new Dictionary<uint, Vector3>(cropBoxNetIds.Count);
            foreach (uint boxNetId in cropBoxNetIds)
            {
                if (boxNetId == 0U)
                {
                    continue;
                }

                if (this.TryHomelandFarmResolveEntityFieldLocalPosition(boxNetId, out Vector3 boxFieldLocal))
                {
                    boxByFieldCell[HomelandFarmFieldCellKey(boxFieldLocal)] = boxNetId;
                }

                if (this.homelandFarmPlanterSowAnchorByNetId.TryGetValue(boxNetId, out HomelandFarmPlanterSowAnchor sowAnchor)
                    && sowAnchor != null
                    && sowAnchor.WorldPosition != Vector3.zero)
                {
                    boxAnchorWorldPos[boxNetId] = sowAnchor.WorldPosition;
                }

                if (this.TryHomelandFarmResolveFarmEntityPosition(boxNetId, out Vector3 boxPos) && boxPos != Vector3.zero)
                {
                    boxWorldPos[boxNetId] = boxPos;
                }
            }

            // Collect every crop that occupies a box (demand), each with its best EXACT (cell-key)
            // box and world position. Assignment below is injective: every crop claims a DISTINCT
            // box, so the occupied count can never fall below the crop count. Previously two crops
            // could collapse onto one box (HashSet) or one freshly-sown crop landing just outside
            // the 0.35m / 3D-cell tolerance matched nothing -> its box stayed flagged empty -> sow
            // targeted a planted box and the server rejected the whole command (MaxPlantCountLimit).
            // The direct CropBoxItemData-link signal is already applied by the pre-pass in
            // TryHomelandFarmBuildOccupiedCropBoxNetIds, so it is intentionally not repeated here.
            List<HomelandFarmCropOccupancyCandidate> demand = new List<HomelandFarmCropOccupancyCandidate>();
            foreach (uint cropNetId in scanNetIds)
            {
                if (cropNetId == 0U || cropBoxNetIds.Contains(cropNetId))
                {
                    continue;
                }

                if (!this.TryHomelandFarmIsFarmCropOrPlantNetId(cropNetId))
                {
                    continue;
                }

                if (this.TryHomelandFarmIsDiscardedAutoFarmCropNetId(cropNetId)
                    || this.TryHomelandFarmIsOrphanPlantOnlyWithoutCropData(cropNetId, scanNetIds))
                {
                    continue;
                }

                uint exactBox = 0U;
                int tier = int.MaxValue;
                if (this.TryHomelandFarmResolveEntityFieldLocalPosition(cropNetId, out Vector3 cropFieldLocal))
                {
                    string cropCellKey = HomelandFarmFieldCellKey(cropFieldLocal);
                    if (boxBySowFieldCell.TryGetValue(cropCellKey, out uint sowMatchedBoxNetId) && sowMatchedBoxNetId != 0U)
                    {
                        exactBox = sowMatchedBoxNetId;
                        tier = 1;
                    }
                    else if (boxByFieldCell.TryGetValue(cropCellKey, out uint matchedBoxNetId) && matchedBoxNetId != 0U)
                    {
                        exactBox = matchedBoxNetId;
                        tier = 2;
                    }
                }

                bool hasPos = this.TryHomelandFarmResolveFarmEntityPosition(cropNetId, out Vector3 cropPos)
                    && cropPos != Vector3.zero;

                demand.Add(new HomelandFarmCropOccupancyCandidate
                {
                    ExactBoxNetId = exactBox,
                    Tier = tier,
                    CropPos = cropPos,
                    HasPos = hasPos,
                    Resolved = false,
                });
            }

            // Pass 1: claim exact (cell-key) matches first, in confidence order. A cell maps to a
            // single box, so a crop with an exact box is accounted for even if that box was already
            // marked occupied by the link pre-pass (same logical box, no double counting).
            int exactCount = 0;
            demand.Sort((a, b) => a.Tier.CompareTo(b.Tier));
            for (int i = 0; i < demand.Count; i++)
            {
                HomelandFarmCropOccupancyCandidate c = demand[i];
                if (c.ExactBoxNetId != 0U)
                {
                    occupiedOut.Add(c.ExactBoxNetId);
                    c.Resolved = true;
                    demand[i] = c;
                    exactCount++;
                }
            }

            // Pass 2: each still-unresolved candidate claims the nearest UNCLAIMED box, but ONLY when
            // it sits within HomelandFarmCropBoxOccupancyMatchRadius of one (injective backup for a
            // crop whose cell-key was off). The radius gate is essential: the scan demand also carries
            // mature ground plants (flowers/trees) that are NOT in crop boxes; without it pass 2 would
            // hand them the remaining empty boxes and report the field full. Binding the tightest fits
            // first keeps each claim the closest free box. No unbounded last-resort: an entity far from
            // every box is not on a box.
            demand.Sort((a, b) =>
                HomelandFarmNearestBoxDistanceSq(a, boxAnchorWorldPos, boxWorldPos)
                    .CompareTo(HomelandFarmNearestBoxDistanceSq(b, boxAnchorWorldPos, boxWorldPos)));
            int fuzzyCount = 0;
            for (int i = 0; i < demand.Count; i++)
            {
                HomelandFarmCropOccupancyCandidate c = demand[i];
                if (c.Resolved)
                {
                    continue;
                }

                uint nearestBox = 0U;
                if (c.HasPos)
                {
                    float bestSq = HomelandFarmCropBoxOccupancyMatchRadius * HomelandFarmCropBoxOccupancyMatchRadius;
                    foreach (KeyValuePair<uint, Vector3> boxEntry in boxAnchorWorldPos)
                    {
                        if (occupiedOut.Contains(boxEntry.Key))
                        {
                            continue;
                        }

                        Vector3 delta = boxEntry.Value - c.CropPos;
                        delta.y = 0f;
                        float sq = delta.sqrMagnitude;
                        if (sq < bestSq)
                        {
                            bestSq = sq;
                            nearestBox = boxEntry.Key;
                        }
                    }

                    foreach (KeyValuePair<uint, Vector3> boxEntry in boxWorldPos)
                    {
                        if (occupiedOut.Contains(boxEntry.Key))
                        {
                            continue;
                        }

                        Vector3 delta = boxEntry.Value - c.CropPos;
                        delta.y = 0f;
                        float sq = delta.sqrMagnitude;
                        if (sq < bestSq)
                        {
                            bestSq = sq;
                            nearestBox = boxEntry.Key;
                        }
                    }
                }

                if (nearestBox != 0U)
                {
                    occupiedOut.Add(nearestBox);
                    fuzzyCount++;
                }
            }

            this.HomelandFarmLog("Occupancy assign: crops=" + demand.Count + " exact=" + exactCount
                + " fuzzy=" + fuzzyCount + " occupied=" + occupiedOut.Count + "/" + cropBoxNetIds.Count + ".");
        }

        // Smallest planar distance from a crop candidate to any crop box (claimed or not); used only
        // to order the injective pass so the tightest-fitting crops bind their box first.
        private static float HomelandFarmNearestBoxDistanceSq(
            HomelandFarmCropOccupancyCandidate candidate,
            Dictionary<uint, Vector3> boxAnchorWorldPos,
            Dictionary<uint, Vector3> boxWorldPos)
        {
            if (candidate.Resolved || !candidate.HasPos)
            {
                return float.MaxValue;
            }

            float bestSq = float.MaxValue;
            foreach (Vector3 boxPos in boxAnchorWorldPos.Values)
            {
                Vector3 delta = boxPos - candidate.CropPos;
                delta.y = 0f;
                float sq = delta.sqrMagnitude;
                if (sq < bestSq)
                {
                    bestSq = sq;
                }
            }

            foreach (Vector3 boxPos in boxWorldPos.Values)
            {
                Vector3 delta = boxPos - candidate.CropPos;
                delta.y = 0f;
                float sq = delta.sqrMagnitude;
                if (sq < bestSq)
                {
                    bestSq = sq;
                }
            }

            return bestSq;
        }

        private static string HomelandFarmFieldCellKey(Vector3 fieldLocalPos)
        {
            // XZ-only: crop boxes sit on a constant field-local y plane, but freshly-sown crop
            // entities report y inconsistently (0.0 / 0.06 / 0.12). Including y made such a crop
            // miss its box's cell and fall through to the tight world-proximity match, leaving the
            // box wrongly flagged empty -> sow targeted a planted box -> server rejected the batch.
            Vector3 cell = HomelandFarmReduceCraftPrecision(fieldLocalPos);
            return cell.x.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + "|"
                + cell.z.ToString("F3", System.Globalization.CultureInfo.InvariantCulture);
        }

        private bool TryHomelandFarmResolveEntityFieldLocalPosition(uint netId, out Vector3 fieldLocalPos)
        {
            fieldLocalPos = Vector3.zero;
            if (netId == 0U)
            {
                return false;
            }

            if (this.TryHomelandFarmResolveFarmEntityPosition(netId, out Vector3 worldPos) && worldPos != Vector3.zero
                && this.TryHomelandFarmTryResolveSowFieldLocalPositionFromEntityWorld(netId, worldPos, out fieldLocalPos, out _))
            {
                return true;
            }

            // Do not walk raw entity pointers (transformComponent/localPosition): stale loaded-entity
            // handles from large radius scans can AV the mono runtime.

            if (!this.HomelandFarmPrefersAuraComponentData())
            {
                Type transformDataType = this.FindLoadedType(
                    "XDTDataAndProtocol.ComponentsData.TransformComponentData",
                    "TransformComponentData");
                if (transformDataType != null
                    && this.TryHomelandFarmGetComponentData(transformDataType, netId, out object transformDataObj, out _)
                    && transformDataObj != null)
                {
                    if (this.TryGetObjectMember(transformDataObj, "position", out object posObj)
                        && posObj is Vector3 managedPos
                        && managedPos != Vector3.zero)
                    {
                        fieldLocalPos = HomelandFarmReduceCraftPrecision(managedPos);
                        return true;
                    }
                }
            }

            return false;
        }

        private bool TryHomelandFarmCropBoxHasCrop(uint boxNetId, out uint linkedCropNetId)
        {
            linkedCropNetId = 0U;
            if (boxNetId == 0U)
            {
                return false;
            }

            if (!this.HomelandFarmPrefersAuraComponentData()
                && this.TryHomelandFarmGetComponentData(this.homelandFarmCropItemDataType, boxNetId, out _, out _, "CropItemData"))
            {
                linkedCropNetId = boxNetId;
                return true;
            }

            if (!this.TryHomelandFarmGetComponentData(this.homelandFarmCropBoxItemDataType, boxNetId, out object cropBoxData, out _, "CropBoxItemData")
                || cropBoxData == null)
            {
                return false;
            }

            for (int i = 0; i < HomelandFarmCropBoxLinkMembers.Length; i++)
            {
                if (this.TryHomelandFarmReadComponentUInt(cropBoxData, out uint linkId, HomelandFarmCropBoxLinkMembers[i])
                    && linkId != 0U)
                {
                    linkedCropNetId = linkId;
                    return true;
                }
            }

            return this.TryHomelandFarmReadComponentInt(cropBoxData, out int cropCount, "cropCount", "CropCount") && cropCount > 0;
        }

        private bool TryHomelandFarmIsEmptyCropPlanter(uint netId, uint playerNetId, HashSet<uint> occupiedCropBoxNetIds)
        {
            if (netId == 0U || !this.EnsureHomelandFarmReflectionReady())
            {
                return false;
            }

            if (occupiedCropBoxNetIds != null && occupiedCropBoxNetIds.Contains(netId))
            {
                return false;
            }

            if (this.homelandFarmAutoPendingSowBoxNetIds.Contains(netId))
            {
                return false;
            }

            if (!this.TryHomelandFarmGetComponentData(this.homelandFarmCropBoxItemDataType, netId, out _, out _, "CropBoxItemData"))
            {
                return false;
            }

            uint effectiveOwnerNetId = playerNetId;
            if (this.TryHomelandFarmGetSelfPlayInFieldOwnerNetId(out uint fieldOwnerNetId) && fieldOwnerNetId != 0U)
            {
                effectiveOwnerNetId = fieldOwnerNetId;
            }

            if (effectiveOwnerNetId != 0U
                && this.TryHomelandFarmTryReadOwnerId(netId, out uint ownerId)
                && ownerId != 0U
                && ownerId != effectiveOwnerNetId)
            {
                return false;
            }

            return true;
        }

        private bool TryHomelandFarmAppendEmptyPlanterPoint(
            uint netId,
            HashSet<ulong> usedLevelObjectNetIds,
            List<object> plantPoints,
            int maxSlots,
            ref string statusNote)
        {
            if (plantPoints == null || usedLevelObjectNetIds == null || plantPoints.Count >= maxSlots)
            {
                return false;
            }

            // Dedup by planter entity netId (one point per box).
            if (!usedLevelObjectNetIds.Add(netId))
            {
                return false;
            }

            // CropSeeding expects, per box (SeedBagCommand → PlayerSeedBagAction → GenConfirmOption):
            //   levelObjectNetId = boxArg.zoneElement.putZoneId (validated GetLevelObject)
            //   pos              = ReducePrecision(worldToLocal * element.root world position)
            //   angle            = RoundToInt(boxArg.rotation.eulerAngles.y) field-local
            if (!this.TryHomelandFarmResolveBoxFieldPlacement(netId, out ulong levelObjectNetId, out Vector3 sendPos, out int angle)
                || levelObjectNetId == 0UL)
            {
                bool hasPutZone = this.TryHomelandFarmResolveCropBoxSowLevelObjectId(netId, out ulong putZoneProbe);
                statusNote = "Planter placement unavailable (putZone=" + (hasPutZone ? putZoneProbe.ToString() : "missing") + ").";
                return false;
            }

            object point = this.CreateHomelandFarmCropPlantPoint(sendPos, angle, levelObjectNetId, netId);
            if (point == null)
            {
                statusNote = "CropPlantPoint create failed.";
                return false;
            }

            this.HomelandFarmLog("Sow point planter=" + netId + " putZone=" + levelObjectNetId
                + " pos=" + sendPos.ToString("F3") + " angle=" + angle + ".");
            plantPoints.Add(point);
            return true;
        }

        // FieldComponent registers its root put zone as LevelObjectId(homeNetId, 1). The ulong
        // encoding is owner in the low 32 bits and slot id in the high 32 bits. Crop-box entities
        // often store only homeNetId (id=0) in TransformData.parentNetId; the server rejects that
        // with InvalidPlantBox because GetLevelObject needs the full encoded id.
        private static ulong TryHomelandFarmEncodeFieldPutZoneId(ulong parentOrHomeNetId)
        {
            if (parentOrHomeNetId == 0UL)
            {
                return 0UL;
            }

            if ((parentOrHomeNetId >> 32) == 0UL)
            {
                return parentOrHomeNetId | (1UL << 32);
            }

            return parentOrHomeNetId;
        }

        // Legacy alias: LevelObjectId(entityNetId, 1).
        private bool TryHomelandFarmResolvePlanterPutZoneId(uint entityNetId, out ulong putZoneId)
        {
            putZoneId = 0UL;
            if (entityNetId == 0U)
            {
                return false;
            }

            putZoneId = TryHomelandFarmEncodeFieldPutZoneId(entityNetId);
            return putZoneId != 0UL;
        }

        // Matches CraftMath.ReducePrecision used by BuildSingle.GenSimpleConfirmOption.
        private static Vector3 HomelandFarmReduceCraftPrecision(Vector3 vector3)
        {
            vector3.x = Mathf.Round(vector3.x * 1000f) * 0.001f;
            vector3.y = Mathf.Round(vector3.y * 1000f) * 0.001f;
            vector3.z = Mathf.Round(vector3.z * 1000f) * 0.001f;
            return vector3;
        }

        // UI GrowCrop wire uses field-local y=0.06 on crop-box cells; aura rectMatrix / transform paths
        // can land on 0, ~0.1, or double-offset ~0.12 — all must normalize to 0.06 or the server
        // rejects the batch with InvalidPlantBox.
        private static Vector3 HomelandFarmNormalizeCropSowFieldLocalPos(Vector3 fieldLocalPos)
        {
            float y = fieldLocalPos.y;
            if (Mathf.Abs(y) < 0.001f
                || (y > 0.001f && y < 0.15f)
                || Mathf.Abs(y - (HomelandFarmCropSowFieldLocalY * 2f)) < 0.015f)
            {
                fieldLocalPos.y = HomelandFarmCropSowFieldLocalY;
            }

            return HomelandFarmReduceCraftPrecision(fieldLocalPos);
        }

        // Matches CraftMath.ReducePrecision(..., anglePrecision=90, BoxSide.Bottom) on field-local rotation.
        private static int HomelandFarmQuantizeFieldLocalSowAngleY(Quaternion fieldLocalRotation)
        {
            float angleY = fieldLocalRotation.eulerAngles.y;
            angleY = Mathf.Round(angleY / 90f) * 90f;
            return Mathf.RoundToInt(angleY);
        }

        // Resolves CropSeeding values for one planter box (SeedBagCommand / GenSimpleConfirmOption):
        //   putZoneId      = validated put-zone LevelObject on the crop box entity
        //   fieldLocalPos  = ReducePrecision(worldToLocal * seedPreviewRootWorldPos)
        //   angleY         = RoundToInt(fieldLocal preview rotation Y, 90° steps)
        private bool TryHomelandFarmResolveBoxFieldPlacement(uint netId, out ulong putZoneId, out Vector3 fieldLocalPos, out int angleY)
        {
            putZoneId = 0UL;
            fieldLocalPos = Vector3.zero;
            angleY = 0;
            if (netId == 0U)
            {
                return false;
            }

            if (!this.TryHomelandFarmResolveCropBoxSowLevelObjectId(netId, out putZoneId) || putZoneId == 0UL)
            {
                return false;
            }

            if (!this.TryHomelandFarmTryResolveSowPointFromCraftPutZone(
                    netId,
                    putZoneId,
                    out fieldLocalPos,
                    out angleY,
                    out string status))
            {
                this.HomelandFarmLog("Sow point planter=" + netId + " putZone=" + putZoneId + ": " + status);
                return false;
            }

            return fieldLocalPos != Vector3.zero;
        }

        private bool TryHomelandFarmResolveBoxFieldPlacement(uint netId, out ulong putZoneId, out Vector3 fieldLocalPos)
        {
            return this.TryHomelandFarmResolveBoxFieldPlacement(netId, out putZoneId, out fieldLocalPos, out _);
        }

        // Suspend the mono GC so raw object pointers held during per-box native sow resolution are
        // not collected/moved mid-sequence (cause of the random sow-all native AV). Paired with
        // HomelandFarmAuraGcEnable in a finally. No-op if the export is unavailable.
        private void HomelandFarmAuraGcDisable()
        {
            try
            {
                auraMonoGcDisable?.Invoke();
            }
            catch
            {
            }
        }

        private void HomelandFarmAuraGcEnable()
        {
            try
            {
                auraMonoGcEnable?.Invoke();
            }
            catch
            {
            }
        }

        // Coroutine: resolves empty planter slots, yielding every HomelandFarmSowSlotsPerFrame boxes
        // so the per-box AuraMono resolution is spread across frames (prevents the native crash on
        // sow-all at large radius). Results are returned via homelandFarmSowSlotPoints/Status/Ok.
        private IEnumerator FindEmptyCropPlanterSlotsRoutine(int maxSlots, bool useAutoFarmCollectShortcuts = false)
        {
            this.homelandFarmSowSlotOk = false;
            this.homelandFarmSowSlotPoints = new List<object>();
            this.homelandFarmSowSlotStatus = "No empty planter slots found.";

            if (maxSlots <= 0)
            {
                this.homelandFarmSowSlotStatus = "Seed count is zero.";
                yield break;
            }

            if (!this.TryHomelandFarmIsInHomeland(out string homelandStatus))
            {
                this.homelandFarmSowSlotStatus = homelandStatus;
                yield break;
            }

            if (!this.EnsureHomelandFarmReflectionReady())
            {
                this.homelandFarmSowSlotStatus = string.IsNullOrEmpty(this.homelandFarmReflectionUnavailableStatus)
                    ? "Homeland farm reflection unavailable."
                    : this.homelandFarmReflectionUnavailableStatus;
                yield break;
            }

            this.TryEnsureHomelandFarmInteropAssembliesLoaded();
            this.EnsureHomelandFarmSowManagedReflection();
            this.TryEnsureHomelandFarmComponentDataManagedReflection();
            this.homelandFarmAuraComponentMissCache.Clear();

            this.TryGetHomelandFarmPlayerNetId(out uint playerNetId, out _);
            bool hasPlayerPos = useAutoFarmCollectShortcuts
                ? this.TryGetHomelandFarmScanCenter(out Vector3 playerPos)
                : this.TryGetHomelandFarmPlayerPosition(out playerPos);
            float radius = this.homelandFarmWaterRadius;
            HashSet<uint> cropNetIds = new HashSet<uint>();
            if (hasPlayerPos)
            {
                // Use the exact sow radius (no +2 padding): padding pulls in occupied planters
                // outside the intended zone, and the server then rejects the whole batch with
                // PlantBoxHasCrop even though the planters the player is standing on are empty.
                this.TryHomelandFarmCollectFarmEntityNetIds(
                    cropNetIds,
                    out _,
                    playerPos,
                    radius,
                    useAutoFarmCollectShortcuts: useAutoFarmCollectShortcuts);
            }
            else
            {
                this.TryHomelandFarmCollectCropEntityNetIds(cropNetIds, out _);
            }

            this.HomelandFarmLog("Sow slot scan: cropNetIds=" + cropNetIds.Count + " resolving crop boxes...");

            HashSet<uint> cropBoxNetIds = new HashSet<uint>();
            if (this.homelandFarmLastScanCropBoxNetIds.Count > 0)
            {
                foreach (uint boxNetId in this.homelandFarmLastScanCropBoxNetIds)
                {
                    if (boxNetId != 0U && cropNetIds.Contains(boxNetId))
                    {
                        cropBoxNetIds.Add(boxNetId);
                    }
                }
            }

            if (cropBoxNetIds.Count == 0)
            {
                foreach (uint candidateNetId in cropNetIds)
                {
                    if (candidateNetId == 0U)
                    {
                        continue;
                    }

                    if (this.TryHomelandFarmClassifyFarmNetId(candidateNetId, out bool isCropBox) && isCropBox)
                    {
                        cropBoxNetIds.Add(candidateNetId);
                    }
                }
            }

            if (cropBoxNetIds.Count == 0)
            {
                this.TryHomelandFarmCollectComponentsNetIds(this.homelandFarmCropBoxComponentType, cropBoxNetIds, "CropBoxComponent(empty)");
            }

            this.HomelandFarmLog("Sow slot scan: cropBoxes=" + cropBoxNetIds.Count + " marking occupied...");

            HashSet<uint> occupiedCropBoxNetIds = new HashSet<uint>();
            this.TryHomelandFarmBuildOccupiedCropBoxNetIds(cropBoxNetIds, cropNetIds, occupiedCropBoxNetIds);
            this.HomelandFarmLog("Sow slot scan: occupied=" + occupiedCropBoxNetIds.Count + " finding empties...");

            // Let the heavy occupied-detection frame settle before starting per-box resolution.
            yield return null;

            List<object> plantPoints = this.homelandFarmSowSlotPoints;
            HashSet<ulong> usedLevelObjectNetIds = new HashSet<ulong>();
            int emptyBoxCount = 0;
            string statusNote = string.Empty;
            int sinceYield = 0;

            this.EnsureHomelandFarmScannerTypes();

            // Best-effort GC suspension for the per-box resolution. The crash-prone per-box
            // GetLevelObject/rectMatrix calls were removed (put-zone is deterministic, world pose
            // comes from the cached entity position), so this is now only cheap insurance for the
            // remaining GetAllComponents reads. No-op where mono_gc_disable is unavailable.
            this.HomelandFarmAuraGcDisable();
            try
            {
                foreach (uint netId in cropBoxNetIds)
                {
                    if (plantPoints.Count >= maxSlots)
                    {
                        break;
                    }

                    if (this.TryHomelandFarmIsEmptyCropPlanter(netId, playerNetId, occupiedCropBoxNetIds)
                        && this.TryHomelandFarmAppendEmptyPlanterPoint(netId, usedLevelObjectNetIds, plantPoints, maxSlots, ref statusNote))
                    {
                        emptyBoxCount++;
                    }

                    // Spread per-box AuraMono resolution across frames.
                    if (++sinceYield >= HomelandFarmSowSlotsPerFrame)
                    {
                        sinceYield = 0;
                        yield return null;
                    }
                }
            }
            finally
            {
                this.HomelandFarmAuraGcEnable();
            }

            if (plantPoints.Count == 0)
            {
                this.homelandFarmSowSlotStatus = "No empty planter slots (cropBoxes=" + cropBoxNetIds.Count
                    + ", occupied=" + occupiedCropBoxNetIds.Count + ")."
                    + (string.IsNullOrEmpty(statusNote) ? string.Empty : " " + statusNote);
                yield break;
            }

            this.homelandFarmSowSlotStatus = "Found " + plantPoints.Count + " empty slot(s) in radius " + radius.ToString("F0")
                + " (empty=" + emptyBoxCount + ", occupied=" + occupiedCropBoxNetIds.Count + "/" + cropBoxNetIds.Count + ").";
            this.HomelandFarmLog(this.homelandFarmSowSlotStatus);
            this.homelandFarmSowSlotOk = true;
        }

        private unsafe bool TryHomelandFarmTryGetBackPackNameAuraMono(int staticId, int step, uint netId, out string displayName)
        {
            displayName = string.Empty;
            if (staticId <= 0 || !this.EnsureAuraMonoApiReady() || auraMonoClassFromName == null || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            try
            {
                IntPtr gameSystemImage = this.FindAuraMonoImage(new[] { "XDTGameSystem", "XDTGameSystem.dll" });
                if (gameSystemImage == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr backpackItemClass = auraMonoClassFromName(gameSystemImage, "XDTGameSystem.UISystem.BackPack", "BackpackItem");
                if (backpackItemClass == IntPtr.Zero)
                {
                    backpackItemClass = auraMonoClassFromName(gameSystemImage, "UISystem.BackPack", "BackpackItem");
                }

                if (backpackItemClass == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr getBackpackNameMethod = this.FindAuraMonoMethodOnHierarchy(backpackItemClass, "GetBackPackName", 3);
                if (getBackpackNameMethod == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[3];
                args[0] = (IntPtr)(&staticId);
                args[1] = (IntPtr)(&step);
                args[2] = (IntPtr)(&netId);
                IntPtr nameObj = auraMonoRuntimeInvoke(getBackpackNameMethod, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero || nameObj == IntPtr.Zero || !this.TryReadMonoString(nameObj, out string rawName))
                {
                    return false;
                }

                displayName = this.CleanResolvedBagFoodName(rawName);
                return !string.IsNullOrWhiteSpace(displayName);
            }
            catch
            {
                displayName = string.Empty;
                return false;
            }
        }

        private unsafe bool TryHomelandFarmTryGetEntityTableNameAuraMono(int staticId, out string displayName)
        {
            displayName = string.Empty;
            if (staticId <= 0 || !this.EnsureAuraMonoApiReady() || auraMonoClassFromName == null || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            try
            {
                IntPtr ecsImage = this.FindAuraMonoImage(new[] { "EcsClient", "EcsClient.dll" });
                if (ecsImage == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr tableDataClass = auraMonoClassFromName(ecsImage, string.Empty, "TableData");
                if (tableDataClass == IntPtr.Zero)
                {
                    tableDataClass = auraMonoClassFromName(ecsImage, "EcsClient", "TableData");
                }

                if (tableDataClass == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr getEntityMethod = this.FindAuraMonoMethodOnHierarchy(tableDataClass, "GetEntity", 2);
                IntPtr exc = IntPtr.Zero;
                IntPtr entityObj = IntPtr.Zero;
                if (getEntityMethod != IntPtr.Zero)
                {
                    bool needException = false;
                    IntPtr* args = stackalloc IntPtr[2];
                    args[0] = (IntPtr)(&staticId);
                    args[1] = (IntPtr)(&needException);
                    entityObj = auraMonoRuntimeInvoke(getEntityMethod, IntPtr.Zero, (IntPtr)args, ref exc);
                }

                if (exc != IntPtr.Zero || entityObj == IntPtr.Zero)
                {
                    exc = IntPtr.Zero;
                    getEntityMethod = this.FindAuraMonoMethodOnHierarchy(tableDataClass, "GetEntity", 1);
                    if (getEntityMethod == IntPtr.Zero)
                    {
                        return false;
                    }

                    IntPtr* args = stackalloc IntPtr[1];
                    args[0] = (IntPtr)(&staticId);
                    entityObj = auraMonoRuntimeInvoke(getEntityMethod, IntPtr.Zero, (IntPtr)args, ref exc);
                }

                if (exc != IntPtr.Zero || entityObj == IntPtr.Zero)
                {
                    return false;
                }

                if (this.TryGetMonoStringMember(entityObj, "name", out string rawName)
                    || this.TryGetMonoStringMember(entityObj, "Name", out rawName))
                {
                    displayName = this.CleanResolvedBagFoodName(rawName);
                    return !string.IsNullOrWhiteSpace(displayName);
                }

                return false;
            }
            catch
            {
                displayName = string.Empty;
                return false;
            }
        }

        private unsafe bool TryHomelandFarmTryGetCropFertilizerTableRowAuraMono(
            int fertilizerStaticId,
            out int effectType,
            out int effectLevel,
            out int decorationId,
            out int feedbackEffect)
        {
            effectType = 0;
            effectLevel = 0;
            decorationId = 0;
            feedbackEffect = 0;
            if (!this.TryHomelandFarmTryGetCropFertilizerTableRowAuraMonoObject(fertilizerStaticId, out IntPtr rowObj))
            {
                return false;
            }

            int actionEffect = 0;
            if (!this.TryHomelandFarmReadAuraCropFertilizerRowFields(
                    rowObj,
                    out _,
                    out effectType,
                    out decorationId,
                    out feedbackEffect,
                    out actionEffect))
            {
                return false;
            }

            if (!this.TryInvokeAuraMonoZeroArgInt(rowObj, out effectLevel, "get_effectLevel", "get_EffectLevel"))
            {
                this.TryGetMonoInt32Member(rowObj, "_effectLevel", out effectLevel);
                this.TryGetMonoInt32Member(rowObj, "effectLevel", out effectLevel);
            }

            return true;
        }

        private unsafe bool TryHomelandFarmTryGetCropFertilizerTableRowAuraMono(int fertilizerStaticId, out int effectType, out int effectLevel, out int decorationId)
        {
            return this.TryHomelandFarmTryGetCropFertilizerTableRowAuraMono(
                fertilizerStaticId,
                out effectType,
                out effectLevel,
                out decorationId,
                out _);
        }

        private unsafe bool TryHomelandFarmTryGetCropFertilizerTableRowAuraMono(int fertilizerStaticId, out int effectType, out int effectLevel)
        {
            return this.TryHomelandFarmTryGetCropFertilizerTableRowAuraMono(
                fertilizerStaticId,
                out effectType,
                out effectLevel,
                out _,
                out _);
        }

        private bool TryHomelandFarmTryGetCropFertilizerTableRow(int fertilizerStaticId, out object row, out int effectType, out int effectLevel)
        {
            row = null;
            effectType = 0;
            effectLevel = 0;
            if (fertilizerStaticId <= 0)
            {
                return false;
            }

            if (this.EnsureHomelandFarmTableDataReflection() && this.homelandFarmGetCropfertilizerMethod != null)
            {
                try
                {
                    row = this.homelandFarmGetCropfertilizerMethod.Invoke(null, new object[] { fertilizerStaticId });
                    if (row != null)
                    {
                        if (!this.TryReadManagedInt32Member(row, "effectType", out effectType))
                        {
                            this.TryReadManagedInt32Member(row, "EffectType", out effectType);
                        }

                        if (!this.TryReadManagedInt32Member(row, "effectLevel", out effectLevel))
                        {
                            this.TryReadManagedInt32Member(row, "EffectLevel", out effectLevel);
                        }

                        return true;
                    }
                }
                catch
                {
                }
            }

            return this.TryHomelandFarmTryGetCropFertilizerTableRowAuraMono(fertilizerStaticId, out effectType, out effectLevel);
        }

        private bool TryHomelandFarmTryGetCropFertilizerTableRowById(int cropFertilizerId, out int effectType, out int effectLevel)
        {
            effectType = 0;
            effectLevel = 0;
            return cropFertilizerId > 0
                && this.TryHomelandFarmTryGetCropFertilizerTableRow(cropFertilizerId, out _, out effectType, out effectLevel);
        }

        private bool TryHomelandFarmCheckFertilizerEffectValid(
            int cropFertilizerIdOnCrop,
            int selectedEffectType,
            int selectedEffectLevel,
            int affectType)
        {
            if (selectedEffectType != affectType)
            {
                return false;
            }

            switch (affectType)
            {
                case HomelandFarmFertilizerEffectGrowthValue:
                    if (cropFertilizerIdOnCrop == 0)
                    {
                        return true;
                    }

                    if (this.TryHomelandFarmTryGetCropFertilizerTableRowById(cropFertilizerIdOnCrop, out int existingEffectType, out int existingEffectLevel)
                        && selectedEffectType == existingEffectType
                        && selectedEffectLevel > existingEffectLevel)
                    {
                        return true;
                    }

                    return false;
                case HomelandFarmFertilizerEffectGrowthRate:
                    return true;
                case HomelandFarmFertilizerEffectGrowthProduct:
                    return cropFertilizerIdOnCrop != 0;
                default:
                    return false;
            }
        }

        private bool IsHomelandFarmCropFertilizable(uint cropNetId, int fertilizerStaticId, HashSet<uint> scanNetIds, out string reason)
        {
            reason = string.Empty;
            if (cropNetId == 0U || fertilizerStaticId <= 0)
            {
                reason = "Crop or fertilizer missing.";
                return false;
            }

            if (!this.EnsureHomelandFarmReflectionReady())
            {
                reason = this.homelandFarmReflectionUnavailableStatus;
                return false;
            }

            if (!this.TryGetHomelandFarmPlayerNetId(out uint playerNetId, out _) || playerNetId == 0U)
            {
                reason = "Player netId unavailable.";
                return false;
            }

            uint effectiveOwnerNetId = playerNetId;
            if (this.TryHomelandFarmGetSelfPlayInFieldOwnerNetId(out uint fieldOwnerNetId) && fieldOwnerNetId != 0U)
            {
                effectiveOwnerNetId = fieldOwnerNetId;
            }

            bool onOwnField = this.TryHomelandFarmIsOnOwnFarmField(playerNetId);
            if (!this.TryHomelandFarmTryResolveOwnCropOwnerNetId(
                    cropNetId,
                    scanNetIds,
                    playerNetId,
                    effectiveOwnerNetId,
                    onOwnField,
                    out _))
            {
                reason = "Not own crop.";
                return false;
            }

            // Fertilize must apply to CROPS only. A crop has CropItemData; flowers / trees have
            // PlantItemData. Require CropItemData and reject anything else (a PlantItemData-only
            // entity is a flower/tree and must never be fertilized here).
            if (!this.TryHomelandFarmGetComponentData(this.homelandFarmCropItemDataType, cropNetId, out object cropData, out _, "CropItemData")
                || cropData == null)
            {
                reason = "Not a crop (no CropItemData).";
                return false;
            }

            // NOTE: previously rejected stage==4 as "already mature", but on this build stage-4
            // crops are still fertilizable (confirmed: they can be fertilized manually). Let the
            // server be the authority on maturity; do not pre-reject by stage here.

            // Read the crop's current fertilizer slots up front (works via AuraMono even without the
            // managed/aura fertilizer table) so we can ALWAYS skip crops already carrying the
            // selected fertilizer — i.e. fertilize only crops that are not yet fertilized with it.
            int manureId = 0;
            int breedingPowderId = 0;
            if (!this.TryHomelandFarmReadComponentInt(cropData, out manureId, "manureId", "ManureId"))
            {
                manureId = 0;
            }

            if (!this.TryHomelandFarmReadComponentInt(cropData, out breedingPowderId, "breedingPowderId", "BreedingPowderId"))
            {
                breedingPowderId = 0;
            }

            if (fertilizerStaticId > 0 && (manureId == fertilizerStaticId || breedingPowderId == fertilizerStaticId))
            {
                reason = "Already fertilized (same fertilizer).";
                return false;
            }

            bool hasTableRow = this.TryHomelandFarmTryGetCropFertilizerTableRow(
                fertilizerStaticId,
                out _,
                out int selectedEffectType,
                out int selectedEffectLevel);
            if (!hasTableRow)
            {
                // No fertilizer table (managed or AuraMono). The crop doesn't already carry this
                // exact fertilizer; let the server validate effect-slot compatibility for the rest.
                return true;
            }

            bool valid = this.TryHomelandFarmCheckFertilizerEffectValid(manureId, selectedEffectType, selectedEffectLevel, HomelandFarmFertilizerEffectGrowthValue)
                || this.TryHomelandFarmCheckFertilizerEffectValid(0, selectedEffectType, selectedEffectLevel, HomelandFarmFertilizerEffectGrowthRate)
                || this.TryHomelandFarmCheckFertilizerEffectValid(breedingPowderId, selectedEffectType, selectedEffectLevel, HomelandFarmFertilizerEffectGrowthProduct);
            if (!valid)
            {
                reason = "Already fertilized or incompatible effect.";
            }

            return valid;
        }

        private List<uint> ScanHomelandFarmOwnCropNetIds(uint playerNetId)
        {
            List<uint> result = new List<uint>();
            if (playerNetId == 0U || !this.EnsureHomelandFarmReflectionReady())
            {
                return result;
            }

            HashSet<uint> netIds = new HashSet<uint>();
            this.TryHomelandFarmCollectCropEntityNetIds(netIds, out _);
            foreach (uint netId in netIds)
            {
                if (!this.TryHomelandFarmGetComponentData(this.homelandFarmCropItemDataType, netId, out _, out _, "CropItemData"))
                {
                    continue;
                }

                if (!this.TryHomelandFarmTryReadOwnerId(netId, out uint ownerId) || ownerId != playerNetId)
                {
                    continue;
                }

                result.Add(netId);
            }

            return result;
        }

        private void StartHomelandFarmSowAll(bool silent)
        {
            if (!this.TryBeginHomelandFarmAction(silent, out _))
            {
                return;
            }

            this.HomelandFarmLog("Start sow all source=" + this.homelandFarmSeedStorage + " radius=" + this.homelandFarmWaterRadius.ToString("F1"));
            this.homelandFarmLastStatus = "Sowing crops...";
            this.homelandFarmCoroutine = ModCoroutines.Start(this.HomelandFarmSowAllRoutine(silent));
        }

        private void StartHomelandFarmFertilizeAll(bool silent)
        {
            if (!this.TryBeginHomelandFarmAction(silent, out _))
            {
                return;
            }

            this.HomelandFarmLog("Start fertilize all source=" + this.homelandFarmFertStorage + " radius=" + this.homelandFarmWaterRadius.ToString("F1"));
            this.homelandFarmLastStatus = "Fertilizing crops...";
            this.homelandFarmCoroutine = ModCoroutines.Start(this.HomelandFarmFertilizeAllRoutine(silent));
        }

        private IEnumerator HomelandFarmSowAllRoutine(bool silent)
        {
            yield return null;

            int sowedPoints = 0;
            int batchCount = 0;
            int failCount = 0;
            try
            {
                if (this.homelandFarmScannedSeeds.Count == 0)
                {
                    this.RefreshHomelandFarmSeeds();
                }

                if (this.homelandFarmScannedSeeds.Count == 0)
                {
                    this.homelandFarmLastStatus = "No crop seeds in " + this.homelandFarmSeedStorage + ".";
                    if (!silent)
                    {
                        this.AddMenuNotification(this.homelandFarmLastStatus, new Color(1f, 0.55f, 0.45f));
                    }

                    yield break;
                }

                int seedIndex = Mathf.Clamp(this.homelandFarmSelectedSeedIndex, 0, this.homelandFarmScannedSeeds.Count - 1);
                HomelandFarmInventoryItem seed = this.homelandFarmScannedSeeds[seedIndex];
                if (seed == null || seed.NetId == 0U || seed.Count <= 0)
                {
                    this.homelandFarmLastStatus = "Selected seed unavailable.";
                    yield break;
                }

                // Sowing shares the same per-command cap as watering: the server rejects the whole
                // CropSeeding request (OnBuildSeedResult=MaxPlantOpCountLimit) if the point count
                // exceeds the player's hobby-skill cell capacity (1/3/6/9...). Batch by that value.
                int sowBatchSize = Mathf.Clamp(this.TryHomelandFarmGetSprinklerCellCount(), 1, HomelandFarmBatchLimit);
                this.HomelandFarmLog("Sow batch size=" + sowBatchSize + " (hobby skill cell count)");

                int remainingSeeds = seed.Count;
                while (remainingSeeds > 0)
                {
                    // Drive the slot-scan coroutine manually (yielding its values) so it works under
                    // both loaders: BepInEx wraps the outer coroutine to Il2Cpp, where a nested
                    // "yield return managedIEnumerator" is not reliably pumped by Unity.
                    IEnumerator slotRoutine = this.FindEmptyCropPlanterSlotsRoutine(remainingSeeds);
                    while (slotRoutine.MoveNext())
                    {
                        yield return slotRoutine.Current;
                    }

                    List<object> plantPoints = this.homelandFarmSowSlotPoints;
                    string slotStatus = this.homelandFarmSowSlotStatus;
                    if (!this.homelandFarmSowSlotOk
                        || plantPoints == null
                        || plantPoints.Count == 0)
                    {
                        this.HomelandFarmLog("Sow slot scan stopped: " + slotStatus);
                        if (sowedPoints == 0)
                        {
                            this.homelandFarmLastStatus = slotStatus;
                            if (!silent)
                            {
                                this.AddMenuNotification("Sow: " + slotStatus, new Color(1f, 0.55f, 0.45f));
                            }
                        }

                        break;
                    }

                    yield return null;

                    for (int offset = 0; offset < plantPoints.Count && remainingSeeds > 0; offset += sowBatchSize)
                    {
                        int batchSize = Math.Min(sowBatchSize, Math.Min(plantPoints.Count - offset, remainingSeeds));
                        List<object> batch = plantPoints.GetRange(offset, batchSize);
                        if (this.TryHomelandFarmSow(seed.NetId, batch, out string sowStatus))
                        {
                            sowedPoints += batch.Count;
                            remainingSeeds -= batch.Count;
                            batchCount++;
                            this.HomelandFarmLog("Sow batch ok seedNetId=" + seed.NetId + " count=" + batch.Count + " " + sowStatus);
                        }
                        else
                        {
                            failCount++;
                            this.HomelandFarmLog("Sow batch failed: " + sowStatus);
                            if (sowedPoints == 0)
                            {
                                this.homelandFarmLastStatus = sowStatus;
                                if (!silent)
                                {
                                    this.AddMenuNotification("Sow: " + sowStatus, new Color(1f, 0.55f, 0.45f));
                                }

                                yield break;
                            }

                            break;
                        }

                        yield return new WaitForSecondsRealtime(HomelandFarmCommandDelaySeconds);
                    }

                    // A single radius scan already returns every empty planter in range. Never
                    // re-scan and re-sow: the server has not marked the just-sown slots as occupied
                    // yet, so a re-scan finds the SAME slots and floods CropSeeding (crash + wasted seeds).
                    break;
                }

                this.homelandFarmLastStatus = "Sowed " + sowedPoints + " slot(s) in " + batchCount + " batch(es)"
                    + (failCount > 0 ? ", " + failCount + " failed" : string.Empty) + ".";
                if (!silent)
                {
                    Color notifyColor = sowedPoints > 0
                        ? new Color(0.45f, 1f, 0.55f)
                        : new Color(1f, 0.55f, 0.45f);
                    this.AddMenuNotification("Sow: " + sowedPoints + " points (" + batchCount + " batches)", notifyColor);
                }
            }
            finally
            {
                this.homelandFarmCoroutine = null;
                this.homelandFarmBusyUntil = Time.realtimeSinceStartup + HomelandFarmActionCooldownSeconds;
            }
        }

        private IEnumerator HomelandFarmFertilizeAllRoutine(bool silent)
        {
            yield return null;

            int fertilized = 0;
            int batchCount = 0;
            int failCount = 0;
            try
            {
                if (this.homelandFarmScannedFertilizers.Count == 0)
                {
                    this.RefreshHomelandFarmFertilizers();
                }

                if (this.homelandFarmScannedFertilizers.Count == 0)
                {
                    this.homelandFarmLastStatus = "No crop fertilizers in " + this.homelandFarmFertStorage + ".";
                    if (!silent)
                    {
                        this.AddMenuNotification(this.homelandFarmLastStatus, new Color(1f, 0.55f, 0.45f));
                    }

                    yield break;
                }

                int fertIndex = Mathf.Clamp(this.homelandFarmSelectedFertilizerIndex, 0, this.homelandFarmScannedFertilizers.Count - 1);
                HomelandFarmInventoryItem fertilizer = this.homelandFarmScannedFertilizers[fertIndex];
                if (fertilizer == null || fertilizer.StaticId <= 0)
                {
                    this.homelandFarmLastStatus = "Selected fertilizer unavailable.";
                    yield break;
                }

                if (!this.TryGetHomelandFarmPlayerNetId(out uint playerNetId, out _))
                {
                    this.homelandFarmLastStatus = "Player netId unavailable.";
                    yield break;
                }

                HashSet<uint> scanNetIds = new HashSet<uint>();
                if (this.TryGetHomelandFarmPlayerPosition(out Vector3 playerPos))
                {
                    this.TryHomelandFarmCollectFarmEntityNetIds(
                        scanNetIds,
                        out _,
                        playerPos,
                        this.homelandFarmWaterRadius + 2f,
                        useAutoFarmCollectShortcuts: false);
                }

                yield return null;

                // Fertilize targets CROPS only (CropItemData). Do NOT include PlantItemData
                // (flowers / trees) — those are a separate system and must never be fertilized here.
                List<uint> ownCrops = this.ScanHomelandFarmCropsByRadius(
                    cropData => cropData != null,
                    "Fertilizable crops",
                    requireOwn: true,
                    preCollectedNetIds: scanNetIds,
                    includePlantData: false);
                Dictionary<string, int> rejectReasons = new Dictionary<string, int>(StringComparer.Ordinal);
                int maxTargets = Math.Max(1, fertilizer.Count);
                for (int i = 0; i < ownCrops.Count; i++)
                {
                    if (this.IsHomelandFarmCropFertilizable(ownCrops[i], fertilizer.StaticId, scanNetIds, out string rejectReason))
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(rejectReason))
                    {
                        if (!rejectReasons.TryGetValue(rejectReason, out int rejectCount))
                        {
                            rejectCount = 0;
                        }

                        rejectReasons[rejectReason] = rejectCount + 1;
                    }
                }

                List<uint> targets = this.BuildHomelandFarmFertilizeTargets(
                    ownCrops,
                    fertilizer.StaticId,
                    scanNetIds,
                    maxTargets);
                if (targets.Count > 0)
                {
                    this.HomelandFarmLog("Fertilize command netIds=" + string.Join(",", targets.ToArray()));
                }

                if (targets.Count == 0 && rejectReasons.Count > 0)
                {
                    System.Text.StringBuilder rejectSummary = new System.Text.StringBuilder();
                    foreach (KeyValuePair<string, int> entry in rejectReasons)
                    {
                        if (rejectSummary.Length > 0)
                        {
                            rejectSummary.Append("; ");
                        }

                        rejectSummary.Append(entry.Key).Append('=').Append(entry.Value);
                    }

                    this.HomelandFarmLog("Fertilize rejects (" + ownCrops.Count + " scanned): " + rejectSummary);
                }

                if (targets.Count == 0)
                {
                    this.homelandFarmLastStatus = "No fertilizable own crops for selected fertilizer.";
                    if (!silent)
                    {
                        this.AddMenuNotification(this.homelandFarmLastStatus, new Color(1f, 0.55f, 0.45f));
                    }

                    yield break;
                }

                if (fertilizer.NetId != 0U)
                {
                    if (!this.TryHomelandFarmEquipHandhold(fertilizer.NetId, out string equipStatus))
                    {
                        this.homelandFarmLastStatus = "Equip fertilizer failed: " + equipStatus;
                        this.HomelandFarmLog(this.homelandFarmLastStatus);
                        if (!silent)
                        {
                            this.AddMenuNotification(this.homelandFarmLastStatus, new Color(1f, 0.55f, 0.45f));
                        }

                        yield break;
                    }

                    yield return null;
                }
                else
                {
                    this.HomelandFarmLog("Fertilize warning: fertilizer backpack netId missing; server may reject.");
                }

                // Fertilize uses the direct AddManure command path (no casting animation). The
                // batch size is the equipped fertilizer's per-command cell cap (server rejects a
                // larger ManuredNetworkCommand).
                int fertilizeBatchSize = Mathf.Clamp(this.TryHomelandFarmGetFertilizerCastCellCount(), 1, HomelandFarmBatchLimit);
                this.HomelandFarmLog("Fertilize batch size=" + fertilizeBatchSize + " (TableMode.num for equipped fertilizer mode)");

                for (int offset = 0; offset < targets.Count; offset += fertilizeBatchSize)
                {
                    int batchSize = Math.Min(fertilizeBatchSize, targets.Count - offset);
                    List<uint> batch = targets.GetRange(offset, batchSize);
                    Dictionary<uint, HomelandFarmCropFertilizeSnapshot> beforeSnapshots =
                        this.TryHomelandFarmSnapshotCropFertilizeStates(batch);

                    this.HomelandFarmLog("Fertilize netIds=" + string.Join(",", batch.ToArray()));

                    if (this.TryHomelandFarmSendFertilizeAddManure(batch, out string manureStatus))
                    {
                        this.HomelandFarmLog("Fertilize AddManure: " + manureStatus);
                    }
                    else
                    {
                        this.HomelandFarmLog("Fertilize AddManure failed: " + manureStatus);
                    }

                    yield return new WaitForSecondsRealtime(HomelandFarmCommandDelaySeconds);

                    int applied = this.CountHomelandFarmFertilizeApplied(
                        batch,
                        beforeSnapshots,
                        fertilizer.StaticId,
                        scanNetIds,
                        out string verifyDetail);
                    if (applied > 0)
                    {
                        fertilized += applied;
                        batchCount++;
                        this.HomelandFarmLog("Fertilize batch ok applied=" + applied + "/" + batch.Count + " " + verifyDetail);
                    }
                    else
                    {
                        failCount++;
                        this.HomelandFarmLog("Fertilize batch fail count=" + batch.Count + " verify=" + verifyDetail);
                    }
                }

                this.homelandFarmLastStatus = "Fertilized " + fertilized + "/" + targets.Count + " crop(s) in " + batchCount + " batch(es)"
                    + (failCount > 0 ? ", " + failCount + " failed" : string.Empty) + ".";
                if (!silent)
                {
                    Color notifyColor = fertilized > 0
                        ? new Color(0.45f, 1f, 0.55f)
                        : new Color(1f, 0.55f, 0.45f);
                    this.AddMenuNotification("Fertilize: " + fertilized + "/" + targets.Count, notifyColor);
                }
            }
            finally
            {
                this.homelandFarmCoroutine = null;
                this.homelandFarmBusyUntil = Time.realtimeSinceStartup + HomelandFarmActionCooldownSeconds;
            }
        }

        // Rebuild fertilizer pile visuals for nearby crops that already have manureId set.
        private int TryHomelandFarmSyncNearbyManureVisualsOnce(bool logResults = true)
        {
            if (!this.EnsureHomelandFarmReflectionReady())
            {
                return 0;
            }

            List<uint> crops = this.ScanHomelandFarmCropsByRadius(
                _ => true,
                "ManureVisualSync",
                requireOwn: false,
                logScanSummary: logResults);
            HashSet<uint> seen = new HashSet<uint>();
            int synced = 0;
            for (int i = 0; i < crops.Count; i++)
            {
                uint cropNetId = crops[i];
                if (cropNetId == 0U)
                {
                    continue;
                }

                seen.Add(cropNetId);
                if (!this.TryHomelandFarmGetComponentData(this.homelandFarmCropItemDataType, cropNetId, out object cropData, out _, "CropItemData")
                    || cropData == null
                    || !this.TryHomelandFarmReadComponentInt(cropData, out int manureId, "manureId", "ManureId", "_manureId")
                    || manureId <= 0)
                {
                    this.homelandFarmSyncedManureVisualByCropNetId.Remove(cropNetId);
                    continue;
                }

                if (this.homelandFarmSyncedManureVisualByCropNetId.TryGetValue(cropNetId, out int syncedManureId)
                    && syncedManureId == manureId)
                {
                    continue;
                }

                if (this.TryHomelandFarmRefreshCropManureVisual(cropNetId, manureId, out string status))
                {
                    this.homelandFarmSyncedManureVisualByCropNetId[cropNetId] = manureId;
                    synced++;
                    if (logResults)
                    {
                        this.HomelandFarmLog("Manure visual auto-sync crop=" + cropNetId + " manure=" + manureId + " " + status);
                    }
                }
            }

            if (this.homelandFarmSyncedManureVisualByCropNetId.Count == 0)
            {
                return synced;
            }

            List<uint> stale = null;
            foreach (KeyValuePair<uint, int> entry in this.homelandFarmSyncedManureVisualByCropNetId)
            {
                if (seen.Contains(entry.Key))
                {
                    continue;
                }

                if (stale == null)
                {
                    stale = new List<uint>();
                }

                stale.Add(entry.Key);
            }

            if (stale != null)
            {
                for (int i = 0; i < stale.Count; i++)
                {
                    this.homelandFarmSyncedManureVisualByCropNetId.Remove(stale[i]);
                }
            }

            return synced;
        }

        private float DrawHomelandFarmStorageSourceToggle(
            float left,
            float y,
            float width,
            HomelandFarmStorageSource current,
            out HomelandFarmStorageSource selected)
        {
            selected = current;
            const float buttonW = 100f;
            const float buttonH = 24f;
            const float gap = 8f;
            HomelandFarmStorageSource[] values =
            {
                HomelandFarmStorageSource.Backpack,
                HomelandFarmStorageSource.Warehouse,
                HomelandFarmStorageSource.Both
            };
            string[] labels =
            {
                this.L("homeland_farm.storage_backpack"),
                this.L("homeland_farm.storage_warehouse"),
                this.L("homeland_farm.storage_both")
            };
            for (int i = 0; i < values.Length; i++)
            {
                bool isSelected = current == values[i];
                GUIStyle style = isSelected
                    ? (this.themePrimaryButtonStyle ?? GUI.skin.button)
                    : (GUI.skin.button);
                if (GUI.Button(new Rect(left + i * (buttonW + gap), y, buttonW, buttonH), labels[i], style))
                {
                    selected = values[i];
                }
            }

            return y + buttonH + 8f;
        }

        private float DrawHomelandFarmInventorySelector(
            float left,
            float y,
            float width,
            List<HomelandFarmInventoryItem> items,
            ref int selectedIndex,
            string emptyLabel)
        {
            const float rowH = 24f;
            if (items == null || items.Count == 0)
            {
                GUI.Label(new Rect(left, y, width, rowH), emptyLabel, GUI.skin.label);
                return y + rowH + 8f;
            }

            selectedIndex = Mathf.Clamp(selectedIndex, 0, items.Count - 1);
            HomelandFarmInventoryItem selected = items[selectedIndex];
            string label = selected != null ? selected.Label : emptyLabel;
            if (GUI.Button(new Rect(left, y, 28f, rowH), this.L("homeland_farm.prev")))
            {
                selectedIndex = (selectedIndex - 1 + items.Count) % items.Count;
            }

            GUI.Label(new Rect(left + 34f, y, width - 124f, rowH), label, GUI.skin.label);
            if (GUI.Button(new Rect(left + width - 84f, y, 28f, rowH), this.L("homeland_farm.next")))
            {
                selectedIndex = (selectedIndex + 1) % items.Count;
            }

            GUI.Label(new Rect(left + width - 50f, y, 50f, rowH), (selectedIndex + 1) + "/" + items.Count, GUI.skin.label);
            return y + rowH + 8f;
        }

        private bool TryHomelandFarmWarmupPrefetchInteropMethods()
        {
            this.TryEnsureHomelandFarmInteropAssembliesLoaded();
            this.EnsureHomelandFarmSowManagedReflection();

            if (this.homelandFarmCropSeedingInteropMethod == null)
            {
                if (this.homelandFarmCropSeedingMethod != null)
                {
                    this.homelandFarmCropSeedingInteropMethod = this.homelandFarmCropSeedingMethod;
                }
                else
                {
                    Type cropProtocolType = this.homelandFarmCropProtocolManagerType
                        ?? this.ResolveHomelandFarmManagedType(
                            "CropProtocolManager",
                            "XDTDataAndProtocol.ProtocolService.Plant.CropProtocolManager");
                    if (cropProtocolType != null)
                    {
                        if (this.homelandFarmCropProtocolManagerType == null)
                        {
                            this.homelandFarmCropProtocolManagerType = cropProtocolType;
                        }

                        this.homelandFarmCropSeedingInteropMethod = this.ResolveHomelandFarmCropSeedingMethod()
                            ?? this.GetMethodByNameAndParamCountQuiet(cropProtocolType, "CropSeeding", 2);
                    }
                }
            }

            if (this.homelandFarmCropAddManureInteropMethod == null)
            {
                Type cropProtocolType = this.homelandFarmCropProtocolManagerType
                    ?? this.ResolveHomelandFarmManagedType(
                        "CropProtocolManager",
                        "XDTDataAndProtocol.ProtocolService.Plant.CropProtocolManager");
                if (cropProtocolType != null)
                {
                    if (this.homelandFarmCropProtocolManagerType == null)
                    {
                        this.homelandFarmCropProtocolManagerType = cropProtocolType;
                    }

                    this.homelandFarmCropAddManureInteropMethod = this.ResolveHomelandFarmListOnlyStaticMethod(
                        cropProtocolType,
                        "AddManure");
                }
            }

            return this.homelandFarmCropSeedingInteropMethod != null || this.homelandFarmCropAddManureInteropMethod != null;
        }

        private void LogHomelandFarmWarmupPrefetchSummary()
        {
            this.HomelandFarmLog(
                "Warmup cache: managed=" + this.homelandFarmManagedReflectionReady
                + " aura=" + this.homelandFarmAuraReflectionReady
                + " componentData=" + this.TryEnsureHomelandFarmComponentDataManagedReflection()
                + " scanner=" + this.homelandFarmScannerTypesResolved
                + " auraFarm=" + this.auraFarmMethodsReady
                + " sow=" + (this.homelandFarmCropPlantPointType != null && this.homelandFarmCropSeedingMethod != null)
                + " seedingInterop=" + (this.homelandFarmCropSeedingInteropMethod != null)
                + " toolEquip=" + this.homelandFarmToolEquipTypesResolved
                + " inventory=" + this.homelandFarmInventoryReflectionResolved
                + " levelObjectCache=" + this.homelandFarmAuraLevelObjectPositionCache.Count + ".");
        }

        private void EnsureHomelandFarmWarmupStarted()
        {
            if (this.homelandFarmWarmupStarted)
            {
                return;
            }

            if (!this.IsHomelandFarmSceneLoadFinished())
            {
                return;
            }

            this.homelandFarmWarmupStarted = true;
            this.homelandFarmWarmupCoroutine = ModCoroutines.Start(this.HomelandFarmWarmupRoutine());
        }

        // Resolve the heavy reflection / native-method lookups ahead of the first action, spread across
        // frames so the one-time ~several-second cost does not freeze the first water/harvest/sow click.
        private IEnumerator HomelandFarmWarmupRoutine()
        {
            yield return null;
            this.TryEnsureHomelandFarmInteropAssembliesLoaded();
            yield return null;
            this.EnsureHomelandFarmReflectionReady();
            yield return null;
            this.TryEnsureHomelandFarmComponentDataManagedReflection();
            yield return null;
            this.EnsureHomelandFarmScannerTypes();
            this.TryEnsureHomelandFarmEntitiesGetComponentsReady(out string getComponentsStatus);
            if (!string.IsNullOrEmpty(getComponentsStatus))
            {
                this.HomelandFarmLog("Warmup GetComponents: " + getComponentsStatus);
            }

            while (!this.auraFarmMethodsReady)
            {
                this.ResolveAuraFarmRuntimeMethods();
                if (this.auraFarmMethodsReady)
                {
                    break;
                }

                yield return null;
            }

            yield return null;
            this.EnsureNoclipVehicleAuraMono(logIfPending: true);
            this.TryResolveHomelandFarmAuraProtocol(out _);
            this.TryResolveHomelandFarmAuraScanClasses(out _);
            yield return null;
            // Resolve + log the farm component classes (Plant / CropBox / Crop) up front so the
            // first scan never pays the resolution cost and we can see what resolved in the log.
            this.HomelandFarmResolveFarmComponentClassesInternal(logResults: true);
            // EXPERIMENT (Option 4): probe the AuraMono Entities.GetComponents path readiness up
            // front so the log shows whether the direct-ECS source can run before any scan.
            if (HomelandFarmAllowUnsafeAuraMonoGetComponents)
            {
                bool auraGetCompReady = this.TryHomelandFarmIsAuraMonoGetComponentsReady(out string auraGetCompStatus);
                this.HomelandFarmLog("Warmup GetComponents[AuraMono]: ready=" + auraGetCompReady + " (" + auraGetCompStatus + ").");
            }

            yield return null;
            this.EnsureHomelandFarmSowManagedReflection();
            this.TryHomelandFarmWarmupPrefetchInteropMethods();
            yield return null;
            this.TryHomelandFarmEnsureToolEquipTypes();
            this.TryHomelandFarmEnsureNetworkCommandTypes(out _);
            yield return null;
            this.EnsureHomelandFarmInventoryReflection();
            this.EnsureHomelandFarmTableDataReflection();
            this.EnsureHomelandFarmLocalPlayerComponentType();
            this.EnsureHomelandFarmPlayerDataCenterType();
            this.TryResolveHomelandFarmCropSeedEntityType(out _);
            this.TryResolveHomelandFarmCropFertilizerEntityType(out _);
            this.TryResolveHomelandFarmSprinklerEntityType(out _);
            yield return null;
            this.TryHomelandFarmResolveAuraCropPlantPointMembers(out _);
            yield return null;
            this.TryHomelandFarmCacheAuraLevelObjectPositions(true, allowDictionaryScan: true);
            this.LogHomelandFarmWarmupPrefetchSummary();
            this.HomelandFarmLog("Warmup complete (reflection + scanner + sow + inventory + level-object cache prefetched).");
            this.homelandFarmWarmupComplete = true;
            this.homelandFarmWarmupCoroutine = null;
        }

        private bool IsHomelandFarmWarmupReady()
        {
            return this.homelandFarmWarmupComplete && this.auraFarmMethodsReady;
        }

        private float DrawHomelandFarmTab(int startY)
        {
            this.EnsureHomelandFarmWarmupStarted();
            float y = startY;
            const float left = 40f;
            const float width = 520f;
            Color textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB);
            GUIStyle labelStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            labelStyle.normal.textColor = textColor;
            GUIStyle sectionStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold };
            sectionStyle.normal.textColor = new Color(textColor.r, textColor.g, textColor.b, 0.9f);
            GUIStyle statusStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, wordWrap = true };
            statusStyle.normal.textColor = new Color(textColor.r, textColor.g, textColor.b, 0.82f);
            bool busy = this.IsHomelandFarmBusy();
            bool farmInteractive = !busy && this.IsHomelandFarmWarmupReady();

            // 1. AUTO FARMING — capture planters, then loop sow -> weed -> harvest.
            Rect autoRect = new Rect(left, y, width, 150f);
            GUI.Box(autoRect, string.Empty, this.themePanelStyle ?? GUI.skin.box);
            this.DrawCardOutline(autoRect, 1f);
            GUI.Label(new Rect(autoRect.x + 16f, autoRect.y + 12f, 300f, 20f), this.L("homeland_farm.auto_section"), labelStyle);
            float autoY = autoRect.y + 38f;

            GUI.enabled = farmInteractive && !this.homelandFarmAutoRunning;
            if (GUI.Button(new Rect(autoRect.x + 16f, autoY, 200f, 28f), this.L("homeland_farm.auto_capture"), this.themePrimaryButtonStyle ?? GUI.skin.button))
            {
                this.CaptureHomelandFarmAutoPlanters();
            }

            GUI.enabled = true;
            GUI.Label(new Rect(autoRect.x + 226f, autoY + 5f, width - 242f, 18f),
                this.homelandFarmAutoCaptured
                    ? this.LF("homeland_farm.auto_captured", this.homelandFarmAutoPlanterCount)
                    : this.L("homeland_farm.auto_not_captured"),
                sectionStyle);
            autoY += 36f;

            GUI.Label(new Rect(autoRect.x + 16f, autoY, width - 32f, 18f), this.L("homeland_farm.auto_hint"), sectionStyle);
            autoY += 24f;

            if (this.homelandFarmAutoRunning)
            {
                if (GUI.Button(new Rect(autoRect.x + 16f, autoY, width - 32f, 32f), this.L("homeland_farm.auto_stop"), this.themeDangerButtonStyle ?? GUI.skin.button))
                {
                    this.StopHomelandFarmCoroutine();
                    this.homelandFarmLastStatus = "homeland_farm.status_stopped";
                }
            }
            else
            {
                GUI.enabled = farmInteractive
                    && this.homelandFarmAutoCaptured
                    && this.homelandFarmAutoPlanterCount > 0
                    && this.homelandFarmScannedSeeds.Count > 0;
                if (GUI.Button(new Rect(autoRect.x + 16f, autoY, width - 32f, 32f), this.L("homeland_farm.auto_start"), this.themePrimaryButtonStyle ?? GUI.skin.button))
                {
                    this.StartHomelandFarmAuto();
                }

                GUI.enabled = true;
            }

            y += 162f;

            // 2. FARM RADIUS — single slider drives every radius-based action below.
            GUIStyle valueLabelStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleRight };
            valueLabelStyle.normal.textColor = textColor;
            GUIStyle sliderLabelStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold };
            sliderLabelStyle.normal.textColor = new Color(textColor.r, textColor.g, textColor.b, 0.78f);

            Rect radiusRect = new Rect(left, y, width, 70f);
            GUI.Box(radiusRect, string.Empty, this.themePanelStyle ?? GUI.skin.box);
            this.DrawCardOutline(radiusRect, 1f);
            GUI.Label(new Rect(radiusRect.x + 16f, radiusRect.y + 10f, 300f, 18f), this.L("homeland_farm.radius_section"), labelStyle);
            float radiusInner = width - 32f;
            float radiusSliderY = radiusRect.y + 34f;
            GUI.Label(new Rect(radiusRect.x + 16f, radiusSliderY, radiusInner * 0.55f, 18f), this.L("homeland_farm.radius_slider_label"), sliderLabelStyle);
            GUI.Label(new Rect(radiusRect.x + 16f + radiusInner * 0.55f, radiusSliderY, radiusInner * 0.45f, 18f), $"{this.homelandFarmWaterRadius:F0}m", valueLabelStyle);
            radiusSliderY += 20f;
            this.homelandFarmWaterRadius = Mathf.Round(this.DrawAccentSlider(
                new Rect(radiusRect.x + 16f, radiusSliderY, radiusInner, 20f),
                this.homelandFarmWaterRadius,
                HomelandFarmMinWaterRadius,
                HomelandFarmMaxWaterRadius));
            // Persist the radius to config (debounced) when the slider value settles.
            if (this.homelandFarmWaterRadiusLastSeen < 0f)
            {
                // First draw — adopt the loaded value without scheduling a save.
                this.homelandFarmWaterRadiusLastSeen = this.homelandFarmWaterRadius;
            }
            else if (this.homelandFarmWaterRadius != this.homelandFarmWaterRadiusLastSeen)
            {
                this.homelandFarmWaterRadiusLastSeen = this.homelandFarmWaterRadius;
                this.homelandFarmWaterRadiusSavePending = true;
                this.homelandFarmWaterRadiusSaveAt = Time.realtimeSinceStartup + HomelandFarmRadiusSaveDebounceSeconds;
            }

            if (this.homelandFarmWaterRadiusSavePending && Time.realtimeSinceStartup >= this.homelandFarmWaterRadiusSaveAt)
            {
                this.homelandFarmWaterRadiusSavePending = false;
                this.PersistHomelandFarmRadius();
            }
            y += 82f;

            // 3. CROPS — scan + select the seed used by Sow / Auto farm.
            Rect sowRect = new Rect(left, y, width, 160f);
            GUI.Box(sowRect, string.Empty, this.themePanelStyle ?? GUI.skin.box);
            this.DrawCardOutline(sowRect, 1f);
            GUI.Label(new Rect(sowRect.x + 16f, sowRect.y + 12f, 200f, 20f), this.L("homeland_farm.sow_section"), labelStyle);
            float sowY = sowRect.y + 36f;
            GUI.Label(new Rect(sowRect.x + 16f, sowY, width - 32f, 18f), this.L("homeland_farm.seed_storage"), sectionStyle);
            sowY += 22f;
            GUI.enabled = farmInteractive;
            HomelandFarmStorageSource seedStorage = this.homelandFarmSeedStorage;
            sowY = this.DrawHomelandFarmStorageSourceToggle(sowRect.x + 16f, sowY, width - 32f, this.homelandFarmSeedStorage, out seedStorage);
            if (seedStorage != this.homelandFarmSeedStorage)
            {
                this.homelandFarmSeedStorage = seedStorage;
            }

            if (GUI.Button(new Rect(sowRect.x + 16f, sowY, 120f, 24f), this.L("homeland_farm.refresh_seeds"), this.themePrimaryButtonStyle ?? GUI.skin.button))
            {
                this.RefreshHomelandFarmSeeds();
            }

            GUI.Label(new Rect(sowRect.x + 146f, sowY + 4f, 200f, 18f),
                this.homelandFarmScannedSeeds.Count > 0
                    ? this.LF("homeland_farm.cached_seeds", this.homelandFarmScannedSeeds.Count)
                    : this.L("homeland_farm.press_refresh_seeds"),
                sectionStyle);
            sowY += 32f;
            sowY = this.DrawHomelandFarmInventorySelector(
                sowRect.x + 16f,
                sowY,
                width - 32f,
                this.homelandFarmScannedSeeds,
                ref this.homelandFarmSelectedSeedIndex,
                this.L("homeland_farm.no_seeds"));
            GUI.enabled = true;
            y += 172f;

            // 4. FERTILIZER — scan + select the fertilizer used by Fertilize.
            Rect fertRect = new Rect(left, y, width, 160f);
            GUI.Box(fertRect, string.Empty, this.themePanelStyle ?? GUI.skin.box);
            this.DrawCardOutline(fertRect, 1f);
            GUI.Label(new Rect(fertRect.x + 16f, fertRect.y + 12f, 220f, 20f), this.L("homeland_farm.fertilize_section"), labelStyle);
            float fertY = fertRect.y + 36f;
            GUI.Label(new Rect(fertRect.x + 16f, fertY, width - 32f, 18f), this.L("homeland_farm.fert_storage"), sectionStyle);
            fertY += 22f;
            GUI.enabled = farmInteractive;
            HomelandFarmStorageSource fertStorage = this.homelandFarmFertStorage;
            fertY = this.DrawHomelandFarmStorageSourceToggle(fertRect.x + 16f, fertY, width - 32f, this.homelandFarmFertStorage, out fertStorage);
            if (fertStorage != this.homelandFarmFertStorage)
            {
                this.homelandFarmFertStorage = fertStorage;
            }

            if (GUI.Button(new Rect(fertRect.x + 16f, fertY, 140f, 24f), this.L("homeland_farm.refresh_fertilizers"), this.themePrimaryButtonStyle ?? GUI.skin.button))
            {
                this.RefreshHomelandFarmFertilizers();
            }

            GUI.Label(new Rect(fertRect.x + 166f, fertY + 4f, 220f, 18f),
                this.homelandFarmScannedFertilizers.Count > 0
                    ? this.LF("homeland_farm.cached_fertilizers", this.homelandFarmScannedFertilizers.Count)
                    : this.L("homeland_farm.press_refresh_fertilizers"),
                sectionStyle);
            fertY += 32f;
            fertY = this.DrawHomelandFarmInventorySelector(
                fertRect.x + 16f,
                fertY,
                width - 32f,
                this.homelandFarmScannedFertilizers,
                ref this.homelandFarmSelectedFertilizerIndex,
                this.L("homeland_farm.no_fertilizers"));
            GUI.enabled = true;
            y += 172f;

            // 5. ACTION BUTTONS — use the radius + selected seed/fertilizer above.
            Rect opsRect = new Rect(left, y, width, 196f);
            GUI.Box(opsRect, string.Empty, this.themePanelStyle ?? GUI.skin.box);
            this.DrawCardOutline(opsRect, 1f);
            GUI.Label(new Rect(opsRect.x + 16f, opsRect.y + 12f, 300f, 20f), this.L("homeland_farm.operations_section"), labelStyle);

            const float buttonW = 230f;
            const float buttonH = 30f;
            const float buttonGapX = 12f;
            const float buttonGapY = 8f;
            float col1 = opsRect.x + 16f;
            float col2 = opsRect.x + 16f + buttonW + buttonGapX;
            float rowY = opsRect.y + 40f;
            GUI.enabled = farmInteractive;
            if (GUI.Button(new Rect(col1, rowY, buttonW, buttonH), this.L("homeland_farm.water_in_radius"), this.themePrimaryButtonStyle ?? GUI.skin.button))
            {
                this.StartHomelandFarmWater(HomelandFarmWaterMode.InRadius, silent: false);
            }

            if (GUI.Button(new Rect(col2, rowY, buttonW, buttonH), this.L("homeland_farm.harvest_crops_all"), this.themePrimaryButtonStyle ?? GUI.skin.button))
            {
                this.StartHomelandFarmHarvestCrops(silent: false);
            }

            rowY += buttonH + buttonGapY;
            if (GUI.Button(new Rect(col1, rowY, buttonW, buttonH), this.L("homeland_farm.weed_all"), this.themePrimaryButtonStyle ?? GUI.skin.button))
            {
                this.StartHomelandFarmWeedAll(silent: false);
            }

            if (GUI.Button(new Rect(col2, rowY, buttonW, buttonH), this.L("homeland_farm.collect_plant_seeds_all"), this.themePrimaryButtonStyle ?? GUI.skin.button))
            {
                this.StartHomelandFarmCollectPlantSeeds(silent: false);
            }

            rowY += buttonH + buttonGapY;
            if (GUI.Button(new Rect(col1, rowY, buttonW, buttonH), this.L("homeland_farm.sow"), this.themePrimaryButtonStyle ?? GUI.skin.button))
            {
                this.StartHomelandFarmSowAll(silent: false);
            }

            if (GUI.Button(new Rect(col2, rowY, buttonW, buttonH), this.L("homeland_farm.fertilize"), this.themePrimaryButtonStyle ?? GUI.skin.button))
            {
                this.StartHomelandFarmFertilizeAll(silent: false);
            }

            rowY += buttonH + buttonGapY;
            if (GUI.Button(new Rect(col1, rowY, width - 32f, buttonH), this.L("homeland_farm.log_water_radius"), this.themePrimaryButtonStyle ?? GUI.skin.button))
            {
                this.LogHomelandFarmRadiusWaterDiagnostics();
            }

            GUI.enabled = true;
            y += 208f;

            Rect statusRect = new Rect(left, y, width, 52f);
            GUI.Box(statusRect, string.Empty, this.themePanelStyle ?? GUI.skin.box);
            this.DrawCardOutline(statusRect, 1f);
            string statusKey = this.IsHomelandFarmWarmupReady()
                ? (this.homelandFarmLastStatus ?? "homeland_farm.status_idle")
                : "homeland_farm.status_warming";
            GUI.Label(new Rect(statusRect.x + 16f, statusRect.y + 10f, width - 32f, 36f), this.L(statusKey), statusStyle);
            y += 62f;

            GUI.enabled = this.homelandFarmCoroutine != null;
            if (GUI.Button(new Rect(left, y, 160f, 30f), this.L("homeland_farm.stop"), this.themePrimaryButtonStyle ?? GUI.skin.button))
            {
                this.StopHomelandFarmCoroutine();
                this.homelandFarmLastStatus = "homeland_farm.status_stopped";
            }

            GUI.enabled = true;
            return y + 40f;
        }

        private void HomelandFarmLog(string msg)
        {
            if (!HomelandFarmLogsEnabled || string.IsNullOrEmpty(msg))
            {
                return;
            }

            ModLogger.Msg("[HomelandFarm] " + msg);
        }

        // Persist the farm radius to config. The radius lives in the Keybinds config section, which
        // PopulateAllConfigSections already serializes; the slider just never triggered a save on its
        // own, so changing only the radius was lost on restart. Quiet (no keybind-save notification).
        private void PersistHomelandFarmRadius()
        {
            try
            {
                UnifiedConfigData data = this.LoadOrCreateUnifiedConfig();
                this.PopulateAllConfigSections(data);
                this.SaveUnifiedConfig(data);
                this.HomelandFarmLog("Saved farm radius=" + this.homelandFarmWaterRadius.ToString("F0") + "m to config.");
            }
            catch (Exception ex)
            {
                this.HomelandFarmLog("Failed to save farm radius: " + ex.Message);
            }
        }

        // Verbose per-call AuraMono GetComponents diagnostics (step1..step6 / step3a..3d). These
        // were invaluable while bringing up the direct-ECS scan path but are far too noisy for
        // normal runs (printed per component type per scan). Flip on only when re-debugging the
        // AuraMono inflate/invoke path. Failures still log via the normal HomelandFarmLog.
        private const bool HomelandFarmVerboseAuraGetComponentsLogs = false;

        private void HomelandFarmVerboseLog(string msg)
        {
            if (!HomelandFarmVerboseAuraGetComponentsLogs)
            {
                return;
            }

            this.HomelandFarmLog(msg);
        }
    }
}
