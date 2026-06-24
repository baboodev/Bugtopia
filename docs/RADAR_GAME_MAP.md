# Radar "Game" map display — final implementation

Status: **shipped**. Lets the radar render its selected resources natively on the in-game
**minimap + in-world tracking** (and optionally the **big map**), instead of (or alongside) the
screen-space ESP overlay. Plan/history: [plans/2026-06-22-radar-game-mapspots.md](plans/2026-06-22-radar-game-mapspots.md).
Implementation: `buddy/HeartopiaComplete.MapSpots.cs` (+ radar UI/config in `HeartopiaComplete.Radar.cs`,
`HeartopiaComplete.ConfigTypes.cs`, `HeartopiaComplete.cs`).

## User-facing

Radar panel → Game mode:
- **ESP Overlay / Game Map** selector (`radarDisplayMode`, 0/1).
- **Map Markers (nearest): N** slider (`radarGameTrackLimit`, 1–30) — caps how many nearest resources are tracked.
- **Show on big map** toggle (`radarBigMapSpots`, **default OFF**). Off = minimap + in-world only.

Cooldown/depleted resources are hidden. Per-resource icons match the game's native icons. Players show the
native stranger pin. Birds/Fish/Insects show their category icons.

---

## How the map system works (verified in ilspy-dumps)

The in-game map is a **data-driven registry**, not GameObjects.

- **Minimap** (`CommonMapBar` → `MiniMapSystem.GetMiniMapSpots`) renders **every tracking item directly**
  via `trackingItem.GetAtlasSpriteId()`. It does NOT render `Collectable` map-spots (only Field/Homeland
  are pulled in besides tracks). → tracking is the channel for the minimap + in-world pointers.
- **Big map** (`MapPanel` → `MapSystem.GetVisibleSpot`) renders **`MapSpotData` spots only**, never raw
  tracks. A spot's icon = `MapSpot.GetAtlasSpriteID()`: if a track matches the spot it borrows the track's
  icon (and the spot is flagged `IsTracked` → the widget shows a rectangle frame, `MapSpotWidget.tracked_go`);
  otherwise it uses the category sprite `ui_dynamic_collectable_{usageId}`.

### Tracking injection (minimap + in-world)
`TrackData`/`StartTrack`/`StopTrack` (`XDTDataAndProtocol.ProtocolService.Track`, image XDTDataAndProtocol)
are **pure value structs**. Add a local track by dispatching `XDTGame.Core.EventCenter.DispatchEvent<StartTrack>(in evt)`
(generic — inflate via `mono_metadata_get_generic_inst` + `mono_class_inflate_generic_method`; build the
struct in a raw buffer using `mono_field_get_offset` − 2*IntPtr). `TrackingSystem.AddTracking` adds it
locally (no server). Enums are **byte**: `TrackType` (`Player`=1, `Bird`=5, `Fish`=6, `Insect`=7,
`MapResource`=8, `NavigationPoint`=11, `Furniture`=14), `TrackReason.Local`=1. Each track also spawns an
on-screen HUD pointer → **limit the count** (`radarGameTrackLimit`).

### Big-map spot injection
`MapSpotProtocolManager.AddSpot(SpotEnum category, int useId, Vector3 pos, SpotReason reason, GameSceneId)`
/ `RemoveSpot(... 4 args)` (static, value args; image XDTDataAndProtocol, ns ...ProtocolService.MapSpot).
Invoke via AuraMono (`Vector3` by pointer; enums as ints). Enum ints: `SpotEnum.Collectable`=5,
`SpotReason.Auto`=0, `GameSceneId.StarTown`=1. `Collectable` spots pass LOD (`CheckCanShowInLOD` → true);
`Navigation` is hidden when `TableData.GetMapElement(usageId)` is null.

---

## Icon resolution (the hard part)

Icons live in two atlases:
- **NormalItem** (`ui_item_normal_{prefab}`, ~6205 sprites incl. `p_gather_*` mushrooms, `p_material_*`,
  `p_fruit_*`). Used by `TrackType.Furniture` → `RewardUtility.GetIconName(StaticId)`.
- **Collectable** packed `SpriteAtlas` "collectable_13" (**32 sprites**, keyed by ITEM id). Used by
  `TrackType.MapResource` AND big-map `Collectable` spots → `ui_dynamic_collectable_{id}`. The 32 ids:
  - **40xxx** materials/fruits: 40001 Branch, 40002 Timber, 40003 Quality Timber, 40004 Rare Timber,
    40006 Roaming Oak Timber, 40021 Stone, 40022 Ore, 40026 Flawless Fluorite, 40033 Bamboo, 40101 Apple,
    40201 Mandarin, 40301 Coconut, 40501 Blueberry, 40502 Raspberry.
  - **48xxx** mushrooms: 48001 Oyster Mushroom, 48002 Shiitake, 48003 Button Mushroom, 48004 Penny Bun,
    48005 Black Truffle, 48006 Matsutake.
  - **49xxx** "Bizarre" mushroom variants.
  (The `.ab` file list only shows 4 string-named fruit sprites; the numeric ones are PACKED inside the
  atlas — enumerate at runtime via `Resources.FindObjectsOfTypeAll<SpriteAtlas>()` + `SpriteAtlas.GetSprites`,
  names suffixed `(Clone)`. The atlas only loads once a collectable icon is actually rendered.)

