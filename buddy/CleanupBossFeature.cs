using System;
using System.Collections.Generic;
using UnityEngine;

namespace HeartopiaMod
{
    // ============================================================================================
    // OCEAN CLEANUP BOSS — fight the event's public pollutant the way a player does.
    //
    // Model measured live over four fights on 2026-09-15 (tools/CleanupProbe recordings):
    //   * the boss (TypeId 140012, MonsterType PublicPollutant) spawns at a FIXED spot
    //     (-17.3, -73.1, 40.0) once the personal stage ends; its HP is scaled per event (36k..72k);
    //   * cleaning reaches 9.5 m from its centre — SeaCleanChecker.DetectMaxDistance (5 m) plus the
    //     boss detection sphere (4.5 m). The checker acquires by a 50° cone from the player's
    //     forward; once holding, only IsMainButtonDown + target alive are re-checked;
    //   * three explosions per fight at ~62/46/26 % HP. ExplosionStarted carries an 8 s countdown
    //     and a 4.5 m safe zone drawn from ≥3 fixed spots 8-16 m away (the same spot can repeat);
    //     ExplosionFinished bounces every player OUTSIDE the zone away (PlayerBounceContext);
    //   * the boss QTE is a HOLD-AND-RELEASE loop (SeaCleanMonsterComponent.StartQTE/TickQTE/
    //     BeginHold/EndHold): Ready → the button goes down → Holding → after the round's hold
    //     duration → AwaitingRelease (the gauge turns red) → the button must go UP → the round
    //     counts, Ready again with a new duration → … → Completed after 1-3 rounds. A button that
    //     stays down through AwaitingRelease just waits for the QTE to time out (Failed), and the
    //     boss takes no damage meanwhile (StartQTE sends ReqStopCleanPublicPollutant). So the
    //     SeaCleanExecutionStateEvent drives the button: release on AwaitingRelease, press again on
    //     Ready / Completed / Failed;
    //   * moving while cleaning stops the clean (PlayerStateSeaClean.OnStateTick), and re-entering
    //     the clean needs a fresh PRESS (SeaCleanCommand fires on the press, not on the level), so
    //     the walker is released and the character settled before the button goes down, and the
    //     hold is re-pressed whenever IsCleaning reads false.
    //
    // Levers, all through AuraMono: the farm walker for the approach and the dash to the bubble
    // (TryBeginFarmWalk / RunFarmWalkTick / AbortFarmWalk — the Quest Walk driving pattern),
    // PlayerMoveComponent.WorldFaceTo for the facing (the Fishing.cs helper with its position
    // write off), LocalPlayerComponent._OnMainButtonEvent(bool) for the hold — the game's own
    // input listener, so SeaCleanCommand → PlayerStateSeaClean runs with the animation, the beam
    // and the ReqStartCleanPublicPollutant protocol exactly as for a real press — and
    // PlayerStateSeaClean.IsCleaning to verify the hold actually locked a target.
    //
    // Coexistence: while the event is joined, Aura Farm's node picker targets only contamination
    // inside the measured event area (CleanupEventFarmGateActive), so the personal stage is farmed
    // by Aura Farm and the boss by this file. The walker has one writer: Aura Farm is stopped when
    // the fight starts and restarted after it when this file was the one that stopped it; a Quest
    // Walk is stopped the same way.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // ── Tuning ──────────────────────────────────────────────────────────────────────────────

        // Approach stops here (3-D distance to the boss centre; cleaning reaches 9.5 m).
        private const float CleanupBossEngageDistance = 8.0f;
        // Holding and further than this (bounced, drifted) → approach again.
        private const float CleanupBossReengageDistance = 9.3f;
        // Aim point for the approach, measured from the boss centre along the player's bearing.
        // Shrinks by 1 m after every leg that ended out of range or every three lost holds.
        private const float CleanupBossStandoffDefault = 7.0f;
        private const float CleanupBossStandoffMin = 5.5f;
        // Stop the bubble dash this far inside the safe-zone radius.
        private const float CleanupBossSafeZoneMargin = 1.5f;
        // Movement must have stopped before the press, or the clean is cancelled on the first tick.
        private const float CleanupBossSettleDelay = 0.4f;
        // How long after a press the hold is first verified, and the re-verify cadence afterwards.
        private const float CleanupBossHoldVerifyDelay = 1.0f;
        // Re-press pacing after a refused or lost hold.
        private const float CleanupBossRetryInterval = 1.5f;
        private const float CleanupBossWalkRetryInterval = 2f;
        private const float CleanupBossPosRefreshInterval = 5f;
        // A missed ExplosionFinished: leave the bubble this long after the countdown ended.
        private const float CleanupBossExplosionGrace = 4f;
        private const int CleanupBossMaxWalkEnds = 4;
        private const int CleanupBossHoldFailuresBeforeCloser = 3;
        private const float CleanupBossIdleScanJoined = 5f;
        private const float CleanupBossIdleScanAway = 20f;
        private const float CleanupBossStatusInterval = 0.5f;
        // WorldFaceTo needs a beat before the checker's cone sees the new forward.
        private const float CleanupBossFaceToPressDelay = 0.3f;
        // SeaCleanExecutionStateEvent (layout shared with SeaCleanQteFeature.cs: state@0, target
        // InstanceID@8, isShield@16) — the round counters sit after holdProgress@20 / isLastRound@24.
        private const int CleanupBossQteCompletedRoundsOffset = 28;
        private const int CleanupBossQteTotalRoundsOffset = 32;
        private const int CleanupBossQteStateHolding = 2;
        private const int CleanupBossQteStateAwaitingRelease = 3;
        private const int CleanupBossQteStateCompleted = 4;
        private const int CleanupBossQteStateFailed = 5;
        // The gap between the QTE release and the next press: the game must see up, then down.
        private const float CleanupBossQteRepressDelay = 0.1f;
        // A run that stopped for a non-terminal reason (unreachable, depleted cleaner, walk-to-
        // nodes refused) is retried from the idle scan only after this long.
        private const float CleanupBossRestartBackoff = 30f;

        // Event area: the bounding box of every pollutant recorded over four events (hidden pool
        // and visible field) plus 5 m. Area geometry is not in the tables (Areainfo.shapeId is a
        // level object), so this is the measured footprint, not a rule.
        private const float CleanupEventMinX = -54f, CleanupEventMaxX = 48f;
        private const float CleanupEventMinY = -82f, CleanupEventMaxY = -48f;
        private const float CleanupEventMinZ = -21f, CleanupEventMaxZ = 73f;

        // Event hooks (all global). Layouts measured with the recorder — see the header.
        private const string CleanupEventJoinedEventName = "XDTDataAndProtocol.Events.CleanupEventJoinedEvent";
        private const string CleanupEventLeftEventName = "XDTDataAndProtocol.Events.CleanupEventLeftEvent";
        private const string CleanupEventPhaseChangedEventName = "XDTDataAndProtocol.Events.CleanupEventPhaseChangedEvent";
        private const string CleanupEventPublicPhaseFailedEventName = "XDTDataAndProtocol.Events.CleanupEventPublicPhaseFailedEvent";
        private const string CleanupEventPublicPollutantKilledEventName = "XDTDataAndProtocol.Events.CleanupEventPublicPollutantKilledEvent";
        private const string CleanupEventPublicPollutantRemovedEventName = "XDTDataAndProtocol.Events.CleanupEventPublicPollutantRemovedEvent";
        private const string CleanupEventExplosionStartedEventName = "XDTDataAndProtocol.Events.CleanupEventExplosionStartedEvent";
        private const string CleanupEventExplosionFinishedEventName = "XDTDataAndProtocol.Events.CleanupEventExplosionFinishedEvent";
        // {stageIndex@0, stage@4} = 8 bytes. Stages: Started 0 / Personal 1 / Public 2 / Rest 3 /
        // Over 4. Measured 2026-09-16: ONE event = Started+Personal → Public (boss) → Rest (60 s) →
        // Personal → Public (boss) → Over; Joined/Left are the activity entity streaming in and
        // out, not the event's bounds.
        private const string CleanupEventStageChangedEventName = "XDTDataAndProtocol.Events.CleanupEventStageChangedEvent";
        private const int CleanupStageStarted = 0, CleanupStagePersonal = 1, CleanupStagePublic = 2, CleanupStageRest = 3, CleanupStageOver = 4;
        // Event mode of Aura Farm: the live pollutant scan is re-read at most this often when the
        // previous read came back empty (the farm asks every tick while waiting).
        private const float CleanupEventPickRetryInterval = 2f;
        // After the event ends (Over / Left) an active Aura Farm holds this long before it goes back
        // to its own targets and relocations (user rule 2026-09-17).
        private const float CleanupEventEndFarmHold = 5f;
        // Event-mode pick ranks by ROUTE length (user rule 2026-09-16): the nearest few by straight
        // line are measured against the SAME aim point the walker will use (the contamination
        // standoff applied — measuring the raw position put the sweep into the floor and called a
        // clear 21.5 m swim a 32.5 m graph route), and the shortest measured route wins outright.
        // Guards kept from the tour: a route over 3x the line is a sparse-graph artefact and counts
        // as unmeasured; an unmeasured candidate is ranked by line x1.5. A 1 m margin absorbs noise.
        private const int CleanupEventPickShortlist = 6;
        private const float CleanupEventPickImplausibleFactor = 3f;
        private const float CleanupEventPickUnmeasuredPenalty = 1.5f;
        private const float CleanupEventPickSwitchMargin = 1f;

