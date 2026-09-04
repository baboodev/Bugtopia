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
        private class MenuNotification
        {
            public string Key;
            public string Message;
            public Color Color;
            public float CreatedAt;
            public float ExpireAt;
            public float Duration;
            public bool Force;
        }

        [Serializable]
        public class KeybindConfigData
        {
            public int keyToggleMenu;
            public int keyToggleRadar;
            public int keyActionPanel;
            public int keyAuraFarm;
            public int keyWaterWeedRadius;
            public int keyAutoFish;
            public int keyAutoFishingTeleport;
            public int keyAutoFishShadowNet;
            public int keyBypassUI;
            public int keyDisableAll;
            public int keyInspectPlayer;
            public int keyInspectMove;
            public int keyAutoRepair;
            public int keyQuestWalk;
            public int keyAutoJoinFriend;
            public int keyJoinPublic;
            public int keyJoinMyTown;
            public int keyNoclip;
            public int keyCameraToggle;
            public int keyAutoIceSkating;
            public int keyAutoEat;
            public int keyUseBait;
            public int keyUseAttractor;
            public int keyAntiAfk;
            public int keyBypassOverlap;
            public int keyBirdVacuum;
            public int keyAutoSnow;
            public int keyAutoSand;
            public int keySeaCleanQte;
            public int keyEquipSeaCleaner;
            // 0/absent = "use default" (radius can never legitimately be 0).
            public float seaCleanAutoRadius;
            // "Clean Without Delays" toggle (replaced the old 0-1s delay slider). Default true (no
            // delay = instant in-range sweep); old configs lacking the element keep this initializer.
            public bool seaCleanCleanNoDelay = true;
            // Aura Farm: auto-teleport to a cleansing coral area while the Corrupted debuff (610)
            // is active and hold until it clears. Default true; old configs keep the initializer.
            public bool autoCleanseCorruptedEnabled = true;
            // "Hide Crystal Clear Banner" — suppress the sea-clean cleanliness-stage banner
            // (SeaCleanCleanBannerPanel). Default false = banner shows as vanilla.
            public bool hideSeaCleanBannerEnabled;
            // "Disable OOB Teleport" — suppress the client-side out-of-bounds rescue
            // (PlayerFishingShipComponent.TryBackToShip / LeaveShipThenTeleportToSafePos).
            // Default false = vanilla: leaving the scene's detect box teleports you to a safe point.
            public bool disableOobTeleportEnabled;
            // Noclip: stream the driven position to the server at the game's 20 Hz movement tick
            // instead of letting it land in one jump on release. Default true; old configs lacking
            // the element keep this initializer.
            public bool noclipSyncPositionEnabled = true;
            // Instant Teleport: refuse the game's animated transfer command and warp directly.
            // The wait-for-field companion defaults to ON (old configs keep the initializer).
            public bool instantTeleportEnabled;
            public bool instantTeleportWaitFieldLoaded = true;
            // Little Whale figurine finder (daily photo hide-and-seek, MapDynamicResource 300023-33).
            public bool littleWhaleFinderEnabled;
            // Sanrio gacha machine finder (event scene machines, MapDynamicResource 11305-07).
            public bool sanrioGachaFinderEnabled;
            // Sanrio finder daily-drop tracker (successes the mod observed; 06:00 game-day key).
            public long sanrioDropDayStamp;
            public int sanrioDropTotalToday;
            public int sanrioDropSceneDoneMask;
            // Custom Swim Sprint (underwater dash SwimSprintConfig override). Duration 0/absent =
            // "use default 0.5s"; slider max (30) = Infinite. Cooldown 0 = instant re-dash.
            public bool swimSprintTweakEnabled;
            public float swimSprintDurationSeconds;
            public float swimSprintCooldownSeconds;
            // Space/Ctrl (ascend/descend) no longer cancel the underwater dash (detour guard).
            public bool swimSprintVerticalGuardEnabled;
            // Custom Jump (MotionConfig jump-arc override, JumpTuningFeature.cs). Heights are the
            // two APEXES in metres (hold = JumpingHighest verbatim, tap = converted to
            // JumpingInitSpeed); gravity/fall limit are stored POSITIVE and negated on write.
            // 0/absent on any of the four = "use the game default".
            public bool jumpTuningEnabled;
            public float jumpTuningHoldHeight;
            public float jumpTuningTapHeight;
            public float jumpTuningGravity;
            public float jumpTuningFallSpeedLimit;
            // Game UI tip/toast display-time overrides (TipShowTimeConfig), seconds, ordered as
            // GameUiTimingFieldNames (GameUiTimingsFeature.cs). Null/short/0 entries = game defaults.
            public bool gameUiTimingsEnabled;
            public float[] gameUiTimingSeconds;
            public int keyGameSpeed1x;
            public int keyGameSpeed2x;
            public int keyGameSpeed5x;
            public int keyGameSpeed10x;
            public int keyEquipAxe;
            public int keyEquipNet;
            public int keyEquipRod;
            public int keyEquipSprinkler;
            public int keyEquipBirdScanner;
            public int keyEquipPad;
            public int keyPadConfirm;
            public int keyPadCancel;
            public int keyPadRotate;
            public int keyPadMove;
            public int keyPadDelete;
            public int keyAutoInsectFarm;
            public int keyAutoBirdFarm;
            public int keyMassCook;
            public int keyAutoPuzzle;
            public int keyAutoCatPlay;
            public int keyAutoDogTrain;
            public int keyAutoPetWash;
            public int keyFeedAllCats;
            public int keyFeedAllDogs;
            public int keySpawnBubble;
            public float noclipSpeed;
            public float noclipBoostMultiplier;
            public float areaLoadDelay;
            public float auraCollectWaitTimeout;
            // Foraging: min real-time seconds between Aura Farm teleports (0-10, 0 = off).
            public float foragingTeleportDelaySeconds;
            // "Stealth Foraging" — while the farm runs: force noclip, suppress the OOB rescue and
            // land resource hops BELOW the node (contamination -5m, everything else -1.5m).
            // Default false = vanilla hops.
            public bool stealthForagingEnabled;
            // "Walk to Nodes" — ground-walk the Track waypoint route to each node instead of
            // teleporting (FarmWalkFeature.cs). Mutually exclusive with Stealth Foraging, and
            // forces game speed to 1x for the run. Default false = vanilla hops.
            public bool farmWalkToNodeEnabled;
            // "Compare Game Track" — ask the game to route to the same node itself and log where
            // the two differ. Diagnostic only; it clears the player's own manual track, so it is
            // off by default.
            public bool farmWalkTrackCompareEnabled;
            // "Walk to Zone Point" — travel to the next farm area on foot instead of the area:*
            // teleport. Independent of the vehicle switch below.
            public bool farmWalkToAreaEnabled;
            // "Use Vehicle" + its distance slider — summon the default vehicle for a long zone
            // haul. Land only; underwater summons are rejected by the server.
            public bool farmWalkUseVehicleEnabled;
            public float farmWalkVehicleMinDistance;
            // Distance from the destination at which the driver gets out.
            public float farmWalkVehicleDismountDistance;
            public float resourceAutoRepairPauseSeconds;
            public float gameSpeed;
            public bool fpsBypassEnabled;
            public int fpsBypassTarget;
            // FPS Watchdog (FpsWatchdogFeature.cs). SAVED INVERTED, and that is deliberate: the
            // watchdog defaults ON, but this class has no constructor, so every bool an existing
            // Config.xml does not carry deserializes to false. Persisting "enabled" would silently
            // switch the watchdog off for every user who already has a config; persisting
            // "disabled" makes the missing element mean exactly what it should — still on.
            public bool fpsWatchdogDisabled;
            public int fpsWatchdogHitchMs;
            public int fpsWatchdogLowFps;
            public int lodOverrideMode;
            public float lodCustomBias;
            public int lodCustomMaxLevel;
            public bool gameLodFurnitureEnabled;
            public int gameLodFurnitureMaxObjects;
            public int gameLodFurnitureDistance;
            public int gameLodFurnitureMeshDistance;
            // NOTE: gameLodForceLod0Enabled was removed 2026-07-27 (the flag blanked every UGC
            // texture). Old Config.xml files still carrying the element deserialize fine — the XML
            // serializer ignores unknown elements — so no migration is needed.
            public bool gameLodBrgBiasEnabled;
            public float gameLodBrgBias;
            public bool gameLodVegetationEnabled;
            public int gameLodVegetationPref;      // legacy (pre-2026-07-25): raw PC_LODBIAS value
            public float gameLodVegetationMult;
            public int gameLodVegetationBaselinePref;
            public int gameLodVegetationTargetPref;
            public bool gameLodVegetationApplyDuringLoad;
            public bool gameLodSignificanceOffEnabled;
            public bool gameLodNineCellEnabled;
            public float gameLodNineCellMult;
            public bool gameLodShadowEnabled;
            public float gameLodShadowDistance;
            public bool gameLodHlodEnabled;
            public float gameLodHlodMult;
            public bool gameLodXdLodEnabled;
            public bool customCameraFOVEnabled;
            public float cameraFOV;
            public bool hideJumpButtonEnabled;
            public bool noCollisionPlayerEnabled;
            public bool noCollisionVehicleEnabled;
            public bool bunnyHopEnabled;
            public bool analogMoveBridgeEnabled;
            public bool skipShowOffAnimations;
            public bool quietCongratsPopups;
            public bool quietBpPayRewardPopup;
            public bool emoteUnlockEnabled;
            public bool friendInteractUnlockEnabled;
            public bool foragingAnimEnabled;
            public bool skipCraftDyeAnimations;
            public bool autoLearnRecipes;
            public bool autoLikeOwnHome;
            public bool craftDirectSendEnabled;
            public bool interactObstacleBypassEnabled;
            public bool interactBuildModeBypassEnabled;
            public bool persistentHudEnabled;
            // Self-tab bypass toggles. These were session-only until now even though their UI
            // handlers already called SaveKeybinds — the fields simply had no home in the config.
            public bool vehicleBypassEnabled;
            public bool vehicleBypassServerEventsEnabled;
            public bool warehouseBypassEnabled;
            public bool strangerChatBypassEnabled;
            public bool chatForceTranslateEnabled;
            public bool chatTranslateForceAllLangs;
            public bool blockTutorials;
            public bool partyAutoDeclineInvites;
            public bool partyAutoLeaveParties;
            public bool activityAutoDeclineInvites;
            public bool activityAutoLeaveEvents;
            // Settings -> Logging: all MasterLog* verbose switches, persisted since the whole set
            // was made default-OFF. Field names match the static flags 1:1 so the XML is greppable.
            public bool MasterLogAuraFarm;
            public bool MasterLogBirdFarm;
            public bool MasterLogBirdFarmCrashTrace;
            public bool MasterLogInsectFarm;
            public bool MasterLogAutoFish;
            public bool MasterLogCombinedFarm;
            public bool MasterLogInstantCatch;
            public bool MasterLogAutoFarm;
            public bool MasterLogForagingTeleport;
            public bool MasterLogQuestAssistant;
            public bool MasterLogAutoEatRepair;
            public bool MasterLogNpcTeleport;
            public bool MasterLogNetCook;
            public bool MasterLogNetCookScan;
            public bool MasterLogPuzzle;
            public bool MasterLogAutoSell;
            public bool MasterLogRadarIconEsp;
            public bool MasterLogMapSpots;
            // Defaults ON — one line per session, and it is the answer to "can the live scan replace
            // the hardcoded arrays". An old Config.xml has no element, so the initializer holds.
            public bool MasterLogGatherScan = true;
            public bool MasterLogGatherHarvest;
            public bool MasterLogBubbleRadar;
            public bool MasterLogAutoBuy;
            public bool MasterLogForceOpenShop;
            public bool MasterLogPetPlay;
            public bool MasterLogPetFeed;
            public bool MasterLogWildAnimalFeed;
            public bool MasterLogHomelandFarm;
            public bool MasterLogPadBuild;
            public bool MasterLogWildAnimalGift;
            public bool MasterLogAutoIceSkating;
            public bool MasterLogDailyQuestSubmit;
            public bool MasterLogDailyClaims;
            public bool MasterLogBirdPhotoSubmit;
            public bool MasterLogStrangerChat;
            public bool MasterLogGameEvents;
            public bool MasterLogEntityEvents;
            public bool MasterLogGameIcons;
            public bool MasterLogPersistentHud;
            public bool MasterLogSandSculpture;
            public bool MasterLogShowOffBypass;
            public bool MasterLogSnowSculpture;
            public bool MasterLogSeaCleanQte;
            public bool MasterLogCorruptionCleanse;
            public bool MasterLogUnderwaterRadar;
            public bool MasterLogGameLod;
            public bool MasterLogWorldStage;
            public bool MasterLogInputMap;
            public bool MasterLogPartyAutoDecline;
            public bool MasterLogActivityAutoDecline;
            public bool MasterLogFpsWatchdog;
            // Ten flags that existed in code but had no config field and no Logging-tab row, so
            // they could only be changed by editing the source and rebuilding. Added together with
            // the Tier-1/Tier-2 split — see FeatureLog.cs.
            public bool MasterLogActionPanel;
            public bool MasterLogAutoLearn;
            public bool MasterLogCraftAnimSkip;
            public bool MasterLogCraftDirectSend;
            public bool MasterLogEmoteUnlock;
            public bool MasterLogInteractObstacle;
            public bool MasterLogForagingAnim;
            public bool MasterLogHomeLike;
            public bool MasterLogMusicPlayer;
            public bool MasterLogRepairThrowTrim;
            // Defaults ON, like MasterLogGatherScan: the flag has shipped `true` and its output is
            // one register line plus a highlight-block line, so turning it silently off on upgrade
            // would be a regression. The initializer must live on the CONFIG field too — an old
            // Config.xml has no element, and XmlSerializer only overwrites what is present.
            public bool MasterLogTutorialBlock = true;
            public bool autoIceSkatingEnabled;
            public int autoIceSkatingMinUltimateScore = 900;
            public bool autoIceSkatingOnlyX2Ultimate = true;
            public bool autoIceSkatingLast30sUltimate = true;
            public bool autoIceSkatingPerfectMove = true;
            public bool autoIceSkatingPreferNewMove = true;
            public int iceSkatingChallengeEndScore = 1500;
            public int shopBuyAllMaxPerItem = 200;
            public float snowStartDelaySeconds = 0.3f;      // fill+start -> first report pause
            public float snowNextCycleDelaySeconds = 0.5f;  // gather -> next fill pause
            public int snowballUseLimit = 0;                // sculptures per run; 0 = unlimited
            public int snowQteSuccessCount = 20;            // perfect QTE reports per sculpture (0-20)
            public bool fastBubbleGenEnabled;
            public bool musicPlayerLoop;
            public bool musicPlayerNetworkMode;
            public bool musicPlayerSourceGameRecords;
            public string musicPlayerSelectedTrack = string.Empty;
            public float bubbleBubblesPerMinute;
            public bool bubbleSpawnAtPlayerEnabled;
            public bool autoBubbleCollectEnabled;
            public float autoBubbleCollectRadius = 10f; // 0 = unlimited, default 10m
            public float petFeedScanRadiusMeters;
            public float netCookInterval;
            public float netCookScanRadiusMeters;
            public bool netCookMiniGameOnly;
            public bool netCookMoveIngredients;
            public bool netCookRememberStoves;
            public bool netCookCaptureOwnOnly;
            public bool netCookCaptureRadiusOnly;
            public bool netCookUseAllIngredients;
            public int netCookCookQuantity;
            public float homelandFarmWaterRadius;
            public bool homelandFarmAutoFertilizeEnabled;
            public float autoFishScanTimeout = -1f;
            public float autoFishTeleportDelay = -1f;
            public float autoFishFishShadowDetectRange = -1f;
            public bool autoFishInstantCatch = false;
            public float autoFishInstantCatchSendHz = -1f;
            public bool autoFishAutoBaitEnabled = false;
            public int autoFishAutoBaitChoice = 1;        // 0 = Bait, 1 = Attractor
            public int autoFishAutoBaitMax = -1;
            public float autoFishAutoBaitNoFishSeconds = -1f;
            public bool autoFishSkipCatchAnim = false;
            public bool autoFishSkipCastAnim = false;
            public bool autoFishSkipBaitAnim = false;
            public bool autoFishKeepCameraAndHud = false;
            public bool autoFishServerSide = false;
            public bool fishingRouteCustomOnly = false;
            public float autoFishReelMaxDuration = -1f;
            public float autoFishReelHoldDuration = -1f;
            public float autoFishReelPauseDuration = -1f;
            public float insectCatchCooldown;
            public float insectScanRange;
            public int insectBatchSize = 3;
            public bool insectTeleportEnabled = true;
            public bool insectPauseTeleportOnTriggersEnabled;
            public bool insectPauseTeleportOnRepairEnabled;
            public bool insectPauseTeleportOnEatEnabled;
            public float insectRepairTeleportPauseSeconds;
            public float insectEatTeleportPauseSeconds;

            // Combined Farming (CombinedFarmFeature). Negative/empty = "not in this file yet", so an
            // older config loads the feature's own defaults instead of zeroing its windows.
            public bool combinedFarmEnabled = true;
            public bool combinedFarmRepairStowedTools = true;
            public string combinedFarmPriorityOrder = string.Empty;
            public float combinedFarmEmptySliceSeconds = -1f;
            public float combinedFarmPreemptConfirmSeconds = -1f;
            public bool notificationsEnabled;
            public int notificationPosition = 5;
            public bool blockGameUiWhenMenuOpen;
            public bool privacyBlockLogUploads;
            public bool privacyBlockRoomMerges;
            public bool privacyBlockSpamReports;
            public bool privacyBlockUploadCheat;
            public bool privacyBlockFriendVisitNotify;
            public bool mapRevealBlockedPlayers;
            public bool stealthBlockEnabled;
            public bool stealthBlockNotifyFriends;
            // Registry of blocks WE issued — persisted so a crash mid-run does not orphan them on
            // the server with no way to tell ours from the user's own manual blocks.
            public List<long> stealthBlockOwnedShortIds = new List<long>();
            public bool autoClickStartEnabled;
            public bool autoCloseAnnouncementEnabled;
            public int maxAutoEatAttempts;
            public bool showStatusOverlay;
            public bool hideIdEnabled;
            public bool customDisplayIdEnabled;
            public string customDisplayId;
            public bool antiAfkEnabled;
            public bool mouseLookEnabled;
            public bool showMouseLookCrosshair;
            public float antiAfkInterval;
            public int autoRepairType;
            public int autoRepairUseTarget;
            public int autoEatFoodType;
            public string autoEatCustomFoodName;
            public bool repairTeleportBackEnabled;
            public bool autoRepairOnToastEnabled;
            public bool autoRepairNoAnimationEnabled;
            public bool autoRepairThrowAtFeetEnabled;
            public bool trimRepairThrowAnimation = true;
            // One-shot marker for the 2026-08-06 switch of the primary repair-throw path from the
            // direct send to the trimmed game throw. Absent in every config written before that, so
            // a false here means "this config predates the switch" and the load forces the new
            // primary once. Without it the change would be invisible to existing installs.
            public bool repairThrowPathTrimMigrated;
            public bool autoEatOnToastEnabled;
            public bool autoEatAutoTriggerEnabled;
            public bool autoEatNoAnimationEnabled = true;
            public int autoRepairTriggerPercent;
            public int autoEatTriggerPercent;
            public bool autoSellEnabled;
            public string autoSellItemKey;
            public int autoSellMaxPerStack;
            public int autoSellReserveCount;
            public bool autoSellAllMatchingStacks;
            public bool autoSellFullStack;
            public bool dailyQuestSubmitSkipFiveStar;
            public bool dailyClaimsAutoClaimEnabled;
            public bool autoSellMatchFamily;
            public bool autoSellHideBagItems;
            public int autoSellSelectedStaticId;
            public int autoSellSelectedStar;
            public float autoSellInterval;
            public int autoSellScanSource;
            public bool autoSellFestivalTokensEnabled;
            public bool ugcCacheRaiseLimitEnabled;
            public int ugcCacheTargetCapacity;
        }

        [Serializable]
        public class UiThemeConfigData
        {
            // Palette schema version; configs saved before the 2.0 redesign carry 0 and their
            // palette is discarded on load (scale is kept) so the new defaults take effect.
            public int uiThemeVersion;
            public float uiAccentR;
            public float uiAccentG;
            public float uiAccentB;
            public float uiHeaderR;
            public float uiHeaderG;
            public float uiHeaderB;
            public float uiSuccessR;
            public float uiSuccessG;
            public float uiSuccessB;
            public float uiTextR;
            public float uiTextG;
            public float uiTextB;
            public float uiMainTabTextR;
            public float uiMainTabTextG;
            public float uiMainTabTextB;
            public float uiSubTabTextR;
            public float uiSubTabTextG;
            public float uiSubTabTextB;
            public float uiWindowR;
            public float uiWindowG;
            public float uiWindowB;
            public float uiPanelR;
            public float uiPanelG;
            public float uiPanelB;
            public float uiContentR;
            public float uiContentG;
            public float uiContentB;
            public float uiWindowAlpha;
            public float uiPanelAlpha;
            public float uiContentAlpha;
            public float uiScale;
            // Force the legacy UnityEngine.UI.Text renderer instead of TMP. Exposed so the TMP and
            // legacy renderings can be compared side by side (they differ most on CJK), and as an
            // escape hatch if TMP ever fails on a build. Default false = TMP.
            public bool uiLegacyTextRenderer;
            // (No font field: the UGUI shell is hard-pinned to LiberationSans SDF. A "uiFontName"
            // key from the removed picker may still sit in older saved configs — it is simply
            // ignored on load.)
        }

        [Serializable]
        public class RadarConfigData
        {
            public int radarMarkerStyle;
            public float radarMaxDistance = 75f;
            public int radarDisplayMode = 0; // 0 = ESP overlay, 1 = in-game map spots
            public int radarGameTrackLimit = 5; // Game mode: max nearest resources tracked on the map
            public bool radarBigMapSpots = false; // Game mode: also show markers on the big map
            public bool radarPlayerAvatarsAll = false; // real avatar photos on map markers for ALL players (detour)
            // Real names for non-friends (nameplate / map label / chat). Split out of radarPlayerAvatarsAll,
            // which used to drive BOTH detour groups. TRI-STATE so the split is a silent no-op for existing
            // configs: -1 = key absent (pre-split file) -> inherit radarPlayerAvatarsAll on load, 0/1 = an
            // explicit choice the user has made since. Same legacy-mapping idiom as resourceVisualEspStyle == 3.
            public int radarPlayerNamesAll = -1;
            public bool resourceVisualEspEnabled = true;
            public int resourceVisualEspStyle = 0;
            public bool resourceVisualEspShowDistance = true;
            public bool resourceVisualEspShowConnector = true;
            public bool resourceVisualEspShowOffscreen = true;
            public bool resourceVisualEspShowGroundRing = false;
            public float resourceVisualEspScale = 1f;
            public float resourceVisualEspOpacity = 0.92f;
            public int resourceVisualEspMaxMarkers = 120;
            public bool priorityCapybaraSlab;
            public bool priorityOakSlab;
        }

        [Serializable]
        public class BirdFarmConfigData
        {
            public bool perfectPhotoEnabled = false;
            public bool autoScareMaxPhotoEnabled = true;
            public int captureMode = 0;
            public float catchCooldown = 1.5f;
            public float scanRange = 35f;
            public int multiCatchLimit = 1;
        }

        // One user-chosen game-key override. Addressed by action MAP + binding INDEX rather than by
        // action name, because that is exactly what ApplyBindingOverride takes and because a map
        // can hold several bindings for the same action (composite parts, alternates). Stable
        // across sessions since the asset ships with the game; a game update that reorders bindings
        // would invalidate these, which is why applying one that no longer matches is a no-op
        // rather than an error (see GameKeyBindings.cs).
        [Serializable]
        public class GameKeyOverrideEntry
        {
            public string Binding = "";   // "<map>/<index>", e.g. "AllKeyboardKeysMap/40"
            public string Path = "";      // control path, e.g. "<Keyboard>/e"
        }

        [Serializable]
        public class UnifiedConfigData
        {
            public List<GameKeyOverrideEntry> GameKeyOverrides = new List<GameKeyOverrideEntry>();
            public KeybindConfigData Keybinds = new KeybindConfigData();
            public UiThemeConfigData UiTheme = new UiThemeConfigData();
            public RadarConfigData Radar = new RadarConfigData();
            public BirdFarmConfigData BirdFarm = new BirdFarmConfigData();
            public List<CustomTeleportEntry> CustomTeleports = new List<CustomTeleportEntry>();
            public List<CustomTeleportEntry> FishingRouteSpots = new List<CustomTeleportEntry>();
            public string Language = "en";
        }

    }
}
