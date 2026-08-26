using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppInterop.Runtime.Runtime;
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

using UnityObject = UnityEngine.Object;
using Il2CppType = Il2CppSystem.Type;
using Il2CppFieldInfo = Il2CppSystem.Reflection.FieldInfo;
using Il2CppMethodInfo = Il2CppSystem.Reflection.MethodInfo;
using Il2CppPropertyInfo = Il2CppSystem.Reflection.PropertyInfo;
using Il2CppBindingFlags = Il2CppSystem.Reflection.BindingFlags;
using Il2CppObject = Il2CppSystem.Object;
using Object = UnityEngine.Object;


namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {

        private string GetForagingModeLabel()
        {
            return this.auraFarmEnabled ? "Aura Farm" : "No mode";
        }

        private string GetForagingStatusDisplayText(bool compact)
        {
            if (!this.autoFarmActive)
            {
                return "Idle";
            }

            string status = this.autoFarmStatus;
            if (string.IsNullOrWhiteSpace(status)
                || status == "READY"
                || status == "Idle"
                || status == "NO_TOGGLES"
                || status == "NO_TOGGLES_ERROR"
                || status == "RADAR_OFF_ERROR"
                || status == "MODE_REQUIRED_ERROR")
            {
                return compact ? "Running" : "Running (" + this.GetForagingModeLabel() + ")";
            }

            if (!compact)
            {
                return status;
            }

            if (status.StartsWith("Collecting", StringComparison.OrdinalIgnoreCase))
                return "Collecting";
            if (status.StartsWith("Cleaning", StringComparison.OrdinalIgnoreCase))
                return "Cleaning";
            if (status.StartsWith("Paused", StringComparison.OrdinalIgnoreCase))
                return "Paused";
            if (status.StartsWith("Sea cleaner depleted", StringComparison.OrdinalIgnoreCase))
                return "Repair Needed";
            if (status.StartsWith("Scanning", StringComparison.OrdinalIgnoreCase))
                return "Scanning";
            if (status.StartsWith("Loading", StringComparison.OrdinalIgnoreCase))
                return "Loading";
            if (status.StartsWith("Moving", StringComparison.OrdinalIgnoreCase)
                || status.StartsWith("Going", StringComparison.OrdinalIgnoreCase)
                || status.StartsWith("Teleporting", StringComparison.OrdinalIgnoreCase))
                return "Moving";
            if (status.StartsWith("Farming", StringComparison.OrdinalIgnoreCase))
                return "Farming";
            if (status.StartsWith("Adjusting camera", StringComparison.OrdinalIgnoreCase))
                return "Camera Fix";
            if (status.StartsWith("Rechecking", StringComparison.OrdinalIgnoreCase))
                return "Rechecking";
            if (status.StartsWith("No nodes found", StringComparison.OrdinalIgnoreCase))
                return "Cycling";
            if (status.StartsWith("Node found", StringComparison.OrdinalIgnoreCase))
                return "Node Found";

            return status;
        }


        private void AutoFarmLog(string message)
        {
            if (!AutoFarmLogsEnabled)
            {
                return;
            }

            try
            {
                ModLogger.Msg("[AutoFarm] " + message);
            }
            catch
            {
            }
        }








        // Token: 0x06000015 RID: 21 RVA: 0x00003ECC File Offset: 0x000020CC
        private void RunAutoFarmLogic()
        {
            // Stealth Block gate (StealthBlockFeature.cs): while the mass-block is enabled but not
            // ARMED, the whole state machine holds — no hop, no dwell advance. That is what makes
            // "wait until every stranger is blocked and no friend is around before diving" true for
            // the START, and it re-engages mid-run the moment an unblocked stranger walks in.
            if (!this.IsStealthBlockFarmHoldClear(out string stealthHold))
            {
                this.autoFarmStatus = stealthHold;
                this.autoFarmTimer = 0f;
                return;
            }

            // Repair-aura hold, ahead of the state machine so it applies in EVERY state. A repair
            // kit thrown underwater drops its aura on the sea floor; if the player keeps swimming
            // (or just floats where the throw happened) the repair never starts. Bounded inside.
            if (this.farmWalkToNodeEnabled && this.ProcessFarmRepairAuraHold())
            {
                this.autoFarmTimer = 0f;
                return;
            }

            this.RefreshActivePriorityLocations();
            this.autoFarmTimer += Time.unscaledDeltaTime;
            this.priorityRecheckTimer += Time.unscaledDeltaTime;
            bool flag = this.cameraStuckDisplayTimer > 0f;
            if (flag)
            {
                this.cameraStuckDisplayTimer -= Time.unscaledDeltaTime;
            }
            switch (this.farmState)
            {
                case HeartopiaComplete.AutoFarmState.ScanningForNodes:
                    {
                        // Auto Repair coordination (mirrors the FishingRoute hop gate): never
                        // start a teleport while a repair kit is in use or the restore aura is
                        // still ticking — the hop would yank the player out of the repair circle.
                        // Only the teleport-initiating states are gated; an in-flight
                        // collect/clean dwell (Collecting) always finishes.
                        if (this.IsAutoRepairBusy())
                        {
                            this.autoFarmStatus = "Paused for Auto Repair...";
                            this.autoFarmTimer = 0f;
                            break;
                        }

                        // Standing over the loot the last node dropped (see FinishCollectingCycle).
                        // After the repair gate on purpose — a repair in progress outranks it.
                        if (Time.unscaledTime < this.farmLootHoldUntil)
                        {
                            this.autoFarmStatus = "Picking up the drop...";
                            break;
                        }

                        // Corrupted debuff (buff 610) + Contamination radar: park at the nearest
                        // cleansing coral until it clears. Repair still wins (gate above); an
                        // in-flight Collecting dwell is never interrupted (this state only).
                        if (this.TryBeginCorruptionCleanse())
                        {
                            break;
                        }

                        // Teleport-rate throttle (Foraging Settings slider): hold the next hop until
                        // the configured delay has elapsed since the last teleport. Placed after the
                        // repair/corruption gates so those keep priority.
                        if (this.IsFarmTeleportThrottled(out float tpCooldownScan))
                        {
                            this.autoFarmStatus = $"Teleport cooldown... ({tpCooldownScan:F1}s)";
                            break;
                        }

                        // Periodic recheck of priority locations
                        if (this.priorityRecheckTimer >= 60f) // 1 minute
                        {
                            this.priorityRecheckTimer = 0f;
                            Vector3? recheckLocation = this.GetActivePriorityLocation();
                            if (recheckLocation != null)
                            {
                                float distance = Vector3.Distance(Camera.main.transform.position, recheckLocation.Value);
                                this.autoFarmStatus = $"Rechecking priority location ({distance:F0}m)...";
                                this.AutoFarmLog("Periodic priority recheck -> location " + recheckLocation.Value + " distance=" + distance.ToString("F1"));
                                // ⚠️ SAME RULE AS THE ZONE MOVE BELOW: a destination the walker can
                                // route to is not warped to. This site used to teleport whatever the
                                // Walk to Zone Point switch said, which made the switch mean "walk
                                // between farm zones, except when the priority list moves you".
                                if (this.farmWalkToAreaEnabled
                                    && this.TryBeginFarmWalkToArea(recheckLocation.Value, "priority location"))
                                {
                                    // ⚠️ THE SAME BOOKKEEPING THE TELEPORT DOES. Arriving on foot is
                                    // still arriving: currentPriorityLocation is what the collect
                                    // cycle reads to decide whether this location keeps its slot,
                                    // and lastTeleportWasPriorityLocation gates that check at all —
                                    // its name is historical, it means "this cycle belongs to a
                                    // priority location", not "we warped".
                                    this.currentPriorityLocation = recheckLocation;
                                    this.lastTeleportWasPriorityLocation = true;
                                    this.autoFarmStatus = "Walking to the priority location...";
                                    this.farmState = HeartopiaComplete.AutoFarmState.WalkingToNode;
                                    this.autoFarmTimer = 0f;
                                    break;
                                }

                                this.FarmTeleportTo(this.ApplyForagingAreaTeleportOffset(recheckLocation.Value),
                                    "area:priority-recheck", recheckLocation.Value);
                                this.currentPriorityLocation = recheckLocation;
                                this.lastTeleportWasPriorityLocation = true;
                                this.farmState = HeartopiaComplete.AutoFarmState.WaitingForPriorityArea;
                                this.autoFarmTimer = 0f;
                                break;
                            }
                        }

                        // If we're already working an active priority area, keep sweeping
                        // matching nodes in that area before jumping back to the anchor.
                        if (this.currentPriorityLocation.HasValue)
                        {
                            float distanceToActivePriorityArea = Vector3.Distance(Camera.main.transform.position, this.currentPriorityLocation.Value);
                            if (distanceToActivePriorityArea <= 120f)
                            {
                                Vector3? activeAreaPriorityNode = this.FindClosestPriorityNodeForLocation(this.currentPriorityLocation.Value, Camera.main.transform.position, false);
                                if (activeAreaPriorityNode != null)
                                {
                                    float distance = Vector3.Distance(Camera.main.transform.position, activeAreaPriorityNode.Value);
                                    this.autoFarmStatus = $"Sweeping active priority node ({distance:F0}m)...";
                                    this.AutoFarmLog("Active priority sweep -> node " + activeAreaPriorityNode.Value
                                        + " area=" + this.currentPriorityLocation.Value + " distance=" + distance.ToString("F1"));
                                    this.lastNodePosition = activeAreaPriorityNode.Value;
                                    // Walk-to-node mode (FarmWalkFeature.cs): walk the route the
                                    // Track line follows instead of hopping. Falls through to the
                                    // teleport below whenever no route exists.
                                    string activePriorityLabel = this.lastFoundPriorityNodeLabel;
                                    if (this.TryBeginFarmWalk(activeAreaPriorityNode.Value, "node:priority-active", true, activePriorityLabel))
                                    {
                                        this.lastTeleportWasPriorityLocation = true;
                                        this.farmState = HeartopiaComplete.AutoFarmState.WalkingToNode;
                                        this.autoFarmTimer = 0f;
                                        break;
                                    }
                                    this.FarmTeleportTo(this.ApplyForagingNodeTeleportOffset(activeAreaPriorityNode.Value, activePriorityLabel),
                                        "node:priority-active", activeAreaPriorityNode.Value);
                                    this.lastTeleportWasPriorityLocation = true;
                                    this.farmState = HeartopiaComplete.AutoFarmState.Collecting;
                                    this.autoFarmTimer = 0f;
                                    this.autoCollectClickedSinceArrival = false;
                                    this.cameraRotationAttempts = 0;
                                    this.BeginFarmNodeDwell(activePriorityLabel);
                                    break;
                                }
                            }
                        }

                        // FIRST: Check for priority nodes that are actually visible on screen right now.
                        Vector3? priorityNode = this.FindClosestVisiblePriorityNode(Camera.main.transform.position, Time.unscaledTime);
                        if (priorityNode != null)
                        {
                            float distance = Vector3.Distance(Camera.main.transform.position, priorityNode.Value);
                            this.autoFarmStatus = $"Teleporting to priority node ({distance:F0}m)...";
                            this.AutoFarmLog("Visible priority node -> " + priorityNode.Value
                                + " mappedArea=" + (this.lastFoundPriorityNodeLocation.HasValue ? this.lastFoundPriorityNodeLocation.Value.ToString() : "none")
                                + " distance=" + distance.ToString("F1"));
                            this.lastNodePosition = priorityNode.Value;
                            string visiblePriorityLabel = this.lastFoundPriorityNodeLabel;
                            if (this.TryBeginFarmWalk(priorityNode.Value, "node:priority-visible", true, visiblePriorityLabel))
                            {
                                if (this.lastFoundPriorityNodeLocation.HasValue)
                                {
                                    this.currentPriorityLocation = this.lastFoundPriorityNodeLocation;
                                }
                                this.lastTeleportWasPriorityLocation = this.currentPriorityLocation.HasValue;
                                this.farmState = HeartopiaComplete.AutoFarmState.WalkingToNode;
                                this.autoFarmTimer = 0f;
                                break;
                            }
                            this.FarmTeleportTo(this.ApplyForagingNodeTeleportOffset(priorityNode.Value, visiblePriorityLabel),
                                "node:priority-visible", priorityNode.Value);
                            if (this.lastFoundPriorityNodeLocation.HasValue)
                            {
                                this.currentPriorityLocation = this.lastFoundPriorityNodeLocation;
                            }
                            this.lastTeleportWasPriorityLocation = this.currentPriorityLocation.HasValue;
                            this.farmState = HeartopiaComplete.AutoFarmState.Collecting;
                            this.autoFarmTimer = 0f;
                            this.autoCollectClickedSinceArrival = false;
                            this.cameraRotationAttempts = 0;
                            this.BeginFarmNodeDwell(visiblePriorityLabel);
                            break;
                        }

                        // SECOND: Route to an active priority location even if no priority node is visible yet.
                        Vector3? priorityLocation = this.GetActivePriorityLocation();
                        if (priorityLocation != null)
                        {
                            float distance = Vector3.Distance(Camera.main.transform.position, priorityLocation.Value);
                            this.autoFarmStatus = $"Going to priority location ({distance:F0}m)...";
                            this.AutoFarmLog("Priority location fallback -> " + priorityLocation.Value + " distance=" + distance.ToString("F1"));
                            // ⚠️ SAME RULE AS THE ZONE MOVE BELOW: a destination the walker can
                            // route to is not warped to. This site used to teleport whatever the
                            // Walk to Zone Point switch said, which made the switch mean "walk
                            // between farm zones, except when the priority list moves you".
                            if (this.farmWalkToAreaEnabled
                                && this.TryBeginFarmWalkToArea(priorityLocation.Value, "priority location"))
                            {
                                this.currentPriorityLocation = priorityLocation;
                                this.lastTeleportWasPriorityLocation = true;
                                this.autoFarmStatus = "Walking to the priority location...";
                                this.farmState = HeartopiaComplete.AutoFarmState.WalkingToNode;
                                this.autoFarmTimer = 0f;
                                break;
                            }

                            this.FarmTeleportTo(this.ApplyForagingAreaTeleportOffset(priorityLocation.Value),
                                "area:priority-fallback", priorityLocation.Value);
                            this.currentPriorityLocation = priorityLocation;
                            this.lastTeleportWasPriorityLocation = true;
                            this.farmState = HeartopiaComplete.AutoFarmState.WaitingForPriorityArea;
                            this.autoFarmTimer = 0f;
                            break;
                        }

                        // A node skipped as unreachable gets one more attempt now that the next one
                        // has been collected — from a different position, so the route is
                        // recomputed from a new angle (FarmWalkFeature.cs).
                        if (this.TryTakeFarmWalkRetryNode(out Vector3 retryNode, out string retryLabel))
                        {
                            this.lastNodePosition = retryNode;
                            if (this.TryBeginFarmWalk(retryNode, "node:retry", false, retryLabel))
                            {
                                ModLogger.Msg("[FarmWalk] retrying the node skipped earlier at "
                                    + FormatNavMeshVector(retryNode) + ".");
                                this.lastTeleportWasPriorityLocation = false;
                                this.farmState = HeartopiaComplete.AutoFarmState.WalkingToNode;
                                this.autoFarmTimer = 0f;
                                break;
                            }
                            // No route from here either — fall through to the normal scan.
                        }

                        // THIRD: Normal scanning logic.
                        //
                        // Walk mode follows a PLANNED tour instead of re-picking the nearest node
                        // every scan: on foot the greedy pick walks into dead ends and the target
                        // can change mid-approach. Teleporting keeps the old behaviour — a warp
                        // costs the same from anywhere, so ordering buys nothing there.
                        Vector3? vector;
                        string scanNodeLabel;
                        if (this.farmWalkToNodeEnabled)
                        {
                            Vector3 tourOrigin = this.ResolveFarmTourOrigin();
                            this.TopUpFarmTour(tourOrigin, 0);
                            vector = this.TryGetNextFarmTourStop(tourOrigin, out Vector3 tourStop, out scanNodeLabel)
                                ? new Vector3?(tourStop)
                                : null;
                        }
                        else
                        {
                            vector = this.FindClosestAvailableNode(out scanNodeLabel);
                        }

                        bool flag2 = vector != null;
                        if (flag2)
                        {
                            float value = Vector3.Distance(Camera.main.transform.position, vector.Value);
                            this.autoFarmStatus = $"Teleporting to node ({value:F0}m)...";
                            this.AutoFarmLog("Normal node target -> " + vector.Value + " label=" + scanNodeLabel + " distance=" + value.ToString("F1"));
                            this.lastNodePosition = vector.Value;
                            if (this.TryBeginFarmWalk(vector.Value, "node:" + (scanNodeLabel ?? "unlabelled"), false, scanNodeLabel))
                            {
                                this.NoteFarmWalkRouteSucceeded();
                                this.lastTeleportWasPriorityLocation = false;
                                this.farmState = HeartopiaComplete.AutoFarmState.WalkingToNode;
                                this.autoFarmTimer = 0f;
                                break;
                            }

                            // Unroutable: take another node rather than warping to this one.
                            if (this.farmWalkToNodeEnabled
                                && this.TryDeferUnroutableFarmNode(vector.Value, scanNodeLabel))
                            {
                                this.autoFarmStatus = "No route there — trying another node...";
                                this.autoFarmTimer = 0f;
                                break;
                            }

                            this.FarmTeleportTo(this.ApplyForagingNodeTeleportOffset(vector.Value, scanNodeLabel),
                                "node:" + (scanNodeLabel ?? "unlabelled"), vector.Value);
                            this.lastTeleportWasPriorityLocation = false;
                            this.farmState = HeartopiaComplete.AutoFarmState.Collecting;
                            this.autoFarmTimer = 0f;
                            this.autoCollectClickedSinceArrival = false;
                            this.cameraRotationAttempts = 0;
                            this.BeginFarmNodeDwell(scanNodeLabel);
                        }
                        else if (this.ShouldHoldFarmScanForSkippedNode())
                        {
                            // A node was just skipped as unreachable and nothing else is listed yet.
                            // Keep scanning for a few seconds instead of warping: the radar marker
                            // list rebuilds on its own cadence and nodes respawn, so most of these
                            // resolve into an ordinary walk if given a moment.
                            this.autoFarmStatus = "Looking for another node...";
                        }
                        else if (this.TryConsumeFarmWalkSkippedNode(out Vector3 skippedNode, out string skippedLabel))
                        {
                            // Nothing else here, and the only reason this one is not a candidate is
                            // that the walker just skipped it.
                            //
                            // ⚠️ A ROUTE THAT EXISTS IS NOT ALLOWED TO BE WARPED PAST. This branch
                            // used to teleport unconditionally, on the reasoning that the walker had
                            // just given up on this node — but "the walk gave up" and "there is no
                            // way there" are different facts. Measured 2026-08-23: the walk reached
                            // 0.3 m of the end of its route and quit because the bubble hung 2.2 m
                            // overhead, which says nothing about the route being unwalkable. Ninety
                            // seconds later this branch warped 40 m to a node the graph could route
                            // to perfectly well.
                            //
                            // So ask the router first. Only when it cannot build a route at all is
                            // the teleport the thing that is left.
                            //
                            // Livelock is already handled a layer down: a node that fails again
                            // within FarmWalkRepeatOffenderWindow of a reclaim is parked for five
                            // minutes rather than reclaimed a second time.
                            if (this.farmWalkToNodeEnabled
                                && this.TryBeginFarmWalk(skippedNode, "node:skip-reclaim", false, skippedLabel))
                            {
                                this.NoteFarmWalkRouteSucceeded();
                                ModLogger.Msg("[FarmWalk] no other node in range — walking back to the "
                                    + "skipped one at " + FormatNavMeshVector(skippedNode)
                                    + " (a route exists, so this is not a teleport).");
                                this.lastNodePosition = skippedNode;
                                this.lastTeleportWasPriorityLocation = false;
                                this.farmState = HeartopiaComplete.AutoFarmState.WalkingToNode;
                                this.autoFarmTimer = 0f;
                                this.autoFarmStatus = "Walking back to the skipped node...";
                                break;
                            }

                            ModLogger.Msg("[FarmWalk] no other node in range and no route to the skipped "
                                + "one at " + FormatNavMeshVector(skippedNode)
                                + " — taking it back with a teleport.");
                            this.FarmTeleportTo(this.ApplyForagingNodeTeleportOffset(skippedNode, skippedLabel),
                                "node:skip-reclaim", skippedNode);
                            this.lastNodePosition = skippedNode;
                            this.lastTeleportWasPriorityLocation = false;
                            this.farmState = HeartopiaComplete.AutoFarmState.Collecting;
                            this.autoFarmTimer = 0f;
                            this.autoCollectClickedSinceArrival = false;
                            this.cameraRotationAttempts = 0;
                            this.BeginFarmNodeDwell(skippedLabel);
                        }
                        else
                        {
                            this.farmState = HeartopiaComplete.AutoFarmState.MovingToLocation;
                            this.autoFarmTimer = 0f;
                        }
                        break;
                    }
                case HeartopiaComplete.AutoFarmState.WalkingToNode:
                    {
                        // Ground walk along the Track waypoint route (FarmWalkFeature.cs). The tick
                        // owns its own timeout / stuck escalation and returns true once the player
                        // is at the node — whether it walked there or gave up and teleported — so
                        // both outcomes hand over to Collecting identically.
                        if (this.RunFarmWalkTick())
                        {
                            this.EnterFarmCollectingAfterWalk();
                        }
                        break;
                    }
                case HeartopiaComplete.AutoFarmState.Collecting:
                    {
                        // Contamination nodes get the sea-clean sweep dwell instead of the aura
                        // pick wait. Deliberately NOT gated on IsAutoRepairBusy — an in-flight
                        // dwell finishes (and can even hold for a cleaner repair).
                        if (this.autoFarmTargetIsContamination)
                        {
                            this.RunContaminationCleanWait();
                            break;
                        }
                        if (this.auraFarmEnabled && this.auraCollectWaitArmed)
                        {
                            this.RunAuraCollectWait();
                            break;
                        }
                        bool flag3 = this.autoFarmTimer >= 5f;
                        if (flag3)
                        {
                            this.StampVisitedNode(this.lastNodePosition, Time.unscaledTime + FarmVisitedRetryStampSeconds);
                            this.FinishCollectingCycle();
                        }
                        else
                        {
                            bool flag4 = this.autoFarmTimer >= 3f;
                            if (flag4)
                            {
                                this.StampVisitedNode(this.lastNodePosition, Time.unscaledTime + FarmVisitedRetryStampSeconds);
                                this.FinishCollectingCycle();
                            }
                            else
                            {
                                bool hasAnyPrompt = this.HasAnyVisibleInteractPrompt();
                                bool auraHasRecentCommand = this.auraFarmEnabled && Time.unscaledTime - this.auraLastSuccessfulCommandAt <= 1.2f;
                                bool flag5 = !this.autoCollectClickedSinceArrival && (!this.auraFarmEnabled || this.auraLastTargetCount <= 0 || !auraHasRecentCommand) && !this.mouseLookCaptureActive && !hasAnyPrompt;
                                if (flag5)
                                {
                                    bool flag6 = this.autoFarmTimer >= 1f && this.cameraRotationAttempts == 0;
                                    if (flag6)
                                    {
                                        this.RotateCameraAroundPlayer(90f);
                                        this.cameraRotationAttempts = 1;
                                        this.autoFarmStatus = "Adjusting camera (90 deg)...";
                                        this.cameraStuckDisplayTimer = 2f;
                                    }
                                    else
                                    {
                                        bool flag7 = this.autoFarmTimer >= 1.75f && this.cameraRotationAttempts == 1;
                                        if (flag7)
                                        {
                                            this.RotateCameraAroundPlayer(90f);
                                            this.cameraRotationAttempts = 2;
                                            this.autoFarmStatus = "Adjusting camera (180 deg)...";
                                            this.cameraStuckDisplayTimer = 2f;
                                        }
                                        else
                                        {
                                            bool flag8 = this.autoFarmTimer >= 2.5f && this.cameraRotationAttempts == 2;
                                            if (flag8)
                                            {
                                                this.RotateCameraAroundPlayer(90f);
                                                this.cameraRotationAttempts = 3;
                                                this.autoFarmStatus = "Adjusting camera (270 deg)...";
                                                this.cameraStuckDisplayTimer = 2f;
                                            }
                                            else
                                            {
                                                bool flag9 = this.cameraRotationAttempts < 3;
                                                if (flag9)
                                                {
                                                    this.autoFarmStatus = $"Collecting... ({3f - this.autoFarmTimer:F1}s remaining)";
                                                }
                                            }
                                        }
                                    }
                                }
                                else
                                {
                                    this.autoFarmStatus = $"Collecting... ({3f - this.autoFarmTimer:F1}s remaining)";
                                }
                            }
                        }
                        break;
                    }
                case HeartopiaComplete.AutoFarmState.MovingToLocation:
                    {
                        // Auto Repair coordination: hold the location hop while a repair runs.
                        if (this.IsAutoRepairBusy())
                        {
                            this.autoFarmStatus = "Paused for Auto Repair...";
                            this.autoFarmTimer = 0f;
                            break;
                        }

                        // Corrupted debuff: cleanse before hopping to the next farm location.
                        if (this.TryBeginCorruptionCleanse())
                        {
                            break;
                        }

                        if (this.IsFarmTeleportThrottled(out float tpCooldownMove))
                        {
                            this.autoFarmStatus = $"Teleport cooldown... ({tpCooldownMove:F1}s)";
                            break;
                        }

                        // Let the aura finish on the node we just reached before leaving the area.
                        //
                        // The throttle above measures time since the last TELEPORT, and in walk
                        // mode there may not have been one for minutes — so it gates nothing and
                        // the relocation fires the moment the scan comes up empty. The 22:31 run
                        // arrived at a Shiitake at :06 and warped to Oyster Spawn at :09, three
                        // seconds later, with the collect still in flight. Measure from the last
                        // NODE arrival instead, using the same delay the user configured.
                        float sinceNodeActivity = Time.unscaledTime - this.lastFarmNodeActivityAt;
                        if (this.lastFarmNodeActivityAt > 0f
                            && sinceNodeActivity < this.foragingTeleportDelaySeconds)
                        {
                            this.autoFarmStatus =
                                $"Letting the last node finish... ({this.foragingTeleportDelaySeconds - sinceNodeActivity:F1}s)";
                            break;
                        }

                        bool flag10 = this.farmLocations.Count == 0;
                        if (flag10)
                        {
                            this.autoFarmStatus = "No locations configured!";
                        }
                        else
                        {
                            bool flag11 = this.IsAnyMushroomRadarEnabled();
                            bool flag12 = this.showBlueberryRadar || this.showRaspberryRadar;
                            bool flagTree = this.showTreeRadar;
                            // Branch bushes grow among the trees, so they ride the same waypoints.
                            bool flagBranch = this.showBranchRadar;
                            bool flagRareTree = this.showRareTreeRadar;
                            bool flagAppleTree = this.showAppleTreeRadar;
                            bool flagMandarinTree = this.showOrangeTreeRadar;
                            bool flagStone = this.showStoneRadar;
                            bool flagOre = this.showOreRadar;
                            bool flagMeteor = this.showMeteorRadar;
                            bool flagEventFiddlehead = this.showFiddleheadRadar;
                            bool flagEventTallMustard = this.showTallMustardRadar;
                            bool flagEventBurdock = this.showBurdockRadar;
                            bool flagEventMustardGreens = this.showMustardGreensRadar;
                            // Any underwater radar category shares the sea-area waypoints so the farm
                            // can hop to a fresh sea region once the current one is cleared.
                            bool flagUnderwater = this.showContaminatedRadar || this.showGlasswortRadar
                                || this.showSeaGrapeRadar || this.showWakameRadar;
                            int num = this.currentLocationIndex;
                            HeartopiaComplete.FarmLocation farmLocation = null;
                            int num2 = 0;
                            HeartopiaComplete.FarmLocation farmLocation2;
                            for (; ; )
                            {
                                this.currentLocationIndex = (this.currentLocationIndex + 1) % this.farmLocations.Count;
                                farmLocation2 = this.farmLocations[this.currentLocationIndex];
                                bool flag13 = false;
                                bool flag14 = farmLocation2.Type == "any";
                                if (flag14)
                                {
                                    flag13 = true;
                                }
                                else
                                {
                                    bool flag15 = farmLocation2.Type == "both" && (flag11 || flag12);
                                    if (flag15)
                                    {
                                        flag13 = true;
                                    }
                                    else
                                    {
                                        bool flag16 = farmLocation2.Type == "mushroom" && flag11 && this.IsMushroomLocationEnabled(farmLocation2.Name);
                                        if (flag16)
                                        {
                                            flag13 = true;
                                        }
                                        else
                                        {
                                            bool flag17 = (farmLocation2.Type == "berry"
                                                || farmLocation2.Type == "blueberry"
                                                || farmLocation2.Type == "redberry") && flag12;
                                            if (flag17)
                                            {
                                                flag13 = true;
                                            }
                                            else if (farmLocation2.Type == "tree" && (flagTree || flagBranch))
                                            {
                                                flag13 = true;
                                            }
                                            else if (farmLocation2.Type == "raretree" && flagRareTree)
                                            {
                                                flag13 = true;
                                            }
                                            else if (farmLocation2.Type == "appletree" && flagAppleTree)
                                            {
                                                flag13 = true;
                                            }
                                            else if (farmLocation2.Type == "mandarintree" && flagMandarinTree)
                                            {
                                                flag13 = true;
                                            }
                                            else if (farmLocation2.Type == "stone" && flagStone)
                                            {
                                                flag13 = true;
                                            }
                                            else if (farmLocation2.Type == "ore" && flagOre)
                                            {
                                                flag13 = true;
                                            }
                                            else if (farmLocation2.Type == "meteor" && flagMeteor)
                                            {
                                                flag13 = true;
                                            }
                                            else if (farmLocation2.Type == "event_fiddlehead" && flagEventFiddlehead)
                                            {
                                                flag13 = true;
                                            }
                                            else if (farmLocation2.Type == "event_tall_mustard" && flagEventTallMustard)
                                            {
                                                flag13 = true;
                                            }
                                            else if (farmLocation2.Type == "event_burdock" && flagEventBurdock)
                                            {
                                                flag13 = true;
                                            }
                                            else if (farmLocation2.Type == "event_mustard_greens" && flagEventMustardGreens)
                                            {
                                                flag13 = true;
                                            }
                                            else if (farmLocation2.Type == "underwater" && flagUnderwater)
                                            {
                                                flag13 = true;
                                            }
                                        }
                                    }
                                }
                                bool flag18 = flag13;
                                if (flag18)
                                {
                                    break;
                                }
                                num2++;
                                if (num2 >= this.farmLocations.Count)
                                {
                                    goto IL_4AB;
                                }
                            }
                            farmLocation = farmLocation2;
                        IL_4AB:
                            bool flag19 = farmLocation == null;
                            if (flag19)
                            {
                                // Wedge fix: toggles with no farmLocations entry (underwater
                                // plants / contamination have none) used to dead-end this state
                                // forever. Fall back to waiting on radar markers where we stand —
                                // markers keep rebuilding every RunRadar pass.
                                this.autoFarmStatus = "No matching locations for enabled toggles!";
                                this.farmState = HeartopiaComplete.AutoFarmState.WaitingForNodes;
                                this.autoFarmTimer = 0f;
                            }
                            else
                            {
                                // Travel there on foot (and by vehicle on a long haul) when the
                                // user asked for it. Both switches are independent: walking is
                                // useful without the vehicle, and the vehicle only ever shortens a
                                // walk that was going to happen anyway.
                                if (this.farmWalkToAreaEnabled
                                    && this.TryBeginFarmWalkToArea(farmLocation.Position, farmLocation.Name))
                                {
                                    this.autoFarmStatus = "Travelling to " + farmLocation.Name + "...";
                                    this.farmState = HeartopiaComplete.AutoFarmState.WalkingToNode;
                                    this.autoFarmTimer = 0f;
                                    break;
                                }

                                this.autoFarmStatus = "Moving to " + farmLocation.Name + "...";
                                this.FarmTeleportTo(this.ApplyForagingAreaTeleportOffset(farmLocation.Position),
                                    "area:" + farmLocation.Name, farmLocation.Position);
                                this.farmState = HeartopiaComplete.AutoFarmState.LoadingArea;
                                this.autoFarmTimer = 0f;
                            }
                        }
                        break;
                    }
                case HeartopiaComplete.AutoFarmState.LoadingArea:
                    {
                        bool flag20 = this.autoFarmTimer >= this.areaLoadDelay;
                        if (flag20)
                        {
                            this.farmState = HeartopiaComplete.AutoFarmState.WaitingForNodes;
                            this.autoFarmTimer = 0f;
                        }
                        else
                        {
                            this.autoFarmStatus = $"Loading area... ({this.areaLoadDelay - this.autoFarmTimer:F1}s remaining)";
                        }
                        break;
                    }
                case HeartopiaComplete.AutoFarmState.WaitingForNodes:
                    {
                        // Auto Repair coordination: hold the node hop while a repair runs.
                        if (this.IsAutoRepairBusy())
                        {
                            this.autoFarmStatus = "Paused for Auto Repair...";
                            this.autoFarmTimer = 0f;
                            break;
                        }

                        // Corrupted debuff: cleanse before hopping to the next node.
                        if (this.TryBeginCorruptionCleanse())
                        {
                            break;
                        }

                        // In walk mode the throttle must NOT gate this branch. It exists to space
                        // out teleports, and walking is not a teleport — gating here meant every
                        // area relocation was followed by the configured delay of standing still
                        // before the walker was allowed to even look at the next node. The one
                        // teleport this branch can still perform is guarded on its own line below.
                        bool waitThrottled = this.IsFarmTeleportThrottled(out float tpCooldownWait);
                        if (!this.farmWalkToNodeEnabled && waitThrottled)
                        {
                            this.autoFarmStatus = $"Teleport cooldown... ({tpCooldownWait:F1}s)";
                            break;
                        }

                        // Same tour + walk path as ScanningForNodes. This branch used to teleport
                        // unconditionally, which is what put two hops back to back in the log:
                        // area:Oyster Spawn landed at (-139.8, 21.3, 205.2) and eight seconds later
                        // node:Oyster warped 7.7 m to (-132.2, 22.8, 203.6) — a distance the walker
                        // covers in three seconds. An area relocation always ends here, so every
                        // single relocation was costing a second, pointless teleport.
                        Vector3? vector2;
                        string waitingNodeLabel;
                        if (this.farmWalkToNodeEnabled)
                        {
                            Vector3 waitOrigin = this.ResolveFarmTourOrigin();
                            this.TopUpFarmTour(waitOrigin, 0);
                            vector2 = this.TryGetNextFarmTourStop(waitOrigin, out Vector3 waitStop, out waitingNodeLabel)
                                ? new Vector3?(waitStop)
                                : null;
                        }
                        else
                        {
                            vector2 = this.FindClosestAvailableNode(out waitingNodeLabel);
                        }

                        bool flag21 = vector2 != null;
                        if (flag21)
                        {
                            float value2 = Vector3.Distance(Camera.main.transform.position, vector2.Value);
                            if (this.TryBeginFarmWalk(vector2.Value, "node:" + (waitingNodeLabel ?? "unlabelled"),
                                    false, waitingNodeLabel))
                            {
                                this.NoteFarmWalkRouteSucceeded();
                                this.lastNodePosition = vector2.Value;
                                this.lastTeleportWasPriorityLocation = false;
                                this.farmState = HeartopiaComplete.AutoFarmState.WalkingToNode;
                                this.autoFarmTimer = 0f;
                                break;
                            }

                            // Unroutable: take another node rather than warping to this one.
                            if (this.farmWalkToNodeEnabled
                                && this.TryDeferUnroutableFarmNode(vector2.Value, waitingNodeLabel))
                            {
                                this.autoFarmStatus = "No route there — trying another node...";
                                this.autoFarmTimer = 0f;
                                break;
                            }

                            // Walking did not start, so this really is a teleport — and now the
                            // throttle applies, exactly as it did before walk mode bypassed the
                            // branch gate above.
                            if (waitThrottled)
                            {
                                this.autoFarmStatus = $"Teleport cooldown... ({tpCooldownWait:F1}s)";
                                break;
                            }

                            this.autoFarmStatus = $"Node found! Teleporting ({value2:F0}m)...";
                            this.FarmTeleportTo(this.ApplyForagingNodeTeleportOffset(vector2.Value, waitingNodeLabel),
                                "node:" + (waitingNodeLabel ?? "unlabelled"), vector2.Value);
                            this.lastNodePosition = vector2.Value;
                            this.farmState = HeartopiaComplete.AutoFarmState.Collecting;
                            this.autoFarmTimer = 0f;
                            this.autoCollectClickedSinceArrival = false;
                            this.cameraRotationAttempts = 0;
                            this.BeginFarmNodeDwell(waitingNodeLabel);
                        }
                        else
                        {
                            bool flag22 = this.autoFarmTimer >= 5f;
                            if (flag22)
                            {
                                this.autoFarmStatus = "No nodes found, cycling...";
                                this.farmState = HeartopiaComplete.AutoFarmState.MovingToLocation;
                                this.autoFarmTimer = 0f;
                            }
                            else
                            {
                                this.autoFarmStatus = $"Scanning for nodes... ({5f - this.autoFarmTimer:F1}s)";
                            }
                        }
                        break;
                    }
                case HeartopiaComplete.AutoFarmState.WaitingForPriorityArea:
                    {
                        bool flag23 = this.autoFarmTimer >= this.areaLoadDelay;
                        if (flag23)
                        {
                            // Start collecting at priority location. lastNodePosition still points at a
                            // previous node here, so the radar-confirm wait must stay disarmed.
                            this.farmState = HeartopiaComplete.AutoFarmState.Collecting;
                            this.autoFarmTimer = 0f;
                            this.autoCollectClickedSinceArrival = false;
                            this.cameraRotationAttempts = 0;
                            this.ResetContaminationDwellState(); // priority areas are plant dwells
                            this.ArmAuraCollectWait(false);
                            this.autoFarmStatus = "Farming at priority location...";
                        }
                        else
                        {
                            this.autoFarmStatus = $"Loading priority area... ({this.areaLoadDelay - this.autoFarmTimer:F1}s remaining)";
                        }
                        break;
                    }
                case HeartopiaComplete.AutoFarmState.CleansingCorruption:
                    {
                        // Corrupted debuff: hold inside a cleansing-coral area until buff 610
                        // clears (CorruptionCleanseFeature.cs), then resume scanning.
                        this.RunCorruptionCleanseWait();
                        break;
                    }
            }
        }

        // ---- Contamination (sea-clean) farm dwell ------------------------------------------
        // The Aura Farm travels to "Contaminated" radar markers like any other node; the
        // Collecting dwell then runs the shared sea-clean sweep (SeaCleanQteFeature pass)
        // instead of the aura pick wait. All cross-frame state below is scalars — no
        // coroutines, no raw mono pointers held across frames.
        private bool autoFarmTargetIsContamination = false;
        // Bubble targets get their own dwell completion: the aura cannot collect bubbles (touch /
        // AutoBubbleCollect territory), so no aura confirmation ever fires for them.
        private bool autoFarmTargetIsBubble = false;
        private int contaminationZeroPassCount = 0;
        private int contaminationKillsThisNode = 0;
        private float contaminationLastConsumedPassAt = 0f;
        private float contaminationRepairHoldSince = -1f;
        private float contaminationNextToolCheckAt = 0f;
        private bool contaminationToolReady = false;
        private bool contaminationToolDepleted = false;
        private string contaminationToolStatus = string.Empty;

        // Global minimum interval between Aura Farm teleports (node / area / priority hops), user-set
        // 0-10s in Foraging Settings (0 = off). Real-time (unscaled) so 5x game speed doesn't shrink
        // it. Prevents the farm from teleporting too frequently.
        private float foragingTeleportDelaySeconds = 0f;
        private float lastFarmTeleportAt = -999f;

        // When the farm last ARRIVED at a node — a walk arrival or a node teleport. Distinct from
        // lastFarmTeleportAt, which only moves on teleports and therefore says nothing at all in
        // walk mode. Zero means "no node touched yet this run", which must not block anything.
        private float lastFarmNodeActivityAt;

        // Visited-node stamp durations. A node PROVEN cold is blocked for its REAL remaining
        // cooldown when a server end time is known (CollectColdEvent.endUnixTimeMs for the node we
        // just drained, or the live scan's coldEndTime) — cooldowns differ per resource
        // (MapResourceProduce totalData: trees/bushes 120s, stones/ore 300s, rare tree & daily
        // rocks/meteors 86400s), so no single constant fits. When the node is proven cold but no
        // end time is readable, fall back to the common 120s; ambiguous outcomes (timeout with no
        // cooldown evidence — usually world streaming) keep the short retry stamp so a slow-loading
        // node isn't lost for minutes. The old flat 15s expired faster than a 2-3-dead-node loop
        // takes, so the farm circled the same depleted trees/bushes indefinitely.
        private const float FarmVisitedRetryStampSeconds = 15f;
        // How old a visited stamp must be before the warm-purge may overrule it. Longer than the
        // retry stamp on purpose: a retry stamp expires on its own and never needs correcting.
        private const float FarmVisitedPurgeMinAge = 30f;
        private const float FarmVisitedColdStampFallbackSeconds = 120f;
        // Upper bound for the visited stamp.
        //
        // ⚠️ THIS WAS RAISED TO 8 h ON A WRONG READING AND IS BACK TO TEN MINUTES. The reasoning was
        // "a picked mushroom's coldEndTime sits ~25 300 s out, so a ten-minute cap guarantees a
        // revisit loop". Both halves were wrong: those 25 300 s were a LEFTOVER value the game's own
        // `inCold` ignores, and a picked mushroom has no cooldown at all — it is removed from the
        // world (measured over five hand-picks and five farm collects, 2026-08-19, every one
        // `removed from the world`).
        //
        // So the original reason for a cap stands unchanged: recentlyVisitedNodes is a BACKSTOP, it
        // is only ever corrected by TIME, and a bad end-time read must not be able to park a node
        // for hours. The live warm-purge in SyncLiveResourceColdStates releases it earlier anyway.
        private const float FarmVisitedColdStampMaxSeconds = 600f;

        // Real cooldown end (unix ms) of the node being collected, captured from the drain-end
        // CollectColdEvent (endMs > now). 0 = unknown (event-forage families drain with endMs=0).
        private long auraCollectNodeColdEndMs;

        // Remaining-cooldown stamp: real end + 2s grace when known, else the 120s fallback. Clamped
        // to [retry, 10min]: long (daily) cooldowns are carried by the label cooldown dicts (which
        // live corrections CAN shorten); this per-position backstop stays bounded so a bad end-time
        // read can't park a node for hours (see FarmVisitedColdStampMaxSeconds).
        // THE only way a visited stamp is written. It exists so the WHEN is recorded alongside the
        // UNTIL: the warm-purge in SyncLiveResourceColdStates needs to tell a stamp that describes
        // what the farm just did from one that has gone stale, and an expiry alone cannot.
        private void StampVisitedNode(Vector3 node, float expiresAt)
        {
            this.StampVisitedNode(node, expiresAt, approachFailure: false);
        }

        // approachFailure: the stamp exists because the node COULD NOT BE REACHED (no route, a
        // repeated refusal, a skipped approach). Those are cleared when a run starts; resource
        // cooldowns and facts about the world are not.
        private void StampVisitedNode(Vector3 node, float expiresAt, bool approachFailure)
        {
            // ⚠️ A NODE THE GAME SAYS IS COLD IS NEVER PARKED FOR LESS THAN ITS COOLDOWN.
            //
            // Every caller decides a duration from what IT knows, and several of them know nothing:
            // the dwell timeout and the walk skip both fall back to the 15 s retry stamp. On a
            // mushroom that is cold for seven hours that is a revisit every fifteen seconds, which
            // is exactly what the 2026-08-19 probe caught — stamp expiring in 9 s on an entity with
            // 25 307 s of cooldown left. Rather than repair each caller (and miss the next one), the
            // floor is applied here, at the single point every stamp goes through.
            //
            // MAX, never overwrite: park and repeat-offender stamps are deliberately long and must
            // not be shortened to a cooldown that happens to be nearer.
            if (this.TryGetLiveNodeColdState(node, 0f, out bool nodeCold, out long nodeColdEndMs) && nodeCold)
            {
                float coldFloor = Time.unscaledTime + this.GetVisitedColdStampSeconds(nodeColdEndMs);
                if (coldFloor > expiresAt)
                {
                    expiresAt = coldFloor;
                }
            }

            this.recentlyVisitedNodes[node] = expiresAt;
            this.visitedNodeStampedAt[node] = Time.unscaledTime;
            if (approachFailure)
            {
                this.approachFailureStamps.Add(node);
            }
            else
            {
                // A cooldown on top of a ban: the node is no longer "unreachable", it is merely cold.
                this.approachFailureStamps.Remove(node);
            }
        }

        private void ForgetVisitedNode(Vector3 node)
        {
            this.recentlyVisitedNodes.Remove(node);
            this.visitedNodeStampedAt.Remove(node);
            this.approachFailureStamps.Remove(node);
        }

        // A run reset clears ONLY the unreachable bans. A mushroom's cooldown knows nothing about
        // the player pressing Stop, and wiping it sends the farm walking out to check on what it
        // harvested itself a moment ago.
        private void ClearApproachFailureStamps()
        {
            if (this.approachFailureStamps.Count == 0)
            {
                return;
            }

            foreach (Vector3 node in this.approachFailureStamps)
            {
                this.recentlyVisitedNodes.Remove(node);
                this.visitedNodeStampedAt.Remove(node);
            }

            ModLogger.Msg("[FarmWalk] cleared " + this.approachFailureStamps.Count
                + " unreachable-node ban(s); resource cooldowns kept.");
            this.approachFailureStamps.Clear();
        }

        private float GetVisitedColdStampSeconds(long coldEndUnixMs)
        {
            long nowUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (coldEndUnixMs > nowUnixMs)
            {
                return Mathf.Clamp((float)((coldEndUnixMs - nowUnixMs) / 1000.0) + 2f,
                    FarmVisitedRetryStampSeconds, FarmVisitedColdStampMaxSeconds);
            }
            return FarmVisitedColdStampFallbackSeconds;
        }

        // Sea-clean teleport lift: contamination nodes and cleansing corals sit on the sea floor —
        // teleporting straight onto the point lands inside the geometry. Arrive 7m above instead
        // (underwater = swimming, so the height is free). Applied ONLY to the teleport argument —
        // lastNodePosition keeps the true node position for marker matching and dwell checks.
        private const float SeaCleanTeleportYOffset = 7f;

        // Contamination splits into two anchor classes, and they need OPPOSITE adjustments — see
        // TryGetContaminatedAnchorClass (SeaCleanQteFeature.cs) for how they are told apart:
        //   * HOSTED (permanent, stuck to a coral growing on the ground) -> dive -3m like a node.
        //   * POINT  (temporary, spawned at a sub-area point, floating in open water) -> +4m.
        //     Diving under one of these puts the player in open water below the pollutant, which is
        //     where the "player pops back to the surface" reports come from; arriving above it
        //     keeps the hop inside the pollutant's own volume.
        // Class unknown (not in the radar index this pass) falls back to the hosted dive — the
        // conservative choice, and the same direction every other stealth hop takes.
        private const float StealthForagingContaminationPointLift = 4f;

        // Y adjustment for a RESOURCE-NODE hop. Deliberately NOT applied to world-load
        // checkpoints (farm-location waypoints, priority-area anchors, cleansing corals) — those
        // are area arrivals that must land where the world streams in normally.
        //   * Stealth Foraging engaged -> contamination -3m hosted / +4m point-anchored, every
        //     other resource -1.5m (StealthForagingFeature.cs).
        //   * Otherwise -> NO Y adjustment at all. Contamination used to take the vanilla
        //     SeaCleanTeleportYOffset (+7m) lift here; that lift now lives only on the
        //     cleansing-coral hop (CorruptionCleanseFeature.cs), which is a different target.
        private Vector3 ApplyForagingNodeTeleportOffset(Vector3 position, string nodeLabel)
        {
            if (!this.StealthForagingActive)
            {
                return position;
            }

            if (string.Equals(nodeLabel, "Contaminated", StringComparison.Ordinal))
            {
                this.TryGetContaminatedAnchorClass(position, out bool hosted);
                position.y += hosted
                    ? -StealthForagingContaminationDepth
                    : StealthForagingContaminationPointLift;
            }
            else
            {
                position.y -= StealthForagingNodeDepth;
            }

            return position;
        }

        // ---- Foraging teleport trace (MasterLogForagingTeleport) -------------------------------
        // Investigating "at some points the player surfaces above ground/floor". Logging only the
        // teleport ARGUMENT would prove nothing — the offset is deterministic. What matters is
        // where the player actually ends up, so each hop also takes two delayed samples of the live
        // player position: one right after the warp settles and one later, which separates "the
        // warp landed high" from "the warp landed right and then the player drifted up".
        private string foragingTpKind = string.Empty;
        private Vector3 foragingTpSource;
        private Vector3 foragingTpRequested;
        private float foragingTpSampleAt1 = -1f;
        private float foragingTpSampleAt2 = -1f;

        private const float ForagingTpSampleDelay1 = 0.6f;
        private const float ForagingTpSampleDelay2 = 2.5f;

        private static string FormatForagingTpVector(Vector3 v)
            => v.x.ToString("F3") + ", " + v.y.ToString("F3") + ", " + v.z.ToString("F3");

        // Every Aura Farm hop goes through this wrapper so the throttle clock is stamped uniformly;
        // IsFarmTeleportThrottled() below paces the teleport-initiating states off it.
        //
        // kind   — what we are hopping to ("node:Contaminated", "area:Sea Area 4", ...).
        // source — the TRUE resource/area position, before any Stealth Foraging Y offset. The
        //          offset only ever goes into `position`, so this pair is what the trace compares.
        private void FarmTeleportTo(Vector3 position, string kind = "unknown", Vector3 source = default)
        {
            // Walk-to-node mode, step 0 probes. Purely observational — the hop below is unchanged.
            // The navmesh one (NavMeshWalkFeature.cs) has already returned a firm NO; it is kept
            // only so a future game update that wires XDNavigationMgr up would show itself.
            // The live question is the Track waypoint graph (TrackPathGraphFeature.cs).
            this.ProbeTrackPathGraph();
            this.ProbeNavMeshRoute(source == default ? position : source, kind);

            this.lastFarmTeleportAt = Time.unscaledTime;

            // A node hop IS an arrival at a resource, same as a walk finishing — it starts the
            // "let the aura finish" window that MovingToLocation waits on.
            if (kind != null && kind.StartsWith("node:"))
            {
                this.lastFarmNodeActivityAt = Time.unscaledTime;
            }

            // Any area:* hop is a relocation to a different spawn, so the planned tour describes a
            // place we are no longer in. Dropping it here — at the single chokepoint every farm
            // teleport goes through — is the only way to be sure no relocation path is missed.
            // Node hops deliberately KEEP the tour: the plan is exactly what they are following.
            if (kind != null && kind.StartsWith("area:"))
            {
                this.ResetFarmTour();
            }

            this.LogForagingTeleportRequest(kind, source, position);
            this.TeleportToLocation(position);
            // Stealth Foraging: hold the hover exactly where the hop aimed (the warp clears the
            // noclip hold, which would otherwise re-seed from a surface-snapped arrival).
            this.PinStealthForagingNoclipHold(position);
        }

        private void LogForagingTeleportRequest(string kind, Vector3 source, Vector3 requested)
        {
            // EVERY farm teleport gets a one-line record, unconditionally. The detailed arrival
            // sampling below stays behind MasterLogForagingTeleport, but "the farm just warped and
            // nothing says why" is not a debuggable state — with the flag off, a log could show a
            // clean run of walks while the player was actually being teleported around.
            if (!MasterLogForagingTeleport)
            {
                this.foragingTpSampleAt1 = -1f;
                this.foragingTpSampleAt2 = -1f;
                ModLogger.Msg("[FarmTeleport] " + (string.IsNullOrEmpty(kind) ? "unknown" : kind)
                    + " -> (" + FormatForagingTpVector(requested) + ")");
                return;
            }

            this.foragingTpKind = string.IsNullOrEmpty(kind) ? "unknown" : kind;
            // Spell out which contamination class drove the offset, so a surfacing report in the
            // log identifies itself without cross-referencing the [ForagingTp] contaminated lines.
            if (this.foragingTpKind.EndsWith("Contaminated", StringComparison.Ordinal))
            {
                this.foragingTpKind += this.TryGetContaminatedAnchorClass(source, out bool hostedAnchor)
                    ? (hostedAnchor ? "/hosted" : "/point")
                    : "/unknown-anchor";
            }
            this.foragingTpSource = source;
            this.foragingTpRequested = requested;

            float now = Time.unscaledTime;
            this.foragingTpSampleAt1 = now + ForagingTpSampleDelay1;
            this.foragingTpSampleAt2 = now + ForagingTpSampleDelay2;

            ModLogger.Msg("[ForagingTp] " + this.foragingTpKind
                + " src=(" + FormatForagingTpVector(source) + ")"
                + " tp=(" + FormatForagingTpVector(requested) + ")"
                + " dy=" + (requested.y - source.y).ToString("F3")
                + " stealth=" + (this.StealthForagingActive ? "on" : "off")
                + " noclip=" + (this.noclipEnabled ? "on" : "off"));
        }

        // Drained every frame from OnUpdate (cheap: two float compares when idle).
        private void ProcessForagingTeleportTraceOnUpdate()
        {
            if (this.foragingTpSampleAt1 < 0f && this.foragingTpSampleAt2 < 0f)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (this.foragingTpSampleAt1 >= 0f && now >= this.foragingTpSampleAt1)
            {
                this.foragingTpSampleAt1 = -1f;
                this.SampleForagingTeleportArrival("t+" + ForagingTpSampleDelay1.ToString("0.0") + "s");
            }

            if (this.foragingTpSampleAt2 >= 0f && now >= this.foragingTpSampleAt2)
            {
                this.foragingTpSampleAt2 = -1f;
                this.SampleForagingTeleportArrival("t+" + ForagingTpSampleDelay2.ToString("0.0") + "s");
            }
        }

        private void SampleForagingTeleportArrival(string stamp)
        {
            if (!MasterLogForagingTeleport)
            {
                return;
            }

            if (!this.TryGetLocalPlayerPosition(out Vector3 actual))
            {
                ModLogger.Msg("[ForagingTp]   " + stamp + " " + this.foragingTpKind + " player position unavailable");
                return;
            }

            // vsTp  > 0 -> the player is ABOVE where we asked to land (the surfacing bug).
            // vsSrc > 0 -> the player is above the resource itself, i.e. fully surfaced.
            ModLogger.Msg("[ForagingTp]   " + stamp + " " + this.foragingTpKind
                + " at=(" + FormatForagingTpVector(actual) + ")"
                + " vsTp=" + (actual.y - this.foragingTpRequested.y).ToString("F3")
                + " vsSrc=" + (actual.y - this.foragingTpSource.y).ToString("F3")
                + " state=" + this.farmState
                + " noclip=" + (this.noclipEnabled ? "on" : "off"));
        }

        // True while the configured teleport delay hasn't elapsed since the last farm teleport.
        private bool IsFarmTeleportThrottled(out float remaining)
        {
            remaining = 0f;
            float delay = this.foragingTeleportDelaySeconds;
            if (delay <= 0f)
            {
                return false;
            }
            remaining = delay - (Time.unscaledTime - this.lastFarmTeleportAt);
            return remaining > 0f;
        }

        // Clears all contamination-dwell bookkeeping (farm toggle on/off + every node hop).
        private void ResetContaminationDwellState()
        {
            this.autoFarmTargetIsContamination = false;
            this.autoFarmTargetIsBubble = false;
            this.contaminationZeroPassCount = 0;
            this.contaminationKillsThisNode = 0;
            this.contaminationLastConsumedPassAt = Time.unscaledTime;
            this.contaminationRepairHoldSince = -1f;
            this.contaminationNextToolCheckAt = 0f;
            this.contaminationToolReady = false;
            this.contaminationToolDepleted = false;
            this.contaminationToolStatus = string.Empty;
        }

        // Starts the Collecting dwell for a freshly targeted radar node: "Contaminated" markers
        // get the sea-clean sweep dwell (RunContaminationCleanWait), everything else the normal
        // aura collect wait.
        private void BeginFarmNodeDwell(string nodeLabel)
        {
            this.ResetContaminationDwellState();
            bool contamination = string.Equals(nodeLabel, "Contaminated", StringComparison.Ordinal);
            this.autoFarmTargetIsContamination = contamination;
            this.autoFarmTargetIsBubble = string.Equals(nodeLabel, "Bubble", StringComparison.Ordinal);
            if (contamination)
            {
                // Ignore sweep passes completed before (or immediately after) arrival — the
                // reported player position may not have settled yet right after the teleport,
                // so an early zero-actionable pass could belong to the OLD position.
                this.contaminationLastConsumedPassAt = Time.unscaledTime + 0.5f;
            }
            this.ArmAuraCollectWait(!contamination);
        }

        // Contamination-node Collecting dwell (Aura Farm x Auto Sea Clean): drive the shared
        // sweep pass at this node until nothing killable is left in range, then hop. The sweep
        // and its throttles (0.5s scan interval + kill pacing) are shared with the standalone
        // Auto Sea Clean tick — whichever caller runs a pass, the results land in
        // seaCleanLastPass* and are consumed here exactly once.
        private void RunContaminationCleanWait()
        {
            float now = Time.unscaledTime;
            float maxWait = Mathf.Max(6f, this.auraCollectWaitTimeout);

            // Types not ready — bounded wait (EnsureSeaCleanQteAuraResolved retries every 2s).
            if (!this.EnsureSeaCleanQteAuraResolved(out _)
                || this.seaCleanQteMonsterClass == IntPtr.Zero
                || this.seaCleanQteExecuteKillMethod == IntPtr.Zero)
            {
                if (this.autoFarmTimer >= maxWait)
                {
                    this.FinishContaminationCleanDwell(now, "types unavailable");
                    return;
                }

                this.autoFarmStatus = $"Cleaning... waiting for game types ({maxWait - this.autoFarmTimer:F0}s)";
                return;
            }

            // Farm-owned tool gate (allowSwap variant — the standalone no-yank rule is
            // untouched). The AuraMono tool read runs at 4Hz, cached in scalars between checks.
            if (now >= this.contaminationNextToolCheckAt)
            {
                this.contaminationNextToolCheckAt = now + 0.25f;
                this.contaminationToolReady = this.TrySeaCleanFarmEnsureCleanerEquipped(now, out bool cleanerDepleted, out string toolStatus);
                this.contaminationToolDepleted = cleanerDepleted;
                this.contaminationToolStatus = toolStatus;
            }

            if (!this.contaminationToolReady && this.contaminationToolDepleted)
            {
                if (this.IsAutoRepairBusy())
                {
                    // A repair for the depleted cleaner is running/queued: hold this dwell (and
                    // suspend its timeout) so the restorer can land, bounded so a wedged repair
                    // can never pin the farm here.
                    if (this.contaminationRepairHoldSince < 0f)
                    {
                        this.contaminationRepairHoldSince = now;
                    }

                    if (now - this.contaminationRepairHoldSince <= 45f)
                    {
                        this.autoFarmTimer = 0f;

                        // The kit was thrown, but the restorer is an ENTITY resting on the sea
                        // floor and its repair aura is a SPHERE around it. Floating where the
                        // throw happened leaves the player outside that sphere, so the repair is
                        // approved and then never actually starts. Sink onto the kit and stay
                        // there until the restore buff is running.
                        this.autoFarmStatus = this.TryHoldDescentIntoRepairAura()
                            ? "Cleaning... sinking into the repair aura"
                            : "Cleaning... waiting for sea cleaner repair";
                        return;
                    }
                }

                // Underwater the throw can be silently refused: the restorer is an ENTITY the
                // server places on ground, and from open water there may be nothing to place it
                // on, so CanPutRestorerResult never approves and no ToolRestorerEvent arrives.
                // IsAutoRepairBusy() then reads false and the dwell used to just give up.
                // Instead: sink toward the floor and throw again, until the repair truly starts.
                if (this.TryRetryContaminationRepairThrow(now))
                {
                    this.autoFarmTimer = 0f;
                    return;
                }

                this.autoFarmStatus = "Sea cleaner depleted - repair needed";
                this.FinishContaminationCleanDwell(now, "cleaner depleted");
                return;
            }
            this.contaminationRepairHoldSince = -1f;
            this.ResetContaminationRepairRetryState();

            // Run the shared sweep (self-throttled). It runs even while the equip is still
            // pending: kills stay blocked by the pass's own tool gate, but actionable counts
            // keep flowing so a node with nothing killable (shared/public only) hops early.
            this.TrySeaCleanAutoCleanPass(out _, out _, out _, out _);

            // Consume every completed pass exactly once, no matter which caller ran it.
            if (this.seaCleanLastPassCompletedAt > this.contaminationLastConsumedPassAt)
            {
                this.contaminationLastConsumedPassAt = this.seaCleanLastPassCompletedAt;
                this.contaminationKillsThisNode += this.seaCleanLastPassKilled;
                if (this.seaCleanLastPassKilled == 0 && this.seaCleanLastPassActionable == 0)
                {
                    this.contaminationZeroPassCount++;
                }
                else
                {
                    this.contaminationZeroPassCount = 0;
                }
            }

            // Done: two consecutive passes with nothing killable and nothing killed.
            if (this.contaminationZeroPassCount >= 2 && this.autoFarmTimer >= 1f)
            {
                this.FinishContaminationCleanDwell(now, "area clear");
                return;
            }

            // One unreachable/wedged node must not stall the loop forever.
            if (this.autoFarmTimer >= maxWait)
            {
                this.FinishContaminationCleanDwell(now, "timeout");
                return;
            }

            float remaining = maxWait - this.autoFarmTimer;
            if (!this.contaminationToolReady)
            {
                this.autoFarmStatus = $"Cleaning... {this.contaminationToolStatus} ({remaining:F0}s)";
            }
            else if (this.contaminationKillsThisNode > 0)
            {
                this.autoFarmStatus = $"Cleaning... {this.contaminationKillsThisNode} cleaned ({remaining:F0}s)";
            }
            else if (this.contaminationZeroPassCount > 0 && this.seaCleanLastPassNoLever > 0)
            {
                this.autoFarmStatus = $"Cleaning... only shared pollutants here ({remaining:F0}s)";
            }
            else
            {
                this.autoFarmStatus = $"Cleaning... sweeping pollutants ({remaining:F0}s)";
            }
        }

        // Finish the contamination dwell: stamp the node (15s when something was actually
        // cleaned here, 60s when nothing was killable — shared/public markers stay on the
        // radar and must not ping-pong the farm) and hop via the normal cycle finish.
        private void FinishContaminationCleanDwell(float now, string reason)
        {
            float stampSeconds = this.contaminationKillsThisNode > 0 ? 15f : 60f;
            this.StampVisitedNode(this.lastNodePosition, now + stampSeconds);
            this.AutoFarmLog($"Contamination dwell done at {this.lastNodePosition} (kills={this.contaminationKillsThisNode}, reason={reason}, stamp={stampSeconds:F0}s)");
            this.FinishCollectingCycle();
        }

        // Aura-mode Collecting: hold the hop to the next radar target until this node is
        // actually collected. After a long teleport the resource entity streams in late, so
        // the old fixed 3s dwell hopped away before the aura ever saw the target. Completion
        // is read from the radar itself: a collected node's marker is hidden by the cooldown
        // stamp (~10s) and later shown as [CD], while a still-loading node keeps (or regains)
        // an available marker. The aura-idle window keeps us in place while a tree is still
        // being chopped or a cluster around the node is still being swept.
        // Arms/disarms the radar-confirm wait for the next Collecting dwell and resets the
        // per-node entity tracking captured by TryCaptureAuraCollectNodeOwner.
        private void ArmAuraCollectWait(bool armed)
        {
            this.auraCollectWaitArmed = armed;
            this.auraCollectNodeOwnerNetId = 0U;
            this.auraCollectNodeResourceNetId = 0U;
            this.auraCollectNodeColdEndMs = 0L;
            this.auraCollectNodeEntitySeen = false;
            this.auraCollectNodeConfirmedAt = -1f;
            this.auraNextCollectNodeProbeAt = 0f;
            this.auraCollectNodeDiagLogged = false;
            this.auraCollectCaptureMissedOwners.Clear();
            this.auraCollectNodeAbsentTicks = 0;
            this.auraCollectNodeSeenPresentAt = -1f;
            this.auraCollectSeenAvailByNetId.Clear();
            this.auraCollectOurNetIds.Clear();
            this.auraCollectLastBackpackAt = -1f;
            this.auraCollectNodeCapturedAt = -1f;
            if (armed)
            {
                this.EnsureAuraCollectColdEventHook();
            }
        }

        // EventCenter hooks: CollectColdEvent fires the instant a collectable flips to cooldown
        // (the exact moment the in-game interact icon disappears) and carries the resource netId
        // the pick command targeted — the only build-independent, per-resource collect signal
        // (managed XDT* entity resolution is dead on this build, and cold bush shapes stay in
        // the axe-checker). CollectObjectShowEvent covers despawn-style objects.
        // ⚠️ HOOK THIS AT WORLD-READY, NOT WHEN THE FARM ARMS — the difference decides whether the
        // farm can answer "is that node worth walking to" at all.
        //
        // TrackModule.OnCreate() calls IDynamicMapItemService.UpdateAllColdTime() once at startup,
        // which walks EVERY map-resource point, computes its verdict (for a dynamic bush, from
        // DynamicBushGrowComponent.MaturityTime) and broadcasts one CollectColdEvent per resource.
        // That single sweep is the only moment the client volunteers a verdict for resources nobody
        // has touched — afterwards events fire only on CHANGE, which is why a probe that subscribed
        // late measured verdicts for 6 of 59 nearby objects.
        //
        // The verdicts do not go stale: endUnixTimeMs is an ABSOLUTE instant, so "not ready until T"
        // stays true however long ago it was heard.
        internal bool RegisterCollectColdHookOnWorldReady()
        {
            // The clear guards against a netId being reused across worlds. "netIds are
            // per-session" turned out to be too strong, though: a static entity's netId held
            // across a full game RESTART (21118, its cooldown down by exactly the elapsed wall
            // clock) — which is what makes reseeding long cooldowns from disk sound
            // (ColdLedgerPersistFeature.cs).
            this.collectColdByNetId.Clear();
            this.SeedPersistedColdLedger();
            this.EnsureAuraCollectColdEventHook();
            return true;
        }

        private bool collectColdWorldReadyRegistered;

        // ⚠️ REGISTER THE HOOK EAGERLY, CLEAR THE LEDGER ON THE GATE — two different timings, and
        // getting them the same way costs the startup sweep.
        //
        // Registering an event hook is METADATA ONLY and is safe at any time; it is the INSTALL that
        // needs a live world, and ProcessGameEventHooksOnUpdate already performs it on the first
        // tick after IsWorldReady. Doing the registration from a world-ready CALLBACK instead put it
        // behind the gate's deferred-warmup phase — measured 2026-08-19: world ready at 04:02:16,
        // hook installed at 04:02:18, and TrackModule's one-shot UpdateAllColdTime sweep had already
        // gone by, leaving the ledger empty.
        //
        // The documented pre-world hazard is about INFLATING DispatchEvent<T> before a world exists
        // ([[eventhook-preworld-inflate-abort]]); it does not apply to registration, and the install
        // path keeps its own IsWorldReady gate.
        private void EnsureCollectColdRegistrations()
        {
            if (this.collectColdWorldReadyRegistered)
            {
                return;
            }

            this.collectColdWorldReadyRegistered = true;
            this.EnsureAuraCollectColdEventHook();
            this.RegisterWorldReadyCallback("CollectColdLedger", this.RegisterCollectColdHookOnWorldReady);
        }

        // Driven from the same place the cold sync already runs, so it costs nothing when neither
        // the radar nor the farm is on; its own 30 s throttle keeps the sweep rare.
        private void ProcessCollectColdSweepOnUpdate()
        {
            if (!this.isRadarActive && !this.autoFarmActive)
            {
                return;
            }

            float now = Time.unscaledTime;

            // Any visible resource the ledger has never heard of means a new entity appeared — the
            // regrowth case above. Pull the sweep forward instead of waiting out the interval.
            if (now >= this.collectColdSweepEarliestAt && this.SnapshotHasUnknownNetId())
            {
                this.collectColdSweepEarliestAt = now + CollectColdSweepMinGap;
                this.collectColdNextSweepAt = 0f;
            }

            this.TrySweepCollectColdLedger(now);

            if (this.collectColdCoveragePendingAt >= 0f && now >= this.collectColdCoveragePendingAt)
            {
                this.collectColdCoveragePendingAt = -1f;
                this.LogCollectColdCoverage();
            }
        }

        // ── Ask the client to publish a verdict for EVERY map resource ───────────────────────────
        //
        // The ledger above is only as good as its coverage, and events alone do not provide it: the
        // client volunteers a CollectColdEvent when a resource CHANGES, plus one sweep at startup
        // (TrackModule.OnCreate -> UpdateAllColdTime) that lands before any hook of ours can be
        // installed — measured, world ready 04:06:20, hook live 04:06:21, sweep already gone. A probe
        // subscribing afterwards saw verdicts for 6 of 59 nearby objects.
        //
        // So we ask for the sweep ourselves. IDynamicMapItemService.UpdateAllColdTime() walks every
        // map-resource point, computes each verdict (for a dynamic bush, from
        // DynamicBushGrowComponent.MaturityTime — the same number that draws the growth ring the
        // player sees) and broadcasts one event per resource. Measured in-game: one call produced
        // 153 events and took the ledger from 2 to 66 entries, with verdicts for objects 10 m away
        // that nobody had approached.
        //
        // ⚠️ WHY Get<T> AND NOT TryGet<T>. Both are generic and both need the same inflate, but
        // Get<T>(bool) returns a REFERENCE and takes one value argument, while TryGet<T>(out T, bool)
        // has an out parameter — and out parameters through AuraMono are only safe for reference
        // types. Get is the shape that fits the rule instead of testing it.
        //
        // ⚠️ WORLD-READY ONLY. Inflating a generic method before the world exists is the documented
        // abort (mono_metadata_get_generic_inst -> g_assert), so this never runs off the gate.
        // ⚠️ SWEEP ON UNKNOWN NETIDS, NOT ON A CLOCK.
        //
        // A collected resource is REMOVED and a NEW entity grows in its place, with a NEW netId —
        // measured on one spot across a single run: 14215718 -> 3630230 -> 3631959 -> 3696675. So the
        // verdict held for the old netId describes an object that no longer exists, and the fresh
        // one starts life unknown to the ledger. On a 30 s clock that gap is exactly when the farm
        // walks over and finds a mushroom that is still growing.
        //
        // The snapshot already lists every visible netId, so an unknown one is a direct signal that
        // the ledger is behind — sweeping on that closes the gap in about a second instead of thirty.
        // The interval below is now only a floor between sweeps, not the trigger.
        private const float CollectColdSweepInterval = 30f;
        private const float CollectColdSweepMinGap = 3f;
        private float collectColdNextSweepAt;
        private IntPtr collectColdGetInflated = IntPtr.Zero;
        private bool collectColdSweepUnavailable;

        // WALK-SIDE ONLY: may the farm set off towards this node?
        //
        // Separate from TryGetLiveNodeColdState on purpose — see the note there. A dynamic bush
        // (mushroom, event plant) that carries no broadcast verdict is UNCONFIRMED, and unconfirmed
        // is not a reason to walk 20 m: picking one removes the entity and a NEW one grows in its
        // place with a new netId, whose component reads exactly like a ripe one (inCold=False,
        // coldEnd=0, avail=3). The client's verdict is the only thing that can tell them apart.
        //
        // Restricted to that family because the restriction is measured: with the sweep running,
        // coverage was 3/3 on mushrooms but 77 of 117 on trees/stone/berries — those never get a
        // broadcast, because UpdateAllColdTime only computes one where DynamicBushGrowComponent
        // exists. Applying this to them would park half the map for nothing; their component test
        // is sound and stays.
        // Should this resource be drawn at all?
        //
        // The radar used to draw a spent resource with a "_cooldown" mesh, which was right when the
        // only thing that could be wrong with a node was a cooldown. It is not right for a dynamic
        // bush: a mushroom that is GROWING reads inCold=False on its component, so it was drawn as
        // ordinary and available, and both the map and the farm treated it as a destination.
        //
        // Marker visibility therefore asks the same three questions the walk does:
        //   • the component says spent                       -> hide
        //   • the client's verdict says not ready yet        -> hide (this is the growth case)
        //   • dynamic bush with no verdict at all            -> hide, it is UNCONFIRMED
        // The last one hides a bush for the second or two before the sweep answers for it, which is
        // the correct trade: a marker that may be a growing stub is worse than a marker that appears
        // a moment late.
        internal bool IsGatherableHiddenFromMarkers(uint netId, int staticId, bool componentCold)
        {
            if (componentCold)
            {
                return true;
            }

            CollectColdRecord record = default(CollectColdRecord);
            if (netId != 0u && this.collectColdByNetId.TryGetValue(netId, out record))
            {
                return record.EndUnixMs > NowUnixMs();
            }

            return staticId >= 130001 && staticId <= 130025;
        }

        private bool IsFarmTargetUnconfirmed(Vector3 node, out int staticId)
        {
            staticId = 0;
            float bestSqr = 2.25f;
            uint netId = 0u;
            bool found = false;
            for (int i = 0; i < this.liveCollectableColds.Count; i++)
            {
                Vector3 d = this.liveCollectableColds[i].Position - node;
                float sqr = (d.x * d.x) + (d.z * d.z);
                if (sqr >= bestSqr)
                {
                    continue;
                }

                bestSqr = sqr;
                found = true;
                netId = this.liveCollectableColds[i].NetId;
                staticId = this.liveCollectableColds[i].StaticId;
            }

            if (!found || staticId < 130001 || staticId > 130025)
            {
                return false;
            }

            return netId == 0u || !this.collectColdByNetId.ContainsKey(netId);
        }

        private float collectColdSweepEarliestAt;
        private float collectColdCoveragePendingAt = -1f;

        private bool SnapshotHasUnknownNetId()
        {
            for (int i = 0; i < this.liveCollectableColds.Count; i++)
            {
                uint netId = this.liveCollectableColds[i].NetId;
                if (netId != 0u && !this.collectColdByNetId.ContainsKey(netId))
                {
                    return true;
                }
            }

            return false;
        }

        // How many of the resources currently in view carry a verdict. This is the number that says
        // whether "never walk to an unconfirmed node" is affordable, so it is logged rather than
        // assumed — and it is logged only when it CHANGES, since it is stable most of the time.
        private int collectColdLastCoverageLogged = -1;

        private void LogCollectColdCoverage()
        {
            int known = 0, total = 0;
            for (int i = 0; i < this.liveCollectableColds.Count; i++)
            {
                uint netId = this.liveCollectableColds[i].NetId;
                if (netId == 0u)
                {
                    continue;
                }

                total++;
                if (this.collectColdByNetId.ContainsKey(netId))
                {
                    known++;
                }
            }

            int missing = total - known;
            if (missing == this.collectColdLastCoverageLogged)
            {
                return;
            }

            this.collectColdLastCoverageLogged = missing;
            ModLogger.Msg("[CollectCold] verdict coverage " + known + "/" + total
                + " in view (" + missing + " unconfirmed), ledger=" + this.collectColdByNetId.Count);
        }

        private void TrySweepCollectColdLedger(float now)
        {
            if (this.collectColdSweepUnavailable || !this.IsWorldReady || now < this.collectColdNextSweepAt)
            {
                return;
            }

            this.collectColdNextSweepAt = now + CollectColdSweepInterval;

            if (this.collectColdGetInflated == IntPtr.Zero && !this.TryResolveCollectColdSweep())
            {
                return;
            }

            // The service is a managed object: resolve it fresh each sweep rather than caching the
            // pointer across frames, where the moving GC would invalidate it.
            IntPtr boolArg = Marshal.AllocHGlobal(1);
            try
            {
                Marshal.WriteByte(boolArg, 0, 0);   // isLogError: false — a miss must stay quiet
                IntPtr service = this.InvokeCollectColdGet(boolArg);
                if (service == IntPtr.Zero)
                {
                    return;
                }

                IntPtr serviceClass = auraMonoObjectGetClass == null ? IntPtr.Zero : auraMonoObjectGetClass(service);
                IntPtr update = serviceClass == IntPtr.Zero
                    ? IntPtr.Zero
                    : this.FindAuraMonoMethodOnHierarchy(serviceClass, "UpdateAllColdTime", 0);
                if (update == IntPtr.Zero)
                {
                    this.collectColdSweepUnavailable = true;
                    ModLogger.Msg("[CollectCold] UpdateAllColdTime not found on the resolved service — "
                        + "ledger will fill from change events only.");
                    return;
                }

                IntPtr exc = IntPtr.Zero;
                auraMonoRuntimeInvoke(update, service, IntPtr.Zero, ref exc);
                if (exc != IntPtr.Zero)
                {
                    this.collectColdSweepUnavailable = true;
                    ModLogger.Msg("[CollectCold] UpdateAllColdTime threw — sweep disabled for this session.");
                    return;
                }

                // The events land on the hook drain, i.e. next tick — so report coverage then, not
                // now, or the number would always read one sweep behind.
                this.collectColdCoveragePendingAt = Time.unscaledTime + 0.5f;
            }
            finally
            {
                Marshal.FreeHGlobal(boolArg);
            }
        }

        private unsafe IntPtr InvokeCollectColdGet(IntPtr boolArg)
        {
            if (this.collectColdGetInflated == IntPtr.Zero || auraMonoRuntimeInvoke == null)
            {
                return IntPtr.Zero;
            }

            IntPtr* args = stackalloc IntPtr[1];
            args[0] = boolArg;
            IntPtr exc = IntPtr.Zero;
            IntPtr service = auraMonoRuntimeInvoke(this.collectColdGetInflated, IntPtr.Zero, (IntPtr)args, ref exc);
            return exc == IntPtr.Zero ? service : IntPtr.Zero;
        }

        private bool TryResolveCollectColdSweep()
        {
            IntPtr ecsService = this.FindAuraMonoClassInAllLoadedImages("EcsService", "XDTDataAndProtocol.ProtocolService");
            IntPtr iface = this.FindAuraMonoClassInAllLoadedImages("IDynamicMapItemService", "EcsSystem.ClientSystem.Mapresource");
            IntPtr openGet = ecsService == IntPtr.Zero
                ? IntPtr.Zero
                : this.FindAuraMonoMethodOnHierarchy(ecsService, "Get", 1);

            if (ecsService == IntPtr.Zero || iface == IntPtr.Zero || openGet == IntPtr.Zero)
            {
                this.collectColdSweepUnavailable = true;
                ModLogger.Msg("[CollectCold] sweep unavailable (EcsService=" + (ecsService != IntPtr.Zero)
                    + " IDynamicMapItemService=" + (iface != IntPtr.Zero) + " Get=" + (openGet != IntPtr.Zero)
                    + ") — ledger will fill from change events only.");
                return false;
            }

            // Same inflate the event hooks use, and the same arity guard: a wrong method_inst hands
            // the body a garbage pointer and takes the process down.
            if (!this.TryInflateDispatchForEvent(openGet, iface, 1, out IntPtr inflated))
            {
                this.collectColdSweepUnavailable = true;
                ModLogger.Msg("[CollectCold] could not inflate EcsService.Get<IDynamicMapItemService> — "
                    + "ledger will fill from change events only.");
                return false;
            }

            this.collectColdGetInflated = inflated;
            ModLogger.Msg("[CollectCold] resource-verdict sweep armed (every "
                + CollectColdSweepInterval.ToString("F0") + "s).");
            return true;
        }

        private void EnsureAuraCollectColdEventHook()
        {
            if (this.auraCollectColdHookRegistered)
            {
                return;
            }

            // CollectColdEvent { uint resourceNetId@0; long endUnixTimeMs@8; float totalTime@16;
            // int availableNum@20; string displayIcon@24 }
            bool cold = this.RegisterGameEventHook(
                "ScriptsRefactory.DataAndProtocol.Events.CollectColdEvent",
                32,
                this.OnAuraCollectColdEvent);

            // CollectObjectShowEvent { uint netId@0; bool show@4 }
            bool show = this.RegisterGameEventHook(
                "ScriptsRefactory.DataAndProtocol.Events.CollectObjectShowEvent",
                8,
                this.OnAuraCollectObjectShowEvent);

            // RefreshBackPackEvent (shared with Auto Sell — same detour, extra handler): marks
            // when the gathered loot actually landed in the backpack.
            this.RegisterGameEventHook(AutoSellBackpackEventName, AutoSellBackpackEventBytes, this.OnAuraCollectBackpackRefresh);

            this.auraCollectColdHookRegistered = cold || show;
        }

        private void OnAuraCollectBackpackRefresh(GameEventSnapshot e)
        {
            if (!this.autoFarmActive
                || !this.auraFarmEnabled
                || this.farmState != HeartopiaComplete.AutoFarmState.Collecting
                || !this.auraCollectWaitArmed)
            {
                return;
            }

            if (e.ReadInt32(0) != AutoSellBackpackStorageType)
            {
                return;
            }

            this.auraCollectLastBackpackAt = Time.unscaledTime;
        }

        // CollectColdEvent stream semantics (verified from live logs): while the aura drains a
        // multi-charge bush the server emits events with endMs=0 and a DECREMENTING availableNum
        // for the bush actually being picked (charges arrive on a ~2.5s server timer), plus
        // endMs=0/full-availableNum refreshes for every other spammed in-radius bush; the drain
        // completes with a single event carrying a real endMs (cooldown start) — that is the
        // "collected" moment. Captured owner/resource ids can be aggregate level-object ids that
        // never appear in events, so binding is done by the decrement pattern instead.
        private void OnAuraCollectColdEvent(GameEventSnapshot e)
        {
            uint resourceNetId = e.ReadUInt32(0);
            long endMs = (long)e.ReadUInt64(8);
            int availableNum = e.ReadInt32(20);

            // ⚠️ RECORD FIRST, GATE SECOND. This event is the CLIENT'S OWN verdict for one resource:
            // ResourceProtocolManager.CmdUpdateCollectCold computes it (for a dynamic bush, from
            // DynamicBushGrowComponent.MaturityTime — a mushroom GROWS, it does not cool) and
            // broadcasts it keyed by netId. Everything below this point is about the node the farm
            // is standing on, and the old early-return threw the other 800-odd verdicts away.
            //
            // Keeping them is what makes a target answerable BEFORE walking to it: the verdict for a
            // node 30 m off arrives here on its own, with no approach and no polling. Measured over
            // one run: 874 events, 149 distinct netIds.
            if (resourceNetId != 0U)
            {
                // ⚠️ TWO EVENTS ARRIVE PER RESOURCE PER SWEEP, AND THE SECOND ONE LIES.
                //
                // UpdateAllColdTime's loop body sends both:
                //     if (has DynamicBushGrowComponent)
                //         CmdUpdateCollectCold(netId, ParseToUnix(grow.MaturityTime), growTime, ...);
                //     UpdateResourcePoint(resourceId, netId);          // ← unconditional, sends again
                // and UpdateResourcePoint recomputes from a filter keyed on the SELF player:
                //     long num = 0L;
                //     foreach (e2 in _groupedFilterByMapResource.GetEntities(selfRef, resourceId))
                //         num = GetColdEndTime(e2, out total);
                //     CmdUpdateCollectCold(netId, num, total, availableNum);
                // When that filter yields nothing, num stays 0 and the resource is broadcast as
                // READY — overwriting the maturity time sent a line earlier.
                //
                // Keeping only the LAST event is therefore wrong: a mushroom the player can watch
                // growing (the ring is on screen, hand-collect refuses) was recorded as available,
                // which is precisely the wrong direction for a farm that must not walk to it.
                //
                // ⚠️ A ZERO NEVER ERASES A FUTURE ABSOLUTE END — ONLY THE CLOCK DOES.
                //
                // The first cut of this rule was a one-second window ("a zero may not erase a
                // live maturity time from the SAME sweep"), which stops only same-sweep pairs.
                // The seeded ledger showed why that is not enough: the player-keyed filter above
                // reaches only interaction range — measured 23:30, every REAL end arrived 28-42 m
                // out and everything further heard zeros — so EVERY sweep re-broadcasts "ready"
                // for every far resource. Fourteen 9-hour cooldowns restored at world-ready were
                // wiped within seconds, and the farm toured the same six dead rare trees again.
                //
                // An absolute instant stays true however long ago it was heard. A matured bush
                // still goes warm the honest way — its stored end passes, after which zeros write
                // freely — and a NON-zero end (shorter or longer) always wins, so an accelerated
                // maturity or a server-side reschedule still updates.
                CollectColdRecord previous;
                bool zeroAgainstFutureEnd = endMs <= 0L
                    && this.collectColdByNetId.TryGetValue(resourceNetId, out previous)
                    && previous.EndUnixMs > NowUnixMs();

                if (!zeroAgainstFutureEnd)
                {
                    this.collectColdByNetId[resourceNetId] = new CollectColdRecord
                    {
                        EndUnixMs = endMs,
                        AvailableNum = availableNum,
                        SeenAt = Time.unscaledTime,
                    };

                    // Cooldowns measured in hours survive restarts on disk; everything shorter
                    // is not worth a row (ColdLedgerPersistFeature.cs).
                    this.NotePersistableColdVerdict(resourceNetId, endMs);
                }
            }

            if (!this.autoFarmActive || !this.auraFarmEnabled)
            {
                return;
            }

            this.AutoFarmLog("CollectColdEvent netId=" + resourceNetId
                + " endMs=" + endMs
                + " availableNum=" + availableNum
                + " (captured res=" + this.auraCollectNodeResourceNetId
                + " owner=" + this.auraCollectNodeOwnerNetId + ")");

            if (resourceNetId == 0U
                || this.farmState != HeartopiaComplete.AutoFarmState.Collecting
                || !this.auraCollectWaitArmed)
            {
                return;
            }

            // Direct id match (when the capture yielded a real entity id) marks it as ours too.
            if (resourceNetId == this.auraCollectNodeResourceNetId
                || resourceNetId == this.auraCollectNodeOwnerNetId)
            {
                this.auraCollectOurNetIds.Add(resourceNetId);
            }

            int prevAvailable;
            if (this.auraCollectSeenAvailByNetId.TryGetValue(resourceNetId, out prevAvailable)
                && availableNum < prevAvailable)
            {
                if (this.auraCollectOurNetIds.Add(resourceNetId))
                {
                    this.AutoFarmLog($"Aura node bush bound by charge decrement: netId={resourceNetId} ({prevAvailable}->{availableNum})");
                }
            }
            this.auraCollectSeenAvailByNetId[resourceNetId] = availableNum;

            long nowUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            // Drained = a real cooldown end in the future OR charges exhausted: some resource
            // families (event forage) never set endMs/inCold — their drain event only zeroes
            // availableNum (and their shape leaves the axe-checker).
            bool eventSaysDrained = endMs > nowUnixMs || availableNum == 0;
            if (!eventSaysDrained || this.auraCollectNodeConfirmedAt >= 0f || !this.autoCollectClickedSinceArrival)
            {
                return;
            }

            // Cold with a real end time: ours if bound by decrement/id, or the first cold seen
            // before any binding (single-charge resources go cold on their first pick event).
            if (!this.auraCollectOurNetIds.Contains(resourceNetId) && this.auraCollectOurNetIds.Count != 0)
            {
                return;
            }

            this.auraCollectNodeConfirmedAt = Time.unscaledTime;
            if (endMs > nowUnixMs)
            {
                // Real server cooldown end for OUR node — the visited stamp uses it verbatim.
                this.auraCollectNodeColdEndMs = endMs;
            }
            this.AutoFarmLog($"Aura collect confirmed by CollectColdEvent (netId={resourceNetId}, endMs={endMs})");
        }

        private void OnAuraCollectObjectShowEvent(GameEventSnapshot e)
        {
            if (!this.autoFarmActive || !this.auraFarmEnabled)
            {
                return;
            }

            uint netId = e.ReadUInt32(0);
            bool show = e.ReadBool(4);
            if (show || netId == 0U)
            {
                return;
            }

            this.AutoFarmLog("CollectObjectShowEvent netId=" + netId + " show=false"
                + " (captured res=" + this.auraCollectNodeResourceNetId
                + " owner=" + this.auraCollectNodeOwnerNetId + ")");

            if (this.farmState != HeartopiaComplete.AutoFarmState.Collecting
                || !this.auraCollectWaitArmed
                || this.auraCollectNodeConfirmedAt >= 0f
                || !this.autoCollectClickedSinceArrival)
            {
                return;
            }

            // Despawn-style objects (single-charge gathers) hide on collect.
            if (netId == this.auraCollectNodeResourceNetId
                || netId == this.auraCollectNodeOwnerNetId
                || this.auraCollectOurNetIds.Contains(netId))
            {
                this.auraCollectNodeConfirmedAt = Time.unscaledTime;
                this.AutoFarmLog($"Aura collect confirmed by CollectObjectShowEvent (netId={netId})");
            }
        }

        // Called from the aura tick right after a collect command is sent: remember the owner
        // netId of the entity standing on the current foraging node so the wait loop can read
        // its collected state directly instead of waiting for the radar rescan.
        private void TryCaptureAuraCollectNodeOwner(uint ownerNetId, uint resourceNetId, Vector3 targetAnchor)
        {
            if (!this.autoFarmActive
                || this.farmState != HeartopiaComplete.AutoFarmState.Collecting
                || !this.auraCollectWaitArmed
                || this.auraCollectNodeOwnerNetId != 0U
                || ownerNetId == 0U)
            {
                return;
            }

            // Most discovery paths register targets WITHOUT positions (owner-only), so the
            // cached anchor is usually zero — resolve the entity position on demand instead
            // (same chain the live cooldown sync uses). Owners that resolved >3m away are
            // remembered to avoid re-resolving them every resend tick.
            Vector3 anchor = targetAnchor;
            if (anchor == Vector3.zero)
            {
                if (this.auraCollectCaptureMissedOwners.Contains(ownerNetId))
                {
                    return;
                }

                object entity = this.TryGetAuraOwnerEntity(ownerNetId);
                if (entity == null || !this.TryGetAuraEntityPosition(entity, out anchor))
                {
                    return;
                }
            }

            if ((anchor - this.lastNodePosition).sqrMagnitude > 9f)
            {
                this.auraCollectCaptureMissedOwners.Add(ownerNetId);
                return;
            }

            this.auraCollectNodeOwnerNetId = ownerNetId;
            this.auraCollectNodeResourceNetId = resourceNetId != 0U ? resourceNetId : ownerNetId;
            this.auraCollectNodeCapturedAt = Time.unscaledTime;
            this.auraCollectNodeEntitySeen = false;
            this.auraNextCollectNodeProbeAt = 0f;
            this.auraCollectNodeAbsentTicks = 0;
            this.AutoFarmLog($"Aura node owner captured netId={ownerNetId} res={this.auraCollectNodeResourceNetId} at {this.lastNodePosition} (anchor {anchor})");
        }

        // Build-independent collected signal, called from the aura tick right after the target
        // buffer was refreshed successfully: the captured owner vanishing from the axe-checker
        // means its physical gather shape was removed/deactivated — which is what actually stops
        // the aura from re-sending on this build (the managed inCold pre-send check never fires
        // here because XDT* entity resolution is Mono-only). Three consecutive absent ticks
        // (~0.25s) confirm, riding over single flaky scans.
        private void UpdateAuraCollectNodePresence()
        {
            if (!this.autoFarmActive
                || this.farmState != HeartopiaComplete.AutoFarmState.Collecting
                || !this.auraCollectWaitArmed
                || this.auraCollectNodeOwnerNetId == 0U
                || this.auraCollectNodeConfirmedAt >= 0f)
            {
                return;
            }

            if (this.auraOwnerTargetBuffer.Contains(this.auraCollectNodeOwnerNetId))
            {
                this.auraCollectNodeAbsentTicks = 0;
                return;
            }

            this.auraCollectNodeAbsentTicks++;
            if (this.auraCollectNodeAbsentTicks >= 3)
            {
                this.auraCollectNodeConfirmedAt = Time.unscaledTime;
                this.AutoFarmLog($"Aura node left axe-checker (netId={this.auraCollectNodeOwnerNetId}) -> collected");
            }
        }

        // Polls the captured node entity's CollectableObjectComponent (throttled): coldEndTime
        // in the future or availableNum==0 is exactly the state the game's interact icon reads,
        // so it flips within the server round-trip instead of the 2s radar cadence. An entity/
        // component that despawns after having been seen once counts as collected too.
        private void ProbeAuraCollectNodeState(float now)
        {
            if (this.auraCollectNodeConfirmedAt >= 0f
                || this.auraCollectNodeOwnerNetId == 0U
                || now < this.auraNextCollectNodeProbeAt)
            {
                return;
            }

            this.auraNextCollectNodeProbeAt = now + 0.2f;
            if (!this.ResolveAuraFarmRuntimeMethods())
            {
                return;
            }

            object entity = this.TryGetAuraOwnerEntity(this.auraCollectNodeOwnerNetId);
            object collectable = entity != null
                ? this.TryGetAuraEntityComponent(entity, this.auraCollectableObjectComponentType)
                : null;
            if (collectable == null)
            {
                if (this.auraCollectNodeEntitySeen)
                {
                    this.auraCollectNodeConfirmedAt = now;
                    this.AutoFarmLog($"Aura node probe: entity/component despawned (netId={this.auraCollectNodeOwnerNetId}) -> collected");
                }
                else if (!this.auraCollectNodeDiagLogged)
                {
                    this.auraCollectNodeDiagLogged = true;
                    this.AutoFarmLog($"Aura node probe diag: netId={this.auraCollectNodeOwnerNetId} entity={(entity != null ? "ok" : "null")} collectable=null");
                }
                return;
            }

            this.auraCollectNodeEntitySeen = true;

            // inCold is the exact flag the game's interact icon reads; coldEndTime/availableNum
            // cover builds where the bool member fails to resolve.
            bool inCold;
            bool inColdRead = this.TryGetAuraCollectableInCold(collectable, out inCold);
            long coldEndTimeMs = 0L;
            int availableNum = -1;
            string resTypeName = string.Empty;
            bool cooldownRead = this.TryReadLiveCollectableCooldown(collectable, out coldEndTimeMs, out availableNum, out resTypeName);
            long nowUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            if (!this.auraCollectNodeDiagLogged)
            {
                this.auraCollectNodeDiagLogged = true;
                this.AutoFarmLog("Aura node probe diag: netId=" + this.auraCollectNodeOwnerNetId
                    + " inCold=" + (inColdRead ? inCold.ToString() : "unreadable")
                    + " coldEndMs=" + (cooldownRead ? coldEndTimeMs.ToString() : "unreadable")
                    + " availableNum=" + (cooldownRead ? availableNum.ToString() : "unreadable")
                    + " resType=" + (string.IsNullOrEmpty(resTypeName) ? "<none>" : resTypeName));
            }

            if ((inColdRead && inCold)
                || (cooldownRead && (coldEndTimeMs > nowUnixMs || availableNum == 0)))
            {
                this.auraCollectNodeConfirmedAt = now;
            }
        }

        private void RunAuraCollectWait()
        {
            // Stand still while the gathering animation still owes swings. Only the HOP is delayed:
            // the aura keeps sending its collect commands throughout, and every confirmation below
            // still lands — it is simply read a moment later. Without this the dwell ends ~2 s after
            // arrival and a three-swing chop (~4.5 s, one whole clip per swing) never finishes.
            // Bounded by ForagingAnimHoldBudget, so a wedged animator cannot stall the farm.
            if (this.ShouldHoldFarmNodeForForagingAnim())
            {
                this.foragingAnimHoldSpent += Time.unscaledDeltaTime;
                this.autoFarmStatus = "Collecting... playing the gathering animation";
                return;
            }

            float now = Time.unscaledTime;

            // The held time is not dwell time. autoFarmTimer runs through the hold, so without this
            // the Collect Wait Max slider would be half spent before the collect is even judged, and
            // a node that finished normally would report a timeout and get the SHORT retry stamp.
            float maxWait = Mathf.Max(4f, this.auraCollectWaitTimeout) + this.foragingAnimHoldSpent;
            bool auraIdle = now - this.auraLastSuccessfulCommandAt >= 1.25f;
            bool markerFound;
            string nodeMarkerLabel;
            bool markerOnCooldown = this.TryGetNodeMarkerState(this.lastNodePosition, out markerFound, out nodeMarkerLabel);

            // Bubble targets: the aura cannot collect bubbles (they pop by touch / AutoBubbleCollect),
            // so none of the aura confirmations below ever fire and the dwell burned the whole
            // Collect Wait Max per bubble (30s each, user report 2026-07-12). A bubble is done when
            // its tracked marker despawns (popped/collected — or drifted >2.5m, in which case it can
            // be re-targeted at its new spot); and if the marker still stands after a few seconds,
            // standing longer will not pop it — hop with the short retry stamp.
            if (this.autoFarmTargetIsBubble)
            {
                // A BUBBLE IS NOT A PLACE. markerFound asks "is there a marker where we stopped",
                // which was a fair question while every marker stood still; it is not one now that
                // markers follow the bubble. A bubble travelling at swimming speed clears the spot
                // within about a second, so this branch declared a collect on EVERY bubble it ever
                // dwelt on — four in one minute at 18:00, each "collected/despawned after 1,0s",
                // none of them real.
                //
                // The chase knows which bubble it was on. Ask after that one by name; the marker
                // test survives only as the fallback for a dwell that never went through a walk.
                bool chased = this.farmWalkBubbleId != 0;
                bool gone = chased ? !this.IsBubbleStillLive(this.farmWalkBubbleId) : !markerFound;

                if (this.autoFarmTimer >= 1f && gone)
                {
                    this.AutoFarmLog($"Bubble collected/despawned after {this.autoFarmTimer:F1}s at {this.lastNodePosition}"
                        + (chased ? $" (bubble {this.farmWalkBubbleId} is gone)" : " (no marker left, identity unknown)"));
                    this.StampVisitedNode(this.lastNodePosition, now + FarmVisitedRetryStampSeconds);
                    this.FinishCollectingCycle();
                    return;
                }

                if (this.autoFarmTimer >= 6f)
                {
                    this.AutoFarmLog($"Bubble dwell capped after {this.autoFarmTimer:F1}s at {this.lastNodePosition}"
                        + (chased ? $" — bubble {this.farmWalkBubbleId} is STILL ALIVE" : " (marker still present)"));
                    this.StampVisitedNode(this.lastNodePosition, now + FarmVisitedRetryStampSeconds);
                    this.FinishCollectingCycle();
                    return;
                }

                this.autoFarmStatus = "Collecting bubble...";
                return;
            }

            // Fast path: the node reported collected (CollectColdEvent / despawn / entity state).
            // No aura-quiet gate here: the aura keeps re-spamming every in-radius bush (the server
            // just refuses the far ones), so auraLastSuccessfulCommandAt never goes quiet in berry
            // fields — a short post-confirm grace is all that's needed.
            this.ProbeAuraCollectNodeState(now);
            if (this.auraCollectNodeOwnerNetId == 0U
                && !this.auraCollectNodeDiagLogged
                && this.autoCollectClickedSinceArrival
                && this.autoFarmTimer >= 3f)
            {
                this.auraCollectNodeDiagLogged = true;
                this.AutoFarmLog($"Aura node probe diag: aura sent commands but no owner matched node {this.lastNodePosition} within 3m (missedOwners={this.auraCollectCaptureMissedOwners.Count})");
            }
            bool hasCollectProgress = this.auraCollectNodeConfirmedAt >= 0f
                || this.auraCollectOurNetIds.Count > 0
                || this.auraCollectLastBackpackAt >= 0f;

            // Authoritative live state of THIS node (tight XZ identification, scan must postdate
            // arrival). The scan arbitrates the event heuristics both ways below.
            bool liveNodeFound = this.TryGetLiveNodeColdState(this.lastNodePosition, now - this.autoFarmTimer, out bool liveNodeCold, out long liveNodeColdEndMs);

            // Best-known real cooldown end for the visited stamp: our own drain event's endMs wins
            // (freshest, always ours), else the live entity's coldEndTime; 0 => 120s fallback.
            long knownColdEndMs = this.auraCollectNodeColdEndMs;
            if (knownColdEndMs <= 0L && liveNodeFound && liveNodeCold)
            {
                knownColdEndMs = liveNodeColdEndMs;
            }

            // Scan-driven confirm: we made progress here and the node's entity flipped cold —
            // collected, even if the event binding missed it (partial event streams).
            if (liveNodeFound && liveNodeCold && hasCollectProgress && this.auraCollectNodeConfirmedAt < 0f)
            {
                this.auraCollectNodeConfirmedAt = now;
                this.AutoFarmLog($"Aura collect confirmed by live scan (node flipped cold) after {this.autoFarmTimer:F1}s");
            }

            // DESPAWN-family confirm — the half the cooldown test cannot see.
            //
            // Mushrooms and the other dynamic bushes never go cold: picking one REMOVES the entity.
            // So `liveNodeCold` stays false for them forever, the confirm above never fires, and the
            // dwell ran out its full Collect Wait Max on every single one ("Aura collect wait timed
            // out after 5,0s ... label=Mushroom, clicked=True" — 2026-08-19 log).
            //
            // ⚠️ ABSENCE NEEDS A PRIOR SIGHTING. A node still streaming in is absent too, which is
            // exactly the case `nodePresent` below exists to protect. Seen by one post-arrival scan
            // and missing from a LATER one is the unambiguous form, and it needs no id binding at
            // all — which is what makes it work when the aura's capture grabbed the wrong object.
            if (liveNodeFound)
            {
                this.auraCollectNodeSeenPresentAt = this.liveCollectableScanCompletedAt;
            }
            else if (this.auraCollectNodeConfirmedAt < 0f
                && this.auraCollectNodeSeenPresentAt >= 0f
                && this.liveCollectableScanCompletedAt > this.auraCollectNodeSeenPresentAt)
            {
                this.auraCollectNodeConfirmedAt = now;
                this.AutoFarmLog($"Aura collect confirmed by live scan (node despawned) after {this.autoFarmTimer:F1}s");
            }

            // "The node exists locally" — the aura addressed an object ≤3m of it (capture), a
            // post-arrival scan contains its entity, or one did until it was picked. While the
            // destination is still streaming in after a long teleport NONE holds, and every confirm
            // seen so far can only belong to already-loaded NEIGHBORS the aura swept in parallel —
            // never hop on those.
            bool nodePresent = this.auraCollectNodeOwnerNetId != 0U
                || liveNodeFound
                || this.auraCollectNodeSeenPresentAt >= 0f;

            if (this.auraCollectNodeConfirmedAt >= 0f && this.autoFarmTimer >= 0.5f && nodePresent)
            {
                // The scan is the arbiter against neighbor-misbound event confirms: when a scan
                // NEWER than the confirmation still sees the node warm, the confirm was for some
                // other bush — hold until the node truly flips (or the timeout bounds it).
                // ⚠️ THE EVENT ALONE IS NOT PROOF THAT *THIS* NODE WAS DRAINED, so the hop waits for
                // a scan that POSTDATES the confirmation and then believes the scan, not the event.
                //
                // The aura sprays its pick at every object in radius and the server answers for all
                // of them, so a neighbour's drain (endMs=0 with availableNum==0) satisfies
                // OnAuraCollectColdEvent's "first cold seen before any binding" clause and confirms
                // OUR node. Measured 2026-08-19: three mushrooms the farm had "confirmed by
                // CollectColdEvent" minutes earlier were still WARM in the live component read —
                // never picked at all. The farm hopped, the stamp expired, and it walked back to a
                // mushroom it believed it had already taken. That is the revisiting the user sees.
                //
                // Waiting costs at most one scan interval (~2 s) because the confirm already
                // happened; the outer Collect Wait Max still bounds the whole dwell, and a
                // despawn-family node simply reads absent, which is not a contradiction.
                bool livePostConfirmScan =
                    this.liveCollectableScanCompletedAt >= this.auraCollectNodeConfirmedAt + 0.2f;
                bool liveContradictsConfirm = !livePostConfirmScan || (liveNodeFound && !liveNodeCold);
                if (!liveContradictsConfirm)
                {
                    // Hop 1s after the loot actually landed in the backpack (RefreshBackPackEvent);
                    // when no bag refresh was seen this dwell, 1s after the collect confirmation.
                    // Unrelated bag traffic (a neighbour's loot, anything else filling the bag) must not
                    // slide the anchor forever — 3s after the confirm the hop goes regardless.
                    float hopAnchor = Mathf.Max(this.auraCollectNodeConfirmedAt, this.auraCollectLastBackpackAt);
                    if (now - hopAnchor >= 1f || now - this.auraCollectNodeConfirmedAt >= 3f)
                    {
                        this.AutoFarmLog($"Aura collect done after {this.autoFarmTimer:F1}s at {this.lastNodePosition} (bagRefresh={(this.auraCollectLastBackpackAt >= 0f ? "yes" : "none")})");
                        // We just drained it — block for its real remaining cooldown.
                        this.StampVisitedNode(this.lastNodePosition, now + this.GetVisitedColdStampSeconds(knownColdEndMs));
                        this.FinishCollectingCycle();
                        return;
                    }
                }

                // Confirmed but the loot is still settling (or the scan says the node is still
                // active) — hold here so the radar fallbacks can't hop earlier; the shared
                // timeout below stays as the outer bound.
                if (this.autoFarmTimer < maxWait)
                {
                    this.autoFarmStatus = !livePostConfirmScan
                        ? "Collecting... verifying the node against the scan"
                        : liveContradictsConfirm
                            ? "Collecting... node still active"
                            : "Collecting... securing loot";
                    return;
                }
            }

            // Authoritative live state arrived and says the node is already on server cooldown
            // -> skip right away. The capture-age guard gives our OWN pick's events 1.25s to
            // land first, so a node we just drained still goes through the normal completion
            // path (with its bag-settle wait).
            if (!hasCollectProgress
                && this.autoFarmTimer >= 0.75f
                && (!this.autoCollectClickedSinceArrival
                    || this.auraCollectNodeCapturedAt < 0f
                    || now - this.auraCollectNodeCapturedAt >= 1.25f)
                && liveNodeFound
                && liveNodeCold)
            {
                this.AutoFarmLog($"Aura node is live-cold (mono scan) after {this.autoFarmTimer:F1}s at {this.lastNodePosition} -> skipping");
                // Proven server cooldown — block for its real remaining window.
                this.StampVisitedNode(this.lastNodePosition, now + this.GetVisitedColdStampSeconds(knownColdEndMs));
                this.FinishCollectingCycle();
                return;
            }

            // NOTE: silence-based early bail removed by request — a silent node waits the full
            // Collect Wait Max slider (the world may still be loading / the server settling our
            // position after a teleport chain; the collect will happen). Fast skips remain only
            // for PROVEN cooldown: the live-scan branch above.

            if (this.autoFarmTimer >= 1f && auraIdle)
            {
                // Radar shows the node on cooldown -> collected (by us or by someone else).
                if (markerFound && markerOnCooldown)
                {
                    this.AutoFarmLog($"Aura collect confirmed (marker cooldown) after {this.autoFarmTimer:F1}s at {this.lastNodePosition}");
                    // Marker shows [CD] — proven cooldown, block for its known/fallback window.
                    this.StampVisitedNode(this.lastNodePosition, now + this.GetVisitedColdStampSeconds(knownColdEndMs));
                    this.FinishCollectingCycle();
                    return;
                }

                // Marker vanished after the aura actually addressed THIS node (capture): stamped
                // nodes are hidden from the radar before their [CD] marker appears. The capture
                // requirement keeps this from firing during world streaming, when clicks belong
                // to already-loaded neighbors and the node's mesh simply isn't there yet.
                if (!markerFound && this.autoCollectClickedSinceArrival && this.auraCollectNodeOwnerNetId != 0U)
                {
                    // The narrowing found a distance that works: this kind keeps it, and the next
                    // stubborn node starts its own budget from scratch.
                    this.farmWalkStandoffRetries = 0;
                    this.AutoFarmLog($"Aura collect confirmed (marker gone) after {this.autoFarmTimer:F1}s at {this.lastNodePosition}");
                    // Collected (stamped nodes hide their marker) — real/fallback cooldown, not 15s.
                    this.StampVisitedNode(this.lastNodePosition, now + this.GetVisitedColdStampSeconds(knownColdEndMs));
                    this.FinishCollectingCycle();
                    return;
                }
            }

            // One unreachable/bugged node must not stall the loop forever.
            if (this.autoFarmTimer >= maxWait)
            {
                string markerState = markerFound ? (markerOnCooldown ? "cooldown" : "available") : "none";
                // ⚠️ A TIMEOUT WITH THE MARKER STILL AVAILABLE IS A MEASUREMENT, NOT JUST A FAILURE.
                //
                // The walker stops at a stand-off rather than driving into the node, and that
                // distance is a single number for every resource — which is wrong: measured
                // 2026-08-22, Raspberry, Ore, Stone and Mandarin Tree all collected from ~1.05 m
                // while a Button mushroom at 1.03 m timed out with its marker still showing.
                //
                // Rather than guess a number per resource, learn it: a kind that failed from the
                // stand-off gets a tighter one for the rest of the session. One timeout buys a
                // permanent fix for that kind, and nothing is assumed about the kinds that work.
                if (!string.IsNullOrEmpty(nodeMarkerLabel) && markerState == "available"
                    && this.farmWalkStandoffRetries < FarmWalkMaxStandoffSteps
                    && this.TryNarrowFarmWalkStandoff(nodeMarkerLabel, out float wasStandoff,
                        out float nowStandoff))
                {
                    this.farmWalkStandoffRetries++;
                    ModLogger.Msg("[FarmWalk] '" + nodeMarkerLabel + "' did not collect from "
                        + wasStandoff.ToString("0.0#") + "m — stepping in to "
                        + nowStandoff.ToString("0.0#") + "m and trying this same node again ("
                        + this.farmWalkStandoffRetries + "/" + FarmWalkMaxStandoffSteps + ").");

                    // ⚠️ THE SAME NODE, NOW. Learning a number and walking away from the node that
                    // taught it means the lesson costs a resource every time, and a number that is
                    // still too far costs another. Step in and try again here.
                    if (this.TryBeginFarmWalk(this.lastNodePosition, "node:" + nodeMarkerLabel,
                            false, nodeMarkerLabel))
                    {
                        this.farmState = HeartopiaComplete.AutoFarmState.WalkingToNode;
                        this.autoFarmTimer = 0f;
                        return;
                    }

                    // No route back to a node we are standing next to: fall through and give up on
                    // it the ordinary way rather than pretending the retry happened.
                }

                this.AutoFarmLog($"Aura collect wait timed out after {this.autoFarmTimer:F1}s at {this.lastNodePosition} (marker={markerState}, label={(string.IsNullOrEmpty(nodeMarkerLabel) ? "<none>" : nodeMarkerLabel)}, clicked={this.autoCollectClickedSinceArrival})");
                // Cooldown evidence at timeout => real/fallback block; otherwise short retry (streaming lag).
                bool timedOutCold = (markerFound && markerOnCooldown) || (liveNodeFound && liveNodeCold);
                this.StampVisitedNode(this.lastNodePosition, now + (timedOutCold ? this.GetVisitedColdStampSeconds(knownColdEndMs) : FarmVisitedRetryStampSeconds));
                this.FinishCollectingCycle();
                return;
            }

            float remaining = maxWait - this.autoFarmTimer;
            if (!auraIdle)
            {
                this.autoFarmStatus = $"Collecting... aura working ({remaining:F0}s)";
            }
            else if (!markerFound && !this.autoCollectClickedSinceArrival)
            {
                this.autoFarmStatus = $"Collecting... waiting for area to load ({remaining:F0}s)";
            }
            else
            {
                this.autoFarmStatus = $"Collecting... waiting for node ({remaining:F0}s)";
            }
        }

        // Reads the radar marker state at a node position: cooldown flag + label of the closest
        // labeled marker within 2.5m. markerFound=false when no such marker exists (hidden
        // after a collect stamp, not streamed in yet, or radar container unavailable).
        private bool TryGetNodeMarkerState(Vector3 nodePosition, out bool markerFound, out string markerLabel)
        {
            markerFound = false;
            markerLabel = string.Empty;
            bool onCooldown = false;
            if (!this.isRadarActive || this.radarContainer == null)
            {
                return false;
            }

            float bestSqr = 6.25f;
            for (int i = 0; i < this.radarContainer.transform.childCount; i++)
            {
                Transform child = this.radarContainer.transform.GetChild(i);
                if (child == null)
                {
                    continue;
                }

                float sqr = (child.position - nodePosition).sqrMagnitude;
                if (sqr >= bestSqr)
                {
                    continue;
                }

                string label = this.GetMarkerCanonicalLabel(child.gameObject);
                if (string.IsNullOrEmpty(label))
                {
                    continue;
                }

                bestSqr = sqr;
                markerFound = true;
                markerLabel = label;
                onCooldown = this.IsMarkerOnCooldown(child.gameObject);
            }

            return onCooldown;
        }

        // Reconciles the farm's visited-node memory against the live collectable scan every ~2s
        // while the radar or the farm runs.
        //
        // The scan itself is the cooldown truth (entry.OnCooldown, read from the component) and is
        // consumed directly wherever a verdict is needed; nothing is copied into a side table any
        // more. What still needs doing here is the one thing the scan cannot do by itself: evict
        // nodes from recentlyVisitedNodes that the scan reports WARM.
        private void SyncLiveResourceColdStates()
        {
            if (!this.isRadarActive && !this.autoFarmActive)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now < this.nextLiveColdSyncAt)
            {
                return;
            }
            this.nextLiveColdSyncAt = now + 2f;

            // Shared throttle with the game-map feature (mapResNextScanAt) — whoever asks first
            // runs the scan, the other consumes the same snapshot.
            this.RefreshCollectableScan();
            if (this.liveCollectableColds.Count == 0)
            {
                return;
            }

            // Live truth beats a STALE stamp: a node the scan sees WARM (collectable right now) must
            // not stay parked in recentlyVisitedNodes — a wrong stamp there is corrected by nothing
            // else (it only expires by time), and while nearby nodes sit wrongly blocked
            // FindClosestAvailableNode returns null and the farm wanders the area-waypoint rotation
            // ("jumps between empty loading spots").
            //
            // Two exemptions, both of them bugs this purge caused before they existed:
            //
            // ⚠️ THE NODE BEING WORKED (3 m of lastNodePosition). Right after our drain the server
            // flip lags a beat, and a warm read would purge the fresh stamp -> bounce-back.
            //
            // ⚠️ EVERY FRESH STAMP, wherever the farm has since walked to. The 3 m exemption stops
            // applying the moment the farm steps to the next node 5 m away, so the previous node's
            // stamp was wiped by the very next scan and the farm went straight back to a node it had
            // just worked (2026-08-19 log: collect times out at :04, same node re-targeted at :09).
            // A stamp seconds old cannot be stale — it records what the farm just did. The age gate
            // also narrows the purge to stamps LONGER than the retry stamp, which is exactly the
            // class (120-600 s cold stamps) whose resource can be ripe again before it expires.
            //
            // ⚠️ AND IT ASKS BY IDENTITY, NOT PROXIMITY. This used to walk the scan and purge any
            // stamp within 2 m of a warm entity — so in a mushroom clump a warm NEIGHBOUR cleared
            // the stamp of the node we had just collected, and the farm walked back to an empty
            // spot a minute later. TryGetLiveNodeColdState is the same tight identification the
            // collect dwell uses, and for a despawned node it simply reports "not there", which is
            // no evidence about it at all.
            List<Vector3> warmVisitedPurge = null;
            foreach (KeyValuePair<Vector3, float> visited in this.recentlyVisitedNodes)
            {
                if (Vector3.Distance(visited.Key, this.lastNodePosition) <= 3f)
                {
                    continue;
                }

                // No recorded age = written before this bookkeeping existed; treat it as old
                // rather than immortal.
                if (this.visitedNodeStampedAt.TryGetValue(visited.Key, out float stampedAt)
                    && now - stampedAt < FarmVisitedPurgeMinAge)
                {
                    continue;
                }

                if (!this.TryGetLiveNodeColdState(visited.Key, 0f, out bool visitedCold) || visitedCold)
                {
                    continue;
                }

                (warmVisitedPurge ??= new List<Vector3>()).Add(visited.Key);
            }

            if (warmVisitedPurge != null)
            {
                for (int i = 0; i < warmVisitedPurge.Count; i++)
                {
                    this.ForgetVisitedNode(warmVisitedPurge[i]);
                }
            }
        }

        // Reads the node's authoritative live state from the last mono collectable scan. Returns
        // false while the node's entity is not in a scan newer than minScanCompletedAt (world
        // still streaming in / scan stale) — the caller keeps waiting in that case.
        // IDENTIFICATION, not proximity: XZ-only match within 1.5m (entity anchors sit ~0.5-1m
        // above marker positions, and a looser 3D radius let a cold NEIGHBOR be attributed to a
        // warm node — false skips).
        private bool TryGetLiveNodeColdState(Vector3 nodePosition, float minScanCompletedAt, out bool onCooldown)
        {
            return this.TryGetLiveNodeColdState(nodePosition, minScanCompletedAt, out onCooldown, out _);
        }

        // coldEndUnixMs = the entity's real coldEndTime (unix ms) when on cooldown; 0 when warm or
        // unreadable (some families never set it — callers fall back to the 120s stamp).
        private bool TryGetLiveNodeColdState(Vector3 nodePosition, float minScanCompletedAt, out bool onCooldown, out long coldEndUnixMs)
        {
            onCooldown = false;
            coldEndUnixMs = 0L;
            if (this.liveCollectableScanCompletedAt < minScanCompletedAt)
            {
                return false;
            }

            float bestSqr = 2.25f;
            bool found = false;
            uint netId = 0u;
            int staticId = 0;
            for (int i = 0; i < this.liveCollectableColds.Count; i++)
            {
                Vector3 delta = this.liveCollectableColds[i].Position - nodePosition;
                float sqr = delta.x * delta.x + delta.z * delta.z;
                if (sqr >= bestSqr)
                {
                    continue;
                }

                bestSqr = sqr;
                found = true;
                onCooldown = this.liveCollectableColds[i].OnCooldown;
                coldEndUnixMs = this.liveCollectableColds[i].ColdEndMs;
                netId = this.liveCollectableColds[i].NetId;
                staticId = this.liveCollectableColds[i].StaticId;
            }

            if (!found)
            {
                return false;
            }

            // ⚠️ THE EVENT OVERRULES THE COMPONENT, ONE WAY ONLY.
            //
            // The two sources are not equally informative, and the asymmetry is measured, not
            // assumed (2026-08-19):
            //   • A component reading inCold=TRUE is reliable — every hand-picked resource flipped
            //     it, and it reads True on objects the event table knows nothing about.
            //   • A component reading inCold=FALSE proves nothing. Five farm arrivals in a row read
            //     False and collected nothing, because the component's data is only written when
            //     CmdUpdateCollectCold happens to fire for that netId — zeroes there mean "no data",
            //     NOT "available". Reading them as "available" is what sent the farm to spent nodes.
            //
            // So a live verdict may only ever ADD a reason to skip, never clear one.
            CollectColdRecord record = default(CollectColdRecord);
            bool haveRecord = netId != 0u && this.collectColdByNetId.TryGetValue(netId, out record);
            if (haveRecord && record.EndUnixMs > NowUnixMs())
            {
                onCooldown = true;
                if (coldEndUnixMs <= 0L)
                {
                    coldEndUnixMs = record.EndUnixMs;
                }

                return true;
            }

            // ⚠️ "NO VERDICT" IS NOT DECIDED HERE, and that separation is load-bearing.
            //
            // This method answers "is the thing at this position spent", and TWO different questions
            // read it:
            //   • should the farm WALK to that node — there, an unknown state must count as "not
            //     confirmed", or the farm sets off towards a mushroom that is still growing;
            //   • is the node the farm is STANDING ON collected yet — there, the same unknown means
            //     nothing, and treating it as "spent" corrupts the dwell's completion test.
            //
            // Folding the first rule in here broke the second: measured 2026-08-19, nine targets
            // produced zero collects and six timeouts, against six collects in eleven before it.
            // The walk-side rule now lives in IsFarmTargetUnconfirmed, where only the walk reads it.
            _ = staticId;
            return true;
        }

        // Token: 0x06000016 RID: 22 RVA: 0x0000459C File Offset: 0x0000279C
        // selectedLabel = canonical marker label of the returned node (empty when none) — the
        // caller uses it to route "Contaminated" nodes into the sea-clean dwell.
        private Vector3? FindClosestAvailableNode(out string selectedLabel)
        {
            selectedLabel = string.Empty;
            bool flag = !this.isRadarActive || this.radarContainer == null;
            Vector3? result;
            if (flag)
            {
                result = null;
            }
            else
            {
                Vector3 position = Camera.main.transform.position;
                Vector3? vector = null;
                float num = float.MaxValue;
                float unscaledTime = Time.unscaledTime;
                List<Vector3> list = new List<Vector3>();
                foreach (KeyValuePair<Vector3, float> keyValuePair in this.recentlyVisitedNodes)
                {
                    bool flag2 = unscaledTime >= keyValuePair.Value;
                    if (flag2)
                    {
                        list.Add(keyValuePair.Key);
                    }
                }
                foreach (Vector3 key in list)
                {
                    this.ForgetVisitedNode(key);
                }

                // Scan for all enabled items
                for (int i = 0; i < this.radarContainer.transform.childCount; i++)
                {
                    Transform child = this.radarContainer.transform.GetChild(i);
                    bool flag3 = child == null;
                    if (!flag3)
                    {
                        GameObject gameObject = child.gameObject;
                        string markerLabel = this.GetMarkerCanonicalLabel(gameObject);
                        bool markerOnCooldown = this.IsMarkerOnCooldown(gameObject);
                        bool flag4 = string.IsNullOrEmpty(markerLabel);
                        if (!flag4)
                        {
                            bool flag5 = markerOnCooldown;
                            if (!flag5)
                            {
                                // Authoritative live check bypassing marker-rebuild/stamp lag:
                                // a candidate whose entity is known cold is never targeted.
                                bool liveCandidateCold;
                                if (this.TryGetLiveNodeColdState(child.position, unscaledTime - 6f, out liveCandidateCold) && liveCandidateCold)
                                {
                                    continue;
                                }
                                bool flag6 = false;
                                foreach (Vector3 vector2 in this.recentlyVisitedNodes.Keys)
                                {
                                    bool flag7 = Vector3.Distance(child.position, vector2) < 2f;
                                    if (flag7)
                                    {
                                        flag6 = true;
                                        break;
                                    }
                                }
                                bool flag8 = flag6;
                                if (!flag8)
                                {
                                    bool flag9 = false;
                                    // Underwater update (2026-07-09): sea plants + contamination.
                                    // Exact-toggle branches checked before the generic substring
                                    // chain below so these labels can never be shadowed by it.
                                    bool flag10 = (this.showGlasswortRadar && markerLabel.Contains("Glasswort"))
                                        || (this.showSeaGrapeRadar && markerLabel.Contains("Sea Grape"))
                                        || (this.showWakameRadar && markerLabel.Contains("Wakame"))
                                        || (this.showContaminatedRadar && markerLabel.Contains("Contaminated"))
                                        || this.ShouldShowMushroomByLabel(markerLabel)
                                        || (this.showFiddleheadRadar && markerLabel.Contains("Fiddlehead"))
                                        || (this.showTallMustardRadar && markerLabel.Contains("Tall Mustard"))
                                        || (this.showBurdockRadar && markerLabel.Contains("Burdock"))
                                        || (this.showMustardGreensRadar && markerLabel.Contains("Mustard Greens"));
                                    if (flag10)
                                    {
                                        flag9 = true;
                                    }
                                    else
                                    {
                                        bool flag11 = markerLabel.Contains("Blueberry") && this.showBlueberryRadar;
                                        if (flag11)
                                        {
                                            flag9 = true;
                                        }
                                        else
                                        {
                                            bool flag12 = markerLabel.Contains("Raspberry") && this.showRaspberryRadar;
                                            if (flag12)
                                            {
                                                flag9 = true;
                                            }
                                            else if (markerLabel.Contains("Rare Tree") && this.showRareTreeRadar)
                                            {
                                                flag9 = true;
                                            }
                                            else if (markerLabel.Contains("Apple Tree") && this.showAppleTreeRadar)
                                            {
                                                flag9 = true;
                                            }
                                            else if (markerLabel.Contains("Mandarin Tree") && this.showOrangeTreeRadar)
                                            {
                                                flag9 = true;
                                            }
                                            else if (markerLabel.Contains("Stone") && this.showStoneRadar)
                                            {
                                                flag9 = true;
                                            }
                                            else if (markerLabel.Contains("Ore") && this.showOreRadar)
                                            {
                                                flag9 = true;
                                            }
                                            else if (markerLabel.Contains("Branch") && this.showBranchRadar)
                                            {
                                                flag9 = true;
                                            }
                                            else if (markerLabel.Contains("Tree") && this.showTreeRadar)
                                            {
                                                flag9 = true;
                                            }
                                            else
                                            {
                                                bool flag13 = markerLabel.Contains("Bubble") && this.showBubbleRadar;
                                                if (flag13)
                                                {
                                                    flag9 = true;
                                                }
                                                else
                                                {
                                                    bool flagBird = markerLabel.Contains("Bird") && this.showBirdRadar;
                                                    if (flagBird)
                                                    {
                                                        flag9 = true;
                                                    }
                                                    else if (markerLabel.Contains("Player") && this.showOtherPlayersRadar)
                                                    {
                                                        flag9 = true;
                                                    }
                                                    else if (markerLabel.Contains("Morph") && this.showOtherPlayersRadar)
                                                    {
                                                        flag9 = true;
                                                    }
                                                    else
                                                    {
                                                        bool flag14 = markerLabel.Contains("Insect") && this.showInsectRadar;
                                                        if (flag14)
                                                        {
                                                            flag9 = true;
                                                        }
                                                        else if (markerLabel.Contains("Meteor") && this.showMeteorRadar)
                                                        {
                                                            flag9 = true;
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    bool flag15 = !flag9;
                                    if (!flag15)
                                    {
                                        if (child.position.sqrMagnitude < 0.01f)
                                        {
                                            continue;
                                        }

                                        // Every eligible candidate, not just the nearest — the tour
                                        // planner needs the whole set to order it. Collecting here
                                        // rather than duplicating the filter chain keeps the two
                                        // callers from drifting apart.
                                        this.farmCandidateSink?.Add(new FarmTourStop(child.position, markerLabel));

                                        float num2 = Vector3.Distance(position, child.position);
                                        bool flag16 = num2 < num;
                                        if (flag16)
                                        {
                                            num = num2;
                                            vector = new Vector3?(child.position);
                                            selectedLabel = markerLabel;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                result = vector;
            }
            return result;
        }

        private Vector3? FindClosestVisiblePriorityNode(Vector3 playerPos, float currentTime)
        {
            this.lastFoundPriorityNodeLocation = null;
            this.lastFoundPriorityNodeLabel = string.Empty;
            // Check if any priorities are enabled
            bool hasPriorities = this.priorityOysterMushroom || this.priorityButtonMushroom || this.priorityPennyBun ||
                                this.priorityShiitake || this.priorityTruffle || this.priorityFiddlehead || this.priorityTallMustard || this.priorityBurdock || this.priorityMustardGreens || this.priorityBlueberry ||
                                this.priorityRaspberry || this.priorityBubble || this.priorityInsect;

            if (!hasPriorities)
            {
                return null; // No priorities set, return null to use normal scanning
            }

            Vector3? closestPriority = null;
            float closestDistance = float.MaxValue;
            Camera cam = Camera.main;
            if (cam == null)
            {
                return null;
            }

            for (int i = 0; i < this.radarContainer.transform.childCount; i++)
            {
                Transform child = this.radarContainer.transform.GetChild(i);
                if (child == null) continue;

                GameObject gameObject = child.gameObject;
                string markerLabel = this.GetMarkerCanonicalLabel(gameObject);
                if (string.IsNullOrEmpty(markerLabel) || this.IsMarkerOnCooldown(gameObject)) continue;

                // Check if recently visited
                bool isRecentlyVisited = false;
                foreach (Vector3 vector2 in this.recentlyVisitedNodes.Keys)
                {
                    if (Vector3.Distance(child.position, vector2) < 2f)
                    {
                        isRecentlyVisited = true;
                        break;
                    }
                }
                if (isRecentlyVisited) continue;

                Vector3 screenPoint = cam.WorldToScreenPoint(child.position + new Vector3(0f, 1.1f, 0f));
                bool isVisibleOnScreen = screenPoint.z > 0.05f
                    && screenPoint.x >= 8f
                    && screenPoint.x <= (Screen.width - 8f)
                    && screenPoint.y >= 8f
                    && screenPoint.y <= (Screen.height - 8f);
                if (!isVisibleOnScreen)
                {
                    continue;
                }

                // Check if this node matches a priority
                bool isPriorityMatch = false;

                if (this.priorityOysterMushroom && markerLabel.Contains("Oyster"))
                    isPriorityMatch = true;
                else if (this.priorityButtonMushroom && markerLabel.Contains("Button"))
                    isPriorityMatch = true;
                else if (this.priorityPennyBun && markerLabel.Contains("Penny Bun"))
                    isPriorityMatch = true;
                else if (this.priorityShiitake && markerLabel.Contains("Shiitake"))
                    isPriorityMatch = true;
                else if (this.priorityTruffle && markerLabel.Contains("Truffle"))
                    isPriorityMatch = true;
                else if (this.priorityFiddlehead && markerLabel.Contains("Fiddlehead"))
                    isPriorityMatch = true;
                else if (this.priorityTallMustard && (markerLabel.Contains("Tall Mustard") || markerLabel.Contains("Mustard")))
                    isPriorityMatch = true;
                else if (this.priorityBurdock && markerLabel.Contains("Burdock"))
                    isPriorityMatch = true;
                else if (this.priorityMustardGreens && markerLabel.Contains("Mustard Greens"))
                    isPriorityMatch = true;
                else if (this.priorityBlueberry && markerLabel.Contains("Blueberry") && this.showBlueberryRadar)
                    isPriorityMatch = true;
                else if (this.priorityRaspberry && markerLabel.Contains("Raspberry") && this.showRaspberryRadar)
                    isPriorityMatch = true;
                else if (this.priorityBubble && markerLabel.Contains("Bubble") && this.showBubbleRadar)
                    isPriorityMatch = true;
                else if (this.priorityInsect && markerLabel.Contains("Insect") && this.showInsectRadar)
                    isPriorityMatch = true;

                if (isPriorityMatch)
                {
                    Vector3? mappedPriorityLocation = this.GetPriorityLocationForNodeText(markerLabel);
                    if (mappedPriorityLocation.HasValue && !this.IsPriorityLocationAvailable(mappedPriorityLocation.Value, currentTime))
                    {
                        continue;
                    }

                    float distance = Vector3.Distance(playerPos, child.position);
                    if (distance < closestDistance)
                    {
                        closestDistance = distance;
                        closestPriority = child.position;
                        this.lastFoundPriorityNodeLocation = mappedPriorityLocation;
                        this.lastFoundPriorityNodeLabel = markerLabel;
                    }
                }
            }

            return closestPriority;
        }

        private Vector3? FindClosestPriorityNodeForLocation(Vector3 priorityLocation, Vector3 playerPos, bool requireVisibleOnScreen)
        {
            this.lastFoundPriorityNodeLocation = priorityLocation;
            this.lastFoundPriorityNodeLabel = string.Empty;
            if (!this.isRadarActive || this.radarContainer == null)
            {
                return null;
            }

            Camera cam = requireVisibleOnScreen ? Camera.main : null;
            if (requireVisibleOnScreen && cam == null)
            {
                return null;
            }

            Vector3? closestPriority = null;
            float closestDistance = float.MaxValue;
            const float priorityAreaNodeSearchRadius = 120f;

            for (int i = 0; i < this.radarContainer.transform.childCount; i++)
            {
                Transform child = this.radarContainer.transform.GetChild(i);
                if (child == null)
                {
                    continue;
                }

                GameObject gameObject = child.gameObject;
                string markerLabel = this.GetMarkerCanonicalLabel(gameObject);
                if (string.IsNullOrEmpty(markerLabel) || this.IsMarkerOnCooldown(gameObject))
                {
                    continue;
                }

                Vector3? mappedPriorityLocation = this.GetPriorityLocationForNodeText(markerLabel);
                if (!mappedPriorityLocation.HasValue || mappedPriorityLocation.Value != priorityLocation)
                {
                    continue;
                }

                if (Vector3.Distance(child.position, priorityLocation) > priorityAreaNodeSearchRadius)
                {
                    continue;
                }

                bool isRecentlyVisited = false;
                foreach (Vector3 visitedNode in this.recentlyVisitedNodes.Keys)
                {
                    if (Vector3.Distance(child.position, visitedNode) < 2f)
                    {
                        isRecentlyVisited = true;
                        break;
                    }
                }
                if (isRecentlyVisited)
                {
                    continue;
                }

                if (requireVisibleOnScreen)
                {
                    Vector3 screenPoint = cam.WorldToScreenPoint(child.position + new Vector3(0f, 1.1f, 0f));
                    bool isVisibleOnScreen = screenPoint.z > 0.05f
                        && screenPoint.x >= 8f
                        && screenPoint.x <= (Screen.width - 8f)
                        && screenPoint.y >= 8f
                        && screenPoint.y <= (Screen.height - 8f);
                    if (!isVisibleOnScreen)
                    {
                        continue;
                    }
                }

                float distance = Vector3.Distance(playerPos, child.position);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestPriority = child.position;
                    this.lastFoundPriorityNodeLabel = markerLabel;
                }
            }

            return closestPriority;
        }

        private Vector3? GetPriorityLocationForNodeText(string text)
        {
            if (text.Contains("Oyster")) return this.priorityLocations["Oyster Mushroom"];
            if (text.Contains("Button")) return this.priorityLocations["Button Mushroom"];
            if (text.Contains("Penny Bun")) return this.priorityLocations["Penny Bun"];
            if (text.Contains("Shiitake")) return this.priorityLocations["Shiitake"];
            if (text.Contains("Truffle")) return this.priorityLocations["Black Truffle"];
            if (text.Contains("Fiddlehead")) return this.priorityLocations["Fiddlehead"];
            if (text.Contains("Tall Mustard")) return this.priorityLocations["Tall Mustard"];
            if (text.Contains("Burdock")) return this.priorityLocations["Burdock"];
            if (text.Contains("Mustard Greens")) return this.priorityLocations["Mustard Greens"];
            if (text.Contains("Blueberry")) return this.priorityLocations["Blueberry"];
            if (text.Contains("Raspberry")) return this.priorityLocations["Raspberry"];
            return null;
        }



        private bool HasAvailablePriorityNodeForLocation(Vector3 location)
        {
            if (!this.isRadarActive || this.radarContainer == null)
            {
                return false;
            }

            for (int i = 0; i < this.radarContainer.transform.childCount; i++)
            {
                Transform child = this.radarContainer.transform.GetChild(i);
                if (child == null || child.gameObject == null)
                {
                    continue;
                }

                GameObject markerObject = child.gameObject;
                string markerLabel = this.GetMarkerCanonicalLabel(markerObject);
                if (string.IsNullOrEmpty(markerLabel) || this.IsMarkerOnCooldown(markerObject))
                {
                    continue;
                }

                Vector3? mappedLocation = this.GetPriorityLocationForNodeText(markerLabel);
                if (!mappedLocation.HasValue || mappedLocation.Value != location)
                {
                    continue;
                }

                bool isRecentlyVisited = false;
                foreach (Vector3 visitedNode in this.recentlyVisitedNodes.Keys)
                {
                    if (Vector3.Distance(child.position, visitedNode) < 2f)
                    {
                        isRecentlyVisited = true;
                        break;
                    }
                }

                if (!isRecentlyVisited)
                {
                    return true;
                }
            }

            return false;
        }

        private bool ShouldShowMushroomByLabel(string labelText)
        {
            if (string.IsNullOrEmpty(labelText))
            {
                return false;
            }

            if (this.showMushroomRadar)
            {
                return labelText.Contains("Mushroom")
                    || labelText.Contains("Oyster")
                    || labelText.Contains("Button")
                    || labelText.Contains("Penny Bun")
                    || labelText.Contains("Shiitake")
                    || labelText.Contains("Truffle");
            }

            return (this.showOysterMushroomRadar && labelText.Contains("Oyster"))
                || (this.showButtonMushroomRadar && labelText.Contains("Button"))
                || (this.showPennyBunRadar && labelText.Contains("Penny Bun"))
                || (this.showShiitakeRadar && labelText.Contains("Shiitake"))
                || (this.showTruffleRadar && labelText.Contains("Truffle"));
        }

        private bool IsMushroomLocationEnabled(string locationName)
        {
            if (this.showMushroomRadar)
            {
                return true;
            }

            string name = (locationName ?? string.Empty).ToLowerInvariant();
            if (name.Contains("oyster"))
            {
                return this.showOysterMushroomRadar;
            }

            if (name.Contains("button"))
            {
                return this.showButtonMushroomRadar;
            }

            if (name.Contains("penny"))
            {
                return this.showPennyBunRadar;
            }

            if (name.Contains("shiitake"))
            {
                return this.showShiitakeRadar;
            }

            if (name.Contains("truffle"))
            {
                return this.showTruffleRadar;
            }

            // Unknown mushroom location names: allow if any specific mushroom toggle is enabled.
            return this.showOysterMushroomRadar
                || this.showButtonMushroomRadar
                || this.showPennyBunRadar
                || this.showShiitakeRadar
                || this.showTruffleRadar;
        }


        private void ToggleAutoFarm()
        {
            bool flag = this.AnyRadarLootToggleEnabled();
            
            // Fix: Check if Radar is active before enabling Auto Farm
            if (!this.autoFarmActive)
            {
                if (!flag)
                {
                    this.autoFarmStatus = "NO_TOGGLES_ERROR";
                    return;
                }
                if (!this.auraFarmEnabled)
                {
                    this.autoFarmStatus = "MODE_REQUIRED_ERROR";
                    this.AddMenuNotification("Enable Aura Farm first", new Color(1f, 0.75f, 0.45f));
                    return;
                }
                if (!this.isRadarActive)
                {
                    this.autoFarmStatus = "RADAR_OFF_ERROR";
                    return;
                }
            }

            // Stop path: surface BEFORE the flag flips — StealthForagingActive is gated on
            // autoFarmActive, so after the flip this would be a no-op and the player would be
            // dropped out of noclip inside the terrain (StealthForagingFeature.cs).
            if (this.autoFarmActive)
            {
                // Release any injected move axis first — stopping mid-walk would otherwise leave
                // the player driving into whatever they were heading for.
                this.AbortFarmWalk();
                this.SurfaceFromStealthForaging("Stop Foraging");

                // Per-run state: a node that beat the walker last session deserves a fresh try, and
                // the rescue cooldown should not carry over into a run that starts minutes later.
                this.farmWalkNodeFailures.Clear();
                this.farmWalkLastRescueTeleportAt = 0f;
                this.lastFarmNodeActivityAt = 0f;
                this.farmWalkBlockedGraphNodes.Clear();  // bans are per-run heuristics
                this.ResetFarmWalkRunState();

                // The tour is per-run too. Carrying one over means the next run opens with a plan
                // built around wherever the player happened to be standing minutes ago.
                this.ResetFarmTour();
            }

            this.autoFarmActive = !this.autoFarmActive;
            bool flag3 = this.autoFarmActive;
            if (flag3)
            {
                // Walk-to-node mode pins 1x: at timeScale 5 the player covers five times the ground
                // per REAL second, and the server's MovementAntiCheating samples real time (flags
                // >4.3 m/s on foot), so a legitimate walk would read as a 20 m/s teleport-slide.
                this.SetGameSpeed(this.FarmRunGameSpeed);
                this.CheckRadarAutoToggle(); // This won't auto-enable radar, but checks consistency
                this.autoFarmStatus = "Starting Auto Farm...";
                this.autoFarmTimer = 0f;
                this.nextLiveColdSyncAt = 0f; // fresh authoritative cold states before the first hop
                this.lastScanTime = 0f;       // rebuild radar markers from them in the same frame (sync -> RunRadar -> farm tick order in OnUpdate)
                // -1, not 0. The rotation pre-increments — `(index + 1) % Count` — so starting at 0
                // makes the first relocation go to farmLocations[1] and leaves index 0 as the LAST
                // stop of a full cycle. With Black Truffle Spawn sitting at index 0 and the farm
                // being restarted often, it was reached once in an entire log while Oyster (index
                // 1) was reached seven times. -1 makes index 0 the first stop instead.
                this.currentLocationIndex = -1;
                this.ClearApproachFailureStamps();
                this.cameraRotationAttempts = 0;

                // ⚠️ ON START AS WELL AS ON STOP, and not out of caution. A run does not always
                // follow a stop of this feature: the very first Start after a launch, and a start
                // that follows a world change or a crash, both reach here with whatever the last
                // session left behind. Clearing in one place only means "clean slate" holds for the
                // common path and quietly fails for exactly the cases where stale state is likeliest.
                this.ResetFarmWalkRunState();
                this.farmWalkNodeFailures.Clear();
                this.farmWalkBlockedGraphNodes.Clear();
                this.farmWalkLastRescueTeleportAt = 0f;
                this.ResetFarmTour();
                this.ResetContaminationDwellState();
                this.ResetCorruptionCleanseState();
                this.ResetNavMeshProbeState();
                this.ResetTrackPathGraphProbeState();
                this.priorityLocationCooldowns.Clear();
                this.RefreshActivePriorityLocations();
                this.currentPriorityLocation = this.GetActivePriorityLocation();
                this.lastTeleportWasPriorityLocation = false;
                this.priorityRecheckTimer = 0f; // Reset recheck timer
                // Take the axe now, while we are still standing where the run began — the swing
                // controller needs a tool in hand, and doing this per node would be a server round
                // trip each time.
                this.NoteForagingAnimRunStarted();

                this.AutoFarmLog("Started. activePriorityLocations=" + this.activePriorityLocations.Count
                    + " currentPriorityLocation=" + (this.currentPriorityLocation.HasValue ? this.currentPriorityLocation.Value.ToString() : "none"));

                if (this.currentPriorityLocation.HasValue)
                {
                    // Startup is the last site that warped unconditionally, and it had the weakest
                    // excuse: the farm has just been switched on, so there is no failed walk behind
                    // it — only a destination and no attempt to reach it. Same rule as everywhere
                    // else, and the same fallback when the router cannot answer (the track graph may
                    // not be loaded yet at this moment, in which case the teleport still happens).
                    if (this.farmWalkToAreaEnabled
                        && this.TryBeginFarmWalkToArea(this.currentPriorityLocation.Value, "priority location"))
                    {
                        this.AutoFarmLog("Startup walking to priority location "
                            + this.currentPriorityLocation.Value + " (a route exists, so no teleport).");
                        this.lastTeleportWasPriorityLocation = true;
                        this.farmState = HeartopiaComplete.AutoFarmState.WalkingToNode;
                        this.autoFarmStatus = "Walking to the priority location...";
                    }
                    else
                    {
                        this.AutoFarmLog("Startup routing to priority location "
                            + this.currentPriorityLocation.Value + " — no route, teleporting.");
                        this.FarmTeleportTo(this.ApplyForagingAreaTeleportOffset(this.currentPriorityLocation.Value),
                            "area:startup-priority", this.currentPriorityLocation.Value);
                        this.lastTeleportWasPriorityLocation = true;
                        this.farmState = HeartopiaComplete.AutoFarmState.WaitingForPriorityArea;
                        this.autoFarmStatus = "Going to priority location...";
                    }
                }
                else
                {
                    this.AutoFarmLog("Startup entering normal scan mode (no active priority location).");
                    this.farmState = HeartopiaComplete.AutoFarmState.ScanningForNodes;
                }

                int autoStopSeconds = this.GetAutoFarmAutoStopSeconds();
                if (this.autoFarmAutoStopEnabled && autoStopSeconds > 0)
                {
                    this.autoFarmAutoStopAt = Time.unscaledTime + autoStopSeconds;
                    this.AddMenuNotification("Auto Farm auto-stop set: " + this.FormatDurationHms(autoStopSeconds), new Color(0.55f, 0.88f, 1f));
                }
                else
                {
                    this.autoFarmAutoStopAt = -1f;
                }

                ModLogger.Msg("[AUTO FARM] Enabled");
            }
            else
            {
                this.NoteForagingAnimRunStopped();   // give the axe back
                this.farmState = HeartopiaComplete.AutoFarmState.Idle;
                this.autoFarmStatus = "READY";
                this.autoFarmTimer = 0f;
                this.SetGameSpeed(1f);
                this.currentPriorityLocation = null;
                this.lastTeleportWasPriorityLocation = false;
                this.autoFarmAutoStopAt = -1f;
                this.ResetContaminationDwellState();
                this.ResetCorruptionCleanseState();
                this.AutoFarmLog("Stopped. reason=manual-toggle");
                ModLogger.Msg("[AUTO FARM] Disabled");
            }
        }

        private int GetAutoFarmAutoStopSeconds()
        {
            return Math.Max(0, this.autoFarmAutoStopHours) * 3600
                + Math.Max(0, this.autoFarmAutoStopMinutes) * 60
                + Math.Max(0, this.autoFarmAutoStopSeconds);
        }

        private bool AreHeavyFarmAutomationsActive()
        {
            return BirdNetFarm.IsEnabled || InsectNetFarm.IsEnabled;
        }


        // Token: 0x02000008 RID: 8
        private class FarmLocation
        {
            // Token: 0x0600002E RID: 46 RVA: 0x00008437 File Offset: 0x00006637
            public FarmLocation(string name, Vector3 position, string type)
            {
                this.Name = name;
                this.Position = position;
                this.Type = type;
            }

            // Token: 0x04000052 RID: 82
            public string Name;

            // Token: 0x04000053 RID: 83
            public Vector3 Position;

            // Token: 0x04000054 RID: 84
            public string Type;
        }

        // Token: 0x02000009 RID: 9
        private enum AutoFarmState
        {
            // Token: 0x04000056 RID: 86
            Idle,
            // Token: 0x04000057 RID: 87
            ScanningForNodes,
            // Token: 0x04000058 RID: 88
            TeleportingToNode,
            // Token: 0x04000059 RID: 89
            Collecting,
            // Token: 0x0400005A RID: 90
            MovingToLocation,
            // Token: 0x0400005B RID: 91
            LoadingArea,
            // Token: 0x0400005C RID: 92
            WaitingForNodes,
            // Token: 0x0400005D RID: 93
            WaitingForPriorityArea,
            // Corrupted-debuff cleanse hold (CorruptionCleanseFeature.cs)
            CleansingCorruption,
            // Ground walk to a resource node (FarmWalkFeature.cs) — sits between ScanningForNodes
            // and Collecting when the walk-to-node mode is on.
            WalkingToNode
        }

    }
}
