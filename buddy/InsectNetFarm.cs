using System;
using System.Collections.Generic;
using UnityEngine;

namespace HeartopiaMod
{
    public static class InsectNetFarm
    {
        private static bool debugLoggingEnabled => HeartopiaComplete.MasterLogInsectFarm;

        // Shared by the gated Log() and the unconditional FeatureLog Tier-1 lines, so one grep
        // finds both tiers.
        private const string LogTag = "InsectFarmNet";

        private static bool enabled = false;
        private static float catchCooldown = 1.5f;
        // Scan-range bounds. The ceiling dropped 100 -> 12 m on 2026-08-23; public so the UGUI
        // slider (HeartopiaComplete.UguiInsectsContent.cs) and the config migration
        // (HeartopiaComplete.Config.cs) read the same numbers the setter clamps to.
        public const float ScanRangeMin = 1f;
        public const float ScanRangeMax = 12f;
        private static float scanRange = ScanRangeMax;
        private static int batchSize = 3;
        private static bool teleportEnabled = true;
        private static bool pauseTeleportOnRepairEnabled = false;
        private static bool pauseTeleportOnEatEnabled = false;
        
        private static float repairTeleportPauseSeconds = 18f;
        private static float eatTeleportPauseSeconds = 5f;
        private static float repairTeleportPauseUntil = -999f;
        private static float eatTeleportPauseUntil = -999f;
        private static float lastAttemptAt = -999f;
        private static string lastStatus = "Idle";
        private static string lastToolStatus = "Unknown";
        private static int sessionCatchCount = 0;
        private const float ToolStatusRefreshInterval = 0.25f;
        private const float ToolStatusRefreshIntervalWhileEquipping = 0.15f;
        private const float NetEquipRetryInterval = 3.25f;
        private const float NetEquipConfirmationGraceSeconds = 1f;
        private static bool lastKnownNetEquipped = false;
        private static bool netEquipRequestActive = false;
        private static float nextNetEquipAttemptAt = -999f;
        private static float nextToolStatusRefreshAt = -999f;
        private static float lastNetConfirmedAt = -999f;
        private static int previousToolId = 0;
        private static bool previousToolRestorePending = false;
        private static readonly Dictionary<uint, float> recentCountedNetIds = new Dictionary<uint, float>();
        private static readonly Dictionary<uint, float> recentTargetedNetIds = new Dictionary<uint, float>();
        private static readonly List<uint> expiredRecentCountedBuffer = new List<uint>(16);
        private static readonly List<uint> expiredRecentTargetedBuffer = new List<uint>(16);
        private static uint pendingTargetNetId = 0U;
        private static float pendingTargetUntil = -999f;
        private static int patrolIndex = 0;
        private static float lastPatrolTeleportAt = -999f;
        private const float PatrolTeleportCooldown = 0.5f;
        private static readonly Vector3[] patrolPositions = new Vector3[]
        {
            new Vector3(72.630f,25.590f,-2.451f),
            new Vector3(65.529f,26.348f,32.722f),
            new Vector3(69.554f,23.489f,70.941f),
            new Vector3(13.055f,25.171f,79.288f),
            new Vector3(-22.724f,23.678f,88.086f),
            new Vector3(-54.698f,21.164f,86.624f),
            new Vector3(-97.321f,20.629f,113.111f),
            new Vector3(-91.336f,25.547f,69.312f),
            new Vector3(-109.310f,19.931f,-40.587f),
            new Vector3(-90.820f,19.002f,-96.760f),
            new Vector3(-82.552f,27.026f,-153.556f),
            new Vector3(-21.354f,15.274f,-101.155f),
            new Vector3(9.246f,14.482f,-132.875f),
            new Vector3(58.208f,19.692f,-137.300f),
            new Vector3(107.229f,19.699f,-135.771f),
            new Vector3(158.384f,24.081f,-97.293f),
            new Vector3(150.486f,21.504f,-52.707f),
            new Vector3(185.881f,20.919f,-20.343f),
            new Vector3(207.724f,21.619f,62.205f),
            new Vector3(170.220f,31.484f,89.347f),
            new Vector3(113.930f,20.028f,116.534f),
            new Vector3(50.200f,22.680f,165.194f),
            new Vector3(11.113f,24.039f,229.267f),
            new Vector3(-19.243f,28.761f,208.386f),
            new Vector3(-52.120f,31.123f,212.418f),
            new Vector3(-81.265f,31.086f,186.662f),
            new Vector3(-129.224f,35.219f,219.063f),
            new Vector3(-146.526f,27.472f,181.798f),
            new Vector3(-185.296f,23.685f,104.815f),
            new Vector3(-238.499f,35.778f,18.708f),
            new Vector3(-194.233f,19.750f,-12.592f),
            new Vector3(-184.032f,25.326f,-87.924f),
            new Vector3(-136.144f,17.994f,-136.361f),
            new Vector3(-3.093f,23.280f,156.825f),
            new Vector3(292.163f,14.054f,100.373f),
            new Vector3(265.060f,12.427f,104.026f),
            new Vector3(188.788f,18.256f,-42.686f),
            new Vector3(153.897f,20.972f,-46.769f),
            new Vector3(174.849f,25.791f,-92.613f),
            new Vector3(150.717f,23.348f,-119.486f),
            new Vector3(74.287f,21.253f,-122.799f),
            new Vector3(27.281f,13.799f,-134.880f),
            new Vector3(-29.587f,12.929f,-117.912f),
            new Vector3(-79.884f,17.140f,-137.786f),
            new Vector3(-93.886f,18.175f,-177.683f),
            new Vector3(-146.344f,19.229f,-115.327f),
            new Vector3(-173.341f,23.177f,-93.806f),
            new Vector3(-189.293f,22.464f,-63.419f),
            new Vector3(-185.638f,20.890f,-26.130f),
            new Vector3(-206.994f,28.042f,34.327f),
            new Vector3(-200.905f,25.543f,63.187f),
            new Vector3(-130.037f,27.486f,103.761f),
            new Vector3(-137.555f,23.012f,164.556f),
            new Vector3(-132.235f,25.377f,199.333f),
            new Vector3(-38.262f,13.008f,237.845f),
            new Vector3(41.946f,23.455f,231.585f)
        };

