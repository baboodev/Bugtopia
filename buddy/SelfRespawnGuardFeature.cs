using System;
using UnityEngine;

namespace HeartopiaMod
{
    // Self-respawn guard: keep the mod consistent while the server re-creates the local player.
    //
    // Since the 2026-09-24 update the server removes and re-adds the local player after any fast
    // move over ~50-80 m (every teleport point, courier ride and mod teleport of that size;
    // measured 2-6 s). The client DELETES the player entity
    // (StarTownLevelService.OnUnSpawnPlayerEntity -> DataCenter.StarTownData.DeleteEntity) and
    // builds a new one once the server re-adds it and LocalPlayerComponent.WaitSpawnPrepared passes.
    // Three things in the mod went wrong across that gap:
    //   1. The noclip drive caches the player and move component in AuraMonoObjectCache, which only
    //      drops on a WORLD change — after a respawn it kept driving the deleted component, so
    //      noclip and Stealth Foraging lost the player for the rest of the session.
    //   2. GetLocalPlayer() is GameObject.Find("p_player_skeleton(Clone)"): with our own skeleton
    //      gone it found a nearby remote player and kept it cached while that one stayed active,
    //      so the farm and the radar measured from somebody else.
    //   3. The Auto Farm collect wait ran through the gap, so a node reached by a long hop often
    //      timed out before the player even existed again — and collect timeouts escalate the
    //      per-spot park (15 s, 2 min, then 10 min).
    //
    // Driven by the game's own PlayerUnSpawnEvent / PlayerSpawnEvent (GLOBAL, payload
    // { uint playerNetId }, BasePlayerComponent.OnUnSpawn / OnSpawned), filtered to our netId.
    // Between the two the "respawn gap" is open: the farm state machine and the aura hold (their
    // clocks do not advance). Until the spawn event arrives, GetLocalPlayer() returns null and
    // noclip does not resolve the player; the caches are dropped on both edges. The gap closes on
    // the spawn event plus a short settle, or after a hard bound in case the spawn event is missed.
    //
    // Closing it also checks WHERE the player came back. A Stealth Foraging area hop dives ~30 m
    // under the ground, and one respawn there (2026-09-24, Ideapad) never fired its spawn event:
    // the new entity fell for the whole 25 s bound and was found at y = -1254, so the radar saw no
    // nodes and the farm abandoned the area. The mod teleport that triggered the respawn is
    // remembered; if the player is back more than 30 m from it, that teleport is repeated once,
    // and the Stealth Foraging hover is pinned to it again instead of re-seeding wherever the
    // player happens to be.
    public partial class HeartopiaComplete
    {
        private const string PlayerUnSpawnEventName = "ScriptsRefactory.DataAndProtocol.Events.PlayerUnSpawnEvent";
        private const string PlayerSpawnEventName = "ScriptsRefactory.DataAndProtocol.Events.PlayerSpawnEvent";
        private const int PlayerSpawnEventPayloadBytes = 4; // uint playerNetId @0

        // After our spawn event: the new skeleton, its move component and the aura's checker need a
        // moment before anything reads them.
        private const float SelfRespawnSettleSeconds = 0.5f;
        // WaitSpawnPrepared gives up after 20 s on the client; the server's own gap is ~2 s.
        private const float SelfRespawnGapMaxSeconds = 25f;
        // A mod teleport this recent is what the server reacted to.
        private const float SelfRespawnTeleportWindowSeconds = 2f;
        // Back farther than this from that teleport's target = the respawn put us somewhere else.
        private const float SelfRespawnTargetToleranceMeters = 30f;

        // Read by the static GetLocalPlayer(), hence static.
        private static bool selfPlayerRespawnGap;
        // The part of the gap before the spawn event: no entity to read or drive at all.
        private static bool selfPlayerAwaitingSpawn;

        private Vector3 selfRespawnLastTeleportTarget;
        private float selfRespawnLastTeleportAt = -999f;
        private bool selfRespawnHasTarget;
        private Vector3 selfRespawnTarget;
        private bool selfRespawnRetriedTarget;

