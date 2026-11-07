# House building — reading, generating and placing home designs

How a Heartopia home is stored, how to read one out of the running game, what the
catalogue and the placement rules actually are, and how to build a design from an
agent over the MCP bridge.

Everything marked **measured** was verified live against build **2.9.5** on
2026-09-07/08 in a home in blueprint-edit + god mode. Everything else comes from
`ilspy-dumps/` and `oversea_tables.db`.

**Status: the round trip works.** An 842-object home was dumped, replayed onto an empty blueprint
and diffed back to **842/842 position-exact, zero missing, zero stray, zero genuine size or dye
differences** (§7.5). If you read only one section before building, read **§7.2d** — zone
permission comes from a camera ray, not from the position you set, and nothing else works until
that is handled.

**Game update 2026-09-24:** the official Ignore Overlap / Floating Placement / Free Placement /
Free Rotation settings, the PC build hotkeys and the Advanced-mode WASD camera are in **§12**.
The build engine itself (detections, confirm, protocol) did not change shape; §7 still holds.

**See also:** [UGC_BUILD_MECHANISMS.md](./UGC_BUILD_MECHANISMS.md) (what placed
mechanisms *do*), [UGC_SYSTEM.md](./UGC_SYSTEM.md) (player-made records/books),
[MCP_BRIDGE.md](./MCP_BRIDGE.md) (how to reach the running game),
[DYE_REPLICATION.md](./DYE_REPLICATION.md) (a furniture piece's dye: the owner paints raw
RGB, a visitor shows it only when the `Color32` is an exact palette entry).

---

## 1. The mental model

A home is a **flat list of placements** inside one build field:

```
placement = (staticId, localPosition, angle, [size ext], [dye], [state])
```

There is no room graph, no wall/floor topology and no scene file. Walls, floors,
roofs, stairs and teacups are all the same kind of record — they differ only by
`FurnitureTypeFlag` and by which surfaces they are allowed to snap to. That is
why a design can be treated as pure data: read it, transform it, write it back
placement by placement.

Three layers hold that list, and they disagree about detail — pick by purpose:

| Layer | Type | Holds | Use it for |
|---|---|---|---|
| **View** (client only) | `FurnitureBaseComponent` + `Entity` | staticId, world **and** local transform, furniture type | reading the home you are standing in |
| **ECS** (replicated) | `ItemComponent`, `LocalPositionComponent`, `AngleComponent`, `RoofComponent`, `DyeColorNewComponent`, … | the authoritative per-item state | server truth, sync events |
| **Blueprint** (serialised) | `BuildBluePrintData` | the whole design as one MessagePack tree | exporting / importing a design |

---

## 2. Coordinates, grid and the buildable volume

### 2.1 Frames

Every placement is stored **local to the build field root**, not in world space.
The field root is translated and yaw-rotated relative to the world (**measured**:
`pos (108.89, 22.874, 21.67)`, `yaw 266.6°` for one particular home — it is per
home and per session, never hardcode it).

```
world = fieldRootPos + fieldRootRot * local
local = inverse(fieldRootRot) * (world - fieldRootPos)
```

Recover the root from any placed item, since the view `Entity` exposes both
frames:

```
fieldRootRot = entity.rotation * inverse(entity.localRotation)
fieldRootPos = entity.position - fieldRootRot * entity.localPosition
```

`snippets/build_state.cs` prints both.

> Why this matters: `GodCraftMode._dstPosition` — the field you write to move a
> held object — is **world**. A design is **local**. Skipping the rotation makes
> X and Z come out swapped or diagonal.

### 2.2 The unit system

`XDTLevelAndEntity.Core.Craft.CraftMath`:

| Constant | Value | Meaning |
|---|---|---|
| `SlabUnit` | 1.0 m | one floor slab |
| `BuildUnit` | 0.5 m | the coarse build cell |
| `HalfUnit` | 0.25 m | the finest snap |
| `PositionPrecision` | 1000 | positions quantise to **millimetres** |

Per-item snapping comes from `Putitem.precision` through
`CraftMath.PrecisionToCellSize`, which is **not** a simple divisor:

```
if precision > 100:            # three packed per-axis digits, e.g. 424
    cell = (d(p/100), d(p%100/10), d(p%10))
else:
    cell = (d(p), d(p), d(p))
where d(n) = clamp(n, 1, 8) * 0.25
```

So `precision 2 → 0.5 m` (most furniture), `4 → 1.0 m`, `424 → (1.0, 0.5, 1.0)`
(floor slabs), `1 or 0 → 0.25 m`. Anything else is authoring noise.

That cell applies in the game's `Fast` and `Fine` place precision. The official **Free**
place precision (2026-09-24, §12.3) skips it entirely: no anchor snap, only
`CraftMath.ReducePrecision(Vector3)` to the millimetre.

Footprints are stored in **half-metre units** — `TableData.InitPutItemData` does
`realLength = length * 0.5` (likewise width/height). A `length` of 2 is a 1 m item.

### 2.3 The field volume

| Constant | Value |
|---|---|
| `FieldLeft` / `FieldRight` | 12 m each → X ∈ [−12, +12] |
| `FieldFront` / `FieldBack` | 9 / 21 m → Z ∈ [−9, +21] |
| `LimitBelow` / `LimitHeight` | −5 / +8 m (`MaxLimitHeight` 16 with tracks) |
| `LimitLength` × `LimitWidth` | 24 × 30 m |
| `MaxCellX/Y/Z` | 48 / 16 / 60 (0.5 m cells) |

**Measured** on a real home: X ∈ [−11.5, 11.5], Z ∈ [−6, 8.75], Y ∈ {0, 0.06,
0.49, 0.5, 0.85, 2.5, 3.5} — consistent with the above.

### 2.4 Angles

Rotation is packed into one int, all three euler axes:

```
angle = (X << 20) | (Z << 10) | Y          # BuildingCalExtensions.ToBuildingRotValue
(X, Z, Y) = (v >> 20, (v >> 10) & 0x3FF, v & 0x3FF)
```

Blueprints split the same int into two ushorts: `Angle = v & 0xFFFF`,
`Angle2 = v >> 16`.

Snapping is `CraftMath.ReducePrecision(q, anglePrecision, side)` — it rounds the
euler component belonging to the placing face to a multiple of `anglePrecision`
(`Putitem.rotateAngle`, default 90, clamped to ≤180). **X/Z tilt only survives
with the Free-angle toggle on** (`anglePrecision = 1`); otherwise confirm snaps
it away. `buddy/BuildingFreeRotateFeature.cs` drives the same fields from the
mod's Free Placement panel.

The official **Free** rotate precision (2026-09-24, §12.3) also yields
`CraftMath.EffectiveAnglePrecision == 1`, but its slider turns the item about the
**placing-side axis only** (−180…180°, whole degrees); X/Z tilt still needs the
mod's `_dstRotation` jog. Every axis reaches the wire in whole degrees either way
(`ToBuildingRotValue`).

Positions are packed for blueprints too:

```
posValue = ((x*1000 + 512000) << 40) | ((z*1000 + 512000) << 19) | (y*1000 + 100000)
```

Note the asymmetry: X and Z get 21 bits around a ±512 m origin, Y gets 19 bits
around −100 m.

---

## 3. Reading the home you are standing in  (**measured**)

The view layer is the cheapest complete read: one `Entities.GetComponents<T>`
call, no entity-graph walking.

```
FurnitureBaseComponent                       (XDTLevelAndEntity.Gameplay.Component.Homeland)
├─ _coreData : LevelEntityComponentData      → staticId, stage, resType, ownerId
├─ _extData  : FurnitureBaseData             → homeNetId, dyeColorData, size (StructSize?), indoor
├─ _furnitureTypeFlag : FurnitureTypeFlag
└─ <entity>k__BackingField : Entity
   ├─ _netId                                  (0 until the server assigns one)
   ├─ get_position / get_rotation             world
   ├─ get_localPosition / get_localRotation   field-root local  ← the design frame
   └─ <transformComponent>k__BackingField : ResourceComponent
      └─ _transformData : TransformComponentData → parentNetId, position, rotation
```

Ready-made snippet: **`tools/HouseDesign/snippets/dump_home.cs`**. Pass the output
path as `args`; it writes one JSON row per item. **Measured**: 842 objects, 49
distinct staticIds, ~30 ms.

Two traps:

- **A retired item sits at local `y = -100`.** Deleting from the build UI recycles
  the view component rather than destroying it, so a stale entry lingers for a
  while. The dump snippet filters `y < -50`; a raw count will be off by however
  many objects were deleted this session.
- `_netId` is **0** for anything placed but not yet saved — locally-created items
  only get an id when the batch reaches the server.

### 3.1 Sizes and dye — the two fields that need a raw read  (**measured**)

`FurnitureBaseData` carries both, and both are traps for the curated reader:

| Field | Type | How to read it |
|---|---|---|
| `size` | `Nullable<StructSize>`, **inline** | `mono_field_get_value_object` |
| `dyeDolorData` | `List<DyeColorData>` | ordinary field read + collection enumeration |

`IMonoApi.TryGetField` hands back a **pointer to the inline struct data**, not a box, so reading
`height` by name lands 16 bytes past the value and answers 0 for everything — the inline-struct
offset trap. It looks like "no item has a size", which is wrong. Only the field that happens to sit
at the box's own offset (`length`) reads correctly, which makes the bug look like real data.

`mono_field_get_value_object(domain, field, obj)` boxes it properly instead: a `Nullable<T>` comes
back as null or a genuine boxed `T`, and then field-by-name reads work normally. Pin `obj` across
the call — boxing allocates, and an allocation can move it. `HouseBuilderPlugin.BuildBridge.BoxField`
does this by reflecting the mod's own `auraMonoObjectGetClass` / `auraMonoClassGetFieldFromName` /
`auraMonoFieldGetValueObject` exports.

**`StructSize` units are quarter-metres**: `length, topLength, width, topWidth, height` are ints,
`curvature` a byte. Measured on a real home — most furniture is all-zero (fixed size), while
pillars carry `height` 12 and 14 (3.0 m and 3.5 m) and a roof piece is `40 × 8 × 2` (10 × 2 × 0.5 m).

**`DyeColorData` is `{byte body, byte colorIndex, int color, int texture}`.** `color` is **RGBA
packed big-endian** — R is the high byte, alpha the low one (always 255 here), so
`#%02X%02X%02X % (v>>24, v>>16, v>>8)`. `texture` is the material's own staticId and resolves
through the catalogue: measured 36948 Square Tile Flooring, 36923 Smooth Brick Wall,
36952 Concrete Flooring, 36940 Concrete Ceiling. `body` selects the part — 0/1 for the two colour
slots of the down side, 16/17 for the up side (`OverLoadData` builds it as `slot | (upSide << 4)`).
Those same texture ids are what the Load counter charges as materials (§6.1).

### 3.2 Structure sizes

Walls, floors and roofs carry a variable size in `RoofComponent {length, width,
height, curve, upLength, upWidth}` — quarter-metre units — packed for the wire
into `StructExt` (`ulong`, one byte per field: length, width, height, type,
topLength, topWidth, curvature). `StructSize.GetSize()` returns
`(length, height, width) * 0.25`.

### 3.3 Reading another player's home  (**measured 2026-09-08**)

The read path in §3 is the **view layer**. It needs no build mode, no god mode, no blueprint and no
ownership — only that the home is streamed in. So standing next to a neighbour's house is enough to
dump it, and nothing about the procedure differs from dumping your own.

What does differ is that the scene now holds more than one field, and they must be told apart.

**`FurnitureBaseData.homeNetId` is the discriminator, and `0` is not a home.** The zero group is the
public-area props, parented to the scene root — their "local" position *is* world, so mixing them
into a dump puts two frames in one file. Measured in a neighbourhood: 709 components = 687 in the
neighbour's field + 22 area props whose local coordinates span X −229…11, Z −187…256.

**`LevelEntityComponentData.ownerId` is the owner's player netId** (0 on area props). Measured
alongside `game_player_get`: local player 7599450, three neighbouring plots owned by 7700829,
7775814 and 7789115.

⚠️ **Only the home you are beside is streamed, and it unloads whole.** Measured: walking 41 m
dropped home 7709200 (687 items) to **zero** components and streamed home 7782384 (539 items) in its
place. A home missing from the list is a proximity problem, never a permission one — and a dump
taken while walking can be silently partial. Stand still, run `homes`, then dump.

**The field root must come from that home's own items.** Any single item yields the whole frame
(`rootRot = worldRot * inverse(localRot)`, `rootPos = worldPos − rootRot * localPos`), but it must be
an item *of that home*. Before this was enforced, a neighbour dump was written with the root of the
home the player had last stood in — a plausible-looking file, wrong by 200 m.

```
plugin.call house homes                        → every streamed field, nearest first
plugin.call house dump {"path":…}              → the only field in the scene; refuses if ambiguous
plugin.call house dump {"path":…,"nearest":true}
plugin.call house dump {"path":…,"home":7782384}
plugin.call house dump {"path":…,"owner":7775814}
plugin.call house dump {"path":…,"includeArea":true}   → no filter, frames mixed (diagnostics only)
```

The file carries `home` and `owner` alongside `fieldRootPos`/`fieldRootYaw`, so a dump says whose it
is. `render_design.py` reads it unchanged.

Two caveats that a foreign dump will trip over:

- ⚠️ **The local Z axis is not oriented the same way in every home.** Ours spans Z −9…+21
  (§2.3); the neighbour's spans **−19.1…+8.6** — the mirror image. `hdesign.FIELD_FRONT` /
  `FIELD_BACK` describe *our* plot's orientation, so `validate()` reports a foreign dump as largely
  "outside the field volume". That is the validator being parochial, not the dump being wrong; the
  ±12 X limit did hold. Treat `validate()` as a pre-flight for building into **your own** field
  only.
