# Farm Walk: route selection, step by step

What actually happens in the code between "there is a target" and "the axis is pushed that way". Every
branch comes with its condition, its constant and, where it is known, the number that produced it.

Files: `buddy/FarmWalkFeature.cs` (routing, following, escapes), `buddy/TrackPathGraphFeature.cs` (the
waypoint graph, snapping, A\*).

The overall design: **the position is never written directly**. All movement is a joystick axis
through `TrySetGameMoveAxis`, i.e. the game's own locomotion, so the server sees ordinary walking. A
teleport remains an emergency exit only.

---

## 1. Starting a walk — `BeginFarmWalk`

```
the player's position cannot be read   → refuse, no walk
Distance3D(self, target) <= 0.25 m     → route = [target], one corner, go straight there
otherwise                              → TryBuildFarmWalkRoute(self, target)
the route failed to build              → refuse (the caller falls through to a teleport)
```

`FarmWalkCollectDistance = 0.25 m`. The "already there" branch exists for one specific bug: a refusal
sent the caller into `FarmTeleportTo`, and the farm teleported onto a node while standing 1.3 m from
it.

Reset on every new walk: the jump counter, the permission for the one-shot detour search
(`farmWalkDetourSearchDone`), the one-shot probe, the one-shot height correction, the escape phase and
the progress baseline.

---

## 2. Building the route — `TryBuildFarmWalkRoute(from, to)`

It writes into a **separate buffer**, `farmWalkScratchCorners`, and moves the result into
`farmWalkCorners` only on success. The reason: `TryComputeTrackGraphPath` clears its output list first
thing, so writing directly would empty the current route and the very next corner read would go out of
bounds.

### 2.1. Snapping the ends to the graph — `TryFindReachableTrackGraphNode`

Radius `FarmWalkGraphSnapRadius = 60 m`, up to `FarmWalkSnapMaxProbes = 12` candidates.

```
the graph is not ready                      → refuse
physics (Linecast) unavailable              → just take the nearest node
candidates in range, minus the excluded     → sort by distance
  for the first 12:
    a Passable ray to the node is clear     → take this node ✔
  none is clear                             → take the nearest, but log a warning
zero candidates while exclusions exist      → ⚠ starvation valve: ignore exclusions for this snap
zero candidates with no exclusions          → refuse
```

The **starvation valve** is not decoration: in one long session 696 of 1745 waypoints ended up banned,
all in the area the farm was working, and the snap started returning "no node within 60 m" forever. A
heuristic has no right to make routing impossible.

The ray is tested on the **Passable** mask, not `All`: `All` includes Ground, and a ray between two
points at ground level runs into the terrain on any slope.

### 2.2. A\* over the graph — `TryComputeTrackGraphPath`

An ordinary A\*, with Euclidean distance to the target as the heuristic and the distance between nodes
as the edge cost.

```
startIndex == endIndex → a one-point route (both ends snapped to the same node)
a path was found       → the list of node positions from start to finish
the open list is empty → refuse
```

Neighbours are stored **as positions** rather than references, so the graph can be rebuilt without
worrying about object identity.

### 2.3. Appending the real target

`to` is appended to the node list. This mirrors the game's `GetPath2`: a walk has to end at the
resource, not at the last waypoint.

### 2.4. A degenerate route and the detour search

The entry condition applies **only on a walk's first build**, not on a re-path:

```
!farmWalkActive  &&  !farmWalkDetourSearchDone  &&  corners < 3  &&  Linecast available
```

Most of the farm's hops are 5–20 m, shorter than the graph's spacing, so both ends snap to **one**
node, A\* returns a single point, and the route comes out as `[node, target]` — a straight line in
which the geometry between the player and the resource was never checked at all.

The game does not do this: its `GetPath` produces a two-point straight line only when
`!Linecast(start, end - horizontalOffset, All)`. The test is copied verbatim, with two caveats:

- **both ends are lifted** by `FarmWalkShortcutProbeLift = 1.2 m`. Without the lift an `All` ray runs
  along the ground and reports "occupied" on any slope — which is what happened: "direct line blocked"
  fired on an open beach at 8.9 m, then 11.8, then 13.8, while the player backed away from an oyster;
- **0.5 m** is subtracted from the target along the direction (`pullBack`), as in the original.

```
the straight line is clear   → leave it as is
the straight line is blocked → TryBuildDetouredFarmWalkRoute
                               a detour was found → the route is replaced, corner cutting is NOT applied
                               none was found     → keep the straight line, but log why this will be hard
```

The "first build only" restriction was earned too: re-running it every 1.5 s reset the corner index to
0, the player turned back toward the first corner, the corridor test failed, and the route was built
again — 45 seconds of walking **away** from a node with the same six lines in the log.

### 2.5. The detour — `TryBuildDetouredFarmWalkRoute`

Up to `FarmWalkDetourAttempts = 4` attempts. Each one: exclude the previous end node, snap the target
to a **different** node, run A\* into the separate `farmWalkDetourCorners` buffer, append the target.

```
corners < 2                                        → next attempt
last_node → target is blocked (Passable mask)      → next attempt
otherwise                                          → accept, corner index = 0
```

Three details, each paid for by its own bug:

- **a separate buffer**, not `farmWalkScratchCorners`: a probe overwrote the route the caller was
  about to commit, and after four failures the walk got the last rejected candidate. That is how an
  18.8 m two-corner walk turned into "timed out 13,1m short [corner 1/8]";
- the final leg is checked on **Passable**, not `All`: 15–20 m from a point to a resource with both
  ends on the ground is blocked by definition against `All`, and all four attempts always failed;
- corner cutting is **not applied** to a detour: the route exists precisely because the straight line
  is blocked, and collapsing it would put the wall back in the middle;
- the corner index is set to **0** rather than "skip what has been covered": the first corner is the
  whole point of the detour.

### 2.6. Corner cutting — `ShortcutFarmWalkRoute`

