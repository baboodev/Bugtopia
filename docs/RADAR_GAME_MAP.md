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
- **Player Avatars (all)** toggle (`radarPlayerAvatarsAll`, **default OFF**) — real avatar photos on map
  player markers for every player, not only friends (installs two opt-in NativeDetours; see below).

Cooldown/depleted resources are hidden. Per-resource icons match the game's native icons. Players show the
native player pin — or their real avatar photo (friends always, everyone with the toggle). Birds/Fish/Insects
show their category icons.

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
   **VALIDATE the id against the atlas** (`mapAtlasIdSet`, built from the runtime dump): the produce path can
   resolve a real drop-item id that has NO collectable sprite — meteors (produce 601/602/603 → Starfall Shard
   40034/40035/40036) are not in the 32-sprite atlas, and a `MapResource` track with such an id renders
   BLANK. Empty set (atlas not yet enumerated) = optimistic; self-corrects once the atlas loads because a
   type change re-dispatches the track.
2. **atlas-name match** (mushrooms: produceId=0, entity id 130005 has no collectable sprite). The radar
   LABEL is the item name ("Shiitake"); match it to the collectable atlas name→id map (built from the
   runtime atlas dump via `TableData.GetEntity(id).name`, the localized `TableEntity.name` property — use the
   unambiguous 2-param `GetEntity`, NOT `RewardUtility.GetName` which has two 3-param overloads incl. a Guid one).
3. **produce drop-item id NOT in the atlas** (meteor → Starfall Shard) — `Furniture` track with that item id:
   the item's NormalItem icon (`ui_item_normal_*`) exists for inventory items even when the collectable sprite
   doesn't. Minimap only (`via=produceItem`).
4. **entity static id fallback** — `EntityUtil.GetEntityResId(entity)` (managed EntityUtil absent → AuraMono;
   enumerate both 1-arg overloads, invoke with the entity object). Works only for mushroom-type gathers via
   NormalItem; trees/stones expose an object prefab (`p_tree_*` absent → blank).

### Track type chosen from the resolution
- (1) or (2) succeeded → **`MapResource`** track (icon in collectable atlas → shows on minimap **and** drives
  a big-map spot).
- (3) or (4) → **`Furniture`** track (NormalItem icon, **minimap only** — no collectable sprite for the big map).
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

## Player avatars on map markers

**Native mechanism (friends).** The map widgets show the real avatar photo when the spot's target is a
friend: `MiniMapSpotWidget.SetData` → `spot.IsFriend` → `HeadIconWidget` ("HeadIconWidget_Map") →
`SetIcon(url)` → `photo_widget.SetTexture(url, HeadIconDefinition)`, with
`url = FriendSystem.GetUserProfile(usageId).AvatarImageUrl`. Same gate on the big map:
`MapSpotWidget` → `MapSpot.IsFriend`. For a Player TRACK the widget's `usageId` = `TrackData.TargetNetId`
(`MiniMapSystem.GetMiniMapSpots`). The profile cache (`FriendSystem._userDataCache`) is warmed by VANILLA
for every player with a server Player spot (`MapSpotsSystem.CreateMapSpotData` →
`FriendSystem.UpdateUserCacheInMap(netId)`) — it is NOT friend-gated; only the `IsFriend` check is.

**Avatars for EVERYONE on the maps (`radarPlayerAvatarsAll`, opt-in).** Two callback-free NativeDetours on
the friend-gate getters, Apply/Undo following the toggle (Building-hook pattern: no trampoline, no managed
callbacks, allocation-free bodies):
- `MiniMapSpot.get_IsFriend` — STRUCT getter: `this` = raw struct data pointer → header-subtracted field
  offsets (`trackType` byte, `usageId` uint). Body: `trackType==Player && usageId!=0 && usageId!=self`.
- `MapSpot.get_IsFriend` — CLASS getter: `this` = object pointer → header-inclusive offsets (`category`
  int == SpotEnum.Player=3, `usageId` int). Same body semantics.
Self netId cached (`TryResolveSelfPlayerNetIdMono`, refreshed ~2 s). Non-player spots return false exactly
like vanilla. These getters are read ONLY by the map widgets (mini/big map) — clicking a player still opens
`PersonalInformationPanel` via the real `TryGetFriendByNetId`, so **dialogs treat strangers correctly** (no
friend-only leakage). Undone on toggle-off and in `Cleanup()`.

