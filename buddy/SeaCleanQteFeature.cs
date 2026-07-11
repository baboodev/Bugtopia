using System;
using System.Collections.Generic;
using UnityEngine;

namespace HeartopiaMod
{
    // Auto Sea Clean — full auto ocean-cleanup (2026-07-09 update).
    //
    // The legit flow (see .research-record/SEA_CLEAN_QTE_REPORT.md + memory
    // seaclean-qte-client-authoritative) is: equip HandHoldSeaCleaner (ToolSystem toolId 7) → aim
    // at a pollutant → hold main button → PlayerStateSeaClean damages the EXCLUSIVE pollutant
    // locally → at an HP threshold a client-rolled hold-and-release QTE runs → on pass
    // SeaCleanMonsterComponent.ExecuteKill() → MarkCleaned(qtePassed:true) → the dying Tick sends
    // ReqExclusiveSyncState(netId, typeId, 0f, true, true). The server never re-decides anything —
    // the whole exclusive branch is client-authoritative and self-reported.
    //
    // ExecuteKill() itself has NO precondition beyond !isShared (no QTE, no cleaning state), so
    // this feature short-circuits the entire loop:
    //   * AUTO-CLEAN LOOP (scan-driven): every 0.5s scan SeaCleanMonsterComponent, pick the
    //     nearest exclusive pollutant (!IsShared && !IsPublicPollutant && !IsPlayerHosted &&
    //     !IsCleaned && !IsHidden) within range and AuraMono-invoke ExecuteKill() on it — paced,
    //     one kill per interval. The sea cleaner must be in hand (server plausibility: the legit
    //     client can never clean without it); if hands are empty the tool is AUTO-EQUIPPED via
    //     the shared ToolSystem.SetHandhold path (TryEquipHandTool(7)). If another tool is held
    //     we do NOT yank it — status tells the user.
    //   * QTE AUTO-PASS (event-driven, kept from v1): SeaCleanExecutionStateEvent(state==Ready,
    //     !shield) → exclusive target ExecuteKill() / public boss
    //     ReqStartCleanPublicPollutant(netId, qtePassed:true) ("never-miss"; server owns boss HP).
    //     This still covers manual cleaning of the public boss and any QTE the loop can't own.
    //
    // Shared/env-hosted and player-hosted (body pollution) targets have no client kill lever —
    // they are skipped (the QTE auto-pass still helps when cleaning them manually).
    //
    // Fail-closed everywhere: if AuraMono / pinning / type resolution is not ready, no-op — never
    // crash. Gated entirely behind seaCleanQteEnabled; no runtime work (and no hook) when OFF.
    public partial class HeartopiaComplete
    {
        // Verbose diagnosis. Off by default — the user turns this on to verify offsets/enum/target
        // resolution live (we cannot test in-game). Every key step logs under [SeaCleanQte].
        private bool MasterLogSeaCleanQte = false;

        private bool seaCleanQteEnabled = false;
        private KeyCode seaCleanQteHotkey = KeyCode.None;

        // Event hooks are registered once (idempotent per name in the engine) the first time the
        // feature is enabled; the handlers themselves gate on seaCleanQteEnabled so a disabled
        // feature does zero work. This mirrors PetPlay's EnsurePetPlayEventHooks.
        private bool seaCleanQteEventHooksRegistered = false;

        // Boss netId captured from CleanupEventPublicPollutantSpawnedEvent (fallback if the scan
        // can't identify a public pollutant currently in QTE).
        private uint seaCleanQteBossNetId = 0U;

        // Throttles: collapse duplicate same-dispatch Ready events, and avoid re-killing the same
        // exclusive target within a short window (harmless but keeps the log clean).
        private float seaCleanQteNextActionAt = 0f;
        private uint seaCleanQteLastActedNetId = 0U;
        private float seaCleanQteLastActedAt = -999f;
        private float seaCleanQteNextResolveAt = 0f;

        // ---- Auto-clean loop (scan-driven ExecuteKill) ----
        // ToolSystem tool type of the sea cleaner (EcsClient ToolType enum: Null=0, Axe=1,
        // Sprinkler=2, Rod=3, BirdScanner=4, Net=5, Pad=6, SeaCleaner=7).
        internal const int SeaCleanerToolTypeId = 7;
        private const float SeaCleanAutoScanInterval = 0.5f;
        private const float SeaCleanAutoEquipRetryInterval = 3f;

        // User-tunable via tab sliders (persisted in the unified config). Radius stays under the
        // game's own aim-select DetectMaxDistance (~20m) so every kill is plausibly reachable.
        private const float SeaCleanAutoRadiusMin = 1f;
        private const float SeaCleanAutoRadiusMax = 20f;
        private const float SeaCleanAutoRadiusDefault = 7f;
        // Out-of-world guard: a game bug spawns pollutants far below the playable sea floor (valid
        // sea points sit at Y ~ -23..-65). The radar ignores any pollutant below this Y so the ESP /
        // game map / Aura Farm never target an unreachable one (user-reported boundary 2026-07-11).
        private const float ContaminatedRadarMinWorldY = -78f;
        // Paced-kill interval used when "Clean Without Delays" is OFF: one kill per scan pass (0.5s)
        // for server plausibility. When the toggle is ON, every in-range pollutant is swept at once.
        private const float SeaCleanPacedKillDelaySeconds = 0.05f;

        private float seaCleanAutoRadius = SeaCleanAutoRadiusDefault;
        // "Clean Without Delays" toggle (replaced the old 0-1s delay slider). Default ON = instant sweep.
        private bool seaCleanCleanNoDelay = true;

        private float seaCleanAutoNextScanAt = 0f;
        private float seaCleanAutoNextKillAt = 0f;
        private float seaCleanAutoNextEquipAttemptAt = 0f;
        private int seaCleanAutoKillCount = 0;
        private string seaCleanAutoLastStatus = string.Empty;

