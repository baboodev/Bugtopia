using System;
using System.Collections.Generic;
using UnityEngine;

namespace HeartopiaMod
{
    // Auto-Cleanse Corrupted — Aura Farm x body pollution (2026-07 SeaClean update).
    //
    // While the Aura Farm works Contamination radar nodes the server can slap the "Corrupted"
    // body-pollution debuff (BuffConfig 610) on the player. The game's own cure is standing inside
    // one of the cleansing-coral polygon areas (SeaCleanConfig.bodyPollutionConfig
    // .cleanseFlowAreaTriggerIds — live triggers 3007/3008/3009): SeaCleanModule.TickCleanseFlowArea
    // matches the SELF player position against those areas every 0.2s and the 500ms
    // PlayerEnterAreaCommand self-report starts the server-side cleanse. The mod therefore sends
    // ZERO protocol commands — it only teleports into the polygon and HOLDS there until the buff
    // clears, then resumes farming.
    //
    // Detection is event-primary + poll-fallback:
    //   * BodyPollutionSyncEvent   {playerNetId u32@0, isPolluted bool@4, elapsedSeconds f32@8,
    //                               isInCleanseFlow bool@12, cleanseProgress f32@16,
    //                               areaTriggerId i32@20} => 24 B (offsets computed from the Mono
    //                               sequential layout — verify live with MasterLogCorruptionCleanse).
    //   * BodyPollutionClearedEvent {playerNetId u32@0} => 4 B (also fires on world teardown, so a
    //                               clear is only trusted after a BuffSystem.HasBuff re-query).
    //   * CleanseFlowStateChangedEvent {playerNetId u32@0, areaTriggerId i32@4,
    //                               isInCleanseFlow bool@8, cleanseProgress f32@12} => 16 B.
    //   * UpdateBuffUiEvent        {buffId i32@0} => 4 B, SELF player only (shared with the
    //                               AutoEatRepair hook — the engine multiplexes handlers per name).
    //   * Poll: BuffSystem.HasBuff(selfNetId, 610) every few seconds while the gate is on.
    //
    // Hook handlers write ONLY scalars/bools into fields (no allocation, no logging, no AuraMono);
    // the managed tick (ProcessCorruptionCleanseOnUpdate) consumes them, runs the confirm queries
    // and prints the MasterLog diagnostics. Fail-closed everywhere: any resolve failure leaves the
    // feature idle and the farm untouched.
    public partial class HeartopiaComplete
    {
        // Verbose diagnostics. Off by default — flip to verify the event offsets, the resolved
        // trigger ids/centers and the self-netId match live (also forces hook registration + the
        // buff poll + area resolve even while the farm gate is off, so it can be verified by just
        // swimming into pollution).
        private bool MasterLogCorruptionCleanse = false;

        // Persisted feature gate (saved with the unified config like seaCleanCleanNoDelay).
        private bool autoCleanseCorruptedEnabled = true;

        // "Corrupted" body-pollution debuff (TableBuffConfig id, SeaCleanConfig
        // bodyPollutionConfig.pollutionBuffId on the live build).
        private const int CorruptionBuffId = 610;

        private const float CorruptionCleanseConfirmTimeoutSeconds = 10f;   // per candidate area
        private const float CorruptionCleanseTotalTimeoutSeconds = 120f;    // whole cleanse run
        private const float CorruptionCleanseRetriggerBlockSeconds = 300f;  // after timeout/exhaust
        private const float CorruptionCleanseHoldRadiusMeters = 6f;         // XZ drift before re-teleport
        private const float CorruptionCleanseReteleportMinIntervalSeconds = 2f;
        private const float CorruptionBuffPollIntervalSeconds = 7f;         // event-miss safety net
        private const float CorruptionAreaResolveRetrySeconds = 5f;

        // Authoritative list lives in SeaCleanConfig.bodyPollutionConfig.cleanseFlowAreaTriggerIds
        // (read at runtime below); these are the live-build values used when that read fails.
        private static readonly int[] CorruptionCleanseFallbackTriggerIds = { 3007, 3008, 3009 };

        // ---- Event wiring ----------------------------------------------------------------------

        private const string CorruptionBodyPollutionSyncEventName = "XDTDataAndProtocol.Events.BodyPollutionSyncEvent";
        private const int CorruptionBodyPollutionSyncEventBytes = 24;
        private const string CorruptionBodyPollutionClearedEventName = "XDTDataAndProtocol.Events.BodyPollutionClearedEvent";
        private const int CorruptionBodyPollutionClearedEventBytes = 4;
        private const string CorruptionCleanseFlowStateEventName = "XDTDataAndProtocol.Events.CleanseFlowStateChangedEvent";
        private const int CorruptionCleanseFlowStateEventBytes = 16;
        private const string CorruptionUpdateBuffUiEventName = "XDTDataAndProtocol.Events.UpdateBuffUiEvent";
        private const int CorruptionUpdateBuffUiEventBytes = 4;

        private bool corruptionCleanseEventHooksRegistered;

        // ---- Buff state (hook fast-path + confirm-query writes) ---------------------------------
        private bool corruptionBuffActive;
        private bool corruptionInCleanseFlow;
        private float corruptionCleanseProgress;
        private float corruptionBuffLastChangedAt = -1f;
        private float corruptionNextBuffPollAt;
        private bool corruptionBuffRecheckRequested;
        private float corruptionNextBuffRecheckAllowedAt;
        private uint corruptionCachedSelfNetId;