        public static bool IsEnabled => enabled;
        public static void SetEnabled(bool value, HeartopiaComplete host = null)
        {
            if (enabled == value)
            {
                return;
            }

            // Read before the disable branch clears them: the end-of-run totals are the point.
            int endCatches = sessionCatchCount;
            int endConfirmed = sessionConfirmedCount;

            if (value && !enabled)
            {
                CapturePreviousTool(host);
            }

            if (!value)
            {
                suspended = false; // a stopped farm must never stay paused — see ForceStop
            }

            enabled = value;
            lastAttemptAt = -999f;
            lastStatus = enabled ? "Enabled" : "Disabled";
            lastToolStatus = "Unknown";
            lastKnownNetEquipped = false;
            netEquipRequestActive = false;
            nextNetEquipAttemptAt = -999f;
            nextToolStatusRefreshAt = -999f;
            lastNetConfirmedAt = -999f;
            if (!enabled)
            {
                RestorePreviousTool(host);
                ResetAckStats();
                sessionCatchCount = 0;
                recentCountedNetIds.Clear();
                recentTargetedNetIds.Clear();
                pendingTargetNetId = 0U;
                pendingTargetUntil = -999f;
                patrolIndex = 0;
                lastPatrolTeleportAt = -999f;
                repairTeleportPauseUntil = -999f;
                eatTeleportPauseUntil = -999f;
            }
            // TIER 1 — unconditional. MasterLogInsectFarm ships OFF, so before the split the only
            // evidence a run had ever happened was the NetCaughtInsectEvent hook install, and that
            // fires once per session at most.
            if (enabled)
            {
                FeatureLog.Toggle(LogTag, true,
                    $"cooldown={catchCooldown:F1}s range={scanRange:F0}m batch={batchSize} teleport={teleportEnabled}");
            }
            else
            {
                FeatureLog.Toggle(LogTag, false,
                    $"session totals: sent={endCatches} server-confirmed={endConfirmed}");
            }

            Log("Toggle changed: " + (enabled ? "enabled" : "disabled"));
        }

        public static void ToggleEnabled(HeartopiaComplete host = null)
        {
            SetEnabled(!enabled, host);
        }