        private struct CleanupEventCandidate
        {
            public uint NetId;
            public Vector3 Position;
            public float Straight;
        }

        private readonly List<CleanupEventCandidate> cleanupEventCandidates = new List<CleanupEventCandidate>(64);

        private enum CleanupBossState
        {
            Idle,
            Approach,
            Settle,
            Engage,
            ToSafeZone,
            InSafeZone,
        }

        // ── State ───────────────────────────────────────────────────────────────────────────────

        // Persisted (Config.xml); Sea Clean tab.
        internal bool cleanupBossAutoEnabled;
        internal string cleanupBossStatus = string.Empty;

        private bool cleanupBossHooksRegistered;
        private int cleanupBossErrorCount;
        private int cleanupBossEpoch = -1;

        // Event bookkeeping, fed by the hooks and by the idle scan.
        private bool cleanupEventJoined;
        private int cleanupEventStage = -1;
        private int cleanupEventStageIndex = -1;
        private bool cleanupEventFarmModeWas;
        private float cleanupEventFarmHoldUntil = -1f;
        private float cleanupEventPickEmptyAt = -100f;
        private bool cleanupEventPickQuiet;
        private uint cleanupBossNetId;
        private bool cleanupBossAlive;
        private float cleanupBossHp;
        private float cleanupBossMaxHp;
        private int cleanupBossPhase;
        private Vector3 cleanupBossPos;
        private bool cleanupBossPosKnown;
        private float cleanupBossPosReadAt;
        private Vector3 cleanupSafeZonePos;
        private float cleanupSafeZoneRadius;
        private float cleanupSafeZoneUntil;
        private bool cleanupExploding;

        // Run
        private CleanupBossState cleanupBossState = CleanupBossState.Idle;
        private bool cleanupBossWalkActive;
        private float cleanupBossNextWalkAt;
        private int cleanupBossWalkEnds;
        private float cleanupBossStandoff = CleanupBossStandoffDefault;
        private float cleanupBossSettleAt;
        private bool cleanupBossHeld;
        private float cleanupBossHoldSince;
        private float cleanupBossVerifyAt;
        private float cleanupBossNextPressAt;
        private int cleanupBossHoldFailures;
        private int cleanupBossPresses;
        private float cleanupBossNextIdleScanAt;
        private float cleanupBossNextStatusAt;
        private float cleanupBossRunStartedAt;
        private bool cleanupBossResumeFarm;
        private bool cleanupBossFacePending;
        private int cleanupBossQteRounds;
        // Set by the spawn handler, consumed by the tick: the run start stops Aura Farm and
        // touches the walker, which is tick work, not event-dispatch work.
        private bool cleanupBossStartRequested;
        private float cleanupBossNoRestartUntil;
        private IntPtr cleanupBossGetExplodingMethod = IntPtr.Zero;

        // Read by FarmWalkRunActive — the out-of-bounds rescue must stay suppressed while this file
        // drives the walker through the water, exactly as for Aura Farm and Quest Walk.
        internal bool CleanupBossDrivingWalker =>
            this.cleanupBossState == CleanupBossState.Approach
            || this.cleanupBossState == CleanupBossState.ToSafeZone;

        // The event is in a stage where the arena is farmed: Started, Personal, or the 60 s Rest
        // between the two bosses. Public belongs to the boss run; Over and Left end it.
        private bool IsCleanupEventFarmStage =>
            this.cleanupEventStage == CleanupStageStarted
            || this.cleanupEventStage == CleanupStagePersonal
            || this.cleanupEventStage == CleanupStageRest;

        // Read by FindClosestAvailableNode: contamination inside the event area only.
        internal bool CleanupEventFarmGateActive => this.cleanupBossAutoEnabled && this.IsCleanupEventFarmStage;

        // Aura Farm's event mode (docs/plans/2026-09-16-ocean-cleanup-farm-mode.md): targets come
        // from the live pollutant scan, nearest first, no tour; an empty scan waits in place; the
        // corrupted-debuff cleanse trip is suppressed. Read by HeartopiaComplete.Farm.cs.
        internal bool CleanupEventFarmModeActive =>
            this.cleanupBossAutoEnabled && this.autoFarmActive && this.IsCleanupEventFarmStage;

        // The post-event pause: Aura Farm neither picks nor relocates while it runs. Read by
        // HeartopiaComplete.Farm.cs next to CleanupEventFarmModeActive.
        internal bool CleanupEventFarmHoldActive =>
            this.autoFarmActive && Time.unscaledTime < this.cleanupEventFarmHoldUntil;

        private bool CleanupBossRunActive => this.cleanupBossState != CleanupBossState.Idle;

        private static void CleanupBossLog(string message)
        {
            ModLogger.Msg("[CleanupBoss] " + message);
        }

        // ── Farm gate ───────────────────────────────────────────────────────────────────────────

        private bool IsCleanupEventFarmCandidate(string markerLabel, Vector3 position)
        {
            if (string.IsNullOrEmpty(markerLabel)
                || markerLabel.IndexOf("Contaminated", StringComparison.Ordinal) < 0)
            {
                return false;
            }

            return position.x >= CleanupEventMinX && position.x <= CleanupEventMaxX
                && position.y >= CleanupEventMinY && position.y <= CleanupEventMaxY
                && position.z >= CleanupEventMinZ && position.z <= CleanupEventMaxZ;
        }

        // ── Per-frame driver (OnUpdate, next to Quest Walk) ─────────────────────────────────────

        private void ProcessCleanupBossOnUpdate()
        {
            if (!this.cleanupBossAutoEnabled)
            {
                if (this.CleanupBossRunActive)
                {
                    this.StopCleanupBossRun("disabled");
                }
                return;
            }

            if (this.cleanupBossErrorCount >= 3)
            {
                return;
            }

            try
            {
                // World gate first: during a swap every cached position belongs to the old map.
                if (!this.IsWorldReady)
                {
                    if (this.CleanupBossRunActive)
                    {
                        this.StopCleanupBossRun("world is changing");
                    }
                    return;
                }

                int epoch = AuraMonoWorldEpoch;
                if (epoch != this.cleanupBossEpoch)
                {
                    bool first = this.cleanupBossEpoch < 0;
                    this.cleanupBossEpoch = epoch;
                    if (!first)
                    {
                        this.ForgetCleanupEvent("world changed (epoch " + epoch + ")");
                    }
                }

                this.EnsureCleanupBossEventHooks();
                this.TrackCleanupEventFarmMode();

                float now = Time.unscaledTime;
                if (!this.CleanupBossRunActive)
                {
                    if (this.cleanupBossStartRequested)
                    {
                        this.cleanupBossStartRequested = false;
                        if (this.cleanupBossAlive && this.cleanupBossNetId != 0U)
                        {
                            this.BeginCleanupBossRun("boss spawned");
                            return;
                        }
                    }
                    this.TickCleanupBossIdle(now);
                    return;
                }

                this.DriveCleanupBoss(now);
            }
            catch (Exception ex)
            {
                this.cleanupBossErrorCount++;
                CleanupBossLog("tick error (" + this.cleanupBossErrorCount + "/3, disabled at 3): " + ex.Message);
                this.cleanupBossStatus = "Error (" + this.cleanupBossErrorCount + "/3): " + ex.Message;
                if (this.cleanupBossErrorCount >= 3)
                {
                    // Going quiet is not stopping: the move axis is stateful and only the abort
                    // releases it (the Quest Walk lesson).
                    this.StopCleanupBossRun("3 tick errors");
                }
            }
        }