        // ---- Raw last-event diagnostics (hook-written, allocation-free) -------------------------
        private int corruptionEvtSyncCount;
        private int corruptionEvtSyncLen;
        private uint corruptionEvtSyncNetId;
        private bool corruptionEvtSyncIsPolluted;
        private float corruptionEvtSyncElapsed;
        private bool corruptionEvtSyncInFlow;
        private float corruptionEvtSyncProgress;
        private int corruptionEvtSyncAreaId;
        private int corruptionEvtClearedCount;
        private int corruptionEvtClearedLen;
        private uint corruptionEvtClearedNetId;
        private int corruptionEvtFlowCount;
        private int corruptionEvtFlowLen;
        private uint corruptionEvtFlowNetId;
        private int corruptionEvtFlowAreaId;
        private bool corruptionEvtFlowInFlow;
        private float corruptionEvtFlowProgress;
        private int corruptionEvtBuffUi610Count;

        // ---- Cleanse-run state -------------------------------------------------------------------
        private int corruptionCleanseTargetTriggerId;
        private Vector3 corruptionCleanseTargetCenter = Vector3.zero;
        private int corruptionCleanseCandidateIndex;
        private float corruptionCleanseStartedAt = -1f;
        private float corruptionCleanseArrivedAt = -1f;
        private float corruptionCleanseNextReteleportAt;
        private float corruptionCleanseRetriggerBlockedUntil;

        private struct CorruptionCleanseArea
        {
            public int TriggerId;
            public Vector3 Center;
        }

        // Resolved (triggerId, center) list — cached per world (AuraMonoWorldEpoch), scalars only.
        private readonly List<CorruptionCleanseArea> corruptionCleanseAreas = new List<CorruptionCleanseArea>();
        private bool corruptionCleanseAreasResolved;
        private int corruptionCleanseAreasWorldEpoch = -1;
        private float corruptionNextAreaResolveAt;
        private float corruptionNextAreaResolveFailLogAt;
        private string corruptionCleanseAreaSource = "none";

        // Nearest-first candidate order for the CURRENT run (rebuilt on every begin).
        private readonly List<CorruptionCleanseArea> corruptionCleanseOrderedCandidates = new List<CorruptionCleanseArea>();

        // AuraMono class/method pointers (image lifetime — safe to cache raw).
        private IntPtr corruptionBuffSystemClass = IntPtr.Zero;
        private IntPtr corruptionBuffHasBuffMethod = IntPtr.Zero;
        private IntPtr corruptionMetaAreaServiceClass = IntPtr.Zero;

        private float corruptionNextMasterLogAt;

        private void CorruptionCleanseLog(string message)
        {
            if (!this.MasterLogCorruptionCleanse)
            {
                return;
            }

            ModLogger.Msg("[CorruptionCleanse] " + message);
        }

        // Called every frame from OnUpdate (beside ProcessSeaCleanQteOnUpdate). Registers the event
        // hooks once the gate first passes, keeps the buff state fresh (handler-requested rechecks +
        // the periodic HasBuff poll) and prints the MasterLog diagnostics.
        private void ProcessCorruptionCleanseOnUpdate()
        {
            bool gate = this.autoFarmActive && this.showContaminatedRadar && this.autoCleanseCorruptedEnabled;
            bool wantsTracking = gate
                || this.farmState == HeartopiaComplete.AutoFarmState.CleansingCorruption
                || this.MasterLogCorruptionCleanse;
            if (!wantsTracking)
            {
                return;
            }

            this.EnsureCorruptionCleanseEventHooks();

            float now = Time.unscaledTime;
            bool recheck = this.corruptionBuffRecheckRequested && now >= this.corruptionNextBuffRecheckAllowedAt;
            if (recheck || now >= this.corruptionNextBuffPollAt)
            {
                this.corruptionBuffRecheckRequested = false;
                this.corruptionNextBuffRecheckAllowedAt = now + 1f;
                this.corruptionNextBuffPollAt = now + CorruptionBuffPollIntervalSeconds;
                if (this.TryQueryCorruptionBuffActive(out bool buffActive) && buffActive != this.corruptionBuffActive)
                {
                    this.corruptionBuffActive = buffActive;
                    this.corruptionBuffLastChangedAt = now;
                    if (!buffActive)
                    {
                        this.corruptionInCleanseFlow = false;
                        this.corruptionCleanseProgress = 0f;
                    }

                    this.CorruptionCleanseLog("HasBuff(" + CorruptionBuffId + ") => " + buffActive + " (confirm query)");
                }
            }

            // With the diag flag on, resolve the areas eagerly too so the parent can verify the
            // trigger ids + centers without running the whole farm (throttled internally).
            if (this.MasterLogCorruptionCleanse)
            {
                this.TryResolveCorruptionCleanseAreas();
            }

            this.LogCorruptionCleanseMasterDiag(now);
        }

        // Idempotent — the engine multiplexes multiple handlers per event name (UpdateBuffUiEvent is
        // shared with AutoEatRepair). Handlers self-gate and write scalars only.
        private void EnsureCorruptionCleanseEventHooks()
        {
            if (this.corruptionCleanseEventHooksRegistered)
            {
                return;
            }

            this.corruptionCleanseEventHooksRegistered = true;
            this.RegisterGameEventHook(CorruptionBodyPollutionSyncEventName, CorruptionBodyPollutionSyncEventBytes, this.OnCorruptionBodyPollutionSyncEvent);
            this.RegisterGameEventHook(CorruptionBodyPollutionClearedEventName, CorruptionBodyPollutionClearedEventBytes, this.OnCorruptionBodyPollutionClearedEvent);
            this.RegisterGameEventHook(CorruptionCleanseFlowStateEventName, CorruptionCleanseFlowStateEventBytes, this.OnCorruptionCleanseFlowStateChangedEvent);
            this.RegisterGameEventHook(CorruptionUpdateBuffUiEventName, CorruptionUpdateBuffUiEventBytes, this.OnCorruptionUpdateBuffUiEvent);
            ModLogger.Msg("[CorruptionCleanse] event hooks registered (BodyPollutionSync/Cleared, CleanseFlowStateChanged, UpdateBuffUi).");
        }

