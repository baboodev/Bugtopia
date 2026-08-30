using System;
using System.Collections.Generic;
using UnityEngine;

namespace HeartopiaMod
{
    // Combined Farming — coordinator for running Auto Fish + Auto Insect + Auto Bird together.
    // Plan: docs/plans/2026-07-27-combined-farm-coordinator.md
    //
    // ── Why a coordinator ────────────────────────────────────────────────────────────────────────
    // The server validates the tool in hand on every catch, and each farm equips its own tool on its
    // own 3.25 s retry timer. Enable two of them and the handhold thrashes: nothing ever reaches a
    // confirmed-equipped state, so nothing catches. This class makes exactly ONE farm live at a time
    // (the others are SUSPENDED — paused, not stopped), which is what turns three mutually-exclusive
    // engines into one that produces from all three sources.
    //
    // ── Which farm runs: STRICT PRIORITY, not a time slice ───────────────────────────────────────
    // The active class is simply the highest-priority enabled class that has targets in range:
    //
    //     fish  >  insects  >  birds
    //
    // so fishing runs until no fish are left in the zone, then insects run until none are left, and
    // birds get the tool only when neither of the other two has anything (or is switched off). A
    // slice has NO time limit — it ends when its class runs dry, or when a higher-priority class
    // shows up (which preempts it at the first safe point). The only clocks involved are the two
    // hysteresis windows below, and they exist purely to damp a flickering census.
    //
    // ── Activation ───────────────────────────────────────────────────────────────────────────────
    // Self-arming: it takes over as soon as TWO OR MORE of the farms are enabled, and lets go the
    // moment that drops back to one. With a single farm on, every code path here is inert and the
    // farms behave exactly as they always have.
    //
    // ── What it owns ─────────────────────────────────────────────────────────────────────────────
    //   * the handhold — via per-farm suspend + FarmToolBroker (phase 1),
    //   * the repair pause — a serial equip→repair cycle, because the restore aura only ever fixes
    //     the tool in hand, so the two stowed tools would otherwise wear down invisibly (phase 2),
    //   * movement — an anchor at the fishing spot that bounds the insect farm's teleports and gates
    //     FishingRouteFeature's hops to the Fish slice (phase 3).
    // Settings live in Resource Gathering → Combined (phase 4).
    public static class CombinedFarmFeature
    {
        private static bool DebugLoggingEnabled => HeartopiaComplete.MasterLogCombinedFarm;

        // Shared by the gated Log() and the unconditional FeatureLog Tier-1 lines.
        private const string LogTag = "CombinedFarm";

        // Tool ids as used by ToolSystem/_toolsData and GetAutoRepairSupportedToolName.
        public const int ToolIdRod = 3;
        public const int ToolIdBirdScanner = 4;
        public const int ToolIdNet = 5;
        private static readonly int[] FarmToolIds = { ToolIdRod, ToolIdBirdScanner, ToolIdNet };

        // One scan kind per step, so a census never stacks three heavy scans into one frame (the
        // bird farm already warns about ticks over 120 ms). Three steps ≈ one full census every 2.1s.
        private const float CensusStepIntervalSeconds = 0.7f;
        private const float DurabilityStepIntervalSeconds = 1.5f;
        private const float SummaryLogIntervalSeconds = 10f;
        private const float CoordinatorStepIntervalSeconds = 0.25f;
        private const float CensusFreshSeconds = 12f; // older than this = "unknown", not "empty"

        // ── Census ranges: ALWAYS the farm's own configured reach ────────────────────────────────
        // Never hard-code these. The first build did (60/50/35 m) and it broke the whole priority
        // model for anyone who tuned their ranges: with the net set to 12 m the census still counted
        // insects out to 50 m, so the insect class never read empty, the slice never ended, and birds
        // never got a turn — 263 s of "insects=12" with +0 caught, because nothing counted was
        // actually reachable. Presence must mean CATCHABLE, so it is measured with the same radius
        // the farm itself uses.
        private static float GetCensusRange(FarmSlice slice)
        {
            switch (slice)
            {
                case FarmSlice.Fish:
                    return AutoFishingFarm.GetDetectRange();
                case FarmSlice.Insect:
                    // Teleport mode changes what "reachable" means: the farm hops to the nearest
                    // loaded insect whatever the distance (its scan range then only bounds the catch
                    // it performs on arrival), so no radius gate applies. 0 = unlimited.
                    return InsectNetFarm.GetTeleportEnabled() ? 0f : InsectNetFarm.GetScanRange();
                case FarmSlice.Bird:
                    return BirdNetFarm.GetScanRange();
                default:
                    return 0f;
            }
        }

        // Presence hysteresis. There is deliberately NO time limit on a slice: a class is farmed for
        // as long as it has targets. These two windows exist only to damp a flickering census, since
        // every switch costs an equip round-trip plus, for birds, a 1.25 s scanner stabilize and a
        // runtime-state clear.
        //   EmptySlice   — how long the active class must read EMPTY before we hand over.
        //   PreemptConfirm — how long a higher-priority class must read NON-EMPTY before it takes over.
        private const float EmptySliceSecondsMin = 1f;
        private const float EmptySliceSecondsMax = 30f;
        private const float EmptySliceSecondsDefault = 5f;
        private const float PreemptConfirmSecondsMin = 0.5f;
        private const float PreemptConfirmSecondsMax = 10f;
        private const float PreemptConfirmSecondsDefault = 2f;

        private static float emptySliceSeconds = EmptySliceSecondsDefault;
        private static float preemptConfirmSeconds = PreemptConfirmSecondsDefault;
        // Escape hatch, default ON. Off = the pre-coordinator world where two enabled farms fight
        // over the handhold and neither reaches a confirmed equip — kept only so a field problem can
        // be ruled out without switching farms off.
        private static bool coordinationEnabled = true;
        // Off = the phase-2 repair cycle never runs; auto-repair then only ever sees the tool in hand
        // (i.e. the stowed ones wear down invisibly), which is the shipped behaviour without this
        // feature.
        private static bool repairStowedToolsEnabled = true;
        // A switch waits for the active farm's safe point (no live fishing session / no bird ACKs in
        // flight). Bounded, so a wedged session can never freeze the rotation.
        private const float SafePointWaitSeconds = 20f;

        private const string WorldReadyCallbackName = "CombinedFarmProbe";
        private const string BrokerOwnerName = "CombinedFarm";

        public enum FarmSlice
        {
            None = 0,
            Fish = 1,
            Insect = 2,
            Bird = 3,
        }

        // STRICT priority, highest first. The class that runs is simply the first enabled one that
        // has targets in range: fish while there are fish, then insects while there are insects,
        // birds only when neither of the other two has anything (or is switched off).
        //
        // User-orderable (all six permutations). NOTE for anything indexing off this: PriorityOf()
        // returns a position in THIS array and classTargetsSinceAt is indexed by it, so a reorder
        // must clear those clocks or a class inherits another's presence timestamp.
        private static readonly FarmSlice[] SliceOrder = { FarmSlice.Fish, FarmSlice.Insect, FarmSlice.Bird };

        private static readonly FarmSlice[][] PriorityPresets =
        {
            new[] { FarmSlice.Fish, FarmSlice.Insect, FarmSlice.Bird },
            new[] { FarmSlice.Fish, FarmSlice.Bird, FarmSlice.Insect },
            new[] { FarmSlice.Insect, FarmSlice.Fish, FarmSlice.Bird },
            new[] { FarmSlice.Insect, FarmSlice.Bird, FarmSlice.Fish },
            new[] { FarmSlice.Bird, FarmSlice.Fish, FarmSlice.Insect },
            new[] { FarmSlice.Bird, FarmSlice.Insect, FarmSlice.Fish },
        };

        private static int priorityPresetIndex;