        // Results of the most recent completed sweep pass (TrySeaCleanAutoCleanPass), no matter
        // which caller ran it — the standalone tick and the Aura Farm contamination dwell share
        // one 0.5s scan throttle, so a pass may execute in either caller's frame slot. The farm
        // dwell consumes these exactly once per pass for its done-detection.
        private float seaCleanLastPassCompletedAt = -1f;
        private int seaCleanLastPassActionable = 0;
        private int seaCleanLastPassKilled = 0;
        private int seaCleanLastPassNoLever = 0;

        private struct SeaCleanAutoCandidate
        {
            public int Index;
            public uint NetId;
            public float DistSqr;
        }

        // Reused across scan passes to avoid per-pass allocations.
        private readonly List<SeaCleanAutoCandidate> seaCleanAutoCandidates = new List<SeaCleanAutoCandidate>();
        private static readonly Comparison<SeaCleanAutoCandidate> SeaCleanAutoCandidateByDistance =
            (a, b) => a.DistSqr.CompareTo(b.DistSqr);

        // Cached AuraMono class/method pointers (class/method IntPtrs may stay raw — image lifetime).
        private IntPtr seaCleanQteMonsterClass = IntPtr.Zero;
        private IntPtr seaCleanQteExecuteKillMethod = IntPtr.Zero;
        private IntPtr seaCleanQteGetIsInQteMethod = IntPtr.Zero;
        private IntPtr seaCleanQteGetIsSharedMethod = IntPtr.Zero;
        private IntPtr seaCleanQteGetIsPublicMethod = IntPtr.Zero;
        private IntPtr seaCleanQteGetIsCleanedMethod = IntPtr.Zero;
        private IntPtr seaCleanQteGetIsQteShieldMethod = IntPtr.Zero;
        private IntPtr seaCleanQteGetIsHiddenMethod = IntPtr.Zero;
        private IntPtr seaCleanQteGetIsPlayerHostedMethod = IntPtr.Zero;
        private IntPtr seaCleanQteProtocolClass = IntPtr.Zero;
        private IntPtr seaCleanQteReqStartPublicMethod = IntPtr.Zero;

        // ---- Event wiring ----------------------------------------------------------------------

        private const string SeaCleanExecutionStateEventName = "XDTDataAndProtocol.Events.SeaCleanExecutionStateEvent";
        // SeaCleanExecutionStateEvent (namespace XDTDataAndProtocol.Events) — Mono sequential layout.
        //   state (SeaCleanExecutionState=int)        @0
        //   targetEntityInstanceId (InstanceID, 8B, contains a long => 8-aligned) @8 (4..7 = padding)
        //     .id (EntityId=uint) @8, .world (int) @12
        //   isShieldQTE (bool/byte)                   @16
        //   holdProgress (float)                      @20
        //   isLastRound (bool/byte)                   @24
        //   completedRounds (int)                     @28
        //   totalRounds (int)                         @32
        // => 40 bytes (8-aligned tail). Offsets GUESSED from CLR/Mono natural alignment; verify live
        //    with MasterLogGameEvents (the InstanceID 8-alignment is the main uncertainty).
        private const int SeaCleanExecutionStateEventBytes = 40;
        private const int SeaCleanExecStateOffset = 0;
        private const int SeaCleanExecTargetEntityIdOffset = 8;   // InstanceID.id (low uint)
        private const int SeaCleanExecTargetWorldOffset = 12;     // InstanceID.world
        private const int SeaCleanExecIsShieldOffset = 16;

        // SeaCleanExecutionState enum: Idle=0, Ready=1, Holding=2, AwaitingRelease=3, Completed=4, Failed=5.
        private const int SeaCleanExecStateReady = 1;

        private const string CleanupEventPublicPollutantSpawnedEventName = "XDTDataAndProtocol.Events.CleanupEventPublicPollutantSpawnedEvent";
        // CleanupEventPublicPollutantSpawnedEvent: netId(uint)@0, maxHp(float)@4, currentHp(float)@8,
        // phaseIndex(byte)@12 => 16 bytes (4-aligned tail).
        private const int CleanupEventPublicPollutantSpawnedEventBytes = 16;

        private void SeaCleanQteLog(string message)
        {
            if (!this.MasterLogSeaCleanQte)
            {
                return;
            }

            ModLogger.Msg("[SeaCleanQte] " + message);
        }

        // Called every frame from OnUpdate. Registers the event hooks the first time the feature is
        // enabled (and never when it stays OFF), then runs the throttled auto-clean scan loop. The
        // QTE auto-pass stays event-driven; the loop is what actually finds + kills pollutants.
        private void ProcessSeaCleanQteOnUpdate()
        {
            if (!this.seaCleanQteEnabled)
            {
                return;
            }

            this.EnsureSeaCleanQteEventHooks();
            this.TickSeaCleanAutoClean();
        }

        // Thin wrapper for the standalone Auto Sea Clean toggle: run the shared sweep pass and
        // apply its status line (empty = keep the previous one, e.g. while throttled or while
        // kills are paced) — semantics identical to the pre-refactor tick.
        private void TickSeaCleanAutoClean()
        {
            this.TrySeaCleanAutoCleanPass(out _, out _, out _, out string status);
            if (!string.IsNullOrEmpty(status))
            {
                this.seaCleanAutoLastStatus = status;
            }
        }