        // BodyPollutionSyncEvent — primary signal (active + in-flow + progress in one). Carries a
        // playerNetId, so it fires for peers too: only a self-attributed event drives state.
        private void OnCorruptionBodyPollutionSyncEvent(GameEventSnapshot e)
        {
            if (!this.autoCleanseCorruptedEnabled && !this.MasterLogCorruptionCleanse)
            {
                return;
            }

            this.corruptionEvtSyncCount++;
            this.corruptionEvtSyncLen = e.Length;
            uint netId = e.ReadUInt32(0);
            bool isPolluted = e.ReadBool(4);
            bool inFlow = e.ReadBool(12);
            float progress = e.ReadSingle(16);
            this.corruptionEvtSyncNetId = netId;
            this.corruptionEvtSyncIsPolluted = isPolluted;
            this.corruptionEvtSyncElapsed = e.ReadSingle(8);
            this.corruptionEvtSyncInFlow = inFlow;
            this.corruptionEvtSyncProgress = progress;
            this.corruptionEvtSyncAreaId = e.ReadInt32(20);

            uint self = this.corruptionCachedSelfNetId;
            if (e.Length >= CorruptionBodyPollutionSyncEventBytes && self != 0U && netId == self)
            {
                // Self fast path. The offsets are computed (not live-confirmed), so a confirm
                // query is still requested — a wrong layout self-heals off the HasBuff poll.
                if (isPolluted != this.corruptionBuffActive)
                {
                    this.corruptionBuffActive = isPolluted;
                    this.corruptionBuffLastChangedAt = Time.unscaledTime;
                }

                this.corruptionInCleanseFlow = inFlow;
                this.corruptionCleanseProgress = progress;
                this.corruptionBuffRecheckRequested = true;
            }
            else if (self == 0U)
            {
                this.corruptionBuffRecheckRequested = true; // can't attribute yet — let the tick query
            }
        }

        // Terminal — but it also fires on world teardown, so it only requests a HasBuff confirm.
        private void OnCorruptionBodyPollutionClearedEvent(GameEventSnapshot e)
        {
            if (!this.autoCleanseCorruptedEnabled && !this.MasterLogCorruptionCleanse)
            {
                return;
            }

            this.corruptionEvtClearedCount++;
            this.corruptionEvtClearedLen = e.Length;
            uint netId = e.ReadUInt32(0);
            this.corruptionEvtClearedNetId = netId;
            uint self = this.corruptionCachedSelfNetId;
            if (self == 0U || netId == self)
            {
                this.corruptionBuffRecheckRequested = true;
            }
        }

        // "We're inside the area and cleansing" confirmation + live progress.
        private void OnCorruptionCleanseFlowStateChangedEvent(GameEventSnapshot e)
        {
            if (!this.autoCleanseCorruptedEnabled && !this.MasterLogCorruptionCleanse)
            {
                return;
            }

            this.corruptionEvtFlowCount++;
            this.corruptionEvtFlowLen = e.Length;
            uint netId = e.ReadUInt32(0);
            int areaId = e.ReadInt32(4);
            bool inFlow = e.ReadBool(8);
            float progress = e.ReadSingle(12);
            this.corruptionEvtFlowNetId = netId;
            this.corruptionEvtFlowAreaId = areaId;
            this.corruptionEvtFlowInFlow = inFlow;
            this.corruptionEvtFlowProgress = progress;

            uint self = this.corruptionCachedSelfNetId;
            if (e.Length >= CorruptionCleanseFlowStateEventBytes && self != 0U && netId == self)
            {
                this.corruptionInCleanseFlow = inFlow;
                this.corruptionCleanseProgress = progress;
            }
        }

        // Self-player buff add/update/remove (id only, no direction) — re-query on 610.
        private void OnCorruptionUpdateBuffUiEvent(GameEventSnapshot e)
        {
            if (!this.autoCleanseCorruptedEnabled && !this.MasterLogCorruptionCleanse)
            {
                return;
            }

            if (e.ReadInt32(0) != CorruptionBuffId)
            {
                return;
            }

            this.corruptionEvtBuffUi610Count++;
            this.corruptionBuffRecheckRequested = true;
        }

        // BuffSystem.HasBuff(selfNetId, 610) via the DataModule<BuffSystem>.Instance singleton.
        // Returns false (state untouched) on ANY resolve failure — fail-closed.
        private unsafe bool TryQueryCorruptionBuffActive(out bool active)
        {
            active = false;
            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
                {
                    return false;
                }

                if (!this.TryResolveSelfPlayerNetId(out uint selfNetId) || selfNetId == 0U)
                {
                    return false;
                }

                this.corruptionCachedSelfNetId = selfNetId; // handlers compare against this scalar

                if (this.corruptionBuffSystemClass == IntPtr.Zero)
                {
                    this.corruptionBuffSystemClass = this.FindAuraMonoClassByFullName("XDTGameSystem.GameplaySystem.Buff.BuffSystem");
                    if (this.corruptionBuffSystemClass == IntPtr.Zero)
                    {
                        this.corruptionBuffSystemClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTGameSystem.GameplaySystem.Buff", "BuffSystem");
                    }
                }

                if (this.corruptionBuffSystemClass == IntPtr.Zero)
                {
                    return false;
                }

                if (this.corruptionBuffHasBuffMethod == IntPtr.Zero)
                {
                    this.corruptionBuffHasBuffMethod = this.FindAuraMonoMethodOnHierarchy(this.corruptionBuffSystemClass, "HasBuff", 2);
                }

                if (this.corruptionBuffHasBuffMethod == IntPtr.Zero)
                {
                    return false;
                }

                // Level-scoped module instance — resolve fresh per query, never cached across frames.
                IntPtr instance = this.TryGetAuraMonoDataModuleInstance(this.corruptionBuffSystemClass);
                if (instance == IntPtr.Zero)
                {
                    return false;
                }

                uint instancePin = AuraMonoPinNew(instance);
                try
                {
                    uint netIdArg = selfNetId;
                    int buffIdArg = CorruptionBuffId;
                    IntPtr* args = stackalloc IntPtr[2];
                    args[0] = (IntPtr)(&netIdArg);
                    args[1] = (IntPtr)(&buffIdArg);
                    IntPtr exc = IntPtr.Zero;
                    IntPtr boxed = auraMonoRuntimeInvoke(this.corruptionBuffHasBuffMethod, instance, (IntPtr)args, ref exc);
                    if (exc != IntPtr.Zero || boxed == IntPtr.Zero)
                    {
                        return false;
                    }

                    return this.TryUnboxMonoBoolean(boxed, out active);
                }
                finally
                {
                    AuraMonoPinFree(instancePin);
                }
            }
            catch
            {
                return false;
            }
        }

