using System;
using UnityEngine;

namespace HeartopiaMod
{
    // The route the in-game "Track" star-line follows — reachability probe.
    //
    // BACKGROUND: the navmesh hypothesis is DEAD. NavMeshWalkFeature's probe returned
    // PathInvalid/corners=0 with no mesh within 5 m at every distance from 9 m to 119 m, because
    // XDNavigationMgr.LoadNavMeshDataSync has ZERO callers in the Mono dump — nothing ever feeds
    // UnityEngine.AI, so the NavMeshData/NavMeshDataInstance fields visible in the IL2CPP dump are
    // dead scaffolding.
    //
    // What Track actually does (ilspy-dumps/XDTLevelAndEntity/...GameplaySystem.TrackingPoint/):
    //   TrackPathConfig            — the graph: Points[] of { position, neighbour[] }
    //   AStar.Init(config)         — builds nodes + a QuadTree over Bounds(0, 2000x100x2000)
    //   AStar.GetPath(a, b, ignoreY) — public A*, returns List<Vector3> of corners
    //   TrackingPathModule         — orchestrates, then draws one star per 0.5 m (perInstanceDis)
    // All Mono, so all AuraMono-reachable, and all client-side (the server only sends the target
    // POSITION in TrackData — it never sends a polyline).
    //
    // THIS FILE IS STILL ONLY A PROBE. The navmesh detour cost a build because an API that existed
    // was assumed to work; the same discipline applies here, so nothing walks until the log shows a
    // node count and a real corner list. Two independent routes to the graph are probed:
    //
    //   Route A (preferred) — Managers._serviceDic -> ConfigManager -> MainGameLvlConf
    //       -> TrackPathConditionConfig -> GetTrackPathConfig() -> Points.
    //     Preferred because it lets the mod build its OWN AStar and never touch the live module,
    //     so the player's visible Track line is never disturbed.
    //
    //   Route B (fallback) — Managers.GetModule(typeof(TrackingPathModule)) -> _aStar -> nodes.
    //     Cheaper (the graph is already built) but _aStar is NULL until a RefreshTrackPath event
    //     fires and is nulled again in OnRelease, so it cannot be relied on standing alone.
    //
    // Crash-safety notes for anything built on this later:
    //   * AStar.GetPath needs EXACT node positions — it does nodes.Find(n => n.position == start)
    //     and dereferences the result, so a raw player position is an instant NullReferenceException.
    //   * TrackingPathModule.FindNearestPoint is public but has an `out Vector3` — a struct out-param
    //     through mono_runtime_invoke corrupts the stack (see AGENTS.md §12). Do the snap in C#.
    //   * AStar.GetNeighbour takes a `ref HashSet<AStarNode>`; constructing that generic through
    //     AuraMono is a documented crash trap. Snapshot Nodes into managed memory instead.
    public partial class HeartopiaComplete
    {
        // TrackingPathModule's namespace lives in XDTLevelAndEntity. Namespace != assembly here, so
        // the image list is explicit (FindAuraMonoClassByFullName picks only the first image and
        // has historically guessed wrong — that is the TrackModule/BuildModule crash lineage).
        private static readonly string[] TrackPathModuleImageNames =
        {
            "XDTLevelAndEntity", "XDTLevelAndEntity.dll",
            "XDTGameUI", "XDTGameUI.dll",
            "Client", "Client.dll",
            "Assembly-CSharp", "Assembly-CSharp.dll"
        };

        // XDTGame.Framework.Managers lives in XDTBaseService; pin it so an unrelated "Managers"
        // class cannot be picked (a wrong pick makes GetModule silently return null).
        private static readonly string[] TrackPathManagersImageNames =
        {
            "XDTBaseService", "XDTBaseService.dll",
            "XDTLevelAndEntity", "XDTLevelAndEntity.dll",
            "Client", "Client.dll",
            "Assembly-CSharp", "Assembly-CSharp.dll"
        };

        // One report per farm run (reset alongside the navmesh probe).
        private bool trackGraphProbeLogged;

        private void ResetTrackPathGraphProbeState()
        {
            this.trackGraphProbeLogged = false;
        }