        // ── Combined Farming: suspend/resume (CombinedFarmFeature) ───────────────────────────────
        // A PAUSE, not a stop — see the same block in AutoFishingFarm. This farm has no long-running
        // session, so any tick boundary is a safe suspend point; the only per-tick state that must go
        // is the target lock (a netId reserved for a catch that will now not happen).
        // Resume replays SetEnabled's tool-state reset (minus the counters and the tool capture) so
        // the net is requested immediately and the 1 s equip-confirmation grace restarts honestly
        // instead of trusting a "Net Equipped" reading from before another tool was held.
        private static bool suspended;
        public static bool IsSuspended => suspended;
        public static void SetSuspended(bool value)
        {
            if (suspended == value)
            {
                return;
            }

            suspended = value;
            pendingTargetNetId = 0U;
            pendingTargetUntil = -999f;

            if (value)
            {
                lastStatus = "Paused (combined farming)";
                Log("Suspended by the combined-farm coordinator.");
                return;
            }

            lastAttemptAt = -999f;
            lastToolStatus = "Unknown";
            lastKnownNetEquipped = false;
            netEquipRequestActive = false;
            nextNetEquipAttemptAt = -999f;
            nextToolStatusRefreshAt = -999f;
            lastNetConfirmedAt = -999f;
            recentTargetedNetIds.Clear();
            lastStatus = "Resumed";
            Log("Resumed by the combined-farm coordinator.");
        }
        public static bool IsDebugLoggingEnabled() => debugLoggingEnabled;
        public static string GetLastStatus() => string.IsNullOrWhiteSpace(lastStatus) ? "Idle" : lastStatus;
        public static string GetLastToolStatus() => string.IsNullOrWhiteSpace(lastToolStatus) ? "Unknown" : lastToolStatus;
        public static int GetSessionCatchCount() => sessionCatchCount;
        public static float GetCatchCooldown() => catchCooldown;
        public static void SetCatchCooldown(float v) { catchCooldown = Mathf.Clamp(v, 0.2f, 10f); }
        public static float GetScanRange() => scanRange;
        public static void SetScanRange(float v) { scanRange = Mathf.Clamp(v, ScanRangeMin, ScanRangeMax); }
        public static int GetBatchSize() => batchSize;
        public static void SetBatchSize(int v) { batchSize = Mathf.Clamp(v, 1, 10); }
        public static bool GetTeleportEnabled() => teleportEnabled;
        public static void SetTeleportEnabled(bool v) { teleportEnabled = v; }
        public static bool GetPauseTeleportOnTriggersEnabled() => pauseTeleportOnRepairEnabled || pauseTeleportOnEatEnabled;
        public static void SetPauseTeleportOnTriggersEnabled(bool v)
        {
            pauseTeleportOnRepairEnabled = v;
            pauseTeleportOnEatEnabled = v;
        }
        public static bool GetPauseTeleportOnRepairEnabled() => pauseTeleportOnRepairEnabled;
        public static void SetPauseTeleportOnRepairEnabled(bool v) { pauseTeleportOnRepairEnabled = v; }
        public static bool GetPauseTeleportOnEatEnabled() => pauseTeleportOnEatEnabled;
        public static void SetPauseTeleportOnEatEnabled(bool v) { pauseTeleportOnEatEnabled = v; }
        public static float GetRepairTeleportPauseSeconds() => repairTeleportPauseSeconds;
        public static void SetRepairTeleportPauseSeconds(float v) { repairTeleportPauseSeconds = Mathf.Clamp(v, 0.5f, 60.2f); }
        public static float GetEatTeleportPauseSeconds() => eatTeleportPauseSeconds;
        public static void SetEatTeleportPauseSeconds(float v) { eatTeleportPauseSeconds = Mathf.Clamp(v, 0.5f, 15f); }

        public static void NotifyRepairTriggered()
        {
            if (!pauseTeleportOnRepairEnabled)
            {
                return;
            }

            repairTeleportPauseUntil = Time.unscaledTime + repairTeleportPauseSeconds;
            Log($"Repair-triggered teleport pause armed for {repairTeleportPauseSeconds:F1}s.");
        }

        public static void NotifyAutoEatTriggered()
        {
            if (!pauseTeleportOnEatEnabled)
            {
                return;
            }

            eatTeleportPauseUntil = Time.unscaledTime + eatTeleportPauseSeconds;
            Log($"Auto-eat-triggered teleport pause armed for {eatTeleportPauseSeconds:F1}s.");
        }

        // `host` is only consulted for the live repair state; the two legacy timers below stay as the
        // user configured them.
        private static bool IsTeleportTemporarilyPaused(HeartopiaComplete host, out string reason, out float remainingSeconds)
        {
            // The restore aura is a circle on the GROUND: teleporting mid-repair walks the player out
            // of it and the kit is wasted. The legacy pause is a fixed 18 s timer armed at trigger
            // time and only when the user ticked its option — this is the real signal, always on and
            // exact at both ends. (Same rule the fishing route already follows for its hops.)
            if (host != null)
            {
                bool repairBusy = false;
                try { repairBusy = host.IsAutoRepairBusy(); } catch { }
                if (repairBusy)
                {
                    reason = "Repair aura";
                    remainingSeconds = 0f;
                    return true;
                }
            }

            float now = Time.unscaledTime;
            float repairRemaining = repairTeleportPauseUntil - now;
            float eatRemaining = eatTeleportPauseUntil - now;

            if (repairRemaining > 0f && repairRemaining >= eatRemaining)
            {
                reason = "Repair";
                remainingSeconds = repairRemaining;
                return true;
            }

            if (eatRemaining > 0f)
            {
                reason = "Eat";
                remainingSeconds = eatRemaining;
                return true;
            }

            reason = string.Empty;
            remainingSeconds = 0f;
            return false;
        }