        private bool selfRespawnRegistered;
        private uint selfRespawnNetId;
        private int selfRespawnNetIdEpoch = -1;
        private float selfRespawnGapOpenedAt;
        private float selfRespawnReleaseAt = -1f; // >= 0 while settling after the spawn event
        private int selfRespawnCount;

        internal static bool IsSelfPlayerRespawnGap => selfPlayerRespawnGap;
        internal static bool IsSelfPlayerAwaitingSpawn => selfPlayerAwaitingSpawn;

        // Called by both TeleportToLocation overloads. A new destination re-arms the one-shot retry.
        private void NoteSelfTeleportTarget(Vector3 target)
        {
            if ((target - this.selfRespawnLastTeleportTarget).sqrMagnitude > 1f)
            {
                this.selfRespawnRetriedTarget = false;
            }
            this.selfRespawnLastTeleportTarget = target;
            this.selfRespawnLastTeleportAt = Time.unscaledTime;
        }

        // Every frame from OnUpdate (cheap when idle: one bool and one float compare).
        private void ProcessSelfRespawnGuardOnUpdate()
        {
            if (!this.selfRespawnRegistered)
            {
                this.selfRespawnRegistered = true;
                bool unspawnOk = this.RegisterGameEventHook(PlayerUnSpawnEventName, PlayerSpawnEventPayloadBytes, this.OnPlayerUnSpawnEventHook);
                bool spawnOk = this.RegisterGameEventHook(PlayerSpawnEventName, PlayerSpawnEventPayloadBytes, this.OnPlayerSpawnEventHook);
                if (!unspawnOk || !spawnOk)
                {
                    FeatureLog.Fail("SelfRespawn", "event hook registration incomplete — unspawn=" + unspawnOk + " spawn=" + spawnOk);
                }
            }

            if (!selfPlayerRespawnGap)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (this.selfRespawnReleaseAt >= 0f && now >= this.selfRespawnReleaseAt)
            {
                this.CloseSelfRespawnGap("respawned after " + (now - this.selfRespawnGapOpenedAt).ToString("F1") + " s");
            }
            else if (now - this.selfRespawnGapOpenedAt >= SelfRespawnGapMaxSeconds)
            {
                this.CloseSelfRespawnGap("no spawn event within " + SelfRespawnGapMaxSeconds.ToString("F0") + " s");
            }
        }

        // Remote players fire the same pair whenever they stream in or out, so this runs often in
        // a busy town; the netId compare is the whole cost once ours is cached for this world.
        private bool IsSelfPlayerNetId(uint netId)
        {
            if (netId == 0U)
            {
                return false;
            }

            int epoch = HeartopiaComplete.AuraMonoWorldEpoch;
            if (this.selfRespawnNetId == 0U || this.selfRespawnNetIdEpoch != epoch)
            {
                if (!this.TryResolveSelfPlayerNetId(out uint self) || self == 0U)
                {
                    return false;
                }
                this.selfRespawnNetId = self;
                this.selfRespawnNetIdEpoch = epoch;
            }

            return netId == this.selfRespawnNetId;
        }

        private void OnPlayerUnSpawnEventHook(GameEventSnapshot e)
        {
            if (!this.IsSelfPlayerNetId(e.ReadUInt32(0)))
            {
                return;
            }

            if (!selfPlayerRespawnGap)
            {
                this.selfRespawnCount++;
                this.selfRespawnGapOpenedAt = Time.unscaledTime;
                this.selfRespawnHasTarget = Time.unscaledTime - this.selfRespawnLastTeleportAt <= SelfRespawnTeleportWindowSeconds;
                this.selfRespawnTarget = this.selfRespawnLastTeleportTarget;
                ModLogger.Msg("[SelfRespawn] the server removed our player (respawn #" + this.selfRespawnCount
                    + ") — holding the farm and the aura until it is back.");
            }

            selfPlayerRespawnGap = true;
            selfPlayerAwaitingSpawn = true;
            this.selfRespawnReleaseAt = -1f;
            this.DropSelfPlayerCaches();
        }