**IN-WORLD avatar pointer — SCOPED force-friend (re-enabled).** The world pointer (`MapTrackWidget.SetData`)
draws the avatar head icon only when `FriendProtocolManager.TryGetFriendByNetId(cell.NetID)` is true (else a
plain icon). That check routes through `IFriendService` → the concrete `FriendClientService.TryGetFriendByNetId`,
which is also used by ~30 dialog/panel call sites — so the first attempt (global force-friend) made strangers
look like friends everywhere and broke `PersonalInformationPanel`. **Fix: confine the override to the render
call.** A trampoline detour on `MapTrackWidget.SetData` (`EnsureTrackWidgetPatch`) sets `mapTrackWidgetRendering`
for the duration of that one call; `FriendGateNative` (the `TryGetFriendByNetId` detour, `EnsureFriendGatePatch`)
force-returns TRUE **only while that flag is set** AND the netId ∈ `mapAvatarWorldNetIds` (our injected world
players, self excluded). Every other caller runs with the flag clear → real result → no dialog leak. **The
friend check is read in TWO places at different times**, so both are bracketed (`EnsureTrackWidgetPatch` detours
both): `MapTrackCellModel.SetData(TrackingItem)` → `TrackingItem.GetAtlasSpriteId()` sets
`iconId.SpriteName = GetUserProfile(netId).AvatarImageUrl` (the avatar URL, loaded as a texture by
`HeadIconWidget.SetIcon(url)`), and `MapTrackWidget.SetData(MapPositionTrackBarModel)` picks the head-icon
branch. Bracketing only the widget (not the cell) gave a **blank white icon** — the URL was never stored. World
"Player" tracks are injected only while the toggle is on (`radarPlayerAvatarsAll`); Morphs stay tracked
regardless (hide-and-seek). Same reentrancy pattern as `mapNameReadingSelf`; save/restore flag for nesting.
Undone on toggle-off + `Cleanup()`.

**Big-map "tracked square" on players — suppressed.** Our Player track incidentally matches the vanilla
Player spot (`IsSameTrackPoint`: usageId == TargetNetId) → `MapSpot.IsTracked` = true → `MapSpotWidget`
activates `tracked_go` (a square frame) that a normal friend spot doesn't get. A trampoline detour on
`MapSpot.get_IsTracked` (`MapSpotIsTrackedNative`) keeps the real value except: (a) **Collectable** category
→ always false (vanilla never makes Collectable spots, so all are our big-map resource markers; the icon
still resolves via `GetAtlasSpriteID`/`GetTrackData`, which is independent of `IsTracked`); (b) **Player**
category with usageId ∈ `mapAvatarWorldNetIds` → false. Both lose the square and regain normal untracked
sort/LOD. This detour is managed independently of the avatar toggle — installed when `radarBigMapSpots ||
radarPlayerAvatarsAll` (via `EnsureIsTrackedPatch`, offsets from `TryEnsureMapSpotOffsets`), so resource
frames go away with just "Show on big map" on. The `bg_img` friend background stays (normal styling that real
friends get too).

**Gotchas found:** `FriendClientService`'s C# namespace is `ClientSystem.Social.Friend` (the `EcsSystem`
prefix is only the ilspy folder = image name); and the managed `TryGetSelfPlayerNetId` returns 0 on this
build → use `TryResolveSelfPlayerNetIdMono` (AuraMono `PlayerDataCenter.GetSelfNetPlayerId`). Also:
`XDTGameUI`-image classes (`MapTrackWidget`, `MapTrackCellModel`) don't resolve through
`FindAuraMonoClassByFullName` (its across-assemblies search silently misses that image) — use the
`FindAuraMonoClassInAllLoadedImages(className, nameSpace)` fallback (args reversed) and always log which class
failed, or you get a stuck no-op with no install/error line (that was the "blank/placeholder avatar" bug).

## Real player names for non-friends (`radarPlayerAvatarsAll`)

The game shows strangers a **Title**, not their name. There is **no single lever** — different surfaces read the
name from different functions:
- **map spot label / chat** → `PlayerServiceSystem.GetPlayerName(shortId, title)`.
- **over-head nameplate** → `EntityTrackBarModel.TryGetName` (XDTGUI.Module.Track.Bars), which for a player does
  `GetUserProfile(shortId).Title.TitleString` (acquaintance → Title) or `return false` (non-acquaintance →
  **nothing**). It does NOT call GetPlayerName.