        // Fired once per Aura Farm run, from the same hop chokepoint as the navmesh probe.
        private void ProbeTrackPathGraph()
        {
            if (this.trackGraphProbeLogged)
            {
                return;
            }

            this.trackGraphProbeLogged = true;

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
                {
                    ModLogger.Msg("[TrackGraph] AuraMono not ready — probe skipped.");
                    return;
                }

                this.ProbeTrackPathGraphViaConfig();
                this.ProbeTrackPathGraphViaModule();
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[TrackGraph] probe aborted: " + ex);
            }
        }

        // Route A — ConfigManager -> MainGameLvlConf -> TrackPathConditionConfig -> GetTrackPathConfig().
        private void ProbeTrackPathGraphViaConfig()
        {
            if (!this.TryGetAuraMonoManagerFromServiceDic("ConfigManager", out IntPtr configManager) || configManager == IntPtr.Zero)
            {
                ModLogger.Msg("[TrackGraph] A: ConfigManager not found in Managers._serviceDic.");
                return;
            }

            if (!this.TryGetMonoObjectMember(configManager, "MainGameLvlConf", out IntPtr levelConfig) || levelConfig == IntPtr.Zero)
            {
                ModLogger.Msg("[TrackGraph] A: ConfigManager found, but MainGameLvlConf is null/unreadable.");
                return;
            }

            if (!this.TryGetMonoObjectMember(levelConfig, "TrackPathConditionConfig", out IntPtr conditionConfig) || conditionConfig == IntPtr.Zero)
            {
                ModLogger.Msg("[TrackGraph] A: MainGameLvlConf reached, but TrackPathConditionConfig is null/unreadable.");
                return;
            }

            // Tuning values worth having in the log — the walker reuses the game's own thresholds
            // rather than inventing new ones.
            this.TryGetMonoSingleMember(conditionConfig, "deviateDis", out float deviateDis);
            this.TryGetMonoSingleMember(conditionConfig, "tryLineConnectDis", out float tryLineConnectDis);
            this.TryGetMonoBoolMember(conditionConfig, "DisIgnoreY", out bool ignoreY);

            if (!this.TryInvokeAuraMonoZeroArg(conditionConfig, out IntPtr trackPathConfig, "GetTrackPathConfig")
                || trackPathConfig == IntPtr.Zero)
            {
                ModLogger.Msg("[TrackGraph] A: GetTrackPathConfig() returned null.");
                return;
            }

            if (!this.TryGetMonoObjectMember(trackPathConfig, "Points", out IntPtr pointList) || pointList == IntPtr.Zero)
            {
                ModLogger.Msg("[TrackGraph] A: TrackPathConfig reached, but Points is null/unreadable.");
                return;
            }

            if (!this.TryGetMonoIntMember(pointList, "Count", out int pointCount))
            {
                ModLogger.Msg("[TrackGraph] A: Points reached, but Count is unreadable.");
                return;
            }

            ModLogger.Msg("[TrackGraph] A: OK — waypoint graph has " + pointCount + " points"
                + " (deviateDis=" + deviateDis.ToString("F1") + "sqr"
                + ", tryLineConnectDis=" + tryLineConnectDis.ToString("F1") + "sqr"
                + ", ignoreY=" + ignoreY + ").");

            if (pointCount <= 0)
            {
                return;
            }

            // Sample a handful of points spread across the list: position + neighbour count is
            // everything the snapshot walk will need, so this proves the whole read path at once.
            // Their distance from the player also says whether the graph covers where we farm.
            bool haveSelf = this.TryGetNavMeshSelfPosition(out Vector3 selfPos, out _);
            int sampled = 0;
            int neighbourTotal = 0;
            float nearestSqr = float.MaxValue;

            int step = Mathf.Max(1, pointCount / 6);
            for (int i = 0; i < pointCount && sampled < 6; i += step)
            {
                if (!this.TryGetAuraMonoListItem(pointList, i, out IntPtr pointObj) || pointObj == IntPtr.Zero)
                {
                    ModLogger.Msg("[TrackGraph] A: Points[" + i + "] unreadable — indexer path failed.");
                    return;
                }

                if (!this.TryGetMonoVector3Member(pointObj, "position", out Vector3 pointPos))
                {
                    ModLogger.Msg("[TrackGraph] A: Points[" + i + "].position unreadable.");
                    return;
                }

                int neighbourCount = 0;
                if (this.TryGetMonoObjectMember(pointObj, "neighbour", out IntPtr neighbourList) && neighbourList != IntPtr.Zero)
                {
                    this.TryGetMonoIntMember(neighbourList, "Count", out neighbourCount);
                }

                neighbourTotal += neighbourCount;
                sampled++;

                if (haveSelf)
                {
                    float dx = pointPos.x - selfPos.x;
                    float dz = pointPos.z - selfPos.z;
                    float sqr = dx * dx + dz * dz;
                    if (sqr < nearestSqr)
                    {
                        nearestSqr = sqr;
                    }
                }

                ModLogger.Msg("[TrackGraph] A: Points[" + i + "] pos=" + FormatNavMeshVector(pointPos)
                    + " neighbours=" + neighbourCount);
            }

            if (haveSelf && nearestSqr < float.MaxValue)
            {
                ModLogger.Msg("[TrackGraph] A: nearest SAMPLED point is " + Mathf.Sqrt(nearestSqr).ToString("F1")
                    + "m away (sampled " + sampled + " of " + pointCount
                    + ", so this is an upper bound — the true nearest is closer).");
            }

            ModLogger.Msg("[TrackGraph] A: read path verified — " + sampled + " points sampled, "
                + neighbourTotal + " neighbour links total. A private AStar can be built from this.");
        }