        private void ForgetCleanupEvent(string why)
        {
            if (this.CleanupBossRunActive)
            {
                this.StopCleanupBossRun(why);
            }

            this.cleanupEventJoined = false;
            this.cleanupEventStage = -1;
            this.cleanupEventStageIndex = -1;
            this.cleanupBossAlive = false;
            this.cleanupBossNetId = 0U;
            this.cleanupBossPosKnown = false;
            this.cleanupExploding = false;
            this.cleanupBossStartRequested = false;
            this.cleanupBossStatus = "Idle.";
        }

        private void EnsureCleanupBossEventHooks()
        {
            if (this.cleanupBossHooksRegistered)
            {
                return;
            }

            this.cleanupBossHooksRegistered = true;
            int ok = 0;
            ok += this.RegisterGameEventHook(CleanupEventJoinedEventName, 12, this.OnCleanupBossJoinedEvent) ? 1 : 0;
            ok += this.RegisterGameEventHook(CleanupEventLeftEventName, 4, this.OnCleanupBossLeftEvent) ? 1 : 0;
            ok += this.RegisterGameEventHook(CleanupEventPublicPollutantSpawnedEventName, CleanupEventPublicPollutantSpawnedEventBytes, this.OnCleanupBossSpawnedEvent) ? 1 : 0;
            ok += this.RegisterGameEventHook(CleanupEventPhaseChangedEventName, 12, this.OnCleanupBossPhaseChangedEvent) ? 1 : 0;
            ok += this.RegisterGameEventHook(CleanupEventExplosionStartedEventName, 24, this.OnCleanupBossExplosionStartedEvent) ? 1 : 0;
            ok += this.RegisterGameEventHook(CleanupEventExplosionFinishedEventName, 4, this.OnCleanupBossExplosionFinishedEvent) ? 1 : 0;
            ok += this.RegisterGameEventHook(CleanupEventPublicPollutantKilledEventName, 4, this.OnCleanupBossKilledEvent) ? 1 : 0;
            ok += this.RegisterGameEventHook(CleanupEventPublicPollutantRemovedEventName, 4, this.OnCleanupBossRemovedEvent) ? 1 : 0;
            ok += this.RegisterGameEventHook(CleanupEventPublicPhaseFailedEventName, 4, this.OnCleanupBossPhaseFailedEvent) ? 1 : 0;
            ok += this.RegisterGameEventHook(CleanupEventStageChangedEventName, 8, this.OnCleanupEventStageChangedEvent) ? 1 : 0;
            // Shared with SeaCleanQteFeature when that registered first (no extra slot).
            ok += this.RegisterGameEventHook(SeaCleanExecutionStateEventName, SeaCleanExecutionStateEventBytes, this.OnCleanupBossQteStateEvent) ? 1 : 0;
            CleanupBossLog("Registered " + ok + "/11 hooks (10 CleanupEvent + SeaCleanExecutionState).");
        }

        // ── Event handlers (main thread) ────────────────────────────────────────────────────────

        private void OnCleanupBossJoinedEvent(GameEventSnapshot e)
        {
            int configIdx = e.ReadInt32(0);
            int stageIndex = e.ReadInt32(4);
            uint activityNetId = e.ReadUInt32(8);
            this.cleanupEventJoined = true;
            this.cleanupBossAlive = false;
            this.cleanupBossNetId = 0U;
            this.cleanupBossPosKnown = false;
            this.cleanupExploding = false;
            CleanupBossLog("joined configIdx=" + configIdx + " stageIndex=" + stageIndex
                + " activityNetId=" + activityNetId
                + (this.cleanupBossAutoEnabled ? " — farm gate on (contamination inside the event area only)." : "."));
            if (this.cleanupBossAutoEnabled)
            {
                this.cleanupBossStatus = "Event joined — waiting for the boss.";
            }
        }

        private void OnCleanupBossLeftEvent(GameEventSnapshot e)
        {
            CleanupBossLog("left the event.");
            this.ForgetCleanupEvent("left the event");
        }

        private void OnCleanupBossSpawnedEvent(GameEventSnapshot e)
        {
            uint netId = e.ReadUInt32(0);
            float maxHp = e.ReadSingle(4);
            float hp = e.ReadSingle(8);
            int phase = e.ReadInt32(12);
            if (netId == 0U)
            {
                return;
            }

            this.cleanupEventJoined = true;
            this.cleanupBossNetId = netId;
            this.cleanupBossAlive = true;
            this.cleanupBossMaxHp = maxHp;
            this.cleanupBossHp = hp;
            this.cleanupBossPhase = phase;
            this.cleanupBossPosKnown = false;
            this.cleanupExploding = false;
            CleanupBossLog("boss spawned netId=" + netId + " hp=" + hp.ToString("F0") + "/" + maxHp.ToString("F0")
                + " phase=" + phase + (this.cleanupBossAutoEnabled ? "" : " (automation off)."));
            if (this.cleanupBossAutoEnabled && !this.CleanupBossRunActive)
            {
                this.cleanupBossStartRequested = true;
                this.cleanupBossStatus = "Boss spawned — starting.";
            }
        }

        private void OnCleanupBossPhaseChangedEvent(GameEventSnapshot e)
        {
            uint netId = e.ReadUInt32(0);
            float hp = e.ReadSingle(4);
            float maxHp = e.ReadSingle(8);
            if (netId != 0U && (this.cleanupBossNetId == 0U || netId == this.cleanupBossNetId))
            {
                this.cleanupBossHp = hp;
                this.cleanupBossMaxHp = maxHp;
                this.cleanupBossPhase++;
            }
            CleanupBossLog("phase changed netId=" + netId + " hp=" + hp.ToString("F0") + "/" + maxHp.ToString("F0") + ".");
        }

        private void OnCleanupBossExplosionStartedEvent(GameEventSnapshot e)
        {
            uint netId = e.ReadUInt32(0);
            int countdown = e.ReadInt32(4);
            Vector3 zone = new Vector3(e.ReadSingle(8), e.ReadSingle(12), e.ReadSingle(16));
            float radius = e.ReadSingle(20);
            if (radius <= 0f || radius > 50f)
            {
                radius = 4.5f;
            }
            if (countdown <= 0 || countdown > 60)
            {
                countdown = 8;
            }

            float now = Time.unscaledTime;
            this.cleanupSafeZonePos = zone;
            this.cleanupSafeZoneRadius = radius;
            this.cleanupSafeZoneUntil = now + countdown;
            this.cleanupExploding = true;

            float away = this.TryGetLocalPlayerPosition(out Vector3 me) ? Vector3.Distance(me, zone) : -1f;
            CleanupBossLog("explosion started netId=" + netId + " countdown=" + countdown + "s safeZone="
                + FormatNavMeshVector(zone) + " r=" + radius.ToString("F1") + " player " + away.ToString("F1") + "m away.");

            if (!this.CleanupBossRunActive)
            {
                return;
            }

            this.ReleaseCleanupBossButton("explosion started");
            this.AbortCleanupBossWalk();
            this.cleanupBossState = CleanupBossState.ToSafeZone;
            this.cleanupBossNextWalkAt = 0f;
            this.cleanupBossWalkEnds = 0;
            this.cleanupBossStatus = "Explosion in " + countdown + "s — heading for the bubble (" + away.ToString("F0") + "m).";
        }

        private void OnCleanupBossExplosionFinishedEvent(GameEventSnapshot e)
        {
            this.cleanupExploding = false;
            float away = this.TryGetLocalPlayerPosition(out Vector3 me) ? Vector3.Distance(me, this.cleanupSafeZonePos) : -1f;
            CleanupBossLog("explosion finished netId=" + e.ReadUInt32(0) + " — player " + away.ToString("F1")
                + "m from the bubble centre (r=" + this.cleanupSafeZoneRadius.ToString("F1") + ").");

            if (!this.CleanupBossRunActive)
            {
                return;
            }

            if (this.cleanupBossState == CleanupBossState.ToSafeZone || this.cleanupBossState == CleanupBossState.InSafeZone)
            {
                this.AbortCleanupBossWalk();
                this.BeginCleanupBossApproach("explosion over");
            }
        }

