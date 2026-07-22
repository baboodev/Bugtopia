using System;
using UnityEngine;

namespace HeartopiaMod
{
    public static class AutoFishingFarm
    {
        private static bool debugLoggingEnabled => HeartopiaComplete.MasterLogAutoFish;

        private static bool enabled = false;
        private static bool instantCatchEnabled = false;
        private static float nextInstantCatchAt = -999f;
        // Per-cast instant-catch diagnostics.
        private static int instantCatchCastSeq = 0;
        private static bool instantCatchBiteLogged = false;
        private static bool instantCatchResultLogged = false;
        private static float instantCatchBiteAt = -1f;
        private const float InstantCatchInterval = 0.1f;
        // High-frequency buoy resend: the decision loop is gated (~0.15s), but our successLength=-2 must
        // be the server's value within ~1 frame of the bite/buoy-activation to win the latch race for
        // close fish. This fires (before the gate) using state cached from the last tick. The rate is
        // user-tunable (Hz) — higher = surer win but more reliable traffic.
        private const float InstantCatchSendHzMin = 0f; // 0 = disable our timed resend (rely on the detour)
        private const float InstantCatchSendHzMax = 240f;
        private const float InstantCatchSendHzDefault = 0f; // detour handles it; resend is an opt-in fallback
        private static float instantCatchSendHz = InstantCatchSendHzDefault;
        private static float nextHighFreqInstantAt = -999f;
        private static bool instantCatchActiveCached = false;
        private static int instantCatchSendCount = 0;

        // --- Event-driven fishing signals (see docs/GAME_EVENTS.md). All fishing events are GLOBAL
        // (FishingProtocolManager / HandHoldFishingRod via EventCenter.DispatchEvent(in @event)). The
        // continuous battle/reel control stays per-frame (polled); these events only mark the exact
        // transition moments (buoy active, bite, result, catch, reset). Additive — they do not replace
        // the polling state machine, only sharpen Instant Catch activation + per-cast diagnostics. ---
        private static bool fishingEventHooksRegistered;
        private static float fishBiteEventAt = -999f;        // CmdOnFishBait
        private static uint fishBiteShadowNetId;
        private static float fishBattleResultAt = -999f;     // CmdFishBattleResult
        private static bool fishBattleResultSuccess;
        private static int fishBattleResultFishId;
        private static int fishBattleResultFailReason; // FailReason@8: 0=None 1=Distance 2=LineBreak 3=TimeOut
        private const float FishScanAvoidSeconds = 1.6f; // skip the just-caught object this long (ghost despawn)
        private static int castTargetInstanceId; // Unity instance id of the fish object this cast targeted
        private static float fishCatchEventAt = -999f;        // PlayerCatchFish
        private static uint fishCatchFishNetId;
        // Instant-catch active window opened by the buoy-active / bite events and closed by the
        // result/catch/reset events — lets the optional high-frequency resend start within ~1 frame of
        // the actual bite instead of waiting for the ~0.15s decision-loop poll.
        private static float instantCatchEventActiveUntil = -999f;
        private const float FishingEventActiveWindow = 8f;
        private static float lastLoggedFishBiteAt = -999f;
        private static float lastLoggedFishResultAt = -999f;

        private const string FishOnBaitEventName = "XDTDataAndProtocol.Events.CmdOnFishBait";
        private const string FishBattleResultEventName = "XDTDataAndProtocol.Events.CmdFishBattleResult";
        private const string FishBuoyResultEventName = "XDTDataAndProtocol.Events.CmdActivateRodBuoyResult";
        private const string FishCatchEventName = "ScriptsRefactory.DataAndProtocol.Events.PlayerCatchFish";
        private const string FishResetStateEventName = "XDTDataAndProtocol.Events.ResetFishState";

        private static void EnsureFishingEventHooks(HeartopiaComplete host)
        {
            if (fishingEventHooksRegistered || host == null)
            {
                return;
            }

            fishingEventHooksRegistered = true;
            host.RegisterGameEventHook(FishOnBaitEventName, 4, OnFishOnBaitEvent);             // {uint fishShadowNetId@0}
            host.RegisterGameEventHook(FishBattleResultEventName, 12, OnFishBattleResultEvent);  // {bool result@0; int fishId@4; FailReason@8}
            host.RegisterGameEventHook(FishBuoyResultEventName, 4, OnFishBuoyResultEvent);       // {bool result@0}
            host.RegisterGameEventHook(FishCatchEventName, 8, OnPlayerCatchFishEvent);           // {uint playerNetId@0; uint fishNetId@4}
            host.RegisterGameEventHook(FishResetStateEventName, 0, OnResetFishStateEvent);       // empty
        }

        // Handlers run on the Unity main thread (event drain) — only set fields / open-close the window.
        private static void OnFishOnBaitEvent(HeartopiaComplete.GameEventSnapshot e)
        {
            fishBiteEventAt = Time.unscaledTime;
            fishBiteShadowNetId = e.ReadUInt32(0);
            instantCatchEventActiveUntil = fishBiteEventAt + FishingEventActiveWindow;
        }

        private static void OnFishBuoyResultEvent(HeartopiaComplete.GameEventSnapshot e)
        {
            if (e.ReadBool(0))
            {
                instantCatchEventActiveUntil = Time.unscaledTime + FishingEventActiveWindow;
            }
        }

        // FailReason enum (EcsClient/.../FailReason.cs): None/Distance/LineBreak/TimeOut.
        private static string FailReasonName(int r)
        {
            switch (r)
            {
                case 0: return "None";
                case 1: return "Distance";
                case 2: return "LineBreak";
                case 3: return "TimeOut";
                default: return "?" + r;
            }
        }

        private static void OnFishBattleResultEvent(HeartopiaComplete.GameEventSnapshot e)
        {
            fishBattleResultAt = Time.unscaledTime;
            fishBattleResultSuccess = e.ReadBool(0);
            fishBattleResultFishId = e.ReadInt32(4);
            fishBattleResultFailReason = e.ReadInt32(8); // 0=None 1=Distance 2=LineBreak 3=TimeOut
            instantCatchEventActiveUntil = -999f; // cycle ended

            // On a catch, mark the exact caught fish object (its instance id, captured at cast) so the
            // next scan skips only its lingering ghost — not the rest of a clustered school.
            if (fishBattleResultSuccess && castTargetInstanceId != 0)
            {
                HeartopiaComplete.fishScanGhostInstanceId = castTargetInstanceId;
                HeartopiaComplete.fishScanGhostUntil = Time.unscaledTime + FishScanAvoidSeconds;
            }
        }

        private static void OnPlayerCatchFishEvent(HeartopiaComplete.GameEventSnapshot e)
        {
            fishCatchEventAt = Time.unscaledTime;
            fishCatchFishNetId = e.ReadUInt32(4);
            instantCatchEventActiveUntil = -999f;
        }

        private static void OnResetFishStateEvent(HeartopiaComplete.GameEventSnapshot e)
        {
            instantCatchEventActiveUntil = -999f;
        }

        // --- Auto Bait: throw a bait/attractor when no fish has been in scan range for N seconds ---
        public enum AutoBaitChoice { Bait = 0, Attractor = 1 }
        private const float AutoBaitNoFishSecondsMin = 3f;
        private const float AutoBaitNoFishSecondsMax = 60f;
        private const float AutoBaitNoFishSecondsDefault = 10f;
        private const int AutoBaitMaxCountDefault = 10;
        private const float AutoBaitMinCooldown = 8f;   // bridge the 1-3s server spawn latency after a throw
        private const float AutoBaitFailBackoff = 5f;   // item not in bag — don't spam attempts
        private static bool autoBaitEnabled = false;
        private static AutoBaitChoice autoBaitChoice = AutoBaitChoice.Attractor;
        private static int autoBaitMaxCount = AutoBaitMaxCountDefault;
        private static float autoBaitNoFishSeconds = AutoBaitNoFishSecondsDefault;
        private static int autoBaitRemaining = AutoBaitMaxCountDefault;
        private static float noFishSinceAt = -1f;
        private static float nextAutoBaitAt = -999f;

        // --- Skip Catch Animation: cut the ~2.7s post-catch take-in (and fail-slack) theater ---
        // Invokes FishingProtocolManager.TakeUpRod → ResetFishState, the game's own instant-reset
        // path (local-only; the server already concluded the session at battle result).
        private static bool skipCatchAnimEnabled = false;
        private static bool skipCatchAnimInvokedForCast = false;

