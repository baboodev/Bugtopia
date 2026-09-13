using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HeartopiaMod
{
    // Pet poop (Entity 7100 "Dog Poop", EntityType 44 "pickable", ids 7100-7199): find every
    // dropping on the map, show it as a radar / map marker, and pick it up automatically when the
    // player is close.
    //
    // How the game does it (ilspy-dumps, verified live 2026-09-11):
    //   - The dropping is a networked ECS entity carrying XDT.Scene.Shared.Modules.Throwable.
    //     PickableComponent{StaticId=7100, CreatedAt, ReasonType, FeatureType, AllowedNetId}.
    //     ThrowableSyncSystem -> ThrowableProtocolManager.SpawnPickable adds a DataCenter entity
    //     (DynamicComponentData{staticId} + PickableComponentData{enablePick=true}) rendered
    //     through BRG, so there is NO per-instance GameObject named after the prefab
    //     (p_dogpoop_dogpoop001) — a GameObject-name scan never sees it. The VIEW entity carries
    //     XDTLevelAndEntity.Gameplay.Component.Pickable.PickableComponent plus
    //     ...Dynamic.DynamicComponent (get_StaticId), which is what this file scans.
    //   - Manual pickup: InteractId 22 PickupShitCommand -> player.Cast(SwitchFurnitureArg type=20)
    //     -> SwitchFurnitureArg.SelfSendSwitchEvent -> ThrowableProtocolManager.Pickup(netId) ->
    //     PickupNetworkCommand{Entity}. We call Pickup(uint) directly (static, non-generic, sends
    //     the command internally). Result: PickupNetworkEvent{Invalid/Success/BagNotEnough};
    //     BagNotEnough surfaces as UITipEvent 93683, poop expiry as tip 266 (PetPoopDisappear).
    //   - Thrown dog toys (Throwable 7000 beanbag) ALSO carry PickableComponentData, so every
    //     pickable is qualified by DynamicComponent.StaticId before it counts as poop.
    //
    // No targeted EventCenter event exists for "a pickable appeared" (DataCreated<T> is a nested
    // generic; EntityCreateEvent fires for every entity), so this is a throttled component scan —
    // GetComponents<PickableComponent> enumerates only the handful of pickables in the world, and
    // the DynamicComponent walk runs only for a netId seen for the first time.
    //
    // Measured 2026-09-11 (4 droppings, one session): the server accepts Pickup at 5.0 m; a fresh
    // dropping is ignored at +0/+4/+8 s and taken at +15 s, so the grace window is 8-15 s; droppings
    // appeared 5-15 min apart with two pets on a walk (server check every DogConst
    // DefaultCheckCooldown=300 s, not every check produces one). Still unknown: whether another
    // player's dog's poop is pickable (AllowedNetId) and how far beyond 5 m the server allows.
    public partial class HeartopiaComplete
    {
        private const string PetPoopTag = "PetPoop";
        private const int PetPoopStaticIdMin = 7100;   // cn_tables EntityType 44 "pickable": minId
        private const int PetPoopStaticIdMax = 7199;   // ... maxId
        internal const int PetPoopItemId = 7100;       // Entity 7100 "Dog Poop" (bag item + icon p_dogpoop_dogpoop001)
        private const string PetPoopPickableViewClassName = "XDTLevelAndEntity.Gameplay.Component.Pickable.PickableComponent";
        private const string PetPoopDynamicViewClassName = "XDTLevelAndEntity.Gameplay.Component.Dynamic.DynamicComponent";
        private const string PetPoopThrowableManagerClassName = "XDTDataAndProtocol.ProtocolService.Throwable.ThrowableProtocolManager";
        private const string PetPoopUiTipEventName = "ScriptsRefactory.DataAndProtocol.Events.UITipEvent";
        private const int PetPoopTipBagFull = 93683;
        private const int PetPoopTipDisappeared = 266;
        private const float PetPoopScanInterval = 1f;
        private const float PetPoopScanNotReadyBackoff = 5f;
        // A dropping is NOT pickable the moment it appears: sends at +0/+4/+8 s were ignored and the
        // first accepted one was at +15 s (2026-09-11). No minimum age (user's call) — instead the
        // retries are dense enough that one lands right after the window opens.
        private const float PetPoopPickupRetrySeconds = 3f;   // per netId, between attempts
        private const int PetPoopPickupMaxAttempts = 8;       // ~24 s of tries, then the dropping is left alone
        private const float PetPoopPickupDripSeconds = 0.5f;  // between sends
        private const float PetPoopBagFullPauseSeconds = 60f;
        // The server only honours Pickup when the player is right on top of the dropping (~2 m,
        // user-measured 2026-09-11; a wider radius just burns the send budget), so this is fixed.
        private const float PetPoopPickupRadius = 2f;
        private const string PetPoopTrackedMarkerPrefix = "PetPoopMarker_";

        private sealed class PetPoopEntry
        {
            public uint NetId;
            public Vector3 Position;
            public float FirstSeenAt;
            public float LastSeenAt;
            public float LastPickupAt;
            public int PickupAttempts;
        }

        private bool showPetPoopRadar;

        private readonly Dictionary<uint, PetPoopEntry> petPoopEntries = new Dictionary<uint, PetPoopEntry>();
        private readonly HashSet<uint> petPoopSeenScratch = new HashSet<uint>();
        private readonly List<uint> petPoopScratchIds = new List<uint>();
        private readonly HashSet<uint> petPoopIgnoredNetIds = new HashSet<uint>(); // pickables that are not poop (thrown toys)
        private readonly Dictionary<uint, GameObject> trackedPetPoopMarkers = new Dictionary<uint, GameObject>();
        private IntPtr petPoopPickableViewClass = IntPtr.Zero;
        private IntPtr petPoopDynamicViewClass = IntPtr.Zero;
        private IntPtr petPoopPickupMethod = IntPtr.Zero;
        private bool petPoopResolveRegistered;
        private bool petPoopTipHookRegistered;
        private float petPoopNextScanAt;
        private bool petPoopHasScanned;
        private float petPoopNextPickupAt;
        private float petPoopLastSendAt = -100f;
        private float petPoopBagFullUntil;
        private int petPoopScanEpoch = -1;
        private int petPoopSendCount;
        private int petPoopCollectedCount;
        // Droppings the walker gave up on (no route / dwell capped): netId -> unscaled time until
        // which the farm must not target them again. The scan keeps listing them for the radar.
        private readonly Dictionary<uint, float> petPoopWalkSkipUntil = new Dictionary<uint, float>();

        // ── toggles ─────────────────────────────────────────────────────────────────────────────

        // Aura Farm means "collect everything in reach", so the running aura is the only switch
        // for pickup — no separate toggle to remember on a walk with the dogs. The foraging farm
        // counts too: its walker targets droppings (HeartopiaComplete.Farm.cs, "node:poop"), and a
        // walk to one is pointless unless the pickup runs when it gets there.
        private bool IsPetPoopPickupActive => this.auraFarmEnabled || this.autoFarmActive;

        // Called from SetAuraFarmEnabled: the aura flipping on mid-session must run the pending
        // resolve now, not at the next world load. Only from there - never from a per-frame path
        // (that would reset the gate's bounded retry counter every frame).
        private void OnPetPoopAuraFarmToggled(bool enabled)
        {
            if (enabled)
            {
                this.EnsurePetPoopHooksRegistered();
                if (!this.ArePetPoopHandlesResolved())
                {
                    this.ResetWorldReadyCallback(PetPoopTag);
                }
            }
            else if (this.petPoopSendCount > 0)
            {
                FeatureLog.Life(PetPoopTag, "aura off — session totals: sent=" + this.petPoopSendCount
                    + " collected=" + this.petPoopCollectedCount);
            }
        }

        // ── walk-to-node surface (HeartopiaComplete.Farm.cs) ────────────────────────────────────

        // Nearest dropping the farm may walk to: on the map right now, not refused by the server
        // (send budget spent) and not parked by the walker. No distance cap of its own — the scan
        // only ever sees view entities streamed in around the player.
        internal bool TryGetNearestPetPoopTarget(Vector3 from, out Vector3 position, out uint netId)
        {
            position = Vector3.zero;
            netId = 0u;
            if (this.petPoopEntries.Count == 0)
            {
                return false;
            }

            float now = Time.unscaledTime;
            float bestDistSqr = float.MaxValue;
            foreach (PetPoopEntry entry in this.petPoopEntries.Values)
            {
                if (entry.PickupAttempts >= PetPoopPickupMaxAttempts)
                {
                    continue;
                }

                if (this.petPoopWalkSkipUntil.TryGetValue(entry.NetId, out float until) && now < until)
                {
                    continue;
                }

                float distSqr = (entry.Position - from).sqrMagnitude;
                if (distSqr < bestDistSqr)
                {
                    bestDistSqr = distSqr;
                    position = entry.Position;
                    netId = entry.NetId;
                }
            }

            return netId != 0u;
        }

        internal bool IsPetPoopStillOnMap(uint netId)
        {
            return netId != 0u && this.petPoopEntries.ContainsKey(netId);
        }

        internal void SkipPetPoopForWalk(uint netId, float seconds)
        {
            if (netId != 0u)
            {
                this.petPoopWalkSkipUntil[netId] = Time.unscaledTime + seconds;
            }
        }

        internal string GetPetPoopLiveSummary()
        {
            int visible = this.petPoopEntries.Count;
            string s = visible + " on the map, sent " + this.petPoopSendCount + ", collected " + this.petPoopCollectedCount + ".";
            if (Time.unscaledTime < this.petPoopBagFullUntil)
            {
                s += " Paused: bag full.";
            }

            return s;
        }

        // Metadata-only registrations (RegisterGameEventHook / RegisterWorldReadyCallback are
        // callable from anywhere); the actual Mono resolve happens on the world-ready gate.
        private void EnsurePetPoopHooksRegistered()
        {
            if (!this.petPoopTipHookRegistered)
            {
                this.petPoopTipHookRegistered = true;
                if (!this.RegisterGameEventHook(PetPoopUiTipEventName, 4, this.OnPetPoopTipEvent))
                {
                    FeatureLog.Fail(PetPoopTag, "UITipEvent hook registration refused — bag-full pauses will not work");
                }
            }

            if (!this.petPoopResolveRegistered)
            {
                this.petPoopResolveRegistered = true;
                this.RegisterWorldReadyCallback(PetPoopTag, this.TryResolvePetPoopHandlesOnWorldReady);
            }
        }

        // ── resolve (world-ready gate) ──────────────────────────────────────────────────────────

        private bool TryResolvePetPoopHandlesOnWorldReady()
        {
            if (this.petPoopPickableViewClass != IntPtr.Zero
                && this.petPoopDynamicViewClass != IntPtr.Zero
                && this.petPoopPickupMethod != IntPtr.Zero)
            {
                return true;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return false;
            }

            if (this.petPoopPickableViewClass == IntPtr.Zero)
            {
                this.petPoopPickableViewClass = this.FindAuraMonoClassByFullName(PetPoopPickableViewClassName);
            }

            if (this.petPoopDynamicViewClass == IntPtr.Zero)
            {
                this.petPoopDynamicViewClass = this.FindAuraMonoClassByFullName(PetPoopDynamicViewClassName);
            }

            if (this.petPoopPickupMethod == IntPtr.Zero)
            {
                IntPtr mgr = this.FindAuraMonoClassByFullName(PetPoopThrowableManagerClassName);
                if (mgr != IntPtr.Zero)
                {
                    this.petPoopPickupMethod = this.FindAuraMonoMethodOnHierarchy(mgr, "Pickup", 1);
                }
            }

            bool ok = this.petPoopPickableViewClass != IntPtr.Zero
                && this.petPoopDynamicViewClass != IntPtr.Zero
                && this.petPoopPickupMethod != IntPtr.Zero;
            if (ok)
            {
                FeatureLog.Once(PetPoopTag, "resolved", "resolved PickableComponent + DynamicComponent views and ThrowableProtocolManager.Pickup(1)");
            }
            else
            {
                FeatureLog.Fail(PetPoopTag, "resolve pending: pickableView=" + (this.petPoopPickableViewClass != IntPtr.Zero)
                    + " dynamicView=" + (this.petPoopDynamicViewClass != IntPtr.Zero)
                    + " Pickup(1)=" + (this.petPoopPickupMethod != IntPtr.Zero));
            }

            return ok;
        }

        private bool ArePetPoopHandlesResolved()
        {
            return this.petPoopPickableViewClass != IntPtr.Zero
                && this.petPoopDynamicViewClass != IntPtr.Zero
                && this.petPoopPickupMethod != IntPtr.Zero;
        }

        // ── per-frame work ──────────────────────────────────────────────────────────────────────

        private void ProcessPetPoopOnUpdate()
        {
            if (!this.IsPetPoopPickupActive || !this.IsWorldReady)
            {
                return;
            }

            this.EnsurePetPoopHooksRegistered();
            FeatureLog.Once(PetPoopTag, "aura-implied", "pickup active because Aura Farm is running");
            if (!this.RefreshPetPoopEntries())
            {
                return;
            }

            this.TryIssuePetPoopPickup();
        }

        // Throttled scan shared by auto pickup and the radar category. Returns true when the entry
        // table reflects a completed scan (this tick or a recent one).
        private bool RefreshPetPoopEntries()
        {
            float now = Time.unscaledTime;
            if (this.petPoopScanEpoch != this.WorldReadyEpoch)
            {
                // New world: netIds are per instance, and the markers hang off the old container.
                this.petPoopScanEpoch = this.WorldReadyEpoch;
                this.petPoopEntries.Clear();
                this.petPoopIgnoredNetIds.Clear();
                this.petPoopWalkSkipUntil.Clear();
                this.ClearPetPoopTrackedMarkers();
                this.petPoopHasScanned = false;
                this.petPoopBagFullUntil = 0f;
            }

            if (now < this.petPoopNextScanAt)
            {
                return this.petPoopHasScanned;
            }

            if (!this.ArePetPoopHandlesResolved())
            {
                this.EnsurePetPoopHooksRegistered();
                this.petPoopNextScanAt = now + PetPoopScanNotReadyBackoff;
                return false;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                || !this.TryHomelandFarmIsAuraMonoGetComponentsReady(out _))
            {
                this.petPoopNextScanAt = now + PetPoopScanNotReadyBackoff;
                return false;
            }

            this.petPoopNextScanAt = now + PetPoopScanInterval;
            this.petPoopSeenScratch.Clear();
            List<uint> newNetIds = null;
            if (!this.TryScanPetPoopPickables(now, ref newNetIds))
            {
                return this.petPoopHasScanned;
            }

            this.petPoopHasScanned = true;
            if (newNetIds != null)
            {
                this.QualifyNewPetPoopEntries(newNetIds);
            }

            // Gone = picked up (by us or anyone), expired (tip 266), or streamed out.
            this.petPoopScratchIds.Clear();
            foreach (KeyValuePair<uint, PetPoopEntry> kv in this.petPoopEntries)
            {
                if (!this.petPoopSeenScratch.Contains(kv.Key))
                {
                    this.petPoopScratchIds.Add(kv.Key);
                }
            }

            for (int i = 0; i < this.petPoopScratchIds.Count; i++)
            {
                uint netId = this.petPoopScratchIds[i];
                PetPoopEntry gone = this.petPoopEntries[netId];
                this.petPoopEntries.Remove(netId);
                this.RemovePetPoopTrackedMarker(netId);
                if (gone.PickupAttempts > 0)
                {
                    this.petPoopCollectedCount++;
                    FeatureLog.Life(PetPoopTag, "collected netId=" + netId + " after " + gone.PickupAttempts
                        + " send(s), " + (now - gone.FirstSeenAt).ToString("F0") + " s on the map (total " + this.petPoopCollectedCount + ")");
                }
                else
                {
                    FeatureLog.Life(PetPoopTag, "- poop netId=" + netId + " gone without our pickup after "
                        + (now - gone.FirstSeenAt).ToString("F0") + " s");
                }
            }

            return true;
        }

        // Enumerate the view PickableComponent instances and read the owner entity's netId +
        // position. Pin discipline for the moving sgen GC: the component list is pinned by
        // TryAuraMonoGetComponentObjects, each derived entity is pinned across its two reads.
        private bool TryScanPetPoopPickables(float now, ref List<uint> newNetIds)
        {
            List<uint> compPins = new List<uint>();
            try
            {
                // The 4-arg overload: the 3-arg one returns false for an EMPTY world too, which left a
                // collected dropping on the map forever (entries were never pruned once the last one
                // was gone). Empty = a successful scan with zero rows.
                if (!this.TryAuraMonoGetComponentObjects(this.petPoopPickableViewClass, out List<IntPtr> components,
                        out bool infrastructureOk, compPins) || components == null)
                {
                    if (infrastructureOk)
                    {
                        return true;
                    }

                    FeatureLog.Fail(PetPoopTag, "GetComponents<PickableComponent> could not run — scan skipped");
                    return false;
                }

                for (int i = 0; i < components.Count; i++)
                {
                    IntPtr comp = components[i];
                    if (comp == IntPtr.Zero
                        || !this.TryGetMonoObjectMember(comp, "entity", out IntPtr entityObj)
                        || entityObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    uint entityPin = AuraMonoPinNew(entityObj);
                    try
                    {
                        // Real network id only — netId==0 would be a local view entity.
                        if (!this.TryGetMonoUInt32Member(entityObj, "netId", out uint netId) || netId == 0u
                            || this.petPoopIgnoredNetIds.Contains(netId))
                        {
                            continue;
                        }

                        if (!this.TryGetMonoVector3Member(entityObj, "position", out Vector3 pos))
                        {
                            continue;
                        }

                        this.petPoopSeenScratch.Add(netId);
                        if (!this.petPoopEntries.TryGetValue(netId, out PetPoopEntry entry))
                        {
                            entry = new PetPoopEntry { NetId = netId, FirstSeenAt = now };
                            this.petPoopEntries[netId] = entry;
                            (newNetIds ?? (newNetIds = new List<uint>())).Add(netId);
                        }

                        entry.Position = pos;
                        entry.LastSeenAt = now;
                    }
                    finally
                    {
                        AuraMonoPinFree(entityPin);
                    }
                }

                return true;
            }
            finally
            {
                FreeAuraMonoPins(compPins);
            }
        }

        // A pickable is poop only when its DynamicComponent.StaticId is in the EntityType 44 range;
        // anything else (a thrown dog toy) is remembered and never scanned again.
        private void QualifyNewPetPoopEntries(List<uint> newNetIds)
        {
            HashSet<uint> want = new HashSet<uint>(newNetIds);
            List<uint> compPins = new List<uint>();
            try
            {
                if (!this.TryAuraMonoGetComponentObjects(this.petPoopDynamicViewClass, out List<IntPtr> components,
                        out bool infrastructureOk, compPins) || components == null)
                {
                    if (!infrastructureOk)
                    {
                        FeatureLog.Fail(PetPoopTag, "GetComponents<DynamicComponent> could not run — new pickables stay unqualified");
                    }

                    return;
                }

                for (int i = 0; i < components.Count && want.Count > 0; i++)
                {
                    IntPtr comp = components[i];
                    if (comp == IntPtr.Zero
                        || !this.TryGetMonoObjectMember(comp, "entity", out IntPtr entityObj)
                        || entityObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    uint netId;
                    uint entityPin = AuraMonoPinNew(entityObj);
                    try
                    {
                        if (!this.TryGetMonoUInt32Member(entityObj, "netId", out netId) || netId == 0u)
                        {
                            continue;
                        }
                    }
                    finally
                    {
                        AuraMonoPinFree(entityPin);
                    }

                    if (!want.Remove(netId))
                    {
                        continue;
                    }

                    if (!this.TryGetMonoInt32Member(comp, "StaticId", out int staticId))
                    {
                        FeatureLog.Fail(PetPoopTag, "DynamicComponent.StaticId unreadable for netId=" + netId + " — treating it as poop");
                        staticId = PetPoopItemId;
                    }

                    if (staticId < PetPoopStaticIdMin || staticId > PetPoopStaticIdMax)
                    {
                        this.petPoopIgnoredNetIds.Add(netId);
                        this.petPoopEntries.Remove(netId);
                        FeatureLog.Detail(PetPoopTag, MasterLogPetPlay, "pickable netId=" + netId + " staticId=" + staticId + " is not poop — ignored");
                        continue;
                    }

                    PetPoopEntry entry = this.petPoopEntries[netId];
                    FeatureLog.Once(PetPoopTag, "first-seen", "first dropping seen: netId=" + netId + " staticId=" + staticId
                        + " at " + entry.Position.ToString("F1"));
                    // Tier 1 on purpose: one line per dropping (about one per 5 min) is the dataset
                    // for "how often do they poop", and a marker/pickup that never happens needs it.
                    FeatureLog.Life(PetPoopTag, "+ poop netId=" + netId + " staticId=" + staticId
                        + " pos=" + entry.Position.ToString("F1"));
                }

                // A pickable whose DynamicComponent is not spawned yet stays in the table and is
                // qualified on the next scan that sees it as new — drop it now so that happens.
                foreach (uint unresolved in want)
                {
                    this.petPoopEntries.Remove(unresolved);
                }
            }
            finally
            {
                FreeAuraMonoPins(compPins);
            }
        }

        // ── pickup ──────────────────────────────────────────────────────────────────────────────

        private void TryIssuePetPoopPickup()
        {
            float now = Time.unscaledTime;
            if (now < this.petPoopNextPickupAt || now < this.petPoopBagFullUntil || this.petPoopEntries.Count == 0)
            {
                return;
            }

            // Skeleton-first player position — transform.root is the SHIP while sea-fishing.
            if (!this.TryGetLocalPlayerPosition(out Vector3 playerPos) || playerPos == Vector3.zero)
            {
                return;
            }

            float radiusSqr = PetPoopPickupRadius * PetPoopPickupRadius;
            PetPoopEntry best = null;
            float bestDistSqr = float.MaxValue;
            foreach (PetPoopEntry entry in this.petPoopEntries.Values)
            {
                if (entry.PickupAttempts >= PetPoopPickupMaxAttempts
                    || now - entry.LastPickupAt < PetPoopPickupRetrySeconds)
                {
                    continue;
                }

                float distSqr = (entry.Position - playerPos).sqrMagnitude;
                if (distSqr > radiusSqr || distSqr >= bestDistSqr)
                {
                    continue;
                }

                best = entry;
                bestDistSqr = distSqr;
            }

            if (best == null)
            {
                return;
            }

            best.PickupAttempts++;
            best.LastPickupAt = now;
            this.petPoopNextPickupAt = now + PetPoopPickupDripSeconds;
            if (this.TryInvokePetPoopPickupAura(best.NetId))
            {
                this.petPoopSendCount++;
                this.petPoopLastSendAt = now;
                FeatureLog.Once(PetPoopTag, "first-send", "first Pickup sent: netId=" + best.NetId
                    + " dist=" + Mathf.Sqrt(bestDistSqr).ToString("F1") + " m");
                FeatureLog.Life(PetPoopTag, "Pickup(" + best.NetId + ") attempt " + best.PickupAttempts
                    + " age=" + (now - best.FirstSeenAt).ToString("F0") + " s dist=" + Mathf.Sqrt(bestDistSqr).ToString("F1"));
                if (best.PickupAttempts == PetPoopPickupMaxAttempts)
                {
                    FeatureLog.Fail(PetPoopTag, "netId=" + best.NetId + " still there after " + PetPoopPickupMaxAttempts
                        + " sends — leaving it (not ours, out of range for the server, or the bag is full)");
                }
            }
            else
            {
                FeatureLog.Fail(PetPoopTag, "Pickup invoke failed for netId=" + best.NetId + " — see earlier resolve lines");
            }
        }

        // ThrowableProtocolManager.Pickup(uint) — static, non-generic; builds PickupNetworkCommand
        // and calls WebRequestUtility.SendCommand<T> in normally-JIT'd game code (never
        // mono_runtime_invoke a generic ourselves). Value-type arg = pointer to the raw value.
        private unsafe bool TryInvokePetPoopPickupAura(uint netId)
        {
            if (netId == 0u
                || this.petPoopPickupMethod == IntPtr.Zero
                || !this.EnsureAuraMonoApiReady()
                || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            try
            {
                uint localNetId = netId;
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&localNetId);
                IntPtr exc = IntPtr.Zero;
                auraMonoRuntimeInvoke(this.petPoopPickupMethod, IntPtr.Zero, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    FeatureLog.Fail(PetPoopTag, "Pickup(" + netId + ") threw inside the game");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                FeatureLog.Fail(PetPoopTag, "Pickup(" + netId + ") invoke exception: " + ex.GetType().Name + ": " + ex.Message);
                return false;
            }
        }

        // UITipEvent{ tipId@0 }. 93683 = PickupResult.BagNotEnough (needs a free bag slot) — only
        // treated as ours when it lands right after one of our sends. 266 = a dropping expired.
        private void OnPetPoopTipEvent(GameEventSnapshot e)
        {
            int tipId = e.ReadInt32(0);
            float now = Time.unscaledTime;
            if (tipId == PetPoopTipBagFull)
            {
                if (this.IsPetPoopPickupActive && now - this.petPoopLastSendAt < 3f && now >= this.petPoopBagFullUntil)
                {
                    this.petPoopBagFullUntil = now + PetPoopBagFullPauseSeconds;
                    FeatureLog.Life(PetPoopTag, "bag full (tip 93683) — pausing pickup for " + PetPoopBagFullPauseSeconds.ToString("F0") + " s");
                }
            }
            else if (tipId == PetPoopTipDisappeared)
            {
                FeatureLog.Detail(PetPoopTag, MasterLogPetPlay, "a dropping expired (tip 266)");
            }
        }

        // ── radar markers ───────────────────────────────────────────────────────────────────────

        // Called from RunRadar while the "Dog Poop" category is on. Markers are keyed by netId and
        // named with PetPoopTrackedMarkerPrefix so the radar's per-scan child sweep keeps them.
        private void SyncPetPoopRadarMarkers(Vector3 scanOrigin, Material xRay, Material bg)
        {
            FeatureLog.Once(PetPoopTag, "radar-first-sync", "poop markers syncing — the Dog Poop radar category is on");
            this.EnsurePetPoopHooksRegistered();
            this.RefreshPetPoopEntries();

            float maxRangeSqr = this.radarMaxDistance * this.radarMaxDistance;
            this.petPoopSeenScratch.Clear();
            foreach (PetPoopEntry entry in this.petPoopEntries.Values)
            {
                if ((scanOrigin - entry.Position).sqrMagnitude > maxRangeSqr)
                {
                    continue;
                }

                this.petPoopSeenScratch.Add(entry.NetId);
                if (this.trackedPetPoopMarkers.TryGetValue(entry.NetId, out GameObject existing) && existing != null)
                {
                    continue;
                }

                GameObject marker = this.CreateMarker(entry.Position, "petpoop", xRay, bg, null);
                if (marker == null)
                {
                    continue;
                }

                marker.name = PetPoopTrackedMarkerPrefix + entry.NetId.ToString();
                this.trackedPetPoopMarkers[entry.NetId] = marker;
            }

            this.petPoopScratchIds.Clear();
            foreach (KeyValuePair<uint, GameObject> tracked in this.trackedPetPoopMarkers)
            {
                if (tracked.Value == null || !this.petPoopSeenScratch.Contains(tracked.Key))
                {
                    this.petPoopScratchIds.Add(tracked.Key);
                }
            }

            for (int i = 0; i < this.petPoopScratchIds.Count; i++)
            {
                this.RemovePetPoopTrackedMarker(this.petPoopScratchIds[i]);
            }
        }

        private static bool IsPetPoopTrackedMarkerName(string markerName)
        {
            return !string.IsNullOrEmpty(markerName)
                && markerName.StartsWith(PetPoopTrackedMarkerPrefix, StringComparison.Ordinal);
        }

        private void RemovePetPoopTrackedMarker(uint netId)
        {
            if (this.trackedPetPoopMarkers.TryGetValue(netId, out GameObject marker) && marker != null)
            {
                this.RemoveMarkerMetadata(marker);
                this.RemoveTrackedMarkerMapping(marker);
                Object.Destroy(marker);
            }

            this.trackedPetPoopMarkers.Remove(netId);
        }

        private void ClearPetPoopTrackedMarkers()
        {
            foreach (KeyValuePair<uint, GameObject> entry in this.trackedPetPoopMarkers)
            {
                if (entry.Value != null)
                {
                    this.RemoveMarkerMetadata(entry.Value);
                    this.RemoveTrackedMarkerMapping(entry.Value);
                    Object.Destroy(entry.Value);
                }
            }

            this.trackedPetPoopMarkers.Clear();
        }
    }
}
