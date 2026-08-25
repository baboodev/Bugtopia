# Aura Farm — Walk to Nodes

The mode in which the Aura Farm **walks along the ground** to each resource instead of teleporting,
retracing the route the game's own **Track** feature draws (the line of stars to a map marker).

Toggle: **Resource Gathering → Foraging → SETTINGS → Walk to Nodes** (persisted in `Config.xml`).

---

## 1. Where the route comes from

**Not a navmesh.** The `UnityEngine.AI` hypothesis was tested in game and disproved: `SamplePosition`
finds no mesh at any distance and `CalculatePath` always returns `PathInvalid`. The reason is that
`XDNavigationMgr.LoadNavMeshDataSync` has **zero callers** anywhere in the Mono dump — nobody loads
the mesh. The probe is kept in `NavMeshWalkFeature.cs` in case a patch ever enables it.

Track's real mechanism is a **client-side A\* over a hand-authored waypoint graph**:

| What | Where |
|---|---|
| The graph: `Points[]` of `{ position, neighbour[] }` | `EngineWrapper/TrackPathConfig.cs` |
| A\* (`Init`, `GetPath`, `GetNeighbour`) | `XDTLevelAndEntity/...TrackingPoint/AStar.cs` |
| The orchestrator plus smoothing | `.../TrackingPoint/TrackingPathModule.cs` |
| Settings and graph selection | `XDTDataAndProtocol/.../TrackPathConditionConfig.cs` |

The server sends only **the target's position** (`TrackData.Position`) — the client computes the route.

`TrackPathGraphFeature.cs` builds **its own** `AStar` from
`ConfigManager.MainGameLvlConf.TrackPathConditionConfig.GetTrackPathConfig()` and snapshots the graph
into managed arrays **once per world** (invalidated through
`RegisterWorldLoadingStartedCallback`). After that the A\* runs in pure C# — **there is not one
AuraMono call in the per-frame walking path** — so the stale-pointer problem of a moving GC cannot
reach it.

Measurements: land is **1745 nodes / 7980 links in 9–12 ms**, the underwater area is **86 nodes / 962
links** (each level has its own graph; the underwater one is far sparser, hence the straight 20–30 m
hops).

### ⚠️ Snapping to the graph requires a linecast

A route has three parts, and only the middle one lies in the graph:

```
player ──?──> start node ──A*──> end node ──?──> resource
       ^^^ off-graph                        ^^^ off-graph
```

Both outer legs are **checked by nobody**, so snapping by "nearest node by distance" happily lays them
through a wall while A\* reports a perfectly good route. The game does not allow this:
`_FindNearestPoint` takes the nearest node **that has a clear line to it** on the `Passable` layer
(details and figures in §10).

`TryFindReachableTrackGraphNode` reproduces that: candidates within `FarmWalkGraphSnapRadius` are
sorted by distance and each is tested with `IsFarmWalkLineClear(..., farmWalkMaskPassable)`, up to
`FarmWalkSnapMaxProbes` = 12 probes (each being two Mono linecasts, at foot and chest height). If none
passes, the nearest is taken anyway but with an **unconditional** log line: this is exactly the case
that later ends in `final approach not walkable`.

### ⚠️ An `All` ray has to be lifted off the ground

`All` = 13457 includes the **Ground** layer. A ray between two points lying on the ground runs into
the terrain on any slope, which makes "the straight line is blocked" permanently true. The game gets
around this with the config's `startOffset` / `endOffset` in `TrackPathConditionConfig`, which lift
both ends:

```csharp
PhysicsExtension.Linecast(start + startOffset, end + endOffset - pullBack, All)
```

Without the lift the check lies on an open beach. Run at 21:53: "the straight line is blocked" fired
at 8.9 m, then 11.8, then 13.8 — the distance was **growing** while the player walked backwards away
from an oyster for 45 seconds.

The rule: a short leg uses `All` **with** the `FarmWalkShortcutProbeLift` lift; a long off-graph leg
(waypoint → resource, 15–20 m) uses `Passable` only, because on `All` it is blocked by definition.