        private void OnPlayerSpawnEventHook(GameEventSnapshot e)
        {
            if (!selfPlayerRespawnGap || !this.IsSelfPlayerNetId(e.ReadUInt32(0)))
            {
                return;
            }

            // The new entity exists now; its skeleton is findable, so the caches can re-resolve,
            // and noclip may hold it during the settle (a Stealth dive has no ground to land on).
            this.DropSelfPlayerCaches();
            selfPlayerAwaitingSpawn = false;
            this.selfRespawnReleaseAt = Time.unscaledTime + SelfRespawnSettleSeconds;
        }

        private void CloseSelfRespawnGap(string reason)
        {
            bool sawSpawn = !selfPlayerAwaitingSpawn;
            selfPlayerRespawnGap = false;
            selfPlayerAwaitingSpawn = false;
            this.selfRespawnReleaseAt = -1f;
            if (!sawSpawn)
            {
                this.DropSelfPlayerCaches(); // on a spawn event they were dropped then
            }
            ModLogger.Msg("[SelfRespawn] player back (" + reason + ").");
            this.RestoreSelfRespawnTarget();
        }

        // See the file header: put the player back where the teleport that caused the respawn was
        // going. One retry per destination: if the server moves us away again it has an opinion,
        // and a second round would only chain respawns.
        private void RestoreSelfRespawnTarget()
        {
            if (!this.selfRespawnHasTarget)
            {
                return;
            }

            this.selfRespawnHasTarget = false;
            Vector3 target = this.selfRespawnTarget;
            if (!this.TryGetNoclipSelfAnchorPose(out Vector3 selfPos, out _, out _, out string source))
            {
                ModLogger.Msg("[SelfRespawn] position after the respawn unreadable — not checking it against " + target.ToString("F1") + ".");
                return;
            }

            float offBy = Vector3.Distance(selfPos, target);
            if (offBy > SelfRespawnTargetToleranceMeters)
            {
                if (this.selfRespawnRetriedTarget)
                {
                    ModLogger.Msg("[SelfRespawn] back at " + selfPos.ToString("F1") + ", " + offBy.ToString("F0")
                        + " m from " + target.ToString("F1") + " again after a retry — leaving it there.");
                    return;
                }

                this.selfRespawnRetriedTarget = true;
                ModLogger.Msg("[SelfRespawn] back at " + selfPos.ToString("F1") + " (via " + source + "), "
                    + offBy.ToString("F0") + " m from the teleport target " + target.ToString("F1") + " — repeating that teleport.");
                this.TeleportToLocation(target);
            }

            // The Stealth Foraging hover is the dive target, not wherever the new entity ended up.
            if (this.StealthForagingActive)
            {
                this.PinStealthForagingNoclipHold(target);
            }
        }

        // Everything that holds our player across frames. Both edges call it: on the unspawn so
        // nothing drives the deleted entity through the gap, on the spawn so the first lookup
        // after it lands on the new one.
        private void DropSelfPlayerCaches()
        {
            this.InvalidateNoclipPlayerDriveCache();
            this.noclipPlayerResolveRetryAt = 0f;
            this.noclipFootHoldValid = false;
            cachedLocalPlayer = null;
            lastLocalPlayerCheckTime = -999f;
        }

        // Farm / aura gate. Status text for the farm panel while holding.
        private bool IsSelfRespawnHoldActive(out string status)
        {
            if (!selfPlayerRespawnGap)
            {
                status = null;
                return false;
            }

            status = this.selfRespawnReleaseAt >= 0f
                ? "Player respawned — settling..."
                : "Waiting for the server to respawn the player ("
                    + (Time.unscaledTime - this.selfRespawnGapOpenedAt).ToString("F0") + " s)";
            return true;
        }
    }
}