        // The auto-clean sweep pass, shared by the standalone tick (above) and the Aura Farm
        // contamination dwell (RunContaminationCleanWait): find the nearest exclusive pollutants
        // in range and ExecuteKill them, scan-throttled and kill-paced. Body/pins/pacing are the
        // original tick's: everything is synchronous within one call (no yields, no cross-frame
        // IntPtr holds); the component list is pinned by TryAuraMonoGetComponentObjects and each
        // derived entity object is pinned across its reads (moving-SGen rule).
        //
        // Returns true when the scan actually ran this call (throttle allowed + types resolved +
        // player position available) — the out counts are only meaningful then, and the pass
        // results are additionally mirrored into seaCleanLastPass* for the cross-caller consumer.
        // actionableInRange counts killable (exclusive) candidates within radius and is computed
        // BEFORE the equip gate, so a blocked tool still reports what is actionable.
        // standaloneStatus is the standalone tab's status line (empty = keep previous); the
        // equip gate inside is the standalone NO-YANK one — the farm dwell does its own
        // allowSwap equip before calling this.
        private bool TrySeaCleanAutoCleanPass(out int actionableInRange, out int killedThisPass, out int noLeverCount, out string standaloneStatus)
        {
            actionableInRange = 0;
            killedThisPass = 0;
            noLeverCount = 0;
            standaloneStatus = string.Empty;

            float now = Time.unscaledTime;
            if (now < this.seaCleanAutoNextScanAt)
            {
                return false;
            }

            this.seaCleanAutoNextScanAt = now + SeaCleanAutoScanInterval;

            if (!this.EnsureSeaCleanQteAuraResolved(out string resolveStatus)
                || this.seaCleanQteMonsterClass == IntPtr.Zero
                || this.seaCleanQteExecuteKillMethod == IntPtr.Zero)
            {
                standaloneStatus = "Waiting for game types (AuraMono): " + resolveStatus;
                return false;
            }

            if (!this.TryGetLocalPlayerPosition(out Vector3 playerPos))
            {
                standaloneStatus = "Player position unavailable.";
                return false;
            }

            List<uint> compPins = new List<uint>();
            try
            {
                if (!this.TryAuraMonoGetComponentObjects(this.seaCleanQteMonsterClass, out List<IntPtr> components, compPins)
                    || components == null || components.Count == 0)
                {
                    standaloneStatus = "No pollutants around — swim into a sea-clean area.";
                    this.SeaCleanRecordPassResult(now, 0, 0, 0);
                    return true;
                }

                float radius = Mathf.Clamp(this.seaCleanAutoRadius, SeaCleanAutoRadiusMin, SeaCleanAutoRadiusMax);
                float radiusSqr = radius * radius;
                this.seaCleanAutoCandidates.Clear();

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

                    // ExecuteKill during an active non-shield QTE IS the game's own success path
                    // (OnQTESucceeded → ExecuteKill), so in-QTE targets stay candidates — this keeps
                    // the loop working even if the event-hook layout guess is off. Shield QTEs are
                    // left alone (BreakShield is their legit path, not a kill).
                    if (this.SeaCleanQteInvokeBoolGetter(compObj, this.seaCleanQteGetIsInQteMethod, out bool isInQte) && isInQte
                        && this.SeaCleanQteInvokeBoolGetter(compObj, this.seaCleanQteGetIsQteShieldMethod, out bool isShieldQte) && isShieldQte)
                    {
                        continue;
                    }

                    // ExecuteKill only works on exclusive pollutants: shared/env-hosted, the public
                    // boss and player body pollution have server-owned state — no client lever.
                    bool isShared = this.SeaCleanQteInvokeBoolGetter(compObj, this.seaCleanQteGetIsSharedMethod, out bool shared) && shared;
                    bool isPublic = this.SeaCleanQteInvokeBoolGetter(compObj, this.seaCleanQteGetIsPublicMethod, out bool pub) && pub;
                    bool isPlayerHosted = this.SeaCleanQteInvokeBoolGetter(compObj, this.seaCleanQteGetIsPlayerHostedMethod, out bool hosted) && hosted;
                    if (isShared || isPublic || isPlayerHosted)
                    {
                        noLeverCount++;
                        continue;
                    }

                    if (!this.TryGetMonoObjectMember(compObj, "entity", out IntPtr entityObj) || entityObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    uint entityPin = AuraMonoPinNew(entityObj);
                    try
                    {
                        // Real network id only — netId==0 would be a local view entity and the
                        // dying-Tick self-report would be meaningless.
                        if (!this.TryGetMonoUInt32Member(entityObj, "netId", out uint netId) || netId == 0U)
                        {
                            continue;
                        }

                        if (!this.TryGetMonoVector3Member(entityObj, "position", out Vector3 pollutantPos))
                        {
                            continue;
                        }

                        float distSqr = (pollutantPos - playerPos).sqrMagnitude;
                        if (distSqr > radiusSqr)
                        {
                            continue;
                        }

                        this.seaCleanAutoCandidates.Add(new SeaCleanAutoCandidate
                        {
                            Index = i,
                            NetId = netId,
                            DistSqr = distSqr
                        });
                    }
                    finally
                    {
                        AuraMonoPinFree(entityPin);
                    }
                }

                actionableInRange = this.seaCleanAutoCandidates.Count;

                if (this.seaCleanAutoCandidates.Count == 0)
                {
                    standaloneStatus = noLeverCount > 0
                        ? "Only shared/public pollutants nearby — clean them manually, the QTE auto-passes."
                        : "No cleanable pollutant within " + radius.ToString("F0") + "m.";
                    this.SeaCleanRecordPassResult(now, 0, 0, noLeverCount);
                    return true;
                }

                // Server plausibility: the legit client can never clean without the sea cleaner in
                // hand, so hold the kill until it is equipped (auto-equips from empty hands).
                if (!this.TrySeaCleanEnsureCleanerEquipped(now, out string toolStatus))
                {
                    standaloneStatus = toolStatus;
                    this.SeaCleanRecordPassResult(now, actionableInRange, 0, noLeverCount);
                    return true;
                }

                // Kill nearest-first, as many as the delay budget allows this pass: delay 0 sweeps
                // everything in range at once; delay > 0 paces one kill per interval across passes.
                this.seaCleanAutoCandidates.Sort(SeaCleanAutoCandidateByDistance);
                float delay = this.seaCleanCleanNoDelay ? 0f : SeaCleanPacedKillDelaySeconds;
                uint lastKilledNetId = 0U;

                for (int c = 0; c < this.seaCleanAutoCandidates.Count; c++)
                {
                    if (now < this.seaCleanAutoNextKillAt)
                    {
                        break;
                    }

                    SeaCleanAutoCandidate candidate = this.seaCleanAutoCandidates[c];
                    if (!this.TrySeaCleanExecuteKill(components[candidate.Index], candidate.NetId, "auto"))
                    {
                        continue;
                    }

                    this.seaCleanAutoNextKillAt = now + delay;
                    killedThisPass++;
                    lastKilledNetId = candidate.NetId;
                    this.seaCleanAutoKillCount++;
                }

                this.SeaCleanRecordPassResult(now, actionableInRange, killedThisPass, noLeverCount);

                if (killedThisPass > 1)
                {
                    standaloneStatus = "Cleaned " + killedThisPass + " pollutants (session total: " + this.seaCleanAutoKillCount + ").";
                }
                else if (killedThisPass == 1)
                {
                    standaloneStatus = "Cleaned pollutant #" + lastKilledNetId + " (session total: " + this.seaCleanAutoKillCount + ").";
                }
                // killedThisPass == 0 while paced: keep the previous status (usually the last kill).
                return true;
            }
            finally
            {
                FreeAuraMonoPins(compPins);
            }
        }

