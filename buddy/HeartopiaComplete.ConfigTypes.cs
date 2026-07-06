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
        public class PatrolData { public List<SerializableVector3> Points = new List<SerializableVector3>(); }

        [Serializable]
        public class SerializableVector3
        {
            public float x, y, z;
            public SerializableVector3() { }
            public SerializableVector3(Vector3 v) { x = v.x; y = v.y; z = v.z; }
            public Vector3 ToVector3() { return new Vector3(x, y, z); }
        }

        [Serializable]
        public class SerializableQuaternion
        {
            public float x, y, z, w;
            public SerializableQuaternion() { }
            public SerializableQuaternion(Quaternion q) { x = q.x; y = q.y; z = q.z; w = q.w; }
            public Quaternion ToQuaternion() { return new Quaternion(x, y, z, w); }
        }

        [Serializable]
        public class CookingPatrolPoint
        {
            public SerializableVector3 Position;
            public SerializableQuaternion Rotation;
            public CookingPatrolPoint() { }
            public CookingPatrolPoint(Vector3 pos, Quaternion rot)
            {
                Position = new SerializableVector3(pos);
                Rotation = new SerializableQuaternion(rot);
            }
        }

        [Serializable]
        public class TreeFarmPatrolPoint
        {
            public SerializableVector3 Position;
            public SerializableQuaternion Rotation;
            public TreeFarmPatrolPoint() { }
            public TreeFarmPatrolPoint(Vector3 pos, Quaternion rot)
            {
                Position = new SerializableVector3(pos);
                Rotation = new SerializableQuaternion(rot);
            }
        }

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

        public class CookingPatrolData
        {
            public List<CookingPatrolPoint> Points = new List<CookingPatrolPoint>();
        }

        public class TreeFarmPatrolData
        {
            public List<TreeFarmPatrolPoint> Points = new List<TreeFarmPatrolPoint>();
        }

        [Serializable]
        public class KeybindConfigData
        {
            public int keyToggleMenu;
            public int keyToggleRadar;
            public int keyAutoForaging;
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
            public float resourceTeleportCooldown;
            public float resourceClickDuration;
            public float resourceAutoRepairPauseSeconds;
            public float gameSpeed;
            public bool fpsBypassEnabled;
            public int fpsBypassTarget;
            public int lodOverrideMode;
            public float lodCustomBias;
            public int lodCustomMaxLevel;
            public bool customCameraFOVEnabled;
            public float cameraFOV;
            public bool hideJumpButtonEnabled;
            public bool bunnyHopEnabled;
            public bool analogMoveBridgeEnabled;
            public bool skipShowOffAnimations;
            public bool persistentHudEnabled;
            public bool autoIceSkatingEnabled;
            public int autoIceSkatingMinUltimateScore = 900;
            public bool autoIceSkatingOnlyX2Ultimate = true;
            public bool autoIceSkatingLast30sUltimate = true;
            public bool autoIceSkatingPerfectMove = true;
            public bool autoIceSkatingPreferNewMove = true;
            public int iceSkatingChallengeEndScore = 1500;
            public int shopBuyAllMaxPerItem = 200;
            public bool fastBubbleGenEnabled;
            public float bubbleBubblesPerMinute;
            public bool bubbleSpawnAtPlayerEnabled;
            public bool autoBubbleCollectEnabled;
            public float autoBubbleCollectRadius;
            public float cookingAutoSpeed;
            public float cookingWaitAtSpot;
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
            public bool notificationsEnabled;
            public int notificationPosition = 5;
            public bool blockGameUiWhenMenuOpen;
            public bool privacyBlockLogUploads;
            public bool privacyBlockRoomMerges;
            public bool privacyBlockSpamReports;
            public bool privacyBlockUploadCheat;
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
            public bool autoSellSkipFiveStar;
            public bool dailyQuestSubmitSkipFiveStar;
            public bool autoSellMatchFamily;
            public bool autoSellHideBagItems;
            public int autoSellStarFilter;
            public float autoSellInterval;
            public int autoSellScanSource;
            public bool autoSellFestivalTokensEnabled;
            public bool collectEventResources;
            public bool auraFarmLootCollectEnabled;
            public float auraFarmLootCollectDistance;
        }

        [Serializable]
        public class UiThemeConfigData
        {
            public float uiAccentR;
            public float uiAccentG;
            public float uiAccentB;
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
            public bool resourceVisualEspEnabled = true;
            public int resourceVisualEspStyle = 0;
            public bool resourceVisualEspShowDistance = true;
            public bool resourceVisualEspShowConnector = true;
            public bool resourceVisualEspShowOffscreen = true;
            public bool resourceVisualEspShowGroundRing = false;
            public float resourceVisualEspScale = 1f;
            public float resourceVisualEspOpacity = 0.92f;
            public int resourceVisualEspMaxMarkers = 120;
            public bool priorityFiddlehead;
            public bool priorityTallMustard;
            public bool priorityBurdock;
            public bool priorityMustardGreens;
        }

        [Serializable]
        public class NamedCookingPatrolSave
        {
            public string Name;
            public List<CookingPatrolPoint> Points = new List<CookingPatrolPoint>();
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

        [Serializable]
        public class UnifiedConfigData
        {
            public KeybindConfigData Keybinds = new KeybindConfigData();
            public UiThemeConfigData UiTheme = new UiThemeConfigData();
            public RadarConfigData Radar = new RadarConfigData();
            public BirdFarmConfigData BirdFarm = new BirdFarmConfigData();
            public PatrolData Patrol = new PatrolData();
            public TreeFarmPatrolData TreeFarmPatrol = new TreeFarmPatrolData();
            public List<NamedCookingPatrolSave> CookingPatrolSaves = new List<NamedCookingPatrolSave>();
            public List<CustomTeleportEntry> CustomTeleports = new List<CustomTeleportEntry>();
            public List<CustomTeleportEntry> FishingRouteSpots = new List<CustomTeleportEntry>();
            public string Language = "en";
        }

    }
}