        // ── Census ───────────────────────────────────────────────────────────────────────────────
        // A snapshot of what is farmable around the player, independent of which tool is in hand.
        // `*At` is Time.unscaledTime of the last successful refresh for that class (-999 = never).
        public struct FarmCensus
        {
            public int fishInRange;
            public Vector3 fishNearest;
            public float fishAt;

            public int insectsInRange;
            public Vector3 insectNearest;
            public float insectAt;

            public int birdsInRange;
            public Vector3 birdNearest;
            public float birdAt;
        }

        // Durability of one tool, read without equipping it.
        public struct ToolDurability
        {
            public int toolId;
            public int durability;
            public int maxDurability;
            public float readAt;      // -999 = never read successfully
            public string lastStatus;

            public float Ratio => this.maxDurability > 0 ? (float)this.durability / this.maxDurability : -1f;
            public bool IsKnown => this.readAt > 0f && this.maxDurability > 0;
        }

        private static FarmCensus census = new FarmCensus
        {
            fishAt = -999f,
            insectAt = -999f,
            birdAt = -999f,
        };

        private static readonly ToolDurability[] toolDurabilities = new ToolDurability[]
        {
            new ToolDurability { toolId = ToolIdRod, readAt = -999f, lastStatus = "not read" },
            new ToolDurability { toolId = ToolIdBirdScanner, readAt = -999f, lastStatus = "not read" },
            new ToolDurability { toolId = ToolIdNet, readAt = -999f, lastStatus = "not read" },
        };

        private static bool worldReadyRegistered;
        private static bool probeArmed;
        private static float nextCensusStepAt = -999f;
        private static int censusStepIndex;
        private static float nextDurabilityStepAt = -999f;
        private static int durabilityStepIndex;
        private static float nextSummaryLogAt = -999f;
        private static string lastSummarySignature = string.Empty;
        private static bool toolSystemProbeConfirmed;

        // ── Repair cycle (phase 2) ───────────────────────────────────────────────────────────────
        // The restore aura only ever repairs the tool IN HAND, so repairing N tools costs N ×
        // (equip → repair). That makes it a serial sub-FSM that owns the whole coordinator while it
        // runs: every farm suspended, nobody moving (the aura is a circle on the ground — walking out
        // of it cancels the repair), one tool at a time.
        private const float RepairEquipTimeoutSeconds = 10f;
        private const float RepairStartTimeoutSeconds = 6f;   // request → machine actually busy
        private const float RepairFinishTimeoutSeconds = 45f; // busy → idle (kit throw + aura)
        private const float RepairEquipRetrySeconds = 3.25f;  // same cadence the farms use
        private const float RepairSettleSeconds = 1f;         // let a fresh equip's durability read land
        private const float RepairCycleCooldownSeconds = 30f;
        private const int RepairMaxPassesPerTool = 2;

        private enum RepairStep
        {
            Idle = 0,
            Equip = 1,
            Assess = 2,
            AwaitStart = 3,
            AwaitFinish = 4,
        }

        private static bool repairCycleActive;
        private static RepairStep repairStep;
        private static readonly List<int> repairQueue = new List<int>(3);
        private static int repairQueueIndex;
        private static int repairPassesForTool;
        private static float repairStepDeadline;
        private static float repairNextEquipAttemptAt;
        private static float repairSettleUntil;
        private static float nextRepairCycleAllowedAt = -999f;
        private static int repairCycleToolsFixed;
        private static int repairCycleToolsSkipped;

        private static bool coordinating;
        private static FarmSlice activeSlice = FarmSlice.None;
        private static float sliceStartedAt = -999f;
        private static float sliceEmptySinceAt = -999f;
        private static float switchWantedSinceAt = -999f;
        private static int sliceStartCatchCount;
        private static int sliceStartConfirmedCount;
        private static float nextCoordinatorStepAt = -999f;
        // Per class (indexed like SliceOrder): when its census last went from empty to non-empty.
        // -999 = currently empty/unknown. Drives the preempt-confirm window.
        private static readonly float[] classTargetsSinceAt = { -999f, -999f, -999f };

        // ── Live status (phase 4) ────────────────────────────────────────────────────────────────
        // Plain strings for the HUD status list and the settings tab. They intentionally mirror what
        // the debug log prints, so a user-reported screenshot and a log line describe the same state.
        public static string GetLiveSummary()
        {
            if (!coordinating)
            {
                return coordinationEnabled ? "Idle" : "Off";
            }

            if (repairCycleActive)
            {
                string tool = repairQueueIndex < repairQueue.Count ? GetToolName(repairQueue[repairQueueIndex]) : "-";
                return "Repairing " + tool + " (" + (repairQueueIndex + 1) + "/" + repairQueue.Count + ")";
            }

            return SliceLabel(activeSlice) + " — " + GetSliceTargetCount(activeSlice) + " in range";
        }

        public static string GetLiveTargets()
        {
            return "Fish " + census.fishInRange + DescribeRange(FarmSlice.Fish)
                + " · Insects " + census.insectsInRange + DescribeRange(FarmSlice.Insect)
                + " · Birds " + census.birdsInRange + DescribeRange(FarmSlice.Bird);
        }

        public static string GetLiveToolDurabilities()
        {
            return DescribeDurability(0) + " " + DescribeDurability(1) + " " + DescribeDurability(2);
        }

        // ── Settings (phase 4) ───────────────────────────────────────────────────────────────────
        public static bool GetCoordinationEnabled() => coordinationEnabled;
        public static void SetCoordinationEnabled(bool value) => coordinationEnabled = value;

        public static bool GetRepairStowedToolsEnabled() => repairStowedToolsEnabled;
        public static void SetRepairStowedToolsEnabled(bool value) => repairStowedToolsEnabled = value;

        public static float GetEmptySliceSeconds() => emptySliceSeconds;
        public static void SetEmptySliceSeconds(float value) =>
            emptySliceSeconds = Mathf.Clamp(value, EmptySliceSecondsMin, EmptySliceSecondsMax);
        public static float GetEmptySliceSecondsMin() => EmptySliceSecondsMin;
        public static float GetEmptySliceSecondsMax() => EmptySliceSecondsMax;

        public static float GetPreemptConfirmSeconds() => preemptConfirmSeconds;
        public static void SetPreemptConfirmSeconds(float value) =>
            preemptConfirmSeconds = Mathf.Clamp(value, PreemptConfirmSecondsMin, PreemptConfirmSecondsMax);
        public static float GetPreemptConfirmSecondsMin() => PreemptConfirmSecondsMin;
        public static float GetPreemptConfirmSecondsMax() => PreemptConfirmSecondsMax;

        public static int GetPriorityPresetIndex() => priorityPresetIndex;

        public static string[] GetPriorityPresetLabels()
        {
            string[] labels = new string[PriorityPresets.Length];
            for (int i = 0; i < PriorityPresets.Length; i++)
            {
                labels[i] = SliceLabel(PriorityPresets[i][0]) + " > " + SliceLabel(PriorityPresets[i][1])
                    + " > " + SliceLabel(PriorityPresets[i][2]);
            }

            return labels;
        }

        private static string SliceLabel(FarmSlice slice)
        {
            switch (slice)
            {
                case FarmSlice.Fish: return "Fish";
                case FarmSlice.Insect: return "Insects";
                case FarmSlice.Bird: return "Birds";
                default: return "-";
            }
        }

        public static void SetPriorityPresetIndex(int value)
        {
            int clamped = Mathf.Clamp(value, 0, PriorityPresets.Length - 1);
            if (clamped == priorityPresetIndex)
            {
                return;
            }

            priorityPresetIndex = clamped;
            FarmSlice[] preset = PriorityPresets[clamped];
            for (int i = 0; i < SliceOrder.Length; i++)
            {
                SliceOrder[i] = preset[i];
                // The presence clocks are indexed by PRIORITY POSITION, so leaving them would hand a
                // class the timestamp of whoever used to sit at that index.
                classTargetsSinceAt[i] = -999f;
            }

            sliceEmptySinceAt = -999f;
            switchWantedSinceAt = -999f;
            Log("Priority order set to " + GetPriorityPresetLabels()[clamped] + ".");
        }