        private void OnCleanupBossKilledEvent(GameEventSnapshot e)
        {
            CleanupBossLog("boss killed netId=" + e.ReadUInt32(0) + ".");
            this.cleanupBossAlive = false;
            if (this.CleanupBossRunActive)
            {
                this.StopCleanupBossRun("boss killed");
            }
            this.cleanupBossStatus = "Boss cleaned.";
        }

        private void OnCleanupBossRemovedEvent(GameEventSnapshot e)
        {
            CleanupBossLog("boss removed netId=" + e.ReadUInt32(0) + ".");
            this.cleanupBossAlive = false;
            if (this.CleanupBossRunActive)
            {
                this.StopCleanupBossRun("boss removed");
            }
        }

        private void OnCleanupBossPhaseFailedEvent(GameEventSnapshot e)
        {
            CleanupBossLog("public phase failed netId=" + e.ReadUInt32(0) + ".");
            this.cleanupBossAlive = false;
            if (this.CleanupBossRunActive)
            {
                this.StopCleanupBossRun("public phase failed");
            }
        }

        // ── Run lifecycle ───────────────────────────────────────────────────────────────────────

        private void BeginCleanupBossRun(string why)
        {
            if (this.questWalkFollowing)
            {
                this.StopQuestWalk("cleanup boss");
                CleanupBossLog("stopped Quest Walk for the fight.");
            }

            // One writer on the walker. Aura Farm is stopped through its own toggle (which releases
            // the axis, surfaces from stealth and resets its run state) and restarted the same way
            // once the fight is over.
            this.cleanupBossResumeFarm = false;
            if (this.autoFarmActive)
            {
                this.ToggleAutoFarm();
                this.cleanupBossResumeFarm = !this.autoFarmActive;
                CleanupBossLog(this.cleanupBossResumeFarm
                    ? "stopped Aura Farm for the fight; it restarts afterwards."
                    : "could not stop Aura Farm — continuing anyway.");
            }

            if (!this.farmWalkToNodeEnabled)
            {
                // Through the mod's own toggle handler (mutual exclusion with Stealth Foraging,
                // the 1x speed pin), never the raw field.
                this.OnUguiForagingWalkToggled(true);
                if (!this.farmWalkToNodeEnabled)
                {
                    this.cleanupBossStatus = "Could not enable Walk to Nodes.";
                    this.cleanupBossNoRestartUntil = Time.unscaledTime + CleanupBossRestartBackoff;
                    CleanupBossLog("refused: Walk to Nodes could not be enabled.");
                    return;
                }
                CleanupBossLog("enabled Walk to Nodes.");
            }

            this.cleanupBossRunStartedAt = Time.unscaledTime;
            this.cleanupBossStandoff = CleanupBossStandoffDefault;
            this.cleanupBossPresses = 0;
            this.cleanupBossQteRounds = 0;
            this.cleanupBossHoldFailures = 0;
            this.cleanupBossHeld = false;
            this.cleanupBossWalkActive = false;
            this.cleanupBossNextStatusAt = 0f;
            CleanupBossLog("run started (" + why + ") boss netId=" + this.cleanupBossNetId
                + (this.cleanupBossPosKnown ? " at " + FormatNavMeshVector(this.cleanupBossPos) : " (position pending)") + ".");

            if (this.cleanupExploding && Time.unscaledTime < this.cleanupSafeZoneUntil)
            {
                this.cleanupBossState = CleanupBossState.ToSafeZone;
                this.cleanupBossNextWalkAt = 0f;
                this.cleanupBossWalkEnds = 0;
                this.cleanupBossStatus = "Explosion under way — heading for the bubble.";
                return;
            }

            this.BeginCleanupBossApproach(why);
        }

        private void BeginCleanupBossApproach(string why)
        {
            this.cleanupBossState = CleanupBossState.Approach;
            this.cleanupBossNextWalkAt = 0f;
            this.cleanupBossWalkEnds = 0;
            this.cleanupBossHeld = false;
            this.cleanupBossStatus = "Approaching the boss.";
            CleanupBossLog("approach (" + why + ") standoff=" + this.cleanupBossStandoff.ToString("F1") + "m.");
        }

        private void StopCleanupBossRun(string why)
        {
            if (!this.CleanupBossRunActive)
            {
                return;
            }

            this.ReleaseCleanupBossButton(why);
            this.AbortCleanupBossWalk();
            this.cleanupBossState = CleanupBossState.Idle;
            this.cleanupBossNoRestartUntil = Time.unscaledTime + CleanupBossRestartBackoff;
            float ran = Time.unscaledTime - this.cleanupBossRunStartedAt;
            CleanupBossLog("run stopped (" + why + ") after " + ran.ToString("F0") + "s, " + this.cleanupBossPresses + " presses, "
                + this.cleanupBossQteRounds + " QTE rounds released.");
            this.cleanupBossStatus = "Stopped: " + why + ".";

            if (this.cleanupBossResumeFarm)
            {
                this.cleanupBossResumeFarm = false;
                if (!this.autoFarmActive)
                {
                    try
                    {
                        this.ToggleAutoFarm();
                        CleanupBossLog(this.autoFarmActive ? "restarted Aura Farm." : "Aura Farm did not restart: " + this.autoFarmStatus);
                    }
                    catch (Exception ex)
                    {
                        CleanupBossLog("Aura Farm restart threw: " + ex.Message);
                    }
                }
            }
        }

        private void AbortCleanupBossWalk()
        {
            if (!this.cleanupBossWalkActive)
            {
                return;
            }

            this.cleanupBossWalkActive = false;
            try
            {
                this.AbortFarmWalk();
            }
            catch (Exception ex)
            {
                CleanupBossLog("AbortFarmWalk threw: " + ex.Message);
            }
        }

        // ── Idle: start a fight this file did not see begin ─────────────────────────────────────

        // The toggle can be flipped mid-fight, the hooks register lazily, and a player can join
        // with the boss already up: a live public pollutant found by scan starts the run too.
        private void TickCleanupBossIdle(float now)
        {
            if (now < this.cleanupBossNextIdleScanAt)
            {
                return;
            }

            this.cleanupBossNextIdleScanAt = now + (this.cleanupEventJoined ? CleanupBossIdleScanJoined : CleanupBossIdleScanAway);
            if (now < this.cleanupBossNoRestartUntil)
            {
                return;
            }

            if (this.cleanupBossAlive && this.cleanupBossNetId != 0U)
            {
                this.BeginCleanupBossRun("boss known");
                return;
            }

            if (this.TryScanCleanupBoss(0U, out uint netId, out Vector3 pos, out bool exploding))
            {
                this.cleanupEventJoined = true;
                this.cleanupBossNetId = netId;
                this.cleanupBossAlive = true;
                this.cleanupBossPos = pos;
                this.cleanupBossPosKnown = true;
                this.cleanupBossPosReadAt = now;
                CleanupBossLog("scan found a live public pollutant netId=" + netId + " at " + FormatNavMeshVector(pos)
                    + (exploding ? " (exploding)" : "") + " — no spawn event seen.");
                this.BeginCleanupBossRun("boss found by scan");
            }
        }

        // ── Drive ───────────────────────────────────────────────────────────────────────────────

        private void DriveCleanupBoss(float now)
        {
            if (!this.TryGetLocalPlayerPosition(out Vector3 me))
            {
                this.cleanupBossStatus = "Player position unavailable.";
                return;
            }

            this.RefreshCleanupBossPosition(now);

            switch (this.cleanupBossState)
            {
                case CleanupBossState.Approach:
                    this.TickCleanupBossApproach(now, me);
                    break;
                case CleanupBossState.Settle:
                    if (now >= this.cleanupBossSettleAt)
                    {
                        this.cleanupBossState = CleanupBossState.Engage;
                        this.cleanupBossNextPressAt = now;
                        this.cleanupBossFacePending = true;
                        this.cleanupBossHeld = false;
                    }
                    break;
                case CleanupBossState.Engage:
                    this.TickCleanupBossEngage(now, me);
                    break;
                case CleanupBossState.ToSafeZone:
                    this.TickCleanupBossToSafeZone(now, me);
                    break;
                case CleanupBossState.InSafeZone:
                    this.TickCleanupBossInSafeZone(now, me);
                    break;
            }
        }