- ⛔ **The structural shell never reaches the VIEW layer for a visitor** — but it is
  readable anyway, out of the bake: §3.4 is what actually completes the dump.
  Settled by standing 3.4 m from a home, on its deck, facing a plainly player-built wall — dyed
  purple brick, two fitted windows, dark tile decking, a railing (screenshot). None of it is in the
  scan. Across three homes (687 / 539 / 320 items) there was not one `FurnitureTypeFlag`
  1/2/3/7/9/16/18/20/22/24: no wall, floor, roof, pillar, stairs or fence. It is not hiding under
  another component either — `BuildComponent` and `BuildEditComponent` return the same population
  (322 vs 342 furniture), and `BuildViewComponent`, `BuildCompoundComponent`,
  `BuildConnectStructComponent`, `BuildSubItemComponent`, `BuildGroupComponent`,
  `AppearanceComponent` and `BuildAnimationBaseComponent` all return **0** instances. The geometry
  is visibly rendered, so it is drawn without an entity behind it — the `BuildWorld_BRG` batch path.
  There is no staticId, netId or transform to read for it.

  What *does* arrive is everything that is a real entity, including the structure-shaped things
  that carry mechanics: `ft 36 OperateStruct` (measured: seven **Ice Rink Flooring** pieces, each
  with its own `StructSize` — 24×20, 4×16, 8×8 quarter-metres), `ft 37 OperateWindow`
  (a **Metal Industrial Bay Window**), `ft 10 Door`, `ft 23 UnOperateStruct` (puzzles, snowball
  machines), `ft 35 MechanicalClock`, plus items, plants, crops and carpets. Note those sized
  pieces are exactly the `CheckExtInfo` types from §7.2e — the same list that governs whether a
  size reaches the server governs whether it comes back.

  So: a neighbour's dump reproduces their **decoration**, not their **architecture**. To copy a
  built house you still need the owner's own client.

Minor: 9 of the 539 items reported `netId == 0`. Harmless for a design (the id is only needed for
`delete`/`pick`), but it means a remote dump is not a reliable source of addressable ids.

### 3.4 The structure lives in the bake — and the bake is readable  (**measured 2026-09-08**)

§3.3 ends with walls missing from a neighbour's dump. They are missing from the *view layer*; they
are not missing from the client. Structural pieces are drawn from a **bake**: a batched render
payload the server hands out per home, with no entity behind any piece. That is why no component
scan finds them and why the geometry is nonetheless on screen.

**The anchor is a module singleton.** `BakeManager` is a `DataModule<BakeManager>`, and
`XDTGame.Framework.Managers.GetModule(Type)` returns module singletons — the same static the mod
already uses to reach `BuildModule`. Build the `Type` **object** from the class itself
(`mono_class_get_type` → `mono_type_get_object`), never by name: a name-resolved type on an invoke
is the documented process kill (§11).

```
BakeManager._bakeDatas : List<BuildVersionData>      one entry per streamed home
  .id / .areaId                                      home netId / owner id
  .loader : BakeVersionLoader
    .position / .rotation                            the field root, world space
    ._bakeDataArray : BakeData[]                     one BakeData per layer (5 in practice)
      .combineEntityDatas       List<CombineEntityData>   WALLS
      .floorCombineEntityDatas  List<CombineEntityData>   FLOORS
      .GridFusionDatas          List<StructEntityData>    fences, grids
      .singleEntityDatas        List<SingleEntityData>    pillars, stairs, roofs, …
      .digEntityDatas           List<DigEntityData>       holes cut for doors and windows
```

`BuildVersionData.id` matches the `homeNetId` of §3.3 and `.areaId` matches its `ownerId`, so the
two reads address the same homes. The loader's `position`/`rotation` matched the root derived from
the furniture **exactly** — structure and contents share one field frame and simply merge.

**Three position encodings, all metres in that frame:**

| List | Type | Position | Rotation |
|---|---|---|---|
| `combineEntityDatas`, `floorCombineEntityDatas` | `CombineEntityData` | `LocationCollection`: a **box**, six bytes of `min`/`max` with a +100 bias on X and Z | quarter turns in the 7th byte |
| `GridFusionDatas`, `digEntityDatas` | `StructEntityData` | `StructLocationCollection`: the ordinary `ToBuildingPosValue` packing (§2) | a parallel `List<short>` of degrees |
| `singleEntityDatas`, `animSingleEntityDatas` | `SingleEntityData` | a plain `Vector3` | `Int16` degrees |

`CombineEntityData` also carries `materialId` (the dye texture's staticId) and `colorsIndex`;
`SingleEntityData` carries `ext`/`ext2`/`ext3` (the size words) and its own `Colors` list.
`Location` and `dye` are **inline struct fields** — box them first or every read lands 16 bytes off
(§3.1).

No calibration was needed: a wall decodes to `scale [4, 2.5, 0]`, and 2.5 m is the documented wall
height. Measured on a neighbour's home: **332 structural pieces** — 57 wall rows, 31 floor rows,
138 grid, 58 single, 48 dig — alongside 321 furniture rows, in one file, three storeys with floors
at Y 0.5, 3 and 5.5, and 15 distinct materials including 36923 Smooth Brick Wall.

**A baked wall or floor row is a render MERGE, not a placement — the dump undoes it.** Two things
have to be reversed, and both are measured:

1. **A run covers many unit pieces.** `Wall` 33801 is a **1 m** piece horizontally and
   `Flooring` 36301 is 1 × 1, yet runs arrive 3, 4, 6, 7 and 10 m long and 3 × 6 wide. Walk the
   box in unit steps.

   ⛔ **Do not infer the vertical step from the data.** An earlier version took the *shortest* run
   of each staticId as one piece tall. It is right whenever the house happens to contain a
   single-height run and silently wrong when it does not: measured on a house whose 33801 runs are
   **all** 5 m, the inference returned 5 m, every wall came out as one piece instead of two, and
   every storey ended up floating with a 2.5 m gap under it — 316 rows then failed with
   `placeZone is NULL` because the walls they attach to were half missing.

   One piece's height belongs to the **`FurnitureTypeFlag`**, not to the staticId and not to the
   house: **Wall (1) = 2.5 m and HalfWall (2) = 1 m** — confirmed against live entity rows in a
   real home, where 33801 sits at Y 0, 33802 at 2.5 and the deck at 3.5, the chain §7.2e records.

   ⚠️ **LowWall (24) is set to 1 m, and that is a choice rather than a measurement.** Every
   route to measuring it failed, and each failure is itself a fact: `GetScalingParams` is empty
   for it as for every wall type; with `redetect:"contact"` a second low wall above the first is
   refused — a low wall offers no `Field|Floor|Pool` zone for anything to stand on; with
   `redetect:"tile"` the second snaps into the same cell as the first and is absorbed; no home
   inspected so far contains one; and every `33803` run in a neighbour's bake is `runH 1`. 1 m is
   the **conservative** reading — it leaves `runH 1` as one piece, where 0.5 would split every
   low wall in two and invent a piece that may not exist. Prefer the gap to the phantom: a
   missing piece reads as a hole, an invented one pokes through the geometry. The bake carries no type, so `BakeReader`
   expands **horizontally only** and reports the run's full height as `runH`;
   `tools/HouseDesign/annotate_dump.py` knows the type (it has the catalogue) and does the
   vertical split. ⚠️ It must read the type from the **catalogue**, not from the row's `kind` —
   `kind` lumps every variant under `"wall"`, so trusting it types a HalfWall as a full Wall and
   hands it the wrong unit.
2. **Every piece appears TWICE, once per face**, each face carrying its own `materialId`: a wall's
   two sides, a floor's top and bottom. Measured on a real home: 282 run-cells collapsed to **141
   walls** and 534 to **267 floors**, with exactly two faces on every one of the 408 — not one
   exception. Summing runs would build the house twice.

`ExpandRuns` in `BakeReader.cs` does both, keys cells by (staticId, axis, plane, index, level),
and keeps the face pair as `materials`. `dig` rows are dropped rather than expanded: a hole is not
an object, the door that made it is already among the furniture rows, and it arrives twice anyway
(once per layer).

**The coordinate convention, and how far it is verified.**

| | Convention | Evidence |
|---|---|---|
| Frame | identical to the furniture rows | the 7 door/window entities and their `dig` rows match to the last decimal, and the bake's own `loader.position/rotation` equals the root derived from the furniture |
| Y | the piece's **base**, `packed × 0.5` | door entities sit at exactly the wall runs' bases — 0.5 m and 3.0 m, which are the storey levels |
| Along the run | the cell **centre** (`i + 0.5`) | the fence rows, which are in verified entity coordinates and are 1 m panels like walls, sit at half-integers along their line and integers across it — the same shape the expansion emits |
| Across the run | the plane's integer coordinate | as above |

✅ **All of it is now confirmed by replay** (2026-09-08). A neighbour's 1486-row dump was replayed
onto an empty blueprint at home and diffed back with `tools/HouseDesign/diff_dump.py`. On the
**ground level, where nothing depends on a support having been built first**, every structural
piece matched position-exactly:

| Level | 36301 Flooring | 33801 Wall | 33803 Low Wall |
|---|---|---|---|
| ground | 215 / 215 | 10 / 10 | 91 / 91 |
| second | 26 / 26 | 5 / 5 | 7 / 7 |
| third | 1 / 55 | — | — |

Two whole storeys, **354 / 354 position-exact**. The counts matching is the second half of the
proof: had the face pair not been deduplicated, the replay would have carried twice as many.

Over the whole house, 1108 of 1480 rows matched on staticId **and** position, and **zero replay
rows were absent from the source** — nothing landed in the wrong place. The 25 % gap is entirely
things that never got placed, not things placed wrongly.

**The multi-storey case, once the vertical split and the build order were both right** (a
619-row, four-level house replayed with auto-save left ON): 590 placed, 586 survived the save,
**585 of 586 matching the dump exactly**. Every level came through —

| | dump | replay |
|---|---|---|
| Flooring 36301 | 0:102, 5:6, 7:100, 10:4 | 0:102, 5:6, 7:100, 10:4 |
| Wall 33801 | 0:57, 2.5:57, 5:2, 7.5:2 | 0:57, 2.5:**56**, 5:2, 7.5:2 |
| HalfWall 33802 | 5:58, 6:61, 7:3, 8:4, 9:4 | 5:58, 6:61, 7:3, 8:4, 9:4 |
| Wood Tile Roof 32304 | 3.5:6, 5:1 | 3.5:6, 5:1 (with their sizes) |

The `Y 2.5` wall course is the one that does not exist at all without the type-based vertical split,
and that a suppressed auto-save deleted. Its surviving here is what closes both loops.

⚠️ **The third storey is the cautionary half.** Its position deltas were `(0, −5, 0)` ×41 and
`(0, −10, 0)` ×38 — whole storeys on the ground. That is the §7.5 hazard, not a decoding error:
with the floor it should rest on missing, the game **snaps the piece down a level instead of
rejecting it**, so one failed floor drops everything above it. Build a multi-storey design
bottom-up and verify each level landed before trusting the next.

⚠️ **Replaying someone else's home runs into ownership**, in two flavours the failure rows
distinguish: `never entered Focus` (the craft box refuses to arm the item at all) and
`would PLACE … only the bank check follows` (it arms, aims and validates, and `CheckSuccess`
refuses). Neither costs anything — `BuyAndPut` in single-player crafting is a local
`InitSingleCraftBox` with no purchase and no server call (the cost is settled at save, §7.4).
Measured: 198 of 1454 rows, most of them one themed furniture set plus the collapsed third storey.

### 3.5 A bake row's colour: material + theme index, resolved through the config  (**measured 2026-09-09**)

A copied house came out white — 243 of its 254 walls unpainted — because a bake row carries **no
colour at all**. It carries two numbers, and both had been misread:

| On the row | What it is |
|---|---|
| `materialId` | on the RUN, one per **face**. A wall is in the bake twice (§3.4), and the two entries can name different materials — that is a wall papered one way inside and another outside. |
| `colorsIndex[i]` | one byte per **location** in the run: piece *i*'s colour. |

`colorsIndex` is the one that was wrong. It reads like a list of colour slots for the batch, and
treating it that way paints every piece of a run identically. `BakeRenderingProcessorWall` indexes
it in lockstep with the locations —

```csharp
for (int i = 0; i < location.Count; i++)
    Handle(location[i], data.colorsIndex[i], …);
```

— and the counts confirm it with no room for interpretation: measured on one home, runs of **14,
24 and 8 locations carried exactly 14, 24 and 8 indices**.

#### What the two numbers resolve to

`SetColor` in the same file spells out the lookup, and `DyeFurnitureSystem.GetCurrentDyeColorData`
spells out the packing:

```csharp
ItemDyeColorByMaterial material = Config.GetMaterial(materialId);   // DyeColorConfig
ColorTheme theme = material.colorThemes[colorsIndex[i]];            // the THEME index
foreach (ColorData cd in theme.partColors)
    new DyeColorData { body = (bodyId << 4) | cd.part,              // bodyId = which SIDE
                       color = cd.color.ToHex(),                    // (r<<24)|(g<<16)|(b<<8)|a
                       texture = materialId };
```

`bodyId` is 0 or 1 and comes from `GetCurrentBody` — the sign of the player-local `z` for a wall,
`y` for a floor. It is the same two-sidedness the bake stores, and it is exactly why a live wall's
dye list reads bodies **0, 1, 16, 17**: two parts on each of two sides. `colorIndex` on the dye row
stays 0; the theme index does not survive into it.

⚠️ The colours live **only in `DyeColorConfig`**, a ScriptableObject. `TableMaterial` and
`TableMaterialColor` hold nothing but an id, so the offline table dump cannot answer this — it has
to be read from the running game.

#### Reading it without decoding a single struct

`colorThemes` is `ColorTheme[]`, a **value-type array**, and those cannot be enumerated: the
per-element `Array.GetValue` path was removed from `TryEnumerateAuraMonoCollectionItems` after it
aborted inside `icall.c`. But nothing needs to read a `ColorData` — the game will do the whole
conversion if it is handed the array:

```
SetCurrentColor(stable, materialId, theme.partColors)   // stores the array, no events
GetCurrentDyeColorData(CurrentStable, side)             // -> List<DyeColorData>, ready to apply
```

So the only raw read left is reaching `partColors` **inside** `colorThemes[i]`:
`mono_array_element_size` + `mono_array_addr_with_size`, then one `Marshal.ReadIntPtr` of a
reference field. Both ends are checked rather than trusted — the element size must be 16
(`bool` + pad + `Color32` + pointer) and the object at +8 must report class `ColorData[]` — so a
layout change refuses loudly instead of handing a wrong pointer to the runtime.

⚠️ `SetCurrentColor` writes the paint brush's current selection. It is UI state only, and
`BuildStatusPanel` rewrites it on every palette change, but a dump does leave the brush pointing at
the last material it looked up.

#### Confirmed against a live wall

`dyetheme {"material":36923,"themes":32}` walks a material's themes. Theme 8 reproduces the four
dye rows of a real painted wall in our own home **exactly**:

| body | live entity | `colorThemes[8]` |
|---|---|---|
| 0 | `1802987519` | `1802987519` |
| 1 | `-638792449` | `-638792449` |
| 16 | `1802987519` | `1802987519` |
| 17 | `-638792449` | `-638792449` |

A wrong offset could not have produced a colour that matches a live wall byte for byte, so this
confirms the layout gate as well as the lookup.

#### Material 0 is a face with no material  (**measured**)

A face whose `materialId` is 0 has a default finish and takes no colour — the renderer agrees, and
runs `SetColor` only `if (colorsIndex != null && materialId != 0)`.

The two faces have to be judged **one at a time**. Measured on one home: 80 of its 216 walls carry
material 0 on the first face and a real material on the second, so bailing out on the first face
leaves those walls white. Judging each face separately took that home from **136 of 216 walls
painted to 216 of 216**. Its 195 remaining floors have material 0 on *both* faces and stay plain,
which is what the game draws too.

#### `single` rows need none of this

`SingleEntityData.Colors` is `List<SingleEntityDataColor> { byte body; int color; }` — roofs,
pillars and stairs carry their colours outright. They become dye rows directly with `texture = 0`,
which `DyeColorClientUtil` reads as "plain colour, no material".

#### `grid` and `dig` rows carry colour INDICES, not colours  (**measured 2026-09-09**)

`StructEntityData.dye` is a `DyeCollection`, and its `colors` field is the trap in this whole
subject: it is a `List<long>` whose entries are **eight packed bytes**, byte *i* being the colour
index chosen for colour part *i*.

```csharp
num |= (long)colorIndex << (index * 8);          // DyeCollection.Add
byte b = (byte)(colorOffset >> (i * 8));         // DyeColorConfig.GetColors
Color = colorParts[i].colors[b];
```

`indexs[p]` says which word piece *p* uses; empty means every piece shares word 0, because
`Compress()` drops the list when there is only one.

Read as RGBA they give **plausible near-black values** — `0x3`, `0x606`, `0xa0a` — which is exactly
how the mislabel survived a rewrite: it produces colours, just wrong ones. The tell is the alpha
byte. A real theme colour always has alpha 255; every one of these had alpha 3–7. Checking the
alpha histogram of a dump is a one-line audit worth keeping (measured after the fix: **841 dye
entries, all alpha 255**).

Turning them back into colours needs `DyeColorConfig._itemDyeColorDic[staticId].colorParts`, where
`ColorPart { byte partIndex; Color32[] colors }` and `partIndex` **is** the dye row's `body` (the
same nibble packing — `GetColorIndexByStaticId` matches it against `colorData.body` directly). That
dictionary is private on `DyeColorConfig`, reachable only through the generic static
`M<IConfigManager>.Inst`, which is the shape §11 says not to invoke. So **fences, grids and
door/window frames are dumped with their `dyeWords` recorded and no `dye`** — they replay at their
default colour rather than a wrong one.