        private void SeaCleanRecordPassResult(float now, int actionable, int killed, int noLever)
        {
            this.seaCleanLastPassCompletedAt = now;
            this.seaCleanLastPassActionable = actionable;
            this.seaCleanLastPassKilled = killed;
            this.seaCleanLastPassNoLever = noLever;
        }

        // True only when the sea cleaner (toolId 7) is the current ToolSystem tool with durability
        // left. Empty hands (GetCurrentTool()==null → the read helper fails) auto-equips it via the
        // shared TryEquipHandTool path, throttled. A DIFFERENT equipped tool is never yanked.
        private bool TrySeaCleanEnsureCleanerEquipped(float now, out string status)
        {
            if (this.TryGetCurrentToolDurability(out int toolId, out int durability, out _, out string toolStatus))
            {
                if (toolId == SeaCleanerToolTypeId)
                {
                    if (durability <= 0)
                    {
                        status = "Sea cleaner durability exhausted — repair it (Tool Restorer / workbench).";
                        return false;
                    }

                    status = "ok";
                    return true;
                }

                status = "Another tool is equipped — unequip it (or press the Equip Sea Cleaner hotkey).";
                return false;
            }

            // No current ToolSystem tool (empty hands or a non-tool handhold). GetCurrentTool()
            // returning null lands here — attempt the auto-equip, throttled.
            if (now < this.seaCleanAutoNextEquipAttemptAt)
            {
                status = "Equipping sea cleaner…";
                return false;
            }

            this.seaCleanAutoNextEquipAttemptAt = now + SeaCleanAutoEquipRetryInterval;
            if (this.TryEquipHandTool(SeaCleanerToolTypeId, out string equipStatus))
            {
                status = "Equipping sea cleaner…";
                this.SeaCleanQteLog("auto-equip sea cleaner sent: " + equipStatus + " (tool read: " + toolStatus + ")");
            }
            else
            {
                status = "Sea cleaner equip failed: " + equipStatus;
                this.SeaCleanQteLog(status);
            }

            return false;
        }

        // Farm-dwell tool gate (Aura Farm contamination nodes ONLY): unlike the standalone
        // no-yank rule above, the farm MAY swap FROM another equipped tool — it teleported to a
        // contamination marker on purpose — but never during the Auto Repair kit-use phase (the
        // game must see an idle player or the ToolRestorer use is silently dropped). Shares the
        // standalone 3s equip throttle so the two paths never double-send SetHandhold.
        // cleanerDepleted distinguishes "cleaner in hand but durability 0" (caller decides
        // hold-for-repair vs hop) from a pending/blocked equip.
        private bool TrySeaCleanFarmEnsureCleanerEquipped(float now, out bool cleanerDepleted, out string status)
        {
            cleanerDepleted = false;
            if (this.TryGetCurrentToolDurability(out int toolId, out int durability, out _, out _)
                && toolId == SeaCleanerToolTypeId)
            {
                if (durability <= 0)
                {
                    cleanerDepleted = true;
                    status = "sea cleaner depleted";
                    return false;
                }

                status = "ok";
                return true;
            }

            // Empty hands OR another tool equipped: the swap is allowed for the farm dwell,
            // except while the repair kit-use phase needs the player untouched.
            if (this.IsAutoRepairUsePhase())
            {
                status = "waiting for Auto Repair";
                return false;
            }

            if (now < this.seaCleanAutoNextEquipAttemptAt)
            {
                status = "equipping sea cleaner";
                return false;
            }

            this.seaCleanAutoNextEquipAttemptAt = now + SeaCleanAutoEquipRetryInterval;
            if (this.TryEquipHandTool(SeaCleanerToolTypeId, out string farmEquipStatus))
            {
                status = "equipping sea cleaner";
                this.SeaCleanQteLog("farm auto-equip sea cleaner sent: " + farmEquipStatus);
            }
            else
            {
                status = "sea cleaner equip failed: " + farmEquipStatus;
                this.SeaCleanQteLog(status);
            }

            return false;
        }

        private void EnsureSeaCleanQteEventHooks()
        {
            if (this.seaCleanQteEventHooksRegistered)
            {
                return;
            }

            this.seaCleanQteEventHooksRegistered = true;
            this.RegisterGameEventHook(SeaCleanExecutionStateEventName, SeaCleanExecutionStateEventBytes, this.OnSeaCleanExecutionStateEvent);
            this.RegisterGameEventHook(CleanupEventPublicPollutantSpawnedEventName, CleanupEventPublicPollutantSpawnedEventBytes, this.OnCleanupEventPublicPollutantSpawnedEvent);
            this.SeaCleanQteLog("Registered SeaCleanExecutionStateEvent + CleanupEventPublicPollutantSpawnedEvent hooks.");
        }