        // --- Skip Cast Animation: finish the throw clip early so ActivateRodBuoy goes out sooner ---
        // The cast clip gates the FSM transition, which gates FloatInWater/ActivateRodBuoy (the
        // server-side buoy activation, ~1.5-2s). We poll the animator after each cast: once it's in
        // SpinningRod for TWO consecutive polls (the clip has ticked into its Playing phase),
        // CrossFade(Fishing, 0) makes the clip hit its own natural end condition immediately.
        private const float SkipCastPollStartDelay = 0.10f;
        private const float SkipCastPollInterval = 0.07f;
        private const float SkipCastWindowSeconds = 1.5f;
        private static bool skipCastAnimEnabled = false;
        // Skip Bait Animation: send bait/attractor (Use Bait / Use Attractor / Auto Bait) via the direct
        // network command instead of the ~1.5-2s throw animation. Read by HeartopiaComplete's bait use.
        private static bool skipBaitAnimEnabled = false;
        private static float skipCastWindowUntil = -999f;
        private static float nextSkipCastPollAt = -999f;
        private static int skipCastSpinSeenCount = 0;
        private static bool skipCastDoneForCast = false;

        private static float fishShadowDetectRange = 60f;
        private static string lastStatus = "Idle";
        private static string lastToolStatus = "Unknown";
        private static string lastTargetStatus = "None";
        private const float RodEquipRetryInterval = 3.25f;
        private static float nextRodEquipAttemptAt = -999f;
        private static int previousToolId = 0;
        private static bool previousToolRestorePending = false;
        private static float nextActionAt = -999f;
        private static float sessionStartedAt = -999f;
        private static float waitingSinceAt = -999f;
        private static float hookedSinceAt = -999f;
        private static float lastHookedStateAt = -999f;
        private static float lastBattleStateAt = -999f;
        private static float lastBattleLostBaitAt = -999f;
        private static float ignoreStaleFishingStateUntil = -999f;
        private static uint lastBattleBaitNetId = 0U;
        private static float lastCastSentAt = -999f;
        private static float nextWorldReadyLogAt = -999f;
        private static float nextFishingStateLogAt = -999f;
        private static float nextActiveStateLogAt = -999f;
        private static string lastActiveStateLogKey = string.Empty;
        private static float nextTargetMissLogAt = -999f;
        private static float nextPressUpdateAt = -999f;
        private static int consecutiveTargetMisses = 0;
        private static uint currentTargetNetId = 0U;
        private static Vector3 currentTargetPos = Vector3.zero;
        private static bool? lastRequestedPressed = null;
        private const float BattlePressCooldown = 0.08f;
        private const float TensionEmergencyReleaseThreshold = 0.15f;
        private const float TensionResumePullThreshold = 0.35f;
        private const float PostHookIdleGraceSeconds = 0.75f;
        private const float PostBattleIdleGraceSeconds = 5f;
        private const float PostLostBattleIdleGraceSeconds = 8f;
        private const float StaleIdleGraceSeconds = 0.35f;
        private const float PostCastIdleGraceSeconds = 4f;
        private const float FastRecastDelay = 0.1f;
        private const float AfterCastPollDelay = 0.15f;
        private const float EmptyScanMinDelay = 0.55f;
        private const float EmptyScanMaxDelay = 1.5f;
        private const float EmptyScanMissLogInterval = 10f;

        public static bool IsEnabled => enabled;
        public static bool IsDebugLoggingEnabled() => debugLoggingEnabled;

        // --- Read-only signals for FishingRouteFeature (location rotation) ---
        // Last fish-shadow scan result: when it ran and how many shadows were inside the radius.
        public static float LastScanAt { get; private set; } = -999f;
        public static int LastInRangeCount { get; private set; }
        // True while the engine is inside an active fishing session (cast/waiting/battle/hook).
        public static bool IsInFishingSession { get; private set; }
        private static bool wasInFishingSession = false; // for the cast-cycle-end durability check edge
        // When Auto Bait last threw a bait/attractor (route restarts its no-fish window on this).
        public static float LastAutoBaitAt { get; private set; } = -999f;
        public static string GetLastStatus() => GetDisplayStatus(lastStatus);
        public static string GetLastToolStatus() => GetDisplayToolStatus(lastToolStatus);
        public static string GetLastTargetStatus() => GetDisplayTargetStatus(lastTargetStatus);
        public static float GetDetectRange() => fishShadowDetectRange;
        public static void SetDetectRange(float value) => fishShadowDetectRange = Mathf.Clamp(value, 1f, 200f);
        // UGUI slider entry — the IMGUI slider block's own behavior (DrawSection ~721-731:
        // round, change-detect, "Range updated" status, log) as a callable. Deliberately a
        // SEPARATE method from SetDetectRange, which stays the silent clamp-and-store used by
        // config load and FishingRouteFeature's forced-200m/restore-snapshot paths.
        public static void SetDetectRangeFromUi(float value)
        {
            float rounded = Mathf.Round(Mathf.Clamp(value, 1f, 200f));
            if (Math.Abs(rounded - fishShadowDetectRange) > 0.0001f)
            {
                fishShadowDetectRange = rounded;
                lastTargetStatus = "Range updated";
                Log("Detect range changed to " + rounded.ToString("F0") + "m");
            }
        }
        public static float GetInstantCatchSendHz() => instantCatchSendHz;
        public static void SetInstantCatchSendHz(float value) => instantCatchSendHz = Mathf.Clamp(value, InstantCatchSendHzMin, InstantCatchSendHzMax);

        // --- Auto Bait accessors (used by UI + config persistence) ---
        public static bool GetAutoBaitEnabled() => autoBaitEnabled;
        public static void SetAutoBaitEnabled(bool value)
        {
            if (autoBaitEnabled == value)
            {
                return;
            }

            autoBaitEnabled = value;
            ResetAutoBaitCounter();      // fresh budget each time it's turned on
            noFishSinceAt = -1f;
            nextAutoBaitAt = -999f;
            Log("Auto Bait " + (value ? "enabled" : "disabled") + " choice=" + autoBaitChoice + " max=" + autoBaitMaxCount);
        }
        public static int GetAutoBaitChoice() => (int)autoBaitChoice;
        public static void SetAutoBaitChoice(int value) => autoBaitChoice = (AutoBaitChoice)Mathf.Clamp(value, 0, 1);
        public static int GetAutoBaitMaxCount() => autoBaitMaxCount;
        public static void SetAutoBaitMaxCount(int value)
        {
            int clamped = Mathf.Clamp(value, 0, 999);
            if (clamped == autoBaitMaxCount)
            {
                return;
            }

            autoBaitMaxCount = clamped;
            ResetAutoBaitCounter();      // changing the limit refills the live counter
        }
        public static float GetAutoBaitNoFishSeconds() => autoBaitNoFishSeconds;
        public static void SetAutoBaitNoFishSeconds(float value) => autoBaitNoFishSeconds = Mathf.Clamp(value, AutoBaitNoFishSecondsMin, AutoBaitNoFishSecondsMax);
        public static bool GetSkipCatchAnimEnabled() => skipCatchAnimEnabled;
        public static void SetSkipCatchAnimEnabled(bool value) => skipCatchAnimEnabled = value;
        public static bool GetSkipCastAnimEnabled() => skipCastAnimEnabled;
        public static void SetSkipCastAnimEnabled(bool value)
        {
            skipCastAnimEnabled = value;
            if (!value)
            {
                skipCastWindowUntil = -999f;
            }
        }
        public static bool GetSkipBaitAnimEnabled() => skipBaitAnimEnabled;
        public static void SetSkipBaitAnimEnabled(bool value) => skipBaitAnimEnabled = value;
        public static int GetAutoBaitRemaining() => autoBaitRemaining;
        public static void ResetAutoBaitCounter()
        {
            autoBaitRemaining = autoBaitMaxCount;
            nextAutoBaitAt = -999f;
        }

        // Maintains the "no fish in radius" timer and throws the selected bait/attractor when the
        // radius has been fish-free longer than the configured window. Called every scan tick.
        private static void TryAutoBaitTick(HeartopiaComplete host, float now, int inRangeCount)
        {
            if (!autoBaitEnabled)
            {
                noFishSinceAt = -1f;
                return;
            }

            if (inRangeCount > 0)
            {
                noFishSinceAt = -1f;   // fish present — reset the no-fish window
                return;
            }

            if (noFishSinceAt < 0f)
            {
                noFishSinceAt = now;
            }

            if (autoBaitRemaining <= 0 || now < nextAutoBaitAt)
            {
                return;
            }

            if ((now - noFishSinceAt) < autoBaitNoFishSeconds)
            {
                return;
            }

            bool useBait = autoBaitChoice == AutoBaitChoice.Bait;
            if (host.TryThrowFishBaitForAuto(useBait, out string kind))
            {
                autoBaitRemaining--;
                nextAutoBaitAt = now + AutoBaitMinCooldown;
                noFishSinceAt = now;   // restart the window while the server spawns fish
                LastAutoBaitAt = now;
                Log($"Auto Bait thrown ({kind}); remaining={autoBaitRemaining}/{autoBaitMaxCount}");
                try { host.UI_AddMenuNotification(host.UI_Localize("Auto Bait thrown") + " (" + autoBaitRemaining + ")", new Color(0.45f, 1f, 0.55f)); } catch { }
                if (autoBaitRemaining == 0)
                {
                    try { host.UI_AddMenuNotification(host.UI_Localize("Auto Bait limit reached"), new Color(1f, 0.65f, 0.45f)); } catch { }
                }
            }
            else
            {
                nextAutoBaitAt = now + AutoBaitFailBackoff;
                Log("Auto Bait: " + kind + " not available in bag; backing off " + AutoBaitFailBackoff + "s");
            }
        }
        public static bool GetInstantCatchEnabled() => instantCatchEnabled;
        public static void SetInstantCatchEnabled(bool value)
        {
            if (instantCatchEnabled == value)
            {
                return;
            }

            instantCatchEnabled = value;
            // Drives the NotifyFloatInWater detour (rewrites the game's successLength to -2 at source)
            // and fully removes it when disabled so it can't affect normal fishing.
            HeartopiaComplete.instantCatchSuccessSpoofActive = value;
            HeartopiaComplete.SetInstantCatchNotifyHookApplied(value);
            nextInstantCatchAt = -999f;
            Log("Instant Catch " + (value ? "enabled" : "disabled"));
        }