### ⚠️ Try candidates only in a separate buffer

`TryComputeTrackGraphPath` **clears its output list at the start**. Probing a candidate straight into
`farmWalkScratchCorners` destroys the route the caller was about to commit: after four failed probes
the walker got the last rejected candidate. In the log this appeared as
`timed out 13,1m short [corner 1/8]` for a walk that had started with two corners.

### ⚠️ The detour search runs once per walk

`farmWalkDetourSearchDone`. Every successful detour sets `cornerIndex = 0`; if the search runs on
every re-path, the player turns back toward the first corner, the corridor test fails, the re-path
fires again — the route "rebuilds back and forth" and the walk goes nowhere.

---

## 2. Movement

The joystick axis is fed through `TrySetGameMoveAxis` (`MovementInputFeature.cs`) — the game's own
locomotion, which gives terrain following, collisions, slopes and swimming for free, and lets the
server see ordinary movement. **The position is never written directly.**

- The direction is rotated by **minus the camera yaw**: the game applies the camera rotation itself
  further down the chain.
- Turning is capped at **360°/s** — otherwise the character snaps at corners and after a re-path.
- Speed is always full (1.0). The 0.95 cap existed only for stamina, and the game has none. A full
  axis yields `MotionInfo.MovingSpeedLimit` = 4.0 m/s, which is the game's own running ceiling, below
  the `MovementAntiCheating` threshold of 4.3 m/s.
- Over the last 2 m the speed eases down to 0.2, or the locomotion overshoots the target and circles.

**The player's position** is read only from the Mono entity (`InteractSystem.player`, then
`EntityUtil.GetSelfPlayer`). `GetPlayer()` must not be used — it can return somebody else's player.

---

## 3. Arrival

The threshold is **0.25 m horizontally** plus a **3-D ≤ 1.8 m** check (the server's rule,
`CollectAntiCheating.Distance` = 2 m).

Splitting the axes matters: walking cannot change height, so a pure 3-D check made any slightly raised
resource unreachable. "Close horizontally, far vertically" means a ledge, and that is a fair reason
for a short teleport hop.

Server refusals are **silent**, so the only confirmation of a collect is that the farm stops
re-opening the same node.

---

## 4. Stuck detectors

Two independent ones, catching different failures:

| Detector | What it measures | Why |
|---|---|---|
| Displacement | 3-D over 0.6 s, threshold 0.15 m | a hard wedge (a real one reads 0.00–0.05) |
| Route remaining | the length of the path left, 0.5 m / 3 s | "moving, but getting nowhere" |

Route remaining is the **path length**, not the straight-line distance: going around a hill or a bay
temporarily increases the distance to the target, and the straight-line metric killed healthy walks.

⚠️ **Re-base the baseline** on every change of vertical state, during an obstacle detour and after a
re-path. Comparing a metric against a baseline taken with a different formula is the source of this
feature's worst bug (circling on a descent).

---

## 5. Escalation when stuck

**On land:** a single jump → a **5 m** back-off (by distance; the timeout is only a safety net) →
turn and run-up → a **series of bunny hops** starting from a third of the remaining distance. The jump
is repeated on every landing (`TryReadBunnyHopSurfaceState`), or it is swallowed mid-air and no chain
forms. A short back-off was entirely eaten by the 360°/s turn, leaving the character jumping on the
spot.

**Wandering along the ground is not allowed** — on land an obstacle is usually cleared by height
rather than by going around.

**Underwater:** back off 5 m → ascend (only if the target is higher) → probe **8 directions**, the
first being backwards, then 135/225/90/270/45/315/0 off the bearing to the target. Down there
obstacles genuinely are gone around, and the player is already moving in three dimensions.

---

## 6a. Underwater unsticking (two different mechanics)

There is no jump underwater: `TryFarmWalkJump` checks `TryGetFarmWalkSwimLocomotion` and substitutes a
back-off. The budget from `FarmWalkMaxJumpsPerWalk` (4) is **shared** between jumps and back-offs,
hence `unstick N/4` in the log.