        private void RefreshCleanupBossPosition(float now)
        {
            if (this.cleanupBossPosKnown && now < this.cleanupBossPosReadAt + CleanupBossPosRefreshInterval)
            {
                return;
            }

            this.cleanupBossPosReadAt = now;
            if (this.TryScanCleanupBoss(this.cleanupBossNetId, out uint netId, out Vector3 pos, out bool exploding))
            {
                if (!this.cleanupBossPosKnown || Vector3.Distance(pos, this.cleanupBossPos) > 0.5f)
                {
                    CleanupBossLog("boss netId=" + netId + " at " + FormatNavMeshVector(pos) + (exploding ? " (exploding)." : "."));
                }
                this.cleanupBossPos = pos;
                this.cleanupBossPosKnown = true;
                if (this.cleanupBossNetId == 0U)
                {
                    this.cleanupBossNetId = netId;
                }
            }
            else if (!this.cleanupBossPosKnown)
            {
                this.cleanupBossStatus = "Boss position unknown — scanning.";
            }
        }

        private void TickCleanupBossApproach(float now, Vector3 me)
        {
            if (!this.cleanupBossPosKnown)
            {
                return;
            }

            float d = Vector3.Distance(me, this.cleanupBossPos);
            if (d <= CleanupBossEngageDistance)
            {
                this.AbortCleanupBossWalk();
                this.cleanupBossState = CleanupBossState.Settle;
                this.cleanupBossSettleAt = now + CleanupBossSettleDelay;
                this.cleanupBossStatus = "In range (" + d.ToString("F1") + "m) — settling.";
                CleanupBossLog("in range at " + d.ToString("F1") + "m — settling " + CleanupBossSettleDelay.ToString("F1") + "s before the press.");
                return;
            }

            if (!this.cleanupBossWalkActive)
            {
                if (now < this.cleanupBossNextWalkAt)
                {
                    return;
                }

                this.cleanupBossNextWalkAt = now + CleanupBossWalkRetryInterval;
                Vector3 bearing = me - this.cleanupBossPos;
                bearing.y = 0f;
                if (bearing.sqrMagnitude < 0.01f)
                {
                    bearing = Vector3.back;
                }
                bearing.Normalize();
                Vector3 aim = this.cleanupBossPos + bearing * this.cleanupBossStandoff;
                aim.y = this.cleanupBossPos.y + 1f;

                if (!this.TryBeginFarmWalk(aim, "cleanupboss:approach", true, null))
                {
                    this.cleanupBossStatus = "The walker refused the approach — see the log.";
                    CleanupBossLog("walker refused the approach to " + FormatNavMeshVector(aim) + " (" + d.ToString("F1") + "m from the boss).");
                    return;
                }

                this.cleanupBossWalkActive = true;
                CleanupBossLog("walking to " + FormatNavMeshVector(aim) + " — " + d.ToString("F1") + "m from the boss, standoff "
                    + this.cleanupBossStandoff.ToString("F1") + "m.");
                return;
            }

            if (now >= this.cleanupBossNextStatusAt)
            {
                this.cleanupBossNextStatusAt = now + CleanupBossStatusInterval;
                this.cleanupBossStatus = "Approaching the boss (" + d.ToString("F0") + "m).";
            }

            if (this.RunFarmWalkTick())
            {
                this.cleanupBossWalkActive = false;
                this.cleanupBossWalkEnds++;
                float ended = this.TryGetLocalPlayerPosition(out Vector3 after) ? Vector3.Distance(after, this.cleanupBossPos) : d;
                CleanupBossLog("the walker ended the leg at " + ended.ToString("F1") + "m from the boss (" + this.cleanupBossWalkEnds + "/" + CleanupBossMaxWalkEnds + ").");
                if (ended > CleanupBossEngageDistance)
                {
                    this.cleanupBossStandoff = Mathf.Max(CleanupBossStandoffMin, this.cleanupBossStandoff - 1f);
                    if (this.cleanupBossWalkEnds >= CleanupBossMaxWalkEnds)
                    {
                        this.StopCleanupBossRun("cannot reach the boss");
                    }
                }
            }
        }

        private void TickCleanupBossEngage(float now, Vector3 me)
        {
            float d = this.cleanupBossPosKnown ? Vector3.Distance(me, this.cleanupBossPos) : -1f;
            if (d > CleanupBossReengageDistance)
            {
                this.ReleaseCleanupBossButton("drifted to " + d.ToString("F1") + "m");
                this.BeginCleanupBossApproach("out of range at " + d.ToString("F1") + "m");
                return;
            }

            if (!this.TrySeaCleanFarmEnsureCleanerEquipped(now, out bool depleted, out string toolStatus))
            {
                if (depleted)
                {
                    this.StopCleanupBossRun("sea cleaner depleted");
                    return;
                }

                this.cleanupBossStatus = toolStatus;
                return;
            }

            if (!this.cleanupBossHeld)
            {
                if (now < this.cleanupBossNextPressAt)
                {
                    return;
                }

                if (this.cleanupBossFacePending)
                {
                    this.cleanupBossFacePending = false;
                    this.FaceCleanupBoss(me);
                    this.cleanupBossNextPressAt = now + CleanupBossFaceToPressDelay;
                    return;
                }

                if (this.SetCleanupBossButton(true, out string why))
                {
                    this.cleanupBossHeld = true;
                    this.cleanupBossPresses++;
                    this.cleanupBossHoldSince = now;
                    this.cleanupBossVerifyAt = now + CleanupBossHoldVerifyDelay;
                    this.cleanupBossStatus = "Pressed — cleaning the boss (" + d.ToString("F1") + "m).";
                    CleanupBossLog("press #" + this.cleanupBossPresses + " at " + d.ToString("F1") + "m.");
                }
                else
                {
                    this.cleanupBossNextPressAt = now + CleanupBossRetryInterval;
                    this.cleanupBossStatus = "Press failed: " + why;
                    CleanupBossLog("press failed: " + why);
                }
                return;
            }

            if (now < this.cleanupBossVerifyAt)
            {
                return;
            }

            this.cleanupBossVerifyAt = now + CleanupBossHoldVerifyDelay;
            bool downKnown = this.TryReadCleanupBossMainButton(out bool down);
            bool cleaningKnown = this.TryReadCleanupBossCleaning(out bool cleaning);
            if ((downKnown && !down) || (cleaningKnown && !cleaning))
            {
                this.cleanupBossHoldFailures++;
                float held = now - this.cleanupBossHoldSince;
                this.ReleaseCleanupBossButton("hold lost after " + held.ToString("F1") + "s (down=" + (downKnown ? down.ToString() : "?")
                    + " cleaning=" + (cleaningKnown ? cleaning.ToString() : "?") + ", " + this.cleanupBossHoldFailures + " in a row)");
                this.cleanupBossNextPressAt = now + CleanupBossRetryInterval;
                this.cleanupBossFacePending = true;
                if (this.cleanupBossHoldFailures >= CleanupBossHoldFailuresBeforeCloser)
                {
                    this.cleanupBossHoldFailures = 0;
                    if (this.cleanupBossStandoff > CleanupBossStandoffMin)
                    {
                        this.cleanupBossStandoff = Mathf.Max(CleanupBossStandoffMin, this.cleanupBossStandoff - 1f);
                        this.BeginCleanupBossApproach("hold keeps failing — moving closer");
                    }
                }
                return;
            }

            this.cleanupBossHoldFailures = 0;
            this.cleanupBossStatus = "Cleaning the boss — HP " + this.cleanupBossHp.ToString("F0") + "/" + this.cleanupBossMaxHp.ToString("F0")
                + ", " + d.ToString("F1") + "m, held " + (now - this.cleanupBossHoldSince).ToString("F0") + "s.";
        }