        private static void Log(string message)
        {
            if (!debugLoggingEnabled)
            {
                return;
            }
            ModLogger.Msg("[InsectFarmNet] " + message);
        }

        // ── Server ACK: NetCaughtInsectEvent ─────────────────────────────────────────────────────
        // The catch is fire-and-forget — CatchingInsectCommand goes out with a list of netIds and the
        // client learns nothing. The server does answer, though: CatchingInsectResult carries a
        // per-insect `CatchingResult` bool, and InsectProtocolManager dispatches
        // NetCaughtInsectEvent (keyed by the INSECT netId) for each success. A rejected insect
        // produces no event at all, which is exactly why an out-of-range catch looked like nothing
        // happening while sessionCatchCount kept climbing.
        //
        // So: remember where each sent insect was, then watch which netIds come back. That yields
        //   * an honest confirmed-catch count next to the optimistic sent-count, and
        //   * the empirical accept radius — the largest distance ever confirmed vs the smallest
        //     distance ever rejected, which brackets the server's real limit.
        //
        // Not covered: bubble/combo catches, which the game reports through InsectBubbleBouncingEvent
        // instead. Those show up here as "rejected" and are excluded from the radius bracket.
        private const string NetCaughtInsectEventName = "XDTDataAndProtocol.Events.NetCaughtInsectEvent";
        // {uint playerNetId@0; uint rewardNetId@4; bool IsFirstCatching@8; ShowOffReason reason@12;
        //  int quality@16; bool isQualityUp@20; bool isSelected@21} = 24B aligned. Only the dispatch
        // key (the insect netId) is actually read.
        private const int NetCaughtInsectEventBytes = 24;
        private const float SendAckTimeoutSeconds = 6f;

        private struct PendingSend
        {
            public float Distance;
            public float SentAt;
        }

        private static bool insectAckHookRegistered;
        private static int sessionSentCount;
        private static int sessionConfirmedCount;
        private static int sessionUnconfirmedCount;
        private static float maxConfirmedDistance = -1f;
        private static float minUnconfirmedDistance = -1f;
        private static readonly Dictionary<uint, PendingSend> pendingSends = new Dictionary<uint, PendingSend>();
        private static readonly List<uint> expiredPendingSendBuffer = new List<uint>(16);
        // Every netId the server has ever ACK'd this session. The farm re-sends a batch every tick,
        // and an insect that was already caught is still in the scan list until it despawns — so
        // without this the SAME insect is counted as "sent" again and then times out "unconfirmed",
        // which is what put a 0.6 m entry in the rejection bracket. A resend of a caught insect is
        // not a rejection, it is our own duplicate.
        private static readonly HashSet<uint> confirmedNetIds = new HashSet<uint>();
        private static bool ackStatsDirty;

        public static int GetSessionConfirmedCatchCount() => sessionConfirmedCount;

        private static bool AckLoggingEnabled =>
            HeartopiaComplete.MasterLogInsectFarm || HeartopiaComplete.MasterLogCombinedFarm;

        private static void AckLog(string message)
        {
            if (!AckLoggingEnabled)
            {
                return;
            }

            ModLogger.Msg("[InsectFarmAck] " + message);
        }

        // Registration is cheap and idempotent: it only records the request — the detour itself is
        // installed by the world-ready gate (HeartopiaComplete.EventHook.cs), never from here.
        private static void EnsureInsectAckHook(HeartopiaComplete host)
        {
            if (insectAckHookRegistered || host == null)
            {
                return;
            }

            insectAckHookRegistered = true;
            host.RegisterGameEventHookByNetId(NetCaughtInsectEventName, NetCaughtInsectEventBytes, OnNetCaughtInsect);
        }