**Vertical blocked** → a probe (`BeginFarmWalkProbe`). It alternates a horizontal swim-out with a 0.6 s
vertical attempt, and the vertical always aims at the target's height (`want = dy > 0 ? 1 : -1`).
**Four sides**: back, right, left, forward — back first, because we usually wedge face-on. It used to
be eight directions; the diagonals lay between rays already tried and did not pay for themselves. One
probe per walk (`farmWalkProbeUsed`).

⚠️ A horizontal leg ends **on distance — 5 m** (`FarmWalkProbeHorizontalDistance`), not on a timer,
exactly like the back-off. A fixed 0.8 s covered whatever it covered: push off a reef and it is metres,
be pinched in a crevice and it is centimetres, and the probe declared the direction "tested" without
having moved at all. The timer (`FarmWalkProbeHorizontalTimeout` = 4 s) remains as a guard for exactly
the case the distance check cannot end: blocked this way too.
Log: `probe leg 2/4 (90 deg) swam 5,0m of 5,0m (clear) — trying the vertical.`

**Horizontal blocked** → back off 5 m, then a vertical leg **whose direction alternates between
rounds**: round 1 up, round 2 down, and so on (`farmWalkBackOffRound` /
`farmWalkBackOffVerticalDir`).
⚠️ Ascending used to happen only when the node was higher, and on a descent the phase fell through to
Idle. The logic was literally correct (ascending undoes a descent) but left the "wedged while diving"
case with no vertical at all: back off, resume the same descent, hit the same rock. Alternating gives a
chance both to climb over and to duck under, wherever the node is.

The back-off line is unconditional and prints the round: `backed off 5,0m (clear), round 2 — descending.`

---

## 6. Underwater specifics

- Depth: `SwimLocomotion.SetSwimVerticalInput(bool asc, bool pressed)` — it works in **every** swimming
  mode and needs no camera work.
  ⚠️ **Do not press it every frame**: each press refreshes `_verticalInputBufferStartTime`, and a
  reversal within 0.3 s is rejected — spamming freezes depth control.
- Hysteresis: 0.35 m to engage, 0.12 m to release. A single threshold chatters.
- Axis order: target **above** — surface first, then swim; **below** — swim there first and descend
  4 m from the target.
- ⚠️ Everything vertical happens **only while actually swimming** (`farmWalkIsSwimming`). On land it
  zeroed the movement axis and the character stood there jumping on the spot.
- Sprint: `TryStartSprint()` on hops of 25 m or more, cancelled at 15 m by **turning** — the game
  itself drops the sprint on a turn larger than `LargeTurnAngleThreshold`.
- The repair kit is **an entity on the seabed** with an aura sphere. After throwing it you must
  **descend into it** or the repair will not start. It is held in every farm state, not only in the
  cleaning dwell.

---

## 7. Route smoothing

A\* returns a path **through the graph**, not a player's path: on land that was 11 corners over 12 m.

The smoothing follows the game's shape: **only the first** corner is cut, and **only** if the ray to
the second is clear — one at a time, no more than 3 per rebuild, spanning ≤10 m (`tryLineConnectDis`).

The ray is `MonoGame.ScriptFramework.PhysicsExtension.Linecast` (⚠️ image **EngineWrapper**, not
XDTLevelAndEntity), and the masks are read from `TrackingPathModule`'s own statics (`All` / `Passable`).
It is checked at **foot and chest height (+1.2 m)**, and both must be clear — a low ray slips under
railings and through gaps.

⚠️ Taking "the furthest visible corner" is not allowed: the route collapses into a straight line (18.9 m
into 2 corners) and the character walks through buildings.

---

## 7b. Target eligibility: how the mod knows a resource can be collected

Walking to a resource that cannot be taken is this mode's most expensive mistake: it spends tens of
metres, jumps, probes and a rescue teleport, and then a full dwell on top for nothing. So the verdict
comes from the game rather than being inferred from a convenient-looking field.

### A mushroom does not go cold, it grows