        private static string GetDisplayStatus(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return "Idle";
            }

            string value = raw.Trim();
            string lower = value.ToLowerInvariant();
            if (lower.Contains("battle pull") || lower.Contains("battle proxy"))
            {
                return "Reeling";
            }

            if (lower.Contains("fish on hook") || lower.Contains("hooked"))
            {
                return "Fish hooked";
            }

            if (lower.Contains("fast recast") || lower.Contains("catch resolved"))
            {
                return "Catch secured";
            }

            if (lower.Contains("failed") || lower.Contains("fail") || lower.Contains("lost"))
            {
                return "Fish escaped";
            }

            if (lower.Contains("paused for auto repair"))
            {
                return "Repairing tool";
            }

            if (lower.Contains("recasting") || lower.Contains("idle stall") || lower.Contains("stale"))
            {
                return "Preparing next cast";
            }

            if (lower.Contains("waiting for bite") || lower.Contains("waiting for cast") || lower.Contains("entering fishing"))
            {
                return "Waiting for bite";
            }

            if (lower.Contains("waiting for hook") || lower.Contains("catch resolution") || lower.Contains("battle resolution"))
            {
                return "Resolving catch";
            }

            if (lower.Contains("cast sent"))
            {
                return "Cast deployed";
            }

            if (lower.Contains("no fish shadow"))
            {
                return "Scanning for fish";
            }

            if (lower.Contains("tool check"))
            {
                return "Verifying equipment";
            }

            if (lower.Contains("equip rod"))
            {
                return "Fishing rod required";
            }

            if (lower.StartsWith("error:", StringComparison.OrdinalIgnoreCase))
            {
                return "Attention required";
            }

            if (string.Equals(value, "Battle", StringComparison.OrdinalIgnoreCase))
            {
                return "Reeling";
            }

            if (string.Equals(value, "FishingOnHook", StringComparison.OrdinalIgnoreCase))
            {
                return "Fish hooked";
            }

            if (string.Equals(value, "FishingFail", StringComparison.OrdinalIgnoreCase)
                || string.Equals(value, "BattleFailSlack", StringComparison.OrdinalIgnoreCase))
            {
                return "Fish escaped";
            }

            return value;
        }

        private static string GetDisplayToolStatus(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return "Unknown";
            }

            string value = raw.Trim();
            string lower = value.ToLowerInvariant();
            if (lower.Contains("fishing rod equipped"))
            {
                return "Fishing rod ready";
            }

            if (lower.Contains("holding other") || lower.Contains("not fishing rod") || lower.StartsWith("holding ", StringComparison.OrdinalIgnoreCase))
            {
                return "Fishing rod required";
            }

            if (lower.Contains("no tool"))
            {
                return "No tool equipped";
            }

            if (lower.Contains("player unavailable") || lower.Contains("unavailable") || lower.Contains("exception"))
            {
                return "Equipment unavailable";
            }