        // Config round-trip. The order is stored by NAME, not by preset index, so reordering or
        // extending the preset list later cannot silently repoint a saved setting.
        public static string GetPriorityOrderKey()
        {
            FarmSlice[] preset = PriorityPresets[Mathf.Clamp(priorityPresetIndex, 0, PriorityPresets.Length - 1)];
            return SliceKey(preset[0]) + "," + SliceKey(preset[1]) + "," + SliceKey(preset[2]);
        }

        public static void SetPriorityOrderKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            for (int i = 0; i < PriorityPresets.Length; i++)
            {
                FarmSlice[] preset = PriorityPresets[i];
                string candidate = SliceKey(preset[0]) + "," + SliceKey(preset[1]) + "," + SliceKey(preset[2]);
                if (string.Equals(candidate, key.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    SetPriorityPresetIndex(i);
                    return;
                }
            }
        }

        private static string SliceKey(FarmSlice slice)
        {
            switch (slice)
            {
                case FarmSlice.Fish: return "fish";
                case FarmSlice.Insect: return "insect";
                case FarmSlice.Bird: return "bird";
                default: return "none";
            }
        }

        // ── Movement arbitration (phase 3) ───────────────────────────────────────────────────────
        // Two of the farms move the player: the insect farm teleports (to its target, and along a
        // 55-point patrol when the area is empty) and FishingRouteFeature hops between fishing spots.
        // Run them together and they undo each other — the insect patrol walks away from the water
        // the route just picked. Suspension already stops the inactive farm from moving; what is left
        // is bounding what the ACTIVE one may do.
        //
        // The rule is an ANCHOR, not a per-farm special case: while fishing is one of the enabled
        // farms, the anchor is the fishing spot (refreshed continuously while the Fish slice runs, so
        // it follows the player's own repositioning), and any move must land within its radius. That
        // lets the insect slice hop to nearby targets but refuses the long patrol jumps that would
        // abandon the spot. With no fishing farm enabled there is nothing to protect and roaming is
        // unrestricted — the pre-coordinator behaviour.
        private const float MoveAnchorMinRadius = 20f;
        private static Vector3 moveAnchor;
        private static bool hasMoveAnchor;

        // FishingRouteFeature: hop only while the Fish slice actually owns the tool.
        public static bool AllowsRouteHop =>
            !coordinating || (!repairCycleActive && activeSlice == FarmSlice.Fish);

        private static float GetMoveAnchorRadius()
        {
            // The reach of the thing being protected: move farther than the rod's detect range and
            // the spot is lost anyway. Floored so a tiny range does not pin the player in place.
            return Mathf.Max(AutoFishingFarm.GetDetectRange(), MoveAnchorMinRadius);
        }

        public static bool AllowsMoveTo(Vector3 destination, out string reason)
        {
            reason = string.Empty;
            if (!coordinating)
            {
                return true;
            }

            if (repairCycleActive)
            {
                reason = "repair cycle";
                return false;
            }

            // The restore aura is a circle on the ground — stepping out of it wastes the kit.
            // (InsectNetFarm checks this itself too; keeping it here makes the rule one-sided-safe
            // for any future mover.)
            if (!hasMoveAnchor || !AutoFishingFarm.IsEnabled)
            {
                return true;
            }

            float radius = GetMoveAnchorRadius();
            float distance = new Vector2(destination.x - moveAnchor.x, destination.z - moveAnchor.z).magnitude;
            if (distance <= radius)
            {
                return true;
            }

            reason = "would leave the fishing spot (" + distance.ToString("F0") + "m > " + radius.ToString("F0") + "m)";
            return false;
        }

        // Called every coordinator step. While the Fish slice runs, the player IS at the spot worth
        // protecting, so the anchor tracks them; otherwise it stays where fishing last happened.
        private static void UpdateMoveAnchor(HeartopiaComplete host)
        {
            if (!AutoFishingFarm.IsEnabled)
            {
                hasMoveAnchor = false;
                return;
            }

            if (hasMoveAnchor && activeSlice != FarmSlice.Fish)
            {
                return;
            }

            if (host.TryGetLocalPlayerPosition(out Vector3 playerPos))
            {
                moveAnchor = playerPos;
                hasMoveAnchor = true;
            }
        }

        // ── Read-only accessors (phase 2 consumes these) ──────────────────────────────────────────
        public static bool IsCoordinating => coordinating;
        public static FarmSlice ActiveSlice => activeSlice;

        public static bool TryGetToolDurability(int toolId, out int durability, out int maxDurability, out float ratio)
        {
            durability = 0;
            maxDurability = 0;
            ratio = -1f;

            int index = IndexOfTool(toolId);
            if (index < 0 || !toolDurabilities[index].IsKnown)
            {
                return false;
            }

            durability = toolDurabilities[index].durability;
            maxDurability = toolDurabilities[index].maxDurability;
            ratio = toolDurabilities[index].Ratio;
            return true;
        }

        private static int IndexOfTool(int toolId)
        {
            for (int i = 0; i < FarmToolIds.Length; i++)
            {
                if (FarmToolIds[i] == toolId)
                {
                    return i;
                }
            }

            return -1;
        }

        public static string GetToolName(int toolId)
        {
            switch (toolId)
            {
                case ToolIdRod: return "Rod";
                case ToolIdBirdScanner: return "BirdScanner";
                case ToolIdNet: return "Net";
                default: return "Tool" + toolId;
            }
        }

        // ── Tick ─────────────────────────────────────────────────────────────────────────────────
        // Called from OnUpdate behind combinedFarmBreaker, after the three farm ticks.
        public static void Update(HeartopiaComplete host)
        {
            if (host == null)
            {
                return;
            }

            bool wantCoordination = coordinationEnabled && CountEnabledFarms() >= 2;
            bool wantProbe = DebugLoggingEnabled;

            if (!wantCoordination && !wantProbe)
            {
                if (coordinating)
                {
                    StopCoordinating(host, "fewer than two farms enabled");
                }
                else
                {
                    // A farm disabled while suspended would stay paused forever and look broken the
                    // next time it is switched on. Cheap: SetSuspended returns on an unchanged value.
                    ReleaseSuspensionOfDisabledFarms(host);
                }

                if (probeArmed)
                {
                    ResetProbeState();
                }
                return;
            }

            // Registration is a one-shot bool-guarded subscribe (the GameLodFeature idiom), NOT a
            // retry poll: the gate owns retries. Every Mono resolve this class needs happens inside
            // the callback, so nothing resolves before a world exists (AGENTS.md world-ready rule).
            if (!worldReadyRegistered)
            {
                worldReadyRegistered = true;
                host.RegisterWorldReadyCallback(WorldReadyCallbackName, () => TryPrimeToolSystemProbe(host));
            }

            if (!host.IsWorldReady)
            {
                return;
            }

            float now = Time.unscaledTime;

            if (now >= nextDurabilityStepAt)
            {
                nextDurabilityStepAt = now + DurabilityStepIntervalSeconds;
                StepDurabilityProbe(host, now);
            }

            if (now >= nextCensusStepAt)
            {
                nextCensusStepAt = now + CensusStepIntervalSeconds;
                StepCensus(host, now);
            }

            if (now >= nextCoordinatorStepAt)
            {
                nextCoordinatorStepAt = now + CoordinatorStepIntervalSeconds;
                StepCoordinator(host, now, wantCoordination);
            }

            if (coordinating)
            {
                FarmToolBroker.Tick(host);
            }

            LogSummaryIfDue(host, now);
        }

