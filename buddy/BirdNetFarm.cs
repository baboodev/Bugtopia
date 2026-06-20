using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace HeartopiaMod
{
    public static class BirdNetFarm
    {
        private const bool debugLoggingEnabled = HeartopiaComplete.MasterLogBirdFarm;
        private const bool verboseCrashTraceEnabled = HeartopiaComplete.MasterLogBirdFarmCrashTrace;

        private static bool enabled = false;
        private static bool perfectPhotoEnabled = false;
        private static int captureMode = 0;
        private static bool captureModeDropdownOpen = false;
        private static float catchCooldown = 1.5f;
        private static float scanRange = 35f;
        private static int multiCatchLimit = 1;
        private static float lastAttemptAt = -999f;
        private static float pendingSaveAt = -999f;

        private static float enableWarmupUntil = -999f;
        private static float scannerReadyAt = -999f;
        private static float sessionStartedAt = -999f;
        private static string lastStatus = "Idle";
        private static string lastToolStatus = "Unknown";
        private static float lastKnownScannerToolStatusAt = -999f;
        private const float ScannerEquipRetryInterval = 3.25f;
        private static bool scannerEquipRequestActive = false;
        private static float nextScannerEquipAttemptAt = -999f;
        private static int previousToolId = 0;
        private static bool previousToolRestorePending = false;
        private static int sessionCatchCount = 0;
        private static int sessionScaredCount = 0;
        private static int consecutiveNoTargetTicks = 0;
        private static int consecutiveServerRejectTicks = 0;  // count of successive server-side rejections
        private static float nextRetryAt = -999f;
        private static bool lastScannerEquipped = false;
        private static float safetyStopBlockUntil = -999f; // block re-enable for 60s after safety stop
        private static readonly HashSet<uint> sessionCountedNetIds = new HashSet<uint>();
        // ── Multi-pending confirmation ─────────────────────────────────────────────────────────────────
        // With multi-catch we send up to 10 birds per tick. We track ALL their netIds so that
        // any server ACK for any of them increments the session counter correctly.
        private static readonly HashSet<uint> _pendingConfirmNetIds = new HashSet<uint>();
        private static readonly List<uint> _tickSentNetIdsReuse = new List<uint>(16); // Reusable list to prevent GC
        private static readonly HashSet<uint> _tickSentNetIdsSetReuse = new HashSet<uint>(); // Dedupe duplicate sends within a burst
        private static readonly Dictionary<uint, int> _pendingTimeoutStrikes = new Dictionary<uint, int>();
        private static float _pendingConfirmExpiresAt = -999f;   // when the current batch window closes
        private const float PendingConfirmDelay = 0.5f;    // wait 500ms before draining server ACKs
        private const float PendingConfirmTimeout = 8.0f;  // generous window; server typically ACKs within 1-3s
        private const int PendingConfirmHighWatermark = 9;
        private const float PendingConfirmPressureDelay = 1.5f;
        private const int PendingTimeoutStrikesBeforeBlacklist = 2;
        private const float PendingTimeoutBlacklistSeconds = 12f;
        private const float SafetyStopSeconds = 90f;         // auto-stop after 90 seconds
        private const float SafetyStopCooldownSeconds = 60f; // block re-enable for 60s after stop
        private const float RuntimeRecycleSeconds = 180f;    // clear native-derived state every 3 minutes
        private const float RuntimeRecyclePauseSeconds = 3f;
        private const float StationaryRadiusMeters = 3f;
        private const float StationaryThrottleAfterSeconds = 45f;
        private const int StationaryMultiCatchLimit = 3;
        private const int MaxMultiCatchLimit = 10;
        private const float MultiCatchBurstSpacingSeconds = 0.45f;
        private const float UnresolvedBirdBackoffSeconds = 2.5f;
        private const double SlowTickWarnMilliseconds = 120.0;
        private const float SlowTickLogCooldownSeconds = 10f;
        private static float nextCrashHeartbeatAt = -999f;
        private static float nextSlowTickLogAt = -999f;
        private static string crashTracePath = null;
        private static Vector3 lastMovementSamplePos = Vector3.zero;
        private static float stationarySinceAt = -999f;
        private static bool stationaryThrottleActive = false;
        private static int multiCatchBurstRemaining = 0;
        private static int multiCatchBurstTarget = 0;
        private static float nextRuntimeRecycleAt = -999f;
        private static readonly string[] CaptureModeOptions = { "Safe Capture", "Spam Capture" };

        public static bool IsEnabled => enabled;
        public static bool IsAutoScareMaxPhotoEnabled => enabled;
        private static bool IsSpamMaxPhotoCaptureMode => captureMode == 1;
        public static void SetEnabled(bool value, HeartopiaComplete host = null)
        {
            if (enabled == value)
            {
                return;
            }

            Breadcrumbs.Drop("bf.setenabled.begin", value.ToString());
            if (value && !enabled)
            {
                Breadcrumbs.Drop("bf.capturetool.begin");
                CapturePreviousTool(host);
                Breadcrumbs.Drop("bf.capturetool.ok");
            }

            enabled = value;
            lastAttemptAt = -999f;
            enableWarmupUntil = enabled ? Time.unscaledTime + 0.75f : -999f;
            scannerReadyAt = -999f;
            sessionStartedAt = enabled ? Time.unscaledTime : -999f;
            lastStatus = enabled ? "Enabled" : "Disabled";
            lastToolStatus = "Unknown";
            lastKnownScannerToolStatusAt = -999f;
            lastScannerEquipped = false;
            scannerEquipRequestActive = false;
            nextScannerEquipAttemptAt = -999f;
            consecutiveServerRejectTicks = 0;
            Breadcrumbs.Drop("bf.clearstate.begin");
            host?.ClearBirdFarmRuntimeState();
            Breadcrumbs.Drop("bf.clearstate.ok");
            nextCrashHeartbeatAt = enabled ? Time.unscaledTime + 30f : -999f;
            nextSlowTickLogAt = -999f;
            nextRuntimeRecycleAt = enabled ? Time.unscaledTime + RuntimeRecycleSeconds : -999f;
            stationarySinceAt = enabled ? Time.unscaledTime : -999f;
            stationaryThrottleActive = false;
            lastMovementSamplePos = Vector3.zero;
            multiCatchBurstRemaining = 0;
            multiCatchBurstTarget = 0;
            if (!enabled)
            {
                RestorePreviousTool(host);
                host?.ClearBirdFarmRuntimeState();
                sessionCatchCount = 0;
                sessionScaredCount = 0;
                sessionCountedNetIds.Clear();
                consecutiveNoTargetTicks = 0;
                consecutiveServerRejectTicks = 0;
                nextRetryAt = -999f;
                nextRuntimeRecycleAt = -999f;
                scannerReadyAt = -999f;
                sessionStartedAt = -999f;
                lastScannerEquipped = false;
                lastKnownScannerToolStatusAt = -999f;
                scannerEquipRequestActive = false;
                nextScannerEquipAttemptAt = -999f;
                previousToolId = 0;
                previousToolRestorePending = false;
                _pendingConfirmNetIds.Clear();
                _pendingTimeoutStrikes.Clear();
                _pendingConfirmExpiresAt = -999f;
                stationarySinceAt = -999f;
                stationaryThrottleActive = false;
                lastMovementSamplePos = Vector3.zero;
                multiCatchBurstRemaining = 0;
                multiCatchBurstTarget = 0;
            }
            TraceCrashBreadcrumb("Toggle changed: " + (enabled ? "enabled" : "disabled") + $" perfectPhoto={perfectPhotoEnabled} cooldown={catchCooldown:F1} range={scanRange:F0} multiCatch={multiCatchLimit}");
            Log("Toggle changed: " + (enabled ? "enabled" : "disabled"));
        }

        public static void ToggleEnabled(HeartopiaComplete host = null)
        {
            SetEnabled(!enabled, host);
        }
        public static bool IsDebugLoggingEnabled() => debugLoggingEnabled;
        public static bool IsStationaryThrottleActive() => stationaryThrottleActive;
        public static int GetConsecutiveNoTargetTicks() => consecutiveNoTargetTicks;
        public static string GetLastStatus() => GetDisplayStatus(lastStatus);
        public static string GetLastToolStatus() => GetDisplayToolStatus(lastToolStatus);
        public static int GetSessionCatchCount() => sessionCatchCount;
        public static int GetSessionScaredCount() => sessionScaredCount;

        private static void ReportSlowTickIfNeeded(long startTicks, int detectedCount, int resolvedCount, int sentCount, string status)
        {
            double elapsedMs = (DateTime.UtcNow.Ticks - startTicks) / (double)TimeSpan.TicksPerMillisecond;
            if (elapsedMs < SlowTickWarnMilliseconds || Time.unscaledTime < nextSlowTickLogAt)
            {
                return;
            }

            nextSlowTickLogAt = Time.unscaledTime + SlowTickLogCooldownSeconds;
            string trimmedStatus = string.IsNullOrWhiteSpace(status) ? "n/a" : status;
            if (trimmedStatus.Length > 120)
            {
                trimmedStatus = trimmedStatus.Substring(0, 120);
            }

            ModLogger.Msg($"[BirdNetFarmPerf] Slow tick {elapsedMs:F0}ms detected={detectedCount} resolved={resolvedCount} sent={sentCount} status={trimmedStatus}");
        }

        private static string GetDisplayStatus(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return "Idle";
            }

            if (status.StartsWith("Sent request ", StringComparison.Ordinal))
            {
                return "Capturing bird...";
            }

            if (status.StartsWith("Bird scared away:", StringComparison.Ordinal))
            {
                return "Bird scared away";
            }

            switch (status)
            {
                case "Enabled":
                    return "Ready";
                case "Disabled":
                    return "Disabled";
                case "Idle":
                    return "Idle";
                case "Waiting for Bird Scanner to stabilize":
                    return "Preparing Bird Scanner...";
                case "Waiting for bird scan refresh":
                    return "Scanning for birds...";
                case "No birds detected":
                    return "No birds nearby";
                case "No valid targets in range":
                    return "No capturable birds nearby";
                case "Target ready":
                    return "Bird found";
                case "Scanner unavailable":
                    return "Scanner unavailable";
            }

            if (status.StartsWith("Server rejected bird", StringComparison.Ordinal))
            {
                return "Refreshing bird scan...";
            }

            if (status.StartsWith("Auto-stopped:", StringComparison.Ordinal))
            {
                return "Auto-stopped";
            }

            if (status.StartsWith("Skipping unresolved", StringComparison.Ordinal))
            {
                return "Waiting for a capturable bird...";
            }

            if (status.StartsWith("Waiting for capturable bird pose", StringComparison.Ordinal))
            {
                return "Waiting for a capturable bird...";
            }

            if (status.StartsWith("Error:", StringComparison.Ordinal) || status.StartsWith("Scan error:", StringComparison.Ordinal))
            {
                return "Scanner error";
            }

            return status;
        }

        private static string GetDisplayToolStatus(string status)
        {
            if (string.IsNullOrWhiteSpace(status))
            {
                return "Unknown";
            }

            switch (status)
            {
                case "Holding Other Tool":
                    return "Wrong tool equipped";
                case "Tool state unavailable":
                case "Player Unavailable":
                    return "Unavailable";
                case "Equipped (active)":
                case "Bird Scanner Equipped":
                    return "Bird Scanner equipped";
                default:
                    return status;
            }
        }

        private static bool IsScannerUnavailableStatus(string status)
        {
            return string.IsNullOrWhiteSpace(status)
                || string.Equals(status, "Unknown", StringComparison.Ordinal)
                || string.Equals(status, "Tool state unavailable", StringComparison.Ordinal)
                || string.Equals(status, "Player Unavailable", StringComparison.Ordinal);
        }

        private static bool IsUnresolvedBirdStatus(string status)
        {
            return !string.IsNullOrWhiteSpace(status)
                && status.StartsWith("Skipping unresolved action/perch bird", StringComparison.Ordinal);
        }

        // ── Persistent config helpers (called by HeartopiaComplete for XML save/load) ──
        public static void PopulateBirdFarmConfig(HeartopiaComplete.BirdFarmConfigData data)
        {
            if (data == null) return;
            data.perfectPhotoEnabled = perfectPhotoEnabled;
            data.autoScareMaxPhotoEnabled = true;
            data.captureMode = captureMode;
            data.catchCooldown = catchCooldown;
            data.scanRange = scanRange;
            data.multiCatchLimit = multiCatchLimit;
        }

        public static void ApplyBirdFarmConfig(HeartopiaComplete.BirdFarmConfigData data)
        {
            if (data == null) return;
            perfectPhotoEnabled = data.perfectPhotoEnabled;
            captureMode = Mathf.Clamp(data.captureMode, 0, CaptureModeOptions.Length - 1);
            catchCooldown = Mathf.Clamp(data.catchCooldown, 0.2f, 10f);
            scanRange = Mathf.Clamp(data.scanRange, 1f, 100f);
            multiCatchLimit = Mathf.Clamp(data.multiCatchLimit, 1, MaxMultiCatchLimit);
        }

        private static void Log(string message)
        {
            if (!debugLoggingEnabled)
            {
                return;
            }

            ModLogger.Msg("[BirdNetFarm] " + message);
        }

        private static string GetCrashTracePath()
        {
            if (!string.IsNullOrWhiteSpace(crashTracePath))
            {
                return crashTracePath;
            }

#if MELONLOADER
            string logsDir = Path.Combine(Directory.GetCurrentDirectory(), "MelonLoader", "Logs");
#elif BEPINEX
            string logsDir = Path.Combine(Directory.GetCurrentDirectory(), "BepInEx");
#else
            string logsDir = Path.Combine(Directory.GetCurrentDirectory(), "UserData");
#endif
            Directory.CreateDirectory(logsDir);
            crashTracePath = Path.Combine(logsDir, "birdfarm-crashtrace.log");
            return crashTracePath;
        }

        private static void AppendCrashTrace(string message)
        {
            try
            {
                string path = GetCrashTracePath();
                if (File.Exists(path))
                {
                    FileInfo info = new FileInfo(path);
                    if (info.Exists && info.Length > 262144)
                    {
                        File.WriteAllText(path, string.Empty);
                    }
                }

                string line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}";
                File.AppendAllText(path, line);
            }
            catch
            {
            }
        }

        public static void TraceCrashBreadcrumb(string message)
        {
            if (!verboseCrashTraceEnabled)
            {
                return;
            }

            AppendCrashTrace("[BirdNetFarm] " + message);
        }

        private static bool TryGetCurrentPlayerPosition(out Vector3 playerPos)
        {
            playerPos = Vector3.zero;

            try
            {
                GameObject player = HeartopiaComplete.GetLocalPlayer();
                if (player != null)
                {
                    playerPos = player.transform.position;
                    return true;
                }
            }
            catch
            {
            }

            if (Camera.main != null)
            {
                playerPos = Camera.main.transform.position;
                return true;
            }

            return false;
        }

        public static float DrawSection(HeartopiaComplete host, int startY)
        {
            int num = startY;
            GUIStyle small = new GUIStyle(GUI.skin.label) { fontSize = 12 };
            GUIStyle header = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };

            GUI.Label(new Rect(20f, num, 320f, 22f), host.UI_Localize("Auto Bird Farm"), header);
            num += 28;

            if (host.UI_DrawPrimaryActionButton(new Rect(20f, num, 260f, 35f), "Equip Bird Scanner"))
            {
                host.EquipHandTool(4);
            }
            num += 45;

            bool nextEnabled = host.UI_DrawSwitchToggle(new Rect(20f, num, 280f, 25f), enabled, "Auto Bird Farm");
            if (nextEnabled != enabled)
            {
                SetEnabled(nextEnabled, host);
            }
            num += 30;

            bool prevPerfectPhotoEnabled = perfectPhotoEnabled;
            perfectPhotoEnabled = host.UI_DrawSwitchToggle(new Rect(20f, num, 280f, 25f), perfectPhotoEnabled, "Perfect Photo");
            if (perfectPhotoEnabled != prevPerfectPhotoEnabled)
            {
                Log("Perfect Photo changed: " + (perfectPhotoEnabled ? "enabled" : "disabled"));
                pendingSaveAt = Time.unscaledTime + 2f;
            }
            num += 30;

            int prevCaptureMode = captureMode;
            captureMode = host.UI_DrawSingleSelectDropdown(new Rect(20f, num + 22f, 260f, 28f), "Capture Mode", CaptureModeOptions, captureMode, ref captureModeDropdownOpen);
            if (captureMode != prevCaptureMode)
            {
                Log("Capture Mode changed: " + CaptureModeOptions[captureMode]);
                pendingSaveAt = Time.unscaledTime + 2f;
                host.ClearBirdFarmRuntimeState();
                _pendingConfirmNetIds.Clear();
                _pendingTimeoutStrikes.Clear();
                _pendingConfirmExpiresAt = -999f;
                multiCatchBurstRemaining = 0;
                multiCatchBurstTarget = 0;
            }
            num += captureModeDropdownOpen ? 122 : 80;

            GUI.Label(new Rect(20f, num, 360f, 20f), host.UI_LocalizeFormat("Status: {0}", host.UI_Localize(GetDisplayStatus(lastStatus))), small);
            num += 24;
            GUI.Label(new Rect(20f, num, 360f, 20f), host.UI_LocalizeFormat("Tool: {0}", host.UI_Localize(GetDisplayToolStatus(lastToolStatus))), small);
            num += 24;
            GUI.Label(new Rect(20f, num, 360f, 20f), host.UI_LocalizeFormat("Birds: {0} caught | {1} scared", sessionCatchCount, sessionScaredCount), small);
            num += 24;

            GUI.Label(new Rect(20f, num, 320f, 20f), host.UI_LocalizeFormat("Catch Cooldown: {0:F1}s", catchCooldown), small);
            num += 22;
            float prevCooldown = catchCooldown;
            catchCooldown = Mathf.Round(host.UI_DrawAccentSlider(new Rect(20f, num, 260f, 20f), catchCooldown, 0.2f, 10f) * 10f) / 10f;
            if (Math.Abs(catchCooldown - prevCooldown) > 0.0001f)
            {
                Log("Catch Cooldown changed to " + catchCooldown.ToString("F1") + "s");
                pendingSaveAt = Time.unscaledTime + 2f; // debounce
            }
            num += 30;

            GUI.Label(new Rect(20f, num, 320f, 20f), host.UI_LocalizeFormat("Scan Range: {0:F0}m", scanRange), small);
            num += 22;
            float prevRange = scanRange;
            scanRange = Mathf.Round(host.UI_DrawAccentSlider(new Rect(20f, num, 260f, 20f), scanRange, 1f, 100f));
            if (Math.Abs(scanRange - prevRange) > 0.0001f)
            {
                Log("Scan Range changed to " + scanRange.ToString("F0") + "m");
                pendingSaveAt = Time.unscaledTime + 2f; // debounce
            }
            num += 30;

            GUI.Label(new Rect(20f, num, 320f, 20f), host.UI_LocalizeFormat("Multi-Catch Limit: {0}", multiCatchLimit), small);
            num += 22;
            int prevMulti = multiCatchLimit;
            multiCatchLimit = Mathf.RoundToInt(host.UI_DrawAccentSlider(new Rect(20f, num, 260f, 20f), (float)multiCatchLimit, 1f, MaxMultiCatchLimit));
            if (multiCatchLimit != prevMulti)
            {
                Log("Multi-Catch Limit changed to " + multiCatchLimit);
                pendingSaveAt = Time.unscaledTime + 2f; // debounce
            }
            num += 30;

            return num;
        }


        public static void Update(HeartopiaComplete host)
        {
            if (host == null) return;

            if (enabled)
            {
                Breadcrumbs.Tick("BirdFarm.update");
                try { host.TryEnsureBirdFarmMaxPhotoPatch(); } catch { }
            }

            // Flush any pending slider-change config save (debounced 2s after last change)
            if (pendingSaveAt > 0f && Time.unscaledTime >= pendingSaveAt)
            {
                pendingSaveAt = -999f;
                try { host.SaveAllSettings(); } catch { }
            }

            if (!enabled)
            {
                return;
            }

            if (Time.unscaledTime >= nextRuntimeRecycleAt)
            {
                host.ClearBirdFarmRuntimeState();
                _pendingConfirmNetIds.Clear();
                _pendingTimeoutStrikes.Clear();
                _pendingConfirmExpiresAt = -999f;
                multiCatchBurstRemaining = 0;
                multiCatchBurstTarget = 0;
                consecutiveNoTargetTicks = 0;
                consecutiveServerRejectTicks = 0;
                scannerReadyAt = Time.unscaledTime + 1.25f;
                nextRetryAt = Time.unscaledTime + RuntimeRecyclePauseSeconds;
                nextRuntimeRecycleAt = Time.unscaledTime + RuntimeRecycleSeconds;
                lastStatus = "Stability refresh - pausing briefly";
                Log("Runtime recycle: cleared bird farm runtime state and paused briefly.");
                return;
            }

            if (Time.unscaledTime >= nextCrashHeartbeatAt)
            {
                nextCrashHeartbeatAt = Time.unscaledTime + 30f;
                TraceCrashBreadcrumb($"Heartbeat status={lastStatus} tool={lastToolStatus} catches={sessionCatchCount} pending={_pendingConfirmNetIds.Count} retryAt={nextRetryAt:F2}");
            }

            if (TryGetCurrentPlayerPosition(out Vector3 currentPlayerPos))
            {
                if (lastMovementSamplePos == Vector3.zero)
                {
                    lastMovementSamplePos = currentPlayerPos;
                    stationarySinceAt = Time.unscaledTime;
                }
                else
                {
                    float movedDistance = Vector3.Distance(lastMovementSamplePos, currentPlayerPos);
                    if (movedDistance >= StationaryRadiusMeters)
                    {
                        if (stationaryThrottleActive)
                        {
                            TraceCrashBreadcrumb($"Stationary throttle cleared after moving {movedDistance:F2}m");
                        }

                        lastMovementSamplePos = currentPlayerPos;
                        stationarySinceAt = Time.unscaledTime;
                        stationaryThrottleActive = false;
                    }
                    else if (!stationaryThrottleActive && stationarySinceAt > 0f && Time.unscaledTime - stationarySinceAt >= StationaryThrottleAfterSeconds)
                    {
                        stationaryThrottleActive = true;
                        TraceCrashBreadcrumb($"Stationary throttle enabled after {Time.unscaledTime - stationarySinceAt:F1}s in one area");
                    }
                }
            }


            // ── ACK confirmation drain: count server-confirmed captures every frame ──────────────────
            // After each tick we register all sent netIds into _pendingConfirmNetIds.
            // We drain TryConsumeRecentBirdFarmCapture() to count only server-confirmed captures.
            // Dedup TTL is 30s so the same bird doesn't get double-counted across multiple ticks.
            // Birds rejected with "Target Bird does not exist" never produce an ACK so they
            // are naturally excluded from the count.
            if (_pendingConfirmNetIds.Count > 0 && Time.unscaledTime >= _pendingConfirmExpiresAt)
            {
                try
                {
                    uint confirmedNetId;
                    while (host.TryConsumeRecentBirdFarmCapture(out confirmedNetId) && confirmedNetId != 0U)
                    {
                        if (_pendingConfirmNetIds.Contains(confirmedNetId))
                        {
                            // Count unique birds caught this session, not repeated
                            // spam-photo hits on the same bird netId.
                            if (sessionCountedNetIds.Add(confirmedNetId))
                            {
                                host.ConfirmRecentBirdFarmCapture(confirmedNetId);
                                sessionCatchCount++;
                                TraceCrashBreadcrumb($"Confirmed capture netId={confirmedNetId} total={sessionCatchCount}");
                                Log($"Confirmed bird capture netId={confirmedNetId} (+1 → total={sessionCatchCount})");
                            }
                            _pendingTimeoutStrikes.Remove(confirmedNetId);
                            _pendingConfirmNetIds.Remove(confirmedNetId);
                        }
                    }

                    // Expire the batch window after PendingConfirmTimeout seconds.
                    if (Time.unscaledTime - _pendingConfirmExpiresAt >= PendingConfirmTimeout)
                    {
                        if (_pendingConfirmNetIds.Count > 0)
                        {
                            Log($"Pending batch timed out with {_pendingConfirmNetIds.Count} unconfirmed — blacklisting");
                            foreach (uint stale in _pendingConfirmNetIds)
                            {
                                int strikes;
                                _pendingTimeoutStrikes.TryGetValue(stale, out strikes);
                                strikes++;
                                if (strikes >= PendingTimeoutStrikesBeforeBlacklist)
                                {
                                    _pendingTimeoutStrikes.Remove(stale);
                                    try { host.BlacklistBirdFarmNetId(stale, PendingTimeoutBlacklistSeconds); } catch { }
                                    Log($"Timeout strike {strikes}/{PendingTimeoutStrikesBeforeBlacklist} for netId={stale}; temporary blacklist {PendingTimeoutBlacklistSeconds:F0}s");
                                }
                                else
                                {
                                    _pendingTimeoutStrikes[stale] = strikes;
                                    Log($"Timeout strike {strikes}/{PendingTimeoutStrikesBeforeBlacklist} for netId={stale}; not blacklisted yet");
                                }
                            }
                        }
                        _pendingConfirmNetIds.Clear();
                        _pendingConfirmExpiresAt = -999f;
                    }
                }
                catch (Exception ex)
                {
                    TraceCrashBreadcrumb("Confirm drain error: " + ex.GetType().Name + ": " + ex.Message);
                    Log("Confirm drain error: " + ex.Message);
                    _pendingConfirmNetIds.Clear();
                    _pendingConfirmExpiresAt = -999f;
                }
            }

            bool hasActiveMultiCatchBurst = multiCatchBurstRemaining > 0;
            if (!hasActiveMultiCatchBurst && Time.unscaledTime - lastAttemptAt < catchCooldown)
            {
                return;
            }

            if (Time.unscaledTime < enableWarmupUntil)
            {
                return;
            }

            if (Time.unscaledTime < nextRetryAt)
            {
                return;
            }

            if (_pendingConfirmNetIds.Count >= PendingConfirmHighWatermark)
            {
                lastStatus = "Waiting for server confirm...";
                nextRetryAt = Time.unscaledTime + PendingConfirmPressureDelay;
                return;
            }


            try
            {
                bool gotToolStatus = host.TryGetBirdScannerToolStatus(out bool scannerEquipped, out string toolStatus);
                if (!gotToolStatus)
                {
                    if (lastScannerEquipped || !IsScannerUnavailableStatus(lastToolStatus))
                    {
                        scannerEquipped = lastScannerEquipped;
                        if (lastScannerEquipped)
                        {
                            lastToolStatus = "Bird Scanner Equipped";
                        }
                    }
                    else if (!string.IsNullOrWhiteSpace(toolStatus))
                    {
                        lastToolStatus = toolStatus;
                    }
                    else
                    {
                        lastToolStatus = "Unknown";
                    }
                }
                else
                {
                    lastToolStatus = scannerEquipped ? "Bird Scanner Equipped" : toolStatus;
                    if (scannerEquipped)
                    {
                        lastKnownScannerToolStatusAt = Time.unscaledTime;
                        scannerEquipRequestActive = false;
                        nextScannerEquipAttemptAt = -999f;
                    }
                }
                bool shouldEquipScanner = !scannerEquipped
                    && (gotToolStatus || !IsScannerUnavailableStatus(lastToolStatus));
                if (shouldEquipScanner)
                {
                    lastScannerEquipped = false;
                    lastKnownScannerToolStatusAt = -999f;
                    scannerReadyAt = -999f;
                    multiCatchBurstRemaining = 0;
                    multiCatchBurstTarget = 0;
                    lastAttemptAt = Time.unscaledTime;
                    EnsureBirdScannerEquipped(host);
                    consecutiveNoTargetTicks = 0;
                    return;
                }

                if (gotToolStatus && scannerEquipped)
                {
                    if (!lastScannerEquipped)
                    {
                        lastScannerEquipped = true;
                        lastKnownScannerToolStatusAt = Time.unscaledTime;
                        scannerReadyAt = Time.unscaledTime + 1.25f;
                        lastAttemptAt = Time.unscaledTime;
                        lastStatus = "Waiting for Bird Scanner to stabilize";
                        consecutiveNoTargetTicks = 0;
                        nextRetryAt = -999f;
                        host.ClearBirdFarmRuntimeState();
                        multiCatchBurstRemaining = 0;
                        multiCatchBurstTarget = 0;
                        Log("Tick skipped: scanner equipped, waiting for stabilize");
                        return;
                    }

                    if (Time.unscaledTime < scannerReadyAt)
                    {
                        lastAttemptAt = Time.unscaledTime;
                        lastStatus = "Waiting for Bird Scanner to stabilize";
                        consecutiveNoTargetTicks = 0;
                        nextRetryAt = -999f;
                        return;
                    }
                }
                else
                {
                    lastScannerEquipped = false;
                    scannerReadyAt = -999f;
                }

                int requestedCatchLimit = (stationaryThrottleActive && !IsSpamMaxPhotoCaptureMode)
                    ? Mathf.Min(multiCatchLimit, StationaryMultiCatchLimit)
                    : multiCatchLimit;
                if (multiCatchBurstRemaining <= 0)
                {
                    multiCatchBurstTarget = requestedCatchLimit;
                    multiCatchBurstRemaining = requestedCatchLimit;
                }

                int effectiveCatchLimit = Mathf.Clamp(multiCatchBurstRemaining, 1, MaxMultiCatchLimit);
                Log($"Tick start: range={scanRange:F0} cooldown={catchCooldown:F1} multiCatch={multiCatchLimit} burst={multiCatchBurstRemaining}/{multiCatchBurstTarget} effectiveMultiCatch={effectiveCatchLimit}");

                int totalDetected = 0, totalResolved = 0, totalSent = 0;
                string tickStatus = "Idle";
                bool anySuccess = false;
                int catchLimit = effectiveCatchLimit;
                _tickSentNetIdsReuse.Clear();
                _tickSentNetIdsSetReuse.Clear();
                long slowTickStartTicks = DateTime.UtcNow.Ticks;
                host.BeginBirdFarmBurst();
                try
                {
                    for (int catchIdx = 0; catchIdx < catchLimit; catchIdx++)
                    {
                        bool iterResult = host.TryTakeNearbyBirdPhotos(scanRange, perfectPhotoEnabled, IsSpamMaxPhotoCaptureMode, out int dc, out int rc, out int sc, out string st);
                        totalDetected = Mathf.Max(totalDetected, dc);
                        if (catchIdx == 0 || (!iterResult || sc == 0))
                        {
                            tickStatus = st;
                        }
                        totalResolved += rc;
                        totalSent += sc;
                        if (iterResult && sc > 0)
                        {
                            anySuccess = true;

                            try
                            {
                                IReadOnlyList<uint> iterIds = host.GetLastBirdFarmSentNetIdsView();
                                if (iterIds != null)
                                {
                                    foreach (uint id in iterIds)
                                    {
                                        if (id == 0U)
                                        {
                                            continue;
                                        }

                                        host.RememberBirdFarmBurstNetId(id);
                                        if (_tickSentNetIdsSetReuse.Add(id))
                                        {
                                            _tickSentNetIdsReuse.Add(id);
                                        }
                                    }
                                }
                            }
                            catch { }
                        }
                        if (!iterResult || sc == 0) break;
                    }
                }
                finally
                {
                    host.EndBirdFarmBurst();
                }
                bool result = anySuccess;
                int sentCount = _tickSentNetIdsReuse.Count > 0 ? _tickSentNetIdsReuse.Count : totalSent;
                int resolvedCount = Mathf.Max(totalResolved, sentCount);
                int detectedCount = Mathf.Max(totalDetected, resolvedCount);
                string status = sentCount > 0 ? $"Sent request {sentCount}/{resolvedCount}" : tickStatus;

                lastAttemptAt = Time.unscaledTime;
                lastStatus = status;
                // Check for server-side rejection toast ("Target Bird does not exist").
                bool wasRejectedByServer = status != null
                    && (status.IndexOf("does not exist", StringComparison.OrdinalIgnoreCase) >= 0
                        || status.IndexOf("BirdNotExist", StringComparison.OrdinalIgnoreCase) >= 0);
                // NOTE: Do NOT call TryConsumeRecentBirdFarmCapture here — that would drain ACKs
                // before the confirm-drain loop in Update() gets a chance to process them.

                if (result && sentCount > 0)
                {
                    consecutiveNoTargetTicks = 0;
                    consecutiveServerRejectTicks = 0;
                    multiCatchBurstRemaining = Mathf.Max(0, multiCatchBurstRemaining - sentCount);
                    if (sentCount < catchLimit)
                    {
                        multiCatchBurstRemaining = 0;
                        multiCatchBurstTarget = 0;
                    }

                    nextRetryAt = multiCatchBurstRemaining > 0
                        ? Time.unscaledTime + MultiCatchBurstSpacingSeconds
                        : Time.unscaledTime + Mathf.Max(PendingConfirmDelay, catchCooldown);
                    // Set lastScannerEquipped=true when successfully sending.
                    lastScannerEquipped = true;
                    lastKnownScannerToolStatusAt = Time.unscaledTime;

                    // Register ALL sent netIds into the pending confirm set so the ACK drain
                    // loop above can match server confirmations to them and count captures.
                    // Do NOT count here — only server-ACK'd netIds count toward the session total.
                    if (_tickSentNetIdsReuse.Count > 0)
                    {
                        foreach (uint id in _tickSentNetIdsReuse)
                            _pendingConfirmNetIds.Add(id);
                        _pendingConfirmExpiresAt = Time.unscaledTime + PendingConfirmDelay;
                        TraceCrashBreadcrumb($"Sent batch count={_tickSentNetIdsReuse.Count} pending={_pendingConfirmNetIds.Count} status={status}");
                        Log($"Pending confirm batch: {_pendingConfirmNetIds.Count} netIds ({_tickSentNetIdsReuse.Count} new this tick), window opens at t={_pendingConfirmExpiresAt:F2}");
                    }
                }
                else if (wasRejectedByServer)
                {
                    consecutiveServerRejectTicks++;
                    consecutiveNoTargetTicks = 0;
                    multiCatchBurstRemaining = 0;
                    multiCatchBurstTarget = 0;
                    host.ClearBirdFarmRuntimeState();
                    nextRetryAt = Time.unscaledTime + 2f;
                    lastStatus = $"Server rejected bird (retry {consecutiveServerRejectTicks}) — refreshing scan";
                    Log($"Server rejection #{consecutiveServerRejectTicks}: forcing cache clear. status={status}");

                    if (consecutiveServerRejectTicks >= 5)
                    {
                        enabled = false;
                        safetyStopBlockUntil = Time.unscaledTime + SafetyStopCooldownSeconds;
                        lastStatus = "Auto-stopped: too many server rejections";
                        consecutiveServerRejectTicks = 0;
                        host.ClearBirdFarmRuntimeState();
                        multiCatchBurstRemaining = 0;
                        multiCatchBurstTarget = 0;
                        Log("Disabled after 5 consecutive server rejections.");
                        return;
                    }
                }
                else if (string.Equals(status, "No bird entity targets found", StringComparison.Ordinal)
                    || string.Equals(status, "No bird targets resolved", StringComparison.Ordinal)
                    || string.Equals(status, "No scanner bird target", StringComparison.Ordinal)
                    || string.Equals(status, "Waiting for bird scan refresh", StringComparison.Ordinal)
                    || (status != null && status.StartsWith("No fresh birds available", StringComparison.Ordinal))
                    || string.Equals(status, "Aura mono bird entity scan found no birds", StringComparison.Ordinal))
                {
                    consecutiveNoTargetTicks++;
                    consecutiveServerRejectTicks = 0;
                    multiCatchBurstRemaining = 0;
                    multiCatchBurstTarget = 0;
                    float backoffSeconds = Mathf.Min(1.5f, 0.5f * consecutiveNoTargetTicks);
                    nextRetryAt = Time.unscaledTime + backoffSeconds;
                }
                else if (IsUnresolvedBirdStatus(status)
                    || (status != null && status.StartsWith("Waiting for capturable bird pose", StringComparison.Ordinal)))
                {
                    consecutiveNoTargetTicks++;
                    consecutiveServerRejectTicks = 0;
                    multiCatchBurstRemaining = 0;
                    multiCatchBurstTarget = 0;
                    nextRetryAt = Time.unscaledTime + UnresolvedBirdBackoffSeconds;
                }
                else
                {
                    consecutiveNoTargetTicks = 0;
                    consecutiveServerRejectTicks = 0;
                    multiCatchBurstRemaining = 0;
                    multiCatchBurstTarget = 0;
                    nextRetryAt = -999f;
                }
                Log($"Tick result: success={result} detected={detectedCount} resolved={resolvedCount} sent={sentCount} status={status}");
                ReportSlowTickIfNeeded(slowTickStartTicks, detectedCount, resolvedCount, sentCount, status);

            }
            catch (Exception ex)
            {
                lastAttemptAt = Time.unscaledTime;
                lastStatus = "Error: " + ex.Message;
                consecutiveNoTargetTicks = 0;
                nextRetryAt = Time.unscaledTime + 2f;
                TraceCrashBreadcrumb("Update exception: " + ex.GetType().Name + ": " + ex.Message);
                Log("Update error: " + ex);
                if (debugLoggingEnabled)
                {
                    ModLogger.Msg("[BirdNetFarm] Update error: " + ex.Message);
                }
            }
        }

        public static int NotifyMaxPhotoAutoScare(uint netId, bool success, string status)
        {
            if (netId != 0U)
            {
                _pendingConfirmNetIds.Remove(netId);
                _pendingTimeoutStrikes.Remove(netId);
            }

            multiCatchBurstRemaining = 0;
            multiCatchBurstTarget = 0;
            consecutiveServerRejectTicks = 0;
            nextRetryAt = Time.unscaledTime + (success ? 1.5f : 2.5f);
            if (success)
            {
                sessionScaredCount++;
                lastStatus = $"Bird scared away: {sessionScaredCount}";
            }
            else
            {
                lastStatus = "Max photo bird scare failed";
            }

            Log($"MaxPhoto auto-scare netId={netId} success={success} status={status}");
            return sessionScaredCount;
        }

        private static void EnsureBirdScannerEquipped(HeartopiaComplete host)
        {
            if (host == null)
            {
                return;
            }

            scannerEquipRequestActive = true;
            float now = Time.unscaledTime;
            if (now >= nextScannerEquipAttemptAt)
            {
                host.EquipHandTool(4);
                nextScannerEquipAttemptAt = now + ScannerEquipRetryInterval;
                lastStatus = "Equipping Bird Scanner...";
                Log("Bird Scanner missing; sent equip request.");
                return;
            }

            lastStatus = "Waiting for Bird Scanner equip...";
        }

        private static void CapturePreviousTool(HeartopiaComplete host)
        {
            previousToolId = 0;
            previousToolRestorePending = false;

            // NOTE: deliberately does NOT read the current tool here. Enabling Auto Bird Farm runs on
            // the hotkey frame, and TryGetCurrentToolInfo can trigger a cold AuraMono ToolSystem module
            // resolve — a heavy enumeration of live mono game objects that races the game's GC and
            // native-AVs when an object is freed mid-walk (reliably under a debugger, the no-crashlog
            // crash localized to bf.capturetool.begin). The "restore previous tool after bird farm"
            // convenience is dropped to keep enabling crash-safe; RestorePreviousTool falls back to the
            // bird-scanner tool status when nothing was captured.
        }

        private static void RestorePreviousTool(HeartopiaComplete host)
        {
            if (host == null)
            {
                previousToolId = 0;
                previousToolRestorePending = false;
                return;
            }

            if (!previousToolRestorePending || previousToolId == 0)
            {
                if (host.TryGetBirdScannerToolStatus(out bool scannerEquipped, out _) && scannerEquipped)
                {
                    host.EquipHandTool(0);
                    Log("No previous supported tool captured; unequipping Bird Scanner.");
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

        public static void ForceStop(HeartopiaComplete host = null)
        {
            enabled = false;
            lastAttemptAt = -999f;
            enableWarmupUntil = -999f;
            scannerReadyAt = -999f;
            sessionStartedAt = -999f;

            lastStatus = "Disabled";
            lastToolStatus = "Unknown";
            lastKnownScannerToolStatusAt = -999f;
            scannerEquipRequestActive = false;
            nextScannerEquipAttemptAt = -999f;
            previousToolId = 0;
            previousToolRestorePending = false;
            sessionCatchCount = 0;
            sessionScaredCount = 0;
            consecutiveNoTargetTicks = 0;
            nextRetryAt = -999f;
            lastScannerEquipped = false;
            sessionCountedNetIds.Clear();
            _pendingConfirmNetIds.Clear();
            _pendingTimeoutStrikes.Clear();
            _pendingConfirmExpiresAt = -999f;
            nextCrashHeartbeatAt = -999f;
            nextRuntimeRecycleAt = -999f;
            stationarySinceAt = -999f;
            stationaryThrottleActive = false;
            lastMovementSamplePos = Vector3.zero;
            multiCatchBurstRemaining = 0;
            multiCatchBurstTarget = 0;
            TraceCrashBreadcrumb("ForceStop invoked");
            if (host != null)
            {
                host.ClearBirdFarmRuntimeState();
            }
        }

    }
}