Measured in the live game (2026-08-19, ten collects — five by hand, five by the farm):

* A collected **dynamic bush** (a mushroom, an event plant) **is removed from the world**. A **new
  entity with a new netId** appears in its place — at one spot over a single run:
  `14215718 → 3630230 → 3631959 → 3696675`.
* The new entity **grows**, and that is the very half-circle the player sees on screen. Its component
  is empty meanwhile: `inCold=False`, `coldEndTime=0`, `availableNum=3` — byte for byte like a ripe
  one.
* ⚠️ **Zeros in the component mean "no data", not "available".** Five failed arrivals in a row read
  exactly that way. Reading zeros as "available" was the cause of walking to depleted nodes.
* There is no growth state on `CollectableObjectComponent` at all. The client computes it:
  `DynamicMapItemService.GetDynamicBushColdEndTime` → `ParseToUnix(DynamicBushGrowComponent.MaturityTime)`.

Trees, stone and berries behave differently — the entity stays put and honestly goes on cooldown, and
for them the component's `inCold` is trustworthy.

### The client's verdict: a table by netId

`ResourceProtocolManager.CmdUpdateCollectCold(netId, coldEndTime, totalColdTime, availableNum)`
dispatches `CollectColdEvent` and only afterwards writes to `CollectableObjectData` — and only if the
object is known to `DataCenter`. The event comes first, so that is what we listen to.

* The hook is **registered immediately** rather than from a deferred world-ready callback:
  registration is only metadata and is always safe, and attaching the detour waits for `IsWorldReady`
  anyway. Measured: the world was ready at 04:06:20, the hook installed through the deferred callback
  at 04:06:21 and missed the opening broadcast.
* The verdict for **every** netId is recorded, not just the current node's. The old handler discarded
  roughly 800 verdicts per run behind its gates.

### The sweep: asking the game to publish a verdict for everything

`TrackModule.OnCreate()` calls `UpdateAllColdTime()` **once at startup**, and catching that broadcast
is structurally impossible. So the mod asks for it itself:

```
EcsService.Get<IDynamicMapItemService>()   ← inflate the generic (the same technique as DispatchEvent<T>)
        ↓
service.UpdateAllColdTime()                ← walks every resource point
        ↓
CollectColdEvent × N                       ← 153 events per call, table 2 → 66 entries
```

* Take `Get<T>(bool)` and **not** `TryGet<T>(out T, bool)`: the former has no out parameter. Through
  AuraMono an out slot is safe only for reference types.
* Inflate **only while `IsWorldReady`**: before the world exists this is a documented `abort()`
  (`mono_metadata_get_generic_inst` → `g_assert`).
* The sweep is triggered **by an unfamiliar netId appearing in a snapshot**, not by a clock. A resource
  is reborn between timer ticks, and a thirty-second pause is precisely the window in which the farm
  arrives at a growing mushroom. Thirty seconds remain as the floor between sweeps.

### ⚠️ The sweep's second event lies with a zero

The body of the `UpdateAllColdTime` loop sends **two** events per resource:

```csharp
if (has DynamicBushGrowComponent)
    CmdUpdateCollectCold(netId, ParseToUnix(grow.MaturityTime), growTime, ...);   // the truth
UpdateResourcePoint(resourceId, netId);                                           // unconditionally, again
```

`UpdateResourcePoint` recomputes through a filter bound to the player; when the filter is empty `num`
stays `0` and "ready" goes out, overwriting the maturity time from the first event. So **a zero does
not overwrite a live maturity time within one second**. Between sweeps the overwrite does work — or a
matured bush would stay marked as taken forever.

### Two predicates that must not be merged

| Question | Who asks | How "unknown" is read |
|---|---|---|
| Is whatever stands here taken | the collect dwell (`TryGetLiveNodeColdState`) | not at all; it is not evidence |
| Is this target worth walking to | the walker and the tour plan (`IsFarmTargetUnconfirmed`) | **unconfirmed ⇒ do not go** |

