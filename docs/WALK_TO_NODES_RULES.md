# Walk to Nodes: the rules

Normative document: **how the mode is supposed to behave**. Distinct from
[FARM_WALK_ROUTING.md](FARM_WALK_ROUTING.md), which describes *what the code does*, step by step.

Every rule is numbered so it can be cited. Tags:
**[M]** — measured in game, the number is confirmed;
**[J]** — a judgement call, nothing measured behind it;
**[?]** — in doubt, needs checking.

Files: `buddy/FarmWalkFeature.cs`, `buddy/FarmWalkGeometryFeature.cs`,
`buddy/FarmWalkVehicleFeature.cs`, `buddy/FarmRoutePlanFeature.cs`, `buddy/TrackPathGraphFeature.cs`.

---

## 0. General (all modes)

**0.1** Movement goes through the joystick axis only (`TrySetGameMoveAxis`). Position is never
written directly — the server must see ordinary travel. **[M]**

**0.2** A teleport is an emergency exit, not a means of travel. Permitted **only** when the walk has
spent its budget **and** this is the only target.

**0.3** Exactly one owner writes the axis each frame. While an apex escape runs, ordinary steering
stays silent; every other unstick phase, conversely, takes its direction from **inside** the
steering. **[M]**

**0.4** Arrival is judged by the aura's trigger radius — **1.5 m** — not by zero distance. The walker
stops at a **stand-off**, not on top of the node: driving in to 0.25 m made the last metre the
expensive part of every walk. **[M]**

**0.4a** The stand-off is **per resource kind and learned, not assumed**. It starts at **1.1 m**.
Measured 2026-08-22: Raspberry, Ore, Stone and Mandarin Tree all collected from ~1.05 m while a
Button mushroom at 1.03 m did not — so there is no single right number, and nothing is assumed about
the kinds that already work. **[M]**

**0.4b** A kind that times out with its marker still available **steps in by 0.3 m and retries the
same node**, up to 4 steps, floored at 0.25 m — below which we are standing on the node, which is
what the stand-off exists to avoid. Learning a number and walking away means the node that taught
the lesson is lost, and if the new number is still too far the next one is lost as well. Stepping in
on the node in hand costs seconds and finds the real distance rather than a safe guess: Button
settled at **0.8 m** in one step, where a single jump to a safe value had used 0.5 m. The step budget
is per stubborn node — a successful collect resets it — while the learned distance persists. **[M]**

**0.4c** Every arrival test uses the learned stand-off, including the "height not closed but still in
reach" fallback. That fallback kept a global 1.4 m after the walk had already proved the kind does
not collect from 1.1 m, and accepted an arrival at 1.37 m that collected nothing. **[M]**

**0.5** A target is judged **only** by an authority that knows something about it. The collectable
scan rules on `node:` walks alone; `quest:`, `cleanse:`, `area:` and bubbles are outside its
jurisdiction. Stated as a positive rule rather than a list of exceptions — the list turned out
incomplete four times running. **[M]**

**0.6** No route built — take **another target**, not a teleport. The node is parked for 45 s and
returns to the plan. **[J]**

**0.7** In walk mode the target is chosen by **route length**, not by straight line. Teleport mode
keeps the straight line: a warp costs the same from anywhere. **[J]**

**0.7a** The measurement must **reproduce the builder**, not approximate it: the same reachable start
snap, the same corner shortcutting. Otherwise it compares numbers that are not comparable — raw A\*
output over a sparse graph reported "34 m away costs 266 m to travel" for a swim the walker would
have taken in one straight leg. **[M]**

**0.7b** A candidate whose route **could not be measured** is not dropped: it is ranked by straight
line with a penalty. Unreachability is proved by a failed walk (§0.6), not by the measurer's
silence. **[M]**

**0.7c** A measurement exceeding **three times the straight line** is not a route, it is the graph's
sparseness: treated as unmeasured. **[M]**

**0.7d** The nearest by straight line keeps the pick unless the winner is better by **20 % and at
least 5 m**. Otherwise the metric's own error decides: one run took a target 48 m away over one
39 m away to save two metres. **[M]**

**0.7e** Candidates are measured in straight-line order and the search **stops on a bound**, not on a
count: once the best measured route is no longer than the straight line to the next candidate,
nothing remaining can win, because a route is never shorter than its straight line. A fixed
shortlist of four was a cost guess with nothing behind it — and on land it silently became "the
first four stops of the planned circuit", which is a route order, not a distance ranking. The
remaining cap (12) is a backstop against a pathological field, not the policy. **[M]**

**0.8** Every heuristic (bans, exclusions, filters) must have a starvation valve: it may never make
routing impossible. **[M]**

