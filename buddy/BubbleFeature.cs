using HarmonyLib;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Collections.Generic;
using UnityEngine;

namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        private const float BubbleSpawnHeightOffset = 0.35f;

        private static readonly string[] BubbleCommandTypeNames =
        {
            "XDT.Scene.Shared.Modules.Bubble.CreateActivityEventPersonalRewardBubbleNetworkCommand",
            "EcsClient.XDT.Scene.Shared.Modules.Bubble.CreateActivityEventPersonalRewardBubbleNetworkCommand",
            "CreateActivityEventPersonalRewardBubbleNetworkCommand",
            "XDT.Scene.Shared.Modules.Bubble.CreateBubbleNetworkCommand",
            "EcsClient.XDT.Scene.Shared.Modules.Bubble.CreateBubbleNetworkCommand",
            "CreateBubbleNetworkCommand"
        };

        private bool bubbleFeatureDiscoveryLogged = false;
        private bool bubbleSendCommandPatchApplied = false;
        private bool bubbleActivitySpawnPatchApplied = false;
        private bool bubbleDailySpawnPatchApplied = false;
        private bool bubbleVisualPatchesApplied = false;
        private bool bubbleMonoResolverLogged = false;
        private IntPtr bubbleMonoCreateActivityBubbleMethodPtr = IntPtr.Zero;
        private IntPtr bubbleMonoCreateBubbleMethodPtr = IntPtr.Zero;
        private IntPtr bubbleMonoActivityEventTimeCounterFieldPtr = IntPtr.Zero;
        private float bubbleSpawnRateAccumulator = 0f;
        private bool bubbleMonoActivitySpawnHookApplied = false;
        private bool bubbleMonoDailySpawnHookApplied = false;
        private float nextBubbleFeaturePatchAttemptAt = -999f;
        private float nextBubbleFeatureProbeLogAt = -999f;
        private int bubbleFeaturePatchAttemptCount = 0;
        private const float BubbleFeaturePatchRetryIntervalSeconds = 5f;
        private const float BubbleFeatureProbeLogIntervalSeconds = 30f;

        private void InitializeBubbleFeature()
        {
            this.nextBubbleFeaturePatchAttemptAt = -999f;
            this.nextBubbleFeatureProbeLogAt = -999f;
        }

        /// <summary>
        /// Primary path: Harmony on WebRequestUtility.SendCommand, or Mono resolved CreateActivityBubble (Aura-style).
        /// </summary>
        private bool BubbleFeaturePrimaryPatchReady()
        {
            return this.bubbleSendCommandPatchApplied
                || this.bubbleMonoActivitySpawnHookApplied;
        }

        private bool BubbleFeatureNeedsHarmonyRetry()
        {
            if (!this.BubbleFeaturePrimaryPatchReady())
            {
                return true;
            }

            if (this.spawnBubbleAtPlayerEnabled && !this.BubbleFeatureSpawnAtPlayerReady())
            {
                return true;
            }

            if (this.fastBubbleGenEnabled && !this.BubbleFeatureFastGenSatisfied())
            {
                return true;
            }

            return false;
        }

        private bool BubbleFeatureSpawnAtPlayerReady()
        {
            bool activityReady = this.bubbleActivitySpawnPatchApplied || this.bubbleMonoActivitySpawnHookApplied;
            bool dailyReady = this.bubbleDailySpawnPatchApplied || this.bubbleMonoDailySpawnHookApplied;
            return activityReady && dailyReady;
        }

        private void RequestBubbleFeatureImmediateRetry()
        {
            this.nextBubbleFeaturePatchAttemptAt = -999f;
            this.nextBubbleFeatureProbeLogAt = -999f;
        }

        private bool BubbleFeatureFastGenSatisfied()
        {
            if (!this.fastBubbleGenEnabled)
            {
                return true;
            }

            return this.bubbleMonoActivityEventTimeCounterFieldPtr != IntPtr.Zero
                && this.bubbleMonoCreateActivityBubbleMethodPtr != IntPtr.Zero;
        }

        private void ProcessBubbleFeatureOnUpdate()
        {
            this.ProcessBubbleFeatureMonoRuntimeEffects();

            if (!this.BubbleFeatureNeedsHarmonyRetry())
            {
                return;
            }

            if (Time.unscaledTime < this.nextBubbleFeaturePatchAttemptAt)
            {
                return;
            }

            this.nextBubbleFeaturePatchAttemptAt = Time.unscaledTime + BubbleFeaturePatchRetryIntervalSeconds;
            this.bubbleFeaturePatchAttemptCount++;

            try
            {
                this.TryEnsureBubbleInteropAssembliesLoaded();
                this.ClearBubbleFeatureTypeMissCaches();
                this.TryResolveBubbleFeatureMono();
                this.TryApplyBubbleMonoNativeHooks();
                if (!this.bubbleSendCommandPatchApplied)
                {
                    this.TryApplyBubbleSendCommandPatch();
                }

                if (!this.bubbleActivitySpawnPatchApplied && !this.bubbleMonoActivitySpawnHookApplied)
                {
                    this.TryApplyActivityBubbleSpawnPatch();
                }

                if (!this.bubbleDailySpawnPatchApplied && !this.bubbleMonoDailySpawnHookApplied)
                {
                    this.TryApplyDailyBubbleSpawnPatch();
                }

                if (this.spawnBubbleAtPlayerEnabled && !this.bubbleVisualPatchesApplied)
                {
                    this.TryApplyBubbleVisualPatches();
                }

                if (!this.bubbleFeatureDiscoveryLogged && this.BubbleFeaturePrimaryPatchReady())
                {
                    this.bubbleFeatureDiscoveryLogged = true;
                    ModLogger.Msg(string.Format(
                        "[BubbleFeature] Ready: sendCommand={0}, monoActivitySpawnHook={1}, monoDailyHook={2}, harmony(activity={3}, daily={4}, visual={5}), monoFastTimer={6}",
                        this.bubbleSendCommandPatchApplied,
                        this.bubbleMonoActivitySpawnHookApplied,
                        this.bubbleMonoDailySpawnHookApplied,
                        this.bubbleActivitySpawnPatchApplied,
                        this.bubbleDailySpawnPatchApplied,
                        this.bubbleVisualPatchesApplied,
                        this.bubbleMonoActivityEventTimeCounterFieldPtr != IntPtr.Zero));
                }

                if (!this.BubbleFeaturePrimaryPatchReady())
                {
                    this.LogBubbleTypeProbeThrottled();
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[BubbleFeature] Patch attempt failed: " + ex.Message);
            }
        }

        private IntPtr TryGetAuraMonoMethodNativePointer(IntPtr method)
        {
            if (method == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            try
            {
                if (auraMonoCompileMethod != null)
                {
                    auraMonoCompileMethod(method);
                }

                if (auraMonoMethodGetUnmanagedThunk != null)
                {
                    IntPtr thunk = auraMonoMethodGetUnmanagedThunk(method);
                    if (thunk != IntPtr.Zero)
                    {
                        return thunk;
                    }
                }

                if (auraMonoMethodGetUnmanaged != null)
                {
                    return auraMonoMethodGetUnmanaged(method);
                }
            }
            catch
            {
            }

            return IntPtr.Zero;
        }

        private void TryApplyBubbleMonoNativeHooks()
        {
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return;
            }

            if (!this.bubbleMonoActivitySpawnHookApplied && this.bubbleMonoCreateActivityBubbleMethodPtr != IntPtr.Zero)
            {
                IntPtr nativePtr = this.TryGetAuraMonoMethodNativePointer(this.bubbleMonoCreateActivityBubbleMethodPtr);
                if (nativePtr == IntPtr.Zero)
                {
                    ModLogger.Msg("[BubbleFeature] CreateActivityBubble native pointer unavailable (compile/thunk).");
                }
                else if (BubbleFeatureNativeHooks.TryInstallCreateActivityBubbleHook(nativePtr))
                {
                    this.bubbleMonoActivitySpawnHookApplied = true;
                    this.bubbleActivitySpawnPatchApplied = true;
                    ModLogger.Msg("[OK] Mono hook ActivityEventProtocolManager.CreateActivityBubble (spawn location)");
                }
                else
                {
                    ModLogger.Msg("[BubbleFeature] CreateActivityBubble native hook install failed.");
                }
            }

            if (!this.bubbleMonoDailySpawnHookApplied && this.bubbleMonoCreateBubbleMethodPtr != IntPtr.Zero)
            {
                if (BubbleFeatureNativeHooks.TryInstallCreateBubbleHook(
                        this.TryGetAuraMonoMethodNativePointer(this.bubbleMonoCreateBubbleMethodPtr)))
                {
                    this.bubbleMonoDailySpawnHookApplied = true;
                    this.bubbleDailySpawnPatchApplied = true;
                    ModLogger.Msg("[OK] Mono hook BubbleProtocolManager.CreateBubble(Vector3) (spawn location)");
                }
            }
        }

        private void TryResolveBubbleFeatureMono()
        {
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return;
            }

            try
            {
                if (this.bubbleMonoCreateActivityBubbleMethodPtr == IntPtr.Zero)
                {
                    IntPtr activityProtocolClass = this.FindAuraMonoClassByFullName(
                        "XDTDataAndProtocol.ProtocolService.ActivityEvent.ActivityEventProtocolManager");
                    if (activityProtocolClass != IntPtr.Zero)
                    {
                        this.bubbleMonoCreateActivityBubbleMethodPtr = this.FindAuraMonoMethodOnHierarchy(
                            activityProtocolClass,
                            "CreateActivityBubble",
                            1);
                    }
                }

                if (this.bubbleMonoCreateBubbleMethodPtr == IntPtr.Zero)
                {
                    IntPtr bubbleProtocolClass = this.FindAuraMonoClassByFullName(
                        "XDTDataAndProtocol.ProtocolService.Bubble.BubbleProtocolManager");
                    if (bubbleProtocolClass != IntPtr.Zero)
                    {
                        this.bubbleMonoCreateBubbleMethodPtr = this.FindAuraMonoMethodOnHierarchy(
                            bubbleProtocolClass,
                            "CreateBubble",
                            1);
                    }
                }

                if (this.bubbleMonoActivityEventTimeCounterFieldPtr == IntPtr.Zero)
                {
                    IntPtr moduleClass = this.FindAuraMonoClassByFullName(
                        "XDTLevelAndEntity.Game.Module.ActivityEvent.ActivityEventModule");
                    if (moduleClass != IntPtr.Zero)
                    {
                        this.bubbleMonoActivityEventTimeCounterFieldPtr = this.FindAuraMonoFieldOnHierarchy(
                            moduleClass,
                            "_timeCounter");
                        if (this.bubbleMonoActivityEventTimeCounterFieldPtr == IntPtr.Zero)
                        {
                            this.bubbleMonoActivityEventTimeCounterFieldPtr = this.FindAuraMonoFieldOnHierarchy(
                                moduleClass,
                                "timeCounter");
                        }
                    }
                }

                if (!this.bubbleMonoResolverLogged
                    && (this.bubbleMonoCreateActivityBubbleMethodPtr != IntPtr.Zero
                        || this.bubbleMonoCreateBubbleMethodPtr != IntPtr.Zero))
                {
                    this.bubbleMonoResolverLogged = true;
                    ModLogger.Msg(string.Format(
                        "[BubbleFeature] Mono resolver: CreateActivityBubble={0}, CreateBubble={1}, ActivityEvent._timeCounter={2}",
                        this.bubbleMonoCreateActivityBubbleMethodPtr != IntPtr.Zero,
                        this.bubbleMonoCreateBubbleMethodPtr != IntPtr.Zero,
                        this.bubbleMonoActivityEventTimeCounterFieldPtr != IntPtr.Zero));
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[BubbleFeature] Mono resolver failed: " + ex.Message);
            }
        }

        private void ProcessBubbleFeatureMonoRuntimeEffects()
        {
            if (!this.fastBubbleGenEnabled)
            {
                return;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return;
            }

            this.TryProcessBubbleSpawnRateViaMono();
        }

        private unsafe void TryProcessBubbleSpawnRateViaMono()
        {
            float ratePerMinute = Mathf.Clamp(this.bubbleBubblesPerMinute, 0f, 100f);
            if (ratePerMinute <= 0f || this.bubbleMonoCreateActivityBubbleMethodPtr == IntPtr.Zero)
            {
                return;
            }

            if (this.bubbleMonoActivityEventTimeCounterFieldPtr != IntPtr.Zero
                && this.TryResolveAuraMonoModule(
                    "XDTLevelAndEntity.Game.Module.ActivityEvent.ActivityEventModule",
                    out IntPtr moduleObj)
                && moduleObj != IntPtr.Zero
                && auraMonoFieldSetValue != null)
            {
                float zero = 0f;
                auraMonoFieldSetValue(moduleObj, this.bubbleMonoActivityEventTimeCounterFieldPtr, (IntPtr)(&zero));
            }

            float deltaSeconds = Mathf.Max(Time.deltaTime, 0f);
            this.bubbleSpawnRateAccumulator += ratePerMinute / 60f * deltaSeconds;

            while (this.bubbleSpawnRateAccumulator >= 1f)
            {
                if (!this.TryInvokeMonoCreateActivityBubbleAt(BubbleFeaturePatches.GetSpawnPositionAtPlayer()))
                {
                    break;
                }

                this.bubbleSpawnRateAccumulator -= 1f;
            }
        }

        private unsafe bool TryInvokeMonoCreateActivityBubbleAt(Vector3 spawn)
        {
            return this.TryInvokeMonoVector3BubbleMethod(this.bubbleMonoCreateActivityBubbleMethodPtr, spawn);
        }

        private unsafe bool TryInvokeMonoCreateBubbleAt(Vector3 spawn)
        {
            return this.TryInvokeMonoVector3BubbleMethod(this.bubbleMonoCreateBubbleMethodPtr, spawn);
        }

        private unsafe bool TryInvokeMonoVector3BubbleMethod(IntPtr methodPtr, Vector3 spawn)
        {
            if (methodPtr == IntPtr.Zero
                || auraMonoRuntimeInvoke == null
                || !this.AttachAuraMonoThread()
                || spawn == Vector3.zero)
            {
                return false;
            }

            try
            {
                IntPtr exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&spawn);
                auraMonoRuntimeInvoke(methodPtr, IntPtr.Zero, (IntPtr)args, ref exc);
                return exc == IntPtr.Zero;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Keybind: spawn one activity (or daily) bubble at the player.</summary>
        public bool TrySpawnBubbleOnKeybind()
        {
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return false;
            }

            if (this.bubbleMonoCreateActivityBubbleMethodPtr == IntPtr.Zero
                && this.bubbleMonoCreateBubbleMethodPtr == IntPtr.Zero)
            {
                this.TryResolveBubbleFeatureMono();
            }

            Vector3 spawn = BubbleFeaturePatches.GetSpawnPositionAtPlayer();
            if (spawn == Vector3.zero)
            {
                return false;
            }

            if (this.TryInvokeMonoCreateActivityBubbleAt(spawn))
            {
                return true;
            }

            return this.TryInvokeMonoCreateBubbleAt(spawn);
        }

        private void TryApplyBubbleSendCommandPatch()
        {
            Type webRequestType = this.FindWebRequestUtilityRuntimeType();
            if (webRequestType == null)
            {
                return;
            }

            MethodInfo sendCommandOpen = null;
            foreach (MethodInfo method in webRequestType.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (method == null || method.Name != "SendCommand" || !method.IsGenericMethodDefinition)
                {
                    continue;
                }

                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 3)
                {
                    sendCommandOpen = method;
                    break;
                }
            }

            if (sendCommandOpen == null)
            {
                ModLogger.Msg("[BubbleFeature] WebRequestUtility.SendCommand generic not found.");
                return;
            }

            MethodInfo prefix = typeof(BubbleFeaturePatches).GetMethod(
                nameof(BubbleFeaturePatches.WebRequestUtility_SendCommand_Prefix),
                BindingFlags.Public | BindingFlags.Static);
            if (prefix == null)
            {
                return;
            }

            harmonyInstance.Patch(sendCommandOpen, new HarmonyMethod(prefix), null, null, null, null);
            this.bubbleSendCommandPatchApplied = true;
            ModLogger.Msg("[OK] Patched WebRequestUtility.SendCommand (bubble spawn location) -> " + DescribeType(webRequestType));
        }

        private void TryApplyActivityBubbleSpawnPatch()
        {
            Type protocolType = this.ResolveBubbleGameType(
                "XDTDataAndProtocol.ProtocolService.ActivityEvent.ActivityEventProtocolManager",
                "ActivityEventProtocolManager");
            if (protocolType == null)
            {
                return;
            }

            MethodInfo createMethod = protocolType.GetMethod(
                "CreateActivityBubble",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new Type[] { typeof(Vector3) },
                null);
            if (createMethod == null)
            {
                return;
            }

            MethodInfo prefix = typeof(BubbleFeaturePatches).GetMethod(
                nameof(BubbleFeaturePatches.CreateActivityBubble_Prefix),
                BindingFlags.Public | BindingFlags.Static);
            if (prefix == null)
            {
                return;
            }

            harmonyInstance.Patch(createMethod, new HarmonyMethod(prefix), null, null, null, null);
            this.bubbleActivitySpawnPatchApplied = true;
            ModLogger.Msg("[OK] Patched ActivityEventProtocolManager.CreateActivityBubble -> " + DescribeType(protocolType));
        }

        private void TryApplyDailyBubbleSpawnPatch()
        {
            Type protocolType = this.ResolveBubbleGameType(
                "XDTDataAndProtocol.ProtocolService.Bubble.BubbleProtocolManager",
                "BubbleProtocolManager");
            if (protocolType == null)
            {
                return;
            }

            MethodInfo createMethod = protocolType.GetMethod(
                "CreateBubble",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static,
                null,
                new Type[] { typeof(Vector3) },
                null);
            if (createMethod == null)
            {
                return;
            }

            MethodInfo prefix = typeof(BubbleFeaturePatches).GetMethod(
                nameof(BubbleFeaturePatches.CreateBubbleVector3_Prefix),
                BindingFlags.Public | BindingFlags.Static);
            if (prefix == null)
            {
                return;
            }

            harmonyInstance.Patch(createMethod, new HarmonyMethod(prefix), null, null, null, null);
            this.bubbleDailySpawnPatchApplied = true;
            ModLogger.Msg("[OK] Patched BubbleProtocolManager.CreateBubble(Vector3) -> " + DescribeType(protocolType));
        }

        private void TryApplyBubbleVisualPatches()
        {
            Type bubbleMoveComponentType = this.FindBubbleMoveComponentRuntimeType();
            Type bubbleComponentType = this.FindBubbleComponentRuntimeType();
            if (bubbleMoveComponentType == null || bubbleComponentType == null)
            {
                return;
            }

            bool any = false;

            MethodInfo moveOnStart = bubbleMoveComponentType.GetMethod(
                "MoveOnStart",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            MethodInfo moveOnStartPostfix = typeof(BubbleFeaturePatches).GetMethod(
                nameof(BubbleFeaturePatches.BubbleMoveComponent_MoveOnStart_Postfix),
                BindingFlags.Public | BindingFlags.Static);
            if (moveOnStart != null && moveOnStartPostfix != null)
            {
                harmonyInstance.Patch(moveOnStart, null, new HarmonyMethod(moveOnStartPostfix), null, null, null);
                ModLogger.Msg("[OK] Patched BubbleMoveComponent.MoveOnStart");
                any = true;
            }

            MethodInfo born = bubbleComponentType.GetMethod("Born", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            MethodInfo bornPrefix = typeof(BubbleFeaturePatches).GetMethod(
                nameof(BubbleFeaturePatches.BubbleComponent_Born_Prefix),
                BindingFlags.Public | BindingFlags.Static);
            if (born != null && bornPrefix != null)
            {
                harmonyInstance.Patch(born, new HarmonyMethod(bornPrefix), null, null, null, null);
                ModLogger.Msg("[OK] Patched BubbleComponent.Born");
                any = true;
            }

            MethodInfo place = bubbleComponentType.GetMethod("Place", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            MethodInfo placePrefix = typeof(BubbleFeaturePatches).GetMethod(
                nameof(BubbleFeaturePatches.BubbleComponent_Place_Prefix),
                BindingFlags.Public | BindingFlags.Static);
            if (place != null && placePrefix != null)
            {
                harmonyInstance.Patch(place, new HarmonyMethod(placePrefix), null, null, null, null);
                ModLogger.Msg("[OK] Patched BubbleComponent.Place");
                any = true;
            }

            if (any)
            {
                this.bubbleVisualPatchesApplied = true;
            }
        }

        private Type ResolveBubbleGameType(string fullName, string shortName)
        {
            this.ClearBubbleTypeMissCache(fullName, shortName);

            Type type = this.FindLoadedType(
                fullName,
                fullName + ", Assembly-CSharp",
                fullName + ", Client",
                fullName + ", GameApp",
                "Il2Cpp" + fullName,
                shortName);
            if (type != null)
            {
                return type;
            }

            string suffix = "." + shortName;
            type = this.FindLoadedTypeBySuffix(suffix, "." + shortName);
            if (type != null && BubbleTypeMatchesExpected(type, fullName, shortName))
            {
                return type;
            }

            return this.ScanAllAssembliesForBubbleType(fullName, shortName);
        }

        private Type FindBubbleMoveComponentRuntimeType()
        {
            return this.FindLoadedType(
                    "XDTLevelAndEntity.Gameplay.Component.Bubble.BubbleMoveComponent",
                    "XDTLevelAndEntity.GamePlay.Component.Bubble.BubbleMoveComponent",
                    "Il2CppXDTLevelAndEntity.Gameplay.Component.Bubble.BubbleMoveComponent",
                    "BubbleMoveComponent")
                ?? this.FindLoadedTypeBySuffix(
                    "Gameplay.Component.Bubble.BubbleMoveComponent",
                    "GamePlay.Component.Bubble.BubbleMoveComponent",
                    ".BubbleMoveComponent")
                ?? this.ScanAllAssembliesForBubbleType(
                    "XDTLevelAndEntity.Gameplay.Component.Bubble.BubbleMoveComponent",
                    "BubbleMoveComponent");
        }

        private void ClearBubbleTypeMissCache(string fullName, string shortName)
        {
            this.loadedTypeMissCacheUntil.Remove(fullName + "|" + shortName);
            this.loadedTypeMissCacheUntil.Remove(string.Join("|", fullName, shortName));
            this.loadedTypeMissCacheUntil.Remove("suffix:." + shortName);
        }

        private static bool BubbleTypeMatchesExpected(Type type, string expectedFullName, string shortName)
        {
            if (type == null)
            {
                return false;
            }

            string full = StripIl2CppTypePrefix(type.FullName ?? string.Empty);
            string expected = StripIl2CppTypePrefix(expectedFullName);
            if (string.Equals(full, expected, StringComparison.Ordinal))
            {
                return true;
            }

            if (full.EndsWith("." + shortName, StringComparison.Ordinal))
            {
                return expectedFullName.IndexOf("Bubble", StringComparison.Ordinal) >= 0
                    || expectedFullName.IndexOf("ActivityEvent", StringComparison.Ordinal) >= 0
                    || expectedFullName.IndexOf("Equip", StringComparison.Ordinal) >= 0;
            }

            return string.Equals(type.Name, shortName, StringComparison.Ordinal);
        }

        private Type ScanAllAssembliesForBubbleType(string fullName, string shortName)
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string assemblyName = assembly.GetName().Name ?? string.Empty;
                if (assemblyName.StartsWith("System", StringComparison.Ordinal) ||
                    assemblyName.StartsWith("Microsoft", StringComparison.Ordinal) ||
                    assemblyName.StartsWith("Harmony", StringComparison.Ordinal) ||
                    assemblyName == "helper")
                {
                    continue;
                }

                Type match = FindBubbleTypeInAssembly(assembly, fullName, shortName);
                if (match != null)
                {
                    return match;
                }
            }

            return null;
        }

        private static Type FindBubbleTypeInAssembly(Assembly assembly, string fullName, string shortName)
        {
            if (assembly == null)
            {
                return null;
            }

            try
            {
                Type direct = assembly.GetType(fullName, false);
                if (direct != null)
                {
                    return direct;
                }
            }
            catch
            {
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
                return null;
            }

            if (types == null)
            {
                return null;
            }

            string expected = StripIl2CppTypePrefix(fullName);
            for (int i = 0; i < types.Length; i++)
            {
                Type candidate = types[i];
                if (candidate == null)
                {
                    continue;
                }

                string candidateFull = StripIl2CppTypePrefix(candidate.FullName ?? string.Empty);
                if (string.Equals(candidateFull, expected, StringComparison.Ordinal))
                {
                    return candidate;
                }

                if (string.Equals(candidate.Name, shortName, StringComparison.Ordinal) &&
                    candidateFull.EndsWith("." + shortName, StringComparison.Ordinal))
                {
                    return candidate;
                }
            }

            return null;
        }

        private static string StripIl2CppTypePrefix(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value;
            }

            return value.StartsWith("Il2Cpp", StringComparison.Ordinal) ? value.Substring("Il2Cpp".Length) : value;
        }

        private void LogBubbleTypeProbeThrottled()
        {
            if (Time.unscaledTime < this.nextBubbleFeatureProbeLogAt)
            {
                return;
            }

            this.nextBubbleFeatureProbeLogAt = Time.unscaledTime + BubbleFeatureProbeLogIntervalSeconds;

            Type webRequestType = this.FindWebRequestUtilityRuntimeType();
            Type activityCommandType = this.FindBubbleNetworkCommandType();
            Type bubbleComponentType = this.FindBubbleComponentRuntimeType();
            Type activityModuleType = this.ResolveBubbleGameType(
                "XDTLevelAndEntity.Game.Module.ActivityEvent.ActivityEventModule",
                "ActivityEventModule");

            int assemblyCount = AppDomain.CurrentDomain.GetAssemblies().Length;
            List<string> gameAssemblyNames = new List<string>();
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string assemblyName = assembly.GetName().Name ?? string.Empty;
                if (assemblyName.IndexOf("XDT", StringComparison.OrdinalIgnoreCase) >= 0
                    || assemblyName.IndexOf("Client", StringComparison.OrdinalIgnoreCase) >= 0
                    || assemblyName.IndexOf("Assembly-CSharp", StringComparison.OrdinalIgnoreCase) >= 0
                    || assemblyName.IndexOf("Game", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    gameAssemblyNames.Add(assemblyName);
                }
            }

            ModLogger.Msg(string.Format(
                "[BubbleFeature] Retry #{0} (every {1}s): WebRequestUtility={2}, bubbleCommand={3}, BubbleComponent={4}, ActivityEventModule={5}; mono(CreateActivityBubble={6}, hook={7}); assemblies={8}, game-like=[{9}]",
                this.bubbleFeaturePatchAttemptCount,
                BubbleFeaturePatchRetryIntervalSeconds,
                webRequestType != null ? webRequestType.FullName : "null",
                activityCommandType != null ? activityCommandType.FullName : "null",
                bubbleComponentType != null ? bubbleComponentType.FullName : "null",
                activityModuleType != null ? activityModuleType.FullName : "null",
                this.bubbleMonoCreateActivityBubbleMethodPtr != IntPtr.Zero,
                this.bubbleMonoActivitySpawnHookApplied,
                assemblyCount,
                gameAssemblyNames.Count > 0 ? string.Join(", ", gameAssemblyNames.ToArray()) : "none"));

            List<string> hits = new List<string>();
            string[] needles = { "BubbleComponent", "BubbleMoveComponent", "ActivityEventProtocolManager", "CreateActivityEventPersonalRewardBubble", "CreateBubbleNetworkCommand", "WebRequestUtility" };

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string assemblyName = assembly.GetName().Name ?? string.Empty;
                if (assemblyName.StartsWith("System", StringComparison.Ordinal) ||
                    assemblyName.StartsWith("Microsoft", StringComparison.Ordinal) ||
                    assemblyName.StartsWith("Harmony", StringComparison.Ordinal) ||
                    assemblyName == "helper")
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
                    if (type == null)
                    {
                        continue;
                    }

                    string name = type.Name ?? string.Empty;
                    string fullName = type.FullName ?? name;
                    for (int n = 0; n < needles.Length; n++)
                    {
                        if (name.IndexOf(needles[n], StringComparison.OrdinalIgnoreCase) >= 0 ||
                            fullName.IndexOf(needles[n], StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            hits.Add((fullName) + "@" + assemblyName);
                            break;
                        }
                    }
                }
            }

            if (hits.Count > 0)
            {
                ModLogger.Msg("[BubbleFeature] Type probe hits (" + hits.Count + "): " + string.Join("; ", hits.ToArray()));
            }
            else
            {
                ModLogger.Msg("[BubbleFeature] Type probe: no bubble/network types in loaded assemblies yet (enter world; retries continue).");
            }
        }

        private void ClearBubbleFeatureTypeMissCaches()
        {
            string[] keys =
            {
                "XDTDataAndProtocol.ProtocolService.WebRequestUtility|WebRequestUtility",
                string.Join("|", "XDTDataAndProtocol.ProtocolService.WebRequestUtility", "WebRequestUtility"),
                string.Join("|", BubbleCommandTypeNames),
            };

            for (int i = 0; i < keys.Length; i++)
            {
                this.loadedTypeMissCacheUntil.Remove(keys[i]);
            }

            foreach (string commandName in BubbleCommandTypeNames)
            {
                this.loadedTypeMissCacheUntil.Remove(commandName);
                this.loadedTypeLookupCache.Remove(commandName);
            }

            this.loadedTypeLookupCache.Remove("XDTDataAndProtocol.ProtocolService.WebRequestUtility|WebRequestUtility");
            this.loadedTypeMissCacheUntil.Remove("suffix:.BubbleComponent");
            this.loadedTypeMissCacheUntil.Remove("suffix:.WebRequestUtility");
        }

        private void TryEnsureBubbleInteropAssembliesLoaded()
        {
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
                if (!Directory.Exists(interopDir))
                {
                    return;
                }

                string[] dllFiles = Directory.GetFiles(interopDir, "*.dll");
                for (int i = 0; i < dllFiles.Length; i++)
                {
                    string fileName = Path.GetFileNameWithoutExtension(dllFiles[i]) ?? string.Empty;
                    if (fileName.IndexOf("Assembly-CSharp", StringComparison.OrdinalIgnoreCase) < 0
                        && fileName.IndexOf("Client", StringComparison.OrdinalIgnoreCase) < 0
                        && fileName.IndexOf("GameApp", StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        continue;
                    }

                    try
                    {
                        Assembly.LoadFrom(dllFiles[i]);
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }

        private Type FindWebRequestUtilityRuntimeType()
        {
            Type type = this.FindLoadedType(
                "XDTDataAndProtocol.ProtocolService.WebRequestUtility",
                "Il2CppXDTDataAndProtocol.ProtocolService.WebRequestUtility",
                "WebRequestUtility");
            if (type != null)
            {
                return type;
            }

            string[] qualifiedNames =
            {
                "XDTDataAndProtocol.ProtocolService.WebRequestUtility",
                "Il2Cpp.XDTDataAndProtocol.ProtocolService.WebRequestUtility",
                "Il2CppXDTDataAndProtocol.ProtocolService.WebRequestUtility"
            };

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                for (int i = 0; i < qualifiedNames.Length; i++)
                {
                    try
                    {
                        Type direct = assembly.GetType(qualifiedNames[i], false);
                        if (direct != null)
                        {
                            return direct;
                        }
                    }
                    catch
                    {
                    }
                }
            }

            return this.FindTypeWithGenericSendCommand();
        }

        private Type FindTypeWithGenericSendCommand()
        {
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                string assemblyName = assembly.GetName().Name ?? string.Empty;
                if (assemblyName.StartsWith("System", StringComparison.Ordinal) ||
                    assemblyName.StartsWith("Microsoft", StringComparison.Ordinal) ||
                    assemblyName.StartsWith("Harmony", StringComparison.Ordinal) ||
                    assemblyName == "helper")
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
                    if (candidate == null)
                    {
                        continue;
                    }

                    if (string.Equals(candidate.Name, "WebRequestUtility", StringComparison.Ordinal))
                    {
                        if (this.TypeHasSendCommandGeneric(candidate))
                        {
                            return candidate;
                        }
                    }

                    if (this.TypeHasSendCommandGeneric(candidate)
                        && (candidate.Name ?? string.Empty).IndexOf("WebRequest", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return candidate;
                    }
                }
            }

            return null;
        }

        private bool TypeHasSendCommandGeneric(Type type)
        {
            if (type == null)
            {
                return false;
            }

            foreach (MethodInfo method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (method == null || method.Name != "SendCommand" || !method.IsGenericMethodDefinition)
                {
                    continue;
                }

                if (method.GetParameters().Length == 3)
                {
                    return true;
                }
            }

            return false;
        }

        private Type FindBubbleNetworkCommandType()
        {
            for (int i = 0; i < BubbleCommandTypeNames.Length; i++)
            {
                Type type = this.FindLoadedType(BubbleCommandTypeNames[i]);
                if (type != null)
                {
                    return type;
                }
            }

            return this.ScanAllAssembliesForBubbleType(
                "XDT.Scene.Shared.Modules.Bubble.CreateActivityEventPersonalRewardBubbleNetworkCommand",
                "CreateActivityEventPersonalRewardBubbleNetworkCommand");
        }

        private static class BubbleFeatureNativeHooks
        {
            [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
            private delegate void CreateBubbleVector3NativeDelegate(IntPtr locationPtr);

            private static CreateBubbleVector3NativeDelegate createActivityBubbleOriginal;
            private static CreateBubbleVector3NativeDelegate createBubbleOriginal;

            public static bool TryInstallCreateActivityBubbleHook(IntPtr nativeMethod)
            {
                return TryInstallVector3Hook(nativeMethod, CreateActivityBubbleNativeHook, ref createActivityBubbleOriginal);
            }

            public static bool TryInstallCreateBubbleHook(IntPtr nativeMethod)
            {
                return TryInstallVector3Hook(nativeMethod, CreateBubbleNativeHook, ref createBubbleOriginal);
            }

            private static bool TryInstallVector3Hook(
                IntPtr nativeMethod,
                CreateBubbleVector3NativeDelegate hook,
                ref CreateBubbleVector3NativeDelegate originalSlot)
            {
                if (nativeMethod == IntPtr.Zero || originalSlot != null)
                {
                    return originalSlot != null;
                }

                IntPtr hookPtr = Marshal.GetFunctionPointerForDelegate(hook);
                if (!BubbleMonoNativeHook.TryInstall(nativeMethod, hookPtr, out IntPtr trampoline) || trampoline == IntPtr.Zero)
                {
                    return false;
                }

                originalSlot = Marshal.GetDelegateForFunctionPointer<CreateBubbleVector3NativeDelegate>(trampoline);
                return true;
            }

            private static void CreateActivityBubbleNativeHook(IntPtr locationPtr)
            {
                RewriteLocationAndCallOriginal(locationPtr, createActivityBubbleOriginal);
            }

            private static void CreateBubbleNativeHook(IntPtr locationPtr)
            {
                RewriteLocationAndCallOriginal(locationPtr, createBubbleOriginal);
            }

            private static void RewriteLocationAndCallOriginal(IntPtr locationPtr, CreateBubbleVector3NativeDelegate original)
            {
                if (original == null)
                {
                    return;
                }

                try
                {
                    if (Instance != null && Instance.spawnBubbleAtPlayerEnabled && locationPtr != IntPtr.Zero)
                    {
                        Vector3 spawn = BubbleFeaturePatches.GetSpawnPositionAtPlayer();
                        if (spawn != Vector3.zero)
                        {
                            Marshal.StructureToPtr(spawn, locationPtr, false);
                        }
                    }

                    original(locationPtr);
                }
                catch (Exception ex)
                {
                    ModLogger.Msg("[BubbleFeature] Mono native hook: " + ex.Message);
                }
            }
        }

        public static class BubbleFeaturePatches
        {
            public static Vector3 GetSpawnPositionAtPlayer()
            {
                if (Instance == null)
                {
                    return Vector3.zero;
                }

                GameObject player = Instance.GetPlayerObject();
                if (player == null)
                {
                    return Vector3.zero;
                }

                Vector3 pos = player.transform.position;
                return pos + Vector3.up * BubbleSpawnHeightOffset;
            }

            public static void WebRequestUtility_SendCommand_Prefix(object command, bool reliable, object channel)
            {
                if (Instance == null || !Instance.spawnBubbleAtPlayerEnabled || command == null)
                {
                    return;
                }

                try
                {
                    if (!BubbleFeaturePatches.IsBubbleSpawnCommand(command))
                    {
                        return;
                    }

                    Vector3 spawn = GetSpawnPositionAtPlayer();
                    if (spawn == Vector3.zero)
                    {
                        return;
                    }

                    if (!BubbleFeaturePatches.TrySetCommandLocation(command, spawn))
                    {
                        ModLogger.Msg("[BubbleFeature] SendCommand prefix: could not set location on " + command.GetType().FullName);
                    }
                }
                catch (Exception ex)
                {
                    ModLogger.Msg("[BubbleFeature] SendCommand prefix: " + ex.Message);
                }
            }

            private static bool IsBubbleSpawnCommand(object command)
            {
                string typeName = command.GetType().Name ?? string.Empty;
                if (typeName.IndexOf("CreateActivityEventPersonalRewardBubble", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }

                if (typeName.IndexOf("CreateBubbleNetworkCommand", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }

                return typeName.IndexOf("Bubble", StringComparison.OrdinalIgnoreCase) >= 0
                    && typeName.IndexOf("NetworkCommand", StringComparison.OrdinalIgnoreCase) >= 0
                    && typeName.IndexOf("Award", StringComparison.OrdinalIgnoreCase) < 0;
            }

            private static bool TrySetCommandLocation(object command, Vector3 location)
            {
                Type commandType = command.GetType();
                FieldInfo locationField = commandType.GetField("location", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (locationField != null && locationField.FieldType == typeof(Vector3))
                {
                    locationField.SetValue(command, location);
                    return true;
                }

                Traverse traverse = Traverse.Create(command);
                if (traverse.Field("location").FieldExists())
                {
                    traverse.Field("location").SetValue(location);
                    return true;
                }

                return false;
            }

            public static void CreateActivityBubble_Prefix(ref Vector3 targetPosition)
            {
                if (Instance == null || !Instance.spawnBubbleAtPlayerEnabled)
                {
                    return;
                }

                Vector3 spawn = GetSpawnPositionAtPlayer();
                if (spawn != Vector3.zero)
                {
                    targetPosition = spawn;
                }
            }

            public static void CreateBubbleVector3_Prefix(ref Vector3 targetPosition)
            {
                CreateActivityBubble_Prefix(ref targetPosition);
            }

            public static void BubbleMoveComponent_MoveOnStart_Postfix(object __instance)
            {
                if (Instance == null || !Instance.spawnBubbleAtPlayerEnabled || __instance == null)
                {
                    return;
                }

                try
                {
                    Vector3 spawn = GetSpawnPositionAtPlayer();
                    if (spawn == Vector3.zero)
                    {
                        return;
                    }

                    Type type = __instance.GetType();
                    FieldInfo bornPosField = type.GetField("_bornPos", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (bornPosField != null)
                    {
                        bornPosField.SetValue(__instance, spawn);
                    }

                    FieldInfo flyTimeField = type.GetField("_flyTime", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (flyTimeField != null)
                    {
                        flyTimeField.SetValue(__instance, 0f);
                    }

                    FieldInfo componentDataField = type.GetField("_componentData", BindingFlags.Instance | BindingFlags.NonPublic);
                    if (componentDataField != null)
                    {
                        object dataVal = componentDataField.GetValue(__instance);
                        if (dataVal != null)
                        {
                            Type dataType = dataVal.GetType();
                            FieldInfo bornPositionField = dataType.GetField("bornPosition", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            FieldInfo tarPositionField = dataType.GetField("tarPosition", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                            if (bornPositionField != null)
                            {
                                bornPositionField.SetValue(dataVal, spawn);
                            }
                            if (tarPositionField != null)
                            {
                                tarPositionField.SetValue(dataVal, spawn);
                            }
                            componentDataField.SetValue(__instance, dataVal);
                        }
                    }
                }
                catch (Exception ex)
                {
                    ModLogger.Msg("[BubbleFeature] MoveOnStart postfix: " + ex.Message);
                }
            }

            public static void BubbleComponent_Born_Prefix(object __instance, ref Vector3 location)
            {
                if (Instance == null || !Instance.spawnBubbleAtPlayerEnabled)
                {
                    return;
                }

                Vector3 spawn = GetSpawnPositionAtPlayer();
                if (spawn != Vector3.zero)
                {
                    location = spawn;
                }
            }

            public static void BubbleComponent_Place_Prefix(object __instance, ref Vector3 location)
            {
                BubbleComponent_Born_Prefix(__instance, ref location);
            }
        }
    }
}
