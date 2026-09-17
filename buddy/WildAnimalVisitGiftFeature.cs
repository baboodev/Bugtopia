using System;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace HeartopiaMod
{
    // Visiting wild animals that carry a gift: a radar / game-map marker ("Daily -> Gift Animals")
    // and an opt-in auto-claim (Animal Care).
    //
    // How the game models it (ilspy-dumps + live session, 2026-09-14):
    //   - A wild animal that comes to visit carries XDT.Scene.Shared.Modules.Gift.AnimalGiftComponent
    //     {Type=Visit, DropGroup, ExtraGifts} on its OWN ECS entity. There is no gift-box entity for
    //     it (WildAnimalGiftComponent count was 0 with the gift animal standing next to the player),
    //     which is why "Claim All Wild Gifts" never sees it: that routine starts from
    //     WildAnimalProtocolManager.HaveGift() — the gift-BOX group list — and bails when it is empty.
    //   - WildAnimalProtocolManager.HaveGift(EcsEntity) is the game's own verdict: the component
    //     exists AND, for Visit gifts, today's PlayerSpeciesVisitComponent.TodayGiftTakenCount is under
    //     AnimalSystemConf[7] (3 per day) and this player's GiftTaken entry is under AnimalSystemConf[10]
    //     (1 per animal). WildAnimalGiftCommand shows the in-world gift prompt from the same value, so
    //     a marker here appears exactly when the game would offer the pickup.
    //   - Claim = AnimalProtocolManager.TakeGift(netId) -> AnimalGiftTakeNetworkCommand{Host}. The
    //     command carries no player position and the client's IsExecutable is 0. Live test
    //     2026-09-14: claimed from 36.7 m, haveGift flipped false and the reward (a puzzle box)
    //     reached the bag. The only reach limit we know of is streaming: the animal's view entity has
    //     to exist around the player, and this scan only ever sees those.
    //
    // Why a throttled scan and not an event: WildAnimalVisitEvent only reports that a visit started
    // or ended (per group, no netId); the animal streams in later and the gift flag changes through a
    // DataCenter component update that dispatches no EventCenter event. GetComponents<WildAnimalComponent>
    // returns a handful of animals, so a 2 s scan is cheap.
    //
    // Auto-claim shipped for everyone 2026-09-17 (it started behind the beta gate). Whether the server
    // keeps a record of the claim distance is still unknown.
    public partial class HeartopiaComplete
    {
        private const string WildVisitGiftTag = "WildVisitGift";
        private const string WildVisitGiftAnimalViewClassName = "XDTLevelAndEntity.Gameplay.Component.WildAnimal.WildAnimalComponent";
        private const float WildVisitGiftScanInterval = 2f;
        private const float WildVisitGiftScanNotReadyBackoff = 5f;
        private const float WildVisitGiftClaimRetrySeconds = 10f;  // per netId, between attempts
        private const int WildVisitGiftClaimMaxAttempts = 3;       // then the animal is left alone
        private const float WildVisitGiftClaimDripSeconds = 0.5f;  // between sends
        private const string WildVisitGiftTrackedMarkerPrefix = "WildGiftAnimalMarker_";
        // Icon everywhere is the game's own animal paw, "ui_dynamic_hud_map_mark_animalgroup" (Map atlas):
        // the game map gets it from a TrackType.Animal track (HeartopiaComplete.MapSpots.cs), the ESP tag
        // crops it out of the loaded SpriteAtlas (HeartopiaComplete.Radar.cs, TryGetRadarIconFromSpriteAtlas).
        internal const string WildVisitGiftIconSpriteName = "ui_dynamic_hud_map_mark_animalgroup";

        private sealed class WildVisitGiftEntry
        {
            public uint NetId;
            public Vector3 Position;
            public float FirstSeenAt;
            public float LastSeenAt;
            public float LastClaimAt;
            public int ClaimAttempts;
        }

        private struct WildVisitGiftAnimalSighting
        {
            public uint NetId;
            public Vector3 Position;
        }

        // Radar category — session-only, like every other radar flag.
        private bool showWildGiftAnimalRadar;
        // Persisted (Config.xml).
        private bool wildAnimalAutoClaimVisitGifts;

        private readonly Dictionary<uint, WildVisitGiftEntry> wildVisitGiftEntries = new Dictionary<uint, WildVisitGiftEntry>();
        private readonly List<WildVisitGiftAnimalSighting> wildVisitGiftSightings = new List<WildVisitGiftAnimalSighting>();
        private readonly HashSet<uint> wildVisitGiftPresentScratch = new HashSet<uint>();
        private readonly HashSet<uint> wildVisitGiftSeenScratch = new HashSet<uint>();
        private readonly List<uint> wildVisitGiftScratchIds = new List<uint>();
        private readonly Dictionary<uint, GameObject> trackedWildGiftAnimalMarkers = new Dictionary<uint, GameObject>();
        private IntPtr wildVisitGiftAnimalViewClass = IntPtr.Zero;
        private bool wildVisitGiftResolveRegistered;
        private float wildVisitGiftNextScanAt;
        private bool wildVisitGiftHasScanned;
        private float wildVisitGiftNextClaimAt;
        private int wildVisitGiftScanEpoch = -1;
        private int wildVisitGiftSendCount;
        private int wildVisitGiftClaimedCount;
        private bool wildVisitGiftAutoLastState;
        private FeatureBreakerState wildVisitGiftBreaker;

        private bool IsWildVisitGiftAutoClaimActive => this.wildAnimalAutoClaimVisitGifts;

        internal string GetWildVisitGiftLiveSummary()
        {
            return this.wildVisitGiftEntries.Count + " animal(s) with a gift in view, sent "
                + this.wildVisitGiftSendCount + ", claimed " + this.wildVisitGiftClaimedCount + ".";
        }

        // Called when either surface is switched on from the UI: a resolve that already gave up (or
        // never ran because nothing wanted it) must run now, not at the next world load. Never call
        // this from a per-frame path — it would reset the gate's bounded retry counter every frame.
        private void OnWildVisitGiftSurfaceEnabled()
        {
            this.EnsureWildVisitGiftRegistered();
            if (this.wildVisitGiftAnimalViewClass == IntPtr.Zero)
            {
                this.ResetWorldReadyCallback(WildVisitGiftTag);
            }
        }

        // Metadata-only registration; the Mono resolve itself runs on the world-ready gate.
        private void EnsureWildVisitGiftRegistered()
        {
            if (!this.wildVisitGiftResolveRegistered)
            {
                this.wildVisitGiftResolveRegistered = true;
                this.RegisterWorldReadyCallback(WildVisitGiftTag, this.TryResolveWildVisitGiftOnWorldReady);
            }
        }

        private bool TryResolveWildVisitGiftOnWorldReady()
        {
            if (this.wildVisitGiftAnimalViewClass != IntPtr.Zero)
            {
                return true;
            }

            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
            {
                return false;
            }

            this.wildVisitGiftAnimalViewClass = this.FindAuraMonoClassByFullName(WildVisitGiftAnimalViewClassName);
            if (this.wildVisitGiftAnimalViewClass == IntPtr.Zero)
            {
                FeatureLog.Fail(WildVisitGiftTag, "resolve pending: " + WildVisitGiftAnimalViewClassName + " not found");
                return false;
            }

            FeatureLog.Once(WildVisitGiftTag, "resolved", "resolved WildAnimalComponent view class");
            return true;
        }

        // ── per-frame work (auto-claim) ─────────────────────────────────────────────────────────

        private void ProcessWildVisitGiftOnUpdate()
        {
            bool active = this.IsWildVisitGiftAutoClaimActive;
            if (active != this.wildVisitGiftAutoLastState)
            {
                this.wildVisitGiftAutoLastState = active;
                FeatureLog.Life(WildVisitGiftTag, active
                    ? "auto-claim ON"
                    : "auto-claim OFF — session totals: sent=" + this.wildVisitGiftSendCount + " claimed=" + this.wildVisitGiftClaimedCount);
            }

            if (!active || !this.IsWorldReady)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (!this.wildVisitGiftBreaker.ShouldRun(now))
            {
                return;
            }

            try
            {
                this.EnsureWildVisitGiftRegistered();
                if (this.RefreshWildVisitGiftEntries())
                {
                    this.TryIssueWildVisitGiftClaim();
                }

                this.wildVisitGiftBreaker.Success();
            }
            catch (Exception ex)
            {
                this.wildVisitGiftBreaker.Failure(WildVisitGiftTag, ex, now);
            }
        }

        // Throttled scan shared by auto-claim and the radar category. Returns true when the entry
        // table reflects a completed scan (this tick or a recent one).
        private bool RefreshWildVisitGiftEntries()
        {
            float now = Time.unscaledTime;
            if (this.wildVisitGiftScanEpoch != this.WorldReadyEpoch)
            {
                // New world: netIds are per instance, and the markers hang off the old container.
                this.wildVisitGiftScanEpoch = this.WorldReadyEpoch;
                this.wildVisitGiftEntries.Clear();
                this.ClearWildGiftAnimalTrackedMarkers();
                this.wildVisitGiftHasScanned = false;
            }

            if (now < this.wildVisitGiftNextScanAt)
            {
                return this.wildVisitGiftHasScanned;
            }

            if (this.wildVisitGiftAnimalViewClass == IntPtr.Zero)
            {
                this.EnsureWildVisitGiftRegistered();
                this.wildVisitGiftNextScanAt = now + WildVisitGiftScanNotReadyBackoff;
                return false;
            }

            // Fail closed without the gchandle exports: an unpinned enumeration walks movable sgen
            // memory (memory: auramono-pinning-fail-closed).
            if (!AuraMonoPinningAvailable || !this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                || !this.TryHomelandFarmIsAuraMonoGetComponentsReady(out _))
            {
                this.wildVisitGiftNextScanAt = now + WildVisitGiftScanNotReadyBackoff;
                return false;
            }

            this.wildVisitGiftNextScanAt = now + WildVisitGiftScanInterval;
            if (!this.TryCollectWildVisitGiftSightings())
            {
                return this.wildVisitGiftHasScanned;
            }

            // Gift verdicts run on scalars only — the component pins are already released, and each
            // invoke pins the one boxed entity it works on.
            this.wildVisitGiftPresentScratch.Clear();
            this.wildVisitGiftSeenScratch.Clear();
            for (int i = 0; i < this.wildVisitGiftSightings.Count; i++)
            {
                WildVisitGiftAnimalSighting sighting = this.wildVisitGiftSightings[i];
                this.wildVisitGiftPresentScratch.Add(sighting.NetId);
                if (!this.WildVisitGiftAnimalHasClaimableGift(sighting.NetId))
                {
                    continue;
                }

                this.wildVisitGiftSeenScratch.Add(sighting.NetId);
                if (!this.wildVisitGiftEntries.TryGetValue(sighting.NetId, out WildVisitGiftEntry entry))
                {
                    entry = new WildVisitGiftEntry { NetId = sighting.NetId, FirstSeenAt = now };
                    this.wildVisitGiftEntries[sighting.NetId] = entry;
                    FeatureLog.Once(WildVisitGiftTag, "first-seen", "first gift animal seen: netId=" + sighting.NetId
                        + " at " + sighting.Position.ToString("F1"));
                    FeatureLog.Life(WildVisitGiftTag, "+ gift animal netId=" + sighting.NetId + " pos=" + sighting.Position.ToString("F1"));
                }

                entry.Position = sighting.Position;
                entry.LastSeenAt = now;
            }

            this.wildVisitGiftHasScanned = true;

            this.wildVisitGiftScratchIds.Clear();
            foreach (KeyValuePair<uint, WildVisitGiftEntry> kv in this.wildVisitGiftEntries)
            {
                if (!this.wildVisitGiftSeenScratch.Contains(kv.Key))
                {
                    this.wildVisitGiftScratchIds.Add(kv.Key);
                }
            }

            for (int i = 0; i < this.wildVisitGiftScratchIds.Count; i++)
            {
                uint netId = this.wildVisitGiftScratchIds[i];
                WildVisitGiftEntry gone = this.wildVisitGiftEntries[netId];
                this.wildVisitGiftEntries.Remove(netId);
                this.RemoveWildGiftAnimalTrackedMarker(netId);

                bool animalStillHere = this.wildVisitGiftPresentScratch.Contains(netId);
                if (animalStillHere && gone.ClaimAttempts > 0)
                {
                    this.wildVisitGiftClaimedCount++;
                    FeatureLog.Life(WildVisitGiftTag, "claimed netId=" + netId + " after " + gone.ClaimAttempts
                        + " send(s), " + (now - gone.FirstSeenAt).ToString("F0") + " s after it was seen (total " + this.wildVisitGiftClaimedCount + ")");
                    this.AddMenuNotification(this.L("Wild animal gift claimed"), new Color(0.45f, 1f, 0.55f));
                }
                else
                {
                    FeatureLog.Life(WildVisitGiftTag, "- gift animal netId=" + netId
                        + (animalStillHere ? " no longer has a gift (taken by hand or daily limit reached)" : " out of view")
                        + (gone.ClaimAttempts > 0 ? " after " + gone.ClaimAttempts + " send(s)" : string.Empty));
                }
            }

            return true;
        }

        // Enumerate the view WildAnimalComponent instances and read each owner entity's netId +
        // position. Pin discipline for the moving sgen GC: the component list is pinned by
        // TryAuraMonoGetComponentObjects, each derived entity is pinned across its two reads.
        private bool TryCollectWildVisitGiftSightings()
        {
            this.wildVisitGiftSightings.Clear();
            List<uint> compPins = new List<uint>();
            try
            {
                // The 4-arg overload: an empty world is a successful scan with zero rows, so animals
                // that walked away are still pruned.
                if (!this.TryAuraMonoGetComponentObjects(this.wildVisitGiftAnimalViewClass, out List<IntPtr> components,
                        out bool infrastructureOk, compPins) || components == null)
                {
                    if (infrastructureOk)
                    {
                        return true;
                    }

                    FeatureLog.Fail(WildVisitGiftTag, "GetComponents<WildAnimalComponent> could not run — scan skipped");
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
                        if (!this.TryGetMonoUInt32Member(entityObj, "netId", out uint netId) || netId == 0u
                            || !this.TryGetMonoVector3Member(entityObj, "position", out Vector3 pos))
                        {
                            continue;
                        }

                        this.wildVisitGiftSightings.Add(new WildVisitGiftAnimalSighting { NetId = netId, Position = pos });
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

        // The game's own verdict (WildAnimalProtocolManager.HaveGift(EcsEntity)) on the network
        // entity — it already applies the per-day and per-animal visit limits.
        private bool WildVisitGiftAnimalHasClaimableGift(uint netId)
        {
            if (!this.TryGetNetworkEntityAuraMono(netId, out IntPtr networkEntityObj) || networkEntityObj == IntPtr.Zero)
            {
                return false;
            }

            // Boxed EcsEntity: the invoke below unboxes it and allocates its own result, so keep the
            // box from moving under the unboxed argument pointer.
            uint pin = AuraMonoPinNew(networkEntityObj);
            try
            {
                return this.TryAuraMonoWildAnimalHaveGiftEntity(networkEntityObj);
            }
            finally
            {
                AuraMonoPinFree(pin);
            }
        }

        // ── claim ───────────────────────────────────────────────────────────────────────────────

        private void TryIssueWildVisitGiftClaim()
        {
            float now = Time.unscaledTime;
            if (now < this.wildVisitGiftNextClaimAt || this.wildVisitGiftEntries.Count == 0)
            {
                return;
            }

            // "Claim All Wild Gifts" owns TakeGift (and its group filter) while it runs.
            if (this.wildAnimalGiftCoroutine != null)
            {
                return;
            }

            WildVisitGiftEntry target = null;
            foreach (WildVisitGiftEntry entry in this.wildVisitGiftEntries.Values)
            {
                if (entry.ClaimAttempts >= WildVisitGiftClaimMaxAttempts
                    || (entry.ClaimAttempts > 0 && now - entry.LastClaimAt < WildVisitGiftClaimRetrySeconds))
                {
                    continue;
                }

                target = entry;
                break;
            }

            if (target == null)
            {
                return;
            }

            target.ClaimAttempts++;
            target.LastClaimAt = now;
            this.wildVisitGiftNextClaimAt = now + WildVisitGiftClaimDripSeconds;

            string distance = this.TryGetLocalPlayerPosition(out Vector3 playerPos) && playerPos != Vector3.zero
                ? Vector3.Distance(playerPos, target.Position).ToString("F1") + " m"
                : "unknown";
            if (this.TryInvokeWildAnimalTakeGiftAuraMono(target.NetId, out string status))
            {
                this.wildVisitGiftSendCount++;
                FeatureLog.Once(WildVisitGiftTag, "first-send", "first TakeGift sent: netId=" + target.NetId + " dist=" + distance);
                FeatureLog.Life(WildVisitGiftTag, "TakeGift(" + target.NetId + ") attempt " + target.ClaimAttempts + " dist=" + distance);
                if (target.ClaimAttempts == WildVisitGiftClaimMaxAttempts)
                {
                    FeatureLog.Fail(WildVisitGiftTag, "netId=" + target.NetId + " attempt " + WildVisitGiftClaimMaxAttempts
                        + " sent — if the gift is still there after this, the server refuses it; leaving the animal alone");
                }
            }
            else
            {
                FeatureLog.Fail(WildVisitGiftTag, "TakeGift not sent for netId=" + target.NetId + ": " + status);
            }
        }

        // ── radar markers ───────────────────────────────────────────────────────────────────────

        // Called from RunRadar while "Gift Animals" is on. Markers are keyed by netId, named with
        // WildVisitGiftTrackedMarkerPrefix so the radar's per-scan child sweep keeps them, and MOVED
        // on every pass — the animal walks, and the map track sync reads the marker's transform.
        private void SyncWildGiftAnimalRadarMarkers(Vector3 scanOrigin, Material xRay, Material bg)
        {
            FeatureLog.Once(WildVisitGiftTag, "radar-first-sync", "gift animal markers syncing — the Gift Animals radar category is on");
            this.EnsureWildVisitGiftRegistered();
            this.RefreshWildVisitGiftEntries();

            float maxRangeSqr = this.radarMaxDistance * this.radarMaxDistance;
            this.wildVisitGiftSeenScratch.Clear();
            foreach (WildVisitGiftEntry entry in this.wildVisitGiftEntries.Values)
            {
                if ((scanOrigin - entry.Position).sqrMagnitude > maxRangeSqr)
                {
                    continue;
                }

                this.wildVisitGiftSeenScratch.Add(entry.NetId);
                if (this.trackedWildGiftAnimalMarkers.TryGetValue(entry.NetId, out GameObject existing) && existing != null)
                {
                    existing.transform.position = entry.Position;
                    continue;
                }

                GameObject marker = this.CreateMarker(entry.Position, "giftanimal", xRay, bg, null);
                if (marker == null)
                {
                    continue;
                }

                marker.name = WildVisitGiftTrackedMarkerPrefix + entry.NetId.ToString();
                this.trackedWildGiftAnimalMarkers[entry.NetId] = marker;
            }

            this.wildVisitGiftScratchIds.Clear();
            foreach (KeyValuePair<uint, GameObject> tracked in this.trackedWildGiftAnimalMarkers)
            {
                if (tracked.Value == null || !this.wildVisitGiftSeenScratch.Contains(tracked.Key))
                {
                    this.wildVisitGiftScratchIds.Add(tracked.Key);
                }
            }

            for (int i = 0; i < this.wildVisitGiftScratchIds.Count; i++)
            {
                this.RemoveWildGiftAnimalTrackedMarker(this.wildVisitGiftScratchIds[i]);
            }
        }

        private static bool IsWildGiftAnimalTrackedMarkerName(string markerName)
        {
            return !string.IsNullOrEmpty(markerName)
                && markerName.StartsWith(WildVisitGiftTrackedMarkerPrefix, StringComparison.Ordinal);
        }

        private void RemoveWildGiftAnimalTrackedMarker(uint netId)
        {
            if (this.trackedWildGiftAnimalMarkers.TryGetValue(netId, out GameObject marker) && marker != null)
            {
                this.RemoveMarkerMetadata(marker);
                this.RemoveTrackedMarkerMapping(marker);
                Object.Destroy(marker);
            }

            this.trackedWildGiftAnimalMarkers.Remove(netId);
        }

        private void ClearWildGiftAnimalTrackedMarkers()
        {
            foreach (KeyValuePair<uint, GameObject> entry in this.trackedWildGiftAnimalMarkers)
            {
                if (entry.Value != null)
                {
                    this.RemoveMarkerMetadata(entry.Value);
                    this.RemoveTrackedMarkerMapping(entry.Value);
                    Object.Destroy(entry.Value);
                }
            }

            this.trackedWildGiftAnimalMarkers.Clear();
        }
    }
}