            return value;
        }

        private static string GetDisplayTargetStatus(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return "None";
            }

            string value = raw.Trim();
            string lower = value.ToLowerInvariant();
            string distance = TryExtractDistance(value);
            if (lower.Contains("netid=") || lower.Contains("target resolved"))
            {
                return string.IsNullOrEmpty(distance) ? "Target Lock" : "Target Lock (" + distance + ")";
            }

            if (lower.Contains("direct=") || lower.Contains("throw") || lower.Contains("enterfishing"))
            {
                return "Casting to target";
            }

            if (lower.Contains("no active fish shadows") || lower.Contains("not found") || lower.Contains("no fish shadow"))
            {
                return "Scanning for fish";
            }

            if (lower.Contains("range updated"))
            {
                return "Range updated";
            }

            if (lower.Contains("exitfishing") || lower.Contains("cancelfishing") || lower.Contains("reset"))
            {
                return "Resetting session";
            }

            if (lower.Contains("failed fish") || lower.Contains("fishing fail") || lower.Contains("lost fish"))
            {
                return "Fish escaped";
            }

            if (lower.Contains("hooked fish") || lower.Contains("landing fish"))
            {
                return "Fish hooked";
            }

            if (lower.Contains("recent") || lower.Contains("waiting"))
            {
                return "Awaiting update";
            }

            if (lower.Contains("stale") || lower.Contains("proxy"))
            {
                return "Refreshing session";
            }

            if (lower.Contains("fishing rod equipped"))
            {
                return "Ready";
            }

            if (lower.Contains("holding other") || lower.Contains("no tool") || lower.Contains("not fishing rod"))
            {
                return "Fishing rod required";
            }

            if (lower.Contains("unavailable") || lower.Contains("exception"))
            {
                return "Waiting for game state";
            }

            return value;
        }

        private static string TryExtractDistance(string value)
        {
            int start = value.IndexOf("dist=", StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                return string.Empty;
            }

            start += 5;
            int end = value.IndexOf('m', start);
            if (end <= start)
            {
                return string.Empty;
            }

            string distance = value.Substring(start, end - start).Trim();
            return string.IsNullOrWhiteSpace(distance) || string.Equals(distance, "unknown", StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : distance + "m";
        }

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
            nextRodEquipAttemptAt = -999f;
            nextActionAt = -999f;
            instantCatchActiveCached = false;
            skipCatchAnimInvokedForCast = false;
            skipCastWindowUntil = -999f;
            nextSkipCastPollAt = -999f;
            skipCastSpinSeenCount = 0;
            skipCastDoneForCast = false;
            sessionStartedAt = -999f;
            waitingSinceAt = -999f;
            hookedSinceAt = -999f;
            lastHookedStateAt = -999f;
            lastBattleStateAt = -999f;
            lastBattleLostBaitAt = -999f;
            ignoreStaleFishingStateUntil = -999f;
            lastBattleBaitNetId = 0U;
            instantCatchEventActiveUntil = -999f;
            fishBiteEventAt = -999f;
            fishBattleResultAt = -999f;
            fishCatchEventAt = -999f;
            lastLoggedFishBiteAt = -999f;
            lastLoggedFishResultAt = -999f;
            lastCastSentAt = -999f;
            nextWorldReadyLogAt = -999f;
            nextFishingStateLogAt = -999f;
            nextActiveStateLogAt = -999f;
            lastActiveStateLogKey = string.Empty;
            nextTargetMissLogAt = -999f;
            nextPressUpdateAt = -999f;
            consecutiveTargetMisses = 0;
            currentTargetNetId = 0U;
            currentTargetPos = Vector3.zero;
            lastRequestedPressed = null;
            IsInFishingSession = false;
            wasInFishingSession = false;
            LastScanAt = -999f;
            LastInRangeCount = 0;
            lastStatus = enabled ? "Enabled" : "Disabled";
            lastToolStatus = "Unknown";
            lastTargetStatus = enabled ? "Scanning for fish shadows..." : "Idle";

            if (!enabled && host != null)
            {
                try { host.TrySetFishingPressed(false, out _); } catch { }
                RestorePreviousTool(host);
            }

            Log("Toggle changed: " + (enabled ? "enabled" : "disabled") + $" range={fishShadowDetectRange:F0}");
        }

        public static void ToggleEnabled(HeartopiaComplete host = null)
        {
            SetEnabled(!enabled, host);
        }

        public static float DrawSection(HeartopiaComplete host, int startY)
        {
            int num = startY;
            GUIStyle small = new GUIStyle(GUI.skin.label) { fontSize = 12 };
            GUIStyle header = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };

            GUI.Label(new Rect(20f, num, 320f, 22f), host.UI_Localize("Auto Fishing Farm"), header);
            num += 28;

            if (host.UI_DrawPrimaryActionButton(new Rect(20f, num, 260f, 35f), "Auto Equip Rod"))
            {
                host.EquipHandTool(3);
                Log("Auto Equip Rod button pressed.");
            }
            num += 42;

            bool nextEnabled = host.UI_DrawSwitchToggle(new Rect(20f, num, 280f, 25f), enabled, "Auto Fish Shadow Net");
            if (nextEnabled != enabled)
            {
                SetEnabled(nextEnabled, host);
                host.UI_AddMenuNotification(
                    "Auto Fish Shadow Net " + (nextEnabled ? "Enabled" : "Disabled"),
                    nextEnabled ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.55f, 0.55f));
            }
            num += 30;

            bool nextInstant = host.UI_DrawSwitchToggle(new Rect(20f, num, 280f, 25f), instantCatchEnabled, "Instant Catch");
            if (nextInstant != instantCatchEnabled)
            {
                SetInstantCatchEnabled(nextInstant);
                host.UI_AddMenuNotification(
                    "Instant Catch " + (nextInstant ? "Enabled" : "Disabled"),
                    nextInstant ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.55f, 0.55f));
                try { host.UI_SaveKeybinds(false); } catch { }
            }
            num += 30;

            // Send Rate slider intentionally hidden: the NotifyFloatInWater detour handles instant catch
            // at the source, so the timed resend stays at 0 Hz (init default). Kept in code as a fallback.

            bool nextSkipAnim = host.UI_DrawSwitchToggle(new Rect(20f, num, 280f, 25f), skipCatchAnimEnabled, "Skip Catch Animation");
            if (nextSkipAnim != skipCatchAnimEnabled)
            {
                SetSkipCatchAnimEnabled(nextSkipAnim);
                host.UI_AddMenuNotification(
                    "Skip Catch Animation " + (nextSkipAnim ? "Enabled" : "Disabled"),
                    nextSkipAnim ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.55f, 0.55f));
                try { host.UI_SaveKeybinds(false); } catch { }
            }
            num += 30;

            bool nextSkipCast = host.UI_DrawSwitchToggle(new Rect(20f, num, 280f, 25f), skipCastAnimEnabled, "Skip Cast Animation");
            if (nextSkipCast != skipCastAnimEnabled)
            {
                SetSkipCastAnimEnabled(nextSkipCast);
                host.UI_AddMenuNotification(
                    "Skip Cast Animation " + (nextSkipCast ? "Enabled" : "Disabled"),
                    nextSkipCast ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.55f, 0.55f));
                try { host.UI_SaveKeybinds(false); } catch { }
            }
            num += 30;

            bool nextSkipBait = host.UI_DrawSwitchToggle(new Rect(20f, num, 280f, 25f), skipBaitAnimEnabled, "Skip Bait Animation");
            if (nextSkipBait != skipBaitAnimEnabled)
            {
                SetSkipBaitAnimEnabled(nextSkipBait);
                host.UI_AddMenuNotification(
                    "Skip Bait Animation " + (nextSkipBait ? "Enabled" : "Disabled"),
                    nextSkipBait ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.55f, 0.55f));
                try { host.UI_SaveKeybinds(false); } catch { }
            }
            num += 30;

            GUI.Label(new Rect(20f, num, 360f, 20f), host.UI_LocalizeFormat("Status: {0}", GetLastStatus()), small);
            num += 24;
            GUI.Label(new Rect(20f, num, 360f, 20f), host.UI_LocalizeFormat("Tool: {0}", GetLastToolStatus()), small);
            num += 24;
            GUI.Label(new Rect(20f, num, 360f, 36f), host.UI_LocalizeFormat("Target: {0}", GetLastTargetStatus()), small);
            num += 40;

            GUI.Label(new Rect(20f, num, 320f, 20f), host.UI_LocalizeFormat("Scan Range: {0:F0}m", fishShadowDetectRange), small);
            num += 22;
            float prevRange = fishShadowDetectRange;
            fishShadowDetectRange = Mathf.Round(host.UI_DrawAccentSlider(new Rect(20f, num, 260f, 20f), fishShadowDetectRange, 1f, 200f));
            if (Math.Abs(fishShadowDetectRange - prevRange) > 0.0001f)
            {
                lastTargetStatus = "Range updated";
                Log("Detect range changed to " + fishShadowDetectRange.ToString("F0") + "m");
                try { host.UI_SaveKeybinds(false); } catch { }
            }
            num += 30;

            // --- Auto Bait ---
            bool nextAutoBait = host.UI_DrawSwitchToggle(new Rect(20f, num, 280f, 25f), autoBaitEnabled, "Auto Bait");
            if (nextAutoBait != autoBaitEnabled)
            {
                SetAutoBaitEnabled(nextAutoBait);
                host.UI_AddMenuNotification(
                    "Auto Bait " + (nextAutoBait ? "Enabled" : "Disabled"),
                    nextAutoBait ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.55f, 0.55f));
                try { host.UI_SaveKeybinds(false); } catch { }
            }
            num += 30;

            if (autoBaitEnabled)
            {
                string choiceLabel = autoBaitChoice == AutoBaitChoice.Bait
                    ? host.UI_Localize("Item: Bait")
                    : host.UI_Localize("Item: Attractor");
                if (host.UI_DrawSecondaryActionButton(new Rect(20f, num, 260f, 30f), choiceLabel))
                {
                    SetAutoBaitChoice(autoBaitChoice == AutoBaitChoice.Bait ? (int)AutoBaitChoice.Attractor : (int)AutoBaitChoice.Bait);
                    Log("Auto Bait item changed to " + autoBaitChoice);
                    try { host.UI_SaveKeybinds(false); } catch { }
                }
                num += 36;

                GUI.Label(new Rect(20f, num, 320f, 20f), host.UI_LocalizeFormat("Max: {0}", autoBaitMaxCount), small);
                num += 22;
                int prevMax = autoBaitMaxCount;
                int newMax = Mathf.RoundToInt(host.UI_DrawAccentSlider(new Rect(20f, num, 260f, 20f), autoBaitMaxCount, 0f, 50f));
                if (newMax != prevMax)
                {
                    SetAutoBaitMaxCount(newMax);
                    try { host.UI_SaveKeybinds(false); } catch { }
                }
                num += 30;

                GUI.Label(new Rect(20f, num, 320f, 20f), host.UI_LocalizeFormat("No-fish: {0:F0}s", autoBaitNoFishSeconds), small);
                num += 22;
                float prevSec = autoBaitNoFishSeconds;
                autoBaitNoFishSeconds = Mathf.Round(host.UI_DrawAccentSlider(new Rect(20f, num, 260f, 20f), autoBaitNoFishSeconds, AutoBaitNoFishSecondsMin, AutoBaitNoFishSecondsMax));
                if (Math.Abs(autoBaitNoFishSeconds - prevSec) > 0.0001f)
                {
                    try { host.UI_SaveKeybinds(false); } catch { }
                }
                num += 30;

                GUI.Label(new Rect(20f, num, 180f, 24f), host.UI_LocalizeFormat("Remaining: {0}/{1}", autoBaitRemaining, autoBaitMaxCount), small);
                if (host.UI_DrawSecondaryActionButton(new Rect(190f, num - 3f, 90f, 28f), host.UI_Localize("Reset")))
                {
                    ResetAutoBaitCounter();
                }
                num += 32;
            }

            num = Mathf.CeilToInt(FishingRouteFeature.DrawSection(host, num + 10f));

            return num + 20f;
        }

        public static void Update(HeartopiaComplete host)
        {
            if (!enabled || host == null)
            {
                return;
            }

            float now = Time.unscaledTime;
            try
            {
                // Register the fishing event hooks while fishing automation is active (engine installs
                // the detours lazily). They give exact bite/result/catch transitions; see below.
                EnsureFishingEventHooks(host);

                // Install the NotifyFloatInWater detour once (lazy; before any cast) so the game's own
                // successLength send carries -2 at the source. Idempotent / tried-guarded.
                if (instantCatchEnabled)
                {
                    host.EnsureNotifyFloatInWaterHook();
                }

                // Event-precise per-cast diagnostics (bite/result) — fire exactly when the game
                // dispatches CmdOnFishBait / CmdFishBattleResult, independent of the polled state.
                if (instantCatchEnabled)
                {
                    if (fishBiteEventAt > lastLoggedFishBiteAt)
                    {
                        lastLoggedFishBiteAt = fishBiteEventAt;
                        float dtBiteCast = host.InstantCatchCastAt > 0f ? fishBiteEventAt - host.InstantCatchCastAt : -1f;
                        host.InstantCatchDiag("cast#" + instantCatchCastSeq + " EVENT-BITE shadow=" + fishBiteShadowNetId
                            + " after " + dtBiteCast.ToString("F2") + "s from cast");
                    }
                    if (fishBattleResultAt > lastLoggedFishResultAt)
                    {
                        lastLoggedFishResultAt = fishBattleResultAt;
                        float dtResCast = host.InstantCatchCastAt > 0f ? fishBattleResultAt - host.InstantCatchCastAt : -1f;
                        float dtResBite = fishBiteEventAt > 0f ? fishBattleResultAt - fishBiteEventAt : -1f;
                        host.InstantCatchDiag("cast#" + instantCatchCastSeq + " EVENT-RESULT success=" + fishBattleResultSuccess
                            + " fishId=" + fishBattleResultFishId + " reason=" + FailReasonName(fishBattleResultFailReason)
                            + " after " + dtResCast.ToString("F2") + "s from cast, "
                            + dtResBite.ToString("F2") + "s from bite");
                    }
                }

                // High-frequency buoy resend — runs EVERY frame (not gated by nextActionAt), so the
                // collapsed successLength is refreshed within ~1 frame of the bite/buoy-activation.
                // Uses the active flag cached by the decision loop; harmlessly no-ops if not fishing.
                // Skip Cast Animation polling — must run OUTSIDE the nextActionAt gate (the window
                // opens 0.1s after cast, while the decision loop may be sleeping). Waits for the
                // animator to be in SpinningRod on two consecutive polls, then crossfades to Fishing
                // so the throw clip finishes naturally and ActivateRodBuoy goes out immediately.
                if (skipCastAnimEnabled && !skipCastDoneForCast && now < skipCastWindowUntil && now >= nextSkipCastPollAt)
                {
                    nextSkipCastPollAt = now + SkipCastPollInterval;
                    if (host.TrySkipFishingCastAnimMono(skipCastSpinSeenCount >= 1, out bool inSpinningRod, out string skipStatus))
                    {
                        skipCastDoneForCast = true;
                        Log("Skip cast animation: crossfade to Fishing fired (t=" + (now - lastCastSentAt).ToString("F2") + "s)");
                    }
                    else if (inSpinningRod)
                    {
                        skipCastSpinSeenCount++;
                    }
                    else if (now + SkipCastPollInterval >= skipCastWindowUntil)
                    {
                        Log("Skip cast animation window expired: " + skipStatus);
                    }
                }

                // Send Rate 0 = disable our timed buoy resend entirely (the NotifyFloatInWater detour
                // already rewrites successLength to -2 at source). Avoids a divide-by-zero too.
                if (instantCatchEnabled && (instantCatchActiveCached || now < instantCatchEventActiveUntil) && instantCatchSendHz > 0f && now >= nextHighFreqInstantAt)
                {
                    nextHighFreqInstantAt = now + (1f / instantCatchSendHz);
                    if (host.TryArmFishingInstantCatch(out _))
                    {
                        instantCatchSendCount++;
                    }
                }

                if (now < nextActionAt)
                {
                    return;
                }

                if (!host.IsFishingAutomationWorldReady())
                {
                    lastStatus = "Waiting for world";
                    lastToolStatus = "Unavailable";
                    lastTargetStatus = "Join the world first";
                    nextActionAt = now + 1.5f;
                    if (now >= nextWorldReadyLogAt)
                    {
                        nextWorldReadyLogAt = now + 5f;
                        Log("World not ready yet; waiting for local player session.");
                    }
                    return;
                }

                nextWorldReadyLogAt = -999f;
                bool hasFishingState = host.TryGetFishingAutomationState(out bool inFishingState, out string fishState, out bool pressed, out float pullStrength, out float rodDurability, out uint baitingFishNetId, out string fishingStateStatus);
                if (!hasFishingState)
                {
                    if (sessionStartedAt > 0f && now - sessionStartedAt < 15f)
                    {
                        lastStatus = "Waiting for fishing state";
                        lastTargetStatus = fishingStateStatus;
                        nextActionAt = now + 1f;
                        if (now >= nextFishingStateLogAt)
                        {
                            nextFishingStateLogAt = now + 5f;
                            Log("Fishing state unavailable during active session. status=" + fishingStateStatus);
                        }
                        return;
                    }

                    if (now >= nextFishingStateLogAt)
                    {
                        nextFishingStateLogAt = now + 5f;
                        Log("Fishing state unavailable before cast. Continuing with net-based setup. status=" + fishingStateStatus);
                    }
                }
                else
                {
                    nextFishingStateLogAt = -999f;
                }

                if (hasFishingState)
                {
                    string activeStateKey = inFishingState + "|" + fishState + "|" + baitingFishNetId;
                    if (!string.Equals(lastActiveStateLogKey, activeStateKey, StringComparison.Ordinal) || now >= nextActiveStateLogAt)
                    {
                        lastActiveStateLogKey = activeStateKey;
                        nextActiveStateLogAt = now + 5f;
                        Log($"Fishing state: inFishing={inFishingState} state={fishState} pressed={pressed} pull={pullStrength:F2} tension={rodDurability:F2} baitNetId={baitingFishNetId}");
                    }
                }

                bool staleIdleFishingReport =
                    hasFishingState
                    && inFishingState
                    && string.Equals(fishState, "Idle", StringComparison.OrdinalIgnoreCase)
                    && baitingFishNetId != 0U
                    && pullStrength > 0.05f
                    && rodDurability <= 0.05f;

                if (staleIdleFishingReport && now < ignoreStaleFishingStateUntil)
                {
                    hasFishingState = false;
                    inFishingState = false;
                    lastStatus = "Ignoring stale fishing state";
                    lastTargetStatus = $"Stale bait netId={baitingFishNetId}; scanning for next shadow";
                }

                bool inSessionNow = hasFishingState && inFishingState;
                // Falling edge = a cast cycle just ended (catch OR срыв). The rod was just used, so
                // check its durability NOW: with animations off + fast catches the game's HandHold
                // durability event and the timed poll lag behind the wear, and the rod snaps between
                // checks (cast then instantly fails). Direct kit throw (no idle slot) repairs mid-fishing.
                if (wasInFishingSession && !inSessionNow)
                {
                    host.RequestDurabilityCheck();
                }
                wasInFishingSession = inSessionNow;

                IsInFishingSession = inSessionNow;
                if (hasFishingState && inFishingState)
                {
                    if (sessionStartedAt <= 0f)
                    {
                        sessionStartedAt = now;
                    }

                    // --- Per-cast diagnostics: bite + result timing (one line each per cast) ---
                    if (instantCatchEnabled)
                    {
                        // Real bites show state Waiting/Battle; a stale baitingFishNetId in Idle (left
                        // over from a previous session after a toggle) is NOT a bite — exclude it.
                        bool isBattle = string.Equals(fishState, "Battle", StringComparison.OrdinalIgnoreCase)
                            || (baitingFishNetId != 0U && string.Equals(fishState, "Waiting", StringComparison.OrdinalIgnoreCase));
                        if (!instantCatchBiteLogged && isBattle)
                        {
                            instantCatchBiteLogged = true;
                            instantCatchBiteAt = now;
                            float dt = host.InstantCatchCastAt > 0f ? now - host.InstantCatchCastAt : -1f;
                            host.InstantCatchDiag("cast#" + instantCatchCastSeq + " BITE after " + dt.ToString("F2")
                                + "s state=" + fishState + " baitNetId=" + baitingFishNetId);
                        }

                        bool isResult = string.Equals(fishState, "FishingOnHook", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(fishState, "BattleFailSlack", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(fishState, "FishingFail", StringComparison.OrdinalIgnoreCase);
                        if (!instantCatchResultLogged && isResult)
                        {
                            instantCatchResultLogged = true;
                            float dtCast = host.InstantCatchCastAt > 0f ? now - host.InstantCatchCastAt : -1f;
                            float dtBite = instantCatchBiteAt > 0f ? now - instantCatchBiteAt : -1f;
                            float effHz = dtCast > 0.01f ? instantCatchSendCount / dtCast : 0f;
                            host.InstantCatchDiag("cast#" + instantCatchCastSeq + " RESULT=" + fishState
                                + " after " + dtCast.ToString("F2") + "s from cast, "
                                + dtBite.ToString("F2") + "s from bite"
                                + " | sends=" + instantCatchSendCount + " (~" + effHz.ToString("F0") + " Hz)");
                        }
                    }

                    // Skip Catch Animation: on the first tick of a terminal state (catch take-in or the
                    // fail-slack timers), fire the game's own instant reset once. Local-only; the server
                    // already concluded the session, and outside the Fishing FSM state it's a no-op.
                    if (skipCatchAnimEnabled && !skipCatchAnimInvokedForCast
                        && (string.Equals(fishState, "FishingOnHook", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(fishState, "BattleFailSlack", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(fishState, "FishingFail", StringComparison.OrdinalIgnoreCase)))
                    {
                        skipCatchAnimInvokedForCast = true;
                        if (host.TryInvokeFishingTakeUpRodMono(out string takeUpStatus))
                        {
                            Log("Skip catch animation: reset fired at state=" + fishState);
                        }
                        else
                        {
                            Log("Skip catch animation failed: " + takeUpStatus);
                        }
                    }

                    // Instant Catch: echo the buoy's real position with a collapsed successLength so the
                    // battle resolves immediately once the fish bites. The buoy is never moved. The
                    // actual sends are driven every frame by the high-frequency path above; here we just
                    // cache the "active" flag it uses and emit the throttled status/diag line.
                    bool instantActive = instantCatchEnabled
                        && (string.Equals(fishState, "Waiting", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(fishState, "Idle", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(fishState, "Battle", StringComparison.OrdinalIgnoreCase));
                    instantCatchActiveCached = instantActive;
                    if (instantActive && now >= nextInstantCatchAt)
                    {
                        nextInstantCatchAt = now + InstantCatchInterval;

                        string buoyStatus = "n/a";
                        if (host.TryArmFishingInstantCatch(out buoyStatus))
                        {
                            lastTargetStatus = "Instant catch: " + buoyStatus;
                        }

                        Log("Instant catch tick state=" + fishState
                            + " buoy=" + buoyStatus + " baitNetId=" + baitingFishNetId);
                    }

                    bool looksLikeBattleProxy =
                        string.Equals(fishState, "Idle", StringComparison.OrdinalIgnoreCase)
                        && baitingFishNetId != 0U
                        && pullStrength > 0.05f
                        && pullStrength <= 1.05f
                        && rodDurability > 0.05f;

                    if (string.Equals(fishState, "BattleFailSlack", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(fishState, "FishingFail", StringComparison.OrdinalIgnoreCase))
                    {
                        if ((pressed || lastRequestedPressed != false) && now >= nextPressUpdateAt)
                        {
                            if (host.TrySetFishingPressed(false, out string failReleaseStatus))
                            {
                                lastRequestedPressed = false;
                                Log("Fail state release updated pressed=False status=" + failReleaseStatus);
                            }

                            nextPressUpdateAt = now + BattlePressCooldown;
                        }

                        lastStatus = $"Fishing failed state {fishState}; released line";
                        lastTargetStatus = baitingFishNetId != 0U ? $"Failed fish netId={baitingFishNetId}" : "Fishing fail";
                        return;
                    }

                    if (string.Equals(fishState, "Battle", StringComparison.OrdinalIgnoreCase) || looksLikeBattleProxy)
                    {
                        waitingSinceAt = -999f;
                        hookedSinceAt = -999f;
                        bool lostBattleBait =
                            !looksLikeBattleProxy
                            && baitingFishNetId == 0U
                            && lastBattleBaitNetId != 0U
                            && lastBattleStateAt > 0f
                            && now - lastBattleStateAt <= PostBattleIdleGraceSeconds;

                        lastBattleStateAt = now;
                        if (baitingFishNetId != 0U)
                        {
                            lastBattleBaitNetId = baitingFishNetId;
                        }
                        else if (lostBattleBait)
                        {
                            lastBattleLostBaitAt = now;
                        }

                        bool desiredPressed = true;
                        bool tensionReadable = rodDurability >= 0f;
                        bool tensionAboutToBreak = tensionReadable && rodDurability <= TensionEmergencyReleaseThreshold;
                        bool recoveringFromBreakZone = lastRequestedPressed == false
                            && tensionReadable
                            && rodDurability < TensionResumePullThreshold;

                        // The visible red line follows durability, not PullStrength.
                        // Keep pulling through a full pull bar unless the line is actually near break.
                        if (lostBattleBait || tensionAboutToBreak || recoveringFromBreakZone)
                        {
                            desiredPressed = false;
                        }

                        bool shouldSendPressUpdate =
                            lastRequestedPressed == null
                            || lastRequestedPressed.Value != desiredPressed
                            || pressed != desiredPressed;
                        bool requestedPressChanged =
                            lastRequestedPressed == null
                            || lastRequestedPressed.Value != desiredPressed;

                        bool canSendPressUpdate = lostBattleBait || now >= nextPressUpdateAt;
                        if (shouldSendPressUpdate && canSendPressUpdate)
                        {
                            if (host.TrySetFishingPressed(desiredPressed, out string pressedStatus))
                            {
                                lastRequestedPressed = desiredPressed;
                                nextPressUpdateAt = now + BattlePressCooldown;
                                if (requestedPressChanged || lostBattleBait)
                                {
                                    Log("Battle control updated pressed=" + desiredPressed + " status=" + pressedStatus);
                                }
                            }
                            else
                            {
                                nextPressUpdateAt = now + BattlePressCooldown;
                                Log("Battle control failed to update pressed=" + desiredPressed + " status=" + pressedStatus);
                            }
                        }

                        string controlReason = desiredPressed
                            ? "pulling"
                            : (lostBattleBait ? "lost bait; released" : "saving line");
                        lastStatus = looksLikeBattleProxy
                            ? $"Battle proxy pull {pullStrength:F2} tension {rodDurability:F2} {controlReason}"
                            : $"Battle pull {pullStrength:F2} tension {rodDurability:F2} {controlReason}";
                        lastTargetStatus = baitingFishNetId != 0U
                            ? $"Hooked fish netId={baitingFishNetId}"
                            : (lostBattleBait
                                ? $"Battle lost fish netId={lastBattleBaitNetId}; waiting for game resolution"
                                : (looksLikeBattleProxy ? "Hooked fish awaiting state sync" : "Battle in progress"));
                        return;
                    }

                    if ((pressed || lastRequestedPressed != false) && now >= nextPressUpdateAt)
                    {
                        if (host.TrySetFishingPressed(false, out _))
                        {
                            lastRequestedPressed = false;
                        }
                        nextPressUpdateAt = now + 0.15f;
                    }

                    if (string.Equals(fishState, "FishingOnHook", StringComparison.OrdinalIgnoreCase))
                    {
                        waitingSinceAt = -999f;
                        if (hookedSinceAt <= 0f)
                        {
                            hookedSinceAt = now;
                        }
                        lastHookedStateAt = now;

                        lastStatus = "Fish on hook";
                        lastTargetStatus = baitingFishNetId != 0U
                            ? $"Landing fish netId={baitingFishNetId}"
                            : "Landing fish";

                        if (now - hookedSinceAt >= PostHookIdleGraceSeconds)
                        {
                            lastStatus = "Waiting for hook resolution";
                            lastTargetStatus = "FishingOnHook persisted; not canceling";
                            nextActionAt = now + 0.2f;
                        }
                        return;
                    }

                    hookedSinceAt = -999f;

                    if (string.Equals(fishState, "Waiting", StringComparison.OrdinalIgnoreCase))
                    {
                        if ((pressed || lastRequestedPressed != false) && now >= nextPressUpdateAt)
                        {
                            if (host.TrySetFishingPressed(false, out string releaseStatus))
                            {
                                lastRequestedPressed = false;
                                Log("Waiting release updated status=" + releaseStatus);
                            }

                            nextPressUpdateAt = now + 0.15f;
                        }

                        // Give Auto Repair its execution window: the game ignores the ToolRestorer
                        // use while the player is casting/fishing, so a repair triggered mid-session
                        // burns all its use-retries against a busy player and dies — the rod then
                        // drains to 0 before the depleted toast finally repairs it (casts blocked =
                        // player idle). Exiting during Waiting only sacrifices the current cast;
                        // Battle/Hook states resolve naturally and the pre-cast hold takes over.
                        // Use-phase only: once the kit is consumed the restore aura repairs the rod
                        // passively and fishing continues through it.
                        if (host.IsAutoRepairUsePhase())
                        {
                            if (host.TryExitFishing(out string repairExitStatus))
                            {
                                lastStatus = "Paused for Auto Repair";
                                lastTargetStatus = repairExitStatus;
                                sessionStartedAt = -999f;
                                waitingSinceAt = -999f;
                                nextActionAt = now + 0.5f;
                                Log("Waiting cast released for auto repair. status=" + repairExitStatus);
                                return;
                            }
                        }

                        if (waitingSinceAt <= 0f)
                        {
                            waitingSinceAt = now;
                        }

                        lastStatus = "Waiting for bite";
                        if (now - waitingSinceAt >= 12f)
                        {
                            if (host.TryExitFishing(out string exitStatus))
                            {
                                lastStatus = "Recasting after timeout";
                                lastTargetStatus = exitStatus;
                                waitingSinceAt = -999f;
                                sessionStartedAt = -999f;
                                nextActionAt = now + FastRecastDelay;
                                Log("Waiting timeout reached; exit fishing invoked. status=" + exitStatus);
                            }
                            else
                            {
                                Log("Waiting timeout reached; exit fishing failed. status=" + exitStatus);
                            }
                        }
                        return;
                    }

                    if (string.Equals(fishState, "FishingFail", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(fishState, "BattleFailSlack", StringComparison.OrdinalIgnoreCase))
                    {
                        if ((pressed || lastRequestedPressed != false) && now >= nextPressUpdateAt)
                        {
                            if (host.TrySetFishingPressed(false, out string failReleaseStatus))
                            {
                                lastRequestedPressed = false;
                                Log("Fail-state release updated status=" + failReleaseStatus);
                            }

                            nextPressUpdateAt = now + 0.15f;
                        }

                        lastStatus = string.Equals(fishState, "BattleFailSlack", StringComparison.OrdinalIgnoreCase)
                            ? "Fish battle failed"
                            : "Fishing failed";
                        lastTargetStatus = "Waiting for fishing state reset";
                        return;
                    }

                    if (string.Equals(fishState, "Idle", StringComparison.OrdinalIgnoreCase))
                    {
                        float activeFor = now - sessionStartedAt;
                        bool hasCastTension = pullStrength > 0.05f;
                        bool hasAttachedFishProxy = baitingFishNetId != 0U;
                        bool staleFinishedProxy = hasAttachedFishProxy && hasCastTension && rodDurability <= 0.05f;
                        bool looksLikePostCastWaitingProxy = hasCastTension || hasAttachedFishProxy;
                        bool recentlySawHook = lastHookedStateAt > 0f && now - lastHookedStateAt <= PostHookIdleGraceSeconds;
                        bool recentlySawBattle = lastBattleStateAt > 0f && now - lastBattleStateAt <= PostBattleIdleGraceSeconds;
                        bool recentlyLostBattleBait = lastBattleLostBaitAt > 0f && now - lastBattleLostBaitAt <= PostLostBattleIdleGraceSeconds;
                        bool recentlySentCast = lastCastSentAt > 0f && now - lastCastSentAt <= PostCastIdleGraceSeconds;
                        bool looksLikeCatchResolvedIdle =
                            recentlySawHook
                            && !pressed
                            && baitingFishNetId == 0U
                            && pullStrength <= 0.05f
                            && rodDurability <= 0.05f;

                        if (staleFinishedProxy && activeFor >= StaleIdleGraceSeconds)
                        {
                            if (host.TryExitFishing(out string staleProxyExitStatus))
                            {
                                lastStatus = "Recasting after stale finished proxy";
                                lastTargetStatus = staleProxyExitStatus;
                                sessionStartedAt = -999f;
                                waitingSinceAt = -999f;
                                lastCastSentAt = -999f;
                                lastBattleStateAt = -999f;
                                lastBattleLostBaitAt = -999f;
                                lastBattleBaitNetId = 0U;
                                ignoreStaleFishingStateUntil = now + 3f;
                                nextActionAt = now + FastRecastDelay;
                                Log("Stale idle proxy cleared; exit fishing invoked. status=" + staleProxyExitStatus);
                            }
                            else
                            {
                                lastStatus = "Stale idle proxy";
                                lastTargetStatus = staleProxyExitStatus;
                                ignoreStaleFishingStateUntil = now + 1.5f;
                                nextActionAt = now + 0.5f;
                                Log("Stale idle proxy exit failed. status=" + staleProxyExitStatus);
                            }
                            return;
                        }

                        if (looksLikeCatchResolvedIdle)
                        {
                            if (host.TryExitFishing(out string catchExitStatus))
                            {
                                lastStatus = "Fast recast after catch";
                                lastTargetStatus = catchExitStatus;
                                sessionStartedAt = -999f;
                                waitingSinceAt = -999f;
                                hookedSinceAt = -999f;
                                lastHookedStateAt = -999f;
                                lastCastSentAt = -999f;
                                lastBattleStateAt = -999f;
                                lastBattleLostBaitAt = -999f;
                                lastBattleBaitNetId = 0U;
                                nextActionAt = now + FastRecastDelay;
                                Log("Catch resolved; fast exit fishing invoked. status=" + catchExitStatus);
                            }
                            else
                            {
                                lastStatus = "Catch resolved";
                                lastTargetStatus = catchExitStatus;
                                nextActionAt = now + 0.2f;
                                Log("Catch resolved; fast exit fishing failed. status=" + catchExitStatus);
                            }
                            return;
                        }

                        if (recentlySawHook)
                        {
                            if ((pressed || lastRequestedPressed != false) && now >= nextPressUpdateAt)
                            {
                                if (host.TrySetFishingPressed(false, out string postHookReleaseStatus))
                                {
                                    lastRequestedPressed = false;
                                    Log("Post-hook idle release updated status=" + postHookReleaseStatus);
                                }

                                nextPressUpdateAt = now + 0.15f;
                            }

                            lastStatus = "Waiting for catch resolution";
                            lastTargetStatus = "Recent FishingOnHook; not canceling idle state";
                            nextActionAt = now + FastRecastDelay;
                            return;
                        }

                        if (recentlySawBattle || recentlyLostBattleBait)
                        {
                            if ((pressed || lastRequestedPressed != false) && now >= nextPressUpdateAt)
                            {
                                if (host.TrySetFishingPressed(false, out string postBattleReleaseStatus))
                                {
                                    lastRequestedPressed = false;
                                    Log("Post-battle idle release updated status=" + postBattleReleaseStatus);
                                }

                                nextPressUpdateAt = now + 0.15f;
                            }

                            lastStatus = "Waiting for battle resolution";
                            lastTargetStatus = recentlyLostBattleBait && lastBattleBaitNetId != 0U
                                ? $"Battle lost fish netId={lastBattleBaitNetId}; not canceling idle state"
                                : lastBattleBaitNetId != 0U
                                ? $"Recent battle netId={lastBattleBaitNetId}; not canceling idle state"
                                : "Recent battle; not canceling idle state";
                            nextActionAt = now + FastRecastDelay;
                            return;
                        }

                        if (looksLikePostCastWaitingProxy)
                        {
                            if (waitingSinceAt <= 0f)
                            {
                                waitingSinceAt = now;
                            }

                            bool looksLikePreBattleHookProxy = hasAttachedFishProxy && pullStrength > 1.05f;
                            lastStatus = looksLikePreBattleHookProxy
                                ? $"Hooked waiting proxy netId={baitingFishNetId} pull {pullStrength:F2}"
                                : (hasAttachedFishProxy
                                    ? $"Fish attached proxy netId={baitingFishNetId} pull {pullStrength:F2}"
                                    : $"Waiting for bite proxy pull {pullStrength:F2}");
                            lastTargetStatus = looksLikePreBattleHookProxy
                                ? "Fish is on the bait; waiting for battle state"
                                : (hasAttachedFishProxy
                                    ? "Fish shadow attached; waiting for battle state"
                                    : "Cast is active; waiting for bite state");
                            if (now - waitingSinceAt >= 12f)
                            {
                                if (host.TryExitFishing(out string proxyExitStatus))
                                {
                                    lastStatus = "Recasting after proxy timeout";
                                    lastTargetStatus = proxyExitStatus;
                                    waitingSinceAt = -999f;
                                    sessionStartedAt = -999f;
                                    nextActionAt = now + FastRecastDelay;
                                    Log("Proxy waiting timeout reached; exit fishing invoked. status=" + proxyExitStatus);
                                }
                                else
                                {
                                    Log("Proxy waiting timeout reached; exit fishing failed. status=" + proxyExitStatus);
                                }
                            }
                            return;
                        }

                        waitingSinceAt = -999f;
                        if (recentlySentCast)
                        {
                            lastStatus = "Waiting for cast to settle";
                            lastTargetStatus = "Recent cast; waiting for Waiting/Battle state";
                            nextActionAt = now + AfterCastPollDelay;
                            return;
                        }

                        if (activeFor < StaleIdleGraceSeconds)
                        {
                            lastStatus = "Entering fishing";
                            lastTargetStatus = "Waiting for fishing state to advance";
                            return;
                        }

                        if (host.TryExitFishing(out string exitStatus))
                        {
                            lastStatus = "Recasting after idle stall";
                            lastTargetStatus = exitStatus;
                                sessionStartedAt = -999f;
                                waitingSinceAt = -999f;
                                lastCastSentAt = -999f;
                                lastBattleStateAt = -999f;
                                lastBattleLostBaitAt = -999f;
                                lastBattleBaitNetId = 0U;
                                nextActionAt = now + FastRecastDelay;
                            Log("Fishing stayed idle too long; exit fishing invoked. status=" + exitStatus);
                        }
                        else
                        {
                            lastStatus = "Idle fishing stall";
                            lastTargetStatus = exitStatus;
                            nextActionAt = now + 1f;
                            Log("Fishing stayed idle too long; exit fishing failed. status=" + exitStatus);
                        }
                        return;
                    }

                    lastStatus = string.IsNullOrWhiteSpace(fishState) ? "Fishing active" : fishState;
                    return;
                }

                instantCatchActiveCached = false;
                waitingSinceAt = -999f;
                hookedSinceAt = -999f;
                lastHookedStateAt = -999f;
                lastBattleStateAt = -999f;
                lastBattleLostBaitAt = -999f;
                lastBattleBaitNetId = 0U;
                if (hasFishingState)
                {
                    sessionStartedAt = -999f;
                    nextActiveStateLogAt = -999f;
                    lastActiveStateLogKey = string.Empty;
                }
                if (lastRequestedPressed != false && host.TrySetFishingPressed(false, out _))
                {
                    lastRequestedPressed = false;
                }

                // Hold new casts only while the repair kit USE is active/queued — that idle
                // window is what lets the kit actually be consumed. Once the restorer lands,
                // its aura repairs the rod passively and casting resumes immediately (fishing
                // does not move the player, so they stay inside the aura).
                if (host.IsAutoRepairUsePhase())
                {
                    lastStatus = "Paused for Auto Repair";
                    lastTargetStatus = "Waiting for repair kit use";
                    nextActionAt = now + 0.5f;
                    return;
                }

                if (!host.TryGetFishingRodToolStatus(out bool rodEquipped, out string toolStatus))
                {
                    lastToolStatus = toolStatus;
                    lastStatus = "Tool check unavailable";
                    lastTargetStatus = toolStatus;
                    nextActionAt = now + 1f;
                    Log("Rod tool status unavailable: " + toolStatus);
                    return;
                }

                lastToolStatus = toolStatus;
                if (!rodEquipped)
                {
                    EnsureFishingRodEquipped(host);
                    lastTargetStatus = toolStatus;
                    nextActionAt = now + 1f;
                    return;
                }

                nextRodEquipAttemptAt = -999f;

                bool foundTarget = host.TryFindNearestFishShadowTarget(fishShadowDetectRange, out uint targetNetId, out Vector3 targetPos, out float targetDistance, out int detectedCount, out int inRangeCount, out string targetStatus);
                LastScanAt = now;
                LastInRangeCount = inRangeCount;

                // Auto Bait: track how long the radius has been fish-free and throw a bait/attractor
                // once that exceeds the configured window. Runs every scan regardless of hit/miss.
                TryAutoBaitTick(host, now, inRangeCount);

                if (!foundTarget)
                {
                    consecutiveTargetMisses++;
                    float missDelay = Mathf.Min(EmptyScanMaxDelay, EmptyScanMinDelay + (consecutiveTargetMisses * 0.12f));
                    lastStatus = "No fish shadow target";
                    lastTargetStatus = targetStatus;
                    nextActionAt = now + missDelay;
                    if (now >= nextTargetMissLogAt)
                    {
                        nextTargetMissLogAt = now + EmptyScanMissLogInterval;
                        Log("Fish shadow target not found. status=" + targetStatus + $" nextScan={missDelay:F1}s misses={consecutiveTargetMisses}");
                    }
                    return;
                }

                consecutiveTargetMisses = 0;
                currentTargetNetId = targetNetId;
                currentTargetPos = targetPos;
                lastTargetStatus = $"netId={targetNetId} dist={(targetDistance > 0f ? targetDistance.ToString("F1") : "unknown")}m found={detectedCount}";
                Log("Fish shadow target resolved: " + lastTargetStatus + " pos=" + targetPos);

                if (host.TryEnterFishingAtTarget(targetPos, out string enterStatus))
                {
                    lastStatus = "Cast sent to fish shadow";
                    lastTargetStatus = $"netId={targetNetId} dist={(targetDistance > 0f ? targetDistance.ToString("F1") : "unknown")}m";
                    sessionStartedAt = now;
                    waitingSinceAt = now;
                    hookedSinceAt = -999f;
                    lastHookedStateAt = -999f;
                    lastBattleStateAt = -999f;
                    lastBattleLostBaitAt = -999f;
                    lastBattleBaitNetId = 0U;
                    lastCastSentAt = now;
                    lastRequestedPressed = false;
                    nextPressUpdateAt = now + 0.15f;
                    nextActionAt = now + AfterCastPollDelay;

                    // Per-cast diagnostics: start a new cast window.
                    instantCatchCastSeq++;
                    // Remember the fish object this cast targeted (for post-catch ghost skip).
                    castTargetInstanceId = host.GetLastFishShadowTargetInstanceId();
                    instantCatchBiteLogged = false;
                    instantCatchResultLogged = false;
                    skipCatchAnimInvokedForCast = false;
                    // Skip Cast Animation: open the animator poll window (the clip starts on the next
                    // FSM tick, so the first poll waits a beat).
                    skipCastWindowUntil = skipCastAnimEnabled ? now + SkipCastWindowSeconds : -999f;
                    nextSkipCastPollAt = now + SkipCastPollStartDelay;
                    skipCastSpinSeenCount = 0;
                    skipCastDoneForCast = false;
                    instantCatchBiteAt = -1f;
                    host.InstantCatchCastSeq = instantCatchCastSeq;
                    host.InstantCatchCastAt = now;
                    // Start the high-frequency buoy resend immediately so -2 is established before the bite.
                    instantCatchActiveCached = instantCatchEnabled;
                    nextHighFreqInstantAt = -999f;
                    instantCatchSendCount = 0;
                    host.InstantCatchDiag("=== CAST #" + instantCatchCastSeq + " ==="
                        + " target=" + targetPos
                        + " dist=" + (targetDistance > 0f ? targetDistance.ToString("F1") : "?") + "m"
                        + " netId=" + targetNetId + " enter=" + enterStatus);

                    Log("EnterFishing succeeded. status=" + enterStatus + " targetNetId=" + targetNetId);
                    return;
                }

                lastStatus = "Cast failed";
                lastTargetStatus = enterStatus;
                nextActionAt = now + FastRecastDelay;
                Log("EnterFishing failed. status=" + enterStatus + " targetNetId=" + targetNetId);
            }
            catch (Exception ex)
            {
                lastStatus = "Error: " + ex.Message;
                nextActionAt = now + 0.5f;
                Log("Update error: " + ex);
            }
        }

        public static void ForceStop(HeartopiaComplete host = null)
        {
            enabled = false;
            nextRodEquipAttemptAt = -999f;
            previousToolId = 0;
            previousToolRestorePending = false;
            nextActionAt = -999f;
            instantCatchActiveCached = false;
            sessionStartedAt = -999f;
            waitingSinceAt = -999f;
            hookedSinceAt = -999f;
            lastHookedStateAt = -999f;
            lastBattleStateAt = -999f;
            lastBattleLostBaitAt = -999f;
            ignoreStaleFishingStateUntil = -999f;
            lastBattleBaitNetId = 0U;
            instantCatchEventActiveUntil = -999f;
            fishBiteEventAt = -999f;
            fishBattleResultAt = -999f;
            fishCatchEventAt = -999f;
            lastLoggedFishBiteAt = -999f;
            lastLoggedFishResultAt = -999f;
            lastCastSentAt = -999f;
            nextWorldReadyLogAt = -999f;
            nextFishingStateLogAt = -999f;
            lastActiveStateLogKey = string.Empty;
            nextTargetMissLogAt = -999f;
            nextPressUpdateAt = -999f;
            consecutiveTargetMisses = 0;
            currentTargetNetId = 0U;
            currentTargetPos = Vector3.zero;
            lastRequestedPressed = null;
            IsInFishingSession = false;
            wasInFishingSession = false;
            LastScanAt = -999f;
            LastInRangeCount = 0;
            lastStatus = "Disabled";
            lastToolStatus = "Unknown";
            lastTargetStatus = "Idle";
            if (host != null)
            {
                try { host.TrySetFishingPressed(false, out _); } catch { }
            }
            Log("ForceStop invoked.");
        }

        private static void EnsureFishingRodEquipped(HeartopiaComplete host)
        {
            if (host == null)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now >= nextRodEquipAttemptAt)
            {
                host.EquipHandTool(3);
                nextRodEquipAttemptAt = now + RodEquipRetryInterval;
                lastStatus = "Equipping rod...";
                Log("Fishing rod missing; sent equip request.");
                return;
            }

            lastStatus = "Waiting for rod equip...";
        }

        private static void CapturePreviousTool(HeartopiaComplete host)
        {
            previousToolId = 0;
            previousToolRestorePending = false;

            if (host == null || !host.TryGetCurrentToolInfo(out int toolId, out _, out _))
            {
                return;
            }

            previousToolId = toolId;
            previousToolRestorePending = toolId != 0 && toolId != 3;
            if (previousToolRestorePending)
            {
                Log("Captured previous toolId=" + previousToolId);
            }
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
                if (host.TryGetFishingRodToolStatus(out bool rodEquipped, out _) && rodEquipped)
                {
                    host.EquipHandTool(0);
                    Log("No previous supported tool captured; unequipping rod.");
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

        private static void Log(string message)
        {
            if (!debugLoggingEnabled)
            {
                return;
            }

            ModLogger.Msg("[AutoFishingFarm] " + message);
        }
    }
}
