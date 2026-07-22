using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        private static bool AutoIceSkatingLogsEnabled => MasterLogAutoIceSkating;
        private const float AutoIceSkatingMinStartTriggerInterval = 0.03f;
        private const float AutoIceSkatingMinPerfectRatio = 0.02f;
        private const float AutoIceSkatingLogThrottleSeconds = 1.5f;
        private const float AutoIceSkatingReflectionRetrySeconds = 5f;

        private bool autoIceSkatingEnabled;
        private int autoIceSkatingMinUltimateScore = AutoIceSkatingDefaultMinUltimateScore;
        private bool autoIceSkatingOnlyX2Ultimate = true;
        private bool autoIceSkatingLast30sUltimate = true;
        private bool autoIceSkatingPerfectMove = true;
        private bool autoIceSkatingPreferNewMove = true;
        private string autoIceSkatingLastStatus = "Idle.";
        private float autoIceSkatingLastTriggerAt = -999f;
        private int autoIceSkatingLastSeenPerformingActionId;
        private int autoIceSkatingLastPerfectPhaseKey;
        private FeatureBreakerState autoIceSkatingBreaker;
        private float autoIceSkatingReflectionRetryAt = -999f;
        private string autoIceSkatingLastLoggedStatus = string.Empty;
        private readonly Dictionary<string, float> autoIceSkatingLogThrottle = new Dictionary<string, float>();
        private int autoIceSkatingLastUltimateSkipPerformingId;
        private string autoIceSkatingLastUltimateSkipSignature = string.Empty;
        private readonly StringBuilder autoIceSkatingUltimateCandidatesLogBuffer = new StringBuilder();

        private Type autoIceSkatingLocalPlayerComponentType;
        private Type autoIceSkatingGameSkateModeType;
        private Type autoIceSkatingTableDataType;
        private Type autoIceSkatingTableSkateActionType;
        private MethodInfo autoIceSkatingGetGameModeGeneric;
        private MethodInfo autoIceSkatingCharacterGetModeGeneric;
        private MethodInfo autoIceSkatingGetSkateActionMethod;
        private MethodInfo autoIceSkatingSkillTriggerMethod;
        private MethodInfo autoIceSkatingCanTriggerUltimateMethod;
        private MethodInfo autoIceSkatingCalculateSpeedRateMethod;
        private PropertyInfo autoIceSkatingEnergyProperty;
        private MethodInfo autoIceSkatingIsReceiverMethod;
        private MethodInfo autoIceSkatingGetRatioInConfiguredPhaseMethod;
        private PropertyInfo autoIceSkatingGameModeActivedProperty;
        private PropertyInfo autoIceSkatingCurrentModeProperty;
        private PropertyInfo autoIceSkatingUltimateSkillProperty;
        private PropertyInfo autoIceSkatingSkateSkillsProperty;
        private FieldInfo autoIceSkatingCurrentCastActionField;
        private FieldInfo autoIceSkatingCastActionIdField;
        private FieldInfo autoIceSkatingChallengeInfoField;
        private MethodInfo autoIceSkatingChallengeIsNewActionMethod;
        private PropertyInfo autoIceSkatingTableSkateActionScoreProperty;
        private PropertyInfo autoIceSkatingTableSkateActionBonusScoreProperty;
        private PropertyInfo autoIceSkatingTableSkateActionPrefectPhaseProperty;
        private PropertyInfo autoIceSkatingTableSkateActionNormalPhaseProperty;
        private PropertyInfo autoIceSkatingTableSkateActionIdProperty;
        private MethodInfo autoIceSkatingGetSkateActionStateMethod;
        private FieldInfo autoIceSkatingTableSkateActionStatePhaseField;
        private PropertyInfo autoIceSkatingTableSkateActionActionTypeProperty;
        private PropertyInfo autoIceSkatingTableSkateActionPairMotionProperty;
        private MethodInfo autoIceSkatingGetSkateActionTypeMethod;
        private PropertyInfo autoIceSkatingTableSkateActionTypeUltimateActionIdProperty;
        private MethodInfo autoIceSkatingGetPairSkateUltimateMethod;
        private PropertyInfo autoIceSkatingTablePairSkateUltimateScoreProperty;
        private PropertyInfo autoIceSkatingTablePairSkateUltimateBonusScoreProperty;
        private MethodInfo autoIceSkatingCheckPairSkateMethod;
        private FieldInfo autoIceSkatingSkateActionsField;
        private PropertyInfo autoIceSkatingCastNormalActionIdProperty;
        private PropertyInfo autoIceSkatingTableSkateActionIconTipCountProperty;

        private bool autoIceSkatingUsesAura;
        private IntPtr autoIceSkatingAuraGameSkateModeClass;
        private IntPtr autoIceSkatingAuraLocalPlayerClass;
        private IntPtr autoIceSkatingAuraCharacterClass;
        private IntPtr autoIceSkatingAuraTableDataClass;
        private IntPtr autoIceSkatingAuraGetGameModeOpenMethod;
        private IntPtr autoIceSkatingAuraCharacterGetModeOpenMethod;
        private IntPtr autoIceSkatingAuraInflatedGetGameModeMethod;
        private IntPtr autoIceSkatingAuraInflatedCharacterGetModeMethod;
        private IntPtr autoIceSkatingAuraGetSkateActionMethod;
        private IntPtr autoIceSkatingAuraGetSkateActionStateMethod;
        private IntPtr autoIceSkatingAuraSkillTriggerMethod;
        private IntPtr autoIceSkatingAuraCanTriggerUltimateMethod;
        private IntPtr autoIceSkatingAuraIsReceiverMethod;
        private IntPtr autoIceSkatingAuraCalculateSpeedRateMethod;
        private IntPtr autoIceSkatingAuraGetRatioInConfiguredPhaseMethod;
        private IntPtr autoIceSkatingAuraGetUltimateSkillMethod;
        private IntPtr autoIceSkatingAuraGetSkateActionTypeMethod;
        private IntPtr autoIceSkatingAuraGetPairSkateUltimateMethod;
        private IntPtr autoIceSkatingAuraCheckPairSkateMethod;
        private IntPtr autoIceSkatingAuraChallengeInfoField;
        private IntPtr autoIceSkatingAuraChallengeDataClass;
        private IntPtr autoIceSkatingAuraChallengeDataUsedActionsField;
        private IntPtr autoIceSkatingAuraChallengeDataTimestampField;
        private IntPtr autoIceSkatingAuraGameTimeUtilityClass;
        private IntPtr autoIceSkatingAuraGetUnixTimeMsMethod;
        private bool autoIceSkatingAuraGetUnixTimeReturnsSeconds;
        private IntPtr autoIceSkatingAuraHashSetContainsMethod;
        private int autoIceSkatingAuraChallengeDataUsedActionsOffset = -1;
        private int autoIceSkatingAuraChallengeDataTimestampOffset = -1;
        private bool autoIceSkatingAuraSkateSessionActive;
        private float autoIceSkatingAuraSkateReadyAt = -999f;
        private bool autoIceSkatingAuraWasInChallenge;
        private float autoIceSkatingAuraChallengeCountdownUntil = -999f;
        private IntPtr autoIceSkatingAuraSkateActionsField;
        private IntPtr autoIceSkatingAuraCurrentCastActionField;
        private IntPtr autoIceSkatingAuraCastInfoField;
        private readonly List<int> autoIceSkatingTreeActionIdsBuffer = new List<int>();
        private readonly List<IntPtr> autoIceSkatingAuraKeyBuffer = new List<IntPtr>();
        private readonly HashSet<int> autoIceSkatingUltimateIdDedup = new HashSet<int>();
        private int autoIceSkatingCachedMaxUltimateScore;
        private int autoIceSkatingCachedMaxUltimateId;
        private int autoIceSkatingLastMaxUltimateScore;
        private int autoIceSkatingCachedMaxUltimateSkillsHash;
        private IntPtr autoIceSkatingAuraChallengeDataDurationField;
        private int autoIceSkatingAuraChallengeDataDurationOffset = -1;
        private PropertyInfo autoIceSkatingChallengeRemainingTimeProperty;
        private float autoIceSkatingCachedMaxUltimateAt = -999f;
        private const int AutoIceSkatingMaxActionId = 100000;
        private const int AutoIceSkatingMaxTreeActionsScanned = 128;
        private const float AutoIceSkatingMaxUltimateCacheSeconds = 0.35f;
        private const int AutoIceSkatingSkateModeChallenge = 2;
        private const int AutoIceSkatingSkateActionTypeUltimate = 11;
        private const int AutoIceSkatingUltimateEnergyTierRequired = 2;
        private const int AutoIceSkatingDefaultMinUltimateScore = 900;
        private const int AutoIceSkatingMinUltimateScoreSliderMax = 2000;
        private const int AutoIceSkatingUltimatePickBias = 100000;
        private const int AutoIceSkatingDurationPriorityScale = 100;
        private const float AutoIceSkatingEnergyTierUnit = 100f;
        private const int AutoIceSkatingAuraCastActionInfoSize = 12;
        private const int AutoIceSkatingAuraCastActionNormalActionOffset = 8;
        private const int AutoIceSkatingAuraCastStatusInfoSize = 32;
        private const int AutoIceSkatingAuraChallengeDataSize = 64;
        private const float AutoIceSkatingChallengeCountdownSeconds = 3f;
        private const float AutoIceSkatingSkateEnterWarmupSeconds = 0.85f;
        // Below this remaining challenge time, stop hoarding energy for x2: cast an ultimate
        // already at tier x1 (still requiring the 900-point floor) before leftover energy is wasted.
        private const float AutoIceSkatingEndgameSeconds = 30f;

        private struct AutoIceSkatingUltimateScoreDetail
        {
            public int TableScore;
            public int TableBonus;
            public int ChallengeBonusApplied;
            public int PreRateScore;
            public int FinalScore;
            public float SpeedRate;
            public bool HasStarBonus;
            public bool PairOverride;

            public string ToScoreLog()
            {
                StringBuilder sb = new StringBuilder();
                sb.Append("tableScore=").Append(this.TableScore);
                sb.Append(" tableBonus=").Append(this.TableBonus);
                sb.Append(" challenge+=").Append(this.ChallengeBonusApplied);
                sb.Append(" preRate=").Append(this.PreRateScore);
                sb.Append(" final=").Append(this.FinalScore);
                sb.Append(" rate=").Append(this.SpeedRate.ToString("0.##"));
                if (this.PairOverride)
                {
                    sb.Append(" pair=1");
                }

                return sb.ToString();
            }
        }

        private void AutoIceSkatingResetUltimateSkipLogState()
        {
            this.autoIceSkatingLastUltimateSkipPerformingId = 0;
            this.autoIceSkatingLastUltimateSkipSignature = string.Empty;
        }

        private void AutoIceSkatingSyncUltimateSkipPerformingAction(int performingActionId)
        {
            if (performingActionId == this.autoIceSkatingLastUltimateSkipPerformingId)
            {
                return;
            }

            this.autoIceSkatingLastUltimateSkipPerformingId = performingActionId;
            this.autoIceSkatingLastUltimateSkipSignature = string.Empty;
        }

        private void AutoIceSkatingLogUltimateWaitDisabled(
            string context,
            int energyTier,
            string reason,
            int maxUltimateScore,
            string candidatesDetail)
        {
            string signature = context + "|wait-off|r=" + reason + "|max=" + maxUltimateScore + "|c=" + candidatesDetail;
            string message = "ultimate wait off tier=x" + energyTier
                + " ctx=" + context
                + " reason=" + reason
                + " max=" + maxUltimateScore
                + " min=" + this.autoIceSkatingMinUltimateScore
                + " candidates=" + candidatesDetail;
            if (!string.Equals(signature, this.autoIceSkatingLastUltimateSkipSignature, StringComparison.Ordinal))
            {
                this.autoIceSkatingLastUltimateSkipSignature = signature;
                this.AutoIceSkatingLog(message, force: true);
                return;
            }

            this.AutoIceSkatingLog(message, "ultimate-wait:" + signature);
        }

        private void AutoIceSkatingLog(string message, string throttleKey = null, bool force = false)
        {
            if (!AutoIceSkatingLogsEnabled)
            {
                return;
            }

            if (!force && !string.IsNullOrEmpty(throttleKey))
            {
                float now = Time.unscaledTime;
                if (this.autoIceSkatingLogThrottle.TryGetValue(throttleKey, out float lastAt)
                    && now - lastAt < AutoIceSkatingLogThrottleSeconds)
                {
                    return;
                }

                this.autoIceSkatingLogThrottle[throttleKey] = now;
            }

            ModLogger.Msg("[AutoIceSkating] " + message);
        }

        private void AutoIceSkatingSetStatus(string status, string throttleKey = null, bool log = true, bool force = false)
        {
            this.autoIceSkatingLastStatus = status;
            if (!log)
            {
                return;
            }

            if (!force && string.Equals(this.autoIceSkatingLastLoggedStatus, status, StringComparison.Ordinal))
            {
                return;
            }

            this.autoIceSkatingLastLoggedStatus = status;
            this.AutoIceSkatingLog(status, throttleKey ?? ("status:" + status), force: force || throttleKey == null);
        }

        private void ProcessAutoIceSkatingOnUpdate()
        {
            if (!this.autoIceSkatingEnabled)
            {
                this.AutoIceSkatingSetStatus("Disabled.", log: false);
                this.AutoIceSkatingResetPerformingTrackers();
                this.autoIceSkatingLastLoggedStatus = string.Empty;
                return;
            }

            // "Menu open" = any MODAL registry surface (the UGUI shell) — showMenu is retired.
            if (this.IsAnyModalInputSurfaceOpen())
            {
                this.AutoIceSkatingSetStatus("Paused (menu open).", "menu-open");
                return;
            }

            if (ShouldBlockGameplayInput())
            {
                this.AutoIceSkatingSetStatus("Paused (gameplay input blocked).", "input-blocked");
                return;
            }

            float now = Time.unscaledTime;
            if (!this.autoIceSkatingBreaker.ShouldRun(now))
            {
                this.AutoIceSkatingSetStatus("Circuit breaker cooldown.", "breaker");
                return;
            }

            try
            {
                this.TickAutoIceSkating(now);
                this.autoIceSkatingBreaker.Success();
            }
            catch (Exception ex)
            {
                this.autoIceSkatingBreaker.Failure("AutoIceSkating", ex, now);
                this.AutoIceSkatingLog("tick exception: " + ex, "tick-exception", force: true);
                this.AutoIceSkatingSetStatus("Error: " + ex.Message, force: true);
            }
        }

        private void TickAutoIceSkating(float now)
        {
            if (!this.TryResolveAutoIceSkatingReflection(out string resolveDetail))
            {
                this.AutoIceSkatingSetStatus(resolveDetail, "reflection-fail");
                return;
            }

            if (this.autoIceSkatingUsesAura)
            {
                this.TickAutoIceSkatingAura(now);
                return;
            }

            if (!this.TryGetAutoIceSkatingMode(out object localPlayer, out object skateMode, out string modeSource))
            {
                this.AutoIceSkatingResetPerformingTrackers();
                this.AutoIceSkatingSetStatus("Not skating (" + modeSource + ").", "not-skating");
                return;
            }

            bool actived = (bool)this.autoIceSkatingGameModeActivedProperty.GetValue(skateMode, null);
            object currentMode = this.autoIceSkatingCurrentModeProperty != null
                ? this.autoIceSkatingCurrentModeProperty.GetValue(skateMode, null)
                : null;
            if (!actived)
            {
                this.AutoIceSkatingResetPerformingTrackers();
                this.AutoIceSkatingSetStatus(
                    "Skate mode inactive (enter ice first). mode=" + (currentMode?.ToString() ?? "null") + " via " + modeSource + ".",
                    "inactive");
                return;
            }

            if ((bool)this.autoIceSkatingIsReceiverMethod.Invoke(skateMode, null))
            {
                this.AutoIceSkatingSetStatus("Pair receiver — manual only.", "receiver");
                return;
            }

            object challengeInfo = this.autoIceSkatingChallengeInfoField.GetValue(skateMode);

            this.TryCollectAutoIceSkatingSkills(skateMode, out List<int> skills, out string skillsDetail);
            if (skills.Count == 0)
            {
                this.AutoIceSkatingSetStatus("No skills in tree (" + skillsDetail + ").", "no-skills");
                return;
            }

            int currentActionId = this.TryReadCurrentCastActionId(skateMode);
            this.AutoIceSkatingSyncPerformingAction(currentActionId);
            this.AutoIceSkatingSyncUltimateSkipPerformingAction(currentActionId);
            if (currentActionId > 0)
            {
                bool canUltimate = (bool)this.autoIceSkatingCanTriggerUltimateMethod.Invoke(skateMode, null);
                bool endgame = this.autoIceSkatingLast30sUltimate
                    && this.AutoIceSkatingIsChallengeEndgameManaged(skateMode, challengeInfo);
                int requiredTier = endgame
                    ? 1
                    : (this.autoIceSkatingOnlyX2Ultimate ? AutoIceSkatingUltimateEnergyTierRequired : 1);
                if (canUltimate
                    && this.AutoIceSkatingIsUltimateEnergyTierReadyManaged(skateMode, requiredTier, out int energyTier))
                {
                    bool isPairSkate = this.TryAutoIceSkatingIsPairSkateManaged(skateMode);
                    string candidatesDetail = this.BuildAutoIceSkatingUltimateCandidatesManaged(
                        skateMode,
                        challengeInfo,
                        skills,
                        isPairSkate);
                    if (this.TryAutoIceSkatingSelectUltimateManaged(
                            skateMode,
                            challengeInfo,
                            skills,
                            isPairSkate,
                            out int ultimateId,
                            out int ultimateScore,
                            out int maxSeenScore))
                    {
                        if (this.TryAutoIceSkatingAttemptUltimateTriggerManaged(
                                skateMode,
                                ultimateId,
                                ultimateScore,
                                energyTier,
                                endgame,
                                candidatesDetail,
                                endgame ? "performing-endgame" : "performing",
                                now))
                        {
                            return;
                        }
                    }
                    else
                    {
                        this.AutoIceSkatingLogUltimateWaitDisabled(
                            "performing",
                            energyTier,
                            "no-qualifying",
                            maxSeenScore,
                            candidatesDetail);
                    }
                }

                if (this.TryAutoIceSkatingTryPerfectInterruptManaged(
                        skateMode,
                        challengeInfo,
                        skills,
                        currentActionId,
                        now))
                {
                    return;
                }

                this.AutoIceSkatingSetStatus("Performing action " + currentActionId + ".", "performing", log: false);
                return;
            }

            if (this.TryAutoIceSkatingTickUltimateOnIdleManaged(skateMode, challengeInfo, skills, now))
            {
                return;
            }

            this.AutoIceSkatingResetPerformingTrackers();
            if (!this.AutoIceSkatingIsStartTriggerReady(now))
            {
                return;
            }

            int startActionId = this.PickAutoIceSkatingBestSkill(
                skateMode,
                challengeInfo,
                skills,
                preferDifferentFrom: 0,
                out string startPickDetail);
            this.AutoIceSkatingLog("idle pick=" + startActionId + " skills=[" + string.Join(",", skills) + "] (" + startPickDetail + ")", "idle-pick");
            if (startActionId > 0)
            {
                this.TryAutoIceSkatingSkillTrigger(skateMode, startActionId, now, "start");
            }
            else
            {
                this.AutoIceSkatingSetStatus("Could not pick a skill (" + startPickDetail + ").", "pick-fail");
            }
        }

        private void AutoIceSkatingResetPerformingTrackers()
        {
            this.autoIceSkatingLastSeenPerformingActionId = 0;
            this.autoIceSkatingLastPerfectPhaseKey = 0;
            this.AutoIceSkatingResetUltimateSkipLogState();
        }

        private void AutoIceSkatingSyncPerformingAction(int currentActionId)
        {
            if (currentActionId != this.autoIceSkatingLastSeenPerformingActionId)
            {
                this.autoIceSkatingLastSeenPerformingActionId = currentActionId;
                this.autoIceSkatingLastPerfectPhaseKey = 0;
            }
        }

        private static int AutoIceSkatingMakePerfectPhaseKey(int actionId, int phaseIndex)
        {
            return (actionId << 8) | (phaseIndex & 0xFF);
        }

        private bool AutoIceSkatingIsStartTriggerReady(float now)
        {
            return now - this.autoIceSkatingLastTriggerAt >= AutoIceSkatingMinStartTriggerInterval;
        }

        private bool TryAutoIceSkatingTryPerfectInterruptManaged(
            object skateMode,
            object challengeInfo,
            List<int> skills,
            int currentActionId,
            float now)
        {
            // "Perfect move" off: chain the next move as soon as the game allows an interrupt
            // (SkillTrigger gates blend time / interruptibility), not waiting for the perfect window.
            if (!this.autoIceSkatingPerfectMove)
            {
                if (!this.AutoIceSkatingIsStartTriggerReady(now))
                {
                    return false;
                }

                int immediateId = this.PickAutoIceSkatingBestSkill(
                    skateMode,
                    challengeInfo,
                    skills,
                    preferDifferentFrom: currentActionId,
                    out string immediateDetail);
                this.AutoIceSkatingLog("immediate pick=" + immediateId + " (" + immediateDetail + ")", "immediate-pick", force: true);
                return immediateId > 0
                    && this.TryAutoIceSkatingSkillTrigger(skateMode, immediateId, now, "immediate");
            }

            object currentConfig = this.TryAutoIceSkatingInvokeGetSkateAction(currentActionId);
            int[] prefectPhase = ReadAutoIceSkatingIntArray(currentConfig, this.autoIceSkatingTableSkateActionPrefectPhaseProperty);
            float perfectRatio = 0f;
            int perfectPhaseIndex = -1;
            bool inPerfect = prefectPhase != null
                && prefectPhase.Length > 0
                && this.TryIsInConfiguredPhase(skateMode, prefectPhase, out perfectRatio, out perfectPhaseIndex);
            this.AutoIceSkatingLog(
                "performing action=" + currentActionId
                + " prefectLen=" + (prefectPhase?.Length ?? 0)
                + " inPerfect=" + inPerfect
                + (inPerfect ? (" ratio=" + perfectRatio.ToString("0.###") + " phaseIdx=" + perfectPhaseIndex) : string.Empty),
                "performing-" + currentActionId);

            int phaseKey = AutoIceSkatingMakePerfectPhaseKey(currentActionId, perfectPhaseIndex);
            if (!inPerfect || phaseKey == this.autoIceSkatingLastPerfectPhaseKey)
            {
                return false;
            }

            int nextActionId = this.PickAutoIceSkatingBestSkill(
                skateMode,
                challengeInfo,
                skills,
                preferDifferentFrom: currentActionId,
                out string pickDetail);
            this.AutoIceSkatingLog("perfect window pick=" + nextActionId + " (" + pickDetail + ")", "perfect-pick", force: true);
            if (nextActionId > 0
                && this.TryAutoIceSkatingSkillTrigger(skateMode, nextActionId, now, "perfect", applyCooldown: false))
            {
                this.autoIceSkatingLastPerfectPhaseKey = phaseKey;
                return true;
            }

            return false;
        }

        private static int AutoIceSkatingGetUltimateEnergyTier(int energy)
        {
            if (energy < AutoIceSkatingEnergyTierUnit)
            {
                return 0;
            }

            return (int)Math.Floor(energy / AutoIceSkatingEnergyTierUnit);
        }

        private bool AutoIceSkatingIsUltimateEnergyTierReadyManaged(object skateMode, int requiredTier, out int energyTier)
        {
            energyTier = 0;
            if (skateMode == null || this.autoIceSkatingEnergyProperty == null)
            {
                return false;
            }

            int energy = Convert.ToInt32(this.autoIceSkatingEnergyProperty.GetValue(skateMode, null));
            energyTier = AutoIceSkatingGetUltimateEnergyTier(energy);
            return energyTier >= requiredTier;
        }

        private bool AutoIceSkatingIsUltimateEnergyTierReadyAura(IntPtr skateMode, int requiredTier, out int energyTier)
        {
            energyTier = 0;
            if (skateMode == IntPtr.Zero || !this.TryGetMonoInt32Member(skateMode, "Energy", out int energy))
            {
                return false;
            }

            energyTier = AutoIceSkatingGetUltimateEnergyTier(energy);
            return energyTier >= requiredTier;
        }

        private bool AutoIceSkatingIsChallengeEndgameManaged(object skateMode, object challengeInfo)
        {
            if (challengeInfo == null
                || this.autoIceSkatingChallengeRemainingTimeProperty == null
                || !this.AutoIceSkatingIsChallengeManaged(skateMode))
            {
                return false;
            }

            try
            {
                float remaining = Convert.ToSingle(this.autoIceSkatingChallengeRemainingTimeProperty.GetValue(challengeInfo, null));
                return remaining > 0f && remaining <= AutoIceSkatingEndgameSeconds;
            }
            catch
            {
                return false;
            }
        }

        private unsafe bool AutoIceSkatingIsChallengeEndgameAura(IntPtr skateMode)
        {
            if (skateMode == IntPtr.Zero
                || this.autoIceSkatingAuraChallengeDataTimestampOffset < 0
                || this.autoIceSkatingAuraChallengeDataDurationOffset < 0
                || !this.TryAutoIceSkatingAuraIsInChallengeMode(skateMode))
            {
                return false;
            }

            byte* buffer = stackalloc byte[AutoIceSkatingAuraChallengeDataSize];
            if (!this.TryAutoIceSkatingAuraReadChallengeDataBuffer(skateMode, buffer))
            {
                return false;
            }

            long timestamp = *(long*)(buffer + this.autoIceSkatingAuraChallengeDataTimestampOffset);
            int duration = *(int*)(buffer + this.autoIceSkatingAuraChallengeDataDurationOffset);
            if (timestamp <= 0L || duration <= 0 || duration > 36000)
            {
                return false;
            }

            if (!this.TryAutoIceSkatingAuraGetUnixTimeMs(out long nowMs))
            {
                return false;
            }

            // ChallengeData.RemainingTime: Duration + countdown(3s) - passed seconds.
            float remaining = (float)duration + AutoIceSkatingChallengeCountdownSeconds
                - (float)(nowMs - timestamp) / 1000f;
            return remaining > 0f && remaining <= AutoIceSkatingEndgameSeconds;
        }

        private bool TryAutoIceSkatingIsPairSkateManaged(object skateMode)
        {
            if (skateMode == null || this.autoIceSkatingCheckPairSkateMethod == null)
            {
                return false;
            }

            try
            {
                return (bool)this.autoIceSkatingCheckPairSkateMethod.Invoke(skateMode, null);
            }
            catch
            {
                return false;
            }
        }

        private bool TryAutoIceSkatingIsPairSkateAura(IntPtr skateMode)
        {
            return skateMode != IntPtr.Zero
                && this.autoIceSkatingAuraCheckPairSkateMethod != IntPtr.Zero
                && this.TryAutoIceSkatingAuraInvokeBool(skateMode, this.autoIceSkatingAuraCheckPairSkateMethod, out bool isPairSkate)
                && isPairSkate;
        }

        private int TryAutoIceSkatingResolveUltimateActionIdManaged(int normalActionId)
        {
            if (normalActionId <= 0
                || this.autoIceSkatingGetSkateActionTypeMethod == null
                || this.autoIceSkatingTableSkateActionActionTypeProperty == null
                || this.autoIceSkatingTableSkateActionTypeUltimateActionIdProperty == null)
            {
                return 0;
            }

            object normalConfig = this.TryAutoIceSkatingInvokeGetSkateAction(normalActionId);
            if (normalConfig == null)
            {
                return 0;
            }

            int actionType = Convert.ToInt32(this.autoIceSkatingTableSkateActionActionTypeProperty.GetValue(normalConfig, null));
            if (actionType == AutoIceSkatingSkateActionTypeUltimate)
            {
                return 0;
            }

            object typeRow = this.TryAutoIceSkatingInvokeGetSkateActionType(actionType);
            if (typeRow == null)
            {
                return 0;
            }

            int ultimateId = Convert.ToInt32(this.autoIceSkatingTableSkateActionTypeUltimateActionIdProperty.GetValue(typeRow, null));
            return this.TryAutoIceSkatingInvokeGetSkateAction(ultimateId) != null ? ultimateId : 0;
        }

        private unsafe int TryAutoIceSkatingResolveUltimateActionIdAura(int normalActionId)
        {
            if (normalActionId <= 0)
            {
                return 0;
            }

            IntPtr normalConfig = this.TryAutoIceSkatingAuraGetSkateActionRow(normalActionId);
            if (normalConfig == IntPtr.Zero || !this.TryGetMonoInt32Member(normalConfig, "actionType", out int actionType))
            {
                return 0;
            }

            if (actionType == AutoIceSkatingSkateActionTypeUltimate)
            {
                return 0;
            }

            IntPtr typeRow = this.TryAutoIceSkatingAuraGetSkateActionTypeRow(actionType);
            if (typeRow == IntPtr.Zero || !this.TryGetMonoInt32Member(typeRow, "ultimateActionId", out int ultimateId))
            {
                return 0;
            }

            return this.TryAutoIceSkatingAuraGetSkateActionRow(ultimateId) != IntPtr.Zero ? ultimateId : 0;
        }

        private object TryAutoIceSkatingInvokeGetSkateActionType(int actionTypeId)
        {
            if (this.autoIceSkatingGetSkateActionTypeMethod == null || actionTypeId <= 0)
            {
                return null;
            }

            ParameterInfo[] parameters = this.autoIceSkatingGetSkateActionTypeMethod.GetParameters();
            if (parameters != null && parameters.Length >= 2)
            {
                return this.autoIceSkatingGetSkateActionTypeMethod.Invoke(null, new object[] { actionTypeId, false });
            }

            return this.autoIceSkatingGetSkateActionTypeMethod.Invoke(null, new object[] { actionTypeId });
        }

        private object TryAutoIceSkatingInvokeGetPairSkateUltimate(int pairMotionId)
        {
            if (this.autoIceSkatingGetPairSkateUltimateMethod == null || pairMotionId <= 0)
            {
                return null;
            }

            ParameterInfo[] parameters = this.autoIceSkatingGetPairSkateUltimateMethod.GetParameters();
            if (parameters != null && parameters.Length >= 2)
            {
                return this.autoIceSkatingGetPairSkateUltimateMethod.Invoke(null, new object[] { pairMotionId, false });
            }

            return this.autoIceSkatingGetPairSkateUltimateMethod.Invoke(null, new object[] { pairMotionId });
        }

        private int ScoreAutoIceSkatingUltimateManaged(
            object skateMode,
            int ultimateActionId,
            object challengeInfo,
            bool isPairSkate,
            out bool hasStarBonus)
        {
            hasStarBonus = false;
            if (!this.TryComputeAutoIceSkatingUltimateScoreManaged(
                    skateMode,
                    ultimateActionId,
                    challengeInfo,
                    isPairSkate,
                    out AutoIceSkatingUltimateScoreDetail detail))
            {
                return 0;
            }

            hasStarBonus = detail.HasStarBonus;
            return detail.FinalScore;
        }

        private bool TryComputeAutoIceSkatingUltimateScoreManaged(
            object skateMode,
            int ultimateActionId,
            object challengeInfo,
            bool isPairSkate,
            out AutoIceSkatingUltimateScoreDetail detail)
        {
            detail = default;
            object config = this.TryAutoIceSkatingInvokeGetSkateAction(ultimateActionId);
            if (config == null)
            {
                return false;
            }

            int score = Convert.ToInt32(this.autoIceSkatingTableSkateActionScoreProperty.GetValue(config, null));
            int bonus = this.autoIceSkatingTableSkateActionBonusScoreProperty != null
                ? Convert.ToInt32(this.autoIceSkatingTableSkateActionBonusScoreProperty.GetValue(config, null))
                : 0;
            bool hasIconTip = this.autoIceSkatingTableSkateActionIconTipCountProperty != null
                && Convert.ToInt32(this.autoIceSkatingTableSkateActionIconTipCountProperty.GetValue(config, null)) > 0;
            if (isPairSkate
                && this.autoIceSkatingTableSkateActionPairMotionProperty != null
                && this.autoIceSkatingGetPairSkateUltimateMethod != null)
            {
                int pairMotion = Convert.ToInt32(this.autoIceSkatingTableSkateActionPairMotionProperty.GetValue(config, null));
                if (pairMotion > 0)
                {
                    object pair = this.TryAutoIceSkatingInvokeGetPairSkateUltimate(pairMotion);
                    if (pair != null)
                    {
                        detail.PairOverride = true;
                        if (this.autoIceSkatingTablePairSkateUltimateScoreProperty != null)
                        {
                            score = Convert.ToInt32(this.autoIceSkatingTablePairSkateUltimateScoreProperty.GetValue(pair, null));
                        }

                        if (this.autoIceSkatingTablePairSkateUltimateBonusScoreProperty != null)
                        {
                            bonus = Convert.ToInt32(this.autoIceSkatingTablePairSkateUltimateBonusScoreProperty.GetValue(pair, null));
                        }
                    }
                }
            }

            detail.TableScore = score;
            detail.TableBonus = bonus;
            detail.ChallengeBonusApplied = 0;
            detail.PreRateScore = score;
            if (this.AutoIceSkatingIsChallengeManaged(skateMode)
                && challengeInfo != null
                && this.autoIceSkatingChallengeIsNewActionMethod != null
                && (bool)this.autoIceSkatingChallengeIsNewActionMethod.Invoke(challengeInfo, new object[] { ultimateActionId }))
            {
                detail.ChallengeBonusApplied = bonus;
                detail.PreRateScore = score + bonus;
                if (hasIconTip)
                {
                    detail.HasStarBonus = true;
                }
            }

            detail.SpeedRate = 1f;
            if (skateMode != null && this.autoIceSkatingCalculateSpeedRateMethod != null)
            {
                try
                {
                    detail.SpeedRate = Convert.ToSingle(this.autoIceSkatingCalculateSpeedRateMethod.Invoke(skateMode, null));
                }
                catch
                {
                    detail.SpeedRate = 1f;
                }
            }

            detail.FinalScore = detail.PreRateScore;
            if (detail.PreRateScore > 0 && detail.SpeedRate != 1f)
            {
                detail.FinalScore = (int)((float)detail.PreRateScore * detail.SpeedRate);
            }

            return true;
        }

        private unsafe int ScoreAutoIceSkatingUltimateAura(
            IntPtr skateMode,
            int ultimateActionId,
            IntPtr challengeInfo,
            bool isPairSkate,
            out bool hasStarBonus)
        {
            hasStarBonus = false;
            if (!this.TryComputeAutoIceSkatingUltimateScoreAura(
                    skateMode,
                    ultimateActionId,
                    challengeInfo,
                    isPairSkate,
                    out AutoIceSkatingUltimateScoreDetail detail))
            {
                return 0;
            }

            hasStarBonus = detail.HasStarBonus;
            return detail.FinalScore;
        }

        private unsafe bool TryComputeAutoIceSkatingUltimateScoreAura(
            IntPtr skateMode,
            int ultimateActionId,
            IntPtr challengeInfo,
            bool isPairSkate,
            out AutoIceSkatingUltimateScoreDetail detail)
        {
            detail = default;
            IntPtr config = this.TryAutoIceSkatingAuraGetSkateActionRow(ultimateActionId);
            if (config == IntPtr.Zero)
            {
                return false;
            }

            int score = 0;
            int bonus = 0;
            this.TryGetMonoInt32Member(config, "score", out score);
            this.TryGetMonoInt32Member(config, "bonusScore", out bonus);
            bool hasIconTip = this.TryGetMonoInt32Member(config, "iconTipCount", out int iconTipCount) && iconTipCount > 0;
            if (isPairSkate && this.TryGetMonoInt32Member(config, "pairMotion", out int pairMotion) && pairMotion > 0)
            {
                IntPtr pair = this.TryAutoIceSkatingAuraGetPairSkateUltimateRow(pairMotion);
                if (pair != IntPtr.Zero)
                {
                    detail.PairOverride = true;
                    this.TryGetMonoInt32Member(pair, "score", out score);
                    this.TryGetMonoInt32Member(pair, "bonusScore", out bonus);
                }
            }

            detail.TableScore = score;
            detail.TableBonus = bonus;
            detail.ChallengeBonusApplied = 0;
            detail.PreRateScore = score;
            if (this.TryAutoIceSkatingAuraIsInChallengeMode(skateMode)
                && this.TryAutoIceSkatingAuraChallengeIsNewAction(skateMode, ultimateActionId))
            {
                detail.ChallengeBonusApplied = bonus;
                detail.PreRateScore = score + bonus;
                if (hasIconTip)
                {
                    detail.HasStarBonus = true;
                }
            }

            detail.SpeedRate = 1f;
            this.TryAutoIceSkatingAuraGetSpeedRate(skateMode, out detail.SpeedRate);
            detail.FinalScore = detail.PreRateScore;
            if (detail.PreRateScore > 0 && detail.SpeedRate != 1f)
            {
                detail.FinalScore = (int)((float)detail.PreRateScore * detail.SpeedRate);
            }

            return true;
        }

        private unsafe bool TryAutoIceSkatingAuraGetSpeedRate(IntPtr skateMode, out float rate)
        {
            rate = 1f;
            if (skateMode == IntPtr.Zero
                || this.autoIceSkatingAuraCalculateSpeedRateMethod == IntPtr.Zero
                || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr boxed = auraMonoRuntimeInvoke(this.autoIceSkatingAuraCalculateSpeedRateMethod, skateMode, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || boxed == IntPtr.Zero || auraMonoObjectUnbox == null)
            {
                return false;
            }

            if (!this.TryAuraMonoBoxedIsValueType(boxed))
            {
                return false;
            }

            IntPtr raw = auraMonoObjectUnbox(boxed);
            if (raw == IntPtr.Zero)
            {
                return false;
            }

            rate = *(float*)raw;
            return true;
        }

        private bool AutoIceSkatingIsChallengeManaged(object skateMode)
        {
            if (skateMode == null || this.autoIceSkatingCurrentModeProperty == null)
            {
                return false;
            }

            return Convert.ToInt32(this.autoIceSkatingCurrentModeProperty.GetValue(skateMode, null)) == AutoIceSkatingSkateModeChallenge;
        }

        private static int AutoIceSkatingHashSkills(List<int> skills)
        {
            int hash = 17;
            if (skills == null)
            {
                return hash;
            }

            for (int i = 0; i < skills.Count; i++)
            {
                hash = (hash * 31) + skills[i];
            }

            return hash;
        }

        private void AutoIceSkatingInvalidateMaxUltimateCache()
        {
            this.autoIceSkatingCachedMaxUltimateScore = 0;
            this.autoIceSkatingCachedMaxUltimateId = 0;
            this.autoIceSkatingCachedMaxUltimateSkillsHash = 0;
            this.autoIceSkatingCachedMaxUltimateAt = -999f;
        }

        private unsafe bool TryReadAutoIceSkatingAuraCastActionIds(IntPtr skateMode, out int actionId, out int normalActionId)
        {
            actionId = 0;
            normalActionId = 0;
            if (skateMode == IntPtr.Zero
                || this.autoIceSkatingAuraCurrentCastActionField == IntPtr.Zero
                || auraMonoFieldGetValue == null
                || !this.AttachAuraMonoThread())
            {
                return false;
            }

            byte* buffer = stackalloc byte[AutoIceSkatingAuraCastActionInfoSize];
            auraMonoFieldGetValue(skateMode, this.autoIceSkatingAuraCurrentCastActionField, (IntPtr)buffer);
            actionId = *(int*)buffer;
            normalActionId = *(int*)(buffer + AutoIceSkatingAuraCastActionNormalActionOffset);

            if (actionId <= 0 || actionId > AutoIceSkatingMaxActionId)
            {
                actionId = 0;
            }

            if (normalActionId <= 0 || normalActionId > AutoIceSkatingMaxActionId)
            {
                normalActionId = 0;
            }

            return true;
        }

        private int TryReadCurrentCastNormalActionIdManaged(object skateMode)
        {
            if (skateMode == null || this.autoIceSkatingCurrentCastActionField == null)
            {
                return 0;
            }

            object castInfo = this.autoIceSkatingCurrentCastActionField.GetValue(skateMode);
            if (castInfo == null || this.autoIceSkatingCastNormalActionIdProperty == null)
            {
                return 0;
            }

            int normalActionId = Convert.ToInt32(this.autoIceSkatingCastNormalActionIdProperty.GetValue(castInfo, null));
            if (normalActionId <= 0 || normalActionId > AutoIceSkatingMaxActionId)
            {
                return 0;
            }

            return this.TryAutoIceSkatingInvokeGetSkateAction(normalActionId) != null ? normalActionId : 0;
        }

        private unsafe int TryReadAutoIceSkatingAuraCurrentCastNormalActionId(IntPtr skateMode)
        {
            this.TryReadAutoIceSkatingAuraCastActionIds(skateMode, out _, out int normalActionId);
            return normalActionId;
        }

        private bool TryAutoIceSkatingAttemptUltimateTriggerManaged(
            object skateMode,
            int ultimateId,
            int ultimateScore,
            int energyTier,
            bool endgame,
            string candidatesDetail,
            string context,
            float now)
        {
            if (ultimateId <= 0 || !this.AutoIceSkatingIsStartTriggerReady(now))
            {
                return false;
            }

            this.AutoIceSkatingLog(
                "ultimate ready tier=x" + energyTier
                + " ctx=" + context
                + " id=" + ultimateId
                + " score=" + ultimateScore
                + " min=" + this.autoIceSkatingMinUltimateScore
                + " candidates=" + candidatesDetail,
                "ultimate-ready:" + ultimateId + ":" + ultimateScore,
                force: true);
            if (this.TryAutoIceSkatingSkillTrigger(skateMode, ultimateId, now, endgame ? "ultimate-endgame" : "ultimate"))
            {
                this.AutoIceSkatingInvalidateMaxUltimateCache();
                return true;
            }

            return false;
        }

        private unsafe bool TryAutoIceSkatingAttemptUltimateTriggerAura(
            IntPtr skateMode,
            int ultimateId,
            int ultimateScore,
            int energyTier,
            bool endgame,
            string candidatesDetail,
            string context,
            float now)
        {
            if (ultimateId <= 0 || !this.AutoIceSkatingIsStartTriggerReady(now))
            {
                return false;
            }

            this.AutoIceSkatingLog(
                "ultimate ready tier=x" + energyTier
                + " ctx=" + context
                + " id=" + ultimateId
                + " score=" + ultimateScore
                + " min=" + this.autoIceSkatingMinUltimateScore
                + " candidates=" + candidatesDetail,
                "aura-ultimate-ready:" + ultimateId + ":" + ultimateScore,
                force: true);
            if (this.TryAutoIceSkatingAuraSkillTrigger(skateMode, ultimateId, now, endgame ? "ultimate-endgame" : "ultimate"))
            {
                this.AutoIceSkatingInvalidateMaxUltimateCache();
                return true;
            }

            return false;
        }

        private string BuildAutoIceSkatingUltimateCandidatesManaged(
            object skateMode,
            object challengeInfo,
            List<int> skills,
            bool isPairSkate)
        {
            this.autoIceSkatingUltimateCandidatesLogBuffer.Clear();
            this.TryCollectAutoIceSkatingTreeNormalActionIdsManaged(skateMode, skills, this.autoIceSkatingTreeActionIdsBuffer);
            this.autoIceSkatingUltimateIdDedup.Clear();
            int scanLimit = Math.Min(this.autoIceSkatingTreeActionIdsBuffer.Count, AutoIceSkatingMaxTreeActionsScanned);
            for (int i = 0; i < scanLimit; i++)
            {
                int ultimateId = this.TryAutoIceSkatingResolveUltimateActionIdManaged(this.autoIceSkatingTreeActionIdsBuffer[i]);
                if (ultimateId <= 0 || !this.autoIceSkatingUltimateIdDedup.Add(ultimateId))
                {
                    continue;
                }

                if (!this.TryComputeAutoIceSkatingUltimateScoreManaged(
                        skateMode,
                        ultimateId,
                        challengeInfo,
                        isPairSkate,
                        out AutoIceSkatingUltimateScoreDetail detail))
                {
                    continue;
                }

                if (this.autoIceSkatingUltimateCandidatesLogBuffer.Length > 0)
                {
                    this.autoIceSkatingUltimateCandidatesLogBuffer.Append(';');
                }

                float dur = this.EstimateAutoIceSkatingActionDurationManaged(ultimateId);
                this.autoIceSkatingUltimateCandidatesLogBuffer
                    .Append(this.autoIceSkatingTreeActionIdsBuffer[i])
                    .Append("->")
                    .Append(ultimateId)
                    .Append('{')
                    .Append(detail.ToScoreLog())
                    .Append(" dur=")
                    .Append(dur.ToString("0.###"))
                    .Append(detail.FinalScore >= this.autoIceSkatingMinUltimateScore ? " ok" : " below-min")
                    .Append('}');
            }

            return this.autoIceSkatingUltimateCandidatesLogBuffer.Length > 0
                ? this.autoIceSkatingUltimateCandidatesLogBuffer.ToString()
                : "none";
        }

        private unsafe string BuildAutoIceSkatingUltimateCandidatesAura(
            IntPtr skateMode,
            IntPtr challengeInfo,
            List<int> skills,
            bool isPairSkate)
        {
            this.autoIceSkatingUltimateCandidatesLogBuffer.Clear();
            if (!this.TryCollectAutoIceSkatingTreeNormalActionIdsAura(skateMode, skills, this.autoIceSkatingTreeActionIdsBuffer))
            {
                return "none";
            }

            this.autoIceSkatingUltimateIdDedup.Clear();
            int scanLimit = Math.Min(this.autoIceSkatingTreeActionIdsBuffer.Count, AutoIceSkatingMaxTreeActionsScanned);
            for (int i = 0; i < scanLimit; i++)
            {
                int ultimateId = this.TryAutoIceSkatingResolveUltimateActionIdAura(this.autoIceSkatingTreeActionIdsBuffer[i]);
                if (ultimateId <= 0 || !this.autoIceSkatingUltimateIdDedup.Add(ultimateId))
                {
                    continue;
                }

                if (!this.TryComputeAutoIceSkatingUltimateScoreAura(
                        skateMode,
                        ultimateId,
                        challengeInfo,
                        isPairSkate,
                        out AutoIceSkatingUltimateScoreDetail detail))
                {
                    continue;
                }

                if (this.autoIceSkatingUltimateCandidatesLogBuffer.Length > 0)
                {
                    this.autoIceSkatingUltimateCandidatesLogBuffer.Append(';');
                }

                float dur = this.EstimateAutoIceSkatingActionDurationAura(ultimateId);
                this.autoIceSkatingUltimateCandidatesLogBuffer
                    .Append(this.autoIceSkatingTreeActionIdsBuffer[i])
                    .Append("->")
                    .Append(ultimateId)
                    .Append('{')
                    .Append(detail.ToScoreLog())
                    .Append(" dur=")
                    .Append(dur.ToString("0.###"))
                    .Append(detail.FinalScore >= this.autoIceSkatingMinUltimateScore ? " ok" : " below-min")
                    .Append('}');
            }

            return this.autoIceSkatingUltimateCandidatesLogBuffer.Length > 0
                ? this.autoIceSkatingUltimateCandidatesLogBuffer.ToString()
                : "none";
        }

        private bool TryAutoIceSkatingTickUltimateOnIdleManaged(
            object skateMode,
            object challengeInfo,
            List<int> skills,
            float now)
        {
            bool canUltimate = (bool)this.autoIceSkatingCanTriggerUltimateMethod.Invoke(skateMode, null);
            bool endgame = this.autoIceSkatingLast30sUltimate
                && this.AutoIceSkatingIsChallengeEndgameManaged(skateMode, challengeInfo);
            int requiredTier = endgame
                ? 1
                : (this.autoIceSkatingOnlyX2Ultimate ? AutoIceSkatingUltimateEnergyTierRequired : 1);
            if (!canUltimate || !this.AutoIceSkatingIsUltimateEnergyTierReadyManaged(skateMode, requiredTier, out int energyTier))
            {
                return false;
            }

            bool isPairSkate = this.TryAutoIceSkatingIsPairSkateManaged(skateMode);
            string candidatesDetail = this.BuildAutoIceSkatingUltimateCandidatesManaged(
                skateMode,
                challengeInfo,
                skills,
                isPairSkate);
            if (!this.TryAutoIceSkatingSelectUltimateManaged(
                    skateMode,
                    challengeInfo,
                    skills,
                    isPairSkate,
                    out int ultimateId,
                    out int ultimateScore,
                    out int maxSeenScore))
            {
                this.AutoIceSkatingLogUltimateWaitDisabled("idle", energyTier, "no-qualifying", maxSeenScore, candidatesDetail);
                return false;
            }

            return this.TryAutoIceSkatingAttemptUltimateTriggerManaged(
                skateMode,
                ultimateId,
                ultimateScore,
                energyTier,
                endgame,
                candidatesDetail,
                endgame ? "idle-endgame" : "idle",
                now);
        }

        private unsafe bool TryAutoIceSkatingTickUltimateOnIdleAura(
            IntPtr skateMode,
            IntPtr challengeInfo,
            List<int> skills,
            float now)
        {
            bool endgame = this.autoIceSkatingLast30sUltimate && this.AutoIceSkatingIsChallengeEndgameAura(skateMode);
            int requiredTier = endgame
                ? 1
                : (this.autoIceSkatingOnlyX2Ultimate ? AutoIceSkatingUltimateEnergyTierRequired : 1);
            if (!this.TryAutoIceSkatingAuraInvokeBool(skateMode, this.autoIceSkatingAuraCanTriggerUltimateMethod, out bool canUltimate)
                || !canUltimate
                || !this.AutoIceSkatingIsUltimateEnergyTierReadyAura(skateMode, requiredTier, out int energyTier))
            {
                return false;
            }

            bool isPairSkate = this.TryAutoIceSkatingIsPairSkateAura(skateMode);
            string candidatesDetail = this.BuildAutoIceSkatingUltimateCandidatesAura(
                skateMode,
                challengeInfo,
                skills,
                isPairSkate);
            if (!this.TryAutoIceSkatingSelectUltimateAura(
                    skateMode,
                    challengeInfo,
                    skills,
                    isPairSkate,
                    out int ultimateId,
                    out int ultimateScore,
                    out int maxSeenScore))
            {
                this.AutoIceSkatingLogUltimateWaitDisabled("idle", energyTier, "no-qualifying", maxSeenScore, candidatesDetail);
                return false;
            }

            return this.TryAutoIceSkatingAttemptUltimateTriggerAura(
                skateMode,
                ultimateId,
                ultimateScore,
                energyTier,
                endgame,
                candidatesDetail,
                endgame ? "idle-endgame" : "idle",
                now);
        }

        private void TryCollectAutoIceSkatingTreeNormalActionIdsManaged(object skateMode, List<int> skillsFallback, List<int> output)
        {
            output.Clear();
            if (skillsFallback != null)
            {
                for (int i = 0; i < skillsFallback.Count; i++)
                {
                    if (skillsFallback[i] > 0)
                    {
                        output.Add(skillsFallback[i]);
                    }
                }
            }

            if (skateMode == null || this.autoIceSkatingSkateActionsField == null)
            {
                return;
            }

            object dictObj = this.autoIceSkatingSkateActionsField.GetValue(skateMode);
            if (dictObj is IDictionary dict)
            {
                foreach (object key in dict.Keys)
                {
                    int actionId = Convert.ToInt32(key);
                    if (actionId > 0 && !output.Contains(actionId))
                    {
                        output.Add(actionId);
                    }
                }
            }
        }

        private unsafe bool TryCollectAutoIceSkatingTreeNormalActionIdsAura(
            IntPtr skateMode,
            List<int> skillsFallback,
            List<int> output)
        {
            output.Clear();
            if (skillsFallback != null)
            {
                for (int i = 0; i < skillsFallback.Count; i++)
                {
                    if (skillsFallback[i] > 0)
                    {
                        output.Add(skillsFallback[i]);
                    }
                }
            }

            if (skateMode == IntPtr.Zero || auraMonoFieldGetValue == null)
            {
                return output.Count > 0;
            }

            IntPtr dictObj = IntPtr.Zero;
            if (this.autoIceSkatingAuraSkateActionsField != IntPtr.Zero)
            {
                auraMonoFieldGetValue(skateMode, this.autoIceSkatingAuraSkateActionsField, (IntPtr)(&dictObj));
            }
            else if (!this.TryGetMonoObjectMember(skateMode, "_skateActions", out dictObj))
            {
                return output.Count > 0;
            }

            if (dictObj == IntPtr.Zero || auraMonoObjectGetClass == null || auraMonoRuntimeInvoke == null)
            {
                return output.Count > 0;
            }

            IntPtr dictClass = auraMonoObjectGetClass(dictObj);
            IntPtr getKeysMethod = dictClass != IntPtr.Zero
                ? this.FindAuraMonoMethodOnHierarchy(dictClass, "get_Keys", 0)
                : IntPtr.Zero;
            if (getKeysMethod == IntPtr.Zero)
            {
                return output.Count > 0;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr keysObj = auraMonoRuntimeInvoke(getKeysMethod, dictObj, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || keysObj == IntPtr.Zero)
            {
                return output.Count > 0;
            }

            this.autoIceSkatingAuraKeyBuffer.Clear();
            if (!this.TryEnumerateAuraMonoCollectionItems(keysObj, this.autoIceSkatingAuraKeyBuffer))
            {
                return output.Count > 0;
            }

            int limit = Math.Min(this.autoIceSkatingAuraKeyBuffer.Count, AutoIceSkatingMaxTreeActionsScanned);
            for (int i = 0; i < limit; i++)
            {
                IntPtr keyObj = this.autoIceSkatingAuraKeyBuffer[i];
                int actionId = 0;
                if (this.TryUnboxMonoInt32(keyObj, out actionId)
                    || this.TryGetMonoInt32Member(keyObj, "m_value", out actionId)
                    || this.TryGetMonoInt32Member(keyObj, "value__", out actionId))
                {
                    if (actionId > 0 && !output.Contains(actionId))
                    {
                        output.Add(actionId);
                    }
                }
            }

            return output.Count > 0;
        }

        private bool TryAutoIceSkatingSelectUltimateManaged(
            object skateMode,
            object challengeInfo,
            List<int> skills,
            bool isPairSkate,
            out int ultimateId,
            out int ultimateScore,
            out int maxSeenScore)
        {
            ultimateId = 0;
            ultimateScore = 0;
            maxSeenScore = this.autoIceSkatingLastMaxUltimateScore;
            int skillsHash = AutoIceSkatingHashSkills(skills);
            float now = Time.unscaledTime;
            if (this.autoIceSkatingCachedMaxUltimateId > 0
                && skillsHash == this.autoIceSkatingCachedMaxUltimateSkillsHash
                && now - this.autoIceSkatingCachedMaxUltimateAt < AutoIceSkatingMaxUltimateCacheSeconds)
            {
                ultimateId = this.autoIceSkatingCachedMaxUltimateId;
                ultimateScore = this.autoIceSkatingCachedMaxUltimateScore;
                return true;
            }

            if (skateMode == null || this.autoIceSkatingGetSkateActionTypeMethod == null)
            {
                return false;
            }

            this.TryCollectAutoIceSkatingTreeNormalActionIdsManaged(skateMode, skills, this.autoIceSkatingTreeActionIdsBuffer);
            if (this.autoIceSkatingTreeActionIdsBuffer.Count == 0)
            {
                return false;
            }

            maxSeenScore = 0;
            float bestDuration = float.PositiveInfinity;
            this.autoIceSkatingUltimateIdDedup.Clear();
            int scanLimit = Math.Min(this.autoIceSkatingTreeActionIdsBuffer.Count, AutoIceSkatingMaxTreeActionsScanned);
            for (int i = 0; i < scanLimit; i++)
            {
                int candidateId = this.TryAutoIceSkatingResolveUltimateActionIdManaged(this.autoIceSkatingTreeActionIdsBuffer[i]);
                if (candidateId <= 0 || !this.autoIceSkatingUltimateIdDedup.Add(candidateId))
                {
                    continue;
                }

                int score = this.ScoreAutoIceSkatingUltimateManaged(
                    skateMode,
                    candidateId,
                    challengeInfo,
                    isPairSkate,
                    out _);
                if (score > maxSeenScore)
                {
                    maxSeenScore = score;
                }

                if (score < this.autoIceSkatingMinUltimateScore)
                {
                    continue;
                }

                // Among qualifying (>= 900) ultimates pick the shortest one; tie -> higher score.
                float duration = this.EstimateAutoIceSkatingActionDurationManaged(candidateId);
                if (ultimateId == 0
                    || duration < bestDuration
                    || (duration == bestDuration && score > ultimateScore))
                {
                    ultimateId = candidateId;
                    ultimateScore = score;
                    bestDuration = duration;
                }
            }

            this.autoIceSkatingLastMaxUltimateScore = maxSeenScore;
            if (ultimateId <= 0)
            {
                return false;
            }

            this.autoIceSkatingCachedMaxUltimateId = ultimateId;
            this.autoIceSkatingCachedMaxUltimateScore = ultimateScore;
            this.autoIceSkatingCachedMaxUltimateSkillsHash = skillsHash;
            this.autoIceSkatingCachedMaxUltimateAt = now;
            return true;
        }

        private unsafe bool TryAutoIceSkatingSelectUltimateAura(
            IntPtr skateMode,
            IntPtr challengeInfo,
            List<int> skills,
            bool isPairSkate,
            out int ultimateId,
            out int ultimateScore,
            out int maxSeenScore)
        {
            ultimateId = 0;
            ultimateScore = 0;
            maxSeenScore = this.autoIceSkatingLastMaxUltimateScore;
            int skillsHash = AutoIceSkatingHashSkills(skills);
            float now = Time.unscaledTime;
            if (this.autoIceSkatingCachedMaxUltimateId > 0
                && skillsHash == this.autoIceSkatingCachedMaxUltimateSkillsHash
                && now - this.autoIceSkatingCachedMaxUltimateAt < AutoIceSkatingMaxUltimateCacheSeconds)
            {
                ultimateId = this.autoIceSkatingCachedMaxUltimateId;
                ultimateScore = this.autoIceSkatingCachedMaxUltimateScore;
                return true;
            }

            if (skateMode == IntPtr.Zero || this.autoIceSkatingAuraGetSkateActionTypeMethod == IntPtr.Zero)
            {
                return false;
            }

            if (!this.TryCollectAutoIceSkatingTreeNormalActionIdsAura(skateMode, skills, this.autoIceSkatingTreeActionIdsBuffer))
            {
                return false;
            }

            maxSeenScore = 0;
            float bestDuration = float.PositiveInfinity;
            this.autoIceSkatingUltimateIdDedup.Clear();
            int scanLimit = Math.Min(this.autoIceSkatingTreeActionIdsBuffer.Count, AutoIceSkatingMaxTreeActionsScanned);
            for (int i = 0; i < scanLimit; i++)
            {
                int candidateId = this.TryAutoIceSkatingResolveUltimateActionIdAura(this.autoIceSkatingTreeActionIdsBuffer[i]);
                if (candidateId <= 0 || !this.autoIceSkatingUltimateIdDedup.Add(candidateId))
                {
                    continue;
                }

                int score = this.ScoreAutoIceSkatingUltimateAura(
                    skateMode,
                    candidateId,
                    challengeInfo,
                    isPairSkate,
                    out _);
                if (score > maxSeenScore)
                {
                    maxSeenScore = score;
                }

                if (score < this.autoIceSkatingMinUltimateScore)
                {
                    continue;
                }

                // Among qualifying (>= 900) ultimates pick the shortest one; tie -> higher score.
                float duration = this.EstimateAutoIceSkatingActionDurationAura(candidateId);
                if (ultimateId == 0
                    || duration < bestDuration
                    || (duration == bestDuration && score > ultimateScore))
                {
                    ultimateId = candidateId;
                    ultimateScore = score;
                    bestDuration = duration;
                }
            }

            this.autoIceSkatingLastMaxUltimateScore = maxSeenScore;
            if (ultimateId <= 0)
            {
                return false;
            }

            this.autoIceSkatingCachedMaxUltimateId = ultimateId;
            this.autoIceSkatingCachedMaxUltimateScore = ultimateScore;
            this.autoIceSkatingCachedMaxUltimateSkillsHash = skillsHash;
            this.autoIceSkatingCachedMaxUltimateAt = now;
            return true;
        }

        private bool TryAutoIceSkatingSkillTrigger(object skateMode, int actionId, float now, string reason, bool applyCooldown = true)
        {
            try
            {
                this.autoIceSkatingSkillTriggerMethod.Invoke(skateMode, new object[] { actionId });
                if (applyCooldown)
                {
                    this.autoIceSkatingLastTriggerAt = now;
                }
                this.AutoIceSkatingSetStatus("Triggered " + actionId + " (" + reason + ").", force: true);
                object challengeInfo = this.autoIceSkatingChallengeInfoField != null
                    ? this.autoIceSkatingChallengeInfoField.GetValue(skateMode)
                    : null;
                this.AutoIceSkatingLog(
                    "SkillTrigger(" + actionId + ") reason=" + reason
                    + " {" + this.DescribeAutoIceSkatingActionManaged(actionId, challengeInfo) + "}",
                    force: true);
                return true;
            }
            catch (Exception ex)
            {
                this.AutoIceSkatingSetStatus("SkillTrigger failed: " + ex.Message, force: true);
                this.AutoIceSkatingLog("SkillTrigger(" + actionId + ") failed: " + ex, force: true);
                return false;
            }
        }

        private int PickAutoIceSkatingBestSkill(
            object skateMode,
            object challengeInfo,
            List<int> skills,
            int preferDifferentFrom,
            out string detail)
        {
            int bestId = 0;
            bool bestNew = false;
            float bestDuration = float.PositiveInfinity;
            int fallbackId = 0;
            bool fallbackNew = false;
            float fallbackDuration = float.PositiveInfinity;
            bool inChallenge = this.AutoIceSkatingIsChallengeManaged(skateMode);
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < skills.Count; i++)
            {
                int actionId = skills[i];
                if (actionId <= 0)
                {
                    continue;
                }

                // Strategy: among simple actions prefer NEW ones (challenge novelty bonus),
                // then the SHORTEST duration.
                bool isNew = inChallenge
                    && challengeInfo != null
                    && this.autoIceSkatingChallengeIsNewActionMethod != null
                    && (bool)this.autoIceSkatingChallengeIsNewActionMethod.Invoke(challengeInfo, new object[] { actionId });
                float duration = this.EstimateAutoIceSkatingActionDurationManaged(actionId);
                if (sb.Length > 0)
                {
                    sb.Append(" | ");
                }

                sb.Append('{').Append(this.DescribeAutoIceSkatingActionManaged(actionId, challengeInfo)).Append('}');
                if (actionId != preferDifferentFrom)
                {
                    if (AutoIceSkatingPreferAction(isNew, duration, bestId, bestNew, bestDuration))
                    {
                        bestId = actionId;
                        bestNew = isNew;
                        bestDuration = duration;
                    }
                }
                else if (AutoIceSkatingPreferAction(isNew, duration, fallbackId, fallbackNew, fallbackDuration))
                {
                    fallbackId = actionId;
                    fallbackNew = isNew;
                    fallbackDuration = duration;
                }
            }

            int picked = bestId > 0 ? bestId : fallbackId;
            detail = "candidates " + sb + " picked=" + picked;
            return picked;
        }

        // When "prefer new" is on, new actions outrank used ones; within the same novelty
        // class (or always, if "prefer new" is off) the shortest duration wins.
        private bool AutoIceSkatingPreferAction(
            bool candidateNew,
            float candidateDuration,
            int currentId,
            bool currentNew,
            float currentDuration)
        {
            if (currentId <= 0)
            {
                return true;
            }

            if (this.autoIceSkatingPreferNewMove && candidateNew != currentNew)
            {
                return candidateNew;
            }

            return candidateDuration < currentDuration;
        }

        private static int AutoIceSkatingReadMemberInt(object target, string name)
        {
            if (target == null)
            {
                return 0;
            }

            try
            {
                Type type = target.GetType();
                PropertyInfo property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (property != null)
                {
                    return Convert.ToInt32(property.GetValue(target, null));
                }

                FieldInfo field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                return field != null ? Convert.ToInt32(field.GetValue(target)) : 0;
            }
            catch
            {
                return 0;
            }
        }

        private static float AutoIceSkatingReadMemberFloat(object target, string name)
        {
            if (target == null)
            {
                return 0f;
            }

            try
            {
                Type type = target.GetType();
                PropertyInfo property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (property != null)
                {
                    return Convert.ToSingle(property.GetValue(target, null));
                }

                FieldInfo field = type.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                return field != null ? Convert.ToSingle(field.GetValue(target)) : 0f;
            }
            catch
            {
                return 0f;
            }
        }

        // Full property dump for one action so the log shows how candidates differ.
        private string DescribeAutoIceSkatingActionManaged(int actionId, object challengeInfo)
        {
            object config = this.TryAutoIceSkatingInvokeGetSkateAction(actionId);
            if (config == null)
            {
                return "id=" + actionId + "<no-config>";
            }

            int score = AutoIceSkatingReadMemberInt(config, "score");
            int bonus = AutoIceSkatingReadMemberInt(config, "bonusScore");
            int type = AutoIceSkatingReadMemberInt(config, "actionType");
            int energy = AutoIceSkatingReadMemberInt(config, "energy");
            int iconTip = AutoIceSkatingReadMemberInt(config, "iconTipCount");
            int pair = AutoIceSkatingReadMemberInt(config, "pairMotion");
            float prefScore = AutoIceSkatingReadMemberFloat(config, "prefectScoreRatio");
            float prefEnergy = AutoIceSkatingReadMemberFloat(config, "prefectEnergyRatio");
            float dur = this.EstimateAutoIceSkatingActionDurationManaged(actionId);
            bool isNew = challengeInfo != null
                && this.autoIceSkatingChallengeIsNewActionMethod != null
                && (bool)this.autoIceSkatingChallengeIsNewActionMethod.Invoke(challengeInfo, new object[] { actionId });
            int ultId = this.TryAutoIceSkatingResolveUltimateActionIdManaged(actionId);
            return "id=" + actionId
                + " type=" + type
                + " dur=" + dur.ToString("0.###")
                + " score=" + score
                + " bonus=" + bonus
                + " new=" + (isNew ? 1 : 0)
                + " prefScore=" + prefScore.ToString("0.##")
                + " energy=" + energy
                + " prefEnergy=" + prefEnergy.ToString("0.##")
                + " iconTip=" + iconTip
                + " pair=" + pair
                + " ult=" + ultId;
        }

        private unsafe string DescribeAutoIceSkatingActionAura(IntPtr skateMode, int actionId)
        {
            IntPtr config = this.TryAutoIceSkatingAuraGetSkateActionRow(actionId);
            if (config == IntPtr.Zero)
            {
                return "id=" + actionId + "<no-config>";
            }

            this.TryGetMonoInt32Member(config, "score", out int score);
            this.TryGetMonoInt32Member(config, "bonusScore", out int bonus);
            this.TryGetMonoInt32Member(config, "actionType", out int type);
            this.TryGetMonoInt32Member(config, "energy", out int energy);
            this.TryGetMonoInt32Member(config, "iconTipCount", out int iconTip);
            this.TryGetMonoInt32Member(config, "pairMotion", out int pair);
            this.TryGetMonoSingleMember(config, "prefectScoreRatio", out float prefScore);
            this.TryGetMonoSingleMember(config, "prefectEnergyRatio", out float prefEnergy);
            float dur = this.EstimateAutoIceSkatingActionDurationAura(actionId);
            bool isNew = this.TryAutoIceSkatingAuraChallengeIsNewAction(skateMode, actionId);
            int ultId = this.TryAutoIceSkatingResolveUltimateActionIdAura(actionId);
            return "id=" + actionId
                + " type=" + type
                + " dur=" + dur.ToString("0.###")
                + " score=" + score
                + " bonus=" + bonus
                + " new=" + (isNew ? 1 : 0)
                + " prefScore=" + prefScore.ToString("0.##")
                + " energy=" + energy
                + " prefEnergy=" + prefEnergy.ToString("0.##")
                + " iconTip=" + iconTip
                + " pair=" + pair
                + " ult=" + ultId;
        }

        private float EstimateAutoIceSkatingActionDurationManaged(int actionId)
        {
            object config = this.TryAutoIceSkatingInvokeGetSkateAction(actionId);
            if (config == null)
            {
                return float.PositiveInfinity;
            }

            int[] phaseIds = ReadAutoIceSkatingIntArray(config, this.autoIceSkatingTableSkateActionPrefectPhaseProperty);
            if (phaseIds == null || phaseIds.Length == 0)
            {
                phaseIds = ReadAutoIceSkatingIntArray(config, this.autoIceSkatingTableSkateActionNormalPhaseProperty);
            }

            return this.EstimateAutoIceSkatingPhaseIdsDurationManaged(phaseIds);
        }

        private float EstimateAutoIceSkatingPhaseIdsDurationManaged(int[] phaseIds)
        {
            if (phaseIds == null || phaseIds.Length == 0 || this.autoIceSkatingGetSkateActionStateMethod == null)
            {
                return float.PositiveInfinity;
            }

            float sum = 0f;
            bool any = false;
            for (int i = 0; i < phaseIds.Length; i++)
            {
                object state = this.TryAutoIceSkatingInvokeGetSkateActionState(phaseIds[i]);
                if (state == null)
                {
                    continue;
                }

                float span = this.ReadAutoIceSkatingPhaseSpanManaged(state);
                if (span < 0f)
                {
                    continue;
                }

                sum += span;
                any = true;
            }

            return any ? sum : float.PositiveInfinity;
        }

        private float ReadAutoIceSkatingPhaseSpanManaged(object state)
        {
            if (state == null || this.autoIceSkatingTableSkateActionStatePhaseField == null)
            {
                return -1f;
            }

            object phaseObj = this.autoIceSkatingTableSkateActionStatePhaseField.GetValue(state);
            if (phaseObj is float[] phases && phases.Length >= 2)
            {
                return phases[phases.Length - 1] - phases[0];
            }

            if (phaseObj is IList list && list.Count >= 2)
            {
                float first = Convert.ToSingle(list[0]);
                float last = Convert.ToSingle(list[list.Count - 1]);
                return last - first;
            }

            return -1f;
        }

        private object TryAutoIceSkatingInvokeGetSkateActionState(int stateId)
        {
            if (this.autoIceSkatingGetSkateActionStateMethod == null || stateId <= 0)
            {
                return null;
            }

            ParameterInfo[] parameters = this.autoIceSkatingGetSkateActionStateMethod.GetParameters();
            if (parameters.Length >= 2 && parameters[1].ParameterType == typeof(bool))
            {
                return this.autoIceSkatingGetSkateActionStateMethod.Invoke(null, new object[] { stateId, false });
            }

            return this.autoIceSkatingGetSkateActionStateMethod.Invoke(null, new object[] { stateId });
        }

        private bool TryIsInConfiguredPhase(object skateMode, int[] phaseIds, out float ratioInPhase, out int phaseIndex)
        {
            ratioInPhase = 0f;
            phaseIndex = 0;
            object[] args = { phaseIds, 0f, 0 };
            bool result = (bool)this.autoIceSkatingGetRatioInConfiguredPhaseMethod.Invoke(skateMode, args);
            if (!result)
            {
                return false;
            }

            ratioInPhase = (float)args[1];
            phaseIndex = (int)args[2];
            return ratioInPhase > AutoIceSkatingMinPerfectRatio && ratioInPhase <= 1f;
        }

        private int TryReadCurrentCastActionId(object skateMode)
        {
            object castInfo = this.autoIceSkatingCurrentCastActionField.GetValue(skateMode);
            if (castInfo == null)
            {
                return 0;
            }

            int actionId = Convert.ToInt32(this.autoIceSkatingCastActionIdField.GetValue(castInfo));
            if (actionId <= 0 || actionId > AutoIceSkatingMaxActionId)
            {
                return 0;
            }

            object row = this.TryAutoIceSkatingInvokeGetSkateAction(actionId);
            return row != null ? actionId : 0;
        }

        private int TryReadSkateActionId(object config)
        {
            if (config == null || this.autoIceSkatingTableSkateActionIdProperty == null)
            {
                return 0;
            }

            return Convert.ToInt32(this.autoIceSkatingTableSkateActionIdProperty.GetValue(config, null));
        }

        private object TryAutoIceSkatingInvokeGetSkateAction(int actionId)
        {
            if (this.autoIceSkatingGetSkateActionMethod == null || actionId <= 0)
            {
                return null;
            }

            ParameterInfo[] parameters = this.autoIceSkatingGetSkateActionMethod.GetParameters();
            if (parameters != null && parameters.Length >= 2)
            {
                return this.autoIceSkatingGetSkateActionMethod.Invoke(null, new object[] { actionId, false });
            }

            return this.autoIceSkatingGetSkateActionMethod.Invoke(null, new object[] { actionId });
        }

        private static int[] ReadAutoIceSkatingIntArray(object target, PropertyInfo property)
        {
            if (target == null || property == null)
            {
                return null;
            }

            object value = property.GetValue(target, null);
            if (value is int[] direct)
            {
                return direct;
            }

            if (value is IList list && list.Count > 0)
            {
                int[] converted = new int[list.Count];
                for (int i = 0; i < list.Count; i++)
                {
                    converted[i] = Convert.ToInt32(list[i]);
                }

                return converted;
            }

            return null;
        }

        private void TryCollectAutoIceSkatingSkills(object skateMode, out List<int> skills, out string detail)
        {
            skills = new List<int>();
            StringBuilder sb = new StringBuilder();
            object skillsObj = this.autoIceSkatingSkateSkillsProperty.GetValue(skateMode, null);
            if (skillsObj == null)
            {
                detail = "SkateSkills=null";
                return;
            }

            sb.Append("type=").Append(skillsObj.GetType().FullName).Append(' ');
            if (skillsObj is IList list)
            {
                sb.Append("IList.Count=").Append(list.Count).Append(' ');
                for (int i = 0; i < list.Count; i++)
                {
                    try
                    {
                        int id = Convert.ToInt32(list[i]);
                        if (id > 0)
                        {
                            skills.Add(id);
                        }
                    }
                    catch (Exception ex)
                    {
                        sb.Append("idx").Append(i).Append(" err=").Append(ex.Message).Append(' ');
                    }
                }
            }
            else if (skillsObj is IEnumerable enumerable)
            {
                int index = 0;
                foreach (object entry in enumerable)
                {
                    try
                    {
                        int id = Convert.ToInt32(entry);
                        if (id > 0)
                        {
                            skills.Add(id);
                        }
                    }
                    catch (Exception ex)
                    {
                        sb.Append("enum").Append(index).Append(" err=").Append(ex.Message).Append(' ');
                    }

                    index++;
                }

                sb.Append("enumerated=").Append(index).Append(' ');
            }
            else if (this.TryGetObjectMember(skillsObj, "Count", out object countObj)
                && this.TryInvokeIntIndexer(skillsObj, out List<int> indexed))
            {
                skills.AddRange(indexed);
                sb.Append("indexed.Count=").Append(countObj).Append(' ');
            }
            else
            {
                sb.Append("unreadable");
            }

            detail = sb.ToString().Trim();
        }

        private bool TryInvokeIntIndexer(object listObj, out List<int> values)
        {
            values = new List<int>();
            if (listObj == null || !this.TryGetObjectMember(listObj, "Count", out object countObj))
            {
                return false;
            }

            int count = Convert.ToInt32(countObj);
            MethodInfo getItem = listObj.GetType().GetMethod("get_Item", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(int) }, null);
            if (getItem == null)
            {
                return false;
            }

            for (int i = 0; i < count; i++)
            {
                object entry = getItem.Invoke(listObj, new object[] { i });
                values.Add(Convert.ToInt32(entry));
            }

            return true;
        }

        private bool TryGetAutoIceSkatingMode(out object localPlayer, out object skateMode, out string source)
        {
            localPlayer = null;
            skateMode = null;
            source = "unresolved";

            if (!this.TryResolveAutoIceSkatingLocalPlayer(out localPlayer, out string playerSource))
            {
                source = "no LocalPlayerComponent (" + playerSource + ")";
                return false;
            }

            if (this.TryResolveAutoIceSkatingModeFromPlayer(localPlayer, out skateMode, out string modePath))
            {
                source = playerSource + " -> " + modePath;
                return true;
            }

            if (this.TryGetObjectMember(localPlayer, "character", out object characterObj)
                && characterObj != null
                && this.TryResolveAutoIceSkatingModeFromCharacter(characterObj, out skateMode, out modePath))
            {
                source = playerSource + " -> character -> " + modePath;
                return true;
            }

            source = playerSource + " -> GameSkateMode null";
            return false;
        }

        private bool TryResolveAutoIceSkatingLocalPlayer(out object localPlayer, out string source)
        {
            localPlayer = null;
            source = "none";
            this.EnsureAutoIceSkatingLocalPlayerTypeResolved();
            if (this.autoIceSkatingLocalPlayerComponentType == null)
            {
                source = "LocalPlayerComponent type missing";
                return false;
            }

            object candidate = null;
            string candidateSource = "none";
            if (this.TryGetManagedSelfPlayerObject(out candidate, out candidateSource)
                && this.TryAutoIceSkatingAcceptPlayerCandidate(candidate, candidateSource, out localPlayer, out source))
            {
                return true;
            }

            if (this.TryGetManagedViewModuleSelfPlayerObject(out candidate, out candidateSource)
                && this.TryAutoIceSkatingAcceptPlayerCandidate(candidate, candidateSource, out localPlayer, out source))
            {
                return true;
            }

            if (this.TryGetManagedSelfPlayerEntityObject(out candidate, out candidateSource)
                && this.TryAutoIceSkatingAcceptPlayerCandidate(candidate, candidateSource, out localPlayer, out source))
            {
                return true;
            }

            source = "all player fallbacks failed";
            return false;
        }

        private bool TryAutoIceSkatingAcceptPlayerCandidate(object candidate, string candidateSource, out object localPlayer, out string source)
        {
            localPlayer = null;
            source = candidateSource ?? "none";
            if (candidate == null)
            {
                return false;
            }

            try
            {
                if (this.autoIceSkatingLocalPlayerComponentType.IsInstanceOfType(candidate))
                {
                    localPlayer = candidate;
                    return true;
                }

                if (this.TryGetComponentOnObject(candidate, this.autoIceSkatingLocalPlayerComponentType, out object component))
                {
                    localPlayer = component;
                    source = candidateSource + " -> GetComponent<LocalPlayerComponent>";
                    return true;
                }
            }
            catch (Exception ex)
            {
                this.AutoIceSkatingLog("player candidate failed: " + ex.Message, "player-candidate");
            }

            return false;
        }

        private bool TryResolveAutoIceSkatingModeFromPlayer(object localPlayer, out object skateMode, out string path)
        {
            skateMode = null;
            path = "GetGameMode";
            if (this.autoIceSkatingGetGameModeGeneric == null)
            {
                return false;
            }

            try
            {
                MethodInfo getGameMode = this.autoIceSkatingGetGameModeGeneric.MakeGenericMethod(this.autoIceSkatingGameSkateModeType);
                skateMode = getGameMode.Invoke(localPlayer, null);
                return skateMode != null;
            }
            catch (Exception ex)
            {
                this.AutoIceSkatingLog("GetGameMode failed: " + ex.Message, "getgamemode-fail");
                return false;
            }
        }

        private bool TryResolveAutoIceSkatingModeFromCharacter(object characterObj, out object skateMode, out string path)
        {
            skateMode = null;
            path = "Character.GetMode";
            if (this.autoIceSkatingCharacterGetModeGeneric == null)
            {
                return false;
            }

            try
            {
                MethodInfo getMode = this.autoIceSkatingCharacterGetModeGeneric.MakeGenericMethod(this.autoIceSkatingGameSkateModeType);
                skateMode = getMode.Invoke(characterObj, null);
                return skateMode != null;
            }
            catch (Exception ex)
            {
                this.AutoIceSkatingLog("Character.GetMode failed: " + ex.Message, "getmode-fail");
                return false;
            }
        }

        private bool TryGetComponentOnObject(object host, Type componentType, out object component)
        {
            component = null;
            if (host == null || componentType == null)
            {
                return false;
            }

            try
            {
                if (componentType.IsInstanceOfType(host))
                {
                    component = host;
                    return true;
                }

                Type hostType = host.GetType();
                MethodInfo getComponent = null;
                MethodInfo[] methods = hostType.GetMethods(BindingFlags.Public | BindingFlags.Instance);
                for (int i = 0; i < methods.Length; i++)
                {
                    MethodInfo method = methods[i];
                    if (method != null
                        && string.Equals(method.Name, "GetComponent", StringComparison.Ordinal)
                        && method.IsGenericMethodDefinition
                        && method.GetParameters().Length == 0)
                    {
                        getComponent = method;
                        break;
                    }
                }

                if (getComponent == null)
                {
                    return false;
                }

                component = getComponent.MakeGenericMethod(componentType).Invoke(host, null);
                return component != null;
            }
            catch
            {
                return false;
            }
        }

        private void EnsureAutoIceSkatingLocalPlayerTypeResolved()
        {
            if (this.autoIceSkatingLocalPlayerComponentType != null)
            {
                return;
            }

            this.autoIceSkatingLocalPlayerComponentType = this.FindLoadedType(
                "XDTLevelAndEntity.Gameplay.Component.Player.LocalPlayerComponent",
                "ScriptsRefactory.LevelAndEntity.Gameplay.Component.Player.LocalPlayerComponent",
                "Il2CppXDTLevelAndEntity.Gameplay.Component.Player.LocalPlayerComponent",
                "LocalPlayerComponent");
        }

        private bool TryResolveAutoIceSkatingReflection(out string detail)
        {
            detail = string.Empty;
            if (this.autoIceSkatingSkillTriggerMethod != null
                || (this.autoIceSkatingUsesAura && this.autoIceSkatingAuraSkillTriggerMethod != IntPtr.Zero))
            {
                return true;
            }

            float now = Time.unscaledTime;
            if (now < this.autoIceSkatingReflectionRetryAt)
            {
                detail = "Reflection retry in " + (this.autoIceSkatingReflectionRetryAt - now).ToString("0.0") + "s.";
                return false;
            }

            this.autoIceSkatingReflectionRetryAt = now + AutoIceSkatingReflectionRetrySeconds;
            if (this.TryResolveAutoIceSkatingManagedReflection(out detail))
            {
                this.autoIceSkatingUsesAura = false;
                return true;
            }

            if (this.TryResolveAutoIceSkatingAuraReflection(out detail))
            {
                this.autoIceSkatingUsesAura = true;
                return true;
            }

            return false;
        }

        private bool TryResolveAutoIceSkatingManagedReflection(out string detail)
        {
            detail = string.Empty;
            StringBuilder missing = new StringBuilder();

            this.EnsureAutoIceSkatingLocalPlayerTypeResolved();
            this.autoIceSkatingGameSkateModeType = this.FindLoadedType(
                "XDTLevelAndEntity.Game.GameMode.GameSkateMode",
                "ScriptsRefactory.LevelAndEntity.Game.GameMode.GameSkateMode",
                "Il2CppXDTLevelAndEntity.Game.GameMode.GameSkateMode",
                "GameSkateMode");
            this.autoIceSkatingTableDataType = this.FindLoadedType(
                "XDTDataAndProtocol.Config.TableData",
                "TableData",
                "EcsClient.TableData");
            this.autoIceSkatingTableSkateActionType = this.FindLoadedType(
                "TableSkateAction",
                "EcsClient.TableSkateAction");

            if (this.autoIceSkatingLocalPlayerComponentType == null) missing.Append("LocalPlayerComponent;");
            if (this.autoIceSkatingGameSkateModeType == null) missing.Append("GameSkateMode;");
            if (this.autoIceSkatingTableDataType == null) missing.Append("TableData;");
            if (this.autoIceSkatingTableSkateActionType == null) missing.Append("TableSkateAction;");

            if (missing.Length > 0)
            {
                detail = "Managed types missing: " + missing;
                this.AutoIceSkatingLog(detail, "reflection-managed-missing");
                return false;
            }

            Type characterType = this.FindLoadedType(
                "XDTLevelAndEntity.Game.GameMode.Character",
                "ScriptsRefactory.LevelAndEntity.Game.GameMode.Character",
                "Character");
            if (characterType != null)
            {
                MethodInfo[] characterMethods = characterType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                for (int i = 0; i < characterMethods.Length; i++)
                {
                    MethodInfo method = characterMethods[i];
                    if (method != null
                        && string.Equals(method.Name, "GetMode", StringComparison.Ordinal)
                        && method.IsGenericMethodDefinition
                        && method.GetParameters().Length == 0)
                    {
                        this.autoIceSkatingCharacterGetModeGeneric = method;
                        break;
                    }
                }
            }

            this.autoIceSkatingGetGameModeGeneric = this.autoIceSkatingLocalPlayerComponentType.GetMethod(
                "GetGameMode",
                BindingFlags.Public | BindingFlags.Instance);
            this.autoIceSkatingGetSkateActionMethod = this.autoIceSkatingTableDataType.GetMethod(
                "GetSkateAction",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(int), typeof(bool) },
                null);
            if (this.autoIceSkatingGetSkateActionMethod == null)
            {
                this.autoIceSkatingGetSkateActionMethod = this.autoIceSkatingTableDataType.GetMethod(
                    "GetSkateAction",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(int) },
                    null);
            }
            this.autoIceSkatingSkillTriggerMethod = this.autoIceSkatingGameSkateModeType.GetMethod(
                "SkillTrigger",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(int) },
                null);
            this.autoIceSkatingCanTriggerUltimateMethod = this.autoIceSkatingGameSkateModeType.GetMethod(
                "CanTriggerUltimate",
                BindingFlags.Public | BindingFlags.Instance);
            this.autoIceSkatingCalculateSpeedRateMethod = this.autoIceSkatingGameSkateModeType.GetMethod(
                "CalculateSpeedRate",
                BindingFlags.Public | BindingFlags.Instance);
            this.autoIceSkatingIsReceiverMethod = this.autoIceSkatingGameSkateModeType.GetMethod(
                "IsReceiver",
                BindingFlags.Public | BindingFlags.Instance);
            this.autoIceSkatingGetRatioInConfiguredPhaseMethod = this.autoIceSkatingGameSkateModeType.GetMethod(
                "GetRatioInConfiguredPhase",
                BindingFlags.NonPublic | BindingFlags.Instance,
                null,
                new[] { typeof(int[]), typeof(float).MakeByRefType(), typeof(int).MakeByRefType() },
                null);

            this.autoIceSkatingGameModeActivedProperty = this.autoIceSkatingGameSkateModeType.GetProperty(
                "actived",
                BindingFlags.Public | BindingFlags.Instance);
            this.autoIceSkatingCurrentModeProperty = this.autoIceSkatingGameSkateModeType.GetProperty(
                "CurrentMode",
                BindingFlags.Public | BindingFlags.Instance);
            this.autoIceSkatingUltimateSkillProperty = this.autoIceSkatingGameSkateModeType.GetProperty(
                "UltimateSkill",
                BindingFlags.Public | BindingFlags.Instance);
            this.autoIceSkatingEnergyProperty = this.autoIceSkatingGameSkateModeType.GetProperty(
                "Energy",
                BindingFlags.Public | BindingFlags.Instance);
            this.autoIceSkatingSkateSkillsProperty = this.autoIceSkatingGameSkateModeType.GetProperty(
                "SkateSkills",
                BindingFlags.Public | BindingFlags.Instance);
            this.autoIceSkatingCurrentCastActionField = this.autoIceSkatingGameSkateModeType.GetField(
                "_currentCastAction",
                BindingFlags.NonPublic | BindingFlags.Instance);
            this.autoIceSkatingChallengeInfoField = this.autoIceSkatingGameSkateModeType.GetField(
                "ChallengeInfo",
                BindingFlags.Public | BindingFlags.Instance);

            StringBuilder apiMissing = new StringBuilder();
            if (this.autoIceSkatingGetGameModeGeneric == null || !this.autoIceSkatingGetGameModeGeneric.IsGenericMethodDefinition) apiMissing.Append("GetGameMode;");
            if (this.autoIceSkatingGetSkateActionMethod == null) apiMissing.Append("GetSkateAction;");
            if (this.autoIceSkatingSkillTriggerMethod == null) apiMissing.Append("SkillTrigger;");
            if (this.autoIceSkatingCanTriggerUltimateMethod == null) apiMissing.Append("CanTriggerUltimate;");
            if (this.autoIceSkatingIsReceiverMethod == null) apiMissing.Append("IsReceiver;");
            if (this.autoIceSkatingGetRatioInConfiguredPhaseMethod == null) apiMissing.Append("GetRatioInConfiguredPhase;");
            if (this.autoIceSkatingGameModeActivedProperty == null) apiMissing.Append("actived;");
            if (this.autoIceSkatingUltimateSkillProperty == null) apiMissing.Append("UltimateSkill;");
            if (this.autoIceSkatingEnergyProperty == null) apiMissing.Append("Energy;");
            if (this.autoIceSkatingSkateSkillsProperty == null) apiMissing.Append("SkateSkills;");
            if (this.autoIceSkatingCurrentCastActionField == null) apiMissing.Append("_currentCastAction;");
            if (this.autoIceSkatingChallengeInfoField == null) apiMissing.Append("ChallengeInfo;");

            if (apiMissing.Length > 0)
            {
                detail = "API missing: " + apiMissing;
                this.AutoIceSkatingLog(detail, "reflection-api", force: true);
                return false;
            }

            Type castActionInfoType = this.autoIceSkatingCurrentCastActionField.FieldType;
            this.autoIceSkatingCastActionIdField = castActionInfoType.GetField(
                "actionID",
                BindingFlags.Public | BindingFlags.Instance);
            if (this.autoIceSkatingCastActionIdField == null)
            {
                this.autoIceSkatingCastActionIdField = castActionInfoType.GetField(
                    "actionId",
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            }

            Type challengeDataType = this.autoIceSkatingChallengeInfoField.FieldType;
            this.autoIceSkatingChallengeIsNewActionMethod = challengeDataType.GetMethod(
                "IsNewAction",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                new[] { typeof(int) },
                null);
            this.autoIceSkatingChallengeRemainingTimeProperty = challengeDataType.GetProperty(
                "RemainingTime",
                BindingFlags.Public | BindingFlags.Instance);

            this.autoIceSkatingTableSkateActionScoreProperty = this.autoIceSkatingTableSkateActionType.GetProperty("score");
            this.autoIceSkatingTableSkateActionBonusScoreProperty = this.autoIceSkatingTableSkateActionType.GetProperty("bonusScore");
            this.autoIceSkatingTableSkateActionPrefectPhaseProperty = this.autoIceSkatingTableSkateActionType.GetProperty("prefectPhase");
            this.autoIceSkatingTableSkateActionNormalPhaseProperty = this.autoIceSkatingTableSkateActionType.GetProperty("normalPhase");
            this.autoIceSkatingTableSkateActionIdProperty = this.autoIceSkatingTableSkateActionType.GetProperty("id");
            this.autoIceSkatingTableSkateActionActionTypeProperty = this.autoIceSkatingTableSkateActionType.GetProperty("actionType");
            this.autoIceSkatingTableSkateActionIconTipCountProperty = this.autoIceSkatingTableSkateActionType.GetProperty("iconTipCount");
            this.autoIceSkatingTableSkateActionPairMotionProperty = this.autoIceSkatingTableSkateActionType.GetProperty("pairMotion");
            this.autoIceSkatingGetSkateActionTypeMethod = this.autoIceSkatingTableDataType.GetMethod(
                "GetSkateActionType",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(int), typeof(bool) },
                null);
            if (this.autoIceSkatingGetSkateActionTypeMethod == null)
            {
                this.autoIceSkatingGetSkateActionTypeMethod = this.autoIceSkatingTableDataType.GetMethod(
                    "GetSkateActionType",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(int) },
                    null);
            }

            if (this.autoIceSkatingGetSkateActionTypeMethod != null)
            {
                Type tableSkateActionTypeType = this.autoIceSkatingGetSkateActionTypeMethod.ReturnType;
                this.autoIceSkatingTableSkateActionTypeUltimateActionIdProperty = tableSkateActionTypeType.GetProperty("ultimateActionId");
            }

            this.autoIceSkatingGetSkateActionStateMethod = this.autoIceSkatingTableDataType.GetMethod(
                "GetSkateActionState",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(int), typeof(bool) },
                null);
            if (this.autoIceSkatingGetSkateActionStateMethod == null)
            {
                this.autoIceSkatingGetSkateActionStateMethod = this.autoIceSkatingTableDataType.GetMethod(
                    "GetSkateActionState",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(int) },
                    null);
            }

            if (this.autoIceSkatingGetSkateActionStateMethod != null)
            {
                Type tableSkateActionStateType = this.autoIceSkatingGetSkateActionStateMethod.ReturnType;
                this.autoIceSkatingTableSkateActionStatePhaseField = tableSkateActionStateType.GetField("phase");
            }

            this.autoIceSkatingGetPairSkateUltimateMethod = this.autoIceSkatingTableDataType.GetMethod(
                "GetPairSkateUltimate",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(int), typeof(bool) },
                null);
            if (this.autoIceSkatingGetPairSkateUltimateMethod == null)
            {
                this.autoIceSkatingGetPairSkateUltimateMethod = this.autoIceSkatingTableDataType.GetMethod(
                    "GetPairSkateUltimate",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(int) },
                    null);
            }

            if (this.autoIceSkatingGetPairSkateUltimateMethod != null)
            {
                Type tablePairSkateUltimateType = this.autoIceSkatingGetPairSkateUltimateMethod.ReturnType;
                this.autoIceSkatingTablePairSkateUltimateScoreProperty = tablePairSkateUltimateType.GetProperty("score");
                this.autoIceSkatingTablePairSkateUltimateBonusScoreProperty = tablePairSkateUltimateType.GetProperty("bonusScore");
            }

            this.autoIceSkatingCheckPairSkateMethod = this.autoIceSkatingGameSkateModeType.GetMethod(
                "CheckPairSkate",
                BindingFlags.Public | BindingFlags.Instance,
                null,
                Type.EmptyTypes,
                null);
            this.autoIceSkatingSkateActionsField = this.autoIceSkatingGameSkateModeType.GetField(
                "_skateActions",
                BindingFlags.NonPublic | BindingFlags.Instance);
            this.autoIceSkatingCastNormalActionIdProperty = castActionInfoType.GetProperty(
                "normalActionID",
                BindingFlags.Public | BindingFlags.Instance);

            if (this.autoIceSkatingCastActionIdField == null
                || this.autoIceSkatingTableSkateActionScoreProperty == null
                || this.autoIceSkatingTableSkateActionPrefectPhaseProperty == null)
            {
                detail = "Table/cast API shape mismatch.";
                this.AutoIceSkatingLog(detail, "reflection-shape", force: true);
                return false;
            }

            detail = "Managed reflection OK.";
            this.AutoIceSkatingLog(
                "managed reflection ok LocalPlayer=" + this.autoIceSkatingLocalPlayerComponentType.FullName
                + " GameSkateMode=" + this.autoIceSkatingGameSkateModeType.FullName
                + " TableData=" + this.autoIceSkatingTableDataType.FullName,
                force: true);
            return true;
        }

        private bool TryResolveAutoIceSkatingAuraReflection(out string detail)
        {
            detail = string.Empty;
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                detail = "AuraMono unavailable (enter town).";
                return false;
            }

            StringBuilder missing = new StringBuilder();
            this.autoIceSkatingAuraGameSkateModeClass = this.TryAutoIceSkatingFindAuraClass(
                "XDTLevelAndEntity.Game.GameMode.GameSkateMode",
                "ScriptsRefactory.LevelAndEntity.Game.GameMode.GameSkateMode",
                "GameSkateMode");
            this.autoIceSkatingAuraLocalPlayerClass = this.TryAutoIceSkatingFindAuraClass(
                "XDTLevelAndEntity.Gameplay.Component.Player.LocalPlayerComponent",
                "ScriptsRefactory.LevelAndEntity.Gameplay.Component.Player.LocalPlayerComponent",
                "LocalPlayerComponent");
            this.autoIceSkatingAuraCharacterClass = this.TryAutoIceSkatingFindAuraClass(
                "XDTLevelAndEntity.Game.GameMode.Character",
                "ScriptsRefactory.LevelAndEntity.Game.GameMode.Character",
                "Character");
            this.autoIceSkatingAuraTableDataClass = this.FindAuraMonoTableDataClass();
            if (this.autoIceSkatingAuraTableDataClass == IntPtr.Zero)
            {
                this.autoIceSkatingAuraTableDataClass = this.TryAutoIceSkatingFindAuraClass(
                    "XDTDataAndProtocol.Config.TableData",
                    "TableData",
                    "EcsClient.TableData");
            }

            if (this.autoIceSkatingAuraGameSkateModeClass == IntPtr.Zero) missing.Append("GameSkateMode;");
            if (this.autoIceSkatingAuraLocalPlayerClass == IntPtr.Zero) missing.Append("LocalPlayerComponent;");
            if (this.autoIceSkatingAuraTableDataClass == IntPtr.Zero) missing.Append("TableData;");

            if (missing.Length > 0)
            {
                detail = "Aura types missing: " + missing + " (enter town / start skating).";
                this.AutoIceSkatingLog(detail, "reflection-aura-missing", force: true);
                return false;
            }

            if (this.autoIceSkatingAuraGetGameModeOpenMethod == IntPtr.Zero)
            {
                this.autoIceSkatingAuraGetGameModeOpenMethod = this.FindAuraMonoMethodOnHierarchy(
                    this.autoIceSkatingAuraLocalPlayerClass,
                    "GetGameMode",
                    0);
            }

            if (this.autoIceSkatingAuraCharacterGetModeOpenMethod == IntPtr.Zero && this.autoIceSkatingAuraCharacterClass != IntPtr.Zero)
            {
                this.autoIceSkatingAuraCharacterGetModeOpenMethod = this.FindAuraMonoMethodOnHierarchy(
                    this.autoIceSkatingAuraCharacterClass,
                    "GetMode",
                    0);
            }

            this.autoIceSkatingAuraGetSkateActionMethod = this.FindAuraMonoMethodOnHierarchy(
                this.autoIceSkatingAuraTableDataClass,
                "GetSkateAction",
                2);
            if (this.autoIceSkatingAuraGetSkateActionMethod == IntPtr.Zero)
            {
                this.autoIceSkatingAuraGetSkateActionMethod = this.FindAuraMonoMethodOnHierarchy(
                    this.autoIceSkatingAuraTableDataClass,
                    "GetSkateAction",
                    1);
            }
            this.autoIceSkatingAuraGetSkateActionStateMethod = this.FindAuraMonoMethodOnHierarchy(
                this.autoIceSkatingAuraTableDataClass,
                "GetSkateActionState",
                2);
            if (this.autoIceSkatingAuraGetSkateActionStateMethod == IntPtr.Zero)
            {
                this.autoIceSkatingAuraGetSkateActionStateMethod = this.FindAuraMonoMethodOnHierarchy(
                    this.autoIceSkatingAuraTableDataClass,
                    "GetSkateActionState",
                    1);
            }
            this.autoIceSkatingAuraSkillTriggerMethod = this.FindAuraMonoMethodOnHierarchy(
                this.autoIceSkatingAuraGameSkateModeClass,
                "SkillTrigger",
                1);
            this.autoIceSkatingAuraCanTriggerUltimateMethod = this.FindAuraMonoMethodOnHierarchy(
                this.autoIceSkatingAuraGameSkateModeClass,
                "CanTriggerUltimate",
                0);
            this.autoIceSkatingAuraCalculateSpeedRateMethod = this.FindAuraMonoMethodOnHierarchy(
                this.autoIceSkatingAuraGameSkateModeClass,
                "CalculateSpeedRate",
                0);
            this.autoIceSkatingAuraIsReceiverMethod = this.FindAuraMonoMethodOnHierarchy(
                this.autoIceSkatingAuraGameSkateModeClass,
                "IsReceiver",
                0);
            this.autoIceSkatingAuraGetRatioInConfiguredPhaseMethod = this.FindAuraMonoMethodOnHierarchy(
                this.autoIceSkatingAuraGameSkateModeClass,
                "GetRatioInConfiguredPhase",
                3);
            this.autoIceSkatingAuraGetUltimateSkillMethod = this.FindAuraMonoMethodOnHierarchy(
                this.autoIceSkatingAuraGameSkateModeClass,
                "get_UltimateSkill",
                0);
            this.autoIceSkatingAuraGetSkateActionTypeMethod = this.FindAuraMonoMethodOnHierarchy(
                this.autoIceSkatingAuraTableDataClass,
                "GetSkateActionType",
                2);
            if (this.autoIceSkatingAuraGetSkateActionTypeMethod == IntPtr.Zero)
            {
                this.autoIceSkatingAuraGetSkateActionTypeMethod = this.FindAuraMonoMethodOnHierarchy(
                    this.autoIceSkatingAuraTableDataClass,
                    "GetSkateActionType",
                    1);
            }

            this.autoIceSkatingAuraGetPairSkateUltimateMethod = this.FindAuraMonoMethodOnHierarchy(
                this.autoIceSkatingAuraTableDataClass,
                "GetPairSkateUltimate",
                2);
            if (this.autoIceSkatingAuraGetPairSkateUltimateMethod == IntPtr.Zero)
            {
                this.autoIceSkatingAuraGetPairSkateUltimateMethod = this.FindAuraMonoMethodOnHierarchy(
                    this.autoIceSkatingAuraTableDataClass,
                    "GetPairSkateUltimate",
                    1);
            }

            this.autoIceSkatingAuraCheckPairSkateMethod = this.FindAuraMonoMethodOnHierarchy(
                this.autoIceSkatingAuraGameSkateModeClass,
                "CheckPairSkate",
                0);
            this.autoIceSkatingAuraSkateActionsField = this.FindAuraMonoFieldOnHierarchy(
                this.autoIceSkatingAuraGameSkateModeClass,
                "_skateActions");
            this.autoIceSkatingAuraCurrentCastActionField = this.FindAuraMonoFieldOnHierarchy(
                this.autoIceSkatingAuraGameSkateModeClass,
                "_currentCastAction");
            this.autoIceSkatingAuraCastInfoField = this.FindAuraMonoFieldOnHierarchy(
                this.autoIceSkatingAuraGameSkateModeClass,
                "CastInfo");
            this.autoIceSkatingAuraChallengeInfoField = this.FindAuraMonoFieldOnHierarchy(
                this.autoIceSkatingAuraGameSkateModeClass,
                "ChallengeInfo");
            this.autoIceSkatingAuraChallengeDataClass = this.TryAutoIceSkatingFindAuraClass(
                "XDTLevelAndEntity.Game.GameMode.ChallengeData",
                "ChallengeData");
            if (this.autoIceSkatingAuraChallengeDataClass != IntPtr.Zero)
            {
                this.autoIceSkatingAuraChallengeDataUsedActionsField = this.FindAuraMonoFieldOnHierarchy(
                    this.autoIceSkatingAuraChallengeDataClass,
                    "UsedActions");
                this.autoIceSkatingAuraChallengeDataTimestampField = this.FindAuraMonoFieldOnHierarchy(
                    this.autoIceSkatingAuraChallengeDataClass,
                    "Timestamp");
                this.autoIceSkatingAuraChallengeDataDurationField = this.FindAuraMonoFieldOnHierarchy(
                    this.autoIceSkatingAuraChallengeDataClass,
                    "Duration");
                if (auraMonoFieldGetOffset != null)
                {
                    // mono_field_get_offset includes the MonoObject header, but ChallengeInfo is a
                    // struct field read raw (unboxed) via mono_field_get_value into a stack buffer.
                    int boxedHeaderSize = 2 * IntPtr.Size;
                    if (this.autoIceSkatingAuraChallengeDataUsedActionsField != IntPtr.Zero)
                    {
                        this.autoIceSkatingAuraChallengeDataUsedActionsOffset =
                            (int)auraMonoFieldGetOffset(this.autoIceSkatingAuraChallengeDataUsedActionsField) - boxedHeaderSize;
                    }

                    if (this.autoIceSkatingAuraChallengeDataTimestampField != IntPtr.Zero)
                    {
                        this.autoIceSkatingAuraChallengeDataTimestampOffset =
                            (int)auraMonoFieldGetOffset(this.autoIceSkatingAuraChallengeDataTimestampField) - boxedHeaderSize;
                    }

                    if (this.autoIceSkatingAuraChallengeDataDurationField != IntPtr.Zero)
                    {
                        this.autoIceSkatingAuraChallengeDataDurationOffset =
                            (int)auraMonoFieldGetOffset(this.autoIceSkatingAuraChallengeDataDurationField) - boxedHeaderSize;
                    }
                }

                if (this.autoIceSkatingAuraChallengeDataDurationOffset < 0
                    || this.autoIceSkatingAuraChallengeDataDurationOffset + sizeof(int) > AutoIceSkatingAuraChallengeDataSize)
                {
                    this.autoIceSkatingAuraChallengeDataDurationOffset = -1;
                }

                if (this.autoIceSkatingAuraChallengeDataUsedActionsOffset < 0
                    || this.autoIceSkatingAuraChallengeDataUsedActionsOffset + IntPtr.Size > AutoIceSkatingAuraChallengeDataSize)
                {
                    this.autoIceSkatingAuraChallengeDataUsedActionsOffset = 24;
                }

                if (this.autoIceSkatingAuraChallengeDataTimestampOffset < 0
                    || this.autoIceSkatingAuraChallengeDataTimestampOffset + sizeof(long) > AutoIceSkatingAuraChallengeDataSize)
                {
                    this.autoIceSkatingAuraChallengeDataTimestampOffset = -1;
                }
            }

            this.autoIceSkatingAuraGameTimeUtilityClass = this.TryAutoIceSkatingFindAuraClass(
                "XDTDataAndProtocol.ProtocolService.GameTimeUtility",
                "GameTimeUtility");
            if (this.autoIceSkatingAuraGameTimeUtilityClass != IntPtr.Zero)
            {
                this.autoIceSkatingAuraGetUnixTimeMsMethod = this.FindAuraMonoMethodOnHierarchy(
                    this.autoIceSkatingAuraGameTimeUtilityClass,
                    "GetUnixTimeMs",
                    0);
                if (this.autoIceSkatingAuraGetUnixTimeMsMethod == IntPtr.Zero)
                {
                    this.autoIceSkatingAuraGetUnixTimeMsMethod = this.FindAuraMonoMethodOnHierarchy(
                        this.autoIceSkatingAuraGameTimeUtilityClass,
                        "GetUnixTime",
                        0);
                    this.autoIceSkatingAuraGetUnixTimeReturnsSeconds = this.autoIceSkatingAuraGetUnixTimeMsMethod != IntPtr.Zero;
                }
            }

            StringBuilder apiMissing = new StringBuilder();
            if (this.autoIceSkatingAuraGetGameModeOpenMethod == IntPtr.Zero
                && this.autoIceSkatingAuraCharacterGetModeOpenMethod == IntPtr.Zero)
            {
                apiMissing.Append("GetGameMode/GetMode;");
            }

            if (this.autoIceSkatingAuraGetSkateActionMethod == IntPtr.Zero) apiMissing.Append("GetSkateAction;");
            if (this.autoIceSkatingAuraSkillTriggerMethod == IntPtr.Zero) apiMissing.Append("SkillTrigger;");
            if (this.autoIceSkatingAuraCanTriggerUltimateMethod == IntPtr.Zero) apiMissing.Append("CanTriggerUltimate;");
            if (this.autoIceSkatingAuraIsReceiverMethod == IntPtr.Zero) apiMissing.Append("IsReceiver;");
            if (this.autoIceSkatingAuraGetRatioInConfiguredPhaseMethod == IntPtr.Zero) apiMissing.Append("GetRatioInConfiguredPhase;");

            if (apiMissing.Length > 0)
            {
                detail = "Aura API missing: " + apiMissing;
                this.AutoIceSkatingLog(detail, "reflection-aura-api", force: true);
                return false;
            }

            if (!this.TryAutoIceSkatingInflateAuraGenericMethod(
                    this.autoIceSkatingAuraGetGameModeOpenMethod,
                    this.autoIceSkatingAuraGameSkateModeClass,
                    out this.autoIceSkatingAuraInflatedGetGameModeMethod)
                && !this.TryAutoIceSkatingInflateAuraGenericMethod(
                    this.autoIceSkatingAuraCharacterGetModeOpenMethod,
                    this.autoIceSkatingAuraGameSkateModeClass,
                    out this.autoIceSkatingAuraInflatedCharacterGetModeMethod))
            {
                detail = "Aura generic inflate failed for GameSkateMode.";
                this.AutoIceSkatingLog(detail, "reflection-aura-inflate", force: true);
                return false;
            }

            detail = "Aura reflection OK.";
            this.AutoIceSkatingLog(
                "aura reflection ok GameSkateMode="
                + this.GetAuraMonoClassDisplayName(this.autoIceSkatingAuraGameSkateModeClass)
                + " TableData="
                + this.GetAuraMonoClassDisplayName(this.autoIceSkatingAuraTableDataClass),
                force: true);
            return true;
        }

        private IntPtr TryAutoIceSkatingFindAuraClass(params string[] fullNames)
        {
            if (fullNames == null)
            {
                return IntPtr.Zero;
            }

            for (int i = 0; i < fullNames.Length; i++)
            {
                IntPtr candidate = this.FindAuraMonoClassByFullName(fullNames[i]);
                if (candidate != IntPtr.Zero)
                {
                    return candidate;
                }
            }

            return IntPtr.Zero;
        }

        private unsafe bool TryAutoIceSkatingInflateAuraGenericMethod(
            IntPtr openMethod,
            IntPtr typeArgClass,
            out IntPtr inflatedMethod)
        {
            inflatedMethod = IntPtr.Zero;
            if (openMethod == IntPtr.Zero
                || typeArgClass == IntPtr.Zero
                || auraMonoClassInflateGenericMethod == null
                || auraMonoClassGetType == null
                || auraMonoMetadataGetGenericInst == null)
            {
                return false;
            }

            IntPtr typeArg = auraMonoClassGetType(typeArgClass);
            if (typeArg == IntPtr.Zero)
            {
                return false;
            }

            IntPtr* typeArgs = stackalloc IntPtr[1];
            typeArgs[0] = typeArg;
            IntPtr genericInst = auraMonoMetadataGetGenericInst(1, (IntPtr)typeArgs);
            if (genericInst == IntPtr.Zero)
            {
                return false;
            }

            MonoGenericContext context = new MonoGenericContext
            {
                class_inst = IntPtr.Zero,
                method_inst = genericInst
            };

            inflatedMethod = auraMonoClassInflateGenericMethod(openMethod, ref context);
            if (inflatedMethod == IntPtr.Zero)
            {
                return false;
            }

            if (auraMonoCompileMethod != null)
            {
                try
                {
                    auraMonoCompileMethod(inflatedMethod);
                }
                catch
                {
                }
            }

            return AuraMonoMethodParamCountIs(inflatedMethod, 0);
        }

        private unsafe void TickAutoIceSkatingAura(float now)
        {
            if (!this.TryGetAutoIceSkatingAuraMode(out IntPtr skateMode, out string modeSource))
            {
                this.AutoIceSkatingResetPerformingTrackers();
                this.AutoIceSkatingSetStatus("Not skating (" + modeSource + ").", "aura-not-skating");
                return;
            }

            if (!this.TryGetMonoBoolMember(skateMode, "actived", out bool actived) || !actived)
            {
                this.autoIceSkatingAuraSkateSessionActive = false;
                this.autoIceSkatingAuraWasInChallenge = false;
                this.autoIceSkatingAuraChallengeCountdownUntil = -999f;
                this.AutoIceSkatingResetPerformingTrackers();
                this.AutoIceSkatingSetStatus("Skate mode inactive (enter ice first) via " + modeSource + ".", "aura-inactive");
                return;
            }

            if (!this.autoIceSkatingAuraSkateSessionActive)
            {
                this.autoIceSkatingAuraSkateSessionActive = true;
                this.autoIceSkatingAuraSkateReadyAt = now + AutoIceSkatingSkateEnterWarmupSeconds;
            }

            if (now < this.autoIceSkatingAuraSkateReadyAt)
            {
                this.AutoIceSkatingResetPerformingTrackers();
                this.AutoIceSkatingSetStatus("Skate enter warmup.", "aura-enter-warmup", log: false);
                return;
            }

            if (this.TryAutoIceSkatingAuraInvokeBool(skateMode, this.autoIceSkatingAuraIsReceiverMethod, out bool isReceiver) && isReceiver)
            {
                this.AutoIceSkatingSetStatus("Pair receiver — manual only.", "aura-receiver");
                return;
            }

            bool inChallenge = this.TryAutoIceSkatingAuraIsInChallengeMode(skateMode);
            if (inChallenge && !this.autoIceSkatingAuraWasInChallenge)
            {
                this.autoIceSkatingAuraChallengeCountdownUntil = now + AutoIceSkatingChallengeCountdownSeconds;
            }
            else if (!inChallenge)
            {
                this.autoIceSkatingAuraChallengeCountdownUntil = -999f;
            }

            this.autoIceSkatingAuraWasInChallenge = inChallenge;

            if (inChallenge && now < this.autoIceSkatingAuraChallengeCountdownUntil)
            {
                this.AutoIceSkatingResetPerformingTrackers();
                this.AutoIceSkatingSetStatus("Challenge countdown.", "aura-challenge-countdown", log: false);
                return;
            }

            IntPtr challengeInfo = IntPtr.Zero;

            this.TryCollectAutoIceSkatingAuraSkills(skateMode, out List<int> skills, out string skillsDetail);
            if (skills.Count == 0)
            {
                this.AutoIceSkatingSetStatus("No skills in tree (" + skillsDetail + ").", "aura-no-skills");
                return;
            }

            int currentActionId = this.TryReadAutoIceSkatingAuraCurrentCastActionId(skateMode);
            this.AutoIceSkatingSyncPerformingAction(currentActionId);
            this.AutoIceSkatingSyncUltimateSkipPerformingAction(currentActionId);
            if (currentActionId > 0)
            {
                bool canUltimate = this.TryAutoIceSkatingAuraInvokeBool(skateMode, this.autoIceSkatingAuraCanTriggerUltimateMethod, out bool canUltimateValue)
                    && canUltimateValue;
                bool endgame = this.autoIceSkatingLast30sUltimate && this.AutoIceSkatingIsChallengeEndgameAura(skateMode);
                int requiredTier = endgame
                    ? 1
                    : (this.autoIceSkatingOnlyX2Ultimate ? AutoIceSkatingUltimateEnergyTierRequired : 1);
                if (canUltimate
                    && this.AutoIceSkatingIsUltimateEnergyTierReadyAura(skateMode, requiredTier, out int energyTier))
                {
                    bool isPairSkate = this.TryAutoIceSkatingIsPairSkateAura(skateMode);
                    string candidatesDetail = this.BuildAutoIceSkatingUltimateCandidatesAura(
                        skateMode,
                        challengeInfo,
                        skills,
                        isPairSkate);
                    if (this.TryAutoIceSkatingSelectUltimateAura(
                            skateMode,
                            challengeInfo,
                            skills,
                            isPairSkate,
                            out int ultimateId,
                            out int ultimateScore,
                            out int maxSeenScore))
                    {
                        if (this.TryAutoIceSkatingAttemptUltimateTriggerAura(
                                skateMode,
                                ultimateId,
                                ultimateScore,
                                energyTier,
                                endgame,
                                candidatesDetail,
                                endgame ? "performing-endgame" : "performing",
                                now))
                        {
                            return;
                        }
                    }
                    else
                    {
                        this.AutoIceSkatingLogUltimateWaitDisabled(
                            "performing",
                            energyTier,
                            "no-qualifying",
                            maxSeenScore,
                            candidatesDetail);
                    }
                }

                if (this.TryAutoIceSkatingTryPerfectInterruptAura(
                        skateMode,
                        challengeInfo,
                        skills,
                        currentActionId,
                        now))
                {
                    return;
                }

                this.AutoIceSkatingSetStatus("Performing action " + currentActionId + ".", "aura-performing", log: false);
                return;
            }

            if (this.TryAutoIceSkatingTickUltimateOnIdleAura(skateMode, challengeInfo, skills, now))
            {
                return;
            }

            this.AutoIceSkatingResetPerformingTrackers();
            if (!this.AutoIceSkatingIsStartTriggerReady(now))
            {
                return;
            }

            int startActionId = this.PickAutoIceSkatingAuraBestSkill(
                skateMode,
                challengeInfo,
                skills,
                preferDifferentFrom: 0,
                out string startPickDetail);
            this.AutoIceSkatingLog("idle pick=" + startActionId + " skills=[" + string.Join(",", skills) + "] (" + startPickDetail + ")", "aura-idle-pick");
            if (startActionId > 0)
            {
                this.TryAutoIceSkatingAuraSkillTrigger(skateMode, startActionId, now, "start");
            }
            else
            {
                this.AutoIceSkatingSetStatus("Could not pick a skill (" + startPickDetail + ").", "aura-pick-fail");
            }
        }

        private unsafe bool TryAutoIceSkatingTryPerfectInterruptAura(
            IntPtr skateMode,
            IntPtr challengeInfo,
            List<int> skills,
            int currentActionId,
            float now)
        {
            // "Perfect move" off: chain the next move as soon as the game allows an interrupt
            // (SkillTrigger gates blend time / interruptibility), not waiting for the perfect window.
            if (!this.autoIceSkatingPerfectMove)
            {
                if (!this.AutoIceSkatingIsStartTriggerReady(now))
                {
                    return false;
                }

                int immediateId = this.PickAutoIceSkatingAuraBestSkill(
                    skateMode,
                    challengeInfo,
                    skills,
                    preferDifferentFrom: currentActionId,
                    out string immediateDetail);
                this.AutoIceSkatingLog("immediate pick=" + immediateId + " (" + immediateDetail + ")", "aura-immediate-pick", force: true);
                return immediateId > 0
                    && this.TryAutoIceSkatingAuraSkillTrigger(skateMode, immediateId, now, "immediate");
            }

            IntPtr prefectPhaseArray = this.TryAutoIceSkatingAuraGetSkateActionFieldObject(currentActionId, "prefectPhase");
            int prefectLen = this.TryAutoIceSkatingAuraGetMonoArrayLength(prefectPhaseArray);
            float perfectRatio = 0f;
            int perfectPhaseIndex = -1;
            bool inPerfect = prefectLen > 0
                && this.TryAutoIceSkatingAuraIsInConfiguredPhase(skateMode, prefectPhaseArray, out perfectRatio, out perfectPhaseIndex);
            this.AutoIceSkatingLog(
                "performing action=" + currentActionId
                + " prefectLen=" + prefectLen
                + " inPerfect=" + inPerfect
                + (inPerfect ? (" ratio=" + perfectRatio.ToString("0.###") + " phaseIdx=" + perfectPhaseIndex) : string.Empty),
                "aura-performing-" + currentActionId);

            int phaseKey = AutoIceSkatingMakePerfectPhaseKey(currentActionId, perfectPhaseIndex);
            if (!inPerfect || phaseKey == this.autoIceSkatingLastPerfectPhaseKey)
            {
                return false;
            }

            int nextActionId = this.PickAutoIceSkatingAuraBestSkill(
                skateMode,
                challengeInfo,
                skills,
                preferDifferentFrom: currentActionId,
                out string pickDetail);
            this.AutoIceSkatingLog("perfect window pick=" + nextActionId + " (" + pickDetail + ")", "aura-perfect-pick", force: true);
            if (nextActionId > 0
                && this.TryAutoIceSkatingAuraSkillTrigger(skateMode, nextActionId, now, "perfect", applyCooldown: false))
            {
                this.autoIceSkatingLastPerfectPhaseKey = phaseKey;
                return true;
            }

            return false;
        }

        private unsafe bool TryGetAutoIceSkatingAuraMode(out IntPtr skateMode, out string source)
        {
            skateMode = IntPtr.Zero;
            source = "unresolved";
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
            {
                source = "AuraMono not ready";
                return false;
            }

            if (!this.TryAutoIceSkatingAuraGetSelfPlayer(out IntPtr localPlayer, out string playerSource))
            {
                source = "no LocalPlayerComponent (" + playerSource + ")";
                return false;
            }

            if (this.autoIceSkatingAuraInflatedGetGameModeMethod != IntPtr.Zero)
            {
                IntPtr exc = IntPtr.Zero;
                skateMode = auraMonoRuntimeInvoke(this.autoIceSkatingAuraInflatedGetGameModeMethod, localPlayer, IntPtr.Zero, ref exc);
                if (exc == IntPtr.Zero && skateMode != IntPtr.Zero)
                {
                    source = playerSource + " -> GetGameMode<GameSkateMode>";
                    return true;
                }
            }

            if (this.TryGetMonoObjectMember(localPlayer, "character", out IntPtr characterObj)
                && characterObj != IntPtr.Zero
                && this.autoIceSkatingAuraInflatedCharacterGetModeMethod != IntPtr.Zero)
            {
                IntPtr exc = IntPtr.Zero;
                skateMode = auraMonoRuntimeInvoke(this.autoIceSkatingAuraInflatedCharacterGetModeMethod, characterObj, IntPtr.Zero, ref exc);
                if (exc == IntPtr.Zero && skateMode != IntPtr.Zero)
                {
                    source = playerSource + " -> character.GetMode<GameSkateMode>";
                    return true;
                }
            }

            if (this.autoIceSkatingAuraCharacterGetModeOpenMethod != IntPtr.Zero
                && this.autoIceSkatingAuraCharacterClass != IntPtr.Zero
                && this.TryAutoIceSkatingInflateAuraGenericMethod(
                    this.autoIceSkatingAuraCharacterGetModeOpenMethod,
                    this.autoIceSkatingAuraGameSkateModeClass,
                    out IntPtr inflatedGetMode)
                && this.TryGetMonoObjectMember(localPlayer, "character", out IntPtr characterObj2)
                && characterObj2 != IntPtr.Zero)
            {
                this.autoIceSkatingAuraInflatedCharacterGetModeMethod = inflatedGetMode;
                IntPtr exc = IntPtr.Zero;
                skateMode = auraMonoRuntimeInvoke(inflatedGetMode, characterObj2, IntPtr.Zero, ref exc);
                if (exc == IntPtr.Zero && skateMode != IntPtr.Zero)
                {
                    source = playerSource + " -> character.GetMode<GameSkateMode> (late inflate)";
                    return true;
                }
            }

            source = playerSource + " -> GameSkateMode null";
            return false;
        }

        private unsafe bool TryAutoIceSkatingAuraGetSelfPlayer(out IntPtr localPlayer, out string source)
        {
            localPlayer = IntPtr.Zero;
            source = string.Empty;
            string[] entityUtilTypeNames =
            {
                "XDTLevelAndEntity.BaseSystem.EntitiesManager.EntityUtil",
                "ScriptsRefactory.LevelAndEntity.BaseSystem.EntitiesManager.EntityUtil",
                "EntityUtil"
            };

            for (int i = 0; i < entityUtilTypeNames.Length && localPlayer == IntPtr.Zero; i++)
            {
                IntPtr entityUtilClass = this.TryAutoIceSkatingFindAuraClass(entityUtilTypeNames[i]);
                if (entityUtilClass == IntPtr.Zero)
                {
                    entityUtilClass = this.FindAuraMonoClassAcrossLoadedAssemblies(
                        "XDTLevelAndEntity.BaseSystem.EntitiesManager",
                        "EntityUtil");
                }

                if (entityUtilClass == IntPtr.Zero)
                {
                    continue;
                }

                IntPtr getSelfPlayerMethod = this.FindAuraMonoMethodOnHierarchy(entityUtilClass, "GetSelfPlayer", 0);
                if (getSelfPlayerMethod == IntPtr.Zero)
                {
                    continue;
                }

                IntPtr exc = IntPtr.Zero;
                localPlayer = auraMonoRuntimeInvoke(getSelfPlayerMethod, IntPtr.Zero, IntPtr.Zero, ref exc);
                if (exc == IntPtr.Zero && localPlayer != IntPtr.Zero)
                {
                    source = "Aura EntityUtil.GetSelfPlayer()";
                }
                else
                {
                    localPlayer = IntPtr.Zero;
                }
            }

            if (localPlayer != IntPtr.Zero)
            {
                return true;
            }

            string[] entityManagerTypeNames =
            {
                "XDTLevelAndEntity.BaseSystem.EntityManager",
                "ScriptsRefactory.LevelAndEntity.BaseSystem.EntityManager",
                "EntityManager"
            };
            for (int i = 0; i < entityManagerTypeNames.Length && localPlayer == IntPtr.Zero; i++)
            {
                IntPtr managerClass = this.TryAutoIceSkatingFindAuraClass(entityManagerTypeNames[i]);
                if (managerClass == IntPtr.Zero)
                {
                    continue;
                }

                if (this.TryGetAuraMonoStaticObjectField(managerClass, "Instance", out IntPtr managerObj) && managerObj != IntPtr.Zero
                    && this.TryGetMonoObjectMember(managerObj, "selfPlayer", out localPlayer)
                    && localPlayer != IntPtr.Zero)
                {
                    source = "Aura EntityManager.Instance.selfPlayer";
                }
                else
                {
                    localPlayer = IntPtr.Zero;
                }
            }

            return localPlayer != IntPtr.Zero;
        }

        private unsafe bool TryAutoIceSkatingAuraSkillTrigger(IntPtr skateMode, int actionId, float now, string reason, bool applyCooldown = true)
        {
            if (skateMode == IntPtr.Zero || this.autoIceSkatingAuraSkillTriggerMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            int id = actionId;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&id);
            IntPtr exc = IntPtr.Zero;
            auraMonoRuntimeInvoke(this.autoIceSkatingAuraSkillTriggerMethod, skateMode, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                this.AutoIceSkatingSetStatus("SkillTrigger failed (Aura).", force: true);
                this.AutoIceSkatingLog("Aura SkillTrigger(" + actionId + ") exception", force: true);
                return false;
            }

            if (applyCooldown)
            {
                this.autoIceSkatingLastTriggerAt = now;
            }
            this.AutoIceSkatingSetStatus("Triggered " + actionId + " (" + reason + ").", force: true);
            this.AutoIceSkatingLog(
                "Aura SkillTrigger(" + actionId + ") reason=" + reason
                + " {" + this.DescribeAutoIceSkatingActionAura(skateMode, actionId) + "}",
                force: true);
            return true;
        }

        private unsafe bool TryAutoIceSkatingAuraInvokeBool(IntPtr target, IntPtr method, out bool value)
        {
            value = false;
            if (target == IntPtr.Zero || method == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr boxed = auraMonoRuntimeInvoke(method, target, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || boxed == IntPtr.Zero)
            {
                return false;
            }

            return this.TryUnboxMonoBoolean(boxed, out value);
        }

        private unsafe int TryReadAutoIceSkatingAuraCurrentCastActionId(IntPtr skateMode)
        {
            if (skateMode == IntPtr.Zero)
            {
                return 0;
            }

            if (!this.TryReadAutoIceSkatingAuraCastActionIds(skateMode, out int actionId, out _))
            {
                return 0;
            }

            return actionId;
        }

        private bool TryAutoIceSkatingAuraIsValidSkateActionId(int actionId)
        {
            if (actionId <= 0 || actionId > AutoIceSkatingMaxActionId)
            {
                return false;
            }

            return this.TryAutoIceSkatingAuraGetSkateActionRow(actionId) != IntPtr.Zero;
        }

        private unsafe bool TryAutoIceSkatingAuraIsInChallengeMode(IntPtr skateMode)
        {
            if (skateMode == IntPtr.Zero)
            {
                return false;
            }

            if (this.TryGetMonoInt32Member(skateMode, "CurrentMode", out int mode))
            {
                return mode == AutoIceSkatingSkateModeChallenge;
            }

            IntPtr klass = auraMonoObjectGetClass != null ? auraMonoObjectGetClass(skateMode) : IntPtr.Zero;
            IntPtr getter = klass != IntPtr.Zero
                ? this.FindAuraMonoMethodOnHierarchy(klass, "get_CurrentMode", 0)
                : IntPtr.Zero;
            if (getter == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr boxed = auraMonoRuntimeInvoke(getter, skateMode, IntPtr.Zero, ref exc);
            return exc == IntPtr.Zero && this.TryUnboxMonoInt32(boxed, out mode) && mode == AutoIceSkatingSkateModeChallenge;
        }

        private unsafe int TryReadAutoIceSkatingAuraUltimateActionId(IntPtr skateMode)
        {
            if (skateMode == IntPtr.Zero || this.autoIceSkatingAuraGetUltimateSkillMethod == IntPtr.Zero)
            {
                return 0;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr config = auraMonoRuntimeInvoke(this.autoIceSkatingAuraGetUltimateSkillMethod, skateMode, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || config == IntPtr.Zero)
            {
                return 0;
            }

            if (this.TryGetMonoInt32Member(config, "id", out int id) && id > 0)
            {
                return id;
            }

            return this.TryGetMonoInt32Member(config, "Id", out id) ? id : 0;
        }

        private unsafe IntPtr TryAutoIceSkatingAuraGetSkateActionRow(int actionId)
        {
            if (actionId <= 0 || this.autoIceSkatingAuraGetSkateActionMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return IntPtr.Zero;
            }

            int id = actionId;
            bool needException = false;
            IntPtr exc = IntPtr.Zero;
            IntPtr row;
            if (AuraMonoMethodParamCountIs(this.autoIceSkatingAuraGetSkateActionMethod, 2))
            {
                IntPtr* args = stackalloc IntPtr[2];
                args[0] = (IntPtr)(&id);
                args[1] = (IntPtr)(&needException);
                row = auraMonoRuntimeInvoke(this.autoIceSkatingAuraGetSkateActionMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            }
            else
            {
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&id);
                row = auraMonoRuntimeInvoke(this.autoIceSkatingAuraGetSkateActionMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            }

            return exc == IntPtr.Zero ? row : IntPtr.Zero;
        }

        private unsafe IntPtr TryAutoIceSkatingAuraGetSkateActionTypeRow(int actionTypeId)
        {
            if (actionTypeId <= 0 || this.autoIceSkatingAuraGetSkateActionTypeMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return IntPtr.Zero;
            }

            int id = actionTypeId;
            bool needException = false;
            IntPtr exc = IntPtr.Zero;
            IntPtr row;
            if (AuraMonoMethodParamCountIs(this.autoIceSkatingAuraGetSkateActionTypeMethod, 2))
            {
                IntPtr* args = stackalloc IntPtr[2];
                args[0] = (IntPtr)(&id);
                args[1] = (IntPtr)(&needException);
                row = auraMonoRuntimeInvoke(this.autoIceSkatingAuraGetSkateActionTypeMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            }
            else
            {
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&id);
                row = auraMonoRuntimeInvoke(this.autoIceSkatingAuraGetSkateActionTypeMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            }

            return exc == IntPtr.Zero ? row : IntPtr.Zero;
        }

        private unsafe IntPtr TryAutoIceSkatingAuraGetPairSkateUltimateRow(int pairMotionId)
        {
            if (pairMotionId <= 0 || this.autoIceSkatingAuraGetPairSkateUltimateMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return IntPtr.Zero;
            }

            int id = pairMotionId;
            bool needException = false;
            IntPtr exc = IntPtr.Zero;
            IntPtr row;
            if (AuraMonoMethodParamCountIs(this.autoIceSkatingAuraGetPairSkateUltimateMethod, 2))
            {
                IntPtr* args = stackalloc IntPtr[2];
                args[0] = (IntPtr)(&id);
                args[1] = (IntPtr)(&needException);
                row = auraMonoRuntimeInvoke(this.autoIceSkatingAuraGetPairSkateUltimateMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            }
            else
            {
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&id);
                row = auraMonoRuntimeInvoke(this.autoIceSkatingAuraGetPairSkateUltimateMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            }

            return exc == IntPtr.Zero ? row : IntPtr.Zero;
        }

        private unsafe int[] TryReadAutoIceSkatingAuraSkateActionIntArray(int actionId, string fieldName)
        {
            IntPtr row = this.TryAutoIceSkatingAuraGetSkateActionRow(actionId);
            if (row == IntPtr.Zero || !this.TryGetMonoObjectMember(row, fieldName, out IntPtr arrayObj) || arrayObj == IntPtr.Zero)
            {
                return null;
            }

            return this.TryReadAutoIceSkatingAuraMonoIntArray(arrayObj);
        }

        private unsafe int[] TryReadAutoIceSkatingAuraMonoIntArray(IntPtr arrayObj)
        {
            if (arrayObj == IntPtr.Zero || auraMonoArrayLength == null || auraMonoArrayAddrWithSize == null || !this.IsAuraMonoArrayObject(arrayObj))
            {
                return null;
            }

            try
            {
                int arrayCount = (int)Math.Min(auraMonoArrayLength(arrayObj).ToUInt64(), 64UL);
                if (arrayCount <= 0)
                {
                    return Array.Empty<int>();
                }

                IntPtr arrayBase = auraMonoArrayAddrWithSize(arrayObj, 4, UIntPtr.Zero);
                if (arrayBase == IntPtr.Zero)
                {
                    return null;
                }

                int[] values = new int[arrayCount];
                for (int i = 0; i < arrayCount; i++)
                {
                    values[i] = Marshal.ReadInt32(arrayBase, i * 4);
                }

                return values;
            }
            catch
            {
                return null;
            }
        }

        private unsafe IntPtr TryAutoIceSkatingAuraGetSkateActionFieldObject(int actionId, string fieldName)
        {
            IntPtr row = this.TryAutoIceSkatingAuraGetSkateActionRow(actionId);
            if (row == IntPtr.Zero || !this.TryGetMonoObjectMember(row, fieldName, out IntPtr fieldObj))
            {
                return IntPtr.Zero;
            }

            return fieldObj;
        }

        private unsafe int TryAutoIceSkatingAuraGetMonoArrayLength(IntPtr arrayObj)
        {
            if (arrayObj == IntPtr.Zero || auraMonoArrayLength == null || !this.IsAuraMonoArrayObject(arrayObj))
            {
                return 0;
            }

            try
            {
                return (int)Math.Min(auraMonoArrayLength(arrayObj).ToUInt64(), 64UL);
            }
            catch
            {
                return 0;
            }
        }

        private unsafe bool TryAutoIceSkatingAuraIsInConfiguredPhase(
            IntPtr skateMode,
            IntPtr phaseArrayObj,
            out float ratioInPhase,
            out int phaseIndex)
        {
            ratioInPhase = 0f;
            phaseIndex = 0;
            if (skateMode == IntPtr.Zero
                || phaseArrayObj == IntPtr.Zero
                || this.autoIceSkatingAuraGetRatioInConfiguredPhaseMethod == IntPtr.Zero
                || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            float ratio = 0f;
            int phaseIdx = 0;
            IntPtr* invokeArgs = stackalloc IntPtr[3];
            invokeArgs[0] = phaseArrayObj;
            invokeArgs[1] = (IntPtr)(&ratio);
            invokeArgs[2] = (IntPtr)(&phaseIdx);
            IntPtr exc = IntPtr.Zero;
            IntPtr boxed = auraMonoRuntimeInvoke(
                this.autoIceSkatingAuraGetRatioInConfiguredPhaseMethod,
                skateMode,
                (IntPtr)invokeArgs,
                ref exc);
            if (exc != IntPtr.Zero || !this.TryUnboxMonoBoolean(boxed, out bool result) || !result)
            {
                return false;
            }

            ratioInPhase = ratio;
            phaseIndex = phaseIdx;
            return ratioInPhase > AutoIceSkatingMinPerfectRatio && ratioInPhase <= 1f;
        }

        private unsafe void TryCollectAutoIceSkatingAuraSkills(IntPtr skateMode, out List<int> skills, out string detail)
        {
            skills = new List<int>();
            detail = "SkateSkills unavailable";
            if (skateMode == IntPtr.Zero)
            {
                return;
            }

            IntPtr skillsList = IntPtr.Zero;
            if (!this.TryGetMonoObjectMember(skateMode, "SkateSkills", out skillsList) || skillsList == IntPtr.Zero)
            {
                this.TryGetMonoObjectMember(skateMode, "_skateSkills", out skillsList);
            }

            if (skillsList == IntPtr.Zero)
            {
                detail = "SkateSkills=null";
                return;
            }

            if (!this.TryAutoIceSkatingAuraReadIntList(skillsList, skills))
            {
                detail = "SkateSkills unreadable";
                return;
            }

            detail = "Aura count=" + skills.Count;
        }

        private unsafe bool TryAutoIceSkatingAuraReadIntList(IntPtr listObj, List<int> output)
        {
            output.Clear();
            if (listObj == IntPtr.Zero || auraMonoObjectGetClass == null || auraMonoClassGetMethodFromName == null || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr listClass = auraMonoObjectGetClass(listObj);
            if (listClass == IntPtr.Zero)
            {
                return false;
            }

            IntPtr countMethod = auraMonoClassGetMethodFromName(listClass, "get_Count", 0);
            IntPtr getItemMethod = auraMonoClassGetMethodFromName(listClass, "get_Item", 1);
            if (countMethod == IntPtr.Zero || getItemMethod == IntPtr.Zero)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr countBoxed = auraMonoRuntimeInvoke(countMethod, listObj, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || countBoxed == IntPtr.Zero || !this.TryUnboxMonoInt32(countBoxed, out int count))
            {
                return false;
            }

            if (count <= 0)
            {
                return true;
            }

            count = Math.Min(count, 64);
            int index = 0;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&index);
            for (int i = 0; i < count; i++)
            {
                index = i;
                exc = IntPtr.Zero;
                IntPtr itemBoxed = auraMonoRuntimeInvoke(getItemMethod, listObj, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero || itemBoxed == IntPtr.Zero || !this.TryUnboxMonoInt32(itemBoxed, out int id)
                    || !this.TryAutoIceSkatingAuraIsValidSkateActionId(id))
                {
                    continue;
                }

                output.Add(id);
            }

            return true;
        }

        private unsafe int PickAutoIceSkatingAuraBestSkill(
            IntPtr skateMode,
            IntPtr challengeInfo,
            List<int> skills,
            int preferDifferentFrom,
            out string detail)
        {
            int bestId = 0;
            bool bestNew = false;
            float bestDuration = float.PositiveInfinity;
            int fallbackId = 0;
            bool fallbackNew = false;
            float fallbackDuration = float.PositiveInfinity;
            bool inChallenge = this.TryAutoIceSkatingAuraIsInChallengeMode(skateMode);
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < skills.Count; i++)
            {
                int actionId = skills[i];
                if (actionId <= 0)
                {
                    continue;
                }

                // Strategy: among simple actions prefer NEW ones (challenge novelty bonus),
                // then the SHORTEST duration.
                bool isNew = inChallenge && this.TryAutoIceSkatingAuraChallengeIsNewAction(skateMode, actionId);
                float duration = this.EstimateAutoIceSkatingActionDurationAura(actionId);
                if (sb.Length > 0)
                {
                    sb.Append(" | ");
                }

                sb.Append('{').Append(this.DescribeAutoIceSkatingActionAura(skateMode, actionId)).Append('}');
                if (actionId != preferDifferentFrom)
                {
                    if (AutoIceSkatingPreferAction(isNew, duration, bestId, bestNew, bestDuration))
                    {
                        bestId = actionId;
                        bestNew = isNew;
                        bestDuration = duration;
                    }
                }
                else if (AutoIceSkatingPreferAction(isNew, duration, fallbackId, fallbackNew, fallbackDuration))
                {
                    fallbackId = actionId;
                    fallbackNew = isNew;
                    fallbackDuration = duration;
                }
            }

            int picked = bestId > 0 ? bestId : fallbackId;
            detail = "candidates " + sb + " picked=" + picked;
            return picked;
        }

        private unsafe float EstimateAutoIceSkatingActionDurationAura(int actionId)
        {
            int[] phaseIds = this.TryReadAutoIceSkatingAuraSkateActionIntArray(actionId, "prefectPhase");
            if (phaseIds == null || phaseIds.Length == 0)
            {
                phaseIds = this.TryReadAutoIceSkatingAuraSkateActionIntArray(actionId, "normalPhase");
            }

            return this.EstimateAutoIceSkatingPhaseIdsDurationAura(phaseIds);
        }

        private unsafe float EstimateAutoIceSkatingPhaseIdsDurationAura(int[] phaseIds)
        {
            if (phaseIds == null || phaseIds.Length == 0 || this.autoIceSkatingAuraGetSkateActionStateMethod == IntPtr.Zero)
            {
                return float.PositiveInfinity;
            }

            float sum = 0f;
            bool any = false;
            for (int i = 0; i < phaseIds.Length; i++)
            {
                IntPtr state = this.TryAutoIceSkatingAuraGetSkateActionStateRow(phaseIds[i]);
                if (state == IntPtr.Zero)
                {
                    continue;
                }

                float span = this.ReadAutoIceSkatingPhaseSpanAura(state);
                if (span < 0f)
                {
                    continue;
                }

                sum += span;
                any = true;
            }

            return any ? sum : float.PositiveInfinity;
        }

        private unsafe float ReadAutoIceSkatingPhaseSpanAura(IntPtr state)
        {
            if (state == IntPtr.Zero || !this.TryGetMonoObjectMember(state, "phase", out IntPtr phaseArray))
            {
                return -1f;
            }

            float[] phases = this.TryReadAutoIceSkatingAuraMonoFloatArray(phaseArray);
            if (phases == null || phases.Length < 2)
            {
                return -1f;
            }

            return phases[phases.Length - 1] - phases[0];
        }

        private unsafe IntPtr TryAutoIceSkatingAuraGetSkateActionStateRow(int stateId)
        {
            if (stateId <= 0 || this.autoIceSkatingAuraGetSkateActionStateMethod == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return IntPtr.Zero;
            }

            int id = stateId;
            bool needException = false;
            IntPtr exc = IntPtr.Zero;
            IntPtr row;
            if (AuraMonoMethodParamCountIs(this.autoIceSkatingAuraGetSkateActionStateMethod, 2))
            {
                IntPtr* args = stackalloc IntPtr[2];
                args[0] = (IntPtr)(&id);
                args[1] = (IntPtr)(&needException);
                row = auraMonoRuntimeInvoke(this.autoIceSkatingAuraGetSkateActionStateMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            }
            else
            {
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&id);
                row = auraMonoRuntimeInvoke(this.autoIceSkatingAuraGetSkateActionStateMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            }

            return exc == IntPtr.Zero ? row : IntPtr.Zero;
        }

        private unsafe float[] TryReadAutoIceSkatingAuraMonoFloatArray(IntPtr arrayObj)
        {
            if (arrayObj == IntPtr.Zero || auraMonoArrayLength == null || auraMonoArrayAddrWithSize == null || !this.IsAuraMonoArrayObject(arrayObj))
            {
                return null;
            }

            try
            {
                int arrayCount = (int)Math.Min(auraMonoArrayLength(arrayObj).ToUInt64(), 16UL);
                if (arrayCount < 2)
                {
                    return null;
                }

                IntPtr arrayBase = auraMonoArrayAddrWithSize(arrayObj, 4, UIntPtr.Zero);
                if (arrayBase == IntPtr.Zero)
                {
                    return null;
                }

                float[] values = new float[arrayCount];
                for (int i = 0; i < arrayCount; i++)
                {
                    values[i] = BitConverter.Int32BitsToSingle(Marshal.ReadInt32(arrayBase, i * 4));
                }

                return values;
            }
            catch
            {
                return null;
            }
        }

        private unsafe bool TryAutoIceSkatingAuraReadChallengeDataBuffer(IntPtr skateMode, byte* buffer)
        {
            if (skateMode == IntPtr.Zero
                || buffer == null
                || this.autoIceSkatingAuraChallengeInfoField == IntPtr.Zero
                || auraMonoFieldGetValue == null
                || !this.AttachAuraMonoThread())
            {
                return false;
            }

            auraMonoFieldGetValue(skateMode, this.autoIceSkatingAuraChallengeInfoField, (IntPtr)buffer);
            return true;
        }

        private unsafe bool TryAutoIceSkatingAuraGetUnixTimeMs(out long unixMs)
        {
            unixMs = 0L;
            if (this.autoIceSkatingAuraGetUnixTimeMsMethod == IntPtr.Zero
                || auraMonoRuntimeInvoke == null
                || !this.AttachAuraMonoThread())
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr boxed = auraMonoRuntimeInvoke(
                this.autoIceSkatingAuraGetUnixTimeMsMethod,
                IntPtr.Zero,
                IntPtr.Zero,
                ref exc);
            if (exc != IntPtr.Zero || boxed == IntPtr.Zero)
            {
                return false;
            }

            if (!this.autoIceSkatingAuraGetUnixTimeReturnsSeconds
                && auraMonoObjectUnbox != null
                && this.TryAuraMonoBoxedIsValueType(boxed))
            {
                IntPtr raw = auraMonoObjectUnbox(boxed);
                if (raw != IntPtr.Zero)
                {
                    unixMs = *(long*)raw;
                    return unixMs > 0L;
                }
            }

            if (this.TryUnboxMonoInt32(boxed, out int unixValue) && unixValue > 0)
            {
                unixMs = this.autoIceSkatingAuraGetUnixTimeReturnsSeconds
                    ? (long)unixValue * 1000L
                    : unixValue;
                return unixMs > 0L;
            }

            return false;
        }

        private unsafe bool TryAutoIceSkatingAuraHashSetContainsInt(IntPtr hashSetObj, int value)
        {
            if (hashSetObj == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            if (this.autoIceSkatingAuraHashSetContainsMethod == IntPtr.Zero)
            {
                IntPtr klass = auraMonoObjectGetClass != null ? auraMonoObjectGetClass(hashSetObj) : IntPtr.Zero;
                if (klass != IntPtr.Zero)
                {
                    this.autoIceSkatingAuraHashSetContainsMethod = this.FindAuraMonoMethodOnHierarchy(
                        klass,
                        "Contains",
                        1);
                }
            }

            if (this.autoIceSkatingAuraHashSetContainsMethod != IntPtr.Zero)
            {
                int id = value;
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = (IntPtr)(&id);
                IntPtr exc = IntPtr.Zero;
                IntPtr boxed = auraMonoRuntimeInvoke(
                    this.autoIceSkatingAuraHashSetContainsMethod,
                    hashSetObj,
                    (IntPtr)args,
                    ref exc);
                if (exc == IntPtr.Zero && this.TryUnboxMonoBoolean(boxed, out bool contains))
                {
                    return contains;
                }
            }

            return this.TryAutoIceSkatingAuraCollectionContainsInt(hashSetObj, value);
        }

        private bool TryAutoIceSkatingAuraIsLikelyMonoObject(IntPtr obj)
        {
            if (obj == IntPtr.Zero || auraMonoObjectGetClass == null)
            {
                return false;
            }

            // mono_object_get_class dereferences obj; an unaligned or low address is
            // certainly not a managed object and would AV the process (uncatchable).
            ulong address = (ulong)obj.ToInt64();
            if (address < 0x10000UL || (address & (ulong)(IntPtr.Size - 1)) != 0UL)
            {
                return false;
            }

            try
            {
                return auraMonoObjectGetClass(obj) != IntPtr.Zero;
            }
            catch
            {
                return false;
            }
        }

        private bool TryAutoIceSkatingAuraCollectionContainsInt(IntPtr collectionObj, int value)
        {
            this.autoIceSkatingAuraKeyBuffer.Clear();
            if (!this.TryEnumerateAuraMonoCollectionItems(collectionObj, this.autoIceSkatingAuraKeyBuffer))
            {
                return false;
            }

            for (int i = 0; i < this.autoIceSkatingAuraKeyBuffer.Count; i++)
            {
                IntPtr item = this.autoIceSkatingAuraKeyBuffer[i];
                if (this.TryUnboxMonoInt32(item, out int itemId) && itemId == value)
                {
                    return true;
                }

                if (this.TryGetMonoInt32Member(item, "m_value", out itemId) && itemId == value)
                {
                    return true;
                }

                if (this.TryGetMonoInt32Member(item, "value__", out itemId) && itemId == value)
                {
                    return true;
                }
            }

            return false;
        }

        private unsafe bool TryAutoIceSkatingAuraChallengeIsNewAction(IntPtr skateMode, int actionId)
        {
            if (skateMode == IntPtr.Zero
                || this.autoIceSkatingAuraChallengeDataUsedActionsOffset < 0)
            {
                return false;
            }

            if (!this.TryAutoIceSkatingAuraIsInChallengeMode(skateMode))
            {
                return false;
            }

            byte* buffer = stackalloc byte[AutoIceSkatingAuraChallengeDataSize];
            if (!this.TryAutoIceSkatingAuraReadChallengeDataBuffer(skateMode, buffer))
            {
                return false;
            }

            IntPtr usedActions = *(IntPtr*)(buffer + this.autoIceSkatingAuraChallengeDataUsedActionsOffset);
            if (usedActions == IntPtr.Zero || !this.TryAutoIceSkatingAuraIsLikelyMonoObject(usedActions))
            {
                return true;
            }

            return !this.TryAutoIceSkatingAuraHashSetContainsInt(usedActions, actionId);
        }

    }
}