        // ── Slice arbitration ────────────────────────────────────────────────────────────────────
        private static void StepCoordinator(HeartopiaComplete host, float now, bool wantCoordination)
        {
            if (!wantCoordination)
            {
                if (coordinating)
                {
                    StopCoordinating(host, "fewer than two farms enabled");
                }
                else
                {
                    ReleaseSuspensionOfDisabledFarms(host);
                }
                return;
            }

            if (!coordinating)
            {
                StartCoordinating(host, now);
                return;
            }

            // The repair cycle owns the coordinator while it runs — no slice changes, no movement.
            if (repairCycleActive)
            {
                StepRepairCycle(host, now);
                return;
            }

            UpdatePresenceClocks(now);
            UpdateMoveAnchor(host);

            if (ShouldStartRepairCycle(host, now))
            {
                StartRepairCycle(host, now);
                return;
            }

            // The user (or the bird farm's own 5-rejection stop) can switch a farm off underneath us.
            if (!IsSliceEnabled(activeSlice))
            {
                FarmSlice replacement = PickDesiredSlice(now);
                if (replacement == FarmSlice.None)
                {
                    // Nothing with targets — take anything still enabled rather than re-entering this
                    // branch (and re-logging) every step with a dead slice.
                    replacement = PickFirstEnabledSlice();
                }

                if (replacement == FarmSlice.None)
                {
                    StopCoordinating(host, "no farm left to run");
                    return;
                }

                Log("Slice " + activeSlice + " lost its farm — switching.");
                SwitchSlice(host, now, replacement, "farm disabled");
                return;
            }

            EnforceSuspension(host);

            FarmSlice desired = PickDesiredSlice(now);
            if (desired == FarmSlice.None || desired == activeSlice)
            {
                // Either we are already on the right class, or nothing anywhere has targets — in
                // which case stay put and let the farm's own idle behaviour work (auto-bait, insect
                // patrol). Switching tools to another empty class would only cost equips.
                switchWantedSinceAt = -999f;
                return;
            }

            bool preempt = PriorityOf(desired) < PriorityOf(activeSlice);
            if (preempt)
            {
                // A higher-priority class showed up. Require it to hold its targets for the confirm
                // window so one flickering census reading cannot yank the tool out of a working slice.
                if (!HasHadTargetsFor(desired, now, preemptConfirmSeconds))
                {
                    switchWantedSinceAt = -999f;
                    return;
                }
            }
            else
            {
                // Only a LOWER-priority class wants in, which by construction means the active class
                // has nothing left. Hand over once it has read empty for the full window — this is
                // the "fish until there are no fish in range" rule.
                bool ranDry = sliceEmptySinceAt > 0f && now - sliceEmptySinceAt >= GetEmptyWindowSeconds(activeSlice);
                if (!ranDry)
                {
                    switchWantedSinceAt = -999f;
                    return;
                }
            }

            string reason = preempt ? "higher priority" : "ran dry";
            if (!IsSliceAtSafePoint(activeSlice))
            {
                if (switchWantedSinceAt < 0f)
                {
                    switchWantedSinceAt = now;
                }

                if (now - switchWantedSinceAt < SafePointWaitSeconds)
                {
                    return;
                }

                SwitchSlice(host, now, desired, reason + " (forced past safe point)");
                return;
            }

            SwitchSlice(host, now, desired, reason);
        }

        // ── Repair cycle ─────────────────────────────────────────────────────────────────────────
        // Entry: any tool belonging to an ENABLED farm reads at/below the auto-repair threshold. The
        // durability comes from the census, i.e. read WITHOUT equipping — that is the whole point of
        // TryGetToolDurabilityById: today's auto-repair only ever sees the tool in hand, so in a
        // rotation the two stowed tools wear down invisibly until their protocols start failing.
        private static bool ShouldStartRepairCycle(HeartopiaComplete host, float now)
        {
            if (!repairStowedToolsEnabled || now < nextRepairCycleAllowedAt || !host.GetAutoRepairOnDurabilityEnabled())
            {
                return false;
            }

            // Repairing means holding each tool in turn, so the active farm must be at a point where
            // taking its tool away costs nothing.
            if (!IsSliceAtSafePoint(activeSlice))
            {
                return false;
            }

            return CollectToolsNeedingRepair(host, null);
        }

        // Fills `into` (when given) with the tools worth a pass, and returns whether there are any.
        // Order: the ACTIVE slice's tool goes LAST, so the cycle ends already holding the tool the
        // farm about to resume needs — one equip round-trip saved, and no "wrong tool" first tick.
        private static bool CollectToolsNeedingRepair(HeartopiaComplete host, List<int> into)
        {
            into?.Clear();
            int threshold = host.GetAutoRepairTriggerPercent();
            int activeTool = GetSliceToolId(activeSlice);
            bool any = false;

            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 0; i < SliceOrder.Length; i++)
                {
                    FarmSlice slice = SliceOrder[i];
                    if (!IsSliceEnabled(slice))
                    {
                        continue;
                    }

                    int toolId = GetSliceToolId(slice);
                    bool isActiveTool = toolId == activeTool;
                    // pass 0 = everything except the active slice's tool, pass 1 = only it.
                    if ((pass == 0) == isActiveTool)
                    {
                        continue;
                    }

                    if (!TryGetToolDurability(toolId, out int durability, out int maxDurability, out float ratio)
                        || ratio < 0f)
                    {
                        continue;
                    }

                    if (ratio * 100f > threshold)
                    {
                        continue;
                    }

                    any = true;
                    into?.Add(toolId);
                }
            }