        // Public boss appeared / phase change — remember its netId for the public-pollutant fallback.
        private void OnCleanupEventPublicPollutantSpawnedEvent(GameEventSnapshot e)
        {
            if (!this.seaCleanQteEnabled)
            {
                return;
            }

            uint netId = e.ReadUInt32(0);
            if (netId != 0U)
            {
                this.seaCleanQteBossNetId = netId;
                this.SeaCleanQteLog("Public pollutant spawned netId=" + netId + " (boss fallback captured).");
            }
        }

        // A QTE changed state. Auto-pass when it just became active (Ready) and it is NOT a shield QTE.
        private void OnSeaCleanExecutionStateEvent(GameEventSnapshot e)
        {
            if (!this.seaCleanQteEnabled)
            {
                return;
            }

            int state = e.ReadInt32(SeaCleanExecStateOffset);
            bool isShield = e.ReadBool(SeaCleanExecIsShieldOffset);
            uint targetEntityId = e.ReadUInt32(SeaCleanExecTargetEntityIdOffset);
            int targetWorld = e.ReadInt32(SeaCleanExecTargetWorldOffset);

            this.SeaCleanQteLog("QTE event state=" + state + " shield=" + isShield
                + " targetEntityId=" + targetEntityId + " world=" + targetWorld);

            // Only act when the QTE opens (Ready). Acting here short-circuits the hold entirely:
            // ExecuteKill (exclusive) instantly kills; ReqStartCleanPublicPollutant(true) reports a
            // pass (public boss). Shield QTEs are left alone (per design).
            if (state != SeaCleanExecStateReady || isShield)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now < this.seaCleanQteNextActionAt)
            {
                return;
            }

            this.seaCleanQteNextActionAt = now + 0.2f;

            this.TryAutoPassSeaCleanQte("event");
        }