        // Main-thread drain — safe to allocate/log here.
        private static void OnNetCaughtInsect(HeartopiaComplete.GameEventSnapshot e)
        {
            sessionConfirmedCount++;
            ackStatsDirty = true;
            // TIER 1, once per session — the farm is not just armed, the server is accepting.
            // Rejections are silent on this channel, so a missing line here is itself the signal.
            FeatureLog.Once(LogTag, "first-ack",
                "first server-confirmed catch this session (netId=" + e.NetId + ") — the farm is producing");

            uint insectNetId = e.NetId;
            if (insectNetId != 0U)
            {
                confirmedNetIds.Add(insectNetId);
            }

            if (insectNetId == 0U || !pendingSends.TryGetValue(insectNetId, out PendingSend pending))
            {
                // Confirmed, but we never recorded the send (e.g. caught before this session's
                // bookkeeping started). Counts toward the total, not toward the radius bracket.
                return;
            }

            pendingSends.Remove(insectNetId);
            if (pending.Distance > maxConfirmedDistance)
            {
                maxConfirmedDistance = pending.Distance;
            }
        }

        private static void RecordSentInsects(HeartopiaComplete host, float now)
        {
            List<uint> sentIds = host.GetLastInsectFarmSentNetIds();
            IReadOnlyList<Vector3> sentPositions = host.GetLastInsectFarmSentPositionsView();
            if (sentIds == null || sentIds.Count == 0)
            {
                return;
            }

            GameObject player = host.GetPlayerObject();
            Vector3 playerPos = player != null
                ? player.transform.position
                : (Camera.main != null ? Camera.main.transform.position : Vector3.zero);

            for (int i = 0; i < sentIds.Count; i++)
            {
                uint netId = sentIds[i];
                if (netId == 0U)
                {
                    continue;
                }

                // Count each insect ONCE. A netId already confirmed is a duplicate send of a caught
                // insect (the server drops it, correctly); one already in flight must not restart its
                // own ACK timer or it could never time out.
                if (confirmedNetIds.Contains(netId) || pendingSends.ContainsKey(netId))
                {
                    continue;
                }

                float distance = -1f;
                if (sentPositions != null && i < sentPositions.Count)
                {
                    Vector3 insectPos = sentPositions[i];
                    distance = new Vector2(insectPos.x - playerPos.x, insectPos.z - playerPos.z).magnitude;
                }

                sessionSentCount++;
                pendingSends[netId] = new PendingSend { Distance = distance, SentAt = now };
            }

            ackStatsDirty = true;
        }

        // Anything still pending past the timeout was rejected: the server answered
        // CatchingResult=false (or dropped it), and no event will ever arrive.
        private static void SweepPendingSends(float now)
        {
            if (pendingSends.Count == 0)
            {
                return;
            }

            expiredPendingSendBuffer.Clear();
            foreach (KeyValuePair<uint, PendingSend> pair in pendingSends)
            {
                if (now - pair.Value.SentAt < SendAckTimeoutSeconds)
                {
                    continue;
                }

                expiredPendingSendBuffer.Add(pair.Key);
            }

            for (int i = 0; i < expiredPendingSendBuffer.Count; i++)
            {
                uint netId = expiredPendingSendBuffer[i];
                if (!pendingSends.TryGetValue(netId, out PendingSend pending))
                {
                    continue;
                }

                pendingSends.Remove(netId);
                sessionUnconfirmedCount++;
                if (pending.Distance >= 0f && (minUnconfirmedDistance < 0f || pending.Distance < minUnconfirmedDistance))
                {
                    minUnconfirmedDistance = pending.Distance;
                }

                ackStatsDirty = true;
            }
        }

        private static void LogAckStatsIfDirty()
        {
            if (!ackStatsDirty || !AckLoggingEnabled)
            {
                return;
            }

            ackStatsDirty = false;
            AckLog("confirmed " + sessionConfirmedCount + "/" + sessionSentCount + " sent"
                + " (" + sessionUnconfirmedCount + " unconfirmed, " + pendingSends.Count + " in flight)"
                + " | max confirmed " + (maxConfirmedDistance >= 0f ? maxConfirmedDistance.ToString("F1") + "m" : "n/a")
                + ", min unconfirmed " + (minUnconfirmedDistance >= 0f ? minUnconfirmedDistance.ToString("F1") + "m" : "n/a"));
        }

        private static void ResetAckStats()
        {
            sessionSentCount = 0;
            sessionConfirmedCount = 0;
            sessionUnconfirmedCount = 0;
            maxConfirmedDistance = -1f;
            minUnconfirmedDistance = -1f;
            pendingSends.Clear();
            confirmedNetIds.Clear();
            ackStatsDirty = false;
        }


