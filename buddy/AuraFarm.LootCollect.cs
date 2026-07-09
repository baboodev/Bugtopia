using System;
using System.Collections.Generic;
using UnityEngine;

namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        private bool auraFarmLootCollectEnabled = true;
        private float auraFarmLootCollectDistance = 100f;
        private const float AuraFarmLootCollectDistanceMin = 1f;
        private const float AuraFarmLootCollectDistanceMax = 500f;
        private const float AuraFarmLootResendCooldown = 5f;
        private const int AuraFarmLootBatchPerTick = 1;
        private const int AuraLootCollectedState = 4;

        // Mirrors GatherConfig.CollectCommandReSendTime (hardcoded `=> 5f` in the game). The game's
        // LootManager skips any loot whose _sendCommandTime is younger than this, and stamps the
        // field when IT queues a pick. Since the 2026-07-09 update PickUpLootNetworkCommand.itemNetId
        // is [VerifyEntity]-validated server-side, so the loser of a mod-vs-game double-send gets the
        // generic "Error in uploaded data" toast (ErrorCode.DataError = 3). Sharing the game's own
        // stamp makes the two senders mutually exclusive.
        private const float AuraLootGameResendWindowSeconds = 5f;

        // Grace period after a world/scene change before loot picks resume: the server tears down
        // the old session and re-syncs map-resource items; a pick sent in that window references an
        // entity the new session no longer knows -> ErrorNetworkEvent(DataError=3) toast.
        private const float AuraLootWorldChangeQuietSeconds = 3f;

        private IntPtr auraLootComponentClass = IntPtr.Zero;
        private IntPtr auraMonoSendPickLootMethodPtr = IntPtr.Zero;
        private IntPtr auraLootSendCommandTimeFieldPtr = IntPtr.Zero;
        private bool auraLootResolverReady;
        private bool auraLootResolverFailed;
        private int auraLootLastWorldEpoch;
        private float auraLootQuietUntil;
        private readonly Dictionary<uint, float> auraLootLastSendAtByNetId = new Dictionary<uint, float>(32);
        private readonly List<AuraLootCandidate> auraLootCandidateBuffer = new List<AuraLootCandidate>(64);
        private readonly List<uint> auraLootScanPins = new List<uint>(64);
        private float auraLootNextCooldownCleanupAt;

        private struct AuraLootCandidate
        {
            public uint NetId;
            public float DistanceSqr;
            // Live LootComponent pointer. Valid ONLY while the auraLootScanPins pins are held, i.e.
            // within the current synchronous UpdateAuraFarmLootCollect pass — never carried across
            // a yield or into a later frame (moving sgen GC).
            public IntPtr Component;
        }

        private void UpdateAuraFarmLootCollect()
        {
            if (!this.auraFarmEnabled || !this.auraFarmLootCollectEnabled)
            {
                return;
            }

            float now = Time.unscaledTime;

            // World/scene change: netIds are world-scoped, so per-netId state from the old world is
            // garbage in the new one, and picks fired while the new world's resource sync settles
            // reference entities the server session cannot resolve (the post-update [VerifyEntity]
            // check rejects those as DataError=3). Reset and hold briefly.
            int worldEpoch = HeartopiaComplete.AuraMonoWorldEpoch;
            if (worldEpoch != this.auraLootLastWorldEpoch)
            {
                this.auraLootLastWorldEpoch = worldEpoch;
                this.ResetAuraLootCollectRuntimeState();
                this.auraLootQuietUntil = now + AuraLootWorldChangeQuietSeconds;
                if (HeartopiaComplete.MasterLogAuraFarm)
                {
                    ModLogger.Msg("[AuraFarm] Loot collect: world epoch -> " + worldEpoch + ", quiet for " + AuraLootWorldChangeQuietSeconds + "s.");
                }
            }

            if (now < this.auraLootQuietUntil)
            {
                return;
            }

            if (!this.EnsureAuraLootCollectResolver())
            {
                return;
            }

            if (!this.TryGetLocalPlayerPosition(out Vector3 playerPos) || playerPos == Vector3.zero)
            {
                return;
            }

            float maxDist = Mathf.Clamp(this.auraFarmLootCollectDistance, AuraFarmLootCollectDistanceMin, AuraFarmLootCollectDistanceMax);
            float maxDistSqr = maxDist * maxDist;
            this.auraLootCandidateBuffer.Clear();
            this.auraLootScanPins.Clear();

            try
            {
                if (!this.TryCollectAuraLootCandidatesAuraMono(playerPos, maxDistSqr, this.auraLootCandidateBuffer, this.auraLootScanPins)
                    || this.auraLootCandidateBuffer.Count == 0)
                {
                    return;
                }

                this.auraLootCandidateBuffer.Sort((a, b) => a.DistanceSqr.CompareTo(b.DistanceSqr));

                int sent = 0;
                for (int i = 0; i < this.auraLootCandidateBuffer.Count; i++)
                {
                    if (sent >= AuraFarmLootBatchPerTick)
                    {
                        break;
                    }

                    AuraLootCandidate candidate = this.auraLootCandidateBuffer[i];
                    uint netId = candidate.NetId;
                    if (netId == 0U)
                    {
                        continue;
                    }

                    if (this.auraLootLastSendAtByNetId.TryGetValue(netId, out float lastSendAt) && now - lastSendAt < AuraFarmLootResendCooldown)
                    {
                        continue;
                    }

                    // Stamp the game's own dedup field BEFORE sending — exactly what LootManager
                    // does when it enqueues (_sendCommandTime = time). The vanilla sender then stays
                    // quiet for CollectCommandReSendTime and cannot double-pick this netId; a
                    // double-pick's loser is what the server rejects as DataError(3). Fail closed:
                    // no stamp, no send.
                    if (!this.TryStampAuraLootSendCommandTime(candidate.Component))
                    {
                        continue;
                    }

                    if (!this.TrySendAuraPickLoot(netId))
                    {
                        continue;
                    }

                    this.auraLootLastSendAtByNetId[netId] = now;
                    sent++;
                }
            }
            finally
            {
                FreeAuraMonoPins(this.auraLootScanPins);
                this.auraLootScanPins.Clear();
                this.auraLootCandidateBuffer.Clear();
            }

            if (now >= this.auraLootNextCooldownCleanupAt)
            {
                this.auraLootNextCooldownCleanupAt = now + 30f;
                this.CleanupAuraLootSendCooldownMap(now);
            }
        }

        private void CleanupAuraLootSendCooldownMap(float now)
        {
            if (this.auraLootLastSendAtByNetId.Count == 0)
            {
                return;
            }

            List<uint> expired = null;
            foreach (KeyValuePair<uint, float> entry in this.auraLootLastSendAtByNetId)
            {
                if (now - entry.Value < AuraFarmLootResendCooldown * 2f)
                {
                    continue;
                }

                if (expired == null)
                {
                    expired = new List<uint>(8);
                }

                expired.Add(entry.Key);
            }

            if (expired == null)
            {
                return;
            }

            for (int i = 0; i < expired.Count; i++)
            {
                this.auraLootLastSendAtByNetId.Remove(expired[i]);
            }
        }

        // AuraMono-only scan (LootComponent is an XDTLevelAndEntity type; managed reflection over it
        // is dead code per repo policy, and post-update it also mis-parses Entity.netId — the
        // property now returns a NetId struct, so a managed `is uint` check silently drops every
        // candidate). Candidate component pointers are returned pinned via `pins`; the CALLER owns
        // the pins and must free them at the end of the same synchronous pass.
        private bool TryCollectAuraLootCandidatesAuraMono(Vector3 playerPos, float maxDistSqr, List<AuraLootCandidate> output, List<uint> pins)
        {
            if (this.auraLootComponentClass == IntPtr.Zero)
            {
                return false;
            }

            if (!this.TryAuraMonoGetComponentObjects(this.auraLootComponentClass, out List<IntPtr> components, pins) || components == null)
            {
                return false;
            }

            // The game stamps/compares _sendCommandTime on the SCALED clock (LootManager passes
            // Time.time). Use the same clock here or the dedup window is wrong under timescale.
            float gameNow = Time.time;

            for (int i = 0; i < components.Count; i++)
            {
                IntPtr comp = components[i];
                if (comp == IntPtr.Zero)
                {
                    continue;
                }

                if (this.TryGetMonoInt32Member(comp, "_state", out int state) && state == AuraLootCollectedState)
                {
                    continue;
                }

                if (this.TryGetMonoBoolMember(comp, "collected", out bool collected) && collected)
                {
                    continue;
                }

                float waiting = 0f;
                if (!this.TryGetAuraLootWaitingForCollectingTime(comp, out waiting) || waiting <= 0f)
                {
                    continue;
                }

                // The game's shared dedup stamp: LootManager writes _sendCommandTime when IT queues
                // a pick for this loot. Skip anything sent within the game's own resend window, and
                // fail closed when the stamp is unreadable — sending without the dedup is exactly
                // the double-pick that the server now rejects as DataError(3).
                if (!this.TryGetMonoSingleMember(comp, "_sendCommandTime", out float sendCommandTime))
                {
                    continue;
                }

                if (gameNow - sendCommandTime < AuraLootGameResendWindowSeconds)
                {
                    continue;
                }

                if (!this.TryGetMonoObjectMember(comp, "entity", out IntPtr entityObj) || entityObj == IntPtr.Zero)
                {
                    continue;
                }

                uint entityPin = AuraMonoPinNew(entityObj);
                try
                {
                    if (!this.TryGetMonoUInt32Member(entityObj, "netId", out uint netId) || netId == 0U)
                    {
                        continue;
                    }

                    if (!this.TryGetMonoVector3Member(entityObj, "position", out Vector3 lootPos))
                    {
                        continue;
                    }

                    float distSqr = (lootPos - playerPos).sqrMagnitude;
                    if (distSqr > maxDistSqr)
                    {
                        continue;
                    }

                    output.Add(new AuraLootCandidate { NetId = netId, DistanceSqr = distSqr, Component = comp });
                }
                finally
                {
                    AuraMonoPinFree(entityPin);
                }
            }

            return true;
        }

        private bool TryGetAuraLootWaitingForCollectingTime(IntPtr lootComponent, out float waiting)
        {
            waiting = 0f;
            if (lootComponent == IntPtr.Zero)
            {
                return false;
            }

            if (!this.TryGetMonoInt32Member(lootComponent, "_state", out int state)
                || (state != 1 && state != 2))
            {
                return false;
            }

            return this.TryGetMonoSingleMember(lootComponent, "_waitingForCollectingTime", out waiting) && waiting > 0f;
        }

        // Writes LootComponent._sendCommandTime = Time.time — the exact stamp LootManager applies
        // when it enqueues a pick, so the vanilla sender throttles itself for
        // CollectCommandReSendTime (5s) on loot we just sent. Value-type field: pass the address of
        // the value to mono_field_set_value. The field pointer is mono metadata (stable, not a heap
        // object) and is cached for the session.
        private unsafe bool TryStampAuraLootSendCommandTime(IntPtr lootComponent)
        {
            if (lootComponent == IntPtr.Zero || auraMonoObjectGetClass == null || auraMonoFieldSetValue == null)
            {
                return false;
            }

            if (this.auraLootSendCommandTimeFieldPtr == IntPtr.Zero)
            {
                IntPtr klass = auraMonoObjectGetClass(lootComponent);
                if (klass == IntPtr.Zero)
                {
                    return false;
                }

                this.auraLootSendCommandTimeFieldPtr = this.FindAuraMonoFieldOnHierarchy(klass, "_sendCommandTime");
                if (this.auraLootSendCommandTimeFieldPtr == IntPtr.Zero)
                {
                    if (HeartopiaComplete.MasterLogAuraFarm)
                    {
                        ModLogger.Msg("[AuraFarm] Loot collect: LootComponent._sendCommandTime not found — picks disabled (fail closed).");
                    }

                    return false;
                }
            }

            float stamp = Time.time;
            auraMonoFieldSetValue(lootComponent, this.auraLootSendCommandTimeFieldPtr, (IntPtr)(&stamp));
            return true;
        }

        private bool EnsureAuraLootCollectResolver()
        {
            if (this.auraLootResolverReady)
            {
                return true;
            }

            if (this.auraLootResolverFailed)
            {
                return false;
            }

            // Don't latch failure while the AuraMono API itself is still warming up — retry next tick.
            if (!this.EnsureAuraMonoApiReady())
            {
                return false;
            }

            if (this.auraLootComponentClass == IntPtr.Zero)
            {
                this.auraLootComponentClass = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.Gameplay.Component.Gather.LootComponent");
                if (this.auraLootComponentClass == IntPtr.Zero)
                {
                    this.auraLootComponentClass = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.GamePlay.Component.Gather.LootComponent");
                }
            }

            if (this.auraMonoSendPickLootMethodPtr == IntPtr.Zero
                && auraMonoClassFromName != null
                && auraMonoClassGetMethodFromName != null)
            {
                IntPtr dataImage = this.FindAuraMonoImage(new string[] { "XDTDataAndProtocol", "XDTDataAndProtocol.dll" });
                if (dataImage != IntPtr.Zero)
                {
                    IntPtr resourceClass = auraMonoClassFromName(dataImage, "XDTDataAndProtocol.ProtocolService.Resource", "ResourceProtocolManager");
                    if (resourceClass != IntPtr.Zero)
                    {
                        this.auraMonoSendPickLootMethodPtr = auraMonoClassGetMethodFromName(resourceClass, "SendPickLootCommand", 1);
                    }
                }
            }

            bool hasSendPath = this.auraMonoSendPickLootMethodPtr != IntPtr.Zero;
            bool hasScanPath = this.auraLootComponentClass != IntPtr.Zero;

            if (hasSendPath && hasScanPath)
            {
                this.auraLootResolverReady = true;
                return true;
            }

            this.auraLootResolverFailed = true;
            if (HeartopiaComplete.MasterLogAuraFarm)
            {
                ModLogger.Msg("[AuraFarm] Loot collect resolver failed. send=" + hasSendPath + " scan=" + hasScanPath);
            }

            return false;
        }

        private bool TrySendAuraPickLoot(uint lootNetId)
        {
            try
            {
                if (this.auraMonoSendPickLootMethodPtr != IntPtr.Zero && auraMonoRuntimeInvoke != null)
                {
                    return this.InvokeAuraMonoPickLoot(lootNetId);
                }
            }
            catch (Exception ex)
            {
                if (HeartopiaComplete.MasterLogAuraFarm)
                {
                    ModLogger.Msg("[AuraFarm] SendPickLootCommand failed: " + ex.Message);
                }
            }

            return false;
        }

        private unsafe bool InvokeAuraMonoPickLoot(uint lootNetId)
        {
            if (this.auraMonoSendPickLootMethodPtr == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            if (!this.AttachAuraMonoThread())
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            uint id = lootNetId;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&id);
            auraMonoRuntimeInvoke(this.auraMonoSendPickLootMethodPtr, IntPtr.Zero, (IntPtr)args, ref exc);
            return exc == IntPtr.Zero;
        }

        private void ResetAuraLootCollectRuntimeState()
        {
            this.auraLootLastSendAtByNetId.Clear();
            this.auraLootCandidateBuffer.Clear();
            this.auraLootNextCooldownCleanupAt = 0f;
            this.auraLootQuietUntil = 0f;
        }
    }
}