### Resolving a resource → icon id
Per matched resource (matched to a live `CollectableObjectComponent` within 3 m, XZ):
1. **produce drop-item id** — `CollectableObjectComponent.itemTypeID` (produceId) →
   `TableData.GetMapResourceProduce(produceId)` (sig `(int,bool needException=false)` — **2 params**) →
   `hitProduce` is `string[][]` of **dropGroup KEYS** (e.g. "TREE2031", "BUSH101"), NOT item ids. Resolve a
   key → item via `TableData.get_TableRandomDropsAndLowerUpperLimitsByDropGroup()` →
   `Dictionary<string,(int,int,List<TableRandomDrop>,List<int>)>`; `get_Item(key)` → boxed ValueTuple →
   unbox → Item3 (`List<TableRandomDrop>`) at **offset +8** → per `TableRandomDrop` read `content`
   (`TableRewardItem[]`), **guard length>0**, `content[0]` → `rewardType`/`rewardParam`.
   **Do NOT call `RewardUtility.GetDropGroup`** — it does an unconditional `content[0]` and throws
   `IndexOutOfRange` on rows with empty content (e.g. TREE2032), silently dropping the rare group.
   Iterate **all** `hitProduce[i][j]` keys; a rare tree lists Timber + Quality + Rare Timber across slots.
   Pick the **highest-tier** = highest id within the primary item's 10000-family (rarity via `GetQuality`
   returns 1 for all tiers, so it can't rank — id does). Ignore out-of-family ids (e.g. bonus boxes 70xxx).
2. **atlas-name match** (mushrooms: produceId=0, entity id 130005 has no collectable sprite). The radar
   LABEL is the item name ("Shiitake"); match it to the collectable atlas name→id map (built from the
   runtime atlas dump via `TableData.GetEntity(id).name`, the localized `TableEntity.name` property — use the
   unambiguous 2-param `GetEntity`, NOT `RewardUtility.GetName` which has two 3-param overloads incl. a Guid one).
3. **entity static id fallback** — `EntityUtil.GetEntityResId(entity)` (managed EntityUtil absent → AuraMono;
   enumerate both 1-arg overloads, invoke with the entity object). Works only for mushroom-type gathers via
   NormalItem; trees/stones expose an object prefab (`p_tree_*` absent → blank).

### Track type chosen from the resolution
- (1) or (2) succeeded → **`MapResource`** track (icon in collectable atlas → shows on minimap **and** drives
  a big-map spot).
- (3) only → **`Furniture`** track (NormalItem icon, **minimap only** — no collectable sprite for the big map).
- Birds/Fish/Insect → `Bird`/`Fish`/`Insect` (theme_107/104/108). Players/morphs → `Player` (native pin).

Resolved icon id is cached per radar label (`mapTrackLabelIcon` + `mapTrackLabelProduce`), so distant /
streamed-out markers reuse it (the game only instantiates a `CollectableObjectComponent` near the player —
distant markers can't resolve a produce id on their own).

---

## Big-map specifics & the frame trade-off

The big map needs a `MapSpotData` spot per marker. To show many markers per-position with correct icons,
each spot needs a UNIQUE `usageId` (spots merge if they share `usageId`+category+reason) AND a matching track
to supply the icon. A matching track makes the spot `IsTracked` → the game draws a **rectangle frame**.
Therefore (pick two of three): many / real-icons / no-frame.
- **Per-position + icon** (what the toggle enables): unique `usageId` = low 32 bits of the marker token; the
  `MapResource` track carries `TargetNetId = usageId` so the spot matches it (`IsSameTrackPoint`:
  `usageId == TargetNetId`) and borrows `ui_dynamic_collectable_{itemId}`. → frame on each.
- **One-per-type, no frame**: bare spot with `usageId = itemId` (its own sprite, no track) — but spots merge.
- Frameless + per-position ⇒ blank icons (unique id has no sprite).

So **"Show on big map" is OFF by default** (no frames). Item-icon track types (`Furniture` etc.) are absent
from `IsSameType`, so they can NEVER drive a big-map spot — only `MapResource` can. Mushrooms therefore only
reach the big map via the atlas-name path (→ `MapResource`/48xxx).

---

## Cooldown filtering (two layers)
1. `RadarMarkerMetadata.IsCooldown` — but the radar's own cooldown tracking is LOCAL/heuristic
   (`rareTreeCooldowns_res` etc.) and misses resources depleted by others / before login.
2. Authoritative: `CollectableObjectComponent.inCold` (bool property `get_inCold`, read via `TryGetMonoBoolMember`)
   into `MapResEntity.OnCooldown`; a matched-but-cooled marker is skipped (no marker, StopTrack-removed).
   Distant/streamed-out resources have no entity → fall back to layer 1.

---

## Key crash-safety notes
- All AuraMono invokes: `EnsureAuraMonoApiReady` / `AttachAuraMonoThread`, exc check, no MonoObject* held
  across yields/frames (this all runs synchronously inside the sync pass).
- `mono_runtime_invoke` arg convention: value types by pointer (`args[i] = &value`; Vector3/int/enum/bool),
  reference types as the object pointer directly (`args[i] = strObj`). Struct **out** params are unsafe
  (stack corruption) — only reference-type out/returns; boxed value **returns** are fine (unbox).
- ValueTuple after unbox: fields at sequential offsets from the unboxed data pointer.