            return any;
        }

        private static void StartRepairCycle(HeartopiaComplete host, float now)
        {
            CollectToolsNeedingRepair(host, repairQueue);
            if (repairQueue.Count == 0)
            {
                nextRepairCycleAllowedAt = now + RepairCycleCooldownSeconds;
                return;
            }

            repairCycleActive = true;
            repairQueueIndex = 0;
            repairPassesForTool = 0;
            repairCycleToolsFixed = 0;
            repairCycleToolsSkipped = 0;
            repairStep = RepairStep.Equip;
            repairNextEquipAttemptAt = -999f;
            repairStepDeadline = now + RepairEquipTimeoutSeconds;

            // Everything stops: a farm ticking here would fight for the handhold, and a teleport
            // would walk the player out of the restore aura.
            SuspendAll(host);

            string queue = string.Empty;
            for (int i = 0; i < repairQueue.Count; i++)
            {
                queue += (queue.Length > 0 ? " → " : string.Empty) + GetToolName(repairQueue[i]);
            }

            Log("Repair cycle: " + repairQueue.Count + " tool(s) below "
                + host.GetAutoRepairTriggerPercent() + "% — " + queue + " (farms paused, staying put).");
        }

        private static void StepRepairCycle(HeartopiaComplete host, float now)
        {
            if (repairQueueIndex >= repairQueue.Count)
            {
                EndRepairCycle(host, now, "done");
                return;
            }

            int toolId = repairQueue[repairQueueIndex];

            switch (repairStep)
            {
                case RepairStep.Equip:
                {
                    // The farms are suspended, so the coordinator is the one writing the handhold
                    // here — the single-writer rule still holds.
                    bool equipped = host.TryGetCurrentToolInfo(out int heldToolId, out _, out _) && heldToolId == toolId;
                    if (equipped)
                    {
                        repairStep = RepairStep.Assess;
                        repairSettleUntil = now + RepairSettleSeconds;
                        repairStepDeadline = now + RepairFinishTimeoutSeconds;
                        return;
                    }

                    if (now >= repairStepDeadline)
                    {
                        Log("Repair cycle: " + GetToolName(toolId) + " would not equip in time — skipped.");
                        AdvanceRepairTool(now, fixedIt: false);
                        return;
                    }

                    if (now >= repairNextEquipAttemptAt)
                    {
                        repairNextEquipAttemptAt = now + RepairEquipRetrySeconds;
                        host.EquipHandTool(toolId);
                    }
                    return;
                }

                case RepairStep.Assess:
                {
                    // Give the freshly equipped tool a moment: the durability read goes through the
                    // same ToolSystem the equip just updated.
                    if (now < repairSettleUntil)
                    {
                        return;
                    }

                    int threshold = host.GetAutoRepairTriggerPercent();
                    // FRESH read, not the census cache: that cache refreshes one tool per 1.5 s step,
                    // i.e. up to ~4.5 s stale per tool — long enough to hand back the pre-repair
                    // value right after a kit landed and burn a second pass on an already-full tool.
                    if (TryReadToolDurabilityNow(host, toolId, out float ratio)
                        && ratio * 100f > threshold)
                    {
                        Log("Repair cycle: " + GetToolName(toolId) + " now " + Mathf.RoundToInt(ratio * 100f)
                            + "% — no pass needed.");
                        AdvanceRepairTool(now, fixedIt: repairPassesForTool > 0);
                        return;
                    }

                    if (repairPassesForTool >= RepairMaxPassesPerTool)
                    {
                        Log("Repair cycle: " + GetToolName(toolId) + " still low after "
                            + repairPassesForTool + " pass(es) — moving on (out of kits?).");
                        AdvanceRepairTool(now, fixedIt: false);
                        return;
                    }

                    // A live aura from the previous tool repairs whatever is in hand — wait it out
                    // before spending another kit on this one.
                    if (host.IsAutoRepairBusy())
                    {
                        repairStep = RepairStep.AwaitFinish;
                        repairStepDeadline = now + RepairFinishTimeoutSeconds;
                        return;
                    }

                    repairPassesForTool++;
                    host.RequestDurabilityCheck();
                    repairStep = RepairStep.AwaitStart;
                    repairNextEquipAttemptAt = now + 1.5f; // reused as the re-request clock
                    repairStepDeadline = now + RepairStartTimeoutSeconds;
                    return;
                }

                case RepairStep.AwaitStart:
                {
                    if (host.IsAutoRepairBusy())
                    {
                        repairStep = RepairStep.AwaitFinish;
                        repairStepDeadline = now + RepairFinishTimeoutSeconds;
                        return;
                    }

                    // RequestDurabilityCheck is throttled to ≤1/s host-side, so a single request can
                    // be swallowed outright (e.g. a farm asked for one a moment earlier). Keep asking
                    // until the machine actually goes busy or the window closes.
                    if (now >= repairNextEquipAttemptAt)
                    {
                        repairNextEquipAttemptAt = now + 1.5f;
                        host.RequestDurabilityCheck();
                    }

                    if (now >= repairStepDeadline)
                    {
                        // The trigger never fired: no kit in the bag, a cooldown, or the durability
                        // latch. Nothing to wait for.
                        Log("Repair cycle: " + GetToolName(toolId) + " repair did not start (no kit / cooldown).");
                        AdvanceRepairTool(now, fixedIt: false);
                    }
                    return;
                }

                case RepairStep.AwaitFinish:
                {
                    if (!host.IsAutoRepairBusy())
                    {
                        // Back to Assess: it re-reads durability and decides whether another pass is
                        // warranted or the tool is done.
                        repairStep = RepairStep.Assess;
                        repairSettleUntil = now + RepairSettleSeconds;
                        return;
                    }

                    if (now >= repairStepDeadline)
                    {
                        Log("Repair cycle: " + GetToolName(toolId) + " repair did not finish in "
                            + RepairFinishTimeoutSeconds.ToString("F0") + "s — moving on.");
                        AdvanceRepairTool(now, fixedIt: false);
                    }
                    return;
                }

                default:
                    EndRepairCycle(host, now, "unknown step");
                    return;
            }
        }

        // Direct read + cache refresh, so the summary line reports what the cycle actually acted on.
        private static bool TryReadToolDurabilityNow(HeartopiaComplete host, int toolId, out float ratio)
        {
            ratio = -1f;
            if (!host.TryGetToolDurabilityById(toolId, out int durability, out int maxDurability, out string status)
                || maxDurability <= 0)
            {
                return false;
            }

            ratio = (float)durability / maxDurability;

            int index = IndexOfTool(toolId);
            if (index >= 0)
            {
                toolDurabilities[index].durability = durability;
                toolDurabilities[index].maxDurability = maxDurability;
                toolDurabilities[index].readAt = Time.unscaledTime;
                toolDurabilities[index].lastStatus = status;
            }

            return true;
        }

        private static void AdvanceRepairTool(float now, bool fixedIt)
        {
            if (fixedIt)
            {
                repairCycleToolsFixed++;
            }
            else
            {
                repairCycleToolsSkipped++;
            }

            repairQueueIndex++;
            repairPassesForTool = 0;
            repairStep = RepairStep.Equip;
            repairNextEquipAttemptAt = -999f;
            repairStepDeadline = now + RepairEquipTimeoutSeconds;
        }

        private static void EndRepairCycle(HeartopiaComplete host, float now, string reason)
        {
            repairCycleActive = false;
            repairStep = RepairStep.Idle;
            repairQueue.Clear();
            repairQueueIndex = 0;
            repairPassesForTool = 0;
            nextRepairCycleAllowedAt = now + RepairCycleCooldownSeconds;

            // Resume the slice that was running: activeSlice was never changed, so this puts exactly
            // one farm back to work — and its tool is already in hand if the queue was ordered right.
            EnforceSuspension(host);
            sliceStartedAt = now;
            sliceEmptySinceAt = -999f;
            switchWantedSinceAt = -999f;
            sliceStartCatchCount = GetSliceCatchCount(activeSlice);
            sliceStartConfirmedCount = InsectNetFarm.GetSessionConfirmedCatchCount();

            Log("Repair cycle finished (" + reason + "): " + repairCycleToolsFixed + " repaired, "
                + repairCycleToolsSkipped + " skipped. Resuming " + activeSlice + ".");
        }

        // Per-class presence clocks + the active slice's empty clock, both refreshed every step.
        // Freshness is handled asymmetrically on purpose: a stale census is "unknown", and unknown
        // must never START a switch in either direction — it neither proves a class ran dry nor
        // proves a higher-priority class has something worth taking the tool for.
        private static void UpdatePresenceClocks(float now)
        {
            for (int i = 0; i < SliceOrder.Length; i++)
            {
                if (HasFreshTargets(SliceOrder[i], now))
                {
                    if (classTargetsSinceAt[i] < 0f)
                    {
                        classTargetsSinceAt[i] = now;
                    }
                }
                else
                {
                    classTargetsSinceAt[i] = -999f;
                }
            }

            if (IsKnownEmpty(activeSlice, now))
            {
                if (sliceEmptySinceAt < 0f)
                {
                    sliceEmptySinceAt = now;
                }
            }
            else
            {
                sliceEmptySinceAt = -999f;
            }
        }

        private static void StartCoordinating(HeartopiaComplete host, float now)
        {
            coordinating = true;
            FarmToolBroker.Acquire(BrokerOwnerName);
            hasMoveAnchor = false;
            activeSlice = FarmSlice.None;
            sliceStartedAt = now;
            sliceEmptySinceAt = -999f;
            switchWantedSinceAt = -999f;

            // Everything pauses first, so no two farms can be asking for a tool while the first slice
            // is chosen.
            SuspendAll(host);
            for (int i = 0; i < classTargetsSinceAt.Length; i++)
            {
                classTargetsSinceAt[i] = -999f;
            }

            FarmSlice first = PickDesiredSlice(now);
            if (first == FarmSlice.None)
            {
                first = PickFirstEnabledSlice();
            }

            // TIER 1 — unconditional. This coordinator is what actually drove the fish/insect/bird
            // sub-farms, and with MasterLogCombinedFarm off (the shipped default) its whole run was
            // invisible: the sub-farms had to be inferred from lazily-installed event hooks.
            FeatureLog.Toggle(LogTag, true, "coordinating " + CountEnabledFarms()
                + " farm(s) by priority (" + DescribePriorityOrder() + "), first slice=" + first);
            Log("Coordinating " + CountEnabledFarms() + " farms by priority ("
                + DescribePriorityOrder() + ") — a class runs until it has no targets left.");
            SwitchSlice(host, now, first, "start");
        }

        private static void StopCoordinating(HeartopiaComplete host, string reason)
        {
            if (!coordinating)
            {
                return;
            }

            LogSliceEnd(Time.unscaledTime, "stop: " + reason);
            // A repair cycle in flight must not survive the stop: it holds every farm suspended and
            // drives the handhold itself.
            repairCycleActive = false;
            repairStep = RepairStep.Idle;
            repairQueue.Clear();
            repairQueueIndex = 0;
            repairPassesForTool = 0;
            coordinating = false;
            hasMoveAnchor = false;
            activeSlice = FarmSlice.None;
            sliceStartedAt = -999f;
            sliceEmptySinceAt = -999f;
            switchWantedSinceAt = -999f;

            // Order matters: hand every farm back to itself first, THEN release the broker, so the
            // farms' own capture/restore stays disabled until the player's tool has been put back.
            ResumeAll(host);
            FarmToolBroker.Release(host, restoreTool: CountEnabledFarms() == 0);
            FeatureLog.Toggle(LogTag, false, reason);
            Log("Stopped coordinating (" + reason + ").");
        }

        private static void SwitchSlice(HeartopiaComplete host, float now, FarmSlice next, string reason)
        {
            if (next == FarmSlice.None)
            {
                return;
            }

            if (next != activeSlice)
            {
                LogSliceEnd(now, reason);
            }

            activeSlice = next;
            sliceStartedAt = now;
            sliceEmptySinceAt = -999f;
            switchWantedSinceAt = -999f;
            sliceStartCatchCount = GetSliceCatchCount(next);
            sliceStartConfirmedCount = InsectNetFarm.GetSessionConfirmedCatchCount();

            EnforceSuspension(host);
            // TIER 1, once per slice class per session. Deliberately NOT Life(): a slice can rotate
            // every few seconds (combinedFarmEmptySliceSeconds), which would put hundreds of lines
            // an hour into the log — the exact per-tick flood the split exists to avoid. One line
            // proving each class actually ran is what the record needs; the per-switch line stays
            // in Tier 2 immediately below.
            FeatureLog.Once(LogTag, "slice:" + next, "first " + next + " slice this session (" + reason
                + "), targets=" + GetSliceTargetCount(next));
            Log("Slice → " + next + " (" + reason + "), targets=" + GetSliceTargetCount(next)
                + ", tool=" + GetToolName(GetSliceToolId(next)));
        }

        // The invariant this class exists to hold: exactly one farm unsuspended, and no disabled farm
        // left paused. Idempotent — SetSuspended early-returns on an unchanged value.
        private static void EnforceSuspension(HeartopiaComplete host)
        {
            AutoFishingFarm.SetSuspended(AutoFishingFarm.IsEnabled && activeSlice != FarmSlice.Fish, host);
            InsectNetFarm.SetSuspended(InsectNetFarm.IsEnabled && activeSlice != FarmSlice.Insect);
            BirdNetFarm.SetSuspended(BirdNetFarm.IsEnabled && activeSlice != FarmSlice.Bird, host);
        }

        private static void SuspendAll(HeartopiaComplete host)
        {
            AutoFishingFarm.SetSuspended(AutoFishingFarm.IsEnabled, host);
            InsectNetFarm.SetSuspended(InsectNetFarm.IsEnabled);
            BirdNetFarm.SetSuspended(BirdNetFarm.IsEnabled, host);
        }

        private static void ResumeAll(HeartopiaComplete host)
        {
            AutoFishingFarm.SetSuspended(false, host);
            InsectNetFarm.SetSuspended(false);
            BirdNetFarm.SetSuspended(false, host);
        }

        private static void ReleaseSuspensionOfDisabledFarms(HeartopiaComplete host)
        {
            if (!AutoFishingFarm.IsEnabled && AutoFishingFarm.IsSuspended)
            {
                AutoFishingFarm.SetSuspended(false, host);
            }

            if (!InsectNetFarm.IsEnabled && InsectNetFarm.IsSuspended)
            {
                InsectNetFarm.SetSuspended(false);
            }

            if (!BirdNetFarm.IsEnabled && BirdNetFarm.IsSuspended)
            {
                BirdNetFarm.SetSuspended(false, host);
            }
        }

        // The class that SHOULD be running right now: highest priority first, first one that is
        // enabled and has targets in range. None = nothing anywhere has targets.
        private static FarmSlice PickDesiredSlice(float now)
        {
            for (int i = 0; i < SliceOrder.Length; i++)
            {
                FarmSlice candidate = SliceOrder[i];
                if (IsSliceEnabled(candidate) && HasFreshTargets(candidate, now))
                {
                    return candidate;
                }
            }

            return FarmSlice.None;
        }

        private static FarmSlice PickFirstEnabledSlice()
        {
            for (int i = 0; i < SliceOrder.Length; i++)
            {
                if (IsSliceEnabled(SliceOrder[i]))
                {
                    return SliceOrder[i];
                }
            }

            return FarmSlice.None;
        }

        // Lower number = higher priority (index in SliceOrder).
        private static int PriorityOf(FarmSlice slice)
        {
            for (int i = 0; i < SliceOrder.Length; i++)
            {
                if (SliceOrder[i] == slice)
                {
                    return i;
                }
            }

            return int.MaxValue;
        }

        private static bool HasHadTargetsFor(FarmSlice slice, float now, float seconds)
        {
            int index = PriorityOf(slice);
            if (index >= SliceOrder.Length)
            {
                return false;
            }

            float since = classTargetsSinceAt[index];
            return since > 0f && now - since >= seconds;
        }

        // How long the active class must read empty before it hands the tool over.
        //
        // Fishing gets a longer window while Auto Bait still has charges: that feature exists to
        // REFILL an empty radius (it throws after its own no-fish delay, and the server spawns fish
        // 1-3 s later), so leaving on the default 5 s would mean the setting could never fire once
        // combined farming is on. A class that can summon its own targets is not dry yet.
        private static float GetEmptyWindowSeconds(FarmSlice slice)
        {
            if (slice == FarmSlice.Fish
                && AutoFishingFarm.GetAutoBaitEnabled()
                && AutoFishingFarm.HasAutoBaitBudget)
            {
                return Mathf.Max(emptySliceSeconds, AutoFishingFarm.GetAutoBaitNoFishSeconds() + 3f);
            }

            return emptySliceSeconds;
        }

        private static string DescribePriorityOrder()
        {
            string order = string.Empty;
            for (int i = 0; i < SliceOrder.Length; i++)
            {
                if (!IsSliceEnabled(SliceOrder[i]))
                {
                    continue;
                }

                order += (order.Length > 0 ? " > " : string.Empty) + SliceOrder[i];
            }

            return order.Length > 0 ? order : "none";
        }

        private static int CountEnabledFarms()
        {
            int count = 0;
            if (AutoFishingFarm.IsEnabled) count++;
            if (InsectNetFarm.IsEnabled) count++;
            if (BirdNetFarm.IsEnabled) count++;
            return count;
        }

        private static bool IsSliceEnabled(FarmSlice slice)
        {
            switch (slice)
            {
                case FarmSlice.Fish: return AutoFishingFarm.IsEnabled;
                case FarmSlice.Insect: return InsectNetFarm.IsEnabled;
                case FarmSlice.Bird: return BirdNetFarm.IsEnabled;
                default: return false;
            }
        }

        private static bool IsCensusFresh(FarmSlice slice, float now)
        {
            switch (slice)
            {
                case FarmSlice.Fish: return census.fishAt > 0f && now - census.fishAt <= CensusFreshSeconds;
                case FarmSlice.Insect: return census.insectAt > 0f && now - census.insectAt <= CensusFreshSeconds;
                case FarmSlice.Bird: return census.birdAt > 0f && now - census.birdAt <= CensusFreshSeconds;
                default: return false;
            }
        }

        // Confirmed present: a recent reading with something in it.
        private static bool HasFreshTargets(FarmSlice slice, float now)
        {
            return IsCensusFresh(slice, now) && GetSliceTargetCount(slice) > 0;
        }

        // Confirmed absent — NOT the negation of the above: a class whose census is stale is unknown,
        // and unknown must not be treated as empty (that would hand the tool away on no evidence).
        private static bool IsKnownEmpty(FarmSlice slice, float now)
        {
            return IsCensusFresh(slice, now) && GetSliceTargetCount(slice) == 0;
        }

        private static int GetSliceTargetCount(FarmSlice slice)
        {
            switch (slice)
            {
                case FarmSlice.Fish: return census.fishInRange;
                case FarmSlice.Insect: return census.insectsInRange;
                case FarmSlice.Bird: return census.birdsInRange;
                default: return 0;
            }
        }

        private static int GetSliceToolId(FarmSlice slice)
        {
            switch (slice)
            {
                case FarmSlice.Fish: return ToolIdRod;
                case FarmSlice.Insect: return ToolIdNet;
                case FarmSlice.Bird: return ToolIdBirdScanner;
                default: return 0;
            }
        }

        private static int GetSliceCatchCount(FarmSlice slice)
        {
            switch (slice)
            {
                case FarmSlice.Fish: return AutoFishingFarm.SessionCatchCount;
                case FarmSlice.Insect: return InsectNetFarm.GetSessionCatchCount();
                case FarmSlice.Bird: return BirdNetFarm.GetSessionCatchCount();
                default: return 0;
            }
        }

        // Leaving a slice must not throw away work already in flight:
        //   * fishing — a cast/battle is a multi-second server-side session; cutting it mid-way loses
        //     the fish (and would strand the reel state),
        //   * birds — captures only count when the server ACK lands, up to 8 s after the send.
        // The insect farm sends fire-and-forget per tick, so it has no in-flight state to protect.
        private static bool IsSliceAtSafePoint(FarmSlice slice)
        {
            switch (slice)
            {
                case FarmSlice.Fish: return !AutoFishingFarm.IsInFishingSession;
                case FarmSlice.Bird: return !BirdNetFarm.HasPendingConfirms;
                default: return true;
            }
        }

        // World-ready callback: resolve ToolSystem + GetTool once per world. Returning false asks the
        // gate to retry (bounded) — e.g. while the module dictionary is still filling.
        private static bool TryPrimeToolSystemProbe(HeartopiaComplete host)
        {
            probeArmed = true;
            nextCensusStepAt = -999f;
            nextDurabilityStepAt = -999f;
            nextSummaryLogAt = -999f;
            lastSummarySignature = string.Empty;

            if (host.TryGetToolDurabilityById(ToolIdRod, out int durability, out int maxDurability, out string status))
            {
                toolSystemProbeConfirmed = true;
                Log($"World ready — ToolSystem.GetTool resolved (rod {durability}/{maxDurability}). Probe armed.");
                return true;
            }

            Log("World ready — ToolSystem.GetTool not resolvable yet: " + status);
            return false;
        }

        private static void ResetProbeState()
        {
            probeArmed = false;
            toolSystemProbeConfirmed = false;
            censusStepIndex = 0;
            durabilityStepIndex = 0;
            nextCensusStepAt = -999f;
            nextDurabilityStepAt = -999f;
            nextSummaryLogAt = -999f;
            nextCoordinatorStepAt = -999f;
            lastSummarySignature = string.Empty;
            census = new FarmCensus { fishAt = -999f, insectAt = -999f, birdAt = -999f };
            for (int i = 0; i < toolDurabilities.Length; i++)
            {
                toolDurabilities[i] = new ToolDurability
                {
                    toolId = FarmToolIds[i],
                    readAt = -999f,
                    lastStatus = "not read",
                };
            }
        }

        // One tool per step: each honoured read invokes GetTool + pins the returned object, and
        // there is no reason to pay for three in one frame.
        private static void StepDurabilityProbe(HeartopiaComplete host, float now)
        {
            int index = durabilityStepIndex % FarmToolIds.Length;
            durabilityStepIndex = (durabilityStepIndex + 1) % FarmToolIds.Length;
            int toolId = FarmToolIds[index];

            if (host.TryGetToolDurabilityById(toolId, out int durability, out int maxDurability, out string status))
            {
                toolSystemProbeConfirmed = true;
                toolDurabilities[index].durability = durability;
                toolDurabilities[index].maxDurability = maxDurability;
                toolDurabilities[index].readAt = now;
                toolDurabilities[index].lastStatus = status;
                return;
            }

            toolDurabilities[index].lastStatus = status;
        }

        private static void StepCensus(HeartopiaComplete host, float now)
        {
            int step = censusStepIndex % 3;
            censusStepIndex = (censusStepIndex + 1) % 3;

            try
            {
                switch (step)
                {
                    case 0:
                        StepFishCensus(host, now);
                        break;
                    case 1:
                        StepInsectCensus(host, now);
                        break;
                    default:
                        StepBirdCensus(host, now);
                        break;
                }
            }
            catch (Exception ex)
            {
                Log("Census step " + step + " error: " + ex.Message);
            }
        }

        // Fish presence has three sources, in order of preference:
        //   1. a live fishing session — the farm is busy catching one, which is presence by
        //      definition, and this stops a mid-battle "no free shadows" reading from arming the
        //      ran-dry exit;
        //   2. the running farm's own published scan — free, and it avoids a second scan perturbing
        //      the engine's target bookkeeping;
        //   3. our own scan, used when the farm is off or suspended (its published values go stale
        //      during the other slices, and stale must not read as empty).
        private static void StepFishCensus(HeartopiaComplete host, float now)
        {
            if (AutoFishingFarm.IsEnabled && AutoFishingFarm.IsInFishingSession)
            {
                census.fishInRange = Mathf.Max(1, AutoFishingFarm.LastInRangeCount);
                census.fishNearest = Vector3.zero;
                census.fishAt = now;
                return;
            }

            bool farmPublishing = AutoFishingFarm.IsEnabled
                && !AutoFishingFarm.IsSuspended
                && AutoFishingFarm.LastScanAt > 0f
                && now - AutoFishingFarm.LastScanAt <= 5f;
            if (farmPublishing)
            {
                census.fishInRange = AutoFishingFarm.LastInRangeCount;
                // The farm publishes the COUNT but not the position — clear the nearest instead
                // of leaving the last self-scanned one, which would age silently.
                census.fishNearest = Vector3.zero;
                census.fishAt = AutoFishingFarm.LastScanAt;
                return;
            }

            if (host.TryFindNearestFishShadowTarget(GetCensusRange(FarmSlice.Fish), out uint _, out Vector3 position,
                    out float _, out int _, out int inRangeCount, out string _))
            {
                census.fishNearest = position;
            }

            census.fishInRange = inRangeCount;
            census.fishAt = now;
        }

        private static void StepInsectCensus(HeartopiaComplete host, float now)
        {
            if (host.TryGetLoadedInsectTargets(out int _, out List<uint> _, out List<Vector3> positions, out string _)
                && positions != null
                && positions.Count > 0)
            {
                if (!host.TryGetLocalPlayerPosition(out Vector3 playerPos))
                {
                    playerPos = positions[0];
                }

                float insectRange = GetCensusRange(FarmSlice.Insect);
                float best = float.MaxValue;
                int inRange = 0;
                Vector3 nearest = Vector3.zero;
                for (int i = 0; i < positions.Count; i++)
                {
                    Vector3 insectPos = positions[i];
                    float distance = new Vector2(insectPos.x - playerPos.x, insectPos.z - playerPos.z).magnitude;
                    if (insectRange > 0f && distance > insectRange)
                    {
                        continue;
                    }

                    inRange++;
                    if (distance < best)
                    {
                        best = distance;
                        nearest = insectPos;
                    }
                }

                census.insectsInRange = inRange;
                census.insectNearest = nearest;
                census.insectAt = now;
                return;
            }

            census.insectsInRange = 0;
            census.insectAt = now;
        }

        private static void StepBirdCensus(HeartopiaComplete host, float now)
        {
            if (host.TryScanBirdObjectsNearby(GetCensusRange(FarmSlice.Bird), out int inRangeCount, out Vector3 nearestPos,
                    out float _, out string _))
            {
                census.birdNearest = nearestPos;
            }

            census.birdsInRange = inRangeCount;
            census.birdAt = now;
        }

        private static void LogSliceEnd(float now, string reason)
        {
            if (activeSlice == FarmSlice.None || sliceStartedAt < 0f)
            {
                return;
            }

            int caught = GetSliceCatchCount(activeSlice) - sliceStartCatchCount;
            if (caught < 0)
            {
                // The farm's session counter reset under us (it was switched off mid-slice, which is
                // exactly how a slice usually ends when the player disables a farm), so the delta is
                // meaningless rather than negative — hence the "+-17 caught" line in the 2026-07-28 log.
                Log("Slice " + activeSlice + " ended after " + (now - sliceStartedAt).ToString("F1")
                    + "s, catch count unknown (farm counter reset) (" + reason + ").");
                return;
            }

            // Insects report both numbers: the optimistic send count the farm has always used, and
            // the server-confirmed one from the NetCaughtInsectEvent ACK. Both are per-slice DELTAS —
            // printing a session total next to a slice delta invites exactly the wrong comparison.
            // A gap between them is a silent rejection.
            string confirmed = activeSlice == FarmSlice.Insect
                ? " (" + (InsectNetFarm.GetSessionConfirmedCatchCount() - sliceStartConfirmedCount) + " confirmed)"
                : string.Empty;
            Log("Slice " + activeSlice + " ended after " + (now - sliceStartedAt).ToString("F1")
                + "s with +" + caught + " caught" + confirmed + " (" + reason + ").");
        }

        // One compact line per change (or every SummaryLogIntervalSeconds while static), so a session
        // log can be read top-to-bottom to answer both Phase-0 questions.
        private static void LogSummaryIfDue(HeartopiaComplete host, float now)
        {
            if (!DebugLoggingEnabled)
            {
                return;
            }

            string signature = census.fishInRange + "|" + census.insectsInRange + "|" + census.birdsInRange
                + "|" + DurabilityBucket(0) + "|" + DurabilityBucket(1) + "|" + DurabilityBucket(2)
                + "|" + activeSlice + "|" + (repairCycleActive ? repairStep + ":" + repairQueueIndex : string.Empty);
            bool changed = !string.Equals(signature, lastSummarySignature, StringComparison.Ordinal);
            if (!changed && now < nextSummaryLogAt)
            {
                return;
            }

            lastSummarySignature = signature;
            nextSummaryLogAt = now + SummaryLogIntervalSeconds;

            string held = "?";
            // Only ask for the held tool once GetTool has answered at least once: that proves the
            // ToolSystem module is resolved and warm, so this cannot trigger a COLD resolve racing
            // the GC (the crash class documented at BirdNetFarm.CapturePreviousTool).
            if (toolSystemProbeConfirmed && host.TryGetCurrentToolInfo(out int heldToolId, out string heldToolName, out string _))
            {
                held = heldToolId + (string.IsNullOrEmpty(heldToolName) ? string.Empty : "/" + heldToolName);
            }

            // Ranges are printed next to the counts on purpose: a count taken with the wrong radius
            // looks perfectly healthy in a log that omits it (see GetCensusRange).
            Log("census fish=" + census.fishInRange + DescribeRange(FarmSlice.Fish)
                + " insects=" + census.insectsInRange + DescribeRange(FarmSlice.Insect)
                + " birds=" + census.birdsInRange + DescribeRange(FarmSlice.Bird)
                + " | slice=" + DescribeSliceState()
                + " held=" + held
                + " | " + DescribeDurability(0)
                + " " + DescribeDurability(1)
                + " " + DescribeDurability(2));
        }

        // The active farm's OWN status line. Without it a stuck slice is invisible here: the census
        // and the held tool look fine while the farm sits in one of its many silent early-return
        // branches ("Waiting for world", "Paused for Auto Repair", "Tool check unavailable",
        // "Equipping rod…"). That is exactly how a 76 s Fish slice with the bird scanner still in
        // hand got logged as healthy.
        private static string DescribeSliceState()
        {
            if (!coordinating)
            {
                return "off";
            }

            if (repairCycleActive)
            {
                string tool = repairQueueIndex < repairQueue.Count
                    ? GetToolName(repairQueue[repairQueueIndex])
                    : "-";
                return "repair:" + tool + "/" + repairStep
                    + " (" + (repairQueueIndex + 1) + "/" + repairQueue.Count + ", resume " + activeSlice + ")";
            }

            return activeSlice + DescribeActiveFarmState();
        }

        private static string DescribeActiveFarmState()
        {
            if (!coordinating || repairCycleActive)
            {
                return string.Empty;
            }

            switch (activeSlice)
            {
                case FarmSlice.Fish:
                    return " st=\"" + AutoFishingFarm.GetLastStatus() + "\"/\"" + AutoFishingFarm.GetLastToolStatus() + "\"";
                case FarmSlice.Insect:
                    return " st=\"" + InsectNetFarm.GetLastStatus() + "\"/\"" + InsectNetFarm.GetLastToolStatus() + "\"";
                case FarmSlice.Bird:
                    return " st=\"" + BirdNetFarm.GetLastStatus() + "\"/\"" + BirdNetFarm.GetLastToolStatus() + "\"";
                default:
                    return string.Empty;
            }
        }

        private static string DescribeRange(FarmSlice slice)
        {
            float range = GetCensusRange(slice);
            return range > 0f ? "@" + range.ToString("F0") + "m" : "@any";
        }

        // Bucketed to 5 % so the change-detector reacts to real wear, not to a single point of noise.
        private static int DurabilityBucket(int index)
        {
            ToolDurability tool = toolDurabilities[index];
            return tool.IsKnown ? Mathf.RoundToInt(tool.Ratio * 20f) : -1;
        }

        private static string DescribeDurability(int index)
        {
            ToolDurability tool = toolDurabilities[index];
            string name = GetToolName(tool.toolId);
            if (!tool.IsKnown)
            {
                return name + "=?(" + (tool.lastStatus ?? "unknown") + ")";
            }

            return name + "=" + tool.durability + "/" + tool.maxDurability
                + "(" + Mathf.RoundToInt(tool.Ratio * 100f) + "%)";
        }

        private static void Log(string message)
        {
            if (!DebugLoggingEnabled)
            {
                return;
            }

            ModLogger.Msg("[CombinedFarm] " + message);
        }
    }
}