        private void TickCleanupBossToSafeZone(float now, Vector3 me)
        {
            float d = Vector3.Distance(me, this.cleanupSafeZonePos);
            float stopAt = Mathf.Max(1f, this.cleanupSafeZoneRadius - CleanupBossSafeZoneMargin);
            float left = this.cleanupSafeZoneUntil - now;
            if (d <= stopAt)
            {
                this.AbortCleanupBossWalk();
                this.cleanupBossState = CleanupBossState.InSafeZone;
                this.cleanupBossStatus = "Inside the bubble (" + d.ToString("F1") + "m) — waiting for the explosion.";
                CleanupBossLog("inside the bubble at " + d.ToString("F1") + "m with " + left.ToString("F1") + "s to spare.");
                return;
            }

            if (!this.cleanupExploding && now > this.cleanupSafeZoneUntil + CleanupBossExplosionGrace)
            {
                CleanupBossLog("no ExplosionFinished " + CleanupBossExplosionGrace.ToString("F0") + "s past the countdown — back to the boss.");
                this.AbortCleanupBossWalk();
                this.BeginCleanupBossApproach("explosion timed out");
                return;
            }

            if (!this.cleanupBossWalkActive)
            {
                if (now < this.cleanupBossNextWalkAt)
                {
                    return;
                }

                this.cleanupBossNextWalkAt = now + 1f;
                // Rule 4.11: straight to the centre, always. The boss on the line is not an
                // obstacle — twelve dashes whose line ran 0.2-4.9 m from its centre all reached
                // the bubble; its collider only ever fooled the sweep.
                if (!this.TryBeginFarmWalk(this.cleanupSafeZonePos, "cleanupboss:safezone", true, null))
                {
                    this.cleanupBossStatus = "The walker refused the bubble — see the log.";
                    CleanupBossLog("walker refused the bubble at " + FormatNavMeshVector(this.cleanupSafeZonePos) + " (" + d.ToString("F1") + "m).");
                    return;
                }

                this.cleanupBossWalkActive = true;
                CleanupBossLog("dashing to the bubble " + d.ToString("F1") + "m away, " + left.ToString("F1") + "s left.");
                return;
            }

            if (now >= this.cleanupBossNextStatusAt)
            {
                this.cleanupBossNextStatusAt = now + CleanupBossStatusInterval;
                this.cleanupBossStatus = "To the bubble (" + d.ToString("F0") + "m, " + Mathf.Max(0f, left).ToString("F0") + "s).";
            }

            if (this.RunFarmWalkTick())
            {
                this.cleanupBossWalkActive = false;
                this.cleanupBossWalkEnds++;
                CleanupBossLog("the walker ended the bubble leg at " + d.ToString("F1") + "m (" + this.cleanupBossWalkEnds + ").");
            }
        }

        private void TickCleanupBossInSafeZone(float now, Vector3 me)
        {
            if (!this.cleanupExploding && now > this.cleanupSafeZoneUntil + CleanupBossExplosionGrace)
            {
                CleanupBossLog("no ExplosionFinished " + CleanupBossExplosionGrace.ToString("F0") + "s past the countdown — back to the boss.");
                this.BeginCleanupBossApproach("explosion timed out");
                return;
            }

            float d = Vector3.Distance(me, this.cleanupSafeZonePos);
            if (d > this.cleanupSafeZoneRadius - 0.5f && now < this.cleanupSafeZoneUntil)
            {
                CleanupBossLog("drifted out of the bubble (" + d.ToString("F1") + "m) — going back in.");
                this.cleanupBossState = CleanupBossState.ToSafeZone;
                this.cleanupBossNextWalkAt = 0f;
                return;
            }

            if (now >= this.cleanupBossNextStatusAt)
            {
                this.cleanupBossNextStatusAt = now + CleanupBossStatusInterval;
                this.cleanupBossStatus = "Inside the bubble (" + d.ToString("F1") + "m) — " + Mathf.Max(0f, this.cleanupSafeZoneUntil - now).ToString("F0") + "s.";
            }
        }

        // The boss QTE drives the button (see the header). Only while the run is engaged: the
        // event is global and any sea-clean QTE the player holds elsewhere is not ours to answer.
        private void OnCleanupBossQteStateEvent(GameEventSnapshot e)
        {
            if (this.cleanupBossState != CleanupBossState.Engage)
            {
                return;
            }

            int state = e.ReadInt32(SeaCleanExecStateOffset);
            bool shield = e.ReadBool(SeaCleanExecIsShieldOffset);
            int done = e.ReadInt32(CleanupBossQteCompletedRoundsOffset);
            int total = e.ReadInt32(CleanupBossQteTotalRoundsOffset);
            float now = Time.unscaledTime;
            string rounds = " (round " + Mathf.Clamp(done + 1, 1, Mathf.Max(1, total)) + "/" + total + (shield ? ", shield)" : ")");

            switch (state)
            {
                case SeaCleanExecStateReady:
                    // A fresh QTE with the button already down starts its own hold (OnStateTick);
                    // a round boundary after our release needs the press back.
                    if (!this.cleanupBossHeld)
                    {
                        this.cleanupBossNextPressAt = now + CleanupBossQteRepressDelay;
                        this.cleanupBossFacePending = false;
                    }
                    CleanupBossLog("QTE ready" + rounds + (this.cleanupBossHeld ? " — holding." : " — pressing again."));
                    break;
                case CleanupBossQteStateHolding:
                    CleanupBossLog("QTE holding" + rounds + ".");
                    break;
                case CleanupBossQteStateAwaitingRelease:
                    // The gauge is red: release, the round counts on the way up.
                    this.cleanupBossQteRounds++;
                    if (this.cleanupBossHeld)
                    {
                        this.ReleaseCleanupBossButton("QTE gauge full" + rounds);
                        this.cleanupBossNextPressAt = now + CleanupBossQteRepressDelay;
                        this.cleanupBossFacePending = false;
                    }
                    else
                    {
                        CleanupBossLog("QTE gauge full" + rounds + " but the button was not ours to release.");
                    }
                    break;
                case CleanupBossQteStateCompleted:
                    CleanupBossLog("QTE completed after " + total + " round(s) — resuming the hold.");
                    if (!this.cleanupBossHeld)
                    {
                        this.cleanupBossNextPressAt = now + CleanupBossQteRepressDelay;
                        this.cleanupBossFacePending = false;
                    }
                    break;
                case CleanupBossQteStateFailed:
                    CleanupBossLog("QTE failed (timed out)" + rounds + " — resuming the hold.");
                    if (!this.cleanupBossHeld)
                    {
                        this.cleanupBossNextPressAt = now + CleanupBossQteRepressDelay;
                        this.cleanupBossFacePending = false;
                    }
                    break;
            }
        }

        private static string CleanupStageName(int stage)
        {
            switch (stage)
            {
                case CleanupStageStarted: return "Started";
                case CleanupStagePersonal: return "Personal";
                case CleanupStagePublic: return "Public";
                case CleanupStageRest: return "Rest";
                case CleanupStageOver: return "Over";
                default: return "none";
            }
        }

        private void OnCleanupEventStageChangedEvent(GameEventSnapshot e)
        {
            int stageIndex = e.ReadInt32(0);
            int stage = e.ReadInt32(4);
            this.cleanupEventStageIndex = stageIndex;
            this.cleanupEventStage = stage;
            this.cleanupEventJoined = true;
            CleanupBossLog("stage " + CleanupStageName(stage) + " (" + stage + ", index " + stageIndex + ").");
            if (this.cleanupBossAutoEnabled && !this.CleanupBossRunActive)
            {
                this.cleanupBossStatus = "Event stage " + CleanupStageName(stage) + ".";
            }
        }

        // Logs the farm event mode flipping and, on the way out, drops the tour so the plan is
        // rebuilt from where the player stands rather than from the arena.
        private void TrackCleanupEventFarmMode()
        {
            bool active = this.CleanupEventFarmModeActive;
            if (active == this.cleanupEventFarmModeWas)
            {
                return;
            }

            this.cleanupEventFarmModeWas = active;
            if (active)
            {
                this.cleanupEventPickEmptyAt = -100f;
                CleanupBossLog("farm event mode ON (stage " + CleanupStageName(this.cleanupEventStage)
                    + "): nearest live pollutant, no tour, no relocation; corrupted-debuff cleanse trips suppressed.");
            }
            else
            {
                // The event is over (Over, or the entity left) and the farm is still running: hold
                // it for a few seconds before it resumes its own targets.
                bool eventEnded = this.cleanupEventStage == CleanupStageOver || this.cleanupEventStage < 0;
                if (eventEnded && this.autoFarmActive)
                {
                    this.cleanupEventFarmHoldUntil = Time.unscaledTime + CleanupEventEndFarmHold;
                }
                CleanupBossLog("farm event mode OFF (stage " + CleanupStageName(this.cleanupEventStage)
                    + ", farm " + (this.autoFarmActive ? "on" : "off") + ") — tour reset"
                    + (eventEnded && this.autoFarmActive ? ", farm held " + CleanupEventEndFarmHold.ToString("F0") + "s." : "."));
                try
                {
                    this.ResetFarmTour();
                }
                catch (Exception ex)
                {
                    CleanupBossLog("ResetFarmTour threw: " + ex.Message);
                }
            }
        }