        // ---- Coral-area resolution ---------------------------------------------------------------

        // Resolve the (triggerId, center) list. Trigger ids come from the runtime SeaCleanConfig
        // (fallback: hardcoded live-build ids); centers come from MetaAreaClientService
        // .GetTriggerAreaCenter (the game's own polygon centroid), with a Managers._serviceDic →
        // IConfigManager → TriggerAssets walk as the fallback. Cached per world epoch; fail-closed
        // to an empty list (feature idles, farm untouched).
        private bool TryResolveCorruptionCleanseAreas()
        {
            int epoch = HeartopiaComplete.AuraMonoWorldEpoch;
            if (this.corruptionCleanseAreasWorldEpoch != epoch)
            {
                this.corruptionCleanseAreasWorldEpoch = epoch;
                this.corruptionCleanseAreasResolved = false;
                this.corruptionCleanseAreas.Clear();
                this.corruptionNextAreaResolveAt = 0f;
            }

            if (this.corruptionCleanseAreasResolved && this.corruptionCleanseAreas.Count > 0)
            {
                return true;
            }

            float now = Time.unscaledTime;
            if (now < this.corruptionNextAreaResolveAt)
            {
                return false;
            }

            this.corruptionNextAreaResolveAt = now + CorruptionAreaResolveRetrySeconds;

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    return false;
                }

                List<int> triggerIds = new List<int>(4);
                bool idsFromConfig = this.TryReadCorruptionCleanseTriggerIds(triggerIds);
                if (triggerIds.Count == 0)
                {
                    triggerIds.AddRange(CorruptionCleanseFallbackTriggerIds);
                }

                this.corruptionCleanseAreas.Clear();
                string centerSource = "service";
                if (!this.TryResolveCorruptionAreaCentersViaService(triggerIds, this.corruptionCleanseAreas)
                    || this.corruptionCleanseAreas.Count == 0)
                {
                    this.corruptionCleanseAreas.Clear();
                    centerSource = "config-walk";
                    this.TryResolveCorruptionAreaCentersViaConfigWalk(triggerIds, this.corruptionCleanseAreas);
                }

                if (this.corruptionCleanseAreas.Count == 0)
                {
                    if (now >= this.corruptionNextAreaResolveFailLogAt)
                    {
                        this.corruptionNextAreaResolveFailLogAt = now + 60f;
                        ModLogger.Msg("[CorruptionCleanse] cleanse areas unresolved (ids=" + string.Join(",", triggerIds)
                            + " from " + (idsFromConfig ? "config" : "fallback") + ") — feature idle.");
                    }

                    return false;
                }

                this.corruptionCleanseAreasResolved = true;
                this.corruptionCleanseAreaSource = centerSource + "/" + (idsFromConfig ? "config-ids" : "fallback-ids");
                System.Text.StringBuilder sb = new System.Text.StringBuilder(160);
                sb.Append("[CorruptionCleanse] resolved ").Append(this.corruptionCleanseAreas.Count)
                    .Append(" cleanse area(s) via ").Append(this.corruptionCleanseAreaSource).Append(": ");
                for (int i = 0; i < this.corruptionCleanseAreas.Count; i++)
                {
                    if (i > 0)
                    {
                        sb.Append(", ");
                    }

                    sb.Append(this.corruptionCleanseAreas[i].TriggerId).Append('@').Append(this.corruptionCleanseAreas[i].Center.ToString("F1"));
                }