Merging these two questions into one function measurably breaks collecting: nine targets, zero
collects, six timeouts — against six collects out of eleven before it.

The "unconfirmed ⇒ do not go" rule applies **only to dynamic bushes**, and the limit is measured:
verdict coverage is 3/3 for mushrooms and 77 of 117 for trees, stone and berries — the latter get no
broadcast at all, because a verdict is only computed where there is a `DynamicBushGrowComponent`.
Applying the rule to everything would park half the map.

### The result

```
before                6 collects / 11 targets, timeouts on a third of the approaches
merged predicate      0 collects /  9 targets, 6 timeouts
after                 3 collects /  5 targets, 0 timeouts, 1 refusal before the approach
```

A refusal looks like this and is printed with its source:

```
target went on cooldown [client verdict: netId 21385 not ready for 25317s, heard 3s ago] — moving to the next node
```

---

## 8. Handling unreachable nodes

The failure counter is kept **per node** (`farmWalkNodeFailures`) and is reset on a successful arrival
and at the start of a run.

0. **A second failure on the same node** — parked for 5 minutes, whatever the distance. Trying again
   buys nothing: the 20:44 run showed a retry reproduces **the same numbers** (6.8 m, dy=3.8 m),
   because the obstacle is geometry rather than routing.
1. **The rescue teleport** — a first failure, the node closer than `FarmWalkRescueTeleportRange`
   (10 m), the jumps spent, and `FarmWalkRescueTeleportCooldown` (60 s) elapsed since the last rescue.
   The kind in the log is `node:walk-rescue`.
   ⚠️ The cooldown is load-bearing here: at roughly one node every 5–8 s, one teleport a minute is
   under a tenth of the transitions, so the mode still walks and the server sees continuous movement
   between resources. Weakening the cooldown means bringing back the teleporting farm.
2. **A skip** — the node is stamped into `recentlyVisitedNodes` and the farm moves to the next one.
   The log says which of the two checks refused the rescue: `beyond the 10m rescue range` or
   `rescue teleport on cooldown, Ns left`.
3. **A retry after the next collect** — with a **different** end waypoint
   (`farmWalkExcludedEndNodes`), or A\* builds the same approach and fails with the same numbers. No
   further than 35 m, or it is a long trek to a known-bad approach.
4. **Reclaiming a skipped node** — if nothing else is left after a skip, after a 5 s wait.
   ⚠️ Without that pause a single empty scan caused a flight across the whole map
   (`MovingToLocation`).
5. **The break-out teleport** — only after 3 consecutive skips (`node:walk-fallback`).

### Every teleport reason in the log

The line `[FarmTeleport] <kind> -> (x,y,z)` is unconditional. The kind says what actually happened:

| Kind | Is walking tried | What it is |
|---|---|---|
| `node:<label>` | **yes** | an ordinary move to a node; a teleport means the walk failed to build |
| `node:priority-active` | **yes** | a priority node in the current area |
| `node:priority-visible` | **yes** | a priority node within sight |
| `node:retry` | **yes** | a retry of a previously skipped node |
| `node:walk-rescue` | — | the rescue: a failure, ≤10 m, no more than once a minute |
| `node:walk-fallback` | — | the break-out after 3 consecutive skips |
| `node:skip-reclaim` | **no** | a skipped node is reclaimed when nothing else is nearby |
| `area:<name>` | **no** | a move between farm areas |
| `area:priority-recheck` | **no** | the periodic (60 s) re-evaluation of priority areas |
| `area:priority-fallback` | **no** | a priority area when no nodes are visible in it |
| `area:startup-priority` | **no** | the opening hop into a priority area |