        public static void Update(HeartopiaComplete host)
        {
            if (!enabled || suspended)
            {
                return;
            }

            if (host == null)
            {
                return;
            }

            float now = Time.unscaledTime;

            EnsureInsectAckHook(host);
            SweepPendingSends(now);
            LogAckStatsIfDirty();

            RefreshToolState(host, now);

            bool recentlyConfirmedNet = (now - lastNetConfirmedAt) <= NetEquipConfirmationGraceSeconds;
            if (!recentlyConfirmedNet)
            {
                if (!string.Equals(lastToolStatus, "Net Equipped", StringComparison.Ordinal))
                {
                    EnsureNetEquipped(host, now);
                    return;
                }

                lastStatus = "Checking tool...";
                return;
            }

            if (now - lastAttemptAt < catchCooldown)
            {
                return;
            }

            try
            {
                int detectedCount;
                int resolvedCount;
                int sentCount;
                string status;

                CleanupRecentCountWindow(now);
                CleanupRecentTargetWindow(now);

                bool result = false;
                detectedCount = 0;
                resolvedCount = 0;
                sentCount = 0;
                status = "Idle";

                bool hasPendingTarget = pendingTargetNetId != 0U && now < pendingTargetUntil;
                bool teleportPaused = IsTeleportTemporarilyPaused(host, out string teleportPauseReason, out float teleportPauseRemaining);
                if (teleportEnabled && !hasPendingTarget && !teleportPaused)
                {
                    int scannedCount;
                    List<uint> scannedIds;
                    List<Vector3> scannedPositions;
                    string scanStatus;
                    if (host.TryGetLoadedInsectTargets(out scannedCount, out scannedIds, out scannedPositions, out scanStatus)
                        && scannedIds != null
                        && scannedPositions != null
                        && scannedIds.Count > 0
                        && scannedPositions.Count > 0)
                    {
                        GameObject player = host.GetPlayerObject();
                        Vector3 playerPos = player != null ? player.transform.position : (Camera.main != null ? Camera.main.transform.position : Vector3.zero);
                        int chosenIndex = -1;
                        float nearestDistance = float.MaxValue;

                        for (int i = 0; i < scannedPositions.Count && i < scannedIds.Count; i++)
                        {
                            float distance = Vector3.Distance(playerPos, scannedPositions[i]);
                            uint targetId = scannedIds[i];
                            float until;
                            bool recentlyTargeted = targetId != 0U
                                && recentTargetedNetIds.TryGetValue(targetId, out until)
                                && now < until;
                            if (recentlyTargeted)
                            {
                                continue;
                            }

                            if (distance < nearestDistance)
                            {
                                nearestDistance = distance;
                                chosenIndex = i;
                            }
                        }

                        if (chosenIndex >= 0)
                        {
                            uint chosenId = scannedIds[chosenIndex];
                            Vector3 chosenPos = scannedPositions[chosenIndex];
                            const float teleportThreshold = 2.5f;
                            pendingTargetNetId = chosenId;
                            pendingTargetUntil = now + 5f;

                            if (nearestDistance > teleportThreshold)
                            {
                                // Combined farming bounds where a slice may take the player (see
                                // CombinedFarmFeature.AllowsMoveTo). Standalone this is always true.
                                if (!CombinedFarmFeature.AllowsMoveTo(chosenPos, out string moveBlockReason))
                                {
                                    pendingTargetNetId = 0U;
                                    pendingTargetUntil = -999f;
                                    // Park the refused target the same way a taken one is parked, so
                                    // the next scan offers a DIFFERENT insect instead of re-picking
                                    // the nearest unreachable one every tick.
                                    if (chosenId != 0U)
                                    {
                                        recentTargetedNetIds[chosenId] = now + 4f;
                                    }
                                    lastAttemptAt = now;
                                    lastStatus = "Hop blocked: " + moveBlockReason;
                                    Log($"Insect hop to netId={chosenId} refused: {moveBlockReason}");
                                    return;
                                }

                                host.TeleportDirectToLocation(chosenPos);
                                if (chosenId != 0U)
                                {
                                    recentTargetedNetIds[chosenId] = now + 4f;
                                }
                                lastAttemptAt = now - catchCooldown;
                                lastStatus = $"Teleported to insect ({chosenIndex + 1}/{scannedCount})";
                                Log($"Loaded insect scan selected netId={chosenId} index={chosenIndex + 1}/{scannedCount} distance={nearestDistance:F2}; teleported directly to target.");
                                return;
                            }

                            Log($"Loaded insect scan selected netId={chosenId} index={chosenIndex + 1}/{scannedCount} distance={nearestDistance:F2}; already near target, attempting catch.");
                            hasPendingTarget = true;
                        }
                        else
                        {
                            recentTargetedNetIds.Clear();
                            Log("Loaded insect scan found only recently targeted insects; target memory reset.");
                        }
                    }
                    else
                    {
                        Log("Loaded insect scan found no usable insect target: " + scanStatus);
                        if (patrolPositions.Length > 0 && now - lastPatrolTeleportAt >= PatrolTeleportCooldown)
                        {
                            if (patrolIndex < 0 || patrolIndex >= patrolPositions.Length)
                            {
                                patrolIndex = 0;
                            }

                            Vector3 patrolPos = patrolPositions[patrolIndex];
                            int patrolLabel = patrolIndex + 1;
                            // The patrol is the move that most obviously abandons a fishing spot: its
                            // points are fixed world coordinates, all far away. Refusing one must
                            // still ADVANCE the index and arm the cooldown, or the farm would retest
                            // the same unreachable point every 0.5 s forever.
                            patrolIndex = (patrolIndex + 1) % patrolPositions.Length;
                            lastPatrolTeleportAt = now;
                            if (!CombinedFarmFeature.AllowsMoveTo(patrolPos, out string patrolBlockReason))
                            {
                                lastAttemptAt = now;
                                lastStatus = "Patrol paused: " + patrolBlockReason;
                                Log($"Patrol to {patrolLabel}/{patrolPositions.Length} refused: {patrolBlockReason}");
                                return;
                            }

                            host.TeleportDirectToLocation(patrolPos);
                            lastAttemptAt = now - catchCooldown;
                            lastStatus = $"Patrolling insect area ({patrolLabel}/{patrolPositions.Length})";
                            Log($"No loaded insects found; patrolling to location {patrolLabel}/{patrolPositions.Length} pos={patrolPos}");
                            return;
                        }
                    }
                }
                else if (teleportEnabled && teleportPaused)
                {
                    lastStatus = $"Teleport paused by {teleportPauseReason} ({teleportPauseRemaining:F1}s)";
                    Log($"Teleport paused by {teleportPauseReason}; remaining={teleportPauseRemaining:F1}s");
                }

                Log(hasPendingTarget
                    ? $"Tick start: attempting catch on pending target netId={pendingTargetNetId} range={scanRange:F0} batch={batchSize} cooldown={catchCooldown:F1}"
                    : $"Tick start: range={scanRange:F0} batch={batchSize} cooldown={catchCooldown:F1}");
                result = host.TryNetCatchNearbyInsects(scanRange, batchSize, out detectedCount, out resolvedCount, out sentCount, out status);
                lastAttemptAt = now;
                lastStatus = status;
                if (result && sentCount > 0)
                {
                    RecordSentInsects(host, now);
                    foreach (uint netId in host.GetLastInsectFarmSentNetIds())
                    {
                        if (netId == 0U)
                        {
                            continue;
                        }

                        float until;
                        if (recentCountedNetIds.TryGetValue(netId, out until) && now < until)
                        {
                            continue;
                        }

                        recentCountedNetIds[netId] = now + 3f;
                        recentTargetedNetIds.Remove(netId);
                        sessionCatchCount++;
                    }

                    pendingTargetNetId = 0U;
                    pendingTargetUntil = -999f;
                }
                Log($"Tick result: success={result} detected={detectedCount} resolved={resolvedCount} sent={sentCount} status={status}");
                if (result || !teleportEnabled)
                {
                    return;
                }

                if (hasPendingTarget)
                {
                    Log($"Pending target netId={pendingTargetNetId} was not caught this tick; releasing target lock.");
                    pendingTargetNetId = 0U;
                    pendingTargetUntil = -999f;
                }
            }
            catch (Exception ex)
            {
                lastAttemptAt = now;
                lastStatus = "Error: " + ex.Message;
                Log("Update error: " + ex);
            }
        }