                ModLogger.Msg(sb.ToString());
                return true;
            }
            catch (Exception ex)
            {
                if (now >= this.corruptionNextAreaResolveFailLogAt)
                {
                    this.corruptionNextAreaResolveFailLogAt = now + 60f;
                    ModLogger.Msg("[CorruptionCleanse] area resolve failed: " + ex.Message);
                }

                return false;
            }
        }

        // Managers._serviceDic[IConfigManager].manager walk (pin-for-pin from
        // QuestAssistantTryGetNavPointConfigPosition). On success the manager object is PINNED —
        // the caller must AuraMonoPinFree(managerPin) in a finally.
        private unsafe bool TryResolveCorruptionConfigManager(out IntPtr configManagerObj, out uint managerPin, out string status)
        {
            configManagerObj = IntPtr.Zero;
            managerPin = 0U;
            status = "AuraMono unavailable";
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null
                || auraMonoObjectGetClass == null || auraMonoFieldGetValueObject == null || this.auraMonoRootDomain == IntPtr.Zero)
            {
                return false;
            }

            if (!this.TryCreateAuraMonoSystemTypeObjectFromClass("XDTDataAndProtocol.Config.IConfigManager", out IntPtr configTypeObj) || configTypeObj == IntPtr.Zero)
            {
                status = "IConfigManager Type object unresolved";
                return false;
            }

            IntPtr managersClass = this.FindAuraMonoClassByFullName("XDTGame.Framework.Managers");
            if (managersClass == IntPtr.Zero)
            {
                managersClass = this.FindAuraMonoClassAcrossLoadedAssemblies("XDTGame.Framework", "Managers");
            }

            IntPtr serviceDicField = managersClass != IntPtr.Zero ? this.FindAuraMonoFieldOnHierarchy(managersClass, "_serviceDic") : IntPtr.Zero;
            if (serviceDicField == IntPtr.Zero)
            {
                status = "Managers._serviceDic field unresolved";
                return false;
            }

            IntPtr serviceDicObj = auraMonoFieldGetValueObject(this.auraMonoRootDomain, serviceDicField, IntPtr.Zero);
            if (serviceDicObj == IntPtr.Zero)
            {
                status = "Managers._serviceDic is null (Managers not started?)";
                return false;
            }

            uint dicPin = AuraMonoPinNew(serviceDicObj);
            IntPtr serviceObj;
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
                args[0] = configTypeObj;                 // Type key (reference) — object ptr directly.
                args[1] = (IntPtr)(&localServiceObj);    // out ServiceObject (reference out) — ptr to local.
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
                    status = "IConfigManager not registered in _serviceDic";
                    return false;
                }

                serviceObj = localServiceObj;
            }
            finally
            {
                AuraMonoPinFree(dicPin);
            }

            uint serviceObjPin = AuraMonoPinNew(serviceObj);
            try
            {
                if (!this.TryGetMonoObjectMember(serviceObj, "manager", out configManagerObj) || configManagerObj == IntPtr.Zero)
                {
                    status = "ServiceObject.manager (ConfigManager) null";
                    return false;
                }

                managerPin = AuraMonoPinNew(configManagerObj);
                status = "ok";
                return true;
            }
            finally
            {
                AuraMonoPinFree(serviceObjPin);
            }
        }

        // ConfigManager.SeaCleanConfig.bodyPollutionConfig.cleanseFlowAreaTriggerIds (List<int>).
        private bool TryReadCorruptionCleanseTriggerIds(List<int> ids)
        {
            uint managerPin = 0U;
            try
            {
                if (!this.TryResolveCorruptionConfigManager(out IntPtr configManagerObj, out managerPin, out string status))
                {
                    this.CorruptionCleanseLog("trigger-id config read unavailable: " + status);
                    return false;
                }

                // Auto-property → field probe misses, the get_SeaCleanConfig getter resolves it.
                if (!this.TryGetMonoObjectMember(configManagerObj, "SeaCleanConfig", out IntPtr seaCleanConfigObj) || seaCleanConfigObj == IntPtr.Zero)
                {
                    this.CorruptionCleanseLog("ConfigManager.SeaCleanConfig null/unreadable");
                    return false;
                }

                uint configPin = AuraMonoPinNew(seaCleanConfigObj);
                try
                {
                    if (!this.TryGetMonoObjectMember(seaCleanConfigObj, "bodyPollutionConfig", out IntPtr bodyConfigObj) || bodyConfigObj == IntPtr.Zero)
                    {
                        this.CorruptionCleanseLog("SeaCleanConfig.bodyPollutionConfig null/unreadable");
                        return false;
                    }

                    uint bodyPin = AuraMonoPinNew(bodyConfigObj);
                    try
                    {
                        if (!this.TryGetMonoObjectMember(bodyConfigObj, "cleanseFlowAreaTriggerIds", out IntPtr idListObj) || idListObj == IntPtr.Zero)
                        {
                            this.CorruptionCleanseLog("bodyPollutionConfig.cleanseFlowAreaTriggerIds null/unreadable");
                            return false;
                        }

                        List<IntPtr> items = new List<IntPtr>();
                        List<uint> pins = new List<uint>();
                        try
                        {
                            if (!this.TryEnumerateAuraMonoCollectionItems(idListObj, items, pins))
                            {
                                return false;
                            }

                            for (int i = 0; i < items.Count; i++)
                            {
                                if (this.TryUnboxCorruptionInt32(items[i], out int id) && id > 0 && !ids.Contains(id))
                                {
                                    ids.Add(id);
                                }
                            }
                        }
                        finally
                        {
                            FreeAuraMonoPins(pins);
                        }

                        return ids.Count > 0;
                    }
                    finally
                    {
                        AuraMonoPinFree(bodyPin);
                    }
                }
                finally
                {
                    AuraMonoPinFree(configPin);
                }
            }
            catch
            {
                return false;
            }
            finally
            {
                if (managerPin != 0U)
                {
                    AuraMonoPinFree(managerPin);
                }
            }
        }

        // PREFERRED center path: EcsService.TryGet<MetaAreaClientService> (the proven DailyClaims
        // inflate — the same call the game's own SeaCleanModule makes) → GetTriggerAreaCenter(id)
        // per trigger. Returns the game's own polygon centroid; Vector3.zero = unknown trigger.
        private unsafe bool TryResolveCorruptionAreaCentersViaService(List<int> triggerIds, List<CorruptionCleanseArea> output)
        {
            try
            {
                if (auraMonoRuntimeInvoke == null || auraMonoObjectUnbox == null || auraMonoObjectGetClass == null)
                {
                    return false;
                }

                if (this.corruptionMetaAreaServiceClass == IntPtr.Zero)
                {
                    this.corruptionMetaAreaServiceClass = this.FindAuraMonoClassByFullName("ClientSystem.Area.MetaAreaClientService");
                    if (this.corruptionMetaAreaServiceClass == IntPtr.Zero)
                    {
                        this.corruptionMetaAreaServiceClass = this.FindAuraMonoClassAcrossLoadedAssemblies("ClientSystem.Area", "MetaAreaClientService");
                    }
                }

                if (this.corruptionMetaAreaServiceClass == IntPtr.Zero)
                {
                    this.CorruptionCleanseLog("MetaAreaClientService class unresolved");
                    return false;
                }

                if (!this.TryDailyClaimsAuraMonoEcsTryGet(this.corruptionMetaAreaServiceClass, false, out IntPtr serviceObj, out string tryGetStatus)
                    || serviceObj == IntPtr.Zero)
                {
                    this.CorruptionCleanseLog("EcsService.TryGet<MetaAreaClientService> failed: " + tryGetStatus);
                    return false;
                }

                uint servicePin = AuraMonoPinNew(serviceObj);
                try
                {
                    // The game gates its own use on isInited — mirror it when readable.
                    if (this.TryGetMonoBoolMember(serviceObj, "isInited", out bool inited) && !inited)
                    {
                        this.CorruptionCleanseLog("MetaAreaClientService not inited yet");
                        return false;
                    }

                    IntPtr runtimeClass = auraMonoObjectGetClass(serviceObj);
                    IntPtr centerMethod = runtimeClass != IntPtr.Zero
                        ? this.FindAuraMonoMethodOnHierarchy(runtimeClass, "GetTriggerAreaCenter", 1)
                        : IntPtr.Zero;
                    if (centerMethod == IntPtr.Zero)
                    {
                        this.CorruptionCleanseLog("GetTriggerAreaCenter method unresolved");
                        return false;
                    }

                    int triggerIdArg = 0;
                    IntPtr* args = stackalloc IntPtr[1];
                    args[0] = (IntPtr)(&triggerIdArg);
                    for (int i = 0; i < triggerIds.Count; i++)
                    {
                        triggerIdArg = triggerIds[i];
                        IntPtr exc = IntPtr.Zero;
                        IntPtr boxed = auraMonoRuntimeInvoke(centerMethod, serviceObj, (IntPtr)args, ref exc);
                        if (exc != IntPtr.Zero || boxed == IntPtr.Zero)
                        {
                            continue;
                        }

                        IntPtr raw = auraMonoObjectUnbox(boxed);
                        if (raw == IntPtr.Zero)
                        {
                            continue;
                        }

                        Vector3 center = *(Vector3*)raw;
                        if (center == Vector3.zero)
                        {
                            continue; // trigger not present in this level's config
                        }

                        output.Add(new CorruptionCleanseArea { TriggerId = triggerIds[i], Center = center });
                    }

                    return output.Count > 0;
                }
                finally
                {
                    AuraMonoPinFree(servicePin);
                }
            }
            catch
            {
                return false;
            }
        }

        // FALLBACK center path: IConfigManager._mainGameLvlConf.TriggerAssets.triggers →
        // match triggerId → TriggerAsset.anchor. The anchor is the polygon's world-space reference
        // point (its Y bounds the area's height band). The polygon.points Vector3[] is deliberately
        // NOT enumerated here — value-type array enumeration goes through the Array.GetValue icall,
        // a known native-abort source; the primary service path already returns the true centroid.
        private bool TryResolveCorruptionAreaCentersViaConfigWalk(List<int> triggerIds, List<CorruptionCleanseArea> output)
        {
            uint managerPin = 0U;
            try
            {
                if (!this.TryResolveCorruptionConfigManager(out IntPtr configManagerObj, out managerPin, out string status))
                {
                    this.CorruptionCleanseLog("config-walk unavailable: " + status);
                    return false;
                }

                if (!this.TryGetMonoObjectMember(configManagerObj, "_mainGameLvlConf", out IntPtr levelConfObj) || levelConfObj == IntPtr.Zero)
                {
                    this.CorruptionCleanseLog("ConfigManager._mainGameLvlConf null (no scene config loaded?)");
                    return false;
                }

                uint levelPin = AuraMonoPinNew(levelConfObj);
                try
                {
                    if (!this.TryGetMonoObjectMember(levelConfObj, "TriggerAssets", out IntPtr triggerAssetsObj) || triggerAssetsObj == IntPtr.Zero)
                    {
                        this.CorruptionCleanseLog("GameLevelBaseConfig.TriggerAssets null");
                        return false;
                    }

                    uint assetsPin = AuraMonoPinNew(triggerAssetsObj);
                    try
                    {
                        if (!this.TryGetMonoObjectMember(triggerAssetsObj, "triggers", out IntPtr triggersListObj) || triggersListObj == IntPtr.Zero)
                        {
                            this.CorruptionCleanseLog("TriggerAssets.triggers null");
                            return false;
                        }

                        List<IntPtr> items = new List<IntPtr>();
                        List<uint> pins = new List<uint>();
                        try
                        {
                            if (!this.TryEnumerateAuraMonoCollectionItems(triggersListObj, items, pins))
                            {
                                return false;
                            }

                            for (int i = 0; i < items.Count; i++)
                            {
                                if (items[i] == IntPtr.Zero
                                    || !this.TryGetMonoIntMember(items[i], "triggerId", out int triggerId)
                                    || !triggerIds.Contains(triggerId))
                                {
                                    continue;
                                }

                                if (!this.TryGetMonoVector3Member(items[i], "anchor", out Vector3 anchor) || anchor == Vector3.zero)
                                {
                                    continue;
                                }

                                output.Add(new CorruptionCleanseArea { TriggerId = triggerId, Center = anchor });
                            }
                        }
                        finally
                        {
                            FreeAuraMonoPins(pins);
                        }

                        return output.Count > 0;
                    }
                    finally
                    {
                        AuraMonoPinFree(assetsPin);
                    }
                }
                finally
                {
                    AuraMonoPinFree(levelPin);
                }
            }
            catch
            {
                return false;
            }
            finally
            {
                if (managerPin != 0U)
                {
                    AuraMonoPinFree(managerPin);
                }
            }
        }

        private unsafe bool TryUnboxCorruptionInt32(IntPtr boxed, out int value)
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

        // ---- Farm-loop integration ----------------------------------------------------------------

        // Called at the top of the teleport-initiating farm states (after the Auto Repair gate).
        // Returns true when the cleanse run began — the caller breaks out of its state work.
        private bool TryBeginCorruptionCleanse()
        {
            if (!this.autoFarmActive
                || !this.showContaminatedRadar
                || !this.autoCleanseCorruptedEnabled
                || !this.corruptionBuffActive
                || this.IsAutoRepairBusy()
                || Time.unscaledTime < this.corruptionCleanseRetriggerBlockedUntil)
            {
                return false;
            }

            this.EnsureCorruptionCleanseEventHooks();

            if (!this.TryResolveCorruptionCleanseAreas() || this.corruptionCleanseAreas.Count == 0)
            {
                return false; // fail-closed: farm continues normally
            }

            if (!this.TryGetLocalPlayerPosition(out Vector3 playerPos))
            {
                return false;
            }

            this.corruptionCleanseOrderedCandidates.Clear();
            this.corruptionCleanseOrderedCandidates.AddRange(this.corruptionCleanseAreas);
            Vector3 origin = playerPos;
            this.corruptionCleanseOrderedCandidates.Sort((a, b) =>
                CorruptionXzSqrDistance(origin, a.Center).CompareTo(CorruptionXzSqrDistance(origin, b.Center)));

            CorruptionCleanseArea first = this.corruptionCleanseOrderedCandidates[0];
            float now = Time.unscaledTime;
            this.corruptionCleanseCandidateIndex = 0;
            this.corruptionCleanseTargetTriggerId = first.TriggerId;
            this.corruptionCleanseTargetCenter = first.Center;
            this.corruptionCleanseStartedAt = now;
            this.corruptionCleanseArrivedAt = now;
            this.corruptionCleanseNextReteleportAt = now + CorruptionCleanseReteleportMinIntervalSeconds;
            this.TeleportToLocation(first.Center);
            this.farmState = HeartopiaComplete.AutoFarmState.CleansingCorruption;
            this.autoFarmTimer = 0f;
            this.autoFarmStatus = "Cleansing Corrupted...";
            this.AutoFarmLog("Corruption cleanse: buff " + CorruptionBuffId + " active -> teleporting to cleanse area trigger "
                + first.TriggerId + " @ " + first.Center.ToString("F1")
                + " (" + this.corruptionCleanseOrderedCandidates.Count + " candidate(s), source=" + this.corruptionCleanseAreaSource + ")");
            return true;
        }

        // CleansingCorruption state body: confirm the cleanse flow started, hold inside the polygon
        // until the buff clears, escalate through candidates/timeouts. All timing on unscaled time
        // (the farm runs the game at 5x).
        private void RunCorruptionCleanseWait()
        {
            float now = Time.unscaledTime;

            // Feature/radar toggled off mid-run: hand control straight back to the farm.
            if (!this.autoCleanseCorruptedEnabled || !this.showContaminatedRadar)
            {
                this.FinishCorruptionCleanse("feature toggled off", false);
                return;
            }

            // Done — the buff state only flips false off a confirmed HasBuff query (or a
            // self-attributed sync event), so this is the trusted clear.
            if (!this.corruptionBuffActive)
            {
                this.FinishCorruptionCleanse("buff cleared", false);
                return;
            }

            if (now - this.corruptionCleanseStartedAt >= CorruptionCleanseTotalTimeoutSeconds)
            {
                this.FinishCorruptionCleanse("timeout", true);
                return;
            }

            if (!this.corruptionInCleanseFlow)
            {
                // Confirm phase: the game reports entry itself (0.2s area tick + 500ms
                // PlayerEnterAreaCommand); if the flow never starts here the center is off or the
                // area is wrong — try the next-nearest candidate.
                if (now - this.corruptionCleanseArrivedAt >= CorruptionCleanseConfirmTimeoutSeconds)
                {
                    this.corruptionCleanseCandidateIndex++;
                    if (this.corruptionCleanseCandidateIndex < this.corruptionCleanseOrderedCandidates.Count)
                    {
                        CorruptionCleanseArea next = this.corruptionCleanseOrderedCandidates[this.corruptionCleanseCandidateIndex];
                        this.corruptionCleanseTargetTriggerId = next.TriggerId;
                        this.corruptionCleanseTargetCenter = next.Center;
                        this.corruptionCleanseArrivedAt = now;
                        this.corruptionCleanseNextReteleportAt = now + CorruptionCleanseReteleportMinIntervalSeconds;
                        this.TeleportToLocation(next.Center);
                        this.autoFarmStatus = "Cleansing Corrupted... trying next coral area";
                        this.AutoFarmLog("Corruption cleanse: no cleanse flow -> next candidate trigger "
                            + next.TriggerId + " @ " + next.Center.ToString("F1")
                            + " (" + (this.corruptionCleanseCandidateIndex + 1) + "/" + this.corruptionCleanseOrderedCandidates.Count + ")");
                    }
                    else
                    {
                        this.FinishCorruptionCleanse("no area started the cleanse flow", true);
                    }

                    return;
                }

                this.autoFarmStatus = "Cleansing Corrupted... entering coral area";
            }
            else
            {
                float progress = this.corruptionCleanseProgress;
                float pct = progress <= 1.01f ? progress * 100f : progress; // unit unverified: 0..1 or 0..100
                this.autoFarmStatus = "Cleansing Corrupted... " + Mathf.Clamp(pct, 0f, 100f).ToString("F0") + "%";
            }

            // Hold: exiting the polygon cancels the cleanse, so drift gets teleported back —
            // throttled, and never while Auto Repair needs the player untouched.
            if (!this.IsAutoRepairBusy()
                && now >= this.corruptionCleanseNextReteleportAt
                && this.TryGetLocalPlayerPosition(out Vector3 playerPos)
                && CorruptionXzSqrDistance(playerPos, this.corruptionCleanseTargetCenter)
                    > CorruptionCleanseHoldRadiusMeters * CorruptionCleanseHoldRadiusMeters)
            {
                this.corruptionCleanseNextReteleportAt = now + CorruptionCleanseReteleportMinIntervalSeconds;
                this.TeleportToLocation(this.corruptionCleanseTargetCenter);
                this.AutoFarmLog("Corruption cleanse: drifted out of hold radius -> re-teleport to trigger " + this.corruptionCleanseTargetTriggerId);
            }
        }

        // End the run and resume the farm exactly like FinishCollectingCycle's tail (back to
        // scanning with a fresh timer). blockRetrigger keeps a failed run from ping-ponging.
        private void FinishCorruptionCleanse(string reason, bool blockRetrigger)
        {
            if (blockRetrigger)
            {
                this.corruptionCleanseRetriggerBlockedUntil = Time.unscaledTime + CorruptionCleanseRetriggerBlockSeconds;
            }

            this.AutoFarmLog("Corruption cleanse ended (" + reason + ") after "
                + Math.Max(0f, Time.unscaledTime - this.corruptionCleanseStartedAt).ToString("F0") + "s"
                + (blockRetrigger ? " — retrigger blocked for " + CorruptionCleanseRetriggerBlockSeconds.ToString("F0") + "s" : string.Empty));

            this.corruptionCleanseTargetTriggerId = 0;
            this.corruptionCleanseTargetCenter = Vector3.zero;
            this.corruptionCleanseCandidateIndex = 0;
            this.corruptionCleanseOrderedCandidates.Clear();
            this.corruptionCleanseStartedAt = -1f;
            this.corruptionCleanseArrivedAt = -1f;
            this.farmState = HeartopiaComplete.AutoFarmState.ScanningForNodes;
            this.autoFarmTimer = 0f;
        }

        // Clears all run bookkeeping (farm toggle on/off + disable-all). The feature gate and the
        // resolved area cache survive on purpose.
        private void ResetCorruptionCleanseState()
        {
            this.corruptionCleanseTargetTriggerId = 0;
            this.corruptionCleanseTargetCenter = Vector3.zero;
            this.corruptionCleanseCandidateIndex = 0;
            this.corruptionCleanseOrderedCandidates.Clear();
            this.corruptionCleanseStartedAt = -1f;
            this.corruptionCleanseArrivedAt = -1f;
            this.corruptionCleanseNextReteleportAt = 0f;
            this.corruptionCleanseRetriggerBlockedUntil = 0f;
        }

        private static float CorruptionXzSqrDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return dx * dx + dz * dz;
        }

        // Throttled MasterLog dump of everything the hooks captured — the live-verification surface
        // for the computed event offsets, the self-netId match and the resolved areas.
        private void LogCorruptionCleanseMasterDiag(float now)
        {
            if (!this.MasterLogCorruptionCleanse || now < this.corruptionNextMasterLogAt)
            {
                return;
            }

            this.corruptionNextMasterLogAt = now + 2f;

            System.Text.StringBuilder sb = new System.Text.StringBuilder(512);
            sb.Append("[CorruptionCleanse] self=").Append(this.corruptionCachedSelfNetId)
                .Append(" buff610=").Append(this.corruptionBuffActive)
                .Append(" inFlow=").Append(this.corruptionInCleanseFlow)
                .Append(" prog=").Append(this.corruptionCleanseProgress.ToString("F2"))
                .Append(" | sync#").Append(this.corruptionEvtSyncCount)
                .Append(" len=").Append(this.corruptionEvtSyncLen)
                .Append(" netId=").Append(this.corruptionEvtSyncNetId)
                .Append(" polluted=").Append(this.corruptionEvtSyncIsPolluted)
                .Append(" elapsed=").Append(this.corruptionEvtSyncElapsed.ToString("F1"))
                .Append(" flow=").Append(this.corruptionEvtSyncInFlow)
                .Append(" prog=").Append(this.corruptionEvtSyncProgress.ToString("F2"))
                .Append(" area=").Append(this.corruptionEvtSyncAreaId)
                .Append(" | cleared#").Append(this.corruptionEvtClearedCount)
                .Append(" len=").Append(this.corruptionEvtClearedLen)
                .Append(" netId=").Append(this.corruptionEvtClearedNetId)
                .Append(" | flowEvt#").Append(this.corruptionEvtFlowCount)
                .Append(" len=").Append(this.corruptionEvtFlowLen)
                .Append(" netId=").Append(this.corruptionEvtFlowNetId)
                .Append(" area=").Append(this.corruptionEvtFlowAreaId)
                .Append(" inFlow=").Append(this.corruptionEvtFlowInFlow)
                .Append(" prog=").Append(this.corruptionEvtFlowProgress.ToString("F2"))
                .Append(" | buffUi610#").Append(this.corruptionEvtBuffUi610Count)
                .Append(" | areas(").Append(this.corruptionCleanseAreaSource).Append(")=[");
            for (int i = 0; i < this.corruptionCleanseAreas.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append(", ");
                }

                sb.Append(this.corruptionCleanseAreas[i].TriggerId).Append('@').Append(this.corruptionCleanseAreas[i].Center.ToString("F0"));
            }

            sb.Append("] | farm=").Append(this.farmState)
                .Append(" phase=").Append(this.DescribeCorruptionCleansePhase())
                .Append(" blockUntil=").Append(this.corruptionCleanseRetriggerBlockedUntil.ToString("F0"));
            ModLogger.Msg(sb.ToString());
        }

        private string DescribeCorruptionCleansePhase()
        {
            if (this.farmState != HeartopiaComplete.AutoFarmState.CleansingCorruption)
            {
                return "idle";
            }

            if (!this.corruptionInCleanseFlow)
            {
                return "confirm(trigger " + this.corruptionCleanseTargetTriggerId + ", cand "
                    + (this.corruptionCleanseCandidateIndex + 1) + "/" + this.corruptionCleanseOrderedCandidates.Count + ")";
            }

            return "hold(trigger " + this.corruptionCleanseTargetTriggerId + ")";
        }
    }
}