        // Aura Farm's target in event mode: the nearest live, exclusive, visible pollutant inside
        // the event area, by 3-D distance from the player, skipping spots the farm has just dwelt
        // at (recentlyVisitedNodes — the dwell stamps them for 15/60 s). Same reads as the boss
        // scan; everything pinned and synchronous. Empty results are cached for 2 s.
        private bool TryPickCleanupEventTarget(out Vector3 position, out string label)
        {
            position = Vector3.zero;
            label = "Contaminated";
            float now = Time.unscaledTime;
            if (now < this.cleanupEventPickEmptyAt + CleanupEventPickRetryInterval)
            {
                return false;
            }

            if (!this.TryGetLocalPlayerPosition(out Vector3 me))
            {
                this.cleanupEventPickEmptyAt = now;
                return false;
            }

            if (!this.EnsureSeaCleanQteAuraResolved(out _)
                || this.seaCleanQteMonsterClass == IntPtr.Zero
                || this.seaCleanQteGetIsCleanedMethod == IntPtr.Zero)
            {
                this.cleanupEventPickEmptyAt = now;
                return false;
            }

            int seen = 0;
            int inArea = 0;
            int skippedVisited = 0;
            this.cleanupEventCandidates.Clear();
            List<uint> pins = new List<uint>();
            try
            {
                if (!this.TryAuraMonoGetComponentObjects(this.seaCleanQteMonsterClass, out List<IntPtr> components, pins)
                    || components == null || components.Count == 0)
                {
                    this.cleanupEventPickEmptyAt = now;
                    return false;
                }

                for (int i = 0; i < components.Count; i++)
                {
                    IntPtr compObj = components[i];
                    if (compObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    if (this.SeaCleanQteInvokeBoolGetter(compObj, this.seaCleanQteGetIsCleanedMethod, out bool isCleaned) && isCleaned)
                    {
                        continue;
                    }
                    if (this.SeaCleanQteInvokeBoolGetter(compObj, this.seaCleanQteGetIsHiddenMethod, out bool isHidden) && isHidden)
                    {
                        continue;
                    }
                    if ((this.SeaCleanQteInvokeBoolGetter(compObj, this.seaCleanQteGetIsSharedMethod, out bool shared) && shared)
                        || (this.SeaCleanQteInvokeBoolGetter(compObj, this.seaCleanQteGetIsPublicMethod, out bool pub) && pub)
                        || (this.SeaCleanQteInvokeBoolGetter(compObj, this.seaCleanQteGetIsPlayerHostedMethod, out bool hosted) && hosted))
                    {
                        continue;
                    }

                    if (!this.TryGetMonoObjectMember(compObj, "entity", out IntPtr entityObj) || entityObj == IntPtr.Zero)
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
                        if (!this.TryGetMonoVector3Member(entityObj, "position", out Vector3 p))
                        {
                            continue;
                        }

                        seen++;
                        if (!this.IsCleanupEventFarmCandidate("Contaminated", p))
                        {
                            continue;
                        }

                        inArea++;
                        bool visited = false;
                        foreach (Vector3 v in this.recentlyVisitedNodes.Keys)
                        {
                            if (Vector3.Distance(v, p) < 2f)
                            {
                                visited = true;
                                break;
                            }
                        }
                        if (visited)
                        {
                            skippedVisited++;
                            continue;
                        }

                        this.cleanupEventCandidates.Add(new CleanupEventCandidate
                        {
                            NetId = netId,
                            Position = p,
                            Straight = Vector3.Distance(me, p),
                        });
                    }
                    finally
                    {
                        AuraMonoPinFree(entityPin);
                    }
                }
            }
            finally
            {
                FreeAuraMonoPins(pins);
            }

            if (this.cleanupEventCandidates.Count == 0)
            {
                this.cleanupEventPickEmptyAt = now;
                if (!this.cleanupEventPickQuiet)
                {
                    CleanupBossLog("farm event mode: no target (" + seen + " live pollutants, " + inArea + " in the area, "
                        + skippedVisited + " just dwelt at) — waiting.");
                }
                return false;
            }

            this.cleanupEventCandidates.Sort((a, b) => a.Straight.CompareTo(b.Straight));
            int best = this.PickCleanupEventCandidateByRoute(me, out float bestLength, out int measuredCount, out bool bestMeasured);
            CleanupEventCandidate pick = this.cleanupEventCandidates[best];
            position = pick.Position;

            string how;
            if (best == 0)
            {
                how = "nearest by line";
            }
            else
            {
                CleanupEventCandidate first = this.cleanupEventCandidates[0];
                how = "nearest by ROUTE, not by line: " + pick.Straight.ToString("F0") + "m away costs " + bestLength.ToString("F0")
                    + "m" + (bestMeasured ? "" : " (estimated)") + ", against " + first.Straight.ToString("F0") + "m away";
            }

            if (!this.cleanupEventPickQuiet)
            {
                CleanupBossLog("farm event mode: pollutant netId=" + pick.NetId + " at " + FormatNavMeshVector(pick.Position)
                    + " (" + pick.Straight.ToString("F1") + "m line, " + bestLength.ToString("F1") + "m route; " + how + "; "
                    + measuredCount + " of " + Mathf.Min(CleanupEventPickShortlist, this.cleanupEventCandidates.Count) + " measured; "
                    + inArea + " in the area, " + skippedVisited + " just dwelt at).");
            }
            return true;
        }

        // Rule 0.2 for event mode: the walker may take its rescue teleport only when the failed node
        // is the ONLY target. The tour is empty in event mode, so "another stop" is answered from a
        // fresh live scan — any live candidate that is not the failed node means "switch, don't warp".
        // Read by HasAnotherFarmTourStop (FarmRoutePlanFeature.cs).
        private bool HasAnotherCleanupEventTarget(Vector3 exceptThis)
        {
            this.cleanupEventPickQuiet = true;
            this.cleanupEventPickEmptyAt = -100f;
            try
            {
                this.TryPickCleanupEventTarget(out _, out _);
            }
            catch (Exception ex)
            {
                CleanupBossLog("another-target scan threw: " + ex.Message);
            }
            finally
            {
                this.cleanupEventPickQuiet = false;
            }

            int others = 0;
            for (int i = 0; i < this.cleanupEventCandidates.Count; i++)
            {
                if (Vector3.Distance(this.cleanupEventCandidates[i].Position, exceptThis) > 2f)
                {
                    others++;
                }
            }

            CleanupBossLog("farm event mode: the walk to " + FormatNavMeshVector(exceptThis) + " failed — " + others
                + " other live target(s) — " + (others > 0 ? "switching target, no rescue teleport." : "nothing else left, the walker decides."));
            return others > 0;
        }

