using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        // Headless pumpkin carving. The curve QTE in PumpkinStatusPanel is client-only; the
        // server accepts StartMakingPumpkinCarving(base, rough) and later
        // FinishMakingPumpkinCarving(rough, List<int> of four stage percents). A successful
        // Start reply makes the game dispatch JoinPumpkinCarvingEvent, which enters
        // GamePumpkinCarvingMode and opens the panel. That event, the expire event (the vanilla
        // module would Finish with an empty score list — error 458), and the result event (the
        // success/fail toast) are swallowed for the duration of our session.
        // Snow / ice / sand stay on their current tabs; this file only owns the carve loop.

        internal static bool MasterLogPumpkinCarving = false;

        private const float PumpkinApiTickSeconds = 0.05f;
        private const float PumpkinScanBackoffSeconds = 0.25f;
        private const float PumpkinStartBackoffSeconds = 0.35f;
        private const float PumpkinResultTimeoutSeconds = 8f;
        private const float PumpkinPlaceBackoffSeconds = 1.0f;
        private const float PumpkinScanRadius = 60f;
        private const int PumpkinStageCount = 4;
        private const int PumpkinPerfectScore = 100;

        private const string PumpkinJoinEventName = "XDTDataAndProtocol.Events.JoinPumpkinCarvingEvent";
        private const string PumpkinExpireEventName = "XDTDataAndProtocol.Events.InformPumpkinCarvingExpireEvent";
        private const string PumpkinResultEventName = "XDTDataAndProtocol.Events.InformPumpkinCarvingResultEvent";
        private const int PumpkinEventBytes = 8;

        private static readonly string[] PumpkinProtocolTypeNames =
        {
            "XDTDataAndProtocol.ProtocolService.PumpkinCarving.PumpkinCarvingProtocolManager",
        };

        private static readonly string[] PumpkinProtocolImages =
        {
            "XDTDataAndProtocol", "XDTDataAndProtocol.dll",
        };

        private static readonly string[] PumpkinViewImages =
        {
            "XDTLevelAndEntity", "XDTLevelAndEntity.dll",
        };

        private enum PumpkinApiState
        {
            FindRough = 0,
            Start = 1,
            WaitResult = 2,
        }

        private bool autoPumpkinEnabled;
        private bool autoPumpkinCollect;
        private bool autoPumpkinPlace;
        private bool autoPumpkinWasEnabled;
        private KeyCode autoPumpkinHotkey = KeyCode.None;

        private bool pumpkinRegistrationsDone;
        private bool pumpkinHooksRegistered;
        private bool pumpkinResolveReady;
        private IntPtr pumpkinProtocolClass;
        private IntPtr pumpkinStartMethod;
        private IntPtr pumpkinFinishMethod;
        private IntPtr pumpkinViewClass;
        private IntPtr pumpkinBuildClass;
        private IntPtr pumpkinBuildOwnerMethod;
        private IntPtr pumpkinBuildParentMethod;
        private IntPtr pumpkinFinishViewClass;
        private IntPtr pumpkinBaseViewClass;
        private IntPtr pumpkinPoseDeleteMethod;
        private IntPtr pumpkinIntListAddMethod;
        private IntPtr pumpkinTableClass;
        private IntPtr pumpkinGetRoughMethod;
        private IntPtr pumpkinGetBaseMethod;
        private float pumpkinPlaceNextAt;
        private float pumpkinWaitStartedAt;
        private float pumpkinCollectUntil;

        private PumpkinApiState pumpkinApiState;
        private float pumpkinNextActionAt;
        private float pumpkinSessionDeadline;
        private uint pumpkinActiveNetId;
        private uint pumpkinActiveBaseNetId;
        private uint pumpkinIgnoreRoughNetId;
        private bool pumpkinResultSeen;
        private bool pumpkinResultSuccess;
        private bool pumpkinCollectPending;
        private string pumpkinStatus = "idle";
        private string pumpkinLastLoggedStatus = string.Empty;
        private int pumpkinDoneCount;
        private FeatureBreakerState pumpkinBreaker;

        private void EnsurePumpkinRegistrations()
        {
            if (this.pumpkinRegistrationsDone)
            {
                return;
            }

            this.pumpkinRegistrationsDone = true;
            this.RegisterWorldReadyCallback("pumpkin-carving", this.TryResolvePumpkinCarving);
        }

        private bool TryResolvePumpkinCarving()
        {
            if (this.pumpkinResolveReady)
            {
                return true;
            }

            this.ResolveAuraFarmRuntimeMethods();
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return false;
            }

            if (this.pumpkinProtocolClass == IntPtr.Zero)
            {
                for (int i = 0; i < PumpkinProtocolTypeNames.Length; i++)
                {
                    this.pumpkinProtocolClass = this.FindAuraMonoClassByFullName(PumpkinProtocolTypeNames[i]);
                    if (this.pumpkinProtocolClass != IntPtr.Zero)
                    {
                        break;
                    }
                }
            }

            if (this.pumpkinProtocolClass == IntPtr.Zero)
            {
                this.pumpkinProtocolClass = this.FindAuraMonoClassInImages(
                    "XDTDataAndProtocol.ProtocolService.PumpkinCarving",
                    "PumpkinCarvingProtocolManager",
                    PumpkinProtocolImages);
            }

            if (this.pumpkinViewClass == IntPtr.Zero)
            {
                this.pumpkinViewClass = this.FindAuraMonoClassInImages(
                    "XDTLevelAndEntity.Gameplay.Component.Homeland",
                    "PumpkinComponent",
                    PumpkinViewImages);
            }

            if (this.pumpkinBuildClass == IntPtr.Zero)
            {
                this.pumpkinBuildClass = this.FindAuraMonoClassInImages(
                    "XDTLevelAndEntity.Gameplay.Component.Homeland",
                    "BuildComponent",
                    PumpkinViewImages);
            }

            if (this.pumpkinFinishViewClass == IntPtr.Zero)
            {
                this.pumpkinFinishViewClass = this.FindAuraMonoClassInImages(
                    "XDTLevelAndEntity.Gameplay.Component.Homeland",
                    "PumpkinFinishComponent",
                    PumpkinViewImages);
            }

            if (this.pumpkinProtocolClass == IntPtr.Zero
                || this.pumpkinViewClass == IntPtr.Zero
                || this.pumpkinBuildClass == IntPtr.Zero
                || this.pumpkinFinishViewClass == IntPtr.Zero)
            {
                return false;
            }

            if (this.pumpkinStartMethod == IntPtr.Zero)
            {
                this.pumpkinStartMethod = this.FindAuraMonoMethodOnHierarchy(
                    this.pumpkinProtocolClass, "StartMakingPumpkinCarving", 2);
            }

            if (this.pumpkinFinishMethod == IntPtr.Zero)
            {
                this.pumpkinFinishMethod = this.FindAuraMonoMethodOnHierarchy(
                    this.pumpkinProtocolClass, "FinishMakingPumpkinCarving", 2);
            }

            if (this.pumpkinBuildOwnerMethod == IntPtr.Zero)
            {
                this.pumpkinBuildOwnerMethod = this.FindAuraMonoMethodOnHierarchy(
                    this.pumpkinBuildClass, "get_OwnerId", 0);
            }

            if (this.pumpkinBuildParentMethod == IntPtr.Zero)
            {
                this.pumpkinBuildParentMethod = this.FindAuraMonoMethodOnHierarchy(
                    this.pumpkinBuildClass, "GetFirstParentNetId", 0);
            }

            if (this.pumpkinPoseDeleteMethod == IntPtr.Zero)
            {
                IntPtr character = this.FindAuraMonoClassInImages(
                    "XDTDataAndProtocol.ProtocolService.GamePlay.Character",
                    "CharacterProtocolManager",
                    PumpkinProtocolImages);
                if (character != IntPtr.Zero)
                {
                    this.pumpkinPoseDeleteMethod = this.FindAuraMonoMethodOnHierarchy(character, "PoseDeleteBuild", 1);
                }
            }

            if (this.pumpkinStartMethod == IntPtr.Zero
                || this.pumpkinFinishMethod == IntPtr.Zero
                || this.pumpkinBuildOwnerMethod == IntPtr.Zero
                || this.pumpkinBuildParentMethod == IntPtr.Zero
                || this.pumpkinPoseDeleteMethod == IntPtr.Zero)
            {
                return false;
            }

            if (!this.pumpkinHooksRegistered)
            {
                bool join = this.RegisterGameEventHook(PumpkinJoinEventName, PumpkinEventBytes, this.OnPumpkinJoinEvent);
                bool expire = this.RegisterGameEventHook(PumpkinExpireEventName, PumpkinEventBytes, this.OnPumpkinExpireEvent);
                bool result = this.RegisterGameEventHook(PumpkinResultEventName, PumpkinEventBytes, this.OnPumpkinResultEvent);
                this.pumpkinHooksRegistered = join && expire && result;
            }

            if (!this.pumpkinHooksRegistered)
            {
                return false;
            }

            this.pumpkinResolveReady = this.IsGameEventHookInstalled(PumpkinJoinEventName)
                && this.IsGameEventHookInstalled(PumpkinExpireEventName)
                && this.IsGameEventHookInstalled(PumpkinResultEventName);
            return this.pumpkinResolveReady;
        }

        private void OnPumpkinJoinEvent(GameEventSnapshot e)
        {
            if (!MasterLogPumpkinCarving)
            {
                return;
            }

            ModLogger.Msg("[PumpkinCarving] join player=" + e.ReadUInt32(0) + " pumpkin=" + e.ReadUInt32(4));
        }

        private void OnPumpkinExpireEvent(GameEventSnapshot e)
        {
            uint netId = e.ReadUInt32(0);
            bool needReport = e.ReadBool(4);
            this.PumpkinLogTiming("expire pumpkin=" + netId + " needReport=" + needReport
                + " active=" + this.pumpkinActiveNetId);
        }

        private void OnPumpkinResultEvent(GameEventSnapshot e)
        {
            uint netId = e.ReadUInt32(0);
            bool success = e.ReadBool(4);
            bool match = netId != 0U && netId == this.pumpkinActiveNetId;
            // Stop reports the finished entity, not the rough (live: rough 19024996, result
            // 19024997 success=True). A nearby id during this session is that reply.
            bool inSession = this.pumpkinActiveNetId != 0U
                && (this.pumpkinApiState == PumpkinApiState.Start || this.pumpkinApiState == PumpkinApiState.WaitResult);
            bool near = inSession && success && !this.pumpkinResultSeen
                && netId > this.pumpkinActiveNetId
                && netId - this.pumpkinActiveNetId <= 8U;
            if (match || near)
            {
                this.pumpkinResultSeen = true;
                this.pumpkinResultSuccess = success;
            }

            this.PumpkinLogTiming("result pumpkin=" + netId + " success=" + success
                + " active=" + this.pumpkinActiveNetId + " match=" + match + " near=" + near);
        }

        private void ProcessPumpkinCarvingOnUpdate()
        {
            this.EnsurePumpkinRegistrations();
            if (!this.autoPumpkinEnabled)
            {
                if (this.autoPumpkinWasEnabled)
                {
                    this.PumpkinEndSession();
                    this.autoPumpkinWasEnabled = false;
                    this.PumpkinSetStatus("idle");
                }

                return;
            }

            this.autoPumpkinWasEnabled = true;
            float now = Time.unscaledTime;
            if (!this.pumpkinBreaker.ShouldRun(now))
            {
                return;
            }

            if (!this.pumpkinResolveReady || now < this.pumpkinNextActionAt)
            {
                return;
            }

            this.pumpkinNextActionAt = now + PumpkinApiTickSeconds;
            try
            {
                this.PumpkinTick();
                this.pumpkinBreaker.Success();
            }
            catch (Exception ex)
            {
                this.pumpkinBreaker.Failure("PumpkinCarving", ex, now);
                this.PumpkinEndSession();
                this.pumpkinNextActionAt = now + PumpkinStartBackoffSeconds;
            }
        }

        private void PumpkinTick()
        {
            switch (this.pumpkinApiState)
            {
                case PumpkinApiState.FindRough:
                    if (this.pumpkinCollectPending)
                    {
                        if (!this.TryPumpkinCollectOnce(out string collectStatus))
                        {
                            this.PumpkinSetStatus(collectStatus);
                            this.pumpkinNextActionAt = Time.unscaledTime + 0.05f;
                            return;
                        }

                        // The finish view streams in after the rough is already gone. Retry
                        // until pumpkinCollectUntil instead of placing on the first empty scan.
                        if (collectStatus == "no carved pumpkin" && Time.unscaledTime < this.pumpkinCollectUntil)
                        {
                            this.pumpkinNextActionAt = Time.unscaledTime + 0.05f;
                            return;
                        }

                        this.pumpkinCollectPending = false;
                        this.PumpkinSetStatus(collectStatus);
                        if (this.autoPumpkinPlace
                            && (collectStatus.StartsWith("collected ") || collectStatus == "no carved pumpkin"))
                        {
                            this.PumpkinPlaceAfterCollect();
                            return;
                        }

                        this.pumpkinNextActionAt = Time.unscaledTime;
                        return;
                    }

                    if (!this.TryFindPumpkinRough(out uint baseNetId, out uint pumpkinNetId, out string findStatus))
                    {
                        if (this.autoPumpkinPlace && findStatus == "no rough" && Time.unscaledTime >= this.pumpkinPlaceNextAt)
                        {
                            bool placed = this.TryPumpkinAutoPlace(out string placeStatus);
                            this.pumpkinPlaceNextAt = Time.unscaledTime + (placed ? PumpkinPlaceBackoffSeconds : 0.05f);
                            this.PumpkinSetStatus(placeStatus);
                            this.pumpkinNextActionAt = this.pumpkinPlaceNextAt;
                            return;
                        }

                        this.PumpkinSetStatus(findStatus);
                        this.pumpkinNextActionAt = Time.unscaledTime + PumpkinScanBackoffSeconds;
                        return;
                    }

                    this.pumpkinActiveBaseNetId = baseNetId;
                    this.pumpkinActiveNetId = pumpkinNetId;
                    this.pumpkinResultSeen = false;
                    this.pumpkinResultSuccess = false;
                    this.PumpkinSetSessionSuppress(true);
                    this.pumpkinApiState = PumpkinApiState.Start;
                    return;

                case PumpkinApiState.Start:
                    this.PumpkinSetSessionSuppress(true);
                    if (!this.TryPumpkinStart(this.pumpkinActiveBaseNetId, this.pumpkinActiveNetId, out string startStatus))
                    {
                        this.PumpkinSetStatus(startStatus);
                        this.PumpkinEndSession();
                        this.pumpkinNextActionAt = Time.unscaledTime + PumpkinStartBackoffSeconds;
                        return;
                    }

                    if (!this.TryPumpkinFinish(this.pumpkinActiveNetId, out string finishStatus))
                    {
                        this.PumpkinSetStatus(startStatus + "; " + finishStatus);
                        this.PumpkinEndSession();
                        this.pumpkinNextActionAt = Time.unscaledTime + PumpkinStartBackoffSeconds;
                        return;
                    }

                    this.PumpkinSetStatus(finishStatus);
                    this.pumpkinApiState = PumpkinApiState.WaitResult;
                    this.pumpkinWaitStartedAt = Time.unscaledTime;
                    this.pumpkinSessionDeadline = this.pumpkinWaitStartedAt + PumpkinResultTimeoutSeconds;
                    return;

                case PumpkinApiState.WaitResult:
                    float waited = Time.unscaledTime;
                    bool roughBlocks = this.TryPumpkinActiveRoughStillCarvable();
                    if (!this.pumpkinResultSeen && roughBlocks && waited < this.pumpkinSessionDeadline)
                    {
                        return;
                    }

                    int elapsedMs = (int)((waited - this.pumpkinWaitStartedAt) * 1000f);
                    bool success = this.pumpkinResultSeen && this.pumpkinResultSuccess;
                    bool roughGone = !this.pumpkinResultSeen && !roughBlocks;
                    if (success || roughGone)
                    {
                        if (roughGone)
                        {
                            this.PumpkinLogTiming("rough gone " + elapsedMs + "ms net=" + this.pumpkinActiveNetId);
                        }

                        this.pumpkinDoneCount++;
                        this.PumpkinSetStatus("done " + this.pumpkinDoneCount);
                    }
                    else
                    {
                        this.PumpkinLogTiming((this.pumpkinResultSeen ? "fail " : "result timeout ") + elapsedMs + "ms");
                        this.PumpkinSetStatus(this.pumpkinResultSeen ? "fail" : "result timeout");
                    }

                    uint spentRough = this.pumpkinActiveNetId;
                    this.PumpkinEndSession();
                    if (spentRough != 0U)
                    {
                        this.pumpkinIgnoreRoughNetId = spentRough;
                    }

                    if ((success || roughGone) && this.autoPumpkinCollect)
                    {
                        this.pumpkinCollectPending = true;
                        this.pumpkinCollectUntil = waited + 0.4f;
                        this.pumpkinNextActionAt = waited;
                        return;
                    }

                    if ((success || roughGone) && this.autoPumpkinPlace)
                    {
                        this.PumpkinPlaceAfterCollect();
                        return;
                    }

                    this.pumpkinNextActionAt = Time.unscaledTime + PumpkinScanBackoffSeconds;
                    return;
            }
        }

        private void PumpkinPlaceAfterCollect()
        {
            if (this.TryPumpkinAutoPlace(out string placeStatus))
            {
                this.PumpkinSetStatus(placeStatus);
                this.pumpkinPlaceNextAt = Time.unscaledTime + PumpkinPlaceBackoffSeconds;
                this.pumpkinNextActionAt = this.pumpkinPlaceNextAt;
                return;
            }

            this.PumpkinSetStatus(placeStatus);
            this.pumpkinPlaceNextAt = Time.unscaledTime + 0.05f;
            this.pumpkinNextActionAt = this.pumpkinPlaceNextAt;
        }

        private void PumpkinSetSessionSuppress(bool suppress)
        {
            this.SetGameEventHookSuppressForward(PumpkinJoinEventName, suppress);
            this.SetGameEventHookSuppressForward(PumpkinExpireEventName, suppress);
            this.SetGameEventHookSuppressForward(PumpkinResultEventName, suppress);
        }

        private void PumpkinEndSession()
        {
            this.PumpkinSetSessionSuppress(false);
            this.pumpkinActiveNetId = 0U;
            this.pumpkinActiveBaseNetId = 0U;
            this.pumpkinApiState = PumpkinApiState.FindRough;
        }

        private void PumpkinSetStatus(string message)
        {
            this.pumpkinStatus = message ?? string.Empty;
            if (this.pumpkinStatus.Length == 0 || this.pumpkinStatus == this.pumpkinLastLoggedStatus)
            {
                return;
            }

            this.pumpkinLastLoggedStatus = this.pumpkinStatus;
            ModLogger.Msg("[PumpkinCarving] " + this.pumpkinStatus);
        }

        private void PumpkinLogTiming(string message)
        {
            ModLogger.Msg("[PumpkinCarving] " + message);
        }

        private unsafe bool TryCreatePumpkinScoreList(out IntPtr listObj, out uint listPin, out string status)
        {
            listObj = IntPtr.Zero;
            listPin = 0U;
            status = string.Empty;
            if (!this.TryCreateHomeLikeIntList(0, out listObj, out listPin, out status))
            {
                return false;
            }

            if (this.pumpkinIntListAddMethod == IntPtr.Zero)
            {
                IntPtr listClass = auraMonoObjectGetClass(listObj);
                this.pumpkinIntListAddMethod = listClass == IntPtr.Zero
                    ? IntPtr.Zero
                    : this.FindAuraMonoMethodOnHierarchy(listClass, "Add", 1);
            }

            if (this.pumpkinIntListAddMethod == IntPtr.Zero)
            {
                AuraMonoPinFree(listPin);
                listPin = 0U;
                listObj = IntPtr.Zero;
                status = "List<int>.Add unavailable";
                return false;
            }

            int score = PumpkinPerfectScore;
            IntPtr* addArgs = stackalloc IntPtr[1];
            addArgs[0] = (IntPtr)(&score);
            for (int i = 0; i < PumpkinStageCount; i++)
            {
                if (!TryAuraInvoke(this.pumpkinIntListAddMethod, listObj, (IntPtr)addArgs, out _, out string addError))
                {
                    AuraMonoPinFree(listPin);
                    listPin = 0U;
                    listObj = IntPtr.Zero;
                    status = "List<int>.Add failed: " + addError;
                    return false;
                }
            }

            return true;
        }

        private unsafe bool TryPumpkinStart(uint baseNetId, uint pumpkinNetId, out string status)
        {
            status = string.Empty;
            uint b = baseNetId;
            uint p = pumpkinNetId;
            IntPtr* args = stackalloc IntPtr[2];
            args[0] = (IntPtr)(&b);
            args[1] = (IntPtr)(&p);
            if (!TryAuraInvoke(this.pumpkinStartMethod, IntPtr.Zero, (IntPtr)args, out _, out string error))
            {
                status = "Start failed: " + error;
                return false;
            }

            status = "Start(" + baseNetId + "," + pumpkinNetId + ")";
            return true;
        }

        private unsafe bool TryPumpkinFinish(uint pumpkinNetId, out string status)
        {
            IntPtr listObj = IntPtr.Zero;
            uint listPin = 0U;
            try
            {
                if (!this.TryCreatePumpkinScoreList(out listObj, out listPin, out status))
                {
                    return false;
                }

                uint p = pumpkinNetId;
                IntPtr* args = stackalloc IntPtr[2];
                args[0] = (IntPtr)(&p);
                args[1] = listObj;
                if (!TryAuraInvoke(this.pumpkinFinishMethod, IntPtr.Zero, (IntPtr)args, out _, out string error))
                {
                    status = "Finish failed: " + error;
                    return false;
                }

                status = "Finish(" + pumpkinNetId + ",[100,100,100,100])";
                return true;
            }
            finally
            {
                if (listPin != 0U)
                {
                    AuraMonoPinFree(listPin);
                }
            }
        }

        private bool TryFindPumpkinRough(out uint baseNetId, out uint pumpkinNetId, out string status)
        {
            baseNetId = 0U;
            pumpkinNetId = 0U;
            status = "no rough";
            if (!this.TryResolveSelfPlayerNetId(out uint self) || self == 0U)
            {
                status = "self netId unavailable";
                return false;
            }

            if (!this.TryHomelandFarmIsAuraMonoGetComponentsReady(out _))
            {
                status = "GetComponents not ready";
                return false;
            }

            List<uint> pins = new List<uint>();
            try
            {
                if (!this.TryAuraMonoGetComponentObjects(this.pumpkinBuildClass, out List<IntPtr> builds, pins) || builds == null)
                {
                    status = "no build components";
                    return false;
                }

                Dictionary<uint, IntPtr> buildsByNet = new Dictionary<uint, IntPtr>();
                for (int i = 0; i < builds.Count; i++)
                {
                    IntPtr build = builds[i];
                    if (build == IntPtr.Zero)
                    {
                        continue;
                    }

                    if (this.TryHomelandFarmTryReadAuraMonoComponentNetId(build, out uint netId) && netId != 0U)
                    {
                        buildsByNet[netId] = build;
                    }
                }

                if (!this.TryAuraMonoGetComponentObjects(this.pumpkinViewClass, out List<IntPtr> views, pins)
                    || views == null || views.Count == 0)
                {
                    return false;
                }

                bool havePlayer = this.TryGetLocalPlayerPosition(out Vector3 playerPos);
                float bestDist = PumpkinScanRadius;
                bool found = false;
                for (int i = 0; i < views.Count; i++)
                {
                    IntPtr view = views[i];
                    if (view == IntPtr.Zero)
                    {
                        continue;
                    }

                    if (!this.TryReadPumpkinComponentData(view, out int finishedId, out bool rotate))
                    {
                        continue;
                    }

                    if (rotate || finishedId == 0)
                    {
                        continue;
                    }

                    if (!this.TryHomelandFarmTryReadAuraMonoComponentNetId(view, out uint roughNetId)
                        || roughNetId == 0U
                        || roughNetId == this.pumpkinIgnoreRoughNetId)
                    {
                        continue;
                    }

                    if (!buildsByNet.TryGetValue(roughNetId, out IntPtr build))
                    {
                        continue;
                    }

                    if (!this.TryPumpkinInvokeUInt(build, this.pumpkinBuildOwnerMethod, out uint ownerId) || ownerId != self)
                    {
                        continue;
                    }

                    if (!this.TryPumpkinInvokeUInt(build, this.pumpkinBuildParentMethod, out uint parent) || parent == 0U)
                    {
                        continue;
                    }

                    Vector3 pos = Vector3.zero;
                    bool hasDist = havePlayer && this.TryGetAuraMonoEntityPositionFromComponent(view, out pos);
                    if (hasDist)
                    {
                        float dist = Vector3.Distance(playerPos, pos);
                        if (dist > PumpkinScanRadius || (found && dist >= bestDist))
                        {
                            continue;
                        }

                        bestDist = dist;
                    }
                    else if (found)
                    {
                        continue;
                    }

                    baseNetId = parent;
                    pumpkinNetId = roughNetId;
                    found = true;
                }

                if (found)
                {
                    status = "rough " + pumpkinNetId + " base " + baseNetId;
                }

                return found;
            }
            finally
            {
                FreeAuraMonoPins(pins);
            }
        }

        private unsafe bool TryReadPumpkinComponentData(IntPtr componentObj, out int finishedId, out bool rotate)
        {
            finishedId = 0;
            rotate = false;
            if (componentObj == IntPtr.Zero || auraMonoObjectGetClass == null || auraMonoObjectUnbox == null
                || auraMonoClassGetFieldFromName == null || auraMonoFieldGetOffset == null)
            {
                return false;
            }

            if (!this.TryGetMonoObjectMember(componentObj, "_componentData", out IntPtr boxedData) || boxedData == IntPtr.Zero)
            {
                return false;
            }

            uint boxPin = AuraMonoPinNew(boxedData);
            try
            {
                IntPtr dataClass = auraMonoObjectGetClass(boxedData);
                if (dataClass == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr finishedField = auraMonoClassGetFieldFromName(dataClass, "finishedId");
                IntPtr rotateField = auraMonoClassGetFieldFromName(dataClass, "rotate");
                if (finishedField == IntPtr.Zero || rotateField == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr raw = auraMonoObjectUnbox(boxedData);
                if (raw == IntPtr.Zero)
                {
                    return false;
                }

                int header = 2 * IntPtr.Size;
                finishedId = Marshal.ReadInt32(raw, (int)auraMonoFieldGetOffset(finishedField) - header);
                rotate = Marshal.ReadByte(raw, (int)auraMonoFieldGetOffset(rotateField) - header) != 0;
                return true;
            }
            finally
            {
                AuraMonoPinFree(boxPin);
            }
        }

        private bool TryPumpkinInvokeUInt(IntPtr obj, IntPtr method, out uint value)
        {
            value = 0U;
            if (obj == IntPtr.Zero || method == IntPtr.Zero)
            {
                return false;
            }

            if (!TryAuraInvoke(method, obj, IntPtr.Zero, out IntPtr boxed, out _))
            {
                return false;
            }

            return this.TryUnboxMonoUInt32(boxed, out value);
        }

        // Returns false when a take is still busy so the caller retries. true means the scan
        // finished (a piece was taken, or none was eligible).
        private unsafe bool TryPumpkinCollectOnce(out string status)
        {
            status = "no carved pumpkin";
            if (!this.TryResolveSelfPlayerNetId(out uint self) || self == 0U)
            {
                status = "self netId unavailable";
                return true;
            }

            List<uint> pins = new List<uint>();
            try
            {
                if (!this.TryAuraMonoGetComponentObjects(this.pumpkinBuildClass, out List<IntPtr> builds, pins) || builds == null)
                {
                    status = "no build components";
                    return true;
                }

                Dictionary<uint, uint> ownerByNet = new Dictionary<uint, uint>();
                for (int i = 0; i < builds.Count; i++)
                {
                    IntPtr build = builds[i];
                    if (build == IntPtr.Zero)
                    {
                        continue;
                    }

                    if (!this.TryHomelandFarmTryReadAuraMonoComponentNetId(build, out uint netId) || netId == 0U)
                    {
                        continue;
                    }

                    if (this.TryPumpkinInvokeUInt(build, this.pumpkinBuildOwnerMethod, out uint ownerId))
                    {
                        ownerByNet[netId] = ownerId;
                    }
                }

                if (!this.TryAuraMonoGetComponentObjects(this.pumpkinFinishViewClass, out List<IntPtr> views, pins)
                    || views == null || views.Count == 0)
                {
                    return true;
                }

                for (int i = 0; i < views.Count; i++)
                {
                    IntPtr view = views[i];
                    if (view == IntPtr.Zero || !this.TryReadPumpkinFinishCreateByCarving(view, out bool carved) || !carved)
                    {
                        continue;
                    }

                    if (!this.TryHomelandFarmTryReadAuraMonoComponentNetId(view, out uint netId) || netId == 0U)
                    {
                        continue;
                    }

                    if (!ownerByNet.TryGetValue(netId, out uint ownerId) || ownerId != self)
                    {
                        continue;
                    }

                    if (!this.TryPumpkinPoseDelete(netId, out status))
                    {
                        return false;
                    }

                    status = "collected " + netId;
                    return true;
                }

                return true;
            }
            finally
            {
                FreeAuraMonoPins(pins);
            }
        }

        private unsafe bool TryReadPumpkinFinishCreateByCarving(IntPtr componentObj, out bool createByCarving)
        {
            createByCarving = false;
            if (componentObj == IntPtr.Zero || auraMonoObjectGetClass == null || auraMonoObjectUnbox == null
                || auraMonoClassGetFieldFromName == null || auraMonoFieldGetOffset == null)
            {
                return false;
            }

            if (!this.TryGetMonoObjectMember(componentObj, "_componentData", out IntPtr boxedData) || boxedData == IntPtr.Zero)
            {
                return false;
            }

            uint boxPin = AuraMonoPinNew(boxedData);
            try
            {
                IntPtr dataClass = auraMonoObjectGetClass(boxedData);
                IntPtr field = dataClass == IntPtr.Zero ? IntPtr.Zero : auraMonoClassGetFieldFromName(dataClass, "createByCarving");
                if (field == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr raw = auraMonoObjectUnbox(boxedData);
                if (raw == IntPtr.Zero)
                {
                    return false;
                }

                createByCarving = Marshal.ReadByte(raw, (int)auraMonoFieldGetOffset(field) - (2 * IntPtr.Size)) != 0;
                return true;
            }
            finally
            {
                AuraMonoPinFree(boxPin);
            }
        }

        private unsafe bool TryPumpkinPoseDelete(uint netId, out string status)
        {
            status = string.Empty;
            uint arg = netId;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&arg);
            if (!TryAuraInvoke(this.pumpkinPoseDeleteMethod, IntPtr.Zero, (IntPtr)args, out IntPtr boxed, out string error))
            {
                status = "PoseDeleteBuild failed: " + error;
                return false;
            }

            if (boxed != IntPtr.Zero && this.TryUnboxMonoBoolean(boxed, out bool accepted) && !accepted)
            {
                status = "collect busy";
                return false;
            }

            return true;
        }

        // Places a pumpkin base (item TablePumpkinbase) when none is nearby, otherwise a rough
        // (TablePumpkinrough) as a child of the nearest own base. The rough's logic parent is
        // LevelObjectId(baseNetId, 1); GetFirstParentNetId then returns that base for Start.
        private bool TryPumpkinAutoPlace(out string status)
        {
            status = "place unavailable";
            if (!this.TryResolveSelfPlayerNetId(out uint self) || self == 0U)
            {
                status = "self netId unavailable";
                return false;
            }

            if (this.TryPumpkinHasOwnRough(self))
            {
                status = "pumpkin already placed";
                return false;
            }

            if (!this.EnsureSandPlaceResolved(out string placeResolve))
            {
                status = "place types not resolved: " + placeResolve;
                return false;
            }

            if (!this.EnsurePumpkinPlaceTables(out string tableStatus))
            {
                status = tableStatus;
                return false;
            }

            if (!this.TryPumpkinFindOwnBase(self, out uint baseNetId, out Vector3 basePos))
            {
                if (!this.TryPumpkinFindBagItem(this.pumpkinGetBaseMethod, out uint baseItem, out int baseStatic, out string bagStatus))
                {
                    status = "no pumpkin base in backpack: " + bagStatus;
                    return false;
                }

                if (!this.TryResolveSandPlaceTarget(out uint root, out ulong zone, out Vector3 localPos, out int angle, out string targetStatus))
                {
                    status = "target unresolved: " + targetStatus;
                    return false;
                }

                if (!this.TrySendSandBasePlace(baseItem, root, zone, localPos, angle, out string sendStatus))
                {
                    status = "base place failed: " + sendStatus;
                    return false;
                }

                status = "placed pumpkin base " + baseStatic;
                return true;
            }

            if (!this.TryPumpkinFindBagItem(this.pumpkinGetRoughMethod, out uint roughItem, out int roughStatic, out string roughBag))
            {
                status = "no pumpkin in backpack: " + roughBag;
                return false;
            }

            if (!this.TryResolveSandPlaceTarget(out uint buildRoot, out ulong putZone, out Vector3 _, out int roughAngle, out string roughTarget))
            {
                status = "target unresolved: " + roughTarget;
                return false;
            }

            Vector3 local = basePos;
            if (this.TryGetFieldRootWorldToLocal(buildRoot, out Matrix4x4 worldToLocal))
            {
                local = worldToLocal.MultiplyPoint3x4(basePos);
            }

            ulong parentLink = (ulong)baseNetId | (1UL << 32);
            if (!this.TrySendSandBasePlace(roughItem, buildRoot, putZone, local, roughAngle, out string roughSend, parentLink))
            {
                status = "pumpkin place failed: " + roughSend;
                return false;
            }

            status = "placed pumpkin " + roughStatic + " on base " + baseNetId;
            return true;
        }

        private bool EnsurePumpkinPlaceTables(out string status)
        {
            if (this.pumpkinGetRoughMethod != IntPtr.Zero && this.pumpkinGetBaseMethod != IntPtr.Zero
                && this.pumpkinBaseViewClass != IntPtr.Zero)
            {
                status = "cached";
                return true;
            }

            if (this.pumpkinTableClass == IntPtr.Zero)
            {
                this.pumpkinTableClass = this.FindAuraMonoTableDataClass();
            }

            if (this.pumpkinTableClass != IntPtr.Zero && this.pumpkinGetRoughMethod == IntPtr.Zero)
            {
                this.pumpkinGetRoughMethod = this.FindAuraMonoMethodOnHierarchy(this.pumpkinTableClass, "GetPumpkinrough", 2);
                if (this.pumpkinGetRoughMethod == IntPtr.Zero)
                {
                    this.pumpkinGetRoughMethod = this.FindAuraMonoMethodOnHierarchy(this.pumpkinTableClass, "GetPumpkinrough", 1);
                }
            }

            if (this.pumpkinTableClass != IntPtr.Zero && this.pumpkinGetBaseMethod == IntPtr.Zero)
            {
                this.pumpkinGetBaseMethod = this.FindAuraMonoMethodOnHierarchy(this.pumpkinTableClass, "GetPumpkinbase", 2);
                if (this.pumpkinGetBaseMethod == IntPtr.Zero)
                {
                    this.pumpkinGetBaseMethod = this.FindAuraMonoMethodOnHierarchy(this.pumpkinTableClass, "GetPumpkinbase", 1);
                }
            }

            if (this.pumpkinBaseViewClass == IntPtr.Zero)
            {
                this.pumpkinBaseViewClass = this.FindAuraMonoClassInImages(
                    "XDTLevelAndEntity.Gameplay.Component.Homeland",
                    "PumpkinBaseComponent",
                    PumpkinViewImages);
            }

            bool ok = this.pumpkinGetRoughMethod != IntPtr.Zero
                && this.pumpkinGetBaseMethod != IntPtr.Zero
                && this.pumpkinBaseViewClass != IntPtr.Zero;
            status = ok ? "resolved" : "pumpkin place tables missing";
            return ok;
        }

        // True while the rough we just finished is still an idle carvable pumpkin.
        // A failed scan keeps waiting; a missing view means it was taken or replaced.
        private bool TryPumpkinActiveRoughStillCarvable()
        {
            uint active = this.pumpkinActiveNetId;
            if (active == 0U || this.pumpkinViewClass == IntPtr.Zero)
            {
                return false;
            }

            if (!this.TryHomelandFarmIsAuraMonoGetComponentsReady(out _))
            {
                return true;
            }

            List<uint> pins = new List<uint>();
            try
            {
                if (!this.TryAuraMonoGetComponentObjects(this.pumpkinViewClass, out List<IntPtr> views, pins) || views == null)
                {
                    return true;
                }

                if (views.Count == 0)
                {
                    return false;
                }

                for (int i = 0; i < views.Count; i++)
                {
                    IntPtr view = views[i];
                    if (view == IntPtr.Zero
                        || !this.TryHomelandFarmTryReadAuraMonoComponentNetId(view, out uint netId)
                        || netId != active)
                    {
                        continue;
                    }

                    if (!this.TryReadPumpkinComponentData(view, out int finishedId, out bool rotate))
                    {
                        return true;
                    }

                    return !rotate && finishedId != 0;
                }

                return false;
            }
            finally
            {
                FreeAuraMonoPins(pins);
            }
        }

        private bool TryPumpkinHasOwnRough(uint self)
        {
            if (this.pumpkinViewClass == IntPtr.Zero || !this.TryHomelandFarmIsAuraMonoGetComponentsReady(out _))
            {
                return false;
            }

            List<uint> pins = new List<uint>();
            try
            {
                if (!this.TryAuraMonoGetComponentObjects(this.pumpkinBuildClass, out List<IntPtr> builds, pins) || builds == null)
                {
                    return false;
                }

                Dictionary<uint, uint> ownerByNet = new Dictionary<uint, uint>();
                for (int i = 0; i < builds.Count; i++)
                {
                    IntPtr build = builds[i];
                    if (build != IntPtr.Zero
                        && this.TryHomelandFarmTryReadAuraMonoComponentNetId(build, out uint netId)
                        && netId != 0U
                        && this.TryPumpkinInvokeUInt(build, this.pumpkinBuildOwnerMethod, out uint ownerId))
                    {
                        ownerByNet[netId] = ownerId;
                    }
                }

                if (!this.TryAuraMonoGetComponentObjects(this.pumpkinViewClass, out List<IntPtr> views, pins) || views == null)
                {
                    return false;
                }

                for (int i = 0; i < views.Count; i++)
                {
                    IntPtr view = views[i];
                    if (view == IntPtr.Zero)
                    {
                        continue;
                    }

                    if (!this.TryHomelandFarmTryReadAuraMonoComponentNetId(view, out uint roughNetId)
                        || roughNetId == this.pumpkinIgnoreRoughNetId
                        || !ownerByNet.TryGetValue(roughNetId, out uint ownerId)
                        || ownerId != self)
                    {
                        continue;
                    }

                    // A rough that just finished (rotate, or no finished id yet) is the one we
                    // picked up. It must not hold the next placement until the view despawns.
                    if (!this.TryReadPumpkinComponentData(view, out int finishedId, out bool rotate)
                        || rotate || finishedId == 0)
                    {
                        continue;
                    }

                    return true;
                }

                return false;
            }
            finally
            {
                FreeAuraMonoPins(pins);
            }
        }

        private bool TryPumpkinFindOwnBase(uint self, out uint baseNetId, out Vector3 basePos)
        {
            baseNetId = 0U;
            basePos = Vector3.zero;
            if (this.pumpkinBaseViewClass == IntPtr.Zero)
            {
                return false;
            }

            List<uint> pins = new List<uint>();
            try
            {
                if (!this.TryAuraMonoGetComponentObjects(this.pumpkinBuildClass, out List<IntPtr> builds, pins) || builds == null)
                {
                    return false;
                }

                Dictionary<uint, uint> ownerByNet = new Dictionary<uint, uint>();
                for (int i = 0; i < builds.Count; i++)
                {
                    IntPtr build = builds[i];
                    if (build != IntPtr.Zero
                        && this.TryHomelandFarmTryReadAuraMonoComponentNetId(build, out uint netId)
                        && netId != 0U
                        && this.TryPumpkinInvokeUInt(build, this.pumpkinBuildOwnerMethod, out uint ownerId))
                    {
                        ownerByNet[netId] = ownerId;
                    }
                }

                if (!this.TryAuraMonoGetComponentObjects(this.pumpkinBaseViewClass, out List<IntPtr> views, pins) || views == null)
                {
                    return false;
                }

                bool havePlayer = this.TryGetLocalPlayerPosition(out Vector3 playerPos);
                float best = PumpkinScanRadius;
                bool found = false;
                for (int i = 0; i < views.Count; i++)
                {
                    IntPtr view = views[i];
                    if (view == IntPtr.Zero
                        || !this.TryHomelandFarmTryReadAuraMonoComponentNetId(view, out uint netId)
                        || netId == 0U
                        || !ownerByNet.TryGetValue(netId, out uint ownerId)
                        || ownerId != self)
                    {
                        continue;
                    }

                    Vector3 pos = Vector3.zero;
                    bool hasPos = this.TryGetAuraMonoEntityPositionFromComponent(view, out pos);
                    if (havePlayer && hasPos)
                    {
                        float dist = Vector3.Distance(playerPos, pos);
                        if (dist > PumpkinScanRadius || (found && dist >= best))
                        {
                            continue;
                        }

                        best = dist;
                    }
                    else if (found)
                    {
                        continue;
                    }

                    baseNetId = netId;
                    basePos = hasPos ? pos : playerPos;
                    found = true;
                }

                return found;
            }
            finally
            {
                FreeAuraMonoPins(pins);
            }
        }

        private unsafe bool TryPumpkinFindBagItem(IntPtr tableMethod, out uint bagItemNetId, out int staticId, out string status)
        {
            bagItemNetId = 0U;
            staticId = 0;
            status = string.Empty;
            if (tableMethod == IntPtr.Zero)
            {
                status = "table method missing";
                return false;
            }

            if (!this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.BackPack.BackPackSystem", out IntPtr backPack)
                || backPack == IntPtr.Zero)
            {
                status = "BackPackSystem unavailable";
                return false;
            }

            IntPtr getAllItem = this.FindAuraMonoMethodOnHierarchy(auraMonoObjectGetClass(backPack), "GetAllItem", 1);
            if (getAllItem == IntPtr.Zero)
            {
                status = "GetAllItem missing";
                return false;
            }

            int storageBackpack = 1;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&storageBackpack);
            if (!TryAuraInvoke(getAllItem, backPack, (IntPtr)args, out IntPtr listObj, out string error) || listObj == IntPtr.Zero)
            {
                status = "GetAllItem failed: " + error;
                return false;
            }

            List<uint> pins = new List<uint>();
            List<IntPtr> items = new List<IntPtr>();
            try
            {
                if (!this.TryEnumerateAuraMonoCollectionItems(listObj, items, pins) || items.Count == 0)
                {
                    status = "backpack empty";
                    return false;
                }

                for (int i = 0; i < items.Count; i++)
                {
                    IntPtr item = items[i];
                    if (item == IntPtr.Zero
                        || !this.TryGetMonoUInt32Member(item, "netId", out uint candNetId)
                        || candNetId == 0U
                        || !this.TryGetMonoInt32Member(item, "staticId", out int candStatic)
                        || candStatic <= 0
                        || !this.PumpkinTableHasRow(tableMethod, candStatic))
                    {
                        continue;
                    }

                    bagItemNetId = candNetId;
                    staticId = candStatic;
                    status = "item " + candNetId;
                    return true;
                }

                status = "no matching item";
                return false;
            }
            finally
            {
                FreeAuraMonoPins(pins);
            }
        }

        private unsafe bool PumpkinTableHasRow(IntPtr method, int staticId)
        {
            int id = staticId;
            IntPtr result;
            if (AuraMonoMethodParamCountIs(method, 2))
            {
                byte needException = 0;
                IntPtr* args = stackalloc IntPtr[2];
                args[0] = (IntPtr)(&id);
                args[1] = (IntPtr)(&needException);
                if (!TryAuraInvoke(method, IntPtr.Zero, (IntPtr)args, out result, out _))
                {
                    return false;
                }
            }
            else
            {
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&id);
                if (!TryAuraInvoke(method, IntPtr.Zero, (IntPtr)args, out result, out _))
                {
                    return false;
                }
            }

            return result != IntPtr.Zero;
        }
    }
}