⚠️ Every `area:*` is an inter-area move, and walking those **was never in scope** (agreed: "long hops
can be added later"). If the log shows more teleports than expected, look at the kind first: `area:*`
and `node:skip-reclaim` do not count as walker failures.

---

## 9. Agreed decisions (do not revisit without asking)

- **Mutually exclusive with Stealth Foraging** — that one dives under the terrain on noclip.
- **Forced 1x** for the whole run: the server measures real time.
- **No distance limit** — we walk any distance.
- **The rescue teleport** — only ≤10 m and at most once a minute; a second failure on a node parks it
  instead of teleporting.

---

## 10. Diagnostics: comparing against the game's Track

The **Compare Game Track** switch makes the game route to the same target:
`MapSpotProtocolManager.AddSpot(Custom, useId, pos, TrackMap)` →
`TrackProtocolManager.StartLocalTrackMapSign(useId)`, and after 1.5 s `TrackingPathModule._path` is
read.
⚠️ `StartLocalTrackMapSign` internally calls `StopAllLocalTrack()` — it clears the player's manual
track.

The **Walk to Nodes** switch additionally focuses the radar: only markers within 3 m of the current
target remain (there is a single cut-off, at the top of `CreateMarker`, so it covers all ~29 sites
including the underwater and bubble ones), and a green `FarmWalkRouteLine` polyline is drawn over the
world along our remaining corners. The game's golden stars are visible beside it — that is the direct
route comparison.

### ⚠️ The game's Track is the reference. The metric is passability, not length

The first live comparison: **59.7 m from the game against 5.8 m in a straight line** to the same
target; the second measurement was 53.6 m against 11.1 m. The temptation to read this as "our route is
better" is a mistake: the game's path is longer precisely because it goes around what the mod went
straight through. **Longer is correct.**

The cause of the divergence is `TrackingPathModule._FindNearestPoint`:

```csharp
_aStar.GetNeighbour(pos, ref _neighbours);
foreach (node in _neighbours)
    if (dist < best && _HasNoCollider(pos, node.position, Passable))   // ← the filter
```

The game never takes a node on trust: it requires a clear line on the `Passable` layer. Snapping "by
nearest distance" lays the route's first leg (player → start node) and its last (end node → resource)
straight through an obstacle, because **neither of those two legs belongs to the graph and so is
checked nowhere**. A\* meanwhile reports a valid route over genuinely connected nodes, and the walker
drives into a building.

Hence `TryFindReachableTrackGraphNode` (§1): candidates are tried in increasing order of distance,
each tested with a `Passable` linecast, up to 12 probes per route end, falling back to the nearest node
with an explicit log line.

Other things from `GetPath` that help when reading logs:

- `_path` is **not a list of corners** but a Catmull-Rom resample: `PointCount = 100`, stepping
  `i += 2` → always ~51 points regardless of length. Only the length can be compared.
- The loop runs to `t = 0.98`, so the curve stops short of the route's end.
- `AStar.GetNeighbour` is a quadtree with a 15×15 m box around the point; our 60 m is markedly wider.

⚠️ Measure the mod's route length **from the current position over the remaining corners**: the full
route includes the prefix already walked, which is why the first comparison showed 17.6 m instead of
5.8 m.

---

## 11. Files

| File | Responsibility |
|---|---|
| `TrackPathGraphFeature.cs` | the probe, the graph snapshot, the C# A\*, nearest-node search |
| `FarmWalkFeature.cs` | the walker, escalation, depth, sprint, smoothing, retries |
| `FarmWalkTrackCompareFeature.cs` | requesting the game's Track and comparing lengths |
| `FarmWalkRadarFocusFeature.cs` | focusing the radar on the target plus our route line |
| `NavMeshWalkFeature.cs` | the navmesh probe (a negative result) |
| `HeartopiaComplete.Farm.cs` | the `WalkingToNode` state and the three entry points to a node |
| `HeartopiaComplete.Radar.cs` | the cut-off in `CreateMarker`, keeping the line alive between scans |
| `HeartopiaComplete.UguiForagingContent.cs` | the toggle and the mutual exclusion |

**A step-by-step account of route selection** — every branch, threshold and the measurements behind
them: `docs/FARM_WALK_ROUTING.md`.

**Logs:** `[FarmWalk]`, `[TrackGraph]`, `[FarmTeleport]` — all unconditional.
⚠️ Do not hide them behind `MasterLog*`: over this one session a flag gate concealed the cause of a
bug three times.