        // Resolve the pollutant currently in QTE and fire the matching client-authoritative lever.
        // Everything is synchronous within this scope (no yields / no cross-frame IntPtr holds — the
        // scan pins components for the loop and frees them in finally, per the moving-SGen rule).
        private unsafe void TryAutoPassSeaCleanQte(string source)
        {
            if (!this.EnsureSeaCleanQteAuraResolved(out string resolveStatus))
            {
                this.SeaCleanQteLog("resolve unavailable (" + source + "): " + resolveStatus);
                return;
            }

            if (this.seaCleanQteMonsterClass == IntPtr.Zero || this.seaCleanQteGetIsInQteMethod == IntPtr.Zero)
            {
                this.SeaCleanQteLog("monster class / IsInQTE getter unavailable — cannot resolve target.");
                return;
            }

            List<uint> compPins = new List<uint>();
            try
            {
                if (!this.TryAuraMonoGetComponentObjects(this.seaCleanQteMonsterClass, out List<IntPtr> components, compPins)
                    || components == null || components.Count == 0)
                {
                    this.SeaCleanQteLog("no SeaCleanMonsterComponent instances (GetComponents empty/unavailable).");
                    return;
                }

                int inQte = 0;
                for (int i = 0; i < components.Count; i++)
                {
                    IntPtr compObj = components[i];
                    if (compObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    // The definitive "this is the QTE target" signal: the component's own IsInQTE.
                    if (!this.SeaCleanQteInvokeBoolGetter(compObj, this.seaCleanQteGetIsInQteMethod, out bool isInQte) || !isInQte)
                    {
                        continue;
                    }

                    inQte++;

                    // Robust shield-skip independent of the event's isShield byte offset: never act on
                    // a component whose own QTE is a shield QTE.
                    if (this.seaCleanQteGetIsQteShieldMethod != IntPtr.Zero
                        && this.SeaCleanQteInvokeBoolGetter(compObj, this.seaCleanQteGetIsQteShieldMethod, out bool isQteShield)
                        && isQteShield)
                    {
                        this.SeaCleanQteLog("in-QTE target is a shield QTE — leaving it alone.");
                        continue;
                    }

                    // Skip already-dying/cleaned targets.
                    if (this.seaCleanQteGetIsCleanedMethod != IntPtr.Zero
                        && this.SeaCleanQteInvokeBoolGetter(compObj, this.seaCleanQteGetIsCleanedMethod, out bool isCleaned)
                        && isCleaned)
                    {
                        continue;
                    }

                    uint netId = 0U;
                    this.TryHomelandFarmTryReadAuraMonoComponentNetId(compObj, out netId);

                    bool isPublic = this.seaCleanQteGetIsPublicMethod != IntPtr.Zero
                        && this.SeaCleanQteInvokeBoolGetter(compObj, this.seaCleanQteGetIsPublicMethod, out bool pub)
                        && pub;

                    if (isPublic)
                    {
                        if (netId == 0U)
                        {
                            netId = this.seaCleanQteBossNetId; // fallback to the captured boss netId
                        }

                        if (this.TrySeaCleanReqStartPublicPass(netId, source + "/public"))
                        {
                            return;
                        }

                        continue;
                    }

                    // Exclusive (private) pollutant: only when NOT shared. Shared env-hosted
                    // pollutants have no client kill lever — leave them alone.
                    bool isShared = this.seaCleanQteGetIsSharedMethod != IntPtr.Zero
                        && this.SeaCleanQteInvokeBoolGetter(compObj, this.seaCleanQteGetIsSharedMethod, out bool shared)
                        && shared;
                    if (isShared)
                    {
                        this.SeaCleanQteLog("in-QTE target netId=" + netId + " is shared/non-public — no client lever, skipping.");
                        continue;
                    }

                    if (this.TrySeaCleanExecuteKill(compObj, netId, source + "/exclusive"))
                    {
                        return;
                    }
                }

                // No component reported IsInQTE, but a public boss is tracked and the (non-shield)
                // QTE fired: fall back to the boss pass so the public branch still works even if the
                // scan couldn't flag the component in time.
                if (inQte == 0 && this.seaCleanQteBossNetId != 0U)
                {
                    this.SeaCleanQteLog("no in-QTE component found; using tracked boss netId=" + this.seaCleanQteBossNetId + " fallback.");
                    this.TrySeaCleanReqStartPublicPass(this.seaCleanQteBossNetId, source + "/boss-fallback");
                    return;
                }

                this.SeaCleanQteLog("scanned " + components.Count + " pollutant(s), inQTE=" + inQte + " — nothing actionable.");
            }
            finally
            {
                FreeAuraMonoPins(compPins);
            }
        }

        // AuraMono instance invoke: SeaCleanMonsterComponent.ExecuteKill() — the game's own success
        // path for exclusive pollutants (MarkCleaned(qtePassed:true) → dying Tick sends
        // ReqExclusiveSyncState(...,true,true)).
        private unsafe bool TrySeaCleanExecuteKill(IntPtr compObj, uint netId, string source)
        {
            if (this.seaCleanQteExecuteKillMethod == IntPtr.Zero || compObj == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                this.SeaCleanQteLog("ExecuteKill unavailable (method/obj/invoke missing).");
                return false;
            }

            // Avoid re-killing the same target within a short window (event may re-dispatch).
            float now = Time.unscaledTime;
            if (netId != 0U && netId == this.seaCleanQteLastActedNetId && now - this.seaCleanQteLastActedAt < 2f)
            {
                return true;
            }

            IntPtr exc = IntPtr.Zero;
            auraMonoRuntimeInvoke(this.seaCleanQteExecuteKillMethod, compObj, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero)
            {
                this.SeaCleanQteLog("ExecuteKill threw exc=0x" + exc.ToInt64().ToString("X") + " (" + source + ").");
                return false;
            }

            this.seaCleanQteLastActedNetId = netId;
            this.seaCleanQteLastActedAt = now;
            this.SeaCleanQteLog("lever fired: ExecuteKill() netId=" + netId + " (" + source + ").");
            return true;
        }

        // AuraMono static invoke: SeaCleanProtocolManager.ReqStartCleanPublicPollutant(netId, qtePassed:true).
        private unsafe bool TrySeaCleanReqStartPublicPass(uint netId, string source)
        {
            if (this.seaCleanQteReqStartPublicMethod == IntPtr.Zero || netId == 0U || auraMonoRuntimeInvoke == null)
            {
                this.SeaCleanQteLog("ReqStartCleanPublicPollutant unavailable (method/netId/invoke missing, netId=" + netId + ").");
                return false;
            }

            float now = Time.unscaledTime;
            if (netId == this.seaCleanQteLastActedNetId && now - this.seaCleanQteLastActedAt < 0.5f)
            {
                return true;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[2];
            uint targetNetId = netId;
            byte qtePassed = 1; // true
            args[0] = (IntPtr)(&targetNetId);
            args[1] = (IntPtr)(&qtePassed);
            auraMonoRuntimeInvoke(this.seaCleanQteReqStartPublicMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                this.SeaCleanQteLog("ReqStartCleanPublicPollutant threw exc=0x" + exc.ToInt64().ToString("X") + " (" + source + ").");
                return false;
            }

            this.seaCleanQteLastActedNetId = netId;
            this.seaCleanQteLastActedAt = now;
            this.SeaCleanQteLog("lever fired: ReqStartCleanPublicPollutant(" + netId + ", qtePassed:true) (" + source + ").");
            return true;
        }

        private unsafe bool SeaCleanQteInvokeBoolGetter(IntPtr obj, IntPtr getter, out bool value)
        {
            value = false;
            if (obj == IntPtr.Zero || getter == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr boxed = auraMonoRuntimeInvoke(getter, obj, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || boxed == IntPtr.Zero)
            {
                return false;
            }

            return this.TryUnboxMonoBoolean(boxed, out value);
        }

        // Resolve (once, with a retry throttle) the SeaCleanMonsterComponent class + its property
        // getters / ExecuteKill, and the SeaCleanProtocolManager + ReqStartCleanPublicPollutant.
        // Fail-closed: returns false (and no-ops) until AuraMono is up and the types resolve.
        private bool EnsureSeaCleanQteAuraResolved(out string status)
        {
            status = "cached";
            bool haveMonster = this.seaCleanQteMonsterClass != IntPtr.Zero
                && this.seaCleanQteExecuteKillMethod != IntPtr.Zero
                && this.seaCleanQteGetIsInQteMethod != IntPtr.Zero;
            bool haveProtocol = this.seaCleanQteReqStartPublicMethod != IntPtr.Zero;
            if (haveMonster && haveProtocol)
            {
                return true;
            }

            float now = Time.unscaledTime;
            if (now < this.seaCleanQteNextResolveAt)
            {
                status = "resolve throttled";
                return haveMonster; // exclusive path can still run once the monster side resolves
            }

            this.seaCleanQteNextResolveAt = now + 2f;

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    status = "AuraMono API not ready";
                    return false;
                }

                if (this.seaCleanQteMonsterClass == IntPtr.Zero)
                {
                    this.seaCleanQteMonsterClass = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.Gameplay.Component.Pollution.SeaCleanMonsterComponent");
                    if (this.seaCleanQteMonsterClass == IntPtr.Zero)
                    {
                        this.seaCleanQteMonsterClass = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.GamePlay.Component.Pollution.SeaCleanMonsterComponent");
                    }
                    if (this.seaCleanQteMonsterClass == IntPtr.Zero)
                    {
                        this.seaCleanQteMonsterClass = this.FindAuraMonoClassByFullName("ScriptsRefactory.LevelAndEntity.Gameplay.Component.Pollution.SeaCleanMonsterComponent");
                    }
                    if (this.seaCleanQteMonsterClass == IntPtr.Zero)
                    {
                        this.seaCleanQteMonsterClass = this.FindAuraMonoClassAcrossLoadedAssemblies(
                            "XDTLevelAndEntity.Gameplay.Component.Pollution",
                            "SeaCleanMonsterComponent");
                    }
                }

                if (this.seaCleanQteMonsterClass != IntPtr.Zero)
                {
                    if (this.seaCleanQteExecuteKillMethod == IntPtr.Zero)
                    {
                        this.seaCleanQteExecuteKillMethod = this.FindAuraMonoMethodOnHierarchy(this.seaCleanQteMonsterClass, "ExecuteKill", 0);
                    }
                    if (this.seaCleanQteGetIsInQteMethod == IntPtr.Zero)
                    {
                        this.seaCleanQteGetIsInQteMethod = this.FindAuraMonoMethodOnHierarchy(this.seaCleanQteMonsterClass, "get_IsInQTE", 0);
                    }
                    if (this.seaCleanQteGetIsSharedMethod == IntPtr.Zero)
                    {
                        this.seaCleanQteGetIsSharedMethod = this.FindAuraMonoMethodOnHierarchy(this.seaCleanQteMonsterClass, "get_IsShared", 0);
                    }
                    if (this.seaCleanQteGetIsPublicMethod == IntPtr.Zero)
                    {
                        this.seaCleanQteGetIsPublicMethod = this.FindAuraMonoMethodOnHierarchy(this.seaCleanQteMonsterClass, "get_IsPublicPollutant", 0);
                    }
                    if (this.seaCleanQteGetIsCleanedMethod == IntPtr.Zero)
                    {
                        this.seaCleanQteGetIsCleanedMethod = this.FindAuraMonoMethodOnHierarchy(this.seaCleanQteMonsterClass, "get_IsCleaned", 0);
                    }
                    if (this.seaCleanQteGetIsQteShieldMethod == IntPtr.Zero)
                    {
                        this.seaCleanQteGetIsQteShieldMethod = this.FindAuraMonoMethodOnHierarchy(this.seaCleanQteMonsterClass, "get_IsQTEShield", 0);
                    }
                    if (this.seaCleanQteGetIsHiddenMethod == IntPtr.Zero)
                    {
                        this.seaCleanQteGetIsHiddenMethod = this.FindAuraMonoMethodOnHierarchy(this.seaCleanQteMonsterClass, "get_IsHidden", 0);
                    }
                    if (this.seaCleanQteGetIsPlayerHostedMethod == IntPtr.Zero)
                    {
                        this.seaCleanQteGetIsPlayerHostedMethod = this.FindAuraMonoMethodOnHierarchy(this.seaCleanQteMonsterClass, "get_IsPlayerHosted", 0);
                    }
                }

                if (this.seaCleanQteProtocolClass == IntPtr.Zero)
                {
                    this.seaCleanQteProtocolClass = this.FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.SeaClean.SeaCleanProtocolManager");
                    if (this.seaCleanQteProtocolClass == IntPtr.Zero)
                    {
                        this.seaCleanQteProtocolClass = this.FindAuraMonoClassAcrossLoadedAssemblies(
                            "XDTDataAndProtocol.ProtocolService.SeaClean",
                            "SeaCleanProtocolManager");
                    }
                }

                if (this.seaCleanQteProtocolClass != IntPtr.Zero && this.seaCleanQteReqStartPublicMethod == IntPtr.Zero)
                {
                    this.seaCleanQteReqStartPublicMethod = this.FindAuraMonoMethodOnHierarchy(this.seaCleanQteProtocolClass, "ReqStartCleanPublicPollutant", 2);
                }

                haveMonster = this.seaCleanQteMonsterClass != IntPtr.Zero
                    && this.seaCleanQteExecuteKillMethod != IntPtr.Zero
                    && this.seaCleanQteGetIsInQteMethod != IntPtr.Zero;
                haveProtocol = this.seaCleanQteReqStartPublicMethod != IntPtr.Zero;

                status = "monsterClass=0x" + this.seaCleanQteMonsterClass.ToInt64().ToString("X")
                    + " executeKill=0x" + this.seaCleanQteExecuteKillMethod.ToInt64().ToString("X")
                    + " isInQte=0x" + this.seaCleanQteGetIsInQteMethod.ToInt64().ToString("X")
                    + " isShared=0x" + this.seaCleanQteGetIsSharedMethod.ToInt64().ToString("X")
                    + " isPublic=0x" + this.seaCleanQteGetIsPublicMethod.ToInt64().ToString("X")
                    + " reqPublic=0x" + this.seaCleanQteReqStartPublicMethod.ToInt64().ToString("X");
                this.SeaCleanQteLog("resolve: " + status);
                return haveMonster || haveProtocol;
            }
            catch (Exception ex)
            {
                status = "resolve error: " + ex.Message;
                this.SeaCleanQteLog(status);
                return false;
            }
        }

        // ---- UI --------------------------------------------------------------------------------

        private float DrawSeaCleanQteTab(int startY)
        {
            const float left = 40f;
            float y = startY;

            Color textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB);
            GUIStyle headerStyle = new GUIStyle(GUI.skin.label) { fontSize = 14, fontStyle = FontStyle.Bold };
            headerStyle.normal.textColor = textColor;

            GUIStyle bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Normal,
                wordWrap = true
            };
            bodyStyle.normal.textColor = new Color(this.uiSubTabTextR, this.uiSubTabTextG, this.uiSubTabTextB, 0.92f);