- **profile card** (`PersonalInformationPanel`) → renders `EditDesignation(playerProfile.Title)` for strangers.

The real name is `PlayerProfile.Name`, cached for all via `FriendSystem.GetUserProfile`. Implemented SAFELY (no
force-friend). TWO detours cover the surfaces:
- **Read name + shortId** in the throttled RemotePlayerComponent scan:
  `TryGetAuraMonoDataModuleInstance(FriendSystem)` → `get_Instance`; invoke `GetUserProfile` (two 1-arg
  overloads uint/long — probe both, keep whichever returns a populated profile); boxed struct → unbox → read
  `Name` and `Id` at raw offsets (`TryGetTrackFieldRawOffset`). `Id` is an ENCODED shortId string → decode to
  the raw shortId via `ShortIdUtil.DecodeShortId(string)->long` (STATIC, no out-param → the safe netId↔shortId
  bridge; avoids the `TryGetPlayerShortId(out long)` value-type-out crash risk). **Pin the name string BEFORE
  `TryReadMonoString`** (it allocates → a GC could move the unpinned name → stale pointer → crash). Cache
  `shortId → pinned MonoString`, swap in one assignment per scan, free previous pins.
- **map spot / chat** — trampoline detour on `PlayerServiceSystem.GetPlayerName(long, long)`
  (`GetPlayerNameNative`): shortId cached → return the real name, else original. Allocation-free.
- **over-head nameplate + card** — detour BOTH 1-arg `FriendSystem.GetUserProfile` overloads
  (`EnsureGetProfilePatch` / `MirrorProfileNameIntoTitle`). These return `PlayerProfile` **by value (sret)**; the
  hook calls the trampoline to fill the buffer, then mirrors `Name` → `Title._titleString` inside that returned
  copy so `Title.TitleString` yields the real name (the getter returns the `_titleString` backing field, rebuilt
  only on language change). Pure copy-mutation → the FriendSystem cache is untouched. Unconditional (Name
  non-empty) so no overload-arg disambiguation is needed. **sret ABI** (mono INSTANCE method, confirmed via
  crash dump): `this` comes first, THEN the return buffer → `RCX=this, RDX=sret, R8=arg` — mirror into the
  **2nd** pointer param (a *static* sret method like CraftMath has sret first because there's no `this`).
  Original returns the sret ptr in `RAX` → the hook delegate returns `IntPtr` (the trampoline result) to
  preserve RAX. A reentrancy flag (`mapNameReadingSelf`) suppresses the mirror while our own scan is inside
  `GetUserProfile` (the cache-read invoke re-enters the detour). Offsets `Name@8`, `Title@24`,
  `PlayerTitle._titleString@16` → `Title.str@40`; MonoString `length@16` must be >0 (else you'd blank the title
  = the empty-nameplate bug). Allocation-free (Marshal ops).
- **nameplate acquaintance gate** — `TryGetName` only returns a name for friends/acquaintances/hide-seek; a plain
  stranger hits `return false` (empty) until you open their card (which registers them as an acquaintance). So
  detour `FriendSystem.IsAcquaintance(long shortId)` (`EnsureIsAcquaintancePatch` / `IsAcquaintanceNative`) → `1`
  for players in the name cache, else original. `IsAcquaintance`'s ONLY external caller is this nameplate (no
  social/action gating — safe, unlike the reverted force-friend). Now the name shows without opening the card.
- Overrides friends too (real name instead of nickname — low risk, informative; friends' over-head still uses
  NickName); self excluded (RemotePlayerComponent is remote-only). Managed by the `radarPlayerAvatarsAll` toggle
  (`mapNameActive` gates all hooks); GetUserProfile + IsAcquaintance detours undone in `Cleanup`.
Diag: `[MapSpots] name read: … Title.str@40 …` and `name patch: GetUserProfile Title-mirror detour installed on
N overload(s)`.

---

## Key crash-safety notes
- All AuraMono invokes: `EnsureAuraMonoApiReady` / `AttachAuraMonoThread`, exc check, no MonoObject* held
  across yields/frames (this all runs synchronously inside the sync pass).
- `mono_runtime_invoke` arg convention: value types by pointer (`args[i] = &value`; Vector3/int/enum/bool),
  reference types as the object pointer directly (`args[i] = strObj`). Struct **out** params are unsafe
  (stack corruption) — only reference-type out/returns; boxed value **returns** are fine (unbox).
- ValueTuple after unbox: fields at sequential offsets from the unboxed data pointer.
