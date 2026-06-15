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
    // Toast hook moved to ToastHook.cs

    // Token: 0x02000004 RID: 4
    public partial class HeartopiaComplete
    {

        // Keybinds Management
        private KeyCode keyToggleMenu = KeyCode.Insert;
        private KeyCode keyToggleRadar = KeyCode.None;
        private KeyCode keyAutoForaging = KeyCode.None;
        private KeyCode keyAuraFarm = KeyCode.None;
        private KeyCode keyWaterWeedRadius = KeyCode.None;
        private KeyCode keyAutoFish = KeyCode.None;
        private KeyCode keyAutoFishingTeleport = KeyCode.None;
        private KeyCode keyAutoFishShadowNet = KeyCode.None;
        private KeyCode keyBypassUI = KeyCode.None;
        private KeyCode keyDisableAll = KeyCode.None;
        private KeyCode keyInspectPlayer = KeyCode.None;
        private KeyCode keyInspectMove = KeyCode.None;
        private KeyCode keyAutoRepair = KeyCode.None;
        private KeyCode keyAutoJoinFriend = KeyCode.None;
        private KeyCode keyJoinPublic = KeyCode.None;
        private KeyCode keyJoinMyTown = KeyCode.None;
        private KeyCode keyNoclip = KeyCode.None;
        private KeyCode keyCameraToggle = KeyCode.None;
        private KeyCode keyAutoIceSkating = KeyCode.None;
        private KeyCode keyAutoEat = KeyCode.None;
        private KeyCode keyUseBait = KeyCode.None;
        private KeyCode keyAntiAfk = KeyCode.None;
        private KeyCode keyBypassOverlap = KeyCode.None;
        private KeyCode keyBirdVacuum = KeyCode.None;
        private KeyCode keyGameSpeed1x = KeyCode.None;
        private KeyCode keyGameSpeed2x = KeyCode.None;
        private KeyCode keyGameSpeed5x = KeyCode.None;
        private KeyCode keyGameSpeed10x = KeyCode.None;
        private KeyCode keyEquipAxe = KeyCode.None;
        private KeyCode keyEquipNet = KeyCode.None;
        private KeyCode keyEquipRod = KeyCode.None;
        private KeyCode keyEquipSprinkler = KeyCode.None;
        private KeyCode keyEquipBirdScanner = KeyCode.None;
        private KeyCode keyEquipPad = KeyCode.None;
        private KeyCode keyPadConfirm = KeyCode.None;
        private KeyCode keyPadCancel = KeyCode.None;
        private KeyCode keyPadRotate = KeyCode.None;
        private KeyCode keyPadMove = KeyCode.None;
        private KeyCode keyPadDelete = KeyCode.None;
        private KeyCode keyAutoInsectFarm = KeyCode.None;
        private KeyCode keyAutoBirdFarm = KeyCode.None;
        private KeyCode keyMassCook = KeyCode.None;
        private KeyCode keyAutoPuzzle = KeyCode.None;
        private KeyCode keyAutoCatPlay = KeyCode.None;
        private KeyCode keyAutoDogTrain = KeyCode.None;
        private KeyCode keyAutoPetWash = KeyCode.None;
        private KeyCode keyFeedAllCats = KeyCode.None;
        private KeyCode keyFeedAllDogs = KeyCode.None;
        private KeyCode keySpawnBubble = KeyCode.None;
        
        // Key Rebinding State
        private string keyBindingActive = "";
        private float keyBindAssignedAt = -999f;
        
        // Fast, throttled trigger polls for Food & Repair automation.
        private float lastAutoEatTriggerCheckAt = 0f;
        private float lastAutoRepairTriggerCheckAt = 0f;
        private const float AutoEatTriggerCheckInterval = 0.25f;
        private const float AutoRepairTriggerCheckInterval = 0.5f;
        private const float FarmActiveAutoEatTriggerCheckInterval = 0.5f;
        private const float FarmActiveAutoRepairTriggerCheckInterval = 1f;

        // === MASTER BUILD SWITCHES ===
#if HIDE_LOADER_CONSOLE
        private const bool MasterHideLoaderConsole = true;
#else
        private const bool MasterHideLoaderConsole = false;
#endif
        internal const bool MasterLogAuraFarm = false;
        internal const bool MasterLogBirdFarm = false;
        internal const bool MasterLogBirdFarmCrashTrace = false;
        internal const bool MasterLogInsectFarm = false;
        internal const bool MasterLogAutoFish = false;
        internal const bool MasterLogAutoFarm = false;
        private const bool MasterLogAutoEatRepair = false;
        private const bool MasterLogNpcTeleport = false;
        private const bool MasterLogNetCook = false;
        private const bool MasterLogNetCookScan = false;
        private const bool MasterLogPuzzle = false;
        private const bool MasterLogAutoSell = false;
        private const bool MasterLogRadarIconEsp = false;
        private const bool MasterLogBubbleRadar = false;
        private const bool MasterLogAutoBuy = false;
#if HIDE_LOADER_CONSOLE
        private const bool MasterLogForceOpenShop = false;
#else
        private const bool MasterLogForceOpenShop = true;
#endif
        private const bool MasterLogPetPlay = false;
        private const bool MasterLogPetFeed = false;
        private const bool MasterLogWildAnimalFeed = false;
        private const bool MasterLogHomelandFarm = true;
        private const bool MasterLogPadBuild = true;
        private const bool MasterLogWildAnimalGift = true;
        private const bool MasterLogAutoIceSkating = false;
        private const bool MasterLogDailyQuestSubmit = true;
        internal const bool MasterLogDailyClaims = true;
        private const bool MasterLogBirdPhotoSubmit = false;
        private const bool MasterLogStrangerChat = false;


        
        // --- WINDOWS API FOR ESC KEY ---
        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll", SetLastError = true)]
        static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
        [DllImport("user32.dll", SetLastError = true)]
        static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);
        [DllImport("user32.dll", SetLastError = true)]
        static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
        [DllImport("user32.dll", SetLastError = true)]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("kernel32.dll", SetLastError = true)]
        static extern IntPtr GetConsoleWindow();

        private const int SW_HIDE = 0;

        [StructLayout(LayoutKind.Sequential)]
        public struct INPUT
        {
            public int type;
            public InputUnion u;
        }

        [StructLayout(LayoutKind.Explicit)]
        public struct InputUnion
        {
            [FieldOffset(0)]
            public MOUSEINPUT mi;
            [FieldOffset(0)]
            public KEYBDINPUT ki;
            [FieldOffset(0)]
            public HARDWAREINPUT hi;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MOUSEINPUT
        {
            public int dx;
            public int dy;
            public uint mouseData;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HARDWAREINPUT
        {
            public uint uMsg;
            public ushort wParamL;
            public ushort wParamH;
        }

        const uint WM_KEYDOWN = 0x0100;
        const uint WM_KEYUP = 0x0101;
        const uint WM_LBUTTONDOWN = 0x0201;
        const uint WM_LBUTTONUP = 0x0202;
        const uint KEYEVENTF_KEYUP = 0x0002;
        const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        const uint MOUSEEVENTF_LEFTUP = 0x0004;
        const int MK_LBUTTON = 0x0001;
        const int VK_ESCAPE = 0x1B;
        const int VK_F = 0x46;

        // --- PATROL SYSTEM VARIABLES ---
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

        private sealed class RadarMarkerMetadata
        {
            public string CanonicalLabel = string.Empty;
            public string Icon = string.Empty;
            public string SpecificIconKey = string.Empty;
            public bool IsCooldown;
            public Texture2D ResourceVisualEspIconTexture;
            public float ResourceVisualEspNextIconResolveAt;
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
            public bool autoIceSkatingEnabled;
            public int autoIceSkatingMinUltimateScore = 900;
            public bool autoIceSkatingOnlyX2Ultimate = true;
            public bool autoIceSkatingLast30sUltimate = true;
            public bool autoIceSkatingPerfectMove = true;
            public bool autoIceSkatingPreferNewMove = true;
            public bool fastBubbleGenEnabled;
            public float bubbleBubblesPerMinute;
            public float snowClickInterval;
            public float sculptIconClickInterval;
            public float cookingAutoSpeed;
            public float cookingWaitAtSpot;
            public float netCookInterval;
            public float netCookScanRadiusMeters;
            public bool netCookMiniGameOnly;
            public bool netCookMoveIngredients;
            public bool netCookUseAllIngredients;
            public int netCookCookQuantity;
            public float homelandFarmWaterRadius;
            public float autoFishScanTimeout = -1f;
            public float autoFishTeleportDelay = -1f;
            public float autoFishFishShadowDetectRange = -1f;
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
            public string Language = "en";
        }

        private List<Vector3> patrolPoints = new List<Vector3>();
        private bool isPatrolActive = false;
        private float waitAtSpot = 0.3f;
        private object patrolCoroutine;

        // --- COOKING PATROL VARIABLES ---
        private List<CookingPatrolPoint> cookingPatrolPoints = new List<CookingPatrolPoint>();
        private float cookingWaitAtSpot = 0.3f;
        private bool isCookingPatrolActive = false;
        private object cookingPatrolCoroutine;
        private string cookingPatrolSaveName = "";
        private Vector2 cookingPatrolSaveScrollPos = Vector2.zero;
        private bool antiAfkEnabled = false;
        private bool mouseLookEnabled = false;
        private bool showMouseLookCrosshair = true;
        private bool mouseLookCaptureActive = false;
        private bool mouseLookOrbitInitialized = false;
        private float mouseLookOrbitYaw = 0f;
        private float mouseLookOrbitPitch = 12f;
        private float mouseLookOrbitDistance = 3.5f;
        private float mouseLookOrbitPivotHeight = 1.45f;
        private Vector3 mouseLookOrbitPivotLocalOffset = new Vector3(0f, 1.45f, 0f);
        private bool mouseLookHasDefaultCameraSnapshot = false;
        private bool mouseLookWasCaptureActive = false;
        private Vector3 mouseLookDefaultCameraPos = Vector3.zero;
        private Quaternion mouseLookDefaultCameraRot = Quaternion.identity;
        private float mouseLookDefaultCameraFov = 60f;
        private float nextCameraToggleInteractAt = 0f;
        private float antiAfkInterval = 25f;
        private float lastAntiAfkPulseAt = -999f;
        private float antiAfkMouseDownClearAt = 0f;
        private float antiAfkMouseHoldClearAt = 0f;

        // --- AUTO REPAIR VARIABLES ---
        private int autoRepairType = 0; // 0 = Repair Kit, 1 = Crafty Repair Kit
        private int autoRepairUseTarget = 2;
        private readonly string[] autoRepairOptions = { "Repair Kit", "Crafty Repair Kit" };
        private readonly string[] autoRepairKeys = { "toolrestorer_toolrestorer_1", "toolrestorer_toolrestorer_2" };
        private bool autoRepairDropdownOpen = false;
        private bool repairTeleportBackEnabled = false;
        private bool autoRepairOnToastEnabled = false; // Toggle for auto repair via live durability detection
        private bool autoEatOnToastEnabled = false; // Toggle for auto eat via toast notification
        private const bool AutoEatRepairLogsEnabled = MasterLogAutoEatRepair;
        private bool autoEatAutoTriggerEnabled = true;
        private int autoRepairTriggerPercent = 10;
        private int autoEatTriggerPercent = 20;
        private const string AUTO_EAT_FOOD_KEY = "food_bluejam";
        private const string BAG_BUTTON_PATH = "GameApp/startup_root(Clone)/XDUIRoot/Status/StatusPanel(Clone)/AniRoot@ani@queueanimation/right_layout@ani/top_right_layout@go@t/menu_bar@go/bag@w/bag@btn";
        private const string BAG_PANEL_PATH = "GameApp/startup_root(Clone)/XDUIRoot/Scene/BagPanel(Clone)";
        private const string USE_BUTTON_PATH = "GameApp/startup_root(Clone)/XDUIRoot/Scene/BagPanel(Clone)/tip@w@t/operate@go/operate1@btn";
        private const string CLOSE_BUTTON_PATH = "GameApp/startup_root(Clone)/XDUIRoot/Scene/BagPanel(Clone)/close@btn";
        private const string SELECTED_ITEM_PATH = "GameApp/startup_root(Clone)/XDUIRoot/Scene/BagPanel(Clone)/bag1@unbreakscroll/Content/NewPackWidget/Root/select@go"; // Selection indicator that appears when item is clicked
        private const string INTERACT_PROMPT_BUTTON_PATH = "GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)/tracking_bar@w/tracking_common@list/IconsBarWidget(Clone)/root_visible@go/cells@t/cells@list/CommonIconForInteract(Clone)/root_visible@go/icon@img@btn";
        private const string LOGIN_PANEL_PATH = "GameApp/startup_root(Clone)/XDUIRoot/Full/LoginPanel(Clone)";
        private const string LOGIN_ROOM_PANEL_PATH = "GameApp/startup_root(Clone)/XDUIRoot/Full/LoginRoomPanel(Clone)";
        private const string START_GAME_BUTTON_PATH = "GameApp/startup_root(Clone)/XDUIRoot/Full/LoginPanel(Clone)/AniRoot@queueanimation/startGame@btn";
        private const string ROOM_ENTRY_BUTTON_PATH = "GameApp/startup_root(Clone)/XDUIRoot/Full/LoginPanel(Clone)/AniRoot@queueanimation/room@btn";
        private const string FRIEND_TAB_BUTTON_PATH = "GameApp/startup_root(Clone)/XDUIRoot/Full/LoginRoomPanel(Clone)/AniRoot/popup/content/background/tab_bg/tabBar@w/tab@list/Viewport/Content/friend@w/cell@btn";
        private const string ANNOUNCEMENT_CLOSE_BUTTON_PATH = "GameApp/startup_root(Clone)/XDUIRoot/Popup/NewAnnouncementPanel(Clone)/AniRoot/popup/operators/close@btn";
        private const string ROOM_REFRESH_BUTTON_PATH = "GameApp/startup_root(Clone)/XDUIRoot/Full/LoginRoomPanel(Clone)/AniRoot/popup/content/background/refresh@btn";
        private const string STATUS_SKILL_BAR_PATH = "GameApp/startup_root(Clone)/XDUIRoot/Status/StatusPanel(Clone)/AniRoot@ani@queueanimation/right_layout@ani/middle_right_layout@go/skill_bar@w@go";
        private const string STATUS_SKILL_BAR_WIDGET_PATH = "GameApp/startup_root(Clone)/XDUIRoot/Status/StatusPanel(Clone)/AniRoot@ani@queueanimation/right_layout@ani/middle_right_layout@go/skill_bar@w@go/skill_bar@go";
        private const string STATUS_MAIN_JOY_PATH = "GameApp/startup_root(Clone)/XDUIRoot/Status/StatusPanel(Clone)/AniRoot@ani@queueanimation/right_layout@ani/middle_right_layout@go/skill_bar@w@go/skill_bar@go/main_joy@go@w";
        private const string STATUS_PANEL_PATH = "GameApp/startup_root(Clone)/XDUIRoot/Status/StatusPanel(Clone)";
        // Default to the newly added "Bad Food" option (index 0)
        private int autoEatFoodType = 0;
        private readonly string[] autoEatFoodOptions = { "Bad Food", "Blue Jam","Mix Jam", "Bake Mushroom", "Any Food", "Custom Food"};
        private readonly string[] autoEatFoodKeys = { "food_badfood", "food_bluejam", "food_mixjam", "food_bakemushroom", "food_", "food_custom" };
        private string autoEatCustomFoodName = "";
        private bool autoEatFoodDropdownOpen = false;
        private bool customFoodPickMode = false; 
        private string lastClickedBagFood = "";
        private string[] scannedBagFoods = null;
        private Dictionary<string, Texture2D> scannedBagFoodTextures = new Dictionary<string, Texture2D>(); // Cached food textures (copied to survive bag scrolling)
        private readonly Dictionary<string, string> scannedBagFoodDisplayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private Vector2 customFoodScrollPos = Vector2.zero;
        private float customFoodScanRetryTime = 0f;
        private const string ITEM_ICON_CACHE_FOLDER = "ItemIcons";
        private sealed class AutoSellBagItemEntry
        {
            public string SpriteName = string.Empty;
            public string DisplayName = string.Empty;
            public string MatchKey = string.Empty;
            public uint NetId;
            public int Count;
            public int StackCount;
            public int StaticId;
            public int EntityType;
            public int StarRate;
            public int[] StarCounts = new int[6];
            public bool FromBackpack;
            public bool FromWarehouse;
        }

        private sealed class TransferItemEntry
        {
            public string SpriteName = string.Empty;
            public string DisplayName = string.Empty;
            public string MatchKey = string.Empty;
            public uint NetId;
            public int Count;
            public int StaticId;
            public int EntityType;
            public int StarRate;
            public bool IsLocked;
            public bool FromBackpack;
        }

        private List<AutoSellBagItemEntry> autoSellBagItems = null;
        private Dictionary<string, Texture2D> autoSellBagItemTextures = new Dictionary<string, Texture2D>();
        private readonly Dictionary<string, int> autoSellUiStarByMatchKey = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> autoSellUiStarByMatchKeyAndCount = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> autoSellLoadedSpriteResolveFailures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private MethodInfo cachedAutoSellTryGetQualityComponentMethod = null;
        private Type cachedAutoSellNetIdType = null;
        private MethodInfo cachedAutoSellNetIdFromUIntMethod = null;
        private bool autoSellQualityLookupResolved = false;
        private Vector2 autoSellBagItemScrollPos = Vector2.zero;
        private float autoSellBagScanRetryTime = 0f;
        private float autoSellBagScanDeadline = 0f;
        private float autoSellPendingRescanAt = 0f;
        private int autoSellPendingRescanRetries = 0;
        private bool isRepairing = false;
        private int repairStep = 0;
        private float stepTimer = 0f;
        private bool isAutoRepairRunning = false;
        private int autoRepairUseCount = 0;
        private int repairUsesTarget = 1; 
        private bool autoRepairWaiting = false;
        private float autoRepairWaitTimer = 0f;
        private float autoRepairWaitDuration = 18f;
        private bool lastStartWasAutoRepair = false;
        private bool isAutoEating = false;
        private int autoEatStep = 0;
        private float autoEatStepTimer = 0f;
        private int autoEatAttempts = 0;
        private bool autoEatForceSingleUse = false;
        private float nextAutoEatDirectRetryAt = 0f;
        private float nextAutoRepairToastAllowedAt = 0f;
        private float nextMissingRepairItemNotificationAt = 0f;
        private float nextMissingFoodNotificationAt = 0f;
        private bool pendingAutoRepairRequest = false;
        private bool pendingAutoEatRequest = false;
        private string lastDirectBackpackLookupKey = string.Empty;
        private bool lastDirectBackpackLookupAnyFood = false;
        private float nextDirectBackpackLookupRetryAt = -999f;
        private float nextDirectBackpackSnapshotRetryAt = -999f;
        private uint lastDirectBackpackMatchedNetId = 0U;
        private int lastDirectBackpackMatchedStaticId = 0;
        private int lastDirectBackpackMatchedEntityType = 0;
        private int lastDirectBackpackMatchedCount = 0;
        private uint lastRepairUseNetId = 0U;
        private int lastRepairUseCountBefore = 0;
        private string cachedRepairKitKey = "";
        private uint cachedRepairKitNetId = 0U;
        private int cachedRepairKitStaticId = 0;
        private int cachedRepairKitCount = 0;
        private string cachedFoodKey = "";
        private bool cachedFoodAnyFood = false;
        private uint cachedFoodNetId = 0U;
        private int cachedFoodStaticId = 0;
        private int cachedFoodEntityType = 0;
        private int cachedFoodCount = 0;
        private int repairVerifyChecks = 0;
        private int repairUseRetryAttempts = 0;
        private const int DIRECT_REPAIR_STEP_USE = 100;
        private const int DIRECT_REPAIR_STEP_WAIT = 101;
        private const int DIRECT_REPAIR_STEP_VERIFY = 102;
        private const int DIRECT_EAT_STEP_USE = 100;
        private const int DIRECT_EAT_STEP_DELAY = 101;
        private const int BaitStaticId = 20511;
        private const int BackpackFuncChumBait = 103;
        private float nextUseBaitAllowedAt = -999f;
        private const float UseBaitCooldownSeconds = 1f;
        // Resource-farm: pause when auto-repair triggered (seconds)
        private float resourceAutoRepairPauseSeconds = 20f;
        private float resourceRepairPauseUntil = 0f;
        // Timestamp of the last repair trigger to debounce repeated triggers
        private float lastRepairTriggerTime = -999f;
        // Distance to teleport player backward (meters) before starting repair
        private float repairTeleportBackDistance = 2.5f;
        private int maxAutoEatAttempts = 10;
        private bool toolDurabilityReflectionResolved = false;
        private bool toolDurabilityDiscoveryLogged = false;
        private Type cachedDataModuleOpenGenericType = null;
        private Type cachedToolSystemType = null;
        private Type cachedToolDataModuleType = null;
        private PropertyInfo cachedToolDataModuleInstanceProperty = null;
        private PropertyInfo cachedToolSystemInstanceProperty = null;
        private MethodInfo cachedToolSystemGetCurrentToolMethod = null;
        private FieldInfo cachedToolIdField = null;
        private FieldInfo cachedToolDurabilityField = null;
        private FieldInfo cachedToolMaxDurabilityField = null;
        private Type cachedToolClientServiceType = null;
        private MethodInfo cachedToolClientServiceTryGetMethod = null;
        private MethodInfo cachedTryGetTakenToolMethod = null;
        private MethodInfo cachedTryGetToolComponentMethod = null;
        private MethodInfo cachedGetToolDurabilityMethod = null;
        private MethodInfo cachedGetToolDurabilityUpperLimitMethod = null;
        private FieldInfo cachedTakenToolItem1Field = null;
        private FieldInfo cachedToolComponentIdField = null;
        private FieldInfo cachedToolComponentDurabilityField = null;
        private FieldInfo cachedToolComponentMaxDurabilityField = null;
        private int lastObservedToolId = -1;
        private int lastObservedToolDurability = int.MinValue;
        private int lastObservedToolMaxDurability = int.MinValue;
        private string cachedToolDurabilityStatusDisplay = "Unavailable";
        private string cachedFoodRepairEnergyStatusDisplay = "100/100";
        private int cachedEnergyCurrent = 100;
        private int cachedEnergyMax = 100;
        private float nextEnergyValueRefreshAt = 0f;
        private float nextFoodRepairUiStatusRefreshAt = 0f;
        private bool liveDurabilityLowLatched = false;
        private int liveDurabilityLatchedToolId = -1;
        private int liveDurabilityLatchedToolMaxDurability = int.MinValue;
        private float nextToolDurabilityLogAt = 0f;
        private string lastLoggedAutoRepairNetStatus = string.Empty;
        private float nextLiveDurabilityTriggerAt = 0f;
        private float lastToolDurabilityPollAt = -999f;
        private float nextAutoRepairPlayerContextProbeAt = -999f;
        private bool cachedAutoRepairPlayerContextReady = false;
        private string cachedAutoRepairPlayerContextStatus = "player context unavailable";
        private float nextAutoRepairWorldReadyProbeAt = -999f;
        private bool cachedAutoRepairWorldReady = false;
        private string cachedAutoRepairWorldReadyStatus = "world UI unavailable";
        private float nextToolClientServiceResolveAttemptAt = -999f;
        private float nextToolReflectionResolveAttemptAt = -999f;
        private float nextAuraMonoToolSystemResolveAttemptAt = -999f;
        private AuraMonoObjectCache cachedAuraMonoToolSystemObj;
        private IntPtr cachedAuraMonoToolSystemGetCurrentToolMethod = IntPtr.Zero;
        private float nextAutoRepairExpensiveDurabilityFallbackAt = -999f;
        private const float AutoRepairExpensiveFallbackRetrySeconds = 2f;
        private const float AutoRepairExpensiveFallbackMissBackoffSeconds = 8f;
        // Cached AuraMono BagModule pointer + ExecuteBackpackItemFunc method to avoid
        // re-scanning Managers._moduleDic on every repair/eat trigger (FPS fix).
        private AuraMonoObjectCache cachedAuraMonoBagModuleObj;
        private IntPtr cachedAuraMonoBagExecuteMethod = IntPtr.Zero;
        private float nextAutoEatRepairSlowRuntimeLogAt = 0f;
        private Component cachedHudDurabilityComponent = null;
        private float nextHudDurabilitySceneScanAt = 0f;
        private object cachedDirectBackpackSystemObj = null;
        private Type cachedDirectBackpackStorageType = null;
        private MethodInfo cachedDirectBackpackGetAllItemMethod = null;
        private bool cachedDirectBackpackGetAllItemNeedsStorage = true;
        private readonly Dictionary<string, Type> loadedTypeLookupCache = new Dictionary<string, Type>(StringComparer.Ordinal);
        private readonly List<DirectBackpackRuntimeItem> directBackpackRuntimeItems = new List<DirectBackpackRuntimeItem>(256);
        private float directBackpackRuntimeSnapshotAt = -999f;
        private string directBackpackRuntimeSnapshotSource = "";
        private object cachedDirectBagModuleObj = null;
        private Type cachedDirectBackpackFunctionType = null;
        private Type cachedDirectBagStorageType = null;
        private MethodInfo cachedDirectExecuteBackpackItemFuncMethod = null;
        private readonly Dictionary<string, float> loadedTypeMissCacheUntil = new Dictionary<string, float>(StringComparer.Ordinal);
        private readonly Dictionary<string, MethodInfo> methodLookupCache = new Dictionary<string, MethodInfo>(StringComparer.Ordinal);
        private readonly Dictionary<string, float> methodMissCacheUntil = new Dictionary<string, float>(StringComparer.Ordinal);
        private const float LoadedTypeMissCacheSeconds = 30f;
        private const float LoadedMethodMissCacheSeconds = 30f;
        private const float ToolDurabilityPollInterval = 0.5f;
        private const float FarmActiveToolDurabilityPollInterval = 1f;
        private const float ToolDurabilityLogInterval = 8f;
        private const float ToolDurabilityUnavailableLogInterval = 30f;
        private const double AutoEatRepairSlowRuntimeWarnMs = 80.0;
        private const float AutoEatRepairSlowRuntimeLogCooldown = 10f;
        private const float DirectBackpackRuntimeSnapshotTtl = 0.8f;
        private const float BusyDirectBackpackRuntimeSnapshotTtl = 2.5f;
        private const float DirectBackpackLookupMissBackoff = 2.0f;
        private const float DirectBackpackSnapshotFailureBackoff = 1.5f;
        private const bool DirectBackpackUnsafeAuraMonoFallbackEnabled = true;
        private const bool DirectBackpackVerboseLogsEnabled = false;
        private const float EnergyReadCacheInterval = 0.15f;

        private sealed class DirectBackpackRuntimeItem
        {
            public uint NetId;
            public int StaticId;
            public int EntityType;
            public int Count;
            public string Descriptor = "";
            public object ManagedItem;
            // MonoItem is only valid while MonoItemPin (pinned gchandle) roots it: the snapshot
            // outlives the building tick, and SGen moves/collects unrooted objects — reading a
            // stale MonoItem hit mono's "GC filler class" fatal assert (the recurring crash).
            public IntPtr MonoItem;
            public uint MonoItemPin;
        }

        // The only sanctioned way to drop the snapshot list — releases the per-item GC pins.

        // --- TARGET PATHS FOR PATROL ACTIONS ---
        private readonly string[] workPaths = new string[]
        {
            "GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)/tracking_bar@w/tracking_cook_danger@list/CommonIconForCookDanger(Clone)/root_visible@go/icon@img@btn",
            "GameApp/startup_root(Clone)/XDUIRoot/Full/CookPanel(Clone)/AniRoot@queueanimation/detail@t/btnBar@go/confirm@swapbtn",
            "GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)/tracking_bar@w/tracking_common@list/IconsBarWidget(Clone)/root_visible@go/cells@t/cells@list/CommonIconForInteract(Clone)/root_visible@go/icon@img@btn",
            "GameApp/startup_root(Clone)/XDUIRoot/Scene/DialoguePanel(Clone)/AniRoot@go@ani/exit@btn@go"
        };

        // Settings/Keybinds Persistence














        











        private void ApplyMasterConsoleVisibility()
        {
            if (!MasterHideLoaderConsole)
            {
                return;
            }

            ModCoroutines.Start(this.HideLoaderConsoleRoutine());
        }

        private IEnumerator HideLoaderConsoleRoutine()
        {
            // BepInEx may attach/show the console slightly after plugin load.
            float[] delays = new[] { 0f, 0.5f, 1f, 2f, 4f };
            for (int i = 0; i < delays.Length; i++)
            {
                if (delays[i] > 0f)
                {
                    yield return new WaitForSecondsRealtime(delays[i]);
                }

                this.TryHideLoaderConsoleWindow();
            }
        }

        private void TryHideLoaderConsoleWindow()
        {
            try
            {
                IntPtr consoleWindow = GetConsoleWindow();
                if (consoleWindow != IntPtr.Zero)
                {
                    ShowWindow(consoleWindow, SW_HIDE);
                }
            }
            catch
            {
            }
        }

        public void OnInitializeMelon()
        {
            this.ApplyMasterConsoleVisibility();
            HeartopiaComplete.Instance = this;
            HeartopiaComplete.harmonyInstance = new HarmonyLib.Harmony("com.heartopia.teleport");
            ModLogger.Msg("Heartopia Helper initialized!");
            this.InitializeLocalization();
            this.LoadRadarSpeciesIconIndex();
            this.LoadCustomTeleports();
            this.LoadKeybinds();
            this.LoadUiTheme();
            this.LoadPatrolPoints();
            this.LoadRadarSettings();
            this.LoadBirdFarmSettings();
            // NOTE: The hot Unity methods (CharacterController.Move, Transform.position/rotation
            // setters, Input.GetKey*) are intentionally NOT patched here. Patching them globally
            // taxes every frame of normal gameplay even when no mod feature is active, and is a
            // known source of periodic native crashes. They are now installed lazily on first use
            // via EnsurePositionOverridePatched / EnsureRotationOverridePatched / EnsureInputSimPatched.
            ModLogger.Msg("=== Hot-path patches deferred (installed on demand) ===");

            ModLogger.Msg("AutoFish subsystem disabled.");

            try
            {
                ModCoroutines.Start(this.NetCookCoroutineWarmupRoutine());
            }
            catch
            {
            }
            try
            {
                this.InitializeBubbleFeature();
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[ERR] BubbleFeature init failed: " + ex.Message);
            }
        }

        // Token: 0x06000004 RID: 4 RVA: 0x00002390 File Offset: 0x00000590
        public void OnLateUpdate()
        {
            this.ProcessNoclipVehicleOnLateUpdate();
            this.UpdateDirectMouseLookCamera(this.mouseLookCaptureActive);

            bool flag = this.monitorPosition;
            if (flag)
            {
                GameObject gameObject = GameObject.Find("p_player_skeleton(Clone)");
                bool flag2 = gameObject != null;
                if (flag2)
                {
                    Vector3 position = gameObject.transform.position;
                    bool flag3 = Vector3.Distance(position, this.lastKnownPosition) > 0.01f;
                    if (flag3)
                    {
                        ModLogger.Msg($"[POSITION CHANGED] From {this.lastKnownPosition} to {position}");
                        this.lastKnownPosition = position;
                    }
                }
            }
            bool flag4 = HeartopiaComplete.OverrideCameraPosition && this.cameraOverrideFramesRemaining > 0;
            if (flag4)
            {
                GameObject gameObject2 = GameObject.Find("GameApp/startup_root(Clone)/Main Camera");
                bool flag5 = gameObject2 != null;
                if (flag5)
                {
                    gameObject2.transform.position = HeartopiaComplete.CameraOverridePos;
                    gameObject2.transform.rotation = HeartopiaComplete.CameraOverrideRot;
                }
                this.cameraOverrideFramesRemaining--;
                bool flag6 = this.cameraOverrideFramesRemaining <= 0;
                if (flag6)
                {
                    HeartopiaComplete.OverrideCameraPosition = false;
                }
            }

            // Only force FOV while the custom override is enabled.
            if (this.customCameraFOVEnabled)
            {
                this.ApplyCameraFOV();
            }
        }

        // Token: 0x06000005 RID: 5 RVA: 0x000024C0 File Offset: 0x000006C0
        public void OnUpdate()
        {
            // World-epoch poll: invalidates AuraMono object caches after a scene/world change.
            this.UpdateAuraMonoWorldEpoch();
            // Lazily install the hot-path patches only while a feature that needs them is active.
            // This is the safety net for sustained, multi-frame effects; one-shot writers that
            // touch a transform in the same call (teleport, camera) also Ensure directly at the site.
            // Player teleport / noclip: pin position via the Transform.position setter. (The
            // CharacterController.Move patch was removed — the local player isn't driven by it.)
            float hotPatchNow = Time.unscaledTime;
            if (this.noclipEnabled || HeartopiaComplete.OverridePlayerPosition || this.teleportFramesRemaining > 0)
            {
                this.EnsurePositionOverridePatched();
                this.positionOverridePatchLastNeededAt = hotPatchNow;
            }
            // Camera mouse-look: pin camera position + rotation. Does NOT need CharacterController.Move.
            if (this.mouseLookEnabled || HeartopiaComplete.OverrideCameraPosition || this.cameraOverrideFramesRemaining > 0)
            {
                this.EnsurePositionOverridePatched();
                this.EnsureRotationOverridePatched();
                this.positionOverridePatchLastNeededAt = hotPatchNow;
                this.rotationOverridePatchLastNeededAt = hotPatchNow;
            }
            // Player rotation override.
            if (HeartopiaComplete.OverridePlayerRotation || this.playerRotationFramesRemaining > 0)
            {
                this.EnsureRotationOverridePatched();
                this.rotationOverridePatchLastNeededAt = hotPatchNow;
            }
            this.UpdateHotPathOverrideTargetIds();
            // Menu input-block: stop player movement while the menu is open. Routed through the
            // game's MonoInputManager (the player isn't driven by Unity's CharacterController.Move),
            // so no hot-path Harmony patch is installed for this.
            this.UpdateMenuMovementInputBlock();
            // Simulated F-key (Input.GetKey* patches). NOTE: fishing (AutoFishingFarm) and insect
            // (InsectNetFarm) are net-based and do NOT use F-sim, so they are not gated here. Real
            // users: resource farm + auto-forage (set SimulateFKey* directly) and the SimulateFKeyPulse
            // callers (camera-toggle interact, meteor auto-interact, SendFMessage), which also call
            // EnsureInputSimPatched() at their own site.
            if (this.autoResourceFarmEnabled || this.autoFarmActive
                || HeartopiaComplete.SimulateFKeyHeld || HeartopiaComplete.SimulateFKeyDown || HeartopiaComplete.SimulateFKeyUp)
            {
                this.EnsureInputSimPatched();
                this.inputSimPatchLastNeededAt = hotPatchNow;
            }
            this.MaybeUnpatchIdleHotPathPatches(hotPatchNow);

            if (BirdNetFarm.IsEnabled)
            {
                this.EnsureBirdPhotoRuntimeProbePatch();
            }
            if (this.netCookEnabled || this.netCookTargets.Count > 0 || (this.showMenu && this.selectedTab == 3 && this.automationSubTab == 5))
            {
                this.EnsureNetCookWorldCookerRegistrationPatch();
                this.UpdateNetCookRuntimeReadiness();
            }
            if (this.petPlayAutoCatEnabled || this.petPlayAutoDogEnabled || this.petPlayAutoWashEnabled)
            {
                this.EnsurePetPlayRuntimePatches();
            }
            this.EnsureStrangerChatBypassPatch();
            WarehouseBypassFeature.Update(this);
            this.UpdateTransferQtyHoldRepeat();
            this.ProcessPendingTransferListRescan();
            this.ProcessPendingAutoSellListRescan();
            this.UpdatePetPlayAutomation();
            this.UpdateGameUiClickBlockState();
            this.UpdateMouseLookState();
            this.UpdateCameraToggleInteractClick();
            float instantFps = (Time.unscaledDeltaTime > 0.0001f) ? (1f / Time.unscaledDeltaTime) : this.fpsBypassObservedFps;
            if (this.fpsBypassEnabled)
            {
                if (this.fpsBypassObservedFps <= 0f)
                {
                    this.fpsBypassObservedFps = instantFps;
                }
                else
                {
                    this.fpsBypassObservedFps = Mathf.Lerp(this.fpsBypassObservedFps, instantFps, 0.2f);
                }

                if (Time.unscaledTime >= this.nextFpsBypassTuneAt)
                {
                    float error = (float)this.fpsBypassTarget - this.fpsBypassObservedFps;
                    if (error > 0.5f && error < 15f)
                    {
                        // Small drift: nudge cap upward to close the gap
                        this.fpsBypassCompOffset = Mathf.Clamp(this.fpsBypassCompOffset + error * 0.35f, -15f, 15f);
                    }
                    else if (error >= 15f || error < -0.5f)
                    {
                        // Hardware-limited or overshooting: decay offset to 0
                        this.fpsBypassCompOffset = Mathf.MoveTowards(this.fpsBypassCompOffset, 0f, 2f);
                    }
                    this.nextFpsBypassTuneAt = Time.unscaledTime + 0.4f;
                }
            }
            else
            {
                this.fpsBypassObservedFps = 0f;
                this.fpsBypassCompOffset = 0f;
            }

            if (Time.unscaledTime >= this.nextFpsBypassApplyAt)
            {
                if (this.fpsBypassEnabled || this.fpsBypassWasApplied)
                {
                    this.ApplyFpsBypass(this.fpsBypassEnabled);
                }
                this.nextFpsBypassApplyAt = Time.unscaledTime + 0.5f;
            }
            this.ProcessLodOverrideOnUpdate();
            this.ProcessHideJumpButtonOnUpdate();
            this.ProcessBunnyHopOnUpdate();
            this.ProcessAutoIceSkatingOnUpdate();
            this.ProcessBubbleFeatureOnUpdate();
            this.FlushPendingGameSpeedConfigSave();
            this.FlushPendingRadarSettingsSave();
            bool flag2 = HeartopiaComplete.OverridePlayerPosition && this.teleportFramesRemaining > 0;
            if (flag2)
            {
                this.teleportFramesRemaining--;
                bool flag3 = this.teleportFramesRemaining <= 0;
                if (flag3)
                {
                    HeartopiaComplete.OverridePlayerPosition = false;
                }
            }
            this.SyncTeleportPosition();

            // Periodic toast-panel scan fallback (in case UIManager hook isn't available)
            if (this.autoRepairOnToastEnabled)
            {
                try { this.CheckToastPanel(); } catch { }
            }
            try { this.UpdateBottomDialogAutoClicker(); } catch { }

            // Handle player rotation override
            bool flagRotation = HeartopiaComplete.OverridePlayerRotation && this.playerRotationFramesRemaining > 0;
            if (flagRotation)
            {
                this.playerRotationFramesRemaining--;
                GameObject player = GetPlayer();
                if (player != null)
                {
                    player.transform.rotation = HeartopiaComplete.PlayerOverrideRot;
                }
                if (this.playerRotationFramesRemaining <= 0)
                {
                    HeartopiaComplete.OverridePlayerRotation = false;
                }
            }
            
            this.ProcessNoclipMovementOnUpdate();

            if (!string.IsNullOrEmpty(this.keyBindingActive))
            {
                this.TryCaptureSideMouseKeybindOnUpdate();
            }
            
            // Check for keybinds (Only if not currently rebinding and not just assigned)
            if (string.IsNullOrEmpty(this.keyBindingActive) && Time.unscaledTime - this.keyBindAssignedAt >= 0.2f)
            {
                if (this.TryGetModHotkeyDown(this.keyToggleMenu))
                {
                    this.showMenu = !this.showMenu;
                    this.blockInputReleaseUntil = Time.unscaledTime + 0.18f;
                }
                if (this.TryGetModHotkeyDown(this.keyToggleRadar))
                {
                    this.ToggleRadar();
                    this.AddMenuNotification($"Radar {(this.isRadarActive ? "Enabled" : "Disabled")}", this.isRadarActive ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.55f, 0.55f));
                }
                if (this.TryGetModHotkeyDown(this.keyAutoForaging))
                {
                    this.SetAutoCollectEnabled(!this.autoFarmEnabled);
                }
                if (this.TryGetModHotkeyDown(this.keyAuraFarm))
                {
                    this.SetAuraFarmEnabled(!this.auraFarmEnabled);
                }
                if (this.TryGetModHotkeyDown(this.keyWaterWeedRadius))
                {
                    this.StartHomelandFarmWaterAndWeed(silent: false);
                }
                if (this.TryGetModHotkeyDown(this.keyAutoInsectFarm))
                {
                    InsectNetFarm.ToggleEnabled(this);
                    bool insectFarmEnabled = InsectNetFarm.IsEnabled;
                    this.AddMenuNotification($"Auto Insect Farm {(insectFarmEnabled ? "Enabled" : "Disabled")}", insectFarmEnabled ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.55f, 0.55f));
                }
                if (this.TryGetModHotkeyDown(this.keyAutoBirdFarm))
                {
                    BirdNetFarm.ToggleEnabled(this);
                    bool birdFarmEnabled = BirdNetFarm.IsEnabled;
                    this.AddMenuNotification($"Auto Bird Farm {(birdFarmEnabled ? "Enabled" : "Disabled")}", birdFarmEnabled ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.55f, 0.55f));
                }
                if (this.TryGetModHotkeyDown(this.keyAutoFishShadowNet))
                {
                    AutoFishingFarm.ToggleEnabled(this);
                    bool fishShadowNetEnabled = AutoFishingFarm.IsEnabled;
                    this.AddMenuNotification(
                        "Fish Shadow Net " + (fishShadowNetEnabled ? "Enabled" : "Disabled"),
                        fishShadowNetEnabled ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.55f, 0.55f));
                }
                if (this.TryGetModHotkeyDown(this.keyMassCook))
                {
                    if (this.netCookEnabled)
                    {
                        this.StopNetCookInternal("Mass cook stopped");
                        this.AddMenuNotification("Mass Cook Disabled", new Color(1f, 0.55f, 0.55f));
                    }
                    else
                    {
                        this.StartNetCookInternal();
                        bool started = this.netCookEnabled;
                        string status = string.IsNullOrWhiteSpace(this.netCookStatus) ? "Mass cook start requested" : this.netCookStatus;
                        this.AddMenuNotification(status, started ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.55f, 0.55f));
                    }
                }
                if (this.TryGetModHotkeyDown(this.keyAutoPuzzle))
                {
                    bool nextPuzzle = !this.puzzleAutoEnabled;
                    this.SetPuzzleAutoEnabled(nextPuzzle, true);
                }
                if (this.TryGetModHotkeyDown(this.keyAutoCatPlay))
                {
                    this.petPlayAutoCatEnabled = !this.petPlayAutoCatEnabled;
                    this.PetPlayLog("Cat play " + (this.petPlayAutoCatEnabled ? "enabled" : "disabled"));
                    this.AddMenuNotification(
                        "Auto Cat Play " + (this.petPlayAutoCatEnabled ? "Enabled" : "Disabled"),
                        this.petPlayAutoCatEnabled ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.55f, 0.55f));
                }
                if (this.TryGetModHotkeyDown(this.keyAutoDogTrain))
                {
                    this.petPlayAutoDogEnabled = !this.petPlayAutoDogEnabled;
                    this.PetPlayLog("Dog train " + (this.petPlayAutoDogEnabled ? "enabled" : "disabled"));
                    this.AddMenuNotification(
                        "Auto Dog Train " + (this.petPlayAutoDogEnabled ? "Enabled" : "Disabled"),
                        this.petPlayAutoDogEnabled ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.55f, 0.55f));
                }
                if (this.TryGetModHotkeyDown(this.keyAutoPetWash))
                {
                    this.petPlayAutoWashEnabled = !this.petPlayAutoWashEnabled;
                    this.PetPlayLog("Pet wash " + (this.petPlayAutoWashEnabled ? "enabled" : "disabled"));
                    this.AddMenuNotification(
                        "Auto Pet Wash " + (this.petPlayAutoWashEnabled ? "Enabled" : "Disabled"),
                        this.petPlayAutoWashEnabled ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.55f, 0.55f));
                }
                if (this.TryGetModHotkeyDown(this.keyFeedAllCats))
                {
                    this.StartPetFeedAll(false);
                }
                if (this.TryGetModHotkeyDown(this.keyFeedAllDogs))
                {
                    this.StartPetFeedAll(true);
                }
                if (this.TryGetModHotkeyDown(this.keySpawnBubble))
                {
                    bool spawned = this.TrySpawnBubbleOnKeybind();
                    this.AddMenuNotification(
                        spawned ? "Bubble spawned" : "Bubble spawn failed (enter world / wait for mono hooks)",
                        spawned ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.55f, 0.55f));
                }
                if (this.TryGetModHotkeyDown(this.keyBypassUI))
                {
                    this.bypassEnabled = !this.bypassEnabled;
                    ModLogger.Msg("Bypass UI/Skeleton " + (this.bypassEnabled ? "Enabled" : "Disabled"));
                    this.RunBypassLogic(this.bypassEnabled);
                    this.AddMenuNotification($"Bypass UI {(this.bypassEnabled ? "Enabled" : "Disabled")}", this.bypassEnabled ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.55f, 0.55f));
                }
                if (this.TryGetModHotkeyDown(this.keyBypassOverlap))
                {
                    this.bypassOverlapEnabled = !this.bypassOverlapEnabled;
                    HeartopiaComplete.bypassOverlapEnabledStatic = this.bypassOverlapEnabled;
                    if (this.bypassOverlapEnabled && !this.bypassOverlapPatched)
                        this.EnsureBypassPatched();
                    this.AddMenuNotification($"Bypass Overlap {(this.bypassOverlapEnabled ? "Enabled" : "Disabled")}", this.bypassOverlapEnabled ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.55f, 0.55f));
                }
                if (this.TryGetModHotkeyDown(this.keyBirdVacuum))
                {
                    this.birdVacuumEnabled = !this.birdVacuumEnabled;
                    this.AddMenuNotification($"Bird Vacuum {(this.birdVacuumEnabled ? "Enabled" : "Disabled")}", this.birdVacuumEnabled ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.55f, 0.55f));
                }
                if (this.TryGetModHotkeyDown(this.keyDisableAll))
                {
                    this.StopMeteorAutoInteractSequence();
                    this.autoFarmEnabled = false;
                    this.autoFarmActive = false;
                    this.farmState = HeartopiaComplete.AutoFarmState.Idle;
                    this.autoFarmAutoStopAt = -1f;
                    this.SetAuraFarmEnabled(false);
                    this.bypassEnabled = false;
                    this.antiAfkEnabled = false;
                    this.StopAutoCookInternal("Disabled");
                    this.isAutoEating = false;
                    this.StopTreeFarm("Stopped");
                    this.mouseLookEnabled = false;
                    this.noclipEnabled = false;
                    this.UpdateMouseLookState();
                    HeartopiaComplete.OverridePlayerPosition = false;
                    this.ClearNoclipVehicleOverride();
                    this.noclipBoostMultiplier = 2f;
                    this.SetGameSpeed(1f);
                    this.fpsBypassEnabled = false;
                    this.ApplyFpsBypass(false);
                    this.lodOverrideMode = 0;
                    this.RevertLodOverride();
                    this.StopAllAutoFishing();
                    this.autoResourceFarmEnabled = false;
                    this.autoSellEnabled = false;
                    this.autoResourceFarmAutoStopAt = -1f;
                    this.ResetResourceFarmState();
                    this.resourceJustArrived = false;
                    this.netCookEnabled = false;
                    this.netCookDrainAfterIngredientsRunOut = false;
                    this.netCookDrainReason = null;
                    this.puzzleAutoEnabled = false;
                    this.petPlayAutoCatEnabled = false;
                    this.petPlayAutoDogEnabled = false;
                    this.petPlayAutoWashEnabled = false;
                    this.StopWildAnimalFeedCoroutine();
                    SimulateFKeyHeld = false;
                    SimulateFKeyDown = false;
                    SimulateFKeyUp = false;
                    this.fKeySimFrame = 0;
                    try { InsectNetFarm.ForceStop(); } catch (Exception ex) { ModLogger.Msg("[DisableAll] Failed to stop Insect Farm: " + ex.Message); }
                    try { BirdNetFarm.ForceStop(this); } catch (Exception ex) { ModLogger.Msg("[DisableAll] Failed to stop Bird Farm: " + ex.Message); }
                    try { this.ForceStopPuzzleAuto(); } catch (Exception ex) { ModLogger.Msg("[DisableAll] Failed to stop Puzzle: " + ex.Message); }
                    ModLogger.Msg("All features disabled and game speed reset");
                    this.AddMenuNotification("All features disabled", new Color(1f, 0.55f, 0.55f));
                }
                if (this.TryGetModHotkeyDown(this.keyInspectPlayer))
                {
                    this.InspectPlayerComponents();
                }
                if (this.TryGetModHotkeyDown(this.keyInspectMove))
                {
                    this.InspectMovementComponent();
                }
                if (this.TryGetModHotkeyDown(this.keyAutoRepair))
                {
                    if (!this.IsAutoRepairActiveOrQueued() && !this.isAutoEating)
                    {
                        this.AutoEatRepairLog("[AutoRepair] Hotkey requested StartRepair");
                        this.StartRepair();
                        this.AddMenuNotification(this.L("Auto Repair started"), new Color(0.45f, 1f, 0.55f));
                    }
                    else
                    {
                        this.AddMenuNotification(this.L("Auto Repair already running"), new Color(1f, 0.55f, 0.55f));
                    }
                }
                if (this.TryGetModHotkeyDown(this.keyAutoEat))
                {
                    if (!this.isRepairing && !this.isAutoEating)
                    {
                        this.StartAutoEat();
                        this.AddMenuNotification(this.LF("Auto Eat started ({0})", this.GetAutoEatFoodOptionLabel(this.autoEatFoodType)), new Color(0.45f, 1f, 0.55f));
                    }
                    else
                    {
                        this.AddMenuNotification(this.L("Auto Eat already running"), new Color(1f, 0.55f, 0.55f));
                    }
                }
                if (this.TryGetModHotkeyDown(this.keyUseBait))
                {
                    this.TryUseBaitFromBagWithNotification();
                }
                if (this.TryGetModHotkeyDown(this.keyCameraToggle))
                {
                    this.mouseLookEnabled = !this.mouseLookEnabled;
                    this.SaveKeybinds(false);
                    this.UpdateMouseLookState();
                    this.AddMenuNotification(
                        $"Camera Toggle {(this.mouseLookEnabled ? "Enabled" : "Disabled")}",
                        this.mouseLookEnabled ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.55f, 0.55f));
                }
                if (this.TryGetModHotkeyDown(this.keyAutoIceSkating))
                {
                    this.autoIceSkatingEnabled = !this.autoIceSkatingEnabled;
                    if (this.autoIceSkatingEnabled)
                    {
                        this.autoIceSkatingReflectionRetryAt = -999f;
                        this.autoIceSkatingLastLoggedStatus = string.Empty;
                        this.AutoIceSkatingResetPerformingTrackers();
                        this.AutoIceSkatingInvalidateMaxUltimateCache();
                    }
                    else
                    {
                        this.AutoIceSkatingResetPerformingTrackers();
                        this.AutoIceSkatingInvalidateMaxUltimateCache();
                        this.AutoIceSkatingSetStatus("Disabled.", force: true);
                    }

                    this.SaveKeybinds(false);
                    this.AddMenuNotification(
                        $"Auto Ice Skating {(this.autoIceSkatingEnabled ? "Enabled" : "Disabled")}",
                        this.autoIceSkatingEnabled ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.55f, 0.55f));
                }
                if (this.TryGetModHotkeyDown(this.keyAntiAfk))
                {
                    this.antiAfkEnabled = !this.antiAfkEnabled;
                    this.lastAntiAfkPulseAt = Time.unscaledTime;
                    this.antiAfkMouseDownClearAt = 0f;
                    this.antiAfkMouseHoldClearAt = 0f;
                    this.SaveKeybinds(false);
                    this.AddMenuNotification($"Anti AFK {(this.antiAfkEnabled ? "Enabled" : "Disabled")}", this.antiAfkEnabled ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.55f, 0.55f));
                }
                // Auto Snow hotkey handling
                if (this.isListeningForAutoSnowHotkey)
                {
                    foreach (object k in Enum.GetValues(typeof(KeyCode)))
                    {
                        KeyCode kc = (KeyCode)k;
                        if (Input.GetKeyDown(kc) && kc != KeyCode.Escape)
                        {
                            this.autoSnowHotkey = kc;
                            this.isListeningForAutoSnowHotkey = false;
                            this.AddMenuNotification($"Auto Snow Hotkey set: {kc}", new Color(0.45f, 1f, 0.55f));
                            break;
                        }
                    }
                }
                else
                {
                    if (this.TryGetModHotkeyDown(this.autoSnowHotkey))
                    {
                        this.autoSnowEnabled = !this.autoSnowEnabled;
                        this.AddMenuNotification($"Auto Snow Sculpture {(this.autoSnowEnabled ? "Enabled" : "Disabled")}", this.autoSnowEnabled ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.55f, 0.55f));
                    }
                }
                if (this.TryGetModHotkeyDown(this.keyAutoJoinFriend))
                {
                    this.StartLobbyAutoJoinFriend("Hotkey triggered");
                }
                if (this.TryGetModHotkeyDown(this.keyJoinPublic))
                {
                    this.autoJoinFriendEnabled = false;
                    this.autoClickStartEnabled = false;
                    bool success = this.ClickButtonIfExistsReturn(START_GAME_BUTTON_PATH);
                    this.AddMenuNotification($"Join Public: {(success ? "Clicked" : "Button not found")}", success ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.55f, 0.55f));
                }
                if (this.TryGetModHotkeyDown(this.keyJoinMyTown))
                {
                    this.StartLobbyAutoJoinMyTown("Hotkey triggered");
                }
                if (this.TryGetModHotkeyDown(this.keyNoclip))
                {
                    this.noclipEnabled = !this.noclipEnabled;
                    if (this.noclipEnabled)
                    {
                        this.InitializeNoclipOverridePosition();
                        this.AddMenuNotification("Noclip: ENABLED", new Color(0.45f, 1f, 0.55f));
                    }
                    else
                    {
                        HeartopiaComplete.OverridePlayerPosition = false;
                        this.ClearNoclipVehicleOverride();
                        this.AddMenuNotification("Noclip: DISABLED", new Color(1f, 0.55f, 0.55f));
                    }
                }
                if (this.TryGetModHotkeyDown(this.keyGameSpeed1x))
                {
                    this.SetGameSpeed(1f);
                    this.AddMenuNotification("Game Speed: 1x", new Color(0.45f, 1f, 0.55f));
                }
                if (this.TryGetModHotkeyDown(this.keyGameSpeed2x))
                {
                    this.SetGameSpeed(2f);
                    this.AddMenuNotification("Game Speed: 2x", new Color(0.45f, 1f, 0.55f));
                }
                if (this.TryGetModHotkeyDown(this.keyGameSpeed5x))
                {
                    this.SetGameSpeed(5f);
                    this.AddMenuNotification("Game Speed: 5x", new Color(0.45f, 1f, 0.55f));
                }
                if (this.TryGetModHotkeyDown(this.keyGameSpeed10x))
                {
                    this.SetGameSpeed(10f);
                    this.AddMenuNotification("Game Speed: 10x", new Color(0.45f, 1f, 0.55f));
                }
                if (this.TryGetModHotkeyDown(this.keyEquipAxe))
                {
                    if (this.TryToggleEquipHandToolHotkey(1, out bool unequipped, out _))
                    {
                        this.AddMenuNotification(unequipped ? "Unequipping Axe" : "Equipping Axe", new Color(0.45f, 1f, 0.55f));
                    }
                }
                if (this.TryGetModHotkeyDown(this.keyEquipNet))
                {
                    if (this.TryToggleEquipHandToolHotkey(5, out bool unequipped, out _))
                    {
                        this.AddMenuNotification(unequipped ? "Unequipping Net" : "Equipping Net", new Color(0.45f, 1f, 0.55f));
                    }
                }
                if (this.TryGetModHotkeyDown(this.keyEquipRod))
                {
                    if (this.TryToggleEquipHandToolHotkey(3, out bool unequipped, out _))
                    {
                        this.AddMenuNotification(unequipped ? "Unequipping Rod" : "Equipping Rod", new Color(0.45f, 1f, 0.55f));
                    }
                }
                if (this.TryGetModHotkeyDown(this.keyEquipSprinkler))
                {
                    if (this.TryToggleEquipHandToolHotkey(2, out bool unequipped, out _))
                    {
                        this.AddMenuNotification(unequipped ? "Unequipping Sprinkler" : "Equipping Sprinkler", new Color(0.45f, 1f, 0.55f));
                    }
                }
                if (this.TryGetModHotkeyDown(this.keyEquipBirdScanner))
                {
                    if (this.TryToggleEquipHandToolHotkey(4, out bool unequipped, out _))
                    {
                        this.AddMenuNotification(unequipped ? "Unequipping Bird Scanner" : "Equipping Bird Scanner", new Color(0.45f, 1f, 0.55f));
                    }
                }
                if (this.TryGetModHotkeyDown(this.keyEquipPad))
                {
                    if (this.TryToggleEquipHandToolHotkey(6, out bool unequipped, out _))
                    {
                        this.AddMenuNotification(unequipped ? "Unequipping Pad" : "Equipping Pad", new Color(0.45f, 1f, 0.55f));
                    }
                }
                this.ProcessPadBuildHotkeysOnUpdate();
            }

            this.UpdateBuildingFreeSnapOverrides();
            this.UpdateBuildingMovePanelState();
            this.RunAntiAfkTick();
            if (this.antiAfkMouseDownClearAt > 0f && Time.unscaledTime >= this.antiAfkMouseDownClearAt)
            {
                this.antiAfkMouseDownClearAt = 0f;
            }
            if (this.antiAfkMouseHoldClearAt > 0f && Time.unscaledTime >= this.antiAfkMouseHoldClearAt)
            {
                this.antiAfkMouseHoldClearAt = 0f;
            }

            // Check live durability / energy panel triggers on separate lightweight schedules.
            float autoEatRepairNow = Time.unscaledTime;
            bool bagAutomationBusy = this.IsBagAutomationActiveOrQueued();
            float autoRepairPollInterval = this.GetEffectiveAutoRepairTriggerCheckInterval();
            if (this.autoRepairOnToastEnabled
                && !bagAutomationBusy
                && autoEatRepairNow - this.lastAutoRepairTriggerCheckAt >= autoRepairPollInterval)
            {
                this.lastAutoRepairTriggerCheckAt = autoEatRepairNow;
                long autoRepairPollStart = System.Diagnostics.Stopwatch.GetTimestamp();
                this.TryHandleLiveDurabilityAutoRepair();
                this.ReportAutoEatRepairSlowRuntime("repair trigger poll", autoRepairPollStart);
            }

            if (this.autoEatAutoTriggerEnabled
                && !bagAutomationBusy
                && autoEatRepairNow - this.lastAutoEatTriggerCheckAt >= this.GetEffectiveAutoEatTriggerCheckInterval())
            {
                this.lastAutoEatTriggerCheckAt = autoEatRepairNow;

                if (autoEatRepairNow >= this.nextAutoEatDirectRetryAt && IsEnergyAtOrBelowAutoEatTrigger())
                {
                    if (!this.IsAutoRepairActiveOrQueued() && !this.isAutoEating)
                    {
                        this.AutoEatRepairLog($"[AutoEat] Energy panel requested StartAutoEat ({this.GetCurrentEnergyDisplay()}, threshold={this.autoEatTriggerPercent}%)");
                        this.StartAutoEat();
                        this.AddMenuNotification(this.LF("Auto Eat triggered by energy panel ({0})", this.GetAutoEatFoodOptionLabel(this.autoEatFoodType)), new Color(0.45f, 1f, 0.55f));
                    }
                    else if (!this.pendingAutoEatRequest)
                    {
                        this.pendingAutoEatRequest = true;
                        this.AutoEatRepairLog($"[AutoEat] Energy panel trigger queued because bag automation is busy ({this.GetCurrentEnergyDisplay()}, threshold={this.autoEatTriggerPercent}%).");
                    }
                }
            }

            if (this.showMenu && this.selectedTab == 3 && this.automationSubTab == 1)
            {
                this.RefreshFoodRepairUiStatusSnapshot();
            }

            // Update ID display
            this.UpdateIdDisplay();

            if (this.isRepairing && Time.unscaledTime >= this.stepTimer)
            {
                long repairStepStart = System.Diagnostics.Stopwatch.GetTimestamp();
                this.ExecuteRepairStep();
                this.ReportAutoEatRepairSlowRuntime("repair step", repairStepStart);
            }
            if (this.isAutoEating && Time.unscaledTime >= this.autoEatStepTimer)
            {
                long eatStepStart = System.Diagnostics.Stopwatch.GetTimestamp();
                this.ExecuteAutoEatStep();
                this.ReportAutoEatRepairSlowRuntime("eat step", eatStepStart);
            }
            this.ProcessPendingBagAutomation();

            // Camera FOV will be applied in OnLateUpdate to avoid competing with game camera updates

            // Clear scheduled simulated F-key states
            if (this.nextSimulatedFKeyClearAt > 0f && Time.unscaledTime >= this.nextSimulatedFKeyClearAt)
            {
                HeartopiaComplete.SimulateFKeyDown = false;
                HeartopiaComplete.SimulateFKeyHeld = false;
                HeartopiaComplete.SimulateFKeyUp = true;
                this.nextSimulatedFKeyClearAt = 0f;
                this.nextSimulatedFKeyUpClearAt = Time.unscaledTime + 0.05f;
            }
            if (this.nextSimulatedFKeyUpClearAt > 0f && Time.unscaledTime >= this.nextSimulatedFKeyUpClearAt)
            {
                HeartopiaComplete.SimulateFKeyUp = false;
                this.nextSimulatedFKeyUpClearAt = 0f;
            }

            this.ApplyGameSpeed();
            bool flag5 = this.autoFarmEnabled && Time.unscaledTime > this.nextFarmTime;
            if (flag5)
            {
                this.nextFarmTime = Time.unscaledTime + this.farmPeriod;
                this.RunAutoCollectLogic();
            }
            bool flag6 = this.autoCookEnabled;
            if (flag6)
            {
                // Player alert: throttled to every 3s to avoid per-frame FindObjectsOfType crash
                if (this.enablePlayerDetection && !this.cookingCleanupMode
                    && Time.unscaledTime - this.lastPlayerDetectionCheckAt >= 3f)
                {
                    this.lastPlayerDetectionCheckAt = Time.unscaledTime;
                    float nearestPlayer = this.GetNearestPlayerDistance();
                    if (nearestPlayer < cookingPlayerAlertRadius)
                    {
                        this.cookingCleanupMode = true;
                        ModLogger.Msg($"[Cooking] PLAYER DETECTED ({nearestPlayer:F0}m) - Starting cleanup!");
                    }
                }

                // Run the original cook logic on a timer (teleport patrol runs independently as a coroutine)
                if (Time.unscaledTime >= this.nextCookTime)
                {
                    this.RunAutoCookLogic();
                    this.nextCookTime = Time.unscaledTime + 0.2f;
                }
                // Auto-stop timer for Auto Cook
                if (this.autoCookEnabled && this.autoCookAutoStopEnabled && this.autoCookAutoStopAt > 0f && Time.unscaledTime >= this.autoCookAutoStopAt)
                {
                    this.StopAutoCookInternal("auto-stopped (timer)");
                    this.AddMenuNotification("Auto Cook auto-stopped (timer)", new Color(1f, 0.75f, 0.45f));
                }
            }
            if (this.netCookEnabled)
            {
                this.ProcessNetCookLoop();
            }
            this.ProcessSnowSculptureOnUpdate();
            if (this.autoBuyEnabled && Time.unscaledTime >= this.nextAutoBuyLogicTime)
            {
                this.nextAutoBuyLogicTime = Time.unscaledTime + 0.05f;
                this.RunAutoBuyLogic();
            }
            if (this.autoBuyBirdEnabled && Time.unscaledTime >= this.nextAutoBuyBirdLogicTime)
            {
                this.nextAutoBuyBirdLogicTime = Time.unscaledTime + 0.05f;
                this.RunAutoBuyBirdLogic();
            }
            if (this.autoBuyGardenEnabled && Time.unscaledTime >= this.nextAutoBuyGardenLogicTime)
            {
                this.nextAutoBuyGardenLogicTime = Time.unscaledTime + 0.05f;
                this.RunAutoBuyGardenLogic();
            }
            if (this.autoBuyFishingEnabled && Time.unscaledTime >= this.nextAutoBuyFishingLogicTime)
            {
                this.nextAutoBuyFishingLogicTime = Time.unscaledTime + 0.05f;
                this.RunAutoBuyFishingLogic();
            }
            if (this.autoFishingFarmBreaker.ShouldRun(Time.unscaledTime))
            {
                try { AutoFishingFarm.Update(this); this.autoFishingFarmBreaker.Success(); }
                catch (Exception ex) { this.autoFishingFarmBreaker.Failure("AutoFishingFarm", ex, Time.unscaledTime); }
            }
            this.ProcessAutoSell();
            this.RunTreeFarmLogic();
            this.RunLobbyAutoActions();
            this.CloseAnnouncementPanelIfPresent();
            if (this.bypassEnabled || this.bypassObjectsHidden)
            {
                this.RunBypassLogic(this.bypassEnabled);
            }
            bool flag7 = this.birdVacuumEnabled;
            if (flag7)
            {
                this.VacuumBirds();
            }
            this.CheckManualBerryCollectionListeners();
            this.SyncNearbyLiveResourceCooldowns();
            bool flag8 = this.isRadarActive;
            if (flag8)
            {
                bool flag9 = Time.unscaledTime - this.lastScanTime > 2f;
                if (flag9)
                {
                    this.RunRadar();
                    this.lastScanTime = Time.unscaledTime;
                }
                this.UpdateMarkers();
                this.UpdateRadarGroundRings();
                if (this.isRadarActive)
                {
                    this.CleanupExpiredCooldowns();
                }
            }
            bool flag10 = this.autoFarmActive;
            if (flag10)
            {
                this.RunAutoFarmLogic();
                if (this.autoFarmAutoStopEnabled && this.autoFarmAutoStopAt > 0f && Time.unscaledTime >= this.autoFarmAutoStopAt)
                {
                    this.ToggleAutoFarm();
                    this.AddMenuNotification("Auto Farm auto-stopped (timer)", new Color(1f, 0.75f, 0.45f));
                }
            }
            // Farm ticks behind circuit breakers: a systematically failing farm cools down for
            // 30s instead of throwing (and logging) every frame, and is disabled after repeated
            // cooldown cycles. One good tick resets the breaker.
            float farmTickNow = Time.unscaledTime;
            if (this.auraFarmBreaker.ShouldRun(farmTickNow))
            {
                try { this.UpdateAuraFarm(); this.auraFarmBreaker.Success(); }
                catch (Exception ex) { this.auraFarmBreaker.Failure("AuraFarm", ex, farmTickNow); }
            }
            if (this.homelandFarmBreaker.ShouldRun(farmTickNow))
            {
                try { this.UpdateHomelandFarmBackground(); this.homelandFarmBreaker.Success(); }
                catch (Exception ex) { this.homelandFarmBreaker.Failure("HomelandFarm", ex, farmTickNow); }
            }
            if (this.birdNetFarmBreaker.ShouldRun(farmTickNow))
            {
                try { BirdNetFarm.Update(this); this.birdNetFarmBreaker.Success(); }
                catch (Exception ex) { this.birdNetFarmBreaker.Failure("BirdNetFarm", ex, farmTickNow); }
            }
            if (this.insectNetFarmBreaker.ShouldRun(farmTickNow))
            {
                try { InsectNetFarm.Update(this); this.insectNetFarmBreaker.Success(); }
                catch (Exception ex) { this.insectNetFarmBreaker.Failure("InsectNetFarm", ex, farmTickNow); }
            }
            if (this.puzzleNetBreaker.ShouldRun(farmTickNow))
            {
                try { this.UpdatePuzzleAutomation(); this.puzzleNetBreaker.Success(); }
                catch (Exception ex) { this.puzzleNetBreaker.Failure("PuzzleNet", ex, farmTickNow); }
            }
            if (this.resourceFarmBreaker.ShouldRun(farmTickNow))
            {
                try { this.UpdateResourceFarm(); this.resourceFarmBreaker.Success(); }
                catch (Exception ex) { this.resourceFarmBreaker.Failure("ResourceFarm", ex, farmTickNow); }
            }

            this.UpdateVisualDebugEsp();
        }


        private float GetUiScale()
        {
            float requested = this.NormalizeUiScale(this.uiScale > 0f ? this.uiScale : 1f);
            float baseWidth = this.targetWindowWidth > 1f ? this.targetWindowWidth : 1180f;
            float baseHeight = this.targetWindowHeight > 1f ? this.targetWindowHeight : 720f;
            float fitScale = Mathf.Min((float)Screen.width / baseWidth, (float)Screen.height / baseHeight);
            fitScale = Mathf.Clamp(fitScale, UiScaleMin, UiScaleMax);
            return Mathf.Min(requested, fitScale);
        }

        private float NormalizeUiScale(float scale)
        {
            scale = Mathf.Clamp(scale, UiScaleMin, UiScaleMax);
            return Mathf.Clamp(Mathf.Round(scale / UiScaleStep) * UiScaleStep, UiScaleMin, UiScaleMax);
        }




        private void DrawCardOutline(Rect rect, float thickness = 1f)
        {
            Color prev = GUI.color;
            Color edge = new Color(1f, 1f, 1f, Mathf.Clamp(0.06f + (this.uiPanelAlpha * 0.1f), 0.08f, 0.18f));
            Color accentTop = new Color(this.uiAccentR, this.uiAccentG, this.uiAccentB, Mathf.Clamp(0.28f + (this.uiPanelAlpha * 0.28f), 0.25f, 0.6f));
            Color shadow = new Color(0f, 0f, 0f, 0.28f);

            GUI.color = edge;
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.x, rect.y, thickness, rect.height), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), Texture2D.whiteTexture);
            GUI.color = accentTop;
            GUI.DrawTexture(new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, 1f), Texture2D.whiteTexture);
            GUI.color = shadow;
            GUI.DrawTexture(new Rect(rect.x + 1f, rect.yMax - 1f, rect.width - 2f, 1f), Texture2D.whiteTexture);
            GUI.color = prev;
        }

        private void DrawRoundedPanel(Rect rect, float radius, Color fill, Color border, float borderWidth, Color topAccent)
        {
            this.EnsureUiPrimitiveTextures();

            float corner = Mathf.Clamp(radius, 0f, Mathf.Min(rect.width, rect.height) * 0.5f);
            if (corner <= 0.5f)
            {
                GUI.color = fill;
                GUI.DrawTexture(rect, Texture2D.whiteTexture);
                GUI.color = Color.white;
            }
            else
            {
                float diameter = corner * 2f;
                Rect center = new Rect(rect.x + corner, rect.y + corner, rect.width - diameter, rect.height - diameter);
                Rect top = new Rect(rect.x + corner, rect.y, rect.width - diameter, corner);
                Rect bottom = new Rect(rect.x + corner, rect.yMax - corner, rect.width - diameter, corner);
                Rect left = new Rect(rect.x, rect.y + corner, corner, rect.height - diameter);
                Rect right = new Rect(rect.xMax - corner, rect.y + corner, corner, rect.height - diameter);
                Rect topLeft = new Rect(rect.x, rect.y, corner, corner);
                Rect topRight = new Rect(rect.xMax - corner, rect.y, corner, corner);
                Rect bottomLeft = new Rect(rect.x, rect.yMax - corner, corner, corner);
                Rect bottomRight = new Rect(rect.xMax - corner, rect.yMax - corner, corner, corner);

                GUI.color = fill;
                GUI.DrawTexture(center, Texture2D.whiteTexture);
                GUI.DrawTexture(top, Texture2D.whiteTexture);
                GUI.DrawTexture(bottom, Texture2D.whiteTexture);
                GUI.DrawTexture(left, Texture2D.whiteTexture);
                GUI.DrawTexture(right, Texture2D.whiteTexture);
                GUI.BeginGroup(topLeft);
                GUI.DrawTexture(new Rect(0f, 0f, diameter, diameter), this.uiCircleTexture);
                GUI.EndGroup();
                GUI.BeginGroup(topRight);
                GUI.DrawTexture(new Rect(-corner, 0f, diameter, diameter), this.uiCircleTexture);
                GUI.EndGroup();
                GUI.BeginGroup(bottomLeft);
                GUI.DrawTexture(new Rect(0f, -corner, diameter, diameter), this.uiCircleTexture);
                GUI.EndGroup();
                GUI.BeginGroup(bottomRight);
                GUI.DrawTexture(new Rect(-corner, -corner, diameter, diameter), this.uiCircleTexture);
                GUI.EndGroup();
                GUI.color = Color.white;
            }

            if (borderWidth > 0f)
            {
                GUI.color = border.a > 0f ? border : new Color(1f, 1f, 1f, 0.12f);
                GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, borderWidth), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(rect.x, rect.yMax - borderWidth, rect.width, borderWidth), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(rect.x, rect.y, borderWidth, rect.height), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(rect.xMax - borderWidth, rect.y, borderWidth, rect.height), Texture2D.whiteTexture);
                GUI.color = Color.white;

                if (topAccent.a > 0f)
                {
                    GUI.color = topAccent;
                    GUI.DrawTexture(new Rect(rect.x + 1f, rect.y + 1f, rect.width - 2f, 1.5f), Texture2D.whiteTexture);
                    GUI.color = Color.white;
                }
            }
        }













        private void DrawExentriSectionPanel(Rect rect, Color accent, Color fill, Color softLine)
        {
            this.DrawRoundedPanel(rect, 10f, fill, softLine, 1f, Color.clear);

            GUI.color = new Color(accent.r, accent.g, accent.b, 0.9f);
            GUI.DrawTexture(new Rect(rect.x, rect.y, rect.width, 1.5f), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        private bool DrawPrimaryActionButton(Rect rect, string label)
        {
            return GUI.Button(rect, this.L(label), this.themePrimaryButtonStyle ?? GUI.skin.button);
        }

        // Public wrappers for external UI modules

        private bool DrawDangerActionButton(Rect rect, string label)
        {
            return GUI.Button(rect, this.L(label), this.themeDangerButtonStyle ?? GUI.skin.button);
        }

        private bool DrawSwitchToggle(Rect rect, bool value, string label)
        {
            GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.fontSize = 14;
            labelStyle.fontStyle = FontStyle.Normal;
            labelStyle.alignment = TextAnchor.MiddleLeft;
            labelStyle.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB);
            GUI.Label(new Rect(rect.x, rect.y, rect.width - 60f, rect.height), this.L(label), labelStyle);

            this.EnsureUiPrimitiveTextures();
            Rect switchRect = new Rect(rect.xMax - 46f, rect.y + Mathf.Max(0f, (rect.height - 20f) * 0.5f), 40f, 20f);
            Rect trackRect = new Rect(switchRect.x, switchRect.y + 1f, switchRect.width, 18f);
            Color accent = new Color(this.uiAccentR, this.uiAccentG, this.uiAccentB, 0.98f);
            Color offTrack = new Color(0.16f, 0.16f, 0.22f, 0.98f);
            Color onTrack = new Color(accent.r * 0.92f, accent.g * 0.72f, accent.b, 1f);

            if (value)
            {
                Rect glowRect = new Rect(trackRect.x - 2f, trackRect.y - 2f, trackRect.width + 4f, trackRect.height + 4f);
                this.DrawCapsule(glowRect, new Color(accent.r, accent.g, accent.b, 0.28f));
            }

            this.DrawCapsule(trackRect, value ? onTrack : offTrack);

            float knobDiameter = 14f;
            float knobX = value ? (trackRect.xMax - knobDiameter - 2f) : (trackRect.x + 2f);
            Rect knobRect = new Rect(knobX, trackRect.y + (trackRect.height - knobDiameter) * 0.5f, knobDiameter, knobDiameter);
            Color knobColor = value ? new Color(0.96f, 0.97f, 1f, 1f) : new Color(0.68f, 0.7f, 0.78f, 1f);
            GUI.color = knobColor;
            GUI.DrawTexture(knobRect, this.uiCircleTexture);
            GUI.color = Color.white;

            Event e = Event.current;
            if (e != null && e.type == EventType.MouseDown && e.button == 0 && rect.Contains(e.mousePosition))
            {
                value = !value;
                e.Use();
            }

            return value;
        }

        private float GetSwitchToggleHeight(float width, string label, float minHeight = 20f)
        {
            GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.fontSize = 14;
            labelStyle.fontStyle = FontStyle.Normal;
            labelStyle.alignment = TextAnchor.UpperLeft;
            labelStyle.wordWrap = true;
            float labelWidth = Mathf.Max(60f, width - 60f);
            float labelHeight = labelStyle.CalcHeight(new GUIContent(this.L(label)), labelWidth);
            return Mathf.Max(minHeight, labelHeight);
        }

        private bool DrawWrappedSwitchToggle(Rect rect, bool value, string label, float minHeight = 20f)
        {
            GUIStyle labelStyle = new GUIStyle(GUI.skin.label);
            labelStyle.fontSize = 14;
            labelStyle.fontStyle = FontStyle.Normal;
            labelStyle.alignment = TextAnchor.UpperLeft;
            labelStyle.wordWrap = true;
            labelStyle.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB);

            float rowHeight = Mathf.Max(rect.height, this.GetSwitchToggleHeight(rect.width, label, minHeight));
            float labelWidth = Mathf.Max(60f, rect.width - 60f);
            GUI.Label(new Rect(rect.x, rect.y, labelWidth, rowHeight), this.L(label), labelStyle);

            this.EnsureUiPrimitiveTextures();
            Rect switchRect = new Rect(rect.xMax - 46f, rect.y + Mathf.Max(0f, (rowHeight - 20f) * 0.5f), 40f, 20f);
            Rect trackRect = new Rect(switchRect.x, switchRect.y + 1f, switchRect.width, 18f);
            Color accent = new Color(this.uiAccentR, this.uiAccentG, this.uiAccentB, 0.98f);
            Color offTrack = new Color(0.16f, 0.16f, 0.22f, 0.98f);
            Color onTrack = new Color(accent.r * 0.92f, accent.g * 0.72f, accent.b, 1f);

            if (value)
            {
                Rect glowRect = new Rect(trackRect.x - 2f, trackRect.y - 2f, trackRect.width + 4f, trackRect.height + 4f);
                this.DrawCapsule(glowRect, new Color(accent.r, accent.g, accent.b, 0.28f));
            }

            this.DrawCapsule(trackRect, value ? onTrack : offTrack);

            float knobDiameter = 14f;
            float knobX = value ? (trackRect.xMax - knobDiameter - 2f) : (trackRect.x + 2f);
            Rect knobRect = new Rect(knobX, trackRect.y + (trackRect.height - knobDiameter) * 0.5f, knobDiameter, knobDiameter);
            Color knobColor = value ? new Color(0.96f, 0.97f, 1f, 1f) : new Color(0.68f, 0.7f, 0.78f, 1f);
            GUI.color = knobColor;
            GUI.DrawTexture(knobRect, this.uiCircleTexture);
            GUI.color = Color.white;

            Event e = Event.current;
            Rect hitRect = new Rect(rect.x, rect.y, rect.width, rowHeight);
            if (e != null && e.type == EventType.MouseDown && e.button == 0 && hitRect.Contains(e.mousePosition))
            {
                value = !value;
                e.Use();
            }

            return value;
        }

        private float ReadAccentSliderMouseValue(Rect rect, float mouseX, float min, float max, bool integerSteps)
        {
            float tInput = Mathf.Clamp01((mouseX - rect.x) / Mathf.Max(1f, rect.width));
            if (!integerSteps || Mathf.Approximately(min, max))
            {
                return Mathf.Lerp(min, max, tInput);
            }

            int iMin = Mathf.RoundToInt(min);
            int iMax = Mathf.RoundToInt(max);
            int range = Mathf.Max(0, iMax - iMin);
            int stepped = iMin + Mathf.Clamp(Mathf.FloorToInt(tInput * (range + 1)), 0, range);
            return stepped;
        }

        private void DrawAccentSliderVisual(Rect rect, float value, float min, float max)
        {
            float t = Mathf.InverseLerp(min, max, value);
            float lineY = rect.y + rect.height * 0.5f - 2.5f;
            Rect bgRect = new Rect(rect.x, lineY, rect.width, 5f);
            Rect fillRect = new Rect(rect.x, lineY, Mathf.Max(5f, rect.width * t), 5f);
            float thumbX = Mathf.Clamp(rect.x + rect.width * t, rect.x + 6f, rect.xMax - 6f);
            Rect thumbGlowRect = new Rect(thumbX - 8f, rect.y + rect.height * 0.5f - 8f, 16f, 16f);
            Rect thumbRect = new Rect(thumbX - 6f, rect.y + rect.height * 0.5f - 6f, 12f, 12f);

            this.DrawCapsule(bgRect, new Color(0.18f, 0.19f, 0.24f, 0.92f));
            this.DrawCapsule(fillRect, new Color(this.uiAccentR, this.uiAccentG, this.uiAccentB, 0.94f));
            GUI.color = new Color(this.uiAccentR, this.uiAccentG, this.uiAccentB, 0.28f);
            GUI.DrawTexture(thumbGlowRect, this.uiCircleTexture);
            GUI.color = new Color(0.95f, 0.97f, 1f, 1f);
            GUI.DrawTexture(thumbRect, this.uiCircleTexture);
            GUI.color = Color.white;
        }

        private float DrawAccentSlider(Rect rect, float value, float min, float max, bool integerSteps = false)
        {
            int controlId = GUIUtility.GetControlID(FocusType.Passive);
            Event e = Event.current;
            if (e != null && e.button == 0)
            {
                if (e.type == EventType.MouseDown && rect.Contains(e.mousePosition))
                {
                    GUIUtility.hotControl = controlId;
                    value = this.ReadAccentSliderMouseValue(rect, e.mousePosition.x, min, max, integerSteps);
                    e.Use();
                }
                else if (e.type == EventType.MouseDrag && GUIUtility.hotControl == controlId)
                {
                    value = this.ReadAccentSliderMouseValue(rect, e.mousePosition.x, min, max, integerSteps);
                    e.Use();
                }
                else if (e.type == EventType.MouseUp && GUIUtility.hotControl == controlId)
                {
                    GUIUtility.hotControl = 0;
                    e.Use();
                }
            }

            value = Mathf.Clamp(value, min, max);
            this.DrawAccentSliderVisual(rect, value, min, max);
            return value;
        }





        private void InitializeLocalization()
        {
            string preferredLanguage = "en";
            try
            {
                UnifiedConfigData config = this.LoadUnifiedConfig();
                if (config != null && !string.IsNullOrWhiteSpace(config.Language))
                {
                    preferredLanguage = config.Language;
                }
            }
            catch
            {
            }

            LocalizationManager.Initialize(AppDomain.CurrentDomain.BaseDirectory, preferredLanguage);
            this.selectedLanguage = LocalizationManager.CurrentLanguage;
        }

        private string L(string text)
        {
            return LocalizationManager.Translate(text);
        }

        private string LF(string format, params object[] args)
        {
            return LocalizationManager.Format(format, args);
        }






        private void SetGameSpeed(float speed)
        {
            this.gameSpeed = Mathf.Clamp(speed, 1f, 10f);
            this.ApplyGameSpeed(true);
        }



        private void ApplyGameSpeed(bool force = false)
        {
            float speed = Mathf.Clamp(this.gameSpeed, 1f, 10f);
            if (!this.gameTimingCaptured)
            {
                this.baseFixedDeltaTime = Mathf.Max(0.001f, Time.fixedDeltaTime);
                this.baseMaximumDeltaTime = Mathf.Max(this.baseFixedDeltaTime, Time.maximumDeltaTime);
                this.gameTimingCaptured = true;
            }

            if (!force && Math.Abs(Time.timeScale - speed) <= 0.05f && Math.Abs(this.lastAppliedGameSpeed - speed) <= 0.001f)
            {
                return;
            }

            this.gameSpeed = speed;
            Time.timeScale = speed;

            // Keep real-time physics frequency stable at high speed instead of multiplying fixed updates per frame.
            Time.fixedDeltaTime = this.baseFixedDeltaTime * speed;
            Time.maximumDeltaTime = Mathf.Max(this.baseMaximumDeltaTime, Time.fixedDeltaTime * 2f);
            this.lastAppliedGameSpeed = speed;
        }





        public bool TryGetCurrentToolInfo(out int toolId, out string toolName, out string status)
        {
            toolId = 0;
            toolName = string.Empty;
            status = "Unknown";

            if (!this.TryGetCurrentToolDurability(out toolId, out _, out _, out status))
            {
                return false;
            }

            toolName = this.GetAutoRepairSupportedToolName(toolId);
            return true;
        }



        private Type FindLoadedEcsServiceType()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
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

                foreach (Type type in types)
                {
                    if (type == null)
                    {
                        continue;
                    }

                    string typeName = type.Name ?? string.Empty;
                    string fullName = type.FullName ?? string.Empty;
                    bool nameMatch = string.Equals(typeName, "EcsService", StringComparison.Ordinal)
                        || typeName.EndsWith("EcsService", StringComparison.Ordinal)
                        || fullName.EndsWith(".EcsService", StringComparison.Ordinal)
                        || fullName.IndexOf(".ProtocolService.EcsService", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!nameMatch)
                    {
                        continue;
                    }

                    MethodInfo tryGetMethod = type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .FirstOrDefault(m => m.Name == "TryGet" && m.IsGenericMethodDefinition && m.GetParameters().Length == 2);
                    if (tryGetMethod != null)
                    {
                        return type;
                    }
                }
            }

            return null;
        }

        private Type FindLoadedToolServiceType()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch
                {
                    continue;
                }

                foreach (Type type in types)
                {
                    if (type == null)
                    {
                        continue;
                    }

                    bool nameMatch = string.Equals(type.Name, "IToolService", StringComparison.Ordinal)
                        || string.Equals(type.Name, "ToolService", StringComparison.Ordinal);
                    if (!nameMatch)
                    {
                        continue;
                    }

                    MethodInfo tryGetTakenToolMethod = type.GetMethod("TryGetTakenTool", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    MethodInfo getToolDurabilityMethod = type.GetMethod("GetToolDurability", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    MethodInfo tryGetToolComponentMethod = type.GetMethod("TryGetToolComponent", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (tryGetTakenToolMethod != null && (getToolDurabilityMethod != null || tryGetToolComponentMethod != null))
                    {
                        return type;
                    }
                }
            }

            return null;
        }







        private bool TryGetCurrentHandholdObject(out object handholdObj, out string source)
        {
            handholdObj = null;
            source = "none";

            try
            {
                if (this.TryGetManagedInteractSystemObject(out object interactSystemObj, out string interactSource) && interactSystemObj != null)
                {
                    foreach (string memberName in new string[] { "_handhold", "handhold" })
                    {
                        if (this.TryGetObjectMember(interactSystemObj, memberName, out handholdObj) && handholdObj != null)
                        {
                            source = interactSource + " -> " + interactSystemObj.GetType().Name + "." + memberName;
                            return true;
                        }
                    }

                    source = interactSource + " -> handhold";
                }

                object playerObj = null;
                string playerSource = string.Empty;
                if (!this.TryGetManagedSelfPlayerObject(out playerObj, out playerSource) || playerObj == null)
                {
                    if (!this.TryGetManagedInteractPlayerObject(interactSystemObj, out playerObj, out playerSource) || playerObj == null)
                    {
                        source = interactSystemObj != null ? interactSource + " -> player" : "Self player unavailable";
                        return false;
                    }
                }

                object equipComponent;
                if (!(this.TryInvokeZeroArgMember(playerObj, out equipComponent, "get_equipComponent", "GetEquipComponent")
                    || this.TryGetObjectMember(playerObj, "equipComponent", out equipComponent)
                    || this.TryGetObjectMember(playerObj, "_equipComponent", out equipComponent))
                    || equipComponent == null)
                {
                    source = playerSource + " -> equipComponent";
                    return false;
                }

                if ((this.TryInvokeZeroArgMember(equipComponent, out handholdObj, "get_handhold", "GetHandhold")
                    || this.TryGetObjectMember(equipComponent, "handhold", out handholdObj)
                    || this.TryGetObjectMember(equipComponent, "_handhold", out handholdObj)) && handholdObj != null)
                {
                    source = playerSource + " -> " + equipComponent.GetType().Name + ".handhold";
                    return true;
                }

                foreach (string memberName in new string[] { "_handhold", "handhold" })
                {
                    if (this.TryGetObjectMember(playerObj, memberName, out handholdObj) && handholdObj != null)
                    {
                        source = playerSource + " -> " + playerObj.GetType().Name + "." + memberName;
                        return true;
                    }
                }

                source = playerSource + " -> " + equipComponent.GetType().Name + ".handhold";
                return false;
            }
            catch (Exception ex)
            {
                source = "exception: " + ex.Message;
                return false;
            }
        }













        private string GetHudComponentDebugName(Component component)
        {
            if (component == null)
            {
                return "<null>";
            }

            try
            {
                if (this.TryDescribeDynamicMonoBehaviour(component, out string dynamicDescription) && !string.IsNullOrEmpty(dynamicDescription))
                {
                    return dynamicDescription;
                }

                if (this.TryGetHudDurabilityFromManagedWrapper(component, out _, out _, out string managedTargetName) && !string.IsNullOrEmpty(managedTargetName))
                {
                    return (component.GetType().FullName ?? component.GetType().Name ?? "<unknown>") + "->" + managedTargetName;
                }

                Il2CppObject obj = component.TryCast<Il2CppObject>();
                Il2CppType componentType = obj?.GetIl2CppType();
                string baseName = componentType?.FullName?.ToString() ?? componentType?.Name?.ToString() ?? component.GetType().FullName ?? component.GetType().Name ?? "<unknown>";
                if (this.TryGetHudDurabilityTarget(component, out _, out Il2CppType targetType) && targetType != null)
                {
                    string targetName = targetType.FullName?.ToString() ?? targetType.Name?.ToString();
                    if (!string.IsNullOrEmpty(targetName) && !string.Equals(targetName, baseName, StringComparison.Ordinal))
                    {
                        return baseName + "->" + targetName;
                    }
                }

                return baseName;
            }
            catch
            {
                return component.GetType().FullName ?? component.GetType().Name ?? "<unknown>";
            }
        }

        private bool TryDescribeDynamicMonoBehaviour(Component component, out string description)
        {
            description = null;
            if (component == null)
            {
                return false;
            }

            try
            {
                Type wrapperType = component.GetType();
                string wrapperName = wrapperType.FullName ?? wrapperType.Name ?? "<unknown>";
                if (wrapperName.IndexOf("DynamicMonoBehaviour", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    Il2CppObject obj = component.TryCast<Il2CppObject>();
                    string ilName = obj?.GetIl2CppType()?.FullName?.ToString();
                    if (string.IsNullOrEmpty(ilName) || ilName.IndexOf("DynamicMonoBehaviour", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        return false;
                    }
                    wrapperName = ilName;
                }

                string behaviourType = null;
                MethodInfo getBehaviourTypeMethod = wrapperType.GetMethod("GetBehaviourType", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (getBehaviourTypeMethod != null)
                {
                    try
                    {
                        behaviourType = getBehaviourTypeMethod.Invoke(component, null)?.ToString();
                    }
                    catch
                    {
                    }
                }

                object implObject = null;
                string implTypeName = null;
                PropertyInfo implProperty = wrapperType.GetProperty("Impl", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (implProperty != null)
                {
                    try
                    {
                        implObject = implProperty.GetValue(component, null);
                    }
                    catch
                    {
                    }
                }

                if (implObject == null)
                {
                    FieldInfo implInternalField = this.FindFieldInHierarchy(wrapperType, "ImplInternal");
                    if (implInternalField != null)
                    {
                        try
                        {
                            implObject = implInternalField.GetValue(component);
                        }
                        catch
                        {
                        }
                    }
                }

                if (implObject != null)
                {
                    Type implType = implObject.GetType();
                    bool hasRatio = this.FindFieldInHierarchy(implType, "_durabilityRatio") != null;
                    bool hasNode = this.FindFieldInHierarchy(implType, "durabilityNode") != null;
                    implTypeName = (implType.FullName ?? implType.Name ?? "<unknown>") + $"[ratio={hasRatio},node={hasNode}]";
                }
                else
                {
                    implTypeName = "impl=<null>";
                }

                description = string.IsNullOrEmpty(behaviourType)
                    ? wrapperName + "->" + implTypeName
                    : wrapperName + "[" + behaviourType + "]->" + implTypeName;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private FieldInfo FindFieldInHierarchy(Type type, string fieldName)
        {
            while (type != null)
            {
                FieldInfo field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    return field;
                }

                type = type.BaseType;
            }

            return null;
        }

        private string GetTransformPath(Transform transform)
        {
            if (transform == null)
            {
                return "<null>";
            }

            List<string> parts = new List<string>();
            Transform current = transform;
            int depth = 0;
            while (current != null && depth < 32)
            {
                parts.Add(current.name);
                current = current.parent;
                depth++;
            }

            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }


        private bool ToastContainsLocalizedPhrase(string message, string phraseKey)
        {
            if (string.IsNullOrWhiteSpace(message) || string.IsNullOrWhiteSpace(phraseKey))
            {
                return false;
            }

            foreach (string candidate in LocalizationManager.GetTranslationCandidates(phraseKey))
            {
                if (message.IndexOf(candidate, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private bool ToastContainsAllLocalizedPhrases(string message, params string[] phraseKeys)
        {
            if (string.IsNullOrWhiteSpace(message) || phraseKeys == null || phraseKeys.Length == 0)
            {
                return false;
            }

            foreach (string phraseKey in phraseKeys)
            {
                if (!this.ToastContainsLocalizedPhrase(message, phraseKey))
                {
                    return false;
                }
            }

            return true;
        }

        private void SetLocalizationLanguage(string languageCode, bool showNotification = true)
        {
            if (string.IsNullOrWhiteSpace(languageCode))
            {
                languageCode = "en";
            }

            LocalizationManager.SetLanguage(languageCode);
            this.selectedLanguage = LocalizationManager.CurrentLanguage;
            this.SaveKeybinds(false);

            if (showNotification)
            {
                this.AddMenuNotification(this.LF("Language switched to {0}", LocalizationManager.GetLanguageDisplayName(this.selectedLanguage)), new Color(0.55f, 0.88f, 1f));
            }
        }



























        // Public helpers for external modules
















        private sealed class BirdFarmAuraCandidate
        {
            public uint NetId;
            public int StaticId;
            public float Distance;
            public int BirdActionType;
            public int BirdState;
            public uint BirdStandNetId;
            public bool IsPerchBird;
        }

        private sealed class BirdFarmAuraInspectCandidate
        {
            public IntPtr EntityObj;
            public uint NetId;
            public Vector3 Position;
            public float Distance;
        }

        private sealed class BirdFarmAuraResolvedDetail
        {
            public int StaticId;
            public int BirdActionType;
            public int BirdState;
            public uint BirdStandNetId;
            public bool IsPerchBird;
            public float ExpiresAt;
        }

        private readonly struct AuraMonoMethodCacheKey : IEquatable<AuraMonoMethodCacheKey>
        {
            private readonly IntPtr classPtr;
            private readonly string methodName;
            private readonly int paramCount;

            public AuraMonoMethodCacheKey(IntPtr classPtr, string methodName, int paramCount)
            {
                this.classPtr = classPtr;
                this.methodName = methodName ?? string.Empty;
                this.paramCount = paramCount;
            }

            public bool Equals(AuraMonoMethodCacheKey other)
            {
                return this.classPtr == other.classPtr
                    && this.paramCount == other.paramCount
                    && string.Equals(this.methodName, other.methodName, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is AuraMonoMethodCacheKey other && this.Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 17;
                    hash = (hash * 31) + this.classPtr.GetHashCode();
                    hash = (hash * 31) + this.paramCount;
                    hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(this.methodName);
                    return hash;
                }
            }
        }

        private readonly struct AuraMonoFieldCacheKey : IEquatable<AuraMonoFieldCacheKey>
        {
            private readonly IntPtr classPtr;
            private readonly string fieldName;

            public AuraMonoFieldCacheKey(IntPtr classPtr, string fieldName)
            {
                this.classPtr = classPtr;
                this.fieldName = fieldName ?? string.Empty;
            }

            public bool Equals(AuraMonoFieldCacheKey other)
            {
                return this.classPtr == other.classPtr
                    && string.Equals(this.fieldName, other.fieldName, StringComparison.Ordinal);
            }

            public override bool Equals(object obj)
            {
                return obj is AuraMonoFieldCacheKey other && this.Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 17;
                    hash = (hash * 31) + this.classPtr.GetHashCode();
                    hash = (hash * 31) + StringComparer.Ordinal.GetHashCode(this.fieldName);
                    return hash;
                }
            }
        }






























        // GamePhotoMode is a transient Character state (created/destroyed with the bird scanner),
        // not a process-lifetime singleton. Per AGENTS.md §12: do not cache its MonoObject* across
        // frames (raw IntPtr → UAF; AuraMonoObjectCache pin → stale detached state AV). Re-resolve
        // from Character._states on every call; the pointer is only valid in the caller's sync scope.
        private bool TryResolveAuraMonoGamePhotoModeObject(out IntPtr photoModeObj, out string status)
        {
            photoModeObj = IntPtr.Zero;
            status = "not attempted";

            float now = Time.unscaledTime;
            if (now < this.nextBirdFarmPhotoModeMissingBackoffAt)
            {
                status = "GamePhotoMode resolve throttled";
                return false;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoClassFromName == null || auraMonoObjectGetClass == null || auraMonoRuntimeInvoke == null)
            {
                status = "AuraMono API unavailable";
                return false;
            }

            IntPtr levelImage = this.FindAuraMonoImage(new string[] { "XDTLevelAndEntity", "XDTLevelAndEntity.dll", "Client", "Client.dll" });
            IntPtr characterClass = levelImage != IntPtr.Zero ? auraMonoClassFromName(levelImage, "XDTLevelAndEntity.Game.GameMode", "Character") : IntPtr.Zero;
            if (characterClass == IntPtr.Zero)
            {
                characterClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTLevelAndEntity.Game.GameMode", "Character");
            }
            if (characterClass == IntPtr.Zero)
            {
                status = "Character class unavailable";
                return false;
            }

            IntPtr characterObj = IntPtr.Zero;
            if (!this.TryGetAuraMonoStaticObjectField(characterClass, "_character", out characterObj) || characterObj == IntPtr.Zero)
            {
                IntPtr getCharacterMethod = this.FindAuraMonoMethodOnHierarchy(characterClass, "get_character", 0);
                if (getCharacterMethod != IntPtr.Zero)
                {
                    IntPtr exc = IntPtr.Zero;
                    characterObj = auraMonoRuntimeInvoke(getCharacterMethod, IntPtr.Zero, IntPtr.Zero, ref exc);
                }
            }
            if (characterObj == IntPtr.Zero)
            {
                status = "Character._character null";
                return false;
            }

            if (this.TryGetMonoObjectMember(characterObj, "_states", out IntPtr statesObj) && statesObj != IntPtr.Zero)
            {
                List<IntPtr> states = this.birdFarmAuraStateBuffer;
                states.Clear();
                if (this.TryEnumerateAuraMonoCollectionItems(statesObj, states))
                {
                    for (int i = 0; i < states.Count; i++)
                    {
                        IntPtr stateObj = states[i];
                        if (stateObj == IntPtr.Zero)
                        {
                            continue;
                        }

                        string className = this.GetAuraMonoClassDisplayName(auraMonoObjectGetClass(stateObj));
                        if (className.EndsWith(".GamePhotoMode", StringComparison.Ordinal) || string.Equals(className, "GamePhotoMode", StringComparison.Ordinal))
                        {
                            photoModeObj = stateObj;
                            status = "Character._states";
                            this.nextBirdFarmPhotoModeMissingBackoffAt = -999f;
                            return true;
                        }
                    }
                }
            }

            this.nextBirdFarmPhotoModeMissingBackoffAt = now + 0.75f;
            status = "GamePhotoMode not found in Character states";
            return false;
        }





























        private const int MaxEntitySourceDepth = 4; // Prevent stack overflow







        // pins: optional parallel list of pinned GC handles, one per output item, pinned at the
        // moment each item is obtained. REQUIRED whenever the caller reads members of the items
        // afterwards: SGen is a moving collector, and any mono-side allocation between obtaining
        // a raw MonoObject* and using it (incl. our own boxed MoveNext/member reads) can move the
        // object, leaving the pointer on a GC filler ("mono_class_get_flags: unexpected GC filler
        // class" fatal assert — the recurring AFK crash). Free via FreeAuraMonoPins.

        private const int MaxAuraMonoEntities = 8192; // Dense towns can exceed 2000 loaded entities; NetCook needs later cook-builds too.
        // Truncation guard for generic AuraMono collection enumeration (e.g. LevelObjectManager._dictionary,
        // which holds crop boxes / planters). Raised from 4096 so dense worlds don't hide farm targets.
        private const int MaxAuraMonoCollectionItems = 8192;

        // One-shot actions (e.g. wild gift claim) can opt out of the 4096 truncation so dense towns don't hide targets.
        // 0 = use the default MaxAuraMonoEntities cap. Always reset via try/finally after the enumeration.
        private int auraMonoEntityEnumerationCapOverride = 0;

        private int AuraMonoEntityEnumerationCap =>
            this.auraMonoEntityEnumerationCapOverride > 0 ? this.auraMonoEntityEnumerationCapOverride : MaxAuraMonoEntities;











        private readonly Dictionary<IntPtr, string> auraMonoClassDisplayNameCache = new Dictionary<IntPtr, string>();

















        private bool TryGetManagedInteractSystemObject(out object interactSystemObj, out string source)
        {
            interactSystemObj = null;
            source = "none";

            Type interactType = this.FindLoadedType("XDTLevelAndEntity.BaseSystem.InteractSystem.InteractSystem", "InteractSystem");
            if (interactType == null)
            {
                return false;
            }

            try
            {
                PropertyInfo instanceProperty = interactType.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (instanceProperty != null)
                {
                    interactSystemObj = instanceProperty.GetValue(null, null);
                    if (interactSystemObj != null)
                    {
                        source = "InteractSystem.Instance";
                        return true;
                    }
                }
            }
            catch { }

            try
            {
                FieldInfo instanceField = interactType.GetField("_instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    ?? interactType.GetField("instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (instanceField != null)
                {
                    interactSystemObj = instanceField.GetValue(null);
                    if (interactSystemObj != null)
                    {
                        source = "InteractSystem._instance";
                        return true;
                    }
                }
            }
            catch { }

            try
            {
                Type playerInteractionType = this.FindLoadedType(
                    "XDTLevelAndEntity.Gameplay.Interaction.PlayerInteraction",
                    "PlayerInteraction");
                if (playerInteractionType != null)
                {
                    source = "PlayerInteraction[static]";
                    interactSystemObj = playerInteractionType;
                    return true;
                }
            }
            catch { }

            return false;
        }

        private bool TryGetManagedSelfPlayerObject(out object playerObj, out string source)
        {
            playerObj = null;
            source = "none";

            try
            {
                Type entityUtilType = this.FindLoadedType("XDTLevelAndEntity.BaseSystem.EntitiesManager.EntityUtil", "EntityUtil");
                if (entityUtilType != null)
                {
                    MethodInfo getSelfPlayerMethod = entityUtilType.GetMethod("GetSelfPlayer", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (getSelfPlayerMethod != null)
                    {
                        playerObj = getSelfPlayerMethod.Invoke(null, null);
                        if (playerObj != null)
                        {
                            source = "EntityUtil.GetSelfPlayer()";
                            return true;
                        }
                    }
                }
            }
            catch { }

            try
            {
                Type characterType = this.FindLoadedType("XDTLevelAndEntity.Game.GameMode.Character", "Character");
                if (characterType != null)
                {
                    PropertyInfo characterProperty = characterType.GetProperty("character", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    object characterObj = characterProperty != null ? characterProperty.GetValue(null, null) : null;
                    if (characterObj != null)
                    {
                        if (this.TryGetObjectMember(characterObj, "player", out playerObj) && playerObj != null)
                        {
                            source = "Character.character.player";
                            return true;
                        }
                    }
                }
            }
            catch { }

            try
            {
                if (this.TryGetManagedViewModuleSelfPlayerObject(out playerObj, out source))
                {
                    return true;
                }
            }
            catch { }

            return false;
        }


        private bool TryGetManagedInteractPlayerObject(object interactSystemObj, out object playerObj, out string source)
        {
            playerObj = null;
            source = "none";
            if (interactSystemObj == null)
            {
                return false;
            }

            foreach (string memberName in new string[] { "player", "_interactor", "interactor" })
            {
                if (this.TryGetObjectMember(interactSystemObj, memberName, out playerObj) && playerObj != null)
                {
                    source = interactSystemObj.GetType().Name + "." + memberName;
                    return true;
                }
            }

            return false;
        }


















        private bool TryResolveNetIdFromManagedObject(object obj, out uint netId, out string source, int depth = 0)
        {
            netId = 0U;
            source = "none";
            if (obj == null || depth > 3)
            {
                return false;
            }

            foreach (string memberName in new string[] { "netId", "NetId", "ownerNetId", "entityNetId", "insectNetId", "mNetId", "_netId", "Id", "id", "Item1" })
            {
                if (this.TryGetUIntMember(obj, memberName, out netId) && netId != 0U)
                {
                    source = obj.GetType().Name + "." + memberName;
                    return true;
                }
            }

            foreach (string memberName in new string[] { "entity", "Entity", "_entity", "target", "Target", "Item1" })
            {
                if (this.TryGetObjectMember(obj, memberName, out object nestedObj) && nestedObj != null)
                {
                    if (this.TryResolveNetIdFromManagedObject(nestedObj, out netId, out string nestedSource, depth + 1))
                    {
                        source = obj.GetType().Name + "." + memberName + "->" + nestedSource;
                        return true;
                    }
                }
            }

            return false;
        }

        private bool TryResolvePositionFromManagedObject(object obj, out Vector3 position, int depth = 0)
        {
            position = Vector3.zero;
            if (obj == null || depth > 3)
            {
                return false;
            }

            foreach (string memberName in new string[] { "position", "pos", "Position", "Item2" })
            {
                if (this.TryGetObjectMember(obj, memberName, out object rawValue) && rawValue is Vector3 vector)
                {
                    position = vector;
                    return true;
                }
            }

            foreach (string memberName in new string[] { "entity", "Entity", "_entity", "transform", "_transform" })
            {
                if (this.TryGetObjectMember(obj, memberName, out object nestedObj) && nestedObj != null)
                {
                    if (nestedObj is Transform transform)
                    {
                        position = transform.position;
                        return true;
                    }

                    if (this.TryResolvePositionFromManagedObject(nestedObj, out position, depth + 1))
                    {
                        return true;
                    }
                }
            }

            return false;
        }







        private bool TryResolveNetIdFromGameObject(GameObject obj, out uint netId, out string source)
        {
            netId = 0U;
            source = "none";
            if (obj == null)
            {
                return false;
            }

            try
            {
                foreach (Component comp in obj.GetComponents<Component>())
                {
                    if (comp == null)
                    {
                        continue;
                    }

                    if (this.TryResolveNetIdFromComponent(comp, out netId, out source))
                    {
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                this.InsectFarmNetLog("TryResolveNetIdFromGameObject error on " + obj.name + ": " + ex.Message);
            }

            return false;
        }

        private bool TryResolveNetIdFromComponent(Component comp, out uint netId, out string source)
        {
            netId = 0U;
            source = "none";
            if (comp == null)
            {
                return false;
            }

            try
            {
                var ilType = comp.GetIl2CppType();
                if (ilType == null)
                {
                    return false;
                }

                string[] directMembers = new string[] { "netId", "NetId", "ownerNetId", "entityNetId", "insectNetId", "mNetId", "_netId", "Id", "id" };
                foreach (string member in directMembers)
                {
                    if (this.TryReadUIntMember(ilType, comp.TryCast<Il2CppObject>(), member, out netId))
                    {
                        source = ilType.Name + "." + member;
                        return true;
                    }
                }

                string[] entityMembers = new string[] { "entity", "Entity", "_entity", "ownerEntity", "targetEntity" };
                foreach (string member in entityMembers)
                {
                    if (this.TryReadObjectMember(ilType, comp.TryCast<Il2CppObject>(), member, out Il2CppObject nestedObj) && nestedObj != null)
                    {
                        if (this.TryResolveNetIdFromIl2CppObject(nestedObj, out netId, out string nestedSource))
                        {
                            source = ilType.Name + "." + member + "->" + nestedSource;
                            return true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                this.InsectFarmNetLog("TryResolveNetIdFromComponent error: " + ex.Message);
            }

            return false;
        }

        private bool TryResolveNetIdFromIl2CppObject(Il2CppObject obj, out uint netId, out string source)
        {
            netId = 0U;
            source = "none";
            if (obj == null)
            {
                return false;
            }

            try
            {
                var ilType = obj.GetIl2CppType();
                if (ilType == null)
                {
                    return false;
                }

                string[] members = new string[] { "netId", "NetId", "ownerNetId", "entityNetId", "insectNetId", "mNetId", "_netId", "Id", "id", "Item1" };
                foreach (string member in members)
                {
                    if (this.TryReadUIntMember(ilType, obj, member, out netId))
                    {
                        source = ilType.Name + "." + member;
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                this.InsectFarmNetLog("TryResolveNetIdFromIl2CppObject error: " + ex.Message);
            }

            return false;
        }




        private bool TryConvertToUInt(object rawValue, out uint value)
        {
            value = 0U;
            if (rawValue == null)
            {
                return false;
            }

            try
            {
                if (rawValue is uint uintValue)
                {
                    value = uintValue;
                    return value != 0U;
                }
                if (rawValue is int intValue)
                {
                    if (intValue > 0)
                    {
                        value = (uint)intValue;
                        return true;
                    }
                    return false;
                }

                string s = rawValue.ToString();
                if (string.IsNullOrEmpty(s))
                {
                    return false;
                }

                s = new string(s.Where(char.IsDigit).ToArray());
                if (uint.TryParse(s, out value))
                {
                    return value != 0U;
                }
            }
            catch { }

            return false;
        }

        private bool TryConvertToInt(object rawValue, out int value)
        {
            value = 0;
            if (rawValue == null)
            {
                return false;
            }

            try
            {
                if (rawValue is int intValue)
                {
                    value = intValue;
                    return value != 0;
                }
                if (rawValue is uint uintValue && uintValue <= int.MaxValue)
                {
                    value = (int)uintValue;
                    return value != 0;
                }

                string s = rawValue.ToString();
                if (string.IsNullOrEmpty(s))
                {
                    return false;
                }

                s = new string(s.Where(c => char.IsDigit(c) || c == '-').ToArray());
                if (int.TryParse(s, out value))
                {
                    return value != 0;
                }
            }
            catch { }

            return false;
        }
























        // ─────────────────────────────────────────────────────────────────
        // Warehouse Anywhere — off-home warehouse tab (AuraMono SetInteractable + Unity UI).
        // ─────────────────────────────────────────────────────────────────
        private bool warehouseMonoTabGiveUp;
        private float warehouseMonoTabNextAttemptAt = -999f;
        private bool warehouseMonoTabUnlockCommitted;
        private bool warehouseMonoTabUnlockedLogged;
        private bool warehouseMonoMoveButtonLogged;
        private IntPtr warehouseAuraBagPanelTypeObj;
        private int warehouseBagOpenBypassCacheFrame = -1;
        private bool warehouseBagOpenBypassCacheValue;

        private const int BagPanelLifeCycleOpening = 1;
        private const int BagPanelLifeCycleOpened = 2;
        private const int BagPanelLifeCycleClosing = 3;
        private const int BagPanelLifeCycleClosed = 4;




        private unsafe bool ModTryAuraMonoReadBoolProperty(IntPtr targetObj, string propertyName, out bool value)
        {
            value = false;
            if (targetObj == IntPtr.Zero || string.IsNullOrEmpty(propertyName) || auraMonoRuntimeInvoke == null || auraMonoObjectUnbox == null)
            {
                return false;
            }

            IntPtr targetClass = auraMonoObjectGetClass(targetObj);
            IntPtr getter = this.FindAuraMonoMethodOnHierarchy(targetClass, propertyName, 0);
            if (getter == IntPtr.Zero)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr boxed = auraMonoRuntimeInvoke(getter, targetObj, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || boxed == IntPtr.Zero || !this.TryUnboxMonoBoolean(boxed, out value))
            {
                return false;
            }

            return true;
        }














        // ─────────────────────────────────────────────────────────────────
        // Stranger Chat Bypass
        // Stranger bubbles are hidden by the hotfix ChatModule.ShowChatContent gate.
        // Use the same AuraMono resolver style as the other net-based features.
        // ─────────────────────────────────────────────────────────────────






























        private IEnumerable<Type> EnumerateLoadableTypes(Assembly assembly)
        {
            if (assembly == null)
            {
                yield break;
            }

            Type[] types = null;
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
                yield break;
            }

            if (types == null)
            {
                yield break;
            }

            foreach (Type type in types)
            {
                if (type != null)
                {
                    yield return type;
                }
            }
        }

        // Allow external modules to request a settings save (autosave helper)



        public GameObject GetPlayerObject()
        {
            return GetPlayer();
        }

        // Expose DirectClickInteractButton for external modules

        // Resource repair pause helpers for external modules



        // Public wrappers to allow other modules to trigger repair/eat flows


        private void EnsureUiPrimitiveTextures()
        {
            if (this.uiCircleTexture != null)
            {
                return;
            }

            int size = 32;
            Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            tex.wrapMode = TextureWrapMode.Clamp;
            tex.filterMode = FilterMode.Bilinear;
            float radius = (size - 1f) * 0.5f;
            Vector2 c = new Vector2(radius, radius);
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float d = Vector2.Distance(new Vector2(x, y), c);
                    float a = d <= radius ? 1f : 0f;
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                }
            }
            tex.Apply();
            this.uiCircleTexture = tex;
            this.themeTextures.Add(tex);
        }

        private void DrawCapsule(Rect rect, Color color)
        {
            this.EnsureUiPrimitiveTextures();
            float r = rect.height * 0.5f;
            Rect mid = new Rect(rect.x + r, rect.y, rect.width - 2f * r, rect.height);
            Rect left = new Rect(rect.x, rect.y, rect.height, rect.height);
            Rect right = new Rect(rect.xMax - rect.height, rect.y, rect.height, rect.height);
            GUI.color = color;
            GUI.DrawTexture(mid, Texture2D.whiteTexture);
            GUI.DrawTexture(left, this.uiCircleTexture);
            GUI.DrawTexture(right, this.uiCircleTexture);
            GUI.color = Color.white;
        }

    

        private void DrawQuickStatusPanel(Rect panelRect)
        {
            GUIStyle title = new GUIStyle(GUI.skin.label);
            title.fontSize = 14;
            title.fontStyle = FontStyle.Bold;
            title.normal.textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB);

            GUIStyle value = new GUIStyle(GUI.skin.label);
            value.fontSize = 13;
            value.normal.textColor = new Color(this.uiSubTabTextR, this.uiSubTabTextG, this.uiSubTabTextB);

            GUIStyle none = new GUIStyle(GUI.skin.label);
            none.fontSize = 12;
            none.fontStyle = FontStyle.Italic;
            none.normal.textColor = new Color(this.uiSubTabTextR, this.uiSubTabTextG, this.uiSubTabTextB, 0.55f);

            float x = panelRect.x + 14f;
            float w = panelRect.width - 28f;
            float y = panelRect.y + 46f;
            bool anyActive = false;

            // Helper to draw a feature row
            void Row(string label, string detail)
            {
                GUI.Label(new Rect(x, y, w, 22f), this.L(label), title);
                y += 19f;
                GUI.Label(new Rect(x, y, w, 20f), this.L(detail), value);
                y += 26f;
                anyActive = true;
            }

            if (this.isRadarActive)
                Row("Radar", "Active");

            if (this.autoFarmActive)
                Row("Foraging", this.GetForagingStatusDisplayText(false));

            if (this.autoCookEnabled)

            if (this.gameSpeed != 1.0f)
                Row("Speed", $"{this.gameSpeed:F1}x");

            if (this.noclipEnabled)
                Row("Noclip", "Active");

            if (this.bypassOverlapEnabled)
                Row("Bypass Overlap", "Active");

            if (this.birdVacuumEnabled)
                Row("Bird Vacuum", "Active");

            if (this.autoSnowEnabled)
                Row("Auto Snow", "Active");

            if (this.autoJoinFriendEnabled)
                Row("Auto Join Friend", "Active");

            if (InsectNetFarm.IsEnabled)
            {
                GUI.Label(new Rect(x, y, w, 22f), this.L("Insect Farm"), title);
                y += 19f;
                GUI.Label(new Rect(x, y, w, 20f), this.L(InsectNetFarm.GetLastStatus()), value);
                y += 18f;
                GUI.Label(new Rect(x, y, w, 20f), this.L("Tool") + ": " + this.L(InsectNetFarm.GetLastToolStatus()), value);
                y += 18f;
                GUI.Label(new Rect(x, y, w, 20f), this.L("Caught") + ": " + InsectNetFarm.GetSessionCatchCount().ToString(), value);
                y += 26f;
                anyActive = true;
            }

            if (BirdNetFarm.IsEnabled)
            {
                GUI.Label(new Rect(x, y, w, 22f), this.L("Bird Farm"), title);
                y += 19f;
                GUI.Label(new Rect(x, y, w, 20f), this.L(BirdNetFarm.GetLastStatus()), value);
                y += 18f;
                GUI.Label(new Rect(x, y, w, 20f), this.L("Tool") + ": " + this.L(BirdNetFarm.GetLastToolStatus()), value);
                y += 18f;
                GUI.Label(new Rect(x, y, w, 20f), this.L("Caught") + ": " + BirdNetFarm.GetSessionCatchCount().ToString(), value);
                y += 18f;
                GUI.Label(new Rect(x, y, w, 20f), this.L("Scared") + ": " + BirdNetFarm.GetSessionScaredCount().ToString(), value);
                y += 26f;
                anyActive = true;
            }

            if (!anyActive)
                GUI.Label(new Rect(x, y, w, 24f), this.L("No active features"), none);
        }









        private GameObject[] cachedFishShadowTargetObjects = null;
        private float nextFishShadowTargetObjectScanAt = -999f;
        private float nextFishShadowResolverMissLogAt = -999f;
        private string lastFishShadowResolverMissLogStatus = string.Empty;
        private readonly Dictionary<int, int> fishShadowPriorityByFishIdCache = new Dictionary<int, int>();
        private readonly Dictionary<int, string> fishShadowPrioritySourceByFishIdCache = new Dictionary<int, string>();




















        private bool TryInvokeStaticTableGetter(Type tableDataType, string methodName, int id, out object row)
        {
            row = null;
            if (tableDataType == null || string.IsNullOrEmpty(methodName) || id <= 0)
            {
                return false;
            }

            try
            {
                MethodInfo[] methods = tableDataType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                for (int i = 0; i < methods.Length; i++)
                {
                    MethodInfo method = methods[i];
                    if (!string.Equals(method.Name, methodName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length == 1 && parameters[0].ParameterType == typeof(int))
                    {
                        row = method.Invoke(null, new object[] { id });
                    }
                    else if (parameters.Length == 2 && parameters[0].ParameterType == typeof(int) && parameters[1].ParameterType == typeof(bool))
                    {
                        row = method.Invoke(null, new object[] { id, false });
                    }
                    else
                    {
                        continue;
                    }

                    return row != null;
                }
            }
            catch
            {
            }

            return false;
        }

        private bool TryReadObjectInt(object instance, string memberName, out int value)
        {
            value = 0;
            if (instance == null || string.IsNullOrEmpty(memberName))
            {
                return false;
            }

            try
            {
                if (this.TryGetObjectMember(instance, memberName, out object raw) && this.TryConvertToInt(raw, out value))
                {
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }





        private object TryInvokeManagedGetComponent(object entityObj, Type componentType)
        {
            if (entityObj == null || componentType == null)
            {
                return null;
            }

            try
            {
                foreach (MethodInfo method in entityObj.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (method == null || method.Name != "GetComponent" || !method.IsGenericMethodDefinition)
                    {
                        continue;
                    }

                    if (method.GetParameters().Length != 0)
                    {
                        continue;
                    }

                    return method.MakeGenericMethod(componentType).Invoke(entityObj, null);
                }
            }
            catch
            {
            }

            return null;
        }





























        private const int MeteorStarfallExchangeStoreId = 140;










        private MethodInfo FindTableLocalizationMethod(Type tableDataType)
        {
            if (tableDataType == null)
            {
                return null;
            }

            foreach (MethodInfo method in tableDataType.GetMethods(BindingFlags.Public | BindingFlags.Static))
            {
                if (!string.Equals(method.Name, "GetLocalizationText", StringComparison.Ordinal))
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length >= 1 && parameters[0].ParameterType == typeof(int))
                {
                    return method;
                }
            }

            return null;
        }


        private string TryReadObjectString(object obj, string memberName)
        {
            object valueObj;
            if (obj != null && !string.IsNullOrWhiteSpace(memberName) && this.TryGetObjectMember(obj, memberName, out valueObj) && valueObj is string)
            {
                return ((string)valueObj).Trim();
            }

            return string.Empty;
        }







        private unsafe string TryGetLocalizationTextMono(IntPtr tableDataClass, int localizationId)
        {
            if (tableDataClass == IntPtr.Zero || localizationId <= 0 || auraMonoRuntimeInvoke == null)
            {
                return string.Empty;
            }

            try
            {
                IntPtr methodPtr = this.FindAuraMonoMethodOnHierarchy(tableDataClass, "GetLocalizationText", 1);
                int argCount = 1;
                if (methodPtr == IntPtr.Zero)
                {
                    methodPtr = this.FindAuraMonoMethodOnHierarchy(tableDataClass, "GetLocalizationText", 2);
                    argCount = methodPtr != IntPtr.Zero ? 2 : 0;
                }

                if (methodPtr == IntPtr.Zero)
                {
                    return string.Empty;
                }

                IntPtr exc = IntPtr.Zero;
                int locIdArg = localizationId;
                bool needExceptionArg = false;
                if (argCount == 1)
                {
                    IntPtr* args = stackalloc IntPtr[1];
                    args[0] = (IntPtr)(&locIdArg);
                    IntPtr resultObj = auraMonoRuntimeInvoke(methodPtr, IntPtr.Zero, (IntPtr)args, ref exc);
                    if (exc == IntPtr.Zero && resultObj != IntPtr.Zero && this.TryReadMonoString(resultObj, out string value))
                    {
                        return value.Trim();
                    }
                }
                else if (argCount == 2)
                {
                    IntPtr* args = stackalloc IntPtr[2];
                    args[0] = (IntPtr)(&locIdArg);
                    args[1] = (IntPtr)(&needExceptionArg);
                    IntPtr resultObj = auraMonoRuntimeInvoke(methodPtr, IntPtr.Zero, (IntPtr)args, ref exc);
                    if (exc == IntPtr.Zero && resultObj != IntPtr.Zero && this.TryReadMonoString(resultObj, out string value))
                    {
                        return value.Trim();
                    }
                }
            }
            catch
            {
            }

            return string.Empty;
        }







        private unsafe bool TryOpenAuraPanelByTypeName(string fullTypeName, string successStatus)
        {
            if (this.TryOpenPanelByResolvedTypeName(fullTypeName, null, successStatus))
            {
                return true;
            }

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
                {
                    this.forceOpenShopStatus = "Aura mono runtime not ready.";
                    this.LogForceOpenShop(this.forceOpenShopStatus);
                    return false;
                }

                IntPtr uiManagerClass = this.FindAuraMonoClassByFullName("XDTGame.Core.UIManager");
                if (uiManagerClass == IntPtr.Zero)
                {
                    uiManagerClass = this.FindAuraMonoClassInImages(
                        "XDTGame.Core",
                        "UIManager",
                        new string[] { "XDTLevelAndEntity", "XDTLevelAndEntity.dll", "Client", "Client.dll" });
                }
                if (uiManagerClass == IntPtr.Zero)
                {
                    this.forceOpenShopStatus = "Aura UIManager class not found.";
                    this.LogForceOpenShop(this.forceOpenShopStatus);
                    return false;
                }

                IntPtr getInstanceMethod = this.FindAuraMonoMethodOnHierarchy(uiManagerClass, "get_Instance", 0);
                if (getInstanceMethod == IntPtr.Zero)
                {
                    this.forceOpenShopStatus = "Aura UIManager.Instance getter not found.";
                    this.LogForceOpenShop(this.forceOpenShopStatus);
                    return false;
                }

                IntPtr uiManagerObj = IntPtr.Zero;
                IntPtr exc = IntPtr.Zero;
                uiManagerObj = auraMonoRuntimeInvoke(getInstanceMethod, IntPtr.Zero, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero || uiManagerObj == IntPtr.Zero)
                {
                    this.forceOpenShopStatus = "Aura UIManager instance unavailable.";
                    this.LogForceOpenShop(this.forceOpenShopStatus);
                    return false;
                }

                if (!this.TryCreateAuraMonoSystemTypeObject(fullTypeName, out IntPtr typeObj) || typeObj == IntPtr.Zero)
                {
                    this.forceOpenShopStatus = "Aura panel Type object not found: " + fullTypeName;
                    this.LogForceOpenShop(this.forceOpenShopStatus);
                    return false;
                }

                IntPtr openViewMethod = this.FindAuraMonoMethodOnHierarchy(auraMonoObjectGetClass(uiManagerObj), "OpenView", 2);
                if (openViewMethod == IntPtr.Zero)
                {
                    this.forceOpenShopStatus = "Aura UIManager.OpenView(Type, Intent) not found.";
                    this.LogForceOpenShop(this.forceOpenShopStatus);
                    return false;
                }

                this.LogForceOpenShop("Invoking aura UIManager.OpenView for " + fullTypeName);
                IntPtr* args = stackalloc IntPtr[2];
                args[0] = typeObj;
                args[1] = IntPtr.Zero;
                exc = IntPtr.Zero;
                auraMonoRuntimeInvoke(openViewMethod, uiManagerObj, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    this.forceOpenShopStatus = "Aura UIManager.OpenView exception: " + fullTypeName;
                    this.LogForceOpenShop(this.forceOpenShopStatus);
                    return false;
                }

                this.forceOpenShopStatus = successStatus;
                this.LogForceOpenShop("Aura UIManager.OpenView succeeded: " + successStatus);
                return true;
            }
            catch (Exception ex)
            {
                this.forceOpenShopStatus = "Aura UIManager.OpenView failed: " + ex.Message;
                this.LogForceOpenShop("Aura panel open exception: " + ex);
                return false;
            }
        }

        private bool TryOpenPanelByResolvedTypeName(string fullTypeName, Action<object> configureIntent, string successStatus)
        {
            try
            {
                string shortName = fullTypeName;
                int lastDot = fullTypeName.LastIndexOf('.');
                if (lastDot >= 0 && lastDot < fullTypeName.Length - 1)
                {
                    shortName = fullTypeName.Substring(lastDot + 1);
                }

                Type panelType = this.FindLoadedType(fullTypeName, shortName);
                if (panelType == null)
                {
                    this.LogForceOpenShop("Managed panel type not found: " + fullTypeName);
                    return false;
                }

                if (!this.TryCreateUiIntent(out object intent, out _))
                {
                    return false;
                }

                configureIntent?.Invoke(intent);

                if (!this.TryGetUiManagerInstance(out object uiManager, out Type uiManagerType))
                {
                    return false;
                }

                MethodInfo openViewMethod = null;
                foreach (MethodInfo candidate in uiManagerType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!string.Equals(candidate.Name, "OpenView", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    ParameterInfo[] parameters = candidate.GetParameters();
                    if (parameters.Length == 2 && parameters[0].ParameterType == typeof(Type))
                    {
                        openViewMethod = candidate;
                        break;
                    }
                }

                if (openViewMethod == null)
                {
                    this.forceOpenShopStatus = "UIManager.OpenView(Type, Intent) not found.";
                    this.LogForceOpenShop(this.forceOpenShopStatus);
                    return false;
                }

                this.LogForceOpenShop("Opening managed panel type: " + panelType.FullName);
                openViewMethod.Invoke(uiManager, new object[] { panelType, intent });
                this.forceOpenShopStatus = successStatus;
                this.LogForceOpenShop("Managed panel open succeeded: " + successStatus);
                return true;
            }
            catch (Exception ex)
            {
                this.LogForceOpenShop("Managed panel open exception: " + ex);
                return false;
            }
        }

        private bool TryOpenPanelByRegistry(string panelName, Action<object> configureIntent, string successStatus)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(panelName))
                {
                    this.forceOpenShopStatus = "Panel name missing.";
                    this.LogForceOpenShop(this.forceOpenShopStatus);
                    return false;
                }

                if (!this.TryResolvePanelTypeByName(panelName, out Type panelType))
                {
                    return false;
                }

                if (!this.TryCreateUiIntent(out object intent, out Type intentType))
                {
                    return false;
                }

                configureIntent?.Invoke(intent);

                if (!this.TryGetUiManagerInstance(out object uiManager, out Type uiManagerType))
                {
                    return false;
                }

                MethodInfo openViewMethod = uiManagerType.GetMethod("OpenView", new Type[] { typeof(Type), intentType });
                if (openViewMethod == null)
                {
                    foreach (MethodInfo candidate in uiManagerType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                    {
                        if (!string.Equals(candidate.Name, "OpenView", StringComparison.Ordinal))
                        {
                            continue;
                        }

                        ParameterInfo[] parameters = candidate.GetParameters();
                        if (parameters.Length == 2 && parameters[0].ParameterType == typeof(Type))
                        {
                            openViewMethod = candidate;
                            break;
                        }
                    }
                }

                if (openViewMethod == null)
                {
                    this.forceOpenShopStatus = "UIManager.OpenView(Type, Intent) not found.";
                    this.LogForceOpenShop(this.forceOpenShopStatus);
                    return false;
                }

                this.LogForceOpenShop("Opening panel via UIManager registry path: " + panelType.FullName);
                openViewMethod.Invoke(uiManager, new object[] { panelType, intent });
                this.forceOpenShopStatus = successStatus;
                this.LogForceOpenShop("Registry open succeeded: " + successStatus);
                return true;
            }
            catch (Exception ex)
            {
                this.forceOpenShopStatus = "Open failed: " + ex.Message;
                this.LogForceOpenShop("Registry open exception: " + ex);
                return false;
            }
        }

        private bool TryResolvePanelTypeByName(string panelName, out Type panelType)
        {
            panelType = null;

            try
            {
                Type panelDefType = this.FindLoadedType("XDTGame.Auto.Manager.PanelDef", "PanelDef");
                if (panelDefType == null)
                {
                    this.forceOpenShopStatus = "PanelDef type not found.";
                    this.LogForceOpenShop(this.forceOpenShopStatus);
                    return false;
                }

                PropertyInfo instanceProperty = panelDefType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                object panelDefInstance = instanceProperty?.GetValue(null, null);
                if (panelDefInstance == null)
                {
                    this.forceOpenShopStatus = "PanelDef instance unavailable.";
                    this.LogForceOpenShop(this.forceOpenShopStatus);
                    return false;
                }

                MethodInfo resolveMethod = panelDefType.GetMethod("TryGetPanelTypeByName", BindingFlags.Public | BindingFlags.Instance);
                if (resolveMethod == null)
                {
                    this.forceOpenShopStatus = "PanelDef.TryGetPanelTypeByName missing.";
                    this.LogForceOpenShop(this.forceOpenShopStatus);
                    return false;
                }

                object[] invokeArgs = new object[] { panelName, null };
                bool found = false;
                object result = resolveMethod.Invoke(panelDefInstance, invokeArgs);
                if (result is bool ok)
                {
                    found = ok;
                }

                panelType = invokeArgs[1] as Type;
                if (!found || panelType == null)
                {
                    this.forceOpenShopStatus = "Panel '" + panelName + "' not found in PanelDef.";
                    this.LogForceOpenShop(this.forceOpenShopStatus);
                    return false;
                }

                this.LogForceOpenShop("Resolved panel '" + panelName + "' via PanelDef: " + panelType.FullName);
                return true;
            }
            catch (Exception ex)
            {
                this.forceOpenShopStatus = "Panel resolve failed: " + ex.Message;
                this.LogForceOpenShop("PanelDef resolve exception: " + ex);
                return false;
            }
        }

        private bool TryCreateUiIntent(out object intent, out Type intentType)
        {
            intent = null;
            intentType = this.FindLoadedType("XDTGame.Framework.UI.Intent", "XDTGame.Core.Intent", "Intent");
            if (intentType == null)
            {
                this.forceOpenShopStatus = "Intent type not found.";
                this.LogForceOpenShop(this.forceOpenShopStatus);
                return false;
            }

            try
            {
                MethodInfo getMethod = intentType.GetMethod("Get", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
                if (getMethod != null)
                {
                    intent = getMethod.Invoke(null, null);
                }

                if (intent == null)
                {
                    intent = Activator.CreateInstance(intentType);
                }

                if (intent == null)
                {
                    this.forceOpenShopStatus = "Intent instance unavailable.";
                    this.LogForceOpenShop(this.forceOpenShopStatus);
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                this.forceOpenShopStatus = "Intent creation failed: " + ex.Message;
                this.LogForceOpenShop("Intent creation exception: " + ex);
                return false;
            }
        }




        private bool TryInvokeIntentMethod(object intent, string methodName, object[] args)
        {
            if (intent == null)
            {
                this.LogForceOpenShop("Intent configure skipped because intent is null.");
                return false;
            }

            try
            {
                Type intentType = intent.GetType();
                MethodInfo method = null;
                foreach (MethodInfo candidate in intentType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!string.Equals(candidate.Name, methodName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    ParameterInfo[] parameters = candidate.GetParameters();
                    if (parameters.Length == (args?.Length ?? 0))
                    {
                        method = candidate;
                        break;
                    }
                }

                if (method == null)
                {
                    this.LogForceOpenShop("Intent method not found: " + methodName);
                    return false;
                }

                method.Invoke(intent, args);
                this.LogForceOpenShop("Intent configured via " + methodName + ".");
                return true;
            }
            catch (Exception ex)
            {
                this.LogForceOpenShop("Intent configure exception for " + methodName + ": " + ex.Message);
                return false;
            }
        }

        private bool TryInvokeStaticPanelOpen(string[] typeNames, string methodName, object[] args, string successStatus)
        {
            try
            {
                this.LogForceOpenShop("Resolving panel type for method '" + methodName + "' from candidates: " + string.Join(", ", typeNames ?? Array.Empty<string>()));
                Type panelType = this.FindLoadedType(typeNames);
                if (panelType == null)
                {
                    this.LogForceOpenShop("Managed type resolve failed. Trying IL2CPP type fallback.");
                    return this.TryInvokeIl2CppStaticPanelOpen(typeNames, methodName, args, successStatus);
                }

                this.LogForceOpenShop("Resolved panel type: " + panelType.FullName);
                MethodInfo[] methods = panelType.GetMethods(BindingFlags.Public | BindingFlags.Static);
                foreach (MethodInfo method in methods)
                {
                    if (method == null || !string.Equals(method.Name, methodName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length != args.Length)
                    {
                        this.LogForceOpenShop("Skipping method '" + method.Name + "' on " + panelType.Name + " due to parameter count mismatch. Expected " + args.Length + ", found " + parameters.Length + ".");
                        continue;
                    }

                    this.LogForceOpenShop("Invoking " + panelType.Name + "." + method.Name + "(" + args.Length + " args)");
                    method.Invoke(null, args);
                    this.forceOpenShopStatus = successStatus;
                    this.LogForceOpenShop("Invoke succeeded: " + successStatus);
                    return true;
                }

                this.forceOpenShopStatus = methodName + " not found on " + panelType.Name + ".";
                this.LogForceOpenShop(this.forceOpenShopStatus);
                this.LogForceOpenShop("Falling back to IL2CPP static resolve after managed method lookup failure.");
                return this.TryInvokeIl2CppStaticPanelOpen(typeNames, methodName, args, successStatus);
            }
            catch (Exception ex)
            {
                this.forceOpenShopStatus = "Open failed: " + ex.Message;
                this.LogForceOpenShop("Exception while opening panel: " + ex);
                return false;
            }
        }

        private bool TryInvokeIl2CppStaticPanelOpen(string[] typeNames, string methodName, object[] args, string successStatus)
        {
            try
            {
                Il2CppType il2CppType = this.TryGetNpcTeleportIl2CppType(typeNames);
                if (il2CppType == null)
                {
                    this.forceOpenShopStatus = "Panel type not found.";
                    this.LogForceOpenShop(this.forceOpenShopStatus + " IL2CPP fallback also failed.");
                    return false;
                }

                this.LogForceOpenShop("Resolved IL2CPP panel type: " + il2CppType.FullName);
                Il2CppMethodInfo method = il2CppType.GetMethod(methodName);
                if (method == null)
                {
                    this.forceOpenShopStatus = methodName + " not found on " + il2CppType.FullName + ".";
                    this.LogForceOpenShop(this.forceOpenShopStatus);
                    return false;
                }

                Il2CppReferenceArray<Il2CppObject> invokeArgs = this.BuildIl2CppInvokeArgs(args);
                this.LogForceOpenShop("Invoking IL2CPP " + il2CppType.Name + "." + methodName + "(" + (args == null ? 0 : args.Length) + " args)");
                method.Invoke(null, invokeArgs);
                this.forceOpenShopStatus = successStatus;
                this.LogForceOpenShop("IL2CPP invoke succeeded: " + successStatus);
                return true;
            }
            catch (Exception ex)
            {
                this.forceOpenShopStatus = "Open failed: " + ex.Message;
                this.LogForceOpenShop("IL2CPP fallback exception: " + ex);
                return false;
            }
        }






        private float GetStatusOverlayHeight()
        {
            int lineCount = 0;
            if (this.isRadarActive) lineCount++;
            if (this.gameSpeed != 1.0f) lineCount++;
            if (this.noclipEnabled) lineCount++;
            if (this.bypassOverlapEnabled) lineCount++;
            if (this.birdVacuumEnabled) lineCount++;
            if (this.autoFarmActive) lineCount += 2;
            else if (this.auraFarmEnabled) lineCount++;
            if (InsectNetFarm.IsEnabled) lineCount += 4;
            if (BirdNetFarm.IsEnabled) lineCount += 5;
            if (AutoFishingFarm.IsEnabled) lineCount += 4;
            if (this.autoSnowEnabled) lineCount++;
            if (this.autoJoinFriendEnabled) lineCount++;

            if (lineCount == 0)
            {
                return 124f;
            }

            return Mathf.Clamp(112f + (lineCount * 24f), 154f, 448f);
        }

        private float GetStatusOverlayWidth()
        {
            int maxTextLength = 0;

            void Consider(string text)
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    return;
                }

                maxTextLength = Math.Max(maxTextLength, text.Trim().Length);
            }

            if (this.isRadarActive) Consider("Active");
            if (this.gameSpeed != 1.0f) Consider(string.Format("{0:F1}x", this.gameSpeed));
            if (this.noclipEnabled) Consider("Active");
            if (this.bypassOverlapEnabled) Consider("Active");
            if (this.birdVacuumEnabled) Consider("Active");

            if (this.autoFarmActive)
            {
                Consider(this.GetForagingModeLabel());
                Consider(this.GetForagingStatusDisplayText(false));
            }
            else if (this.auraFarmEnabled)
            {
                Consider("Running");
            }

            if (InsectNetFarm.IsEnabled)
            {
                Consider(InsectNetFarm.GetLastStatus());
                Consider(InsectNetFarm.GetLastToolStatus());
                Consider(InsectNetFarm.GetSessionCatchCount().ToString());
            }
            if (BirdNetFarm.IsEnabled)
            {
                Consider(BirdNetFarm.GetLastStatus());
                Consider(BirdNetFarm.GetLastToolStatus());
                Consider(BirdNetFarm.GetSessionCatchCount().ToString());
                Consider(BirdNetFarm.GetSessionScaredCount().ToString());
            }
            if (AutoFishingFarm.IsEnabled)
            {
                Consider(AutoFishingFarm.GetLastStatus());
                Consider(AutoFishingFarm.GetLastToolStatus());
                Consider(AutoFishingFarm.GetLastTargetStatus());
            }
            if (BirdNetFarm.IsEnabled) Consider("Running");
            if (this.autoSnowEnabled) Consider("Active");
            if (this.autoJoinFriendEnabled) Consider("Active");

            if (maxTextLength <= 0)
            {
                return 228f;
            }

            float width = 228f + Mathf.Max(0f, (maxTextLength - 14) * 5.6f);
            return Mathf.Clamp(width, 228f, Mathf.Min(420f, Screen.width - 16f));
        }

        private void DrawStatusOverlay(Rect panelRect)
        {
            GUIStyle headerStyle = new GUIStyle(GUI.skin.label) { fontSize = 13, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false };
            GUIStyle badgeStyle = new GUIStyle(GUI.skin.label) { fontSize = 8, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter, wordWrap = false };
            GUIStyle sectionStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false };
            GUIStyle detailLabelStyle = new GUIStyle(GUI.skin.label) { fontSize = 10, alignment = TextAnchor.MiddleLeft, wordWrap = false };
            GUIStyle detailValueStyle = new GUIStyle(GUI.skin.label) { fontSize = 10, alignment = TextAnchor.MiddleRight, wordWrap = false };
            GUIStyle hintStyle = new GUIStyle(GUI.skin.label) { fontSize = 10, alignment = TextAnchor.MiddleLeft, wordWrap = false };
            GUIStyle footerLabelStyle = new GUIStyle(GUI.skin.label) { fontSize = 10, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleLeft, wordWrap = false };
            GUIStyle footerValueStyle = new GUIStyle(GUI.skin.label) { fontSize = 11, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleRight, wordWrap = false };

            Color textPrimary = new Color(this.uiTextR, this.uiTextG, this.uiTextB, 0.98f);
            Color textMuted = new Color(this.uiSubTabTextR, this.uiSubTabTextG, this.uiSubTabTextB, 0.88f);
            Color separator = new Color(1f, 1f, 1f, 0.06f);
            Color overlayFill = new Color(0.08f, 0.10f, 0.13f, 0.94f);
            Color overlayHeaderFill = new Color(0.10f, 0.12f, 0.17f, 0.98f);
            Color overlayFooterFill = new Color(0.07f, 0.08f, 0.11f, 0.98f);
            Color overlayBorder = new Color(1f, 1f, 1f, 0.07f);
            Color badgeFill = new Color(this.uiAccentR * 0.42f, this.uiAccentG * 0.42f, this.uiAccentB * 0.58f, 0.98f);
            Color badgeIdleFill = new Color(0.17f, 0.20f, 0.27f, 0.98f);

            headerStyle.normal.textColor = textPrimary;
            badgeStyle.normal.textColor = textPrimary;
            sectionStyle.normal.textColor = textPrimary;
            detailLabelStyle.normal.textColor = textMuted;
            detailValueStyle.normal.textColor = textPrimary;
            hintStyle.normal.textColor = textMuted;
            footerLabelStyle.normal.textColor = textMuted;
            footerValueStyle.normal.textColor = textPrimary;

            float x = panelRect.x;
            float y = panelRect.y;
            float w = panelRect.width;

            Color prevColor = GUI.color;

            int activeRows = 0;
            if (this.isRadarActive) activeRows++;
            if (this.gameSpeed != 1.0f) activeRows++;
            if (this.noclipEnabled) activeRows++;
            if (this.bypassOverlapEnabled) activeRows++;
            if (this.birdVacuumEnabled) activeRows++;
            if (this.autoFarmActive) activeRows++;
            else if (this.auraFarmEnabled) activeRows++;
            if (InsectNetFarm.IsEnabled) activeRows++;
            if (BirdNetFarm.IsEnabled) activeRows++;
            if (AutoFishingFarm.IsEnabled) activeRows++;
            if (this.autoSnowEnabled) activeRows++;
            if (this.autoJoinFriendEnabled) activeRows++;

            bool hasActiveSystems = activeRows > 0;
            Rect frameRect = new Rect(x - 6f, y - 6f, w + 12f, panelRect.height + 12f);
            float currentFps = Time.unscaledDeltaTime > 0.0001f ? (1f / Time.unscaledDeltaTime) : this.statusOverlaySmoothedFps;
            if (this.statusOverlaySmoothedFps <= 0f)
            {
                this.statusOverlaySmoothedFps = currentFps;
            }
            else if (currentFps > 0f)
            {
                this.statusOverlaySmoothedFps = Mathf.Lerp(this.statusOverlaySmoothedFps, currentFps, 0.05f);
            }
            if (Time.unscaledTime >= this.nextStatusOverlayFpsRefreshAt)
            {
                this.statusOverlayDisplayedFps = this.statusOverlaySmoothedFps;
                this.nextStatusOverlayFpsRefreshAt = Time.unscaledTime + 0.35f;
            }
            string fpsText = this.statusOverlayDisplayedFps > 0f ? Mathf.RoundToInt(this.statusOverlayDisplayedFps).ToString() : "--";

            this.DrawRoundedPanel(frameRect, 10f, overlayFill, overlayBorder, 1f, Color.clear);
            Rect headerRect = new Rect(frameRect.x + 1f, frameRect.y + 1f, frameRect.width - 2f, 34f);
            Rect footerRect = new Rect(frameRect.x + 1f, frameRect.yMax - 33f, frameRect.width - 2f, 32f);
            Rect bodyRect = new Rect(frameRect.x + 10f, headerRect.yMax + 8f, frameRect.width - 20f, footerRect.y - headerRect.yMax - 14f);

            this.DrawRoundedPanel(headerRect, 10f, overlayHeaderFill, Color.clear, 0f, Color.clear);
            GUI.color = separator;
            GUI.DrawTexture(new Rect(bodyRect.x, bodyRect.y - 4f, bodyRect.width, 1f), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.Label(new Rect(headerRect.x + 12f, headerRect.y + 7f, 116f, 18f), this.L("Helper Status"), headerStyle);

            Rect badgeRect = new Rect(headerRect.xMax - 82f, headerRect.y + 6f, 70f, 20f);
            this.DrawCapsule(badgeRect, hasActiveSystems ? badgeFill : badgeIdleFill);
            GUI.Label(badgeRect, hasActiveSystems ? this.L("ACTIVE") : this.L("STANDBY"), badgeStyle);

            float rowY = bodyRect.y;
            Action drawDivider = () =>
            {
                GUI.color = separator;
                GUI.DrawTexture(new Rect(bodyRect.x + 2f, rowY - 2f, bodyRect.width - 4f, 1f), Texture2D.whiteTexture);
                GUI.color = Color.white;
            };
            Action<string, string> drawFeature = (label, value) =>
            {
                Rect rowRect = new Rect(bodyRect.x, rowY, bodyRect.width, 18f);
                GUI.Label(new Rect(rowRect.x + 8f, rowRect.y + 1f, 112f, 16f), this.L(label), sectionStyle);
                GUI.Label(new Rect(rowRect.x + 120f, rowRect.y + 1f, rowRect.width - 128f, 16f), this.L(value), detailValueStyle);
                rowY += 20f;
            };
            Action<string, string> drawDetail = (label, value) =>
            {
                Rect rowRect = new Rect(bodyRect.x, rowY, bodyRect.width, 16f);
                GUI.Label(new Rect(rowRect.x + 18f, rowRect.y, 92f, 16f), this.L(label), detailLabelStyle);
                GUI.Label(new Rect(rowRect.x + 110f, rowRect.y, rowRect.width - 118f, 16f), this.L(value), detailValueStyle);
                rowY += 18f;
            };
            Action finishBlock = () =>
            {
                rowY += 4f;
                drawDivider();
                rowY += 6f;
            };

            if (!hasActiveSystems)
            {
                Rect idleRect = new Rect(bodyRect.x + 8f, bodyRect.y + 8f, bodyRect.width - 16f, 18f);
                GUI.Label(idleRect, this.L("All systems idle"), hintStyle);
            }
            else
            {
                if (this.isRadarActive)
                {
                    drawFeature("Radar", "Active");
                    finishBlock();
                }
                if (this.gameSpeed != 1.0f)
                {
                    drawFeature("Speed", string.Format("{0:F1}x", this.gameSpeed));
                    finishBlock();
                }
                if (this.noclipEnabled)
                {
                    drawFeature("Noclip", "Active");
                    finishBlock();
                }
                if (this.bypassOverlapEnabled)
                {
                    drawFeature("Bypass Overlap", "Active");
                    finishBlock();
                }
                if (this.birdVacuumEnabled)
                {
                    drawFeature("Bird Vacuum", "Active");
                    finishBlock();
                }
                if (this.autoFarmActive)
                {
                    drawFeature("Foraging", this.GetForagingModeLabel());
                    drawDetail("Status", this.GetForagingStatusDisplayText(false));
                    finishBlock();
                }
                else if (this.auraFarmEnabled)
                {
                    drawFeature("Aura Farm", "Running");
                    finishBlock();
                }
                if (InsectNetFarm.IsEnabled)
                {
                    drawFeature("Insect Farm", "Active");
                    drawDetail("Status", InsectNetFarm.GetLastStatus());
                    drawDetail("Tool", InsectNetFarm.GetLastToolStatus());
                    drawDetail("Caught", InsectNetFarm.GetSessionCatchCount().ToString());
                    finishBlock();
                }
                if (BirdNetFarm.IsEnabled)
                {
                    drawFeature("Bird Farm", "Active");
                    drawDetail("Status", BirdNetFarm.GetLastStatus());
                    drawDetail("Tool", BirdNetFarm.GetLastToolStatus());
                    drawDetail("Caught", BirdNetFarm.GetSessionCatchCount().ToString());
                    drawDetail("Scared", BirdNetFarm.GetSessionScaredCount().ToString());
                    finishBlock();
                }
                if (AutoFishingFarm.IsEnabled)
                {
                    drawFeature("Fishing Farm", "Active");
                    drawDetail("Status", AutoFishingFarm.GetLastStatus());
                    drawDetail("Tool", AutoFishingFarm.GetLastToolStatus());
                    drawDetail("Target", AutoFishingFarm.GetLastTargetStatus());
                    finishBlock();
                }

                if (this.autoSnowEnabled)
                {
                    drawFeature("Auto Snow", "Active");
                    finishBlock();
                }
                if (this.autoJoinFriendEnabled)
                {
                    drawFeature("Auto Join Friend", "Active");
                }
            }

            this.DrawRoundedPanel(footerRect, 10f, overlayFooterFill, Color.clear, 0f, Color.clear);
            GUI.color = separator;
            GUI.DrawTexture(new Rect(footerRect.x, footerRect.y, footerRect.width, 1f), Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(new Rect(footerRect.x + 12f, footerRect.y + 8f, 60f, 16f), this.L("FPS"), footerLabelStyle);
            GUI.Label(new Rect(footerRect.x + 72f, footerRect.y + 7f, footerRect.width - 84f, 18f), fpsText, footerValueStyle);

            GUI.color = prevColor;
        }


        private int birdMaxPhotoScareNotificationTotal = 0;






        private GameObject modClickBlockerOverlay;


        public static bool ShouldBlockGameplayInput()
        {
            HeartopiaComplete instance = HeartopiaComplete.Instance;
            return instance != null &&
                   ((instance.showMenu && instance.blockGameUiWhenMenuOpen) ||
                    Time.unscaledTime < instance.blockInputReleaseUntil);
        }

        // Blocks player movement while the mod menu is open (with "block game input" enabled).
        // The game does NOT move the local player via Unity's CharacterController.Move, so the
        // Harmony Move patch can't stop it — movement goes through MonoInputManager. We disable
        // the Move InputEvent there instead. Edge-triggered so the disable refcount stays balanced.


        private bool TryGetManagedViewModuleSelfPlayerObject(out object playerObj, out string source)
        {
            playerObj = null;
            source = "none";

            try
            {
                Type entityManagerType = this.FindLoadedType(
                    "XDTLevelAndEntity.BaseSystem.EntityManager",
                    "ScriptsRefactory.LevelAndEntity.BaseSystem.EntityManager",
                    "Il2CppXDTLevelAndEntity.BaseSystem.EntityManager",
                    "EntityManager");
                if (entityManagerType == null)
                {
                    return false;
                }

                PropertyInfo instanceProperty = entityManagerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                object entityManager = instanceProperty != null ? instanceProperty.GetValue(null, null) : null;
                if (entityManager == null)
                {
                    return false;
                }

                if (this.TryGetObjectMember(entityManager, "selfPlayer", out object selfPlayerObj) && selfPlayerObj != null)
                {
                    playerObj = selfPlayerObj;
                    source = "EntityManager.Instance.selfPlayer";
                    return true;
                }
            }
            catch { }

            return false;
        }


        private bool TryInvokeMethodByName(object target, string methodName, out object result, object[] args)
        {
            result = null;
            if (target == null || string.IsNullOrWhiteSpace(methodName))
            {
                return false;
            }

            try
            {
                MethodInfo[] methods = target.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                for (int i = 0; i < methods.Length; i++)
                {
                    MethodInfo method = methods[i];
                    if (method == null || !string.Equals(method.Name, methodName, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    ParameterInfo[] parameters = method.GetParameters();
                    if ((args == null ? 0 : args.Length) != parameters.Length)
                    {
                        continue;
                    }

                    result = method.Invoke(target, args);
                    return true;
                }
            }
            catch { }

            return false;
        }







        public static bool IsMenuToggleKey(KeyCode key)
        {
            HeartopiaComplete instance = HeartopiaComplete.Instance;
            return instance != null && key != KeyCode.None && key == instance.keyToggleMenu;
        }

        public static bool IsMenuToggleKeyName(string keyName)
        {
            HeartopiaComplete instance = HeartopiaComplete.Instance;
            if (instance == null || string.IsNullOrEmpty(keyName) || instance.keyToggleMenu == KeyCode.None)
            {
                return false;
            }

            return string.Equals(keyName.Trim(), instance.keyToggleMenu.ToString(), StringComparison.OrdinalIgnoreCase);
        }

        private List<(string label, Func<bool> isActive, Action setActive)> GetActiveTopSubTabs()
        {
            var tabs = new List<(string label, Func<bool> isActive, Action setActive)>();
            if (this.selectedTab == 0)
            {
                tabs.Add(("Main", () => this.selfSubTab == 0, () => this.SetSelfSubTab(0)));
                tabs.Add(("Building", () => this.selfSubTab == 1, () => this.SetSelfSubTab(1)));
            }
            else if (this.selectedTab == 2)
            {
                tabs.Add(("Foraging", () => this.autoFarmSubTab == 0, () => this.SetAutoFarmSubTab(0)));
                tabs.Add(("Chop & Mine", () => this.autoFarmSubTab == 1, () => this.SetAutoFarmSubTab(1)));
                tabs.Add(("Fishing", () => this.autoFarmSubTab == 2, () => this.SetAutoFarmSubTab(2)));
                tabs.Add(("Insects", () => this.autoFarmSubTab == 3, () => this.SetAutoFarmSubTab(3)));
                tabs.Add(("Birds", () => this.autoFarmSubTab == 4, () => this.SetAutoFarmSubTab(4)));
                // Auto Draw quick link removed
            }
            else if (this.selectedTab == 3)
            {
                tabs.Add(("Main", () => this.automationSubTab == 0, () => this.SetAutomationSubTab(0)));
                tabs.Add(("Food & Repair", () => this.automationSubTab == 1, () => this.SetAutomationSubTab(1)));
                tabs.Add(("Snow Sculpting", () => this.automationSubTab == 2, () => this.SetAutomationSubTab(2)));
                tabs.Add(("Auto Buy", () => this.automationSubTab == 3, () => this.SetAutomationSubTab(3)));
                tabs.Add(("Auto Sell", () => this.automationSubTab == 4, () => this.SetAutomationSubTab(4)));
                tabs.Add(("Mass Cook", () => this.automationSubTab == 5, () => this.SetAutomationSubTab(5)));
                tabs.Add(("Puzzle", () => this.automationSubTab == 6, () => this.SetAutomationSubTab(6)));
                tabs.Add(("Pet Care", () => this.automationSubTab == 7, () => this.SetAutomationSubTab(7)));
            }
            else if (this.selectedTab == 8)
            {
                tabs.Add(("Animal Care", () => this.newFeaturesSubTab == 0, () => this.SetNewFeaturesSubTab(0)));
                tabs.Add(("Daily Quests", () => this.newFeaturesSubTab == 1, () => this.SetNewFeaturesSubTab(1)));
                tabs.Add((this.L("homeland_farm.title"), () => this.newFeaturesSubTab == 2, () => this.SetNewFeaturesSubTab(2)));
                tabs.Add((this.L("pictures.title"), () => this.newFeaturesSubTab == 3, () => this.SetNewFeaturesSubTab(3)));
                tabs.Add(("Ice Skating", () => this.newFeaturesSubTab == 4, () => this.SetNewFeaturesSubTab(4)));
                tabs.Add(("Building", () => this.newFeaturesSubTab == 5, () => this.SetNewFeaturesSubTab(5)));
            }
            else if (this.selectedTab == 4)
            {
                tabs.Add(("Main", () => this.radarSubTab == 0, () => this.SetRadarSubTab(0)));
                tabs.Add(("Settings", () => this.radarSubTab == 1, () => this.SetRadarSubTab(1)));
            }
            else if (this.selectedTab == 5)
            {
                tabs.Add(("Home", () => this.teleportSubTab == 0, () => this.SetTeleportSubTab(0)));
                tabs.Add(("Animal Care", () => this.teleportSubTab == 1, () => this.SetTeleportSubTab(1)));
                tabs.Add(("NPCs", () => this.teleportSubTab == 2, () => this.SetTeleportSubTab(2)));
                tabs.Add(("Locations", () => this.teleportSubTab == 3, () => this.SetTeleportSubTab(3)));
                tabs.Add(("Events", () => this.teleportSubTab == 4, () => this.SetTeleportSubTab(4)));
                tabs.Add(("House", () => this.teleportSubTab == 5, () => this.SetTeleportSubTab(5)));
                tabs.Add(("Custom", () => this.teleportSubTab == 6, () => this.SetTeleportSubTab(6)));
                tabs.Add(("XYZ", () => this.teleportSubTab == 7, () => this.SetTeleportSubTab(7)));
            }
            else if (this.selectedTab == 6)
            {
                // No sub-tabs for Bag / Warehouse
            }
            else if (this.selectedTab == 7)
            {
                tabs.Add(("Main", () => this.settingsSubTab == 0, () => this.SetSettingsSubTab(0)));
                tabs.Add(("Keybinds", () => this.settingsSubTab == 1, () => this.SetSettingsSubTab(1)));
                tabs.Add(("UI Theme", () => this.settingsSubTab == 2, () => this.SetSettingsSubTab(2)));
            }
            return tabs;
        }







        // Refreshes the Transform instance ids the hot-path prefixes compare against. Runs once
        // per frame from OnUpdate; GetLocalPlayer/Camera.main are internally cached, so this is
        // far cheaper than the per-set gameObject.name fetches the prefixes used to do.
        private void UpdateHotPathOverrideTargetIds()
        {
            try
            {
                if (HeartopiaComplete.OverridePlayerPosition || HeartopiaComplete.OverridePlayerRotation || this.noclipEnabled)
                {
                    GameObject local = HeartopiaComplete.GetLocalPlayer();
                    HeartopiaComplete.OverridePlayerTransformId = local != null ? local.transform.GetInstanceID() : 0;
                }
                else
                {
                    HeartopiaComplete.OverridePlayerTransformId = 0;
                }

                if (HeartopiaComplete.OverrideCameraPosition || this.mouseLookEnabled)
                {
                    Camera cam = Camera.main;
                    HeartopiaComplete.OverrideCameraTransformId = cam != null ? cam.transform.GetInstanceID() : 0;
                }
                else
                {
                    HeartopiaComplete.OverrideCameraTransformId = 0;
                }
            }
            catch
            {
            }
        }

        // Circuit-breaker states for the per-frame farm ticks (see FeatureBreakerState).
        private FeatureBreakerState auraFarmBreaker;
        private FeatureBreakerState homelandFarmBreaker;
        private FeatureBreakerState birdNetFarmBreaker;
        private FeatureBreakerState insectNetFarmBreaker;
        private FeatureBreakerState puzzleNetBreaker;
        private FeatureBreakerState resourceFarmBreaker;
        private FeatureBreakerState autoFishingFarmBreaker;

        // Removes the hot-path Harmony patches once nothing has needed them for a while, so the
        // per-call prefix tax is not paid for the rest of the session after a one-off teleport.
        private const float HotPathPatchIdleUnpatchSeconds = 60f;
        private float positionOverridePatchLastNeededAt;
        private float rotationOverridePatchLastNeededAt;
        private float inputSimPatchLastNeededAt;

        private void MaybeUnpatchIdleHotPathPatches(float now)
        {
            if (this.positionOverridePatched && now - this.positionOverridePatchLastNeededAt > HotPathPatchIdleUnpatchSeconds)
            {
                this.UnpatchPositionOverride();
            }
            if (this.rotationOverridePatched && now - this.rotationOverridePatchLastNeededAt > HotPathPatchIdleUnpatchSeconds)
            {
                this.UnpatchRotationOverride();
            }
            if (this.inputSimPatched && now - this.inputSimPatchLastNeededAt > HotPathPatchIdleUnpatchSeconds)
            {
                this.UnpatchInputSim();
            }
        }

        private void UnpatchPositionOverride()
        {
            this.positionOverridePatched = false;
            try
            {
                var harmony = HeartopiaComplete.harmonyInstance;
                if (harmony == null) return;
                MethodInfo posSetter = typeof(Transform).GetProperty("position").GetSetMethod();
                MethodInfo posPrefix = typeof(TransformPositionPatch).GetMethod("SetPositionPrefix");
                if (posSetter != null && posPrefix != null)
                {
                    harmony.Unpatch(posSetter, posPrefix);
                }
                ModLogger.Msg("[Patch] Position override removed (idle).");
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[Patch] Position override unpatch failed: " + ex.Message);
            }
        }

        private void UnpatchRotationOverride()
        {
            this.rotationOverridePatched = false;
            try
            {
                var harmony = HeartopiaComplete.harmonyInstance;
                if (harmony == null) return;
                MethodInfo rotSetter = typeof(Transform).GetProperty("rotation").GetSetMethod();
                MethodInfo camRotPrefix = typeof(TransformRotationPatch).GetMethod("SetRotationPrefix");
                MethodInfo playerRotPrefix = typeof(CharacterRotationPatch).GetMethod("SetRotationPrefix");
                if (rotSetter != null && camRotPrefix != null)
                {
                    harmony.Unpatch(rotSetter, camRotPrefix);
                }
                if (rotSetter != null && playerRotPrefix != null)
                {
                    harmony.Unpatch(rotSetter, playerRotPrefix);
                }
                ModLogger.Msg("[Patch] Rotation override removed (idle).");
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[Patch] Rotation override unpatch failed: " + ex.Message);
            }
        }


        // Installs the Transform.position setter patch (player teleport/noclip pinning and
        // camera position override) on first use. This is the hotter of the movement patches,
        // so it is kept separate from the lighter CharacterController.Move patch below and is
        // NOT pulled in by the menu input-block (which only needs Move).
        private void EnsurePositionOverridePatched()
        {
            if (this.positionOverridePatched) return;
            this.positionOverridePatched = true; // set first so a failed attempt is not retried every frame
            try
            {
                var harmony = HeartopiaComplete.harmonyInstance;
                if (harmony == null) { this.positionOverridePatched = false; return; }

                MethodInfo posSetter = typeof(Transform).GetProperty("position").GetSetMethod();
                MethodInfo posPrefix = typeof(TransformPositionPatch).GetMethod("SetPositionPrefix");
                if (posSetter != null && posPrefix != null)
                {
                    harmony.Patch(posSetter, new HarmonyMethod(posPrefix), null, null, null, null);
                }

                ModLogger.Msg("[Patch] Position override installed (Transform.position setter).");
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[Patch] Position override patch failed: " + ex.Message);
            }
        }

        // Installs the rotation patches (Transform.rotation setter, used by both the camera
        // mouse-look override and the player-rotation override) on first use.
        private void EnsureRotationOverridePatched()
        {
            if (this.rotationOverridePatched) return;
            this.rotationOverridePatched = true;
            try
            {
                var harmony = HeartopiaComplete.harmonyInstance;
                if (harmony == null) { this.rotationOverridePatched = false; return; }

                MethodInfo rotSetter = typeof(Transform).GetProperty("rotation").GetSetMethod();
                MethodInfo camRotPrefix = typeof(TransformRotationPatch).GetMethod("SetRotationPrefix");
                MethodInfo playerRotPrefix = typeof(CharacterRotationPatch).GetMethod("SetRotationPrefix");
                if (rotSetter != null && camRotPrefix != null)
                {
                    harmony.Patch(rotSetter, new HarmonyMethod(camRotPrefix), null, null, null, null);
                }
                if (rotSetter != null && playerRotPrefix != null)
                {
                    harmony.Patch(rotSetter, new HarmonyMethod(playerRotPrefix), null, null, null, null);
                }

                ModLogger.Msg("[Patch] Rotation override installed (Transform.rotation setter).");
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[Patch] Rotation override patch failed: " + ex.Message);
            }
        }

        // Installs the Input.GetKey* postfixes used for simulated F-key presses (fishing,
        // insect net, auto-cook interact) on first use.

        private void EnsureBypassPatched()
        {
            if (this.bypassOverlapPatched) return;

            var t = System.AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("XDT.Physics.PhysicsManager") ?? a.GetType("Il2CppXDT.Physics.PhysicsManager"))
                .FirstOrDefault(x => x != null);

            if (t != null)
            {
                try
                {
                    this.bypassHarmony = new HarmonyLib.Harmony("HeartopiaMod.bypass");
                    var p = new HarmonyMethod(typeof(HeartopiaComplete).GetMethod(nameof(BypassPrefix), System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public));
                    string[] methods = { "OverlapBoxNonAlloc", "OverlapSphereNonAlloc" };
                    foreach (var m in t.GetMethods().Where(x => methods.Contains(x.Name))) this.bypassHarmony.Patch(m, p);
                    this.bypassOverlapPatched = true;
                    ModLogger.Msg("Bypass overlap patch applied.");
                }
                catch (Exception ex)
                {
                    ModLogger.Msg("Bypass patch failed: " + ex.Message);
                }
            }
        }

        private static bool BypassPrefix(ref int __result)
        {
            if (bypassOverlapEnabledStatic)
            {
                __result = 0;
                return false;
            }
            return true;
        }







        // Auto Draw tab removed

        // Insect farm UI lives in InsectNetFarm.cs


























































        private object CreateCompatibleUIntList(Type listType, List<uint> values)
        {
            values = values ?? new List<uint>(0);
            if (listType == null || listType.IsAssignableFrom(typeof(List<uint>)))
            {
                return new List<uint>(values);
            }

            object listObj = Activator.CreateInstance(listType);
            MethodInfo addMethod = listType.GetMethod("Add", BindingFlags.Instance | BindingFlags.Public, null, new Type[] { typeof(uint) }, null);
            if (addMethod == null)
            {
                return listObj;
            }

            for (int i = 0; i < values.Count; i++)
            {
                addMethod.Invoke(listObj, new object[] { values[i] });
            }

            return listObj;
        }




        private PropertyInfo GetDataModuleInstanceProperty(Type moduleType)
        {
            if (moduleType == null || !moduleType.IsClass)
            {
                return null;
            }

            try
            {
                if (this.cachedDataModuleOpenGenericType == null)
                {
                    foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                    {
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

                        foreach (Type type in types)
                        {
                            if (type == null || !type.IsGenericTypeDefinition || type.Name != "DataModule`1")
                            {
                                continue;
                            }

                            PropertyInfo instanceProperty = type.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                            if (instanceProperty != null)
                            {
                                this.cachedDataModuleOpenGenericType = type;
                                break;
                            }
                        }

                        if (this.cachedDataModuleOpenGenericType != null)
                        {
                            break;
                        }
                    }
                }

                if (this.cachedDataModuleOpenGenericType == null)
                {
                    return null;
                }

                Type closedDataModuleType = this.cachedDataModuleOpenGenericType.MakeGenericType(moduleType);
                return closedDataModuleType.GetProperty("Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            }
            catch
            {
                return null;
            }
        }
























        private bool TryEnumerateManagedCollectionItems(object collectionObj, List<object> items)
        {
            if (collectionObj == null || items == null)
            {
                return false;
            }

            items.Clear();

            if (collectionObj is IEnumerable enumerable)
            {
                foreach (object item in enumerable)
                {
                    items.Add(item);
                }

                return true;
            }

            try
            {
                Type collectionType = collectionObj.GetType();
                MethodInfo getCountMethod = collectionType.GetMethod("get_Count", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                MethodInfo getItemMethod = collectionType.GetMethod("get_Item", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (getCountMethod == null || getItemMethod == null)
                {
                    return false;
                }

                int count = Convert.ToInt32(getCountMethod.Invoke(collectionObj, null));
                for (int i = 0; i < count; i++)
                {
                    items.Add(getItemMethod.Invoke(collectionObj, new object[] { i }));
                }

                return true;
            }
            catch
            {
                return false;
            }
        }





























        private IntPtr TryResolveAuraMonoNetworkClientClass()
        {
            if (this.cachedBirdPhotoNetworkClientMonoClass != IntPtr.Zero)
            {
                return this.cachedBirdPhotoNetworkClientMonoClass;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoClassFromName == null)
            {
                return IntPtr.Zero;
            }

            IntPtr ecsImage = this.FindAuraMonoImage(new string[] { "EcsClient", "EcsClient.dll" });
            if (ecsImage != IntPtr.Zero)
            {
                this.cachedBirdPhotoNetworkClientMonoClass = auraMonoClassFromName(ecsImage, "XD.GameGerm.Ecs.Boost.Client", "NetworkClient");
            }

            if (this.cachedBirdPhotoNetworkClientMonoClass == IntPtr.Zero)
            {
                this.cachedBirdPhotoNetworkClientMonoClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XD.GameGerm.Ecs.Boost.Client", "NetworkClient");
            }

            return this.cachedBirdPhotoNetworkClientMonoClass;
        }





        private bool TryConvertObjectToIntPtr(object value, out IntPtr pointer)
        {
            pointer = IntPtr.Zero;
            if (value == null)
            {
                return false;
            }

            if (value is IntPtr intPtr)
            {
                pointer = intPtr;
                return pointer != IntPtr.Zero;
            }

            try
            {
                if (value is long longValue)
                {
                    pointer = new IntPtr(longValue);
                    return pointer != IntPtr.Zero;
                }
                if (value is ulong ulongValue && ulongValue <= long.MaxValue)
                {
                    pointer = new IntPtr((long)ulongValue);
                    return pointer != IntPtr.Zero;
                }
                if (value is int intValue)
                {
                    pointer = new IntPtr(intValue);
                    return pointer != IntPtr.Zero;
                }
                if (value is uint uintValue)
                {
                    pointer = new IntPtr((long)uintValue);
                    return pointer != IntPtr.Zero;
                }
            }
            catch
            {
            }

            return false;
        }



        private bool TryInvokeManagedNetIdMethod(object obj, string methodName, out uint value)
        {
            value = 0U;
            object raw = this.TryInvokeManagedObjectMethod(obj, methodName);
            return this.TryConvertManagedNetIdToUInt32(raw, out value);
        }

        private object TryInvokeManagedObjectMethod(object obj, string methodName)
        {
            if (obj == null || string.IsNullOrEmpty(methodName))
            {
                return null;
            }

            try
            {
                MethodInfo method = obj.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                return method?.Invoke(obj, null);
            }
            catch
            {
                return null;
            }
        }

        private bool TryConvertManagedNetIdToUInt32(object raw, out uint value)
        {
            value = 0U;
            if (raw == null)
            {
                return false;
            }

            try
            {
                value = Convert.ToUInt32(raw);
                return value != 0U;
            }
            catch
            {
            }

            string[] valueMemberNames = { "value", "Value", "id", "Id", "_value", "m_Value" };
            for (int i = 0; i < valueMemberNames.Length; i++)
            {
                object innerValue = this.TryGetManagedMemberValue(raw, valueMemberNames[i]);
                if (innerValue == null || ReferenceEquals(innerValue, raw))
                {
                    continue;
                }

                try
                {
                    value = Convert.ToUInt32(innerValue);
                    return value != 0U;
                }
                catch
                {
                }
            }

            string text = raw.ToString();
            if (!string.IsNullOrEmpty(text))
            {
                string digits = new string(text.Where(char.IsDigit).ToArray());
                if (!string.IsNullOrEmpty(digits) && uint.TryParse(digits, out value))
                {
                    return value != 0U;
                }
            }

            return false;
        }

        private bool TryConvertManagedNetIdToUInt64(object raw, out ulong value)
        {
            value = 0UL;
            if (raw == null)
            {
                return false;
            }

            try
            {
                value = Convert.ToUInt64(raw);
                return value != 0UL;
            }
            catch
            {
            }

            string[] valueMemberNames = { "value", "Value", "id", "Id", "_value", "m_Value" };
            for (int i = 0; i < valueMemberNames.Length; i++)
            {
                object innerValue = this.TryGetManagedMemberValue(raw, valueMemberNames[i]);
                if (innerValue == null || ReferenceEquals(innerValue, raw))
                {
                    continue;
                }

                try
                {
                    value = Convert.ToUInt64(innerValue);
                    return value != 0UL;
                }
                catch
                {
                }
            }

            string text = raw.ToString();
            if (!string.IsNullOrEmpty(text))
            {
                string digits = new string(text.Where(char.IsDigit).ToArray());
                if (!string.IsNullOrEmpty(digits) && ulong.TryParse(digits, out value))
                {
                    return value != 0UL;
                }
            }

            return false;
        }























































































        private bool IsLikelyWorldMiscTarget(GameObject obj)
        {
            if (obj == null || !obj.activeInHierarchy)
            {
                return false;
            }

            Renderer r = obj.GetComponent<Renderer>();
            if (r != null)
            {
                return true;
            }

            return obj.GetComponentInChildren<Renderer>(true) != null;
        }




        private bool HasChildComponentNamed(Transform root, string componentTypeName)
        {
            if (root == null || string.IsNullOrEmpty(componentTypeName))
            {
                return false;
            }

            try
            {
                int childCount = root.childCount;
                for (int i = 0; i < childCount; i++)
                {
                    Transform child = root.GetChild(i);
                    if (child == null)
                    {
                        continue;
                    }

                    Component[] components = child.GetComponents<Component>();
                    if (components != null)
                    {
                        for (int j = 0; j < components.Length; j++)
                        {
                            Component component = components[j];
                            if (component != null && string.Equals(component.GetType().Name, componentTypeName, StringComparison.Ordinal))
                            {
                                return true;
                            }
                        }
                    }

                    if (this.HasChildComponentNamed(child, componentTypeName))
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


        private Component FindChildComponentByName(Transform root, string componentTypeName)
        {
            if (root == null || string.IsNullOrEmpty(componentTypeName))
            {
                return null;
            }

            try
            {
                int childCount = root.childCount;
                for (int i = 0; i < childCount; i++)
                {
                    Transform child = root.GetChild(i);
                    if (child == null)
                    {
                        continue;
                    }

                    Component[] components = child.GetComponents<Component>();
                    if (components != null)
                    {
                        for (int j = 0; j < components.Length; j++)
                        {
                            Component component = components[j];
                            if (component != null && string.Equals(component.GetType().Name, componentTypeName, StringComparison.Ordinal))
                            {
                                return component;
                            }
                        }
                    }

                    Component nested = this.FindChildComponentByName(child, componentTypeName);
                    if (nested != null)
                    {
                        return nested;
                    }
                }
            }
            catch
            {
            }

            return null;
        }
























        private bool IsLocalPlayerSkeletonGameObject(GameObject obj)
        {
            if (obj == null)
            {
                return false;
            }

            string name = obj.name;
            if (string.IsNullOrEmpty(name) || !name.Contains("p_player_skeleton"))
            {
                return false;
            }

            GameObject localPlayer = GetLocalPlayer();
            if (localPlayer == null)
            {
                return false;
            }

            if (ReferenceEquals(obj, localPlayer))
            {
                return true;
            }

            if (obj.GetInstanceID() == localPlayer.GetInstanceID())
            {
                return true;
            }

            try
            {
                if (obj.transform != null && localPlayer.transform != null
                    && obj.transform.GetInstanceID() == localPlayer.transform.GetInstanceID())
                {
                    return true;
                }
            }
            catch
            {
            }

            try
            {
                if ((obj.transform.position - localPlayer.transform.position).sqrMagnitude < 0.04f)
                {
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private bool IsOtherPlayerSkeletonGameObject(GameObject obj)
        {
            if (obj == null || !obj.activeInHierarchy)
            {
                return false;
            }

            if (this.IsLocalPlayerSkeletonGameObject(obj))
            {
                return false;
            }

            string name = obj.name;
            return !string.IsNullOrEmpty(name) && name.Contains("p_player_skeleton");
        }


        private struct HideAndSeekMorphRadarSpot
        {
            public uint MarkerNetId;
            public Vector3 Position;
        }



























        // Token: 0x0600000F RID: 15 RVA: 0x00003A9C File Offset: 0x00001C9C
        private void CleanupExpiredCooldowns()
        {
            float now = Time.unscaledTime;
            bool radarNeedsRefresh = false;
            this.expiredBerryCooldownBuffer.Clear();
            foreach (KeyValuePair<int, float> keyValuePair in this.blueberryCooldowns)
            {
                bool flag = now >= keyValuePair.Value;
                if (flag)
                {
                    this.expiredBerryCooldownBuffer.Add(keyValuePair.Key);
                }
            }
            for (int i = 0; i < this.expiredBerryCooldownBuffer.Count; i++)
            {
                int key = this.expiredBerryCooldownBuffer[i];
                this.blueberryCooldowns.Remove(key);
                this.blueberryHideUntil.Remove(key);
                this.blueberryJustCollected.Remove(key);
                radarNeedsRefresh = true;
            }
            this.expiredBerryCooldownBuffer.Clear();
            foreach (KeyValuePair<int, float> keyValuePair2 in this.raspberryCooldowns)
            {
                bool flag2 = now >= keyValuePair2.Value;
                if (flag2)
                {
                    this.expiredBerryCooldownBuffer.Add(keyValuePair2.Key);
                }
            }
            for (int j = 0; j < this.expiredBerryCooldownBuffer.Count; j++)
            {
                int key2 = this.expiredBerryCooldownBuffer[j];
                this.raspberryCooldowns.Remove(key2);
                this.raspberryHideUntil.Remove(key2);
                this.raspberryJustCollected.Remove(key2);
                radarNeedsRefresh = true;
            }
            this.expiredBerryCooldownBuffer.Clear();
            if (radarNeedsRefresh)
            {
                this.RunRadar();
            }
        }

        // Token: 0x06000010 RID: 16 RVA: 0x00003C60 File Offset: 0x00001E60
        private void RunAutoCollectLogic()
        {
            GameObject gameObject = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)/tracking_bar@w/tracking_common@list/IconsBarWidget(Clone)/root_visible@go/cells@t/cells@list/CommonIconForInteract(Clone)/root_visible@go/icon@img@btn");
            bool flag = gameObject == null || !gameObject.activeInHierarchy;
            if (!flag)
            {
                Image component = gameObject.GetComponent<Image>();
                bool flag2 = component == null || component.sprite == null;
                if (!flag2)
                {
                    string text = component.sprite.name.ToLower();
                    if (this.ShouldAutoCollectBySprite(text))
                    {
                        Button component2 = gameObject.GetComponent<Button>();
                        bool flag3 = component2 != null && component2.interactable;
                        if (flag3)
                        {
                            component2.onClick.Invoke();
                            this.autoCollectClickedSinceArrival = true;
                            this.TryMarkNearestNodeCollectedFromPrompt();
                            this.ClickButtonIfExists("GameApp/startup_root(Clone)/XDUIRoot/Scene/DialoguePanel(Clone)/AniRoot@go@ani/exit@btn@go");
                        }
                    }
                }
            }
        }


        private bool TryGetLocalPlayerPosition(out Vector3 playerPos)
        {
            playerPos = Vector3.zero;
            try
            {
                GameObject player = this.FindPlayerRoot();
                if (player != null)
                {
                    playerPos = player.transform.position;
                    return true;
                }
            }
            catch
            {
            }

            Camera cam = Camera.main;
            if (cam != null)
            {
                playerPos = cam.transform.position;
                return true;
            }

            return false;
        }

        private bool IsPromptButtonReady(string path)
        {
            GameObject buttonObj = GameObject.Find(path);
            if (buttonObj == null || !buttonObj.activeInHierarchy)
            {
                return false;
            }

            Button btn = buttonObj.GetComponent<Button>();
            return btn != null && btn.interactable;
        }

        private void MarkNearestCooldownEntry(Vector3 playerPos,
            Vector3[][] positionSets,
            Dictionary<int, float>[] cooldownSets,
            Dictionary<int, float>[] hideSets,
            float[] cooldownDurations,
            string[] labels,
            bool[] enabledSets)
        {
            int bestGroup = -1;
            int bestIndex = -1;
            float bestSqr = 25f;

            for (int group = 0; group < positionSets.Length; group++)
            {
                if (!enabledSets[group])
                {
                    continue;
                }

                int idx = this.FindClosestItemIndexLocal(playerPos, positionSets[group]);
                if (idx < 0)
                {
                    continue;
                }

                float sqr = (positionSets[group][idx] - playerPos).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    bestGroup = group;
                    bestIndex = idx;
                }
            }

            if (bestGroup < 0 || bestIndex < 0)
            {
                return;
            }

            float now = Time.unscaledTime;
            cooldownSets[bestGroup][bestIndex] = now + cooldownDurations[bestGroup];
            hideSets[bestGroup][bestIndex] = now + 10f;
            ModLogger.Msg($"[AutoFarm] {labels[bestGroup]} #{bestIndex} marked collected; cooldown {cooldownDurations[bestGroup]:F1}s");
        }

        private bool HasReadyAutoCollectPrompt()
        {
            string[] paths = new string[]
            {
                "GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)/tracking_bar@w/tracking_chop@list/IconsBarWidget(Clone)/root_visible@go/cells@t/cells@list/CommonIconForInteract(Clone)/root_visible@go/icon@img@btn",
                "GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)/tracking_bar@w/tracking_mine@list/IconsBarWidget(Clone)/root_visible@go/cells@t/cells@list/CommonIconForInteract(Clone)/root_visible@go/icon@img@btn",
                "GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)/tracking_bar@w/tracking_common@list/IconsBarWidget(Clone)/root_visible@go/cells@t/cells@list/CommonIconForInteract(Clone)/root_visible@go/icon@img@btn",
                "GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)/tracking_bar@w/tracking_harvest@list/IconsBarWidget(Clone)/root_visible@go/cells@t/cells@list/CommonIconForInteract(Clone)/root_visible@go/icon@img@btn"
            };

            for (int i = 0; i < paths.Length; i++)
            {
                if (this.IsReadyAutoCollectPromptButton(GameObject.Find(paths[i])))
                {
                    return true;
                }
            }

            try
            {
                GameObject trackingPanel = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)");
                if (trackingPanel != null && trackingPanel.activeInHierarchy)
                {
                    Button[] buttons = trackingPanel.GetComponentsInChildren<Button>(true);
                    if (buttons != null)
                    {
                        foreach (Button btn in buttons)
                        {
                            if (btn == null || btn.gameObject == null)
                            {
                                continue;
                            }

                            string fullPath = this.GetHierarchyPath(btn.transform);
                            if (string.IsNullOrEmpty(fullPath) ||
                                !fullPath.Contains("/CommonIconForInteract(Clone)/root_visible@go/icon@img@btn"))
                            {
                                continue;
                            }

                            if (this.IsReadyAutoCollectPromptButton(btn.gameObject))
                            {
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

        // Checks if ANY interact prompt button is visible in the tracking panel,
        // regardless of whether its resource type is enabled for auto-collect.
        // Used for camera-stuck detection only.
        private bool HasAnyVisibleInteractPrompt()
        {
            string[] paths = new string[]
            {
                "GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)/tracking_bar@w/tracking_chop@list/IconsBarWidget(Clone)/root_visible@go/cells@t/cells@list/CommonIconForInteract(Clone)/root_visible@go/icon@img@btn",
                "GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)/tracking_bar@w/tracking_mine@list/IconsBarWidget(Clone)/root_visible@go/cells@t/cells@list/CommonIconForInteract(Clone)/root_visible@go/icon@img@btn",
                "GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)/tracking_bar@w/tracking_common@list/IconsBarWidget(Clone)/root_visible@go/cells@t/cells@list/CommonIconForInteract(Clone)/root_visible@go/icon@img@btn",
                "GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)/tracking_bar@w/tracking_harvest@list/IconsBarWidget(Clone)/root_visible@go/cells@t/cells@list/CommonIconForInteract(Clone)/root_visible@go/icon@img@btn"
            };

            for (int i = 0; i < paths.Length; i++)
            {
                GameObject go = GameObject.Find(paths[i]);
                if (go != null && go.activeInHierarchy)
                {
                    Image img = go.GetComponent<Image>();
                    if (img != null && img.sprite != null)
                        return true;
                }
            }

            try
            {
                GameObject trackingPanel = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)");
                if (trackingPanel != null && trackingPanel.activeInHierarchy)
                {
                    Image[] images = trackingPanel.GetComponentsInChildren<Image>(true);
                    if (images != null)
                    {
                        foreach (Image img in images)
                        {
                            if (img == null || img.gameObject == null || !img.gameObject.activeInHierarchy || img.sprite == null)
                                continue;
                            string fullPath = this.GetHierarchyPath(img.transform);
                            if (!string.IsNullOrEmpty(fullPath) &&
                                fullPath.Contains("/CommonIconForInteract(Clone)/root_visible@go/icon@img@btn"))
                                return true;
                        }
                    }
                }
            }
            catch { }

            return false;
        }

        private bool IsReadyAutoCollectPromptButton(GameObject gameObject)
        {
            if (gameObject == null || !gameObject.activeInHierarchy)
            {
                return false;
            }

            Image component = gameObject.GetComponent<Image>();
            if (component == null || component.sprite == null)
            {
                return false;
            }

            string text = component.sprite.name.ToLowerInvariant();
            if (!this.ShouldAutoCollectBySprite(text))
            {
                return false;
            }

            Button component2 = gameObject.GetComponent<Button>();
            return component2 != null && component2.interactable;
        }

        private void DebugLogCurrentInteractSprite()
        {
            if (Time.unscaledTime < this.nextInteractSpriteDebugAt)
            {
                return;
            }

            this.nextInteractSpriteDebugAt = Time.unscaledTime + 0.2f;

            GameObject interactObj = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)/tracking_bar@w/tracking_common@list/IconsBarWidget(Clone)/root_visible@go/cells@t/cells@list/CommonIconForInteract(Clone)/root_visible@go/icon@img@btn");
            if (interactObj == null || !interactObj.activeInHierarchy)
            {
                return;
            }

            Image image = interactObj.GetComponent<Image>();
            if (image == null || image.sprite == null)
            {
                return;
            }

            string spriteName = image.sprite.name ?? string.Empty;
            if (string.IsNullOrEmpty(spriteName) || spriteName == this.lastLoggedInteractSpriteName)
            {
                return;
            }

            this.lastLoggedInteractSpriteName = spriteName;
            ModLogger.Msg("[AutoCollectDebug] Interact sprite: " + spriteName);
        }

        private bool ShouldAutoCollectBySprite(string spriteName)
        {
            if (string.IsNullOrEmpty(spriteName))
            {
                return false;
            }

            string text = spriteName.ToLowerInvariant();
            if (text.Contains("mushroom"))
            {
                return this.collectMushrooms;
            }

            if (text.Contains("interaction_8"))
            {
                string nearestLabel = this.GetNearestRadarNodeLabel(6f);
                if (!string.IsNullOrEmpty(nearestLabel))
                {
                    if (nearestLabel.Contains("Fiddlehead") || nearestLabel.Contains("Tall Mustard") || nearestLabel.Contains("Burdock") || nearestLabel.Contains("Mustard Greens"))
                    {
                        return this.collectEventResources;
                    }
                }

                return this.collectBerries;
            }

            if (text.Contains("wildvegetables"))
            {
                return this.collectEventResources;
            }

            return this.collectOther;
        }



        private bool IsAddButtonVisible()
        {
            GameObject gameObject = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Full/CookPanel(Clone)/AniRoot@queueanimation/detail@t/material@list");
            if (gameObject == null)
            {
                return false;
            }
            for (int i = 0; i < gameObject.transform.childCount; i++)
            {
                Transform transform = gameObject.transform.GetChild(i).Find("Root/cornerButton@frame/add@btn");
                if (transform != null && transform.gameObject.activeInHierarchy)
                {
                    return true;
                }
            }
            return false;
        }

        // Token: 0x06000012 RID: 18 RVA: 0x00003DE0 File Offset: 0x00001FE0
        private void ClickButtonIfExists(string path)
        {
            try
            {
                GameObject gameObject = GameObject.Find(path);
                if (gameObject == null) return;
                Button component = gameObject.GetComponent<Button>();
                if (component != null && gameObject.activeInHierarchy && component.interactable)
                {
                    component.onClick.Invoke();
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg($"[ClickButtonIfExists] Error clicking path '{path}': {ex.Message}");
                this.lastAutoCookException = ex.Message;
            }
        }

        // Advanced Cooking Cleanup (sprite-based)


        private void CollectImagesFromPath(string path, List<Image> target)
        {
            GameObject root = GameObject.Find(path);
            if (root == null || !root.activeInHierarchy)
            {
                return;
            }

            Image[] imgs = root.GetComponentsInChildren<Image>(true);
            if (imgs == null || imgs.Length == 0)
            {
                return;
            }

            for (int i = 0; i < imgs.Length; i++)
            {
                Image img = imgs[i];
                if (img != null)
                {
                    target.Add(img);
                }
            }
        }






        private string GetHierarchyPath(Transform transform)
        {
            if (transform == null)
            {
                return string.Empty;
            }

            List<string> parts = new List<string>();
            Transform current = transform;
            while (current != null)
            {
                parts.Add(current.name);
                current = current.parent;
            }
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        private bool IsSelectedToolInUse()
        {
            GameObject equippedGo = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Full/ToolsPanel(Clone)/content/infoBar/layout/equiped@go");
            if (equippedGo == null || !equippedGo.activeInHierarchy)
            {
                return false;
            }

            Text txt = equippedGo.GetComponent<Text>();
            if (txt == null)
            {
                txt = equippedGo.GetComponentInChildren<Text>(true);
            }
            if (txt == null || string.IsNullOrEmpty(txt.text))
            {
                return true; // Active badge with no readable text still indicates equipped.
            }

            string label = txt.text.Trim().ToLowerInvariant();
            return label.Contains("in use") || label.Contains("equipped");
        }

        private bool ClickButtonIfExistsWithParent(string path)
        {
            GameObject gameObject = GameObject.Find(path);
            if (gameObject == null || !gameObject.activeInHierarchy)
            {
                return false;
            }

            Button button = gameObject.GetComponent<Button>();
            if (button == null)
            {
                button = gameObject.GetComponentInParent<Button>();
            }
            if (button != null && button.interactable && button.gameObject.activeInHierarchy)
            {
                button.onClick.Invoke();
                return true;
            }

            return false;
        }

        private void CloseToolboxIfOpen()
        {
            if (this.ClickButtonIfExistsWithParent("GameApp/startup_root(Clone)/XDUIRoot/Full/ToolsPanel(Clone)/back@w/title/back/back@btn"))
            {
                return;
            }

            GameObject toolboxRoot = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Status/StatusPanel(Clone)/AniRoot@ani@queueanimation/right_layout@ani/bottom_right_layout@go/handable_bar@go/toolbox@w@go");
            if (toolboxRoot == null || !toolboxRoot.activeInHierarchy)
            {
                return;
            }

            // Prefer explicit close/back buttons if present in current layout.
            Button[] buttons = toolboxRoot.GetComponentsInChildren<Button>(true);
            if (buttons != null)
            {
                foreach (Button btn in buttons)
                {
                    if (btn == null || btn.gameObject == null || !btn.gameObject.activeInHierarchy || !btn.interactable)
                    {
                        continue;
                    }

                    string n = btn.name.ToLowerInvariant();
                    if (n.Contains("close") || n.Contains("back") || n.Contains("exit") || n.Contains("return"))
                    {
                        btn.onClick.Invoke();
                        return;
                    }
                }
            }

            // Fallback: toggle toolbox detail/entry buttons to collapse it.
            if (this.ClickButtonIfExistsWithParent("GameApp/startup_root(Clone)/XDUIRoot/Status/StatusPanel(Clone)/AniRoot@ani@queueanimation/right_layout@ani/bottom_right_layout@go/handable_bar@go/toolbox@w@go/handable_detail@go/handable_detail@btn"))
            {
                return;
            }

            this.ClickButtonIfExistsWithParent("GameApp/startup_root(Clone)/XDUIRoot/Status/StatusPanel(Clone)/AniRoot@ani@queueanimation/right_layout@ani/bottom_right_layout@go/handable_bar@go/handable@w/handable@btn@ani/bg/dec");
        }


        // Resource-farm helpers


        public void ResetAllCooldowns()
        {
            this.rockCooldowns.Clear();
            this.rockHideUntil.Clear();
            this.oreCooldowns.Clear();
            this.oreHideUntil.Clear();
            this.treeCooldowns.Clear();
            this.treeHideUntil.Clear();
            this.rareTreeCooldowns.Clear();
            this.rareTreeHideUntil.Clear();
            this.appleTreeCooldowns.Clear();
            this.appleTreeHideUntil.Clear();
            this.orangeTreeCooldowns.Clear();
            this.orangeTreeHideUntil.Clear();
            this.resourceMarkersNeedShuffle = true;
            this.visitedResourceMarkerIndices.Clear();
            ModLogger.Msg("[ResourceFarm] All cooldowns reset!");
        }




        private bool IsGatherWidgetVisible()
        {
            try
            {
                GameObject g = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Status/StatusPanel(Clone)/AniRoot@ani@queueanimation/middle_center_layout@go/gather@go/GatherSelectWidget(Clone)");
                return g != null && g.activeInHierarchy;
            }
            catch
            {
                return false;
            }
        }






        private EventSystem EnsureGameplayEventSystemAvailable()
        {
            try
            {
                EventSystem current = EventSystem.current;
                EventSystem target = this.blockedEventSystem != null ? this.blockedEventSystem : current;
                if (target != null && !target.enabled)
                {
                    target.enabled = true;
                }
                if (target != null)
                {
                    target.sendNavigationEvents = true;
                }
                return target;
            }
            catch
            {
                return EventSystem.current;
            }
        }


        private bool TryReadLiveCollectableCooldown(object collectableObject, out long coldEndTimeMs, out int availableNum, out string resTypeName)
        {
            coldEndTimeMs = 0L;
            availableNum = -1;
            resTypeName = string.Empty;
            if (collectableObject == null)
            {
                return false;
            }

            try
            {
                Type componentType = collectableObject.GetType();
                PropertyInfo coldEndTimeProperty = this.GetPropertyQuiet(componentType, "coldEndTime");
                if (coldEndTimeProperty != null)
                {
                    object rawCold = coldEndTimeProperty.GetValue(collectableObject, null);
                    if (rawCold is long)
                    {
                        coldEndTimeMs = (long)rawCold;
                    }
                    else if (rawCold is int)
                    {
                        coldEndTimeMs = (int)rawCold;
                    }
                }

                PropertyInfo availableNumProperty = this.GetPropertyQuiet(componentType, "availableNum");
                if (availableNumProperty != null)
                {
                    object rawAvailable = availableNumProperty.GetValue(collectableObject, null);
                    if (rawAvailable is int)
                    {
                        availableNum = (int)rawAvailable;
                    }
                }

                object rawResType = null;
                if (this.auraCollectableObjectResTypeField != null)
                {
                    rawResType = this.auraCollectableObjectResTypeField.GetValue(collectableObject);
                }
                else if (this.auraCollectableObjectResTypeProperty != null)
                {
                    rawResType = this.auraCollectableObjectResTypeProperty.GetValue(collectableObject, null);
                }
                resTypeName = rawResType != null ? (rawResType.ToString() ?? string.Empty) : string.Empty;

                FieldInfo componentDataField = this.GetFieldQuiet(componentType, "_componentData");
                if (componentDataField != null)
                {
                    object rawComponentData = componentDataField.GetValue(collectableObject);
                    if (rawComponentData != null)
                    {
                        Type componentDataType = rawComponentData.GetType();
                        if (coldEndTimeMs <= 0L)
                        {
                            FieldInfo coldField = this.GetFieldQuiet(componentDataType, "coldEndTime");
                            object rawColdField = coldField != null ? coldField.GetValue(rawComponentData) : null;
                            if (rawColdField is long)
                            {
                                coldEndTimeMs = (long)rawColdField;
                            }
                        }

                        if (availableNum < 0)
                        {
                            FieldInfo availableField = this.GetFieldQuiet(componentDataType, "availableNum");
                            object rawAvailableField = availableField != null ? availableField.GetValue(rawComponentData) : null;
                            if (rawAvailableField is int)
                            {
                                availableNum = (int)rawAvailableField;
                            }
                        }
                    }
                }

                return coldEndTimeMs > 0L || availableNum >= 0 || !string.IsNullOrEmpty(resTypeName);
            }
            catch
            {
                return false;
            }
        }


        private void TrySelectNearestCooldownEntry(Vector3 entityPosition, Vector3[] candidates, Dictionary<int, float> cooldowns, Dictionary<int, float> hideUntil, ref Dictionary<int, float> bestCooldowns, ref Dictionary<int, float> bestHideUntil, ref int bestIndex, ref float bestSqr)
        {
            int index = this.FindClosestItemIndexLocal(entityPosition, candidates);
            if (index < 0)
            {
                return;
            }

            float sqr = (candidates[index] - entityPosition).sqrMagnitude;
            if (sqr >= bestSqr)
            {
                return;
            }

            bestSqr = sqr;
            bestIndex = index;
            bestCooldowns = cooldowns;
            bestHideUntil = hideUntil;
        }



        // Find the closest index in a positions array within a reasonable radius (squared)
        private int FindClosestItemIndexLocal(Vector3 playerPos, Vector3[] positions)
        {
            int result = -1;
            float bestSqr = 25f; // 5 units
            for (int i = 0; i < positions.Length; i++)
            {
                float sq = (positions[i] - playerPos).sqrMagnitude;
                if (sq < bestSqr)
                {
                    bestSqr = sq;
                    result = i;
                }
            }
            return result;
        }

        // Mark nearest tree (of any type) as collected and start its cooldown/hide timers

        private bool IsHoldingTool()
        {
            try
            {
                GameObject statusPanel = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Status/StatusPanel(Clone)");
                if (statusPanel == null || !statusPanel.activeInHierarchy)
                {
                    ModLogger.Msg("[TreeFarm] IsHoldingTool: Status panel not found or not active");
                    return false;
                }

                Image[] images = statusPanel.GetComponentsInChildren<Image>(true);
                if (images == null || images.Length == 0)
                {
                    ModLogger.Msg("[TreeFarm] IsHoldingTool: No images in status panel");
                    return false;
                }

                foreach (Image img in images)
                {
                    if (img == null || img.gameObject == null || !img.gameObject.activeInHierarchy)
                    {
                        continue;
                    }

                    string fullPath = this.GetHierarchyPath(img.transform);
                    if (!string.IsNullOrEmpty(fullPath) &&
                        fullPath.Contains("/CommonIconForTool(Clone)/") &&
                        fullPath.Contains("/icon@img@btn"))
                    {
                        ModLogger.Msg($"[TreeFarm] IsHoldingTool: Found tool image at {fullPath}");
                        return true; // Holding a tool
                    }
                }
                ModLogger.Msg("[TreeFarm] IsHoldingTool: No tool images found");
            }
            catch (Exception ex)
            {
                ModLogger.Msg($"[TreeFarm] IsHoldingTool error: {ex.Message}");
            }
            return false;
        }

        private void WithdrawHeldTools()
        {
            try
            {
                GameObject statusPanel = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Status/StatusPanel(Clone)");
                if (statusPanel != null && statusPanel.activeInHierarchy)
                {
                    Button[] buttons = statusPanel.GetComponentsInChildren<Button>(true);
                    if (buttons != null && buttons.Length > 0)
                    {
                        foreach (Button btn in buttons)
                        {
                            if (btn == null || btn.gameObject == null || !btn.gameObject.activeInHierarchy || !btn.interactable)
                            {
                                continue;
                            }

                            string fullPath = this.GetHierarchyPath(btn.transform);
                            if (!string.IsNullOrEmpty(fullPath) &&
                                fullPath.Contains("/CommonIconForTool(Clone)/") &&
                                fullPath.Contains("/icon@img@btn"))
                            {
                                btn.onClick.Invoke();
                                return; // Withdraw one tool at a time
                            }
                        }
                    }
                }
            }
            catch
            {
            }
        }

        private bool TryClickInteractPrompt()
        {
            // Try to trigger known in-game joystick/trigger objects first (most reliable)
            try
            {
                string[] triggerCandidates = new string[] { "GatherSelectWidget", "skill_main_hold@go@w", "main_joy@go@w" };
                foreach (string candidate in triggerCandidates)
                {
                    if (this.TryActivateTriggerByName(candidate))
                    {
                        ModLogger.Msg($"[TreeFarm] Activated trigger '{candidate}'");
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg($"[TreeFarm] Trigger scan error: {ex.Message}");
            }

            // Fallback: try the tracking panel interact button
            try
            {
                GameObject trackingPanel = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)");
                if (trackingPanel != null && trackingPanel.activeInHierarchy)
                {
                    Button[] buttons = trackingPanel.GetComponentsInChildren<Button>(true);
                    if (buttons != null && buttons.Length > 0)
                    {
                        foreach (Button btn in buttons)
                        {
                            if (btn == null || btn.gameObject == null || !btn.gameObject.activeInHierarchy || !btn.interactable)
                                continue;
                            string fullPath = this.GetHierarchyPath(btn.transform);
                            if (!string.IsNullOrEmpty(fullPath) &&
                                fullPath.Contains("/tracking_common@list/IconsBarWidget(Clone)/") &&
                                fullPath.Contains("/CommonIconForInteract(Clone)/root_visible@go/icon@img@btn"))
                            {
                                ModLogger.Msg("[TreeFarm] Found interact button in tracking panel, clicking");
                                btn.onClick.Invoke();
                                return true;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg($"[TreeFarm] Error searching tracking panel: {ex.Message}");
            }

            // Try fixed path interact button
            if (this.ClickButtonIfExistsReturn(INTERACT_PROMPT_BUTTON_PATH))
            {
                ModLogger.Msg("[TreeFarm] Clicked interact button via path");
                return true;
            }

            // Try swing button
            if (this.ClickButtonIfExistsReturn("GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)/tracking_bar@w/tracking_sand_swing@go@w/root_visible@go/swing@btn"))
            {
                ModLogger.Msg("[TreeFarm] Clicked swing button for interaction");
                return true;
            }

            // Last resort: send the F key simulation
            ModLogger.Msg("[TreeFarm] No UI trigger found, sending F key");
            this.SendFMessage();
            return true;
        }

        private bool TryActivateTriggerByName(string partialName)
        {
            // Use EventSystem.current; if none exists, we cannot simulate UI clicks safely
            if (EventSystem.current == null)
            {
                ModLogger.Msg("[Trigger] EventSystem.current is null; cannot activate UI triggers.");
                return false;
            }

            try
            {
                var allObjects = UnityEngine.Object.FindObjectsOfType<GameObject>();
                foreach (var obj in allObjects)
                {
                    if (obj == null) continue;
                    if (!obj.activeInHierarchy) continue;
                    if (obj.name == null) continue;
                    if (!obj.name.Contains(partialName)) continue;

                    ModLogger.Msg($"[Trigger] Found object matching '{partialName}': {obj.name} - attempting activation");

                    // If object has a Button component, invoke it directly
                    Button btn = obj.GetComponent<Button>();
                    if (btn == null) btn = obj.GetComponentInParent<Button>();
                    if (btn != null && btn.interactable && btn.gameObject.activeInHierarchy)
                    {
                        try { btn.onClick.Invoke(); }
                        catch { }
                        return true;
                    }

                    // Otherwise simulate pointer events
                    var pointer = new PointerEventData(EventSystem.current);
                    ExecuteEvents.Execute(obj, pointer, ExecuteEvents.pointerEnterHandler);
                    ExecuteEvents.Execute(obj, pointer, ExecuteEvents.pointerDownHandler);
                    ExecuteEvents.Execute(obj, pointer, ExecuteEvents.pointerUpHandler);
                    ExecuteEvents.Execute(obj, pointer, ExecuteEvents.pointerClickHandler);
                    ExecuteEvents.Execute(obj, pointer, ExecuteEvents.beginDragHandler);
                    return true;
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg($"[Trigger] Error activating '{partialName}': {ex.Message}");
            }
            return false;
        }


        private bool PerformAutoSwing()
        {
            try
            {
                var all = UnityEngine.Object.FindObjectsOfType<GameObject>();
                foreach (var obj in all)
                {
                    if (obj == null) continue;
                    if (!obj.activeInHierarchy) continue;
                    if (string.IsNullOrEmpty(obj.name)) continue;
                    string n = obj.name.ToLowerInvariant();
                    if (!(n.Contains("main_joy@go@w") || n.Contains("skill_main_hold@go@w") || n.Contains("gatherselectwidget"))) continue;

                    ModLogger.Msg($"[TreeFarm] PerformAutoSwing found trigger object: {obj.name}");

                    Button btn = obj.GetComponent<Button>();
                    if (btn == null) btn = obj.GetComponentInParent<Button>();
                    if (btn != null && btn.interactable && btn.gameObject.activeInHierarchy)
                    {
                        try { btn.onClick.Invoke(); } catch { }
                        return true;
                    }

                    if (EventSystem.current != null)
                    {
                        var pointer = new PointerEventData(EventSystem.current);
                        ExecuteEvents.Execute(obj, pointer, ExecuteEvents.pointerEnterHandler);
                        ExecuteEvents.Execute(obj, pointer, ExecuteEvents.pointerDownHandler);
                        ExecuteEvents.Execute(obj, pointer, ExecuteEvents.pointerUpHandler);
                        ExecuteEvents.Execute(obj, pointer, ExecuteEvents.pointerClickHandler);
                        ExecuteEvents.Execute(obj, pointer, ExecuteEvents.beginDragHandler);
                        return true;
                    }
                }

                // If no UI trigger was found, always try swing button or F key as fallback
                // Try swing button path
                if (this.ClickButtonIfExistsReturn(this.swingButtonPath))
                {
                    ModLogger.Msg("[TreeFarm] Performed fallback swing by clicking swing button");
                    return true;
                }
                // Last resort: send F
                ModLogger.Msg("[TreeFarm] No UI trigger - sending F key as fallback");
                this.SendFMessage();
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[TreeFarm] PerformAutoSwing error: " + ex.Message);
            }
            return false;
        }

        // Removed blocking WaitForSwingConfirm in favor of non-blocking polling handled in RunTreeFarmLogic





        private bool ClickButtonIfExistsReturn(string path)
        {
            GameObject gameObject = GameObject.Find(path);
            if (gameObject == null || !gameObject.activeInHierarchy)
            {
                return false;
            }

            Button component = this.ResolveClickableButton(gameObject);
            if (component != null && component.interactable)
            {
                component.onClick.Invoke();
                return true;
            }

            return this.SimulateClick(gameObject);
        }

        private void CloseAnnouncementPanelIfPresent()
        {
            if (!this.autoCloseAnnouncementEnabled) return;
            float now = Time.unscaledTime;
            if (now < this.nextAnnouncementCloseCheckAt)
            {
                return;
            }

            this.nextAnnouncementCloseCheckAt = now + 0.5f;
            try
            {
                GameObject btnObj = GameObject.Find(ANNOUNCEMENT_CLOSE_BUTTON_PATH);
                if (btnObj != null && btnObj.activeInHierarchy)
                {
                    bool clicked = this.ClickButtonIfExistsReturn(ANNOUNCEMENT_CLOSE_BUTTON_PATH);
                }
            }
            catch { }
        }

        private Button ResolveClickableButton(GameObject target)
        {
            if (target == null)
            {
                return null;
            }

            try
            {
                Button direct = target.GetComponent<Button>();
                if (direct != null)
                {
                    return direct;
                }

                Button child = target.GetComponentInChildren<Button>(true);
                if (child != null)
                {
                    return child;
                }

                Button parent = target.GetComponentInParent<Button>();
                if (parent != null)
                {
                    return parent;
                }

                Transform[] chain = new Transform[]
                {
                    target.transform,
                    target.transform.parent,
                    target.transform.parent != null ? target.transform.parent.parent : null
                };

                for (int i = 0; i < chain.Length; i++)
                {
                    Transform tr = chain[i];
                    if (tr == null) continue;
                    Component[] components = tr.GetComponents<Component>();
                    for (int j = 0; j < components.Length; j++)
                    {
                        Component comp = components[j];
                        if (comp == null) continue;
                        Type type = comp.GetType();

                        PropertyInfo unityComponentProp = type.GetProperty("unityComponent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (unityComponentProp != null && typeof(Button).IsAssignableFrom(unityComponentProp.PropertyType))
                        {
                            Button btn = unityComponentProp.GetValue(comp, null) as Button;
                            if (btn != null) return btn;
                        }

                        FieldInfo unityComponentField = type.GetField("unityComponent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        if (unityComponentField != null && typeof(Button).IsAssignableFrom(unityComponentField.FieldType))
                        {
                            Button btn = unityComponentField.GetValue(comp) as Button;
                            if (btn != null) return btn;
                        }
                    }
                }
            }
            catch { }

            return null;
        }

        private bool IsLoginPanelActive()
        {
            GameObject go = GameObject.Find(LOGIN_PANEL_PATH);
            return go != null && go.activeInHierarchy;
        }

        private bool IsLoginRoomPanelActive()
        {
            GameObject go = GameObject.Find(LOGIN_ROOM_PANEL_PATH);
            return go != null && go.activeInHierarchy;
        }







        // Player Distance Detection
        private float GetNearestPlayerDistance()
        {
            if (Time.unscaledTime < this.nextNearestPlayerDistanceRefreshAt)
            {
                return this.cachedNearestPlayerDistance;
            }

            if (this.cachedPlayerObject == null || !this.cachedPlayerObject.activeInHierarchy)
            {
                this.cachedPlayerObject = GameObject.Find("p_player_skeleton(Clone)");
                if (this.cachedPlayerObject == null)
                {
                    this.cachedNearestPlayerDistance = 999f;
                    this.nextNearestPlayerDistanceRefreshAt = Time.unscaledTime + 1.5f;
                    return this.cachedNearestPlayerDistance;
                }
            }

            Vector3 myPosition = this.cachedPlayerObject.transform.position;
            float nearest = 999f;

            GameObject[] allObjects = UnityEngine.Object.FindObjectsOfType<GameObject>();
            foreach (GameObject obj in allObjects)
            {
                if (obj == null) continue;

                // Find other player skeletons (not our own)
                if (obj.name.Contains("p_player_skeleton") && obj != this.cachedPlayerObject)
                {
                    float distance = Vector3.Distance(myPosition, obj.transform.position);
                    if (distance < nearest)
                    {
                        nearest = distance;
                    }
                }
            }

            this.cachedNearestPlayerDistance = nearest;
            this.nextNearestPlayerDistanceRefreshAt = Time.unscaledTime + 1.5f;
            return this.cachedNearestPlayerDistance;
        }

        // Token: 0x06000013 RID: 19 RVA: 0x00003E34 File Offset: 0x00002034
        private void RunBypassLogic(bool shouldHide)
        {
            if (!shouldHide && !this.bypassObjectsHidden)
            {
                return;
            }

            bool targetState = !shouldHide;
            this.ManageObject(ref this.cacheStatusAnim, "GameApp/startup_root(Clone)/XDUIRoot/Status/StatusPanel(Clone)/AniRoot@ani@queueanimation", targetState);
            this.ManageObject(ref this.cacheCookUI, "GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)/tracking_bar@w/tracking_cook_normal@list", targetState);
            this.ManageObject(ref this.cacheSkeletonBody, "p_player_skeleton(Clone)/sk_player_player_skeleton", targetState);
            this.bypassObjectsHidden = shouldHide;
        }

        // Token: 0x06000014 RID: 20 RVA: 0x00003E80 File Offset: 0x00002080
        private void ManageObject(ref GameObject cached, string path, bool targetState)
        {
            bool flag = cached == null;
            if (flag)
            {
                cached = GameObject.Find(path);
            }
            bool flag2 = cached != null && cached.activeSelf != targetState;
            if (flag2)
            {
                cached.SetActive(targetState);
            }
        }





        private bool IsPriorityLocationAvailable(Vector3 loc, float currentTime)
        {
            bool stillActive = false;
            for (int i = 0; i < this.activePriorityLocations.Count; i++)
            {
                if (this.activePriorityLocations[i] == loc)
                {
                    stillActive = true;
                    break;
                }
            }
            if (!stillActive)
            {
                return false;
            }

            return !this.priorityLocationCooldowns.ContainsKey(loc) || (currentTime - this.priorityLocationCooldowns[loc]) > 300f;
        }


        private Vector3? GetActivePriorityLocation()
        {
            this.RefreshActivePriorityLocations();
            if (this.currentPriorityLocation.HasValue)
            {
                Vector3 value = this.currentPriorityLocation.Value;
                bool stillEnabled = false;
                for (int i = 0; i < this.activePriorityLocations.Count; i++)
                {
                    if (this.activePriorityLocations[i] == value)
                    {
                        stillEnabled = true;
                        break;
                    }
                }
                if (stillEnabled
                    && (!this.priorityLocationCooldowns.ContainsKey(value) || (Time.unscaledTime - this.priorityLocationCooldowns[value]) > 300f))
                {
                    return value;
                }

                this.currentPriorityLocation = null;
            }
            // Return the first active priority location not on cooldown
            foreach (Vector3 loc in this.activePriorityLocations)
            {
                if (!this.priorityLocationCooldowns.ContainsKey(loc) || (Time.unscaledTime - this.priorityLocationCooldowns[loc]) > 300f)
                {
                    return loc;
                }
            }
            return null; // All on cooldown or none active
        }

        private void FinishCollectingCycle()
        {
            this.StopMeteorAutoInteractSequence();

            if (this.autoCollectClickedSinceArrival)
            {
                this.AutoFarmLog("Collect cycle stamped cooldown for node " + this.lastNodePosition);
                this.TryStampVisitedResourceNodeCooldown(this.lastNodePosition);
            }

            // Priority flow:
            // If no collect happened in a priority cycle, cooldown that priority location immediately.
            if (this.lastTeleportWasPriorityLocation && this.currentPriorityLocation.HasValue)
            {
                bool priorityLocationStillHasNodes = this.HasAvailablePriorityNodeForLocation(this.currentPriorityLocation.Value);
                if (priorityLocationStillHasNodes)
                {
                    this.AutoFarmLog("Priority location remains active: " + this.currentPriorityLocation.Value);
                    this.priorityLocationCooldowns.Remove(this.currentPriorityLocation.Value);
                }
                else
                {
                    this.AutoFarmLog("Priority location exhausted -> cooldown start: " + this.currentPriorityLocation.Value);
                    this.priorityLocationCooldowns[this.currentPriorityLocation.Value] = Time.unscaledTime;
                    this.currentPriorityLocation = null;
                }
            }

            this.lastTeleportWasPriorityLocation = false;
            this.farmState = HeartopiaComplete.AutoFarmState.ScanningForNodes;
            this.autoFarmTimer = 0f;
        }










        private string GetPriorityTokenForLocation(Vector3 loc)
        {
            if (loc == this.priorityLocations["Oyster Mushroom"]) return "Oyster";
            if (loc == this.priorityLocations["Button Mushroom"]) return "Button";
            if (loc == this.priorityLocations["Penny Bun"]) return "Penny Bun";
            if (loc == this.priorityLocations["Shiitake"]) return "Shiitake";
            if (loc == this.priorityLocations["Black Truffle"]) return "Truffle";
            if (loc == this.priorityLocations["Fiddlehead"]) return "Fiddlehead";
            if (loc == this.priorityLocations["Tall Mustard"]) return "Tall Mustard";
            if (loc == this.priorityLocations["Burdock"]) return "Burdock";
            if (loc == this.priorityLocations["Mustard Greens"]) return "Mustard Greens";
            if (loc == this.priorityLocations["Blueberry"]) return "Blueberry";
            if (loc == this.priorityLocations["Raspberry"]) return "Raspberry";
            return string.Empty;
        }

        private void RefreshActivePriorityLocations()
        {
            List<Vector3> newActive = new List<Vector3>();

            if (this.priorityOysterMushroom) newActive.Add(this.priorityLocations["Oyster Mushroom"]);
            if (this.priorityButtonMushroom) newActive.Add(this.priorityLocations["Button Mushroom"]);
            if (this.priorityPennyBun) newActive.Add(this.priorityLocations["Penny Bun"]);
            if (this.priorityShiitake) newActive.Add(this.priorityLocations["Shiitake"]);
            if (this.priorityTruffle) newActive.Add(this.priorityLocations["Black Truffle"]);
            if (this.priorityFiddlehead) newActive.Add(this.priorityLocations["Fiddlehead"]);
            if (this.priorityTallMustard) newActive.Add(this.priorityLocations["Tall Mustard"]);
            if (this.priorityBurdock) newActive.Add(this.priorityLocations["Burdock"]);
            if (this.priorityMustardGreens) newActive.Add(this.priorityLocations["Mustard Greens"]);
            if (this.priorityBlueberry) newActive.Add(this.priorityLocations["Blueberry"]);
            if (this.priorityRaspberry) newActive.Add(this.priorityLocations["Raspberry"]);

            this.activePriorityLocations = newActive;

            // Remove cooldowns for locations that are no longer enabled.
            List<Vector3> stale = new List<Vector3>();
            foreach (Vector3 loc in this.priorityLocationCooldowns.Keys)
            {
                bool stillActive = false;
                for (int i = 0; i < this.activePriorityLocations.Count; i++)
                {
                    if (this.activePriorityLocations[i] == loc)
                    {
                        stillActive = true;
                        break;
                    }
                }
                if (!stillActive)
                {
                    stale.Add(loc);
                }
            }
            for (int i = 0; i < stale.Count; i++)
            {
                this.priorityLocationCooldowns.Remove(stale[i]);
            }
        }









        // Token: 0x06000019 RID: 25 RVA: 0x00004A34 File Offset: 0x00002C34
        private void SetAutoCollectEnabled(bool enabled, bool notify = true)
        {
            if (this.autoFarmEnabled == enabled)
            {
                return;
            }

            this.autoFarmEnabled = enabled;
            if (enabled && this.auraFarmEnabled)
            {
                this.SetAuraFarmEnabled(false);
            }

            ModLogger.Msg("Auto Collect " + (this.autoFarmEnabled ? "Enabled" : "Disabled"));
            if (notify)
            {
                this.AddMenuNotification($"Auto Collect {(this.autoFarmEnabled ? "enabled" : "disabled")}", this.autoFarmEnabled ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.55f, 0.55f));
            }
        }





        public string FormatDurationHms(int totalSeconds)
        {
            totalSeconds = Math.Max(0, totalSeconds);
            int h = totalSeconds / 3600;
            int m = (totalSeconds % 3600) / 60;
            int s = totalSeconds % 60;
            return h.ToString("00") + ":" + m.ToString("00") + ":" + s.ToString("00");
        }








        // Token: 0x0600001D RID: 29 RVA: 0x00005FF0 File Offset: 0x000041F0
        private string GetMeshName(Il2CppObject group, Il2CppBindingFlags flags)
        {
            string result;
            try
            {
                Il2CppFieldInfo field = group.GetIl2CppType().GetField("meshInfo", flags);
                Il2CppObject @object = (field != null) ? field.GetValue(group) : null;
                Il2CppObject object2;
                if (@object == null)
                {
                    object2 = null;
                }
                else
                {
                    Il2CppFieldInfo field2 = @object.GetIl2CppType().GetField("lodMesh", flags);
                    object2 = ((field2 != null) ? field2.GetValue(@object) : null);
                }
                Il2CppObject object3 = object2;
                bool flag = object3 == null;
                if (flag)
                {
                    result = "";
                }
                else
                {
                    Il2CppMethodInfo method = object3.GetIl2CppType().GetMethod("GetValue", new Il2CppReferenceArray<Il2CppType>(new Il2CppType[]
                    {
                        Il2CppType.GetType("System.Int32")
                    }));
                    bool flag2 = method == null;
                    if (flag2)
                    {
                        result = "";
                    }
                    else
                    {
                        Il2CppObject object4 = method.Invoke(object3, new Il2CppReferenceArray<Il2CppObject>(new Il2CppObject[]
                        {
                            this.BoxInt(0)
                        }));
                        Mesh mesh = (object4 != null) ? object4.TryCast<Mesh>() : null;
                        result = ((mesh != null) ? mesh.name : null) ?? "";
                    }
                }
            }
            catch
            {
                result = "";
            }
            return result;
        }

        // Token: 0x0600001E RID: 30 RVA: 0x000060F8 File Offset: 0x000042F8
        private Vector3 GetBlockPos(Il2CppObject block, Il2CppBindingFlags flags)
        {
            Il2CppFieldInfo field = block.GetIl2CppType().GetField("aabb", flags);
            Il2CppObject @object = (field != null) ? field.GetValue(block) : null;
            Il2CppObject object2;
            if (@object == null)
            {
                object2 = null;
            }
            else
            {
                Il2CppFieldInfo field2 = @object.GetIl2CppType().GetField("m_Center", flags);
                object2 = ((field2 != null) ? field2.GetValue(@object) : null);
            }
            Il2CppObject object3 = object2;
            float num = object3.GetIl2CppType().GetField("x").GetValue(object3).Unbox<float>();
            float num2 = object3.GetIl2CppType().GetField("y").GetValue(object3).Unbox<float>();
            float num3 = object3.GetIl2CppType().GetField("z").GetValue(object3).Unbox<float>();
            return new Vector3(num, num2, num3);
        }

        private Il2CppObject BoxInt(int val)
        {
            // FIX: Direct field assignment for maximum compatibility
            return new Il2CppSystem.Int32 { m_value = val }.BoxIl2CppObject();
        }

        private Il2CppObject BoxBool(bool val)
        {
            return new Il2CppSystem.Boolean { m_value = val }.BoxIl2CppObject();
        }

        // Token: 0x0600001F RID: 31 RVA: 0x000061B0 File Offset: 0x000043B0

        // NEW FEATURE: Apply Camera FOV

        private void ApplyFpsBypass(bool enabled)
        {
            try
            {
                if (!enabled)
                {
                    if (this.fpsBypassWasApplied)
                    {
                        QualitySettings.vSyncCount = this.fpsBypassOriginalVSyncCount;
                        Application.targetFrameRate = this.fpsBypassOriginalTargetFrameRate;
                        this.fpsBypassWasApplied = false;
                    }
                    this.fpsBypassCompOffset = 0f;
                    this.fpsBypassObservedFps = 0f;
                    return;
                }

                if (!this.fpsBypassWasApplied)
                {
                    this.fpsBypassOriginalVSyncCount = QualitySettings.vSyncCount;
                    this.fpsBypassOriginalTargetFrameRate = Application.targetFrameRate;
                    this.fpsBypassWasApplied = true;
                }

                int target = Mathf.Clamp(Mathf.RoundToInt((float)this.fpsBypassTarget + this.fpsBypassCompOffset), 30, 360);
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = target;
            }
            catch
            {
            }
        }


        private void Cleanup()
        {
            bool flag = this.radarContainer != null;
            if (flag)
            {
                Object.Destroy(this.radarContainer);
                this.radarContainer = null;
            }
            if (this.radarLineMaterial != null)
            {
                Object.Destroy(this.radarLineMaterial);
                this.radarLineMaterial = null;
            }
            if (this.radarFillMaterial != null)
            {
                Object.Destroy(this.radarFillMaterial);
                this.radarFillMaterial = null;
            }
            this.markerToTarget.Clear();
            this.markerMetadataById.Clear();
            this.trackedObjectMarkers.Clear();
            this.trackedBubbleMarkers.Clear();
            this.ClearHideAndSeekMorphMarkers();
            this.bubbleRadarTrackedPositions.Clear();
            this.bubbleRadarSnapshotPositions.Clear();
            this.bubbleRadarSceneTargets.Clear();
            this.bubbleRadarLastSeenAt.Clear();
            this.bubbleRadarSeenIds.Clear();
            this.bubbleRadarDebugNextLogAt.Clear();
            this.bubbleRadarForceRefresh = true;
            this.bubbleRadarHasLastScanOrigin = false;
            this.bubbleRadarActivatedAt = -999f;
            this.bubbleRadarAuraConsecutiveFailures = 0;
            this.nextBubbleMarkerSyncAt = -999f;
            this._cachedBubbleRadarAt = -999f;
            this.nextAuraBubbleScanAttemptAt = -999f;
            this.lastAuraBubbleScanSuccessAt = -999f;
            this.lastAuraBubbleScanFailureAt = -999f;
        }



        // Token: 0x06000022 RID: 34 RVA: 0x00006C04 File Offset: 0x00004E04
        private void SetHomePosition()
        {
            GameObject gameObject = GameObject.Find("p_player_skeleton(Clone)");
            bool flag = gameObject == null;
            if (flag)
            {
                ModLogger.Msg("Player not found!");
            }
            else
            {
                this.homePosition = gameObject.transform.position;
                this.homePositionSet = true;
                this.autoHomeStatus = "Manual home saved";
                ModLogger.Msg($"[HOME] Home position set to: {this.homePosition}");
            }
        }



        private void RefreshAutoHomePosition(bool force = false)
        {
            float unscaledTime = Time.unscaledTime;
            if (!force && this.autoHomePositionValid)
            {
                return;
            }

            if (!force && unscaledTime < this.autoHomeResolveNextAt)
            {
                return;
            }
            this.autoHomeResolveNextAt = unscaledTime + AutoHomeResolveRetryInterval;
            Vector3 vector;
            uint num;
            string text;
            if (this.TryResolveCurrentHomePosition(out vector, out num, out text))
            {
                this.autoHomePosition = vector;
                this.autoHomeNetId = num;
                this.autoHomePositionValid = true;
                this.autoHomeResolveNextAt = float.PositiveInfinity;
                this.autoHomeStatus = "Home Ready";
            }
            else
            {
                this.autoHomePositionValid = false;
                this.autoHomeNetId = 0U;
                this.autoHomeResolveNextAt = unscaledTime + AutoHomeResolveRetryInterval;
                this.autoHomeStatus = text;
            }
        }

        private bool TryResolveCurrentHomePosition(out Vector3 position, out uint homeNetId, out string status)
        {
            position = Vector3.zero;
            homeNetId = 0U;
            status = "Auto home unavailable";
            string monoFailureStatus = string.Empty;
            uint selfPlayerNetId = 0U;
            bool selfResolved = this.TryResolveSelfPlayerNetId(out selfPlayerNetId);
            if (selfResolved && selfPlayerNetId != 0U)
            {
                object ownerField = this.TryResolveFieldByOwnerId(selfPlayerNetId);
                if (ownerField == null)
                {
                    status = $"Auto home: owner path null [self={selfPlayerNetId}]";
                }
                else
                {
                    status = $"Auto home: owner path found [{ownerField.GetType().Name}]";
                }
                if (this.TryExtractHomePosition(ownerField, out position))
                {
                    uint resolvedHomeNetId;
                    if (this.TryGetUIntMember(ownerField, "homeNetId", out resolvedHomeNetId))
                    {
                        homeNetId = resolvedHomeNetId;
                    }
                    status = "Home Ready";
                    return true;
                }
                if (ownerField != null)
                {
                    status = $"Auto home: owner extract failed [{ownerField.GetType().Name}]";
                }
                if (this.TryResolveHomePositionMono(selfPlayerNetId, out position, out uint monoOwnerHomeNetId, out string monoOwnerStatus))
                {
                    if (monoOwnerHomeNetId != 0U)
                    {
                        homeNetId = monoOwnerHomeNetId;
                    }
                    else
                    {
                        this.TryResolveCurrentHomeNetIdMono(out homeNetId);
                    }
                    status = "Home Ready";
                    return true;
                }
                status = monoOwnerStatus;
                monoFailureStatus = monoOwnerStatus;
            }
            else
            {
                status = "Auto home: self player id unavailable";
            }
            if (!this.TryResolveCurrentHomeNetIdMono(out homeNetId))
            {
                Type type = this.FindLoadedType("XDTDataAndProtocol.PlayerDataCenter", "PlayerDataCenter");
                if (type == null)
                {
                    status = "Auto home: PlayerDataCenter not found";
                    return false;
                }
                FieldInfo field = type.GetField("homeNetId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (field == null)
                {
                    status = "Auto home: homeNetId not found";
                    return false;
                }
                object value = field.GetValue(null);
                if (!(value is uint))
                {
                    status = "Auto home: invalid homeNetId";
                    return false;
                }
                homeNetId = (uint)value;
            }
            if (homeNetId == 0U)
            {
                status = "Auto home: homeNetId=0";
                return false;
            }
            if (this.TryResolveHomePositionMono(0U, out position, out uint monoHomeNetId, out string monoHomeStatus))
            {
                if (monoHomeNetId != 0U)
                {
                    homeNetId = monoHomeNetId;
                }
                status = "Home Ready";
                return true;
            }
            monoFailureStatus = monoHomeStatus;
            Type type2 = this.FindLoadedType("XDTLevelAndEntity.BaseSystem.EntitiesManager.Entities", "Entities");
            if (type2 != null)
            {
                object obj = null;
                PropertyInfo property = type2.GetProperty("fieldSystem", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (property != null)
                {
                    obj = property.GetValue(null, null);
                }
                else
                {
                    FieldInfo field2 = type2.GetField("fieldSystem", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    if (field2 != null)
                    {
                        obj = field2.GetValue(null);
                    }
                }
                if (obj != null)
                {
                    MethodInfo methodInfo = obj.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).FirstOrDefault(delegate(MethodInfo m)
                    {
                        if (m.Name != "GetField")
                        {
                            return false;
                        }
                        ParameterInfo[] parameters = m.GetParameters();
                        return parameters.Length == 1 && parameters[0].ParameterType == typeof(uint);
                    });
                    if (methodInfo != null)
                    {
                        object obj2 = methodInfo.Invoke(obj, new object[]
                        {
                            homeNetId
                        });
                        if (obj2 != null)
                        {
                            status = $"Auto home: GetField found [{obj2.GetType().Name}]";
                        }
                        else
                        {
                            status = $"Auto home: GetField null [{homeNetId}]";
                        }
                        if (this.TryExtractHomePosition(obj2, out position))
                        {
                            status = "Home Ready";
                            return true;
                        }
                        if (obj2 != null)
                        {
                            status = $"Auto home: GetField extract failed [{obj2.GetType().Name}]";
                        }
                    }
                    else
                    {
                        status = "Auto home: fieldSystem.GetField missing";
                    }
                }
                else
                {
                    status = "Auto home: fieldSystem unavailable";
                }
            }
            else
            {
                status = "Auto home: Entities type unavailable";
            }
            Type type3 = this.FindLoadedType("XDTLevelAndEntity.GameplaySystem.CraftingSystem.FieldComponent", "FieldComponent");
            if (type3 != null)
            {
                Il2CppType il2CppType = Il2CppType.GetType(type3.AssemblyQualifiedName);
                if (il2CppType != null)
                {
                    UnityObject[] array = Resources.FindObjectsOfTypeAll(il2CppType);
                    foreach (UnityObject unityObject in array)
                    {
                        if (!(unityObject == null))
                        {
                            object obj3 = unityObject;
                            uint num;
                            if (this.TryGetUIntMember(obj3, "homeNetId", out num) && num == homeNetId && this.TryExtractHomePosition(obj3, out position))
                            {
                                status = "Home Ready";
                                return true;
                            }
                            if (this.TryGetUIntMember(obj3, "homeNetId", out num) && num == homeNetId)
                            {
                                status = $"Auto home: il2cpp match extract failed [{obj3.GetType().Name}]";
                            }
                        }
                    }
                }
                else
                {
                    status = "Auto home: FieldComponent il2cpp type unavailable";
                }
            }
            else
            {
                status = "Auto home: FieldComponent type unavailable";
            }
            if (status == "Auto home unavailable" || status == "Auto home: self player id unavailable")
            {
                status = $"Auto home: field {homeNetId} not found";
            }
            if (!string.IsNullOrEmpty(monoFailureStatus) && (status == "Auto home: Entities type unavailable" || status == "Auto home: FieldComponent type unavailable" || status == "Auto home: FieldComponent il2cpp type unavailable"))
            {
                status = monoFailureStatus;
            }
            return false;
        }

        private unsafe bool TryResolveHomePositionMono(uint ownerId, out Vector3 position, out uint resolvedHomeNetId, out string status)
        {
            position = Vector3.zero;
            resolvedHomeNetId = 0U;
            status = "Auto home: mono resolve unavailable";
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoClassFromName == null || auraMonoClassGetMethodFromName == null || auraMonoRuntimeInvoke == null || auraMonoObjectGetClass == null || auraMonoClassGetFieldFromName == null || auraMonoFieldGetValueObject == null)
            {
                return false;
            }
            IntPtr levelImage = this.FindAuraMonoImage(new string[]
            {
                "XDTLevelAndEntity",
                "XDTLevelAndEntity.dll"
            });
            IntPtr homelandClass = levelImage != IntPtr.Zero ? auraMonoClassFromName(levelImage, "XDTLevelAndEntity.GameplaySystem.HomeLand", "HomelandEntitySystem") : IntPtr.Zero;
            if (homelandClass == IntPtr.Zero)
            {
                homelandClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTLevelAndEntity.GameplaySystem.HomeLand", "HomelandEntitySystem");
            }
            if (homelandClass == IntPtr.Zero)
            {
                status = "Auto home: HomelandEntitySystem mono missing";
                return false;
            }
            IntPtr homelandObj = IntPtr.Zero;
            if (ownerId != 0U)
            {
                IntPtr getPlayerField = auraMonoClassGetMethodFromName(homelandClass, "GetPlayerField", 1);
                if (getPlayerField != IntPtr.Zero)
                {
                    IntPtr exc = IntPtr.Zero;
                    uint ownerArg = ownerId;
                    IntPtr* args = stackalloc IntPtr[1];
                    args[0] = (IntPtr)(&ownerArg);
                    homelandObj = auraMonoRuntimeInvoke(getPlayerField, IntPtr.Zero, (IntPtr)args, ref exc);
                }
            }
            if (homelandObj == IntPtr.Zero)
            {
                IntPtr getSelfField = auraMonoClassGetMethodFromName(homelandClass, "GetSelfField", 0);
                if (getSelfField != IntPtr.Zero)
                {
                    IntPtr exc = IntPtr.Zero;
                    homelandObj = auraMonoRuntimeInvoke(getSelfField, IntPtr.Zero, IntPtr.Zero, ref exc);
                }
            }
            if (homelandObj == IntPtr.Zero)
            {
                status = ownerId != 0U ? $"Auto home: mono owner field null [self={ownerId}]" : "Auto home: mono self field null";
                return false;
            }
            if (!this.TryGetMonoUInt32FromObjectMember(homelandObj, "entity", "netId", out resolvedHomeNetId))
            {
                this.TryGetMonoUInt32Member(homelandObj, "homeNetId", out resolvedHomeNetId);
            }
            if (this.TryExtractHomePositionMonoObject(homelandObj, out position))
            {
                return true;
            }
            status = "Auto home: mono extract failed";
            return false;
        }

        private bool TryExtractHomePositionMonoObject(IntPtr obj, out Vector3 position)
        {
            position = Vector3.zero;
            if (obj == IntPtr.Zero || auraMonoObjectGetClass == null || auraMonoClassGetFieldFromName == null || auraMonoFieldGetValueObject == null || auraMonoClassGetMethodFromName == null || auraMonoRuntimeInvoke == null)
            {
                return false;
            }
            IntPtr fieldComponentObj;
            if (this.TryGetMonoObjectMember(obj, "fieldComponent", out fieldComponentObj) || this.TryGetMonoObjectMember(obj, "_fieldComponent", out fieldComponentObj))
            {
                if (this.TryExtractHomePositionMonoObject(fieldComponentObj, out position))
                {
                    return true;
                }
            }
            if (this.TryGetMonoVector3Member(obj, "position", out position))
            {
                return true;
            }
            if (this.TryGetMonoBoundsCenterMember(obj, "Bounds", out position) || this.TryGetMonoBoundsCenterMember(obj, "LocalBounds", out position))
            {
                return true;
            }
            IntPtr entityObj;
            if (this.TryGetMonoObjectMember(obj, "entity", out entityObj) || this.TryGetMonoObjectMember(obj, "_entity", out entityObj))
            {
                if (this.TryExtractHomePositionMonoObject(entityObj, out position))
                {
                    return true;
                }
            }
            IntPtr transformObj;
            if (this.TryGetMonoObjectMember(obj, "transform", out transformObj) || this.TryGetMonoObjectMember(obj, "_transform", out transformObj))
            {
                if (this.TryExtractHomePositionMonoObject(transformObj, out position))
                {
                    return true;
                }
            }
            return false;
        }








        private bool TryResolveSelfPlayerNetId(out uint selfPlayerNetId)
        {
            selfPlayerNetId = 0U;
            if (this.TryResolveSelfPlayerNetIdMono(out selfPlayerNetId))
            {
                return selfPlayerNetId != 0U;
            }
            Type type = this.FindLoadedType("XDTDataAndProtocol.PlayerDataCenter", "PlayerDataCenter");
            if (type == null)
            {
                return false;
            }
            MethodInfo method = type.GetMethod("GetSelfNetPlayerId", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (method == null)
            {
                return false;
            }
            try
            {
                object value = method.Invoke(null, null);
                if (value is uint)
                {
                    selfPlayerNetId = (uint)value;
                    return selfPlayerNetId != 0U;
                }
                selfPlayerNetId = Convert.ToUInt32(value);
                return selfPlayerNetId != 0U;
            }
            catch
            {
                return false;
            }
        }

        private unsafe bool TryResolveSelfPlayerNetIdMono(out uint selfPlayerNetId)
        {
            selfPlayerNetId = 0U;
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoClassFromName == null || auraMonoClassGetMethodFromName == null || auraMonoRuntimeInvoke == null || auraMonoObjectUnbox == null)
            {
                return false;
            }
            IntPtr image = this.FindAuraMonoImage(new string[]
            {
                "XDTDataAndProtocol",
                "XDTDataAndProtocol.dll"
            });
            IntPtr classPtr = image != IntPtr.Zero ? auraMonoClassFromName(image, "XDTDataAndProtocol", "PlayerDataCenter") : IntPtr.Zero;
            if (classPtr == IntPtr.Zero)
            {
                classPtr = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTDataAndProtocol", "PlayerDataCenter");
            }
            if (classPtr == IntPtr.Zero)
            {
                return false;
            }
            IntPtr methodPtr = auraMonoClassGetMethodFromName(classPtr, "GetSelfNetPlayerId", 0);
            if (methodPtr == IntPtr.Zero)
            {
                return false;
            }
            IntPtr exc = IntPtr.Zero;
            IntPtr boxed = auraMonoRuntimeInvoke(methodPtr, IntPtr.Zero, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || boxed == IntPtr.Zero)
            {
                return false;
            }
            IntPtr raw = auraMonoObjectUnbox(boxed);
            if (raw == IntPtr.Zero)
            {
                return false;
            }
            selfPlayerNetId = *(uint*)raw;
            return selfPlayerNetId != 0U;
        }

        private object TryResolveFieldByOwnerId(uint ownerId)
        {
            if (ownerId == 0U)
            {
                return null;
            }

            Type homelandEntitySystemType = this.FindLoadedType("XDTLevelAndEntity.GameplaySystem.HomeLand.HomelandEntitySystem", "HomelandEntitySystem");
            if (homelandEntitySystemType != null)
            {
                MethodInfo getPlayerFieldMethod = homelandEntitySystemType.GetMethod("GetPlayerField", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (getPlayerFieldMethod != null)
                {
                    try
                    {
                        object homelandField = getPlayerFieldMethod.Invoke(null, new object[] { ownerId });
                        if (homelandField != null)
                        {
                            return homelandField;
                        }
                    }
                    catch
                    {
                    }
                }
            }

            Type entitiesType = this.FindLoadedType("XDTLevelAndEntity.BaseSystem.EntitiesManager.Entities", "Entities");
            if (entitiesType == null)
            {
                return null;
            }

            object fieldSystem = null;
            PropertyInfo fieldSystemProperty = entitiesType.GetProperty("fieldSystem", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            if (fieldSystemProperty != null)
            {
                fieldSystem = fieldSystemProperty.GetValue(null, null);
            }
            else
            {
                FieldInfo fieldSystemField = entitiesType.GetField("fieldSystem", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                if (fieldSystemField != null)
                {
                    fieldSystem = fieldSystemField.GetValue(null);
                }
            }

            if (fieldSystem == null)
            {
                return null;
            }

            MethodInfo getFieldByOwnerIdMethod = fieldSystem.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance).FirstOrDefault(delegate(MethodInfo m)
            {
                if (m.Name != "GetFieldByOwnerId")
                {
                    return false;
                }
                ParameterInfo[] parameters = m.GetParameters();
                return parameters.Length == 1 && parameters[0].ParameterType == typeof(uint);
            });
            if (getFieldByOwnerIdMethod == null)
            {
                return null;
            }

            try
            {
                return getFieldByOwnerIdMethod.Invoke(fieldSystem, new object[] { ownerId });
            }
            catch
            {
                return null;
            }
        }

        private unsafe bool TryResolveCurrentHomeNetIdMono(out uint homeNetId)
        {
            homeNetId = 0U;
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoClassFromName == null || auraMonoClassGetFieldFromName == null || auraMonoClassVtable == null || auraMonoFieldStaticGetValue == null || this.auraMonoRootDomain == IntPtr.Zero)
            {
                return false;
            }
            IntPtr image = this.FindAuraMonoImage(new string[]
            {
                "XDTDataAndProtocol",
                "XDTDataAndProtocol.dll"
            });
            IntPtr classPtr = image != IntPtr.Zero ? auraMonoClassFromName(image, "XDTDataAndProtocol", "PlayerDataCenter") : IntPtr.Zero;
            if (classPtr == IntPtr.Zero)
            {
                classPtr = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTDataAndProtocol", "PlayerDataCenter");
            }
            if (classPtr == IntPtr.Zero)
            {
                return false;
            }
            IntPtr fieldPtr = auraMonoClassGetFieldFromName(classPtr, "homeNetId");
            if (fieldPtr == IntPtr.Zero)
            {
                return false;
            }
            IntPtr vtable = auraMonoClassVtable(this.auraMonoRootDomain, classPtr);
            if (vtable == IntPtr.Zero)
            {
                return false;
            }
            uint value = 0U;
            auraMonoFieldStaticGetValue(vtable, fieldPtr, (IntPtr)(&value));
            homeNetId = value;
            return homeNetId != 0U;
        }

        private bool TryExtractHomePosition(object fieldComponent, out Vector3 position)
        {
            position = Vector3.zero;
            if (fieldComponent == null)
            {
                return false;
            }
            object homelandFieldComponent;
            if (this.TryGetObjectMember(fieldComponent, "fieldComponent", out homelandFieldComponent) && homelandFieldComponent != null)
            {
                if (this.TryExtractHomePosition(homelandFieldComponent, out position))
                {
                    return true;
                }
            }
            object obj;
            if (this.TryGetObjectMember(fieldComponent, "Bounds", out obj) && obj is Bounds)
            {
                Bounds bounds = (Bounds)obj;
                if (bounds.size.sqrMagnitude > 0.001f)
                {
                    position = bounds.center;
                    return true;
                }
            }
            Component component = fieldComponent as Component;
            if (component != null)
            {
                position = component.transform.position;
                return true;
            }
            object obj2;
            if (this.TryGetObjectMember(fieldComponent, "entity", out obj2))
            {
                object obj3;
                if (this.TryGetObjectMember(obj2, "position", out obj3) && obj3 is Vector3)
                {
                    position = (Vector3)obj3;
                    return true;
                }
                object obj4;
                if (this.TryGetObjectMember(obj2, "transform", out obj4) && this.TryExtractHomePosition(obj4, out position))
                {
                    return true;
                }
            }
            return false;
        }





        internal void ClearModReflectionLookupMissCaches()
        {
            this.loadedTypeMissCacheUntil.Clear();
            this.methodMissCacheUntil.Clear();
        }



        internal bool ModTryGetManagedSelfPlayerObject(out object playerObj, out string source) =>
            this.TryGetManagedSelfPlayerObject(out playerObj, out source);

        internal bool ModTryInvokeInstanceMethod(object instance, string methodName, params object[] args)
        {
            if (instance == null || string.IsNullOrEmpty(methodName))
            {
                return false;
            }

            try
            {
                Type type = instance.GetType();
                Type[] argTypes = args == null || args.Length == 0
                    ? Type.EmptyTypes
                    : args.Select(a => a?.GetType() ?? typeof(object)).ToArray();
                MethodInfo method = this.GetMethodQuiet(
                    type,
                    methodName,
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                    argTypes);
                if (method == null && (args == null || args.Length == 0))
                {
                    method = this.GetMethodQuiet(
                        type,
                        methodName,
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                        Type.EmptyTypes);
                }

                if (method == null)
                {
                    return false;
                }

                method.Invoke(instance, args);
                return true;
            }
            catch
            {
                return false;
            }
        }











































        private static string BuildMethodLookupCacheKey(Type type, string name, BindingFlags flags, Type[] parameterTypes, int paramCountOnly)
        {
            if (type == null || string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }

            string typeName = type.FullName ?? type.Name ?? type.GetHashCode().ToString();
            if (paramCountOnly >= 0)
            {
                return typeName + "|pc:" + paramCountOnly + "|" + name + "|" + (int)flags;
            }

            Type[] types = parameterTypes ?? Type.EmptyTypes;
            if (types.Length == 0)
            {
                return typeName + "|sig:|" + name + "|" + (int)flags;
            }

            StringBuilder sb = new StringBuilder(typeName.Length + name.Length + (types.Length * 16));
            sb.Append(typeName).Append("|sig:").Append(name).Append('|').Append((int)flags);
            for (int i = 0; i < types.Length; i++)
            {
                Type pt = types[i];
                sb.Append('|').Append(pt?.FullName ?? "_");
            }

            return sb.ToString();
        }

        private MethodInfo ResolveCachedMethodQuiet(Type type, string name, BindingFlags flags, Type[] parameterTypes)
        {
            if (type == null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            Type[] resolvedParameterTypes = parameterTypes ?? Type.EmptyTypes;
            const BindingFlags flatten = BindingFlags.FlattenHierarchy;
            BindingFlags resolvedFlags = flags | flatten;
            string cacheKey = BuildMethodLookupCacheKey(type, name, resolvedFlags, resolvedParameterTypes, -1);
            if (!string.IsNullOrEmpty(cacheKey))
            {
                if (this.methodLookupCache.TryGetValue(cacheKey, out MethodInfo cachedMethod))
                {
                    return cachedMethod;
                }

                if (this.methodMissCacheUntil.TryGetValue(cacheKey, out float missCacheUntil))
                {
                    if (Time.unscaledTime < missCacheUntil)
                    {
                        return null;
                    }

                    this.methodMissCacheUntil.Remove(cacheKey);
                }
            }

            MethodInfo method = type.GetMethod(name, resolvedFlags, null, resolvedParameterTypes, null);
            if (!string.IsNullOrEmpty(cacheKey))
            {
                if (method != null)
                {
                    this.methodLookupCache[cacheKey] = method;
                    this.methodMissCacheUntil.Remove(cacheKey);
                }
                else
                {
                    this.methodMissCacheUntil[cacheKey] = Time.unscaledTime + LoadedMethodMissCacheSeconds;
                }
            }

            return method;
        }

        private MethodInfo ResolveCachedMethodByParamCountQuiet(Type type, string name, int paramCount)
        {
            if (type == null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static
                | BindingFlags.Instance | BindingFlags.FlattenHierarchy;
            string cacheKey = BuildMethodLookupCacheKey(type, name, flags, null, paramCount);
            if (!string.IsNullOrEmpty(cacheKey))
            {
                if (this.methodLookupCache.TryGetValue(cacheKey, out MethodInfo cachedMethod))
                {
                    return cachedMethod;
                }

                if (this.methodMissCacheUntil.TryGetValue(cacheKey, out float missCacheUntil))
                {
                    if (Time.unscaledTime < missCacheUntil)
                    {
                        return null;
                    }

                    this.methodMissCacheUntil.Remove(cacheKey);
                }
            }

            MethodInfo resolved = null;
            MethodInfo[] methods = type.GetMethods(flags);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo candidate = methods[i];
                if (!string.Equals(candidate.Name, name, StringComparison.Ordinal))
                {
                    continue;
                }

                ParameterInfo[] parameters = candidate.GetParameters();
                if (parameters != null && parameters.Length == paramCount)
                {
                    resolved = candidate;
                    break;
                }
            }

            if (!string.IsNullOrEmpty(cacheKey))
            {
                if (resolved != null)
                {
                    this.methodLookupCache[cacheKey] = resolved;
                    this.methodMissCacheUntil.Remove(cacheKey);
                }
                else
                {
                    this.methodMissCacheUntil[cacheKey] = Time.unscaledTime + LoadedMethodMissCacheSeconds;
                }
            }

            return resolved;
        }

        private MethodInfo GetMethodQuiet(Type type, string name, BindingFlags flags, Type[] parameterTypes)
        {
            return this.ResolveCachedMethodQuiet(type, name, flags, parameterTypes);
        }

        private MethodInfo GetMethodByNameAndParamCountQuiet(Type type, string name, int paramCount)
        {
            return this.ResolveCachedMethodByParamCountQuiet(type, name, paramCount);
        }

        private string DescribeType(Type type)
        {
            if (type == null)
            {
                return "null";
            }

            string assemblyName = string.Empty;
            try
            {
                Assembly ownerAssembly = type.Assembly;
                assemblyName = ownerAssembly != null ? ownerAssembly.GetName().Name : string.Empty;
            }
            catch
            {
            }

            return string.IsNullOrEmpty(assemblyName)
                ? (type.FullName ?? type.Name ?? "unknown")
                : (type.FullName ?? type.Name ?? "unknown") + "@" + assemblyName;
        }




        // Token: 0x06000026 RID: 38 RVA: 0x00006E94 File Offset: 0x00005094
        private void InspectPlayerComponents()
        {
            GameObject gameObject = GameObject.Find("p_player_skeleton(Clone)");
            bool flag = gameObject == null;
            if (flag)
            {
                ModLogger.Msg("Player not found!");
            }
            else
            {
                ModLogger.Msg("=== PLAYER COMPONENTS (Il2Cpp) ===");
                Component[] array = gameObject.GetComponents<Component>();
                foreach (Component component in array)
                {
                    bool flag2 = component == null;
                    if (!flag2)
                    {
                        try
                        {
                            string fullName = component.GetType().FullName;
                            ModLogger.Msg("GetType().FullName: " + fullName);
                            Il2CppType il2CppType = component.GetIl2CppType();
                            bool flag3 = il2CppType != null;
                            if (flag3)
                            {
                                ModLogger.Msg("Il2CppType.Name: " + il2CppType.Name);
                                ModLogger.Msg("Il2CppType.FullName: " + il2CppType.FullName);
                            }
                            Type baseType = component.GetType().BaseType;
                            bool flag4 = baseType != null;
                            if (flag4)
                            {
                                ModLogger.Msg("BaseType: " + baseType.FullName);
                            }
                            ModLogger.Msg("comp.name: " + component.name);
                            ModLogger.Msg("---");
                        }
                        catch (Exception ex)
                        {
                            ModLogger.Msg("Error inspecting component: " + ex.Message);
                        }
                    }
                }
                ModLogger.Msg("=== END ===");
            }
        }

        // --- PATROL SYSTEM METHODS ---



        private void RunSpamClicker()
        {
            // Click buttons by path
            foreach (string path in workPaths)
            {
                ClickButtonIfExists(path);
            }
            ClickCookingCleanupThrottled(0.45f);
        }

        // --- FORCE CLOSE MENU ---
        private void ForceCloseMenuIfOpen()
        {
            try
            {
                GameObject cookPanel = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Full/CookPanel(Clone)");

                if (cookPanel != null && cookPanel.activeInHierarchy)
                {
                    // Method 1: Find UI Button
                    bool buttonFound = false;
                    Button[] buttons = cookPanel.GetComponentsInChildren<Button>(true);
                    if (buttons != null)
                    {
                        foreach (Button btn in buttons)
                        {
                            if (btn == null) continue;
                            try
                            {
                                string n = btn.name.ToLower();
                                if (n.Contains("close") || n.Contains("back") || n.Contains("exit") || n.Contains("return"))
                                {
                                    if (btn.interactable)
                                    {
                                        btn.onClick.Invoke();
                                        buttonFound = true;
                                        break;
                                    }
                                }
                            }
                            catch { }
                        }
                    }

                    // Method 2: Send ESC if button fail
                    if (!buttonFound)
                    {
                        SendEscMessage();
                    }
                }
            }
            catch { }
        }

        private void SendEscMessage()
        {
            try
            {
                IntPtr hWnd = GetForegroundWindow();
                if (hWnd != IntPtr.Zero)
                {
                    PostMessage(hWnd, WM_KEYDOWN, (IntPtr)VK_ESCAPE, IntPtr.Zero);
                    PostMessage(hWnd, WM_KEYUP, (IntPtr)VK_ESCAPE, IntPtr.Zero);
                }
            }
            catch { }
        }

        private void SendEnterMessage()
        {
            try
            {
                IntPtr hWnd = GetForegroundWindow();
                if (hWnd != IntPtr.Zero)
                {
                    PostMessage(hWnd, WM_KEYDOWN, (IntPtr)0x0D, IntPtr.Zero);
                    PostMessage(hWnd, WM_KEYUP, (IntPtr)0x0D, IntPtr.Zero);
                }
            }
            catch { }
        }

        private void SendFMessage()
        {
            // Prefer using simulated F-key flags (safer for in-game automation); fallback to native input if needed
            try
            {
                this.SimulateFKeyPulse(0.12f);
                return;
            }
            catch { }
            try
            {
                // Use SendInput for better compatibility if simulation fails
                INPUT[] inputs = new INPUT[2];
                inputs[0].type = 1; // INPUT_KEYBOARD
                inputs[0].u.ki.wVk = VK_F;
                inputs[0].u.ki.dwFlags = 0; // KEYEVENTF_KEYDOWN

                inputs[1].type = 1; // INPUT_KEYBOARD
                inputs[1].u.ki.wVk = VK_F;
                inputs[1].u.ki.dwFlags = KEYEVENTF_KEYUP;

                SendInput(2, inputs, Marshal.SizeOf(typeof(INPUT)));
            }
            catch { }
        }

        private void UpdateBottomDialogAutoClicker()
        {
            bool automationWantsDialogClick =
                this.autoBuyEnabled ||
                this.autoBuyBirdEnabled ||
                this.autoBuyGardenEnabled ||
                this.autoBuyFishingEnabled ||
                this.autoCookEnabled;
            GameObject bottomDialog = this.cachedBottomDialogObject;
            if (bottomDialog == null && !automationWantsDialogClick)
            {
                this.bottomDialogClickTimer = 0f;
                return;
            }

            if (bottomDialog == null && Time.unscaledTime >= this.nextBottomDialogLookupAt)
            {
                this.nextBottomDialogLookupAt = Time.unscaledTime + 0.5f;
                bottomDialog = GameObject.Find(BOTTOM_DIALOG_PATH);
                this.cachedBottomDialogObject = bottomDialog;
            }

            if (bottomDialog == null || !bottomDialog.activeInHierarchy)
            {
                if (bottomDialog != null && !bottomDialog.activeInHierarchy)
                {
                    this.cachedBottomDialogObject = null;
                }
                this.bottomDialogClickTimer = 0f;
                return;
            }

            this.bottomDialogClickTimer += Time.unscaledDeltaTime;
            if (this.bottomDialogClickTimer < BOTTOM_DIALOG_CLICK_INTERVAL)
            {
                return;
            }

            this.bottomDialogClickTimer = 0f;
            this.TryExecuteUiPointerClick(new Vector2((float)Screen.width / 2f, (float)Screen.height * 0.92f));
        }

        private bool TryExecuteUiPointerClick(Vector2 screenPosition)
        {
            EventSystem eventSystem = EventSystem.current;
            if (eventSystem == null || !eventSystem.enabled)
            {
                return false;
            }

            PointerEventData pointerData = new PointerEventData(eventSystem);
            pointerData.button = PointerEventData.InputButton.Left;
            pointerData.position = screenPosition;

            Il2CppSystem.Collections.Generic.List<RaycastResult> hits = new Il2CppSystem.Collections.Generic.List<RaycastResult>();
            eventSystem.RaycastAll(pointerData, hits);
            if (hits.Count <= 0)
            {
                return false;
            }

            ExecuteEvents.Execute<IPointerClickHandler>(hits[0].gameObject, pointerData, ExecuteEvents.pointerClickHandler);
            return true;
        }

        private void SimulateFKeyPulse(float holdSeconds = 0.12f)
        {
            try
            {
                this.EnsureInputSimPatched();
                HeartopiaComplete.SimulateFKeyDown = true;
                HeartopiaComplete.SimulateFKeyHeld = true;
                HeartopiaComplete.SimulateFKeyUp = false;
                this.nextSimulatedFKeyClearAt = Time.unscaledTime + Mathf.Max(0.02f, holdSeconds);
            }
            catch { }
        }

        // Directly simulate an interact (F) press and try to click in-game interact buttons
        private void DirectClickInteractButton()
        {
            try
            {
                try
                {
                    IntPtr hwnd = System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                    if (hwnd == IntPtr.Zero)
                    {
                        hwnd = FindWindow("UnityWndClass", null);
                    }
                    if (hwnd != IntPtr.Zero)
                    {
                        IntPtr lParamDown = new IntPtr(2162689);
                        IntPtr lParamUp = new IntPtr(-1071579135);
                        PostMessage(hwnd, 256U, new IntPtr(70), lParamDown);
                        PostMessage(hwnd, 257U, new IntPtr(70), lParamUp);
                    }
                }
                catch {}

                string[] paths = new string[] {
                    "GameApp/startup_root(Clone)/XDUIRoot/Status/StatusPanel(Clone)/AniRoot@ani@queueanimation/right_layout@ani/middle_right_layout@go/skill_bar@w@go/skill_bar@go/main_joy@go@w/Joy@ani",
                    "GameApp/startup_root(Clone)/XDUIRoot/Status/StatusPanel(Clone)/AniRoot@ani@queueanimation/right_layout@ani/middle_right_layout@go/skill_bar@w@go/skill_bar@go/main_joy@go@w/Joy@ani/stick@frame/normal",
                    "GameApp/startup_root(Clone)/XDUIRoot/Status/StatusPanel(Clone)/AniRoot@ani@queueanimation/right_layout@ani/middle_right_layout@go/skill_bar@w@go/skill_bar@go/main_joy@go@w",
                    "GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)/tracking_bar@w/tracking_chop@list/IconsBarWidget(Clone)/root_visible@go/cells@t/cells@list/CommonIconForInteract(Clone)/root_visible@go/icon@img@btn",
                    "GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)/tracking_bar@w/tracking_mine@list/IconsBarWidget(Clone)/root_visible@go/cells@t/cells@list/CommonIconForInteract(Clone)/root_visible@go/icon@img@btn",
                    "GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)/tracking_bar@w/tracking_common@list/IconsBarWidget(Clone)/root_visible@go/cells@t/cells@list/CommonIconForInteract(Clone)/root_visible@go/icon@img@btn",
                    "GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)/tracking_bar@w/quick_action@btn",
                    "GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)/tracking_bar@w/tracking_harvest@list/IconsBarWidget(Clone)/root_visible@go/cells@t/cells@list/CommonIconForInteract(Clone)/root_visible@go/icon@img@btn"
                };
                foreach (string p in paths)
                {
                    GameObject btn = GameObject.Find(p);
                    if (btn != null && btn.activeInHierarchy)
                    {
                        DirectClickGameButton(btn);
                    }
                }
            }
            catch {}
        }

        private void DirectClickGameButton(GameObject buttonObj)
        {
            try
            {
                RectTransform rt = buttonObj.GetComponent<RectTransform>();
                Vector2 pos = Vector2.zero;
                if (rt != null)
                {
                    Vector3 worldPos = rt.position;
                    pos = new Vector2(worldPos.x, worldPos.y);
                }
                var pointer = new PointerEventData(EventSystem.current);
                pointer.button = PointerEventData.InputButton.Left;
                pointer.position = pos;
                pointer.pressPosition = pos;
                pointer.pointerPress = buttonObj;
                pointer.rawPointerPress = buttonObj;
                pointer.pointerEnter = buttonObj;
                pointer.clickCount = 1;
                pointer.eligibleForClick = true;
                ExecuteEvents.Execute<IPointerEnterHandler>(buttonObj, pointer, ExecuteEvents.pointerEnterHandler);
                ExecuteEvents.Execute<IPointerDownHandler>(buttonObj, pointer, ExecuteEvents.pointerDownHandler);
                ExecuteEvents.Execute<IPointerUpHandler>(buttonObj, pointer, ExecuteEvents.pointerUpHandler);
                ExecuteEvents.Execute<IPointerClickHandler>(buttonObj, pointer, ExecuteEvents.pointerClickHandler);
                Button b = buttonObj.GetComponent<Button>();
                if (b != null && b.interactable)
                {
                    try { b.onClick.Invoke(); } catch {}
                }
            }
            catch {}
        }

        private void SendLeftClickMessage()
        {
            try
            {
                IntPtr hWnd = GetForegroundWindow();
                if (hWnd != IntPtr.Zero)
                {
                    PostMessage(hWnd, WM_LBUTTONDOWN, (IntPtr)MK_LBUTTON, IntPtr.Zero);
                    PostMessage(hWnd, WM_LBUTTONUP, IntPtr.Zero, IntPtr.Zero);
                }
            }
            catch { }
        }

        private void SendLeftClickInputTap()
        {
            try
            {
                mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, UIntPtr.Zero);
                mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, UIntPtr.Zero);
            }
            catch
            {
                this.SendLeftClickMessage();
            }
        }

        private void RunAntiAfkTick()
        {
            if (!this.antiAfkEnabled) return;
            if (this.showMenu) return;
            if (Time.unscaledTime - this.lastAntiAfkPulseAt < Mathf.Max(5f, this.antiAfkInterval)) return;

            this.lastAntiAfkPulseAt = Time.unscaledTime;
            this.TryExecuteUiPointerClick(new Vector2((float)Screen.width / 2f, (float)Screen.height * 0.92f));
            this.antiAfkMouseDownClearAt = Time.unscaledTime + 0.05f;
            this.antiAfkMouseHoldClearAt = Time.unscaledTime + 0.12f;
        }


        // Cached local player lookup to avoid expensive per-frame scans.
        private static GameObject cachedLocalPlayer = null;
        private static float lastLocalPlayerCheckTime = -999f;
        private const float LOCAL_PLAYER_CACHE_INTERVAL = 1f; // seconds

        // Return the local player's skeleton GameObject (`p_player_skeleton(Clone)`).
        // Resolved with a targeted GameObject.Find. NOTE: the game does NOT parent a Camera under the
        // player skeleton on this build — the Main Camera lives under `GameApp/startup_root(Clone)` —
        // so the old FindObjectsOfType + GetComponentInChildren<Camera> "local player" disambiguation
        // never matched and always fell through to this same GameObject.Find (verified via in-game
        // hierarchy diagnostics: hasCamera=False, 1 match, ~3000 objects scanned for nothing). The full
        // scene scan was dropped. In multiplayer with several p_player_skeleton(Clone) this returns the
        // first match; correct local-player disambiguation would need to map the selfPlayer ECS entity
        // to its GameObject.
        public static GameObject GetLocalPlayer()
        {
            // Quick return if cached and valid
            try
            {
                if (cachedLocalPlayer != null && cachedLocalPlayer.activeInHierarchy)
                {
                    return cachedLocalPlayer;
                }
            }
            catch
            {
                cachedLocalPlayer = null;
            }

            // Throttle re-resolves to once per interval REGARDLESS of cache state, so a missing player
            // (world loading, between worlds, despawned) doesn't hit GameObject.Find every call from the
            // hot Transform.position / CharacterController.Move patches. A miss returns the (null/stale)
            // cache and retries at most once per second.
            if (Time.unscaledTime - lastLocalPlayerCheckTime < LOCAL_PLAYER_CACHE_INTERVAL)
            {
                return cachedLocalPlayer;
            }

            lastLocalPlayerCheckTime = Time.unscaledTime;
            cachedLocalPlayer = GameObject.Find("p_player_skeleton(Clone)");
            return cachedLocalPlayer;
        }

        private GameObject GetPlayer() => GetLocalPlayer();

        // Returns the player's root GameObject if available (fallback to GetPlayer)
        private GameObject FindPlayerRoot()
        {
            try
            {
                GameObject p = GetPlayer();
                if (p == null) return null;
                if (p.transform == null) return p;
                Transform root = p.transform.root;
                if (root != null && root.gameObject != null) return root.gameObject;
                return p;
            }
            catch
            {
                return GetPlayer();
            }
        }



        // --- Auto Buy helpers + logic ---





        private bool TryCloseAlertRewardPopupViaTipManager()
        {
            try
            {
                if (this.TryCloseAlertRewardPopupViaTipManagerMono())
                {
                    return true;
                }

                // Resolve types and methods once and cache them (avoids repeated FindLoadedType + GetMethods per 0.12s call)
                if (!this.tipManagerReflectionInitialized)
                {
                    this.tipManagerReflectionInitialized = true;
                    this.cachedTipManagerType = this.FindLoadedType("XDTGame.UI.Panel.Tip.TipManager", "TipManager");
                    this.cachedAlertRewardPanelType = this.FindLoadedType("XDTGame.UI.Panel.AlertRewardPanel", "AlertRewardPanel");
                    if (this.cachedTipManagerType != null)
                    {
                        this.cachedTipInstanceProp = this.cachedTipManagerType.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static);
                        this.cachedGetTipByTypeMethod = this.cachedTipManagerType.GetMethod("GetTip", BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(Type) }, null);
                        this.cachedTipPanelField = this.cachedTipManagerType.GetField("_tipPanel", BindingFlags.Instance | BindingFlags.NonPublic);
                        if (this.cachedAlertRewardPanelType != null)
                        {
                            foreach (MethodInfo method in this.cachedTipManagerType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                            {
                                if (method.Name == "GetTip" && method.IsGenericMethodDefinition && method.GetParameters().Length == 0)
                                    this.cachedGetTipGenericMethod = method.MakeGenericMethod(this.cachedAlertRewardPanelType);
                                else if (method.Name == "CloseTip" && method.IsGenericMethodDefinition && method.GetParameters().Length == 0)
                                    this.cachedCloseTipGenericMethod = method.MakeGenericMethod(this.cachedAlertRewardPanelType);
                            }
                            this.cachedAlertPanelClearMethod = this.cachedAlertRewardPanelType.GetMethod("Clear", BindingFlags.Public | BindingFlags.Instance);
                        }
                    }
                }

                Type tipManagerType = this.cachedTipManagerType;
                Type alertRewardPanelType = this.cachedAlertRewardPanelType;
                if (tipManagerType == null || alertRewardPanelType == null)
                {
                    return false;
                }

                PropertyInfo instanceProp = this.cachedTipInstanceProp;
                object tipManager = instanceProp != null ? instanceProp.GetValue(null, null) : null;
                if (tipManager == null)
                {
                    return false;
                }

                MethodInfo getTipByTypeMethod = this.cachedGetTipByTypeMethod;
                object liveTip = getTipByTypeMethod != null ? getTipByTypeMethod.Invoke(tipManager, new object[] { alertRewardPanelType }) : null;
                if (liveTip != null)
                {
                    MethodInfo closeSelfGeneric = liveTip.GetType().GetMethod("Close", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (closeSelfGeneric != null && closeSelfGeneric.IsGenericMethodDefinition)
                    {
                        closeSelfGeneric.MakeGenericMethod(alertRewardPanelType).Invoke(liveTip, null);
                        return true;
                    }
                }

                MethodInfo getTipMethod = this.cachedGetTipGenericMethod;
                MethodInfo closeTipMethod = this.cachedCloseTipGenericMethod;

                if (getTipMethod != null)
                {
                    object tip = getTipMethod.Invoke(tipManager, null);
                    if (tip != null)
                    {
                        MethodInfo clearMethod = this.cachedAlertPanelClearMethod;
                        if (clearMethod != null)
                        {
                            clearMethod.Invoke(tip, null);
                        }
                    }
                }

                if (closeTipMethod != null)
                {
                    closeTipMethod.Invoke(tipManager, null);
                    return true;
                }

                FieldInfo tipPanelField = this.cachedTipPanelField;
                object tipPanel = tipPanelField != null ? tipPanelField.GetValue(tipManager) : null;
                if (tipPanel != null)
                {
                    FieldInfo tipClassifiesField = tipPanel.GetType().GetField("_tipClassifies", BindingFlags.Instance | BindingFlags.NonPublic);
                    System.Collections.IDictionary tipClassifies = tipClassifiesField != null ? tipClassifiesField.GetValue(tipPanel) as System.Collections.IDictionary : null;
                    if (tipClassifies != null)
                    {
                        foreach (object classifyObj in tipClassifies.Values)
                        {
                            if (classifyObj == null) continue;
                            MethodInfo closeTipByType = classifyObj.GetType().GetMethod("CloseTip", BindingFlags.Public | BindingFlags.Instance, null, new Type[] { typeof(Type) }, null);
                            if (closeTipByType == null) continue;
                            closeTipByType.Invoke(classifyObj, new object[] { alertRewardPanelType });
                        }

                        if (getTipByTypeMethod != null)
                        {
                            object stillOpen = getTipByTypeMethod.Invoke(tipManager, new object[] { alertRewardPanelType });
                            if (stillOpen == null)
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            catch { }

            return false;
        }

        private unsafe bool TryCloseAlertRewardPopupViaTipManagerMono()
        {
            try
            {
                this.ResolveAuraFarmRuntimeMethodsViaMono();
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoClassFromName == null || auraMonoClassGetMethodFromName == null || auraMonoRuntimeInvoke == null || auraMonoStringNew == null || this.auraMonoTypeGetTypeMethodPtr == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr levelImage = this.FindAuraMonoImage(new string[]
                {
                    "XDTLevelAndEntity",
                    "XDTLevelAndEntity.dll"
                });
                IntPtr tipManagerClass = levelImage != IntPtr.Zero ? auraMonoClassFromName(levelImage, "XDTGame.UI.Panel.Tip", "TipManager") : IntPtr.Zero;
                if (tipManagerClass == IntPtr.Zero)
                {
                    tipManagerClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTGame.UI.Panel.Tip", "TipManager");
                }

                IntPtr alertRewardPanelClass = levelImage != IntPtr.Zero ? auraMonoClassFromName(levelImage, "XDTGame.UI.Panel", "AlertRewardPanel") : IntPtr.Zero;
                if (alertRewardPanelClass == IntPtr.Zero)
                {
                    alertRewardPanelClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTGame.UI.Panel", "AlertRewardPanel");
                }

                if (tipManagerClass == IntPtr.Zero || alertRewardPanelClass == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr getInstanceMethod = auraMonoClassGetMethodFromName(tipManagerClass, "get_Instance", 0);
                IntPtr getTipByTypeMethod = auraMonoClassGetMethodFromName(tipManagerClass, "GetTip", 1);
                IntPtr clearMethod = auraMonoClassGetMethodFromName(alertRewardPanelClass, "Clear", 0);
                IntPtr displayMethod = auraMonoClassGetMethodFromName(alertRewardPanelClass, "Display", 0);
                if (getInstanceMethod == IntPtr.Zero || getTipByTypeMethod == IntPtr.Zero || clearMethod == IntPtr.Zero || displayMethod == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr tipManagerObj = IntPtr.Zero;
                IntPtr exc = IntPtr.Zero;
                tipManagerObj = auraMonoRuntimeInvoke(getInstanceMethod, IntPtr.Zero, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero || tipManagerObj == IntPtr.Zero)
                {
                    return false;
                }

                string[] typeNameCandidates = new string[]
                {
                    "XDTGame.UI.Panel.AlertRewardPanel, XDTLevelAndEntity",
                    "XDTGame.UI.Panel.AlertRewardPanel, XDTLevelAndEntity.dll"
                };

                IntPtr typeObj = IntPtr.Zero;
                for (int i = 0; i < typeNameCandidates.Length && typeObj == IntPtr.Zero; i++)
                {
                    IntPtr typeNameStr = auraMonoStringNew(this.auraMonoRootDomain, typeNameCandidates[i]);
                    if (typeNameStr == IntPtr.Zero)
                    {
                        continue;
                    }

                    exc = IntPtr.Zero;
                    IntPtr* typeArgs = stackalloc IntPtr[1];
                    typeArgs[0] = typeNameStr;
                    typeObj = auraMonoRuntimeInvoke(this.auraMonoTypeGetTypeMethodPtr, IntPtr.Zero, (IntPtr)typeArgs, ref exc);
                    if (exc != IntPtr.Zero)
                    {
                        typeObj = IntPtr.Zero;
                    }
                }

                if (typeObj == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr tipObj = IntPtr.Zero;
                exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = typeObj;
                tipObj = auraMonoRuntimeInvoke(getTipByTypeMethod, tipManagerObj, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero || tipObj == IntPtr.Zero)
                {
                    return false;
                }

                exc = IntPtr.Zero;
                auraMonoRuntimeInvoke(clearMethod, tipObj, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero)
                {
                    return false;
                }

                exc = IntPtr.Zero;
                auraMonoRuntimeInvoke(displayMethod, tipObj, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero)
                {
                    return false;
                }

                exc = IntPtr.Zero;
                IntPtr stillOpen = auraMonoRuntimeInvoke(getTipByTypeMethod, tipManagerObj, (IntPtr)args, ref exc);
                return exc == IntPtr.Zero && stillOpen == IntPtr.Zero;
            }
            catch
            {
                return false;
            }
        }

        private bool DirectClickGameButtonReturn(GameObject buttonObj)
        {
            if (buttonObj == null || !buttonObj.activeInHierarchy)
            {
                return false;
            }

            try
            {
                this.DirectClickGameButton(buttonObj);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private bool TryInvokeAlertRewardPanelConfirm(GameObject rewardPanel)
        {
            if (rewardPanel == null)
            {
                return false;
            }

            try
            {
                Transform[] chain = new Transform[]
                {
                    rewardPanel.transform,
                    rewardPanel.transform.parent,
                    rewardPanel.transform.parent != null ? rewardPanel.transform.parent.parent : null
                };

                for (int i = 0; i < chain.Length; i++)
                {
                    Transform tr = chain[i];
                    if (tr == null) continue;
                    Component[] components = tr.GetComponents<Component>();
                    for (int j = 0; j < components.Length; j++)
                    {
                        Component component = components[j];
                        if (component == null) continue;
                        Type type = component.GetType();
                        string typeName = type.Name ?? string.Empty;
                        if (!typeName.Equals("AlertRewardPanel", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        MethodInfo clickConfirm = type.GetMethod("ClickConfirm", BindingFlags.Instance | BindingFlags.NonPublic);
                        if (clickConfirm != null)
                        {
                            ParameterInfo[] parameters = clickConfirm.GetParameters();
                            object[] args = parameters.Length == 1 ? new object[] { null } : null;
                            clickConfirm.Invoke(component, args);
                            return true;
                        }

                        MethodInfo display = type.GetMethod("Display", BindingFlags.Instance | BindingFlags.NonPublic);
                        if (display != null)
                        {
                            display.Invoke(component, null);
                            return true;
                        }
                    }
                }
            }
            catch { }

            return false;
        }


        private bool ClickDialogueOptionByTitle(string title)
        {
            try
            {
                GameObject panel = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Scene/DialoguePanel(Clone)");
                if (panel == null || !panel.activeInHierarchy) return false;
                Transform viewportContent = panel.transform.Find("AniRoot@go@ani/option@w/option@list/Viewport/Content");
                if (viewportContent == null)
                {
                    var allChildren = panel.GetComponentsInChildren<Transform>(true);
                    for (int i = 0; i < allChildren.Length; i++)
                    {
                        var child = allChildren[i];
                        if (child != null && child.name != null && child.name.Contains("ImageTextBtnWidget")) { viewportContent = child.parent; break; }
                    }
                    if (viewportContent == null) return false;
                }
                string lower = title.ToLowerInvariant();
                int childCount = viewportContent.childCount;
                for (int ci = 0; ci < childCount; ci++)
                {
                    Transform cell = viewportContent.GetChild(ci);
                    if (cell == null) continue;
                    var titleTxtTransform = cell.Find("AniRoot@go@ani/cell@btn/title@txt");
                    Text titleTxt = null;
                    if (titleTxtTransform != null) titleTxt = titleTxtTransform.GetComponent<Text>();
                    if (titleTxt == null) titleTxt = cell.GetComponentInChildren<Text>(true);
                    if (titleTxt != null && titleTxt.text != null && titleTxt.text.ToLowerInvariant().Contains(lower))
                    {
                        var btn = cell.GetComponentInChildren<Button>(true);
                        if (btn != null && btn.interactable) { btn.onClick.Invoke(); if (this.autoBuyLogsEnabled) { ModLogger.Msg($"[AutoBuy] Selected dialog option \"{titleTxt.text}\""); } return true; }
                        if (SimulateClick(cell.gameObject)) { if (this.autoBuyLogsEnabled) { ModLogger.Msg($"[AutoBuy] SimClicked dialog option \"{titleTxt.text}\""); } return true; }
                    }
                }
            }
            catch (Exception ex) { LogAutoBuy(" ClickDialogueOptionByTitle error: " + ex.Message); }
            return false;
        }

        private bool ClickDialogueOptionByKeywords(string[] keywords)
        {
            try
            {
                GameObject panel = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Scene/DialoguePanel(Clone)");
                if (panel == null || !panel.activeInHierarchy) return false;

                Transform viewportContent = panel.transform.Find("AniRoot@go@ani/option@w/option@list/Viewport/Content");
                if (viewportContent == null || viewportContent.childCount <= 0) return false;

                int bestIndex = -1;
                int bestScore = int.MinValue;

                for (int ci = 0; ci < viewportContent.childCount; ci++)
                {
                    Transform cell = viewportContent.GetChild(ci);
                    if (cell == null || !cell.gameObject.activeInHierarchy) continue;

                    string textLower = string.Empty;
                    Text titleTxt = cell.Find("AniRoot@go@ani/cell@btn/title@txt")?.GetComponent<Text>() ?? cell.GetComponentInChildren<Text>(true);
                    if (titleTxt != null && !string.IsNullOrEmpty(titleTxt.text))
                    {
                        textLower = titleTxt.text.ToLowerInvariant();
                    }

                    int score = 0;
                    if (!string.IsNullOrEmpty(textLower))
                    {
                        for (int k = 0; k < keywords.Length; k++)
                        {
                            string kw = keywords[k];
                            if (string.IsNullOrEmpty(kw)) continue;
                            string kwLower = kw.ToLowerInvariant();
                            if (textLower.Contains(kwLower)) score += 20;
                        }

                        if (textLower.Contains("cooking store")) score += 100;
                        else if (textLower.Contains("cook") && textLower.Contains("store")) score += 80;
                    }

                    // Icon fallback: shopping/cart-like option often has a cart/store icon.
                    Image[] imgs = cell.GetComponentsInChildren<Image>(true);
                    for (int i = 0; i < imgs.Length; i++)
                    {
                        Image im = imgs[i];
                        if (im == null || im.sprite == null) continue;
                        string sp = im.sprite.name.ToLowerInvariant();
                        if (sp.Contains("shop") || sp.Contains("store") || sp.Contains("cart")) score += 25;
                    }

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestIndex = ci;
                    }
                }

                if (bestIndex < 0 || bestScore <= 0) return false;

                Transform bestCell = viewportContent.GetChild(bestIndex);
                if (bestCell == null) return false;

                Button btn = bestCell.GetComponentInChildren<Button>(true);
                if (btn != null && btn.interactable)
                {
                    btn.onClick.Invoke();
                    LogAutoBuy(" Selected dialog option by keyword score=" + bestScore);
                    return true;
                }

                if (SimulateClick(bestCell.gameObject))
                {
                    LogAutoBuy(" SimClicked dialog option by keyword score=" + bestScore);
                    return true;
                }
            }
            catch (Exception ex)
            {
                LogAutoBuy(" ClickDialogueOptionByKeywords error: " + ex.Message);
            }

            return false;
        }

        private bool HasDialogueOptionsVisible()
        {
            try
            {
                GameObject panel = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Scene/DialoguePanel(Clone)");
                if (panel == null || !panel.activeInHierarchy) return false;

                Transform viewportContent = panel.transform.Find("AniRoot@go@ani/option@w/option@list/Viewport/Content");
                if (viewportContent == null) return false;
                if (viewportContent.childCount <= 0) return false;

                for (int i = 0; i < viewportContent.childCount; i++)
                {
                    Transform cell = viewportContent.GetChild(i);
                    if (cell == null || !cell.gameObject.activeInHierarchy) continue;
                    Text t = cell.GetComponentInChildren<Text>(true);
                    if (t != null && !string.IsNullOrWhiteSpace(t.text)) return true;
                }
            }
            catch { }

            return false;
        }

        private bool TryAdvanceDialogueText()
        {
            try
            {
                GameObject panel = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Scene/DialoguePanel(Clone)");
                if (panel == null || !panel.activeInHierarchy) return false;

                // First try explicit next/continue/skip style buttons.
                Button[] buttons = panel.GetComponentsInChildren<Button>(true);
                for (int i = 0; i < buttons.Length; i++)
                {
                    Button b = buttons[i];
                    if (b == null || !b.interactable || !b.gameObject.activeInHierarchy) continue;
                    string n = (b.name ?? string.Empty).ToLowerInvariant();
                    if (n.Contains("next") || n.Contains("continue") || n.Contains("skip") || n.Contains("content"))
                    {
                        b.onClick.Invoke();
                        return true;
                    }
                }

                // Fallback: click likely dialogue content regions to finish typewriter text.
                string[] clickPaths = new string[]
                {
                    // Exact paths from UI inspector (most reliable for skipping typewriter text)
                    "AniRoot@go@ani/main@go/DialogMsgWidget@go@w/content@go/text@list@t/Viewport/textContent@t/DialogueTextWidget(Clone)/content@txt",
                    "AniRoot@go@ani/main@go/DialogMsgWidget@go@w/content@go/bg",
                    "AniRoot@go@ani/content@w",
                    "AniRoot@go@ani/content@w/content@txt",
                    "AniRoot@go@ani/content@w/content@go",
                    "AniRoot@go@ani/main@go/DialogMsgWidget@go@w/content@go",
                    "AniRoot@go@ani"
                };

                bool clickedAny = false;
                for (int i = 0; i < clickPaths.Length; i++)
                {
                    Transform t = panel.transform.Find(clickPaths[i]);
                    if (t == null || !t.gameObject.activeInHierarchy) continue;
                    if (SimulateClick(t.gameObject)) clickedAny = true;
                }

                if (clickedAny) return true;

                // Broad fallback: find the first interactable button that is NOT inside the options
                // list AND not a back/exit/close button. Catches transparent click-to-advance buttons
                // used by simple farewell dialogues like "Thank you for your patronage."
                Transform optionContent = panel.transform.Find("AniRoot@go@ani/option@w/option@list/Viewport/Content");
                Button[] allButtons = panel.GetComponentsInChildren<Button>(true);
                for (int i = 0; i < allButtons.Length; i++)
                {
                    Button b = allButtons[i];
                    if (b == null || !b.interactable || !b.gameObject.activeInHierarchy) continue;
                    if (optionContent != null && b.transform.IsChildOf(optionContent)) continue;
                    string n = (b.name ?? string.Empty).ToLowerInvariant();
                    if (n.Contains("back") || n.Contains("exit") || n.Contains("close") || n.Contains("cancel")) continue;
                    b.onClick.Invoke();
                    return true;
                }

                return SimulateClick(panel);
            }
            catch { }

            return false;
        }





        // Garden Store version - uses autoBuyGardenShopScrollStep instead of autoBuyShopScrollStep

        // Fishing Store version - uses autoBuyFishingShopScrollStep instead of autoBuyShopScrollStep

        // SalePanel helpers
        private int GetSalePanelCurrentCount(GameObject sale)
        {
            try
            {
                var countControl = sale.transform.Find("AniRoot/popup/content/bottom/countControl@w@go/countControl@go");
                if (countControl == null) countControl = sale.transform.Find("AniRoot/popup/content/bottom/countControl@go");
                if (countControl != null)
                {
                    var texts = countControl.GetComponentsInChildren<Text>(true);
                    foreach (var t in texts)
                    {
                        if (t == null || string.IsNullOrEmpty(t.text)) continue;
                        string s = t.text.Trim();
                        // try parse int
                        int val;
                        if (int.TryParse(s, out val)) return val;
                        // sometimes label shows '1' with spaces
                        string digits = new string(s.Where(c => char.IsDigit(c)).ToArray());
                        if (digits.Length > 0 && int.TryParse(digits, out val)) return val;
                    }
                }
            }
            catch { }
            return -1;
        }

        private int GetSalePanelRemainingStock(GameObject sale)
        {
            try
            {
                // Try the remain@txt@go path first (shows "Remaining: X")
                var remainGo = sale.transform.Find("AniRoot/popup/content/bottom/remain@txt@go");
                if (remainGo != null)
                {
                    var texts = remainGo.GetComponentsInChildren<Text>(true);
                    foreach (var t in texts)
                    {
                        if (t == null || string.IsNullOrEmpty(t.text)) continue;
                        string s = t.text.Trim();
                        // Extract number from "Remaining: 39" format
                        string digits = new string(s.Where(c => char.IsDigit(c)).ToArray());
                        if (digits.Length > 0 && int.TryParse(digits, out int val)) return val;
                    }
                }
            }
            catch { }
            return -1; // Unknown remaining stock
        }

        private bool ClickSaleAddMore(GameObject sale)
        {
            try
            {
                var btn = sale.transform.Find("AniRoot/popup/content/bottom/countControl@w@go/countControl@go/addMore@btn")
                          ?? sale.transform.Find("AniRoot/popup/content/bottom/countControl@go/addMore@btn");
                if (btn != null)
                {
                    var b = btn.GetComponent<Button>();
                    if (b != null && b.interactable) { b.onClick.Invoke(); LogAutoBuy(" Clicked +10"); return true; }
                    if (SimulateClick(btn.gameObject)) { LogAutoBuy(" SimClicked +10"); return true; }
                }
            }
            catch { }
            return false;
        }

        private bool ClickSalePurchase(GameObject sale)
        {
            try
            {
                var buy = sale.transform.Find("AniRoot/popup/operators/buy@btn") ?? sale.transform.Find("AniRoot/popup/operators/buy@btn/buy@btn");
                if (buy != null)
                {
                    var b = buy.GetComponent<Button>() ?? buy.GetComponentInChildren<Button>(true);
                    if (b != null && b.interactable) { b.onClick.Invoke(); LogAutoBuy(" Clicked Purchase"); return true; }
                    if (SimulateClick(buy.gameObject)) { LogAutoBuy(" SimClicked Purchase"); return true; }
                }
            }
            catch { }
            return false;
        }


        // --- Auto Buy Birdwatching Store helpers + logic ---



        // --- Auto Buy Garden Store helpers + logic ---



        // --- Auto Buy Fishing Store helpers + logic ---



        // --- PATROL SAVE/LOAD ---



        private string ExtractJsonVal(string src, string key)
        {
            int start = src.IndexOf(key) + key.Length;
            int end = src.IndexOf(",", start);
            if (end == -1) end = src.IndexOf("}", start);
            return src.Substring(start, end - start).Trim();
        }

        // --- COOKING PATROL SAVE/LOAD ---










        private float ExtractCoordinate(string block, string coord)
        {
            // Match both "x": value and x: value formats
            string pattern = $"\"?{coord}\"?:\\s*([+-]?\\d*\\.?\\d+([eE][+-]?\\d+)?)";
            System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(block, pattern);
            if (match.Success)
            {
                return float.Parse(match.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture);
            }
            throw new FormatException($"Could not find \"{coord}\" coordinate in block: {block}");
        }

        // --- AUTO REPAIR METHODS ---



        private bool IsBagAutomationActiveOrQueued()
        {
            return this.IsAutoRepairActiveOrQueued() || this.IsAutoEatActiveOrQueued();
        }





        void StartRepair(bool bypassDebounce = false)
        {
            if (this.isAutoEating)
            {
                if (!this.pendingAutoRepairRequest)
                {
                    this.pendingAutoRepairRequest = true;
                    this.AutoEatRepairLog("[StartRepair] queued because auto eat is still running");
                }
                else
                {
                    this.AutoEatRepairLog("[StartRepair] ignored duplicate queue request while auto eat is still running");
                }
                return;
            }

            // Debounce: ignore triggers that happen within the configured auto-repair pause window
            float repairStartNow = Time.unscaledTime;
            if (!bypassDebounce && repairStartNow - this.lastRepairTriggerTime < this.resourceAutoRepairPauseSeconds)
            {
                this.AutoEatRepairLog($"[StartRepair] Ignored trigger due to debounce. Now={repairStartNow} LastTrigger={this.lastRepairTriggerTime} Wait={this.resourceAutoRepairPauseSeconds}");
                return;
            }

            // Prevent re-entrancy: if a repair is already running, ignore subsequent starts
            if (this.IsAutoRepairActiveOrQueued())
            {
                this.AutoEatRepairLog("[StartRepair] ignored re-entry (repair already active or queued)");
                return;
            }

            this.lastRepairTriggerTime = repairStartNow;
            this.AutoEatRepairLog($"[StartRepair] invoked at Time.unscaledTime={repairStartNow}");

            bool autoTriggeredRepair = this.lastStartWasAutoRepair;

            // Auto-triggered repair is safer without moving the player because farm teleports
            // or partially loaded terrain can cause a sudden reposition into bad ground.
            if (this.repairTeleportBackEnabled && !autoTriggeredRepair)
            {
                try
                {
                    GameObject p = GetPlayer();
                    if (p != null)
                    {
                        Vector3 cur = p.transform.position;
                        Vector3 back = Vector3.zero;
                        try { back = p.transform.forward; } catch { back = new Vector3(0f, 0f, 1f); }
                        Vector3 target = cur - back.normalized * this.repairTeleportBackDistance;
                        target.y = cur.y; // preserve vertical position
                        this.AutoEatRepairLog($"[StartRepair] Teleporting player backward from {cur} to {target}");
                        TeleportTo(target);
                    }
                }
                catch (Exception ex)
                {
                    this.AutoEatRepairLog("[StartRepair] Teleport backward failed: " + ex.Message);
                }
            }

            // Determine whether this was triggered by durability detection (auto)
            // and initialize auto-repair counters accordingly. We use the
            // `lastStartWasAutoRepair` flag (set by the caller) to detect auto
            // starts; clear it immediately after reading.
            isAutoRepairRunning = autoTriggeredRepair;
            this.lastStartWasAutoRepair = false;
            this.pendingAutoRepairRequest = false;
            autoRepairUseCount = 0;
            autoRepairWaiting = false;
            this.lastRepairUseNetId = 0U;
            this.lastRepairUseCountBefore = 0;
            this.ClearCachedRepairKit();
            this.repairVerifyChecks = 0;
            this.repairUseRetryAttempts = 0;
            repairUsesTarget = Mathf.Clamp(this.autoRepairUseTarget, 1, 3);

            isRepairing = true;
            repairStep = DIRECT_REPAIR_STEP_USE;
            stepTimer = Time.unscaledTime;

            // Pause resource farm teleports while repairing to avoid overlapping actions
            this.resourceRepairPauseUntil = Time.time + this.resourceAutoRepairPauseSeconds;
            InsectNetFarm.NotifyRepairTriggered();
        }

        void StartAutoEat(bool forceSingleUse = false)
        {
            if (this.isRepairing)
            {
                this.pendingAutoEatRequest = true;
                this.AutoEatRepairLog("[AutoEat] StartAutoEat queued because repair is still running.");
                return;
            }

            isAutoEating = true;
            autoEatStep = DIRECT_EAT_STEP_USE;
            autoEatAttempts = 0;
            autoEatForceSingleUse = forceSingleUse;
            autoEatStepTimer = Time.unscaledTime;
            InsectNetFarm.NotifyAutoEatTriggered();
        }

        private void ProcessPendingBagAutomation()
        {
            if (this.isRepairing || this.isAutoEating)
            {
                return;
            }

            if (this.pendingAutoRepairRequest)
            {
                this.pendingAutoRepairRequest = false;
                this.lastStartWasAutoRepair = true;
                this.AutoEatRepairLog("[AutoRepair] Running queued durability repair request.");
                this.StartRepair(true);
                this.resourceRepairPauseUntil = Time.time + this.resourceAutoRepairPauseSeconds;
                return;
            }

            if (this.pendingAutoEatRequest)
            {
                if (!this.autoEatAutoTriggerEnabled || !this.IsEnergyAtOrBelowAutoEatTrigger() || Time.unscaledTime < this.nextAutoEatDirectRetryAt)
                {
                    this.pendingAutoEatRequest = false;
                    return;
                }

                this.pendingAutoEatRequest = false;
                this.AutoEatRepairLog($"[AutoEat] Running queued energy panel request ({this.GetCurrentEnergyDisplay()}, threshold={this.autoEatTriggerPercent}%).");
                this.StartAutoEat();
            }
        }


        void ExecuteRepairStep()
        {
            switch (repairStep)
            {
                case DIRECT_REPAIR_STEP_USE:
                    if (this.TryDirectUseRepairKit())
                    {
                        this.lastRepairUseNetId = this.lastDirectBackpackMatchedNetId;
                        this.lastRepairUseCountBefore = this.lastDirectBackpackMatchedCount;
                        this.repairVerifyChecks = 0;
                        repairStep = DIRECT_REPAIR_STEP_VERIFY;
                        stepTimer = Time.unscaledTime + 1.25f;
                        this.AutoEatRepairLog($"[AutoRepair] Direct repair kit use sent; verifying inventory before counting ({autoRepairUseCount}/{repairUsesTarget}, countBefore={this.lastRepairUseCountBefore}).");
                    }
                    else
                    {
                        this.AutoEatRepairLog("[AutoRepair] Direct repair failed; stopped without opening bag UI.");
                        isRepairing = false;
                        repairStep = 0;
                    }
                    break;

                case DIRECT_REPAIR_STEP_VERIFY:
                    repairVerifyChecks++;
                    if (this.VerifyLastRepairUseSucceeded())
                    {
                        autoRepairUseCount++;
                        repairUseRetryAttempts = 0;
                        this.AutoEatRepairLog($"[AutoRepair] Repair kit use verified ({autoRepairUseCount}/{repairUsesTarget}).");
                        if (autoRepairUseCount < repairUsesTarget)
                        {
                            autoRepairWaiting = true;
                            autoRepairWaitTimer = Time.unscaledTime + autoRepairWaitDuration;
                            stepTimer = autoRepairWaitTimer;
                            repairStep = DIRECT_REPAIR_STEP_WAIT;
                        }
                        else
                        {
                            isRepairing = false;
                            repairStep = 0;
                        }
                    }
                    else if (repairVerifyChecks < 2)
                    {
                        this.AutoEatRepairLog("[AutoRepair] Repair kit use not reflected yet; checking again.");
                        stepTimer = Time.unscaledTime + 1.25f;
                    }
                    else if (repairUseRetryAttempts < 3)
                    {
                        repairUseRetryAttempts++;
                        repairVerifyChecks = 0;
                        this.AutoEatRepairLog($"[AutoRepair] Repair kit was not consumed; retrying use ({repairUseRetryAttempts}/3). Avoid jumping/vehicle while retrying.");
                        repairStep = DIRECT_REPAIR_STEP_USE;
                        stepTimer = Time.unscaledTime + 0.75f;
                    }
                    else
                    {
                        this.AutoEatRepairLog("[AutoRepair] Repair kit was not consumed after retries; stopped to avoid spam.");
                        isRepairing = false;
                        repairStep = 0;
                    }
                    break;

                case DIRECT_REPAIR_STEP_WAIT:
                    if (autoRepairWaiting && Time.unscaledTime >= autoRepairWaitTimer)
                    {
                        autoRepairWaiting = false;
                        repairStep = DIRECT_REPAIR_STEP_USE;
                        stepTimer = Time.unscaledTime;
                    }
                    break;

                default:
                    this.AutoEatRepairLog("[AutoRepair] Unknown repair step " + repairStep + "; stopping.");
                    isRepairing = false;
                    repairStep = 0;
                    break;
            }
        }

        void ExecuteAutoEatStep()
        {
            switch (autoEatStep)
            {
                case DIRECT_EAT_STEP_USE:
                    if (!this.autoEatForceSingleUse && IsEnergyFull())
                    {
                        isAutoEating = false;
                        autoEatStep = 0;
                        this.AutoEatRepairLog($"[Auto Eat] Skipped - energy already full ({this.GetCurrentEnergyDisplay()})");
                        break;
                    }
                    if (!this.autoEatForceSingleUse && autoEatAttempts >= this.maxAutoEatAttempts)
                    {
                        isAutoEating = false;
                        autoEatStep = 0;
                        this.AutoEatRepairLog($"[Auto Eat] Stopped after max attempts ({autoEatAttempts}) - energy at {this.GetCurrentEnergyDisplay()}");
                        break;
                    }
                    if (this.TryDirectUseFood())
                    {
                        autoEatAttempts++;
                        autoEatStep = DIRECT_EAT_STEP_DELAY;
                        autoEatStepTimer = Time.unscaledTime + 0.75f;
                        this.AutoEatRepairLog($"[Auto Eat] Direct food use sent ({this.GetAutoEatFoodOptionLabel(this.autoEatFoodType)}), attempt {autoEatAttempts}.");
                    }
                    else
                    {
                        this.AutoEatRepairLog("[Auto Eat] Direct food use failed; stopped without opening bag UI.");
                        isAutoEating = false;
                        autoEatStep = 0;
                        autoEatForceSingleUse = false;
                        nextAutoEatDirectRetryAt = Time.unscaledTime + 5f;
                    }
                    break;

                case DIRECT_EAT_STEP_DELAY:
                    if (this.autoEatForceSingleUse)
                    {
                        isAutoEating = false;
                        autoEatStep = 0;
                        autoEatForceSingleUse = false;
                        this.AutoEatRepairLog($"[Auto Eat] Used selected food once ({this.GetAutoEatFoodOptionLabel(this.autoEatFoodType)}).");
                    }
                    else if (!IsEnergyFull() && autoEatAttempts < this.maxAutoEatAttempts)
                    {
                        autoEatStep = DIRECT_EAT_STEP_USE;
                        autoEatStepTimer = Time.unscaledTime;
                        this.AutoEatRepairLog($"[Auto Eat] Energy not full yet ({this.GetCurrentEnergyDisplay()}), direct eating another {this.GetAutoEatFoodOptionLabel(this.autoEatFoodType)}... (attempt {autoEatAttempts})");
                    }
                    else
                    {
                        isAutoEating = false;
                        autoEatStep = 0;
                        autoEatForceSingleUse = false;
                        if (IsEnergyFull())
                        {
                            this.AutoEatRepairLog("[Auto Eat] Energy restored to maximum!");
                        }
                        else
                        {
                            this.AutoEatRepairLog($"[Auto Eat] Stopped after {autoEatAttempts} attempts - energy at {this.GetCurrentEnergyDisplay()}");
                        }
                    }
                    break;

                default:
                    this.AutoEatRepairLog("[Auto Eat] Unknown eat step " + autoEatStep + "; stopping.");
                    isAutoEating = false;
                    autoEatStep = 0;
                    autoEatForceSingleUse = false;
                    break;
            }
        }















































        private string TrimTrailingDigitsAndUnderscores(string value)
        {
            string text = this.NormalizeAutoSellMatchKey(value);
            int end = text.Length;
            while (end > 0 && char.IsDigit(text[end - 1]))
            {
                end--;
            }
            while (end > 0 && text[end - 1] == '_')
            {
                end--;
            }
            return end > 0 ? text.Substring(0, end) : text;
        }













        // BattlePassSellPanel indexes PeriodCurrencySales by currency, then matches BackpackItem.staticId to row entityId.













#if BEPINEX
#endif

#if BEPINEX

#endif















#if BEPINEX





#endif















        private Type FindManagedTableDataType()
        {
            string[] fullNames = new[]
            {
                "EcsClient.TableData",
                "TableData",
                "Il2CppEcsClient.TableData",
                "Il2Cpp.TableData"
            };

            for (int i = 0; i < fullNames.Length; i++)
            {
                Type type = this.FindLoadedType(fullNames[i]);
                if (type != null)
                {
                    return type;
                }
            }

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                if (assembly == null)
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
                    Type type = types[i];
                    if (type != null && string.Equals(type.Name, "TableData", StringComparison.Ordinal))
                    {
                        return type;
                    }
                }
            }

            return null;
        }










        private IntPtr TryGetAuraMonoDataModuleInstance(IntPtr moduleClass)
        {
            if (moduleClass == IntPtr.Zero || auraMonoClassGetMethodFromName == null || auraMonoRuntimeInvoke == null)
            {
                return IntPtr.Zero;
            }

            IntPtr getInstanceMethod = auraMonoClassGetMethodFromName(moduleClass, "get_Instance", 0);
            if (getInstanceMethod == IntPtr.Zero)
            {
                getInstanceMethod = this.FindAuraMonoMethodOnHierarchy(moduleClass, "get_Instance", 0);
            }

            if (getInstanceMethod == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            IntPtr exc = IntPtr.Zero;
            return auraMonoRuntimeInvoke(getInstanceMethod, IntPtr.Zero, IntPtr.Zero, ref exc);
        }





        private static readonly Dictionary<string, IntPtr> autoSellIl2CppClassCache = new Dictionary<string, IntPtr>(StringComparer.Ordinal);

















        private static Dictionary<uint, int> ReadUIntIntDictionaryField(object instance, string fieldName)
        {
            if (instance == null || string.IsNullOrWhiteSpace(fieldName))
            {
                return null;
            }

            FieldInfo f = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (f == null)
            {
                return null;
            }

            object v = f.GetValue(instance);
            if (v is Dictionary<uint, int> direct)
            {
                return direct;
            }

            if (v is IDictionary dict)
            {
                Dictionary<uint, int> map = new Dictionary<uint, int>();
                foreach (DictionaryEntry e in dict)
                {
                    try
                    {
                        uint key = Convert.ToUInt32(e.Key);
                        int value = Convert.ToInt32(e.Value);
                        map[key] = value;
                    }
                    catch
                    {
                    }
                }
                return map;
            }

            return null;
        }


































        private bool IsReadableMemoryProtect(uint protect)
        {
            return protect == 0x02U || protect == 0x04U || protect == 0x08U || protect == 0x20U || protect == 0x40U;
        }





























        private unsafe bool TryInvokeToolRestorerProtocol(uint netId)
        {
            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoClassFromName == null || auraMonoRuntimeInvoke == null)
                {
                    this.AutoEatRepairLog("[DirectBackpackMono] ToolRestorer protocol unavailable: Mono API not ready.");
                    return false;
                }

                int staticId = this.lastDirectBackpackMatchedStaticId;
                if (staticId == 0)
                {
                    this.AutoEatRepairLog("[DirectBackpackMono] ToolRestorer protocol unavailable: staticId missing for netId=" + netId);
                    return false;
                }

                IntPtr dataImage = this.FindAuraMonoImage(new[] { "XDTDataAndProtocol", "XDTDataAndProtocol.dll", "Client", "Client.dll" });
                IntPtr protocolClass = dataImage != IntPtr.Zero ? auraMonoClassFromName(dataImage, "XDTDataAndProtocol.ProtocolService.ItemDisplay", "ToolRestorerProtocolManager") : IntPtr.Zero;
                if (protocolClass == IntPtr.Zero)
                {
                    protocolClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTDataAndProtocol.ProtocolService.ItemDisplay", "ToolRestorerProtocolManager");
                }

                IntPtr method = protocolClass != IntPtr.Zero ? this.FindAuraMonoMethodOnHierarchy(protocolClass, "CanPutRestorer", 2) : IntPtr.Zero;
                this.AutoEatRepairLog("[DirectBackpackMono] ToolRestorer protocol lookup. class=0x" + protocolClass.ToString("X") + " method=0x" + method.ToString("X") + " netId=" + netId + " staticId=" + staticId);
                if (method == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr* args = stackalloc IntPtr[2];
                args[0] = (IntPtr)(&netId);
                args[1] = (IntPtr)(&staticId);
                IntPtr exc = IntPtr.Zero;
                auraMonoRuntimeInvoke(method, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    this.AutoEatRepairLog("[DirectBackpackMono] ToolRestorer protocol raised exception.");
                    return false;
                }

                this.AutoEatRepairLog("[DirectBackpackMono] ToolRestorer protocol sent. netId=" + netId + " staticId=" + staticId);
                return true;
            }
            catch (Exception ex)
            {
                this.AutoEatRepairLog("[DirectBackpackMono] ToolRestorer protocol exception: " + ex.Message);
                return false;
            }
        }




        private bool TryGetDirectBagExecutor(out object bagObj, out Type functionType, out Type storageType, out MethodInfo execute)
        {
            bagObj = this.cachedDirectBagModuleObj;
            functionType = this.cachedDirectBackpackFunctionType;
            storageType = this.cachedDirectBagStorageType;
            execute = this.cachedDirectExecuteBackpackItemFuncMethod;
            if (bagObj != null && functionType != null && storageType != null && execute != null)
            {
                return true;
            }

            Type bagType = this.FindLoadedType("XDTLevelAndEntity.Game.Module.Bag.BagModule", "BagModule");
            if (bagType == null)
            {
                this.AutoEatRepairLog("[DirectBackpackManaged] BagModule type unavailable.");
                return false;
            }

            if (!this.TryGetManagedModule(bagType, out bagObj) || bagObj == null)
            {
                this.AutoEatRepairLog("[DirectBackpackManaged] BagModule instance unavailable.");
                return false;
            }

            functionType = this.FindLoadedType("XDTGameSystem.GameplaySystem.BackPack.BackpackItemFunction", "BackpackItemFunction");
            storageType = this.FindLoadedType("EcsClient.XDT.Scene.Shared.Data.StaticPartial.EStorageType", "EStorageType");
            if (functionType == null || storageType == null)
            {
                this.AutoEatRepairLog("[DirectBackpackManaged] enum type unavailable. functionType=" + (functionType != null) + " storageType=" + (storageType != null));
                return false;
            }

            execute = bagObj.GetType().GetMethod("ExecuteBackpackItemFunc", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, new[] { functionType, typeof(uint), storageType }, null);
            if (execute == null)
            {
                this.AutoEatRepairLog("[DirectBackpackManaged] ExecuteBackpackItemFunc method unavailable.");
                return false;
            }

            this.cachedDirectBagModuleObj = bagObj;
            this.cachedDirectBackpackFunctionType = functionType;
            this.cachedDirectBagStorageType = storageType;
            this.cachedDirectExecuteBackpackItemFuncMethod = execute;
            return true;
        }

        private bool TryGetManagedModule(Type moduleType, out object moduleObj)
        {
            moduleObj = null;
            if (moduleType == null)
            {
                return false;
            }

            try
            {
                Type managersType = this.FindLoadedType("XDTGame.Framework.Managers", "Managers");
                if (managersType == null)
                {
                    this.AutoEatRepairLog("[DirectBackpackManaged] Managers type unavailable.");
                    return false;
                }

                MethodInfo getModule = managersType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
                    .FirstOrDefault(m =>
                    {
                        if (m.Name != "GetModule" || m.IsGenericMethodDefinition)
                        {
                            return false;
                        }
                        ParameterInfo[] parameters = m.GetParameters();
                        return parameters.Length == 1 && parameters[0].ParameterType == typeof(Type);
                    });
                if (getModule != null)
                {
                    moduleObj = getModule.Invoke(null, new object[] { moduleType });
                    if (moduleObj != null)
                    {
                        this.AutoEatRepairLog("[DirectBackpackManaged] Resolved module via Managers.GetModule(Type): " + moduleType.FullName);
                        return true;
                    }
                }

                FieldInfo moduleDicField = managersType.GetField("_moduleDic", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                object moduleDicObj = moduleDicField != null ? moduleDicField.GetValue(null) : null;
                if (moduleDicObj is IDictionary moduleDic && moduleDic.Contains(moduleType))
                {
                    object moduleObject = moduleDic[moduleType];
                    if (moduleObject != null && this.TryGetObjectMember(moduleObject, "module", out moduleObj) && moduleObj != null)
                    {
                        this.AutoEatRepairLog("[DirectBackpackManaged] Resolved module via Managers._moduleDic: " + moduleType.FullName);
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                Exception inner = ex.InnerException ?? ex;
                this.AutoEatRepairLog("[DirectBackpackManaged] Module resolve exception for " + moduleType.FullName + ": " + inner.GetType().Name + ": " + inner.Message);
                moduleObj = null;
                return false;
            }
        }

        private object TryGetStaticObjectAcrossHierarchy(Type type, params string[] memberNames)
        {
            Type current = type;
            while (current != null)
            {
                foreach (string memberName in memberNames)
                {
                    PropertyInfo property = current.GetProperty(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                    if (property != null)
                    {
                        try
                        {
                            object value = property.GetValue(null, null);
                            if (value != null)
                            {
                                return value;
                            }
                        }
                        catch { }
                    }

                    FieldInfo field = current.GetField(memberName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                    if (field != null)
                    {
                        try
                        {
                            object value = field.GetValue(null);
                            if (value != null)
                            {
                                return value;
                            }
                        }
                        catch { }
                    }
                }

                current = current.BaseType;
            }

            return null;
        }









        // INVARIANT: module instances are resolved by scanning Managers._moduleDic. Never route
        // this through Managers.GetModule(Type) for a type that exists only on the Mono side:
        // its internal Type.GetType on a no-Instance ViewModule hard-crashes the mono runtime
        // (see docs/plans pad-build migration notes). PadBuild's GetModule(Type) path is the one
        // vetted exception — it passes a Type object resolved from the same mono image.









        // Returns Images scoped to the open bag panel - avoids scene-wide FindObjectsOfType allocation

        // Check if bag panel is currently open
        private bool IsBagOpen()
        {
            GameObject bag = GameObject.Find(BAG_PANEL_PATH);
            return bag != null && bag.activeInHierarchy;
        }

        // Check if user clicked on a food item in bag during pick mode
        // This detects clicks by checking for the Use/Eat button appearing

        // Get the sprite name of the currently selected food item using the selection indicator position

        // Scan bag for all food items (sprites starting with "ui_item_normal_p_" and containing food keywords, or gather_ items)

















































        private bool IsLikelyBagItemSprite(string spriteName)
        {
            if (string.IsNullOrWhiteSpace(spriteName))
            {
                return false;
            }

            return spriteName.StartsWith("ui_item_normal_p_", StringComparison.Ordinal)
                || spriteName.StartsWith("ui_item_special_p_", StringComparison.Ordinal)
                || spriteName.StartsWith("p_", StringComparison.Ordinal)
                || spriteName.Contains("_birdphoto")
                || spriteName.Contains("_gather_")
                || spriteName.Contains("_food_");
        }


        private string ResolveBagItemDisplayName(string matchKey, int staticId)
        {
            if (staticId > 0 && this.TryGetResolvedFoodNameFromStaticId(staticId, out string tableName) && !this.IsPoorBagItemDisplayName(tableName, staticId))
            {
                return tableName;
            }

            string spriteName = this.GetAutoSellItemDisplayName(matchKey);
            if (!this.IsPoorBagItemDisplayName(spriteName, staticId))
            {
                return spriteName;
            }

            return staticId > 0 ? ("Item " + staticId) : (string.IsNullOrWhiteSpace(spriteName) ? "Unknown Item" : spriteName);
        }

        private bool IsPoorBagItemDisplayName(string displayName, int staticId)
        {
            if (string.IsNullOrWhiteSpace(displayName))
            {
                return true;
            }

            string trimmed = displayName.Trim();
            if (trimmed.Length > 0)
            {
                bool allQuestionMarks = true;
                for (int i = 0; i < trimmed.Length; i++)
                {
                    if (trimmed[i] != '?')
                    {
                        allQuestionMarks = false;
                        break;
                    }
                }

                if (allQuestionMarks)
                {
                    return true;
                }
            }

            if (staticId > 0 && trimmed.StartsWith(staticId.ToString(), StringComparison.Ordinal))
            {
                string suffix = trimmed.Substring(staticId.ToString().Length).Trim();
                if (suffix.Length == 0 || this.IsNumericTokenSequence(suffix))
                {
                    return true;
                }
            }

            if (this.IsNumericTokenSequence(trimmed))
            {
                return true;
            }

            for (int i = 0; i < trimmed.Length; i++)
            {
                if (!char.IsDigit(trimmed[i]))
                {
                    return false;
                }
            }

            return trimmed.Length > 0;
        }

        private bool IsNumericTokenSequence(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string[] tokens = value.Split(new[] { ' ', '\t', ',', ';', '|', '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2)
            {
                return false;
            }

            for (int i = 0; i < tokens.Length; i++)
            {
                if (!int.TryParse(tokens[i], out _))
                {
                    return false;
                }
            }

            return true;
        }



        private bool DoesBagItemEntryMatchSprite(AutoSellBagItemEntry entry, string normalizedSprite)
        {
            if (entry == null || string.IsNullOrWhiteSpace(normalizedSprite))
            {
                return false;
            }

            string[] keys =
            {
                this.NormalizeAutoSellMatchKey(entry.SpriteName),
                this.NormalizeAutoSellMatchKey(entry.MatchKey),
                this.NormalizeAutoSellMatchKey(this.GetAutoSellSpriteNameFromMatchKey(entry.MatchKey))
            };

            for (int i = 0; i < keys.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(keys[i]) && string.Equals(keys[i], normalizedSprite, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }






        private Texture2D CopySpriteTexture(Sprite sprite, string logPrefix)
        {
            try
            {
                if (sprite == null || sprite.texture == null)
                {
                    return null;
                }

                Texture2D original = sprite.texture;
                RenderTexture rt = RenderTexture.GetTemporary(original.width, original.height, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(original, rt);

                Texture2D copy = new Texture2D(original.width, original.height, TextureFormat.RGBA32, false);
                RenderTexture previousRT = RenderTexture.active;
                RenderTexture.active = rt;
                copy.ReadPixels(new Rect(0, 0, original.width, original.height), 0, 0);
                copy.Apply();
                RenderTexture.active = previousRT;
                RenderTexture.ReleaseTemporary(rt);
                return copy;
            }
            catch (Exception ex)
            {
                ModLogger.Msg((logPrefix ?? "[BagScan]") + " Failed to copy texture: " + ex.Message);
                return null;
            }
        }



        private string SanitizeCacheFileName(string value)
        {
            string safe = (value ?? string.Empty).Trim().ToLowerInvariant();
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                safe = safe.Replace(c, '_');
            }
            safe = safe.Replace(' ', '_');
            while (safe.Contains("__"))
            {
                safe = safe.Replace("__", "_");
            }
            return safe.Trim('_');
        }




        // Get display name from sprite name



        bool SimulateLegacyClick(GameObject target)
        {
            if (target == null)
            {
                return false;
            }

            try
            {
                var eventTrigger = target.GetComponent<EventTrigger>();
                if (eventTrigger != null && eventTrigger.triggers.Count > 0)
                {
                    PointerEventData eventData = new PointerEventData(EventSystem.current);
                    foreach (var trigger in eventTrigger.triggers)
                    {
                        if (trigger.eventID == EventTriggerType.PointerClick ||
                            trigger.eventID == EventTriggerType.PointerDown)
                        {
                            trigger.callback.Invoke(eventData);
                            return true;
                        }
                    }
                }

                return ExecuteEvents.Execute(target, new PointerEventData(EventSystem.current), ExecuteEvents.pointerClickHandler) ||
                       ExecuteEvents.Execute(target, new PointerEventData(EventSystem.current), ExecuteEvents.pointerDownHandler);
            }
            catch
            {
                return false;
            }
        }

        bool SimulateClick(GameObject target)
        {
            EventSystem eventSystem = this.EnsureGameplayEventSystemAvailable();
            PointerEventData eventData = new PointerEventData(eventSystem);
            var eventTrigger = target.GetComponent<EventTrigger>();
            if (eventTrigger != null && eventTrigger.triggers.Count > 0)
            {
                foreach (var trigger in eventTrigger.triggers)
                {
                    if (trigger.eventID == EventTriggerType.PointerClick ||
                        trigger.eventID == EventTriggerType.PointerDown ||
                        trigger.eventID == EventTriggerType.PointerUp ||
                        trigger.eventID == EventTriggerType.Submit)
                    {
                        trigger.callback.Invoke(eventData);
                        return true;
                    }
                }
            }
            bool handled =
                ExecuteEvents.Execute(target, eventData, ExecuteEvents.submitHandler) ||
                ExecuteEvents.Execute(target, eventData, ExecuteEvents.pointerClickHandler) ||
                ExecuteEvents.Execute(target, eventData, ExecuteEvents.pointerDownHandler) ||
                ExecuteEvents.Execute(target, eventData, ExecuteEvents.pointerUpHandler);

            if (handled)
            {
                return true;
            }

            try
            {
                foreach (Component component in target.GetComponents<Component>())
                {
                    if (component == null) continue;
                    Type type = component.GetType();
                    MethodInfo method =
                        type.GetMethod("OnClick", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ??
                        type.GetMethod("Click", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ??
                        type.GetMethod("OnPointerClick", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (method == null) continue;
                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length == 0)
                    {
                        method.Invoke(component, null);
                        return true;
                    }
                    if (parameters.Length == 1)
                    {
                        method.Invoke(component, new object[] { eventData });
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        bool OpenInventory()
        {
            Button btn = GameObject.Find(BAG_BUTTON_PATH)?.GetComponent<Button>();
            if ((btn == null || !btn.interactable) && !this.TryFindFallbackBagButton(out btn))
            {
                return false;
            }

            if (btn != null && btn.interactable)
            {
                btn.onClick.Invoke();
                return true;
            }
            return false;
        }

        private bool TryFindFallbackBagButton(out Button button)
        {
            button = null;

            try
            {
                GameObject statusPanel = GameObject.Find(STATUS_PANEL_PATH) ?? GameObject.Find("StatusPanel(Clone)");
                if (statusPanel != null)
                {
                    Button[] statusButtons = statusPanel.GetComponentsInChildren<Button>(true);
                    for (int i = 0; i < statusButtons.Length; i++)
                    {
                        Button candidate = statusButtons[i];
                        if (candidate == null || candidate.gameObject == null || !candidate.interactable)
                        {
                            continue;
                        }

                        string lower = (candidate.gameObject.name ?? string.Empty).ToLowerInvariant();
                        if (!lower.Contains("bag"))
                        {
                            continue;
                        }

                        button = candidate;
                        return true;
                    }
                }

                Button[] allButtons = Resources.FindObjectsOfTypeAll<Button>();
                for (int i = 0; i < allButtons.Length; i++)
                {
                    Button candidate = allButtons[i];
                    if (candidate == null || candidate.gameObject == null || !candidate.gameObject.activeInHierarchy || !candidate.interactable)
                    {
                        continue;
                    }

                    string path = this.GetTransformPath(candidate.transform).ToLowerInvariant();
                    if (path.Contains("statuspanel") && path.Contains("bag@"))
                    {
                        button = candidate;
                        return true;
                    }
                }
            }
            catch
            {
            }

            return false;
        }

        bool ClickUseButton()
        {
            var btn = GameObject.Find(USE_BUTTON_PATH)?.GetComponent<Button>();
            if (btn != null && btn.interactable)
            {
                var txt = btn.GetComponentInChildren<Text>();
                if (txt == null)
                {
                    btn.onClick.Invoke();
                    return true;
                }
                string actionText = txt.text == null ? string.Empty : txt.text.Trim();

                foreach (string candidate in LocalizationManager.GetTranslationCandidates("Use"))
                {
                    if (actionText.Equals(candidate, StringComparison.OrdinalIgnoreCase))
                    {
                        btn.onClick.Invoke();
                        return true;
                    }
                }

                foreach (string candidate in LocalizationManager.GetTranslationCandidates("Eat"))
                {
                    if (actionText.Equals(candidate, StringComparison.OrdinalIgnoreCase))
                    {
                        btn.onClick.Invoke();
                        return true;
                    }
                }
            }
            return false;
        }

        void CloseInventory()
        {
            var btn = GameObject.Find(CLOSE_BUTTON_PATH)?.GetComponent<Button>();
            btn?.onClick?.Invoke();
        }







        private void EnsureUiPickerTextures(float hue)
        {
            if (this.uiHueTexture == null)
            {
                this.uiHueTexture = this.CreateHueTexture(18, 180);
                this.themeTextures.Add(this.uiHueTexture);
            }

            if (this.uiSvTexture == null || Math.Abs(this.uiPickerHueCached - hue) > 0.001f)
            {
                if (this.uiSvTexture != null)
                {
                    Object.Destroy(this.uiSvTexture);
                    this.themeTextures.Remove(this.uiSvTexture);
                }
                this.uiSvTexture = this.CreateSvTexture(220, 180, hue);
                this.uiPickerHueCached = hue;
                this.themeTextures.Add(this.uiSvTexture);
            }
        }

        private Texture2D CreateHueTexture(int width, int height)
        {
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            tex.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < height; y++)
            {
                float h = (float)y / (height - 1);
                Color c = Color.HSVToRGB(h, 1f, 1f);
                for (int x = 0; x < width; x++)
                {
                    tex.SetPixel(x, y, c);
                }
            }
            tex.Apply();
            return tex;
        }

        private Texture2D CreateSvTexture(int width, int height, float hue)
        {
            Texture2D tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
            tex.wrapMode = TextureWrapMode.Clamp;
            for (int y = 0; y < height; y++)
            {
                float v = (float)y / (height - 1);
                for (int x = 0; x < width; x++)
                {
                    float s = (float)x / (width - 1);
                    tex.SetPixel(x, y, Color.HSVToRGB(hue, s, v));
                }
            }
            tex.Apply();
            return tex;
        }



        private string ColorToHex(Color color)
        {
            int r = Mathf.RoundToInt(Mathf.Clamp01(color.r) * 255f);
            int g = Mathf.RoundToInt(Mathf.Clamp01(color.g) * 255f);
            int b = Mathf.RoundToInt(Mathf.Clamp01(color.b) * 255f);
            return $"#{r:X2}{g:X2}{b:X2}";
        }

        private bool TryParseHexColor(string input, out Color color)
        {
            color = Color.white;
            if (string.IsNullOrWhiteSpace(input)) return false;
            string hex = input.Trim();
            if (hex.StartsWith("#")) hex = hex.Substring(1);
            if (hex.Length != 6) return false;

            int r;
            int g;
            int b;
            if (!int.TryParse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out r)) return false;
            if (!int.TryParse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out g)) return false;
            if (!int.TryParse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out b)) return false;
            color = new Color(r / 255f, g / 255f, b / 255f, 1f);
            return true;
        }









        // Token: 0x06000027 RID: 39 RVA: 0x00007014 File Offset: 0x00005214
        private void InspectMovementComponent()
        {
            GameObject gameObject = GameObject.Find("p_player_skeleton(Clone)");
            bool flag = gameObject == null;
            if (flag)
            {
                ModLogger.Msg("Player not found!");
            }
            else
            {
                Component[] array = gameObject.GetComponents<Component>();
                Component component = null;
                foreach (Component component2 in array)
                {
                    bool flag2 = component2 == null;
                    if (!flag2)
                    {
                        Il2CppType il2CppType = component2.GetIl2CppType();
                        bool flag3 = il2CppType != null && il2CppType.Name == "DynamicMonoBehaviour";
                        if (flag3)
                        {
                            component = component2;
                            break;
                        }
                    }
                }
                bool flag4 = component == null;
                if (flag4)
                {
                    ModLogger.Msg("DynamicMonoBehaviour not found!");
                }
                else
                {
                    ModLogger.Msg($"=== DynamicMonoBehaviour INSPECTION ===");
                    FieldInfo[] fields = component.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    ModLogger.Msg($"Total fields: {fields.Length}");
                    foreach (FieldInfo fieldInfo in fields)
                    {
                        try
                        {
                            object value = fieldInfo.GetValue(component);
                            ModLogger.Msg($"Field: {fieldInfo.Name} = {value} ({fieldInfo.FieldType.Name})");
                        }
                        catch
                        {
                            ModLogger.Msg("Field: " + fieldInfo.Name + " (couldn't read value)");
                        }
                    }
                    MethodInfo[] methods = component.GetType().GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    ModLogger.Msg($"\nTotal methods: {methods.Length}");
                    foreach (MethodInfo methodInfo in methods)
                    {
                        string value2 = string.Join(", ", from p in methodInfo.GetParameters()
                                                          select p.ParameterType.Name);
                        ModLogger.Msg($"Method: {methodInfo.ReturnType.Name} {methodInfo.Name}({value2})");
                    }
                    ModLogger.Msg("=== END ===");
                }
            }
        }

        // Token: 0x04000002 RID: 2
        public static HeartopiaComplete Instance;

        // Token: 0x04000003 RID: 3
        private new static HarmonyLib.Harmony harmonyInstance;
        internal static HarmonyLib.Harmony ModHarmony => harmonyInstance;

        // Token: 0x04000004 RID: 4
        public static bool OverridePlayerPosition;

        // Token: 0x04000005 RID: 5
        public static Vector3 OverridePosition;

        // Token: 0x04000006 RID: 6
        public static bool OverrideCameraPosition;

        // Token: 0x04000007 RID: 7
        public static Vector3 CameraOverridePos;

        // Token: 0x04000008 RID: 8
        public static Quaternion CameraOverrideRot;

        // Token: 0x04000009 RID: 9
        private int cameraOverrideFramesRemaining = 0;

        // Player Rotation Override
        public static bool OverridePlayerRotation = false;
        public static Quaternion PlayerOverrideRot = Quaternion.identity;
        private int playerRotationFramesRemaining = 0;

        // Transform.GetInstanceID of the hot-path patch targets. Refreshed once per frame in
        // OnUpdate (and lazily by the prefixes when still 0) so the Transform setter prefixes
        // compare ints instead of fetching gameObject.name (native call + string alloc) per set.
        public static int OverridePlayerTransformId;
        public static int OverrideCameraTransformId;

        // IMGUI Theme
        private bool themeInitialized = false;
        private GUIStyle themeWindowStyle;
        private GUIStyle themePanelStyle;
        private GUIStyle themeContentStyle;
        private GUIStyle themeSidebarButtonStyle;
        private GUIStyle themeSidebarButtonActiveStyle;
        private GUIStyle themePrimaryButtonStyle;
        private GUIStyle themeDangerButtonStyle;
        private GUIStyle themeTopTabStyle;
        private GUIStyle themeTopTabActiveStyle;
        private List<Texture2D> themeTextures = new List<Texture2D>();
        private Texture2D uiCircleTexture;
        private Texture2D uiHueTexture;
        private Texture2D uiSvTexture;
        private float uiPickerHueCached = -1f;
        private Material radarLineMaterial = null;
        private Material radarFillMaterial = null;
        private readonly List<GameObject> radarCleanupMarkers = new List<GameObject>(64);
        private readonly List<int> radarCleanupTrackedIds = new List<int>(64);
        private readonly List<GameObject> radarDestroyBuffer = new List<GameObject>(64);
        private readonly Dictionary<int, GameObject> trackedBubbleMarkers = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, Vector3> bubbleRadarTrackedPositions = new Dictionary<int, Vector3>();
        private readonly Dictionary<int, Vector3> bubbleRadarSnapshotPositions = new Dictionary<int, Vector3>();
        private readonly Dictionary<int, GameObject> bubbleRadarSceneTargets = new Dictionary<int, GameObject>();
        private readonly Dictionary<int, float> bubbleRadarLastSeenAt = new Dictionary<int, float>();
        private readonly HashSet<int> bubbleRadarSeenIds = new HashSet<int>();
        private readonly List<IntPtr> bubbleRadarAuraComponentsBuffer = new List<IntPtr>(96);
        private Type cachedBubbleClientServiceType = null;
        private MethodInfo cachedBubbleClientServiceTryGetMethod = null;
        private MethodInfo cachedBubbleClientServiceGetAllMethod = null;
        private Type cachedBubbleOptDataType = null;
        private MethodInfo cachedBubbleOptDataAsMethod = null;
        private MethodInfo cachedBubbleOptDataGetNetIdMethod = null;
        private PropertyInfo cachedBubbleOptDataLocationProperty = null;
        private PropertyInfo cachedBubbleOptDataIdProperty = null;
        private Type cachedBubbleLocationComponentType = null;
        private Type cachedBubbleIdComponentType = null;
        private MethodInfo cachedEntityDataOptTryGetValueMethod = null;
        private float nextBubbleClientServiceResolveAttemptAt = -999f;
        private float nextAuraBubbleScanAttemptAt = -999f;
        private float lastAuraBubbleScanSuccessAt = -999f;
        private float lastAuraBubbleScanFailureAt = -999f;
        private float bubbleRadarActivatedAt = -999f;
        private int bubbleRadarAuraConsecutiveFailures = 0;
        private float nextBubbleMarkerSyncAt = -999f;
        private float _cachedBubbleRadarAt = -999f;
        private float nextBubbleEntityTypeResolveAttemptAt = -999f;
        private Vector3 bubbleRadarLastScanOrigin = Vector3.zero;
        private bool bubbleRadarHasLastScanOrigin = false;
        private bool bubbleRadarForceRefresh = true;
        private const float BubbleRadarRefreshInterval = 10.0f;
        private const float BubbleRadarEmptyRefreshInterval = 4.0f;
        private const float BubbleRadarMovedRefreshInterval = 8.0f;
        private const float BubbleRadarRescanMoveThreshold = 60.0f;
        private const float BubbleRadarMarkerGraceSeconds = 8.0f;
        private const float BubbleRadarServiceResolveRetryInterval = 60.0f;
        private const float BubbleRadarEntityTypeResolveRetryInterval = 45.0f;
        private const float BubbleRadarAuraInitialSettleDelay = 8.0f;
        private const float BubbleRadarAuraRetryInterval = 18.0f;
        private const float BubbleRadarAuraSuccessRefreshInterval = 40.0f;
        private const float BubbleRadarAuraMaxFailureBackoff = 90.0f;
        private const float BubbleRadarMarkerSyncInterval = 0.75f;
        private const float BubbleRadarMaxDistance = 1000.0f;
        private const float BubbleRadarSceneMissingRetainMinDistance = 25.0f;
        private const bool BubbleRadarUnsafeAuraMonoScanEnabled = false;
        private const string BubbleTrackedMarkerPrefix = "BubbleTrackedMarker_";
        private readonly Dictionary<uint, GameObject> trackedHideAndSeekMorphMarkers = new Dictionary<uint, GameObject>();
        private readonly Dictionary<uint, Vector3> hideAndSeekMorphTrackedPositions = new Dictionary<uint, Vector3>();
        private readonly HashSet<uint> hideAndSeekMorphSeenNetIds = new HashSet<uint>();
        private readonly List<HideAndSeekMorphRadarSpot> hideAndSeekMorphCollectBuffer = new List<HideAndSeekMorphRadarSpot>(32);
        private const string HideAndSeekMorphMarkerPrefix = "HideAndSeekMorphMarker_";

        // Token: 0x0400000A RID: 10
        private bool showMenu = true;
        private bool notificationsEnabled = true;
        private int notificationPosition = 5;
        private bool hideIdEnabled = true;
        private string customDisplayId = string.Empty;
        private bool customDisplayIdEnabled = false;
        private string cachedOriginalIdPart = string.Empty;
        private GameObject cachedTestIndexObject = null;
        private Text cachedTestIndexText = null;
        private float nextIdDisplayUpdateAt = 0f;
        private bool fpsBypassEnabled = false;
        private int fpsBypassTarget = 144;
        private float nextFpsBypassApplyAt = 0f;
        private float nextFpsBypassTuneAt = 0f;
        private float fpsBypassObservedFps = 0f;
        private float statusOverlaySmoothedFps = 0f;
        private float statusOverlayDisplayedFps = 0f;
        private float nextStatusOverlayFpsRefreshAt = 0f;
        private float fpsBypassCompOffset = 0f;
        private bool fpsBypassWasApplied = false;
        private int fpsBypassOriginalTargetFrameRate = -1;
        private int fpsBypassOriginalVSyncCount = 0;
        private bool pendingRadarSettingsSave = false;
        private float nextRadarSettingsSaveAt = 0f;
        private bool blockGameUiWhenMenuOpen = false;
        private bool showStatusOverlay = false;
        private float blockInputReleaseUntil = 0f;
        private List<HeartopiaComplete.MenuNotification> menuNotifications = new List<HeartopiaComplete.MenuNotification>();
        private bool notificationPositionDropdownOpen = false;
        private bool eventSystemBlockedByMenu = false;
        private bool eventSystemPrevEnabled = true;
        private EventSystem blockedEventSystem = null;
        private static readonly string[] NotificationPositionOptions = new string[]
        {
            "Top Left",
            "Middle Left",
            "Bottom Left",
            "Top Center",
            "Bottom Center",
            "Top Right",
            "Middle Right",
            "Bottom Right"
        };

        private List<GameObject> meteorList = new List<GameObject>();

        // Token: 0x0400000B RID: 11
        // ========== GUI SIZE AND POSITION ==========
        // Format: new Rect(X, Y, Width, Height)
        // 
        // X: Position from left edge (50 = left side)
        // Y: Position from top edge (50 = top)
        // Width: How WIDE the menu is (300 = default, 400 = wider, 250 = narrower)
        // Height: How TALL the menu is (510 = default, 600 = taller, 400 = shorter)
        // 
        // EXAMPLES:
        // Bigger menu: new Rect(50f, 50f, 400f, 650f)
        // Smaller menu: new Rect(50f, 50f, 250f, 450f)
        // 
        private Rect windowRect = new Rect(120f, 50f, 1060f, 680f);
        private float targetWindowHeight = 680f;
        private float targetWindowWidth = 1060f;

        // Token: 0x0400000C RID: 12
        private int selectedTab = 0;

        // Token: 0x0400000D RID: 13
        private bool wasMouseOverMenuLastFrame = false;

        // Token: 0x0400000E RID: 14
        private int teleportFramesRemaining = 0;

        // Token: 0x0400000F RID: 15
        private Vector3 lastKnownPosition;

        // Token: 0x04000010 RID: 16
        private bool monitorPosition = false;

        // Token: 0x04000011 RID: 17
        private List<HeartopiaComplete.FarmLocation> farmLocations = new List<HeartopiaComplete.FarmLocation>
        {
            new HeartopiaComplete.FarmLocation("Black Truffle Spawn", new Vector3(272.1f, 12.7f, 98.2f), "mushroom"),
            new HeartopiaComplete.FarmLocation("Oyster Spawn", new Vector3(-139.8f, 21.3f, 205.2f), "mushroom"),
            new HeartopiaComplete.FarmLocation("Penny Bun Spawn", new Vector3(176.9f, 25.9f, 59.8f), "mushroom"),
            new HeartopiaComplete.FarmLocation("ShiiTake Spawn", new Vector3(57f, 18.3f, -131.5f), "mushroom"),
            new HeartopiaComplete.FarmLocation("Button Spawn", new Vector3(-156.3f, 18.8f, -115.2f), "mushroom"),
            new HeartopiaComplete.FarmLocation("Fiddlehead Event Area", new Vector3(229.782f, 11.404f, 48.837f), "event_fiddlehead"),
            new HeartopiaComplete.FarmLocation("Tall Mustard Event Area", new Vector3(-125.213f, 11.729f, 290.797f), "event_tall_mustard"),
            new HeartopiaComplete.FarmLocation("Mustard Greens Event Area", new Vector3(-58.984f, 11.035f, -155.413f), "event_mustard_greens"),
            new HeartopiaComplete.FarmLocation("Burdock Event Area", new Vector3(-211.599f, 29.916f, 35.416f), "event_burdock"),
            new HeartopiaComplete.FarmLocation("Meteor Spawn 1", new Vector3(78.566f, 20.045f, -99.045f), "meteor"),
            new HeartopiaComplete.FarmLocation("Meteor Spawn 2", new Vector3(-57.025f, 11.051f, -151.923f), "meteor"),
            new HeartopiaComplete.FarmLocation("Big Blueberry Field", new Vector3(-114.2f, 20.1f, 142f), "blueberry"),
            new HeartopiaComplete.FarmLocation("Raspberry Field", new Vector3(-162.2f, 23.6f, 86.2f), "redberry"),
            new HeartopiaComplete.FarmLocation("Mandarin Spawn", new Vector3(-109f, 20.9f, -102.2f), "mandarintree"),
            new HeartopiaComplete.FarmLocation("Apple Spawn", new Vector3(-15.539f, 21.240f, 121.592f), "appletree"),
            new HeartopiaComplete.FarmLocation("Apple Spawn 2", new Vector3(70.028f, 19.804f, -97.920f), "appletree"),
            new HeartopiaComplete.FarmLocation("Ore Spawn 1", new Vector3(104.520f, 20.347f, -112.503f), "ore"),
			new HeartopiaComplete.FarmLocation("Ore Spawn 2", new Vector3(83.704f, 20.470f, 121.494f), "ore"),
			new HeartopiaComplete.FarmLocation("Ore Spawn 3", new Vector3(-168.193f, 21.477f, 80.902f), "ore"),
            new HeartopiaComplete.FarmLocation("Stone Spawn 1", new Vector3(-97.534f, 19.109f, -99.113f), "stone"),
			new HeartopiaComplete.FarmLocation("Stone Spawn 2", new Vector3(-97.636f, 20.626f, 112.683f), "stone"),
			new HeartopiaComplete.FarmLocation("Stone Spawn 3", new Vector3(98.994f, 25.601f, 89.411f), "stone"),
            new HeartopiaComplete.FarmLocation("Tree Spawn", new Vector3(136.200f, 20.252f, -68.811f), "tree"),
            new HeartopiaComplete.FarmLocation("Rare Tree Spawn", new Vector3(93.626f, 18.635f, -112.966f), "raretree"),
            new HeartopiaComplete.FarmLocation("Rare Tree Spawn 2", new Vector3(-50.454f, 22.314f, -63.417f), "raretree"),
            new HeartopiaComplete.FarmLocation("Rare Tree Spawn 3", new Vector3(-108.954f, 25.088f,49.249f), "raretree"),
            new HeartopiaComplete.FarmLocation("Rare Tree Spawn 4", new Vector3(41.253f, 25.317f, 76.247f), "raretree")
        };

        // House Slot Teleports
        private Vector3[] houseLocations = new Vector3[]
        {
            new Vector3(-96.76f, 19.40f, -69.73f),
            new Vector3(-117.00f, 20.04f, -36.88f),
            new Vector3(-114.41f, 23.10f, 2.27f),
            new Vector3(-125.37f, 22.55f, 57.64f),
            new Vector3(-89.48f, 20.37f, 113.60f),
            new Vector3(-51.02f, 20.50f, 111.90f),
            new Vector3(-1.32f, 23.96f, 91.48f),
            new Vector3(52.89f, 21.45f, 93.46f),
            new Vector3(88.98f, 22.17f, 58.11f),
            new Vector3(90.69f, 21.95f, 18.20f),
            new Vector3(86.92f, 22.03f, -38.49f),
            new Vector3(70.8f, 20f, -71.3f)
        };

        private Vector3[] animalCareLocations = new Vector3[]
        {
            new Vector3(187.12148f, 25.393492f, 30.365686f),
            new Vector3(165.35306f, 24.739582f, -91.25646f),
            new Vector3(-0.40101618f, 13.28f, -149.19249f),
            new Vector3(-128.86902f, 18.01554f, -134.11234f),
            new Vector3(-186.06908f, 19.758656f, -40.702568f),
            new Vector3(-141.521f, 26.897f, 98.54287f),
            new Vector3(-124.4017f, 22.439999f, 209.72852f),
            new Vector3(-92.957405f, 23.451353f, -14.424409f)
        };

        private string[] animalCareLocationNames = new string[]
        {
            "Deer Care",
            "Panda Care",
            "Sea Otter Care",
            "Alpaca Care",
            "Fox Care",
            "Ferret Care",
            "Capybara Care",
            "Bunny Care"
        };

        // Token: 0x04000012 RID: 18
        private Dictionary<string, Vector3> fastTravelLocations = new Dictionary<string, Vector3>
        {
            {
                "Black Truffle Spawn",
                new Vector3(272.1f, 12.7f, 98.2f)
            },
            {
                "Oyster Spawn",
                new Vector3(-139.8f, 21.3f, 205.2f)
            },
            {
                "Penny Bun Spawn",
                new Vector3(176.9f, 25.9f, 59.8f)
            },
            {
                "ShiiTake Spawn",
                new Vector3(57f, 18.3f, -131.5f)
            },
            {
                "Button Spawn",
                new Vector3(-156.3f, 18.8f, -115.2f)
            },
            {
                "Big Blueberry Field",
                new Vector3(-114.2f, 20.1f, 142f)
            }
        };

        // Token: 0x04000013 RID: 19
        private readonly string[] npcTeleportPreferredNames = new string[]
        {
            "Dorothee (Clothing)",
            "Bob (Furniture)",
            "Massimo (Town) (Cooking)",
            "Vanya (Fishing)",
            "Naniwa (Insect Catching)",
            "Blanc (Gardening)",
            "Baily J (Bird Watching)",
            "Mrs.Joan (Pet Caring)",
            "Ka Ching (General Store)",
            "Doris (Rain/Snowfall)",
            "Doris (Meteor Shower)"
        };

        private List<KeyValuePair<string, Vector3>> cachedNpcTeleportEntries = new List<KeyValuePair<string, Vector3>>();
        private static readonly bool NpcTeleportLiveLocationEnabled = true;
        private static readonly bool NpcTeleportDebugLogsEnabled = MasterLogNpcTeleport;

        private Dictionary<string, int> cachedNpcTeleportIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private Dictionary<int, string> cachedNpcTeleportIdNames = new Dictionary<int, string>();

        private bool npcTeleportIdCacheReady;

        private string npcTeleportIdResolveStatus = string.Empty;

        private float nextNpcTeleportIdRetryTime;

        private float nextNpcTeleportDebugLogTime;

        private string npcTeleportStatus = "Press Refresh NPCs";
        private string npcTeleportSearchText = "";

        // Token: 0x04000014 RID: 20
        private Dictionary<string, Vector3> eventLocations = new Dictionary<string, Vector3>
        {
            {
                "Bug Catching",
                new Vector3(7.2f, 22.8f, 183.6f)
            },
            {
                "Fishing",
                new Vector3(-46.4f, 10.7f, -134.1f)
            },
            {
                "Bird Watching",
                new Vector3(-19.4f, 12.3f, -126.9f)
            },
            {
                "Yello Duck Jump Puzzle Challenge",
                new Vector3(-184f, 10.7f, -159.7f)
            },
            {
                "Bubble Machine Challenge",
                new Vector3(-155.8f, 10.8f, -162.4f)
            }
        };

        // Token: 0x04000015 RID: 21
        private Vector2 fastTravelScrollPosition;
        private Vector2 tabScrollPos = Vector2.zero;
        private int settingsSubTab = 0;
        private string selectedLanguage = "en";
        private bool localizationDropdownOpen = false;

        // UI Theme Settings
        private float uiAccentR = 0.36f;
        private float uiAccentG = 0.70f;
        private float uiAccentB = 0.98f;
        private float uiTextR = 0.95f;
        private float uiTextG = 0.97f;
        private float uiTextB = 0.99f;
        private float uiMainTabTextR = 0.75f;
        private float uiMainTabTextG = 0.82f;
        private float uiMainTabTextB = 0.90f;
        private float uiSubTabTextR = 0.53f;
        private float uiSubTabTextG = 0.59f;
        private float uiSubTabTextB = 0.67f;
        private float uiWindowR = 0.055f;
        private float uiWindowG = 0.065f;
        private float uiWindowB = 0.085f;
        private float uiPanelR = 0.075f;
        private float uiPanelG = 0.085f;
        private float uiPanelB = 0.11f;
        private float uiContentR = 0.095f;
        private float uiContentG = 0.11f;
        private float uiContentB = 0.14f;
        private float uiWindowAlpha = 0.94f;
        private float uiPanelAlpha = 0.90f;
        private float uiContentAlpha = 0.86f;
        private const float UiScaleMin = 0.50f;
        private const float UiScaleMax = 3.00f;
        private const float UiScaleStep = 0.10f;
        private float uiScale = 1.00f;
        private int uiThemeColorTarget = 0;
        private bool uiThemePickerOpen = false;
        private string uiThemeHexInput = "#5CB2FA";

        // Token: 0x04000016 RID: 22
        private int teleportSubTab = 0;
        // Radar subtab (0 = Main, 1 = Settings)
        private int radarSubTab = 0;
        // Radar marker visual style: 0 = Default, 1 = Simple Text, 2 = Icon ESP
        private int radarMarkerStyle = 0;
        private float radarMaxDistance = 75f;

        // Token: 0x04000017 RID: 23
        private Vector3 homePosition = Vector3.zero;

        // Token: 0x04000018 RID: 24
        private bool homePositionSet = false;

        private Vector3 autoHomePosition = Vector3.zero;

        private bool autoHomePositionValid = false;

        private uint autoHomeNetId = 0U;

        private float autoHomeResolveNextAt = 0f;

        private const float AutoHomeResolveRetryInterval = 2f;

        private string autoHomeStatus = "Auto home: resolving...";

        // Token: 0x04000019 RID: 25
        private bool autoFarmEnabled;

        // Token: 0x0400001A RID: 26
        private bool autoCookEnabled;

        // Token: 0x0400001B RID: 27
        private bool bypassEnabled;

        // Token: 0x0400001C RID: 28
        private bool birdVacuumEnabled;

        // Token: 0x0400001D RID: 29
        private float gameSpeed = 1f;
        private float baseFixedDeltaTime = 0.02f;
        private float baseMaximumDeltaTime = 0.3333333f;
        private bool gameTimingCaptured = false;
        private float lastAppliedGameSpeed = -1f;
        private bool pendingGameSpeedConfigSave = false;
        private float nextGameSpeedConfigSaveAt = 0f;

        // NEW FEATURES: Jump Height and Camera FOV
        private Vector3 lastPlayerVelocity = Vector3.zero;
        private Vector3 jumpBoostStartPos = Vector3.zero;
        private Vector3 jumpBoostTargetPos = Vector3.zero;
        private bool customCameraFOVEnabled = false;
        private float cameraFOV = 60f;
        private float originalFOV = -1f;
        private float liveCameraFOVBase = -1f;
        private float lastAppliedCustomCameraFOV = -1f;
        private Camera mainCamera = null;
        private bool fastBubbleGenEnabled = false;
        private float bubbleBubblesPerMinute = 15f;

        // Advanced Cooking Bot Variables
        private bool cookingCleanupMode = false;
        private const float cookingPlayerAlertRadius = 25f;
        private float lastPlayerDetectionCheckAt = -999f;
        private float cookingAutoSpeed = 7f;
        private bool cookingPatrolEnabled = false;
        private bool cookingPanelClosed = false;
        private float cookingPanelClosedTime = 0f;
        private float lastConfirmClickTime = -999f;
        private float lastCookingTimerSeenAt = -999f;
        private float cookingTakeoutSafetyDelay = 0.55f;
        private float lastCookRefreshClickAt = -999f;
        private float lastCookConfirmClickAt = -999f;
        private readonly List<Image> cookImageScanBuffer = new List<Image>(256);
        private float nextCookingCleanupScanAt = 0f;
        private bool lastCookingCleanupResult = false;
        private struct NetCookIngredientRequirement
        {
            public int StaticId;
            public int CountPerDish;
        }

        private sealed class NetCookTargetContext
        {
            public uint CookerNetId;
            public int CookerStaticId;
            public int CookerType;
            public ulong LevelObjectNetId;
            public int Phase;
            public int ContinuePulses;
            public int SentCount;
            public int LastStatus = -1;
            public int IdleRetries;
            public float LastStatusActionAt = -999f;
            public float LastCookCommandAt = -999f;
            public float NextActionAt;
            public bool HasWorldPosition;
            public Vector3 WorldPosition;
        }
        private sealed class NetCookRegisteredWorldCooker
        {
            public uint OwnerNetId;
            public int ResourceId;
            public int StaticId;
            public int CookerType;
        }
        private const int NetCookMaxActionsPerTick = 8;
        private const float NetCookMinTargetStaggerSeconds = 0.08f;
        private const float NetCookPhaseAdvanceDelaySeconds = 0.03f;
        private const float NetCookBatchStartHoldSeconds = 0.05f;
        private const int NetCookIdleResyncRetryThreshold = 4;
        private const float NetCookStatusPollDelaySeconds = 0.75f;
        private const float NetCookStartStatusGraceSeconds = 8f;
        private const float NetCookIdleReprepareDelaySeconds = 5f;
        private const float NetCookCollectRestartDelaySeconds = 0.35f;
        private const float NetCookFastRetryDelaySeconds = 0.5f;
        private const float NetCookDefaultScanRadiusMeters = 5f;
        private const float NetCookMinScanRadiusMeters = 2f;
        private const float NetCookMaxScanRadiusMeters = 30f;
        private const float NetCookCaptureCooldownSeconds = 3f;
        private const float NetCookBroadRefreshCooldownSeconds = 15f;
        private const bool AutoFarmLogsEnabled = MasterLogAutoFarm;
        private const bool NetCookLogsEnabled = MasterLogNetCook;
        private const bool NetCookScanDebugLogsEnabled = MasterLogNetCookScan;
        private const int NetCookScanDebugSampleLimit = 48;
        private const int NetCookOwnerNetIdProbeWindow = 2048;
        private const int NetCookFastOwnerNetIdProbeWindow = 768;
        private const int NetCookCandidateOwnerNetIdProbeWindow = 256;
        private const int NetCookOwnerWindowInspectionsPerFrame = 8;
        private const int NetCookDeferredOwnerWindowSeedTargetThreshold = 6;
        private const int NetCookDeferredBroadRefreshTargetThreshold = 24;
        private const float NetCookDeferredBroadRefreshStartDelaySeconds = 0.75f;
        private const float NetCookMinimumStartupCaptureDelaySeconds = 12f;
        private const float NetCookRuntimeReadyGraceSeconds = 3f;
        private const int NetCookMaxCaptureTargets = 32;
        private const bool NetCookUnsafeBroadAuraMonoExpansionEnabled = true;
        private const bool NetCookUseMagicSpice = false;
        private const int NetCookBackpackStorageType = 1;
        private const int NetCookWarehouseStorageType = 2;
        private const float NetCookMaxRefreshIntervalSeconds = 0.5f;
        private const float NetCookPostMoveMaterialRetrySeconds = 3f;
        private const float NetCookPostMoveMaterialRetryIntervalSeconds = 0.12f;
        private bool netCookEnabled = false;
        private bool netCookMiniGameOnly = false;
        private bool netCookMoveIngredients = false;
        private bool netCookUseAllIngredients = false;
        private int netCookCookQuantity = 1;
        private string netCookCookQuantityInput = "1";
        private int netCookMaxCookQuantity = 0;
        private float nextNetCookMaxRefreshAt = 0f;
        private float netCookInterval = 1.5f;
        private float netCookScanRadiusMeters = NetCookDefaultScanRadiusMeters;
        private int netCookRecipeId = 0;
        private int netCookCookerStaticId = 0;
        private int netCookLastCapturedCookerStaticId = 0;
        private int netCookLastCapturedCookerType = 0;
        private uint netCookCookerNetId = 0U;
        private ulong netCookLevelObjectNetId = 0UL;
        private int netCookSentCount = 0;
        private string netCookStatus = "Capture a cooker target first.";
        private bool netCookCaptureInProgress = false;
        private object netCookCaptureCoroutine = null;
        private object netCookCleanupCoroutine = null;
        private object netCookStartCoroutine = null;
        private int netCookCaptureGeneration = 0;
        private bool netCookDrainAfterIngredientsRunOut = false;
        private string netCookDrainReason = null;
        private float nextNetCookCaptureAllowedAt = 0f;
        private float nextNetCookBroadRefreshAllowedAt = 0f;
        private float netCookRuntimeReadySince = 0f;
        private float netCookRuntimeLastReadyAt = 0f;
        private int netCookLastDeferredWorldScanCandidateCount = -1;
        private int netCookLastBroadRefreshWorldScanCandidateCount = -1;
        private bool netCookRecipeDropdownOpen = false;
        private Vector2 netCookRecipeScrollPos = Vector2.zero;
        private string netCookRecipeSearchText = "";
        private readonly List<KeyValuePair<int, string>> netCookRecipeEntries = new List<KeyValuePair<int, string>>(256);
        private readonly List<KeyValuePair<int, string>> netCookVisibleRecipeEntries = new List<KeyValuePair<int, string>>(256);
        private readonly Dictionary<int, int> netCookRecipeCookerTypes = new Dictionary<int, int>();
        private int netCookRecipeCacheCookerStaticId = 0;
        private int netCookRecipeCacheFailureCookerStaticId = 0;
        private float nextNetCookRecipeCacheRetryAt = 0f;
        private readonly List<uint> netCookMaterialNetIds = new List<uint>(16);
        private readonly Dictionary<int, List<NetCookIngredientRequirement>> netCookRecipeRequirementsCache = new Dictionary<int, List<NetCookIngredientRequirement>>(64);
        private readonly List<NetCookTargetContext> netCookTargets = new List<NetCookTargetContext>(16);
        private readonly Dictionary<string, NetCookTargetContext> netCookRegisteredTargets = new Dictionary<string, NetCookTargetContext>(32);
        private readonly Dictionary<uint, NetCookRegisteredWorldCooker> netCookRegisteredWorldCookers = new Dictionary<uint, NetCookRegisteredWorldCooker>(64);
        private readonly Dictionary<int, int> netCookCookerTypeCache = new Dictionary<int, int>(8);
        private readonly HashSet<int> netCookCookerTypeFailedStaticIds = new HashSet<int>();
        private readonly Dictionary<ulong, long> netCookAuraMonoLevelObjectPtrs = new Dictionary<ulong, long>(64);
        private bool netCookWorldCookerRegistrationPatched = false;
        private bool netCookCookBuildSpawnPatched = false;
        private bool netCookCookBuildUpdatePatched = false;
        private float nextNetCookWorldCookerPatchAttemptAt = 0f;
        private bool birdPhotoAuraMonoDiscoveryComplete = false;
        private float nextBirdPhotoRuntimeProbePatchAttemptAt = 0f;
        private int netCookCookerType = 0;
        private MethodInfo netCookPrepareMethod = null;
        private MethodInfo netCookStartMethod = null;
        private MethodInfo netCookExecuteClickCommandMethod = null;
        private MethodInfo netCookSendPrepareCommandMethod = null;
        private MethodInfo netCookSendStartCommandMethod = null;
        private MethodInfo netCookSendContinueCommandMethod = null;
        private MethodInfo netCookSendInteractCommandMethod = null;
        private PropertyInfo netCookCookingSystemInstanceProperty = null;
        private MethodInfo netCookInitRecipeDetailMethod = null;
        private MethodInfo netCookGetRecipeDetailMethod = null;
        private MethodInfo netCookGetAllRecipesMethod = null;
        private MethodInfo netCookRefreshSlotsMethod = null;
        private Type netCookStartCookCommandEventType = null;
        private Type netCookPrepareCommandType = null;
        private Type netCookStartCommandType = null;
        private Type netCookContinueCommandType = null;
        private Type netCookInteractCommandType = null;
        private object netCookReliableChannelValue = null;
        private bool netCookTypeDiagnosticsLogged = false;
        // Simulated F-key helper scheduling
        private float nextSimulatedFKeyClearAt = 0f;
        private float nextSimulatedFKeyUpClearAt = 0f;
        // Auto-cook diagnostics
        private int autoCookLoopTicks = 0;
        private float lastAutoCookHeartbeatAt = -999f;
        private string lastAutoCookException = null;
        // Legacy Auto Cook auto-stop timer fields kept for old routine reuse.
        private bool autoCookAutoStopEnabled = false;
        private int autoCookAutoStopHours = 0;
        private int autoCookAutoStopMinutes = 0;
        private int autoCookAutoStopSeconds = 0;
        private string autoCookAutoStopHoursInput = "0";
        private string autoCookAutoStopMinutesInput = "0";
        private string autoCookAutoStopSecondsInput = "0";
        private float autoCookAutoStopAt = -1f;
        private int autoFarmSubTab = 0; // 0 = Main, 1 = Tree Farm, 2 = Fish Farm, 3 = Insect Farm, 4 = Bird Farm
        private int automationSubTab = 0; // 0 = Main, 1 = Food & Repair, 2 = Snow Sculpting, 3 = Auto Buy, 4 = Auto Sell, 5 = Mass Cook, 6 = Puzzle, 7 = Pet Care
        private int selfSubTab = 0; // 0 = Main
        private Type cachedFishingGameplayApiType = null;
        private Type cachedFishingSubStateType = null;
        private MethodInfo cachedFishingEnterFishingMethod = null;
        private MethodInfo cachedFishingExitFishingMethod = null;
        private float lastFishingEnterRequestedAt = -999f;
        private float lastFishingExitRequestedAt = -999f;
        private GameObject cachedPlayerObject = null;
        private float cachedNearestPlayerDistance = 999f;
        private float nextNearestPlayerDistanceRefreshAt = 0f;
        private bool enablePlayerDetection = false;
        private bool treeFarmEnabled = false;
        private List<TreeFarmPatrolPoint> treeFarmPoints = new List<TreeFarmPatrolPoint>();
        private int treeFarmCurrentIndex = 0;
        private TreeFarmState treeFarmState = TreeFarmState.Idle;
        private float treeFarmNextActionAt = 0f;
        private int treeFarmChopSent = 0;
        private int treeFarmNoPromptAttempts = 0;
        private int treeFarmChopPressCount = 3;
        private float treeFarmChopPressGap = 0.5f;
        private float lastAutoSwingTime = 0f;
        private float swingCooldown = 1.2f;
        // Non-blocking swing confirmation state
        private bool awaitingSwingConfirm = false;
        private float swingConfirmDeadline = 0f;
        private int swingConfirmStartAnimHash = 0;
        private bool swingConfirmStartBtnInteract = false;
        private readonly string swingButtonPath = "GameApp/startup_root(Clone)/XDUIRoot/Bottom/TrackingPanel(Clone)/tracking_bar@w/tracking_sand_swing@go@w/root_visible@go/swing@btn";
        private float treeFarmArrivalDelay = 3f;
        private float treeFarmNextLocationWait = 1.5f;
        private string treeFarmStatus = "Idle";
        // If true, tree farm will use hardcoded map positions (like Resource farm)
        private bool treeFarmUseHardcoded = false;

        // Auto Buy fields
        private bool autoBuyEnabled = false;
        private bool autoBuyLogsEnabled = MasterLogAutoBuy;
        private bool forceOpenShopLogsEnabled = MasterLogForceOpenShop;
        private int autoBuySubState = 0; // 0=idle,1=teleporting,2=waiting_dialogue,3=selecting_store,4=buying,5=returning
        private Vector3 autoBuySavedPosition = Vector3.zero;
        private int autoBuyCurrentIngredientIndex = 0;
        private int autoBuyPurchasedCount = 0;
        private int autoBuyMaxPerIngredient = 50;
        private float autoBuyStepTimer = 0f;
        private float autoBuyPopupCloseRetryAt = 0f;
        private float autoBuyPopupSlowScanAt = 0f;
        private float nextAutoBuyLogicTime = 0f;
        private float nextAutoBuyBirdLogicTime = 0f;
        private float nextAutoBuyGardenLogicTime = 0f;
        private float nextAutoBuyFishingLogicTime = 0f;
        // Cached reflection for TryCloseAlertRewardPopupViaTipManager (avoids repeated GetMethods/FindLoadedType per call)
        private bool tipManagerReflectionInitialized = false;
        private Type cachedTipManagerType = null;
        private Type cachedAlertRewardPanelType = null;
        private PropertyInfo cachedTipInstanceProp = null;
        private MethodInfo cachedGetTipByTypeMethod = null;
        private MethodInfo cachedGetTipGenericMethod = null;
        private MethodInfo cachedCloseTipGenericMethod = null;
        private MethodInfo cachedAlertPanelClearMethod = null;
        private FieldInfo cachedTipPanelField = null;
        private float autoBuyShopWaitStartedAt = 0f;
        private int autoBuyStoreSelectRetryCount = 0;
        private float autoBuyPreviousGameSpeed = 1f;
        private bool autoBuyForcedGameSpeed = false;
        private int autoBuyShopScrollStep = -1;
        private readonly Vector3 autoBuyTargetPos = new Vector3(-12.574f, 31.572f, 43.554f);
        private readonly Vector3 autoBuyNearbyPos = new Vector3(-12.692f, 31.599f, 39.133f);
        private readonly string[] autoBuyIngredientsMatch = new string[] {

            "Springday Brown Sugar",
            "Salsa Sauce",
            "Pasteurized Egg",
            "Meat",
            "Red Bean",
            "Egg",
            "Milk",
            "Rice Flour",
            "Tea Leaves",
            "Cooking Oil",
            "Matcha Powder",
            "Cheese",
            "Butter",
            "Coffee Beans",
            "Universal Ingredient",
            "Amazing Seasoning"
        };

        // Auto Buy Store Selection (0=None, 1=Cooking, 2=Birdwatching, 3=Garden, 4=Fishing)
        private int autoBuySelectedStore = 0;
        private bool autoBuyStoreDropdownOpen = false;
        private readonly string[] autoBuyStoreOptions = new string[] { "None", "Cooking Store", "Birdwatching Store", "Garden Store", "Fishing Store" };

        // Auto Sell fields - direct quick-sell protocol, no sell-panel clicks.
        private bool autoSellEnabled = false;
        private string autoSellItemKey = "";
        private int autoSellMaxPerStack = 200;
        private int autoSellReserveCount = 0;
        private bool autoSellAllMatchingStacks = true;
        private bool autoSellFullStack = true;
        private bool autoSellSkipFiveStar = true;
        private bool dailyQuestSubmitSkipFiveStar = true;
        private bool autoSellMatchFamily = true;
        private bool autoSellHideBagItems = false;
        private int autoSellStarFilter = 0;
        private readonly string[] autoSellStarFilterLabels = new string[] { "Any Star", "1 Star", "2 Star", "3 Star", "4 Star", "5 Star" };
        private bool autoSellFestivalTokensEnabled = false;
        private readonly Dictionary<uint, int> autoSellCollectedStaticIdsByNetId = new Dictionary<uint, int>();
        private Dictionary<uint, string> autoSellLastSellDetailsByNetId = new Dictionary<uint, string>();
        private static HeartopiaComplete autoSellAuraMonoSearchHost;
        private static string autoSellAuraMonoSearchClass;
        private static string autoSellAuraMonoSearchNamespace;
        private static IntPtr autoSellAuraMonoSearchResult;
        private float autoSellInterval = 5f;
        private int autoSellScanSource = 0; // 0 = Bag, 1 = Warehouse, 2 = Both
        private readonly string[] autoSellScanSourceLabels = new string[] { "Bag", "Warehouse", "Both" };
        private bool autoSellScanSourceDropdownOpen = false;
        private float nextAutoSellAt = 0f;
        private string autoSellStatus = "Idle";
        private string autoSellLastMatchSummary = "No scan yet";
        private string autoSellSelectedDetails = "";
        private IntPtr autoSellMonoQuickSellMethod = IntPtr.Zero;
        private IntPtr autoSellMonoBattlePassSellMethod = IntPtr.Zero;
        private IntPtr autoSellMonoInt32ClassPtr = IntPtr.Zero;
        private IntPtr autoSellMonoUIntIntDictionaryClass = IntPtr.Zero;
        private IntPtr autoSellMonoUIntIntDictionarySetItemMethod = IntPtr.Zero;
        private static readonly bool AutoSellLogsEnabled = MasterLogAutoSell;
        private const bool BubbleRadarDebugLoggingEnabled = MasterLogBubbleRadar;

        // Auto Buy Birdwatching Store fields
        private bool autoBuyBirdEnabled = false;
        private int autoBuyBirdSubState = 0;
        private Vector3 autoBuyBirdSavedPosition = Vector3.zero;
        private int autoBuyBirdCurrentItemIndex = 0;
        private int autoBuyBirdPurchasedCount = 0;
        private int autoBuyBirdMaxPerItem = 10;
        private float autoBuyBirdStepTimer = 0f;
        private float autoBuyBirdShopWaitStartedAt = 0f;
        private int autoBuyBirdStoreSelectRetryCount = 0;
        private int autoBuyBirdShopScrollStep = -1;
        private float autoBuyBirdPreviousGameSpeed = 1f;
        private bool autoBuyBirdForcedGameSpeed = false;
        private readonly Vector3 autoBuyBirdTargetPos = new Vector3(11.210f, 37.141f, 5.951f);
        private readonly Vector3 autoBuyBirdNearbyPos = new Vector3(6.170f, 37.141f, 8.974f);
        private readonly string[] autoBuyBirdItemsMatch = new string[] {
            "Auto Bird Whistle",
            "Camouflage Bush"
        };

        // Auto Buy Garden Store fields
        private bool autoBuyGardenEnabled = false;
        private int autoBuyGardenSubState = 0;
        private Vector3 autoBuyGardenSavedPosition = Vector3.zero;
        private int autoBuyGardenCurrentItemIndex = 0;
        private int autoBuyGardenPurchasedCount = 0;
        private int autoBuyGardenMaxPerItem = 10;
        private float autoBuyGardenStepTimer = 0f;
        private float autoBuyGardenShopWaitStartedAt = 0f;
        private int autoBuyGardenStoreSelectRetryCount = 0;
        private int autoBuyGardenShopScrollStep = -1;
        private float autoBuyGardenPreviousGameSpeed = 1f;
        private bool autoBuyGardenForcedGameSpeed = false;
        private readonly Vector3 autoBuyGardenTargetPos = new Vector3(33.817f, 30.713f, -18.786f);
        private readonly Vector3 autoBuyGardenNearbyPos = new Vector3(35.627f, 30.656f, -14.268f);
        private readonly string[] autoBuyGardenItemsMatch = new string[] {
            "Fertilizer",
            "Growth Booster",
            "Rainbow Breeding Powder",
            "Quality Fertilizer",
            "Quality Growth Booster",
            "Top Fertilizer",
            "Top Growth Booster"
        };

        // Auto Buy Fishing Store fields
        private bool autoBuyFishingEnabled = false;
        private int autoBuyFishingSubState = 0;
        private Vector3 autoBuyFishingSavedPosition = Vector3.zero;
        private int autoBuyFishingCurrentItemIndex = 0;
        private int autoBuyFishingPurchasedCount = 0;
        private int autoBuyFishingMaxPerItem = 10;
        private float autoBuyFishingStepTimer = 0f;
        private float autoBuyFishingShopWaitStartedAt = 0f;
        private int autoBuyFishingStoreSelectRetryCount = 0;
        private int autoBuyFishingShopScrollStep = -1;
        private float autoBuyFishingPreviousGameSpeed = 1f;
        private bool autoBuyFishingForcedGameSpeed = false;
        private readonly Vector3 autoBuyFishingTargetPos = new Vector3(-241.214f, 10.622f, -100.257f);
        private readonly Vector3 autoBuyFishingNearbyPos = new Vector3(-237.951f, 10.777f, -104.959f);
        private readonly string[] autoBuyFishingItemsMatch = new string[] {
            "Bait",
            "Mermaid Fish Attractor",
            "Mermaid Perfume"
        };
        private int forceOpenShopSelectedIndex = 0;
        private bool forceOpenShopDropdownOpen = false;
        private string forceOpenShopManualStoreIdInput = string.Empty;
        private string forceOpenShopManualStoreNameInput = string.Empty;
        private string forceOpenShopStatus = "No shop selected.";
        private readonly Dictionary<string, int> forceOpenShopResolvedStoreIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly string[] forceOpenShopOptions = new string[]
        {
            "None",
            "Birdwatching Store",
            "Book Shop",
            "Carpet Shop",
            "Clothing Store",
            "Cooking Store",
            "Face Shop Panel",
            "Fishing Store",
            "Furniture Extra",
            "Fortune Store - Rainbow",
            "Fortune Store - Rain",
            "Garden Store",
            "General Store",
            "Insect Catching Store",
            "Pet Store",
            "Special Home Decor Store",
            "Showroom",
            "Meteor / Starfall Exchange"
        };

        // Hardcoded resource position arrays (ported from decompiled map data)
        private static readonly Vector3[] RockPositions = new Vector3[]
        {
            new Vector3(-31.7f, 20.1f, 115.2f),
            new Vector3(-10.3f, 21.4f, 107.6f),
            new Vector3(15.3f, 21.5f, 102.1f),
            new Vector3(47.9f, 21f, 96.7f),
            new Vector3(66.9f, 21.8f, 91.5f),
            new Vector3(66.7f, 20.5f, 138.9f),
            new Vector3(16.4f, 21.4f, 146.2f),
            new Vector3(-47.1f, 20f, 166.1f),
            new Vector3(-133.4f, 21.5f, 63.9f),
            new Vector3(-83.9f, 20.4f, 120.6f),
            new Vector3(-130.2f, 22.4f, 41.2f),
            new Vector3(-177f, 21.4f, 47.4f),
            new Vector3(-123.9f, 21.8f, -2f),
            new Vector3(-125.6f, 20f, -17.9f),
            new Vector3(-119.1f, 19.9f, -45.2f),
            new Vector3(-109.2f, 19.1f, -64f),
            new Vector3(-95.9f, 20f, -79.4f),
            new Vector3(95.9f, 23.4f, 70.6f),
            new Vector3(142.1f, 23f, 66f),
            new Vector3(97.4f, 22.7f, 50.6f)
        };

        private static readonly Vector3[] OrePositions = new Vector3[]
        {
            new Vector3(-152.3f, 21f, -86.1f),
            new Vector3(-131.3f, 19.2f, -70.4f),
            new Vector3(-169.3f, 19.9f, -34.2f),
            new Vector3(-145.2f, 20.2f, -16.8f),
            new Vector3(-175f, 20.4f, 6.4f),
            new Vector3(-144.1f, 20.7f, 28f),
            new Vector3(-178.4f, 21.4f, 66.3f),
            new Vector3(-98.6f, 20.3f, 171f),
            new Vector3(-74.1f, 20.1f, 149.6f),
            new Vector3(-26.9f, 20f, 161.5f),
            new Vector3(-22.6f, 20.1f, 131.4f),
            new Vector3(21.2f, 21.6f, 146.2f),
            new Vector3(35.3f, 21.2f, 127.2f),
            new Vector3(80.8f, 20.5f, 135.8f),
            new Vector3(141.5f, 23.1f, 78.5f),
            new Vector3(142f, 23.2f, 27.8f),
            new Vector3(130.8f, 21.6f, -6.6f),
            new Vector3(141.6f, 21.9f, -37.9f),
            new Vector3(110.5f, 20.9f, -63.4f),
            new Vector3(115.4f, 20.6f, -106.7f)
        };

        // Hardcoded tree position arrays (ported from decompiled map data)
        private static readonly Vector3[] TreePositions = new Vector3[]
        {
            new Vector3(139.7f, 21.7f, -44.3f),
            new Vector3(144.5f, 22.2f, -42.1f),
            new Vector3(145.2f, 22.3f, -44.1f),
            new Vector3(141.8f, 21.8f, -49.3f),
            new Vector3(152.2f, 21.2f, -50f),
            new Vector3(156.8f, 21f, -38.4f),
            new Vector3(161.7f, 21f, -35.7f),
            new Vector3(160.8f, 21.1f, -32.6f),
            new Vector3(178.4f, 21.7f, -6.1f),
            new Vector3(183.2f, 22.5f, -13.3f),
            new Vector3(198.2f, 17.5f, -23.4f),
            new Vector3(200.2f, 17.3f, -22f),
            new Vector3(198.9f, 17.6f, -17.1f),
            new Vector3(171.6f, 21.3f, 6.5f),
            new Vector3(183.9f, 24.2f, 16f),
            new Vector3(190.4f, 24.1f, 22.4f),
            new Vector3(190.4f, 25.9f, 30.1f),
            new Vector3(170.449f,20.501f,-39.699f),
            new Vector3(174.855f,20.990f,-36.675f),
            new Vector3(173.392f,20.410f,-46.537f),
            new Vector3(173.176f,20.615f,-48.654f),
            new Vector3(179.145f,20.299f,-46.644f),
            new Vector3(183.923f,19.960f,-37.363f),
            new Vector3(191.932f,17.834f,-46.321f),
            new Vector3(195.248f,17.604f,-38.141f),
            new Vector3(197.057f,17.686f,-41.829f),
            new Vector3(198.403f,17.369f,-44.197f),
            new Vector3(198.888f,17.338f,-39.695f),
            new Vector3(203.974f,15.249f,-39.667f),
            new Vector3(209.787f,12.042f,-49.710f),
            new Vector3(190.390f,19.568f,-19.386f),
            new Vector3(196.372f,19.753f,7.629f),
            new Vector3(197.307f,23.185f,14.063f),
            new Vector3(198.850f,23.215f,12.024f),
            new Vector3(206.653f,22.930f,12.314f),
            new Vector3(211.267f,22.288f,15.947f),
            new Vector3(209.340f,22.750f,19.160f),
            new Vector3(204.159f,23.431f,21.673f),
            new Vector3(207.259f,23.441f,33.904f),
            new Vector3(204.171f,23.436f,36.241f),
            new Vector3(207.511f,23.013f,40.964f),
            new Vector3(197.897f,27.291f,32.530f),
            new Vector3(196.316f,26.262f,38.628f),
            new Vector3(191.472f,24.962f,48.498f),
            new Vector3(194.644f,24.920f,52.629f),
            new Vector3(193.433f,25.541f,54.969f),
            new Vector3(190.077f,25.667f,55.608f)
        };

        private static readonly Vector3[] RareTreePositions = new Vector3[]
        {
            new Vector3(-91.5f, 20.4f, -83.1f),
            new Vector3(-64.1f, 20.4f, -68.5f),
            new Vector3(-123.1f, 27.3f, 100.8f),
            new Vector3(56f, 23.7f, -70.4f),
            new Vector3(-106.8f, 25.4f, 60.7f),
            new Vector3(55.8f, 25.4f, 73.9f),
            new Vector3(75.3f, 22.5f, 87.5f),
            new Vector3(84.2f, 18.5f, -114.4f)
        };

        private static readonly Vector3[] AppleTreePositions = new Vector3[]
        {
            new Vector3(141.7f, 22.9f, 74.7f),
            new Vector3(113.1f, 22.9f, 40.1f),
            new Vector3(141.4f, 22.9f, 18f),
            new Vector3(106f, 22.9f, 0.9f),
            new Vector3(105.5f, 21.5f, -14.9f),
            new Vector3(92.8f, 21.8f, -20.3f),
            new Vector3(131.1f, 21.4f, -41.9f),
            new Vector3(92.4f, 21.7f, -44.3f),
            new Vector3(109.2f, 21.4f, -52.8f),
            new Vector3(73.2f, 20.6f, -78f),
            new Vector3(75.1f, 20.6f, -90.3f),
            new Vector3(18.3f, 21.6f, 146.7f),
            new Vector3(36.3f, 20.6f, 115.9f),
            new Vector3(43.7f, 20.5f, 98.5f),
            new Vector3(67.2f, 20.9f, 94.4f),
            new Vector3(-16.1f, 22.9f, 103.0f),
            new Vector3(8.5f, 23.2f, 97.2f),
            new Vector3(28.7f, 22.7f, 96.4f),
            new Vector3(75.5f, 20.7f, 93.4f),
            new Vector3(63.2f, 20.6f, 139.7f),
            new Vector3(94.2f, 22.3f, 55.6f),
            new Vector3(98.1f, 23.1f, 75.3f),
            new Vector3(97.4f, 22.9f, 46.3f),
            new Vector3(94.2f, 22.4f, 29.1f),
            new Vector3(94.7f, 22.6f, 6.3f),
            new Vector3(84.7f, 20.8f, -65.5f),
            new Vector3(125.1f, 21.2f, -89.6f)
        };

        private static readonly Vector3[] OrangeTreePositions = new Vector3[]
        {
            new Vector3(-107.4f, 19.1f, -67f),
            new Vector3(-139.3f, 19.4f, -105.9f),
            new Vector3(-159.4f, 20.2f, -64.3f),
            new Vector3(-120.4f, 20.2f, -29.9f),
            new Vector3(-123.6f, 20.2f, -22.8f),
            new Vector3(-125.4f, 21.4f, -8.9f),
            new Vector3(-121.1f, 22.2f, -6.1f),
            new Vector3(-174f, 20.4f, 9.2f),
            new Vector3(-155.7f, 20.8f, 30.5f),
            new Vector3(-127.4f, 23f, 40.5f),
            new Vector3(-131.6f, 22f, 69.8f),
            new Vector3(-131.2f, 22.3f, 76.4f),
            new Vector3(-95.8f, 20.6f, 118.4f),
            new Vector3(-81.6f, 20.7f, 118.4f),
            new Vector3(-67.1f, 20.4f, 139.4f),
            new Vector3(-32.8f, 20.2f, 160.3f),
            new Vector3(-36.1f, 20.2f, 115.5f),
            new Vector3(-56.5f, 20.2f, 120.4f),
            new Vector3(-65.5f, 21.0f, 120.2f),
            new Vector3(-93.7f, 20.1f, 166.9f),
            new Vector3(-114.2f, 20.3f, 128.9f),
            new Vector3(-177.1f, 21.6f, 38.8f),
            new Vector3(-126.4f, 22f, 23.5f),
            new Vector3(-117.3f, 19.9f, -50.6f),
            new Vector3(-134.5f, 19.2f, -66.4f),
            new Vector3(-94.2f, 19.6f, -89.1f),
            new Vector3(-92.8f, 19.3f, -95.2f)
        };

        // Tree cooldown bookkeeping for hardcoded resource-style mode
        private Dictionary<int, float> treeCooldowns = new Dictionary<int, float>();
        private Dictionary<int, float> rareTreeCooldowns = new Dictionary<int, float>();
        private Dictionary<int, float> appleTreeCooldowns = new Dictionary<int, float>();
        private Dictionary<int, float> orangeTreeCooldowns = new Dictionary<int, float>();

        private Dictionary<int, float> treeHideUntil = new Dictionary<int, float>();
        private Dictionary<int, float> rareTreeHideUntil = new Dictionary<int, float>();
        private Dictionary<int, float> appleTreeHideUntil = new Dictionary<int, float>();
        private Dictionary<int, float> orangeTreeHideUntil = new Dictionary<int, float>();

        private float treeCooldownDuration = 300f;
        private float rareTreeCooldownDuration = 600f;
        private float appleTreeCooldownDuration = 300f;
        private float orangeTreeCooldownDuration = 300f;

        private float treeHideDelay = 10f;

        private System.Random instanceRng = new System.Random();
        // Auto Resource Farm fields (ported and adapted)
        private bool autoResourceFarmEnabled = false;
        private bool farmRocks = false;
        private bool farmOres = false;
        private bool farmTrees = true;
        private bool farmRareTrees = false;
        private bool farmAppleTrees = false;
        private bool farmOrangeTrees = false;

        public Dictionary<int, float> rockCooldowns = new Dictionary<int, float>();
        public Dictionary<int, float> oreCooldowns = new Dictionary<int, float>();
        public Dictionary<int, float> treeCooldowns_res = new Dictionary<int, float>();
        public Dictionary<int, float> rareTreeCooldowns_res = new Dictionary<int, float>();
        public Dictionary<int, float> appleTreeCooldowns_res = new Dictionary<int, float>();
        public Dictionary<int, float> orangeTreeCooldowns_res = new Dictionary<int, float>();

        public Dictionary<int, float> rockHideUntil = new Dictionary<int, float>();
        public Dictionary<int, float> oreHideUntil = new Dictionary<int, float>();
        public Dictionary<int, float> treeHideUntil_res = new Dictionary<int, float>();
        public Dictionary<int, float> rareTreeHideUntil_res = new Dictionary<int, float>();
        public Dictionary<int, float> appleTreeHideUntil_res = new Dictionary<int, float>();
        public Dictionary<int, float> orangeTreeHideUntil_res = new Dictionary<int, float>();

        public float rockCooldownDuration = 300f;
        public float oreCooldownDuration = 300f;
        public float treeCooldownDuration_res = 300f;
        public float rareTreeCooldownDuration_res = 600f;
        public float appleTreeCooldownDuration_res = 300f;
        public float orangeTreeCooldownDuration_res = 300f;
        private float nextLiveResourceCooldownSyncAt = 0f;
        private float liveResourceCooldownSyncInterval = 1f;

        private List<Vector3> resourceMarkerPositions = new List<Vector3>();
        private readonly List<Vector3> resourceMarkerBuffer = new List<Vector3>(512);
        private int currentResourceMarkerIndex = 0;
        private bool resourceMarkersNeedShuffle = true;
        private HashSet<int> visitedResourceMarkerIndices = new HashSet<int>();
        private int lastResourceMarkerCount = 0;
        private bool isResourceFarmTeleport = false;
        // Auto Resource Farm auto-stop fields
        private bool autoResourceFarmAutoStopEnabled = false;
        private int autoResourceFarmAutoStopHours = 0;
        private int autoResourceFarmAutoStopMinutes = 0;
        private int autoResourceFarmAutoStopSeconds = 0;
        private string autoResourceFarmAutoStopHoursInput = "0";
        private string autoResourceFarmAutoStopMinutesInput = "0";
        private string autoResourceFarmAutoStopSecondsInput = "0";
        private float autoResourceFarmAutoStopAt = -1f;
        private bool resourceJustArrived = false;
        private float resourceArrivalTime = 0f;
        private float lastResourceTeleportTime = 0f;
        private Vector3 resourceStartPosition = Vector3.zero;
        private bool hasResourceStartPosition = false;
        private bool isResourceReturningToStart = false;
        private float resourceTeleportCooldown = 3f;
        private float resourceClickDuration = 1.0f;
        private float resourceArrivalDelay = 0.5f;
        private int fKeySimFrame = 0;
        private int resourceClickCount = 0;
        private float bottomDialogClickTimer = 0f;
        private GameObject cachedBottomDialogObject = null;
        private float nextBottomDialogLookupAt = 0f;
        // When enabling resource farm, wait for axe equip if needed
        private bool resourceWaitingForEquip = false;
        private float resourceWaitingForEquipUntil = 0f;
        private int resourceEquipAttempts = 0;
        private const int resourceEquipMaxAttempts = 3;
        public static bool SimulateFKeyHeld = false;
        public static bool SimulateFKeyDown = false;
        public static bool SimulateFKeyUp = false;
        
        // Noclip/Flying Variables
        private bool noclipEnabled = false;
        private float noclipSpeed = 10f;
        private float noclipBoostMultiplier = 2f;
        // Persisted slider backups for cross-instance load
        private float saved_autoFishScanTimeout = -1f;
        private float saved_autoFishTeleportDelay = -1f;
        private float saved_autoFishFishShadowDetectRange = -1f;
        private float saved_autoFishReelMaxDuration = -1f;
        private float saved_autoFishReelHoldDuration = -1f;
        private float saved_autoFishReelPauseDuration = -1f;
        private GameObject cachedToastTextObj = null;
        private GameObject cachedToastRootObj = null;
        private float nextToastRootPathScanAt = 0f;
        private GameObject cachedEnergyTextObj = null;
        private Component cachedEnergyTextComponent = null;
        private Il2CppPropertyInfo cachedEnergyTextProperty = null;
        private float nextEnergyTextPathScanAt = 0f;
        private string lastDetectedToast = "";
        private int lastDetectedToastObjectId = 0;
        private float lastDetectedToastAt = -999f;
        private string lastKnownEnergyDisplay = "100/100";
        private float lastKnownEnergyRatio = 1.0f;
        private float lastToastCheckAt = 0f;
        private const float TOAST_CHECK_INTERVAL = 0.5f;
        private IntPtr cachedInsectCatchMonoMethod = IntPtr.Zero;
        private int cachedInsectCatchMonoMethodParamCount = 0;
        private IntPtr cachedBirdPhotoMonoMethod = IntPtr.Zero;
        private int cachedBirdPhotoMonoMethodParamCount = 0;
        private IntPtr cachedBirdEscapeMonoMethod = IntPtr.Zero;
        private int cachedBirdEscapeMonoMethodParamCount = 0;
        private IntPtr cachedBirdRemoveMonoMethod = IntPtr.Zero;
        private int cachedBirdRemoveMonoMethodParamCount = 0;
        private bool birdFarmMaxPhotoPatchApplied = false;
        private bool birdFarmMaxPhotoPatchUnavailableLogged = false;
        private float nextBirdFarmMaxPhotoPatchAttemptAt = -999f;
        private bool warehouseBypassEnabled = false;
        internal bool WarehouseBypassEnabled => this.warehouseBypassEnabled;
        // Stranger Chat Bypass
        private bool strangerChatBypassEnabled = false;
        private bool strangerChatBypassPatchApplied = false;
        private bool strangerChatBypassPatchUnavailableLogged = false;
        private bool strangerChatOriginalInSelfRoom = false;
        private bool strangerChatOriginalInSelfRoomValid = false;
        private float nextStrangerChatBypassPatchAttemptAt = -999f;
        private IntPtr cachedStrangerChatSelfRoomInRoomFieldPtr = IntPtr.Zero;
        private IntPtr cachedStrangerChatSelfRoomUpdateMethodPtr = IntPtr.Zero;
        private IntPtr cachedStrangerChatSelfRoomProtocolMethodPtr = IntPtr.Zero;
        private float lastBirdFarmMaxPhotoScareAt = -999f;
        private uint lastBirdFarmMaxPhotoScareNetId = 0U;
        private Type cachedBirdProtocolManagerRuntimeType = null;
        private MethodInfo cachedBirdPhotoDirectMethod = null;
        private MethodInfo cachedScannerStatusPanelGetScanningBirdNetIdMethod = null;
        private MethodInfo cachedEntityUtilGetEntityResIdMethod = null;
        private Type cachedBirdPhotoCommandRuntimeType = null;
        private object cachedBirdPhotoClientPeerRuntimeObject = null;
        private MethodInfo cachedBirdPhotoClientPeerSendMethod = null;
        private MethodInfo cachedBirdPhotoSendCommandMethod = null;
        private Type cachedBirdPhotoDetailInfoRuntimeType = null;
        private FieldInfo cachedBirdPhotoDetailInfoRuntimeActionStarField = null;
        private FieldInfo cachedBirdPhotoDetailInfoRuntimeIsPerfectStarField = null;
        private FieldInfo cachedBirdPhotoDetailInfoRuntimeIsCoverStarField = null;
        private FieldInfo cachedBirdPhotoDetailInfoRuntimeActionTypeField = null;
        private FieldInfo cachedBirdPhotoDetailInfoRuntimeIsUsingZoomField = null;
        private FieldInfo cachedBirdPhotoDetailInfoRuntimeStandNetIdField = null;
        private object cachedBirdPhotoReliableChannelValue = null;
        private string cachedBirdPhotoDirectResolveStatus = "not attempted";
        private float cachedBirdPhotoDirectResolveNextAttemptAt = -999f;
        private string cachedBirdPhotoDirectClientResolveStatus = "not attempted";
        private float cachedBirdPhotoDirectClientNextAttemptAt = -999f;
        private string cachedBirdPhotoDirectCommandResolveStatus = "not attempted";
        private float cachedBirdPhotoDirectCommandNextAttemptAt = -999f;
        private IntPtr cachedBirdCamouflageBackpackClass = IntPtr.Zero;
        private IntPtr cachedBirdCamouflageUseMethod = IntPtr.Zero;
        private IntPtr cachedBirdCamouflageCtorMethod = IntPtr.Zero;
        private IntPtr cachedBirdCamouflageNetIdField = IntPtr.Zero;
        private readonly List<uint> lastInsectFarmSentNetIds = new List<uint>();
        private readonly List<uint> lastBirdFarmSentNetIds = new List<uint>();
        private readonly HashSet<uint> birdFarmBurstSentNetIds = new HashSet<uint>();
        private readonly Dictionary<uint, float> recentBirdFarmPhotoNetIds = new Dictionary<uint, float>();
        private readonly Dictionary<uint, int> birdFarmPhotoCountByNetId = new Dictionary<uint, int>();
        private bool birdFarmSpamMaxPhotoModeActive = false;
        private readonly List<BirdFarmAuraCandidate> cachedBirdFarmAuraCandidates = new List<BirdFarmAuraCandidate>();
        private readonly List<IntPtr> birdFarmAuraPhotoModeScannablesBuffer = new List<IntPtr>(64);
        private readonly List<uint> birdFarmAuraPhotoModeScannablePins = new List<uint>(64);
        private readonly HashSet<uint> birdFarmAuraPhotoModeSeenNetIds = new HashSet<uint>();
        private readonly List<IntPtr> birdFarmAuraComponentBuffer = new List<IntPtr>(64);
        private readonly List<IntPtr> birdFarmAuraLevelEntityComponentsBuffer = new List<IntPtr>(64);
        private readonly List<IntPtr> birdFarmAuraStandComponentsBuffer = new List<IntPtr>(64);
        private readonly List<IntPtr> birdFarmAuraStateBuffer = new List<IntPtr>(8);
        private float cachedBirdFarmAuraCandidatesAt = -999f;
        private float cachedBirdFarmAuraNextScanAt = -999f;
        private Vector3 cachedBirdFarmAuraOrigin = Vector3.zero;
        private float cachedBirdFarmAuraRange = -1f;
        private float cachedBirdFarmAuraCacheTtl = 5f;
        private float cachedBirdFarmAuraMoveTolerance = 4f;
        private int cachedBirdFarmAuraEntityCount = 0;
        private float nextBirdFarmPhotoModeMissingBackoffAt = -999f;
        private float nextBirdFarmPhotoModeComponentRefreshAt = -999f;
        private float nextBirdFarmManagedFallbackScanAt = -999f;
        private float nextBirdFarmCleanupAt = -999f;
        private int birdFarmDenseVerifyOffset = 0;
        private int birdFarmDenseEmptyScanStreak = 0;
        private IntPtr cachedBirdPhotoDetailInfoMonoClass = IntPtr.Zero;
        private IntPtr cachedBirdPhotoCommandMonoClass = IntPtr.Zero;
        private IntPtr cachedBirdPhotoNetworkClientMonoClass = IntPtr.Zero;
        private IntPtr cachedBirdPhotoDetailInfoActionStarField = IntPtr.Zero;
        private IntPtr cachedBirdPhotoDetailInfoIsPerfectStarField = IntPtr.Zero;
        private IntPtr cachedBirdPhotoDetailInfoActionTypeField = IntPtr.Zero;
        private IntPtr cachedBirdPhotoDetailInfoStandNetIdField = IntPtr.Zero;
        private bool cachedBirdPhotoDetailInfoFieldsResolved = false;
        private readonly Dictionary<AuraMonoMethodCacheKey, IntPtr> auraMonoMethodLookupCache = new Dictionary<AuraMonoMethodCacheKey, IntPtr>();
        // Cached EntitiesManager class + GetEntity method so per-entity netId->object resolution
        // (TryGetAuraMonoEntityObjectByNetId) does not repeat FindAuraMonoImage / class-from-name
        // on every call. Mono class/method pointers are stable for the process lifetime.
        private IntPtr cachedAuraMonoEntitiesManagerClass = IntPtr.Zero;
        private IntPtr cachedAuraMonoEntitiesGetEntityMethod = IntPtr.Zero;
        private readonly Dictionary<AuraMonoFieldCacheKey, IntPtr> auraMonoFieldLookupCache = new Dictionary<AuraMonoFieldCacheKey, IntPtr>();
        private readonly HashSet<uint> _birdFarmSeenNetIds = new HashSet<uint>();
        // Component verification cache: once we know a netId is (or is not) a real bird,
        // skip the expensive GetAllComponents Mono invoke for the next 15 seconds.
        private readonly Dictionary<uint, float> _verifiedBirdEntityNetIds = new Dictionary<uint, float>();
        private readonly Dictionary<uint, float> _rejectedBirdEntityNetIds = new Dictionary<uint, float>();
        private readonly Dictionary<uint, BirdFarmAuraResolvedDetail> _birdFarmResolvedDetailsByNetId = new Dictionary<uint, BirdFarmAuraResolvedDetail>();
        private readonly List<uint> birdFarmExpiredNetIdBuffer = new List<uint>(64);
        private const bool RadarIconEspDebugLoggingEnabled = MasterLogRadarIconEsp;
        private static readonly bool birdFarmDisableAuraEntityScan = true;
        private string lastBirdPhotoModeResolveStatus = "not attempted";
        private const float BirdFarmManagedFallbackScanInterval = 12f;
        private const float BirdFarmCleanupInterval = 1f;
        private const float BirdEntityVerifyCacheTtl = 15f;
        private const int BirdPoseStretch = 3;
        // AuraMono radar: positions of bird entities found by the entity scan, refreshed with the entity cache.
        private readonly List<Vector3> _auraMonoBirdRadarPositions = new List<Vector3>();
        private float _auraMonoBirdRadarRefreshedAt = -999f;
        private float lastBirdFarmSendAt = -999f;
        private uint lastBirdFarmAttemptedNetId = 0U;
        private uint lastBirdFarmRecentPhotoNetId = 0U;
        private float lastBirdFarmRecentPhotoNetIdAt = -999f;
        private readonly Queue<uint> pendingBirdFarmAttemptedNetIds = new Queue<uint>();
        private const string BOTTOM_DIALOG_PATH = "GameApp/startup_root(Clone)/XDUIRoot/Popup/BottomDialogPanel(Clone)";
        private const float BOTTOM_DIALOG_CLICK_INTERVAL = 0.3f;

        // Lazy patch state for the hot Unity methods (Transform.position/rotation setters,
        // CharacterController.Move, Input.GetKey*). These are only installed once the
        // corresponding in-game feature is actually used, so they cost nothing when idle.
        private bool positionOverridePatched = false;
        private bool rotationOverridePatched = false;
        private bool inputSimPatched = false;

        // True while we have an outstanding DisableInput(Move) on the game's MonoInputManager
        // because the mod menu is open with "block game input" on. Must be balanced 1:1 with EnableInput.
        private bool menuMoveInputDisabled = false;

        // Bypass overlap building state
        private bool bypassOverlapEnabled = false;
        private bool bypassOverlapPatched = false;
        private HarmonyLib.Harmony bypassHarmony = null;
        private static bool bypassOverlapEnabledStatic = false;

        private bool autoJoinFriendEnabled = false;
        private bool autoClickStartEnabled = false;
        private bool autoCloseAnnouncementEnabled = false;
        private float nextAnnouncementCloseCheckAt = -999f;
        private bool lobbyJoinInProgress = false;
        private bool lobbyJoinIsMyTown = false;
        private LobbyJoinState lobbyJoinState = LobbyJoinState.Idle;
        private float lobbyJoinNextActionAt = 0f;
        private int lobbyJoinRefreshAttempts = 0;
        private float lobbyNextAutoJoinAttemptAt = 0f;
        private float lobbyNextAutoStartClickAt = 0f;
        private string lobbyAutoJoinStatus = "Idle";
        // Token: 0x0400001E RID: 30
        private float nextFarmTime;

        // Token: 0x0400001F RID: 31
        private float nextCookTime;

        // Token: 0x04000020 RID: 32
        private readonly float farmPeriod = 1f;
        private bool meteorAutoInteractActive = false;
        private int meteorAutoInteractClicksRemaining = 0;
        private float meteorAutoInteractTimer = 0f;
        private const float meteorAutoInteractInterval = 0.2f;

        // Token: 0x04000022 RID: 34
        private GameObject cacheStatusAnim;

        // Token: 0x04000023 RID: 35
        private GameObject cacheCookUI;

        // Token: 0x04000024 RID: 36
        private GameObject cacheSkeletonBody;
        private bool bypassObjectsHidden = false;
        private string lastLoggedInteractSpriteName = string.Empty;
        private float nextInteractSpriteDebugAt = 0f;

        // Token: 0x04000025 RID: 37
        private bool collectMushrooms = true;

        // Token: 0x04000026 RID: 38
        private bool collectBerries = true;

        private bool collectEventResources = true;

        // Token: 0x04000027 RID: 39
        private bool collectOther = false;

        // Token: 0x04000028 RID: 40
        private GameObject radarContainer;

        // Token: 0x04000029 RID: 41
        public bool isRadarActive = false;

        // Token: 0x0400002A RID: 42
        private bool showMushroomRadar = false;
        private bool showOysterMushroomRadar = false;
        private bool showButtonMushroomRadar = false;
        private bool showPennyBunRadar = false;
        private bool showShiitakeRadar = false;
        private bool showTruffleRadar = false;
        private bool radarMushroomsDropdownOpen = false;
        private bool radarBerriesDropdownOpen = false;
        private bool radarEventsDropdownOpen = false;
        private bool radarResourcesDropdownOpen = false;
        private bool radarTreesDropdownOpen = false;
        private bool radarMiscDropdownOpen = false;
        private bool showFiddleheadRadar = false;
        private bool showTallMustardRadar = false;
        private bool showBurdockRadar = false;
        private bool showMustardGreensRadar = false;

        // Token: 0x0400002B RID: 43
        private bool showBlueberryRadar = false;

        // Token: 0x0400002C RID: 44
        private bool showRaspberryRadar = false;

        // Token: 0x0400002D RID: 45
        private bool showStoneRadar = false;

        // Token: 0x0400002E RID: 46
        private bool showOreRadar = false;

        // Token: 0x0400002E RID: 46
        private bool showBubbleRadar = false;

        private bool showBirdRadar = false;
        private bool showOtherPlayersRadar = false;

        // Token: 0x0400002E RID: 46
        public bool showInsectRadar = false;

        // Token: 0x0400002F RID: 47
        public bool showFishShadowRadar = false;
        private bool showMeteorRadar = false;
        // Draw radar objects as a GUI overlay (like meteors) regardless of world distance
        private bool showRadarGuiOverlay = false;
        
        // Token: 0x04000030 RID: 48
        private bool showTreeRadar = false;
        private bool showRareTreeRadar = false;
        private bool showAppleTreeRadar = false;
        private bool showOrangeTreeRadar = false;

        // Token: 0x04000030 RID: 48
        private float lastScanTime = 0f;

        // Token: 0x04000031 RID: 49
        private const float scanInterval = 2f;

        // Token: 0x04000031 RID: 49
        private const float blueberryRadarRange = 80f;

        // Token: 0x04000032 RID: 50
        private const float raspberryRadarRange = 80f;

        // Token: 0x04000033 RID: 51
        private Dictionary<GameObject, GameObject> markerToTarget = new Dictionary<GameObject, GameObject>();

        // Token: 0x04000034 RID: 52
        private Dictionary<int, RadarMarkerMetadata> markerMetadataById = new Dictionary<int, RadarMarkerMetadata>();

        // Token: 0x04000035 RID: 53
        private Dictionary<int, GameObject> trackedObjectMarkers = new Dictionary<int, GameObject>();
        private readonly Dictionary<string, Texture2D> radarIconEspTextures = new Dictionary<string, Texture2D>();
        private readonly Dictionary<string, float> radarIconEspRetryAt = new Dictionary<string, float>();
        private readonly Dictionary<int, string> radarStaticIdToIconKey = new Dictionary<int, string>();
        private readonly Dictionary<string, float> radarSpeciesDebugNextLogAt = new Dictionary<string, float>();
        private readonly Dictionary<string, float> bubbleRadarDebugNextLogAt = new Dictionary<string, float>();
        private HashSet<string> loggedUnknownForageMeshNames = new HashSet<string>();

        // Radar scan throttle - prevents calling FindObjectsOfType<GameObject>() more than
        // once every 2s inside RunRadar. We deliberately do NOT cache the array in a class field;
        // storing IL2CPP native object references across frames causes use-after-free crashes when
        // Unity destroys those objects while we still hold the C# wrapper.
        private float _cachedRadarGameObjectsAt = -999f;
        private const float RadarGOScanInterval = 2f;

        // Bag / Warehouse (backpack <-> warehouse transfer via BackPackSystem protocol)
        private const int TransferBatchMaxCount = 256;
        private const float TransferQtyHoldRepeatDelay = 0.5f;
        private const float TransferQtyHoldSlowInterval = 0.1f;
        private const float TransferQtyHoldFastInterval = 0.05f;
        private const float TransferQtyHoldFastAfterSeconds = 1f;
        private readonly string[] transferScanSourceLabels = { "Bag", "Warehouse" };
        private int transferScanSource = 0;
        private bool transferScanSourceDropdownOpen = false;
        private bool transferMultiSelectMode = false;
        private bool transferSelectFullStack = false;
        private List<TransferItemEntry> transferItems = null;
        private int selectedTransferIndex = -1;
        private int transferQty = 1;
        private int transferQtyHoldDirection = 0;
        private uint transferQtyHoldNetId = 0U;
        private int transferQtyHoldItemIndex = -1;
        private float transferQtyHoldStartedAt = 0f;
        private float transferQtyHoldLastStepAt = 0f;
        private Vector2 transferItemScrollPos = Vector2.zero;
        private string transferStatus = "Idle";
        private float transferPendingRescanAt = 0f;
        private int transferPendingRescanRetries = 0;
        private readonly Dictionary<uint, int> transferBatch = new Dictionary<uint, int>();
        private IntPtr transferMonoMoveBatchMethod = IntPtr.Zero;
        private MethodInfo cachedMoveBatchBackpackItemsMethod = null;

        // Token: 0x04000035 RID: 53
        private Dictionary<int, float> blueberryCooldowns = new Dictionary<int, float>();
        private readonly List<int> expiredBerryCooldownBuffer = new List<int>(32);

        // Token: 0x04000036 RID: 54
        private Dictionary<int, float> blueberryHideUntil = new Dictionary<int, float>();

        // Token: 0x04000037 RID: 55
        private Dictionary<int, float> blueberryJustCollected = new Dictionary<int, float>();

        // Token: 0x04000038 RID: 56
        private float blueberryCooldownDuration = 125f;

        // Token: 0x04000039 RID: 57
        private const float blueberryHideDelay = 10f;

        // Token: 0x0400003A RID: 58
        private const float blueberryCollectDelay = 4f;

        // Token: 0x0400003B RID: 59
        private Button lastBlueberryButton = null;
        private System.Action blueberryCollectListener = null;
        private const float ManualBerryListenerCheckInterval = 0.5f;
        private float nextManualBerryListenerCheckAt = -999f;

        // Token: 0x0400003C RID: 60
        private Dictionary<int, float> raspberryCooldowns = new Dictionary<int, float>();

        // Token: 0x0400003D RID: 61
        private Dictionary<int, float> raspberryHideUntil = new Dictionary<int, float>();

        // Token: 0x0400003E RID: 62
        private Dictionary<int, float> raspberryJustCollected = new Dictionary<int, float>();

        // Token: 0x0400003F RID: 63
        private float raspberryCooldownDuration = 125f;

        // Token: 0x04000040 RID: 64
        private const float raspberryHideDelay = 10f;

        // Token: 0x04000041 RID: 65
        private const float raspberryCollectDelay = 4f;

        // Token: 0x04000042 RID: 66
        private Button lastRaspberryButton = null;
        private System.Action raspberryCollectListener = null;

        // Token: 0x04000043 RID: 67
        private bool autoFarmActive = false;

        // Token: 0x04000044 RID: 68
        private string autoFarmStatus = "Idle";

        // Token: 0x04000045 RID: 69
        private float autoFarmTimer = 0f;

        private bool autoFarmAutoStopEnabled = false;
        private int autoFarmAutoStopHours = 0;
        private int autoFarmAutoStopMinutes = 0;
        private int autoFarmAutoStopSeconds = 0;
        private string autoFarmAutoStopHoursInput = "0";
        private string autoFarmAutoStopMinutesInput = "0";
        private string autoFarmAutoStopSecondsInput = "0";
        private float autoFarmAutoStopAt = -1f;

        // Token: 0x04000046 RID: 70
        private int currentLocationIndex = 0;

        // Token: 0x04000047 RID: 71
        private HeartopiaComplete.AutoFarmState farmState = HeartopiaComplete.AutoFarmState.Idle;

        // Token: 0x04000048 RID: 72
        private Vector3 lastNodePosition = Vector3.zero;

        // Token: 0x04000049 RID: 73
        private Dictionary<Vector3, float> recentlyVisitedNodes = new Dictionary<Vector3, float>();

        // Token: 0x0400004A RID: 74
        private const float nodeVisitCooldown = 15f;

        // Token: 0x0400004B RID: 75
        private bool autoCollectClickedSinceArrival = false;

        // Token: 0x0400004C RID: 76
        private int cameraRotationAttempts = 0;

        // Token: 0x0400004D RID: 77
        private const int maxCameraRotationAttempts = 3;

        // Token: 0x0400004E RID: 78
        private float cameraStuckDisplayTimer = 0f;

        // Token: 0x0400004F RID: 79
        private float areaLoadDelay = 4f;

        // --- AUTO FARM PRIORITIES ---
        private bool priorityOysterMushroom = false;
        private bool priorityButtonMushroom = false;
        private bool priorityPennyBun = false;
        private bool priorityShiitake = false;
        private bool priorityTruffle = false;
        private bool priorityFiddlehead = false;
        private bool priorityTallMustard = false;
        private bool priorityBurdock = false;
        private bool priorityMustardGreens = false;
        private bool priorityBlueberry = false;
        private bool priorityRaspberry = false;
        private bool priorityBubble = false;
        private bool priorityInsect = false;

        // Priority farming state
        private List<Vector3> activePriorityLocations = new List<Vector3>();
        private Dictionary<Vector3, float> priorityLocationCooldowns = new Dictionary<Vector3, float>();
        private float priorityRecheckTimer = 0f;
        private Vector3? currentPriorityLocation = null;
        private Vector3? lastFoundPriorityNodeLocation = null;
        private bool lastTeleportWasPriorityLocation = false;

        // --- STATIC LOOT LOCATIONS FOR PRIORITIES ---
        private Dictionary<string, Vector3> priorityLocations = new Dictionary<string, Vector3>()
        {
            { "Oyster Mushroom", new Vector3(36.603138f, 26.140745f, 212.39085f) },
            { "Button Mushroom", new Vector3(-219.81989f, 12.863783f, 6.995692f) },
            { "Penny Bun", new Vector3(175.89377f, 25.673292f, 55.985367f) },
            { "Shiitake", new Vector3(-66.63026f, 14.248707f, -169.89787f) },
            { "Black Truffle", new Vector3(258.11917f, 13.1247f, 95.18241f) },
            { "Fiddlehead", new Vector3(229.782f, 11.404f, 48.837f) },
            { "Tall Mustard", new Vector3(-125.213f, 11.729f, 290.797f) },
            { "Burdock", new Vector3(-211.599f, 29.916f, 35.416f) },
            { "Mustard Greens", new Vector3(-58.984f, 11.035f, -155.413f) },
            { "Blueberry", new Vector3(-114.2f, 20.1f, 142f) },
            { "Raspberry", new Vector3(-162.2f, 23.6f, 86.2f) }
        };

        // Token: 0x04000050 RID: 80
        private Vector3[] blueberryPositions = new Vector3[]
        {
            new Vector3(-5.86f, 23.17f, 99.33f),
            new Vector3(-14.15f, 22.26f, 105.27f),
            new Vector3(-28.76f, 20.56f, 113.32f),
            new Vector3(-40.26f, 20.28f, 115.89f),
            new Vector3(-58.97f, 20.68f, 117.67f),
            new Vector3(-65.91f, 20.14f, 123.47f),
            new Vector3(-78.43f, 20.29f, 121.14f),
            new Vector3(-93.45f, 20.52f, 118.26f),
            new Vector3(21.94f, 22.14f, 98.96f),
            new Vector3(37.23f, 20.74f, 100.19f),
            new Vector3(47.98f, 21.6f, 94.41f),
            new Vector3(64.27f, 21f, 94.7f),
            new Vector3(76.87f, 20.5f, 97.59f),
            new Vector3(80.03f, 20.47f, 108.47f),
            new Vector3(74.95f, 20.64f, 137.79f),
            new Vector3(45.61f, 20.67f, 141.06f),
            new Vector3(24.01f, 21.66f, 146.53f),
            new Vector3(9.77f, 21.48f, 147.94f),
            new Vector3(-23.05f, 20.09f, 158.1f),
            new Vector3(-44.83f, 19.96f, 164.72f),
            new Vector3(97.88f, 22.73f, 52.9f),
            new Vector3(100.22f, 22.88f, 43.81f),
            new Vector3(97.27f, 23.1f, 67.74f),
            new Vector3(121.26f, 23.08f, 84.34f),
            new Vector3(137.91f, 23.15f, 82.24f),
            new Vector3(142.78f, 22.95f, 63.66f),
            new Vector3(123.6f, 22.89f, 43.15f),
            new Vector3(96.94f, 22.82f, 28.91f),
            new Vector3(97.84f, 22.82f, 18.08f),
            new Vector3(97.85f, 22.83f, 7.06f),
            new Vector3(103.39f, 22.97f, 1.14f),
            new Vector3(110.07f, 21.81f, -11.87f),
            new Vector3(124.84f, 22.04f, -4.28f),
            new Vector3(93.88f, 21.75f, -22.81f),
            new Vector3(89.69f, 21.88f, -30.47f),
            new Vector3(93.98f, 21.57f, -41.11f),
            new Vector3(89.38f, 20.78f, -58.92f),
            new Vector3(82.51f, 20.87f, -63.06f),
            new Vector3(75.68f, 20.65f, -76.41f),
            new Vector3(76.12f, 20.53f, -86.39f),
            new Vector3(-102.05f, 19.36f, -70.15f),
            new Vector3(-96.11f, 19.69f, -77.12f),
            new Vector3(-91.35f, 19.47f, -92.45f),
            new Vector3(-111.74f, 19.18f, -60.99f),
            new Vector3(-117.23f, 19.84f, -54.52f),
            new Vector3(-118.64f, 19.95f, -42.71f),
            new Vector3(-120.71f, 20.37f, -28.66f),
            new Vector3(-123.18f, 19.91f, -26.54f),
            new Vector3(-127.92f, 21.14f, -3.41f),
            new Vector3(-129.59f, 20.66f, 27.85f),
            new Vector3(-132.93f, 21.48f, 48.36f),
            new Vector3(-131.08f, 21.88f, 66.78f),
            new Vector3(-132.79f, 21.7f, 75.47f),
            new Vector3(-135.89f, 23.12f, 86.09f),
            new Vector3(-16.93f, 20.17f, 140.75f),
            new Vector3(-68.31f, 20.36f, 143.23f),
            new Vector3(-82.89f, 20.23f, 159.33f),
            new Vector3(-97.04f, 20.14f, 165.01f),
            new Vector3(-116.84f, 20.05f, 148.01f),
            new Vector3(-112.68f, 20.21f, 124.94f),
            new Vector3(-105.49f, 20.18f, 119.81f),
            new Vector3(31.73f, 21.5f, 124.29f),
            new Vector3(141.92f, 23.06f, 24.95f),
            new Vector3(141.98f, 22.85f, 8.89f),
            new Vector3(149.34f, 20.9f, -25.04f),
            new Vector3(126.56f, 21.47f, -57.81f),
            new Vector3(109.28f, 21.17f, -61.6f),
            new Vector3(112.94f, 20.65f, -109.74f),
            new Vector3(-125.76f, 21.13f, 5.11f),
            new Vector3(-123.9f, 21.98f, 19.82f),
            new Vector3(5.07f, 23.09f, 97.91f),
            new Vector3(127.5f, 21.23f, -84.44f)
        };

        // Token: 0x04000051 RID: 81
        private Vector3[] raspberryPositions = new Vector3[]
        {
            new Vector3(-168.1f, 20.1f, -44.82f),
            new Vector3(-159.86f, 19.99f, -60.52f),
            new Vector3(-135.03f, 18.95f, -70.75f),
            new Vector3(-198.25f, 21.78f, -71.53f),
            new Vector3(-124.88f, 19.07f, -113.42f),
            new Vector3(-106.8f, 19.11f, -104.88f),
            new Vector3(-153.52f, 20.7f, -15.07f),
            new Vector3(-173.6f, 20.71f, -7.49f),
            new Vector3(-189.19f, 20.02f, 8.1f),
            new Vector3(-176.01f, 20.65f, 12.34f),
            new Vector3(-201.96f, 19.37f, -18.05f),
            new Vector3(-159.2f, 20.92f, 30.83f),
            new Vector3(-178.5f, 21.6f, 43.58f),
            new Vector3(-193.46f, 23.25f, 38.61f),
            new Vector3(-177.76f, 21.35f, 68.47f),
            new Vector3(-163.76f, 21.49f, 79.07f),
            new Vector3(-160.83f, 19.05f, -103.41f)
        };

        // --- Custom Teleport Logic ---
        [Serializable]
        public class CustomTeleportEntry
        {
            public string name;
            public Vector3 position;
        }

        // Removed Wrapper, using manual JSON handling
        private List<CustomTeleportEntry> customTeleportList = new List<CustomTeleportEntry>();
        private string customTeleportName = "My Place";
        private string customTPX = "0";
        private string customTPY = "0";
        private string customTPZ = "0";






        public void OnDeinitializeMelon()
        {
            if (this.eventSystemBlockedByMenu)
            {
                EventSystem restoreTarget = this.blockedEventSystem != null ? this.blockedEventSystem : EventSystem.current;
                if (restoreTarget != null)
                {
                    restoreTarget.enabled = this.eventSystemPrevEnabled;
                }
                this.eventSystemBlockedByMenu = false;
                this.blockedEventSystem = null;
            }

            if (patrolCoroutine != null)
            {
                ModCoroutines.Stop(patrolCoroutine);
                patrolCoroutine = null;
            }
            isPatrolActive = false;
            this.RevertLodOverride();

            foreach (Texture2D texture in this.themeTextures)
            {
                if (texture != null)
                {
                    Object.Destroy(texture);
                }
            }
            this.themeTextures.Clear();
            this.themeInitialized = false;
        }

        // Token: 0x02000008 RID: 8
        private class FarmLocation
        {
            // Token: 0x0600002E RID: 46 RVA: 0x00008437 File Offset: 0x00006637
            public FarmLocation(string name, Vector3 position, string type)
            {
                this.Name = name;
                this.Position = position;
                this.Type = type;
            }

            // Token: 0x04000052 RID: 82
            public string Name;

            // Token: 0x04000053 RID: 83
            public Vector3 Position;

            // Token: 0x04000054 RID: 84
            public string Type;
        }

        // Token: 0x02000009 RID: 9
        private enum AutoFarmState
        {
            // Token: 0x04000056 RID: 86
            Idle,
            // Token: 0x04000057 RID: 87
            ScanningForNodes,
            // Token: 0x04000058 RID: 88
            TeleportingToNode,
            // Token: 0x04000059 RID: 89
            Collecting,
            // Token: 0x0400005A RID: 90
            MovingToLocation,
            // Token: 0x0400005B RID: 91
            LoadingArea,
            // Token: 0x0400005C RID: 92
            WaitingForNodes,
            // Token: 0x0400005D RID: 93
            WaitingForPriorityArea
        }

        private enum TreeFarmState
        {
            Idle,
            EquipAxe,
            WaitAfterEquip,
            TeleportToPoint,
            WaitAfterTeleport,
            ChopAtPoint,
            WaitNextPoint
        }




        // Called by Harmony postfix when UIManager.ShowToast is invoked in-game.
        public void OnToastDetected(string msg)
        {
            this.OnToastDetected(msg, 0);
        }

        private void OnToastDetected(string msg, int toastObjectId)
        {
            try
            {
                if (string.IsNullOrEmpty(msg)) return;
                string s = msg.Trim();
                float now = Time.unscaledTime;

                // Avoid duplicate handling when both the UI hook and panel scanner see the same toast,
                // but still allow a new toast instance with identical text later.
                bool sameToastObject = toastObjectId != 0
                    && toastObjectId == this.lastDetectedToastObjectId
                    && string.Equals(s, this.lastDetectedToast, StringComparison.Ordinal);
                bool sameHookToastBurst = toastObjectId == 0
                    && string.Equals(s, this.lastDetectedToast, StringComparison.Ordinal)
                    && now - this.lastDetectedToastAt < 0.75f;
                if (sameToastObject || sameHookToastBurst) return;

                this.lastDetectedToast = s;
                this.lastDetectedToastObjectId = toastObjectId;
                this.lastDetectedToastAt = now;

                if (BirdNetFarm.IsAutoScareMaxPhotoEnabled && this.IsBirdFarmMaxPhotoToastMessage(s))
                {
                    this.BirdFarmNetLog("[MaxPhotoFallback] Limit toast observed: " + s);
                    this.TryHandleBirdFarmMaxPhotoAutoScare("toast fallback");
                    return;
                }

                // Durability notifications
                if (this.autoRepairOnToastEnabled && this.IsDurabilityToastMessage(s))
                {
                    this.AutoEatRepairLog("[AutoRepair] Durability toast observed; using toast fallback trigger.");
                    this.TryHandleDurabilityAutoRepairTrigger("toast fallback");
                    return;
                }

            }
            catch (Exception ex)
            {
                ModLogger.Msg("[OnToastDetected] Error: " + ex.Message);
            }
        }

        private void CheckToastPanel()
        {
            if (!this.autoRepairOnToastEnabled && !BirdNetFarm.IsAutoScareMaxPhotoEnabled)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now - this.lastToastCheckAt < TOAST_CHECK_INTERVAL) return;
            this.lastToastCheckAt = now;
            try
            {
                GameObject toastsRoot = this.cachedToastRootObj;
                if (toastsRoot == null || !toastsRoot.activeInHierarchy)
                {
                    this.cachedToastRootObj = null;
                    this.cachedToastTextObj = null;
                    if (now < this.nextToastRootPathScanAt)
                    {
                        return;
                    }

                    this.nextToastRootPathScanAt = now + 2f;
                    toastsRoot = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/Tip/TipPanel(Clone)/ToastPanel(Clone)/toasts@t");
                    if (toastsRoot == null)
                    {
                        return;
                    }

                    this.cachedToastRootObj = toastsRoot;
                }

                int childCount = toastsRoot.transform.childCount;
                for (int i = 0; i < childCount; i++)
                {
                    Transform child = toastsRoot.transform.GetChild(i);
                    if (child == null || !child.gameObject.activeInHierarchy) continue;
                    Transform txtTransform = child.Find("AniRoot@ani/root_visible@go/root_visible/value@txt");
                    if (txtTransform == null) continue;
                    GameObject txtObj = txtTransform.gameObject;
                    if (txtObj == null || !txtObj.activeInHierarchy) continue;

                    string text = null;
                    var uiText = txtObj.GetComponent<UnityEngine.UI.Text>();
                    if (uiText != null) text = uiText.text;
                    else
                    {
                        foreach (Component comp in txtObj.GetComponents<Component>())
                        {
                            if (comp == null) continue;
                            try
                            {
                                var ilType = comp.GetIl2CppType();
                                if (ilType != null && ilType.Name == "XDText")
                                {
                                    var prop = ilType.GetProperty("text");
                                    if (prop != null)
                                    {
                                        var val = prop.GetValue(comp);
                                        text = (val != null) ? val.ToString() : null;
                                        break;
                                    }
                                }
                            }
                            catch { }
                        }
                    }

                    if (!string.IsNullOrEmpty(text))
                    {
                        string s = text.Trim();
                        int toastObjectId = child.gameObject.GetInstanceID();
                        if (s != this.lastDetectedToast || toastObjectId != this.lastDetectedToastObjectId)
                        {
                            this.OnToastDetected(s, toastObjectId);
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[CheckToastPanel] error: " + ex.Message);
            }
        }









        private string TryReadUiTextValue(GameObject target)
        {
            if (target == null)
            {
                return null;
            }

            try
            {
                var text = target.GetComponent<Text>();
                if (text != null && !string.IsNullOrEmpty(text.text))
                {
                    return text.text;
                }

                foreach (Component comp in target.GetComponents<Component>())
                {
                    if (comp == null) continue;
                    try
                    {
                        var ilType = comp.GetIl2CppType();
                        if (ilType == null) continue;
                        if (ilType.Name == "XDText")
                        {
                            var prop = ilType.GetProperty("text");
                            if (prop != null)
                            {
                                var val = prop.GetValue(comp);
                                if (val != null)
                                {
                                    string s = val.ToString();
                                    if (!string.IsNullOrEmpty(s))
                                    {
                                        return s;
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
            catch
            {
            }

            return null;
        }








        // Cached energy reading method (from HeartopiaBuddy4)

        private void UpdateIdDisplay()
        {
            try
            {
                float now = Time.unscaledTime;
                string customId = this.customDisplayIdEnabled ? this.NormalizeCustomId(this.customDisplayId) : string.Empty;
                bool shouldRewriteId = this.hideIdEnabled || !string.IsNullOrEmpty(customId);

                if (this.cachedTestIndexObject == null || this.cachedTestIndexText == null)
                {
                    if (now < this.nextIdDisplayUpdateAt)
                    {
                        return;
                    }

                    this.nextIdDisplayUpdateAt = now + 0.25f;
                    this.cachedTestIndexObject = GameObject.Find("GameApp/startup_root(Clone)/XDUIRoot/TEST_INDEX");
                    this.cachedTestIndexText = this.cachedTestIndexObject != null
                        ? this.cachedTestIndexObject.GetComponent<Text>()
                        : null;
                }
                else if (!shouldRewriteId)
                {
                    if (now < this.nextIdDisplayUpdateAt)
                    {
                        return;
                    }

                    this.nextIdDisplayUpdateAt = now + 1f;
                }

                GameObject testIndex = this.cachedTestIndexObject;
                if (testIndex != null && testIndex.activeInHierarchy)
                {
                    var text = this.cachedTestIndexText;
                    if (text != null)
                    {
                        string originalText = text.text;
                        string[] parts = originalText.Split(new string[] { "     " }, StringSplitOptions.None);
                        List<string> rebuiltParts = new List<string>(parts.Length);
                        string currentIdPart = string.Empty;

                        foreach (string part in parts)
                        {
                            if (part.StartsWith("ID:", StringComparison.OrdinalIgnoreCase))
                            {
                                currentIdPart = part;
                                continue;
                            }

                            // Skip existing Helper: parts to avoid duplication
                            if (part.StartsWith("Helper:", StringComparison.OrdinalIgnoreCase))
                            {
                                continue;
                            }

                            rebuiltParts.Add(part);
                        }

                        if (!string.IsNullOrEmpty(currentIdPart))
                        {
                            this.cachedOriginalIdPart = currentIdPart;
                        }

                        if (!string.IsNullOrEmpty(customId))
                        {
                            rebuiltParts.Add("ID:" + customId);
                        }
                        else if (!this.hideIdEnabled)
                        {
                            string idToShow = !string.IsNullOrEmpty(currentIdPart) ? currentIdPart : this.cachedOriginalIdPart;
                            if (!string.IsNullOrEmpty(idToShow))
                            {
                                rebuiltParts.Add(idToShow);
                            }
                        }

                        string newText = string.Join("     ", rebuiltParts.ToArray());

                        if (!string.Equals(newText, originalText, StringComparison.Ordinal))
                        {
                            text.text = newText;
                        }
                    }
                }
                else
                {
                    this.cachedTestIndexObject = null;
                    this.cachedTestIndexText = null;
                }
            }
            catch
            {
                this.cachedTestIndexObject = null;
                this.cachedTestIndexText = null;
            }
        }

        private string NormalizeCustomId(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            string normalized = value.Replace("\r", " ").Replace("\n", " ").Trim();
            if (normalized.StartsWith("ID:", StringComparison.OrdinalIgnoreCase))
            {
                normalized = normalized.Substring(3).Trim();
            }

            while (normalized.Contains("  "))
            {
                normalized = normalized.Replace("  ", " ");
            }

            if (normalized.Length > 24)
            {
                normalized = normalized.Substring(0, 24).Trim();
            }

            return normalized;
        }

        private enum LobbyJoinState
        {
            Idle,
            OpenRoomPanel,
            SelectFriendTab,
            RefreshAndRetry,
            ClickFriendJoin,
            SelectMyTownTab,
            ClickMyTownJoin
        }
    }
}