This mirrors `TrackingPathModule`: **only the first** corner is removed, and **only** if the ray to
the second is clear. No more than `FarmWalkShortcutMaxRemovals = 3` per build, spanning no further
than `FarmWalkShortcutMaxSpan = 10 m` (the game's `tryLineConnectDis` is the same ~10 m).

```
corners < 2 or Linecast unavailable → exit
for 3 passes:
  distance(from, second) > 10 m                       → stop
  the straight line is not shorter than via corner 1  → stop
  the ray is clear at FOOT height AND at +1.2 m       → remove the first corner
  otherwise                                           → stop
```

**Both** heights are checked: a low ray slips under railings and through gaps.

⚠️ Taking "the furthest visible corner" and deleting everything before it is **not allowed** — that
collapsed the route into a straight line (18.9 m into 2 corners) and walked the player through
buildings. One clear line at foot level does not prove the whole detour is unnecessary; usually it is
just a ray that found a gap.

### 2.7. Choosing the starting corner

Not 0, but the first corner that is **genuinely ahead**:

```
while the corner is not the last:
  reached = horizontal distance(from, corner) <= FarmWalkCornerReachDistance (1.2 m)
  passed  = distance(from, next) < distance(corner, next)
  reached || passed → skip the corner
  otherwise         → stop
```

A\* always begins a route at the node nearest the player, and a few metres later that node is already
**behind** them. Two failures come from here: standing on the node, corner 0 is underfoot, the steering
delta is about zero and the body freezes; a couple of metres past it, every rebuild aims backwards, the
player turns around, walks back, makes progress, and turns around again. From outside it is running
back and forth, while the stuck detector reads 0.00 m because it measures **net displacement**, which
is zero for an oscillation at any speed.

---

## 3. Following the route, every frame

### 3.1. Advancing through corners

The same `reached || passed` rule as for choosing the starting corner. On an advance:
`farmWalkLegStart = the corner just passed`, `farmWalkEverAdvanced = true`, and the futile-rebuild
counter is reset.

Corners run out → `FinishFarmWalk("ran out of corners …", teleport: true)`.

### 3.2. Steering

The direction points at the current corner, rotated by **minus the camera yaw**
(`ToCameraSpaceJoystick` further down the chain rotates it back). Speed is full
(`FarmWalkSpeedMax = 1`) and eases down to `0.2` over the last
`FarmWalkSlowApproachDistance = 2 m` — otherwise the locomotion overshoots the target and circles.
Turning is capped at `FarmWalkTurnRateDegPerSecond = 360°/s`.

During an escape, steering is **switched off entirely**: the escape phase owns the axis alone.

---

## 4. Re-pathing

Three real causes, and only these:

| Cause | Condition | Constant |
|---|---|---|
| off corridor | `DistanceToWalkLeg(self, legStart, corner) > 4 m` | `FarmWalkCorridorTolerance` |
| not closing | `now - farmWalkBestAt >= 1.5 s` (half the window) | `FarmWalkNoClosingTimeout = 3 s` |
| safety cadence | `now >= farmWalkNextRepathAt` | `FarmWalkRepathInterval = 12 s` |

⚠️ **The route is pinned.** It used to be rebuilt unconditionally every 1.5 s — about forty times over
a minute-long walk. Every rebuild re-snaps both ends, so it can return a different chain of corners at
different heights. On land this passed unnoticed; underwater it shows as a vertical flip-flop:
`diving 17,6m / surfacing 1,1m / diving 17,6m / surfacing 2,1m` — the aim jumping between corners of
two different routes while the player bobs in place.

### 4.1. Judging the result of a rebuild

```
routeChanged   = the corner count changed OR corner 0 moved
routeImproved  = routeChanged AND the new route length < the best - 0.5 m
```

`FarmWalkRepathMustGain = 0.5 m` — below the noise of re-snapping both ends and appreciably below the
graph's spacing.

```
routeImproved  → the progress baseline is re-based, the futile counter resets
otherwise      → the baseline is NOT touched, the futile counter grows
```

⚠️ **Different ≠ better.** Re-basing on any change made a walk immortal by a second route — through a
branch the first guard does not cover. In places the graph has two attractors and rebuilds bounce
between them, so the route is never "identical":

```
re-pathed (safety cadence): 3 -> 5 corners
shortcut removed 2 corner(s), 3 left
re-pathed (off corridor):   5 -> 3 corners
```

— every nine seconds, for as long as you care to watch. And since "not closing" counts from
`farmWalkBestAt`, every bounce restarted the clock that is supposed to end the walk.

The verdict is printed in the log: `(IDENTICAL)`, `(RESHUFFLED, no shorter)` or `(N.Nm shorter)`.

### 4.2. Futile rebuilds → banning a waypoint

`FarmWalkMaxFutileRepaths = 3`. On reaching it:

```
there is room in the ban list (< FarmWalkMaxBlockedNodes = 48)
  and the current corner snaps to a node
  and that node is not already banned
    → ban it for FarmWalkBlockedNodeTtl = 300 s, add it to the exclusions,
      reset the counter, re-path immediately
otherwise
    → FinishFarmWalk("the graph keeps returning the same unwalkable route", teleport: true)
```

Before giving up on the **node**, give up on the **waypoint**: the route is wedged on a specific
corner, and A\* will keep producing it until that corner is off the table. Four different targets in a
row died at "corner 2" — different destinations, the same blocked point in the middle.

---

## 5. When the route does not help — the escape

The escalation order on land (the underwater path is its own, see `FARM_WALK_TO_NODE.md` §6a).

### 5.1. Detectors

| Detector | What it measures | Threshold |
|---|---|---|
| displacement | 3-D over `FarmWalkStuckSampleInterval = 0.6 s` | `FarmWalkStuckMinProgress = 0.15 m` |
| route remaining | the length of the path left | `0.5 m` over `3 s` |

Displacement is measured **in three dimensions**: descending or ascending is pure vertical, which is
0.00 m horizontally, and the walker would declare itself stuck while descending 12 m to sea grapes.

Route remaining is the **path length**, not the straight-line distance: going around a hill temporarily
increases the straight distance, and the straight-line metric killed healthy walks.

### 5.2. The apex escape — `BeginFarmWalkHopBurst` / `UpdateFarmWalkHopBurst`

This replaced the old "back off 5 m and jump while pushing the axis into the node". All the numbers
were measured in game.

**The precondition without which none of it works:** during an escape **the axis belongs to it alone**.
Ordinary steering toward the corner is switched off for the duration. Until that was true, the walker
put the axis back on the corner every frame — that is, into the obstacle — and the jump was suppressed
completely: `3 hop(s), airborne 0%, 0.00m up` on every heading.

**Stage 1 — press until wedged.** A push at +45° off the bearing to the target, with the direction
**fixed once** in world coordinates (recomputing it from the current bearing turns the diagonal into an
arc around the target, leaving nothing to press against).

```
creep < 0.1 m over 0.3 s, no earlier than 1.0 s → WEDGED, jump from here
travelled >= 3.5 m or 6 s elapsed              → not wedged
travelled < 0.3 m                              → this side is a wall
not wedged: side +45 → try −45; both empty → jump from the blocking point
```

⚠️ An empty press **does not move the reference point**. Otherwise the jumps merely undo the drift,
gain a metre relative to the shifted point and report victory: three "cleared it" in a row while the
walk went 21.1 → 20.5 → 20.3 m.

⚠️ A fruitless press **is remembered for the whole walk**: later escapes go straight to jumping.
Otherwise each one spends another eight metres on a search known to be empty.

**Stage 2 — apex jumps** on the headings `45° → −45° → 0°`.

One cycle:
```
phase 0: RELEASE the axis, then the impulse       ← in exactly that order
phase 1: no axis, wait 0.40 s for the apex        ← no shortcuts based on leaving the ground
phase 2: at the apex, engage the axis
touching the ground: drop the axis immediately
0.35 s to settle → the next impulse
```

Three places where the order is critical, each proven by a failure:

- **release before the impulse.** The calling code decides the axis's fate only after returning, so the
  impulse went out with the previous frame's axis — the last frame of the press, aimed at the wall. The
  heading that matched the press direction gave `airborne 0%`, the others 25–69%;
- **wait the full delay.** The shortcut "or leaving the ground + 0.2 s" engaged steering halfway up.
  Same heading: **+4.00 m** of height with input at the apex against **+0.36 m** at take-off;
- **drop the axis on contact.** Holding it another 0.35 s on the ground meant the next impulse went
  into a body that had been pressed against the obstacle for a third of a second.

**Repeat what pays:**
```
closer >= 0.25 m OR rise >= 0.5 m → repeat the heading (up to 6 times)
otherwise                         → next heading
headings exhausted                → press from the other side, or end the escape
```

The thresholds were 0.30 / 0.80 and dropped a working heading: `+0,30m closer, +0,52m up — next
heading`, when it was still gaining half a metre of height per jump.

**Success** is closing on the target horizontally by `>= 1 m`, **on the ground**, measured from the
blocking point. But the escape does **not** end there: the win is recorded, the series continues while
it pays, and finishes when it stops. The old immediate exit chopped one climb into three escapes of one
or two jumps each with blockings in between.

**Budget:** 22 s per escape, up to **3 escapes per walk**. One was enough only while the escape did not
work: the first went onto a ledge and there was nothing left for the last two metres — `final approach
not walkable (2,0m, dy=-0,1m) … jumps are spent`.

**Where it is called:** the final approach, being stuck, and "not closing". All three used to send a
single `TryFarmWalkJump` impulse — a suppressed mode; it survives only in the underwater branch.

The numbers this is built on:

| Claim | Measurement |
|---|---|
| pushing into an obstacle suppresses the jump | 43 impulses → 0.16 m travelled, `grounded 100%`; 26 "jump first" → 4.15 m, `airborne` up to 32% |
| steering is needed at the apex | simultaneous +0.11 m · at take-off +0.36 m · **at the apex +4.00 and +3.67 m** |
| 45°, not perpendicular | 0/±45 → +1.24 and +2.45 m closer; ±90 → about +0.31 m |
| a climb is a series | five identical jumps by hand: 21.0 → 13.4 m; the first two +0.52 and +0.60 m |
| pressing before jumping helps | a sweep from a bare point — 9 headings in the negative; from a wedge the first heading gave +3.90 m and arrival 3.5 s later |
| the arc's apex | a peak of +1.42 m (`MotionConfig.JumpingHighest` 1.30 plus the run-up), apex at ~0.42 s |
| the result in the mod | one escape: **+3.58 m closer, +4.05 m of height**, 5 jumps, `airborne 63–93%` |

### 5.3. Arrival

The threshold is `0.25 m` horizontally plus a `3-D <= 1.8 m` check (the server's rule,
`CollectAntiCheating.Distance = 2 m`). Splitting the axes matters: walking cannot change height, and a
pure 3-D check made any slightly raised resource unreachable.

### 5.4. The teleport

Only as a safety net: the route failed to build, or the stuck ladder is exhausted. Radius
`FarmWalkRescueTeleportRange = 10 m`, cooldown `FarmWalkRescueTeleportCooldown = 60 s`, at most
`FarmWalkMaxNodeFailures = 2` failures per node — after that the node is parked and the farm goes to
another.

---

## 6. Routing constants at a glance

| Constant | Value | Meaning |
|---|---|---|
| `FarmWalkGraphSnapRadius` | 60 m | radius for snapping the ends to the graph |
| `FarmWalkSnapMaxProbes` | 12 | how many nearest nodes to test with a ray |
| `FarmWalkCornerReachDistance` | 1.2 m | a corner counts as reached |
| `FarmWalkCorridorTolerance` | 4 m | deviation from the route leg = "off corridor" |
| `FarmWalkRepathInterval` | 12 s | the safety cadence for re-pathing |
| `FarmWalkRepathMustGain` | 0.5 m | how much shorter a route must be to count as better |
| `FarmWalkNoClosingTimeout` | 3 s | the "not closing" window (re-path at half) |
| `FarmWalkMaxFutileRepaths` | 3 | futile rebuilds before banning a point |
| `FarmWalkBlockedNodeTtl` | 300 s | how long a waypoint stays banned |
| `FarmWalkMaxBlockedNodes` | 48 | the ban list's limit |
| `FarmWalkShortcutMaxSpan` | 10 m | the maximum span of a cut |
| `FarmWalkShortcutMaxRemovals` | 3 | cuts per build |
| `FarmWalkShortcutProbeLift` | 1.2 m | the second height a ray is checked at |
| `FarmWalkDetourAttempts` | 4 | attempts to find a detour |
| `FarmWalkStuckSampleInterval` | 0.6 s | the displacement detector's cadence |
| `FarmWalkStuckMinProgress` | 0.15 m | that detector's threshold |
| `FarmWalkStuckStrikeLimit` | 3 | strikes before escalating |

The escape has its own constants, `FarmWalkEscape*`, see §5.2.

---

## 7. What has been disproved here and must not come back

- **Unconditional re-pathing on a timer** — the source of the vertical flip-flop and of walking in
  circles.
- **Cutting "to the furthest visible corner"** — collapses the route into a straight line through
  buildings.
- **Re-basing the progress baseline on any route change** — makes a walk immortal.
- **Checking the final leg on the `All` mask** — blocked by definition, so a detour is never found.
- **A ray along the ground with no lift** — "the straight line is blocked" on any slope and on an open
  beach.
- **Backing off with a run-up and jumping while pushing into the obstacle** — the jump is suppressed
  entirely.
- **Judging a jump by leaving the ground** — on a slope `grounded` never releases for a single frame
  even though the climb is happening.

Related documents: `FARM_WALK_TO_NODE.md` (the walk as a whole, underwater specifics, target
eligibility), `TECHNICAL.md` (the waypoint graph and how it is built).