**0.9** After a collect — hold in place for **1.5 s** so the drop can be picked up. **[?]** This is a
hold, not a confirmation: nothing watches an item reach the bag.

**0.10** Everything the mod decided and did not do goes into the log with its reason. A silent
refusal is forbidden — over one session it masked a real defect as "all clear" three times.

**0.11** Any deliberate hold (repair aura, collect dwell, drop pickup) must suspend the progress and
stuck detectors. Those timers measure failure, not a pause: eighteen seconds of standing in a repair
aura, exactly as instructed, once ended in a rescue teleport. **[M]**

---

## 1. Building a route

**1.1** Order: snap the start → snap the end → A\* over the waypoint graph → append the real target →
degenerate-route check → edge audit → corner shortcutting → choose the starting corner.

**1.2** The start snap takes not the nearest node but the nearest **reachable** one: a ray on the
Passable mask **and** the ground profile (step ≤ 0.5 m per sample). Radius 60 m, up to 12
candidates. **[M]**

**1.3** The end snap stays on the nearest node: the last leg decides there, and the walker handles
that itself.

**1.4** A\* skips banned nodes. The end node is exempt — refusing to arrive where we were asked is
failing the route, not routing around an obstacle. **[M]**

**1.5** Edge audit: every leg is checked by the sweep **and** the ground profile. An impassable leg →
ban the waypoint it ends at, and rebuild. No more than **2** attempts per build.

**1.5a** The sweep runs at **two heights**: the capsule's centre (0.63 m) and knee height
(**0.30 m** = stepOffset + radius). One sphere in the middle is not a player — a rail or kerb below
0.48 m passes under it and reports clear, and the player walks into it. **[M]**

**1.5b** A leg is passable only when it is passable **in both directions**. Confirmed in game
2026-08-22: standing against a barrier, walking away from it is free while walking into it from open
ground is blocked, because the sphere is built **at the origin** and a collider it already touches
there is discarded by the cast. A leg that begins beside a wall therefore reports clear and the walk
hits that wall on its first step. **[M]**

**1.5c** Both directions of that comparison must be **levelled** — the oracle lifts only its origin
(radius + 0.05), so the raw two directions are different tilted segments and their disagreement is
the call, not the world. Pre-lower the origin by the same amount. Measured with and without: 228
asymmetric edges vs 229, so the tilt does not explain the result — but it has to be removed before
that can be said. **[M]**

**1.5d** Only a **near-level** leg (rise ≤ 0.5 m) is rejected for being one-way. Asymmetry appears on
any slope for a harmless reason — from the lower end the sphere already rests on the ground — and
unfiltered that gave **229** asymmetric edges against **2** once level ground was required. Steeper
disagreements are logged, never banned. **[J]**

**1.5e** A **solid column** — no height in the span where the capsule fits — standing in a leg makes
it impassable. This is the shape the barriers on the shore and on the rope bridge take, and neither
has a collider to find. ⚠️ Solid at the TOP is not a solid column: rejecting on that alone bans every
leg passing under a bridge deck or an arch. Keep descending until free air is found. **[M]**

**1.6** A ban is **never** issued on leg zero: it starts at the player and speaks about where they
are standing, not about the graph. Eight false bans in six seconds came from exactly that. **[M]**

**1.7** Nor on the final leg: it ends at the resource, and there is nothing there to take off the
table.

**1.8** No more than **2** bans per walk. More than that means the test is wrong, not the graph. **[J]**

**1.9** Shortcutting: only the first corner is removed, and only if the ray is clear **at foot level
and at +1.2 m**. At most 3 per build, span ≤ 10 m. Taking "the furthest visible corner" is
forbidden — that collapsed routes into a straight line through buildings. **[M]**

**1.10** The starting corner is the first one genuinely ahead, not index zero.

**1.11** The game's own route (Quest Walk injects it wholesale) is checked by the same rules. On an
impassable leg — including leg zero — it is **rejected** and ours is built instead. A route once
rejected is never injected again: the game refreshes it every few seconds and would overwrite
ours. **[M]**

---

## 1a. Re-pathing

**1a.1** A re-path is triggered by **three causes only**, and there must be no others:
* **off the corridor** — deviation from the segment "leg start → current corner" over 4 m;
* **not closing** — half of the no-progress timeout;
* **safety cadence** — a long timer, so that "never re-path at all" does not become its own trap.

**1a.2** The corridor is measured from `farmWalkLegStart` to the current corner, **in XZ**. Hence the
obligation: **every** route builder sets `farmWalkLegStart`. The direct-swim branch did not, the
corridor measured against the previous route's point, and "off the corridor" fired **every second**
on a perfectly straight swim. **[M]**

**1a.3** A re-path that changed nothing (`IDENTICAL`) is a **symptom**, not work. If it repeats, the
trigger is broken, not the graph.

