using HarmonyLib;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEngine;

namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        private bool auraFarmEnabled = false;
        private bool auraFarmMethodsReady = false;
        private const bool AuraFarmDebugLogs = HeartopiaComplete.MasterLogAuraFarm;
        private const bool AuraUseManagedSpatialFallbackScans = false;
        private const bool AuraUseMonoTargetFallbacks = false;
        private static readonly float AuraScanInterval = 0.08f;
        private static readonly float AuraMonoFallbackScanInterval = 0.35f;
        private static readonly float AuraPerTargetCooldown = 0.02f;
        private static readonly float AuraEnableWarmup = 0f;
        private const int AuraMergedTargetSoftCap = 32;
        private float auraEnabledAt = 0f;
        private float auraLastScanAt = 0f;
        private float auraLastEmptyStateLogAt = 0f;
        private float auraNextMonoFallbackScanAt = 0f;
        private float auraNextTickAt = 0f;
        private float auraLastActionSummaryLogAt = -999f;
        private float auraLastAxeCheckerLogAt = -999f;
        private float auraLastMonoFallbackLogAt = -999f;
        private float auraLastTargetDetailLogAt = -999f;
        private int auraLastTargetCount = 0;
        private int auraLastLoggedActionTargetCount = -1;
        private int auraLastLoggedTreeHits = -1;
        private int auraLastLoggedStoneHits = -1;
        private int auraLastLoggedBushPicks = -1;
        private int auraLastLoggedAxeCheckerPhysicalCount = -1;
        private int auraLastLoggedAxeCheckerTargetCount = -1;
        private int auraLastLoggedMonoFallbackTargetCount = -1;
        private string auraLastLoggedTargetDetailKey = string.Empty;
        private int auraTotalTreeHits = 0;
        private int auraTotalStoneHits = 0;
        private int auraTotalBushPicks = 0;
        private float auraLastSuccessfulCommandAt = -999f;
        private float auraLastStallRecoveryAt = -999f;
        private uint auraLastDetectedResourceNetId = 0U;
        private uint auraLastDetectedOwnerNetId = 0U;
        private ulong auraLastDetectedTargetNetId = 0UL;
        private string auraLastError = string.Empty;
        private readonly HashSet<uint> auraOwnerTargetBuffer = new HashSet<uint>();
        private readonly HashSet<uint> auraMonoFallbackTargetBuffer = new HashSet<uint>();
        private readonly Dictionary<uint, AuraTargetInfo> auraTargetInfoByOwnerId = new Dictionary<uint, AuraTargetInfo>();
        private readonly Dictionary<uint, float> auraNextAllowedByOwnerId = new Dictionary<uint, float>();
        private readonly List<uint> auraExpiredOwnerBuffer = new List<uint>(64);
        private const float AuraDirectScanRadius = 8f;
        private static readonly string[] auraPreferredAssemblyNameFragments = new string[]
        {
            "Assembly-CSharp",
            "Il2CppAssembly-CSharp",
            "XDT",
            "Game"
        };
        private static readonly string[] auraExcludedAssemblyNamePrefixes = new string[]
        {
            "Unity",
            "System",
            "mscorlib",
            "netstandard",
            "Mono.",
            "MelonLoader",
            "0Harmony",
            "Harmony",
            "Newtonsoft",
            "Il2Cppmscorlib",
            "Il2CppSystem"
        };

        private Type auraResourceProtocolManagerType;
        private Type auraInteractSystemType;
        private Type auraEntityHelperType;
        private Type auraEntityUtilType;
        private Type auraEntitiesType;
        private Type auraAxeCheckerType;
        private Type auraSelectPriorityInfoType;
        private Type auraCollectableObjectComponentType;
        private Type auraCollectableBushComponentType;
        private Type auraDynamicBushComponentType;
        private Type auraLevelObjectManagerType;
        private Type auraLevelObjectTagType;
        private Type auraCylinderType;
        private MethodInfo auraSendPickBushMethod;
        private MethodInfo auraSendAttackTreeMethod;
        private MethodInfo auraSendHitStoneMethod;
        private MethodInfo auraInteractSystemGetInstanceMethod;
        private MethodInfo auraInteractSystemGetPlayerMethod;
        private FieldInfo auraInteractSystemInstanceField;
        private MethodInfo auraInteractSystemGetTargetListMethod;
        private MethodInfo auraAxeCheckerPhysicalSelectMethod;
        private MethodInfo auraEntityHelperGetTargetListMethod;
        private MethodInfo auraEntityHelperGetLevelObjectOwnerMethod;
        private MethodInfo auraEntityHelperGetLevelObjectMethod;
        private MethodInfo auraEntityUtilGetEntityMethod;
        private MethodInfo auraEntityUtilGetSelfPlayerEntityMethod;
        private MethodInfo auraEntityUtilGetLevelObjectMethod;
        private MethodInfo auraEntitiesSphereQueryEntitiesMethod;
        private MethodInfo auraLevelObjectManagerCylinderOverlapNonAllocMethod;
        private FieldInfo auraCollectableObjectResTypeField;
        private PropertyInfo auraCollectableObjectResTypeProperty;
        private FieldInfo auraCollectableObjectInColdField;
        private PropertyInfo auraCollectableObjectInColdProperty;
        private FieldInfo auraEntityNetIdField;
        private PropertyInfo auraEntityNetIdProperty;
        private FieldInfo auraEntityPositionField;
        private PropertyInfo auraEntityPositionProperty;
        private FieldInfo auraEntityRotationField;
        private PropertyInfo auraEntityRotationProperty;
        private FieldInfo auraLevelObjectNetIdField;
        private PropertyInfo auraLevelObjectNetIdProperty;
        private FieldInfo auraLevelObjectOwnerNetIdField;
        private PropertyInfo auraLevelObjectOwnerNetIdProperty;
        private FieldInfo auraLevelObjectResourceIdField;
        private PropertyInfo auraLevelObjectResourceIdProperty;
        private PropertyInfo auraLevelObjectManagerInstanceProperty;
        private FieldInfo auraInteractSystemCurrentTargetField;
        private FieldInfo auraInteractSystemFocusLevelObjectsField;
        private FieldInfo auraInteractSystemSelectedField;
        private FieldInfo auraInteractSystemSelectPriorityLengthField;
        private FieldInfo auraInteractSystemSelectPriorityInfoArrayField;
        private FieldInfo auraInteractSystemInteractCylinderField;
        private PropertyInfo auraInteractSystemCurrentTargetProperty;
        private PropertyInfo auraInteractSystemFocusLevelObjectsProperty;
        private PropertyInfo auraInteractSystemSelectedProperty;
        private PropertyInfo auraInteractSystemInteractCylinderProperty;
        private PropertyInfo auraInteractSystemPlayerProperty;
        private FieldInfo auraAxeCheckerInstanceField;
        private PropertyInfo auraAxeCheckerInstanceProperty;
        private FieldInfo auraCylinderCenterField;
        private PropertyInfo auraCylinderCenterProperty;
        private FieldInfo auraCylinderRadiusField;
        private PropertyInfo auraCylinderRadiusProperty;
        private FieldInfo auraCylinderHeightField;
        private PropertyInfo auraCylinderHeightProperty;
        private FieldInfo auraSelectPriorityInfoShapeField;
        private PropertyInfo auraSelectPriorityInfoShapeProperty;
        private FieldInfo[] auraInteractTargetCandidateFields = Array.Empty<FieldInfo>();
        private PropertyInfo[] auraInteractTargetCandidateProperties = Array.Empty<PropertyInfo>();
        private bool auraMonoApiReady = false;
        private IntPtr auraMonoRootDomain = IntPtr.Zero;
        private int auraMonoAttachedManagedThreadId = int.MinValue;
        private IntPtr auraMonoAttachedDomain = IntPtr.Zero;
        private IntPtr auraMonoSendPickBushMethodPtr = IntPtr.Zero;
        private IntPtr auraMonoSendAttackTreeMethodPtr = IntPtr.Zero;
        private IntPtr auraMonoSendHitStoneMethodPtr = IntPtr.Zero;
        private IntPtr auraMonoInteractGetInstanceMethodPtr = IntPtr.Zero;
        private IntPtr auraMonoInteractSystemClassPtr = IntPtr.Zero;
        private IntPtr auraMonoInteractCurrentTargetFieldPtr = IntPtr.Zero;
        private IntPtr auraMonoInteractFocusLevelObjectsFieldPtr = IntPtr.Zero;
        private IntPtr auraMonoInteractSelectedFieldPtr = IntPtr.Zero;
        private IntPtr auraMonoInteractGetPlayerMethodPtr = IntPtr.Zero;
        private IntPtr auraMonoInteractCanCollectionInteractionMethodPtr = IntPtr.Zero;
        private IntPtr auraMonoInteractCurrentHandholdInteractMethodPtr = IntPtr.Zero;
        private IntPtr auraMonoInteractSelectPriorityLengthFieldPtr = IntPtr.Zero;
        private IntPtr auraMonoInteractSelectPriorityInfoArrayFieldPtr = IntPtr.Zero;
        private IntPtr auraMonoEntityHelperGetLevelObjectOwnerMethodPtr = IntPtr.Zero;
        private IntPtr auraMonoAxeCheckerClassPtr = IntPtr.Zero;
        private IntPtr auraMonoAxeCheckerInstanceFieldPtr = IntPtr.Zero;
        private IntPtr auraMonoAxeCheckerPhysicalSelectMethodPtr = IntPtr.Zero;
        private IntPtr auraMonoSelectPriorityInfoClassPtr = IntPtr.Zero;
        private IntPtr auraMonoLocalPlayerLookDecisionsFieldPtr = IntPtr.Zero;
        private IntPtr auraMonoLocalPlayerLookInteractTargetClassPtr = IntPtr.Zero;
        private IntPtr auraMonoLocalPlayerLookTargetListFieldPtr = IntPtr.Zero;
        private IntPtr auraMonoSelectPriorityInfoShapeFieldPtr = IntPtr.Zero;
        private IntPtr auraMonoDynamicLevelObjectGetUniqueIdMethodPtr = IntPtr.Zero;

        private enum AuraTargetKind
        {
            Unknown,
            Tree,
            Stone,
            Bush
        }

        private sealed class AuraTargetInfo
        {
            public uint OwnerNetId;
            public uint ResourceNetId;
            public ulong TargetNetId;
            public uint LevelResourceId;
            public AuraTargetKind Kind;
            public Vector3 Position;
            public bool HasPosition;
            public string Source = string.Empty;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoGetRootDomainDelegate();

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoThreadAttachDelegate(IntPtr domain);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoImageLoadedDelegate([MarshalAs(UnmanagedType.LPStr)] string name);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void MonoAssemblyForeachDelegate(MonoAssemblyForeachCallbackDelegate callback, IntPtr userData);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void MonoAssemblyForeachCallbackDelegate(IntPtr assembly, IntPtr userData);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoAssemblyGetImageDelegate(IntPtr assembly);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoImageGetNameDelegate(IntPtr image);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoClassFromNameDelegate(IntPtr image, [MarshalAs(UnmanagedType.LPStr)] string nameSpace, [MarshalAs(UnmanagedType.LPStr)] string className);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoClassGetMethodFromNameDelegate(IntPtr klass, [MarshalAs(UnmanagedType.LPStr)] string name, int paramCount);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoClassGetFieldFromNameDelegate(IntPtr klass, [MarshalAs(UnmanagedType.LPStr)] string name);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void MonoFieldGetValueDelegate(IntPtr obj, IntPtr field, IntPtr valuePtr);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoFieldGetValueObjectDelegate(IntPtr domain, IntPtr field, IntPtr obj);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void MonoFieldSetValueDelegate(IntPtr obj, IntPtr field, IntPtr valuePtr);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoRuntimeInvokeDelegate(IntPtr method, IntPtr obj, IntPtr parameters, ref IntPtr exc);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoObjectUnboxDelegate(IntPtr obj);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoStringNewDelegate(IntPtr domain, [MarshalAs(UnmanagedType.LPStr)] string text);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoStringToUtf8Delegate(IntPtr monoString);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void MonoFreeDelegate(IntPtr memory);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoObjectGetClassDelegate(IntPtr obj);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int MonoClassIsValueTypeDelegate(IntPtr klass);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoClassGetTypeDelegate(IntPtr klass);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoClassBindGenericParametersDelegate(IntPtr klass, int paramType, IntPtr types, int typesLen);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoTypeGetObjectDelegate(IntPtr domain, IntPtr type);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoClassGetParentDelegate(IntPtr klass);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoClassGetNameDelegate(IntPtr klass);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoClassGetNamespaceDelegate(IntPtr klass);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoClassGetMethodsDelegate(IntPtr klass, ref IntPtr iter);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoMethodGetNameDelegate(IntPtr method);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoMethodSignatureDelegate(IntPtr method);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate uint MonoSignatureGetParamCountDelegate(IntPtr signature);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate UIntPtr MonoArrayLengthDelegate(IntPtr array);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoArrayAddrWithSizeDelegate(IntPtr array, int size, UIntPtr index);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoArrayNewDelegate(IntPtr domain, IntPtr eclass, UIntPtr n);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoClassVtableDelegate(IntPtr domain, IntPtr klass);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void MonoFieldStaticGetValueDelegate(IntPtr vtable, IntPtr field, IntPtr valuePtr);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoObjectNewDelegate(IntPtr domain, IntPtr klass);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void MonoRuntimeObjectInitDelegate(IntPtr obj);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void MonoCompileMethodDelegate(IntPtr method);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoMethodGetUnmanagedThunkDelegate(IntPtr method);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr MonoMethodGetUnmanagedDelegate(IntPtr method);

        private static MonoGetRootDomainDelegate auraMonoGetRootDomain;
        private static MonoThreadAttachDelegate auraMonoThreadAttach;
        private static MonoImageLoadedDelegate auraMonoImageLoaded;
        private static MonoAssemblyForeachDelegate auraMonoAssemblyForeach;
        private static MonoAssemblyGetImageDelegate auraMonoAssemblyGetImage;
        private static MonoImageGetNameDelegate auraMonoImageGetName;
        private static MonoClassFromNameDelegate auraMonoClassFromName;
        private static MonoClassGetMethodFromNameDelegate auraMonoClassGetMethodFromName;
        private static MonoClassGetFieldFromNameDelegate auraMonoClassGetFieldFromName;
        private static MonoFieldGetValueDelegate auraMonoFieldGetValue;
        private static MonoFieldGetValueObjectDelegate auraMonoFieldGetValueObject;
        private static MonoFieldSetValueDelegate auraMonoFieldSetValue;
        private static MonoRuntimeInvokeDelegate auraMonoRuntimeInvoke;
        private static MonoObjectUnboxDelegate auraMonoObjectUnbox;
        private static MonoStringNewDelegate auraMonoStringNew;
        private static MonoStringToUtf8Delegate auraMonoStringToUtf8;
        private static MonoFreeDelegate auraMonoFree;
        private static MonoObjectGetClassDelegate auraMonoObjectGetClass;
        private static MonoClassIsValueTypeDelegate auraMonoClassIsValueType;
        private static MonoClassGetTypeDelegate auraMonoClassGetType;
        private static MonoClassBindGenericParametersDelegate auraMonoClassBindGenericParameters;
        private static MonoTypeGetObjectDelegate auraMonoTypeGetObject;
        private static MonoClassGetParentDelegate auraMonoClassGetParent;
        private static MonoClassGetNameDelegate auraMonoClassGetName;
        private static MonoClassGetNamespaceDelegate auraMonoClassGetNamespace;
        private static MonoClassGetMethodsDelegate auraMonoClassGetMethods;
        private static MonoMethodGetNameDelegate auraMonoMethodGetName;
        private static MonoMethodSignatureDelegate auraMonoMethodSignature;
        private static MonoSignatureGetParamCountDelegate auraMonoSignatureGetParamCount;
        private static MonoArrayLengthDelegate auraMonoArrayLength;
        private static MonoArrayAddrWithSizeDelegate auraMonoArrayAddrWithSize;
        private static MonoArrayNewDelegate auraMonoArrayNew;
        private static MonoClassVtableDelegate auraMonoClassVtable;
        private static MonoFieldStaticGetValueDelegate auraMonoFieldStaticGetValue;
        private static MonoObjectNewDelegate auraMonoObjectNew;
        private static MonoRuntimeObjectInitDelegate auraMonoRuntimeObjectInit;
        private static MonoCompileMethodDelegate auraMonoCompileMethod;
        private static MonoMethodGetUnmanagedThunkDelegate auraMonoMethodGetUnmanagedThunk;
        private static MonoMethodGetUnmanagedDelegate auraMonoMethodGetUnmanaged;

        private IntPtr auraMonoTypeGetTypeMethodPtr = IntPtr.Zero;
        private IntPtr auraMonoActivatorCreateInstanceMethodPtr = IntPtr.Zero;
        private IntPtr auraMonoUInt64ClassPtr = IntPtr.Zero;
        private IntPtr auraMonoArrayGetValueMethodPtr = IntPtr.Zero;
        private IntPtr auraMonoFocusSetCountMethodPtr = IntPtr.Zero;
        private IntPtr auraMonoFocusSetCopyToMethodPtr = IntPtr.Zero;
        private IntPtr auraMonoSelectedGetKeysMethodPtr = IntPtr.Zero;
        private IntPtr auraMonoSelectedKeysCountMethodPtr = IntPtr.Zero;
        private IntPtr auraMonoSelectedKeysCopyToMethodPtr = IntPtr.Zero;
        private IntPtr auraMonoUInt64ListCountMethodPtr = IntPtr.Zero;
        private IntPtr auraMonoUInt64ListGetItemMethodPtr = IntPtr.Zero;
        private IntPtr auraMonoUInt64ListClearMethodPtr = IntPtr.Zero;
        private IntPtr auraMonoCachedUInt64ListObj = IntPtr.Zero;
        private IntPtr auraMonoSelectPriorityListClassPtr = IntPtr.Zero;
        private IntPtr auraMonoSelectPriorityListCountMethodPtr = IntPtr.Zero;
        private IntPtr auraMonoSelectPriorityListGetItemMethodPtr = IntPtr.Zero;
        private IntPtr auraMonoSelectPriorityListClearMethodPtr = IntPtr.Zero;
        private IntPtr auraMonoCachedSelectPriorityListObj = IntPtr.Zero;

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        private void SetAuraFarmEnabled(bool enabled)
        {
            if (this.auraFarmEnabled == enabled)
            {
                return;
            }

            this.auraFarmEnabled = enabled;
            this.auraEnabledAt = Time.unscaledTime;
            this.auraLastScanAt = 0f;
            this.auraNextMonoFallbackScanAt = 0f;
            this.auraNextTickAt = Time.unscaledTime;
            this.auraLastActionSummaryLogAt = -999f;
            this.auraLastAxeCheckerLogAt = -999f;
            this.auraLastMonoFallbackLogAt = -999f;
            this.auraLastTargetDetailLogAt = -999f;
            this.auraLastTargetCount = 0;
            this.auraLastLoggedActionTargetCount = -1;
            this.auraLastLoggedTreeHits = -1;
            this.auraLastLoggedStoneHits = -1;
            this.auraLastLoggedBushPicks = -1;
            this.auraLastLoggedAxeCheckerPhysicalCount = -1;
            this.auraLastLoggedAxeCheckerTargetCount = -1;
            this.auraLastLoggedMonoFallbackTargetCount = -1;
            this.auraLastLoggedTargetDetailKey = string.Empty;
            this.auraTotalTreeHits = 0;
            this.auraTotalStoneHits = 0;
            this.auraTotalBushPicks = 0;
            this.auraLastSuccessfulCommandAt = -999f;
            this.auraLastStallRecoveryAt = -999f;
            this.auraLastDetectedResourceNetId = 0U;
            this.auraLastDetectedOwnerNetId = 0U;
            this.auraLastDetectedTargetNetId = 0UL;
            this.auraLastError = string.Empty;
            this.auraOwnerTargetBuffer.Clear();
            this.auraMonoFallbackTargetBuffer.Clear();
            this.auraTargetInfoByOwnerId.Clear();
            this.auraNextAllowedByOwnerId.Clear();

            if (enabled)
            {
                this.SetAutoCollectEnabled(false);
                bool ready = this.ResolveAuraFarmRuntimeMethods();
                this.AddMenuNotification(ready ? "Aura Farm enabled" : "Aura Farm enabled, resolver incomplete", ready ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.75f, 0.45f));
                if (AuraFarmDebugLogs)
                {
                    ModLogger.Msg("[AuraFarm] Enabled. MethodsReady=" + ready + " MonoFallbacks=" + AuraUseMonoTargetFallbacks);
                }
            }
            else
            {
                this.AddMenuNotification("Aura Farm disabled", new Color(1f, 0.55f, 0.55f));
                if (AuraFarmDebugLogs)
                {
                    ModLogger.Msg("[AuraFarm] Disabled.");
                }
            }
        }

        private void UpdateAuraFarm()
        {
            if (!this.auraFarmEnabled)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (AuraEnableWarmup > 0f && now - this.auraEnabledAt < AuraEnableWarmup)
            {
                return;
            }

            if (now < this.auraNextTickAt)
            {
                return;
            }

            this.auraNextTickAt = now + AuraScanInterval;
            this.auraLastScanAt = now;

            if (!this.ResolveAuraFarmRuntimeMethods())
            {
                return;
            }

            this.RunAuraFarmTick();
            this.CleanupAuraCooldownMap(now);
        }

        private void RunAuraFarmTick()
        {
            if (!this.CollectAuraOwnerTargets(this.auraOwnerTargetBuffer))
            {
                this.auraLastTargetCount = 0;
                if (AuraFarmDebugLogs && Time.unscaledTime - this.auraLastEmptyStateLogAt >= 1f)
                {
                    this.auraLastEmptyStateLogAt = Time.unscaledTime;
                    ModLogger.Msg("[AuraFarm] Tick alive but found 0 targets. LastError=" + (string.IsNullOrEmpty(this.auraLastError) ? "<none>" : this.auraLastError));
                }
                return;
            }

            this.auraLastTargetCount = this.auraOwnerTargetBuffer.Count;
            float now = Time.unscaledTime;
            int treeHits = 0;
            int stoneHits = 0;
            int bushPicks = 0;

            foreach (uint ownerNetId in this.auraOwnerTargetBuffer)
            {
                float nextAllowed;
                if (this.auraNextAllowedByOwnerId.TryGetValue(ownerNetId, out nextAllowed) && now < nextAllowed)
                {
                    continue;
                }

                bool sentAny = false;
                AuraTargetInfo targetInfo = this.GetAuraTargetInfo(ownerNetId);
                uint resourceNetId = targetInfo != null && targetInfo.ResourceNetId != 0U ? targetInfo.ResourceNetId : ownerNetId;
                AuraTargetKind targetKind = targetInfo != null && targetInfo.Kind != AuraTargetKind.Unknown ? targetInfo.Kind : this.GetAuraTargetKind(ownerNetId);

                if (!this.TryPrepareAuraTargetForCommand(ownerNetId, ref resourceNetId, ref targetKind))
                {
                    this.auraNextAllowedByOwnerId[ownerNetId] = now + Math.Max(0.08f, AuraPerTargetCooldown);
                    continue;
                }

                if (this.InvokeAuraCommandForAllResources(resourceNetId))
                {
                    sentAny = true;
                    if (targetKind == AuraTargetKind.Tree)
                    {
                        treeHits++;
                    }
                    else if (targetKind == AuraTargetKind.Stone)
                    {
                        stoneHits++;
                    }
                    else
                    {
                        bushPicks++;
                    }
                }

                if (sentAny)
                {
                    this.autoCollectClickedSinceArrival = true;
                    this.auraLastSuccessfulCommandAt = now;
                    this.StampAuraFallbackNodeCooldown(ownerNetId, targetKind);
                    this.auraNextAllowedByOwnerId[ownerNetId] = now + AuraPerTargetCooldown;
                }
            }

            this.auraTotalTreeHits += treeHits;
            this.auraTotalStoneHits += stoneHits;
            this.auraTotalBushPicks += bushPicks;

            if (this.auraLastTargetCount > 0 && treeHits + stoneHits + bushPicks == 0 && now - this.auraLastSuccessfulCommandAt > 0.6f && now - this.auraLastStallRecoveryAt > 1f)
            {
                this.auraLastStallRecoveryAt = now;
                this.auraNextAllowedByOwnerId.Clear();
                this.auraLastError = "Aura watchdog cleared target throttles after a stalled tick.";
            }

            if (AuraFarmDebugLogs && (treeHits > 0 || stoneHits > 0 || bushPicks > 0))
            {
                bool summaryChanged = this.auraLastLoggedActionTargetCount != this.auraLastTargetCount
                    || this.auraLastLoggedTreeHits != treeHits
                    || this.auraLastLoggedStoneHits != stoneHits
                    || this.auraLastLoggedBushPicks != bushPicks;
                if (summaryChanged || now - this.auraLastActionSummaryLogAt >= 1f)
                {
                    this.auraLastActionSummaryLogAt = now;
                    this.auraLastLoggedActionTargetCount = this.auraLastTargetCount;
                    this.auraLastLoggedTreeHits = treeHits;
                    this.auraLastLoggedStoneHits = stoneHits;
                    this.auraLastLoggedBushPicks = bushPicks;
                    ModLogger.Msg("[AuraFarm] Targets=" + this.auraLastTargetCount + " TreeHits=" + treeHits + " StoneHits=" + stoneHits + " BushPicks=" + bushPicks);
                }
            }
        }

        private void CleanupAuraCooldownMap(float now)
        {
            if (this.auraNextAllowedByOwnerId.Count == 0)
            {
                return;
            }

            this.auraExpiredOwnerBuffer.Clear();
            foreach (KeyValuePair<uint, float> entry in this.auraNextAllowedByOwnerId)
            {
                if (entry.Value + 2f < now)
                {
                    this.auraExpiredOwnerBuffer.Add(entry.Key);
                }
            }

            if (this.auraExpiredOwnerBuffer.Count == 0)
            {
                return;
            }

            for (int i = 0; i < this.auraExpiredOwnerBuffer.Count; i++)
            {
                this.auraNextAllowedByOwnerId.Remove(this.auraExpiredOwnerBuffer[i]);
            }
        }

        private bool InvokeAuraCommandForAllResources(uint resourceNetId)
        {
            return this.InvokeAuraPickBush(resourceNetId);
        }

        private bool TryPrepareAuraTargetForCommand(uint ownerNetId, ref uint resourceNetId, ref AuraTargetKind targetKind)
        {
            if (ownerNetId == 0U)
            {
                return false;
            }

            AuraTargetInfo cachedInfo = this.GetAuraTargetInfo(ownerNetId);
            object entity = this.TryGetAuraOwnerEntity(ownerNetId);
            if (entity == null)
            {
                if (cachedInfo != null && cachedInfo.HasPosition && targetKind == AuraTargetKind.Unknown)
                {
                    AuraTargetKind positionKind = this.GetAuraTargetKindFromPosition(cachedInfo.Position);
                    if (positionKind != AuraTargetKind.Unknown)
                    {
                        targetKind = positionKind;
                        cachedInfo.Kind = positionKind;
                    }
                }

                if (resourceNetId == 0U)
                {
                    resourceNetId = ownerNetId;
                }

                return true;
            }

            object collectableObject = this.TryGetAuraEntityComponent(entity, this.auraCollectableObjectComponentType);
            if (collectableObject != null)
            {
                bool inCold;
                if (this.TryGetAuraCollectableInCold(collectableObject, out inCold) && inCold)
                {
                    if (AuraFarmDebugLogs)
                    {
                        ModLogger.Msg("[AuraFarm] Skipping live-cold resource ownerNetId=" + ownerNetId);
                    }

                    return false;
                }

                AuraTargetKind componentKind = this.GetAuraTargetKindFromCollectableObject(collectableObject);
                if (componentKind != AuraTargetKind.Unknown)
                {
                    targetKind = componentKind;
                }
            }

            if (resourceNetId == 0U)
            {
                resourceNetId = ownerNetId;
            }

            Vector3 entityPosition;
            if (targetKind == AuraTargetKind.Unknown && this.TryGetAuraEntityPosition(entity, out entityPosition))
            {
                AuraTargetKind positionKind = this.GetAuraTargetKindFromPosition(entityPosition);
                if (positionKind != AuraTargetKind.Unknown)
                {
                    targetKind = positionKind;
                }
            }

            return true;
        }

        private bool TryGetAuraCollectableInCold(object collectableObject, out bool inCold)
        {
            inCold = false;
            if (collectableObject == null)
            {
                return false;
            }

            try
            {
                object raw = null;
                if (this.auraCollectableObjectInColdField != null)
                {
                    raw = this.auraCollectableObjectInColdField.GetValue(collectableObject);
                }
                else if (this.auraCollectableObjectInColdProperty != null)
                {
                    raw = this.auraCollectableObjectInColdProperty.GetValue(collectableObject, null);
                }

                if (raw is bool)
                {
                    inCold = (bool)raw;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private bool IsAuraLocalResourceCooldownActive(Vector3 entityPosition, AuraTargetKind targetKind)
        {
            float now = Time.unscaledTime;
            if (targetKind == AuraTargetKind.Tree || targetKind == AuraTargetKind.Unknown)
            {
                if (this.IsAuraCooldownActiveNear(entityPosition, TreePositions, this.treeCooldowns_res, 25f, now)
                    || this.IsAuraCooldownActiveNear(entityPosition, RareTreePositions, this.rareTreeCooldowns_res, 25f, now)
                    || this.IsAuraCooldownActiveNear(entityPosition, AppleTreePositions, this.appleTreeCooldowns_res, 25f, now)
                    || this.IsAuraCooldownActiveNear(entityPosition, OrangeTreePositions, this.orangeTreeCooldowns_res, 25f, now))
                {
                    return true;
                }
            }

            if (targetKind == AuraTargetKind.Stone || targetKind == AuraTargetKind.Unknown)
            {
                if (this.IsAuraCooldownActiveNear(entityPosition, RockPositions, this.rockCooldowns, 25f, now)
                    || this.IsAuraCooldownActiveNear(entityPosition, OrePositions, this.oreCooldowns, 25f, now))
                {
                    return true;
                }
            }

            if (targetKind == AuraTargetKind.Bush || targetKind == AuraTargetKind.Unknown)
            {
                if (this.IsAuraHorizontalCooldownActiveNear(entityPosition, this.blueberryPositions, this.blueberryCooldowns, 144f, now)
                    || this.IsAuraHorizontalCooldownActiveNear(entityPosition, this.raspberryPositions, this.raspberryCooldowns, 144f, now))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsAuraCooldownActiveNear(Vector3 entityPosition, Vector3[] candidates, Dictionary<int, float> cooldowns, float radiusSqr, float now)
        {
            int index = this.FindAuraClosestIndex(entityPosition, candidates, radiusSqr, false);
            return index >= 0 && cooldowns != null && cooldowns.TryGetValue(index, out float until) && now < until;
        }

        private bool IsAuraHorizontalCooldownActiveNear(Vector3 entityPosition, Vector3[] candidates, Dictionary<int, float> cooldowns, float radiusSqr, float now)
        {
            int index = this.FindAuraClosestIndex(entityPosition, candidates, radiusSqr, true);
            return index >= 0 && cooldowns != null && cooldowns.TryGetValue(index, out float until) && now < until;
        }

        private int FindAuraClosestIndex(Vector3 entityPosition, Vector3[] candidates, float radiusSqr, bool horizontalOnly)
        {
            if (candidates == null || candidates.Length == 0)
            {
                return -1;
            }

            int bestIndex = -1;
            float bestSqr = radiusSqr;
            for (int i = 0; i < candidates.Length; i++)
            {
                float sqr;
                if (horizontalOnly)
                {
                    float dx = candidates[i].x - entityPosition.x;
                    float dz = candidates[i].z - entityPosition.z;
                    sqr = dx * dx + dz * dz;
                }
                else
                {
                    sqr = (candidates[i] - entityPosition).sqrMagnitude;
                }

                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private void StampAuraFallbackNodeCooldown(uint ownerNetId, AuraTargetKind targetKind)
        {
            object entity = this.TryGetAuraOwnerEntity(ownerNetId);
            if (entity == null)
            {
                return;
            }

            Vector3 entityPosition;
            if (!this.TryGetAuraEntityPosition(entity, out entityPosition))
            {
                return;
            }

            float bestSqr = 25f;
            int bestIndex = -1;
            float bestDuration = 0f;
            string bestLabel = string.Empty;
            Dictionary<int, float> bestCooldowns = null;
            Dictionary<int, float> bestHideUntil = null;

            if (targetKind == AuraTargetKind.Tree || targetKind == AuraTargetKind.Unknown)
            {
                this.TrySelectAuraCooldownStamp(entityPosition, TreePositions, this.treeCooldowns_res, this.treeHideUntil_res, this.treeCooldownDuration_res, LocalizationManager.Translate("Tree"), ref bestSqr, ref bestIndex, ref bestDuration, ref bestLabel, ref bestCooldowns, ref bestHideUntil);
                this.TrySelectAuraCooldownStamp(entityPosition, RareTreePositions, this.rareTreeCooldowns_res, this.rareTreeHideUntil_res, this.rareTreeCooldownDuration_res, LocalizationManager.Translate("Rare Tree"), ref bestSqr, ref bestIndex, ref bestDuration, ref bestLabel, ref bestCooldowns, ref bestHideUntil);
                this.TrySelectAuraCooldownStamp(entityPosition, AppleTreePositions, this.appleTreeCooldowns_res, this.appleTreeHideUntil_res, this.appleTreeCooldownDuration_res, LocalizationManager.Translate("Apple Tree"), ref bestSqr, ref bestIndex, ref bestDuration, ref bestLabel, ref bestCooldowns, ref bestHideUntil);
                this.TrySelectAuraCooldownStamp(entityPosition, OrangeTreePositions, this.orangeTreeCooldowns_res, this.orangeTreeHideUntil_res, this.orangeTreeCooldownDuration_res, LocalizationManager.Translate("Mandarin Tree"), ref bestSqr, ref bestIndex, ref bestDuration, ref bestLabel, ref bestCooldowns, ref bestHideUntil);
            }

            if (targetKind == AuraTargetKind.Stone || targetKind == AuraTargetKind.Unknown)
            {
                this.TrySelectAuraCooldownStamp(entityPosition, RockPositions, this.rockCooldowns, this.rockHideUntil, this.rockCooldownDuration, LocalizationManager.Translate("Stone"), ref bestSqr, ref bestIndex, ref bestDuration, ref bestLabel, ref bestCooldowns, ref bestHideUntil);
                this.TrySelectAuraCooldownStamp(entityPosition, OrePositions, this.oreCooldowns, this.oreHideUntil, this.oreCooldownDuration, LocalizationManager.Translate("Ore"), ref bestSqr, ref bestIndex, ref bestDuration, ref bestLabel, ref bestCooldowns, ref bestHideUntil);
            }

            if (targetKind == AuraTargetKind.Bush || targetKind == AuraTargetKind.Unknown)
            {
                float bestBushSqr = 144f;
                this.TrySelectAuraBerryCooldownStamp(entityPosition, this.blueberryPositions, this.blueberryCooldowns, this.blueberryHideUntil, this.blueberryCooldownDuration, LocalizationManager.Translate("Blueberry"), ref bestBushSqr, ref bestIndex, ref bestDuration, ref bestLabel, ref bestCooldowns, ref bestHideUntil);
                this.TrySelectAuraBerryCooldownStamp(entityPosition, this.raspberryPositions, this.raspberryCooldowns, this.raspberryHideUntil, this.raspberryCooldownDuration, LocalizationManager.Translate("Raspberry"), ref bestBushSqr, ref bestIndex, ref bestDuration, ref bestLabel, ref bestCooldowns, ref bestHideUntil);
            }

            if (bestCooldowns == null || bestHideUntil == null || bestIndex < 0)
            {
                return;
            }

            float now = Time.unscaledTime;
            float until = now + Math.Max(1f, bestDuration);
            float hideUntil = now + 10f;
            float existing;
            if (!bestCooldowns.TryGetValue(bestIndex, out existing) || existing < until)
            {
                bestCooldowns[bestIndex] = until;
            }
            bestHideUntil[bestIndex] = hideUntil;

            if (AuraFarmDebugLogs)
            {
                ModLogger.Msg("[AuraFarm] Fallback cooldown stamped: " + bestLabel + " #" + bestIndex + " (" + Math.Max(1f, bestDuration).ToString("F1") + "s)");
            }
        }

        private void TrySelectAuraCooldownStamp(
            Vector3 entityPosition,
            Vector3[] candidates,
            Dictionary<int, float> cooldowns,
            Dictionary<int, float> hideUntil,
            float duration,
            string label,
            ref float bestSqr,
            ref int bestIndex,
            ref float bestDuration,
            ref string bestLabel,
            ref Dictionary<int, float> bestCooldowns,
            ref Dictionary<int, float> bestHideUntil)
        {
            int idx = this.FindClosestItemIndexLocal(entityPosition, candidates);
            if (idx < 0)
            {
                return;
            }

            float sqr = (candidates[idx] - entityPosition).sqrMagnitude;
            if (sqr >= bestSqr)
            {
                return;
            }

            bestSqr = sqr;
            bestIndex = idx;
            bestDuration = duration;
            bestLabel = label;
            bestCooldowns = cooldowns;
            bestHideUntil = hideUntil;
        }

        private void TrySelectAuraBerryCooldownStamp(
            Vector3 entityPosition,
            Vector3[] candidates,
            Dictionary<int, float> cooldowns,
            Dictionary<int, float> hideUntil,
            float duration,
            string label,
            ref float bestSqr,
            ref int bestIndex,
            ref float bestDuration,
            ref string bestLabel,
            ref Dictionary<int, float> bestCooldowns,
            ref Dictionary<int, float> bestHideUntil)
        {
            if (candidates == null || candidates.Length == 0)
            {
                return;
            }

            const float berryMatchRadiusSqr = 144f;
            bestSqr = Math.Min(bestSqr, berryMatchRadiusSqr);

            int idx = -1;
            float closestSqr = bestSqr;
            for (int i = 0; i < candidates.Length; i++)
            {
                float dx = candidates[i].x - entityPosition.x;
                float dz = candidates[i].z - entityPosition.z;
                float sqr = dx * dx + dz * dz;
                if (sqr < closestSqr)
                {
                    closestSqr = sqr;
                    idx = i;
                }
            }

            if (idx < 0)
            {
                return;
            }

            bestSqr = closestSqr;
            bestIndex = idx;
            bestDuration = duration;
            bestLabel = label;
            bestCooldowns = cooldowns;
            bestHideUntil = hideUntil;
        }

        private bool ResolveAuraFarmRuntimeMethods()
        {
            if (this.auraFarmMethodsReady)
            {
                return true;
            }

            try
            {
                this.auraLastError = string.Empty;

                if (this.auraResourceProtocolManagerType == null)
                {
                    this.auraResourceProtocolManagerType = this.FindTypeByName("XDTDataAndProtocol.ProtocolService.Resource.ResourceProtocolManager", "XDTDataAndProtocol.ProtocolService.Resource", "ResourceProtocolManager");
                    if (this.auraResourceProtocolManagerType == null)
                    {
                        this.auraResourceProtocolManagerType = this.FindTypeBySignature("ResourceProtocolManager", "XDTDataAndProtocol", true, false);
                    }
                }

                if (this.auraInteractSystemType == null)
                {
                    this.auraInteractSystemType = this.FindTypeByName("XDTLevelAndEntity.BaseSystem.InteractSystem.InteractSystem", "XDTLevelAndEntity.BaseSystem.InteractSystem", "InteractSystem");
                    if (this.auraInteractSystemType == null)
                    {
                        this.auraInteractSystemType = this.FindTypeBySignature("InteractSystem", "XDTLevelAndEntity", false, true);
                    }
                }

                if (this.auraEntityHelperType == null)
                {
                    this.auraEntityHelperType = this.FindTypeByName("XDTLevelAndEntity.Utils.EntityHelper", "XDTLevelAndEntity.Utils", "EntityHelper");
                    if (this.auraEntityHelperType == null)
                    {
                        this.auraEntityHelperType = this.FindTypeBySignature("EntityHelper", "XDTLevelAndEntity", false, false);
                    }
                }

                if (this.auraEntityUtilType == null)
                {
                    this.auraEntityUtilType = this.FindTypeByName("XDTLevelAndEntity.BaseSystem.EntitiesManager.EntityUtil", "XDTLevelAndEntity.BaseSystem.EntitiesManager", "EntityUtil");
                    if (this.auraEntityUtilType == null)
                    {
                        this.auraEntityUtilType = this.FindTypeBySignature("EntityUtil", "XDTLevelAndEntity", false, false);
                    }
                    if (this.auraEntityUtilType == null)
                    {
                        this.auraEntityUtilType = this.FindTypeBySignature("EntityUtil", null, false, false);
                    }
                }

                if (this.auraEntitiesType == null)
                {
                    this.auraEntitiesType = this.FindTypeByName("XDTLevelAndEntity.BaseSystem.EntitiesManager.Entities", "XDTLevelAndEntity.BaseSystem.EntitiesManager", "Entities");
                    if (this.auraEntitiesType == null)
                    {
                        this.auraEntitiesType = this.FindTypeBySignature("Entities", "XDTLevelAndEntity", false, false);
                    }
                    if (this.auraEntitiesType == null)
                    {
                        this.auraEntitiesType = this.FindTypeBySignature("Entities", null, false, false);
                    }
                }

                if (this.auraAxeCheckerType == null)
                {
                    this.auraAxeCheckerType = this.FindTypeByName("XDTLevelAndEntity.Gameplay.Component.Equip.AxeChecker", "XDTLevelAndEntity.Gameplay.Component.Equip", "AxeChecker");
                    if (this.auraAxeCheckerType == null)
                    {
                        this.auraAxeCheckerType = this.FindTypeBySignature("AxeChecker", "XDTLevelAndEntity", false, false);
                    }
                }

                if (this.auraSelectPriorityInfoType == null)
                {
                    this.auraSelectPriorityInfoType = this.FindTypeByName("XDTLevelAndEntity.BaseSystem.InteractSystem.SelectPriorityInfo", "XDTLevelAndEntity.BaseSystem.InteractSystem", "SelectPriorityInfo");
                    if (this.auraSelectPriorityInfoType == null)
                    {
                        this.auraSelectPriorityInfoType = this.FindTypeBySignature("SelectPriorityInfo", "XDTLevelAndEntity", false, false);
                    }
                }

                if (this.auraLevelObjectManagerType == null)
                {
                    this.auraLevelObjectManagerType = this.FindTypeByName("ScriptsRefactory.LevelAndEntity.LevelObjectManager", "ScriptsRefactory.LevelAndEntity", "LevelObjectManager");
                    if (this.auraLevelObjectManagerType == null)
                    {
                        this.auraLevelObjectManagerType = this.FindTypeBySignature("LevelObjectManager", null, false, false);
                    }
                }

                if (this.auraLevelObjectTagType == null)
                {
                    this.auraLevelObjectTagType = this.FindTypeByName("EcsClient.XDT.Scene.Shared.Data.SharedData.LevelObjectTag", "EcsClient.XDT.Scene.Shared.Data.SharedData", "LevelObjectTag");
                    if (this.auraLevelObjectTagType == null)
                    {
                        this.auraLevelObjectTagType = this.FindTypeBySignature("LevelObjectTag", null, false, false);
                    }
                }

                if (this.auraCylinderType == null)
                {
                    this.auraCylinderType = this.FindTypeByName("XDTGame.Core.SceneQuery.Cylinder", "XDTGame.Core.SceneQuery", "Cylinder");
                    if (this.auraCylinderType == null)
                    {
                        this.auraCylinderType = this.FindTypeByName("XDT.Physics.Cylinder", "XDT.Physics", "Cylinder");
                    }
                    if (this.auraCylinderType == null)
                    {
                        this.auraCylinderType = this.FindTypeBySignature("Cylinder", null, false, false);
                    }
                }

                if (this.auraCollectableObjectComponentType == null)
                {
                    this.auraCollectableObjectComponentType = this.FindTypeByName("XDTLevelAndEntity.Gameplay.Component.Gather.CollectableObjectComponent", "XDTLevelAndEntity.Gameplay.Component.Gather", "CollectableObjectComponent");
                }

                if (this.auraCollectableBushComponentType == null)
                {
                    this.auraCollectableBushComponentType = this.FindTypeByName("XDTLevelAndEntity.Gameplay.Component.Gather.CollectableBushComponent", "XDTLevelAndEntity.Gameplay.Component.Gather", "CollectableBushComponent");
                }

                if (this.auraDynamicBushComponentType == null)
                {
                    this.auraDynamicBushComponentType = this.FindTypeByName("XDTLevelAndEntity.Gameplay.Component.Gather.DynamicBushComponent", "XDTLevelAndEntity.Gameplay.Component.Gather", "DynamicBushComponent");
                }

                if (this.auraResourceProtocolManagerType != null)
                {
                    if (this.auraSendPickBushMethod == null)
                    {
                        this.auraSendPickBushMethod = this.GetMethodQuiet(this.auraResourceProtocolManagerType, "SendPickBushCommand", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, new Type[] { typeof(uint) });
                    }

                    if (this.auraSendAttackTreeMethod == null)
                    {
                        this.auraSendAttackTreeMethod = this.GetMethodQuiet(this.auraResourceProtocolManagerType, "SendAttackTreeCommand", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, new Type[] { typeof(uint), typeof(bool) });
                    }

                    if (this.auraSendHitStoneMethod == null)
                    {
                        this.auraSendHitStoneMethod = this.GetMethodQuiet(this.auraResourceProtocolManagerType, "SendHitStoneCommand", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, new Type[] { typeof(uint), typeof(bool) });
                    }
                }

                if (this.auraInteractSystemType != null)
                {
                    if (this.auraInteractSystemGetInstanceMethod == null)
                    {
                        this.auraInteractSystemGetInstanceMethod = this.GetMethodQuiet(this.auraInteractSystemType, "get_Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance, Type.EmptyTypes);
                        if (this.auraInteractSystemGetInstanceMethod == null)
                        {
                            PropertyInfo prop = this.GetPropertyQuiet(this.auraInteractSystemType, "Instance");
                            if (prop != null)
                            {
                                this.auraInteractSystemGetInstanceMethod = prop.GetGetMethod(true);
                            }
                        }

                        if (this.auraInteractSystemGetInstanceMethod == null)
                        {
                            this.auraInteractSystemGetInstanceMethod = this.GetMethodQuiet(this.auraInteractSystemType, "GetInstance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance, Type.EmptyTypes)
                                ?? this.GetMethodQuiet(this.auraInteractSystemType, "get_Singleton", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance, Type.EmptyTypes);
                        }
                    }

                    if (this.auraInteractSystemInstanceField == null)
                    {
                        this.auraInteractSystemInstanceField = this.GetFieldQuiet(this.auraInteractSystemType, "Instance")
                            ?? this.GetFieldQuiet(this.auraInteractSystemType, "_instance")
                            ?? this.GetFieldQuiet(this.auraInteractSystemType, "instance");
                    }

                    if (this.auraInteractSystemCurrentTargetField == null)
                    {
                        this.auraInteractSystemCurrentTargetField = this.GetFieldQuiet(this.auraInteractSystemType, "_currentSelectTarget");
                    }

                    if (this.auraInteractSystemFocusLevelObjectsField == null)
                    {
                        this.auraInteractSystemFocusLevelObjectsField = this.GetFieldQuiet(this.auraInteractSystemType, "_focusLevelObjects");
                    }

                    if (this.auraInteractSystemSelectedField == null)
                    {
                        this.auraInteractSystemSelectedField = this.GetFieldQuiet(this.auraInteractSystemType, "_selected");
                    }

                    if (this.auraInteractSystemSelectPriorityLengthField == null)
                    {
                        this.auraInteractSystemSelectPriorityLengthField = this.GetFieldQuiet(this.auraInteractSystemType, "_selectPriorityLength");
                    }

                    if (this.auraInteractSystemSelectPriorityInfoArrayField == null)
                    {
                        this.auraInteractSystemSelectPriorityInfoArrayField = this.GetFieldQuiet(this.auraInteractSystemType, "_selectPriorityInfoArray");
                    }

                    if (this.auraInteractSystemInteractCylinderField == null)
                    {
                        this.auraInteractSystemInteractCylinderField = this.GetFieldQuiet(this.auraInteractSystemType, "interactCylinder");
                    }

                    if (this.auraInteractSystemGetTargetListMethod == null)
                    {
                        this.auraInteractSystemGetTargetListMethod = this.GetMethodByNameAndParamCountQuiet(this.auraInteractSystemType, "GetInteractTargetList", 1);
                    }

                    if (this.auraInteractSystemGetPlayerMethod == null)
                    {
                        this.auraInteractSystemGetPlayerMethod = this.GetMethodQuiet(this.auraInteractSystemType, "get_player", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, Type.EmptyTypes)
                            ?? this.GetMethodQuiet(this.auraInteractSystemType, "GetPlayer", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, Type.EmptyTypes);
                    }

                    if (this.auraInteractSystemCurrentTargetProperty == null)
                    {
                        this.auraInteractSystemCurrentTargetProperty = this.GetPropertyQuiet(this.auraInteractSystemType, "CurrentSelectTarget")
                            ?? this.GetPropertyQuiet(this.auraInteractSystemType, "currentSelectTarget");
                    }

                    if (this.auraInteractSystemFocusLevelObjectsProperty == null)
                    {
                        this.auraInteractSystemFocusLevelObjectsProperty = this.GetPropertyQuiet(this.auraInteractSystemType, "FocusLevelObjects")
                            ?? this.GetPropertyQuiet(this.auraInteractSystemType, "focusLevelObjects");
                    }

                    if (this.auraInteractSystemSelectedProperty == null)
                    {
                        this.auraInteractSystemSelectedProperty = this.GetPropertyQuiet(this.auraInteractSystemType, "Selected")
                            ?? this.GetPropertyQuiet(this.auraInteractSystemType, "selected");
                    }

                    if (this.auraInteractSystemInteractCylinderProperty == null)
                    {
                        this.auraInteractSystemInteractCylinderProperty = this.GetPropertyQuiet(this.auraInteractSystemType, "interactCylinder")
                            ?? this.GetPropertyQuiet(this.auraInteractSystemType, "InteractCylinder");
                    }

                    if (this.auraInteractSystemPlayerProperty == null)
                    {
                        this.auraInteractSystemPlayerProperty = this.GetPropertyQuiet(this.auraInteractSystemType, "player")
                            ?? this.GetPropertyQuiet(this.auraInteractSystemType, "Player");
                    }

                    this.RefreshAuraInteractCandidateMembers();
                }

                if (this.auraAxeCheckerType != null)
                {
                    if (this.auraAxeCheckerInstanceField == null)
                    {
                        this.auraAxeCheckerInstanceField = this.GetFieldQuiet(this.auraAxeCheckerType, "Instance");
                    }

                    if (this.auraAxeCheckerInstanceProperty == null)
                    {
                        this.auraAxeCheckerInstanceProperty = this.GetPropertyQuiet(this.auraAxeCheckerType, "Instance");
                    }

                    if (this.auraAxeCheckerPhysicalSelectMethod == null)
                    {
                        this.auraAxeCheckerPhysicalSelectMethod = this.GetMethodByNameAndParamCountQuiet(this.auraAxeCheckerType, "PhysicalSelect", 3);
                    }
                }

                if (this.auraEntityHelperType != null)
                {
                    if (this.auraEntityHelperGetTargetListMethod == null)
                    {
                        this.auraEntityHelperGetTargetListMethod = this.GetMethodByNameAndParamCountQuiet(this.auraEntityHelperType, "GetPlayerInteractTargetList", 1);
                    }

                    if (this.auraEntityHelperGetLevelObjectOwnerMethod == null)
                    {
                        this.auraEntityHelperGetLevelObjectOwnerMethod = this.GetMethodByNameAndParamCountQuiet(this.auraEntityHelperType, "GetLevelObjectOwner", 1);
                    }

                    if (this.auraEntityHelperGetLevelObjectMethod == null)
                    {
                        this.auraEntityHelperGetLevelObjectMethod = this.GetMethodByNameAndParamCountQuiet(this.auraEntityHelperType, "GetLevelObject", 1);
                    }
                }

                if (this.auraEntityUtilType != null && this.auraEntityUtilGetEntityMethod == null)
                {
                    this.auraEntityUtilGetEntityMethod = this.GetMethodQuiet(this.auraEntityUtilType, "GetEntity", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, new Type[] { typeof(uint) });
                }

                if (this.auraEntityUtilType != null && this.auraEntityUtilGetSelfPlayerEntityMethod == null)
                {
                    this.auraEntityUtilGetSelfPlayerEntityMethod = this.GetMethodQuiet(this.auraEntityUtilType, "GetSelfPlayerEntity", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, Type.EmptyTypes);
                }

                if (this.auraEntityUtilType != null && this.auraEntityUtilGetLevelObjectMethod == null)
                {
                    this.auraEntityUtilGetLevelObjectMethod = this.GetMethodByNameAndParamCountQuiet(this.auraEntityUtilType, "GetLevelObject", 1);
                }

                if (this.auraEntitiesType != null && this.auraEntitiesSphereQueryEntitiesMethod == null)
                {
                    this.auraEntitiesSphereQueryEntitiesMethod = this.GetMethodByNameAndParamCountQuiet(this.auraEntitiesType, "SphereQueryEntities", 3);
                }

                if (this.auraLevelObjectManagerType != null)
                {
                    if (this.auraLevelObjectManagerInstanceProperty == null)
                    {
                        this.auraLevelObjectManagerInstanceProperty = this.GetPropertyQuiet(this.auraLevelObjectManagerType, "Instance");
                    }

                    if (this.auraLevelObjectManagerCylinderOverlapNonAllocMethod == null)
                    {
                        this.auraLevelObjectManagerCylinderOverlapNonAllocMethod = this.GetMethodByNameAndParamCountQuiet(this.auraLevelObjectManagerType, "CylinderOverlapNonAlloc", 5);
                    }
                }

                Type auraEntityType = null;
                if (this.auraEntityUtilGetEntityMethod != null)
                {
                    auraEntityType = this.auraEntityUtilGetEntityMethod.ReturnType;
                }
                else if (this.auraEntityUtilGetSelfPlayerEntityMethod != null)
                {
                    auraEntityType = this.auraEntityUtilGetSelfPlayerEntityMethod.ReturnType;
                }

                if (auraEntityType != null)
                {
                    if (this.auraEntityNetIdField == null)
                    {
                        this.auraEntityNetIdField = this.GetFieldQuiet(auraEntityType, "netId");
                    }
                    if (this.auraEntityNetIdProperty == null)
                    {
                        this.auraEntityNetIdProperty = this.GetPropertyQuiet(auraEntityType, "netId")
                            ?? this.GetPropertyQuiet(auraEntityType, "NetId");
                    }
                    if (this.auraEntityPositionField == null)
                    {
                        this.auraEntityPositionField = this.GetFieldQuiet(auraEntityType, "position");
                    }
                    if (this.auraEntityPositionProperty == null)
                    {
                        this.auraEntityPositionProperty = this.GetPropertyQuiet(auraEntityType, "position")
                            ?? this.GetPropertyQuiet(auraEntityType, "Position");
                    }
                    if (this.auraEntityRotationField == null)
                    {
                        this.auraEntityRotationField = this.GetFieldQuiet(auraEntityType, "rotation");
                    }
                    if (this.auraEntityRotationProperty == null)
                    {
                        this.auraEntityRotationProperty = this.GetPropertyQuiet(auraEntityType, "rotation")
                            ?? this.GetPropertyQuiet(auraEntityType, "Rotation");
                    }
                }

                Type auraLevelObjectType = null;
                if (this.auraEntityUtilGetLevelObjectMethod != null)
                {
                    auraLevelObjectType = this.auraEntityUtilGetLevelObjectMethod.ReturnType;
                }
                else if (this.auraEntityHelperGetLevelObjectMethod != null)
                {
                    auraLevelObjectType = this.auraEntityHelperGetLevelObjectMethod.ReturnType;
                }

                if (auraLevelObjectType != null)
                {
                    if (this.auraLevelObjectNetIdField == null)
                    {
                        this.auraLevelObjectNetIdField = this.GetFieldQuiet(auraLevelObjectType, "netId");
                    }
                    if (this.auraLevelObjectNetIdProperty == null)
                    {
                        this.auraLevelObjectNetIdProperty = this.GetPropertyQuiet(auraLevelObjectType, "netId")
                            ?? this.GetPropertyQuiet(auraLevelObjectType, "NetId");
                    }
                    if (this.auraLevelObjectOwnerNetIdField == null)
                    {
                        this.auraLevelObjectOwnerNetIdField = this.GetFieldQuiet(auraLevelObjectType, "ownerNetId");
                    }
                    if (this.auraLevelObjectOwnerNetIdProperty == null)
                    {
                        this.auraLevelObjectOwnerNetIdProperty = this.GetPropertyQuiet(auraLevelObjectType, "ownerNetId")
                            ?? this.GetPropertyQuiet(auraLevelObjectType, "OwnerNetId");
                    }
                    if (this.auraLevelObjectResourceIdField == null)
                    {
                        this.auraLevelObjectResourceIdField = this.GetFieldQuiet(auraLevelObjectType, "resourceID")
                            ?? this.GetFieldQuiet(auraLevelObjectType, "_resourceID");
                    }
                    if (this.auraLevelObjectResourceIdProperty == null)
                    {
                        this.auraLevelObjectResourceIdProperty = this.GetPropertyQuiet(auraLevelObjectType, "resourceID")
                            ?? this.GetPropertyQuiet(auraLevelObjectType, "ResourceID");
                    }
                }

                if (this.auraCylinderType != null)
                {
                    if (this.auraCylinderCenterField == null)
                    {
                        this.auraCylinderCenterField = this.GetFieldQuiet(this.auraCylinderType, "Center");
                    }
                    if (this.auraCylinderCenterProperty == null)
                    {
                        this.auraCylinderCenterProperty = this.GetPropertyQuiet(this.auraCylinderType, "Center")
                            ?? this.GetPropertyQuiet(this.auraCylinderType, "center");
                    }
                    if (this.auraCylinderRadiusField == null)
                    {
                        this.auraCylinderRadiusField = this.GetFieldQuiet(this.auraCylinderType, "Radius");
                    }
                    if (this.auraCylinderRadiusProperty == null)
                    {
                        this.auraCylinderRadiusProperty = this.GetPropertyQuiet(this.auraCylinderType, "Radius")
                            ?? this.GetPropertyQuiet(this.auraCylinderType, "radius");
                    }
                    if (this.auraCylinderHeightField == null)
                    {
                        this.auraCylinderHeightField = this.GetFieldQuiet(this.auraCylinderType, "Height");
                    }
                    if (this.auraCylinderHeightProperty == null)
                    {
                        this.auraCylinderHeightProperty = this.GetPropertyQuiet(this.auraCylinderType, "Height")
                            ?? this.GetPropertyQuiet(this.auraCylinderType, "height");
                    }
                }

                if (this.auraSelectPriorityInfoType != null)
                {
                    if (this.auraSelectPriorityInfoShapeField == null)
                    {
                        this.auraSelectPriorityInfoShapeField = this.GetFieldQuiet(this.auraSelectPriorityInfoType, "shape");
                    }
                    if (this.auraSelectPriorityInfoShapeProperty == null)
                    {
                        this.auraSelectPriorityInfoShapeProperty = this.GetPropertyQuiet(this.auraSelectPriorityInfoType, "shape")
                            ?? this.GetPropertyQuiet(this.auraSelectPriorityInfoType, "Shape");
                    }
                }

                if (this.auraCollectableObjectComponentType != null)
                {
                    if (this.auraCollectableObjectResTypeField == null)
                    {
                        this.auraCollectableObjectResTypeField = this.GetFieldQuiet(this.auraCollectableObjectComponentType, "resType");
                    }

                    if (this.auraCollectableObjectResTypeProperty == null)
                    {
                        this.auraCollectableObjectResTypeProperty = this.GetPropertyQuiet(this.auraCollectableObjectComponentType, "resType")
                            ?? this.GetPropertyQuiet(this.auraCollectableObjectComponentType, "ResType");
                    }

                    if (this.auraCollectableObjectInColdField == null)
                    {
                        this.auraCollectableObjectInColdField = this.GetFieldQuiet(this.auraCollectableObjectComponentType, "inCold");
                    }

                    if (this.auraCollectableObjectInColdProperty == null)
                    {
                        this.auraCollectableObjectInColdProperty = this.GetPropertyQuiet(this.auraCollectableObjectComponentType, "inCold")
                            ?? this.GetPropertyQuiet(this.auraCollectableObjectComponentType, "InCold");
                    }
                }

                this.ResolveAuraFarmRuntimeMethodsViaMono();

                bool hasResourcePath = this.auraSendPickBushMethod != null || this.auraSendAttackTreeMethod != null || this.auraSendHitStoneMethod != null || this.auraMonoSendPickBushMethodPtr != IntPtr.Zero || this.auraMonoSendAttackTreeMethodPtr != IntPtr.Zero || this.auraMonoSendHitStoneMethodPtr != IntPtr.Zero;
                bool hasInteractPath = this.auraInteractSystemGetInstanceMethod != null || this.auraInteractSystemInstanceField != null || this.auraMonoInteractGetInstanceMethodPtr != IntPtr.Zero;
                bool hasEntityHelperPath = this.auraEntityHelperGetTargetListMethod != null && this.auraEntityHelperGetLevelObjectOwnerMethod != null;
                this.auraFarmMethodsReady = hasResourcePath && (hasInteractPath || hasEntityHelperPath);
                if (!this.auraFarmMethodsReady)
                {
                    string resourceTypeName = this.auraResourceProtocolManagerType != null ? this.auraResourceProtocolManagerType.FullName : "null";
                    string interactTypeName = this.auraInteractSystemType != null ? this.auraInteractSystemType.FullName : "null";
                    this.auraLastError = "Aura resolver incomplete. ResourceType=" + resourceTypeName
                        + " InteractType=" + interactTypeName
                        + " Pick=" + (this.auraSendPickBushMethod != null)
                        + " Attack=" + (this.auraSendAttackTreeMethod != null)
                        + " Stone=" + (this.auraSendHitStoneMethod != null)
                        + " InteractInstance=" + (this.auraInteractSystemGetInstanceMethod != null)
                        + " InteractInstanceField=" + (this.auraInteractSystemInstanceField != null)
                        + " CandidateMembers=" + (this.auraInteractTargetCandidateFields.Length + this.auraInteractTargetCandidateProperties.Length)
                        + " InteractTargetList=" + (this.auraInteractSystemGetTargetListMethod != null)
                        + " EntityHelperTargets=" + (this.auraEntityHelperGetTargetListMethod != null)
                        + " EntityHelperOwner=" + (this.auraEntityHelperGetLevelObjectOwnerMethod != null)
                        + " MonoPick=" + (this.auraMonoSendPickBushMethodPtr != IntPtr.Zero)
                        + " MonoAttack=" + (this.auraMonoSendAttackTreeMethodPtr != IntPtr.Zero)
                        + " MonoStone=" + (this.auraMonoSendHitStoneMethodPtr != IntPtr.Zero)
                        + " MonoInteractInstance=" + (this.auraMonoInteractGetInstanceMethodPtr != IntPtr.Zero);
                }
                else
                {
                    string resourceState;
                    if (this.auraResourceProtocolManagerType != null)
                    {
                        resourceState = this.auraResourceProtocolManagerType.Assembly.GetName().Name;
                    }
                    else
                    {
                        resourceState = "mono(pick=" + (this.auraMonoSendPickBushMethodPtr != IntPtr.Zero)
                            + ",attack=" + (this.auraMonoSendAttackTreeMethodPtr != IntPtr.Zero)
                            + ",stone=" + (this.auraMonoSendHitStoneMethodPtr != IntPtr.Zero) + ")";
                    }

                    string interactState;
                    if (this.auraInteractSystemType != null)
                    {
                        interactState = this.auraInteractSystemType.Assembly.GetName().Name;
                    }
                    else
                    {
                        interactState = "mono(instance=" + (this.auraMonoInteractGetInstanceMethodPtr != IntPtr.Zero)
                            + ",canCollect=" + (this.auraMonoInteractCanCollectionInteractionMethodPtr != IntPtr.Zero)
                            + ",currentHandhold=" + (this.auraMonoInteractCurrentHandholdInteractMethodPtr != IntPtr.Zero)
                            + ",currentTargetField=" + (this.auraMonoInteractCurrentTargetFieldPtr != IntPtr.Zero)
                            + ",focusField=" + (this.auraMonoInteractFocusLevelObjectsFieldPtr != IntPtr.Zero)
                            + ",selectedField=" + (this.auraMonoInteractSelectedFieldPtr != IntPtr.Zero)
                            + ",selectPriorityLength=" + (this.auraMonoInteractSelectPriorityLengthFieldPtr != IntPtr.Zero)
                            + ",selectPriorityArray=" + (this.auraMonoInteractSelectPriorityInfoArrayFieldPtr != IntPtr.Zero)
                            + ",lookDecisions=" + (this.auraMonoLocalPlayerLookDecisionsFieldPtr != IntPtr.Zero)
                            + ",lookTargetList=" + (this.auraMonoLocalPlayerLookTargetListFieldPtr != IntPtr.Zero)
                            + ",ownerResolver=" + (this.auraMonoEntityHelperGetLevelObjectOwnerMethodPtr != IntPtr.Zero) + ")";
                    }

                    ModLogger.Msg("[AuraFarm] Runtime resolver ready. Resource=" + resourceState + " Interact=" + interactState);
                }
            }
            catch (Exception ex)
            {
                this.auraLastError = "ResolveAuraFarmRuntimeMethods failed: " + ex.Message;
                this.auraFarmMethodsReady = false;
                ModLogger.Msg("[AuraFarm] " + this.auraLastError);
            }

            return this.auraFarmMethodsReady;
        }

        private bool InvokeAuraPickBush(uint ownerNetId)
        {
            try
            {
                if (this.auraMonoSendPickBushMethodPtr != IntPtr.Zero && auraMonoRuntimeInvoke != null)
                {
                    return this.InvokeAuraMonoPickBush(ownerNetId);
                }

                if (this.auraSendPickBushMethod != null)
                {
                    this.auraSendPickBushMethod.Invoke(null, new object[] { ownerNetId });
                    return true;
                }
            }
            catch (Exception ex)
            {
                this.auraLastError = "SendPickBushCommand failed: " + ex.Message;
                if (AuraFarmDebugLogs)
                {
                    ModLogger.Msg("[AuraFarm] " + this.auraLastError);
                }
            }

            return false;
        }

        private bool InvokeAuraAttackTree(uint ownerNetId, bool isCombo)
        {
            try
            {
                if (this.auraMonoSendAttackTreeMethodPtr != IntPtr.Zero && auraMonoRuntimeInvoke != null)
                {
                    return this.InvokeAuraMonoAttackTree(ownerNetId, isCombo);
                }

                if (this.auraSendAttackTreeMethod != null)
                {
                    this.auraSendAttackTreeMethod.Invoke(null, new object[] { ownerNetId, isCombo });
                    return true;
                }
            }
            catch (Exception ex)
            {
                this.auraLastError = "SendAttackTreeCommand failed: " + ex.Message;
                if (AuraFarmDebugLogs)
                {
                    ModLogger.Msg("[AuraFarm] " + this.auraLastError);
                }
            }

            return false;
        }

        private bool InvokeAuraHitStone(uint ownerNetId, bool isCombo)
        {
            try
            {
                if (this.auraMonoSendHitStoneMethodPtr != IntPtr.Zero && auraMonoRuntimeInvoke != null)
                {
                    return this.InvokeAuraMonoHitStone(ownerNetId, isCombo);
                }

                if (this.auraSendHitStoneMethod != null)
                {
                    this.auraSendHitStoneMethod.Invoke(null, new object[] { ownerNetId, isCombo });
                    return true;
                }
            }
            catch (Exception ex)
            {
                this.auraLastError = "SendHitStoneCommand failed: " + ex.Message;
                if (AuraFarmDebugLogs)
                {
                    ModLogger.Msg("[AuraFarm] " + this.auraLastError);
                }
            }

            return false;
        }

        private bool CollectAuraOwnerTargets(HashSet<uint> output)
        {
            output.Clear();
            this.auraTargetInfoByOwnerId.Clear();

            object interactSystem = this.GetAuraInteractSystemInstance();
            if (interactSystem != null)
            {
                this.TryAddAuraFieldTarget(interactSystem, this.auraInteractSystemCurrentTargetField, output);
                this.TryAddAuraContainerTargets(interactSystem, this.auraInteractSystemFocusLevelObjectsField, output);
                this.TryAddAuraContainerTargets(interactSystem, this.auraInteractSystemSelectedField, output);
                this.TryAddAuraPropertyTarget(interactSystem, this.auraInteractSystemCurrentTargetProperty, output);
                this.TryAddAuraContainerTargets(interactSystem, this.auraInteractSystemFocusLevelObjectsProperty, output);
                this.TryAddAuraContainerTargets(interactSystem, this.auraInteractSystemSelectedProperty, output);

                if (output.Count == 0)
                {
                    this.TryAddAuraTargetsFromCandidateMembers(interactSystem, output);
                }
            }

            if (AuraUseMonoTargetFallbacks && output.Count < AuraMergedTargetSoftCap)
            {
                this.TryCollectAuraOwnerTargetsViaMonoCurrentTarget(output);
            }

            if (AuraUseMonoTargetFallbacks && output.Count < AuraMergedTargetSoftCap)
            {
                this.TryCollectAuraOwnerTargetsViaMonoAdvancedFallbacks(output);
            }

            if (output.Count < AuraMergedTargetSoftCap)
            {
                this.TryCollectAuraOwnerTargetsViaTargetLists(interactSystem, output);
            }

            if (output.Count == 0)
            {
                this.TryCollectAuraOwnerTargetsViaManagedSelectPriority(interactSystem, output);
            }

            if (output.Count == 0)
            {
                this.TryCollectAuraOwnerTargetsViaThrottledMonoFallbacks(interactSystem, output);
            }

            if (AuraUseMonoTargetFallbacks && output.Count < AuraMergedTargetSoftCap)
            {
                this.TryCollectAuraOwnerTargetsViaMonoAxeChecker(interactSystem, output);
            }

            if (AuraUseManagedSpatialFallbackScans && output.Count < AuraMergedTargetSoftCap)
            {
                this.TryCollectAuraOwnerTargetsViaSphereQuery(output);
            }

            if (AuraUseManagedSpatialFallbackScans && output.Count < AuraMergedTargetSoftCap)
            {
                this.TryCollectAuraOwnerTargetsViaCylinderScan(interactSystem, output);
            }

            if (output.Count == 0 && interactSystem == null)
            {
                this.auraLastError = "No aura targets found (InteractSystem/EntityHelper/Mono current target).";
            }

            return output.Count > 0;
        }

        private void TryCollectAuraOwnerTargetsViaManagedSelectPriority(object interactSystem, HashSet<uint> output)
        {
            if (interactSystem == null
                || this.auraInteractSystemSelectPriorityLengthField == null
                || this.auraInteractSystemSelectPriorityInfoArrayField == null
                || (this.auraSelectPriorityInfoShapeField == null && this.auraSelectPriorityInfoShapeProperty == null))
            {
                return;
            }

            try
            {
                object rawLength = this.auraInteractSystemSelectPriorityLengthField.GetValue(interactSystem);
                int length = rawLength is int intLength ? intLength : 0;
                if (length <= 0)
                {
                    return;
                }

                object rawCollection = this.auraInteractSystemSelectPriorityInfoArrayField.GetValue(null)
                    ?? this.auraInteractSystemSelectPriorityInfoArrayField.GetValue(interactSystem);
                if (rawCollection == null)
                {
                    return;
                }

                if (rawCollection is IEnumerable enumerable)
                {
                    int count = 0;
                    foreach (object infoObj in enumerable)
                    {
                        if (infoObj == null)
                        {
                            count++;
                            if (count >= length || count >= AuraMergedTargetSoftCap)
                            {
                                break;
                            }
                            continue;
                        }

                        object shapeObj = null;
                        if (this.auraSelectPriorityInfoShapeField != null)
                        {
                            shapeObj = this.auraSelectPriorityInfoShapeField.GetValue(infoObj);
                        }
                        else if (this.auraSelectPriorityInfoShapeProperty != null)
                        {
                            shapeObj = this.auraSelectPriorityInfoShapeProperty.GetValue(infoObj, null);
                        }

                        if (shapeObj != null && this.TryGetAuraLevelObjectNetId(shapeObj, out ulong levelObjectId) && levelObjectId != 0UL)
                        {
                            this.RegisterAuraTargetFromLevelObjectId(output, levelObjectId, "ManagedSelectPriority[" + count + "]");
                            if (output.Count >= AuraMergedTargetSoftCap)
                            {
                                break;
                            }
                        }

                        count++;
                        if (count >= length || count >= AuraMergedTargetSoftCap)
                        {
                            break;
                        }
                    }
                }
                else if (AuraFarmDebugLogs)
                {
                    ModLogger.Msg("[AuraFarm] Managed select priority collection is not enumerable: " + rawCollection.GetType().FullName);
                }

                if (AuraFarmDebugLogs && output.Count > 0)
                {
                    ModLogger.Msg("[AuraFarm] Managed select priority path produced " + output.Count + " targets.");
                }
            }
            catch (Exception ex)
            {
                this.auraLastError = "Managed select priority read failed: " + ex.Message;
            }
        }

        private void TryCollectAuraOwnerTargetsViaThrottledMonoFallbacks(object interactSystem, HashSet<uint> output)
        {
            float now = Time.unscaledTime;
            if (now >= this.auraNextMonoFallbackScanAt)
            {
                this.auraNextMonoFallbackScanAt = now + AuraMonoFallbackScanInterval;
                this.auraMonoFallbackTargetBuffer.Clear();

                this.TryCollectAuraOwnerTargetsViaMonoAxeChecker(interactSystem, this.auraMonoFallbackTargetBuffer);
                if (this.auraMonoFallbackTargetBuffer.Count == 0)
                {
                    this.TryCollectAuraOwnerTargetsViaMonoManagedLists(interactSystem, this.auraMonoFallbackTargetBuffer);
                }
                if (this.auraMonoFallbackTargetBuffer.Count == 0)
                {
                    this.TryCollectAuraOwnerTargetsViaMonoCurrentTarget(this.auraMonoFallbackTargetBuffer);
                }
                if (this.auraMonoFallbackTargetBuffer.Count == 0)
                {
                    this.TryCollectAuraOwnerTargetsViaMonoAdvancedFallbacks(this.auraMonoFallbackTargetBuffer);
                }

                if (AuraFarmDebugLogs)
                {
                    int fallbackCount = this.auraMonoFallbackTargetBuffer.Count;
                    if (fallbackCount != this.auraLastLoggedMonoFallbackTargetCount || now - this.auraLastMonoFallbackLogAt >= 1f)
                    {
                        this.auraLastMonoFallbackLogAt = now;
                        this.auraLastLoggedMonoFallbackTargetCount = fallbackCount;
                        ModLogger.Msg("[AuraFarm] Throttled mono fallback targets=" + fallbackCount);
                    }
                }
            }

            if (this.auraMonoFallbackTargetBuffer.Count == 0)
            {
                return;
            }

            foreach (uint ownerNetId in this.auraMonoFallbackTargetBuffer)
            {
                output.Add(ownerNetId);
                if (output.Count >= AuraMergedTargetSoftCap)
                {
                    break;
                }
            }
        }

        private object GetAuraInteractSystemInstance()
        {
            if (this.auraInteractSystemGetInstanceMethod != null)
            {
                try
                {
                    return this.auraInteractSystemGetInstanceMethod.Invoke(null, null);
                }
                catch (Exception ex)
                {
                    this.auraLastError = "InteractSystem.Instance lookup failed: " + ex.Message;
                }
            }

            if (this.auraInteractSystemInstanceField != null)
            {
                try
                {
                    return this.auraInteractSystemInstanceField.GetValue(null);
                }
                catch (Exception ex2)
                {
                    this.auraLastError = "InteractSystem.Instance field lookup failed: " + ex2.Message;
                }
            }

            return null;
        }

        private unsafe bool InvokeAuraMonoPickBush(uint ownerNetId)
        {
            if (this.auraMonoSendPickBushMethodPtr == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return false;
            }
            if (!this.AttachAuraMonoThread())
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            uint id = ownerNetId;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&id);
            auraMonoRuntimeInvoke(this.auraMonoSendPickBushMethodPtr, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                this.auraLastError = "mono SendPickBushCommand exception";
                return false;
            }

            return true;
        }

        private unsafe bool InvokeAuraMonoAttackTree(uint ownerNetId, bool isCombo)
        {
            if (this.auraMonoSendAttackTreeMethodPtr == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return false;
            }
            if (!this.AttachAuraMonoThread())
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            uint id = ownerNetId;
            byte combo = isCombo ? (byte)1 : (byte)0;
            IntPtr* args = stackalloc IntPtr[2];
            args[0] = (IntPtr)(&id);
            args[1] = (IntPtr)(&combo);
            auraMonoRuntimeInvoke(this.auraMonoSendAttackTreeMethodPtr, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                this.auraLastError = "mono SendAttackTreeCommand exception";
                return false;
            }

            return true;
        }

        private unsafe bool InvokeAuraMonoHitStone(uint ownerNetId, bool isCombo)
        {
            if (this.auraMonoSendHitStoneMethodPtr == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return false;
            }
            if (!this.AttachAuraMonoThread())
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            uint id = ownerNetId;
            byte combo = isCombo ? (byte)1 : (byte)0;
            IntPtr* args = stackalloc IntPtr[2];
            args[0] = (IntPtr)(&id);
            args[1] = (IntPtr)(&combo);
            auraMonoRuntimeInvoke(this.auraMonoSendHitStoneMethodPtr, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                this.auraLastError = "mono SendHitStoneCommand exception";
                return false;
            }

            return true;
        }

        private AuraTargetKind GetAuraTargetKind(uint ownerNetId)
        {
            object entity = this.TryGetAuraOwnerEntity(ownerNetId);
            if (entity == null)
            {
                return AuraTargetKind.Unknown;
            }

            object collectableObject = this.TryGetAuraEntityComponent(entity, this.auraCollectableObjectComponentType);
            if (collectableObject != null)
            {
                AuraTargetKind kindFromComponent = this.GetAuraTargetKindFromCollectableObject(collectableObject);
                if (kindFromComponent != AuraTargetKind.Unknown)
                {
                    return kindFromComponent;
                }
            }

            if (this.TryGetAuraEntityComponent(entity, this.auraCollectableBushComponentType) != null
                || this.TryGetAuraEntityComponent(entity, this.auraDynamicBushComponentType) != null)
            {
                return AuraTargetKind.Bush;
            }

            if (this.TryGetAuraEntityPosition(entity, out Vector3 entityPosition))
            {
                AuraTargetKind kindFromPosition = this.GetAuraTargetKindFromPosition(entityPosition);
                if (kindFromPosition != AuraTargetKind.Unknown)
                {
                    return kindFromPosition;
                }
            }

            return AuraTargetKind.Unknown;
        }

        private AuraTargetKind GetAuraTargetKindFromCollectableObject(object collectableObject)
        {
            if (collectableObject == null)
            {
                return AuraTargetKind.Unknown;
            }

            try
            {
                object rawResType = null;
                if (this.auraCollectableObjectResTypeField != null)
                {
                    rawResType = this.auraCollectableObjectResTypeField.GetValue(collectableObject);
                }
                else if (this.auraCollectableObjectResTypeProperty != null)
                {
                    rawResType = this.auraCollectableObjectResTypeProperty.GetValue(collectableObject, null);
                }

                string resTypeName = rawResType != null ? rawResType.ToString() ?? string.Empty : string.Empty;
                if (resTypeName.IndexOf("Tree", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return AuraTargetKind.Tree;
                }

                if (resTypeName.IndexOf("Bush", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return AuraTargetKind.Bush;
                }

                if (resTypeName.IndexOf("Stone", StringComparison.OrdinalIgnoreCase) >= 0
                    || resTypeName.IndexOf("Meteroite", StringComparison.OrdinalIgnoreCase) >= 0
                    || resTypeName.IndexOf("Meteor", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return AuraTargetKind.Stone;
                }
            }
            catch
            {
            }

            return AuraTargetKind.Unknown;
        }

        private AuraTargetKind GetAuraTargetKindFromPosition(Vector3 position)
        {
            const float matchRadiusSqr = 25f;
            float bestBush = this.GetAuraClosestDistanceSqr(position, this.blueberryPositions, matchRadiusSqr);
            float bestRaspberry = this.GetAuraClosestDistanceSqr(position, this.raspberryPositions, matchRadiusSqr);
            float bestTree = this.GetAuraClosestDistanceSqr(position, TreePositions, matchRadiusSqr);
            float bestRareTree = this.GetAuraClosestDistanceSqr(position, RareTreePositions, matchRadiusSqr);
            float bestAppleTree = this.GetAuraClosestDistanceSqr(position, AppleTreePositions, matchRadiusSqr);
            float bestOrangeTree = this.GetAuraClosestDistanceSqr(position, OrangeTreePositions, matchRadiusSqr);
            float bestRock = this.GetAuraClosestDistanceSqr(position, RockPositions, matchRadiusSqr);
            float bestOre = this.GetAuraClosestDistanceSqr(position, OrePositions, matchRadiusSqr);

            float bestBushDistance = Math.Min(bestBush, bestRaspberry);
            float bestTreeDistance = Math.Min(Math.Min(bestTree, bestRareTree), Math.Min(bestAppleTree, bestOrangeTree));
            float bestStoneDistance = Math.Min(bestRock, bestOre);
            float bestDistance = Math.Min(bestBushDistance, Math.Min(bestTreeDistance, bestStoneDistance));

            if (bestDistance >= matchRadiusSqr)
            {
                return AuraTargetKind.Unknown;
            }

            if (bestDistance == bestBushDistance)
            {
                return AuraTargetKind.Bush;
            }

            if (bestDistance == bestTreeDistance)
            {
                return AuraTargetKind.Tree;
            }

            return AuraTargetKind.Stone;
        }

        private float GetAuraClosestDistanceSqr(Vector3 position, Vector3[] candidates, float defaultValue)
        {
            if (candidates == null || candidates.Length == 0)
            {
                return defaultValue;
            }

            float best = defaultValue;
            for (int i = 0; i < candidates.Length; i++)
            {
                float sqr = (candidates[i] - position).sqrMagnitude;
                if (sqr < best)
                {
                    best = sqr;
                }
            }

            return best;
        }

        private object TryGetAuraOwnerEntity(uint ownerNetId)
        {
            if (ownerNetId == 0U || this.auraEntityUtilGetEntityMethod == null)
            {
                return null;
            }

            try
            {
                return this.auraEntityUtilGetEntityMethod.Invoke(null, new object[] { ownerNetId });
            }
            catch
            {
                return null;
            }
        }

        private AuraTargetInfo GetAuraTargetInfo(uint ownerNetId)
        {
            if (ownerNetId == 0U)
            {
                return null;
            }

            this.auraTargetInfoByOwnerId.TryGetValue(ownerNetId, out AuraTargetInfo info);
            return info;
        }

        private void RegisterAuraOwnerOnlyTarget(HashSet<uint> output, uint ownerNetId, string source)
        {
            this.RegisterAuraOwnerOnlyTarget(output, ownerNetId, source, false, Vector3.zero);
        }

        private void RegisterAuraOwnerOnlyTarget(HashSet<uint> output, uint ownerNetId, string source, bool hasPosition, Vector3 position)
        {
            if (ownerNetId == 0U)
            {
                return;
            }

            AuraTargetInfo info = this.GetAuraTargetInfo(ownerNetId) ?? new AuraTargetInfo();
            info.OwnerNetId = ownerNetId;
            if (info.ResourceNetId == 0U)
            {
                info.ResourceNetId = ownerNetId;
            }
            if (info.Kind == AuraTargetKind.Unknown)
            {
                info.Kind = this.GetAuraTargetKind(ownerNetId);
            }
            if (hasPosition)
            {
                info.Position = position;
                info.HasPosition = true;
                if (info.Kind == AuraTargetKind.Unknown)
                {
                    info.Kind = this.GetAuraTargetKindFromPosition(position);
                }
            }
            if (!string.IsNullOrEmpty(source))
            {
                info.Source = source;
            }

            this.auraTargetInfoByOwnerId[ownerNetId] = info;
            this.auraLastDetectedResourceNetId = info.ResourceNetId;
            this.auraLastDetectedOwnerNetId = info.OwnerNetId;
            this.auraLastDetectedTargetNetId = info.TargetNetId;
            output.Add(ownerNetId);

            if (AuraFarmDebugLogs)
            {
                this.LogAuraTargetDetail(source, info);
            }
        }

        private void RegisterAuraTargetFromLevelObjectId(HashSet<uint> output, ulong levelObjectId, string source)
        {
            this.RegisterAuraTargetFromLevelObjectId(output, levelObjectId, source, false, Vector3.zero);
        }

        private void RegisterAuraTargetFromLevelObjectId(HashSet<uint> output, ulong levelObjectId, string source, bool hasPosition, Vector3 position)
        {
            if (levelObjectId == 0UL)
            {
                return;
            }

            if (this.TryResolveAuraTargetInfoFromLevelObjectId(levelObjectId, out AuraTargetInfo info))
            {
                if (hasPosition)
                {
                    info.Position = position;
                    info.HasPosition = true;
                    if (info.Kind == AuraTargetKind.Unknown)
                    {
                        info.Kind = this.GetAuraTargetKindFromPosition(position);
                    }
                }
                this.auraTargetInfoByOwnerId[info.OwnerNetId] = info;
                this.auraLastDetectedResourceNetId = info.ResourceNetId;
                this.auraLastDetectedOwnerNetId = info.OwnerNetId;
                this.auraLastDetectedTargetNetId = info.TargetNetId;
                output.Add(info.OwnerNetId);

                if (AuraFarmDebugLogs)
                {
                    this.LogAuraTargetDetail(source, info);
                }

                return;
            }

            if (this.TryResolveOwnerIdFromLevelObjectId(levelObjectId, out uint ownerId) || this.TryResolveOwnerIdFromLevelObjectIdMono(levelObjectId, out ownerId))
            {
                this.RegisterAuraOwnerOnlyTarget(output, ownerId, source + ":ownerOnly", hasPosition, position);
                AuraTargetInfo ownerInfo = this.GetAuraTargetInfo(ownerId);
                if (ownerInfo != null && ownerInfo.TargetNetId == 0UL)
                {
                    ownerInfo.TargetNetId = levelObjectId;
                }
                return;
            }

            if (levelObjectId <= uint.MaxValue)
            {
                this.RegisterAuraOwnerOnlyTarget(output, (uint)levelObjectId, source + ":rawFallback", hasPosition, position);
            }
        }

        private void LogAuraTargetDetail(string source, AuraTargetInfo info)
        {
            if (!AuraFarmDebugLogs || info == null)
            {
                return;
            }

            string key = (source ?? string.Empty)
                + "|" + info.ResourceNetId
                + "|" + info.TargetNetId
                + "|" + info.OwnerNetId
                + "|" + info.LevelResourceId
                + "|" + info.Kind
                + "|" + (info.HasPosition ? info.Position.ToString() : string.Empty);

            float now = Time.unscaledTime;
            if (string.Equals(this.auraLastLoggedTargetDetailKey, key, StringComparison.Ordinal)
                && now - this.auraLastTargetDetailLogAt < 1f)
            {
                return;
            }

            this.auraLastLoggedTargetDetailKey = key;
            this.auraLastTargetDetailLogAt = now;

            ModLogger.Msg("[AuraFarm] Target " + source
                + " resourceNetId=" + info.ResourceNetId
                + " targetNetId=" + info.TargetNetId
                + " ownerNetId=" + info.OwnerNetId
                + " levelResourceId=" + info.LevelResourceId
                + (info.HasPosition ? " pos=" + info.Position : string.Empty)
                + " kind=" + info.Kind);
        }

        private bool TryResolveAuraTargetInfoFromLevelObjectId(ulong levelObjectId, out AuraTargetInfo info)
        {
            info = null;
            object levelObject = this.TryGetAuraLevelObject(levelObjectId);
            if (levelObject == null)
            {
                return false;
            }

            ulong targetNetId = levelObjectId;
            if (this.TryGetAuraLevelObjectNetId(levelObject, out ulong actualTargetNetId) && actualTargetNetId != 0UL)
            {
                targetNetId = actualTargetNetId;
            }

            if (!this.TryGetAuraLevelObjectOwnerNetId(levelObject, out uint ownerNetId) || ownerNetId == 0U)
            {
                ownerNetId = this.TryResolveOwnerIdFromLevelObjectId(levelObjectId, out uint resolvedOwnerId) ? resolvedOwnerId : 0U;
            }

            if (ownerNetId == 0U)
            {
                return false;
            }

            uint levelResourceId = 0U;
            this.TryGetAuraLevelObjectResourceId(levelObject, out levelResourceId);

            info = new AuraTargetInfo
            {
                OwnerNetId = ownerNetId,
                ResourceNetId = ownerNetId,
                TargetNetId = targetNetId,
                LevelResourceId = levelResourceId,
                Kind = this.GetAuraTargetKind(ownerNetId)
            };

            return true;
        }

        private object TryGetAuraLevelObject(ulong levelObjectId)
        {
            if (levelObjectId == 0UL)
            {
                return null;
            }

            MethodInfo method = this.auraEntityUtilGetLevelObjectMethod ?? this.auraEntityHelperGetLevelObjectMethod;
            if (method == null)
            {
                return null;
            }

            try
            {
                return method.Invoke(null, new object[] { levelObjectId });
            }
            catch
            {
                return null;
            }
        }

        private bool TryGetAuraLevelObjectNetId(object levelObject, out ulong targetNetId)
        {
            targetNetId = 0UL;
            if (levelObject == null)
            {
                return false;
            }

            try
            {
                object raw = null;
                if (this.auraLevelObjectNetIdField != null)
                {
                    raw = this.auraLevelObjectNetIdField.GetValue(levelObject);
                }
                else if (this.auraLevelObjectNetIdProperty != null)
                {
                    raw = this.auraLevelObjectNetIdProperty.GetValue(levelObject, null);
                }

                return this.TryConvertAuraLevelObjectId(raw, out targetNetId);
            }
            catch
            {
                return false;
            }
        }

        private bool TryGetAuraLevelObjectOwnerNetId(object levelObject, out uint ownerNetId)
        {
            ownerNetId = 0U;
            if (levelObject == null)
            {
                return false;
            }

            try
            {
                object raw = null;
                if (this.auraLevelObjectOwnerNetIdField != null)
                {
                    raw = this.auraLevelObjectOwnerNetIdField.GetValue(levelObject);
                }
                else if (this.auraLevelObjectOwnerNetIdProperty != null)
                {
                    raw = this.auraLevelObjectOwnerNetIdProperty.GetValue(levelObject, null);
                }

                return this.TryConvertAuraOwnerId(raw, out ownerNetId);
            }
            catch
            {
                return false;
            }
        }

        private bool TryGetAuraLevelObjectResourceId(object levelObject, out uint resourceId)
        {
            resourceId = 0U;
            if (levelObject == null)
            {
                return false;
            }

            try
            {
                object raw = null;
                if (this.auraLevelObjectResourceIdField != null)
                {
                    raw = this.auraLevelObjectResourceIdField.GetValue(levelObject);
                }
                else if (this.auraLevelObjectResourceIdProperty != null)
                {
                    raw = this.auraLevelObjectResourceIdProperty.GetValue(levelObject, null);
                }

                return this.TryConvertAuraOwnerId(raw, out resourceId);
            }
            catch
            {
                return false;
            }
        }

        private void TryCollectAuraOwnerTargetsViaSphereQuery(HashSet<uint> output)
        {
            if (this.auraEntityUtilGetSelfPlayerEntityMethod == null || this.auraEntitiesSphereQueryEntitiesMethod == null)
            {
                if (AuraFarmDebugLogs)
                {
                    ModLogger.Msg("[AuraFarm] SphereQuery skipped: selfPlayerMethod="
                        + (this.auraEntityUtilGetSelfPlayerEntityMethod != null)
                        + " sphereQueryMethod=" + (this.auraEntitiesSphereQueryEntitiesMethod != null));
                }
                return;
            }

            object selfEntity = null;
            try
            {
                selfEntity = this.auraEntityUtilGetSelfPlayerEntityMethod.Invoke(null, null);
            }
            catch (Exception ex)
            {
                if (AuraFarmDebugLogs)
                {
                    ModLogger.Msg("[AuraFarm] SphereQuery self entity lookup failed: " + ex.Message);
                }
                return;
            }

            if (selfEntity == null)
            {
                if (AuraFarmDebugLogs)
                {
                    ModLogger.Msg("[AuraFarm] SphereQuery self entity lookup returned null.");
                }
                return;
            }

            if (!this.TryGetAuraEntityPosition(selfEntity, out Vector3 center))
            {
                if (AuraFarmDebugLogs)
                {
                    ModLogger.Msg("[AuraFarm] SphereQuery self entity position lookup failed.");
                }
                return;
            }

            try
            {
                Type entityType = selfEntity.GetType();
                Type listType = typeof(List<>).MakeGenericType(entityType);
                object results = Activator.CreateInstance(listType);
                if (results == null)
                {
                    return;
                }

                object rawCount = this.auraEntitiesSphereQueryEntitiesMethod.Invoke(null, new object[] { center, AuraDirectScanRadius, results });
                int count = 0;
                if (rawCount is int)
                {
                    count = (int)rawCount;
                }

                if (AuraFarmDebugLogs)
                {
                    ModLogger.Msg("[AuraFarm] SphereQueryEntities count=" + count);
                }

                IEnumerable enumerable = results as IEnumerable;
                if (enumerable == null)
                {
                    return;
                }

                foreach (object entity in enumerable)
                {
                    if (entity == null)
                    {
                        continue;
                    }

                    uint ownerId;
                    if (!this.TryGetAuraEntityNetId(entity, out ownerId) || ownerId == 0U)
                    {
                        continue;
                    }

                    AuraTargetKind kind = this.GetAuraTargetKind(ownerId);
                    if (kind == AuraTargetKind.Unknown)
                    {
                        continue;
                    }

                    this.RegisterAuraOwnerOnlyTarget(output, ownerId, "SphereQuery");
                }

                if (output.Count > 0 && AuraFarmDebugLogs)
                {
                    ModLogger.Msg("[AuraFarm] SphereQuery path produced " + output.Count + " targets.");
                }
            }
            catch (Exception ex)
            {
                if (AuraFarmDebugLogs)
                {
                    ModLogger.Msg("[AuraFarm] SphereQueryEntities failed: " + ex.Message);
                }
            }
        }

        private object GetAuraInteractPlayer(object interactSystem)
        {
            if (interactSystem == null)
            {
                return null;
            }

            if (this.auraInteractSystemPlayerProperty != null)
            {
                try
                {
                    object player = this.auraInteractSystemPlayerProperty.GetValue(interactSystem, null);
                    if (player != null)
                    {
                        return player;
                    }
                }
                catch
                {
                }
            }

            if (this.auraInteractSystemGetPlayerMethod != null)
            {
                try
                {
                    return this.auraInteractSystemGetPlayerMethod.Invoke(interactSystem, null);
                }
                catch
                {
                }
            }

            return null;
        }

        private object GetAuraAxeCheckerManagedInstance()
        {
            if (this.auraAxeCheckerInstanceProperty != null)
            {
                try
                {
                    object value = this.auraAxeCheckerInstanceProperty.GetValue(null, null);
                    if (value != null)
                    {
                        return value;
                    }
                }
                catch
                {
                }
            }

            if (this.auraAxeCheckerInstanceField != null)
            {
                try
                {
                    object value = this.auraAxeCheckerInstanceField.GetValue(null);
                    if (value != null)
                    {
                        return value;
                    }
                }
                catch
                {
                }
            }

            if (this.auraAxeCheckerType != null)
            {
                try
                {
                    return Activator.CreateInstance(this.auraAxeCheckerType);
                }
                catch
                {
                }
            }

            return null;
        }

        private void TryCollectAuraOwnerTargetsViaAxeChecker(object interactSystem, HashSet<uint> output)
        {
            if (interactSystem == null || this.auraAxeCheckerPhysicalSelectMethod == null || (this.auraSelectPriorityInfoShapeField == null && this.auraSelectPriorityInfoShapeProperty == null))
            {
                if (AuraFarmDebugLogs)
                {
                    ModLogger.Msg("[AuraFarm] Managed AxeChecker skipped: interactSystem="
                        + (interactSystem != null)
                        + " physicalSelect=" + (this.auraAxeCheckerPhysicalSelectMethod != null)
                        + " shapeMember=" + (this.auraSelectPriorityInfoShapeField != null || this.auraSelectPriorityInfoShapeProperty != null));
                }
                return;
            }

            object checkerObj = this.GetAuraAxeCheckerManagedInstance();
            object playerObj = this.GetAuraInteractPlayer(interactSystem);
            if (checkerObj == null || playerObj == null)
            {
                if (AuraFarmDebugLogs)
                {
                    ModLogger.Msg("[AuraFarm] Managed AxeChecker skipped: checker=" + (checkerObj != null) + " player=" + (playerObj != null));
                }
                return;
            }

            try
            {
                ParameterInfo[] parameters = this.auraAxeCheckerPhysicalSelectMethod.GetParameters();
                if (parameters == null || parameters.Length != 3)
                {
                    return;
                }

                object selectListObj = Activator.CreateInstance(parameters[2].ParameterType);
                if (selectListObj == null)
                {
                    return;
                }

                object rawCount = this.auraAxeCheckerPhysicalSelectMethod.Invoke(checkerObj, new object[] { playerObj, null, selectListObj });
                int count = rawCount is int intCount ? intCount : 0;

                if (AuraFarmDebugLogs)
                {
                    ModLogger.Msg("[AuraFarm] Managed AxeChecker PhysicalSelect count=" + count);
                }

                if (count <= 0)
                {
                    return;
                }

                if (selectListObj is IEnumerable enumerable)
                {
                    int index = 0;
                    foreach (object infoObj in enumerable)
                    {
                        if (infoObj == null)
                        {
                            index++;
                            if (index >= count)
                            {
                                break;
                            }
                            continue;
                        }

                        object shapeObj = null;
                        if (this.auraSelectPriorityInfoShapeField != null)
                        {
                            shapeObj = this.auraSelectPriorityInfoShapeField.GetValue(infoObj);
                        }
                        else if (this.auraSelectPriorityInfoShapeProperty != null)
                        {
                            shapeObj = this.auraSelectPriorityInfoShapeProperty.GetValue(infoObj, null);
                        }

                        if (shapeObj != null)
                        {
                            if (this.TryGetAuraLevelObjectNetId(shapeObj, out ulong levelObjectId) && levelObjectId != 0UL)
                            {
                                this.RegisterAuraTargetFromLevelObjectId(output, levelObjectId, "ManagedAxeChecker[" + index + "]");
                            }
                            else if (this.TryGetAuraLevelObjectOwnerNetId(shapeObj, out uint ownerNetId) && ownerNetId != 0U)
                            {
                                this.RegisterAuraOwnerOnlyTarget(output, ownerNetId, "ManagedAxeChecker[" + index + "]:owner");
                            }
                        }

                        index++;
                        if (index >= count || output.Count >= AuraMergedTargetSoftCap)
                        {
                            break;
                        }
                    }
                }

                if (AuraFarmDebugLogs && output.Count > 0)
                {
                    ModLogger.Msg("[AuraFarm] Managed AxeChecker path produced " + output.Count + " targets.");
                }
            }
            catch (Exception ex)
            {
                this.auraLastError = "Managed AxeChecker failed: " + ex.Message;
                if (AuraUseMonoTargetFallbacks)
                {
                    this.TryCollectAuraOwnerTargetsViaMonoAxeChecker(interactSystem, output);
                }
            }
        }

        private void TryCollectAuraOwnerTargetsViaMonoAxeChecker(object interactSystem, HashSet<uint> output)
        {
            IntPtr interactObj = this.GetAuraMonoInteractSystemInstance();
            if (interactObj == IntPtr.Zero || this.auraMonoAxeCheckerClassPtr == IntPtr.Zero || this.auraMonoAxeCheckerInstanceFieldPtr == IntPtr.Zero || this.auraMonoAxeCheckerPhysicalSelectMethodPtr == IntPtr.Zero || this.auraMonoSelectPriorityInfoClassPtr == IntPtr.Zero)
            {
                if (AuraFarmDebugLogs)
                {
                    ModLogger.Msg("[AuraFarm] AxeChecker skipped: interactSystem="
                        + (interactObj != IntPtr.Zero)
                        + " axeCheckerType=" + (this.auraMonoAxeCheckerClassPtr != IntPtr.Zero)
                        + " physicalSelect=" + (this.auraMonoAxeCheckerPhysicalSelectMethodPtr != IntPtr.Zero)
                        + " selectPriorityInfo=" + (this.auraMonoSelectPriorityInfoClassPtr != IntPtr.Zero));
                }
                return;
            }

            if (!this.AttachAuraMonoThread() || auraMonoClassVtable == null || auraMonoFieldStaticGetValue == null || auraMonoRuntimeInvoke == null)
            {
                if (AuraFarmDebugLogs)
                {
                    ModLogger.Msg("[AuraFarm] AxeChecker skipped: mono thread/vtable/api unavailable.");
                }
                return;
            }

            IntPtr axeCheckerVtable = auraMonoClassVtable(this.auraMonoRootDomain, this.auraMonoAxeCheckerClassPtr);
            if (axeCheckerVtable == IntPtr.Zero)
            {
                if (AuraFarmDebugLogs)
                {
                    ModLogger.Msg("[AuraFarm] AxeChecker skipped: class vtable null.");
                }
                return;
            }

            IntPtr checkerObj = IntPtr.Zero;
            try
            {
                unsafe
                {
                    auraMonoFieldStaticGetValue(axeCheckerVtable, this.auraMonoAxeCheckerInstanceFieldPtr, (IntPtr)(&checkerObj));
                }
            }
            catch
            {
            }

            if (checkerObj == IntPtr.Zero)
            {
                if (auraMonoObjectNew != null)
                {
                    try
                    {
                        checkerObj = auraMonoObjectNew(this.auraMonoRootDomain, this.auraMonoAxeCheckerClassPtr);
                        if (checkerObj != IntPtr.Zero && auraMonoRuntimeObjectInit != null)
                        {
                            auraMonoRuntimeObjectInit(checkerObj);
                        }
                    }
                    catch
                    {
                        checkerObj = IntPtr.Zero;
                    }
                }

                if (checkerObj == IntPtr.Zero)
                {
                    if (AuraFarmDebugLogs)
                    {
                        ModLogger.Msg("[AuraFarm] AxeChecker skipped: instance null.");
                    }
                    return;
                }
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr playerObj = auraMonoRuntimeInvoke(this.auraMonoInteractGetPlayerMethodPtr, interactObj, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || playerObj == IntPtr.Zero)
            {
                if (AuraFarmDebugLogs)
                {
                    ModLogger.Msg("[AuraFarm] AxeChecker skipped: player lookup failed.");
                }
                return;
            }

            IntPtr selectListObj = this.GetAuraMonoSelectPriorityInfoListObject();
            if (selectListObj == IntPtr.Zero)
            {
                if (AuraFarmDebugLogs)
                {
                    ModLogger.Msg("[AuraFarm] AxeChecker skipped: SelectPriorityInfo list unavailable.");
                }
                return;
            }

            unsafe
            {
                IntPtr* args = stackalloc IntPtr[3];
                args[0] = playerObj;
                args[1] = IntPtr.Zero;
                args[2] = selectListObj;
                exc = IntPtr.Zero;
                auraMonoRuntimeInvoke(this.auraMonoAxeCheckerPhysicalSelectMethodPtr, checkerObj, (IntPtr)args, ref exc);
            }

            if (exc != IntPtr.Zero)
            {
                if (AuraFarmDebugLogs)
                {
                    ModLogger.Msg("[AuraFarm] AxeChecker failed: PhysicalSelect threw.");
                }
                return;
            }

            int count = this.GetAuraMonoSelectPriorityListCount(selectListObj);
            float now = Time.unscaledTime;
            bool axeSummaryChanged = count != this.auraLastLoggedAxeCheckerPhysicalCount;
            if (AuraFarmDebugLogs && (axeSummaryChanged || now - this.auraLastAxeCheckerLogAt >= 1f))
            {
                this.auraLastAxeCheckerLogAt = now;
                this.auraLastLoggedAxeCheckerPhysicalCount = count;
                ModLogger.Msg("[AuraFarm] AxeChecker PhysicalSelect count=" + count);
            }

            for (int i = 0; i < count; i++)
            {
                IntPtr infoObj = this.GetAuraMonoSelectPriorityListItem(selectListObj, i);
                if (infoObj == IntPtr.Zero || this.auraMonoSelectPriorityInfoShapeFieldPtr == IntPtr.Zero)
                {
                    continue;
                }

                IntPtr shapeObj = IntPtr.Zero;
                unsafe
                {
                    auraMonoFieldGetValue(infoObj, this.auraMonoSelectPriorityInfoShapeFieldPtr, (IntPtr)(&shapeObj));
                }

                if (shapeObj == IntPtr.Zero)
                {
                    continue;
                }

                if (this.auraMonoDynamicLevelObjectGetUniqueIdMethodPtr == IntPtr.Zero && auraMonoObjectGetClass != null && auraMonoClassGetMethodFromName != null)
                {
                    IntPtr shapeClass = auraMonoObjectGetClass(shapeObj);
                    if (shapeClass != IntPtr.Zero)
                    {
                        this.auraMonoDynamicLevelObjectGetUniqueIdMethodPtr = auraMonoClassGetMethodFromName(shapeClass, "GetUniqueId", 0);
                    }
                }

                if (this.auraMonoDynamicLevelObjectGetUniqueIdMethodPtr == IntPtr.Zero)
                {
                    continue;
                }

                exc = IntPtr.Zero;
                IntPtr boxedId = auraMonoRuntimeInvoke(this.auraMonoDynamicLevelObjectGetUniqueIdMethodPtr, shapeObj, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero || boxedId == IntPtr.Zero)
                {
                    continue;
                }

                ulong levelObjectId = this.TryReadMonoUnsignedIntegral(boxedId);
                if (levelObjectId != 0UL)
                {
                    Vector3 shapePosition;
                    bool hasShapePosition = this.TryGetAuraMonoObjectPosition(shapeObj, out shapePosition);
                    this.RegisterAuraTargetFromLevelObjectId(output, levelObjectId, "AxeChecker", hasShapePosition, shapePosition);
                }
            }

            if (AuraFarmDebugLogs && output.Count > 0)
            {
                if (output.Count != this.auraLastLoggedAxeCheckerTargetCount || now - this.auraLastAxeCheckerLogAt >= 1f)
                {
                    this.auraLastAxeCheckerLogAt = now;
                    this.auraLastLoggedAxeCheckerTargetCount = output.Count;
                    ModLogger.Msg("[AuraFarm] AxeChecker path produced " + output.Count + " targets.");
                }
            }
        }

        private unsafe bool TryGetAuraMonoObjectPosition(IntPtr obj, out Vector3 position)
        {
            position = Vector3.zero;
            if (obj == IntPtr.Zero)
            {
                return false;
            }

            string[] vectorMemberNames = new string[]
            {
                "position",
                "Position",
                "pos",
                "Pos",
                "_position",
                "_pos",
                "center",
                "Center"
            };

            for (int i = 0; i < vectorMemberNames.Length; i++)
            {
                if (this.TryGetMonoVector3Member(obj, vectorMemberNames[i], out position) && position != Vector3.zero)
                {
                    return true;
                }
            }

            position = this.TryReadAuraMonoVector3Field(obj, vectorMemberNames);
            if (position != Vector3.zero)
            {
                return true;
            }

            string[] boundsMemberNames = new string[]
            {
                "bounds",
                "Bounds",
                "worldBounds",
                "WorldBounds"
            };

            for (int i = 0; i < boundsMemberNames.Length; i++)
            {
                if (this.TryGetMonoBoundsCenterMember(obj, boundsMemberNames[i], out position) && position != Vector3.zero)
                {
                    return true;
                }
            }

            return false;
        }

        private void TryCollectAuraOwnerTargetsViaCylinderScan(object interactSystem, HashSet<uint> output)
        {
            if (this.auraLevelObjectManagerType == null || this.auraLevelObjectManagerCylinderOverlapNonAllocMethod == null || this.auraEntityUtilGetSelfPlayerEntityMethod == null || this.auraLevelObjectTagType == null || this.auraCylinderType == null)
            {
                if (AuraFarmDebugLogs)
                {
                    ModLogger.Msg("[AuraFarm] Cylinder scan skipped: levelObjectManagerType="
                        + (this.auraLevelObjectManagerType != null)
                        + " cylinderMethod=" + (this.auraLevelObjectManagerCylinderOverlapNonAllocMethod != null)
                        + " selfPlayerMethod=" + (this.auraEntityUtilGetSelfPlayerEntityMethod != null)
                        + " levelObjectTagType=" + (this.auraLevelObjectTagType != null)
                        + " cylinderType=" + (this.auraCylinderType != null));
                }
                return;
            }

            object levelObjectManager = this.GetAuraLevelObjectManagerInstance();
            if (levelObjectManager == null)
            {
                if (AuraFarmDebugLogs)
                {
                    ModLogger.Msg("[AuraFarm] Cylinder scan skipped: LevelObjectManager.Instance null.");
                }
                return;
            }

            object selfEntity = null;
            try
            {
                selfEntity = this.auraEntityUtilGetSelfPlayerEntityMethod.Invoke(null, null);
            }
            catch (Exception ex)
            {
                if (AuraFarmDebugLogs)
                {
                    ModLogger.Msg("[AuraFarm] Cylinder scan self entity lookup failed: " + ex.Message);
                }
                return;
            }

            if (selfEntity == null || !this.TryGetAuraEntityPosition(selfEntity, out Vector3 position))
            {
                if (AuraFarmDebugLogs)
                {
                    ModLogger.Msg("[AuraFarm] Cylinder scan missing self entity position.");
                }
                return;
            }

            Quaternion rotation = Quaternion.identity;
            this.TryGetAuraEntityRotation(selfEntity, out rotation);

            Vector3 interactCenter = Vector3.zero;
            float radius = AuraDirectScanRadius;
            float height = 3f;
            this.TryGetAuraInteractCylinder(interactSystem, out interactCenter, out radius, out height);

            try
            {
                object cylinder = Activator.CreateInstance(this.auraCylinderType);
                if (cylinder == null)
                {
                    return;
                }

                float halfHeight = height * 0.5f;
                Vector3 center = position + rotation * interactCenter + halfHeight * Vector3.up;
                this.SetAuraCylinderValue(cylinder, center, radius, height);

                Type levelObjectType = this.auraEntityUtilGetLevelObjectMethod != null ? this.auraEntityUtilGetLevelObjectMethod.ReturnType : null;
                if (levelObjectType == null && this.auraEntityHelperGetLevelObjectMethod != null)
                {
                    levelObjectType = this.auraEntityHelperGetLevelObjectMethod.ReturnType;
                }
                if (levelObjectType == null && this.auraLevelObjectNetIdField != null)
                {
                    levelObjectType = this.auraLevelObjectNetIdField.DeclaringType;
                }
                if (levelObjectType == null && this.auraLevelObjectNetIdProperty != null)
                {
                    levelObjectType = this.auraLevelObjectNetIdProperty.DeclaringType;
                }
                if (levelObjectType == null)
                {
                    if (AuraFarmDebugLogs)
                    {
                        ModLogger.Msg("[AuraFarm] Cylinder scan missing LevelObject type.");
                    }
                    return;
                }

                object results = Activator.CreateInstance(typeof(List<>).MakeGenericType(levelObjectType));
                if (results == null)
                {
                    return;
                }

                object interactableTag = Enum.Parse(this.auraLevelObjectTagType, "Interactable");
                LayerMask layerMask = int.MaxValue;
                object rawCount = this.auraLevelObjectManagerCylinderOverlapNonAllocMethod.Invoke(levelObjectManager, new object[] { cylinder, results, layerMask, interactableTag, -1 });
                int count = rawCount is int ? (int)rawCount : 0;
                if (AuraFarmDebugLogs)
                {
                    ModLogger.Msg("[AuraFarm] CylinderOverlapNonAlloc count=" + count);
                }

                if (results is IEnumerable enumerable)
                {
                    foreach (object levelObject in enumerable)
                    {
                        if (levelObject == null)
                        {
                            continue;
                        }

                        ulong levelObjectId;
                        if (!this.TryGetAuraLevelObjectNetId(levelObject, out levelObjectId) || levelObjectId == 0UL)
                        {
                            continue;
                        }

                        this.RegisterAuraTargetFromLevelObjectId(output, levelObjectId, "CylinderScan");
                    }
                }
            }
            catch (Exception ex)
            {
                if (AuraFarmDebugLogs)
                {
                    ModLogger.Msg("[AuraFarm] Cylinder scan failed: " + ex.Message);
                }
            }
        }

        private object GetAuraLevelObjectManagerInstance()
        {
            if (this.auraLevelObjectManagerInstanceProperty == null)
            {
                return null;
            }

            try
            {
                return this.auraLevelObjectManagerInstanceProperty.GetValue(null, null);
            }
            catch
            {
                return null;
            }
        }

        private bool TryGetAuraEntityRotation(object entity, out Quaternion rotation)
        {
            rotation = Quaternion.identity;
            if (entity == null)
            {
                return false;
            }

            try
            {
                object raw = null;
                if (this.auraEntityRotationField != null)
                {
                    raw = this.auraEntityRotationField.GetValue(entity);
                }
                else if (this.auraEntityRotationProperty != null)
                {
                    raw = this.auraEntityRotationProperty.GetValue(entity, null);
                }

                if (raw is Quaternion q)
                {
                    rotation = q;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private void TryGetAuraInteractCylinder(object interactSystem, out Vector3 center, out float radius, out float height)
        {
            center = Vector3.zero;
            radius = AuraDirectScanRadius;
            height = 3f;
            if (interactSystem == null)
            {
                return;
            }

            try
            {
                object raw = null;
                if (this.auraInteractSystemInteractCylinderField != null)
                {
                    raw = this.auraInteractSystemInteractCylinderField.GetValue(interactSystem);
                }
                else if (this.auraInteractSystemInteractCylinderProperty != null)
                {
                    raw = this.auraInteractSystemInteractCylinderProperty.GetValue(interactSystem, null);
                }

                if (raw == null)
                {
                    return;
                }

                Type t = raw.GetType();
                object centerRaw = this.GetFieldQuiet(t, "center")?.GetValue(raw) ?? this.GetPropertyQuiet(t, "center")?.GetValue(raw, null) ?? this.GetFieldQuiet(t, "Center")?.GetValue(raw) ?? this.GetPropertyQuiet(t, "Center")?.GetValue(raw, null);
                object radiusRaw = this.GetFieldQuiet(t, "radius")?.GetValue(raw) ?? this.GetPropertyQuiet(t, "radius")?.GetValue(raw, null) ?? this.GetFieldQuiet(t, "Radius")?.GetValue(raw) ?? this.GetPropertyQuiet(t, "Radius")?.GetValue(raw, null);
                object heightRaw = this.GetFieldQuiet(t, "height")?.GetValue(raw) ?? this.GetPropertyQuiet(t, "height")?.GetValue(raw, null) ?? this.GetFieldQuiet(t, "Height")?.GetValue(raw) ?? this.GetPropertyQuiet(t, "Height")?.GetValue(raw, null);

                if (centerRaw is Vector3 v)
                {
                    center = v;
                }
                if (radiusRaw != null)
                {
                    radius = Convert.ToSingle(radiusRaw);
                }
                if (heightRaw != null)
                {
                    height = Convert.ToSingle(heightRaw);
                }
            }
            catch
            {
            }
        }

        private void SetAuraCylinderValue(object cylinder, Vector3 center, float radius, float height)
        {
            if (cylinder == null)
            {
                return;
            }

            if (this.auraCylinderCenterField != null)
            {
                this.auraCylinderCenterField.SetValue(cylinder, center);
            }
            else if (this.auraCylinderCenterProperty != null)
            {
                this.auraCylinderCenterProperty.SetValue(cylinder, center, null);
            }

            if (this.auraCylinderRadiusField != null)
            {
                this.auraCylinderRadiusField.SetValue(cylinder, radius);
            }
            else if (this.auraCylinderRadiusProperty != null)
            {
                this.auraCylinderRadiusProperty.SetValue(cylinder, radius, null);
            }

            if (this.auraCylinderHeightField != null)
            {
                this.auraCylinderHeightField.SetValue(cylinder, height);
            }
            else if (this.auraCylinderHeightProperty != null)
            {
                this.auraCylinderHeightProperty.SetValue(cylinder, height, null);
            }
        }

        private bool TryGetAuraEntityNetId(object entity, out uint ownerId)
        {
            ownerId = 0U;
            if (entity == null)
            {
                return false;
            }

            try
            {
                object raw = null;
                if (this.auraEntityNetIdField != null)
                {
                    raw = this.auraEntityNetIdField.GetValue(entity);
                }
                else if (this.auraEntityNetIdProperty != null)
                {
                    raw = this.auraEntityNetIdProperty.GetValue(entity, null);
                }

                return this.TryConvertAuraOwnerId(raw, out ownerId);
            }
            catch
            {
                return false;
            }
        }

        private bool TryGetAuraEntityPosition(object entity, out Vector3 position)
        {
            position = Vector3.zero;
            if (entity == null)
            {
                return false;
            }

            try
            {
                object raw = null;
                if (this.auraEntityPositionField != null)
                {
                    raw = this.auraEntityPositionField.GetValue(entity);
                }
                else if (this.auraEntityPositionProperty != null)
                {
                    raw = this.auraEntityPositionProperty.GetValue(entity, null);
                }

                if (raw is Vector3)
                {
                    position = (Vector3)raw;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private object TryGetAuraEntityComponent(object entity, Type componentType)
        {
            if (entity == null || componentType == null)
            {
                return null;
            }

            try
            {
                MethodInfo[] methods = entity.GetType().GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                for (int i = 0; i < methods.Length; i++)
                {
                    MethodInfo method = methods[i];
                    if (method == null || method.Name != "GetComponent" || !method.IsGenericMethodDefinition)
                    {
                        continue;
                    }

                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters.Length != 1 || parameters[0].ParameterType != typeof(bool))
                    {
                        continue;
                    }

                    return method.MakeGenericMethod(componentType).Invoke(entity, new object[] { true });
                }
            }
            catch
            {
            }

            return null;
        }


        private unsafe void TryCollectAuraOwnerTargetsViaMonoCurrentTarget(HashSet<uint> output)
        {
            if (auraMonoRuntimeInvoke == null || auraMonoFieldGetValue == null)
            {
                return;
            }
            if (!this.AttachAuraMonoThread())
            {
                return;
            }

            if (this.auraMonoInteractGetInstanceMethodPtr == IntPtr.Zero || this.auraMonoInteractCurrentTargetFieldPtr == IntPtr.Zero)
            {
                return;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr interactObj = auraMonoRuntimeInvoke(this.auraMonoInteractGetInstanceMethodPtr, IntPtr.Zero, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || interactObj == IntPtr.Zero)
            {
                return;
            }

            ulong currentTargetRaw = 0UL;
            auraMonoFieldGetValue(interactObj, this.auraMonoInteractCurrentTargetFieldPtr, (IntPtr)(&currentTargetRaw));
            uint currentTargetUInt = currentTargetRaw <= uint.MaxValue ? (uint)currentTargetRaw : 0U;
            if (AuraFarmDebugLogs)
            {
                ModLogger.Msg("[AuraFarm] Mono _currentSelectTarget(raw)=" + currentTargetRaw);
            }
            if (currentTargetUInt != 0U)
            {
                this.RegisterAuraOwnerOnlyTarget(output, currentTargetUInt, "MonoCurrentTarget");
                if (AuraFarmDebugLogs)
                {
                    ModLogger.Msg("[AuraFarm] Mono current target path -> owner " + currentTargetUInt);
                }
                return;
            }

            if (currentTargetRaw != 0UL)
            {
                this.RegisterAuraTargetFromLevelObjectId(output, currentTargetRaw, "MonoCurrentTargetLevelObject");
                if (output.Count > 0)
                {
                    return;
                }
            }

            if (output.Count == 0)
            {
                this.TryCollectAuraOwnerTargetsViaMonoFocusLevelObjects(interactObj, output);
                if (output.Count > 0 && AuraFarmDebugLogs)
                {
                    ModLogger.Msg("[AuraFarm] Mono _focusLevelObjects path produced " + output.Count + " targets.");
                }
            }
            if (output.Count == 0)
            {
                this.TryCollectAuraOwnerTargetsViaMonoSelectedMap(interactObj, output);
                if (output.Count > 0 && AuraFarmDebugLogs)
                {
                    ModLogger.Msg("[AuraFarm] Mono _selected path produced " + output.Count + " targets.");
                }
            }
            if (output.Count == 0)
            {
                this.auraLastError = "Mono target paths empty: currentTarget=0 focus/selected empty.";
                this.LogAuraMonoEmptyState(interactObj);
            }
        }

        private unsafe void LogAuraMonoEmptyState(IntPtr interactObj)
        {
            if (!AuraFarmDebugLogs || interactObj == IntPtr.Zero)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now - this.auraLastEmptyStateLogAt < 1f)
            {
                return;
            }

            this.auraLastEmptyStateLogAt = now;

            bool? canCollect = null;
            int? currentHandholdInteract = null;

            if (auraMonoRuntimeInvoke != null)
            {
                if (this.auraMonoInteractCanCollectionInteractionMethodPtr != IntPtr.Zero)
                {
                    IntPtr exc = IntPtr.Zero;
                    IntPtr boxed = auraMonoRuntimeInvoke(this.auraMonoInteractCanCollectionInteractionMethodPtr, interactObj, IntPtr.Zero, ref exc);
                    if (exc == IntPtr.Zero && boxed != IntPtr.Zero && this.TryUnboxMonoBoolean(boxed, out bool value))
                    {
                        canCollect = value;
                    }
                }

                if (this.auraMonoInteractCurrentHandholdInteractMethodPtr != IntPtr.Zero)
                {
                    IntPtr exc = IntPtr.Zero;
                    IntPtr boxed = auraMonoRuntimeInvoke(this.auraMonoInteractCurrentHandholdInteractMethodPtr, interactObj, IntPtr.Zero, ref exc);
                    if (exc == IntPtr.Zero && boxed != IntPtr.Zero && this.TryUnboxMonoInt32(boxed, out int value))
                    {
                        currentHandholdInteract = value;
                    }
                }
            }

            ModLogger.Msg("[AuraFarm] Mono empty state: canCollect="
                + (canCollect.HasValue ? canCollect.Value.ToString() : "n/a")
                + " currentHandholdInteract="
                + (currentHandholdInteract.HasValue ? currentHandholdInteract.Value.ToString() : "n/a"));
        }

        private unsafe bool TryUnboxMonoInt32(IntPtr boxed, out int value)
        {
            value = 0;
            if (boxed == IntPtr.Zero || auraMonoObjectUnbox == null)
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

        private unsafe bool TryResolveOwnerIdFromLevelObjectIdMono(ulong levelObjectId, out uint ownerId)
        {
            ownerId = 0U;
            if (auraMonoRuntimeInvoke == null || this.auraMonoEntityHelperGetLevelObjectOwnerMethodPtr == IntPtr.Zero)
            {
                return false;
            }
            if (!this.AttachAuraMonoThread())
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            ulong id = levelObjectId;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&id);
            IntPtr boxed = auraMonoRuntimeInvoke(this.auraMonoEntityHelperGetLevelObjectOwnerMethodPtr, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || boxed == IntPtr.Zero)
            {
                return false;
            }

            return this.TryUnboxMonoUInt32(boxed, out ownerId);
        }

        private unsafe bool TryUnboxMonoUInt32(IntPtr boxed, out uint value)
        {
            value = 0U;
            if (boxed == IntPtr.Zero || auraMonoObjectUnbox == null)
            {
                return false;
            }

            IntPtr raw = auraMonoObjectUnbox(boxed);
            if (raw == IntPtr.Zero)
            {
                return false;
            }

            value = *(uint*)raw;
            return value != 0U;
        }

        private unsafe ulong TryReadMonoUnsignedIntegral(IntPtr boxed)
        {
            if (boxed == IntPtr.Zero || auraMonoObjectUnbox == null)
            {
                return 0UL;
            }

            IntPtr raw = auraMonoObjectUnbox(boxed);
            if (raw == IntPtr.Zero)
            {
                return 0UL;
            }

            if (this.TryUnboxMonoUInt32(boxed, out uint uintValue))
            {
                return uintValue;
            }

            return *(ulong*)raw;
        }

        private unsafe bool TryUnboxMonoBoolean(IntPtr boxed, out bool value)
        {
            value = false;
            if (boxed == IntPtr.Zero || auraMonoObjectUnbox == null)
            {
                return false;
            }

            IntPtr raw = auraMonoObjectUnbox(boxed);
            if (raw == IntPtr.Zero)
            {
                return false;
            }

            value = (*(byte*)raw) != 0;
            return true;
        }

        private unsafe bool TryConvertMonoBoxedTargetToOwnerId(IntPtr boxed, out uint ownerId)
        {
            ownerId = 0U;
            if (boxed == IntPtr.Zero || auraMonoObjectUnbox == null)
            {
                return false;
            }

            if (auraMonoObjectGetClass != null && auraMonoClassIsValueType != null)
            {
                IntPtr klass = auraMonoObjectGetClass(boxed);
                if (klass == IntPtr.Zero || auraMonoClassIsValueType(klass) == 0)
                {
                    return false;
                }
            }

            IntPtr raw = auraMonoObjectUnbox(boxed);
            if (raw == IntPtr.Zero)
            {
                return false;
            }

            uint asUInt = *(uint*)raw;
            if (asUInt != 0U)
            {
                ownerId = asUInt;
                return true;
            }

            ulong asUlong = *(ulong*)raw;
            if (asUlong == 0UL)
            {
                return false;
            }

            if (asUlong <= uint.MaxValue)
            {
                ownerId = (uint)asUlong;
                return true;
            }

            return this.TryResolveOwnerIdFromLevelObjectIdMono(asUlong, out ownerId);
        }

        private unsafe void TryCollectAuraOwnerTargetsViaMonoFocusLevelObjects(IntPtr interactObj, HashSet<uint> output)
        {
            if (interactObj == IntPtr.Zero || this.auraMonoInteractFocusLevelObjectsFieldPtr == IntPtr.Zero || auraMonoFieldGetValue == null)
            {
                return;
            }

            IntPtr setObj = IntPtr.Zero;
            auraMonoFieldGetValue(interactObj, this.auraMonoInteractFocusLevelObjectsFieldPtr, (IntPtr)(&setObj));
            if (setObj == IntPtr.Zero || !this.EnsureAuraMonoFocusSetAccessors(setObj))
            {
                return;
            }

            int count = this.GetAuraMonoIntCount(setObj, this.auraMonoFocusSetCountMethodPtr);
            if (AuraFarmDebugLogs)
            {
                ModLogger.Msg("[AuraFarm] Mono _focusLevelObjects count=" + count);
            }
            if (count <= 0)
            {
                return;
            }

            IntPtr arrayObj = this.CreateAuraMonoUInt64ArrayObject(count);
            if (arrayObj == IntPtr.Zero)
            {
                return;
            }

            IntPtr exc = IntPtr.Zero;
            unsafe
            {
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = arrayObj;
                auraMonoRuntimeInvoke(this.auraMonoFocusSetCopyToMethodPtr, setObj, (IntPtr)args, ref exc);
            }
            if (exc != IntPtr.Zero)
            {
                return;
            }

            this.AddOwnerTargetsFromMonoUInt64Array(arrayObj, output, "Mono _focusLevelObjects");
        }

        private unsafe void TryCollectAuraOwnerTargetsViaMonoSelectedMap(IntPtr interactObj, HashSet<uint> output)
        {
            if (interactObj == IntPtr.Zero || this.auraMonoInteractSelectedFieldPtr == IntPtr.Zero || auraMonoFieldGetValue == null)
            {
                return;
            }

            IntPtr mapObj = IntPtr.Zero;
            auraMonoFieldGetValue(interactObj, this.auraMonoInteractSelectedFieldPtr, (IntPtr)(&mapObj));
            if (mapObj == IntPtr.Zero || !this.EnsureAuraMonoSelectedKeyAccessors(mapObj, out IntPtr keysObj) || keysObj == IntPtr.Zero)
            {
                return;
            }

            int count = this.GetAuraMonoIntCount(keysObj, this.auraMonoSelectedKeysCountMethodPtr);
            if (AuraFarmDebugLogs)
            {
                ModLogger.Msg("[AuraFarm] Mono _selected.Keys count=" + count);
            }
            if (count <= 0)
            {
                return;
            }

            IntPtr arrayObj = this.CreateAuraMonoUInt64ArrayObject(count);
            if (arrayObj == IntPtr.Zero)
            {
                return;
            }

            int startIndex = 0;
            IntPtr exc = IntPtr.Zero;
            unsafe
            {
                IntPtr* args = stackalloc IntPtr[2];
                args[0] = arrayObj;
                args[1] = (IntPtr)(&startIndex);
                auraMonoRuntimeInvoke(this.auraMonoSelectedKeysCopyToMethodPtr, keysObj, (IntPtr)args, ref exc);
            }
            if (exc != IntPtr.Zero)
            {
                return;
            }

            this.AddOwnerTargetsFromMonoUInt64Array(arrayObj, output, "Mono _selected.Keys");
        }

        private unsafe void TryCollectAuraOwnerTargetsViaMonoCollectionField(IntPtr interactObj, IntPtr fieldPtr, HashSet<uint> output)
        {
            if (interactObj == IntPtr.Zero || fieldPtr == IntPtr.Zero || auraMonoFieldGetValue == null)
            {
                return;
            }

            IntPtr containerObj = IntPtr.Zero;
            auraMonoFieldGetValue(interactObj, fieldPtr, (IntPtr)(&containerObj));
            if (containerObj == IntPtr.Zero)
            {
                return;
            }

            uint direct;
            if (this.TryConvertMonoBoxedTargetToOwnerId(containerObj, out direct))
            {
                output.Add(direct);
                return;
            }

            this.TryCollectAuraOwnerTargetsFromMonoEnumerable(containerObj, output);
        }

        private void TryCollectAuraOwnerTargetsFromMonoEnumerable(IntPtr enumerableObj, HashSet<uint> output)
        {
            if (enumerableObj == IntPtr.Zero || auraMonoObjectGetClass == null || auraMonoClassGetMethodFromName == null || auraMonoRuntimeInvoke == null)
            {
                return;
            }

            IntPtr enumerableClass = auraMonoObjectGetClass(enumerableObj);
            if (enumerableClass == IntPtr.Zero)
            {
                return;
            }

            IntPtr getEnumeratorMethod = auraMonoClassGetMethodFromName(enumerableClass, "GetEnumerator", 0);
            if (getEnumeratorMethod == IntPtr.Zero)
            {
                return;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr enumeratorObj = auraMonoRuntimeInvoke(getEnumeratorMethod, enumerableObj, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || enumeratorObj == IntPtr.Zero)
            {
                return;
            }

            IntPtr enumeratorClass = auraMonoObjectGetClass(enumeratorObj);
            if (enumeratorClass == IntPtr.Zero)
            {
                return;
            }

            IntPtr moveNextMethod = auraMonoClassGetMethodFromName(enumeratorClass, "MoveNext", 0);
            IntPtr getCurrentMethod = auraMonoClassGetMethodFromName(enumeratorClass, "get_Current", 0);
            if (moveNextMethod == IntPtr.Zero || getCurrentMethod == IntPtr.Zero)
            {
                return;
            }

            for (int i = 0; i < 256; i++)
            {
                exc = IntPtr.Zero;
                IntPtr moveNextRet = auraMonoRuntimeInvoke(moveNextMethod, enumeratorObj, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero || moveNextRet == IntPtr.Zero)
                {
                    break;
                }

                bool hasNext;
                if (!this.TryUnboxMonoBoolean(moveNextRet, out hasNext) || !hasNext)
                {
                    break;
                }

                exc = IntPtr.Zero;
                IntPtr currentObj = auraMonoRuntimeInvoke(getCurrentMethod, enumeratorObj, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero || currentObj == IntPtr.Zero)
                {
                    continue;
                }

                this.TryCollectAuraOwnerTargetFromMonoItem(currentObj, output);
            }
        }

        private bool EnsureAuraMonoFocusSetAccessors(IntPtr setObj)
        {
            if (setObj == IntPtr.Zero || auraMonoObjectGetClass == null || auraMonoClassGetMethodFromName == null)
            {
                return false;
            }

            IntPtr setClass = auraMonoObjectGetClass(setObj);
            if (setClass == IntPtr.Zero)
            {
                return false;
            }

            if (this.auraMonoFocusSetCountMethodPtr == IntPtr.Zero)
            {
                this.auraMonoFocusSetCountMethodPtr = auraMonoClassGetMethodFromName(setClass, "get_Count", 0);
            }
            if (this.auraMonoFocusSetCopyToMethodPtr == IntPtr.Zero)
            {
                this.auraMonoFocusSetCopyToMethodPtr = auraMonoClassGetMethodFromName(setClass, "CopyTo", 1);
            }

            return this.auraMonoFocusSetCountMethodPtr != IntPtr.Zero && this.auraMonoFocusSetCopyToMethodPtr != IntPtr.Zero;
        }

        private bool EnsureAuraMonoSelectedKeyAccessors(IntPtr mapObj, out IntPtr keysObj)
        {
            keysObj = IntPtr.Zero;
            if (mapObj == IntPtr.Zero || auraMonoObjectGetClass == null || auraMonoClassGetMethodFromName == null || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr mapClass = auraMonoObjectGetClass(mapObj);
            if (mapClass == IntPtr.Zero)
            {
                return false;
            }

            if (this.auraMonoSelectedGetKeysMethodPtr == IntPtr.Zero)
            {
                this.auraMonoSelectedGetKeysMethodPtr = auraMonoClassGetMethodFromName(mapClass, "get_Keys", 0);
            }
            if (this.auraMonoSelectedGetKeysMethodPtr == IntPtr.Zero)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            keysObj = auraMonoRuntimeInvoke(this.auraMonoSelectedGetKeysMethodPtr, mapObj, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || keysObj == IntPtr.Zero)
            {
                keysObj = IntPtr.Zero;
                return false;
            }

            IntPtr keysClass = auraMonoObjectGetClass(keysObj);
            if (keysClass == IntPtr.Zero)
            {
                keysObj = IntPtr.Zero;
                return false;
            }

            if (this.auraMonoSelectedKeysCountMethodPtr == IntPtr.Zero)
            {
                this.auraMonoSelectedKeysCountMethodPtr = auraMonoClassGetMethodFromName(keysClass, "get_Count", 0);
            }
            if (this.auraMonoSelectedKeysCopyToMethodPtr == IntPtr.Zero)
            {
                this.auraMonoSelectedKeysCopyToMethodPtr = auraMonoClassGetMethodFromName(keysClass, "CopyTo", 2);
            }

            return this.auraMonoSelectedKeysCountMethodPtr != IntPtr.Zero && this.auraMonoSelectedKeysCopyToMethodPtr != IntPtr.Zero;
        }

        private int GetAuraMonoIntCount(IntPtr obj, IntPtr methodPtr)
        {
            if (obj == IntPtr.Zero || methodPtr == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return 0;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr boxed = auraMonoRuntimeInvoke(methodPtr, obj, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || boxed == IntPtr.Zero)
            {
                return 0;
            }

            this.TryUnboxMonoInt32(boxed, out int count);
            return count;
        }

        private IntPtr CreateAuraMonoUInt64ArrayObject(int length)
        {
            if (length <= 0 || !this.AttachAuraMonoThread() || auraMonoArrayNew == null || this.auraMonoUInt64ClassPtr == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            return auraMonoArrayNew(this.auraMonoRootDomain, this.auraMonoUInt64ClassPtr, new UIntPtr((uint)length));
        }

        private void AddOwnerTargetsFromMonoUInt64Array(IntPtr arrayObj, HashSet<uint> output, string label)
        {
            if (arrayObj == IntPtr.Zero || auraMonoArrayLength == null || auraMonoArrayAddrWithSize == null)
            {
                return;
            }

            ulong length = auraMonoArrayLength(arrayObj).ToUInt64();
            for (int i = 0; i < (int)length; i++)
            {
                IntPtr raw = auraMonoArrayAddrWithSize(arrayObj, sizeof(ulong), (UIntPtr)i);
                if (raw == IntPtr.Zero)
                {
                    continue;
                }

                ulong levelObjectId = (ulong)Marshal.ReadInt64(raw);
                if (levelObjectId == 0UL)
                {
                    continue;
                }

                int beforeCount = output.Count;
                this.RegisterAuraTargetFromLevelObjectId(output, levelObjectId, label + "[" + i + "]");
                if (output.Count > beforeCount)
                {
                    if (AuraFarmDebugLogs && i < 3)
                    {
                        AuraTargetInfo info = null;
                        foreach (uint ownerId in output)
                        {
                            if (this.auraTargetInfoByOwnerId.TryGetValue(ownerId, out info) && info.TargetNetId == levelObjectId)
                            {
                                ModLogger.Msg("[AuraFarm] " + label + "[" + i + "] levelObjectId=" + levelObjectId + " ownerId=" + info.OwnerNetId);
                                break;
                            }
                        }
                    }
                }
            }
        }

        private bool EnsureAuraMonoArrayGetValueAccessor(IntPtr arrayObj)
        {
            if (arrayObj == IntPtr.Zero || auraMonoObjectGetClass == null || auraMonoClassGetMethodFromName == null)
            {
                return false;
            }

            if (this.auraMonoArrayGetValueMethodPtr != IntPtr.Zero)
            {
                return true;
            }

            IntPtr arrayClass = auraMonoObjectGetClass(arrayObj);
            if (arrayClass == IntPtr.Zero)
            {
                return false;
            }

            this.auraMonoArrayGetValueMethodPtr = auraMonoClassGetMethodFromName(arrayClass, "GetValue", 1);
            return this.auraMonoArrayGetValueMethodPtr != IntPtr.Zero;
        }

        private IntPtr GetAuraMonoArrayValue(IntPtr arrayObj, int index)
        {
            if (arrayObj == IntPtr.Zero || auraMonoRuntimeInvoke == null || !this.EnsureAuraMonoArrayGetValueAccessor(arrayObj))
            {
                return IntPtr.Zero;
            }

            IntPtr exc = IntPtr.Zero;
            unsafe
            {
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&index);
                return auraMonoRuntimeInvoke(this.auraMonoArrayGetValueMethodPtr, arrayObj, (IntPtr)args, ref exc);
            }
        }

        private void TryCollectAuraOwnerTargetFromMonoItem(IntPtr itemObj, HashSet<uint> output)
        {
            uint ownerId;
            if (this.TryConvertMonoBoxedTargetToOwnerId(itemObj, out ownerId))
            {
                output.Add(ownerId);
                return;
            }

            if (auraMonoObjectGetClass == null || auraMonoClassGetMethodFromName == null || auraMonoRuntimeInvoke == null)
            {
                return;
            }

            IntPtr itemClass = auraMonoObjectGetClass(itemObj);
            if (itemClass == IntPtr.Zero)
            {
                return;
            }

            IntPtr getKeyMethod = auraMonoClassGetMethodFromName(itemClass, "get_Key", 0);
            if (getKeyMethod == IntPtr.Zero)
            {
                return;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr keyObj = auraMonoRuntimeInvoke(getKeyMethod, itemObj, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || keyObj == IntPtr.Zero)
            {
                return;
            }

            if (this.TryConvertMonoBoxedTargetToOwnerId(keyObj, out ownerId))
            {
                output.Add(ownerId);
            }
        }

        private void ResolveAuraFarmRuntimeMethodsViaMono()
        {
            if (!this.EnsureAuraMonoApiReady())
            {
                return;
            }

            if (!this.AttachAuraMonoThread())
            {
                return;
            }

            try
            {
                IntPtr dataImage = this.FindAuraMonoImage(new string[] { "XDTDataAndProtocol", "XDTDataAndProtocol.dll" });
                if (dataImage != IntPtr.Zero && auraMonoClassFromName != null && auraMonoClassGetMethodFromName != null)
                {
                    IntPtr resourceClass = auraMonoClassFromName(dataImage, "XDTDataAndProtocol.ProtocolService.Resource", "ResourceProtocolManager");
                    if (resourceClass != IntPtr.Zero)
                    {
                        if (this.auraMonoSendPickBushMethodPtr == IntPtr.Zero)
                        {
                            this.auraMonoSendPickBushMethodPtr = auraMonoClassGetMethodFromName(resourceClass, "SendPickBushCommand", 1);
                        }

                    if (this.auraMonoSendAttackTreeMethodPtr == IntPtr.Zero)
                    {
                        this.auraMonoSendAttackTreeMethodPtr = auraMonoClassGetMethodFromName(resourceClass, "SendAttackTreeCommand", 2);
                    }

                    if (this.auraMonoSendHitStoneMethodPtr == IntPtr.Zero)
                    {
                        this.auraMonoSendHitStoneMethodPtr = auraMonoClassGetMethodFromName(resourceClass, "SendHitStoneCommand", 2);
                    }

                }
                }

                IntPtr levelImage = this.FindAuraMonoImage(new string[] { "XDTLevelAndEntity", "XDTLevelAndEntity.dll" });
                IntPtr coreImage = this.FindAuraMonoImage(new string[] { "mscorlib", "mscorlib.dll", "System.Private.CoreLib", "System.Private.CoreLib.dll" });
                if (auraMonoClassFromName != null && auraMonoClassGetMethodFromName != null)
                {
                    IntPtr interactClass = IntPtr.Zero;
                    if (levelImage != IntPtr.Zero)
                    {
                        interactClass = auraMonoClassFromName(levelImage, "XDTLevelAndEntity.BaseSystem.InteractSystem", "InteractSystem");
                    }
                    if (interactClass == IntPtr.Zero)
                    {
                        interactClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTLevelAndEntity.BaseSystem.InteractSystem", "InteractSystem");
                    }
                    if (interactClass != IntPtr.Zero)
                    {
                        this.auraMonoInteractSystemClassPtr = interactClass;
                        if (this.auraMonoInteractGetInstanceMethodPtr == IntPtr.Zero)
                        {
                            this.auraMonoInteractGetInstanceMethodPtr = auraMonoClassGetMethodFromName(interactClass, "get_Instance", 0);
                        }
                        if (this.auraMonoInteractGetPlayerMethodPtr == IntPtr.Zero)
                        {
                            this.auraMonoInteractGetPlayerMethodPtr = auraMonoClassGetMethodFromName(interactClass, "get_player", 0);
                        }
                        if (this.auraMonoInteractCanCollectionInteractionMethodPtr == IntPtr.Zero)
                        {
                            this.auraMonoInteractCanCollectionInteractionMethodPtr = auraMonoClassGetMethodFromName(interactClass, "CanCollectionInteraction", 0);
                        }
                        if (this.auraMonoInteractCurrentHandholdInteractMethodPtr == IntPtr.Zero)
                        {
                            this.auraMonoInteractCurrentHandholdInteractMethodPtr = auraMonoClassGetMethodFromName(interactClass, "CurrentHandholdInteract", 0);
                        }

                        if (this.auraMonoInteractCurrentTargetFieldPtr == IntPtr.Zero && auraMonoClassGetFieldFromName != null)
                        {
                            this.auraMonoInteractCurrentTargetFieldPtr = auraMonoClassGetFieldFromName(interactClass, "_currentSelectTarget");
                        }
                        if (this.auraMonoInteractFocusLevelObjectsFieldPtr == IntPtr.Zero && auraMonoClassGetFieldFromName != null)
                        {
                            this.auraMonoInteractFocusLevelObjectsFieldPtr = auraMonoClassGetFieldFromName(interactClass, "_focusLevelObjects");
                        }
                        if (this.auraMonoInteractSelectedFieldPtr == IntPtr.Zero && auraMonoClassGetFieldFromName != null)
                        {
                            this.auraMonoInteractSelectedFieldPtr = auraMonoClassGetFieldFromName(interactClass, "_selected");
                        }
                        if (this.auraMonoInteractSelectPriorityLengthFieldPtr == IntPtr.Zero && auraMonoClassGetFieldFromName != null)
                        {
                            this.auraMonoInteractSelectPriorityLengthFieldPtr = auraMonoClassGetFieldFromName(interactClass, "_selectPriorityLength");
                        }
                        if (this.auraMonoInteractSelectPriorityInfoArrayFieldPtr == IntPtr.Zero && auraMonoClassGetFieldFromName != null)
                        {
                            this.auraMonoInteractSelectPriorityInfoArrayFieldPtr = auraMonoClassGetFieldFromName(interactClass, "_selectPriorityInfoArray");
                        }
                    }

                    IntPtr entityHelperClass = IntPtr.Zero;
                    if (levelImage != IntPtr.Zero)
                    {
                        entityHelperClass = auraMonoClassFromName(levelImage, "XDTLevelAndEntity.Utils", "EntityHelper");
                    }
                    if (entityHelperClass == IntPtr.Zero)
                    {
                        entityHelperClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTLevelAndEntity.Utils", "EntityHelper");
                    }
                    if (entityHelperClass != IntPtr.Zero && this.auraMonoEntityHelperGetLevelObjectOwnerMethodPtr == IntPtr.Zero)
                    {
                        this.auraMonoEntityHelperGetLevelObjectOwnerMethodPtr = auraMonoClassGetMethodFromName(entityHelperClass, "GetLevelObjectOwner", 1);
                    }

                    if (this.auraMonoAxeCheckerClassPtr == IntPtr.Zero)
                    {
                        if (levelImage != IntPtr.Zero)
                        {
                            this.auraMonoAxeCheckerClassPtr = auraMonoClassFromName(levelImage, "XDTLevelAndEntity.Gameplay.Component.Equip", "HandholdCylinderChecker");
                        }
                        if (this.auraMonoAxeCheckerClassPtr == IntPtr.Zero)
                        {
                            this.auraMonoAxeCheckerClassPtr = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTLevelAndEntity.Gameplay.Component.Equip", "HandholdCylinderChecker");
                        }
                        if (this.auraMonoAxeCheckerClassPtr == IntPtr.Zero)
                        {
                            if (levelImage != IntPtr.Zero)
                            {
                                this.auraMonoAxeCheckerClassPtr = auraMonoClassFromName(levelImage, "XDTLevelAndEntity.Gameplay.Component.Equip", "AxeChecker");
                            }
                            if (this.auraMonoAxeCheckerClassPtr == IntPtr.Zero)
                            {
                                this.auraMonoAxeCheckerClassPtr = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTLevelAndEntity.Gameplay.Component.Equip", "AxeChecker");
                            }
                        }
                    }
                    if (this.auraMonoAxeCheckerClassPtr != IntPtr.Zero)
                    {
                        if (this.auraMonoAxeCheckerInstanceFieldPtr == IntPtr.Zero && auraMonoClassGetFieldFromName != null)
                        {
                            this.auraMonoAxeCheckerInstanceFieldPtr = auraMonoClassGetFieldFromName(this.auraMonoAxeCheckerClassPtr, "Instance");
                        }
                        if (this.auraMonoAxeCheckerPhysicalSelectMethodPtr == IntPtr.Zero)
                        {
                            this.auraMonoAxeCheckerPhysicalSelectMethodPtr = auraMonoClassGetMethodFromName(this.auraMonoAxeCheckerClassPtr, "PhysicalSelect", 3);
                        }
                    }

                    IntPtr localPlayerComponentClass = IntPtr.Zero;
                    if (levelImage != IntPtr.Zero)
                    {
                        localPlayerComponentClass = auraMonoClassFromName(levelImage, "XDTLevelAndEntity.Gameplay.Component.Player", "LocalPlayerComponent");
                    }
                    if (localPlayerComponentClass == IntPtr.Zero)
                    {
                        localPlayerComponentClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTLevelAndEntity.Gameplay.Component.Player", "LocalPlayerComponent");
                    }
                    if (localPlayerComponentClass != IntPtr.Zero && this.auraMonoLocalPlayerLookDecisionsFieldPtr == IntPtr.Zero && auraMonoClassGetFieldFromName != null)
                    {
                        this.auraMonoLocalPlayerLookDecisionsFieldPtr = auraMonoClassGetFieldFromName(localPlayerComponentClass, "_lookAtDecisions");
                    }

                    if (this.auraMonoLocalPlayerLookInteractTargetClassPtr == IntPtr.Zero)
                    {
                        if (levelImage != IntPtr.Zero)
                        {
                            this.auraMonoLocalPlayerLookInteractTargetClassPtr = auraMonoClassFromName(levelImage, "XDTLevelAndEntity.Gameplay.Component.Player", "LocalPlayerLookInteractTarget");
                        }
                        if (this.auraMonoLocalPlayerLookInteractTargetClassPtr == IntPtr.Zero)
                        {
                            this.auraMonoLocalPlayerLookInteractTargetClassPtr = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTLevelAndEntity.Gameplay.Component.Player", "LocalPlayerLookInteractTarget");
                        }
                    }
                    if (this.auraMonoLocalPlayerLookInteractTargetClassPtr != IntPtr.Zero && this.auraMonoLocalPlayerLookTargetListFieldPtr == IntPtr.Zero && auraMonoClassGetFieldFromName != null)
                    {
                        this.auraMonoLocalPlayerLookTargetListFieldPtr = auraMonoClassGetFieldFromName(this.auraMonoLocalPlayerLookInteractTargetClassPtr, "_targetList");
                    }
                }

                if (auraMonoClassFromName != null)
                {
                    if (this.auraMonoSelectPriorityInfoClassPtr == IntPtr.Zero && dataImage != IntPtr.Zero)
                    {
                        this.auraMonoSelectPriorityInfoClassPtr = auraMonoClassFromName(dataImage, "XDTLevelAndEntity.BaseSystem.InteractSystem", "SelectPriorityInfo");
                    }
                    if (this.auraMonoSelectPriorityInfoClassPtr == IntPtr.Zero)
                    {
                        this.auraMonoSelectPriorityInfoClassPtr = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTLevelAndEntity.BaseSystem.InteractSystem", "SelectPriorityInfo");
                    }
                    if (this.auraMonoSelectPriorityInfoClassPtr != IntPtr.Zero && this.auraMonoSelectPriorityInfoShapeFieldPtr == IntPtr.Zero && auraMonoClassGetFieldFromName != null)
                    {
                        this.auraMonoSelectPriorityInfoShapeFieldPtr = auraMonoClassGetFieldFromName(this.auraMonoSelectPriorityInfoClassPtr, "shape");
                    }
                }

                if (coreImage != IntPtr.Zero && auraMonoClassFromName != null && auraMonoClassGetMethodFromName != null)
                {
                    if (this.auraMonoTypeGetTypeMethodPtr == IntPtr.Zero)
                    {
                        IntPtr typeClass = auraMonoClassFromName(coreImage, "System", "Type");
                        if (typeClass != IntPtr.Zero)
                        {
                            this.auraMonoTypeGetTypeMethodPtr = auraMonoClassGetMethodFromName(typeClass, "GetType", 1);
                        }
                    }

                    if (this.auraMonoActivatorCreateInstanceMethodPtr == IntPtr.Zero)
                    {
                        IntPtr activatorClass = auraMonoClassFromName(coreImage, "System", "Activator");
                        if (activatorClass != IntPtr.Zero)
                        {
                            this.auraMonoActivatorCreateInstanceMethodPtr = auraMonoClassGetMethodFromName(activatorClass, "CreateInstance", 1);
                        }
                    }

                    if (this.auraMonoUInt64ClassPtr == IntPtr.Zero)
                    {
                        this.auraMonoUInt64ClassPtr = auraMonoClassFromName(coreImage, "System", "UInt64");
                    }
                }
            }
            catch (Exception ex)
            {
                if (string.IsNullOrEmpty(this.auraLastError))
                {
                    this.auraLastError = "Mono resolver failed: " + ex.Message;
                }
            }
        }

        private bool EnsureAuraMonoApiReady()
        {
            if (this.auraMonoApiReady)
            {
                return true;
            }

            IntPtr monoModule = this.GetAuraMonoModuleHandle();
            if (monoModule == IntPtr.Zero)
            {
                return false;
            }

            auraMonoGetRootDomain = this.GetAuraMonoExport<MonoGetRootDomainDelegate>(monoModule, "mono_get_root_domain");
            auraMonoThreadAttach = this.GetAuraMonoExport<MonoThreadAttachDelegate>(monoModule, "mono_thread_attach");
            auraMonoImageLoaded = this.GetAuraMonoExport<MonoImageLoadedDelegate>(monoModule, "mono_image_loaded");
            auraMonoAssemblyForeach = this.GetAuraMonoExport<MonoAssemblyForeachDelegate>(monoModule, "mono_assembly_foreach");
            auraMonoAssemblyGetImage = this.GetAuraMonoExport<MonoAssemblyGetImageDelegate>(monoModule, "mono_assembly_get_image");
            auraMonoImageGetName = this.GetAuraMonoExport<MonoImageGetNameDelegate>(monoModule, "mono_image_get_name");
            auraMonoClassFromName = this.GetAuraMonoExport<MonoClassFromNameDelegate>(monoModule, "mono_class_from_name");
            auraMonoClassGetMethodFromName = this.GetAuraMonoExport<MonoClassGetMethodFromNameDelegate>(monoModule, "mono_class_get_method_from_name");
            auraMonoClassGetFieldFromName = this.GetAuraMonoExport<MonoClassGetFieldFromNameDelegate>(monoModule, "mono_class_get_field_from_name");
            auraMonoFieldGetValue = this.GetAuraMonoExport<MonoFieldGetValueDelegate>(monoModule, "mono_field_get_value");
            auraMonoFieldGetValueObject = this.GetAuraMonoExport<MonoFieldGetValueObjectDelegate>(monoModule, "mono_field_get_value_object");
            auraMonoFieldSetValue = this.GetAuraMonoExport<MonoFieldSetValueDelegate>(monoModule, "mono_field_set_value");
            auraMonoRuntimeInvoke = this.GetAuraMonoExport<MonoRuntimeInvokeDelegate>(monoModule, "mono_runtime_invoke");
            auraMonoObjectUnbox = this.GetAuraMonoExport<MonoObjectUnboxDelegate>(monoModule, "mono_object_unbox");
            auraMonoStringNew = this.GetAuraMonoExport<MonoStringNewDelegate>(monoModule, "mono_string_new");
            auraMonoStringToUtf8 = this.GetAuraMonoExport<MonoStringToUtf8Delegate>(monoModule, "mono_string_to_utf8");
            auraMonoFree = this.GetAuraMonoExport<MonoFreeDelegate>(monoModule, "mono_free");
            auraMonoObjectGetClass = this.GetAuraMonoExport<MonoObjectGetClassDelegate>(monoModule, "mono_object_get_class");
            auraMonoClassIsValueType = this.GetAuraMonoExport<MonoClassIsValueTypeDelegate>(monoModule, "mono_class_is_valuetype");
            auraMonoClassGetType = this.GetAuraMonoExport<MonoClassGetTypeDelegate>(monoModule, "mono_class_get_type");
            auraMonoClassBindGenericParameters = this.GetAuraMonoExport<MonoClassBindGenericParametersDelegate>(monoModule, "mono_class_bind_generic_parameters");
            auraMonoTypeGetObject = this.GetAuraMonoExport<MonoTypeGetObjectDelegate>(monoModule, "mono_type_get_object");
            auraMonoClassGetParent = this.GetAuraMonoExport<MonoClassGetParentDelegate>(monoModule, "mono_class_get_parent");
            auraMonoClassGetName = this.GetAuraMonoExport<MonoClassGetNameDelegate>(monoModule, "mono_class_get_name");
            auraMonoClassGetNamespace = this.GetAuraMonoExport<MonoClassGetNamespaceDelegate>(monoModule, "mono_class_get_namespace");
            auraMonoClassGetMethods = this.GetAuraMonoExport<MonoClassGetMethodsDelegate>(monoModule, "mono_class_get_methods");
            auraMonoMethodGetName = this.GetAuraMonoExport<MonoMethodGetNameDelegate>(monoModule, "mono_method_get_name");
            auraMonoMethodSignature = this.GetAuraMonoExport<MonoMethodSignatureDelegate>(monoModule, "mono_method_signature");
            auraMonoSignatureGetParamCount = this.GetAuraMonoExport<MonoSignatureGetParamCountDelegate>(monoModule, "mono_signature_get_param_count");
            auraMonoArrayLength = this.GetAuraMonoExport<MonoArrayLengthDelegate>(monoModule, "mono_array_length");
            auraMonoArrayAddrWithSize = this.GetAuraMonoExport<MonoArrayAddrWithSizeDelegate>(monoModule, "mono_array_addr_with_size");
            auraMonoArrayNew = this.GetAuraMonoExport<MonoArrayNewDelegate>(monoModule, "mono_array_new");
            auraMonoClassVtable = this.GetAuraMonoExport<MonoClassVtableDelegate>(monoModule, "mono_class_vtable");
            auraMonoFieldStaticGetValue = this.GetAuraMonoExport<MonoFieldStaticGetValueDelegate>(monoModule, "mono_field_static_get_value");
            auraMonoObjectNew = this.GetAuraMonoExport<MonoObjectNewDelegate>(monoModule, "mono_object_new");
            auraMonoRuntimeObjectInit = this.GetAuraMonoExport<MonoRuntimeObjectInitDelegate>(monoModule, "mono_runtime_object_init");
            auraMonoCompileMethod = this.GetAuraMonoExport<MonoCompileMethodDelegate>(monoModule, "mono_compile_method");
            auraMonoMethodGetUnmanagedThunk = this.GetAuraMonoExport<MonoMethodGetUnmanagedThunkDelegate>(monoModule, "mono_method_get_unmanaged_thunk");
            auraMonoMethodGetUnmanaged = this.GetAuraMonoExport<MonoMethodGetUnmanagedDelegate>(monoModule, "mono_method_get_unmanaged");

            this.auraMonoApiReady = auraMonoGetRootDomain != null
                && auraMonoThreadAttach != null
                && auraMonoImageLoaded != null
                && auraMonoClassFromName != null
                && auraMonoClassGetMethodFromName != null
                && auraMonoClassGetFieldFromName != null
                && auraMonoFieldGetValue != null
                && auraMonoRuntimeInvoke != null
                && auraMonoObjectUnbox != null
                && auraMonoStringNew != null;

            if (this.auraMonoApiReady)
            {
                this.auraMonoRootDomain = auraMonoGetRootDomain();
                if (this.auraMonoAttachedDomain != this.auraMonoRootDomain)
                {
                    this.auraMonoAttachedManagedThreadId = int.MinValue;
                    this.auraMonoAttachedDomain = IntPtr.Zero;
                }
            }

            return this.auraMonoApiReady && this.auraMonoRootDomain != IntPtr.Zero;
        }

        private bool AttachAuraMonoThread()
        {
            if (!this.auraMonoApiReady || auraMonoThreadAttach == null || this.auraMonoRootDomain == IntPtr.Zero)
            {
                return false;
            }

            try
            {
                int managedThreadId = Environment.CurrentManagedThreadId;
                if (this.auraMonoAttachedManagedThreadId == managedThreadId
                    && this.auraMonoAttachedDomain == this.auraMonoRootDomain)
                {
                    return true;
                }

                auraMonoThreadAttach(this.auraMonoRootDomain);
                this.auraMonoAttachedManagedThreadId = managedThreadId;
                this.auraMonoAttachedDomain = this.auraMonoRootDomain;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private IntPtr FindAuraMonoImage(string[] names)
        {
            if (!this.auraMonoApiReady || auraMonoImageLoaded == null || names == null)
            {
                return IntPtr.Zero;
            }

            for (int i = 0; i < names.Length; i++)
            {
                string name = names[i];
                if (string.IsNullOrEmpty(name))
                {
                    continue;
                }

                try
                {
                    IntPtr image = auraMonoImageLoaded(name);
                    if (image != IntPtr.Zero)
                    {
                        return image;
                    }
                }
                catch
                {
                }
            }

            return IntPtr.Zero;
        }

        private IntPtr GetAuraMonoModuleHandle()
        {
            string[] candidates = new string[]
            {
                "mono-2.0-bdwgc.dll",
                "mono-2.0-sgen.dll",
                "mono.dll"
            };

            for (int i = 0; i < candidates.Length; i++)
            {
                IntPtr h = GetModuleHandle(candidates[i]);
                if (h != IntPtr.Zero)
                {
                    return h;
                }
            }

            return IntPtr.Zero;
        }

        private IntPtr FindAuraMonoClassAcrossLoadedAssemblies(string nameSpace, string className)
        {
            if (!this.auraMonoApiReady || auraMonoClassFromName == null || string.IsNullOrWhiteSpace(nameSpace) || string.IsNullOrWhiteSpace(className))
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
                    object value = null;
                    PropertyInfo valueProperty = entryType.GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
                    if (valueProperty != null)
                    {
                        value = valueProperty.GetValue(entry, null);
                    }

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

                    IntPtr classPtr = auraMonoClassFromName(imageHandle, nameSpace, className);
                    if (classPtr != IntPtr.Zero)
                    {
                        return classPtr;
                    }
                }
            }
            catch
            {
            }

            return IntPtr.Zero;
        }

        private T GetAuraMonoExport<T>(IntPtr module, string exportName) where T : class
        {
            if (module == IntPtr.Zero || string.IsNullOrEmpty(exportName))
            {
                return null;
            }

            IntPtr ptr = GetProcAddress(module, exportName);
            if (ptr == IntPtr.Zero)
            {
                return null;
            }

            return Marshal.GetDelegateForFunctionPointer(ptr, typeof(T)) as T;
        }

        private void TryAddAuraFieldTarget(object instance, FieldInfo field, HashSet<uint> output)
        {
            if (instance == null || field == null)
            {
                return;
            }

            try
            {
                object value = field.GetValue(instance);
                uint ownerNetId;
                if (this.TryConvertAuraOwnerId(value, out ownerNetId))
                {
                    output.Add(ownerNetId);
                }
            }
            catch (Exception ex)
            {
                this.auraLastError = "Field read failed: " + ex.Message;
            }
        }

        private void TryAddAuraContainerTargets(object instance, FieldInfo field, HashSet<uint> output)
        {
            if (instance == null || field == null)
            {
                return;
            }

            try
            {
                object container = field.GetValue(instance);
                this.AddAuraOwnerTargetsFromUnknownContainer(container, output);
            }
            catch (Exception ex)
            {
                this.auraLastError = "Container read failed: " + ex.Message;
            }
        }

        private void TryAddAuraPropertyTarget(object instance, PropertyInfo property, HashSet<uint> output)
        {
            if (instance == null || property == null || !property.CanRead || property.GetIndexParameters().Length != 0)
            {
                return;
            }

            try
            {
                object value = property.GetValue(instance, null);
                uint ownerNetId;
                if (this.TryConvertAuraOwnerId(value, out ownerNetId))
                {
                    output.Add(ownerNetId);
                }
            }
            catch (Exception ex)
            {
                this.auraLastError = "Property read failed: " + ex.Message;
            }
        }

        private void TryAddAuraContainerTargets(object instance, PropertyInfo property, HashSet<uint> output)
        {
            if (instance == null || property == null || !property.CanRead || property.GetIndexParameters().Length != 0)
            {
                return;
            }

            try
            {
                object container = property.GetValue(instance, null);
                this.AddAuraOwnerTargetsFromUnknownContainer(container, output);
            }
            catch (Exception ex)
            {
                this.auraLastError = "Property container read failed: " + ex.Message;
            }
        }

        private void TryAddAuraTargetsFromCandidateMembers(object interactSystem, HashSet<uint> output)
        {
            for (int i = 0; i < this.auraInteractTargetCandidateFields.Length; i++)
            {
                this.TryAddAuraContainerTargets(interactSystem, this.auraInteractTargetCandidateFields[i], output);
            }

            for (int i = 0; i < this.auraInteractTargetCandidateProperties.Length; i++)
            {
                this.TryAddAuraContainerTargets(interactSystem, this.auraInteractTargetCandidateProperties[i], output);
            }
        }

        private void TryCollectAuraOwnerTargetsViaTargetLists(object interactSystem, HashSet<uint> output)
        {
            if (AuraUseMonoTargetFallbacks)
            {
                this.TryCollectAuraOwnerTargetsViaMonoManagedLists(interactSystem, output);
                if (output.Count > 0)
                {
                    return;
                }
            }

            this.TryCollectAuraOwnerTargetsViaInteractSystemTargetList(interactSystem, output);
            if (output.Count == 0)
            {
                this.TryCollectAuraOwnerTargetsViaEntityHelperTargetList(output);
            }
        }

        private void TryCollectAuraOwnerTargetsViaMonoManagedLists(object interactSystem, HashSet<uint> output)
        {
            if (output.Count > 0 || auraMonoRuntimeInvoke == null || auraMonoStringNew == null)
            {
                return;
            }
            if (!this.AttachAuraMonoThread())
            {
                return;
            }

            IntPtr listObj = this.GetAuraMonoUInt64ListObject();
            if (listObj == IntPtr.Zero)
            {
                return;
            }

            IntPtr interactObj = this.GetAuraMonoInteractSystemInstance();
            if (interactObj != IntPtr.Zero && this.auraMonoInteractGetTargetListMethodViaClass(out IntPtr getTargetListMethod))
            {
                if (this.TryPopulateAuraOwnerTargetsFromMonoListCall(getTargetListMethod, interactObj, listObj, output, "Mono InteractSystem.GetInteractTargetList"))
                {
                    return;
                }
            }

            if (this.auraMonoEntityHelperGetTargetListMethodViaClass(out IntPtr entityHelperTargetListMethod))
            {
                this.TryPopulateAuraOwnerTargetsFromMonoListCall(entityHelperTargetListMethod, IntPtr.Zero, listObj, output, "Mono EntityHelper.GetPlayerInteractTargetList");
            }
        }

        private void TryCollectAuraOwnerTargetsViaMonoAdvancedFallbacks(HashSet<uint> output)
        {
            if (output.Count > 0)
            {
                return;
            }

            IntPtr interactObj = this.GetAuraMonoInteractSystemInstance();
            if (interactObj == IntPtr.Zero)
            {
                return;
            }

            if (this.TryCollectAuraOwnerTargetsViaMonoSelectPriorityArray(interactObj, output))
            {
                return;
            }

            this.TryCollectAuraOwnerTargetsViaMonoLookAtDecisions(interactObj, output);
        }

        private bool TryCollectAuraOwnerTargetsViaMonoSelectPriorityArray(IntPtr interactObj, HashSet<uint> output)
        {
            if (interactObj == IntPtr.Zero || this.auraMonoInteractSystemClassPtr == IntPtr.Zero || this.auraMonoInteractSelectPriorityLengthFieldPtr == IntPtr.Zero || this.auraMonoInteractSelectPriorityInfoArrayFieldPtr == IntPtr.Zero || auraMonoFieldGetValue == null || auraMonoArrayLength == null || auraMonoClassVtable == null || auraMonoFieldStaticGetValue == null)
            {
                return false;
            }

            unsafe
            {
                int length = 0;
                auraMonoFieldGetValue(interactObj, this.auraMonoInteractSelectPriorityLengthFieldPtr, (IntPtr)(&length));
                if (AuraFarmDebugLogs)
                {
                    ModLogger.Msg("[AuraFarm] Mono _selectPriorityLength=" + length);
                }
                if (length <= 0)
                {
                    return false;
                }

                IntPtr vtable = auraMonoClassVtable(this.auraMonoRootDomain, this.auraMonoInteractSystemClassPtr);
                if (vtable == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr infoArrayObj = IntPtr.Zero;
                auraMonoFieldStaticGetValue(vtable, this.auraMonoInteractSelectPriorityInfoArrayFieldPtr, (IntPtr)(&infoArrayObj));
                if (infoArrayObj == IntPtr.Zero)
                {
                    return false;
                }

                ulong arrayLength = auraMonoArrayLength(infoArrayObj).ToUInt64();
                int count = Math.Min(length, (int)Math.Min(arrayLength, 32UL));
                if (AuraFarmDebugLogs)
                {
                    ModLogger.Msg("[AuraFarm] Mono _selectPriorityInfoArray length=" + arrayLength);
                }

                for (int i = 0; i < count; i++)
                {
                    IntPtr infoObj = this.GetAuraMonoArrayValue(infoArrayObj, i);
                    if (infoObj == IntPtr.Zero)
                    {
                        if (AuraFarmDebugLogs && i < 3)
                        {
                            ModLogger.Msg("[AuraFarm] Mono _selectPriorityInfoArray[" + i + "] GetValue returned null.");
                        }
                        continue;
                    }

                    if (this.auraMonoSelectPriorityInfoShapeFieldPtr == IntPtr.Zero && auraMonoObjectGetClass != null && auraMonoClassGetFieldFromName != null)
                    {
                        IntPtr infoClass = auraMonoObjectGetClass(infoObj);
                        if (infoClass != IntPtr.Zero)
                        {
                            this.auraMonoSelectPriorityInfoShapeFieldPtr = auraMonoClassGetFieldFromName(infoClass, "shape");
                        }
                    }

                    if (this.auraMonoSelectPriorityInfoShapeFieldPtr == IntPtr.Zero)
                    {
                        continue;
                    }

                    IntPtr shapeObj = IntPtr.Zero;
                    auraMonoFieldGetValue(infoObj, this.auraMonoSelectPriorityInfoShapeFieldPtr, (IntPtr)(&shapeObj));
                    if (shapeObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    if (this.auraMonoDynamicLevelObjectGetUniqueIdMethodPtr == IntPtr.Zero && auraMonoObjectGetClass != null && auraMonoClassGetMethodFromName != null)
                    {
                        IntPtr shapeClass = auraMonoObjectGetClass(shapeObj);
                        if (shapeClass != IntPtr.Zero)
                        {
                            this.auraMonoDynamicLevelObjectGetUniqueIdMethodPtr = auraMonoClassGetMethodFromName(shapeClass, "GetUniqueId", 0);
                        }
                    }

                    if (this.auraMonoDynamicLevelObjectGetUniqueIdMethodPtr == IntPtr.Zero)
                    {
                        continue;
                    }

                    IntPtr exc = IntPtr.Zero;
                    IntPtr boxedId = auraMonoRuntimeInvoke(this.auraMonoDynamicLevelObjectGetUniqueIdMethodPtr, shapeObj, IntPtr.Zero, ref exc);
                    if (exc != IntPtr.Zero || boxedId == IntPtr.Zero || auraMonoObjectUnbox == null)
                    {
                        continue;
                    }

                    IntPtr raw = auraMonoObjectUnbox(boxedId);
                    if (raw == IntPtr.Zero)
                    {
                        continue;
                    }

                    ulong levelObjectId = *(ulong*)raw;
                    if (levelObjectId == 0UL)
                    {
                        continue;
                    }

                    int beforeCount = output.Count;
                    Vector3 shapePosition;
                    bool hasShapePosition = this.TryGetAuraMonoObjectPosition(shapeObj, out shapePosition);
                    this.RegisterAuraTargetFromLevelObjectId(output, levelObjectId, "MonoSelectPriority[" + i + "]", hasShapePosition, shapePosition);
                    if (output.Count > beforeCount)
                    {
                        if (AuraFarmDebugLogs && i < 3)
                        {
                            ModLogger.Msg("[AuraFarm] Mono _selectPriorityInfoArray[" + i + "] levelObjectId=" + levelObjectId);
                        }
                    }
                }
            }

            return output.Count > 0;
        }

        private bool TryCollectAuraOwnerTargetsViaMonoLookAtDecisions(IntPtr interactObj, HashSet<uint> output)
        {
            if (interactObj == IntPtr.Zero || this.auraMonoInteractGetPlayerMethodPtr == IntPtr.Zero || this.auraMonoLocalPlayerLookDecisionsFieldPtr == IntPtr.Zero || this.auraMonoLocalPlayerLookInteractTargetClassPtr == IntPtr.Zero || this.auraMonoLocalPlayerLookTargetListFieldPtr == IntPtr.Zero || auraMonoFieldGetValue == null || auraMonoArrayLength == null)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr playerObj = auraMonoRuntimeInvoke(this.auraMonoInteractGetPlayerMethodPtr, interactObj, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || playerObj == IntPtr.Zero)
            {
                return false;
            }

            unsafe
            {
                IntPtr decisionsArray = IntPtr.Zero;
                auraMonoFieldGetValue(playerObj, this.auraMonoLocalPlayerLookDecisionsFieldPtr, (IntPtr)(&decisionsArray));
                if (decisionsArray == IntPtr.Zero)
                {
                    return false;
                }

                ulong length = auraMonoArrayLength(decisionsArray).ToUInt64();
                if (AuraFarmDebugLogs)
                {
                    ModLogger.Msg("[AuraFarm] Mono _lookAtDecisions length=" + length);
                }

                for (int i = 0; i < (int)Math.Min(length, 32UL); i++)
                {
                    IntPtr decisionObj = this.GetAuraMonoArrayValue(decisionsArray, i);
                    if (decisionObj == IntPtr.Zero || auraMonoObjectGetClass == null)
                    {
                        continue;
                    }

                    IntPtr decisionClass = auraMonoObjectGetClass(decisionObj);
                    if (decisionClass == IntPtr.Zero || decisionClass != this.auraMonoLocalPlayerLookInteractTargetClassPtr)
                    {
                        continue;
                    }

                    IntPtr targetListObj = IntPtr.Zero;
                    auraMonoFieldGetValue(decisionObj, this.auraMonoLocalPlayerLookTargetListFieldPtr, (IntPtr)(&targetListObj));
                    if (targetListObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    this.TryPopulateAuraOwnerTargetsFromMonoTargetListObject(targetListObj, output, "Mono LocalPlayerLookInteractTarget");
                    if (output.Count > 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private bool auraMonoInteractGetTargetListMethodViaClass(out IntPtr methodPtr)
        {
            methodPtr = IntPtr.Zero;
            if (this.auraMonoInteractSystemClassPtr == IntPtr.Zero || auraMonoClassGetMethodFromName == null)
            {
                return false;
            }

            methodPtr = auraMonoClassGetMethodFromName(this.auraMonoInteractSystemClassPtr, "GetInteractTargetList", 1);
            return methodPtr != IntPtr.Zero;
        }

        private bool auraMonoEntityHelperGetTargetListMethodViaClass(out IntPtr methodPtr)
        {
            methodPtr = IntPtr.Zero;
            IntPtr levelImage = this.FindAuraMonoImage(new string[] { "XDTLevelAndEntity", "XDTLevelAndEntity.dll" });
            if (levelImage == IntPtr.Zero || auraMonoClassFromName == null || auraMonoClassGetMethodFromName == null)
            {
                return false;
            }

            IntPtr entityHelperClass = auraMonoClassFromName(levelImage, "XDTLevelAndEntity.Utils", "EntityHelper");
            if (entityHelperClass == IntPtr.Zero)
            {
                return false;
            }

            methodPtr = auraMonoClassGetMethodFromName(entityHelperClass, "GetPlayerInteractTargetList", 1);
            return methodPtr != IntPtr.Zero;
        }

        private bool TryPopulateAuraOwnerTargetsFromMonoListCall(IntPtr methodPtr, IntPtr obj, IntPtr listObj, HashSet<uint> output, string label)
        {
            if (methodPtr == IntPtr.Zero || listObj == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            unsafe
            {
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = listObj;
                IntPtr ret = auraMonoRuntimeInvoke(methodPtr, obj, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    this.auraLastError = label + " threw.";
                    return false;
                }

                int returnedCount = 0;
                if (ret != IntPtr.Zero)
                {
                    this.TryUnboxMonoInt32(ret, out returnedCount);
                }

                int listCount = this.GetAuraMonoUInt64ListCount(listObj);
                int finalCount = listCount > 0 ? listCount : returnedCount;

                if (AuraFarmDebugLogs)
                {
                    ModLogger.Msg("[AuraFarm] " + label + " returnedCount=" + returnedCount + " listCount=" + listCount);
                }

                if (finalCount <= 0)
                {
                    return false;
                }

                for (int i = 0; i < finalCount; i++)
                {
                    ulong levelObjectId = this.GetAuraMonoUInt64ListItem(listObj, i);
                    if (levelObjectId == 0UL)
                    {
                        continue;
                    }

                    int beforeCount = output.Count;
                    this.RegisterAuraTargetFromLevelObjectId(output, levelObjectId, label + "[" + i + "]");
                    if (output.Count > beforeCount)
                    {
                        if (AuraFarmDebugLogs && i < 3)
                        {
                            ModLogger.Msg("[AuraFarm] " + label + "[" + i + "] levelObjectId=" + levelObjectId);
                        }
                    }
                }
            }

            return output.Count > 0;
        }

        private void TryPopulateAuraOwnerTargetsFromMonoTargetListObject(IntPtr listObj, HashSet<uint> output, string label)
        {
            if (listObj == IntPtr.Zero)
            {
                return;
            }

            this.CacheAuraMonoUInt64ListAccessors(listObj);
            int listCount = this.GetAuraMonoUInt64ListCount(listObj);
            if (AuraFarmDebugLogs)
            {
                ModLogger.Msg("[AuraFarm] " + label + " listCount=" + listCount);
            }

            for (int i = 0; i < listCount; i++)
            {
                ulong levelObjectId = this.GetAuraMonoUInt64ListItem(listObj, i);
                if (levelObjectId == 0UL)
                {
                    continue;
                }

                int beforeCount = output.Count;
                this.RegisterAuraTargetFromLevelObjectId(output, levelObjectId, label + "[" + i + "]");
                if (output.Count > beforeCount)
                {
                    if (AuraFarmDebugLogs && i < 3)
                    {
                        ModLogger.Msg("[AuraFarm] " + label + "[" + i + "] levelObjectId=" + levelObjectId);
                    }
                }
            }
        }

        private IntPtr GetAuraMonoUInt64ListObject()
        {
            if (!this.AttachAuraMonoThread() || auraMonoStringNew == null || auraMonoRuntimeInvoke == null || this.auraMonoTypeGetTypeMethodPtr == IntPtr.Zero || this.auraMonoActivatorCreateInstanceMethodPtr == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            IntPtr typeNameStr = auraMonoStringNew(this.auraMonoRootDomain, "System.Collections.Generic.List`1[System.UInt64]");
            if (typeNameStr == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            IntPtr exc = IntPtr.Zero;
            unsafe
            {
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = typeNameStr;

                IntPtr typeObj = auraMonoRuntimeInvoke(this.auraMonoTypeGetTypeMethodPtr, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero || typeObj == IntPtr.Zero)
                {
                    this.auraLastError = "Mono Type.GetType(List<UInt64>) returned null.";
                    return IntPtr.Zero;
                }

                exc = IntPtr.Zero;
                args[0] = typeObj;
                IntPtr listObj = auraMonoRuntimeInvoke(this.auraMonoActivatorCreateInstanceMethodPtr, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero || listObj == IntPtr.Zero)
                {
                    this.auraLastError = "Mono Activator.CreateInstance(List<UInt64>) returned null.";
                    return IntPtr.Zero;
                }

                this.CacheAuraMonoUInt64ListAccessors(listObj);
                return listObj;
            }
        }

        private IntPtr GetAuraMonoSelectPriorityInfoListObject()
        {
            if (!this.AttachAuraMonoThread() || auraMonoStringNew == null || auraMonoRuntimeInvoke == null || this.auraMonoTypeGetTypeMethodPtr == IntPtr.Zero || this.auraMonoActivatorCreateInstanceMethodPtr == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            if (this.auraMonoSelectPriorityListClassPtr != IntPtr.Zero && auraMonoObjectNew != null)
            {
                try
                {
                    IntPtr listObj = auraMonoObjectNew(this.auraMonoRootDomain, this.auraMonoSelectPriorityListClassPtr);
                    if (listObj != IntPtr.Zero)
                    {
                        if (auraMonoRuntimeObjectInit != null)
                        {
                            auraMonoRuntimeObjectInit(listObj);
                        }

                        this.CacheAuraMonoSelectPriorityListAccessors(listObj);
                        return listObj;
                    }
                }
                catch
                {
                }
            }

            string[] typeCandidates = new string[]
            {
                "System.Collections.Generic.List`1[[XDTLevelAndEntity.BaseSystem.InteractSystem.SelectPriorityInfo, XDTDataAndProtocol]]",
                "System.Collections.Generic.List`1[[XDTLevelAndEntity.BaseSystem.InteractSystem.SelectPriorityInfo, XDTLevelAndEntity]]"
            };

            for (int i = 0; i < typeCandidates.Length; i++)
            {
                IntPtr typeNameStr = auraMonoStringNew(this.auraMonoRootDomain, typeCandidates[i]);
                if (typeNameStr == IntPtr.Zero)
                {
                    continue;
                }

                IntPtr exc = IntPtr.Zero;
                unsafe
                {
                    IntPtr* args = stackalloc IntPtr[1];
                    args[0] = typeNameStr;
                    IntPtr typeObj = auraMonoRuntimeInvoke(this.auraMonoTypeGetTypeMethodPtr, IntPtr.Zero, (IntPtr)args, ref exc);
                    if (exc != IntPtr.Zero || typeObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    exc = IntPtr.Zero;
                    args[0] = typeObj;
                    IntPtr listObj = auraMonoRuntimeInvoke(this.auraMonoActivatorCreateInstanceMethodPtr, IntPtr.Zero, (IntPtr)args, ref exc);
                    if (exc != IntPtr.Zero || listObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    if (auraMonoObjectGetClass != null)
                    {
                        this.auraMonoSelectPriorityListClassPtr = auraMonoObjectGetClass(listObj);
                    }

                    this.CacheAuraMonoSelectPriorityListAccessors(listObj);
                    return listObj;
                }
            }

            return IntPtr.Zero;
        }

        private void CacheAuraMonoSelectPriorityListAccessors(IntPtr listObj)
        {
            if (listObj == IntPtr.Zero || auraMonoObjectGetClass == null || auraMonoClassGetMethodFromName == null)
            {
                return;
            }

            IntPtr listClass = auraMonoObjectGetClass(listObj);
            if (listClass == IntPtr.Zero)
            {
                return;
            }

            if (this.auraMonoSelectPriorityListClassPtr == IntPtr.Zero)
            {
                this.auraMonoSelectPriorityListClassPtr = listClass;
            }

            if (this.auraMonoSelectPriorityListCountMethodPtr == IntPtr.Zero)
            {
                this.auraMonoSelectPriorityListCountMethodPtr = auraMonoClassGetMethodFromName(listClass, "get_Count", 0);
            }
            if (this.auraMonoSelectPriorityListGetItemMethodPtr == IntPtr.Zero)
            {
                this.auraMonoSelectPriorityListGetItemMethodPtr = auraMonoClassGetMethodFromName(listClass, "get_Item", 1);
            }
            if (this.auraMonoSelectPriorityListClearMethodPtr == IntPtr.Zero)
            {
                this.auraMonoSelectPriorityListClearMethodPtr = auraMonoClassGetMethodFromName(listClass, "Clear", 0);
            }
        }

        private int GetAuraMonoSelectPriorityListCount(IntPtr listObj)
        {
            if (listObj == IntPtr.Zero || this.auraMonoSelectPriorityListCountMethodPtr == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return 0;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr boxed = auraMonoRuntimeInvoke(this.auraMonoSelectPriorityListCountMethodPtr, listObj, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || boxed == IntPtr.Zero)
            {
                return 0;
            }

            this.TryUnboxMonoInt32(boxed, out int count);
            return count;
        }

        private IntPtr GetAuraMonoSelectPriorityListItem(IntPtr listObj, int index)
        {
            if (listObj == IntPtr.Zero || this.auraMonoSelectPriorityListGetItemMethodPtr == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return IntPtr.Zero;
            }

            IntPtr exc = IntPtr.Zero;
            unsafe
            {
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&index);
                return auraMonoRuntimeInvoke(this.auraMonoSelectPriorityListGetItemMethodPtr, listObj, (IntPtr)args, ref exc);
            }
        }

        private void CacheAuraMonoUInt64ListAccessors(IntPtr listObj)
        {
            if (listObj == IntPtr.Zero || auraMonoObjectGetClass == null || auraMonoClassGetMethodFromName == null)
            {
                return;
            }

            IntPtr listClass = auraMonoObjectGetClass(listObj);
            if (listClass == IntPtr.Zero)
            {
                return;
            }

            if (this.auraMonoUInt64ListCountMethodPtr == IntPtr.Zero)
            {
                this.auraMonoUInt64ListCountMethodPtr = auraMonoClassGetMethodFromName(listClass, "get_Count", 0);
            }
            if (this.auraMonoUInt64ListGetItemMethodPtr == IntPtr.Zero)
            {
                this.auraMonoUInt64ListGetItemMethodPtr = auraMonoClassGetMethodFromName(listClass, "get_Item", 1);
            }
            if (this.auraMonoUInt64ListClearMethodPtr == IntPtr.Zero)
            {
                this.auraMonoUInt64ListClearMethodPtr = auraMonoClassGetMethodFromName(listClass, "Clear", 0);
            }
        }

        private int GetAuraMonoUInt64ListCount(IntPtr listObj)
        {
            if (listObj == IntPtr.Zero || this.auraMonoUInt64ListCountMethodPtr == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return 0;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr boxed = auraMonoRuntimeInvoke(this.auraMonoUInt64ListCountMethodPtr, listObj, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || boxed == IntPtr.Zero)
            {
                return 0;
            }

            this.TryUnboxMonoInt32(boxed, out int count);
            return count;
        }

        private ulong GetAuraMonoUInt64ListItem(IntPtr listObj, int index)
        {
            if (listObj == IntPtr.Zero || this.auraMonoUInt64ListGetItemMethodPtr == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return 0UL;
            }

            IntPtr exc = IntPtr.Zero;
            unsafe
            {
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&index);
                IntPtr boxed = auraMonoRuntimeInvoke(this.auraMonoUInt64ListGetItemMethodPtr, listObj, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero || boxed == IntPtr.Zero || auraMonoObjectUnbox == null)
                {
                    return 0UL;
                }

                IntPtr raw = auraMonoObjectUnbox(boxed);
                if (raw == IntPtr.Zero)
                {
                    return 0UL;
                }

                return *(ulong*)raw;
            }
        }

        private IntPtr GetAuraMonoArrayObjectItem(IntPtr arrayObj, int index)
        {
            if (arrayObj == IntPtr.Zero || auraMonoArrayAddrWithSize == null)
            {
                return IntPtr.Zero;
            }

            IntPtr slot = auraMonoArrayAddrWithSize(arrayObj, IntPtr.Size, (UIntPtr)index);
            return slot == IntPtr.Zero ? IntPtr.Zero : Marshal.ReadIntPtr(slot);
        }

        private IntPtr GetAuraMonoInteractSystemInstance()
        {
            if (this.auraMonoInteractGetInstanceMethodPtr == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return IntPtr.Zero;
            }
            if (!this.AttachAuraMonoThread())
            {
                return IntPtr.Zero;
            }

            IntPtr exc = IntPtr.Zero;
            return auraMonoRuntimeInvoke(this.auraMonoInteractGetInstanceMethodPtr, IntPtr.Zero, IntPtr.Zero, ref exc);
        }

        private void TryCollectAuraOwnerTargetsViaInteractSystemTargetList(object interactSystem, HashSet<uint> output)
        {
            if (interactSystem == null || this.auraInteractSystemGetTargetListMethod == null)
            {
                return;
            }

            try
            {
                object listObj = this.CreateListArgumentForMethod(this.auraInteractSystemGetTargetListMethod);
                if (listObj == null)
                {
                    return;
                }

                this.auraInteractSystemGetTargetListMethod.Invoke(interactSystem, new object[] { listObj });
                this.TryAddOwnerTargetsFromLevelObjectIdList(listObj, output);
            }
            catch (Exception ex)
            {
                this.auraLastError = "GetInteractTargetList failed: " + ex.Message;
            }
        }

        private void TryCollectAuraOwnerTargetsViaEntityHelperTargetList(HashSet<uint> output)
        {
            if (this.auraEntityHelperGetTargetListMethod == null)
            {
                return;
            }

            try
            {
                object listObj = this.CreateListArgumentForMethod(this.auraEntityHelperGetTargetListMethod);
                if (listObj == null)
                {
                    return;
                }

                this.auraEntityHelperGetTargetListMethod.Invoke(null, new object[] { listObj });
                this.TryAddOwnerTargetsFromLevelObjectIdList(listObj, output);
            }
            catch (Exception ex)
            {
                this.auraLastError = "EntityHelper.GetPlayerInteractTargetList failed: " + ex.Message;
            }
        }

        private object CreateListArgumentForMethod(MethodInfo method)
        {
            if (method == null)
            {
                return null;
            }

            ParameterInfo[] parameters = method.GetParameters();
            if (parameters == null || parameters.Length != 1)
            {
                return null;
            }

            Type parameterType = parameters[0].ParameterType;
            if (parameterType == null)
            {
                return null;
            }

            try
            {
                return Activator.CreateInstance(parameterType);
            }
            catch
            {
                try
                {
                    return this.CreateFallbackUlongListInstance(parameterType);
                }
                catch
                {
                    return null;
                }
            }
        }

        private object CreateFallbackUlongListInstance(Type targetType)
        {
            if (targetType == null)
            {
                return null;
            }

            Type genericListDefinition = targetType.IsGenericType ? targetType.GetGenericTypeDefinition() : null;
            if (genericListDefinition != null && genericListDefinition.FullName != null && genericListDefinition.FullName.IndexOf("List", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                Type[] args = targetType.GetGenericArguments();
                if (args != null && args.Length == 1 && this.IsIntegerLikeType(args[0]))
                {
                    return Activator.CreateInstance(targetType);
                }
            }

            return null;
        }

        private bool IsIntegerLikeType(Type type)
        {
            if (type == null)
            {
                return false;
            }

            return type == typeof(uint) || type == typeof(ulong) || type == typeof(int) || type == typeof(long);
        }

        private void TryAddOwnerTargetsFromLevelObjectIdList(object listObj, HashSet<uint> output)
        {
            if (listObj == null)
            {
                return;
            }

            IEnumerable enumerable = listObj as IEnumerable;
            if (enumerable == null)
            {
                return;
            }

            foreach (object item in enumerable)
            {
                ulong levelObjectId;
                if (!this.TryConvertAuraLevelObjectId(item, out levelObjectId))
                {
                    continue;
                }

                this.RegisterAuraTargetFromLevelObjectId(output, levelObjectId, "ManagedLevelObjectList");
            }
        }

        private bool TryConvertAuraLevelObjectId(object value, out ulong id)
        {
            id = 0UL;
            if (value == null)
            {
                return false;
            }

            if (value is ulong)
            {
                id = (ulong)value;
                return id != 0UL;
            }

            if (value is uint)
            {
                id = (uint)value;
                return id != 0UL;
            }

            if (value is long)
            {
                long lv = (long)value;
                if (lv > 0L)
                {
                    id = (ulong)lv;
                    return true;
                }
            }

            if (value is int)
            {
                int iv = (int)value;
                if (iv > 0)
                {
                    id = (ulong)((uint)iv);
                    return true;
                }
            }

            try
            {
                id = Convert.ToUInt64(value);
                return id != 0UL;
            }
            catch
            {
                return false;
            }
        }

        private bool TryResolveOwnerIdFromLevelObjectId(ulong levelObjectId, out uint ownerId)
        {
            ownerId = 0U;
            if (levelObjectId == 0UL || this.auraEntityHelperGetLevelObjectOwnerMethod == null)
            {
                return false;
            }

            try
            {
                object raw = this.auraEntityHelperGetLevelObjectOwnerMethod.Invoke(null, new object[] { levelObjectId });
                return this.TryConvertAuraOwnerId(raw, out ownerId);
            }
            catch
            {
                return false;
            }
        }

        private void AddAuraOwnerTargetsFromUnknownContainer(object container, HashSet<uint> output)
        {
            if (container == null)
            {
                return;
            }

            IEnumerable enumerable = container as IEnumerable;
            if (enumerable != null)
            {
                foreach (object item in enumerable)
                {
                    this.TryAddAuraOwnerTargetFromUnknown(item, output);
                }

                return;
            }

            PropertyInfo keysProp = this.GetPropertyQuiet(container.GetType(), "Keys");
            if (keysProp != null)
            {
                object keys = keysProp.GetValue(container, null);
                IEnumerable keysEnumerable = keys as IEnumerable;
                if (keysEnumerable != null)
                {
                    foreach (object item in keysEnumerable)
                    {
                        this.TryAddAuraOwnerTargetFromUnknown(item, output);
                    }
                }
            }
        }

        private void TryAddAuraOwnerTargetFromUnknown(object item, HashSet<uint> output)
        {
            if (item == null)
            {
                return;
            }

            uint ownerNetId;
            if (this.TryConvertAuraOwnerId(item, out ownerNetId))
            {
                output.Add(ownerNetId);
                return;
            }

            Type itemType = item.GetType();
            PropertyInfo keyProp = this.GetPropertyQuiet(itemType, "Key");
            if (keyProp != null)
            {
                object keyValue = keyProp.GetValue(item, null);
                if (this.TryConvertAuraOwnerId(keyValue, out ownerNetId))
                {
                    output.Add(ownerNetId);
                    return;
                }
            }

            FieldInfo keyField = this.GetFieldQuiet(itemType, "key");
            if (keyField != null)
            {
                object keyValue = keyField.GetValue(item);
                if (this.TryConvertAuraOwnerId(keyValue, out ownerNetId))
                {
                    output.Add(ownerNetId);
                }
            }
        }

        private bool TryConvertAuraOwnerId(object value, out uint ownerNetId)
        {
            ownerNetId = 0U;
            if (value == null)
            {
                return false;
            }

            try
            {
                if (value is uint)
                {
                    ownerNetId = (uint)value;
                    return ownerNetId != 0U;
                }

                if (value is int)
                {
                    int intValue = (int)value;
                    if (intValue > 0)
                    {
                        ownerNetId = (uint)intValue;
                        return true;
                    }

                    return false;
                }

                if (value is ulong)
                {
                    ulong ulongValue = (ulong)value;
                    if (ulongValue > 0UL && ulongValue <= uint.MaxValue)
                    {
                        ownerNetId = (uint)ulongValue;
                        return true;
                    }

                    return false;
                }

                if (value is long)
                {
                    long longValue = (long)value;
                    if (longValue > 0L && longValue <= uint.MaxValue)
                    {
                        ownerNetId = (uint)longValue;
                        return true;
                    }

                    return false;
                }

                if (value is short)
                {
                    short shortValue = (short)value;
                    if (shortValue > 0)
                    {
                        ownerNetId = (uint)shortValue;
                        return true;
                    }

                    return false;
                }

                if (value is byte)
                {
                    byte byteValue = (byte)value;
                    if (byteValue > 0)
                    {
                        ownerNetId = byteValue;
                        return true;
                    }

                    return false;
                }

                if (value is string)
                {
                    uint parsed;
                    if (uint.TryParse((string)value, out parsed) && parsed != 0U)
                    {
                        ownerNetId = parsed;
                        return true;
                    }
                }

                PropertyInfo idProp = this.GetPropertyQuiet(value.GetType(), "ownerNetId") ?? this.GetPropertyQuiet(value.GetType(), "OwnerNetId") ?? this.GetPropertyQuiet(value.GetType(), "netId") ?? this.GetPropertyQuiet(value.GetType(), "NetId");
                if (idProp != null)
                {
                    object nestedValue = idProp.GetValue(value, null);
                    return this.TryConvertAuraOwnerId(nestedValue, out ownerNetId);
                }
            }
            catch
            {
            }

            return false;
        }

        private Type FindTypeByName(string fullName, string expectedNamespace, string shortName)
        {
            Type type = this.FindTypeInLikelyAssemblies(fullName, expectedNamespace, shortName);
            if (type != null)
            {
                return type;
            }

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Assembly assembly = assemblies[i];
                if (this.IsAuraExcludedAssembly(assembly))
                {
                    continue;
                }

                if (!this.IsAuraPreferredAssembly(assembly))
                {
                    continue;
                }

                type = this.FindTypeInAssembly(assembly, fullName, expectedNamespace, shortName);
                if (type != null)
                {
                    return type;
                }
            }

            for (int i = 0; i < assemblies.Length; i++)
            {
                Assembly assembly = assemblies[i];
                if (this.IsAuraExcludedAssembly(assembly))
                {
                    continue;
                }

                type = this.FindTypeInAssembly(assembly, fullName, expectedNamespace, shortName);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        private Type FindTypeBySignature(string classNameFragment, string preferredAssemblyFragment, bool requireResourceMethods, bool requireInteractMembers)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Assembly assembly = assemblies[i];
                if (this.IsAuraExcludedAssembly(assembly) || !this.IsAuraPreferredAssembly(assembly))
                {
                    continue;
                }

                string assemblyName = assembly.GetName().Name;
                if (!string.IsNullOrEmpty(preferredAssemblyFragment) &&
                    assemblyName.IndexOf(preferredAssemblyFragment, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                Type match = this.FindTypeBySignatureInAssembly(assembly, classNameFragment, requireResourceMethods, requireInteractMembers);
                if (match != null)
                {
                    return match;
                }
            }

            for (int i = 0; i < assemblies.Length; i++)
            {
                Assembly assembly = assemblies[i];
                if (this.IsAuraExcludedAssembly(assembly) || !this.IsAuraPreferredAssembly(assembly))
                {
                    continue;
                }

                Type match = this.FindTypeBySignatureInAssembly(assembly, classNameFragment, requireResourceMethods, requireInteractMembers);
                if (match != null)
                {
                    return match;
                }
            }

            for (int i = 0; i < assemblies.Length; i++)
            {
                Assembly assembly = assemblies[i];
                if (this.IsAuraExcludedAssembly(assembly))
                {
                    continue;
                }

                Type match = this.FindTypeBySignatureInAssembly(assembly, classNameFragment, requireResourceMethods, requireInteractMembers);
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        private Type FindTypeInLikelyAssemblies(string fullName, string expectedNamespace, string shortName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Assembly assembly = assemblies[i];
                if (this.IsAuraExcludedAssembly(assembly) || !this.IsAuraPreferredAssembly(assembly))
                {
                    continue;
                }

                Type type = this.FindTypeInAssembly(assembly, fullName, expectedNamespace, shortName);
                if (type != null)
                {
                    return type;
                }
            }

            for (int i = 0; i < assemblies.Length; i++)
            {
                Assembly assembly = assemblies[i];
                if (this.IsAuraExcludedAssembly(assembly))
                {
                    continue;
                }

                Type type = this.FindTypeInAssembly(assembly, fullName, expectedNamespace, shortName);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        private Type FindTypeBySignatureInAssembly(Assembly assembly, string classNameFragment, bool requireResourceMethods, bool requireInteractMembers)
        {
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
                return null;
            }

            if (types == null)
            {
                return null;
            }

            for (int i = 0; i < types.Length; i++)
            {
                Type candidate = types[i];
                if (candidate == null)
                {
                    continue;
                }

                if (candidate.Name.IndexOf(classNameFragment, StringComparison.OrdinalIgnoreCase) < 0 &&
                    (candidate.FullName == null || candidate.FullName.IndexOf(classNameFragment, StringComparison.OrdinalIgnoreCase) < 0))
                {
                    continue;
                }

                if (!this.NameMatchesShortType(candidate.Name, classNameFragment))
                {
                    continue;
                }

                if (requireResourceMethods && this.HasAuraResourceSignature(candidate))
                {
                    return candidate;
                }

                if (requireInteractMembers && this.HasAuraInteractSignature(candidate))
                {
                    return candidate;
                }

                if (!requireResourceMethods && !requireInteractMembers)
                {
                    return candidate;
                }
            }

            return null;
        }

        private Type FindTypeInAssembly(Assembly assembly, string fullName, string expectedNamespace, string shortName)
        {
            try
            {
                Type directType = assembly.GetType(fullName, false);
                if (directType != null)
                {
                    return directType;
                }
            }
            catch
            {
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
                return null;
            }

            if (types == null)
            {
                return null;
            }

            for (int i = 0; i < types.Length; i++)
            {
                Type candidate = types[i];
                if (candidate == null)
                {
                    continue;
                }

                if (string.Equals(candidate.FullName, fullName, StringComparison.Ordinal))
                {
                    return candidate;
                }

                if (this.FullNameMatches(candidate.FullName, fullName))
                {
                    return candidate;
                }

                if (!string.IsNullOrEmpty(candidate.FullName) &&
                    candidate.FullName.EndsWith("." + fullName, StringComparison.Ordinal))
                {
                    return candidate;
                }

                if (!string.IsNullOrEmpty(expectedNamespace) &&
                    this.NamespaceMatches(candidate.Namespace, expectedNamespace) &&
                    this.NameMatchesShortType(candidate.Name, shortName))
                {
                    return candidate;
                }

                if (!string.IsNullOrEmpty(expectedNamespace) &&
                    !string.IsNullOrEmpty(candidate.Namespace) &&
                    candidate.Namespace.EndsWith("." + expectedNamespace, StringComparison.Ordinal) &&
                    this.NameMatchesShortType(candidate.Name, shortName))
                {
                    return candidate;
                }
            }

            return null;
        }

        private bool HasAuraResourceSignature(Type candidate)
        {
            if (!this.NameMatchesShortType(candidate.Name, "ResourceProtocolManager"))
            {
                return false;
            }

            MethodInfo pickMethod = this.GetMethodQuiet(candidate, "SendPickBushCommand", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, new Type[] { typeof(uint) });
            MethodInfo attackMethod = this.GetMethodQuiet(candidate, "SendAttackTreeCommand", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, new Type[] { typeof(uint), typeof(bool) });
            return pickMethod != null || attackMethod != null;
        }

        private bool HasAuraInteractSignature(Type candidate)
        {
            if (!this.NameMatchesShortType(candidate.Name, "InteractSystem"))
            {
                return false;
            }

            MethodInfo getInstanceMethod = this.GetMethodQuiet(candidate, "get_Instance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance, Type.EmptyTypes);
            if (getInstanceMethod == null)
            {
                PropertyInfo instanceProperty = this.GetPropertyQuiet(candidate, "Instance");
                if (instanceProperty != null)
                {
                    getInstanceMethod = instanceProperty.GetGetMethod(true);
                }
            }

            if (getInstanceMethod == null)
            {
                getInstanceMethod = this.GetMethodQuiet(candidate, "GetInstance", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance, Type.EmptyTypes)
                    ?? this.GetMethodQuiet(candidate, "get_Singleton", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance, Type.EmptyTypes);
            }

            if (getInstanceMethod == null)
            {
                PropertyInfo singletonProperty = this.GetPropertyQuiet(candidate, "Singleton") ?? this.GetPropertyQuiet(candidate, "singleton");
                if (singletonProperty != null)
                {
                    getInstanceMethod = singletonProperty.GetGetMethod(true);
                }
            }

            if (getInstanceMethod == null)
            {
                return false;
            }

            bool hasCurrent = this.GetFieldQuiet(candidate, "_currentSelectTarget") != null || this.GetPropertyQuiet(candidate, "CurrentSelectTarget") != null || this.GetPropertyQuiet(candidate, "currentSelectTarget") != null;
            bool hasFocus = this.GetFieldQuiet(candidate, "_focusLevelObjects") != null || this.GetPropertyQuiet(candidate, "FocusLevelObjects") != null || this.GetPropertyQuiet(candidate, "focusLevelObjects") != null;
            bool hasSelected = this.GetFieldQuiet(candidate, "_selected") != null || this.GetPropertyQuiet(candidate, "Selected") != null || this.GetPropertyQuiet(candidate, "selected") != null;

            if (!hasCurrent && !hasFocus && !hasSelected)
            {
                this.RefreshAuraInteractCandidateMembers(candidate);
                return this.auraInteractTargetCandidateFields.Length > 0 || this.auraInteractTargetCandidateProperties.Length > 0;
            }

            return hasCurrent || hasFocus || hasSelected;
        }

        private void RefreshAuraInteractCandidateMembers()
        {
            this.RefreshAuraInteractCandidateMembers(this.auraInteractSystemType);
        }

        private void RefreshAuraInteractCandidateMembers(Type interactType)
        {
            if (interactType == null)
            {
                this.auraInteractTargetCandidateFields = Array.Empty<FieldInfo>();
                this.auraInteractTargetCandidateProperties = Array.Empty<PropertyInfo>();
                return;
            }

            List<FieldInfo> fields = new List<FieldInfo>();
            FieldInfo[] allFields = interactType.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            for (int i = 0; i < allFields.Length; i++)
            {
                FieldInfo field = allFields[i];
                if (field == null)
                {
                    continue;
                }

                if (this.IsAuraInteractCandidateName(field.Name))
                {
                    fields.Add(field);
                }
            }

            List<PropertyInfo> properties = new List<PropertyInfo>();
            PropertyInfo[] allProperties = interactType.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            for (int i = 0; i < allProperties.Length; i++)
            {
                PropertyInfo property = allProperties[i];
                if (property == null || !property.CanRead || property.GetIndexParameters().Length != 0)
                {
                    continue;
                }

                if (this.IsAuraInteractCandidateName(property.Name))
                {
                    properties.Add(property);
                }
            }

            this.auraInteractTargetCandidateFields = fields.ToArray();
            this.auraInteractTargetCandidateProperties = properties.ToArray();
        }

        private bool IsAuraInteractCandidateName(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return false;
            }

            return name.IndexOf("target", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("select", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("focus", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("interact", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private MethodInfo GetMethodQuiet(Type type, string name, BindingFlags flags, Type[] parameterTypes)
        {
            if (type == null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            const BindingFlags flatten = BindingFlags.FlattenHierarchy;
            return type.GetMethod(name, flags | flatten, null, parameterTypes ?? Type.EmptyTypes, null);
        }

        private MethodInfo GetMethodByNameAndParamCountQuiet(Type type, string name, int paramCount)
        {
            if (type == null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            MethodInfo[] methods = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            for (int i = 0; i < methods.Length; i++)
            {
                MethodInfo m = methods[i];
                if (!string.Equals(m.Name, name, StringComparison.Ordinal))
                {
                    continue;
                }

                ParameterInfo[] ps = m.GetParameters();
                if (ps != null && ps.Length == paramCount)
                {
                    return m;
                }
            }

            return null;
        }

        private bool FullNameMatches(string candidateFullName, string expectedFullName)
        {
            if (string.IsNullOrEmpty(candidateFullName) || string.IsNullOrEmpty(expectedFullName))
            {
                return false;
            }

            if (string.Equals(candidateFullName, expectedFullName, StringComparison.Ordinal))
            {
                return true;
            }

            string stripped = this.StripIl2CppPrefix(candidateFullName);
            return string.Equals(stripped, expectedFullName, StringComparison.Ordinal);
        }

        private bool NamespaceMatches(string candidateNamespace, string expectedNamespace)
        {
            if (string.IsNullOrEmpty(candidateNamespace) || string.IsNullOrEmpty(expectedNamespace))
            {
                return false;
            }

            if (string.Equals(candidateNamespace, expectedNamespace, StringComparison.Ordinal))
            {
                return true;
            }

            string stripped = this.StripIl2CppPrefix(candidateNamespace);
            return string.Equals(stripped, expectedNamespace, StringComparison.Ordinal);
        }

        private bool NameMatchesShortType(string candidateName, string expectedName)
        {
            if (string.IsNullOrEmpty(candidateName) || string.IsNullOrEmpty(expectedName))
            {
                return false;
            }

            if (string.Equals(candidateName, expectedName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            string stripped = this.StripIl2CppPrefix(candidateName);
            return string.Equals(stripped, expectedName, StringComparison.OrdinalIgnoreCase);
        }

        private string StripIl2CppPrefix(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            return value.StartsWith("Il2Cpp", StringComparison.Ordinal) ? value.Substring("Il2Cpp".Length) : value;
        }

        private PropertyInfo GetPropertyQuiet(Type type, string name)
        {
            if (type == null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            return type.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
        }

        private FieldInfo GetFieldQuiet(Type type, string name)
        {
            if (type == null || string.IsNullOrEmpty(name))
            {
                return null;
            }

            return type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
        }

        private bool IsAuraPreferredAssembly(Assembly assembly)
        {
            string assemblyName = assembly.GetName().Name;
            for (int i = 0; i < auraPreferredAssemblyNameFragments.Length; i++)
            {
                if (assemblyName.IndexOf(auraPreferredAssemblyNameFragments[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsAuraExcludedAssembly(Assembly assembly)
        {
            string assemblyName = assembly.GetName().Name;
            for (int i = 0; i < auraExcludedAssemblyNamePrefixes.Length; i++)
            {
                if (assemblyName.StartsWith(auraExcludedAssemblyNamePrefixes[i], StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

    }
}