> `DyeColorConfig.GetColors(int materialId, byte index)` returns `colorThemes[index].partColors`
> outright, and would replace the raw read above — but it lives on the same unreachable config
> object. Worth revisiting only if a safe handle on `DyeColorConfig` ever turns up.

⚠️ **Watch for locked materials when replaying someone else's home.** `CraftBank.CheckTextureIsLock`
flags any row whose `texture != 0 && !TextureIsUnlock(texture)` — a material the account has not
unlocked. Its only caller is `BuildModule.CheckCanPutModule`, the prefab-**module** path, so it does
not gate the per-piece craft-box loop this document uses; whether that loop enforces the same rule
somewhere else is untested. If a copied house comes back with dyed pieces failing and undyed ones
succeeding, this is the first thing to check.

---

---

## 4. Blueprints — a design as a file

A blueprint IS the serialised design, and it is the only first-class "design
document" the game has.

```
BuildBluePrintBaseData
└─ Data : List<BuildBluePrintData>
   ├─ [0]  LocalPosValue : long        packed position (§2.4)
   ├─ [1]  StaticId : int
   ├─ [2]  Angle : ushort              low half of the packed angle
   ├─ [10] Angle2 : ushort             high half
   ├─ [3]  Roof : BuildBluePrintRoof?  {Ext, Curve, UpExt}
   ├─ [4]  LevelObjectId : byte        which sub-surface of the parent
   ├─ [5]  Colors : List<BuildBluePrintColor>   {Body, ColorIndex, Color, Texture}
   ├─ [6]  ChildList : List<BuildBluePrintData> objects placed ON this one
   ├─ [7]  Creatures : List<BuildBluePrintCreature>
   ├─ [8]  FurnitureState, [9] Quality, [12] OperateState, [15] UsedCount
   ├─ [11] VirtualParents : List<{Index, LevelObjectId}>
   ├─ [13] Index, [14] AttachIndex : Dictionary<int,uint>
   ├─ [17..21] UgcResourceId / UgcReferenceId / UgcOwnerGuid / UgcOwnerShortId
   ├─ [19] UniqueId : long
   └─ [22..24] UgcState / Connect / TimerTrigger
```

MessagePack, `Lz4BlockArray` compression, keyed by integer.

**Where it lives:** not on the ECS wire. `BluePrintClientService.GetBluePrintData`
fetches it over plain HTTP —
`WorldServerAddressConfig.Instance.LoadBluePrintUrl + "?guid=" + BluePrintId` —
and deserialises the `Data` field of the JSON response. The blueprint id comes
from `PlayerBuildBluePrintComponent {BluePrintId, PhotoId, PrintName}` on the
blueprint **item entity** in the bag. Build replays come from
`LoadReplayDataUrl + "?workId=…"` as `List<BuildReplayFrameData>`.

**Commands** (`XDT.Scene.Shared.Modules.Build`, all through
`WebRequestUtility.SendCommand`):

| Command | Fields | Note |
|---|---|---|
| `SaveHouseBluePrintCommand` | `ItemNetId`, `PhotoId`, `PrintName` | the **server** reads your home and uploads it — you cannot upload arbitrary content |
| `UseHouseBluePrintCommand` | `ItemNetId`, `ContentFlag`, `DyeFlag`, `Offset` | apply a blueprint to the home |
| `ViewHouseBluePrintCommand` | `OprType`, `ItemNetId`, `IsQuit` | enter/leave preview |
| `ModifyHouseBluePrintCommand` | name / photo / selected photo index | metadata only |
| `ClearHouseBluePrintCommand` | `ItemNetId` | wipe |
| `QueryHouseBluePrintCostCommand` | `BluePrintId` | → `CostCurrency`, `CostItem` |

`BluePrintCustomData` controls what gets applied: `UseBluePrintContentFlag`
(`Furniture=2`, `Flower=4`), `UseBluePrintDyeFlag` (`None/Material/Furniture/All`)
and a `BluePrintTransformData` whose `Offset` is capped at ±40 m, rotate point at
±20 m, and **`RotateDegrees` must be 0/90/180/270** (`BluePrintErrorCode.
InvalidRotateDegrees` otherwise).

> **You cannot inject a hand-authored blueprint.** Save is server-side; the client
> only names it. To realise a generated design you place its items (§7).

---

## 5. The catalogue

### 5.1 Where it comes from

| Table | Rows | Holds |
|---|---|---|
| `Putitem` | 8766 | **the placement rules** — footprint, snap cell, surfaces, alignment, rotation step, burden, limit |
| `Entity` | 18750 | `_name` (zh) and the shared item metadata; `_name@tr` in the FTS index carries en/de/fr/ja/ko/ru |
| `EntityType` | 243 | `minId`/`maxId` ranges → the entity-type id used by per-type caps |
| `BuildShopItem` | 787 | build-shop price |
| `Decoration`, `Roof`, `Wall`, `Balcony`, … | — | prefab ids per family |
| `FurnitureTabs` | 24 | the build UI's tab tree |
| `HomelandPutLimit` / `HomelandPutBlackList` | 8 / 14 | per-micro-home caps and bans |
| `FreePlaceAndRotateBlackList` | 357 (3 before 2026-09-24) | items the official Free place/rotate precision is demoted for — **UI only**, the engine never reads it (§12.3) |

Export a merged, decoded catalogue with
**`tools/HouseDesign/build_catalog.py`** → `catalog.json` + `catalog.tsv`.

### 5.2 `Putitem` columns that matter

| Column | Decodes to |
|---|---|
| `furnitureType` | `FurnitureTypeFlag` — Wall 1, HalfWall 2, Floor 3, Carpet 4, Item 5, Plant 6, Roof 7, Fence 8, Pillar 9, Door 10, Window 11, Pool 12, Stairs 16, Crop 34, … (full list in `build_catalog.py`) |
| `length` / `width` / `height` | footprint in **half-metre** units |
| `precision` | snap cell — §2.2 |
| `putableTypes` | `PutZoneFlags` mask: Field 1, Stairs 2, Floor 4, Wall 8, Desktop 16, Sand 32, Handrail 64, Longhandrail 128, Water 256, VerticalPlane 512, Pool 1024, Cropland 2048, Staircase 4096, Cone 8192, Verticalmaterial 16384, PumpkinCarving 32768 |
| `adsorptionType`, `simpleAdsorptionType` | `AlignmentType` mask: Center2Center 1, Center2Edge 2, Center2Corner 4, Edge2Center 8, Edge2Edge 16, Edge2Corner 32, Corner2Center 64, Corner2Edge 128, Corner2Corner 256 |
| `rotateAngle` | rotation step in degrees (0 ⇒ treated as 90) |
| `freeRotate` | may leave the step grid |
| `burden` | authoring Load value — the **runtime** value is `BurdenConfig` (§6) |
| `sameMesh` | the grouping key for the duplicate discount |
| `limit` | per-item cap (0 = none; `BuildCountIsNoLimit` raises it to at least 300) |
| `tabTypes`, `showTypes` | where the item appears in the build UI |

`PutZoneFlags` is the single most useful field for generation: an item with
`Field` can go on open ground, `Floor` on a floor slab, `Wall` on a wall face,
`Desktop` on a table top, `Water`/`Pool` in water.

---

## 6. The two budgets

### 6.1 Load / 承重 (`CapacityWidget`, loc 200001104)

Max **350000** = Σ `_burden` over the 18 `HomeLandAreaUnlock` plots, so it grows
as plots unlock. Live values: `BuildModule.GetCurrentCapacity()` /
`GetMaxCapacity()`.

Per-item cost comes from `BurdenConfig` — a **DynamicScriptableObject shipped in a
bundle**, not from `oversea_tables.db`:

```
BurdenId    = meshId != 0 ? meshId : staticId       # the discount group
BaseBurden  = value    * 100
ExtraBurden = holeCost * 100
```

`OverLoadData._AddId` accumulates **incrementally**, keyed by `BurdenId`:

```
first item of a BurdenId :  burden += BaseBurden + ExtraBurden
every later one          :  burden += (int)(BaseBurden * 0.05 + ExtraBurden)
```

`0.05` is `BuildManager.OverLoadSameItemPercent`.

**Measured**: placing a 13th Round Topiary Bush (staticId 33312, base 200) moved
Load 33895 → 33905 — exactly `200 * 0.05`. Deleting it returned it to 33895.

Dyed wall/floor **materials** are charged on the same counter through
`AddByMaterial` (**measured**: `MaterialIdDic` held 5 texture ids with counts
248/248/192/184/184 on a home whose 842 objects only account for ~18k of the
33 895 total). So an offline estimate from `Putitem.burden` alone is a **lower
bound** — `hdesign.estimate_burden` says so explicitly. When the number has to be
right, read `GetCurrentCapacity()`.

### 6.2 Creation Showcase / 创作展示 (loc 200004005)

The UGC budget, **300 per home**, flat at every house level
(`HouseThemeUpgrade.ugcLimit`; micro-homes 300, craft world 1200). Costs from
table `Ugcburden` keyed by `UgcBurdenEnum`: SmallDraw 3, MiddleDraw 5, BigDraw 8,
SuperBigDraw 10, Photo 10, DrawBoard 10, PrivateIsland 10. **Measured**: 4 drawing
boards → 40/300.

### 6.3 Both caps are server-authoritative

The client pre-check (`OverLoadData.CheckCanPut` / `CheckCanUgc`) is UX only.
The server answers `ErrorCode.BuildBurdenLimit = 157` /
`BuildUgcBurdenLimit = 170` on `BuildBatchOperationProEvent`, and
`BuildSaveOption.OnSavingRespond` then toasts 93755, calls
`RevertInvalidLocalOperations` and refunds. Patching the client check does not
raise the cap. Drawing-board **counts** are capped server-side too
(`DrawBoardConfig`: 30 personal / 50 per area / 50 in a homeland).

Per-entity-type caps: `EntityType._p_putLimit` and `HomelandPutLimit`
(e.g. displaybox 10). `BuildModule.GetEntityTypeCapacity(staticId, out cur, out max, out dec)`.

---

## 7. Building a design programmatically  (**measured, end to end**)

### 7.1 The pipeline the game itself uses

```
BuildModule.BuyAndPut(itemNetId, staticId, instanceId)
   → GodCraftMode.Free_DoPlacing            (async void; state Free → Focus next frame)
     → InitSingleCraftBox                   camera ray seeds the initial spot
GodCraftMode._dstPosition / _dstRotation    ← write the target here (WORLD space)
BuildModule.ConfirmPlacing(down: false)
   → GodCraftMode.Focus_Confirm
     → TryDetectCollisionAndFieldArea(_dstPosition, _dstRotation)
     → craftBox.GenConfirmCraftResult(zone, side)
     → commandSet.CheckSuccess → commandSet.Apply     local, queued
…later, on Save:
BuildSaveOption  → BuildBatchOptCommand (batched)  → server
   → BuildBatchOperationProEvent / BuildBatchOptionRespondEvent
```

`itemNetId = 0` means "take a new one" (build shop, or blueprint-edit mode where
ownership is unlimited); a real bag-item netId consumes that item.

### 7.2 The loop that works