**1a.4** Three futile re-paths in a row ban a waypoint. So a false trigger does not merely make
noise — it **corrupts the graph**: seventeen underwater bans came from a broken corridor, not from
geometry. Every change to the triggers must be checked against this chain. **[M]**

**1a.5** The route is never rebuilt while an escape runs: the escape captured its aim at the start
and measures every hop against it. **[M]**

**1a.6** No more than once per second, whatever the reason. **[M]**

**1a.7** A new route is accepted only if it is at least 0.5 m shorter. Identical, or "reshuffled but
no shorter", is rejected: that was the cause of a live 3↔5 corner lock. **[M]**

**1a.8** The verdict is written in words — `IDENTICAL` / `RESHUFFLED, no shorter` / `N.Nm shorter`.
Without it, "re-pathed" is indistinguishable from "twitched". **[M]**

---

## 2. On foot

**2.1** Walking climbs a **0.15 m** step (`stepOffset`). A slope up to **0.5 m** per sample counts as
walkable. A jump clears **1.42 m**. Above that, nothing does. **[M]**

**2.2** Judging a route by the jump ceiling is forbidden: that is what an escape drags out of the
body in extremis, not what walking does.

**2.3** The escape ladder, in order:
1. **a plain running jump** — an impulse every 0.35 s, axis held at the aim, ending the moment it has
   cleared the obstacle (§5.8) and at 1.5 s regardless;
2. **press 45°**, then −45° — looking for a corner;
3. **apex hops** on headings 0 / ±45° from the wedge.

**2.4** Jumping and moving are **incompatible**: an axis pressed into an obstacle suppresses the jump
(43 impulses → 0.16 m of travel, the body never left the ground). So the axis is released **before**
the impulse. **[M]**

**2.5** Steer **at the top of the arc**, 0.4 s after the impulse, not at lift-off: the same heading
gained +4.00 m of height when the axis went in at the apex and +0.36 m at lift-off. **[M]**

**2.6** The steer is released on touchdown. Holding it through a landing suppresses the next jump.

**2.7** The steering direction is **frozen** for the whole attempt. A constant angle off a live
bearing is a circle, not a diagonal: against an aim two metres away one lap closes in six
seconds. **[M]**

**2.8** A heading that pays is repeated — up to 6 times. Success is ≥1 m of closing; a series that
has already earned its metre is carried to the end.

**2.9** A press that runs free (≥3 m without wedging) means **there is no obstacle**. The escape ends
immediately. **[M]**

**2.10** No more than **3** escapes per walk; one escape's budget is 22 s.

**2.11** **[?]** Horizontal closing counts as success even while descending. Jumps downhill always
earn it — the criterion is worth revisiting.

**2.12** **Below is not a dead end.** A node the walker cannot close vertically is abandoned only
when it is ABOVE: 3.6 m of climb is beyond every action the walker owns. Downwards the player simply
steps off and falls, which costs nothing — so with the horizontal already closed, the walker keeps
going along the direction it **arrived on** until the ground runs out. Two attempts, 1.6 s each; the
budget is against a node that is below because it sits under solid rock, where walking on only meets
more ground. Measured 2026-08-22: two nodes skipped in thirteen seconds, one of them a 1.5 m step
down. **[M]**

**2.12a** The drop pushes only until the ground goes away. Once the body is falling the axis has
nothing left to contribute, and holding it carries the player clear over the node — who lands past it
and turns back. Stepping off is a push followed by a release (§5.8). **[M]**

**2.12b** Where the node is below and the horizontal is closed, the **drop outranks a running
escape** — it does not wait for it. One owner per frame is the rule (§0.3); which owner is the
question, and a hop burst is for horizontal obstacles with nothing to offer against a node three
metres down. Deferring to the escape instead killed the drop outright, because the stuck detector
fires first and an escape is therefore always already running by the time the branch is reached. **[M]**

**2.13** Steering at the target is useless once the horizontal is closed — the delta is nothing and
there is no direction left in it. The direction that leaves a ledge is the one the walk **came in
on** (`farmWalkLegStart → target`). **[M]**

**2.14** A stuck strike may not be scored in the first **1.5 s** of a walk. A strike is 0.15 m missed
in a 0.6 s window and a single strike launches an escape — but the body has just stopped from the
previous walk and is still turning and accelerating, so the first window reads as no progress on open
ground. One walk was declared wedged at 12.9 m one second after starting, and the press then proved
there had never been an obstacle. **[M]**

---

## 3. In a vehicle

**3.1** A car does not jump. The apex escape does not apply to it at all, and the delegation sits
**inside** `BeginFarmWalkHopBurst` so that a new call site cannot forget. **[M]**

