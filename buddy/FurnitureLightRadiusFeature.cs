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
    // the UgcSwitch byte's two bits drive DIFFERENT visuals — BatchEntity.cs:1645-1647 applies
    // Get(Light) to the dye/emissive + room-lighting dirty flag and Get(Operate) to the
    // SetProp material property, which IS the flame for campfires (their MechanismItem row
    // carries no timeline, so the Operate bit is the only flame driver). The vanilla switch
    // button (SwitchInteractionCommand) therefore sets BOTH bits at once; flipping only Light
    // lights the model but never produces a flame. Payload semantics: bits 0-1 are the whole
    // byte's meaning, so the absolute value (on ? 0b11 : 0b00) is complete and idempotent —
    // no current-state read is needed, and re-sending the same value is a no-op server-side.
    //
    // Send: HomelandProtocolManager.SwitchState(uint netId, uint buildRootNetId, in UgcSwitch)
    // — the same absolute packet the button sends (SendBuildBatchOperation → Building channel),
    // exactly the op Auto Repair already ships on that channel. buildRootNetId comes from the
    // piece's own get_HomeNetId, the same FurnitureBaseData.homeNetId the vanilla 2-arg
    // overload resolves. THE OVERLOAD IS NOT AMBIGUOUS-ARITY SAFE: there are two 3-arg
    // SwitchState methods ((…, Idx) single-bit toggle and (…, in UgcSwitch) absolute), and
    // mono_class_get_method_from_name returns whichever is declared first. The resolve uses
    // FindAuraMonoMethodByParamTypes instead — the absolute overload is identified by its
    // signature {U4, U4, BYREF} and an unverified candidate is never stored or invoked.
    // DecodeTypeEntityData has the same trap ((int) vs (string)) → {I4}.
    //
    // Capability gate: TableData.DecodeTypeEntityData(staticId) (static, global-namespace
    // TableData in the EcsClient image) → TableEntityType row → f_asLight set AND
    // id == 55 (the lighting family — the only switch behavior traced end to end; f_asLight
    // alone spans 19 furniture/build types and the switch byte would persistently override
    // their auto-on fallback, see the constant comment). The TableUgcSwitch /
    // MechanismItem.ugcSkills / InteractionEffect paths were all live-disproven as light
    // sources — do not re-add them.
    //
    // Scope: pieces whose get_OwnerId is the local player only (same getter + rationale as
    // FurnitureAutoRepair). The build channel's server-side permission for other players'
    // pieces is unverified — sending for them would burn rejected packets.
    public partial class HeartopiaComplete
    {
        internal static bool MasterLogFurnitureLightRadius = false;

        private const float FurnitureLightDefaultRadiusMeters = 15f;
        private const float FurnitureLightMinRadiusMeters = 3f;
        private const float FurnitureLightMaxRadiusMeters = 60f;
        // One action must not put a whole festival of pieces on the wire in one frame — the
        // leftover candidates go out on the next press.
        private const int FurnitureLightMaxSendsPerAction = 64;
        // EntityType row id of the lighting type (DB EntityType id 55, staticId range
        // 350001-359999): the ONLY family whose switch behavior was traced end to end
        // (campfires, braziers, lanterns, crow lamps — the 28 flame pieces of the live pile
        // scan are all type 55). f_asLight=1 alone is NOT the gate: it spans 19 entity types
        // (bed/chair/wardrobe/door/roof/brick/catnest/photoframe/displaybox/...), and writing
        // a UgcSwitchComponent to a piece that has no vanilla switch permanently overrides
        // the auto-on fallback in HomelandUtility.GetLightState (MakeUgcSwitch returns the
        // synced component raw) — a server-persisted wrong glow. Same "not verified ⇒
        // excluded" rule the cooker (id 57) follows.
        private const int FurnitureLightLightingEntityTypeId = 55;

        // MonoTypeEnumeration (CorElementType) values the signature check compares against:
        // uint32 params, and a BYREF slot for the `in UgcSwitch` parameter (the enum overload
        // would present VALUETYPE there — the check rejects it).
        private const int MonoTypeI4 = 0x08;
        private const int MonoTypeU4 = 0x09;
        private const int MonoTypeByref = 0x10;
        // (The enum overload would present VALUETYPE 0x11 in slot 2 — the {U4, U4, BYREF}
        // expectation rejects it; kept here only as the documented counter-example.)

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

        private string furnitureLightStatus = "idle";
        private string furnitureLightLastLoggedStatus = string.Empty;

        private bool furnitureLightRegistrationsDone;
        private bool furnitureLightResolveReady;
        private bool furnitureLightResolveFailureLogged;
        private IntPtr furnitureLightComponentClass;    // FurnitureBaseComponent
        private IntPtr furnitureLightProtocolClass;     // HomelandProtocolManager
        private IntPtr furnitureLightTableDataClass;    // TableData (EcsClient, empty namespace)
        private IntPtr furnitureLightSwitchStateMethod; // (uint, uint, in UgcSwitch) — signature-verified only
        private IntPtr furnitureLightDecodeMethod;      // TableData.DecodeTypeEntityData(int) — signature-verified only
        private IntPtr furnitureLightOwnerGetter;       // FurnitureBaseComponent.get_OwnerId -> uint
        private IntPtr furnitureLightStaticIdGetter;    // FurnitureBaseComponent.get_StaticId -> int
        private IntPtr furnitureLightHomeNetGetter;     // FurnitureBaseComponent.get_HomeNetId -> uint

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

        public string GetFurnitureLightStatus() => this.furnitureLightStatus ?? string.Empty;

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
                    new int[] { MonoTypeU4, MonoTypeU4, MonoTypeByref }, "[FurnitureLights]");
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

            this.furnitureLightResolveReady = this.furnitureLightSwitchStateMethod != IntPtr.Zero
                && this.furnitureLightDecodeMethod != IntPtr.Zero
                && this.furnitureLightOwnerGetter != IntPtr.Zero
                && this.furnitureLightStaticIdGetter != IntPtr.Zero
                && this.furnitureLightHomeNetGetter != IntPtr.Zero;

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
                this.FurnitureLightSetStatus("not ready yet — enter a homeland and retry");
                this.AddMenuNotification(this.L("Furniture lights: ") + this.furnitureLightStatus, FurnitureLightFailColor);
                return;
            }

            float now = Time.unscaledTime;
            if (!this.furnitureLightBreaker.ShouldRun(now))
            {
                this.FurnitureLightSetStatus("cooling down after a repeated error");
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
                this.FurnitureLightSetStatus("error: " + ex.Message);
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
            status = "AuraMono unavailable";
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
                    status = infrastructureOk ? "no furniture pieces loaded" : "component scan unavailable";
                    return false;
                }

                if (!this.TryGetLocalPlayerPosition(out Vector3 playerPos))
                {
                    status = "player position unavailable";
                    return false;
                }

                // Same id the game's own switch button compares against (the repair feature's
                // rationale): without it every piece would be skipped rather than sent.
                if (!this.TryResolveSelfPlayerNetId(out uint selfNetId) || selfNetId == 0U)
                {
                    status = "self player netId unavailable";
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

                    uint ownerId;
                    if (!this.TryInvokeAuraMonoUIntGetter(obj, this.furnitureLightOwnerGetter, out ownerId)
                        || ownerId != selfNetId)
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
                status = "no switchable light pieces of mine within " + (int)radius + " m"
                    + (skippedNoHome > 0 ? " (" + skippedNoHome + " without house root)" : "")
                    + " (pieces seen: " + piecesSeen + ")";
                return false;
            }

            int sent = 0;
            int failed = 0;
            string lastError = null;
            int cap = Math.Min(targets.Count, FurnitureLightMaxSendsPerAction);
            for (int i = 0; i < cap; i++)
            {
                if (!this.TrySendFurnitureLightSwitch(targets[i].NetId, targets[i].HomeNetId, turnOn, out string error))
                {
                    failed++;
                    lastError = error;
                    continue;
                }

                sent++;
            }

            status = (turnOn ? "ON" : "OFF") + ": sent " + sent
                + (targets.Count > cap ? ", capped — closest " + cap + " sent, " + (targets.Count - cap) + " farther pieces not sent" : "")
                + (failed > 0 ? ", failed " + failed + " (" + lastError + ")" : "")
                + (skippedNoHome > 0 ? ", skipped " + skippedNoHome + " no house root" : "");
            if (MasterLogFurnitureLightRadius)
            {
                ModLogger.Msg("[FurnitureLights] " + (turnOn ? "ON" : "OFF") + " radius=" + radius
                    + " pieces=" + piecesSeen + " targets=" + targets.Count + " sent=" + sent + " failed=" + failed);
            }

            return sent > 0 && failed == 0;
        }

        // One static invoke of the signature-verified absolute SwitchState. The UgcSwitch
        // struct is { byte value }; byref args travel as a pointer to the local, the same
        // convention TrySendFurnitureRepair uses for uint. Both bits carry the same value —
        // the vanilla button's SetMask({Light, Operate}, value) result (file header).
        private unsafe bool TrySendFurnitureLightSwitch(uint netId, uint homeNetId, bool turnOn, out string error)
        {
            error = null;
            IntPtr method = this.furnitureLightSwitchStateMethod;
            if (method == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                error = "SwitchState unresolved";
                return false;
            }

            uint localNetId = netId;
            uint localRootNetId = homeNetId;
            byte switchValue = turnOn ? (byte)0b11 : (byte)0b00;

            IntPtr* args = stackalloc IntPtr[3];
            args[0] = (IntPtr)(&localNetId);
            args[1] = (IntPtr)(&localRootNetId);
            args[2] = (IntPtr)(&switchValue);

            IntPtr exc = IntPtr.Zero;
            auraMonoRuntimeInvoke(method, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                error = "exc=0x" + exc.ToInt64().ToString("X");
                return false;
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

                lightPiece = asLight && entityTypeId == FurnitureLightLightingEntityTypeId;
                this.furnitureLightCapabilityCache[staticId] = lightPiece;
                if (MasterLogFurnitureLightRadius)
                {
                    ModLogger.Msg("[FurnitureLights] capability staticId=" + staticId
                        + " entityTypeId=" + entityTypeId + " f_asLight=" + asLight + " → " + lightPiece);
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