Each step needs its own frame, because `Free_DoPlacing` is `async void` and the
craft box re-arms asynchronously after a confirm.

1. **Check state** — `snippets/build_state.cs`. Needs `InGodMode = 1` and
   `SubState` Free(1). CraftState: `Null 0, Free 1, Focus 2, EditBrush 3,
   EditGroup 4, EditDelete 5, EditPaintPick 6, EditTrack 7`.
2. **Arm** — `BuyAndPut(0, staticId, 0)`. Returns immediately; `SubState`
   becomes 2 **on the next frame**.
3. **Aim** — write `_dstPosition` (and `_rotatePosition`, to keep the rotate pivot
   aligned) and `_dstRotation`. Reuse the mod's own helpers by reflection rather
   than writing Mono fields by hand:
   `HeartopiaComplete.TryNudgeFocused(Vector3 localDelta)` and
   `TryRotateFocused(Vector3 localAxis, float deltaDeg)` — both take **field-local**
   arguments and do the world conversion for you. Set the yaw *before* the position,
   and aim at `target − rootOffset`, not at `target` (§7.2f).
4. **Re-detect the zone** — run one of the game's own detections at the target, then
   re-aim. **Skipping this is the single most common cause of a rejected placement**;
   without it the item is validated against whatever the camera ray hit at arm time.
   §7.2d has the calls and the evidence.
