using System;
using System.Collections.Generic;
using MelonLoader;
using UnityEngine;

namespace HeartopiaMod
{
    public static class InsectNetFarm
    {
        private const bool debugLoggingEnabled = HeartopiaComplete.MasterLogInsectFarm;

        private static bool enabled = false;
        private static float catchCooldown = 1.5f;
        private static float scanRange = 50f;
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
        private static int previousToolEquipType = 0;
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

            if (value && !enabled)
            {
                CapturePreviousTool(host);
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
            Log("Toggle changed: " + (enabled ? "enabled" : "disabled"));
        }

        public static void ToggleEnabled(HeartopiaComplete host = null)
        {
            SetEnabled(!enabled, host);
        }
        public static bool IsDebugLoggingEnabled() => debugLoggingEnabled;
        public static string GetLastStatus() => string.IsNullOrWhiteSpace(lastStatus) ? "Idle" : lastStatus;
        public static string GetLastToolStatus() => string.IsNullOrWhiteSpace(lastToolStatus) ? "Unknown" : lastToolStatus;
        public static int GetSessionCatchCount() => sessionCatchCount;
        public static float GetCatchCooldown() => catchCooldown;
        public static void SetCatchCooldown(float v) { catchCooldown = Mathf.Clamp(v, 0.2f, 10f); }
        public static float GetScanRange() => scanRange;
        public static void SetScanRange(float v) { scanRange = Mathf.Clamp(v, 1f, 100f); }
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

        private static bool IsTeleportTemporarilyPaused(out string reason, out float remainingSeconds)
        {
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
            MelonLogger.Msg("[InsectFarmNet] " + message);
        }

        public static float DrawSection(HeartopiaComplete host, int startY)
        {
            int num = startY;
            GUIStyle small = new GUIStyle(GUI.skin.label) { fontSize = 12 };
            GUIStyle header = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };

            GUI.Label(new Rect(20f, num, 320f, 22f), host.UI_Localize("Auto Insect Farm"), header);
            num += 28;

            if (host.UI_DrawPrimaryActionButton(new Rect(20f, num, 260f, 35f), "Auto Equip Net"))
            {
                host.StartToolEquipRequest(2);
                Log("Auto Equip Net button pressed.");
            }
            num += 42;

            bool nextEnabled = host.UI_DrawSwitchToggle(new Rect(20f, num, 280f, 25f), enabled, "Auto Insect Farm");
            if (nextEnabled != enabled)
            {
                SetEnabled(nextEnabled, host);
            }
            num += 30;

            GUI.Label(new Rect(20f, num, 360f, 20f), host.UI_LocalizeFormat("Status: {0}", lastStatus), small);
            num += 24;
            GUI.Label(new Rect(20f, num, 360f, 20f), host.UI_LocalizeFormat("Tool: {0}", lastToolStatus), small);
            num += 24;
            GUI.Label(new Rect(20f, num, 360f, 20f), host.UI_LocalizeFormat("Caught This Session: {0}", sessionCatchCount), small);
            num += 24;

            bool prevTeleportEnabled = teleportEnabled;
            teleportEnabled = host.UI_DrawSwitchToggle(new Rect(20f, num, 280f, 25f), teleportEnabled, "Teleport");
            if (teleportEnabled != prevTeleportEnabled)
            {
                Log("Teleport changed: " + teleportEnabled);
                try { host.UI_SaveKeybinds(false); } catch { }
            }
            num += 30;

            bool pauseTeleportOnTriggersEnabled = GetPauseTeleportOnTriggersEnabled();
            bool prevPauseTriggers = pauseTeleportOnTriggersEnabled;
            pauseTeleportOnTriggersEnabled = host.UI_DrawSwitchToggle(new Rect(20f, num, 280f, 25f), pauseTeleportOnTriggersEnabled, "Pause Teleport On Eat / Repair");
            if (pauseTeleportOnTriggersEnabled != prevPauseTriggers)
            {
                SetPauseTeleportOnTriggersEnabled(pauseTeleportOnTriggersEnabled);
                Log("Pause Teleport On Eat / Repair changed: " + pauseTeleportOnTriggersEnabled);
                try { host.UI_SaveKeybinds(false); } catch { }
            }
            num += 30;

            GUI.Label(new Rect(20f, num, 320f, 20f), host.UI_LocalizeFormat("Repair Teleport Pause: {0:F1}s", repairTeleportPauseSeconds), small);
            num += 22;
            float prevRepairPauseSeconds = repairTeleportPauseSeconds;
            repairTeleportPauseSeconds = host.UI_DrawAccentSlider(new Rect(20f, num, 260f, 20f), repairTeleportPauseSeconds, 0.5f, 60.2f);
            if (Math.Abs(repairTeleportPauseSeconds - prevRepairPauseSeconds) > 0.0001f)
            {
                SetRepairTeleportPauseSeconds(repairTeleportPauseSeconds);
                Log("Repair Teleport Pause changed to " + repairTeleportPauseSeconds.ToString("F1") + "s");
                try { host.UI_SaveKeybinds(false); } catch { }
            }
            num += 30;