**3.2** The driver's ladder: reverse **2 m** → pull out sideways **5 m**, sides alternating per
round, **2** rounds. Each leg ends on distance covered; the 4 s timer is only for "blocked this way
too".

**3.3** Two rounds did not clear it — **get out**, and the on-foot ladder takes over. If the obstacle
is then cleared on foot and enough distance to the target remains, the vehicle is **remounted**.

**3.4** Dismounting happens **before** the destination, not on top of it: the last stretch is on foot
regardless.

**3.5** **[?]** Leg passability for a vehicle is tested exactly as for a pedestrian, though its
footprint and clearance differ. Nothing measured.

---

## 4. Underwater

**4.1** Distances are computed **in 3-D**. Descending *is* travel, and the spread of depths exceeds
the spread in the horizontal. On land it is the reverse — flat. **[M]**

**4.2** If the straight line to the target is clear by the sweep and the target is within **50 m** —
swim straight, **no graph needed**. The sweep is finally the right instrument here: its flaw on land
is that it flies over terrain, and a swimmer does precisely that. **[M]**

**4.2a** **[?] NOT IMPLEMENTED.** A straight swim should **follow the depth along the track** rather
than diving or surfacing all the way. An attempt to do that by interpolating depth against
horizontal progress (2026-08-22) was rejected — it behaved worse than before. Depth control still
aims at the **target's** depth, not the line's.

**4.3** The 50 m limit exists because a single sweep vouching for a two-hundred-metre crossing is a
bet, not a measurement. **[J]**

**4.4** The target chosen is the **nearest**, not the next in the plan: the underwater graph has 86
nodes against 1745 on land, with 20–30 m between them, and an order computed from distances is
worthless there. **[M]**

**4.5** During a swim, every 2 s it is checked whether something has become materially nearer.
Switching requires: the candidate is ≥6 m nearer **and** within 60 % of what remains **and** there is
still ≥12 m to swim. The abandoned target is not stamped visited. **[J]**

**4.6** Passability checks and waypoint bans **do not apply** underwater: the vertical is free there,
a 2.65 m ledge is not an obstacle, and the sweep's verdict would ban perfectly good nodes. **[M]**

**4.7** No ground under a sample is **not a verdict**. An empty column is air **or water**, and
nothing distinguishes them: there is no water level to query for an arbitrary point. Banning a ford
over that would be a mistake. **[M]**

**4.8** The apex escape does not run underwater: it is built entirely on leaving the ground and
landing again. There is its own ladder — back off 5 m, a vertical leg, a 4-direction probe. **[M]**

**4.9** Depth: engage band 0.35 m, release band 0.12 m, re-asserted once a second. The descent
deferral carries 0.75 m of hysteresis and is dropped for good the moment a walk has had to unstick
even once. **[M]**

**4.10** **[?]** One depth hold moves about 0.72 m — more than the deadband. Near the last metre the
controller never settles and oscillates. Not fixed.

---

## 5. Forbidden

**5.1** Judging passability by the sweep **alone** or the ray **alone**: both fly over terrain when
the ends differ in height. The ground profile decides. **[M]**

**5.2** Reading "no ground" as "no way through". **[M]** An empty column is air **or** water and
nothing here separates them; a column where the capsule fits **nowhere** is a different answer
entirely (1.5e), and folding the two together is what hid every wall the collider table cannot see.

**5.2a** Testing passability in **one direction only**. It cannot see a one-way barrier, which is
what the game's own collision query produces wherever a sweep starts against something. **[M]**

**5.2b** Treating the collider table as the authority on obstacles. Measured twice with the player
pressed against a barrier: **zero** blocking colliders within 4–5 m. Terrain and built structures
are not in it. Use it to NAME what was hit, never to decide whether something is there. **[M]**

**5.3** Banning a waypoint on a leg that starts at the player. **[M]**

**5.4** Committing to a target whose reachability is unknown while an authority for it exists and was
not asked.

**5.5** Teleporting instead of changing target.

**5.6** Staying silent about a decision that was made.

**5.7** Encoding as a constant something the game can be made to tell us. A number that a failure
already reveals — the stand-off of §0.4a is the worked example — should be learned from that failure,
not guessed once and defended afterwards.

**5.8** Ending an action on a timer when it has a testable outcome. A timer is a **limit**, not a
criterion: it says "stop trying", never "it worked". Three of these shipped in one day and each cost
seconds of visible nonsense — the plain jump pulsed for its full 1.5 s after the first jump had
already cleared the kerb; the ledge drop pushed for its full 1.6 s and carried the player past the
node, who then turned back; the press ran its distance instead of stopping at the wedge. Ask for the
outcome, and keep the timer only as the backstop for when the outcome never arrives. **[M]**
