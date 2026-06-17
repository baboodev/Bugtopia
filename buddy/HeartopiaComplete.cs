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
            // Install the native crash-dump handler first so a fatal AV during any later init step
            // is still captured (the game's own crash handler otherwise eats it before WER runs).
            CrashDumpHandler.Install();
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
            this.UpdateMovementInputBridge();
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























        // Public wrappers for external UI modules































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


































        // Public helpers for external modules



















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































        // Allow external modules to request a settings save (autosave helper)




        // Expose DirectClickInteractButton for external modules

        // Resource repair pause helpers for external modules



        // Public wrappers to allow other modules to trigger repair/eat flows




    










        private GameObject[] cachedFishShadowTargetObjects = null;
        private float nextFishShadowTargetObjectScanAt = -999f;
        // Narrowed fish-shadow scan: enumerate only FishComponent instances instead of every scene
        // GameObject. Resolved il2cpp type cached (null = fall back to the full GameObject scan).
        private Il2CppSystem.Type cachedFishComponentIl2CppType = null;
        private bool fishComponentIl2CppTypeResolved = false;
        private float nextFishShadowResolverMissLogAt = -999f;
        private string lastFishShadowResolverMissLogStatus = string.Empty;
        private readonly Dictionary<int, int> fishShadowPriorityByFishIdCache = new Dictionary<int, int>();
        private readonly Dictionary<int, string> fishShadowPrioritySourceByFishIdCache = new Dictionary<int, string>();























































        private const int MeteorStarfallExchangeStoreId = 140;






























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
                tabs.Add((this.L("extra.title"), () => this.newFeaturesSubTab == 6, () => this.SetNewFeaturesSubTab(6)));
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





        // Installs the Transform.position setter patch (player teleport/noclip pinning and
        // camera position override) on first use. This is the hotter of the movement patches,
        // so it is kept separate from the lighter CharacterController.Move patch below and is
        // NOT pulled in by the menu input-block (which only needs Move).

        // Installs the rotation patches (Transform.rotation setter, used by both the camera
        // mouse-look override and the player-rotation override) on first use.

        // Installs the Input.GetKey* postfixes used for simulated F-key presses (fishing,
        // insect net, auto-cook interact) on first use.









        // Auto Draw tab removed

        // Insect farm UI lives in InsectNetFarm.cs



















































































































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


        // Checks if ANY interact prompt button is visible in the tracking panel,
        // regardless of whether its resource type is enabled for auto-collect.
        // Used for camera-stuck detection only.








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

        // Mark nearest tree (of any type) as collected and start its cooldown/hide timers







        // Removed blocking WaitForSwingConfirm in favor of non-blocking polling handled in RunTreeFarmLogic








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



        // Token: 0x0600001F RID: 31 RVA: 0x000061B0 File Offset: 0x000043B0

        // NEW FEATURE: Apply Camera FOV



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



















































































        // --- PATROL SYSTEM METHODS ---




        // --- FORCE CLOSE MENU ---







        // Directly simulate an interact (F) press and try to click in-game interact buttons




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


        // Returns the player's root GameObject if available (fallback to GetPlayer)



        // --- Auto Buy helpers + logic ---


















        // Garden Store version - uses autoBuyGardenShopScrollStep instead of autoBuyShopScrollStep

        // Fishing Store version - uses autoBuyFishingShopScrollStep instead of autoBuyShopScrollStep

        // SalePanel helpers





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



















































        private bool IsReadableMemoryProtect(uint protect)
        {
            return protect == 0x02U || protect == 0x04U || protect == 0x08U || protect == 0x20U || protect == 0x40U;
        }












































        // INVARIANT: module instances are resolved by scanning Managers._moduleDic. Never route
        // this through Managers.GetModule(Type) for a type that exists only on the Mono side:
        // its internal Type.GetType on a no-Instance ViewModule hard-crashes the mono runtime
        // (see docs/plans pad-build migration notes). PadBuild's GetModule(Type) path is the one
        // vetted exception — it passes a Type object resolved from the same mono image.









        // Returns Images scoped to the open bag panel - avoids scene-wide FindObjectsOfType allocation

        // Check if bag panel is currently open

        // Check if user clicked on a food item in bag during pick mode
        // This detects clicks by checking for the Use/Eat button appearing

        // Get the sprite name of the currently selected food item using the selection indicator position

        // Scan bag for all food items (sprites starting with "ui_item_normal_p_" and containing food keywords, or gather_ items)





















































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
        private bool netCookCookBuildHookWarned = false;        // logged once if the OnSpawned/OnComponentUpdated patch can't install
        private int netCookCookBuildSpawnHookCount = 0;         // runtime OnSpawned hook fire count
        private int netCookCookBuildUpdateHookCount = 0;        // runtime OnComponentUpdated hook fire count
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







        // Called by Harmony postfix when UIManager.ShowToast is invoked in-game.











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

    }
}