            GUI.Label(new Rect(left, y, 460f, 24f), this.L("Auto Sea Clean"), headerStyle);
            y += 34f;

            bool prev = this.seaCleanQteEnabled;
            this.seaCleanQteEnabled = this.DrawSwitchToggle(new Rect(left, y, 360f, 30f), this.seaCleanQteEnabled, "Auto Sea Clean");
            if (this.seaCleanQteEnabled != prev)
            {
                this.AddMenuNotification(
                    $"Auto Sea Clean {(this.seaCleanQteEnabled ? "Enabled" : "Disabled")}",
                    this.seaCleanQteEnabled ? new Color(0.45f, 1f, 0.55f) : new Color(1f, 0.55f, 0.55f));
            }
            y += 40f;

            GUI.Label(new Rect(left, y, 500f, 76f),
                this.L("Fully automatic ocean cleanup: swim near pollutants — the sea cleaner is equipped automatically (only from empty hands) and solo pollutants in range are cleaned instantly, nearest first. QTEs are auto-passed too (the public boss never misses when you clean it manually). Shared/body pollution has no client lever and is skipped."),
                bodyStyle);
            y += 82f;

            GUI.Label(new Rect(left, y, 230f, 22f), this.LF("Clean radius: {0:F1}m", this.seaCleanAutoRadius), bodyStyle);
            float prevRadius = this.seaCleanAutoRadius;
            this.seaCleanAutoRadius = Mathf.Round(
                this.DrawAccentSlider(new Rect(left + 240f, y + 2f, 200f, 20f), this.seaCleanAutoRadius, SeaCleanAutoRadiusMin, SeaCleanAutoRadiusMax) * 2f) / 2f;
            if (Mathf.Abs(this.seaCleanAutoRadius - prevRadius) > 0.0001f)
            {
                try { this.SaveKeybinds(false); } catch { }
            }
            y += 28f;