        public static void ForceStop()
        {
            enabled = false;
            // A stopped farm must never stay paused: if the coordinator were wedged (its circuit
            // breaker tripped, say) a leftover suspend flag would silently kill this farm the next
            // time the player switched it on.
            suspended = false;
            ResetAckStats();
            lastAttemptAt = -999f;
            lastStatus = "Disabled";
            lastToolStatus = "Unknown";
            lastKnownNetEquipped = false;
            netEquipRequestActive = false;
            nextNetEquipAttemptAt = -999f;
            nextToolStatusRefreshAt = -999f;
            lastNetConfirmedAt = -999f;
            previousToolId = 0;
            previousToolRestorePending = false;
            sessionCatchCount = 0;
            recentCountedNetIds.Clear();
            recentTargetedNetIds.Clear();
            pendingTargetNetId = 0U;
            pendingTargetUntil = -999f;
            patrolIndex = 0;
            lastPatrolTeleportAt = -999f;
            repairTeleportPauseUntil = -999f;
            eatTeleportPauseUntil = -999f;
        }

        private static void CleanupRecentCountWindow(float now)
        {
            expiredRecentCountedBuffer.Clear();
            foreach (KeyValuePair<uint, float> pair in recentCountedNetIds)
            {
                if (now < pair.Value)
                {
                    continue;
                }

                expiredRecentCountedBuffer.Add(pair.Key);
            }

            if (expiredRecentCountedBuffer.Count == 0)
            {
                return;
            }

            for (int i = 0; i < expiredRecentCountedBuffer.Count; i++)
            {
                recentCountedNetIds.Remove(expiredRecentCountedBuffer[i]);
            }
        }