5. **Apply size and dye** (optional) to the held ghost — §7.2e.
6. **Confirm** — `ResetPadBuildRotateThrottle()` (zeroes `_inputTime`, beating
   `CheckInputThreshold`'s 0.3 s gate), then `ConfirmPlacing(false)`. Returns
   `true` on success.
7. **Repeat.** After a successful confirm the box **stays armed with the same
   staticId** (`isCreateNew`), so a run of identical items skips step 2. For a
   different staticId call `CancelPlacing()` first.

**Measured throughput: one confirm, then ~4 frames of rejection.** A second
`ConfirmPlacing` in the same frame returns `false` even with the input threshold
zeroed, and so does every confirm for a few frames afterwards while the craft box
re-arms. A 48-item probe run with a 3-try budget placed exactly 8 — success and
skip in strict alternation, because the skipped item absorbed the cooldown. Give
each item a **frame budget of ~30**, not a try count, and expect roughly
5 frames/item (~12 items/s) — more when a re-arm is needed (§7.2c). On a full house even that
is too fast: the last stubborn failures only disappeared at `start {"settleFrames":20}`, i.e. ~20
idle frames between items (~3 items/s, ~5 min for 842 objects). Slow is free; a failed item is not.

Ready-made: **`tools/HouseDesign/snippets/place_one.cs`** (args
`"<staticId> <x> <y> <z> <yaw>"`, local frame) — call it twice per item, once to
arm and once to place. It is a bare steps 1–3–6 and does **not** re-detect, so it
places reliably only where the camera is already pointing. Use it to poke at one
item, not to build.

> For a whole house, drive the loop from a **resident sandbox plugin**, not from
> `game_eval`: a coroutine started inside an eval snippet is revoked the moment the
> snippet returns (**measured**: only tick 0 of a 5-tick routine ran). That plugin
> is **`tools/HouseBuilderPlugin/`** — see §10. Load it with
> `tools/HouseDesign/snippets/load_builder.cs`; the mechanics are in
> [MCP_BRIDGE.md](./MCP_BRIDGE.md) § "Loading a built plugin from disk".

### 7.2a Build order is a dependency, not a preference  (**measured**)

Nothing that rests on a surface can be placed before that surface exists — the game answers
*"This item can only be placed in the following location: Ground, Flooring, Pool Surface"* and
rejects it. Replaying a real 842-item home sorted only by height and staticId lost **66
placements** in one run, because `Flooring` (36301) sorts after bushes (33312) and walls (33801)
inside the same height level.

Sort by height, then by a **dependency class** derived from `FurnitureTypeFlag`, then by staticId
(so identical items stay adjacent and the craft box re-arms as rarely as possible):

| Class | FurnitureTypeFlag |
|---|---|
| 0 — surfaces | Floor 3, TriangleFloor 22 |
| 1 — structure | Wall 1, HalfWall 2, LowWall 24, TriangleWall 18, Pillar 9, Stairs 16, HalfStairs 17, SlantBlock 20, Roof 7 |
| 2 — everything that sits on them | Item 5, Plant 6, Carpet 4, Crop 34, … |

`HouseBuilderPlugin` does this in `BuildOrderClass`. It needs the `ft` field, which the `dump`
command writes into every row.

### 7.2b An empty cell is not a buildable cell  (**measured**)

`CraftMath`'s field limits (§2.3) are the *coordinate* range, not permission. The
actual **put-zone** is far smaller and depends on which `HomeLandAreaUnlock` plots
the account owns. A 48-point probe over ground that was provably free of objects:

| Region (local) | Result |
|---|---|
| Z −8.5 … −6.5, \|X\| ≤ 10.5 | placeable |
| Z ≥ 9.5, any X | **every** placement rejected |
| \|X\| ≥ 12.5 | **every** placement rejected |

So never derive a build area from "no objects here". Either place inside the
bounding box of what already exists, or probe first — a rejected confirm is free,
it changes nothing.

`Focus_Confirm`'s rejection path is where this surfaces: no `buildZoneElement`, or
`putZoneId == 0`, or `!valid` → toast and `return false`. The toast itself does not distinguish
"outside the zone" from "blocked by another object" from "cannot afford it", but the `why` probe
does — see §7.2d.

⚠️ **Re-read this table with §7.2d in hand.** That 48-point probe was run before the camera-ray
finding, so an unknown share of its rejections were stale-zone artefacts rather than real
permission boundaries. The ±12 X limit and the Z ≥ 9.5 wall held up in later work; treat the
rest as "probe again with `redetect` on" rather than as measured fact.

### 7.2c Tileable items drop the craft box into TILING mode  (**measured**)

Flooring, fences and other tileable pieces stop accepting a plain `ConfirmPlacing` after a few
scripted placements: the box switches to a drag-tile spread (a star-shaped ghost appears at the
cursor) and every confirm returns `false` from then on. Nothing in the state you can read says so —
`SubState` is still Focus(2).

The cure is to **re-take the item**: `CancelPlacing()` then `BuyAndPut` again. So a placement loop
must not treat a run of rejections as "this spot is impossible" — after ~8 rejected frames, cancel,
re-arm once, and only give up if it still fails. Without that, a 729-item replay placed 4 items and
then failed 45 times in a row; with it, the same queue ran at a ~5 % failure rate.

### 7.2d Zone permission comes from a camera ray, not from `_dstPosition`  (**measured — the decisive finding**)

This is the single most important thing on this page. Most of what §7.2b recorded as "this cell is
not buildable" was actually this.

**The craft box does not decide where an item may go from where you put it.** It decides from an
`Alignment` object, and that object is filled in by a **detection**, not by `_dstPosition`. At arm
time `GodCraftMode` runs `RayDetection` from the **build camera through the cursor**; whatever that
ray hits sets `alignment.placeZone`, `alignment.result` and `alignment.side`. `Focus_Confirm` then
validates *that* zone.

A scripted placer writes `_dstPosition` and never runs a detection. So the zone stays whatever the
camera happened to be looking at when the item was armed — often metres away, sometimes nothing at
all — while the ghost sits perfectly on target. Worse, **`alignment.result` is sticky**: a
`NotMatchPutFlag` from one bad ray survives every subsequent move, so the item is refused forever
however correct its position becomes.

`why` on a fence that had been rejected 40 times in a row, verbatim:

```
effectiveCode: 80064  NoBuildingZone
alignment.placeZone = null
branch: "toast 310 NotMatchPutFlag — alignment.placeZone is NULL: the arm-time camera ray
         (or the last repeat detection) found no zone"
dst        (49.171, 24.293, 103.212)
alignment  (54.626, 29.215,  98.489)      ← a completely different place
```

`dst` and `alignment` disagreeing by five metres is the signature. It explains every symptom that
had been chased as something else: items landing one surface lower than intended, items rejected
outright on ground that was demonstrably free, and why deleting unrelated strays "helped" (it
changed what the camera saw).

**The cure: run one of the game's own detections at the target, immediately before the confirm.**
That is the step the UI performs for a human on every mouse move and a scripted `_dstPosition`
write skips. Three are reachable, all with the ghost held and the box running:

| Mode | Call | What it is for |
|---|---|---|
| `tile` | `GodCraftMode.OverlapDetectTile(Vector3, Quaternion)` | snap onto a neighbour of the same `tilingType` — how the UI stacks a half wall on a wall. No-op unless `craftBox.tileAxis != 0`. |
| `contact` | `Alignment.DetectContactZoneInPlace(box, Vector3, Quaternion, bool requireFootprintInArea = true, bool ignorePutFlagMatch = false)` | the zone within ~2 cm under the placing side. Cheapest; moves nothing. **5 parameters since 2026-09-24** (was 3) — AuraMono does not fill defaults, so resolve it with arity 5 and pass `true, false` for the old behaviour. |
| `ray` | `GodCraftMode.RayDetection(ray, Vector3, Quaternion, true)` | the arm-time detection, but aimed **straight down from 5 cm above the target** instead of from the camera. |

`HouseBuilderPlugin` exposes them as `detect {"mode":…}` for a single probe and as
`start {"redetect":"auto"}` for a whole run. **`auto` is the mode to use**: `tile` first when the
item is tileable, then — only while the alignment is still not Success — `contact`, then `ray`,
stopping at the first hit.

Two consequences for the loop:

- **A hit moves the ghost.** `tile` and `ray` adopt the snapped side and pose the way
  `Focus_DetectionWhenRepeat` does, so the placer must re-aim (`MoveTo` again) after a successful
  redetect — otherwise the item lands on the neighbour's grid rather than on its own target.
- **The detections swap the mode's two `Alignment` objects.** Re-fetch `get_alignment` after the
  call; the pointer read before it is now the spare.

Measured: a run of 46 fences that had placed **0** went to **45**, and the full 842-item house went
from a ~5 % failure rate to zero (§7.5). `redetect:"none"` remains the default only because it is
the pre-2026-09-08 behaviour; there is no reason to prefer it.

**Diagnosing a rejection.** `why` reconstructs the confirm without performing it — the mode, what is
held, `_dstPosition` vs the alignment's own position, the alignment's result/side, the place zone
with its flags and rect, the item's `canPut` mask, collision and out-of-bounds pre-checks — and ends
with one plain-English `verdict.branch`. It is what turned a week of guessing into one read. With
`{"gen":true}` it also generates the real `GenConfirmCraftResult` and runs the bank check, then
disposes it. The placer captures a verdict automatically for any item still rejected after 6 frames
(`whyOnFail`, on by default) and stores it in the failure row; `lastwhy` returns the most recent one.
`zones {"net":N}` is the read-only counterpart for an object that is already placed: which zone it is
linked under (what `VirtualLinkLevelObjectNetId` carries on the wire), the zones it offers, and the
build-box-centre-minus-root vector from §7.2f.

### 7.2e Sizes and dye: both have a working data path  (**measured**)

An earlier revision of this page claimed these were pointer-only features, reachable solely through
screen-space gizmo drags and a `List<(int, Color32)>` API that dropped `texture`. That was wrong.
Both apply to the held ghost, from god mode, before `ConfirmPlacing`, with no clicks — and both
survive to the wire.

**Dye.**

```
IBuildBoxElement.GenDyeColorData()  → a FRESH List<DyeColorData> (never the element's own field)
  .Clear() / .Add(&blob)            → 12-byte struct: body@0, colorIndex@1, color@4, texture@8
BuildSingle.ModifyDyeColor(list)    → writes FurnitureBaseData.dyeDolorData + UpdateData
```

The trick that makes it work is **not constructing the list**. String-constructing
`List<DyeColorData>` through `Type.GetType` is the confirmed native crash of this Mono build
(`auramono-bcl-generic-typegettype-crash`); asking the game for a list it already knows how to build
sidesteps generic construction entirely. `GenDyeColorData` returns a new instance on every call, so
clearing and refilling it damages nothing.

`List<T>.Add` for a value-type `T` takes a **pointer to the struct**, so a raw 12-byte blob is a
valid argument — no boxing, no generic instantiation. Field order is the one from §3.1.

`ModifyDyeColor` is what the game's own dye panel funnels into, and it carries the whole struct, so
**`texture` (the material's staticId) survives** — the placed object gets its material back, not
just a tint. Read back through `GenDyeColorData` and compare row counts. The cap is 10 rows
(`BuildDyeData.Colors`).

`GodCraftMode.Focus_DyeColor(List<(int, Color32)>, string)` does exist and does drop `texture`; it
is simply the wrong entry point.

**Size.** Static math throughout — no gizmo, no screen ray:

```
element.GetScalingParams()                    → current / default / min / max / step per axis (metres),
                                                editable-axis mask, padding, frustum axis, curvature
per axis:
  BuildEditMath.CalculateResizeCenterPinningPlacingAxisOnly(center, size, fullSize, axis, side)
  BuildEditMath.ApplyEditBoxResize(craftBox, fullSize, center2)   → BuildFocus.OnScalingBounds
roofs only:
  craftBox.ModifyFrustumTopSize(Vector2)   craftBox.OnModifyCurvature(float)
element.GetScalingParams()                    → verify
```

Details that matter:

- **The axis mask is `EditParames.axis`: 1 = X/length, 2 = Y/height, 4 = Z/width.** `axis == 0`
  means the item has no editable dimension at all — most furniture. Asking for a size on such an
  item errors, which is right: silently succeeding would hide a design mistake.
- **`GetScalingParams` is also how to ask the game what a piece actually IS**, not just what it
  can become — `measure {"sids":[…]}` arms each item, reads the params and drops it, placing
  nothing. Measured across every wall and roof type (2026-09-08):

  | | result |
  |---|---|
  | 33801 Wall, 33802 HalfWall, 33803 LowWall, 33804 TriangleWall, 38817 SlantBlock | `default (0,0,0)`, `axis 0` — **empty**. A wall type has no editable size and reports no dimensions, so its height cannot be looked up: it has to be measured geometrically (`tools/HouseDesign/make_reference.py`). |
  | all 46 roofs (`ft 7`) | height `default 1 m`, `min 0.5`, `max 2.5`, `step 0.5`; length/width default 1–2, max 10 (4 for 32314/32324, 5 for 32316); `axis 7`; `curvature 0.5` except 32335 = 1; `frustumAxis 1` on 32325, 32331, 32333 |

  That negative result is worth as much as the positive one: it is why a bake's wall height must
  never be inferred from the run (§3.4).
- **Apply X, then Z, then Y** — the placing axis last. The pinning math is centre-preserving per
  axis, and doing the placing axis first drags the object off its surface.
- `0` on an editable axis means *default*, which is how the game itself stores a size it never
  changed (`ApplySparseEditSize` zeroes non-editable axes). A dump therefore shows all-zero
  `StructSize` for most items, and that is not missing data.

⚠️ **Only some furniture types send the size to the server.** `CheckExtInfo` accepts
`FurnitureTypeFlag` **7, 9, 11, 31, 32, 33, 36, 37** (roof, pillar, window, …). For anything else
the ghost visibly resizes, the placement succeeds, and the wire carries `Ext = 0` — the size is
lost on save. `BuildBridge.SizeReachesServer` encodes that list; the placer warns once per type
rather than once per item. This is a game rule, not a limitation of the mod.

Measured on a real 842-item home: **62 items (7.4 %)** carry a non-default size — pillars at
3.0 m / 3.5 m, roof slabs up to 10 × 2 × 0.5 m. All of them are decorative; the structural chain
(wall 2.5 m → half wall 1 m → deck at Y 3.5) is entirely default-sized, so a rebuild never depends
on sizes even where they cannot be sent.

The placer applies both between the aim and the confirm. `start {"attributes":false}` places
everything plain; `start {"strictAttributes":true}` skips an item whose dye or size will not apply
instead of placing it plain. `dye` and `resize` also take `{"existing":true}` to write to an object
picked with `pick` (§7.3) rather than to a new ghost.

**House-wide colour scheme.** Separate from per-item dye, and still the cheapest way to repaint a
whole home: **`SaveColorSetNetworkCommand`** — six flat ints (`wallColor/wallTexture`,
`floorColor/floorTexture`, `ceilingColor/ceilingTexture`), so ordinary `IMonoApi.SendCommand` sends
it. `HouseBuilderPlugin`'s `colorscheme` wraps it and refuses to send without `confirm:true`,
because repainting a home has no undo. Measured split on a real home:

| Surface | Material | Colours |
|---|---|---|
| Wall / HalfWall | 36923 Smooth Brick Wall | `#6B7767` (slot 0), `#D9ECCC` (slot 1) |
| Floor deck | 36948 Square Tile Flooring | `#4D4F4C`, `#BFBEB9` |
| Floor (2nd set) | 36952 Concrete Flooring | `#63615F` |
| Ceiling side | 36940 Concrete Ceiling | `#6E7173` |

Everything non-structural — stairs, fences, roofs, pillars, props — carries `texture = 0`, i.e.
colour only and no material.

### 7.2f The focus is the build-box centre, the record is the root  (**measured**)

`_dstPosition` aims the craft box's **build-box centre**. What the confirm records is the object's
**root**. For anything whose pivot sits at its base the two differ by half a box height, so aiming
the focus at the design position puts the root that far low — a half wall ends up inside the wall
beneath it.

Measure the difference once per ghost (`BuildBridge.TryMeasureRootOffset`: snapshot the root the way
`Focus_Confirm` will, compare against the focus) and aim at `target − offset`. Measure **after**
setting the yaw: the offset rotates with the box.

`HouseBuilderPlugin` does this by default; `start {"rootFix":false}` restores the raw behaviour for
comparison.

### 7.2g Doors and windows need a HORIZONTAL detection ray  (**measured 2026-09-09**)

Every door and window failed the same way — `toast 310 NotMatchPutFlag`, `alignment.placeZone` is
**null**, and the toast listing the one zone kind it accepts: `Wall`. It looked like the walls were
not offering that zone. They were.

`zones {"sid":33801,"pos":[2.5,0,2]}` on a wall the script had just placed shows three offered
zones, and the third is exactly what a door wants:

```
flags 8  "Wall"   putableTag true   rectEnabled true
rectSize (1, 0, 2.5)      rectPos (54.508, 21.793, 105.56)
rectUp   (0.126, 0, 0.992)
```

**`rectUp` is horizontal.** A `Wall` zone is a vertical rect standing in the wall plane, its normal
pointing out of the face — and `redetect`'s ray mode casts *straight down* from 5 cm above the
target (§7.2d). A downward ray cannot hit a rect whose normal is horizontal, no matter how exactly
it is aimed. Neither can `tile` (an opening has no tiling axis) or `contact` (an opening is coplanar
with the wall, not 2 cm above anything). All three modes were structurally incapable of finding it.

The fix is a fourth mode, `wall`: the **same** `RayDetection`, cast horizontally along the item's
own ±Z — which is the wall normal, since the opening is coplanar with the wall it goes into —
raised 0.5 m so it crosses the wall's middle instead of grazing the floor at a door's base, and
starting 0.6 m back so it stays inside the neighbouring cell and cannot pick up the next wall along.
Both signs are tried: which face is the visible one depends on how the wall was laid.

⚠️ **Accepting `Wall` is NOT what identifies a wall-mounted item, and getting that wrong broke
every floor tile.** A Flooring reads `canPutFlags 11 = Field|Stairs|Wall` — it accepts `Wall`
because a floor is laid on a wall's **top**. Gating the new mode on bit 3 alone therefore ran the
horizontal ray for every floor: measured, the ray flew **12.099 m** across the plot into bare
terrain, came back `NoBuildingZone` with `placeZone` null, and the whole design failed from item 0
while `checks.contact` in the very same `why` dump reported `Success` with the Field zone 19 mm
below the box. The right discriminator is the one the catalogue already shows as `cells.length = 0`:
an item is in the wall plane when it accepts `Wall` **and cannot stand on the Field** —

```
door / window   canPutFlags = Wall                 -> in the wall plane
Flooring        canPutFlags = Field|Stairs|Wall    -> stands on the ground, or on a wall top
```

so the gate is `(canPut & Wall) != 0 && (canPut & Field) == 0`. No furniture-type list is needed.

⚠️ **`RayDetection` returns "the ray hit something", not "a usable zone was found."** The auto chain
must therefore require `result == Success` before it treats a redetect as done — a bare `Found`
skipped the contact detection that would have worked. Both halves of this bug were mine, and both
were invisible in the opening test, because the floors and walls there had been placed by the build
*before* the wall mode existed. A new detection mode has to be re-run against the items it is NOT
meant to touch, not only the ones it is.

With both corrected, `redetect: "auto"` tries `wall` **first**: for an opening the downward ray is
not merely useless but actively harmful, because it finds the floor and `alignment.result` is
sticky (§7.2d).

**Measured, on a rig of 32 floor tiles + an 8-cell wall run + 5 openings:**

| | before | after |
|---|---|---|
| doors and windows placed | **0 of 5** | **5 of 5**, every one position-exact |
| whole design | 40/45 | **45/45 exact, 0 off, 0 missing** |

⚠️ **One arm failure is not a verdict.** Door 30814 reported `never entered Focus (not placeable
here, or not owned)` in the batch and placed first time when re-run alone — the craft box was still
busy from the previous confirm. `measure` had already armed all five ids, which is the cheap way to
separate "this account cannot place it" from "the box was busy": it arms, reads and drops without
placing anything.

⚠️ **A held ghost shows up in a dump as a placed object.** Dumping while the run's last item was
still armed produced a phantom sixth opening at a snapped pose; `cancel` first, then dump.

### 7.2h Roofs need a pass of their own, and LowWall is 1.0 m  (**measured 2026-09-09**)

Two things a live 638-piece replay settled.

#### The roof went on before the walls at its own height

The build order was three keys deep — `BuildPhase` (shell / contents), then **height**, then
`BuildOrderClass`. Roof shared `BuildOrderClass 1` with Wall, HalfWall, LowWall, Pillar and Stairs,
which left `staticId` to break the tie — and a roof's staticId is *lower* than a wall's. Replaying
the queue offline shows exactly what that produces:

```
Y 2   : ROOF x1  ->  HalfWall x7  ->  LowWall x1
Y 3   : ROOF x1  ->  Stairs x2  ->  HalfWall x7  ->  LowWall x1
```

The roof first, every wall at its own height after it. Visible on screen, and not something a
success count would ever have caught — those roofs place *fine*, in the wrong order.

The fix is a **third pass**, not a reshuffle inside the shell:

| pass | contents |
|---|---|
| 0 | floors, walls, pillars, stairs, slant blocks, fences — every storey, bottom-up |
| 1 | **roofs** |
| 2 | doors, windows, handrails, furniture, plants |

Verified offline on the same design: 583 shell → 10 roofs → 45 contents, and **zero** walls or
floors at or below a roof's height left queued after that roof. Giving roofs their own pass also
settles the ceiling question for free — the floor of the storey above is shell, so it is always
down before any roof is attempted.

#### A bake's vertical scale is PER HOME, and must be calibrated  (**measured 2026-09-09**)

⛔ **There is no universal constant, and assuming one cost three wrong "fixes" to the wrong
variable.** `BakeReader.PackedYToMetres` was 0.5, then 0.25 — both were wrong, because the answer
is not a constant at all. Measured on three homes:

| home | metres per packed unit |
|---|---|
| 13102185 | **0.25** |
| 13644569 | **0.25** |
| 11011462 | **0.125** |

The symptom of a wrong scale is a house built at double (or quadruple) height, and it *looks* like
a wrong piece height — which is why `UNIT_HEIGHT[24]` was changed three times (1.0 → 0.25 → 0.5)
before the scale itself was doubted. **A wrong scale and a wrong unit produce the same visible
defect.**

#### Calibrating it: the bake carries its own ruler

`grid`, `dig` and `single` rows do **not** go through this scale. They come through
`ToBuildingLocalPos` — a millimetre packing of real metres — or carry a literal `Vector3`. They are
therefore an absolute reference inside the same payload, and two **hard constraints** pin the scale
down without any scoring:

1. **A `dig` row is a hole cut in a wall**, so it must lie inside some wall run's vertical span.
2. **Nothing sits above the roof**, so the top floor must reach the highest absolute-metre row.

The right scale satisfies both, and the wrong ones fail visibly:

| home | scale | digs inside a wall | top floor | highest absolute row |
|---|---|---|---|---|
| 11011462 | 0.25 | 20/26 | 9.00 | 4.50 — fences 4.5 m *below* the roof |
| 11011462 | **0.125** | **26/26** | **4.50** | **4.50** ✓ |
| 13102185 | **0.25** | **23/23** | **6.50** | **6.50** ✓ |
| 13102185 | 0.125 | 13/23 | 3.25 | 6.50 — rows *above* the roof |
| 13644569 | **0.25** | **118/118** | **5.50** | **5.50** ✓ |

⭐ **The winning scale makes the top floor equal the highest absolute row to the centimetre.** That
equality is the calibration signal, and it is what `BakeReader.CalibrateVerticalScale` keys on:
it tries ×2, ×1, ×0.5 against the reference, rejects any candidate that puts a row above the roof,
and picks the one with the most holes inside walls, breaking ties on that gap. Rows are rewritten in
place, so everything downstream sees one frame.

Verified end to end: home 11011462 re-dumped with calibration on reads floors at 0.5 / 2.5 / 4.5 —
**a 4.5 m house, matching what a player standing in front of it measured** — a one-course low-wall
plinth (`runH 0.5`), half-wall runs of 1, 2 and 4 m, and 26 of 26 door holes inside a wall.

⚠️ `tools/HouseDesign/rescale_bake.py` predates this and applies a fixed ×0.5. It is only for
rescuing a dump taken before calibration existed, of a home that has since unloaded — and only if
that home's factor really is 2. Re-dump instead whenever the home is still streamed.

#### Piece height: ASK THE GAME. `buildBox.size.y` is the answer  (**measured 2026-09-09**)

⭐ **The craft box's build box carries the piece's real dimensions, and unlike `GetScalingParams`
it is never empty.** `zones {"sid":N,"pos":[…]}` on a placed piece reports it, and `measure` reports
it for an armed one, so any staticId can be measured without placing anything:

| staticId | `buildBoxSize` | height |
|---|---|---|
| `33801` Wall | `[1, 2.5, 0]` | **2.5 m** |
| `33802` HalfWall | `[1, 1, 0]` | **1.0 m** |
| `33803` LowWall | `[1, 0.5, 0]` | **0.5 m** |

All three were independently confirmed in game with a **ladder**: build the wall, then read off
which of a stack of floor tiles at 0.25 m intervals sits flush with its top. `HalfWall` is the
control — if it does not read 1.0, the ruler is wrong and the rest means nothing.

⚠️ **Do not try to measure support by placing.** In god mode with *Bypass Overlap* active, a floor
places at **any** height: all five rungs of that ladder confirmed at exactly the height requested,
including the deliberately wrong ones. "The game accepted the placement, so it is supported" is not
evidence of anything.

⚠️ Rules this whole episode earns, all general:

* **Before offering a measurement as proof, check that the competing hypothesis predicts a
  *different* number.** "The stack 0 → 1 → 3.5 → 6 → 7 → 9.5 → 12 → 13 is continuous" is
  `0, 2S, 12S, 14S, 24S, 26S` for an unknown scale S — continuous for **every** S. "`runH` is the
  height of the RUN" — a run spanning 0 → 1 is a metre whether that is one piece of 1 m, two of
  0.5, or four of 0.25. Neither observation constrained anything, and both were reported as proof.
* **"Every placed piece matches the dump exactly" is not verification.** It was true three times
  while the house was visibly wrong: the replay was faithful to a dump that was itself wrong.
  Checking an artifact against itself only proves the copy step works.
* **Look for an independent ruler.** The answer here came from the one part of the bake that does
  not go through the constant under test. When two things are decoded by different paths, their
  agreement is real evidence; when they share a path, it is circular.

#### What a mid-run audit looks like

Pausing a run and dumping is the cheapest way to separate "placed wrong" from "not placed yet", and
on this run it mattered: the low roofs looked like they were sitting *inside* the house.

* **Nothing was misplaced.** All 298 placed pieces matched a dump row on `(staticId, position)`
  exactly — 0 off, 0 strays, roofs included.
* **The low roofs are interior in the ORIGINAL too** — each has 40–56 walls and floors above it
  within 2 m, at Y 3.5, 6, 7, 9.5, 12 and 13. They only look wrong because everything above Y 5 was
  still queued.

⚠️ **Do not diff a partial build with `diff_dump.py`.** Its matcher pairs each source row with the
nearest replay row of the same staticId, so on a half-built house it pairs placed Y-0 floors against
unplaced Y-13 ones and reports a confident "most common delta (0, −13, 0) ×51". That number is an
artifact of the pairing, not a finding. Compare on exact `(staticId, position)` keys instead until
the run is complete.

### 7.2i Anchor-bound pieces: size lives in `Connected`, and the yaw is a quarter turn out  (**measured 2026-09-09**)

Two defects a finished replay made visible, both in `grid` rows.

#### A grid row's SIZE is in `Connected.connected[i]`, not in `ext`

Every replayed pillar came out the same height, because the height was never in the dump.
`StructEntityData` has **no `ext` field** — the size of each piece lives in the `Connected`
collection, one packed word per location, in the same `SetBuildExt(length, width, height, curve)`
packing the `single` rows use. `BakeRenderingProcessorForPillar` reads it exactly so:

```csharp
StructureUtils.GetBuildEx(data.connected[i], out _, out _, out var height, out _);
float h = (height == 0) ? 1f : height * 0.25f;     // quarter metres, 0 meaning 1 m
```

Fences and handrails carry theirs the same way. `BakeReader` now reads `Connected` alongside
`Location` and `dye` and decodes it per piece. ⚠️ The fix only reaches a dump taken **after** it —
the height is genuinely absent from older ones, so those houses must be re-dumped.

#### A handrail adopts its anchor's frame, which is 90° from its own

All seven replayed handrails came out at **design yaw + 90°**, on two staircases running at
different angles. The mechanism, established rather than guessed:

* a handrail accepts **only** `Longhandrail` — its 310 toast lists nothing else, and a probe that
  tried to stand one on bare ground was refused four times out of four;
* a staircase offers **four** `Longhandrail` anchors — `zones` on one shows ids 4, 5, 7 and 8, two
  per side, each a 0.25 × 0.25 point;
* that zone is found only by the `tile` and `ray` detections, and **both adopt the anchor's pose**.
  `contact`, the one mode that does not adopt it, cannot find the zone at all — 10 of 10 refused.

So the pose cannot be kept out of the snap, and the correction belongs at the aim: ask for
`yaw − 90`. Verified — asking for the design yaw gave 0 of 7 right, asking for design − 90 gave
**9 of 9 exactly matching the original**, on both staircases.

`BuildBridge.AnchorYawOffset` keys this on the item's own `canPutFlags` — `Handrail` (64) or
`Longhandrail` (128) and nothing else — so no furniture-type list has to be maintained.

⚠️ **God mode hides support errors.** With *Bypass Overlap* active a floor confirms at **any**
height: a ladder of five tiles at 0.25 m intervals beside a wall all placed at exactly the height
asked, including the deliberately wrong ones. Never treat "the game accepted the placement" as
evidence that the piece is supported — the check that does work is reading `buildBoxSize`, or
looking.

### 7.2j Face dedup must happen AFTER the vertical split  (**measured 2026-09-09**)

A wall is in the bake once per face and `ExpandRuns` merges the pair — but its cell key carries the
**run's base**, not the piece's own height. The renderer breaks a run wherever the material changes,
and the two sides of a wall need not change material at the same height, so the faces can land in
runs of *different vertical extent*. Their keys then differ, nothing merges, and after the split the
same piece appears **twice**.

Measured on one house: **11 of 168 walls**, each pair one row carrying both materials and one lone
face 180° from it.

⚠️ **The extra face is not cosmetic — it blocks the opening that belongs there.** A wall's `Wall`
zone faces outward along its normal (§7.2g), so the duplicate presents a zone pointing the wrong
way and the horizontal detection ray finds that instead of the real one. Removing the 11 duplicates
took the same house from **5 windows and 2 doors refused to all of them placing first time**, and
from 11 walls at the wrong yaw to none.

The fix belongs in `tools/HouseDesign/annotate_dump.py`, after its vertical split, where pieces are
finally one per row: collapse rows sharing `(staticId, position)` and keep the one that already
carries both materials.

⚠️ **Do not hand a size to a type that cannot take one.** Only the `CheckExtInfo` types
(`BuildBridge.SizeReachesServer`: 7, 9, 11, 31, 32, 33, 36, 37) carry a `StructSize` to the server;
everything else reports `EditParames.axis == Null`. Trying anyway still places the item, but logs a
failure per row: on one house **46 of 50 "failures" were fences being handed a size they have no
axis for**, burying the four real ones. The loader now drops the size for those types.

### 7.2k The detection to use is chosen by the item's MOUNTING side  (**measured 2026-09-09**)

Two more classes of piece, and the same root cause behind both: `redetect` was picking a detection
by mode order rather than by how the piece attaches.

#### An opening on a cell seam needs the ray aimed sideways

A wall stands at its cell **centre** — x or z on a `.5` — while a window or a door usually sits on
the **integer** between two of them. A single horizontal ray aimed at the item then travels along
the seam between two wall colliders and hits nothing. Measured: every opening that survived a
retry sat on such a boundary — a window at z −2 between walls at −1.5 and −2.5, a puzzle at
x 4.75 between walls at 4.5 and 5.5.

`TryRedetect("wall")` therefore samples **along** the wall as well — 0, ±0.25, ±0.5 m — and
vertically too, because a fixed height offset can land on the horizontal seam where two 2.5 m
courses meet. That alone took a batch of refused windows to all placing.

⚠️ **A long ray is worse than a short one, and a hit is not a success.** The ray starts *behind* the
wall and travels toward the piece, so its reach is how much other geometry it may cross first. At
0.6 m a window that would not place reported `found = true` and brought back the wall's **top**
surface — the one a floor lands on — snapping the ghost from local 3.5 up to 5.0 and leaving
`NoBuildingZone`. Two corrections, both measured:

* sample the reach too, **shortest first** (0.15, 0.35, 0.6 m). An opening is coplanar with its
  wall, so it needs almost none, and the nearest surface is the one it is actually against.
* **keep sampling until the alignment reads `Success`**, not until the ray hits something. `Found`
  alone is the same mistake that broke every floor tile when the mode was introduced (§7.2g).

With both, the last three windows of a 964-piece house placed — two immediately, the third after
one retry.

#### A back-mounted item needs `contact`, and a ray actively breaks it

Wall art, a shelf, a wall lamp — these report `placingSide = 32` (**Back**). They mount by the face
against the wall, and `DetectContactZoneInPlace` is exactly the detection for that: it looks for the
zone under the box's *placing* side. Measured on `302581 Ocean Wall Art`, with rays disabled:

```
checks.contact: code 0 Success, side 32 Back, checkDistance 0.019
  zone: flags 8 "Wall", putableTag true, owner = the wall at (2.5, 2.5, −1)
```

It finds the right zone first time, 19 mm away. Meanwhile 60 horizontal rays — five lateral × six
vertical × two directions — all missed, because a ray is the wrong instrument for a face that is
already flush against its target.

⚠️ **And the rays do not merely fail, they poison what follows.** `alignment.result` is sticky
(§7.2d), so a forced ray that finds nothing leaves a refusal behind that `contact` then inherits —
which is why contact worked in an isolated probe and failed inside the chain. `redetect: "auto"`
now runs **contact first whenever the held item's `placingSide` is Back**, before any ray.

Result on the four that had resisted everything: three placed immediately (`Tidal Seashell Wall
Lamp`, `Ocean Wall Art`, `Playful Dolphins Puzzle`), the fourth on a retry.

⭐ The general lesson, which cost two rounds of ray tuning to learn: **read what the piece says
about itself before choosing how to detect for it.** `canPutFlags` says what zones it accepts and
`placingSide` says which of its own faces does the mounting — bottom for furniture, back for wall
decor, and an anchor point for a handrail (§7.2i). Each wants a different detection, and guessing by
trying harder with the wrong one produces exactly the kind of long tail that looks like bad luck.

### 7.3 Removing something

- **Undo** — `BuildModule.UndoAction()` → `GodCraftMode.Free_Undo()`, which
  returns `false` when the stack is empty. **Measured**: `CancelPlacing()` clears
  the undo stack, so cancel-then-undo loses the placement you meant to revert.
  Read `Free_Undo`'s return value; do not trust `UndoAction`.
  ⚠️ **The stack is the whole session's, not yours.** It already holds whatever the
  player did before your code attached, so an undo budget larger than what you
  placed silently eats their edits. Undo exactly `placed`, then verify by object
  count — and note the Load counter can drift below its starting value while the
  world catches up, so it is not a reliable "am I back where I started" signal on
  its own.
- **Delete by netId — no screen point, no raycast** (verified against the running
  2026-09-03 build on 2026-09-08, apply included: 76 objects deleted in one batch,
  every one verified). The click path `BuildModule.OnPointUp(pos)` →
  `GodCraftMode.Free_Focus_Group_Brush_Delete_OnClick(screenPos)` → `Raycast()`
  exists only to turn a screen point into an `IBuildBoxElement`; the handlers it
  dispatches to — `Free_OnClickTarget`, `Group_OnClickTarget`, `Delete_OnClickTarget`
  — all take the element. A `BuildComponent` IS that element, and
  `BuildComponent.InstanceID()` returns `entity.netId`, so the element is
  addressable without a camera: `BuildWorld_BRG._allBuilds :
  Dictionary<uint, BuildComponent>` is keyed by it, and a
  `GetComponents<BuildComponent>` scan (753 objects) matches it in a few ms.

  What `Delete_OnClickTarget(element)` does, minus its `async void` wrapper:

  ```
  BuildCommand cmd = GodCraftMode.Delete_GenCommand(element)    // private, sync: OnceDeleting(world, element)
                                                                 //   → world.NewBuildSingle: the root AND every
                                                                 //     object resting on it (relationship graph)
                                                                 //   → GenDeleteOption(): one OptionDelete each
  ErrorCode e = commandSet.CheckSuccess(cmd, buildBank, out msg) // CraftBank.CheckCostCurrency (a refund, never a cost)
  commandSet.Apply(cmd, buildBank)                               // OptionDelete.Apply → element.SetFakeDestroyed(true),
                                                                 //   root to INVALID_POS (0,-100,0); pushed on the undo stack
  ```

  `TryLockExistingTarget` is a real server lock only in joint building
  (`CraftType.Multi`); in single-player crafting it is `world.IsPreBuildingLocked
  (netId)` and nothing else. The `CraftOpEvent` fired afterwards has one listener:
  the delete sound. Apply is local — on save the option becomes
  `EBuildOperationFlag.Take` / `BuildTakeData { BuildType = 1 }` (§8). `Free_Undo`
  pops it back, so `undo` reverses a delete like a placement.

  `HouseBuilderPlugin` exposes it as `delete {"net":N}` /
  `delete {"sid":N,"pos":[x,y,z]}` with `preview`, `cascade`, `force` (§10).
  **Verification is built in and non-negotiable.** Between generation and apply the
  command is inspected: every option must be an `OptionDelete`, the requested
  object must be among its targets, and more than one target (things resting on
  it) needs `cascade:true`. After apply the reply re-reads each target's
  `FakeDestroyed`, counts live objects before/after (must drop by exactly the
  target count, total unchanged), checks the undo depth (+1) and lists every netId
  that flipped without being a target (must be empty). `verified:false` means read
  the numbers, then `undo` — nothing is retried on its own.

  **In bulk**: `deletemany {"nets":[…]}` (or `{"path":"…"}` for a file holding that
  list) runs the same verified delete **one per frame** and **stops at the first
  unverified removal** rather than ploughing on — a delete that did not do what it
  said is exactly the case where continuing turns one wrong object into fifty.
  Measured: 76 strays cleared in one call, none retried, nothing else touched. A
  netId of 0 is rejected up front, because an unsaved placement has no id to
  address.

  Preconditions: god mode, craft state Free(1) or EditDelete(5), craft box not
  holding anything, `_asyncInput` false, `parameter.craftType == Single`, the target
  not `FakeDestroyed`/`IsPlace`/pre-building-locked. A netId of 0 (placed, unsaved)
  cannot be addressed by `net` — use `sid` + `pos`.

- **Pick an existing object** — `Free_OnClickTarget(element)` from Free state:
  `world.NewBuildSingle(element)` + `InitCraftBox(…, BuildType.Edit)`; the mode's
  `CheckCondition` flips to Focus **on the next frame** with the REAL object held
  (`isCreateNew` false). From there the placer's verbs apply to it: `place`
  (MoveTo/SetYaw + `Focus_Confirm` → `OptionEditBox`, a Move on the wire), `dye` /
  `resize` with `existing:true`, `Focus_Delete()`, or `cancel` (`RevertToOrigin`).
  Plugin: `pick {"net":N}` then `held` to confirm which object landed.

- **Delete by picking a screen point** — the old path
  (`OnPointUp(screen)` → raycast → focus → `InteractExecuteDelete()`) still works
  but depends on the camera, occlusion and the UI swallowing clicks; it has hit a
  roof instead of the pillar behind it. Use the netId path.
- A deleted item's view component is **recycled, not destroyed** — it reappears in
  a scan at local `y = -100`.

### 7.4 Saving

Placements are local until saved. `BuildSaveOption` batches them into
`BuildBatchOptCommand` (≤200 operations per message, ≤256 bytes per operation) and
uploads in groups; the server replies per group with
`BuildBatchOperationProEvent.ErrorCode`. On failure the client reverts the invalid
operations and refunds currency. In the UI this is the floppy-disk button.

### 7.5 What a verified replay looks like  (**measured 2026-09-08**)

The reference run: dump a real 842-object home, open an **empty** blueprint, replay the dump,
diff the result against the dump.

```
plugin.call houseBuilder dump         {"path":"…/home_dump.json"}          842 rows, ~30 ms
   … player opens an empty blueprint, god mode on …
plugin.call houseBuilder load         {"path":"…/home_dump.json"}
plugin.call houseBuilder start        {"redetect":"auto","settleFrames":20}
   … ~5 min, poll `status`; `failures` lists any item that never went in, with its `why` verdict …
plugin.call houseBuilder dump         {"path":"…/replay_dump.json"}
```

Result: **842 / 842 placed, position-exact; 0 missing, 0 stray; Load 33895 — the same number as the
original.** 832 items matched in size and dye as well; the other 10 differ only on axes the item
does not expose (a brick pillar's X/Z, an `axis: 2` height-only piece), i.e. zero genuine
differences.

Settings that mattered, in order of how much they mattered: `redetect:"auto"` (§7.2d),
`settleFrames:20` (§7.2), the dependency sort (§7.2a), the re-arm after rejections (§7.2c), and the
root-offset fix (§7.2f). Remove any one and the run degrades from exact to approximately-right.

A narrower regression that is quicker to re-run — 30 planters plus one crop in each, on a fresh
blueprint — placed **60 / 60 exact** (planters at Y 0, wheat at Y 0.06, growth stage 0) with zero
failures. Crops in planters work; an earlier note in this file claiming otherwise was wrong, and the
cause was §7.2d, not the crops.

⚠️ **Unexplained**: on that fresh 60-object blueprint the Load counter read **0 / 350000** while
60 objects were demonstrably placed. It reads correctly on an established home. Not investigated —
do not use Load as a "did anything get placed" signal on a brand-new blueprint.

### 7.6 The game's own auto-save is what makes a long run crawl  (**measured 2026-09-08**)

A scripted build starts fast and ends slow, and the cause is not in the loop. `BuildWorld_BRG`
saves to the server **every 15 operations** (`AUTOSAVE_OPERATION_COUNT`, or after
`AUTOSAVE_OPERATION_DELAY_MS` = 60 s), and `Free_DoPlacing` returns early while one is in flight:

```
if (SuppressAutoSave || PlayerInteraction.IsBusy() || GlobalSaving.IsOperationSaving()) return;
```

Each save carries the whole build, so it gets slower as the house grows, and the placer spends its
time sitting in `NeedArm` waiting to be allowed to arm. Measured on one house: **7 placements a
second at the start, one a minute by the middle**, with no failures and the plugin ticking a
healthy ~80 times a second throughout. Throughput is also *bursty* — fast between saves, stalled
during them — which makes two-point sampling worse than useless for judging progress. Read the
index over several minutes, not seconds.

**The switch is `BuildWorld_BRG.SuppressAutoSave`, and there is a legal door to it.** It is a
static field, and writing one through raw memory is a documented way to kill this process (§11).
But `XDTLevelAndEntity.GameplaySystem.CraftingSystem.BuildReplay.BuildReplayLocalStorage` is a
public **static** class with two zero-argument methods that do nothing else:

| Call | Effect |
|---|---|
| `StartRecording()` | `IsRecording = true`, `SuppressAutoSave = true` |
| `ForceReset()` | both back to false |
| `get_IsRecording` | reads the flag back, so the change can be verified |

`IsRecording` is read by **nothing** in the build system — the replay file is written only by
`StopRecording(CommandSet)`, which is never called here. So suppressing the save is two by-name
static invokes with no pointer arithmetic and no side effect.

`HouseBuilderPlugin` exposes it as `autosave` (report), `autosave {"on":false}` (suppress),
`autosave {"on":true}` (restore), and `start {"suppressAutoSave":true}` scopes it to one run.

⛔ **Do not suppress it for anything that stacks. Measured, and it cost a whole run.**

The client checks support locally and will place an entire upper storey without complaint. The
**server** checks against what it has committed — and while the save is held back, it has nothing
underneath. On a 615-item, four-level house the run reported **615 placed, 0 failures**; then the
final batch went up and **only `Y = 0` survived**. Every wall, floor and half-wall above the ground
was thrown away: `Wall` 118 → 57 (only the Y 0 course), `Flooring` 212 → 102, `HalfWall` 130 → 0.
The 241 rows that did survive matched the dump exactly, so nothing was misplaced — it was deleted.

The periodic save is not overhead. It is what makes each course **real for the server** before the
next one is built on it. Suppress it only for a single-level design; `HouseBuilderPlugin` refuses
`suppressAutoSave` outright when the queue spans more than one height level.

⚠️ Even then, nothing is uploaded while it is suppressed, so a crash mid-run loses more than it
otherwise would. Every path that ends a run (finish, abort, `stop`, plugin unload) restores it, and
the game clears the flag itself in `OnExitBuildMode`, so it cannot get stuck on.

---

## 8. The protocol layer (reference)

Useful for reading traffic; **not** a practical write path from the mod, because
the command carries nested byte-array lists and
`IMonoApi.SendCommand` only accepts scalar fields.

```
BuildBatchOptCommand
├─ Head : BuildBatchOptHead { Version, BuildRootNetId, IsLastOperation, GamePlayType }
└─ Opts : List<SendBuildOperationBytesPro>      (max 200)
   └─ { NetId, FakeId, OperationListDatas : List<BytesSegment> }
        each segment = [byte EBuildOperationProType][NetworkWriter payload]
```

`EBuildOperationProType`: `Place 1, Take 2, Move 3, Dye 4, Attach 5, Switch 7,
SetChessDistance 8, FiniteState 9, ResourceHarvester 10, CustomRandomBox 11,
InfoBoard 12, TextDisplay 13, PressurePad 14, SelfDefinedSwitch 15,
CollideSwitch 16, Teleport 17, Connect 18, TimerTrigger 19, ConnectAdd 20,
ConnectRemove 21`.

Payloads:

```
BuildPlaceData  { byte BuildType, BuildTransformData, BuildAttributeData{PhotoIds} }
BuildMoveData   { BuildTransformData }
BuildTakeData   { byte BuildType }
BuildDyeData    { List<DyeColorData> Colors }         (max 10)
BuildAttachData { OneAttachNetId, OneAttachTargetSide, ZeroAttachNetId, ZeroAttachTargetSide }

BuildTransformData {
    ulong LevelObjectNetId,   Vector3 LocalPos,   int Angle,
    ulong Ext,                byte Curve,
    bool VirtualLinkHasChange, ulong[8] VirtualLinkLevelObjectNetId }
```

`EBuildPlaceOn`: `Home, PublicArea, SandBoxArea, MicroHomeland, VisitArea, Party,
Unknown`. `GamePlayType`: `Building, HideAndSeek, Planting`.

---

## 9. Error codes worth recognising

| Code | Enum | Meaning |
|---|---|---|
| 157 | `ErrorCode.BuildBurdenLimit` | Load cap — server |
| 170 | `ErrorCode.BuildUgcBurdenLimit` | Creation Showcase cap — server |
| 94330 | `Core.Craft.ErrorCode.UgcBurdenError` | client pre-check for the same |
| 80064 / 80065 | UI tip | put-zone invalid / `putZoneId == 0` |
| 80111 | UI tip | craft box not runnable — it is still re-arming, wait a frame |
| 310 | loc | "cannot place here" + the box's own reason; usually `NotMatchPutFlag` (§7.2d) |
| 92349 | `HasCollision` | the build box overlaps another object — `why` names them |
| 93124 | `WallOverlapFail` | another slab's build box overlaps this one |
| 92344 / 92343 | UI tip | nothing to undo / redo |
| 93755 | loc | save rejected, operations rolled back |
| 3409–3413 | `PlaceDrawBoardNetworkEvent` | drawing-board count / spacing caps |
| — | `BluePrintErrorCode` | 40 values, incl. `OverOffset 19`, `InvalidRotateDegrees 30`, `InBuildingMode 23`, `SavePrintLimitCd 20` |

---

## 10. Tooling in this repo

`tools/HouseDesign/`

| File | Does |
|---|---|
| `build_catalog.py` | `oversea_tables.db` + FTS index → `catalog.json` / `catalog.tsv` (8766 placeable ids with decoded rules) |
| `hdesign.py` | design model: load a dump or a design, quaternion↔yaw, the blueprint pos/angle packers, bounds + grid validation, burden estimate |
| `render_design.py` | design → self-contained HTML: one SVG plan per height level, warnings, bill of materials |
| `snippets/build_state.cs` | `game_eval`: mode, sub-state, both budgets, field-root transform |
| `snippets/dump_home.cs` | `game_eval`: write every placement to a JSON file |
| `snippets/place_one.cs` | `game_eval`: arm + place one item, addressed in local coordinates |
| `snippets/load_builder.cs` | `game_eval`: hot-load the builder plugin from disk |
| `make_design.py` | generate a design from parameters (footprint, walls, floors) |
| `make_probe.py` | generate a grid of probe placements to find the real put-zone boundary (§7.2b) |
| `diff_dump.py` | source dump vs replay dump → matched / moved / missing / stray, with the most common position delta (one repeated offset means one wrong convention) |
| `annotate_dump.py` | fill each bake row's `ft` from the catalogue and split merged wall runs into unit pieces — **run it on every fresh dump before building** (§3.4) |
| `make_reference.py` | generate the calibration build: ladders of each wall type 0.25 m apart (the lowest surviving rung IS the piece height) and roofs requested at several heights (settles what a roof's Y means) |

`tools/HouseBuilderPlugin/` — the resident builder. Build it against the **deployed**
`bugtopia.dll` (`dotnet build tools/HouseBuilderPlugin/HouseBuilderPlugin.csproj -c Release`; a
local build made without `-p:Mcp=true` has no `HeartopiaMod.Plugins` at all), load it with the
snippet above, then drive it over `plugin.call`.

**Reading and diagnosing** — none of these change anything:

| Call | Does |
|---|---|
| `homes` | every build field streamed in — ours and any neighbour's — with owner, item count, distance, local bounds and derived root (§3.3) |
| `bake` | which homes have a bake loaded, with their root transform (§3.4) |
| `bake {"path":…}` | the STRUCTURE alone → JSON: walls, floors, fences, singles, door/window holes, each with its `dye` resolved (§3.5) |
| `dump {"path":…}` | every placement in the current home → JSON (staticId, local pos, yaw, quaternion, `ft`, `StructSize`, dye rows, netId, plus `home`/`owner`) |
| `dump {…,"home":N \| "owner":N \| "nearest":true}` | pick the field when more than one is streamed; without it an ambiguous scene is refused, not guessed (§3.3) |
| `dump {…,"structure":false}` | furniture only. By default a dump MERGES the bake in, marking each row `src` `entity` or `bake` (§3.4) |
| `load {…,"structure":false}` | contents only. By default the structure queues too — it is unit placements after expansion; `dig` rows never queue (§3.4) |
| `state` | craft sub-state, god mode, both budgets, the field-root transform |
| `status` / `failures` | progress, and why each skipped item was skipped (with its `why` verdict) |
| `plan` | item counts, arm switches, frame estimate |
| `why` / `why {"gen":true}` | reconstruct the confirm without performing it → one plain-English verdict (§7.2d). `gen` also runs the real `GenConfirmCraftResult` + bank check, then disposes it |
| `lastwhy` | the verdict captured automatically for the last item that kept being rejected |
| `zones {"net":N}` / `zones {"sid":N,"pos":[…]}` | a placed object's zone bookkeeping: the zone it is linked under, the zones it offers with flags and rects, and its centre-minus-root vector |
| `find {"net":N}` / `find {"sid":N,"pos":[x,y,z],"tol":0.05}` | which placed object an address resolves to, plus the world counts |
| `held` | what is held: netId, staticId, position, `isCreateNew` |
| `probe` | the resolved pointers and which optional features are available |
| `measure {"sids":[…]}` / `measured` | ask the GAME each piece's real dimensions — arms, reads `GetScalingParams`, drops. Places nothing (§7.2e) |
| `dyetheme {"material":N,"theme":N,"themes":N}` | what dye rows a (material, theme) pair resolves to, both sides — the same lookup a bake dump does per piece (§3.5). Leaves the paint brush on the last material it read |

**Building**:

| Call | Does |
|---|---|
| `load {"path":…}` | parse a design (or a live dump), sort by height → dependency class → staticId (§7.2a) |
| `start {"dryRun":true}` | rehearse: aims at every item, confirms none |
| `start {"redetect":"auto","settleFrames":20}` | the settings that reproduce a house exactly (§7.5) |
| `start {…,"maxItems":N,"maxConsecutiveFailures":N}` | bound the run |
| `start {…,"attributes":false}` | place everything plain — no dye, no size |
| `start {…,"strictAttributes":true}` | skip an item whose dye/size will not apply, rather than placing it plain |
| `start {…,"rootFix":false}` / `{"liftBias":m}` | disable the centre→root correction (§7.2f) / bias the aim upward, for comparison runs |
| `start {…,"redetect":"wall"}` | force the horizontal wall-face detection alone — doors and windows only, a no-op elsewhere (§7.2g) |
| `pause` / `resume` / `stop` | control |
| `autosave` / `{"on":false}` / `{"on":true}` | report, suppress or restore the game's periodic save — the thing that makes a long run crawl (§7.6) |
| `start {…,"suppressAutoSave":true}` | suppress it for one run; every exit path restores it |
| `start {"retryFailed":true}` | a second pass over exactly what the last pass refused — most of a cascade is "too early", not "impossible" |
| `setfieldroot {"pos":[x,y,z],"yaw":deg}` | pin the field root by hand when the automatic read is not trusted |

**Editing what is already there**:

| Call | Does |
|---|---|
| `detect {"mode":"tile"\|"contact"\|"ray"}` | run one of the game's own zone detections for the held ghost (§7.2d) |
| `dye {"rows":[{"body":0,"color":…,"texture":…}]}` | recolour the held ghost; `{"existing":true}` targets a picked object (§7.2e) |
| `resize {"length":n,"height":n,"width":n}` | quarter-metre units; `{"existing":true}` as above |
| `colorscheme {…,"confirm":true}` | repaint the whole home (`SaveColorSetNetworkCommand`) — **no undo**, hence the explicit confirm |
| `pick {…target…}` | take a placed object into the craft box (Focus on the next frame) |
| `place {"pos":[x,y,z],"yaw":deg,"confirm":true}` | move/rotate the held object to a field-local pose, optionally confirming |
| `delete {…target…,"preview":true}` | generate and inspect the delete command, apply nothing — lists the cascade |
| `delete {…target…,"cascade":false,"force":false}` | delete by netId with the built-in verification (§7.3) |
| `deletemany {"nets":[…]}` / `{"path":…}` | the same verified delete, one per frame, stopping at the first unverified removal |
| `undo {"count":N}` | pop the build undo stack, one per frame — see the warning in §7.3 |
| `cancel` | drop the held item (clears the undo stack) |

It refuses to run outside god mode, stops within 500 of the Load cap, and aborts
after N consecutive rejections.

**In the game, without MCP.** The plugin adds a **House Builder** page under the mod menu's
Agent tab (Insert opens the shell): *Dump this home* writes to
`%LocalLow%/Bugtopia/HouseDumps/`, naming the file by home, owner and item count; the saved
dumps are listed newest-first as buttons that load one; *BUILD loaded dump* starts a run with
the §7.5 settings (`redetect:"auto"`, `settleFrames:20`); plus dry run, pause/resume, stop,
undo and `why`. *Delete mode* re-labels those same list buttons to delete the file instead of
loading it — armed rather than offered, because the rows look alike and a file deletion has no
undo, and it disarms itself after one deletion. A button only records WHAT to do — the work happens on the next Tick, because
a click handler runs inside the UI's own update and must not rebuild the page under itself.

Design file format (what `render_design.py` and the placer both accept):

```json
{ "name": "porch",
  "items": [
    { "sid": 33312, "pos": [-1, 0, -7], "yaw": 0 },
    { "sid": 36923, "pos": [ 0, 0, -7], "yaw": 90,
      "ft": 1,
      "size": { "length": 0, "width": 0, "height": 10,
                "topLength": 0, "topWidth": 0, "curvature": 0 },
      "dye":  [ {"body": 0, "index": 0, "color": 1802987519, "texture": 36923} ] }
  ] }
```

`sid`, `pos` and `yaw` are the only required keys. `ft` (`FurnitureTypeFlag`) drives the dependency
sort (§7.2a) and the `CheckExtInfo` warning (§7.2e). `size` is in **quarter-metres**, `0` meaning
default; `dye.color` is packed RGBA big-endian and `texture` is the material's staticId (§3.1).

A live dump is exactly this format plus `net` and the raw quaternion, so a home can be read, edited
and replayed without conversion — that is the round trip in §7.5.

---

## 11. Gotchas

- **`_dstPosition` is world, a design is local.** Convert through the field root
  every time; it differs per home.
- **netIds are per-session** and a freshly placed item has netId 0 until save.
- **A confirm succeeds at most once per ~5 frames.** A second `ConfirmPlacing` in the
  same frame returns `false` even with the input threshold zeroed, and so does every
  confirm for a few frames afterwards while the craft box re-arms. Budget *frames*
  per item, never retries — on a full house `settleFrames: 20` (~3 items/s) is what
  finally removed the last failures. "One per frame" was the earlier note here and it
  was too optimistic.
- **`CheckInputThreshold` is 0.3 s** on every craft-mode input. Zero `_inputTime`
  on the active mode (`GodControl` in god mode, `TpsControl` otherwise) before
  each call, or a scripted loop drops most of its inputs.
- **`CancelPlacing` clears the undo stack.** Undo first, cancel second.
- **Zone permission comes from a camera ray, not from `_dstPosition`** — and
  `alignment.result` is **sticky**, so one bad ray poisons every later attempt at that
  item. Always run with `redetect:"auto"`. This is §7.2d and it is the one that costs
  a week if forgotten.
- **`_dstPosition` aims the build-box centre; the design records the root** (§7.2f).
- **A resized item can still upload `Ext = 0`.** Only `FurnitureTypeFlag`
  7/9/11/31/32/33/36/37 pass `CheckExtInfo`; everything else keeps the size locally and
  loses it on save (§7.2e).
- **Never string-construct a BCL generic** to talk to the build system. Ask the game for
  the instance instead — `GenDyeColorData()` for `List<DyeColorData>` — and pass
  value-type arguments to `Add` as a raw struct pointer (§7.2e).
- **Deleted items linger at `y = -100`** in a view scan.
- **Burden estimated offline is a lower bound** — dyed materials are charged too.
- **X/Z tilt needs the Free-angle toggle**, otherwise confirm snaps it to the
  `rotateAngle` step. The official Free rotation (§12.3) is single-axis and does not
  replace it.
- **Blueprint save is server-side.** There is no path that uploads a hand-authored
  design; generated designs must be placed item by item.
- ⛔ **Never invoke a `UnityEngine.*` member through AuraMono**, and never invoke an
  instance method resolved from a class you looked up **by name** — resolve it from
  `mono_object_get_class(obj)` instead. `FindClass("UnityEngine.Transform")` +
  `get_position` on the build camera's transform killed the process outright
  (confirmed from the dump's managed stack). `IMonoApi.Invoke` is a raw
  `mono_runtime_invoke` with no guard: a wrong `(method, instance)` pair is a crash,
  not an error return. Read Unity transforms via the mod's own
  `TryReadBuildingVector3Prop`, which resolves off the object and unboxes.
- Placement is **god-mode centric** here. `TpsControl` (the walk-around build mode)
  exposes the same verb names but different states — `Focus_ConfirmPlacing`
  instead of `Focus_Confirm`, delete from Free instead of Focus.

---

## 12. The official build settings, PC hotkeys and camera  (game update **2026-09-24**)

Read from the dumps (`ilspy-dumps/` vs the 2026-09-21 build), not yet measured live. Line
numbers are the 2026-09-24 dump.

### 12.1 What actually changed

Almost none of this is new engine code. `BuildSettings.ignoreCollision` / `floatingGridSnap`,
`PlacePrecision.Free`, `RotatePrecision.Free`, the settings toggles, their PlayerPrefs keys and
FeatureOpen ids 219-222 were all in the 09-21 client, **shipped dark**: the four FeatureOpen rows
had `unlockCondition = "PlayerHomeLevel <= -10"`, which nobody meets. The 09-24 update:

1. unlocked them at **`HomeEvaluationScore > 1500`** (the only term — "D.G. Member" / 发展家 in
   the changelog is the game's word for *any player*, not a paid membership);
2. rewrote the floating grid into a real 3D lattice (`PlotSpatialLattice`, `PlotSpatialFace`,
   `PlotSpatialFaceSelector`);
3. made Ignore Overlap also skip the connectivity (joint-cutting) test;
4. grew `FreePlaceAndRotateBlackList` from 3 to 357 rows;
5. allowed Free precision only in Craft mode, and made the first-enable confirmation per account;
6. added PC hotkeys, the Advanced-mode WASD pan and Ctrl+wheel plane height;
7. added `CheckPlayerStateSafety` (co-build end / kick / error states only) in front of almost
   every God-mode action — solo building is unaffected.

### 12.2 Gating is client UI; the engine does not check it

| FeatureOpen | id | Text id (confirm on first enable) |
|---|---|---|
| `BuildIgnoreCollision` | 219 | 200006082 |
| `BuildFloatingPlace` | 220 | 200006083 |
| `BuildFreePlacePrecision` | 221 | 200006112 |
| `BuildFreeRotatePrecision` | 222 | 200006113 |

- The server computes the unlock and pushes it; `FeatureOpenSystem.IsFeatureOpen` only reads
  `_featureOpenItems[id].isLock`.
- It is consulted **only** in `BuildStatusPanel` / `SimulationBuildStatusPanel` (toggle
  visibility, precision widgets). No craft mode, no `BuildModule` path and no protocol code
  checks it.
- `BuildModule._ignoreCollision` / `_floating` load from PlayerPrefs
  `build_ignoreCollision_on_{ShortId}` / `build_floating_on_{ShortId}` at init
  (`BuildModule.cs:473-474`) and are copied into `BuildSettings` every frame by
  `CraftSettingsBackfill.ApplyBuildModeSwitches` **without a FeatureOpen check**. A value set
  while unlocked stays in force if the account later drops below the threshold — the UI just
  hides the toggle. Setters: `BuildModule.SetIgnoreCollision(bool)` / `SetFloating(bool)`
  (`:1421-1435`).
- Free precision lives in `HomelandSystem.placePrecision` / `rotatePrecision` (in memory,
  reset to Fast / Fixed90 on leaving Craft). God/TPS modes read it with `allowFree = true`.

### 12.3 What each setting does

**Ignore Overlap** (`Craft/IntersectionTesting.cs`):

- `SingleTest` returns safe for every other placed build piece (box-vs-box overlap) — this
  branch already existed.
- **New:** the connectivity test is skipped too (`:156`), so a piece can cut through the joint of
  walls, half walls, floors, fences, triangle pieces, low walls and tracks.
- **Still enforced:** overlap with non-build scene colliders (`:144-148`, `HasCollision` 92349);
  the slab-on-slab `WallOverlapFail` 93124 (`BuildSingle.OverlapCompleteWithSlab`, not gated);
  field bounds; zone / put-flag alignment; bank, burden and limit.
- The mod's overlap bypass (`IntersectionTesting.Test` + `OverlapCompleteWithSlab` detours) is
  still **stronger**: it also lifts the scene-collider and slab checks, with no score gate and no
  blacklist.

**Floating Placement** (`Craft/Alignment.cs:814-1095`, `BuildSystemBaseMode.cs:2431-2525`):

- The field is a **0.5 m lattice in X, Y and Z**. The item's normal coordinate is forced onto a
  lattice plane; the in-plane coordinates snap to its own placing cell (`Putitem.precision`, so
  the mod's `PrecisionToCellSize` override refines this too).
- The position follows the camera ray up to `godFloatingPlaceDistance = 20 m` (God) or
  `maxDis = 5 m` (TPS), or stops at the first surface. It is rejected outside the field XZ or
  above `field.height + 0.5` (capped at `LimitHeight`), lifted if below the floor, and rounded
  to mm.
- **K** toggles the spatial-axis gizmo: X/Y/Z arrows that move the item one lattice layer at a
  time (`GodCraftMode.cs:3186-3237`).
- **No support or gravity check.** On confirm the place zone is the **field root**
  (`GodCraftMode.cs:2382`), so `BuildTransformData.LevelObjectNetId` points at the field, not at
  a floor or wall — the only thing the server can see.
- Floating + Free placement = a completely free 3D position (the lattice is bypassed).

**Free Placement / Free Rotation** (`PlacePrecision { Fast, Fine, Free }`,
`RotatePrecision { Fixed, Free }`):

- Free placement: no anchor or cell snap, height stays on the zone plane, position rounded to
  **1 mm**, footprint must stay in the put area (`Alignment.cs:724-736`).
- Free rotation: the R key / button opens a −180…180° slider in whole degrees, rotating about the
  **placing-side axis** only; `EffectiveAnglePrecision` returns 1.
- On the wire: positions in mm, every rotation axis in whole degrees — the same quantisation
  as always (§2.4). The server keeps `InvalidPrecision = 9` (toast 93641); whether it is looser
  for unlocked accounts cannot be seen from the client.
- `FreePlaceAndRotateBlackList` (357 rows: entity types wall / quarter wall / floor / lighting
  plus ~353 items — string lights, neon signs, stairs, pools, platforms, pillars, bay windows)
  is applied **only by the UI** (`BuildStatusPanel.ApplyFreePlaceRotateBlackList`), which demotes
  those items to Fine / 90°. The engine never reads it.

### 12.4 PC hotkeys (`BuildStatusPanel.BindPcInput`, `:2681-2705`)

Each key presses the matching **visible, interactable** button, so it does exactly what the
button does. Active while `BuildStatusPanel` is up, not while chat is typing.

| Key | Action |
|---|---|
| Enter / Numpad Enter | confirm |
| Delete | pack to backpack, else wreck |
| R | rotate (90°, or the free-rotate slider) |
| M | move |
| K | floating spatial-axis gizmo |
| O | size, else UGC set |
| P | dye, else paint pick |
| C | connect, else mirror, else track |
| Ctrl+Z / Ctrl+Shift+Z | undo / redo |
| 1-4 | Furniture / Materials / Paint / Module tabs (Free sub-state only) |
| Tab | switch normal ↔ Advanced (God) mode |
| Esc | cancel, else close / back |

They go through `MonoInputManager` listeners, so `DisableInput` mutes them — which is what the
mod's text-input guard relies on (§12.6).

### 12.5 Advanced (God) mode camera (`BuildModule.Update`, `:2314-2360`)

- **WASD** pans: raw `Input.GetKey`, `_buildCamera.Move(-dir.normalized * delta *
  BuildConfig.camera.KeyboardMoveSpeed)`; the config ships **1500** (code default 4000).
  Character movement is disabled for the whole God session (`SetGodMoveInputDisabled`).
- **Wheel** zooms; **Ctrl+wheel** steps the build plane by 0.5 m (0…16) through the same
  `BuildModule.SetPlaneHeight` the mod uses.
- **Left drag on empty space now rotates** the camera; drag-to-pan and Alt+drag are gone.
- `camera.RotateDeceleration` 1500 → 2100.

### 12.6 How the mod lines up with it

| Mod feature | Status after 2026-09-24 |
|---|---|
| Overlap / surface / range / slab / cell detours | unchanged signatures; still stronger than the official settings |
| Free grid (sub-0.25 m cell) and free angle step | redundant for unlocked accounts on non-blacklisted items; still the only way below 1500 score or on blacklisted items |
| 3-axis tilt jog (`_dstRotation`) | no official equivalent |
| God-camera WASD pan | **removed** — the game pans itself; both together moved the camera ~1.33x |
| God-camera vertical | **E / Space up, Q down** (was Space / Ctrl — Ctrl is now the game's Ctrl+Z / Ctrl+wheel modifier) |
| Plane-height slider (0…24) | now adopts the game's current plane every poll, so Ctrl+wheel and the slider agree; the 16…24 range stays a mod extra |
| Text fields while building | `ProcessBuildingTextInputGuardOnUpdate` mutes the game's input events while **any** text field is focused and `BuildStatusPanel` is up (was: only the move panel's coordinate editor) — otherwise Delete would pack the focused item, Tab switch mode, Enter confirm |
| Pad hotkeys | **removed** — the game's Enter / Esc / R / M / Delete do the same and are rebindable in Settings → Game Keys → Build mode |
| `tools/HouseBuilderPlugin` | **not updated**: `XDTLevelAndEntity.Core.World.Entity` is now `XDTGame.GAS.Client.Entity`, and `DetectContactZoneInPlace` takes 5 arguments (§7.2d) |