        // Rank the straight-line-sorted candidates by measured route (the tour's PickFarmTourStopByRoute
        // rules, see the constants above). Returns the index into cleanupEventCandidates; 0 when
        // nothing could be measured. A clear straight swim measures as its line — the same test the
        // route builder makes first — so the pick equals what the walker will actually swim.
        private int PickCleanupEventCandidateByRoute(Vector3 origin, out float bestLength, out int measuredCount, out bool bestMeasured)
        {
            bestLength = float.MaxValue;
            measuredCount = 0;
            bestMeasured = false;
            int best = -1;
            float runnerUpLength = float.MaxValue;
            int shortlist = Mathf.Min(CleanupEventPickShortlist, this.cleanupEventCandidates.Count);
            System.Text.StringBuilder trace = new System.Text.StringBuilder(160);

            int startIndex = -1;
            try
            {
                if (!this.TryFindReachableTrackGraphNode(origin, FarmWalkGraphSnapRadius, out startIndex, this.farmWalkExcludedNodes, "start"))
                {
                    startIndex = -1;
                }
            }
            catch (Exception ex)
            {
                CleanupBossLog("graph snap threw: " + ex.Message);
                startIndex = -1;
            }

            for (int i = 0; i < shortlist; i++)
            {
                CleanupEventCandidate c = this.cleanupEventCandidates[i];

                // The bound: a route is never shorter than its line and the list is in line order.
                if (best >= 0 && bestLength <= c.Straight)
                {
                    break;
                }

                measuredCount++;

                // The walker aims 3 m off the pollutant (TryBeginFarmWalk's contamination standoff,
                // up for a hosted anchor, down otherwise) — measure what it will actually swim.
                Vector3 aim = c.Position;
                this.TryGetContaminatedAnchorClass(c.Position, out bool hostedAnchor);
                aim.y += hostedAnchor ? FarmWalkContaminationStandoff : -FarmWalkContaminationStandoff;

                bool measured;
                float routeLength;
                if (this.IsFarmWalkDirectSwimClear(origin, aim, out _))
                {
                    measured = true;
                    routeLength = Vector3.Distance(origin, aim);
                }
                else
                {
                    try
                    {
                        measured = this.TryMeasureFarmRouteLength(origin, startIndex, aim, out routeLength);
                    }
                    catch (Exception ex)
                    {
                        CleanupBossLog("route measure threw: " + ex.Message);
                        measured = false;
                        routeLength = 0f;
                    }
                }

                if (measured && routeLength > c.Straight * CleanupEventPickImplausibleFactor)
                {
                    measured = false;
                }
                if (!measured)
                {
                    routeLength = c.Straight * CleanupEventPickUnmeasuredPenalty;
                }

                if (i == 0)
                {
                    runnerUpLength = routeLength;
                }

                if (trace.Length > 0)
                {
                    trace.Append(" | ");
                }
                trace.Append(c.Straight.ToString("F0")).Append("m->").Append(routeLength.ToString("F0")).Append('m');
                if (!measured)
                {
                    trace.Append('~');
                }

                // Strictly the shortest route; a candidate must beat the holder by the margin.
                if (best < 0 || routeLength + CleanupEventPickSwitchMargin < bestLength)
                {
                    best = i;
                    bestLength = routeLength;
                    bestMeasured = measured;
                }
            }

            if (!this.cleanupEventPickQuiet)
            {
                CleanupBossLog("farm event mode: routes (line->route, ~ = estimated): " + trace + ".");
            }
            return best < 0 ? 0 : best;
        }

        // ── Levers ──────────────────────────────────────────────────────────────────────────────

        private void FaceCleanupBoss(Vector3 me)
        {
            if (!this.cleanupBossPosKnown)
            {
                return;
            }

            Vector3 flat = this.cleanupBossPos - me;
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.01f)
            {
                return;
            }

            flat.Normalize();
            Quaternion rot = Quaternion.LookRotation(flat, Vector3.up);
            if (this.TrySyncLocalPlayerCastFacingMono(me, rot.eulerAngles, rot, flat, false, false, out string status))
            {
                CleanupBossLog("faced the boss: " + status);
            }
            else
            {
                CleanupBossLog("facing failed: " + status);
            }
        }

        private void ReleaseCleanupBossButton(string why)
        {
            if (!this.cleanupBossHeld)
            {
                return;
            }

            this.cleanupBossHeld = false;
            if (this.SetCleanupBossButton(false, out string fail))
            {
                CleanupBossLog("released (" + why + ").");
            }
            else
            {
                CleanupBossLog("release failed (" + why + "): " + fail);
            }
        }

        // The game's own main-interaction listener: LocalPlayerComponent._OnMainButtonEvent(bool)
        // sets _mainButtonDown and forwards to the current state, so the press runs the
        // SeaCleanCommand → PlayerStateSeaClean flow with the animation and the protocol. Down is
        // ignored while the gameplay HUD input is blocked; up always lands once the flag is set.
        private bool SetCleanupBossButton(bool down, out string why)
        {
            why = string.Empty;
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoObjectGetClass == null)
            {
                why = "AuraMono not ready";
                return false;
            }

            if (!AuraMonoPinningAvailable)
            {
                why = "pinning unavailable";
                return false;
            }

            if (!this.TryGetAuraMonoLocalPlayerObject(out IntPtr playerObj) || playerObj == IntPtr.Zero)
            {
                why = "local player unresolved";
                return false;
            }

            uint pin = AuraMonoPinNew(playerObj);
            try
            {
                IntPtr playerClass = auraMonoObjectGetClass(playerObj);
                IntPtr method = playerClass == IntPtr.Zero
                    ? IntPtr.Zero
                    : this.FindAuraMonoMethodOnHierarchy(playerClass, "_OnMainButtonEvent", 1);
                if (method == IntPtr.Zero)
                {
                    why = "_OnMainButtonEvent(bool) unresolved";
                    return false;
                }

                if (!this.TryInvokeAuraMonoBool1(playerObj, method, down))
                {
                    why = "_OnMainButtonEvent threw";
                    return false;
                }

                return true;
            }
            finally
            {
                AuraMonoPinFree(pin);
            }
        }

        private bool TryReadCleanupBossMainButton(out bool down)
        {
            down = false;
            if (!this.TryGetAuraMonoLocalPlayerObject(out IntPtr playerObj) || playerObj == IntPtr.Zero)
            {
                return false;
            }

            return this.TryGetMonoBoolMember(playerObj, "_mainButtonDown", out down);
        }

        // PlayerStateSeaClean.IsCleaning (= a locked target). HasTarget on the checker is not
        // enough: PhysicalSelect acquires every frame whether or not the state is holding.
        private bool TryReadCleanupBossCleaning(out bool cleaning)
        {
            cleaning = false;
            if (!this.TryFindAuraMonoPlayerState("PlayerStateSeaClean", out IntPtr stateObj) || stateObj == IntPtr.Zero)
            {
                return false;
            }

            if (!this.TryInvokeAuraMonoZeroArg(stateObj, out IntPtr boxed, "get_IsCleaning") || boxed == IntPtr.Zero)
            {
                return false;
            }

            return this.TryUnboxMonoBoolean(boxed, out cleaning);
        }

        // Scan the SeaCleanMonsterComponent pool for a live public pollutant (wantNetId = 0: any).
        // Uses the QTE feature's resolved class + getters; everything is synchronous and pinned.
        private bool TryScanCleanupBoss(uint wantNetId, out uint netId, out Vector3 pos, out bool exploding)
        {
            netId = 0U;
            pos = Vector3.zero;
            exploding = false;

            if (!this.EnsureSeaCleanQteAuraResolved(out _)
                || this.seaCleanQteMonsterClass == IntPtr.Zero
                || this.seaCleanQteGetIsPublicMethod == IntPtr.Zero)
            {
                return false;
            }

            if (this.cleanupBossGetExplodingMethod == IntPtr.Zero)
            {
                this.cleanupBossGetExplodingMethod = this.FindAuraMonoMethodOnHierarchy(this.seaCleanQteMonsterClass, "get_PublicIsExploding", 0);
            }

            List<uint> pins = new List<uint>();
            try
            {
                if (!this.TryAuraMonoGetComponentObjects(this.seaCleanQteMonsterClass, out List<IntPtr> components, pins)
                    || components == null || components.Count == 0)
                {
                    return false;
                }

                for (int i = 0; i < components.Count; i++)
                {
                    IntPtr compObj = components[i];
                    if (compObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    if (!this.SeaCleanQteInvokeBoolGetter(compObj, this.seaCleanQteGetIsPublicMethod, out bool isPublic) || !isPublic)
                    {
                        continue;
                    }

                    if (this.SeaCleanQteInvokeBoolGetter(compObj, this.seaCleanQteGetIsCleanedMethod, out bool isCleaned) && isCleaned)
                    {
                        continue;
                    }

                    if (!this.TryGetMonoObjectMember(compObj, "entity", out IntPtr entityObj) || entityObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    uint entityPin = AuraMonoPinNew(entityObj);
                    try
                    {
                        if (!this.TryGetMonoUInt32Member(entityObj, "netId", out uint id) || id == 0U)
                        {
                            continue;
                        }

                        if (wantNetId != 0U && id != wantNetId)
                        {
                            continue;
                        }

                        if (!this.TryGetMonoVector3Member(entityObj, "position", out Vector3 p))
                        {
                            continue;
                        }

                        netId = id;
                        pos = p;
                        exploding = this.SeaCleanQteInvokeBoolGetter(compObj, this.cleanupBossGetExplodingMethod, out bool ex) && ex;
                        return true;
                    }
                    finally
                    {
                        AuraMonoPinFree(entityPin);
                    }
                }
            }
            finally
            {
                FreeAuraMonoPins(pins);
            }

            return false;
        }
    }
}