            bool prevNoDelay = this.seaCleanCleanNoDelay;
            this.seaCleanCleanNoDelay = this.DrawSwitchToggle(new Rect(left, y, 360f, 30f), this.seaCleanCleanNoDelay, "Clean Without Delays");
            if (this.seaCleanCleanNoDelay != prevNoDelay)
            {
                try { this.SaveKeybinds(false); } catch { }
            }
            y += 36f;

            KeyCode hotkey = this.seaCleanQteHotkey;
            GUI.Label(new Rect(left, y, 500f, 22f),
                this.LF("Hotkey: {0} (rebind in Settings > Keybinds)", FormatKeybindLabel(hotkey)),
                bodyStyle);
            y += 26f;

            if (this.seaCleanQteEnabled)
            {
                GUI.Label(new Rect(left, y, 500f, 22f),
                    this.LF("Cleaned this session: {0}", this.seaCleanAutoKillCount),
                    bodyStyle);
                y += 24f;

                if (!string.IsNullOrEmpty(this.seaCleanAutoLastStatus))
                {
                    GUI.Label(new Rect(left, y, 500f, 40f), this.L("Status: ") + this.seaCleanAutoLastStatus, bodyStyle);
                    y += 44f;
                }
            }

            return y + 20f;
        }

        private float CalculateSeaCleanQteTabHeight()
        {
            return 390f;
        }

        // ---- Radar: "Contaminated places" (sea-clean pollutants) -------------------------------
        // Marks live SeaCleanMonsterComponent instances on the radar so pollutants are easy to find.
        // Reuses the Auto Sea Clean component resolution (class + IsCleaned/IsHidden getters) and works
        // independently of the auto-clean toggle. Called each RunRadar; fail-closed if AuraMono / the
        // type isn't ready (no-op → no markers, never crash). Same pin discipline as the underwater scan.
        private void ScanContaminatedRadar(Vector3 origin, Material line, Material fill, float maxRange)
        {
            if (!this.showContaminatedRadar || line == null || fill == null)
            {
                return;
            }

            // Resolve the monster class + getters on demand (side-effect of EnsureSeaCleanQteAuraResolved).
            this.EnsureSeaCleanQteAuraResolved(out _);
            if (this.seaCleanQteMonsterClass == IntPtr.Zero)
            {
                return;
            }

            float maxSqr = maxRange * maxRange;
            List<uint> compPins = new List<uint>();
            try
            {
                if (!this.TryAuraMonoGetComponentObjects(this.seaCleanQteMonsterClass, out List<IntPtr> components, compPins)
                    || components == null || components.Count == 0)
                {
                    return;
                }

                for (int i = 0; i < components.Count; i++)
                {
                    IntPtr comp = components[i];
                    if (comp == IntPtr.Zero)
                    {
                        continue;
                    }

                    // Skip already-cleaned / hidden pollutants — they aren't actionable contamination.
                    if (this.SeaCleanQteInvokeBoolGetter(comp, this.seaCleanQteGetIsCleanedMethod, out bool isCleaned) && isCleaned)
                    {
                        continue;
                    }
                    if (this.SeaCleanQteInvokeBoolGetter(comp, this.seaCleanQteGetIsHiddenMethod, out bool isHidden) && isHidden)
                    {
                        continue;
                    }
                    // Skip player-hosted "body pollution" — the contamination status a player picks up
                    // mid-clean rides on a player entity, so a radar marker would sit ON the player instead
                    // of a real world pollutant. The auto-clean scan excludes these too (!IsPlayerHosted).
                    if (this.SeaCleanQteInvokeBoolGetter(comp, this.seaCleanQteGetIsPlayerHostedMethod, out bool isPlayerHosted) && isPlayerHosted)
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
                        if (!this.TryGetMonoVector3Member(entityObj, "position", out Vector3 pos))
                        {
                            continue;
                        }
                        // Out-of-world bug guard: pollutants spawned below the playable sea floor are
                        // unreachable — keep them off the radar (and thus off the Aura Farm's targets).
                        if (pos.y < ContaminatedRadarMinWorldY)
                        {
                            continue;
                        }
                        if ((pos - origin).sqrMagnitude > maxSqr)
                        {
                            continue;
                        }

                        this.CreateMarker(pos, "contaminated", line, fill, null);
                    }
                    finally
                    {
                        AuraMonoPinFree(entityPin);
                    }
                }
            }
            finally
            {
                FreeAuraMonoPins(compPins);
            }
        }
    }
}
