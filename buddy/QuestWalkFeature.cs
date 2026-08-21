using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace HeartopiaMod
{
    // ============================================================================================
    // QUEST WALK — walk to the point the GAME published for the tracked quest, across worlds.
    //
    // The game already builds both halves, so this feature does no pathfinding of its own:
    //   * the POINT — TrackingSystem._trackingItems, where TrackData.TaskTableId IS the quest's
    //     taskId (0 = not a quest);
    //   * the ROUTE — TrackingPathModule._path, a ~51-point polyline whose first point is the
    //     player. It is reused when the game happens to be routing at the item we picked.
    //
    // Everything below was measured in a live game before it was written here (the throwaway probe
    // in the plan doc). Each rule is one failure that actually happened, so none of them are
    // defensive guesses:
    //
    //  1. FOLLOW THE QUEST ITEM, NOT THE DRAWN ONE. The game tracks several things and draws
    //     exactly one. Underwater a LOCAL furniture marker 0.4 m away won the star line while the
    //     quest track sat 29 m off — "follow what is drawn" walked half a metre and declared
    //     arrival. Pick by TrackReason.Task.
    //  2. ENTER AN INJECTED ROUTE AT THE NEAREST POINT. _path refreshes on the game's own cadence,
    //     so by re-injection time its leading points are already behind the player. Feeding it from
    //     index 0 walked BACKWARDS: a walk that began 85 m out was 114 m out a minute later.
    //  3. A PORTAL'S TRIGGER NEEDS 0.5 m. At 1 m the walk parks on the doorstep and the transfer
    //     never fires — silently, which reads exactly like the feature being broken.
    //  4. A WORLD CHANGE IS A PAUSE, NOT AN ENDING. Every cached position belongs to the old map,
    //     and the game republishes its track a beat after the gate opens.
    //  5. ARRIVAL IS FLAT ON LAND AND 3-D UNDERWATER. Flat called 0.91 m "arrived" while the player
    //     stood on a roof 4.74 m above the NPC.
    //  6. ANY CACHED "IS X PRESENT" EXPIRES WITH THE WORLD. An answer learned underwater survived
    //     the trip back to land and silently reverted the portal radius to 1 m.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // ── Tuning ───────────────────────────────────────────────────────────────────────────────

        // Arrival radii by what is being tracked. A Field or an Area is a place to be INSIDE, not a
        // point to stand on; an NPC or a resource is a thing to reach; a portal is the tightest of
        // all because its trigger volume is small (rule 3).
        private const float QuestWalkArrivePortalNpc = 0.5f;
        private const float QuestWalkArriveField = 10f;
        private const float QuestWalkArriveArea = 5f;
        private const float QuestWalkArrivePoint = 1f;

        // XDT.Scene.Shared.Modules.Track.TrackType — values from the DUMP, never from a field-order
        // count: the enum SKIPS 4 (Field = 3, then Bird = 5).
        private const int QuestWalkTrackTypeNpc = 2;
        private const int QuestWalkTrackTypeField = 3;
        private const int QuestWalkTrackTypeDynamicMapResource = 18;
        private const int QuestWalkTrackTypeArea = 19;
        private const byte QuestWalkTrackReasonTask = 0;

        // Sazabi…RelateRoom.RoomLevelId.SeaWorld — the underwater WORLD. Deliberately not "am I
        // swimming": the town map has underwater zones too (rule 5).
        private const int QuestWalkRoomLevelSeaWorld = 3;

        // TrackingItem.TrackData sits at +16 and the struct's data starts there, so every field
        // lands at exactly the offset mono.describe reports for TrackData itself.
        private const int QuestWalkOffPosition = 16;
        private const int QuestWalkOffToken = 32;
        private const int QuestWalkOffTargetNetId = 40;
        private const int QuestWalkOffStaticId = 44;
        private const int QuestWalkOffTrackType = 48;
        private const int QuestWalkOffTrackReason = 49;
        private const int QuestWalkOffTaskTableId = 52;
        private const int QuestWalkOffSubTrackType = 60;

        private const float QuestWalkStartCooldown = 2f;
        private const float QuestWalkRetargetDistance = 3f;
        private const int QuestWalkRetargetStableReads = 3;
        private const float QuestWalkRetargetCooldown = 8f;
        private const float QuestWalkFinalApproachLock = 12f;
        private const float QuestWalkTrackLostGrace = 20f;
        private const float QuestWalkNpcStreamingRange = 60f;
        private const float QuestWalkNpcProbeTtl = 5f;
        private const int QuestWalkMaxTrackingItems = 4096;

        // ── State ────────────────────────────────────────────────────────────────────────────────

        // Read by IsOutOfBoundsGuardRequested through FarmWalkRunActive — see the note there.
        internal bool questWalkFollowing;

        internal string questWalkStatus = "Idle.";

        private Vector3 questWalkAim;
        private float questWalkRadius = QuestWalkArrivePoint;
        private bool questWalkParked;
        private Vector3 questWalkParkedAt;
        private int questWalkEpoch = -1;
        private bool questWalkWaitingForWorld;
        private float questWalkTrackLostAt;

        // Set once the game's route has been judged unwalkable for this destination. The injection
        // re-runs on every path update, so without this the rejected route returns immediately.
        private bool questWalkGameRouteRejected;
        private float questWalkNextStartAt;
        private float questWalkNextRetargetAt;
        private Vector3 questWalkCandidate;
        private int questWalkCandidateReads;
        private int questWalkInjectedCorners = -1;
        private int questWalkLastCornerCount = -1;
        private int questWalkErrorCount;
        private int questWalkArrivals;

        // NPC-presence probe, keyed on (npc, WORLD, moment) — rule 6.
        private int questWalkNpcProbeId = -1;
        private int questWalkNpcProbeEpoch = -1;
        private float questWalkNpcProbeAt;
        private bool questWalkNpcMissing;

        private IntPtr questWalkDataCenterClass = IntPtr.Zero;

        private struct QuestWalkTrack
        {
            public bool Valid;
            public Vector3 Target;
            public ulong Token;
            public uint TargetNetId;
            public int StaticId;
            public int TaskTableId;
            public int TrackType;
            public int SubTrackType;
            public bool IsQuestItem;
            public bool HasGameRoute;   // the game's path module is routing at THIS item
            public bool NpcMissingHere; // the NPC it names is not in this world (rule 6)
            public int ItemCount;

            // The SubTrackType half is correct for LOCAL tracks and DEAD for quest ones:
            // ClientTrackService sets it, but a quest track is built by ClientTrackSystem, which
            // never touches it. NpcMissingHere is the half that works on quest tracks.
            public bool ViaTeleport => (this.SubTrackType == QuestWalkTrackTypeDynamicMapResource
                                        && this.TrackType != QuestWalkTrackTypeDynamicMapResource)
                                       || this.NpcMissingHere;
        }

        private QuestWalkTrack questWalkTrack;
        private readonly List<Vector3> questWalkGamePath = new List<Vector3>();

        // ── Entry point (hotkey + the Daily Quests button) ───────────────────────────────────────

        internal void ToggleQuestWalk()
        {
            if (this.questWalkFollowing)
            {
                this.StopQuestWalk("toggled off");
                return;
            }

            // The farm owns the walker while it runs; two drivers would fight for the same movement
            // input every frame.
            if (this.autoFarmActive)
            {
                this.questWalkStatus = this.L("Stop Aura Farm first — it is driving the walker.");
                this.AddMenuNotification(this.questWalkStatus, new Color(1f, 0.55f, 0.55f));
                ModLogger.Msg("[QuestWalk] refused: Aura Farm is running.");
                return;
            }

            this.ReadQuestWalkTrack(out this.questWalkTrack);
            if (!this.questWalkTrack.Valid)
            {
                this.questWalkStatus = this.L("Nothing tracked — track a quest in the game first.");
                this.AddMenuNotification(this.questWalkStatus, new Color(1f, 0.55f, 0.55f));
                ModLogger.Msg("[QuestWalk] refused: no tracked item.");
                return;
            }

            if (!this.farmWalkToNodeEnabled)
            {
                // Through the mod's OWN toggle handler, never the raw field: it carries the mutual
                // exclusion with Stealth Foraging (which parks the player under the terrain, where
                // walking cannot work) and the 1x speed pin.
                this.OnUguiForagingWalkToggled(true);
                if (!this.farmWalkToNodeEnabled)
                {
                    this.questWalkStatus = this.L("Could not enable Walk to Nodes.");
                    this.AddMenuNotification(this.questWalkStatus, new Color(1f, 0.55f, 0.55f));
                    return;
                }
                ModLogger.Msg("[QuestWalk] enabled Walk to Nodes.");
            }

            this.questWalkFollowing = true;
            this.questWalkParked = false;
            this.questWalkNextStartAt = 0f;
            this.questWalkTrackLostAt = 0f;
            this.questWalkGameRouteRejected = false;
            this.questWalkErrorCount = 0;
            this.questWalkEpoch = AuraMonoWorldEpoch;

            float gap = this.QuestWalkDistance(this.questWalkTrack.Target);
            float radius = QuestWalkRadiusFor(this.questWalkTrack);
            ModLogger.Msg("[QuestWalk] following task " + this.questWalkTrack.TaskTableId
                + " at " + FormatNavMeshVector(this.questWalkTrack.Target)
                + " · " + QuestWalkTypeName(this.questWalkTrack.TrackType)
                + (this.questWalkTrack.ViaTeleport ? " (TELEPORT)" : string.Empty)
                + " → arrive within " + radius.ToString("F1") + "m"
                + " · " + gap.ToString("F1") + "m away.");

            // Pressing a button and watching nothing happen reads as broken — say it out loud.
            this.questWalkStatus = (gap >= 0f && gap <= radius)
                ? this.LF("Already there ({0}m).", gap.ToString("F1"))
                : this.LF("Walking to the quest point ({0}m).", gap.ToString("F0"));
            this.AddMenuNotification(this.questWalkStatus, new Color(0.45f, 1f, 0.55f));
        }

        private void StopQuestWalk(string why)
        {
            if (!this.questWalkFollowing)
            {
                return;
            }
            this.questWalkFollowing = false;
            this.questWalkParked = false;
            try
            {
                this.AbortFarmWalk();
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[QuestWalk] AbortFarmWalk threw: " + ex.Message);
            }
            this.questWalkStatus = this.LF("Stopped ({0}).", why);
            ModLogger.Msg("[QuestWalk] stopped (" + why + ").");
        }

        // ── Per-frame driver (called from OnUpdate) ──────────────────────────────────────────────

        private void ProcessQuestWalkOnUpdate()
        {
            if (!this.questWalkFollowing || this.questWalkErrorCount >= 3)
            {
                return;
            }

            try
            {
                // The world gate comes FIRST, before anything reads the game: during a swap the
                // modules we walk are being torn down and every cached position belongs to a map
                // that is going away (rule 4).
                if (!this.IsWorldReady)
                {
                    if (!this.questWalkWaitingForWorld)
                    {
                        this.questWalkWaitingForWorld = true;
                        this.AbortFarmWalk();
                        this.questWalkTrack = default(QuestWalkTrack);
                        this.questWalkStatus = this.L("World is changing — holding.");
                        ModLogger.Msg("[QuestWalk] world is changing — holding the follow, walk aborted.");
                    }
                    return;
                }

                int epoch = AuraMonoWorldEpoch;
                if (epoch != this.questWalkEpoch)
                {
                    this.questWalkEpoch = epoch;
                    this.OnQuestWalkWorldChanged(epoch);
                }

                if (this.questWalkWaitingForWorld)
                {
                    this.questWalkWaitingForWorld = false;
                    this.questWalkTrackLostAt = Time.unscaledTime;
                    ModLogger.Msg("[QuestWalk] world is ready again (epoch " + epoch + ") — re-acquiring the track.");
                }

                this.ReadQuestWalkTrack(out this.questWalkTrack);
                this.DriveQuestWalk();
            }
            catch (Exception ex)
            {
                this.questWalkErrorCount++;
                this.questWalkStatus = this.LF("Error ({0}/3): {1}", this.questWalkErrorCount.ToString(), ex.Message);
                ModLogger.Msg("[QuestWalk] tick error (" + this.questWalkErrorCount + "/3, disabled at 3): " + ex.Message);
            }
        }

        // Everything held is in the old map's coordinates: the walker's corners and target, the
        // parked point, the cached track, and the NPC probe's answer.
        private void OnQuestWalkWorldChanged(int epoch)
        {
            this.AbortFarmWalk();
            this.questWalkTrack = default(QuestWalkTrack);
            this.questWalkParked = false;
            this.questWalkAim = Vector3.zero;
            this.questWalkCandidateReads = 0;
            this.questWalkInjectedCorners = -1;
            this.questWalkLastCornerCount = -1;
            this.questWalkNextRetargetAt = 0f;
            this.questWalkNextStartAt = 0f;
            this.questWalkTrackLostAt = Time.unscaledTime;
            this.questWalkNpcProbeId = -1;
            this.questWalkStatus = this.L("World changed — waiting for the new track.");
            ModLogger.Msg("[QuestWalk] world changed (epoch " + epoch + ") — dropped every cached position.");
        }

        private void DriveQuestWalk()
        {
            float now = Time.unscaledTime;

            if (!this.farmWalkActive)
            {
                // A missing track is normal for a while after a transition — the game republishes
                // it once the new map settles. Only a track that STAYS missing means the quest is
                // really gone; stopping on the first empty read turns a world change into "the game
                // dropped the track".
                if (!this.questWalkTrack.Valid)
                {
                    if (this.questWalkTrackLostAt <= 0f)
                    {
                        this.questWalkTrackLostAt = now;
                        this.questWalkStatus = this.L("No track right now — waiting for it to come back.");
                    }
                    else if (now - this.questWalkTrackLostAt > QuestWalkTrackLostGrace)
                    {
                        this.StopQuestWalk("no track for " + QuestWalkTrackLostGrace.ToString("F0") + "s");
                    }
                    return;
                }
                this.questWalkTrackLostAt = 0f;

                // Parking means "delivered, and still there". Either side moving invalidates it:
                // the quest moving its point, or the player leaving the radius.
                if (this.questWalkParked)
                {
                    float away = this.QuestWalkDistance(this.questWalkTrack.Target);
                    bool samePoint = Vector3.Distance(this.questWalkTrack.Target, this.questWalkParkedAt) <= QuestWalkRetargetDistance;
                    bool stillThere = away >= 0f && away <= QuestWalkRadiusFor(this.questWalkTrack);
                    if (samePoint && stillThere)
                    {
                        return;
                    }
                    this.questWalkParked = false;
                    ModLogger.Msg("[QuestWalk] un-parked: " + (samePoint
                        ? "the player left the radius (" + away.ToString("F1") + "m out)"
                        : "the quest moved its point"));
                }

                if (now < this.questWalkNextStartAt)
                {
                    return;
                }

                float gap = this.QuestWalkDistance(this.questWalkTrack.Target);
                float radius = QuestWalkRadiusFor(this.questWalkTrack);
                if (gap >= 0f && gap <= radius)
                {
                    this.QuestWalkPark(gap, radius, walked: false);
                    return;
                }

                this.BeginQuestWalkLeg();
                return;
            }

            // Radius re-evaluated LIVE, not frozen at the start: whether the point is a portal is
            // only knowable once the player is inside streaming range of it.
            this.questWalkRadius = QuestWalkRadiusFor(this.questWalkTrack);

            float remaining = this.QuestWalkDistance(this.questWalkAim);
            if (remaining >= 0f && remaining <= this.questWalkRadius)
            {
                this.AbortFarmWalk();
                this.QuestWalkPark(remaining, this.questWalkRadius, walked: true);
                return;
            }

            if (this.questWalkTrack.Valid && this.ShouldReaimQuestWalk(now))
            {
                ModLogger.Msg("[QuestWalk] re-aiming: target moved "
                    + Vector3.Distance(this.questWalkTrack.Target, this.questWalkAim).ToString("F1")
                    + "m -> " + FormatNavMeshVector(this.questWalkTrack.Target));
                this.questWalkNextRetargetAt = now + QuestWalkRetargetCooldown;
                this.AbortFarmWalk();
                this.BeginQuestWalkLeg();
                return;
            }

            // While we own the route there is no point letting the walker rebuild its own every
            // 12 s: it discards ours, we put ours back, and the two spend the walk overwriting each
            // other. Only meaningful when a game route was actually injected.
            if (this.questWalkInjectedCorners > 0)
            {
                this.farmWalkNextRepathAt = now + 10f;
            }

            // Corner count changes only on a re-path (advancing moves an index, not the list), so a
            // count that differs from what we injected IS the re-path signal.
            int corners = this.farmWalkCorners.Count;
            if (corners != this.questWalkLastCornerCount)
            {
                this.questWalkLastCornerCount = corners;
                if (corners != this.questWalkInjectedCorners && this.questWalkInjectedCorners > 0
                    && this.questWalkTrack.HasGameRoute && this.questWalkGamePath.Count > 1)
                {
                    int n = this.InjectQuestWalkRoute();
                    if (n > 0)
                    {
                        this.questWalkInjectedCorners = n;
                        this.questWalkLastCornerCount = n;
                    }
                }
            }

            this.questWalkStatus = this.LF("Walking ({0}m, {1} corners).",
                remaining.ToString("F0"), corners.ToString());

            if (this.RunFarmWalkTick())
            {
                // Reached only when the WALKER finished first — its own 0.25 m test, or it gave up
                // and teleported. Our radius is looser, so this is the abnormal exit.
                float ended = this.QuestWalkDistance(this.questWalkAim);
                ModLogger.Msg("[QuestWalk] the walker ended the leg itself at " + ended.ToString("F1")
                    + "m (radius " + this.questWalkRadius.ToString("F1") + "m).");
                this.QuestWalkPark(ended, this.questWalkRadius, walked: true);
            }
        }

        private void QuestWalkPark(float gap, float radius, bool walked)
        {
            this.questWalkParked = true;
            this.questWalkParkedAt = this.questWalkTrack.Valid ? this.questWalkTrack.Target : this.questWalkAim;
            this.questWalkNextStartAt = Time.unscaledTime + QuestWalkStartCooldown;
            if (walked)
            {
                this.questWalkArrivals++;
            }

            bool portal = this.questWalkTrack.ViaTeleport;
            this.questWalkStatus = portal
                ? this.LF("At the portal ({0}m) — waiting for the world to change.", gap.ToString("F1"))
                : this.LF("Arrived ({0}m). Waiting for the quest to move the point.", gap.ToString("F1"));
            ModLogger.Msg("[QuestWalk] " + (walked ? "arrived: " : "already within radius: ")
                + gap.ToString("F1") + "m of the " + QuestWalkTypeName(this.questWalkTrack.TrackType)
                + " target (radius " + radius.ToString("F1") + "m); parked"
                + (portal ? " — this point is a TELEPORT: expecting a world change, then a new track" : string.Empty));
        }

        // ⚠️ SOME TRACK POINTS CARRY NO HEIGHT. The game stores a few of them with Position.y left at
        // exactly 0 — XZ only — and everything downstream then reasons about a destination at sea
        // level. One walk logged
        //     target=(-95,97, 0,00, 197,56)   dyAim=-24,4m dyNode=-24,4m
        // with the player standing at y 24.4: the walker believed the point was twenty-four metres
        // BELOW it, so the 3-D arrival test (1.8 m) could never pass, the vertical logic aimed down,
        // and the on-foot escape was handed a cliff to descend that no jump can solve. The point was
        // in fact above.
        //
        // A y of exactly 0 is the signature; real heights in this world are metres away from it. Take
        // the height from the nearest waypoint of the track graph instead — those positions are real
        // — and fall back to the player's own height, which is a far better guess than sea level.
        private Vector3 RepairQuestWalkAimHeight(Vector3 aim)
        {
            if (Mathf.Abs(aim.y) > 0.001f)
            {
                return aim;
            }

            if (this.TryGetNearestTrackGraphNodePosition(aim, 120f, out Vector3 nodePos))
            {
                ModLogger.Msg("[QuestWalk] track point has no height (y=0) — taking "
                    + nodePos.y.ToString("F1") + "m from the nearest waypoint "
                    + Vector3.Distance(new Vector3(aim.x, nodePos.y, aim.z), nodePos).ToString("F1") + "m away.");
                aim.y = nodePos.y;
                return aim;
            }

            if (this.TryGetLocalPlayerPosition(out Vector3 me))
            {
                ModLogger.Msg("[QuestWalk] track point has no height (y=0) and no waypoint nearby — "
                    + "using the player's own height " + me.y.ToString("F1") + "m.");
                aim.y = me.y;
            }

            return aim;
        }

        private void BeginQuestWalkLeg()
        {
            this.questWalkAim = this.RepairQuestWalkAimHeight(this.questWalkTrack.Target);
            this.questWalkRadius = QuestWalkRadiusFor(this.questWalkTrack);
            this.questWalkCandidateReads = 0;
            this.questWalkParked = false;
            this.questWalkNextStartAt = Time.unscaledTime + QuestWalkStartCooldown;
            this.questWalkInjectedCorners = -1;

            if (!this.TryBeginFarmWalk(this.questWalkAim, "quest:" + this.questWalkTrack.TaskTableId, true, null))
            {
                this.questWalkStatus = this.L("The walker refused this point — see the log.");
                return;
            }

            // The game's route is reused ONLY when the module is routing at the item we picked.
            // When it is drawing a different track, the walker's own route is the correct one —
            // injecting somebody else's would aim us at their target.
            if (this.questWalkTrack.HasGameRoute && this.questWalkGamePath.Count > 1)
            {
                int n = this.InjectQuestWalkRoute();
                if (n > 0)
                {
                    this.questWalkInjectedCorners = n;
                }
            }

            this.questWalkLastCornerCount = this.farmWalkCorners.Count;
        }

        // Replaces the walker's corners with the game's route.
        //
        // ⚠️ ENTRY IS THE NEAREST POINT, NOT ZERO (rule 2). The resample also stops short of the
        // real target (its loop ends at t = 0.98), so the aim point is appended — which is exactly
        // what the game's own GetPath2 does.
        private int InjectQuestWalkRoute()
        {
            if (this.questWalkGamePath.Count == 0)
            {
                return -1;
            }

            // ⚠️ ONCE REJECTED, STAY REJECTED — AND THAT MEANS NOT INJECTING AT ALL.
            //
            // The latch only guarded the AUDIT, not the injection: the corners were overwritten with
            // the game's route first and judged afterwards. So the very next path update — and the
            // game updates its route every few seconds — put the rejected straight line back over
            // the route we had just built to go around. The log said "routed around it: 5 corners of
            // our own" while the character walked the straight one, because by then it was walking
            // the straight one again.
            if (this.questWalkGameRouteRejected)
            {
                return -1;
            }

            int start = 1;
            if (this.TryGetLocalPlayerPosition(out Vector3 me))
            {
                int nearest = 0;
                float best = float.MaxValue;
                for (int i = 0; i < this.questWalkGamePath.Count; i++)
                {
                    float d = (this.questWalkGamePath[i] - me).sqrMagnitude;
                    if (d < best)
                    {
                        best = d;
                        nearest = i;
                    }
                }
                start = nearest + 1;
            }

            this.farmWalkCorners.Clear();
            for (int i = start; i < this.questWalkGamePath.Count; i++)
            {
                this.farmWalkCorners.Add(this.questWalkGamePath[i]);
            }
            this.farmWalkCorners.Add(this.questWalkAim);
            this.farmWalkCornerIndex = 0;

            // The progress baselines were computed from the route just thrown away. Re-base with the
            // SAME formula, or the "not approaching" detector fires on the difference.
            if (this.TryGetLocalPlayerPosition(out Vector3 here))
            {
                float remaining = this.ComputeFarmWalkRouteRemaining(here);
                this.farmWalkBestDistance = remaining;
                this.farmWalkRouteRemainingCache = remaining;
            }

            // ⚠️ THE GAME'S ROUTE IS NOT CHECKED BY ANYONE EITHER.
            //
            // It arrives whole and bypasses TryBuildFarmWalkRoute, so the leg passability test added
            // there never sees it — and the game's route has the same defect. Audited 2026-08-21 on
            // a live quest walk:
            //     leg 0: 13.2m  WALL: the ground steps up 2.13m at 2.4m along  sweep=BLOCKED
            //            | rises -0.4m over the leg
            // 2.13 m against a jump that peaks at 1.42 m, on a leg that DESCENDS overall — so not a
            // slope read as a wall, but real geometry across the path.
            //
            // There is no waypoint to ban here (these are the game's spline points, not graph nodes),
            // so the remedy is different: drop the game's route and build our own, which does know
            // how to route around a banned node. The latch matters — the injection is re-run every
            // time the game's path updates, and without it the rejected route would come straight
            // back on the next tick.
            if (!this.questWalkGameRouteRejected
                && this.TryGetLocalPlayerPosition(out Vector3 auditFrom)
                && this.FindFirstImpassableFarmWalkLeg(auditFrom, this.farmWalkCorners, out string legDetail,
                    includeFirstLeg: true) >= 0)
            {
                this.questWalkGameRouteRejected = true;
                ModLogger.Msg("[QuestWalk] the game's route is not walkable — " + legDetail
                    + ". Building our own instead.");

                if (this.TryBuildFarmWalkRoute(auditFrom, this.questWalkAim))
                {
                    float rebased = this.ComputeFarmWalkRouteRemaining(auditFrom);
                    this.farmWalkBestDistance = rebased;
                    this.farmWalkRouteRemainingCache = rebased;
                    ModLogger.Msg("[QuestWalk] routed around it: " + this.farmWalkCorners.Count
                        + " corners of our own.");
                    return this.farmWalkCorners.Count;
                }

                // Nothing better on offer. Keep the game's route — a blocked route the escape ladder
                // can chew on beats no route at all — and say so rather than failing quietly.
                ModLogger.Msg("[QuestWalk] no route of our own either — keeping the game's blocked one.");
                this.InjectQuestWalkRouteCorners();
            }

            ModLogger.Msg("[QuestWalk] injected the game's route: " + this.farmWalkCorners.Count + " corners.");
            return this.farmWalkCorners.Count;
        }

        // The corner-filling half of the injection on its own, so the fallback can put the game's
        // route back after TryBuildFarmWalkRoute has overwritten the corner list and failed.
        private void InjectQuestWalkRouteCorners()
        {
            int start = 1;
            if (this.TryGetLocalPlayerPosition(out Vector3 me))
            {
                int nearest = 0;
                float best = float.MaxValue;
                for (int i = 0; i < this.questWalkGamePath.Count; i++)
                {
                    float d = (this.questWalkGamePath[i] - me).sqrMagnitude;
                    if (d < best)
                    {
                        best = d;
                        nearest = i;
                    }
                }

                start = nearest + 1;
            }

            this.farmWalkCorners.Clear();
            for (int i = start; i < this.questWalkGamePath.Count; i++)
            {
                this.farmWalkCorners.Add(this.questWalkGamePath[i]);
            }

            this.farmWalkCorners.Add(this.questWalkAim);
            this.farmWalkCornerIndex = 0;
        }

        // A "collect" quest tracks a SPECIFIC nearby entity, and the game re-picks the nearest as
        // the player moves — an apple grove made the target flip between three of them ~25 m apart,
        // and since every flip restarted the walk the final approach never completed. Three guards:
        // the new target must hold still, re-aims obey a cooldown, and inside the final approach the
        // target is LOCKED so a nearly-finished walk is never abandoned.
        private bool ShouldReaimQuestWalk(float now)
        {
            if (now < this.questWalkNextRetargetAt)
            {
                return false;
            }

            if (Vector3.Distance(this.questWalkTrack.Target, this.questWalkAim) <= QuestWalkRetargetDistance)
            {
                this.questWalkCandidateReads = 0;
                return false;
            }

            float toAim = this.QuestWalkDistance(this.questWalkAim);
            if (toAim >= 0f && toAim <= QuestWalkFinalApproachLock)
            {
                this.questWalkCandidateReads = 0;
                return false;
            }

            if (this.questWalkCandidateReads > 0
                && Vector3.Distance(this.questWalkTrack.Target, this.questWalkCandidate) <= 1f)
            {
                this.questWalkCandidateReads++;
            }
            else
            {
                this.questWalkCandidate = this.questWalkTrack.Target;
                this.questWalkCandidateReads = 1;
            }

            return this.questWalkCandidateReads >= QuestWalkRetargetStableReads;
        }

        // ── Geometry ─────────────────────────────────────────────────────────────────────────────

        // FLAT on land, matching how the walker measures its own arrival — terrain routinely puts
        // metres of Y between a player and a point they are standing on. 3-D in the underwater
        // WORLD, where height is a real axis rather than terrain noise (rule 5).
        private float QuestWalkDistance(Vector3 to)
        {
            if (!this.TryGetLocalPlayerPosition(out Vector3 me))
            {
                return -1f;
            }
            float dx = to.x - me.x;
            float dz = to.z - me.z;
            float flat = Mathf.Sqrt(dx * dx + dz * dz);
            if (!this.IsQuestWalkUnderwaterWorld())
            {
                return flat;
            }
            float dy = to.y - me.y;
            return Mathf.Sqrt(flat * flat + dy * dy);
        }

        private static float QuestWalkRadiusFor(QuestWalkTrack track)
        {
            if (track.ViaTeleport && track.TrackType == QuestWalkTrackTypeNpc)
            {
                return QuestWalkArrivePortalNpc;
            }
            switch (track.TrackType)
            {
                case QuestWalkTrackTypeField: return QuestWalkArriveField;
                case QuestWalkTrackTypeArea: return QuestWalkArriveArea;
                default: return QuestWalkArrivePoint;
            }
        }

        private static string QuestWalkTypeName(int trackType)
        {
            switch (trackType)
            {
                case QuestWalkTrackTypeNpc: return "Npc";
                case QuestWalkTrackTypeField: return "Field";
                case 8: return "MapResource";
                case 14: return "Furniture";
                case QuestWalkTrackTypeArea: return "Area";
                case 0: return "unknown";
                default: return "type " + trackType;
            }
        }

        private bool IsQuestWalkUnderwaterWorld()
        {
            try
            {
                if (this.questWalkDataCenterClass == IntPtr.Zero)
                {
                    this.questWalkDataCenterClass = this.FindAuraMonoClassAnySpelling(
                        "XDTDataAndProtocol.ComponentsData.DataCenter");
                    if (this.questWalkDataCenterClass == IntPtr.Zero)
                    {
                        return false;
                    }
                }

                // The helper carries the login-screen guard itself: a raw static read on a class the
                // login screen never initialised is an uncatchable AV.
                return this.TryReadAuraMonoStaticIntField(this.questWalkDataCenterClass,
                           new[] { "LevelId" }, out int level)
                       && level == QuestWalkRoomLevelSeaWorld;
            }
            catch
            {
                return false;
            }
        }

        // ── Reading the game's track ─────────────────────────────────────────────────────────────

        private void ReadQuestWalkTrack(out QuestWalkTrack track)
        {
            track = default(QuestWalkTrack);
            this.questWalkGamePath.Clear();

            IntPtr pathModule = IntPtr.Zero;
            ulong moduleToken = 0UL;
            if (this.TryResolveAuraMonoModule("XDTLevelAndEntity.GameplaySystem.TrackingPoint.TrackingPathModule",
                    out pathModule)
                && pathModule != IntPtr.Zero
                && this.TryGetMonoBoolMember(pathModule, "isNavigating", out bool navigating) && navigating)
            {
                moduleToken = unchecked((ulong)Marshal.ReadInt64(pathModule, 112)); // TrackingPathModule.token
            }

            if (!this.TryPickQuestWalkItem(moduleToken, ref track))
            {
                return;
            }

            if (track.HasGameRoute && pathModule != IntPtr.Zero)
            {
                this.ReadQuestWalkGamePath(pathModule);
            }

            track.NpcMissingHere = this.IsQuestWalkNpcAbsentHere(track);
            track.Valid = true;
        }

        // Not "whatever the path module is drawing" — rule 1. A TrackReason.Task item always wins;
        // between two of the same kind, the one the module is already routing to wins, because that
        // one comes with a free route.
        private bool TryPickQuestWalkItem(ulong moduleToken, ref QuestWalkTrack track)
        {
            if (!this.TryResolveAuraMonoModule("XDTGameSystem.GameplaySystem.Navigation.TrackingSystem",
                    out IntPtr system) || system == IntPtr.Zero)
            {
                return false;
            }
            if (!this.TryGetMonoObjectMember(system, "_trackingItems", out IntPtr dictionary)
                || dictionary == IntPtr.Zero)
            {
                return false;
            }
            if (!this.TryGetMonoObjectMember(dictionary, "Values", out IntPtr values) || values == IntPtr.Zero)
            {
                return false;
            }

            List<IntPtr> items = new List<IntPtr>();
            List<uint> pins = new List<uint>();
            bool found = false;
            bool foundIsQuest = false;
            try
            {
                if (!this.TryEnumerateAuraMonoCollectionItems(values, items, pins, QuestWalkMaxTrackingItems))
                {
                    return false;
                }

                for (int i = 0; i < items.Count; i++)
                {
                    IntPtr it = items[i];
                    if (it == IntPtr.Zero)
                    {
                        continue;
                    }

                    bool isQuest = Marshal.ReadByte(it, QuestWalkOffTrackReason) == QuestWalkTrackReasonTask;
                    ulong token = unchecked((ulong)Marshal.ReadInt64(it, QuestWalkOffToken));
                    bool better = !found
                                  || (isQuest && !foundIsQuest)
                                  || (isQuest == foundIsQuest && token == moduleToken && !track.HasGameRoute);
                    if (!better)
                    {
                        continue;
                    }

                    track.Token = token;
                    track.Target = new Vector3(
                        ReadQuestWalkFloat(it, QuestWalkOffPosition),
                        ReadQuestWalkFloat(it, QuestWalkOffPosition + 4),
                        ReadQuestWalkFloat(it, QuestWalkOffPosition + 8));
                    track.TargetNetId = unchecked((uint)Marshal.ReadInt32(it, QuestWalkOffTargetNetId));
                    track.StaticId = Marshal.ReadInt32(it, QuestWalkOffStaticId);
                    track.TrackType = Marshal.ReadByte(it, QuestWalkOffTrackType);
                    track.TaskTableId = Marshal.ReadInt32(it, QuestWalkOffTaskTableId);
                    track.SubTrackType = Marshal.ReadInt32(it, QuestWalkOffSubTrackType);
                    track.HasGameRoute = token == moduleToken && moduleToken != 0UL;
                    track.IsQuestItem = isQuest;
                    found = true;
                    foundIsQuest = isQuest;
                }

                track.ItemCount = items.Count;
            }
            finally
            {
                for (int i = 0; i < pins.Count; i++)
                {
                    AuraMonoPinFree(pins[i]);
                }
            }

            return found;
        }

        private void ReadQuestWalkGamePath(IntPtr pathModule)
        {
            if (!this.TryGetMonoObjectMember(pathModule, "_path", out IntPtr list) || list == IntPtr.Zero)
            {
                return;
            }
            if (!this.TryGetMonoIntMember(list, "Count", out int count) || count <= 0)
            {
                return;
            }
            for (int i = 0; i < count; i++)
            {
                if (this.TryGetAuraMonoListVector3Item(list, i, out Vector3 point))
                {
                    this.questWalkGamePath.Add(point);
                }
            }
        }

        // The only portal test that works on a quest track. Two halves, and the second IS the rule:
        // the NPC does not resolve here, AND the point is close enough that it would have streamed
        // in if the NPC were there. Both resolvers see only streamed NPCs and no API can ask the
        // server about a distant one, so a bare miss proves nothing.
        private bool IsQuestWalkNpcAbsentHere(QuestWalkTrack track)
        {
            if (track.TrackType != QuestWalkTrackTypeNpc || track.StaticId <= 0)
            {
                this.questWalkNpcProbeId = -1;
                return false;
            }

            float gap = this.QuestWalkDistance(track.Target);
            if (gap < 0f || gap > QuestWalkNpcStreamingRange)
            {
                return false; // too far to conclude anything either way
            }

            int epoch = AuraMonoWorldEpoch;
            float now = Time.unscaledTime;
            bool stale = track.StaticId != this.questWalkNpcProbeId
                         || epoch != this.questWalkNpcProbeEpoch
                         || now - this.questWalkNpcProbeAt > QuestWalkNpcProbeTtl;
            if (!stale)
            {
                return this.questWalkNpcMissing;
            }

            bool present = this.QuestAssistantTryGetNpcNetIdAuraMono(track.StaticId, out _, out _)
                           || this.QuestAssistantTryGetNpcNetIdViaComponentScan(track.StaticId, out _, out _);
            bool worthSaying = present == this.questWalkNpcMissing
                               || track.StaticId != this.questWalkNpcProbeId
                               || epoch != this.questWalkNpcProbeEpoch;

            this.questWalkNpcProbeId = track.StaticId;
            this.questWalkNpcProbeEpoch = epoch;
            this.questWalkNpcProbeAt = now;
            this.questWalkNpcMissing = !present;

            if (worthSaying)
            {
                ModLogger.Msg("[QuestWalk] npc " + track.StaticId + " in this world (epoch " + epoch + "): "
                    + (present ? "yes" : "NO — the point is a stand-in for a transfer"));
            }

            return this.questWalkNpcMissing;
        }

        private static float ReadQuestWalkFloat(IntPtr p, int offset)
        {
            return BitConverter.Int32BitsToSingle(Marshal.ReadInt32(p, offset));
        }

        // ── Status for the Daily Quests page ─────────────────────────────────────────────────────

        internal string BuildQuestWalkSummary()
        {
            if (!this.questWalkFollowing)
            {
                return this.questWalkStatus ?? "Idle.";
            }

            QuestWalkTrack t = this.questWalkTrack;
            if (!t.Valid)
            {
                return this.questWalkStatus ?? this.L("Following — no track right now.");
            }

            float gap = this.QuestWalkDistance(t.Target);
            return (t.TaskTableId != 0 ? "task " + t.TaskTableId : this.L("no task id"))
                + " · " + QuestWalkTypeName(t.TrackType)
                + (t.ViaTeleport ? " → " + this.L("portal") : string.Empty)
                + " · " + gap.ToString("F0") + "m / " + QuestWalkRadiusFor(t).ToString("F1") + "m"
                + (this.questWalkParked ? " · " + this.L("parked") : string.Empty);
        }
    }
}
