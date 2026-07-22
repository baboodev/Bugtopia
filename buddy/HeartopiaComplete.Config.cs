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
            data.sanrioGachaFinderEnabled = this.sanrioGachaFinderEnabled;
            data.sanrioDropDayStamp = this.sanrioDropDayStamp;
            data.sanrioDropTotalToday = this.sanrioDropTotalToday;
            data.sanrioDropSceneDoneMask = this.sanrioDropSceneDoneMask;
            data.swimSprintTweakEnabled = this.swimSprintTweakEnabled;
            data.swimSprintDurationSeconds = this.swimSprintDurationSeconds;
            data.swimSprintCooldownSeconds = this.swimSprintCooldownSeconds;
            data.swimSprintVerticalGuardEnabled = this.swimSprintVerticalGuardEnabled;
            this.SaveGameUiTimingsToConfig(data);
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
            data.chatForceTranslateEnabled = this.chatForceTranslateEnabled;
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
            this.sanrioGachaFinderEnabled = data.sanrioGachaFinderEnabled;
            this.sanrioDropDayStamp = data.sanrioDropDayStamp;
            this.sanrioDropTotalToday = data.sanrioDropTotalToday;
            this.sanrioDropSceneDoneMask = data.sanrioDropSceneDoneMask;
            this.swimSprintTweakEnabled = data.swimSprintTweakEnabled;
            this.swimSprintDurationSeconds = data.swimSprintDurationSeconds <= 0f
                ? SwimSprintDurationDefault
                : Mathf.Clamp(data.swimSprintDurationSeconds, SwimSprintDurationMin, SwimSprintDurationMax);
            this.swimSprintCooldownSeconds = Mathf.Clamp(data.swimSprintCooldownSeconds, SwimSprintCooldownMin, SwimSprintCooldownMax);
            this.swimSprintVerticalGuardEnabled = data.swimSprintVerticalGuardEnabled;
            this.LoadGameUiTimingsFromConfig(data);
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
            this.chatForceTranslateEnabled = data.chatForceTranslateEnabled;
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


        // Shared implementation for BOTH rendering surfaces of the Keybinds reset — the IMGUI
        // RESET TO DEFAULTS button above and the UGUI Settings→Keybinds twin
        // (HeartopiaComplete.UguiKeybindsContent.cs) — extracted verbatim from the inline
        // DrawSettingsTab block (the FishingRouteFeature.RemoveCustomSpotAt precedent). NOTE:
        // this resets more than keybinds — it also restores several Settings→Main fields
        // (notifications, ID display, FPS bypass, LOD override, status overlay), exactly as the
        // inline block always did.
        private void ResetKeybindSettingsToDefaults()
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


    }
}