        // Route B — Managers.GetModule(typeof(TrackingPathModule)) -> _aStar -> nodes.
        private unsafe void ProbeTrackPathGraphViaModule()
        {
            if (auraMonoObjectGetClass == null || auraMonoClassGetType == null || auraMonoTypeGetObject == null
                || this.auraMonoRootDomain == IntPtr.Zero)
            {
                ModLogger.Msg("[TrackGraph] B: mono type API unavailable — skipped.");
                return;
            }

            IntPtr moduleClass = this.FindAuraMonoClassInImages(
                "XDTLevelAndEntity.GameplaySystem.TrackingPoint", "TrackingPathModule", TrackPathModuleImageNames);
            if (moduleClass == IntPtr.Zero)
            {
                ModLogger.Msg("[TrackGraph] B: TrackingPathModule class not found in images.");
                return;
            }

            IntPtr monoType = auraMonoClassGetType(moduleClass);
            IntPtr typeObj = monoType != IntPtr.Zero ? auraMonoTypeGetObject(this.auraMonoRootDomain, monoType) : IntPtr.Zero;
            if (typeObj == IntPtr.Zero)
            {
                ModLogger.Msg("[TrackGraph] B: Type object unavailable.");
                return;
            }

            IntPtr managersClass = this.FindAuraMonoClassInImages("XDTGame.Framework", "Managers", TrackPathManagersImageNames);
            IntPtr getModuleMethod = managersClass != IntPtr.Zero
                ? this.FindAuraMonoMethodOnHierarchy(managersClass, "GetModule", 1)
                : IntPtr.Zero;
            if (getModuleMethod == IntPtr.Zero)
            {
                ModLogger.Msg("[TrackGraph] B: Managers.GetModule(Type) not found.");
                return;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = typeObj;
            IntPtr moduleObj = auraMonoRuntimeInvoke(getModuleMethod, IntPtr.Zero, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || moduleObj == IntPtr.Zero)
            {
                ModLogger.Msg("[TrackGraph] B: GetModule returned null (module not active in this level).");
                return;
            }

            if (!this.TryGetMonoObjectMember(moduleObj, "_aStar", out IntPtr aStarObj) || aStarObj == IntPtr.Zero)
            {
                ModLogger.Msg("[TrackGraph] B: module live, but _aStar is null "
                    + "(expected — it is only built when a RefreshTrackPath event fires).");
                return;
            }

            if (!this.TryGetMonoObjectMember(aStarObj, "nodes", out IntPtr nodesObj) || nodesObj == IntPtr.Zero
                || !this.TryGetMonoIntMember(nodesObj, "Count", out int nodeCount))
            {
                ModLogger.Msg("[TrackGraph] B: _aStar live, but nodes is unreadable.");
                return;
            }

            ModLogger.Msg("[TrackGraph] B: OK — live AStar already built with " + nodeCount + " nodes.");
        }

        // ── GRAPH SNAPSHOT ─────────────────────────────────────────────────
        // PROBE RESULT (2026-08-14, in-world): Route A returned 1745 points with 3–7 neighbours
        // each, spread over x −52..184 / z −103..232 — the graph is real, global to the level, and
        // fully readable. Route B confirmed `_aStar` is null with no active track, so the mod builds
        // its own. Two LIVE config values differ from the dump defaults and are used below:
        //   ignoreY = FALSE          → A* costs are true 3-D metres (NOT squared XZ; the dump's
        //                              class default of true would have meant the opposite)
        //   deviateDis = 16 sqr      → re-path once 4 m off the corridor
        //   tryLineConnectDis = 100 sqr → the game shortcuts straight under 10 m
        //
        // The graph is snapshotted into MANAGED arrays once per world and A* then runs in pure C#.
        // That keeps AuraMono entirely out of the per-frame walking path: no mono pointer is held
        // across a frame, so the moving-GC stale-pointer failure mode cannot apply to the walker.
        private Vector3[] trackGraphPositions;
        private int[][] trackGraphNeighbours;
        private bool trackGraphReady;
        private bool trackGraphBuildFailed;
        private float trackGraphNextBuildAt;

        // A* scratch, allocated once and reused (1745 nodes — an open-list scan is what the game
        // itself does, so no heap is worth the complexity).
        private float[] trackGraphGCost;
        private int[] trackGraphParent;
        private bool[] trackGraphClosed;
        private readonly System.Collections.Generic.List<int> trackGraphOpen = new System.Collections.Generic.List<int>(64);

        internal void InvalidateTrackPathGraph()
        {
            this.trackGraphReady = false;
            this.trackGraphBuildFailed = false;
            this.trackGraphPositions = null;
            this.trackGraphNeighbours = null;
            this.trackGraphNextBuildAt = 0f;
        }

        // True once the managed graph is usable. Retry-throttled so a level without a track graph
        // costs one attempt every 10 s rather than one per hop.
        private bool trackGraphInvalidationRegistered;

        private bool EnsureTrackPathGraph()
        {
            // The graph is per-level (TrackPathConditionConfig hangs off MainGameLvlConf), so a
            // world change must drop it — otherwise the walker would route the new map along the
            // old map's waypoints. Registered lazily; RegisterWorldLoadingStartedCallback fires on
            // the loading splash, before any node of the new world is ever targeted.
            if (!this.trackGraphInvalidationRegistered)
            {
                this.trackGraphInvalidationRegistered = true;
                this.RegisterWorldLoadingStartedCallback(this.InvalidateTrackPathGraph);
            }

            if (this.trackGraphReady)
            {
                return true;
            }

            if (this.trackGraphBuildFailed || !this.IsWorldReady || Time.unscaledTime < this.trackGraphNextBuildAt)
            {
                return false;
            }

            this.trackGraphNextBuildAt = Time.unscaledTime + 10f;
            return this.TryBuildTrackPathGraph();
        }

        // One-shot managed snapshot of TrackPathConfig.Points.
        //
        // GC SAFETY: every mono object read here is PINNED for exactly as long as it is held. The
        // Vector3 member reads box on the mono side, so each one is an allocation that can trigger
        // SGen and move any unpinned object we are still holding — that is the documented
        // stale-pointer AV. Pins are released in a finally.
        private bool TryBuildTrackPathGraph()
        {
            System.Diagnostics.Stopwatch watch = System.Diagnostics.Stopwatch.StartNew();
            System.Collections.Generic.List<uint> pins = new System.Collections.Generic.List<uint>(4);

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread() || auraMonoRuntimeInvoke == null)
                {
                    return false;
                }

                if (!this.TryGetAuraMonoManagerFromServiceDic("ConfigManager", out IntPtr configManager) || configManager == IntPtr.Zero
                    || !this.TryGetMonoObjectMember(configManager, "MainGameLvlConf", out IntPtr levelConfig) || levelConfig == IntPtr.Zero
                    || !this.TryGetMonoObjectMember(levelConfig, "TrackPathConditionConfig", out IntPtr conditionConfig) || conditionConfig == IntPtr.Zero
                    || !this.TryInvokeAuraMonoZeroArg(conditionConfig, out IntPtr trackPathConfig, "GetTrackPathConfig") || trackPathConfig == IntPtr.Zero
                    || !this.TryGetMonoObjectMember(trackPathConfig, "Points", out IntPtr pointList) || pointList == IntPtr.Zero
                    || !this.TryGetMonoIntMember(pointList, "Count", out int pointCount) || pointCount <= 0)
                {
                    ModLogger.Msg("[TrackGraph] build: waypoint graph unreachable in this level — walking will fall back to teleport.");
                    this.trackGraphBuildFailed = true;
                    return false;
                }

                uint listPin = AuraMonoPinNew(pointList);
                if (listPin != 0)
                {
                    pins.Add(listPin);
                }

                Vector3[] positions = new Vector3[pointCount];
                Vector3[][] neighbourPositions = new Vector3[pointCount][];
                int linkCount = 0;

                for (int i = 0; i < pointCount; i++)
                {
                    if (!this.TryGetAuraMonoListItem(pointList, i, out IntPtr pointObj) || pointObj == IntPtr.Zero)
                    {
                        continue;
                    }

                    uint pointPin = AuraMonoPinNew(pointObj);
                    try
                    {
                        if (!this.TryGetMonoVector3Member(pointObj, "position", out Vector3 pointPos))
                        {
                            continue;
                        }

                        positions[i] = pointPos;

                        if (!this.TryGetMonoObjectMember(pointObj, "neighbour", out IntPtr neighbourList) || neighbourList == IntPtr.Zero)
                        {
                            continue;
                        }

                        uint neighbourPin = AuraMonoPinNew(neighbourList);
                        try
                        {
                            if (!this.TryGetMonoIntMember(neighbourList, "Count", out int neighbourCount) || neighbourCount <= 0)
                            {
                                continue;
                            }

                            Vector3[] links = new Vector3[neighbourCount];
                            int written = 0;
                            for (int n = 0; n < neighbourCount; n++)
                            {
                                if (this.TryGetAuraMonoListVector3Item(neighbourList, n, out Vector3 linkPos))
                                {
                                    links[written++] = linkPos;
                                }
                            }

                            if (written > 0)
                            {
                                if (written != neighbourCount)
                                {
                                    Array.Resize(ref links, written);
                                }

                                neighbourPositions[i] = links;
                                linkCount += written;
                            }
                        }
                        finally
                        {
                            AuraMonoPinFree(neighbourPin);
                        }
                    }
                    finally
                    {
                        AuraMonoPinFree(pointPin);
                    }
                }

                // Position -> index, mirroring the game's own Dictionary<Vector3, AStarNode>: it
                // keys nodes by exact position and looks neighbours up the same way, so exact
                // equality is the contract the authored data already satisfies.
                System.Collections.Generic.Dictionary<Vector3, int> byPosition =
                    new System.Collections.Generic.Dictionary<Vector3, int>(pointCount);
                for (int i = 0; i < pointCount; i++)
                {
                    byPosition[positions[i]] = i;
                }

                int[][] neighbours = new int[pointCount][];
                int resolved = 0;
                int unresolved = 0;
                for (int i = 0; i < pointCount; i++)
                {
                    Vector3[] links = neighbourPositions[i];
                    if (links == null || links.Length == 0)
                    {
                        neighbours[i] = Array.Empty<int>();
                        continue;
                    }

                    int[] indices = new int[links.Length];
                    int written = 0;
                    for (int n = 0; n < links.Length; n++)
                    {
                        if (byPosition.TryGetValue(links[n], out int target) && target != i)
                        {
                            indices[written++] = target;
                            resolved++;
                        }
                        else
                        {
                            unresolved++;
                        }
                    }

                    if (written != indices.Length)
                    {
                        Array.Resize(ref indices, written);
                    }

                    neighbours[i] = indices;
                }

                this.trackGraphPositions = positions;
                this.trackGraphNeighbours = neighbours;
                this.trackGraphGCost = new float[pointCount];
                this.trackGraphParent = new int[pointCount];
                this.trackGraphClosed = new bool[pointCount];
                this.trackGraphReady = true;

                watch.Stop();
                ModLogger.Msg("[TrackGraph] build: " + pointCount + " nodes, " + resolved + " links resolved"
                    + (unresolved > 0 ? " (" + unresolved + " unresolved)" : string.Empty)
                    + ", " + linkCount + " read, in " + watch.ElapsedMilliseconds + "ms.");
                return true;
            }
            catch (Exception ex)
            {
                this.trackGraphBuildFailed = true;
                ModLogger.Msg("[TrackGraph] build failed: " + ex);
                return false;
            }
            finally
            {
                FreeAuraMonoPins(pins);
            }
        }

        // Nearest graph node to a world position, by true 3-D distance (live ignoreY is false).
        // maxDistance guards against snapping to a node on the far side of the map when the target
        // sits somewhere the graph does not cover (open water, sea floor).
        // `excluded` lets a caller retry without a node that has already proven unreachable — the
        // nearest waypoint is sometimes on the far side of a fence or wall, and the mod has no
        // line-of-sight test of its own (the game's own _FindNearestPoint filters candidates with a
        // Passable linecast; PhysicsExtension is Mono-only and not resolved here). Learning from a
        // failed approach is the cheap equivalent.
        // How many nearest nodes get a reachability probe before the sweep gives up. Each probe is
        // two Mono linecasts (feet + chest), so this is a wall-clock budget, not a search limit.
        private const int FarmWalkSnapMaxProbes = 12;

        private string lastTrackGraphSnapVerdict = string.Empty;

        private readonly struct TrackGraphSnapCandidate
        {
            internal readonly int Index;
            internal readonly float DistanceSqr;

            internal TrackGraphSnapCandidate(int index, float distanceSqr)
            {
                this.Index = index;
                this.DistanceSqr = distanceSqr;
            }
        }

        private readonly System.Collections.Generic.List<TrackGraphSnapCandidate> trackGraphSnapCandidates
            = new System.Collections.Generic.List<TrackGraphSnapCandidate>();

        private bool TryFindNearestTrackGraphNode(Vector3 position, float maxDistance, out int index,
            System.Collections.Generic.HashSet<int> excluded = null)
        {
            index = -1;
            if (!this.trackGraphReady || this.trackGraphPositions == null)
            {
                return false;
            }

            float bestSqr = maxDistance * maxDistance;
            Vector3[] positions = this.trackGraphPositions;
            for (int i = 0; i < positions.Length; i++)
            {
                if (excluded != null && excluded.Contains(i))
                {
                    continue;
                }

                float sqr = (positions[i] - position).sqrMagnitude;
                if (sqr < bestSqr)
                {
                    bestSqr = sqr;
                    index = i;
                }
            }

            return index >= 0;
        }

        // Nearest graph node that can actually be REACHED from `position`, not merely the nearest one.
        //
        // This is the difference that produced "a wall in the middle". The game's _FindNearestPoint
        // never takes a node on faith:
        //
        //     _aStar.GetNeighbour(pos, ref _neighbours);
        //     foreach (node in _neighbours)
        //         if (dist < best && _HasNoCollider(pos, node.position, Passable))   // <-- the filter
        //
        // Snapping by distance alone puts the route's first leg (player -> start node) and its last
        // leg (end node -> resource) straight through whatever stands between, because neither leg
        // is part of the graph and so neither is ever validated. A* then reports a perfectly good
        // route across nodes that are genuinely connected, and the walker drives into a building.
        //
        // The game's route is LONGER than ours precisely because of this filter, and longer is what
        // correct looks like here: it enters and leaves the graph only where it can.
        //
        // Probes are Mono invokes, so candidates are tried nearest-first and the sweep stops after
        // FarmWalkSnapMaxProbes. Falls back to the plain nearest node (with a log) rather than
        // failing the walk outright — a slightly wrong route still beats a teleport.
        private bool TryFindReachableTrackGraphNode(Vector3 position, float maxDistance, out int index,
            System.Collections.Generic.HashSet<int> excluded, string endpointName)
        {
            index = -1;
            if (!this.trackGraphReady || this.trackGraphPositions == null)
            {
                return false;
            }

            if (!this.EnsureFarmWalkLinecast())
            {
                // No physics access: behave exactly as before rather than refusing to walk.
                return this.TryFindNearestTrackGraphNode(position, maxDistance, out index, excluded);
            }

            this.trackGraphSnapCandidates.Clear();
            Vector3[] positions = this.trackGraphPositions;
            float maxSqr = maxDistance * maxDistance;
            for (int i = 0; i < positions.Length; i++)
            {
                if (excluded != null && excluded.Contains(i))
                {
                    continue;
                }

                float sqr = (positions[i] - position).sqrMagnitude;
                if (sqr < maxSqr)
                {
                    this.trackGraphSnapCandidates.Add(new TrackGraphSnapCandidate(i, sqr));
                }
            }

            if (this.trackGraphSnapCandidates.Count == 0)
            {
                return false;
            }

            this.trackGraphSnapCandidates.Sort((a, b) => a.DistanceSqr.CompareTo(b.DistanceSqr));

            int probes = System.Math.Min(this.trackGraphSnapCandidates.Count, FarmWalkSnapMaxProbes);
            for (int i = 0; i < probes; i++)
            {
                int candidate = this.trackGraphSnapCandidates[i].Index;
                if (this.IsFarmWalkLineClear(position, positions[candidate], this.farmWalkMaskPassable))
                {
                    index = candidate;
                    if (i > 0)
                    {
                        // Unconditional, but deduplicated. Whether the reachability filter ever
                        // REJECTS anything is the only way to tell a working filter from a
                        // decorative one — the first run after it landed printed nothing at all,
                        // which read as "clean" but actually meant "every candidate passed".
                        // The re-path timer then took it to the other extreme: the same line 22
                        // times in 33 seconds. Print it when the verdict CHANGES.
                        string verdict = endpointName + "|" + i + "|"
                            + Mathf.Sqrt(this.trackGraphSnapCandidates[i].DistanceSqr).ToString("F1");
                        if (verdict != this.lastTrackGraphSnapVerdict)
                        {
                            this.lastTrackGraphSnapVerdict = verdict;
                            ModLogger.Msg("[FarmWalk] " + endpointName + " snap skipped " + i
                                + " blocked node(s); took one "
                                + Mathf.Sqrt(this.trackGraphSnapCandidates[i].DistanceSqr).ToString("F1") + "m out.");
                        }
                    }

                    return true;
                }
            }

            // Nothing within reach. Take the nearest anyway, but say so — this is the shape of
            // failure that ends in "final approach not walkable".
            index = this.trackGraphSnapCandidates[0].Index;
            ModLogger.Msg("[FarmWalk] " + endpointName + " snap: no clear line to any of the "
                + probes + " nearest graph nodes — falling back to the nearest ("
                + Mathf.Sqrt(this.trackGraphSnapCandidates[0].DistanceSqr).ToString("F1") + "m).");
            return true;
        }

        // A* over the snapshot, same shape as the game's AStar.GetPath: corner list from start node
        // to end node. The caller appends the true target as the final corner (GetPath2 does
        // exactly that) so the walk finishes at the resource, not at the last waypoint.
        private bool TryComputeTrackGraphPath(int startIndex, int endIndex, System.Collections.Generic.List<Vector3> corners)
        {
            corners.Clear();
            if (!this.trackGraphReady || startIndex < 0 || endIndex < 0)
            {
                return false;
            }

            Vector3[] positions = this.trackGraphPositions;
            int[][] neighbours = this.trackGraphNeighbours;
            float[] gCost = this.trackGraphGCost;
            int[] parent = this.trackGraphParent;
            bool[] closed = this.trackGraphClosed;

            for (int i = 0; i < positions.Length; i++)
            {
                gCost[i] = float.PositiveInfinity;
                parent[i] = -1;
                closed[i] = false;
            }

            if (startIndex == endIndex)
            {
                corners.Add(positions[startIndex]);
                return true;
            }

            this.trackGraphOpen.Clear();
            gCost[startIndex] = 0f;
            this.trackGraphOpen.Add(startIndex);
            Vector3 goal = positions[endIndex];

            while (this.trackGraphOpen.Count > 0)
            {
                int bestSlot = 0;
                int best = this.trackGraphOpen[0];
                float bestF = gCost[best] + Vector3.Distance(positions[best], goal);
                for (int s = 1; s < this.trackGraphOpen.Count; s++)
                {
                    int candidate = this.trackGraphOpen[s];
                    float f = gCost[candidate] + Vector3.Distance(positions[candidate], goal);
                    if (f < bestF)
                    {
                        bestF = f;
                        best = candidate;
                        bestSlot = s;
                    }
                }

                this.trackGraphOpen.RemoveAt(bestSlot);
                if (closed[best])
                {
                    continue;
                }

                closed[best] = true;

                if (best == endIndex)
                {
                    for (int node = endIndex; node >= 0; node = parent[node])
                    {
                        corners.Add(positions[node]);
                    }

                    corners.Reverse();
                    return true;
                }

                int[] links = neighbours[best];
                for (int n = 0; n < links.Length; n++)
                {
                    int next = links[n];
                    if (closed[next])
                    {
                        continue;
                    }

                    float tentative = gCost[best] + Vector3.Distance(positions[best], positions[next]);
                    if (tentative < gCost[next])
                    {
                        gCost[next] = tentative;
                        parent[next] = best;
                        this.trackGraphOpen.Add(next);
                    }
                }
            }

            return false;
        }

        // Vector3List element read — the neighbour lists hold POSITIONS, not node references, so the
        // graph can be rebuilt without any object-identity juggling.
        private unsafe bool TryGetAuraMonoListVector3Item(IntPtr listObj, int index, out Vector3 value)
        {
            value = Vector3.zero;
            if (listObj == IntPtr.Zero || index < 0 || auraMonoObjectGetClass == null
                || auraMonoRuntimeInvoke == null || auraMonoObjectUnbox == null)
            {
                return false;
            }

            IntPtr listClass = auraMonoObjectGetClass(listObj);
            if (listClass == IntPtr.Zero)
            {
                return false;
            }

            IntPtr getItem = this.FindAuraMonoMethodOnHierarchy(listClass, "get_Item", 1);
            if (getItem == IntPtr.Zero)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            int indexValue = index;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&indexValue);
            IntPtr boxed = auraMonoRuntimeInvoke(getItem, listObj, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero || boxed == IntPtr.Zero)
            {
                return false;
            }

            IntPtr raw = auraMonoObjectUnbox(boxed);
            if (raw == IntPtr.Zero)
            {
                return false;
            }

            value = *(Vector3*)raw;
            return true;
        }

        // List<T>/PointList element read via the get_Item(int) indexer. The indexer path is the
        // reliable one through AuraMono — the boxed struct-enumerator path used for Dictionary
        // .Values is documented as flaky and returns 0 items for some collections.
        private unsafe bool TryGetAuraMonoListItem(IntPtr listObj, int index, out IntPtr itemObj)
        {
            itemObj = IntPtr.Zero;
            if (listObj == IntPtr.Zero || index < 0 || auraMonoObjectGetClass == null || auraMonoRuntimeInvoke == null)
            {
                return false;
            }

            IntPtr listClass = auraMonoObjectGetClass(listObj);
            if (listClass == IntPtr.Zero)
            {
                return false;
            }

            IntPtr getItem = this.FindAuraMonoMethodOnHierarchy(listClass, "get_Item", 1);
            if (getItem == IntPtr.Zero)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            int indexValue = index;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = (IntPtr)(&indexValue);
            itemObj = auraMonoRuntimeInvoke(getItem, listObj, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                itemObj = IntPtr.Zero;
                return false;
            }

            return itemObj != IntPtr.Zero;
        }
    }
}
