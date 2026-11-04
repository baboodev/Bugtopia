using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace HeartopiaMod
{
    // Furniture Light Radius (New Features → Extra) — one action sets every light piece the
    // player can switch within a radius around them to ON or OFF (campfires, braziers, lanterns).
    //
    // Mechanics verified against ilspy-dumps + live MCP probes (docs/plans/2026-11-04-furniture-light-radius.md):
    // the UgcSwitch byte's two bits drive dye/room light (Light) and the operate material
    // (Operate). The looping campfire flame is NOT that material. InitInteractActionClip plays
    // config_interaction_{id}_on via RendererComponent.PlayAnim when Operate is set, and nothing
    // in the UgcSwitch update path stops it. The vanilla button (SwitchInteractionCommand) does
    // both: SetMask(Operate|Light) + SwitchState, AND player.Cast, whose server echo
    // (ExecuteActionNetworkEvent) reaches every client, including this one, and
    // BuildActionClipComponent replaces the clip with config_interaction_{id}_off / _on.
    // SwitchState alone changes the light and leaves the loop running. A quickCast alone
    // changes the flame and leaves the light. Confirmed in game, both clients: switchFlag 1
    // plays the off clip, switchFlag 0 plays the on clip, then the absolute byte
    // (on ? 0b11 : 0b00) in the same press. quickCast skips the body pose
    // (QuickActionEvent, not CastActionEvent). The timeline is not played locally.
    // Payload semantics: bits 0-1 are the whole byte's meaning, so the value is idempotent —
    // no current-state read is needed, and re-sending the same value is a no-op server-side.
    //
    // Send: HomelandProtocolManager.SwitchState(uint netId, uint buildRootNetId, in UgcSwitch)
    // — the same absolute packet the button sends (SendBuildBatchOperation → Building channel),
    // exactly the op Auto Repair already ships on that channel. buildRootNetId comes from the
    // piece's own get_HomeNetId, the same FurnitureBaseData.homeNetId the vanilla 2-arg
    // overload resolves. THE OVERLOAD IS NOT AMBIGUOUS-ARITY SAFE: SwitchState(uint,uint,UgcSwitch.Idx)
    // toggles one bit then sends; SwitchState(uint,uint,in UgcSwitch) sends the absolute struct.
    // Both third params are VALUETYPE — Contains("UgcSwitch") also matches UgcSwitch/Idx; resolve
    // requires exact struct name. Wrong overload treats payload as Idx → light-only XOR toggles.
    // DecodeTypeEntityData has the same trap ((int) vs (string)) → {I4}.
    //
    // Capability gate: TableData.DecodeTypeEntityData(staticId) → TableEntityType → f_asLight
    // AND (id == 55 lighting OR id == 221 outdoorlamp). f_ugcSwitch is not a lamp test: on
    // decoration (id 50) it is set for the whole 300001-309999 range.
    //
    // Scope: any light piece in radius whose get_HomeNetId is non-zero. That id is the house
    // instance, not the player who placed the piece, so a visited home is included. homeNetId 0
    // is a public-area prop and stays out.
    public partial class HeartopiaComplete
    {
        internal static bool MasterLogFurnitureLightRadius = false;

        private const float FurnitureLightDefaultRadiusMeters = 15f;
        private const float FurnitureLightMinRadiusMeters = 3f;
        private const float FurnitureLightMaxRadiusMeters = 60f;
        // One action must not put a whole festival of pieces on the wire in one frame — the
        // leftover candidates go out on the next press.
        private const int FurnitureLightMaxSendsPerAction = 64;
        // EntityType row ids from oversea EntityType. f_asLight is a type flag, not a per-item
        // lamp bit: decoration (id 50, 300001-309999) has f_asLight and f_ugcSwitch set for all
        // 3023 rows, including Pumpkin Grimkin Surprise Box 302832 (skill 500100116, trigger
        // PlayerEnter, timeline ugcview_302832_touch). SwitchState on that piece plays the scare.
        // The Lighting table is only 350001-350494 (id 55). Outdoor lamps are id 221.
        private const int FurnitureLightLightingEntityTypeId = 55;
        private const int FurnitureLightOutdoorLampEntityTypeId = 221;

        // CorElementType via mono_type_get_type. Both 3-arg SwitchState overloads are VALUETYPE
        // in slot 2 (UgcSwitch.Idx vs in UgcSwitch, whose mono name is UgcSwitch&). Disambiguate
        // by exact struct name, not BYREF.
        private const int MonoTypeBoolean = 0x02;
        private const int MonoTypeI4 = 0x08;
        private const int MonoTypeU4 = 0x09;
        private const int MonoTypeValuetype = 0x11;
        // TargetDirection.Middle. quickCast does not aim the body; the field is still serialized.
        private const int FurnitureLightTargetDirectionMiddle = 4;
        // InteractType.Switch. Only rows with enableSwitchLight own the on/off flame timeline
        // (doors are also type 2 and must not be played).
        private const int FurnitureLightSwitchInteractionType = 2;
        private const int FurnitureLightFlameMaxDepth = 4;
        private const int FurnitureLightFlameMaxNodes = 96;

        private static readonly string[] FurnitureLightViewImages =
        {
            "XDTLevelAndEntity", "XDTLevelAndEntity.dll",
        };

        // TableData lives in the EcsClient image with an EMPTY namespace (global-namespace
        // table class — FindAuraMonoClassByFullName returns Zero for it by construction).
        private static readonly string[] FurnitureLightTableImages =
        {
            "EcsClient", "EcsClient.dll",
        };

        private static readonly Color FurnitureLightOkColor = new Color(0.45f, 1f, 0.55f);
        private static readonly Color FurnitureLightFailColor = new Color(1f, 0.5f, 0.4f);

        // UI-owned radius (slider writes through SetFurnitureLightRadiusMeters). Not persisted —
        // same policy as AutoFishingFarm's shadow range.
        private float furnitureLightRadiusMeters = FurnitureLightDefaultRadiusMeters;

        private string furnitureLightStatus = string.Empty;
        private string furnitureLightLastLoggedStatus = string.Empty;

        private bool furnitureLightRegistrationsDone;
        private bool furnitureLightResolveReady;
        private bool furnitureLightResolveFailureLogged;
        private bool furnitureLightSignatureMismatchLogged;
        private IntPtr furnitureLightComponentClass;    // FurnitureBaseComponent
        private IntPtr furnitureLightProtocolClass;     // HomelandProtocolManager
        private IntPtr furnitureLightTableDataClass;    // TableData (EcsClient, empty namespace)
        private IntPtr furnitureLightSwitchStateMethod; // (uint, uint, in UgcSwitch) — signature-verified only
        private IntPtr furnitureLightDecodeMethod;      // TableData.DecodeTypeEntityData(int) — signature-verified only
        private IntPtr furnitureLightOwnerGetter;       // FurnitureBaseComponent.get_OwnerId -> uint
        private IntPtr furnitureLightStaticIdGetter;    // FurnitureBaseComponent.get_StaticId -> int
        private IntPtr furnitureLightHomeNetGetter;     // FurnitureBaseComponent.get_HomeNetId -> uint
        private IntPtr furnitureLightUgcSwitchClass;    // XDT.Scene.Shared.Modules.Build.UgcSwitch
        private IntPtr furnitureLightUgcSwitchValueField; // UgcSwitch.value (byte)
        // Flame cast. Resolved lazily and never part of furnitureLightResolveReady: a miss
        // must still let SwitchState turn the light on.
        private IntPtr furnitureLightLevelObjectClass;
        private IntPtr furnitureLightGetInteractionMethod; // TableData.GetInteraction(int, bool)
        private IntPtr furnitureLightSwitchArgClass;       // PlayerActionSwitchInteractArg
        private IntPtr furnitureLightPlayerClass;          // LocalPlayerComponent
        private IntPtr furnitureLightCastMethod;           // LocalPlayerComponent.Cast(ActionContext)
        private bool furnitureLightFlameSignatureLogged;
        private bool furnitureLightFlameFailureLogged;

        private struct FurnitureLightSwitchTarget
        {
            public int InteractId;
            public ulong LevelObjectNetId;
        }

        // staticId → is-a-switchable-light-piece. Config-table derived, world-independent, so
        // it survives world changes on purpose (staticIds are constants). A failed decode is
        // never cached; only the verdict is.
        private readonly Dictionary<int, bool> furnitureLightCapabilityCache = new Dictionary<int, bool>();

        private FeatureBreakerState furnitureLightBreaker;

        // ===== UI-facing state =====

        public float GetFurnitureLightRadiusMeters() => this.furnitureLightRadiusMeters;

        public void SetFurnitureLightRadiusMeters(float value)
        {
            this.furnitureLightRadiusMeters = Mathf.Clamp(Mathf.Round(value),
                FurnitureLightMinRadiusMeters, FurnitureLightMaxRadiusMeters);
        }

        public string GetFurnitureLightStatus()
        {
            if (string.IsNullOrEmpty(this.furnitureLightStatus))
            {
                return this.L("Idle");
            }

            return this.furnitureLightStatus;
        }

        // ===== Registration / resolution =====

        private void EnsureFurnitureLightRegistrations()
        {
            if (this.furnitureLightRegistrationsDone)
            {
                return;
            }

            this.furnitureLightRegistrationsDone = true;
            this.RegisterWorldReadyCallback("furniture-light-radius", this.TryResolveFurnitureLightRadius);
        }

        private bool TryResolveFurnitureLightRadius()
        {
            if (this.furnitureLightResolveReady)
            {
                this.EnsureFurnitureLightFlameReady();
                return true;
            }

            this.ResolveAuraFarmRuntimeMethods();
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return false;
            }

            if (this.furnitureLightComponentClass == IntPtr.Zero)
            {
                this.furnitureLightComponentClass = this.FindAuraMonoClassInImages(
                    "XDTLevelAndEntity.Gameplay.Component.Homeland",
                    "FurnitureBaseComponent",
                    FurnitureLightViewImages);
            }

            if (this.furnitureLightProtocolClass == IntPtr.Zero)
            {
                this.furnitureLightProtocolClass = this.FindAuraMonoClassByFullName(
                    "XDTDataAndProtocol.ProtocolService.Homeland.HomelandProtocolManager");
            }

            if (this.furnitureLightTableDataClass == IntPtr.Zero)
            {
                this.furnitureLightTableDataClass = this.FindAuraMonoClassInImages(
                    string.Empty, "TableData", FurnitureLightTableImages);
            }

            if (this.furnitureLightComponentClass == IntPtr.Zero
                || this.furnitureLightProtocolClass == IntPtr.Zero
                || this.furnitureLightTableDataClass == IntPtr.Zero)
            {
                this.LogFurnitureLightResolveFailureOnce("class unresolved");
                return false;
            }

            // Signature-verified overload picks — see file header. Never store an unverified
            // method: invoking the wrong SwitchState overload sends a single-bit toggle, the
            // exact defect this feature exists to avoid.
            if (this.furnitureLightSwitchStateMethod == IntPtr.Zero)
            {
                this.furnitureLightSwitchStateMethod = this.FindAuraMonoMethodByParamTypes(
                    this.furnitureLightProtocolClass, "SwitchState", 3,
                    new int[] { MonoTypeU4, MonoTypeU4, MonoTypeValuetype },
                    "[FurnitureLights]",
                    2,
                    verifyParamTypeNameExactStruct: "UgcSwitch",
                    logSignatureMismatches: !this.furnitureLightSignatureMismatchLogged);
                if (this.furnitureLightSwitchStateMethod == IntPtr.Zero)
                {
                    this.furnitureLightSignatureMismatchLogged = true;
                }
            }

            if (this.furnitureLightDecodeMethod == IntPtr.Zero)
            {
                this.furnitureLightDecodeMethod = this.FindAuraMonoMethodByParamTypes(
                    this.furnitureLightTableDataClass, "DecodeTypeEntityData", 1,
                    new int[] { MonoTypeI4 }, "[FurnitureLights]");
            }

            if (this.furnitureLightOwnerGetter == IntPtr.Zero)
            {
                this.furnitureLightOwnerGetter = this.FindAuraMonoMethodOnHierarchy(
                    this.furnitureLightComponentClass, "get_OwnerId", 0);
            }

            if (this.furnitureLightStaticIdGetter == IntPtr.Zero)
            {
                this.furnitureLightStaticIdGetter = this.FindAuraMonoMethodOnHierarchy(
                    this.furnitureLightComponentClass, "get_StaticId", 0);
            }

            if (this.furnitureLightHomeNetGetter == IntPtr.Zero)
            {
                this.furnitureLightHomeNetGetter = this.FindAuraMonoMethodOnHierarchy(
                    this.furnitureLightComponentClass, "get_HomeNetId", 0);
            }

            if (this.furnitureLightUgcSwitchClass == IntPtr.Zero)
            {
                this.furnitureLightUgcSwitchClass = this.FindAuraMonoClassByFullName(
                    "XDT.Scene.Shared.Modules.Build.UgcSwitch");
            }

            if (this.furnitureLightUgcSwitchClass != IntPtr.Zero
                && this.furnitureLightUgcSwitchValueField == IntPtr.Zero)
            {
                this.furnitureLightUgcSwitchValueField = this.FindAuraMonoFieldOnHierarchy(
                    this.furnitureLightUgcSwitchClass, "value");
            }

            this.furnitureLightResolveReady = this.furnitureLightSwitchStateMethod != IntPtr.Zero
                && this.furnitureLightDecodeMethod != IntPtr.Zero
                && this.furnitureLightOwnerGetter != IntPtr.Zero
                && this.furnitureLightStaticIdGetter != IntPtr.Zero
                && this.furnitureLightHomeNetGetter != IntPtr.Zero
                && this.furnitureLightUgcSwitchClass != IntPtr.Zero
                && this.furnitureLightUgcSwitchValueField != IntPtr.Zero;

            if (this.furnitureLightResolveReady && MasterLogFurnitureLightRadius)
            {
                ModLogger.Msg("[FurnitureLights] resolve ready protocol=0x" + this.furnitureLightProtocolClass.ToInt64().ToString("X")
                    + " component=0x" + this.furnitureLightComponentClass.ToInt64().ToString("X")
                    + " tableData=0x" + this.furnitureLightTableDataClass.ToInt64().ToString("X")
                    + " switchState=0x" + this.furnitureLightSwitchStateMethod.ToInt64().ToString("X")
                    + " decode=0x" + this.furnitureLightDecodeMethod.ToInt64().ToString("X"));
            }
            else if (!this.furnitureLightResolveReady)
            {
                this.LogFurnitureLightResolveFailureOnce("method/getter unresolved");
            }

            if (this.furnitureLightResolveReady)
            {
                this.EnsureFurnitureLightFlameReady();
            }

            return this.furnitureLightResolveReady;
        }

        private void LogFurnitureLightResolveFailureOnce(string reason)
        {
            if (this.furnitureLightResolveFailureLogged)
            {
                return;
            }

            this.furnitureLightResolveFailureLogged = true;
            ModLogger.Msg("[FurnitureLights] world-ready resolve failed (" + reason
                + ") — the feature stays inert until a later world-ready pass succeeds; check type names against the current build's dumps.");
        }

        // ===== Action (on-demand from the Extra page buttons — no OnUpdate tick) =====

        private void RunFurnitureLightsAction(bool turnOn)
        {
            this.EnsureFurnitureLightRegistrations();

            // A click inside a loaded world resolves inline (main thread, world-ready gate
            // already satisfied) — otherwise the FIRST press after entering the homeland
            // always fails, because the world-ready callback was only just registered. The
            // callback still covers pre-world / failed-resolution retries.
            if (!this.furnitureLightResolveReady && this.IsWorldReady)
            {
                this.TryResolveFurnitureLightRadius();
            }

            if (!this.furnitureLightResolveReady)
            {
                this.FurnitureLightSetStatus(this.L("not ready yet — enter a homeland and retry"));
                this.AddMenuNotification(this.L("Furniture lights: ") + this.furnitureLightStatus, FurnitureLightFailColor);
                return;
            }

            float now = Time.unscaledTime;
            if (!this.furnitureLightBreaker.ShouldRun(now))
            {
                this.FurnitureLightSetStatus(this.L("cooling down after a repeated error"));
                return;
            }

            try
            {
                bool ok = this.TryFurnitureLightApply(turnOn, out string resultStatus);
                this.furnitureLightBreaker.Success();
                this.FurnitureLightSetStatus(resultStatus);
                this.AddMenuNotification(this.L("Furniture lights: ") + resultStatus,
                    ok ? FurnitureLightOkColor : FurnitureLightFailColor);
            }
            catch (Exception ex)
            {
                this.furnitureLightBreaker.Failure("FurnitureLightRadius", ex, now);
                this.FurnitureLightSetStatus(this.LF("error: {0}", ex.Message));
                ModLogger.Msg("[FurnitureLights] action error: " + ex);
                this.AddMenuNotification(this.L("Furniture lights failed: ") + ex.Message, FurnitureLightFailColor);
            }
        }

        // Pinned scan (the FurnitureRepairTick discipline: every element pinned at collection
        // time, pins freed in finally, enumeration INSIDE the try) followed by the absolute
        // both-bits send per candidate. No MonoObject* outlives the call — targets are
        // scalarized to (netId, homeNetId) before the send loop.
        private unsafe bool TryFurnitureLightApply(bool turnOn, out string status)
        {
            status = this.L("AuraMono unavailable");
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            float radius = this.furnitureLightRadiusMeters;
            var targets = new List<(uint NetId, uint HomeNetId, float Dist)>(16);
            int piecesSeen = 0;
            int skippedNoHome = 0;
            List<IntPtr> components;
            List<uint> pins = new List<uint>();
            try
            {
                if (!this.TryAuraMonoGetComponentObjects(
                        this.furnitureLightComponentClass, out components, out bool infrastructureOk, pins))
                {
                    status = infrastructureOk ? this.L("no furniture pieces loaded") : this.L("component scan unavailable");
                    return false;
                }

                if (!this.TryGetLocalPlayerPosition(out Vector3 playerPos))
                {
                    status = this.L("player position unavailable");
                    return false;
                }

                for (int i = 0; i < components.Count; i++)
                {
                    IntPtr obj = components[i];
                    if (obj == IntPtr.Zero)
                    {
                        continue;
                    }

                    piecesSeen++;

                    if (!this.TryHomelandFarmTryReadAuraMonoComponentNetId(obj, out uint netId) || netId == 0U)
                    {
                        continue;
                    }

                    if (!this.TryGetAuraMonoEntityPositionFromComponent(obj, out Vector3 pos))
                    {
                        continue;
                    }

                    float dist = Vector3.Distance(playerPos, pos);
                    if (dist > radius)
                    {
                        continue;
                    }

                    if (!this.TryInvokeAuraMonoIntGetter(obj, this.furnitureLightStaticIdGetter, out int staticId))
                    {
                        continue;
                    }

                    if (!this.TryFurnitureLightIsLightPiece(staticId, out bool lightPiece) || !lightPiece)
                    {
                        continue;
                    }

                    if (!this.TryInvokeAuraMonoUIntGetter(obj, this.furnitureLightHomeNetGetter, out uint homeNetId)
                        || homeNetId == 0U)
                    {
                        // No house root means the build channel has nothing to route the op
                        // under — vanilla's own 2-arg overload would send 0 the same way, so
                        // skipping is strictly safer than mimicking it.
                        skippedNoHome++;
                        continue;
                    }

                    targets.Add((netId, homeNetId, dist));
                }
            }
            finally
            {
                FreeAuraMonoPins(pins);
            }

            // Cap discipline: with a cap, pressing again must NOT re-send the same first N
            // (GetComponents order is stable within a world, so an unsorted cut would leave
            // the same tail unreachable forever while the status promised "next press").
            // Closest-first means the cap always covers the pieces the player is standing at;
            // anything past it is named honestly in the status.
            targets.Sort((a, b) => a.Dist.CompareTo(b.Dist));

            if (targets.Count == 0)
            {
                string noHomePart = skippedNoHome > 0
                    ? this.LF(" ({0} without house root)", skippedNoHome)
                    : string.Empty;
                status = this.LF(
                    "no switchable light pieces within {0} m{1} (pieces seen: {2})",
                    (int)radius,
                    noHomePart,
                    piecesSeen);
                return false;
            }

            int sent = 0;
            int failed = 0;
            int flameCasts = 0;
            string lastError = null;
            bool flameReady = this.EnsureFurnitureLightFlameReady();
            List<uint> playerPins = new List<uint>();
            IntPtr playerObj = IntPtr.Zero;
            if (flameReady)
            {
                this.TryGetFurnitureLightLocalPlayer(out playerObj, playerPins);
            }

            int cap = Math.Min(targets.Count, FurnitureLightMaxSendsPerAction);
            try
            {
                for (int i = 0; i < cap; i++)
                {
                    // Cast first, then SwitchState. Same-frame order confirmed live on two clients:
                    // flag 1 extinguishes the flame, flag 0 ignites it, the byte is only the light.
                    if (playerObj != IntPtr.Zero)
                    {
                        flameCasts += this.TryCastFurnitureLightFlame(targets[i].NetId, turnOn, playerObj);
                    }

                    if (!this.TrySendFurnitureLightSwitch(targets[i].NetId, targets[i].HomeNetId, turnOn, out string error))
                    {
                        failed++;
                        lastError = error;
                        continue;
                    }

                    sent++;
                }
            }
            finally
            {
                // ConfigurableActionClip.OnStart hides the left hand and plays the interact
                // controller even when quickCast skips the body pose. EndCasting does not run
                // the pose-restore clip. Re-focus motion, the same pair EquipHandhold uses
                // after an override change, or walk and jump stay dead until the item changes.
                if (playerObj != IntPtr.Zero && flameCasts > 0)
                {
                    this.RestoreFurnitureLightLocomotion(playerObj);
                }

                FreeAuraMonoPins(playerPins);
            }

            string onOff = turnOn ? this.L("ON") : this.L("OFF");
            string capSuffix = targets.Count > cap
                ? this.LF(", capped — closest {0} sent, {1} farther pieces not sent", cap, targets.Count - cap)
                : string.Empty;
            string failSuffix = failed > 0
                ? this.LF(", failed {0} ({1})", failed, lastError ?? string.Empty)
                : string.Empty;
            string skipSuffix = skippedNoHome > 0
                ? this.LF(", skipped {0} no house root", skippedNoHome)
                : string.Empty;
            string flameSuffix = !flameReady
                ? this.L(", flame cast unresolved")
                : playerObj == IntPtr.Zero
                    ? this.L(", no local player")
                    : this.LF(", flame casts {0}", flameCasts);
            status = this.LF("{0}: sent {1}{2}{3}{4}{5}", onOff, sent, capSuffix, failSuffix, skipSuffix, flameSuffix);
            if (MasterLogFurnitureLightRadius)
            {
                ModLogger.Msg("[FurnitureLights] " + (turnOn ? "ON" : "OFF") + " radius=" + radius
                    + " pieces=" + piecesSeen + " targets=" + targets.Count + " sent=" + sent + " failed=" + failed);
            }

            return sent > 0 && failed == 0;
        }

        // One static invoke of the signature-verified absolute SwitchState. Build a real
        // UgcSwitch on the mono heap (value field), unbox to the struct address mono expects
        // for the 3rd parameter — a bare byte* on the stack is not a UgcSwitch for this sig.
        private unsafe bool TrySendFurnitureLightSwitch(uint netId, uint homeNetId, bool turnOn, out string error)
        {
            error = null;
            IntPtr method = this.furnitureLightSwitchStateMethod;
            if (method == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                error = this.L("SwitchState unresolved");
                return false;
            }

            if (auraMonoObjectNew == null || auraMonoFieldSetValue == null || auraMonoObjectUnbox == null
                || this.furnitureLightUgcSwitchClass == IntPtr.Zero
                || this.furnitureLightUgcSwitchValueField == IntPtr.Zero)
            {
                error = this.L("UgcSwitch layout unresolved");
                return false;
            }

            IntPtr switchObj = auraMonoObjectNew(this.auraMonoRootDomain, this.furnitureLightUgcSwitchClass);
            if (switchObj == IntPtr.Zero)
            {
                error = "UgcSwitch alloc failed";
                return false;
            }

            uint pin = AuraMonoPinNew(switchObj);
            try
            {
                uint localNetId = netId;
                uint localRootNetId = homeNetId;
                byte switchValue = turnOn ? (byte)0b11 : (byte)0b00;
                auraMonoFieldSetValue(switchObj, this.furnitureLightUgcSwitchValueField, (IntPtr)(&switchValue));
                IntPtr switchStructPtr = auraMonoObjectUnbox(switchObj);
                if (switchStructPtr == IntPtr.Zero)
                {
                    error = "UgcSwitch unbox failed";
                    return false;
                }

                IntPtr* args = stackalloc IntPtr[3];
                args[0] = (IntPtr)(&localNetId);
                args[1] = (IntPtr)(&localRootNetId);
                args[2] = switchStructPtr;

                IntPtr exc = IntPtr.Zero;
                auraMonoRuntimeInvoke(method, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    error = "exc=0x" + exc.ToInt64().ToString("X");
                    return false;
                }

                if (MasterLogFurnitureLightRadius)
                {
                    ModLogger.Msg("[FurnitureLights] SwitchState netId=" + netId + " home=" + homeNetId
                        + " value=0x" + switchValue.ToString("X2"));
                }
            }
            finally
            {
                AuraMonoPinFree(pin);
            }

            return true;
        }

        // Capability: staticId → TableEntityType row via the game's own decode (static method,
        // the string overload excluded by the signature check, so no ambiguity). f_asLight set
        // AND the lighting entity type = switchable light piece (constant comment — why
        // f_asLight alone is not the gate). A failed decode is never cached;
        // the verdict is, so a pile of 20 identical pieces decodes once.
        // The returned TableEntityType is a fresh mono heap object — pinned across the member
        // reads (each boxed read allocates, and SGen moves what is still held).
        private unsafe bool TryFurnitureLightIsLightPiece(int staticId, out bool lightPiece)
        {
            lightPiece = false;
            if (this.furnitureLightCapabilityCache.TryGetValue(staticId, out bool cached))
            {
                lightPiece = cached;
                return true;
            }

            if (this.furnitureLightDecodeMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            int localStaticId = staticId;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&localStaticId);

            IntPtr exc = IntPtr.Zero;
            IntPtr typeObj = auraMonoRuntimeInvoke(this.furnitureLightDecodeMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || typeObj == IntPtr.Zero)
            {
                return false;
            }

            uint pin = AuraMonoPinNew(typeObj);
            try
            {
                if (!this.TryGetMonoInt32Member(typeObj, "id", out int entityTypeId)
                    || !this.TryGetMonoBoolMember(typeObj, "f_asLight", out bool asLight))
                {
                    return false;
                }

                lightPiece = asLight && (entityTypeId == FurnitureLightLightingEntityTypeId
                    || entityTypeId == FurnitureLightOutdoorLampEntityTypeId);
                this.furnitureLightCapabilityCache[staticId] = lightPiece;
                if (MasterLogFurnitureLightRadius)
                {
                    ModLogger.Msg("[FurnitureLights] capability staticId=" + staticId
                        + " entityTypeId=" + entityTypeId + " f_asLight=" + asLight
                        + " → " + lightPiece);
                }

                return true;
            }
            finally
            {
                AuraMonoPinFree(pin);
            }
        }

        // Owner/staticId/homeNetId reads go through the shared TryInvokeAuraMono{UInt,Int}Getter
        // helpers (HeartopiaComplete.AuraMono.cs) — same getters as FurnitureAutoRepair.
        // A Zero owner (TryUnboxMonoUInt32 rejects 0) is not the local player, so the piece
        // is skipped; a Zero homeNetId fails the uint read the same way.

        // Cast + GetInteraction. Independent of SwitchState readiness so a flame
        // miss cannot disable the light send. Signature mismatches log once.
        private bool EnsureFurnitureLightFlameReady()
        {
            if (this.furnitureLightCastMethod != IntPtr.Zero
                && this.furnitureLightSwitchArgClass != IntPtr.Zero
                && this.furnitureLightGetInteractionMethod != IntPtr.Zero
                && this.furnitureLightLevelObjectClass != IntPtr.Zero)
            {
                return true;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                || this.furnitureLightTableDataClass == IntPtr.Zero)
            {
                return false;
            }

            if (this.furnitureLightLevelObjectClass == IntPtr.Zero)
            {
                // ByFullName's image list returned null on the running build (log: levelObject=0x0)
                // while the all-image sweep resolves this class. Without it the interact walk
                // never starts, the cast is skipped, and only SwitchState runs.
                this.furnitureLightLevelObjectClass = this.FindAuraMonoClassAnySpelling(
                    "ScriptsRefactory.LevelAndEntity.LevelObject");
            }

            if (this.furnitureLightGetInteractionMethod == IntPtr.Zero)
            {
                this.furnitureLightGetInteractionMethod = this.FindAuraMonoMethodByParamTypes(
                    this.furnitureLightTableDataClass, "GetInteraction", 2,
                    new int[] { MonoTypeI4, MonoTypeBoolean },
                    "[FurnitureLights]",
                    logSignatureMismatches: !this.furnitureLightFlameSignatureLogged);
            }

            if (this.furnitureLightCastMethod == IntPtr.Zero || this.furnitureLightSwitchArgClass == IntPtr.Zero)
            {
                if (this.furnitureLightPlayerClass == IntPtr.Zero)
                {
                    this.furnitureLightPlayerClass = this.FindAuraMonoClassByFullName(
                        "XDTLevelAndEntity.Gameplay.Component.Player.LocalPlayerComponent");
                }

                if (this.furnitureLightPlayerClass != IntPtr.Zero && this.furnitureLightCastMethod == IntPtr.Zero)
                {
                    this.furnitureLightCastMethod = this.FindAuraMonoMethodOnHierarchy(
                        this.furnitureLightPlayerClass, "Cast", 1);
                }

                if (this.furnitureLightSwitchArgClass == IntPtr.Zero)
                {
                    this.furnitureLightSwitchArgClass = this.FindAuraMonoClassByFullName(
                        "XDTLevelAndEntity.Gameplay.Action.PlayerActionSwitchInteractArg");
                }
            }

            bool ready = this.furnitureLightCastMethod != IntPtr.Zero
                && this.furnitureLightSwitchArgClass != IntPtr.Zero
                && this.furnitureLightGetInteractionMethod != IntPtr.Zero
                && this.furnitureLightLevelObjectClass != IntPtr.Zero;
            if (!ready)
            {
                this.furnitureLightFlameSignatureLogged = true;
                if (!this.furnitureLightFlameFailureLogged)
                {
                    this.furnitureLightFlameFailureLogged = true;
                    ModLogger.Msg("[FurnitureLights] flame cast unresolved"
                        + " cast=0x" + this.furnitureLightCastMethod.ToInt64().ToString("X")
                        + " arg=0x" + this.furnitureLightSwitchArgClass.ToInt64().ToString("X")
                        + " interaction=0x" + this.furnitureLightGetInteractionMethod.ToInt64().ToString("X")
                        + " levelObject=0x" + this.furnitureLightLevelObjectClass.ToInt64().ToString("X"));
                }
            }
            else if (MasterLogFurnitureLightRadius)
            {
                ModLogger.Msg("[FurnitureLights] flame cast ready cast=0x"
                    + this.furnitureLightCastMethod.ToInt64().ToString("X"));
            }

            return ready;
        }

        // quickCast of PlayerActionSwitchInteractArg. switchFlag 1 is the off clip, 0 is the on
        // clip (confirmed; not a read of the current Operate bit). The body pose is not played,
        // but OnStart still hides the hand and switches the animator; the caller restores
        // locomotion afterwards. Returns how many casts the player accepted.
        private int TryCastFurnitureLightFlame(uint netId, bool turnOn, IntPtr playerObj)
        {
            if (netId == 0U || playerObj == IntPtr.Zero || auraMonoRuntimeInvoke == null || auraMonoObjectGetClass == null)
            {
                return 0;
            }

            if (!this.TryGetAuraMonoEntityObjectByNetId(netId, out IntPtr entityObj) || entityObj == IntPtr.Zero)
            {
                return 0;
            }

            List<uint> pins = new List<uint>();
            try
            {
                pins.Add(AuraMonoPinNew(entityObj));
                if (!this.TryGetMonoObjectMember(entityObj, "transformComponent", out IntPtr transformObj)
                    || transformObj == IntPtr.Zero
                    || !this.TryGetMonoObjectMember(transformObj, "hierarchy", out IntPtr hierarchyObj)
                    || hierarchyObj == IntPtr.Zero)
                {
                    return 0;
                }

                pins.Add(AuraMonoPinNew(transformObj));
                pins.Add(AuraMonoPinNew(hierarchyObj));

                List<FurnitureLightSwitchTarget> targets = new List<FurnitureLightSwitchTarget>(4);
                int nodes = 0;
                this.CollectFurnitureLightInteractIds(hierarchyObj, 0, targets, pins, ref nodes);
                int casts = 0;
                for (int i = 0; i < targets.Count; i++)
                {
                    FurnitureLightSwitchTarget target = targets[i];
                    if (!this.FurnitureLightInteractionSwitchesLight(target.InteractId))
                    {
                        continue;
                    }

                    if (this.TryCastFurnitureLightSwitch(playerObj, netId, target, !turnOn))
                    {
                        casts++;
                    }
                }

                if (MasterLogFurnitureLightRadius)
                {
                    ModLogger.Msg("[FurnitureLights] flame cast netId=" + netId
                        + " " + (turnOn ? "on" : "off")
                        + " ids=" + targets.Count + " casts=" + casts + " nodes=" + nodes);
                }

                return casts;
            }
            finally
            {
                FreeAuraMonoPins(pins);
            }
        }

        private bool TryGetFurnitureLightLocalPlayer(out IntPtr playerObj, List<uint> pins)
        {
            playerObj = IntPtr.Zero;
            if (this.furnitureLightPlayerClass == IntPtr.Zero
                || !this.TryAuraMonoGetComponentObjects(this.furnitureLightPlayerClass, out List<IntPtr> players, pins)
                || players == null
                || players.Count == 0
                || players[0] == IntPtr.Zero)
            {
                return false;
            }

            playerObj = players[0];
            return true;
        }

        // Direct children of the entity hierarchy, then their children, bounded. The root
        // object's type is the furniture ResourceComponent; LevelObjects hang underneath it,
        // which is the walk InitInteractActionClip does via GetObjectList.
        private void CollectFurnitureLightInteractIds(IntPtr hierarchyObj, int depth, List<FurnitureLightSwitchTarget> targets, List<uint> pins, ref int nodes)
        {
            if (hierarchyObj == IntPtr.Zero || depth >= FurnitureLightFlameMaxDepth || nodes >= FurnitureLightFlameMaxNodes)
            {
                return;
            }

            if (!this.TryInvokeAuraMonoZeroArg(hierarchyObj, out IntPtr childrenObj, "GetAllChild")
                || childrenObj == IntPtr.Zero)
            {
                return;
            }

            pins.Add(AuraMonoPinNew(childrenObj));
            List<IntPtr> children = new List<IntPtr>();
            if (!this.TryEnumerateAuraMonoCollectionItems(childrenObj, children, pins))
            {
                return;
            }

            for (int i = 0; i < children.Count && nodes < FurnitureLightFlameMaxNodes; i++)
            {
                IntPtr child = children[i];
                if (child == IntPtr.Zero)
                {
                    continue;
                }

                nodes++;
                if (this.TryGetMonoObjectMember(child, "Object", out IntPtr levelObjectObj) && levelObjectObj != IntPtr.Zero)
                {
                    pins.Add(AuraMonoPinNew(levelObjectObj));
                    if (this.IsAuraMonoClassAssignableTo(auraMonoObjectGetClass(levelObjectObj), this.furnitureLightLevelObjectClass))
                    {
                        this.AppendFurnitureLightInteractIds(levelObjectObj, targets);
                    }
                }

                this.CollectFurnitureLightInteractIds(child, depth + 1, targets, pins, ref nodes);
            }
        }

        private void AppendFurnitureLightInteractIds(IntPtr levelObjectObj, List<FurnitureLightSwitchTarget> targets)
        {
            if (!this.TryReadFurnitureLightLevelObjectNetId(levelObjectObj, out ulong levelObjectNetId)
                || levelObjectNetId == 0UL)
            {
                return;
            }

            if (!this.TryInvokeAuraMonoZeroArg(levelObjectObj, out IntPtr arrayObj, "GetInteractIdArray")
                || arrayObj == IntPtr.Zero
                || !this.IsAuraMonoArrayObject(arrayObj)
                || auraMonoArrayLength == null
                || auraMonoArrayAddrWithSize == null)
            {
                return;
            }

            uint pin = AuraMonoPinNew(arrayObj);
            try
            {
                int length = (int)Math.Min(auraMonoArrayLength(arrayObj).ToUInt64(), 8UL);
                IntPtr arrayBase = auraMonoArrayAddrWithSize(arrayObj, sizeof(int), UIntPtr.Zero);
                if (arrayBase == IntPtr.Zero)
                {
                    return;
                }

                for (int i = 0; i < length; i++)
                {
                    int interactId = Marshal.ReadInt32(arrayBase, i * sizeof(int));
                    if (interactId <= 0)
                    {
                        continue;
                    }

                    bool seen = false;
                    for (int s = 0; s < targets.Count; s++)
                    {
                        if (targets[s].InteractId == interactId)
                        {
                            seen = true;
                            break;
                        }
                    }

                    if (!seen)
                    {
                        targets.Add(new FurnitureLightSwitchTarget
                        {
                            InteractId = interactId,
                            LevelObjectNetId = levelObjectNetId,
                        });
                    }
                }
            }
            finally
            {
                AuraMonoPinFree(pin);
            }
        }

        private unsafe bool TryReadFurnitureLightLevelObjectNetId(IntPtr levelObjectObj, out ulong netId)
        {
            netId = 0UL;
            if (!this.TryInvokeAuraMonoZeroArg(levelObjectObj, out IntPtr boxed, "get_netId")
                || boxed == IntPtr.Zero
                || auraMonoObjectUnbox == null
                || !this.TryAuraMonoBoxedIsValueType(boxed))
            {
                return false;
            }

            IntPtr raw = auraMonoObjectUnbox(boxed);
            if (raw == IntPtr.Zero)
            {
                return false;
            }

            netId = *(ulong*)raw;
            return netId != 0UL;
        }

        private unsafe bool FurnitureLightInteractionSwitchesLight(int interactId)
        {
            IntPtr method = this.furnitureLightGetInteractionMethod;
            if (method == IntPtr.Zero || interactId <= 0)
            {
                return false;
            }

            int localId = interactId;
            bool needException = false;
            IntPtr* args = stackalloc IntPtr[2];
            args[0] = (IntPtr)(&localId);
            args[1] = (IntPtr)(&needException);
            IntPtr exc = IntPtr.Zero;
            IntPtr rowObj = auraMonoRuntimeInvoke(method, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || rowObj == IntPtr.Zero)
            {
                return false;
            }

            uint pin = AuraMonoPinNew(rowObj);
            try
            {
                if (!this.TryGetMonoInt32Member(rowObj, "interactionType", out int interactionType)
                    || interactionType != FurnitureLightSwitchInteractionType
                    || !this.TryGetMonoBoolMember(rowObj, "enableSwitchLight", out bool enableSwitchLight))
                {
                    return false;
                }

                return enableSwitchLight;
            }
            finally
            {
                AuraMonoPinFree(pin);
            }
        }

        private unsafe bool TryCastFurnitureLightSwitch(IntPtr playerObj, uint furnitureNetId, FurnitureLightSwitchTarget target, bool switchFlag)
        {
            if (playerObj == IntPtr.Zero || this.furnitureLightCastMethod == IntPtr.Zero
                || this.furnitureLightSwitchArgClass == IntPtr.Zero
                || auraMonoObjectNew == null || auraMonoFieldSetValue == null
                || this.auraMonoRootDomain == IntPtr.Zero
                || !AuraMonoPinningAvailable)
            {
                return false;
            }

            IntPtr argObj = auraMonoObjectNew(this.auraMonoRootDomain, this.furnitureLightSwitchArgClass);
            if (argObj == IntPtr.Zero)
            {
                return false;
            }

            uint pin = AuraMonoPinNew(argObj);
            try
            {
                byte quick = 1;
                byte flag = switchFlag ? (byte)1 : (byte)0;
                int interactId = target.InteractId;
                uint netId = furnitureNetId;
                ulong levelObjectNetId = target.LevelObjectNetId;
                int direction = FurnitureLightTargetDirectionMiddle;
                Vector2 forward = Vector2.zero;
                if (!this.TrySetFurnitureLightField(argObj, "quickCast", &quick)
                    || !this.TrySetFurnitureLightField(argObj, "switchFlag", &flag)
                    || !this.TrySetFurnitureLightField(argObj, "interactId", &interactId)
                    || !this.TrySetFurnitureLightField(argObj, "netid", &netId)
                    || !this.TrySetFurnitureLightField(argObj, "target", &levelObjectNetId)
                    || !this.TrySetFurnitureLightField(argObj, "targetDirection", &direction)
                    || !this.TrySetFurnitureLightField(argObj, "targetForward", &forward))
                {
                    if (MasterLogFurnitureLightRadius)
                    {
                        ModLogger.Msg("[FurnitureLights] flame cast field missing interactId=" + interactId);
                    }

                    return false;
                }

                IntPtr* args = stackalloc IntPtr[1];
                args[0] = argObj;
                IntPtr exc = IntPtr.Zero;
                IntPtr result = auraMonoRuntimeInvoke(this.furnitureLightCastMethod, playerObj, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    if (MasterLogFurnitureLightRadius)
                    {
                        ModLogger.Msg("[FurnitureLights] flame cast exc=0x" + exc.ToInt64().ToString("X")
                            + " interactId=" + interactId);
                    }

                    return false;
                }

                int code = result == IntPtr.Zero ? 0 : this.ReadAuraMonoBoxedInt32(result);
                if (MasterLogFurnitureLightRadius || code != 0)
                {
                    ModLogger.Msg("[FurnitureLights] flame cast interactId=" + interactId
                        + " netId=" + furnitureNetId + " target=" + levelObjectNetId
                        + " switchFlag=" + (switchFlag ? 1 : 0) + " ActionErrorCode=" + code);
                }

                return code == 0;
            }
            finally
            {
                AuraMonoPinFree(pin);
            }
        }

        private unsafe void RestoreFurnitureLightLocomotion(IntPtr playerObj)
        {
            if (playerObj == IntPtr.Zero || auraMonoRuntimeInvoke == null || auraMonoObjectGetClass == null)
            {
                return;
            }

            if (!this.TryGetMonoObjectMember(playerObj, "actionGraph", out IntPtr graph) || graph == IntPtr.Zero)
            {
                return;
            }

            uint pin = AuraMonoPinNew(graph);
            try
            {
                IntPtr method = this.FindAuraMonoMethodOnHierarchy(auraMonoObjectGetClass(graph), "UpdateMotionFocus", 1);
                if (method == IntPtr.Zero)
                {
                    return;
                }

                this.InvokeFurnitureLightMotionFocus(method, graph, false);
                this.InvokeFurnitureLightMotionFocus(method, graph, true);
            }
            finally
            {
                AuraMonoPinFree(pin);
            }
        }

        private unsafe void InvokeFurnitureLightMotionFocus(IntPtr method, IntPtr graph, bool focus)
        {
            byte value = focus ? (byte)1 : (byte)0;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&value);
            IntPtr exc = IntPtr.Zero;
            auraMonoRuntimeInvoke(method, graph, (IntPtr)args, ref exc);
        }

        private unsafe bool TrySetFurnitureLightField(IntPtr obj, string fieldName, void* value)
        {
            if (obj == IntPtr.Zero || auraMonoObjectGetClass == null || auraMonoFieldSetValue == null)
            {
                return false;
            }

            IntPtr field = this.FindAuraMonoFieldOnHierarchy(auraMonoObjectGetClass(obj), fieldName);
            if (field == IntPtr.Zero)
            {
                return false;
            }

            auraMonoFieldSetValue(obj, field, (IntPtr)value);
            return true;
        }

        private void FurnitureLightSetStatus(string message)
        {
            this.furnitureLightStatus = message ?? string.Empty;
            if (this.furnitureLightStatus.Length == 0 || this.furnitureLightStatus == this.furnitureLightLastLoggedStatus)
            {
                return;
            }

            this.furnitureLightLastLoggedStatus = this.furnitureLightStatus;
            ModLogger.Msg("[FurnitureLights] " + this.furnitureLightStatus);
        }
    }
}