            GUI.Label(new Rect(20f, num, 320f, 20f), host.UI_LocalizeFormat("Eat Teleport Pause: {0:F1}s", eatTeleportPauseSeconds), small);
            num += 22;
            float prevEatPauseSeconds = eatTeleportPauseSeconds;
            eatTeleportPauseSeconds = host.UI_DrawAccentSlider(new Rect(20f, num, 260f, 20f), eatTeleportPauseSeconds, 0.5f, 15.2f);
            if (Math.Abs(eatTeleportPauseSeconds - prevEatPauseSeconds) > 0.0001f)
            {
                SetEatTeleportPauseSeconds(eatTeleportPauseSeconds);
                Log("Eat Teleport Pause changed to " + eatTeleportPauseSeconds.ToString("F1") + "s");
                try { host.UI_SaveKeybinds(false); } catch { }
            }
            num += 30;

            GUI.Label(new Rect(20f, num, 320f, 20f), host.UI_LocalizeFormat("Catch Cooldown: {0:F1}s", catchCooldown), small);
            num += 22;
            float prevCooldown = catchCooldown;
            catchCooldown = host.UI_DrawAccentSlider(new Rect(20f, num, 260f, 20f), catchCooldown, 0.2f, 10f);
            if (Math.Abs(catchCooldown - prevCooldown) > 0.0001f)
            {
                SetCatchCooldown(catchCooldown);
                Log("Catch Cooldown changed to " + catchCooldown.ToString("F1") + "s");
                try { host.UI_SaveKeybinds(false); } catch { }
            }
            num += 30;

            GUI.Label(new Rect(20f, num, 320f, 20f), host.UI_LocalizeFormat("Scan Range: {0:F0}m", scanRange), small);
            num += 22;
            float prevRange = scanRange;
            scanRange = host.UI_DrawAccentSlider(new Rect(20f, num, 260f, 20f), scanRange, 1f, 100f);
            if (Math.Abs(scanRange - prevRange) > 0.0001f)
            {
                SetScanRange(scanRange);
                Log("Scan Range changed to " + scanRange.ToString("F0") + "m");
                try { host.UI_SaveKeybinds(false); } catch { }
            }
            num += 30;

            GUI.Label(new Rect(20f, num, 320f, 20f), host.UI_LocalizeFormat("Multi-Catch Limit: {0}", batchSize), small);
            num += 22;
            int prevBatch = batchSize;
            batchSize = Mathf.Clamp(Mathf.RoundToInt(host.UI_DrawAccentSlider(new Rect(20f, num, 260f, 20f), batchSize, 1f, 10f)), 1, 10);
            if (batchSize != prevBatch)
            {
                SetBatchSize(batchSize);
                Log("Batch Size changed to " + batchSize);
                try { host.UI_SaveKeybinds(false); } catch { }
            }
            num += 30;

            return num;
        }

        public static void Update(HeartopiaComplete host)
        {
            if (!enabled)
            {
                return;
            }

            if (host == null)
            {
                return;
            }

            float now = Time.unscaledTime;

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
                bool teleportPaused = IsTeleportTemporarilyPaused(out string teleportPauseReason, out float teleportPauseRemaining);
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
                            patrolIndex = (patrolIndex + 1) % patrolPositions.Length;
                            lastPatrolTeleportAt = now;
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
            lastAttemptAt = -999f;
            lastStatus = "Disabled";
            lastToolStatus = "Unknown";
            lastKnownNetEquipped = false;
            netEquipRequestActive = false;
            nextNetEquipAttemptAt = -999f;
            nextToolStatusRefreshAt = -999f;
            lastNetConfirmedAt = -999f;
            previousToolEquipType = 0;
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
                host.StartToolEquipRequest(2);
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
            previousToolEquipType = 0;
            previousToolRestorePending = false;

            if (host == null || !host.TryGetCurrentToolInfo(out int toolId, out _, out _))
            {
                return;
            }

            previousToolEquipType = MapToolIdToEquipType(toolId);
            previousToolRestorePending = previousToolEquipType != 0 && previousToolEquipType != 2;
            if (previousToolRestorePending)
            {
                Log("Captured previous tool equipType=" + previousToolEquipType);
            }
        }

        private static void RestorePreviousTool(HeartopiaComplete host)
        {
            if (host == null)
            {
                previousToolEquipType = 0;
                previousToolRestorePending = false;
                return;
            }

            if (!previousToolRestorePending || previousToolEquipType == 0)
            {
                if (host.TryGetInsectNetToolStatus(out bool netEquipped, out _) && netEquipped)
                {
                    host.StartToolEquipRequest(2);
                    Log("No previous supported tool captured; attempting to toggle net off.");
                }

                previousToolEquipType = 0;
                previousToolRestorePending = false;
                return;
            }

            host.StartToolEquipRequest(previousToolEquipType);
            Log("Restoring previous tool equipType=" + previousToolEquipType);
            previousToolEquipType = 0;
            previousToolRestorePending = false;
        }

        private static int MapToolIdToEquipType(int toolId)
        {
            switch (toolId)
            {
                case 1:
                    return 1;
                case 3:
                    return 3;
                case 4:
                    return 4;
                case 5:
                    return 2;
                default:
                    return 0;
            }
        }
    }
}
