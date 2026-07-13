using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppInterop.Runtime.Runtime;
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

using UnityObject = UnityEngine.Object;
using Il2CppType = Il2CppSystem.Type;
using Il2CppFieldInfo = Il2CppSystem.Reflection.FieldInfo;
using Il2CppMethodInfo = Il2CppSystem.Reflection.MethodInfo;
using Il2CppPropertyInfo = Il2CppSystem.Reflection.PropertyInfo;
using Il2CppBindingFlags = Il2CppSystem.Reflection.BindingFlags;
using Il2CppObject = Il2CppSystem.Object;
using Object = UnityEngine.Object;


namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        private string GetConfigPath()
        {
            HelperPaths.TryMigrateLegacyUserData(AppDomain.CurrentDomain.BaseDirectory);
            return HelperPaths.GetFile("Config.xml");
        }

        private string GetKeybindsPath()
        {
            return HelperPaths.GetFile("keybinds.json");
        }

        private UnifiedConfigData LoadUnifiedConfig()
        {
            try
            {
                string path = this.GetConfigPath();
                if (!File.Exists(path)) return null;
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json)) return null;
                XmlSerializer serializer = new XmlSerializer(typeof(UnifiedConfigData));
                UnifiedConfigData data;
                using (StringReader reader = new StringReader(json))
                {
                    data = serializer.Deserialize(reader) as UnifiedConfigData;
                }
                if (data == null) return null;
                if (data.Keybinds == null) data.Keybinds = new KeybindConfigData();
                if (data.UiTheme == null) data.UiTheme = new UiThemeConfigData();
                if (data.Radar == null) data.Radar = new RadarConfigData();
                if (data.BirdFarm == null) data.BirdFarm = new BirdFarmConfigData();
                if (data.Patrol == null) data.Patrol = new PatrolData();
                if (data.Patrol.Points == null) data.Patrol.Points = new List<SerializableVector3>();
                if (string.IsNullOrWhiteSpace(data.Language)) data.Language = "en";
                if (data.CustomTeleports == null) data.CustomTeleports = new List<CustomTeleportEntry>();
                if (data.FishingRouteSpots == null) data.FishingRouteSpots = new List<CustomTeleportEntry>();
                return data;
            }
            catch (Exception ex)
            {
                ModLogger.Msg("Error loading unified config: " + ex.Message);
                return null;
            }
        }

        private UnifiedConfigData LoadOrCreateUnifiedConfig()
        {
            return this.LoadUnifiedConfig() ?? new UnifiedConfigData();
        }

        private void SaveUnifiedConfig(UnifiedConfigData data)
        {
            if (data == null) data = new UnifiedConfigData();
            string path = this.GetConfigPath();
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            XmlSerializer serializer = new XmlSerializer(typeof(UnifiedConfigData));
            XmlWriterSettings settings = new XmlWriterSettings
            {
                Indent = true,
                IndentChars = "  ",
                NewLineChars = Environment.NewLine,
                NewLineHandling = NewLineHandling.Replace,
                Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)
            };
            using (FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read))
            using (XmlWriter writer = XmlWriter.Create(stream, settings))
            {
                serializer.Serialize(writer, data);
            }
        }

        private void PopulateKeybindConfig(KeybindConfigData data)
        {
            data.keyToggleMenu = (int)this.keyToggleMenu;
            data.keyToggleRadar = (int)this.keyToggleRadar;
            data.keyAuraFarm = (int)this.keyAuraFarm;
            data.keyWaterWeedRadius = (int)this.keyWaterWeedRadius;
            data.keyAutoFish = (int)this.keyAutoFish;
            data.keyAutoFishingTeleport = (int)this.keyAutoFishingTeleport;
            data.keyAutoFishShadowNet = (int)this.keyAutoFishShadowNet;
            data.keyBypassUI = (int)this.keyBypassUI;
            data.keyDisableAll = (int)this.keyDisableAll;
            data.keyInspectPlayer = (int)this.keyInspectPlayer;
            data.keyInspectMove = (int)this.keyInspectMove;
            data.keyAutoRepair = (int)this.keyAutoRepair;
            data.keyAutoJoinFriend = (int)this.keyAutoJoinFriend;
            data.keyJoinPublic = (int)this.keyJoinPublic;
            data.keyJoinMyTown = (int)this.keyJoinMyTown;
            data.keyNoclip = (int)this.keyNoclip;
            data.keyCameraToggle = (int)this.keyCameraToggle;
            data.keyAutoIceSkating = (int)this.keyAutoIceSkating;
            data.keyAutoEat = (int)this.keyAutoEat;
            data.keyUseBait = (int)this.keyUseBait;
            data.keyUseAttractor = (int)this.keyUseAttractor;
            data.keyAntiAfk = (int)this.keyAntiAfk;
            data.keyBypassOverlap = (int)this.keyBypassOverlap;
            data.keyBirdVacuum = (int)this.keyBirdVacuum;
            data.keyAutoSnow = (int)this.autoSnowHotkey;
            data.keyAutoSand = (int)this.autoSandHotkey;
            data.keySeaCleanQte = (int)this.seaCleanQteHotkey;
            data.keyEquipSeaCleaner = (int)this.keyEquipSeaCleaner;
            data.seaCleanAutoRadius = this.seaCleanAutoRadius;
            data.seaCleanCleanNoDelay = this.seaCleanCleanNoDelay;
            data.autoCleanseCorruptedEnabled = this.autoCleanseCorruptedEnabled;
            data.hideSeaCleanBannerEnabled = this.hideSeaCleanBannerEnabled;
            data.littleWhaleFinderEnabled = this.littleWhaleFinderEnabled;
            data.swimSprintTweakEnabled = this.swimSprintTweakEnabled;
            data.swimSprintDurationSeconds = this.swimSprintDurationSeconds;
            data.swimSprintCooldownSeconds = this.swimSprintCooldownSeconds;
            data.swimSprintVerticalGuardEnabled = this.swimSprintVerticalGuardEnabled;
            data.keyGameSpeed1x = (int)this.keyGameSpeed1x;
            data.keyGameSpeed2x = (int)this.keyGameSpeed2x;
            data.keyGameSpeed5x = (int)this.keyGameSpeed5x;
            data.keyGameSpeed10x = (int)this.keyGameSpeed10x;
            data.keyEquipAxe = (int)this.keyEquipAxe;
            data.keyEquipNet = (int)this.keyEquipNet;
            data.keyEquipRod = (int)this.keyEquipRod;
            data.keyEquipSprinkler = (int)this.keyEquipSprinkler;
            data.keyEquipBirdScanner = (int)this.keyEquipBirdScanner;
            data.keyEquipPad = (int)this.keyEquipPad;
            data.keyPadConfirm = (int)this.keyPadConfirm;
            data.keyPadCancel = (int)this.keyPadCancel;
            data.keyPadRotate = (int)this.keyPadRotate;
            data.keyPadMove = (int)this.keyPadMove;
            data.keyPadDelete = (int)this.keyPadDelete;
            data.keyAutoInsectFarm = (int)this.keyAutoInsectFarm;
            data.keyAutoBirdFarm = (int)this.keyAutoBirdFarm;
            data.keyMassCook = (int)this.keyMassCook;
            data.keyAutoPuzzle = (int)this.keyAutoPuzzle;
            data.keyAutoCatPlay = (int)this.keyAutoCatPlay;
            data.keyAutoDogTrain = (int)this.keyAutoDogTrain;
            data.keyAutoPetWash = (int)this.keyAutoPetWash;
            data.keyFeedAllCats = (int)this.keyFeedAllCats;
            data.keyFeedAllDogs = (int)this.keyFeedAllDogs;
            data.keySpawnBubble = (int)this.keySpawnBubble;
            data.noclipSpeed = this.noclipSpeed;
            data.noclipBoostMultiplier = this.noclipBoostMultiplier;
            data.areaLoadDelay = this.areaLoadDelay;
            data.auraCollectWaitTimeout = this.auraCollectWaitTimeout;
            data.foragingTeleportDelaySeconds = this.foragingTeleportDelaySeconds;
            data.resourceAutoRepairPauseSeconds = this.resourceAutoRepairPauseSeconds;
            data.gameSpeed = this.gameSpeed;
            data.fpsBypassEnabled = this.fpsBypassEnabled;
            data.fpsBypassTarget = this.fpsBypassTarget;
            data.lodOverrideMode = this.lodOverrideMode;
            data.lodCustomBias = this.lodCustomBias;
            data.lodCustomMaxLevel = this.lodCustomMaxLevel;
            data.customCameraFOVEnabled = this.customCameraFOVEnabled;
            data.cameraFOV = this.cameraFOV;
            data.hideJumpButtonEnabled = this.hideJumpButtonEnabled;
            data.bunnyHopEnabled = this.bunnyHopEnabled;
            data.analogMoveBridgeEnabled = this.analogMoveBridgeEnabled;
            data.skipShowOffAnimations = this.skipShowOffAnimations;
            data.persistentHudEnabled = this.persistentHudEnabled;
            data.autoIceSkatingEnabled = this.autoIceSkatingEnabled;
            data.autoIceSkatingMinUltimateScore = this.autoIceSkatingMinUltimateScore;
            data.autoIceSkatingOnlyX2Ultimate = this.autoIceSkatingOnlyX2Ultimate;
            data.autoIceSkatingLast30sUltimate = this.autoIceSkatingLast30sUltimate;
            data.autoIceSkatingPerfectMove = this.autoIceSkatingPerfectMove;
            data.autoIceSkatingPreferNewMove = this.autoIceSkatingPreferNewMove;
            data.iceSkatingChallengeEndScore = this.iceSkatingChallengeEndScore;
            data.shopBuyAllMaxPerItem = this.shopBuyAllMaxPerItem;
            data.fastBubbleGenEnabled = this.fastBubbleGenEnabled;
            data.bubbleBubblesPerMinute = this.bubbleBubblesPerMinute;
            data.bubbleSpawnAtPlayerEnabled = this.bubbleSpawnAtPlayerEnabled;
            data.autoBubbleCollectEnabled = this.autoBubbleCollectEnabled;
            data.autoBubbleCollectRadius = this.autoBubbleCollectRadius;
            data.cookingAutoSpeed = this.cookingAutoSpeed;
            data.netCookInterval = this.netCookInterval;
            data.netCookScanRadiusMeters = this.netCookScanRadiusMeters;
            data.netCookMiniGameOnly = this.netCookMiniGameOnly;
            data.netCookMoveIngredients = this.netCookMoveIngredients;
            data.netCookRememberStoves = this.netCookRememberStoves;
            data.netCookCaptureOwnOnly = this.netCookCaptureOwnOnly;
            data.netCookCaptureRadiusOnly = this.netCookCaptureRadiusOnly;
            data.netCookUseAllIngredients = this.netCookUseAllIngredients;
            data.netCookCookQuantity = 1;
            data.homelandFarmWaterRadius = this.homelandFarmWaterRadius;
            data.homelandFarmAutoFertilizeEnabled = this.homelandFarmAutoFertilizeEnabled;
            data.autoFishScanTimeout = -1f;
            data.autoFishTeleportDelay = -1f;
            // While a fishing route is active the live range/toggles are the route's forced
            // values (200m, both on); persist the user's pre-route snapshot instead.
            data.autoFishFishShadowDetectRange = FishingRouteFeature.Active ? FishingRouteFeature.SnapshotDetectRange : AutoFishingFarm.GetDetectRange();
            data.autoFishInstantCatchSendHz = -1f; // send rate is not persisted; always 0 at init
            data.autoFishInstantCatch = AutoFishingFarm.GetInstantCatchEnabled();
            data.autoFishAutoBaitEnabled = false;  // auto-bait toggle is not persisted; always off at start
            data.autoFishAutoBaitChoice = AutoFishingFarm.GetAutoBaitChoice();
            data.autoFishAutoBaitMax = AutoFishingFarm.GetAutoBaitMaxCount();
            data.autoFishAutoBaitNoFishSeconds = AutoFishingFarm.GetAutoBaitNoFishSeconds();
            data.autoFishSkipCatchAnim = AutoFishingFarm.GetSkipCatchAnimEnabled();
            data.autoFishSkipCastAnim = AutoFishingFarm.GetSkipCastAnimEnabled();
            data.autoFishSkipBaitAnim = AutoFishingFarm.GetSkipBaitAnimEnabled();
            data.fishingRouteCustomOnly = FishingRouteFeature.GetCustomSpotsOnly();
            data.autoFishReelMaxDuration = -1f;
            data.autoFishReelHoldDuration = -1f;
            data.autoFishReelPauseDuration = -1f;
            data.insectCatchCooldown = InsectNetFarm.GetCatchCooldown();
            data.insectScanRange = InsectNetFarm.GetScanRange();
            data.insectBatchSize = InsectNetFarm.GetBatchSize();
            data.insectTeleportEnabled = InsectNetFarm.GetTeleportEnabled();
            data.insectPauseTeleportOnTriggersEnabled = InsectNetFarm.GetPauseTeleportOnTriggersEnabled();
            data.insectPauseTeleportOnRepairEnabled = InsectNetFarm.GetPauseTeleportOnRepairEnabled();
            data.insectPauseTeleportOnEatEnabled = InsectNetFarm.GetPauseTeleportOnEatEnabled();
            data.insectRepairTeleportPauseSeconds = InsectNetFarm.GetRepairTeleportPauseSeconds();
            data.insectEatTeleportPauseSeconds = InsectNetFarm.GetEatTeleportPauseSeconds();
            data.notificationsEnabled = this.notificationsEnabled;
            data.notificationPosition = this.notificationPosition;
            data.blockGameUiWhenMenuOpen = this.blockGameUiWhenMenuOpen;
            data.privacyBlockLogUploads = this.privacyBlockLogUploads;
            data.privacyBlockRoomMerges = this.privacyBlockRoomMerges;
            data.privacyBlockSpamReports = this.privacyBlockSpamReports;
            data.privacyBlockUploadCheat = this.privacyBlockUploadCheat;
            data.autoClickStartEnabled = this.autoClickStartEnabled;
            data.autoCloseAnnouncementEnabled = this.autoCloseAnnouncementEnabled;
            data.maxAutoEatAttempts = this.maxAutoEatAttempts;
            data.showStatusOverlay = this.showStatusOverlay;
            data.hideIdEnabled = this.hideIdEnabled;
            data.customDisplayIdEnabled = this.customDisplayIdEnabled;
            data.customDisplayId = this.NormalizeCustomId(this.customDisplayId);
            data.antiAfkEnabled = this.antiAfkEnabled;
            data.mouseLookEnabled = this.mouseLookEnabled;
            data.showMouseLookCrosshair = this.showMouseLookCrosshair;
            data.antiAfkInterval = this.antiAfkInterval;
            data.autoRepairType = this.autoRepairType;
            data.autoRepairUseTarget = this.autoRepairUseTarget;
            data.autoEatFoodType = this.autoEatFoodType;
            data.autoEatCustomFoodName = this.autoEatCustomFoodName ?? "";
            data.repairTeleportBackEnabled = this.repairTeleportBackEnabled;
            data.autoRepairOnToastEnabled = FishingRouteFeature.Active ? FishingRouteFeature.SnapshotAutoRepair : this.autoRepairOnToastEnabled;
            data.autoEatOnToastEnabled = this.autoEatOnToastEnabled;
            data.autoEatAutoTriggerEnabled = FishingRouteFeature.Active ? FishingRouteFeature.SnapshotAutoEatPanel : this.autoEatAutoTriggerEnabled;
            data.autoEatNoAnimationEnabled = this.autoEatNoAnimationEnabled;
            data.autoRepairTriggerPercent = this.autoRepairTriggerPercent;
            data.autoEatTriggerPercent = this.autoEatTriggerPercent;
            data.autoSellEnabled = this.autoSellEnabled;
            data.autoSellItemKey = this.autoSellItemKey ?? "";
            data.autoSellMaxPerStack = this.autoSellMaxPerStack;
            data.autoSellReserveCount = this.autoSellReserveCount;
            data.autoSellAllMatchingStacks = this.autoSellAllMatchingStacks;
            data.autoSellFullStack = this.autoSellFullStack;
            data.dailyQuestSubmitSkipFiveStar = this.dailyQuestSubmitSkipFiveStar;
            data.autoSellMatchFamily = this.autoSellMatchFamily;
            data.autoSellHideBagItems = this.autoSellHideBagItems;
            data.autoSellSelectedStaticId = this.autoSellSelectedStaticId;
            data.autoSellSelectedStar = this.autoSellSelectedStar;
            data.autoSellInterval = this.autoSellInterval;
            data.autoSellScanSource = this.autoSellScanSource;
            data.autoSellFestivalTokensEnabled = this.autoSellFestivalTokensEnabled;
            data.auraFarmLootCollectEnabled = this.auraFarmLootCollectEnabled;
            data.auraFarmLootCollectDistance = this.auraFarmLootCollectDistance;
        }

        private void ApplyKeybindConfig(KeybindConfigData data)
        {
            if (data == null) return;
            this.keyToggleMenu = (KeyCode)data.keyToggleMenu;
            this.keyToggleRadar = (KeyCode)data.keyToggleRadar;
            this.keyAuraFarm = (KeyCode)data.keyAuraFarm;
            this.keyWaterWeedRadius = (KeyCode)data.keyWaterWeedRadius;
            this.keyAutoFish = (KeyCode)data.keyAutoFish;
            this.keyAutoFishingTeleport = (KeyCode)data.keyAutoFishingTeleport;
            this.keyAutoFishShadowNet = (KeyCode)data.keyAutoFishShadowNet;
            this.keyBypassUI = (KeyCode)data.keyBypassUI;
            this.keyDisableAll = (KeyCode)data.keyDisableAll;
            this.keyInspectPlayer = (KeyCode)data.keyInspectPlayer;
            this.keyInspectMove = (KeyCode)data.keyInspectMove;
            this.keyAutoRepair = (KeyCode)data.keyAutoRepair;
            this.keyAutoJoinFriend = (KeyCode)data.keyAutoJoinFriend;
            this.keyJoinPublic = (KeyCode)data.keyJoinPublic;
            this.keyJoinMyTown = (KeyCode)data.keyJoinMyTown;
            this.keyNoclip = (KeyCode)data.keyNoclip;
            this.keyCameraToggle = (KeyCode)data.keyCameraToggle;
            this.keyAutoIceSkating = (KeyCode)data.keyAutoIceSkating;
            this.keyAutoEat = (KeyCode)data.keyAutoEat;
            this.keyUseBait = (KeyCode)data.keyUseBait;
            this.keyUseAttractor = (KeyCode)data.keyUseAttractor;
            this.keyAntiAfk = (KeyCode)data.keyAntiAfk;
            this.keyBypassOverlap = (KeyCode)data.keyBypassOverlap;
            this.keyBirdVacuum = (KeyCode)data.keyBirdVacuum;
            this.autoSnowHotkey = (KeyCode)data.keyAutoSnow;
            this.autoSandHotkey = (KeyCode)data.keyAutoSand;
            this.seaCleanQteHotkey = (KeyCode)data.keySeaCleanQte;
            this.keyEquipSeaCleaner = (KeyCode)data.keyEquipSeaCleaner;
            this.seaCleanAutoRadius = data.seaCleanAutoRadius <= 0f
                ? SeaCleanAutoRadiusDefault
                : Mathf.Clamp(data.seaCleanAutoRadius, SeaCleanAutoRadiusMin, SeaCleanAutoRadiusMax);
            this.seaCleanCleanNoDelay = data.seaCleanCleanNoDelay;
            this.autoCleanseCorruptedEnabled = data.autoCleanseCorruptedEnabled;
            this.hideSeaCleanBannerEnabled = data.hideSeaCleanBannerEnabled;
            this.littleWhaleFinderEnabled = data.littleWhaleFinderEnabled;
            this.swimSprintTweakEnabled = data.swimSprintTweakEnabled;
            this.swimSprintDurationSeconds = data.swimSprintDurationSeconds <= 0f
                ? SwimSprintDurationDefault
                : Mathf.Clamp(data.swimSprintDurationSeconds, SwimSprintDurationMin, SwimSprintDurationMax);
            this.swimSprintCooldownSeconds = Mathf.Clamp(data.swimSprintCooldownSeconds, SwimSprintCooldownMin, SwimSprintCooldownMax);
            this.swimSprintVerticalGuardEnabled = data.swimSprintVerticalGuardEnabled;
            this.keyGameSpeed1x = (KeyCode)data.keyGameSpeed1x;
            this.keyGameSpeed2x = (KeyCode)data.keyGameSpeed2x;
            this.keyGameSpeed5x = (KeyCode)data.keyGameSpeed5x;
            this.keyGameSpeed10x = (KeyCode)data.keyGameSpeed10x;
            this.keyEquipAxe = (KeyCode)data.keyEquipAxe;
            this.keyEquipNet = (KeyCode)data.keyEquipNet;
            this.keyEquipRod = (KeyCode)data.keyEquipRod;
            this.keyEquipSprinkler = (KeyCode)data.keyEquipSprinkler;
            this.keyEquipBirdScanner = (KeyCode)data.keyEquipBirdScanner;
            this.keyEquipPad = (KeyCode)data.keyEquipPad;
            this.keyPadConfirm = (KeyCode)data.keyPadConfirm;
            this.keyPadCancel = (KeyCode)data.keyPadCancel;
            this.keyPadRotate = (KeyCode)data.keyPadRotate;
            this.keyPadMove = (KeyCode)data.keyPadMove;
            this.keyPadDelete = (KeyCode)data.keyPadDelete;
            this.keyAutoInsectFarm = (KeyCode)data.keyAutoInsectFarm;
            this.keyAutoBirdFarm = (KeyCode)data.keyAutoBirdFarm;
            this.keyMassCook = (KeyCode)data.keyMassCook;
            this.keyAutoPuzzle = (KeyCode)data.keyAutoPuzzle;
            this.keyAutoCatPlay = (KeyCode)data.keyAutoCatPlay;
            this.keyAutoDogTrain = (KeyCode)data.keyAutoDogTrain;
            this.keyAutoPetWash = (KeyCode)data.keyAutoPetWash;
            this.keyFeedAllCats = (KeyCode)data.keyFeedAllCats;
            this.keyFeedAllDogs = (KeyCode)data.keyFeedAllDogs;
            this.keySpawnBubble = (KeyCode)data.keySpawnBubble;
            this.noclipSpeed = data.noclipSpeed;
            this.noclipBoostMultiplier = data.noclipBoostMultiplier;
            this.areaLoadDelay = data.areaLoadDelay;
            this.auraCollectWaitTimeout = Mathf.Clamp(
                data.auraCollectWaitTimeout > 0f ? data.auraCollectWaitTimeout : 12f,
                4f,
                30f);
            this.foragingTeleportDelaySeconds = Mathf.Clamp(data.foragingTeleportDelaySeconds, 0f, 10f);
            this.resourceAutoRepairPauseSeconds = data.resourceAutoRepairPauseSeconds;
            this.gameSpeed = data.gameSpeed;
            this.fpsBypassEnabled = data.fpsBypassEnabled;
            this.fpsBypassTarget = Mathf.Clamp(data.fpsBypassTarget <= 0 ? 144 : data.fpsBypassTarget, 30, 360);
            this.lodOverrideMode = Mathf.Clamp(data.lodOverrideMode, 0, 3);
            this.lodCustomBias = Mathf.Clamp(data.lodCustomBias <= 0f ? 1f : data.lodCustomBias, 0.25f, 4f);
            this.lodCustomMaxLevel = Mathf.Clamp(data.lodCustomMaxLevel, 0, 4);
            this.SyncLodOverrideAfterConfigLoad();
            this.customCameraFOVEnabled = data.customCameraFOVEnabled;
            this.cameraFOV = data.cameraFOV;
            this.hideJumpButtonEnabled = data.hideJumpButtonEnabled;
            this.bunnyHopEnabled = data.bunnyHopEnabled;
            this.analogMoveBridgeEnabled = data.analogMoveBridgeEnabled;
            this.skipShowOffAnimations = data.skipShowOffAnimations;
            this.persistentHudEnabled = data.persistentHudEnabled;
            this.autoIceSkatingEnabled = data.autoIceSkatingEnabled;
            this.autoIceSkatingMinUltimateScore = Mathf.Clamp(data.autoIceSkatingMinUltimateScore, 0, AutoIceSkatingMinUltimateScoreSliderMax);
            this.autoIceSkatingOnlyX2Ultimate = data.autoIceSkatingOnlyX2Ultimate;
            this.autoIceSkatingLast30sUltimate = data.autoIceSkatingLast30sUltimate;
            this.autoIceSkatingPerfectMove = data.autoIceSkatingPerfectMove;
            this.autoIceSkatingPreferNewMove = data.autoIceSkatingPreferNewMove;
            this.iceSkatingChallengeEndScore = Mathf.Clamp(
                data.iceSkatingChallengeEndScore > 0 ? data.iceSkatingChallengeEndScore : 1500,
                0,
                999999);
            this.shopBuyAllMaxPerItem = Mathf.Clamp(
                data.shopBuyAllMaxPerItem > 0 ? data.shopBuyAllMaxPerItem : 200,
                1,
                999999);
            this.fastBubbleGenEnabled = data.fastBubbleGenEnabled;
            this.bubbleBubblesPerMinute = Mathf.Clamp(data.bubbleBubblesPerMinute, 0f, 100f);
            this.bubbleSpawnAtPlayerEnabled = data.bubbleSpawnAtPlayerEnabled;
            this.autoBubbleCollectEnabled = data.autoBubbleCollectEnabled;
            this.autoBubbleCollectRadius = Mathf.Clamp(data.autoBubbleCollectRadius, 0f, 100f);
            this.cookingAutoSpeed = data.cookingAutoSpeed;
            this.netCookInterval = Mathf.Clamp(data.netCookInterval > 0f ? data.netCookInterval : 1.5f, 0.25f, 10f);
            this.netCookScanRadiusMeters = Mathf.Clamp(data.netCookScanRadiusMeters > 0f ? data.netCookScanRadiusMeters : NetCookDefaultScanRadiusMeters, NetCookMinScanRadiusMeters, NetCookMaxScanRadiusMeters);
            this.netCookMiniGameOnly = data.netCookMiniGameOnly;
            this.netCookMoveIngredients = data.netCookMoveIngredients;
            this.netCookRememberStoves = data.netCookRememberStoves;
            this.netCookCaptureOwnOnly = data.netCookCaptureOwnOnly;
            this.netCookCaptureRadiusOnly = data.netCookCaptureRadiusOnly;
            this.netCookUseAllIngredients = data.netCookUseAllIngredients;
            this.ResetNetCookDishLimitToDefault();
            this.homelandFarmWaterRadius = Mathf.Clamp(data.homelandFarmWaterRadius > 0f ? data.homelandFarmWaterRadius : HomelandFarmDefaultWaterRadius, HomelandFarmMinWaterRadius, HomelandFarmMaxWaterRadius);
            this.homelandFarmAutoFertilizeEnabled = data.homelandFarmAutoFertilizeEnabled;
            this.saved_autoFishScanTimeout = data.autoFishScanTimeout;
            this.saved_autoFishTeleportDelay = data.autoFishTeleportDelay;
            this.saved_autoFishFishShadowDetectRange = data.autoFishFishShadowDetectRange;
            this.saved_autoFishReelMaxDuration = data.autoFishReelMaxDuration;
            this.saved_autoFishReelHoldDuration = data.autoFishReelHoldDuration;
            this.saved_autoFishReelPauseDuration = data.autoFishReelPauseDuration;
            if (data.insectCatchCooldown > 0f)
            {
                InsectNetFarm.SetCatchCooldown(data.insectCatchCooldown);
            }
            if (data.insectScanRange > 0f)
            {
                InsectNetFarm.SetScanRange(data.insectScanRange);
            }
            if (data.autoFishFishShadowDetectRange > 0f)
            {
                AutoFishingFarm.SetDetectRange(data.autoFishFishShadowDetectRange);
            }
            AutoFishingFarm.SetInstantCatchEnabled(data.autoFishInstantCatch);
            // Send rate is intentionally not restored — always starts at 0 (detour handles instant catch).
            // Auto Bait: restore choice/max/window settings, but NEVER the enabled toggle (always off at start).
            AutoFishingFarm.SetAutoBaitChoice(data.autoFishAutoBaitChoice);
            if (data.autoFishAutoBaitMax >= 0)
            {
                AutoFishingFarm.SetAutoBaitMaxCount(data.autoFishAutoBaitMax);
            }
            if (data.autoFishAutoBaitNoFishSeconds > 0f)
            {
                AutoFishingFarm.SetAutoBaitNoFishSeconds(data.autoFishAutoBaitNoFishSeconds);
            }
            AutoFishingFarm.SetSkipCatchAnimEnabled(data.autoFishSkipCatchAnim);
            AutoFishingFarm.SetSkipCastAnimEnabled(data.autoFishSkipCastAnim);
            AutoFishingFarm.SetSkipBaitAnimEnabled(data.autoFishSkipBaitAnim);
            FishingRouteFeature.SetCustomSpotsOnly(data.fishingRouteCustomOnly);
            if (data.insectBatchSize > 0)
            {
                InsectNetFarm.SetBatchSize(data.insectBatchSize);
            }
            InsectNetFarm.SetTeleportEnabled(data.insectTeleportEnabled);
            InsectNetFarm.SetPauseTeleportOnTriggersEnabled(
                data.insectPauseTeleportOnTriggersEnabled ||
                data.insectPauseTeleportOnRepairEnabled ||
                data.insectPauseTeleportOnEatEnabled);
            if (data.insectRepairTeleportPauseSeconds > 0f)
            {
                InsectNetFarm.SetRepairTeleportPauseSeconds(data.insectRepairTeleportPauseSeconds);
            }
            if (data.insectEatTeleportPauseSeconds > 0f)
            {
                InsectNetFarm.SetEatTeleportPauseSeconds(data.insectEatTeleportPauseSeconds);
            }
            this.notificationsEnabled = data.notificationsEnabled;
            this.notificationPosition = Mathf.Clamp(data.notificationPosition, 0, NotificationPositionOptions.Length - 1);
            this.blockGameUiWhenMenuOpen = data.blockGameUiWhenMenuOpen;
            this.privacyBlockLogUploads = data.privacyBlockLogUploads;
            this.privacyBlockRoomMerges = data.privacyBlockRoomMerges;
            this.privacyBlockSpamReports = data.privacyBlockSpamReports;
            this.privacyBlockUploadCheat = data.privacyBlockUploadCheat;
            this.autoClickStartEnabled = data.autoClickStartEnabled;
            this.autoCloseAnnouncementEnabled = data.autoCloseAnnouncementEnabled;
            this.maxAutoEatAttempts = data.maxAutoEatAttempts;
            this.showStatusOverlay = data.showStatusOverlay;
            this.hideIdEnabled = data.hideIdEnabled;
            this.customDisplayId = this.NormalizeCustomId(data.customDisplayId);
            this.customDisplayIdEnabled = data.customDisplayIdEnabled || !string.IsNullOrEmpty(this.customDisplayId);
            this.antiAfkEnabled = data.antiAfkEnabled;
            this.mouseLookEnabled = data.mouseLookEnabled;
            this.showMouseLookCrosshair = data.showMouseLookCrosshair || !data.mouseLookEnabled;
            this.antiAfkInterval = Mathf.Clamp(data.antiAfkInterval, 5f, 9f);
            this.autoRepairType = Mathf.Clamp(data.autoRepairType, 0, this.autoRepairOptions.Length - 1);
            this.autoRepairUseTarget = Mathf.Clamp(data.autoRepairUseTarget > 0 ? data.autoRepairUseTarget : 2, 1, 3);
            this.autoEatFoodType = Mathf.Clamp(data.autoEatFoodType, 0, this.autoEatFoodOptions.Length - 1);
            this.autoEatCustomFoodName = data.autoEatCustomFoodName ?? "";
            this.repairTeleportBackEnabled = data.repairTeleportBackEnabled;
            this.autoRepairOnToastEnabled = data.autoRepairOnToastEnabled;
            this.autoEatOnToastEnabled = data.autoEatOnToastEnabled;
            this.autoRepairTriggerPercent = Mathf.Clamp(data.autoRepairTriggerPercent > 0 ? data.autoRepairTriggerPercent : 10, 1, 100);
            bool hasAutoEatTriggerConfig = data.autoEatTriggerPercent > 0;
            this.autoEatAutoTriggerEnabled = hasAutoEatTriggerConfig && data.autoEatAutoTriggerEnabled;
            this.autoEatTriggerPercent = Mathf.Clamp(hasAutoEatTriggerConfig ? data.autoEatTriggerPercent : 20, 1, 100);
            this.autoEatNoAnimationEnabled = data.autoEatNoAnimationEnabled;
            bool hasAutoSellConfig = data.autoSellMaxPerStack > 0 || data.autoSellReserveCount > 0 || !string.IsNullOrWhiteSpace(data.autoSellItemKey);
            // Never auto-resume selling on boot/lobby load. The network sell path is only safe after the
            // player is fully in-world, so the user must enable it each session from the panel.
            this.autoSellEnabled = false;
            this.autoSellItemKey = data.autoSellItemKey ?? "";
            this.autoSellMaxPerStack = hasAutoSellConfig ? Mathf.Clamp(data.autoSellMaxPerStack, 0, 200) : 200;
            this.autoSellReserveCount = Mathf.Clamp(data.autoSellReserveCount, 0, 200);
            this.autoSellAllMatchingStacks = hasAutoSellConfig ? data.autoSellAllMatchingStacks : true;
            this.autoSellFullStack = hasAutoSellConfig ? data.autoSellFullStack : true;
            this.dailyQuestSubmitSkipFiveStar = data.dailyQuestSubmitSkipFiveStar;
            this.autoSellMatchFamily = hasAutoSellConfig ? data.autoSellMatchFamily : true;
            this.autoSellHideBagItems = hasAutoSellConfig && data.autoSellHideBagItems;
            this.autoSellSelectedStaticId = Mathf.Max(0, data.autoSellSelectedStaticId);
            this.autoSellSelectedStar = Mathf.Clamp(data.autoSellSelectedStar, 0, 5);
            this.autoSellInterval = Mathf.Clamp(data.autoSellInterval > 0f ? data.autoSellInterval : 5f, 1f, 120f);
            this.autoSellScanSource = Mathf.Clamp(data.autoSellScanSource, 0, 2);
            this.autoSellFestivalTokensEnabled = data.autoSellFestivalTokensEnabled;
            this.auraFarmLootCollectDistance = Mathf.Clamp(
                data.auraFarmLootCollectDistance > 0f ? data.auraFarmLootCollectDistance : 100f,
                1f,
                500f);
            this.auraFarmLootCollectEnabled = data.auraFarmLootCollectDistance > 0f
                ? data.auraFarmLootCollectEnabled
                : true;
        }

        private void PopulateAllConfigSections(UnifiedConfigData data)
        {
            if (data == null) return;
            this.PopulateKeybindConfig(data.Keybinds);
            this.PopulateUiThemeConfig(data.UiTheme);
            this.PopulateRadarConfig(data.Radar);
            if (data.BirdFarm == null) data.BirdFarm = new BirdFarmConfigData();
            BirdNetFarm.PopulateBirdFarmConfig(data.BirdFarm);
            data.Language = string.IsNullOrWhiteSpace(this.selectedLanguage) ? "en" : this.selectedLanguage;

            data.Patrol = new PatrolData();
            foreach (Vector3 p in patrolPoints)
            {
                data.Patrol.Points.Add(new SerializableVector3(p));
            }

            data.CustomTeleports = new List<CustomTeleportEntry>();
            foreach (CustomTeleportEntry entry in this.customTeleportList)
            {
                if (entry == null) continue;
                data.CustomTeleports.Add(new CustomTeleportEntry
                {
                    name = (entry.name ?? "").Replace("\"", "").Replace("\\", ""),
                    position = entry.position
                });
            }

            data.FishingRouteSpots = FishingRouteFeature.ExportCustomSpots();
        }

        private void SaveKeybinds(bool showNotification = true)
        {
            try
            {
                UnifiedConfigData data = this.LoadOrCreateUnifiedConfig();
                this.PopulateAllConfigSections(data);
                this.SaveUnifiedConfig(data);
                ModLogger.Msg("Keybinds Saved!");
                if (showNotification)
                {
                    this.AddMenuNotification("Keybinds saved", new Color(0.55f, 0.88f, 1f));
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("Error Saving Keybinds: " + ex.Message);
                this.AddMenuNotification("Failed to save keybinds", new Color(1f, 0.4f, 0.4f));
            }
        }

        /// <summary>
        /// Silently saves every config section to the shared LocalLow config file.
        /// Called by subsystems (e.g. BirdNetFarm sliders) when their settings change.
        /// </summary>
        public void SaveAllSettings()
        {
            try
            {
                UnifiedConfigData data = this.LoadOrCreateUnifiedConfig();
                this.PopulateAllConfigSections(data);
                this.SaveUnifiedConfig(data);
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[Config] SaveAllSettings error: " + ex.Message);
            }
        }

        private void LoadKeybinds()
        {
            try
            {
                UnifiedConfigData config = this.LoadUnifiedConfig();
                if (config != null)
                {
                    this.ApplyKeybindConfig(config.Keybinds);
                    ModLogger.Msg("Keybinds Loaded.");
                    this.AddMenuNotification("Keybinds loaded", new Color(0.55f, 0.88f, 1f));
                    return;
                }
                string path = this.GetKeybindsPath();
                if (File.Exists(path))
                {
                    string[] lines = File.ReadAllLines(path);
                    foreach (string line in lines)
                    {
                        if (line.Contains("keyToggleMenu")) this.keyToggleMenu = (KeyCode)GetJsonInt(line, "\"keyToggleMenu\":");
                        else if (line.Contains("keyToggleRadar")) this.keyToggleRadar = (KeyCode)GetJsonInt(line, "\"keyToggleRadar\":");
                        else if (line.Contains("keyAuraFarm")) this.keyAuraFarm = (KeyCode)GetJsonInt(line, "\"keyAuraFarm\":");
                        else if (line.Contains("keyAutoFishFarm") || line.Contains("keyAutoFishingTeleport")) this.keyAutoFishingTeleport = (KeyCode)GetJsonInt(line, line.Contains("keyAutoFishFarm") ? "\"keyAutoFishFarm\":" : "\"keyAutoFishingTeleport\":");
                        else if (line.Contains("keyAutoFish")) this.keyAutoFish = (KeyCode)GetJsonInt(line, "\"keyAutoFish\":");
                        else if (line.Contains("keyAutoFishShadowNet")) this.keyAutoFishShadowNet = (KeyCode)GetJsonInt(line, "\"keyAutoFishShadowNet\":");
                        else if (line.Contains("keyBypassUI")) this.keyBypassUI = (KeyCode)GetJsonInt(line, "\"keyBypassUI\":");
                        else if (line.Contains("keyDisableAll")) this.keyDisableAll = (KeyCode)GetJsonInt(line, "\"keyDisableAll\":");
                        else if (line.Contains("keyInspectPlayer")) this.keyInspectPlayer = (KeyCode)GetJsonInt(line, "\"keyInspectPlayer\":");
                        else if (line.Contains("keyInspectMove")) this.keyInspectMove = (KeyCode)GetJsonInt(line, "\"keyInspectMove\":");
                        else if (line.Contains("keyAutoRepair")) this.keyAutoRepair = (KeyCode)GetJsonInt(line, "\"keyAutoRepair\":");
                        else if (line.Contains("keyAutoJoinFriend")) this.keyAutoJoinFriend = (KeyCode)GetJsonInt(line, "\"keyAutoJoinFriend\":");
                        else if (line.Contains("keySeaCleanQte")) this.seaCleanQteHotkey = (KeyCode)GetJsonInt(line, "\"keySeaCleanQte\":");
                        else if (line.Contains("keyAutoSnow")) this.autoSnowHotkey = (KeyCode)GetJsonInt(line, "\"keyAutoSnow\":");
                        else if (line.Contains("keyAutoSand")) this.autoSandHotkey = (KeyCode)GetJsonInt(line, "\"keyAutoSand\":");
                        else if (line.Contains("keyJoinPublic")) this.keyJoinPublic = (KeyCode)GetJsonInt(line, "\"keyJoinPublic\":");
                        else if (line.Contains("keyJoinMyTown")) this.keyJoinMyTown = (KeyCode)GetJsonInt(line, "\"keyJoinMyTown\":");
                        else if (line.Contains("keyAutoInsectFarm")) this.keyAutoInsectFarm = (KeyCode)GetJsonInt(line, "\"keyAutoInsectFarm\":");
                        else if (line.Contains("keyAutoBirdFarm")) this.keyAutoBirdFarm = (KeyCode)GetJsonInt(line, "\"keyAutoBirdFarm\":");
                        else if (line.Contains("keyMassCook")) this.keyMassCook = (KeyCode)GetJsonInt(line, "\"keyMassCook\":");
                        else if (line.Contains("keyAutoPuzzle")) this.keyAutoPuzzle = (KeyCode)GetJsonInt(line, "\"keyAutoPuzzle\":");
                        else if (line.Contains("keyAutoCatPlay")) this.keyAutoCatPlay = (KeyCode)GetJsonInt(line, "\"keyAutoCatPlay\":");
                        else if (line.Contains("keyAutoDogTrain")) this.keyAutoDogTrain = (KeyCode)GetJsonInt(line, "\"keyAutoDogTrain\":");
                        else if (line.Contains("keyAutoPetWash")) this.keyAutoPetWash = (KeyCode)GetJsonInt(line, "\"keyAutoPetWash\":");
                        else if (line.Contains("keyFeedAllCats")) this.keyFeedAllCats = (KeyCode)GetJsonInt(line, "\"keyFeedAllCats\":");
                        else if (line.Contains("keyFeedAllDogs")) this.keyFeedAllDogs = (KeyCode)GetJsonInt(line, "\"keyFeedAllDogs\":");
                        else if (line.Contains("keySpawnBubble")) this.keySpawnBubble = (KeyCode)GetJsonInt(line, "\"keySpawnBubble\":");
                        else if (line.Contains("keyCameraToggle")) this.keyCameraToggle = (KeyCode)GetJsonInt(line, "\"keyCameraToggle\":");
                        else if (line.Contains("keyAutoIceSkating")) this.keyAutoIceSkating = (KeyCode)GetJsonInt(line, "\"keyAutoIceSkating\":");
                        else if (line.Contains("keyNoclip")) this.keyNoclip = (KeyCode)GetJsonInt(line, "\"keyNoclip\":");
                        else if (line.Contains("noclipSpeed")) this.noclipSpeed = GetJsonFloat(line, "\"noclipSpeed\":");
                        else if (line.Contains("noclipBoostMultiplier")) this.noclipBoostMultiplier = GetJsonFloat(line, "\"noclipBoostMultiplier\":");
                        else if (line.Contains("areaLoadDelay")) this.areaLoadDelay = GetJsonInt(line, "\"areaLoadDelay\":");
                        else if (line.Contains("auraCollectWaitTimeout")) this.auraCollectWaitTimeout = Mathf.Clamp(GetJsonFloat(line, "\"auraCollectWaitTimeout\":"), 4f, 30f);
                        else if (line.Contains("foragingTeleportDelaySeconds")) this.foragingTeleportDelaySeconds = Mathf.Clamp(GetJsonFloat(line, "\"foragingTeleportDelaySeconds\":"), 0f, 10f);
                        else if (line.Contains("resourceAutoRepairPauseSeconds")) this.resourceAutoRepairPauseSeconds = GetJsonFloat(line, "\"resourceAutoRepairPauseSeconds\":");
                        else if (line.Contains("gameSpeed")) this.gameSpeed = GetJsonFloat(line, "\"gameSpeed\":");
                        else if (line.Contains("fpsBypassEnabled")) this.fpsBypassEnabled = GetJsonInt(line, "\"fpsBypassEnabled\":") != 0;
                        else if (line.Contains("fpsBypassTarget")) this.fpsBypassTarget = Mathf.Clamp(GetJsonInt(line, "\"fpsBypassTarget\":"), 30, 360);
                        else if (line.Contains("lodOverrideMode")) this.lodOverrideMode = Mathf.Clamp(GetJsonInt(line, "\"lodOverrideMode\":"), 0, 3);
                        else if (line.Contains("lodCustomBias")) this.lodCustomBias = Mathf.Clamp(GetJsonFloat(line, "\"lodCustomBias\":"), 0.25f, 4f);
                        else if (line.Contains("lodCustomMaxLevel")) this.lodCustomMaxLevel = Mathf.Clamp(GetJsonInt(line, "\"lodCustomMaxLevel\":"), 0, 4);
                        else if (line.Contains("customCameraFOVEnabled")) this.customCameraFOVEnabled = GetJsonInt(line, "\"customCameraFOVEnabled\":") != 0;
                        else if (line.Contains("cameraFOV")) this.cameraFOV = GetJsonFloat(line, "\"cameraFOV\":");
                        else if (line.Contains("hideJumpButtonEnabled")) this.hideJumpButtonEnabled = GetJsonInt(line, "\"hideJumpButtonEnabled\":") != 0;
                        else if (line.Contains("bunnyHopEnabled")) this.bunnyHopEnabled = GetJsonInt(line, "\"bunnyHopEnabled\":") != 0;
                        else if (line.Contains("analogMoveBridgeEnabled")) this.analogMoveBridgeEnabled = GetJsonInt(line, "\"analogMoveBridgeEnabled\":") != 0;
                        else if (line.Contains("skipShowOffAnimations")) this.skipShowOffAnimations = GetJsonInt(line, "\"skipShowOffAnimations\":") != 0;
                        else if (line.Contains("persistentHudEnabled")) this.persistentHudEnabled = GetJsonInt(line, "\"persistentHudEnabled\":") != 0;
                        else if (line.Contains("autoIceSkatingMinUltimateScore")) this.autoIceSkatingMinUltimateScore = Mathf.Clamp(GetJsonInt(line, "\"autoIceSkatingMinUltimateScore\":"), 0, AutoIceSkatingMinUltimateScoreSliderMax);
                        else if (line.Contains("autoIceSkatingOnlyX2Ultimate")) this.autoIceSkatingOnlyX2Ultimate = GetJsonInt(line, "\"autoIceSkatingOnlyX2Ultimate\":") != 0;
                        else if (line.Contains("autoIceSkatingLast30sUltimate")) this.autoIceSkatingLast30sUltimate = GetJsonInt(line, "\"autoIceSkatingLast30sUltimate\":") != 0;
                        else if (line.Contains("autoIceSkatingPerfectMove")) this.autoIceSkatingPerfectMove = GetJsonInt(line, "\"autoIceSkatingPerfectMove\":") != 0;
                        else if (line.Contains("autoIceSkatingPreferNewMove")) this.autoIceSkatingPreferNewMove = GetJsonInt(line, "\"autoIceSkatingPreferNewMove\":") != 0;
                        else if (line.Contains("iceSkatingChallengeEndScore")) this.iceSkatingChallengeEndScore = Mathf.Clamp(GetJsonInt(line, "\"iceSkatingChallengeEndScore\":"), 0, 999999);
                        else if (line.Contains("shopBuyAllMaxPerItem")) this.shopBuyAllMaxPerItem = Mathf.Clamp(GetJsonInt(line, "\"shopBuyAllMaxPerItem\":"), 1, 999999);
                        else if (line.Contains("autoIceSkatingEnabled")) this.autoIceSkatingEnabled = GetJsonInt(line, "\"autoIceSkatingEnabled\":") != 0;
                        else if (line.Contains("fastBubbleGenEnabled")) this.fastBubbleGenEnabled = GetJsonInt(line, "\"fastBubbleGenEnabled\":") != 0;
                        else if (line.Contains("bubbleBubblesPerMinute")) this.bubbleBubblesPerMinute = Mathf.Clamp(GetJsonFloat(line, "\"bubbleBubblesPerMinute\":"), 0f, 100f);
                        else if (line.Contains("bubbleSpawnAtPlayerEnabled")) this.bubbleSpawnAtPlayerEnabled = GetJsonInt(line, "\"bubbleSpawnAtPlayerEnabled\":") != 0;
                        else if (line.Contains("autoBubbleCollectEnabled")) this.autoBubbleCollectEnabled = GetJsonInt(line, "\"autoBubbleCollectEnabled\":") != 0;
                        else if (line.Contains("autoBubbleCollectRadius")) this.autoBubbleCollectRadius = Mathf.Clamp(GetJsonFloat(line, "\"autoBubbleCollectRadius\":"), 0f, 100f);
            else if (line.Contains("cookingAutoSpeed")) this.cookingAutoSpeed = GetJsonFloat(line, "\"cookingAutoSpeed\":");
            else if (line.Contains("netCookInterval")) this.netCookInterval = GetJsonFloat(line, "\"netCookInterval\":");
            else if (line.Contains("netCookScanRadiusMeters")) this.netCookScanRadiusMeters = Mathf.Clamp(GetJsonFloat(line, "\"netCookScanRadiusMeters\":"), NetCookMinScanRadiusMeters, NetCookMaxScanRadiusMeters);
            else if (line.Contains("netCookMiniGameOnly")) this.netCookMiniGameOnly = line.IndexOf("true", StringComparison.OrdinalIgnoreCase) >= 0 || GetJsonInt(line, "\"netCookMiniGameOnly\":") != 0;
            else if (line.Contains("netCookMoveIngredients")) this.netCookMoveIngredients = line.IndexOf("true", StringComparison.OrdinalIgnoreCase) >= 0 || GetJsonInt(line, "\"netCookMoveIngredients\":") != 0;
            else if (line.Contains("netCookRememberStoves")) this.netCookRememberStoves = line.IndexOf("true", StringComparison.OrdinalIgnoreCase) >= 0 || GetJsonInt(line, "\"netCookRememberStoves\":") != 0;
            else if (line.Contains("netCookCaptureOwnOnly")) this.netCookCaptureOwnOnly = line.IndexOf("true", StringComparison.OrdinalIgnoreCase) >= 0 || GetJsonInt(line, "\"netCookCaptureOwnOnly\":") != 0;
            else if (line.Contains("netCookCaptureRadiusOnly")) this.netCookCaptureRadiusOnly = line.IndexOf("true", StringComparison.OrdinalIgnoreCase) >= 0 || GetJsonInt(line, "\"netCookCaptureRadiusOnly\":") != 0;
            else if (line.Contains("netCookUseAllIngredients")) this.netCookUseAllIngredients = line.IndexOf("true", StringComparison.OrdinalIgnoreCase) >= 0 || GetJsonInt(line, "\"netCookUseAllIngredients\":") != 0;
            else if (line.Contains("autoFishScanTimeout")) this.saved_autoFishScanTimeout = GetJsonFloat(line, "\"autoFishScanTimeout\":");
                        else if (line.Contains("autoFishTeleportDelay")) this.saved_autoFishTeleportDelay = GetJsonFloat(line, "\"autoFishTeleportDelay\":");
                        else if (line.Contains("autoFishInstantCatchSendHz")) { /* send rate not restored; consume line so it can't collide with autoFishInstantCatch below */ }
                        else if (line.Contains("autoFishFishShadowDetectRange")) AutoFishingFarm.SetDetectRange(GetJsonFloat(line, "\"autoFishFishShadowDetectRange\":"));
                        else if (line.Contains("autoFishInstantCatch")) AutoFishingFarm.SetInstantCatchEnabled(GetJsonInt(line, "\"autoFishInstantCatch\":") != 0);
                        else if (line.Contains("autoFishAutoBaitNoFishSeconds")) AutoFishingFarm.SetAutoBaitNoFishSeconds(GetJsonFloat(line, "\"autoFishAutoBaitNoFishSeconds\":"));
                        else if (line.Contains("autoFishAutoBaitChoice")) AutoFishingFarm.SetAutoBaitChoice(GetJsonInt(line, "\"autoFishAutoBaitChoice\":"));
                        else if (line.Contains("autoFishAutoBaitMax")) AutoFishingFarm.SetAutoBaitMaxCount(GetJsonInt(line, "\"autoFishAutoBaitMax\":"));
                        else if (line.Contains("autoFishSkipCatchAnim")) AutoFishingFarm.SetSkipCatchAnimEnabled(GetJsonInt(line, "\"autoFishSkipCatchAnim\":") != 0);
                        else if (line.Contains("autoFishSkipCastAnim")) AutoFishingFarm.SetSkipCastAnimEnabled(GetJsonInt(line, "\"autoFishSkipCastAnim\":") != 0);
                        else if (line.Contains("autoFishSkipBaitAnim")) AutoFishingFarm.SetSkipBaitAnimEnabled(GetJsonInt(line, "\"autoFishSkipBaitAnim\":") != 0);
                        else if (line.Contains("autoFishReelMaxDuration")) this.saved_autoFishReelMaxDuration = GetJsonFloat(line, "\"autoFishReelMaxDuration\":");
                        else if (line.Contains("autoFishReelHoldDuration")) this.saved_autoFishReelHoldDuration = GetJsonFloat(line, "\"autoFishReelHoldDuration\":");
                        else if (line.Contains("autoFishReelPauseDuration")) this.saved_autoFishReelPauseDuration = GetJsonFloat(line, "\"autoFishReelPauseDuration\":");
                        else if (line.Contains("insectCatchCooldown")) InsectNetFarm.SetCatchCooldown(GetJsonFloat(line, "\"insectCatchCooldown\":"));
                        else if (line.Contains("insectScanRange")) InsectNetFarm.SetScanRange(GetJsonFloat(line, "\"insectScanRange\":"));
                        else if (line.Contains("insectBatchSize")) InsectNetFarm.SetBatchSize(GetJsonInt(line, "\"insectBatchSize\":"));
                        else if (line.Contains("insectTeleportEnabled")) InsectNetFarm.SetTeleportEnabled(GetJsonInt(line, "\"insectTeleportEnabled\":") != 0);
                        else if (line.Contains("insectPauseTeleportOnTriggersEnabled")) InsectNetFarm.SetPauseTeleportOnTriggersEnabled(GetJsonInt(line, "\"insectPauseTeleportOnTriggersEnabled\":") != 0);
                        else if (line.Contains("insectPauseTeleportOnRepairEnabled")) InsectNetFarm.SetPauseTeleportOnRepairEnabled(GetJsonInt(line, "\"insectPauseTeleportOnRepairEnabled\":") != 0);
                        else if (line.Contains("insectPauseTeleportOnEatEnabled")) InsectNetFarm.SetPauseTeleportOnEatEnabled(GetJsonInt(line, "\"insectPauseTeleportOnEatEnabled\":") != 0);
                        else if (line.Contains("insectRepairTeleportPauseSeconds")) InsectNetFarm.SetRepairTeleportPauseSeconds(GetJsonFloat(line, "\"insectRepairTeleportPauseSeconds\":"));
                        else if (line.Contains("insectEatTeleportPauseSeconds")) InsectNetFarm.SetEatTeleportPauseSeconds(GetJsonFloat(line, "\"insectEatTeleportPauseSeconds\":"));
                        else if (line.Contains("insectTeleportCooldown")) InsectNetFarm.SetCatchCooldown(GetJsonFloat(line, "\"insectTeleportCooldown\":"));
                        else if (line.Contains("insectScanTimeout")) InsectNetFarm.SetScanRange(GetJsonFloat(line, "\"insectScanTimeout\":"));
                        else if (line.Contains("keyAutoEat")) this.keyAutoEat = (KeyCode)GetJsonInt(line, "\"keyAutoEat\":");
                        else if (line.Contains("keyUseBait")) this.keyUseBait = (KeyCode)GetJsonInt(line, "\"keyUseBait\":");
                        else if (line.Contains("keyUseAttractor")) this.keyUseAttractor = (KeyCode)GetJsonInt(line, "\"keyUseAttractor\":");
                        else if (line.Contains("keyAntiAfk")) this.keyAntiAfk = (KeyCode)GetJsonInt(line, "\"keyAntiAfk\":");
                        else if (line.Contains("keyBypassOverlap")) this.keyBypassOverlap = (KeyCode)GetJsonInt(line, "\"keyBypassOverlap\":");
                        else if (line.Contains("keyBirdVacuum")) this.keyBirdVacuum = (KeyCode)GetJsonInt(line, "\"keyBirdVacuum\":");
                        else if (line.Contains("notificationsEnabled")) this.notificationsEnabled = GetJsonInt(line, "\"notificationsEnabled\":") != 0;
                        else if (line.Contains("blockGameUiWhenMenuOpen")) this.blockGameUiWhenMenuOpen = GetJsonInt(line, "\"blockGameUiWhenMenuOpen\":") != 0;
                        else if (line.Contains("showStatusOverlay")) this.showStatusOverlay = GetJsonInt(line, "\"showStatusOverlay\":") != 0;
                        else if (line.Contains("maxAutoEatAttempts")) this.maxAutoEatAttempts = GetJsonInt(line, "\"maxAutoEatAttempts\":");
                        else if (line.Contains("autoClickStartEnabled")) this.autoClickStartEnabled = GetJsonInt(line, "\"autoClickStartEnabled\":") != 0;
                        else if (line.Contains("hideIdEnabled")) this.hideIdEnabled = GetJsonInt(line, "\"hideIdEnabled\":") != 0;
                        else if (line.Contains("customDisplayIdEnabled")) this.customDisplayIdEnabled = GetJsonInt(line, "\"customDisplayIdEnabled\":") != 0;
                        else if (line.Contains("customDisplayId")) this.customDisplayId = this.NormalizeCustomId(GetJsonString(line, "\"customDisplayId\":"));
                        else if (line.Contains("antiAfkEnabled")) this.antiAfkEnabled = GetJsonInt(line, "\"antiAfkEnabled\":") != 0;
                        else if (line.Contains("mouseLookEnabled")) this.mouseLookEnabled = GetJsonInt(line, "\"mouseLookEnabled\":") != 0;
                        else if (line.Contains("showMouseLookCrosshair")) this.showMouseLookCrosshair = GetJsonInt(line, "\"showMouseLookCrosshair\":") != 0;
                        else if (line.Contains("antiAfkInterval")) this.antiAfkInterval = Mathf.Clamp(GetJsonFloat(line, "\"antiAfkInterval\":"), 5f, 9f);
                        else if (line.Contains("autoRepairType")) this.autoRepairType = Mathf.Clamp(GetJsonInt(line, "\"autoRepairType\":"), 0, this.autoRepairOptions.Length - 1);
                        else if (line.Contains("autoRepairUseTarget")) this.autoRepairUseTarget = Mathf.Clamp(GetJsonInt(line, "\"autoRepairUseTarget\":"), 1, 3);
                        else if (line.Contains("autoRepairTriggerPercent")) this.autoRepairTriggerPercent = Mathf.Clamp(GetJsonInt(line, "\"autoRepairTriggerPercent\":"), 1, 100);
                        else if (line.Contains("autoEatFoodType")) this.autoEatFoodType = Mathf.Clamp(GetJsonInt(line, "\"autoEatFoodType\":"), 0, this.autoEatFoodOptions.Length - 1);
                        else if (line.Contains("repairTeleportBackEnabled")) this.repairTeleportBackEnabled = GetJsonInt(line, "\"repairTeleportBackEnabled\":") != 0;
                        else if (line.Contains("auraFarmLootCollectEnabled")) this.auraFarmLootCollectEnabled = GetJsonInt(line, "\"auraFarmLootCollectEnabled\":") != 0;
                        else if (line.Contains("auraFarmLootCollectDistance")) this.auraFarmLootCollectDistance = Mathf.Clamp(GetJsonFloat(line, "\"auraFarmLootCollectDistance\":"), 1f, 500f);
                    }
                    this.ResetNetCookDishLimitToDefault();
                    this.SyncLodOverrideAfterConfigLoad();
                    ModLogger.Msg("Keybinds Loaded.");
                    this.AddMenuNotification("Keybinds loaded", new Color(0.55f, 0.88f, 1f));
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("Error Loading Keybinds: " + ex.Message);
                this.AddMenuNotification("Failed to load keybinds", new Color(1f, 0.4f, 0.4f));
            }
        }

        private float CalculateSettingsTabHeight()
        {
            if (this.settingsSubTab == 0)
            {
                return this.CalculateSettingsMainTabHeight();
            }

            if (this.settingsSubTab == 1)
            {
                return string.IsNullOrEmpty(this.keyBindingActive) ? 1720f : 300f;
            }

            if (this.settingsSubTab == 2)
            {
                return this.uiThemePickerOpen ? 1180f : 860f;
            }

            if (this.settingsSubTab == 3)
            {
                return 520f;
            }

            if (this.settingsSubTab == 4)
            {
                return this.CalculateLoggingTabHeight();
            }

            return this.CalculateSettingsMainTabHeight();
        }

        private float GetSettingsMainLanguagePanelHeight()
        {
            string[] languageCodes = LocalizationManager.GetAvailableLanguageCodes();
            float localizationDropdownHeight = (languageCodes.Length * 28f) + 8f;
            float notificationDropdownHeight = (NotificationPositionOptions.Length * 28f) + 8f;
            float languagePanelHeight = this.notificationsEnabled ? 158f : 118f;
            if (this.localizationDropdownOpen)
            {
                float localizationBottom = 42f + 28f + 4f + localizationDropdownHeight;
                float shiftedRowsBottom = this.notificationsEnabled
                    ? localizationBottom + 86f
                    : localizationBottom + 14f;
                languagePanelHeight = Mathf.Max(languagePanelHeight, shiftedRowsBottom);
            }

            if (this.notificationsEnabled && this.notificationPositionDropdownOpen)
            {
                float positionRowY = 122f;
                if (this.localizationDropdownOpen)
                {
                    positionRowY += localizationDropdownHeight + 6f;
                }

                float optionsBottom = positionRowY + 28f + 4f + notificationDropdownHeight + 10f;
                languagePanelHeight = Mathf.Max(languagePanelHeight, optionsBottom);
            }

            return languagePanelHeight;
        }

        private float CalculateSettingsMainTabHeight()
        {
            const float startY = 10f;
            const float sectionGap = 14f;
            const float rowHeight = 30f;

            float height = startY + 26f;
            height += this.GetSettingsMainLanguagePanelHeight() + sectionGap;
            height += (this.customDisplayIdEnabled ? 284f : 246f) + sectionGap;
            height += 48f + rowHeight + (this.fpsBypassEnabled ? rowHeight : 0f) + this.GetLodSettingsPanelHeight() + sectionGap;
            height += 100f + 20f;
            return height + 48f;
        }

        private void QueueGameSpeedConfigSave()
        {
            this.pendingGameSpeedConfigSave = true;
            this.nextGameSpeedConfigSaveAt = Time.unscaledTime + 0.75f;
        }

        private void FlushPendingGameSpeedConfigSave()
        {
            if (!this.pendingGameSpeedConfigSave || Time.unscaledTime < this.nextGameSpeedConfigSaveAt)
            {
                return;
            }

            this.pendingGameSpeedConfigSave = false;
            try { this.SaveKeybinds(false); } catch { }
        }

        private bool TryConfigureIntentInt(object intent, string key, int value)
        {
            return this.TryInvokeIntentMethod(intent, "AddInt", new object[] { key, value });
        }

        private bool TryConfigureIntentBool(object intent, string key, bool value)
        {
            return this.TryInvokeIntentMethod(intent, "AddBool", new object[] { key, value });
        }

        private void DrawQuickSettingsPanel(Rect panelRect)
        {
            GUIStyle title = new GUIStyle(GUI.skin.label);
            title.fontSize = 13;
            title.fontStyle = FontStyle.Bold;
            title.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB);

            GUIStyle value = new GUIStyle(GUI.skin.label);
            value.fontSize = 12;
            value.normal.textColor = new Color(this.uiSubTabTextR, this.uiSubTabTextG, this.uiSubTabTextB, 0.92f);

            float x = panelRect.x + 14f;
            float y = panelRect.y + 42f;
            float w = panelRect.width - 28f;

            GUI.Label(new Rect(x, y, w, 20f), this.L("Keybind"), title);
            GUI.Label(new Rect(x, y + 20f, w, 20f), this.keyToggleMenu.ToString(), value);

            y += 50f;
            GUI.Label(new Rect(x, y, w, 20f), this.L("Accent Color"), title);
            Rect swatchRect = new Rect(panelRect.xMax - 34f, y + 2f, 18f, 18f);
            GUI.color = new Color(this.uiAccentR, this.uiAccentG, this.uiAccentB, 1f);
            GUI.DrawTexture(swatchRect, Texture2D.whiteTexture);
            GUI.color = Color.white;

            y += 30f;
            GUI.Label(new Rect(x, y, w, 18f), $"Slider Integer   {Mathf.RoundToInt(this.gameSpeed * 10f):D2}", value);
            this.DrawAccentSlider(new Rect(x, y + 16f, w, 18f), this.gameSpeed, 1f, 10f);

            y += 44f;
            GUI.Label(new Rect(x, y, w, 18f), $"Slider Float   {this.uiContentAlpha:F3}", value);
            this.DrawAccentSlider(new Rect(x, y + 16f, w, 18f), this.uiContentAlpha, 0.15f, 1f);
        }

        private void SetSettingsSubTab(int subTab)
        {
            if (this.settingsSubTab != subTab)
            {
                this.settingsSubTab = subTab;
                this.tabScrollPos = Vector2.zero;
                this.tabDrawContentHeight = 0f;
                if (subTab != 1)
                {
                    this.keyBindingActive = "";
                }
                if (subTab == 2)
                {
                    this.uiThemeHexInput = this.ColorToHex(this.GetUiThemeColorTargetValue(this.uiThemeColorTarget));
                    this.uiThemePickerOpen = false;
                }
            }
        }

        private float DrawSettingsTab(int startY)
        {
            if (this.settingsSubTab == 0)
            {
                return this.DrawSettingsMainTab(startY);
            }

            if (this.settingsSubTab == 2)
            {
                return this.DrawUiThemeTab(startY);
            }

            if (this.settingsSubTab == 3)
            {
                return this.DrawAboutTab(startY);
            }

            if (this.settingsSubTab == 4)
            {
                return this.DrawLoggingTab(startY);
            }

            int num = startY;
            float left = 24f;
            float contentWidth = 540f;
            Color accent = new Color(this.uiAccentR, this.uiAccentG, this.uiAccentB);
            Color panelFill = new Color(this.uiContentR, this.uiContentG, this.uiContentB, Mathf.Clamp(this.uiPanelAlpha * 0.82f, 0.14f, 0.92f));
            Color panelLine = new Color(accent.r, accent.g, accent.b, 0.24f);

            GUIStyle headerStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold, fontSize = 15 };
            headerStyle.normal.textColor = Color.white;
            GUIStyle subHeaderStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold, fontSize = 11 };
            subHeaderStyle.normal.textColor = accent;
            GUI.Label(new Rect(left, (float)num, contentWidth, 24f), this.L("KEYBIND SETTINGS"), headerStyle);
            num += 26;

            if (!string.IsNullOrEmpty(this.keyBindingActive))
            {
                Rect capturePanel = new Rect(left, (float)num, contentWidth, 116f);
                this.DrawExentriSectionPanel(capturePanel, accent, panelFill, panelLine);
                GUIStyle centerStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };
                centerStyle.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB);
                GUI.Label(new Rect(capturePanel.x + 12f, capturePanel.y + 18f, capturePanel.width - 24f, 20f), this.L("PRESS ANY KEY FOR:"), centerStyle);
                centerStyle.normal.textColor = new Color(1f, 0.86f, 0.36f);
                GUI.Label(new Rect(capturePanel.x + 12f, capturePanel.y + 42f, capturePanel.width - 24f, 24f), this.L(this.keyBindingActive).ToUpperInvariant(), centerStyle);
                
                if (GUI.Button(new Rect(capturePanel.x + 150f, capturePanel.y + 76f, 240f, 30f), this.L("CANCEL"), this.themeDangerButtonStyle ?? GUI.skin.button))
                {
                    this.keyBindingActive = "";
                }

                this.TryCaptureKeybindFromEvent(Event.current);
                return (float)num + 180f;
            }

            this.BeginKeybindSection(ref num, left, contentWidth, "CORE", 6, subHeaderStyle, accent, panelFill, panelLine);
            this.DrawKeybindRowInPanel(ref num, left, contentWidth, "Toggle Menu", ref this.keyToggleMenu);
            this.DrawKeybindRowInPanel(ref num, left, contentWidth, "Toggle Radar", ref this.keyToggleRadar);
            this.DrawKeybindRowInPanel(ref num, left, contentWidth, "Bypass UI", ref this.keyBypassUI);
            this.DrawKeybindRowInPanel(ref num, left, contentWidth, "Disable All", ref this.keyDisableAll);
            this.DrawKeybindRowInPanel(ref num, left, contentWidth, "Inspect Player", ref this.keyInspectPlayer);
            this.DrawKeybindRowInPanel(ref num, left, contentWidth, "Inspect Move", ref this.keyInspectMove);
            num += 14;

            this.BeginKeybindSection(ref num, left, contentWidth, "AUTOMATION", 21, subHeaderStyle, accent, panelFill, panelLine);
            this.DrawKeybindRowInPanel(ref num, left, contentWidth, "Aura Farm", ref this.keyAuraFarm);
            this.DrawKeybindRowInPanel(ref num, left, contentWidth, "Water + Weed Radius", ref this.keyWaterWeedRadius);
            this.DrawKeybindRowInPanel(ref num, left, contentWidth, "Auto Insect Farm", ref this.keyAutoInsectFarm);
            this.DrawKeybindRowInPanel(ref num, left, contentWidth, "Auto Bird Farm", ref this.keyAutoBirdFarm);
            this.DrawKeybindRowInPanel(ref num, left, contentWidth, "Fish Shadow Net", ref this.keyAutoFishShadowNet);
            this.DrawKeybindRowInPanel(ref num, left, contentWidth, "Mass Cook", ref this.keyMassCook);
            this.DrawKeybindRowInPanel(ref num, left, contentWidth, "Auto Puzzle", ref this.keyAutoPuzzle);
            this.DrawKeybindRowInPanel(ref num, left, contentWidth, "Auto Cat Play", ref this.keyAutoCatPlay);
            this.DrawKeybindRowInPanel(ref num, left, contentWidth, "Auto Dog Train", ref this.keyAutoDogTrain);
            this.DrawKeybindRowInPanel(ref num, left, contentWidth, "Auto Pet Wash", ref this.keyAutoPetWash);
            this.DrawKeybindRowInPanel(ref num, left, contentWidth, "Feed All Cats", ref this.keyFeedAllCats);
            this.DrawKeybindRowInPanel(ref num, left, contentWidth, "Feed All Dogs", ref this.keyFeedAllDogs);
            this.DrawKeybindRowInPanel(ref num, left, contentWidth, "Auto Snow Sculpture", ref this.autoSnowHotkey);
            this.DrawKeybindRowInPanel(ref num, left, contentWidth, "Auto Sand Sculpture", ref this.autoSandHotkey);
            this.DrawKeybindRowInPanel(ref num, left, contentWidth, "Auto Sea Clean QTE", ref this.seaCleanQteHotkey);
            this.DrawKeybindRowInPanel(ref num, left, contentWidth, "Bird Vacuum", ref this.keyBirdVacuum);
            this.DrawKeybindRowInPanel(ref num, left, contentWidth, "Spawn Bubble", ref this.keySpawnBubble);
            this.DrawKeybindRowInPanel(ref num, left, contentWidth, "Auto Repair", ref this.keyAutoRepair);
            this.DrawKeybindRowInPanel(ref num, left, contentWidth, "Auto Eat", ref this.keyAutoEat);
            this.DrawKeybindRowInPanel(ref num, left, contentWidth, "Use Bait", ref this.keyUseBait);
            this.DrawKeybindRowInPanel(ref num, left, contentWidth, "Use Attractor", ref this.keyUseAttractor);
            num += 14;

            this.BeginKeybindSection(ref num, left, contentWidth, "PLAYER", 6, subHeaderStyle, accent, panelFill, panelLine);
            this.DrawKeybindRowInPanel(ref num, left, contentWidth, "Noclip", ref this.keyNoclip);
            this.DrawKeybindRowInPanel(ref num, left, contentWidth, "Camera Toggle", ref this.keyCameraToggle);
            this.DrawKeybindRowInPanel(ref num, left, contentWidth, "Auto Ice Skating", ref this.keyAutoIceSkating);
            this.DrawKeybindRowInPanel(ref num, left, contentWidth, "Join My Town", ref this.keyJoinMyTown);
            this.DrawKeybindRowInPanel(ref num, left, contentWidth, "Anti AFK", ref this.keyAntiAfk);
            this.DrawKeybindRowInPanel(ref num, left, contentWidth, "Bypass Overlap", ref this.keyBypassOverlap);
            num += 14;

            this.BeginKeybindSection(ref num, left, contentWidth, "SPEED & TOOLS", 16, subHeaderStyle, accent, panelFill, panelLine);
            this.DrawKeybindRowInPanel(ref num, left, contentWidth, "Game Speed 1x", ref this.keyGameSpeed1x);
            this.DrawKeybindRowInPanel(ref num, left, contentWidth, "Game Speed 2x", ref this.keyGameSpeed2x);
            this.DrawKeybindRowInPanel(ref num, left, contentWidth, "Game Speed 5x", ref this.keyGameSpeed5x);
            this.DrawKeybindRowInPanel(ref num, left, contentWidth, "Game Speed 10x", ref this.keyGameSpeed10x);
            this.DrawKeybindRowInPanel(ref num, left, contentWidth, "Equip Axe", ref this.keyEquipAxe);
            this.DrawKeybindRowInPanel(ref num, left, contentWidth, "Equip Net", ref this.keyEquipNet);
            this.DrawKeybindRowInPanel(ref num, left, contentWidth, "Equip Rod", ref this.keyEquipRod);
            this.DrawKeybindRowInPanel(ref num, left, contentWidth, "Equip Sprinkler", ref this.keyEquipSprinkler);
            this.DrawKeybindRowInPanel(ref num, left, contentWidth, "Equip Bird Scanner", ref this.keyEquipBirdScanner);
            this.DrawKeybindRowInPanel(ref num, left, contentWidth, "Equip Pad", ref this.keyEquipPad);
            this.DrawKeybindRowInPanel(ref num, left, contentWidth, "Equip Sea Cleaner", ref this.keyEquipSeaCleaner);
            this.DrawKeybindRowInPanel(ref num, left, contentWidth, "Pad Confirm", ref this.keyPadConfirm);
            this.DrawKeybindRowInPanel(ref num, left, contentWidth, "Pad Cancel", ref this.keyPadCancel);
            this.DrawKeybindRowInPanel(ref num, left, contentWidth, "Pad Rotate", ref this.keyPadRotate);
            this.DrawKeybindRowInPanel(ref num, left, contentWidth, "Pad Move", ref this.keyPadMove);
            this.DrawKeybindRowInPanel(ref num, left, contentWidth, "Pad Delete", ref this.keyPadDelete);
            num += 18;

            if (this.DrawDangerActionButton(new Rect(left, (float)num, contentWidth, 34f), "RESET TO DEFAULTS"))
            {
                this.keyToggleMenu = KeyCode.Insert;
                this.keyToggleRadar = KeyCode.None;
                this.keyAuraFarm = KeyCode.None;
                this.keyWaterWeedRadius = KeyCode.None;
                this.keyAutoFish = KeyCode.None;
                this.keyAutoFishingTeleport = KeyCode.None;
                this.keyAutoFishShadowNet = KeyCode.None;
                this.keyBypassUI = KeyCode.None;
                this.keyDisableAll = KeyCode.None;
                this.keyInspectPlayer = KeyCode.None;
                this.keyInspectMove = KeyCode.None;
                this.keyAutoRepair = KeyCode.None;
                this.keyAutoJoinFriend = KeyCode.None;
                this.keyNoclip = KeyCode.None;
                this.keyCameraToggle = KeyCode.None;
                this.keyAutoIceSkating = KeyCode.None;
                this.keyJoinPublic = KeyCode.None;
                this.keyJoinMyTown = KeyCode.None;
                this.keyAutoEat = KeyCode.None;
                this.keyUseBait = KeyCode.None;
                this.keyUseAttractor = KeyCode.None;
                this.keyAntiAfk = KeyCode.None;
                this.autoSnowHotkey = KeyCode.None;
                this.autoSandHotkey = KeyCode.None;
                this.seaCleanQteHotkey = KeyCode.None;
                this.keyEquipSeaCleaner = KeyCode.None;
                this.keyBypassOverlap = KeyCode.None;
                this.keyBirdVacuum = KeyCode.None;
                this.keyGameSpeed1x = KeyCode.None;
                this.keyGameSpeed2x = KeyCode.None;
                this.keyGameSpeed5x = KeyCode.None;
                this.keyGameSpeed10x = KeyCode.None;
                this.keyEquipAxe = KeyCode.None;
                this.keyEquipNet = KeyCode.None;
                this.keyEquipRod = KeyCode.None;
                this.keyEquipSprinkler = KeyCode.None;
                this.keyEquipBirdScanner = KeyCode.None;
                this.keyEquipPad = KeyCode.None;
                this.keyPadConfirm = KeyCode.None;
                this.keyPadCancel = KeyCode.None;
                this.keyPadRotate = KeyCode.None;
                this.keyPadMove = KeyCode.None;
                this.keyPadDelete = KeyCode.None;
                this.keyAutoInsectFarm = KeyCode.None;
                this.keyAutoBirdFarm = KeyCode.None;
                this.keyMassCook = KeyCode.None;
                this.keyAutoPuzzle = KeyCode.None;
                this.keyAutoCatPlay = KeyCode.None;
                this.keyAutoDogTrain = KeyCode.None;
                this.keyAutoPetWash = KeyCode.None;
                this.keyFeedAllCats = KeyCode.None;
                this.keyFeedAllDogs = KeyCode.None;
                this.keySpawnBubble = KeyCode.None;
                this.notificationsEnabled = true;
                this.notificationPosition = 5;
                this.hideIdEnabled = false;
                this.customDisplayIdEnabled = false;
                this.customDisplayId = string.Empty;
                this.fpsBypassEnabled = false;
                this.fpsBypassTarget = 144;
                this.ApplyFpsBypass(false);
                this.lodOverrideMode = 0;
                this.lodCustomBias = 1f;
                this.lodCustomMaxLevel = 0;
                this.RevertLodOverride();
                this.showStatusOverlay = false;
                this.SaveKeybinds(false);
                this.AddMenuNotification(this.L("Defaults restored (Toggle Menu: Insert)"), new Color(1f, 0.75f, 0.75f));
            }

            return (float)num + 38f;
        }

        private float DrawSettingsMainTab(int startY)
        {
            int num = startY;
            float left = 24f;
            float contentWidth = 540f;
            float controlWidth = 300f;
            float rowHeight = 30f;
            float sectionGap = 14f;
            Color accent = new Color(this.uiAccentR, this.uiAccentG, this.uiAccentB);
            Color panelFill = new Color(this.uiContentR, this.uiContentG, this.uiContentB, Mathf.Clamp(this.uiPanelAlpha * 0.82f, 0.14f, 0.92f));
            Color panelLine = new Color(accent.r, accent.g, accent.b, 0.24f);

            if (!this.notificationsEnabled)
            {
                this.notificationPositionDropdownOpen = false;
            }

            GUIStyle headerStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold, fontSize = 15 };
            headerStyle.normal.textColor = Color.white;
            GUIStyle subHeaderStyle = new GUIStyle(GUI.skin.label) { alignment = TextAnchor.MiddleLeft, fontStyle = FontStyle.Bold, fontSize = 11 };
            subHeaderStyle.normal.textColor = accent;
            GUIStyle rowLabelStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, alignment = TextAnchor.MiddleLeft };
            rowLabelStyle.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB);
            GUIStyle valueStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft };
            valueStyle.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB);
            GUIStyle arrowStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            arrowStyle.normal.textColor = accent;
            GUIStyle optionStyle = new GUIStyle(GUI.skin.label) { fontSize = 12, alignment = TextAnchor.MiddleLeft };
            optionStyle.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB);
            GUIStyle optionActiveStyle = new GUIStyle(optionStyle);
            optionActiveStyle.normal.textColor = accent;

            GUI.Label(new Rect(left, (float)num, contentWidth, 24f), this.L("SETTINGS"), headerStyle);
            num += 26;

            string[] languageCodes = LocalizationManager.GetAvailableLanguageCodes();
            string currentDisplayName = LocalizationManager.GetLanguageDisplayName(this.selectedLanguage);
            float localizationDropdownHeight = (languageCodes.Length * 28f) + 8f;
            float notificationDropdownHeight = (NotificationPositionOptions.Length * 28f) + 8f;
            float languagePanelHeight = this.GetSettingsMainLanguagePanelHeight();

            Rect languagePanel = new Rect(left, (float)num, contentWidth, languagePanelHeight);
            this.DrawExentriSectionPanel(languagePanel, accent, panelFill, panelLine);
            GUI.Label(new Rect(languagePanel.x + 14f, languagePanel.y + 12f, languagePanel.width - 28f, 18f), this.L("GENERAL"), subHeaderStyle);

            float rowY = languagePanel.y + 42f;
            GUI.Label(new Rect(languagePanel.x + 16f, rowY, 170f, rowHeight), this.L("Localization"), rowLabelStyle);

            Rect dropdownRect = new Rect(languagePanel.xMax - controlWidth - 16f, rowY + 1f, controlWidth, 28f);
            GUI.Box(dropdownRect, "", this.themeTopTabStyle ?? this.themePanelStyle ?? GUI.skin.box);
            this.DrawCardOutline(dropdownRect, 1f);
            if (GUI.Button(dropdownRect, "", GUIStyle.none))
            {
                this.localizationDropdownOpen = !this.localizationDropdownOpen;
                if (this.localizationDropdownOpen)
                {
                    this.notificationPositionDropdownOpen = false;
                }
            }

            GUI.Label(new Rect(dropdownRect.x + 10f, dropdownRect.y + 1f, dropdownRect.width - 32f, dropdownRect.height - 2f), currentDisplayName, valueStyle);
            GUI.Label(new Rect(dropdownRect.xMax - 22f, dropdownRect.y + 1f, 14f, dropdownRect.height - 2f), this.localizationDropdownOpen ? "^" : "v", arrowStyle);

            rowY += 40f;
            if (this.localizationDropdownOpen)
            {
                rowY += localizationDropdownHeight + 6f;
            }

            bool newNotificationsEnabled = this.DrawSwitchToggle(new Rect(languagePanel.x + 16f, rowY, languagePanel.width - 32f, rowHeight), this.notificationsEnabled, "Enable Notifications");
            if (newNotificationsEnabled != this.notificationsEnabled)
            {
                this.notificationsEnabled = newNotificationsEnabled;
                if (!this.notificationsEnabled)
                {
                    this.notificationPositionDropdownOpen = false;
                }
                this.SaveKeybinds(false);
                if (this.notificationsEnabled)
                {
                    this.AddMenuNotification(this.L("Notifications enabled"), new Color(0.55f, 0.88f, 1f));
                }
            }

            rowY += 40f;
            Rect notifDropdownRect = Rect.zero;
            if (this.notificationsEnabled)
            {
                GUI.Label(new Rect(languagePanel.x + 16f, rowY, 170f, rowHeight), this.L("Position"), rowLabelStyle);
                string notificationPositionSummary = this.L(NotificationPositionOptions[Mathf.Clamp(this.notificationPosition, 0, NotificationPositionOptions.Length - 1)]);
                notifDropdownRect = new Rect(languagePanel.xMax - controlWidth - 16f, rowY + 1f, controlWidth, 28f);
                GUI.Box(notifDropdownRect, "", this.themeTopTabStyle ?? this.themePanelStyle ?? GUI.skin.box);
                this.DrawCardOutline(notifDropdownRect, 1f);
                if (GUI.Button(notifDropdownRect, "", GUIStyle.none))
                {
                    this.notificationPositionDropdownOpen = !this.notificationPositionDropdownOpen;
                    if (this.notificationPositionDropdownOpen)
                    {
                        this.localizationDropdownOpen = false;
                    }
                }
                GUI.Label(new Rect(notifDropdownRect.x + 10f, notifDropdownRect.y + 1f, notifDropdownRect.width - 32f, notifDropdownRect.height - 2f), notificationPositionSummary, valueStyle);
                GUI.Label(new Rect(notifDropdownRect.xMax - 22f, notifDropdownRect.y + 1f, 14f, notifDropdownRect.height - 2f), this.notificationPositionDropdownOpen ? "^" : "v", arrowStyle);
            }

            if (this.localizationDropdownOpen)
            {
                Rect optionsBoxRect = new Rect(dropdownRect.x, dropdownRect.yMax + 4f, dropdownRect.width, localizationDropdownHeight);
                GUI.Box(optionsBoxRect, "", this.themeContentStyle ?? this.themePanelStyle ?? GUI.skin.box);
                this.DrawCardOutline(optionsBoxRect, 1f);

                for (int i = 0; i < languageCodes.Length; i++)
                {
                    string code = languageCodes[i];
                    string displayName = LocalizationManager.GetLanguageDisplayName(code);
                    bool isSelected = string.Equals(code, this.selectedLanguage, StringComparison.OrdinalIgnoreCase);
                    Rect optionRect = new Rect(optionsBoxRect.x + 4f, optionsBoxRect.y + 4f + i * 28f, optionsBoxRect.width - 8f, 26f);
                    bool clicked = GUI.Button(optionRect, "", GUIStyle.none);

                    if (isSelected)
                    {
                        GUI.Box(optionRect, "", this.themeTopTabActiveStyle ?? this.themePrimaryButtonStyle ?? GUI.skin.box);
                    }
                    GUI.Label(new Rect(optionRect.x + 10f, optionRect.y + 1f, optionRect.width - 20f, optionRect.height - 2f), (isSelected ? "> " : "") + displayName, isSelected ? optionActiveStyle : optionStyle);

                    if (clicked)
                    {
                        this.SetLocalizationLanguage(code, true);
                        this.localizationDropdownOpen = false;
                    }
                }
            }

            if (this.notificationsEnabled && this.notificationPositionDropdownOpen)
            {
                int previousNotificationPosition = this.notificationPosition;
                Rect optionsBoxRect = new Rect(notifDropdownRect.x, notifDropdownRect.yMax + 4f, notifDropdownRect.width, notificationDropdownHeight);
                GUI.Box(optionsBoxRect, "", this.themeContentStyle ?? this.themePanelStyle ?? GUI.skin.box);
                this.DrawCardOutline(optionsBoxRect, 1f);

                for (int i = 0; i < NotificationPositionOptions.Length; i++)
                {
                    bool isSelected = i == this.notificationPosition;
                    Rect optionRect = new Rect(optionsBoxRect.x + 4f, optionsBoxRect.y + 4f + i * 28f, optionsBoxRect.width - 8f, 26f);
                    bool clicked = GUI.Button(optionRect, "", GUIStyle.none);
                    if (isSelected)
                    {
                        GUI.Box(optionRect, "", this.themeTopTabActiveStyle ?? this.themePrimaryButtonStyle ?? GUI.skin.box);
                    }
                    GUI.Label(new Rect(optionRect.x + 10f, optionRect.y + 1f, optionRect.width - 20f, optionRect.height - 2f), (isSelected ? "> " : "") + this.L(NotificationPositionOptions[i]), isSelected ? optionActiveStyle : optionStyle);
                    if (clicked)
                    {
                        this.notificationPosition = i;
                        this.notificationPositionDropdownOpen = false;
                    }
                }

                if (this.notificationPosition != previousNotificationPosition)
                {
                    this.SaveKeybinds(false);
                    this.AddMenuNotification(this.LF("Notification position: {0}", this.L(NotificationPositionOptions[this.notificationPosition])), new Color(0.55f, 0.88f, 1f));
                }
            }

            num += (int)languagePanel.height + (int)sectionGap;

            Rect behaviorPanel = new Rect(left, (float)num, contentWidth, this.customDisplayIdEnabled ? 284f : 246f);
            this.DrawExentriSectionPanel(behaviorPanel, accent, panelFill, panelLine);
            GUI.Label(new Rect(behaviorPanel.x + 14f, behaviorPanel.y + 12f, behaviorPanel.width - 28f, 18f), this.L("BEHAVIOR"), subHeaderStyle);

            rowY = behaviorPanel.y + 42f;
            bool newAutoClickStart = this.DrawSwitchToggle(new Rect(behaviorPanel.x + 16f, rowY, behaviorPanel.width - 32f, rowHeight), this.autoClickStartEnabled, "Auto Start on Lobby");
            if (newAutoClickStart != this.autoClickStartEnabled)
            {
                this.autoClickStartEnabled = newAutoClickStart;
                this.SaveKeybinds(false);
                if (this.autoClickStartEnabled)
                {
                    this.AddMenuNotification(this.L("Auto Start enabled"), new Color(0.55f, 0.88f, 1f));
                }
                else
                {
                    this.AddMenuNotification(this.L("Auto Start disabled"), new Color(0.88f, 0.6f, 0.6f));
                }
            }

            rowY += 30f;
            bool newAutoCloseAnnouncement = this.DrawSwitchToggle(new Rect(behaviorPanel.x + 16f, rowY, behaviorPanel.width - 32f, rowHeight), this.autoCloseAnnouncementEnabled, "Auto Close Announcements");
            if (newAutoCloseAnnouncement != this.autoCloseAnnouncementEnabled)
            {
                this.autoCloseAnnouncementEnabled = newAutoCloseAnnouncement;
                this.SaveKeybinds(false);
                if (this.autoCloseAnnouncementEnabled)
                {
                    this.AddMenuNotification(this.L("Auto Close Announcement enabled"), new Color(0.55f, 0.88f, 1f));
                }
                else
                {
                    this.AddMenuNotification(this.L("Auto Close Announcement disabled"), new Color(0.88f, 0.6f, 0.6f));
                }
            }

            rowY += 30f;
            bool newHideIdEnabled = this.DrawSwitchToggle(new Rect(behaviorPanel.x + 16f, rowY, behaviorPanel.width - 32f, rowHeight), this.hideIdEnabled, "Hide ID");
            if (newHideIdEnabled != this.hideIdEnabled)
            {
                this.hideIdEnabled = newHideIdEnabled;
                this.nextIdDisplayUpdateAt = 0f;
                this.SaveKeybinds(false);
                if (this.hideIdEnabled)
                {
                    this.AddMenuNotification(this.L("ID display hidden"), new Color(0.55f, 0.88f, 1f));
                }
                else
                {
                    this.AddMenuNotification(this.L("ID display shown"), new Color(0.55f, 0.88f, 1f));
                }
            }

            rowY += 30f;
            bool newCustomIdEnabled = this.DrawSwitchToggle(new Rect(behaviorPanel.x + 16f, rowY, behaviorPanel.width - 32f, rowHeight), this.customDisplayIdEnabled, "Custom ID");
            if (newCustomIdEnabled != this.customDisplayIdEnabled)
            {
                this.customDisplayIdEnabled = newCustomIdEnabled;
                this.nextIdDisplayUpdateAt = 0f;
                this.SaveKeybinds(false);
                if (this.customDisplayIdEnabled)
                {
                    this.AddMenuNotification(this.L("Custom ID enabled"), new Color(0.55f, 0.88f, 1f));
                }
                else
                {
                    this.AddMenuNotification(this.L("Custom ID disabled"), new Color(0.88f, 0.6f, 0.6f));
                }
            }

            if (this.customDisplayIdEnabled)
            {
                rowY += 34f;
                GUI.Label(new Rect(behaviorPanel.x + 28f, rowY, 92f, 24f), this.L("Value"), rowLabelStyle);
                string previousCustomDisplayId = this.customDisplayId ?? string.Empty;
                string nextCustomDisplayId = this.NormalizeCustomId(GUI.TextField(new Rect(behaviorPanel.x + 120f, rowY, 220f, 24f), previousCustomDisplayId, 24));
                if (!string.Equals(nextCustomDisplayId, previousCustomDisplayId, StringComparison.Ordinal))
                {
                    this.customDisplayId = nextCustomDisplayId;
                    this.nextIdDisplayUpdateAt = 0f;
                    this.SaveKeybinds(false);
                    if (string.IsNullOrEmpty(this.customDisplayId))
                    {
                        this.AddMenuNotification(this.L("Custom ID cleared"), new Color(0.88f, 0.6f, 0.6f));
                    }
                    else
                    {
                        this.AddMenuNotification(this.L("Custom ID updated"), new Color(0.55f, 0.88f, 1f));
                    }
                }
                rowY += 34f;
            }
            else
            {
                rowY += 34f;
            }

            bool newShowOverlay = this.DrawSwitchToggle(new Rect(behaviorPanel.x + 16f, rowY, behaviorPanel.width - 32f, rowHeight), this.showStatusOverlay, "Show Status Overlay");
            if (newShowOverlay != this.showStatusOverlay)
            {
                this.showStatusOverlay = newShowOverlay;
                this.SaveKeybinds(false);
                if (this.showStatusOverlay)
                {
                    this.AddMenuNotification(this.L("Status overlay enabled"), new Color(0.55f, 0.88f, 1f));
                }
                else
                {
                    this.AddMenuNotification(this.L("Status overlay disabled"), new Color(0.88f, 0.6f, 0.6f));
                }
            }

            rowY += 30f;
            bool newBlockGameUi = this.DrawSwitchToggle(new Rect(behaviorPanel.x + 16f, rowY, behaviorPanel.width - 32f, rowHeight), this.blockGameUiWhenMenuOpen, "Block Input");
            if (newBlockGameUi != this.blockGameUiWhenMenuOpen)
            {
                this.blockGameUiWhenMenuOpen = newBlockGameUi;
                this.SaveKeybinds(false);
                if (this.blockGameUiWhenMenuOpen)
                {
                    this.AddMenuNotification(this.L("Block Input Enabled"), new Color(0.55f, 0.88f, 1f));
                }
                else
                {
                    this.AddMenuNotification(this.L("Block Input Disabled"), new Color(0.88f, 0.6f, 0.6f));
                }
            }

            num += (int)behaviorPanel.height + (int)sectionGap;

            float performancePanelHeight = 48f + rowHeight + (this.fpsBypassEnabled ? rowHeight : 0f) + this.GetLodSettingsPanelHeight();
            Rect performancePanel = new Rect(left, (float)num, contentWidth, performancePanelHeight);
            this.DrawExentriSectionPanel(performancePanel, accent, panelFill, panelLine);
            GUI.Label(new Rect(performancePanel.x + 14f, performancePanel.y + 12f, performancePanel.width - 28f, 18f), this.L("PERFORMANCE"), subHeaderStyle);

            rowY = performancePanel.y + 40f;
            bool newFpsBypassEnabled = this.DrawSwitchToggle(new Rect(performancePanel.x + 16f, rowY, performancePanel.width - 32f, rowHeight), this.fpsBypassEnabled, "FPS Bypass");
            if (newFpsBypassEnabled != this.fpsBypassEnabled)
            {
                this.fpsBypassEnabled = newFpsBypassEnabled;
                this.nextFpsBypassTuneAt = 0f;
                this.fpsBypassCompOffset = 0f;
                this.fpsBypassObservedFps = 0f;
                this.ApplyFpsBypass(this.fpsBypassEnabled);
                this.SaveKeybinds(false);
                this.AddMenuNotification(
                    this.fpsBypassEnabled ? this.L("FPS Bypass Enabled") : this.L("FPS Bypass Disabled"),
                    this.fpsBypassEnabled ? new Color(0.55f, 0.88f, 1f) : new Color(0.88f, 0.6f, 0.6f));
            }

            rowY += 30f;
            if (this.fpsBypassEnabled)
            {
                GUI.Label(new Rect(performancePanel.x + 16f, rowY, 180f, 20f), this.LF("Target Max FPS: {0}", this.fpsBypassTarget), rowLabelStyle);
                int newFpsTarget = Mathf.RoundToInt(this.DrawAccentSlider(new Rect(performancePanel.x + 200f, rowY, performancePanel.width - 230f, 20f), (float)this.fpsBypassTarget, 30f, 361f));
                if (newFpsTarget != this.fpsBypassTarget)
                {
                    this.fpsBypassTarget = Mathf.Clamp(newFpsTarget, 30, 360);
                    this.ApplyFpsBypass(true);
                    this.SaveKeybinds(false);
                }

                rowY += 30f;
            }

            this.DrawLodSettingsInPerformancePanel(performancePanel, rowY, rowHeight, rowLabelStyle);

            num += (int)performancePanel.height + (int)sectionGap;

            Rect actionPanel = new Rect(left, (float)num, contentWidth, 100f);
            this.DrawExentriSectionPanel(actionPanel, accent, panelFill, panelLine);
            GUI.Label(new Rect(actionPanel.x + 14f, actionPanel.y + 12f, actionPanel.width - 28f, 18f), this.L("QUICK ACTION"), subHeaderStyle);
            if (this.DrawPrimaryActionButton(new Rect(actionPanel.x + 16f, actionPanel.y + 46f, actionPanel.width - 32f, 36f), "Join My Town"))
            {
                this.StartLobbyAutoJoinMyTown("Manual button");
            }
            num += (int)actionPanel.height + 20;

            return (float)num + 48f;
        }

        private static string FormatKeybindLabel(KeyCode key)
        {
            switch (key)
            {
                case KeyCode.None:
                    return "None";
                case KeyCode.Mouse0:
                    return "LMB";
                case KeyCode.Mouse1:
                    return "RMB";
                case KeyCode.Mouse2:
                    return "MMB";
                case KeyCode.Mouse3:
                    return "Back";
                case KeyCode.Mouse4:
                    return "Forward";
                default:
                    if (key >= KeyCode.Mouse5 && key <= KeyCode.Mouse6)
                    {
                        return "Mouse" + ((int)key - (int)KeyCode.Mouse0 + 1);
                    }

                    string text = key.ToString();
                    if (text.StartsWith("Joystick", StringComparison.Ordinal))
                    {
                        return "Gamepad";
                    }

                    return text;
            }
        }

        private void ApplyActiveKeybind(string bindingLabel, KeyCode newKey)
        {
            switch (bindingLabel)
            {
                case "Toggle Menu": this.keyToggleMenu = newKey; break;
                case "Toggle Radar": this.keyToggleRadar = newKey; break;
                case "Aura Farm": this.keyAuraFarm = newKey; break;
                case "Water + Weed Radius": this.keyWaterWeedRadius = newKey; break;
                case "Auto Fish Farm (Auto Teleport)": this.keyAutoFishingTeleport = newKey; break;
                case "Auto Fishing (Teleport)": this.keyAutoFishingTeleport = newKey; break;
                case "Auto Fish (No Teleport)": this.keyAutoFish = newKey; break;
                case "Fish Shadow Net":
                case "Auto Fish Shadow Net": this.keyAutoFishShadowNet = newKey; break;
                case "Bypass UI": this.keyBypassUI = newKey; break;
                case "Disable All": this.keyDisableAll = newKey; break;
                case "Inspect Player": this.keyInspectPlayer = newKey; break;
                case "Inspect Move": this.keyInspectMove = newKey; break;
                case "Auto Repair": this.keyAutoRepair = newKey; break;
                case "Auto Join Friend": this.keyAutoJoinFriend = newKey; break;
                case "Auto Snow Sculpture": this.autoSnowHotkey = newKey; break;
                case "Auto Sand Sculpture": this.autoSandHotkey = newKey; break;
                case "Auto Sea Clean QTE": this.seaCleanQteHotkey = newKey; break;
                case "Equip Sea Cleaner": this.keyEquipSeaCleaner = newKey; break;
                case "Noclip": this.keyNoclip = newKey; break;
                case "Camera Toggle": this.keyCameraToggle = newKey; break;
                case "Auto Ice Skating": this.keyAutoIceSkating = newKey; break;
                case "Join Public": this.keyJoinPublic = newKey; break;
                case "Join My Town": this.keyJoinMyTown = newKey; break;
                case "Auto Eat": this.keyAutoEat = newKey; break;
                case "Use Bait": this.keyUseBait = newKey; break;
                case "Use Attractor": this.keyUseAttractor = newKey; break;
                case "Anti AFK": this.keyAntiAfk = newKey; break;
                case "Bypass Overlap": this.keyBypassOverlap = newKey; break;
                case "Bird Vacuum": this.keyBirdVacuum = newKey; break;
                case "Game Speed 1x": this.keyGameSpeed1x = newKey; break;
                case "Game Speed 2x": this.keyGameSpeed2x = newKey; break;
                case "Game Speed 5x": this.keyGameSpeed5x = newKey; break;
                case "Game Speed 10x": this.keyGameSpeed10x = newKey; break;
                case "Equip Axe": this.keyEquipAxe = newKey; break;
                case "Equip Net": this.keyEquipNet = newKey; break;
                case "Equip Rod": this.keyEquipRod = newKey; break;
                case "Equip Sprinkler": this.keyEquipSprinkler = newKey; break;
                case "Equip Bird Scanner": this.keyEquipBirdScanner = newKey; break;
                case "Equip Pad": this.keyEquipPad = newKey; break;
                case "Pad Confirm": this.keyPadConfirm = newKey; break;
                case "Pad Cancel": this.keyPadCancel = newKey; break;
                case "Pad Rotate": this.keyPadRotate = newKey; break;
                case "Pad Move": this.keyPadMove = newKey; break;
                case "Pad Delete": this.keyPadDelete = newKey; break;
                case "Auto Insect Farm": this.keyAutoInsectFarm = newKey; break;
                case "Auto Bird Farm": this.keyAutoBirdFarm = newKey; break;
                case "Mass Cook": this.keyMassCook = newKey; break;
                case "Auto Puzzle": this.keyAutoPuzzle = newKey; break;
                case "Auto Cat Play": this.keyAutoCatPlay = newKey; break;
                case "Auto Dog Train": this.keyAutoDogTrain = newKey; break;
                case "Auto Pet Wash": this.keyAutoPetWash = newKey; break;
                case "Feed All Cats": this.keyFeedAllCats = newKey; break;
                case "Feed All Dogs": this.keyFeedAllDogs = newKey; break;
                case "Spawn Bubble": this.keySpawnBubble = newKey; break;
            }

            this.keyBindingActive = "";
            this.keyBindAssignedAt = Time.unscaledTime;
            this.SaveKeybinds(false);
            this.AddMenuNotification(
                this.LF("{0}: {1}", this.L(bindingLabel), FormatKeybindLabel(newKey)),
                new Color(this.uiAccentR, this.uiAccentG, this.uiAccentB));
        }

        private void TryCaptureSideMouseKeybindOnUpdate()
        {
            if (string.IsNullOrEmpty(this.keyBindingActive))
            {
                return;
            }

            for (int button = 3; button <= 6; button++)
            {
                KeyCode mouseKey = KeyCode.Mouse0 + button;
                if (!Input.GetMouseButtonDown(button) && !Input.GetKeyDown(mouseKey))
                {
                    continue;
                }

                this.ApplyActiveKeybind(this.keyBindingActive, mouseKey);
                return;
            }
        }

        private bool TryCaptureKeybindFromEvent(Event e)
        {
            if (e == null || string.IsNullOrEmpty(this.keyBindingActive))
            {
                return false;
            }

            KeyCode newKey = KeyCode.None;
            if (e.isKey && e.type == EventType.KeyDown)
            {
                newKey = e.keyCode;
                if (newKey == KeyCode.None)
                {
                    return false;
                }

                if (newKey == KeyCode.Escape)
                {
                    newKey = KeyCode.None;
                }

                e.Use();
            }
            else if (e.type == EventType.MouseDown && e.button >= 0 && e.button <= 2)
            {
                newKey = KeyCode.Mouse0 + e.button;
                e.Use();
            }
            else
            {
                return false;
            }

            this.ApplyActiveKeybind(this.keyBindingActive, newKey);
            return true;
        }

        private void DrawKeybindRow(ref int y, string label, ref KeyCode key)
        {
            // Allow long labels to wrap and keep the bind button readable.
            GUIStyle lblStyle = new GUIStyle(GUI.skin.label) { wordWrap = true };
            float labelWidth = 140f;
            string displayLabel = this.L(label);
            GUIContent content = new GUIContent(displayLabel);
            float labelH = lblStyle.CalcHeight(content, labelWidth);
            labelH = Mathf.Max(labelH, 20f);

            GUI.Label(new Rect(20f, (float)y, labelWidth, labelH), displayLabel, lblStyle);

            string btnText = FormatKeybindLabel(key);

            // Keep button at fixed width to the right of label area
            float btnX = 160f;
            float btnW = 120f;
            float btnY = y + (labelH > 25f ? (labelH - 25f) * 0.5f : 0f);
            if (GUI.Button(new Rect(btnX, btnY, btnW, 25f), btnText))
            {
                this.keyBindingActive = label;
            }

            y += (int)(labelH + 10f);
        }

        private void BeginKeybindSection(ref int y, float x, float width, string title, int rowCount, GUIStyle sectionStyle, Color accent, Color fill, Color line)
        {
            float sectionHeight = 36f + rowCount * 32f;
            Rect panelRect = new Rect(x, y, width, sectionHeight);
            this.DrawExentriSectionPanel(panelRect, accent, fill, line);
            GUI.Label(new Rect(panelRect.x + 14f, panelRect.y + 10f, panelRect.width - 28f, 18f), this.L(title), sectionStyle);
            y += 36;
        }

        private void DrawKeybindRowInPanel(ref int y, float panelX, float panelWidth, string label, ref KeyCode key)
        {
            float rowX = panelX + 14f;
            float rowWidth = panelWidth - 28f;
            Rect rowRect = new Rect(rowX, (float)y, rowWidth, 28f);
            Color rowFill = new Color(1f, 1f, 1f, 0.025f);
            Color rowLine = new Color(1f, 1f, 1f, 0.04f);
            this.DrawRoundedPanel(rowRect, 5f, rowFill, rowLine, 1f, Color.clear);

            GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.fontSize = 12;
            labelStyle.alignment = TextAnchor.MiddleLeft;
            labelStyle.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB);

            GUI.Label(new Rect(rowRect.x + 10f, rowRect.y + 1f, rowRect.width - 156f, rowRect.height - 2f), this.L(label), labelStyle);

            string btnText = FormatKeybindLabel(key);

            Rect bindRect = new Rect(rowRect.xMax - 134f, rowRect.y + 3f, 124f, 22f);
            GUIStyle bindStyle = this.themeTopTabStyle ?? GUI.skin.button;
            if (GUI.Button(bindRect, btnText, bindStyle))
            {
                this.keyBindingActive = label;
            }

            y += 32;
        }

        private float DrawAboutTab(int startY)
        {
            const float left = 24f;
            const float width = 540f;
            float y = startY + 8f;
            Color textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB);
            Color mutedColor = new Color(this.uiSubTabTextR, this.uiSubTabTextG, this.uiSubTabTextB, 0.92f);
            Color accent = new Color(this.uiAccentR, this.uiAccentG, this.uiAccentB);

            GUIStyle titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };
            titleStyle.normal.textColor = textColor;

            GUIStyle headingStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.UpperLeft,
                wordWrap = true
            };
            headingStyle.normal.textColor = accent;

            GUIStyle bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                alignment = TextAnchor.UpperLeft,
                wordWrap = true,
                richText = true
            };
            bodyStyle.normal.textColor = mutedColor;

            GUI.Label(new Rect(left, y, width, 28f), "Bugtopia", titleStyle);
            y += 30f;
            GUI.Label(new Rect(left, y, width, 40f), "Automation and utility mod for Heartopia.", bodyStyle);
            y += 44f;

            GUI.Label(new Rect(left, y, width, 20f), "What it does", headingStyle);
            y += 22f;
            GUI.Label(new Rect(left, y, width, 56f),
                "Farming, gathering, teleport, radar, bag tools, and other QoL helpers — from one in-game menu. Press Insert to open it.",
                bodyStyle);
            y += 62f;

            GUI.Label(new Rect(left, y, width, 20f), "Open & free", headingStyle);
            y += 22f;
            GUI.Label(new Rect(left, y, width, 40f),
                "Bugtopia will always stay open-source and free for everyone.",
                bodyStyle);
            y += 46f;

            GUI.Label(new Rect(left, y, width, 20f), "Credits", headingStyle);
            y += 22f;
            GUI.Label(new Rect(left, y, width, 56f),
                "Based on Heartopia Helper by Rayyy2.\nThank you to everyone who shares ideas for new features.",
                bodyStyle);
            y += 62f;

            GUI.Label(new Rect(left, y, width, 20f), "Disclaimer", headingStyle);
            y += 22f;
            GUI.Label(new Rect(left, y, width, 56f),
                "For educational and research use only. Use at your own risk; you are responsible for any account actions taken by the game operator.",
                bodyStyle);
            y += 62f;

            GUI.Label(new Rect(left, y, width, 20f), $"Version {ModBuildVersion.Display} · bugtopia.dll", bodyStyle);
            y += 28f;

            return y + 12f;
        }

    }
}