        private static void CleanupRecentTargetWindow(float now)
        {
            expiredRecentTargetedBuffer.Clear();
            foreach (KeyValuePair<uint, float> pair in recentTargetedNetIds)
            {
                if (now < pair.Value)
                {
                    continue;
                }

                expiredRecentTargetedBuffer.Add(pair.Key);
            }

            if (expiredRecentTargetedBuffer.Count == 0)
            {
                return;
            }

            for (int i = 0; i < expiredRecentTargetedBuffer.Count; i++)
            {
                recentTargetedNetIds.Remove(expiredRecentTargetedBuffer[i]);
            }
        }

        private static void RefreshToolState(HeartopiaComplete host, float now)
        {
            if (host == null || now < nextToolStatusRefreshAt)
            {
                return;
            }

            bool gotToolStatus = host.TryGetInsectNetToolStatus(out bool netEquipped, out string toolStatus);
            string nextStatus = string.IsNullOrWhiteSpace(toolStatus) ? "Unknown" : toolStatus;
            lastToolStatus = gotToolStatus && netEquipped ? "Net Equipped" : nextStatus;

            if (gotToolStatus)
            {
                lastKnownNetEquipped = netEquipped;
                if (netEquipped)
                {
                    lastNetConfirmedAt = now;
                    if (netEquipRequestActive)
                    {
                        lastStatus = "Net equip confirmed.";
                        Log("Net equip confirmed.");
                    }

                    netEquipRequestActive = false;
                    nextNetEquipAttemptAt = -999f;
                }
                else
                {
                    lastKnownNetEquipped = false;
                }
            }

            nextToolStatusRefreshAt = now + (netEquipRequestActive ? ToolStatusRefreshIntervalWhileEquipping : ToolStatusRefreshInterval);
        }

        private static void EnsureNetEquipped(HeartopiaComplete host, float now)
        {
            if (host == null)
            {
                return;
            }

            netEquipRequestActive = true;

            if (now >= nextNetEquipAttemptAt)
            {
                host.EquipHandTool(5);
                nextNetEquipAttemptAt = now + NetEquipRetryInterval;
                nextToolStatusRefreshAt = now + ToolStatusRefreshIntervalWhileEquipping;
                lastStatus = "Equipping net...";
                Log("Net missing; sent equip request.");
                return;
            }

            lastStatus = "Waiting for net equip...";
        }

        private static void CapturePreviousTool(HeartopiaComplete host)
        {
            previousToolId = 0;
            previousToolRestorePending = false;

            // See AutoFishingFarm.CapturePreviousTool: the coordinator is the single writer of the
            // handhold while it is active, and it owns the capture/restore pair.
            if (FarmToolBroker.IsActive)
            {
                return;
            }

            if (host == null || !host.TryGetCurrentToolInfo(out int toolId, out _, out _))
            {
                return;
            }

            previousToolId = toolId;
            previousToolRestorePending = toolId != 0 && toolId != 5;
            if (previousToolRestorePending)
            {
                Log("Captured previous toolId=" + previousToolId);
            }
        }

        private static void RestorePreviousTool(HeartopiaComplete host)
        {
            if (host == null || FarmToolBroker.IsActive)
            {
                previousToolId = 0;
                previousToolRestorePending = false;
                return;
            }

            if (!previousToolRestorePending || previousToolId == 0)
            {
                if (host.TryGetInsectNetToolStatus(out bool netEquipped, out _) && netEquipped)
                {
                    host.EquipHandTool(0);
                    Log("No previous supported tool captured; unequipping net.");
                }

                previousToolId = 0;
                previousToolRestorePending = false;
                return;
            }

            host.EquipHandTool(previousToolId);
            Log("Restoring previous toolId=" + previousToolId);
            previousToolId = 0;
            previousToolRestorePending = false;
        }
    }
}
