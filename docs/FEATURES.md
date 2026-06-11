# Features Reference

Complete feature catalog for **Heartopia Helper**. Works identically under MelonLoader and BepInEx (same `HeartopiaComplete` core). Menu toggled with **Insert** by default. Labels support en, es, zh-CN, pt-BR.

---

## UI Structure

### Main tabs

| Index | Tab | Purpose |
|-------|-----|---------|
| 0 | **Self** | Player movement, camera, AFK, building overlap bypass |
| 2 | **Resource Gathering** | Foraging, chop/mine, fishing, insects, birds |
| 3 | **Features** | Automation utilities (food, repair, shops, cooking, puzzle, pets) |
| 8 | **New Features** | Animal care, daily quests, homeland farm (crop-box automation) |
| 4 | **Radar** | World resource radar + visual ESP |
| 5 | **Teleport** | Fast travel, NPCs, events, custom points |
| 6 | **Bag / Warehouse** | Backpack ↔ warehouse transfer via `BackPackSystem` / `MoveBatchBackpackItems` |
| 7 | **Settings** | Keybinds, theme, language, notifications, overlays |

Tab index **1** is unused in the main tab bar (historical gap).

### Sub-tabs

**Self**

| Sub-tab | Content |
|---------|---------|
| Main | Camera toggle, noclip, anti-AFK |
| Building | Bypass overlap (placement collision bypass) |

**Resource Gathering**

| Sub-tab | Content |
|---------|---------|
| Foraging | Teleport farm + aura farm |
| Chop & Mine | Tree/stone patrol automation |
| Fishing | `AutoFishingFarm` (active fishing system) |
| Insects | `InsectNetFarm` |
| Birds | `BirdNetFarm` |

**Features**

| Sub-tab | Content |
|---------|---------|
| Main | Quick toggles, game speed, hide UI/player, bird vacuum, FOV, login helpers |
| Food & Repair | Auto eat, auto repair, bag automation |
| Snow Sculpting | Auto QTE, interact-icon start, move snowballs (id 5100) warehouse → bag |
| Auto Buy | Cooking store purchase automation |
| Auto Sell | Inventory sell automation |
| Mass Cook | Network cooking at patrol points |
| Puzzle | Auto puzzle solver |
| Pet Care | Feed all pets, auto cat play, auto dog train |

**New Features**

| Sub-tab | Content |
|---------|---------|
| Animal Care | Wild animal trough feed (manual), claim all wild animal gifts |
| Daily Quests | Auto-submit item delivery orders (CanSubmit) |
| Homeland Farm | Crop-box farming: auto farm, water/weed/harvest/sow/fertilize in radius, seed/fertilizer selection |
| Pictures | Decrypt / re-encrypt `ScreenCapture` cache (Photo, Draw, …). Draw files get a color preview via game `ColorLut`; index maps kept in `Draw/.index/` |

Inventory scan / sort / filter rules for these (and Auto Sell, Bag transfer, pets): **[BACKPACK_AND_ITEMS.md](./BACKPACK_AND_ITEMS.md)**.

**Teleport**

| Sub-tab | Content |
|---------|---------|
| Home | Return home / town entry |
| Animal Care | Animal care locations |
| NPCs | NPC teleport list (cached at runtime) |
| Locations | Fast travel points |
| Events | Event area teleports |
| House | House-related teleports |
| Custom | User-defined teleport list (saved) |
| (extra) | Additional teleport utilities |

---

## Global Controls

### Menu

- **Toggle menu:** Insert (default), rebindable in Settings.
- **Disable all:** Optional hotkey stops active farms and automation.
- **Status overlay:** Optional HUD showing active features and farm states.
- **Notifications:** Toast-style messages inside the mod UI (position configurable).

### Game speed

Hotkeys (all rebindable, default unbound):

- 1×, 2×, 5×, 10× game speed presets
- Slider in Features → Main; affects `Time.timeScale` clamped 1–10.

### Tool equip hotkeys

Quick-equip (when bound):

- Axe, insect net, fishing rod

---

## Self Tab

### Camera Toggle (Mouse Look)

- Orbits camera around player with mouse capture.
- Optional crosshair.
- Restores default camera snapshot when disabled.
- Uses direct camera transform updates in `OnLateUpdate`.

### Noclip

- Enables `OverridePlayerPosition` via Harmony-patched `CharacterController.Move`.
- Movement: WASD, Space/Ctrl vertical, Shift = speed boost multiplier.
- Speed and boost configurable; persisted in config.
- Blocks normal character controller motion while active.

### Anti-AFK

- Periodically simulates mouse input to reduce idle kick.
- Configurable interval (5–120 seconds).

### Building — Bypass Overlap

- Client-side building placement overlap bypass.
- Applies additional Harmony patch on demand (`EnsureBypassPatched`).
- Credits third-party contributor in UI.

---

## Resource Gathering

### Foraging (Teleport Farm)

Classic **radar-driven teleport farm**:

1. Requires **Radar enabled** with at least one loot category selected.
2. Teleports player to radar markers for mushrooms, berries, stones, etc.
3. Clicks interact / collects resources (including meteors via F-key auto-interact when **Aura Farm is off** and meteor radar category is active).
4. Configurable:
   - Area load delay after teleport
   - Resource click duration
   - Teleport cooldown between targets
   - Auto-repair pause during farm
   - Patrol point list (saved JSON)
   - Loot priority weights (fiddlehead, mustard, burdock, etc.)

Status strings: `IDLE`, `TELEPORTING...`, `GATHERING...`, etc.

### Aura Farm

Server-command style farming **without teleporting** to each node:

- Resolves game types at runtime via reflection (`ResourceProtocolManager`, `InteractSystem`, `EntityHelper`, `Entities`, etc.). Details: [TYPE_RESOLUTION.md](./TYPE_RESOLUTION.md).
- Primary target discovery: **AxeChecker** (`HandholdCylinderChecker.PhysicalSelect`) → level-object shapes with `ownerNetId`.
- Sends protocol commands in range:
  - **Bushes** — `SendPickBushCommand`
  - **Trees** — `SendAttackTreeCommand`
  - **Stones** — `SendHitStoneCommand`
- Throttled scan (80 ms tick, 20 ms per-owner cooldown); merged target cap (32).
- Toggle independent of teleport foraging; both can conflict — UI warns when radar/foraging preconditions fail.

#### Meteorites (starfall rocks)

When live meteor props (`p_rock_meteorite*`) are near the player, Aura Farm treats matching AxeChecker targets as meteorites:

1. Scans scene for meteor GameObject positions (~1 s interval, 3 m match radius).
2. **Auto-equips axe** (hand tool id **1**) before mining.
3. Resolves **logic parent** `netId` from the view `ownerNetId` (`CollectableMeteoriteViewComponent` → `CollectableMeteoriteLogic` / `MeteoriteLogic`). `HitStone` must target the **parent**, not the view entity.
4. Sends `SendHitStoneCommand(parentNetId)` — same server path as `PlayerAxeAttackStoneAction`, not bush pick or F-key interact.
5. Refreshes target list and invalidates stale caches when moving between meteors (no toggle restart needed).

**Interaction with teleport foraging:** While Aura Farm is enabled, **meteor auto-interact** (F + UI click during START FORAGING) is disabled — meteors are handled only via Aura Farm API. Enabling Aura Farm also stops any in-progress meteor interact sequence.

**Requirements:** Player within axe range of the meteor; `ResourceProtocolManager.SendHitStoneCommand` (managed or Mono) must resolve. Debug: set `MasterLogAuraFarm = true` in `HeartopiaComplete.cs` and rebuild.

### Chop & Mine

Tree/stone **patrol automation**:

- Records patrol points with position **and** facing rotation.
- Walks route, chops trees / mines stones via game interaction pipeline.
- Separate saved patrol file: `tree_farm_patrol_points.json` (via unified config).
- Can integrate auto-repair pause like foraging.

### Fishing (`AutoFishingFarm`)

**This is the active fishing system on `main`.** (The legacy `AutoFishLogic` / `AutoFishFarm` input-simulation orphans were deleted.)

Behavior:

1. Ensures fishing rod equipped (restores previous tool on disable).
2. Scans for fish shadows within detect range (15–200 m, default 60 m).
3. Resolves targets via server/netId-aware game APIs on `HeartopiaComplete`.
4. Casts, waits for bite, handles hook and **reel minigame** via `TrySetFishingPressed` (not legacy Input patches).
5. Tension-aware reel: emergency release below 0.15, resume pull above 0.35.
6. State machine with grace timers for stale states, post-catch recast, lost bait recovery.

UI displays user-friendly status:

- Scanning for fish, Waiting for bite, Fish hooked, Reeling, Catch secured, Fish escaped, etc.

Optional hotkeys: toggle auto fish, teleport fishing route (if configured).

**Note:** Startup log explicitly states `AutoFish subsystem disabled` — refers to the **old** `AutoFishLogic` pipeline, not `AutoFishingFarm`.

### Insects (`InsectNetFarm`)

- Auto equips insect net; restores previous tool on stop.
- Scans catchable insects in range (default 50 m).
- Batch catch (default 3 per tick).
- Optional **patrol teleport** through ~50 predefined world coordinates when no targets nearby.
- Pause teleport during auto-repair / auto-eat (configurable).
- Cooldown between catch attempts (default 1.5 s).

### Birds (`BirdNetFarm`)

- Auto equips bird scanner; multi-catch support (1–10, default 1).
- Capture modes: **Safe Capture** vs **Spam Capture**.
- Perfect photo / auto-scare options.
- Safety stop after 90 s continuous run; 60 s re-enable cooldown.
- Stationary throttling reduces multi-catch when player barely moves.
- Pending server ACK tracking for burst catches.
- Optional crash trace logging to file when verbose flags enabled in source.

---

## Features Tab

### Main

| Feature | Description |
|---------|-------------|
| Hide UI + Player | Client-side visibility hiding |
| Bird Vacuum | Client-side bird collection assist |
| Custom Camera FOV | Overrides main camera FOV while enabled |
| FPS bypass | Raises target FPS cap |
| Bypass UI | Skips certain UI blocking |
| Auto click start / close announcement | Login flow helpers |
| Join public / friend / my town | Room join automation |
| Inspect player / move | Debug-style player inspection |
| Hide ID / custom display ID | Social name display tweaks |
| Block game UI when menu open | Input focus helper |
| Stranger chat logging | Optional (master log flag off by default) |

### Food & Repair

**Auto Repair**

- Opens bag UI programmatically, finds repair kit (standard or crafty), clicks Use, closes bag.
- Trigger modes: manual hotkey, toast notification, **durability percentage threshold** (default 10%).
- Optional teleport backward before repair (configurable distance).

**Auto Eat**

- Default food key: `food_bluejam` (configurable type / custom name).
- Eats until energy full or max attempts reached.
- Trigger: hotkey, toast, or energy % threshold (default 20%).

Both use hard-coded UI hierarchy paths under `GameApp/startup_root(Clone)/XDUIRoot/...` — **game updates can break these paths**.

Throttled background checks (`AutoEatTriggerCheckInterval`, `AutoRepairTriggerCheckInterval`) with slower intervals while farms active.

### Snow Sculpting

**Tab:** Features → **Snow Sculpting** (`automationSubTab == 2`). Source: `buddy/SnowSculptureFeature.cs` (partial `HeartopiaComplete`).

#### Auto Snow Sculpture

- Configurable click interval (default 20 ms).
- When `SnowSculpturePanel` is open and in **Round** state: invokes `OnPressDown` for all lit QTE buttons via AuraMono (`GetView<SnowSculpturePanel>` → `_lightButtons`). The game sends `ReportSculptingScore` on round end — the mod does **not** duplicate score packets while the panel path is active.
- Fallback (no panel): `SnowSculptureProtocolManager.ReportSculptingScore` / `SendCommand` if managed types resolve.
- Status box: round count + last API status line.

#### Auto Click Icon

- Configurable interval (default 50 ms).
- Replaces UI clicks on the tracking interact icon with the game interact pipeline:
  1. Collect interact targets (`InteractSystem` via AuraMono + static helper).
  2. `ConfirmExecuteHasTargetCommand` for snow commands **15** (start sculpt), **14** (put snowball), **16** (gather statue) — skip if confirm dialog required.
  3. `PlayerInteraction.ExecuteHasTargetCommand(levelObjectId, commandId)` (managed + AuraMono).
- Skips while the snow sculpture QTE panel is already open.
- Decompiled reference: `InteractTrackCellModel.TriggerOnClickByView` → same `ExecuteHasTargetCommand` call.

#### Move snowballs to backpack

- Button **Move snowballs to backpack** on the same tab.
- Scans **warehouse only** (`EStorageType` **2**), filters stacks with **`staticId == 5100`** (Snowball). Locked stacks are skipped.
- Sends `BackpackProtocolManager.MoveBatchBackpackItems` → bag (`targetStorageType` **1**), chunked at 256 stacks per batch (same as Bag/Warehouse transfer). See [BACKPACK_AND_ITEMS.md](./BACKPACK_AND_ITEMS.md).

#### Debug

- `MasterLogSnowSculpture` in `SnowSculptureFeature.cs` (default **false**) — `[SnowSculpture]` lines in `helper.log`.

### Auto Buy

- Teleport → open cooking store → buy configured items → return.
- Master log flag `MasterLogAutoBuy` / `MasterLogForceOpenShop` in source.

### Buy All (Coin) — Selected Shop

- **Menu:** **Features** tab → same dropdown as **Force Open Shop** → **BUY ALL (COIN)**.
- **File:** `buddy/ShopBuyAllFeature.cs` (partial `HeartopiaComplete`).
- **Flow:** coroutine with 2-frame warmup → list goods → filter → buy loop (~100 ms between purchases). Status line under the button.
- **No UI clicks** — protocol only.

#### What gets bought

Includes only items where all of the following hold:

| Field | Value |
|-------|--------|
| `storeMoneyType` | `StoreMoneyType.Currency` |
| `currencyType` | `CurrencyType.Coin` (1) |
| `isUnlock` | true |
| `leftCount` | > 0 |
| `price` | > 0 |

**Skips already owned** (not offered again):

- `boughtCount > 0` on limited-one slots
- `PlayerServiceSystem.GetItemCount(itemStaticId) > 0`
- managed path: `ShopItemData.isObtained`
- avatar rewards: `ShopSystem.CheckIfAvatarHasObtain` (AuraMono)

Coin balance is read via `PlayerServiceSystem.GetCurrencyCount(Coin)`; unaffordable items are skipped.

#### Purchase paths

| Store type | `storeId` | API |
|------------|-----------|-----|
| Normal NPC shops (cooking, garden, general, …) | per dropdown / resolved | `ShopShelfProtocolManager.BuyItem` (AuraMono); managed `ShopSystem.BuyItem(netId, count)` if types load |
| **Clothing Store** | **5** (`DressShopPanel`) | `ShopShelfProtocolManager.BuyClothes` (AuraMono `List<ClothesStoreEntry>`, `wear: false`) — **not** `BuyItem` |

Clothing buys **one piece per command** (game API), not stack count.

#### Listing goods

1. Managed: `ShopSystem.GetStoreGoodsData(storeId)` when `FindLoadedType` works.
2. Fallback AuraMono: same method on `DataModule<ShopSystem>` instance.

`ShopItemData` is read via **field-only** access in the Aura path (`_leftCount`, `rewardData.staticId`, …) — no property getters on structs (avoids crashes).

#### Unsupported shops

| Dropdown entry | Reason |
|----------------|--------|
| Face Shop | Not a Coin `ShopPanel` store |
| Meteor / Starfall Exchange | Item cost (shards), not Coin — `WeatherExchangeShopPanel` |

#### Store IDs (Force Open / buy-all)

Same mapping as `TryResolveForceOpenShopStoreId` in `HeartopiaComplete.cs` (e.g. cooking **53**, garden **51**, clothing **5**, general store resolved at runtime). See **Force Open Shop** below.

#### Debug

- Errors always logged: `[ShopBuyAll] …`
- Verbose steps: set `MasterLogAutoBuy = true` in source (shared with Auto Buy).

### Force Open Shop

- Menu: **Features** tab → dropdown of hardcoded shops → **OPEN SELECTED SHOP**.
- Normal NPC stores: `ShopPanel.OpenShopPanel(storeId)` via AuraMono (`TryOpenShopPanelByStoreId`).
- Fortune rainbow/rain (wish stars): storeId **86** / **87** — still `ShopPanel`, not exchange.
- **Meteor / Starfall exchange** (Doris, starfall shards): `WeatherExchangeShopPanel.OpenWeatherExchangePanel` — storeId **140** (`MeteorStarfallExchangeStoreId`).
- How to add or debug similar panels (IL2CPP vs mono hooks, `storeId` discovery): **[TYPE_RESOLUTION.md § UI panels, hooks, and IL2CPP](./TYPE_RESOLUTION.md#ui-panels-hooks-and-il2cpp-worked-example-weather-exchange-shop)**.

### Auto Sell

- **Scan source:** Bag only, Warehouse only, or **Both** (dropdown).
- **Obtain:** `BackPackSystem.GetAllItem` per storage (managed + AuraMono); optional runtime snapshot when scanning warehouse-only.
- **Filter:** configured item key (descriptor substring / `p_` photo prefix); **star filter** (0 = any, 1–5 = exact star); **skip 5★**; reserve count per group; max per stack; sell full stack.
- **Sort:** none — all matching `netId` stacks are aggregated and sold.
- Interval-based loop; can hide bag items from normal UI while running.

See [BACKPACK_AND_ITEMS.md](./BACKPACK_AND_ITEMS.md#auto-sell-detail).

### Mass Cook (Net Cook)

- Patrol-based mass cooking at saved cooking patrol points (position + rotation per station).
- Scans radius for cook targets; optional mini-game-only mode.
- Config: interval, scan radius, wait at spot, cooking speed.
- Coroutine warmup on mod init (`NetCookCoroutineWarmupRoutine`).

### Puzzle (`PuzzleNetFeature`)

- Detects puzzle UI open state.
- Reads piece layout from game objects / net IDs.
- Auto-solves placement puzzle when enabled.
- Shows `Solving...` / `Waiting for puzzle target...` status.
- Disables itself after successful solve or on error.

### Pet Care

**Pet Feed (`PetFeedFeature`)**

- Feed all visible cats or dogs in sequence.
- Cooldown between bulk feed runs.
- Per-pet single feed from UI list.
- Food list from `PetSystem.GetFoods()` (not a full-bag scan); sorts by **lowest fullness** first, then `staticId`.
- Optional selected-food filter in UI.

See [BACKPACK_AND_ITEMS.md](./BACKPACK_AND_ITEMS.md#pet-feed-detail).

**Pet Play (`PetPlayFeature`)**

- **Auto Cat Play:** answers cat QTE prompts automatically.
- **Auto Dog Train:** handles dog training QTE flow.
- Independent toggles + hotkeys.

---

## Radar Tab

### Core radar

- Scans world for configured resource prefabs / markers.
- Categories: mushrooms (incl. truffle), berries, stones, ores, trees (apple, mandarin, rare), fish shadows, meteors, misc event resources.
- Toggle per category; select all / clear all.
- Max distance slider (25–1000 m, default 75 m).
- Marker styles: **Default** (icon markers) or **Simple Text**.
- Force refresh scan button.

### Resource Visual ESP

Overlay drawn on top of game view for radar markers:

| Setting | Description |
|---------|-------------|
| Enable/disable | Tied to radar config |
| Style | Beacon, Card, Minimal |
| Show distance | On-screen distance label |
| Connector line | Line from marker to screen edge |
| Offscreen indicators | Arrows for off-camera targets |
| Scale / opacity | Visual tuning |
| Max markers | Cap (default 120) |

Uses embedded tree icons for some marker types.

### Priority locations

Weighted preference for specific forage types when multiple markers compete (fiddlehead, tall mustard, burdock, mustard greens).

---

## Teleport Tab

- **Preset lists:** home, animal care, NPCs (runtime cache), fast travel, events, houses.
- **Custom teleports:** add current position, name entry, persist to unified config.
- Teleport implementation sets `OverridePosition` + frame counter to hold player at destination through patched movement.
- NPC list rebuilt from game data when available.

---

## Bag / Warehouse

- Scans **Bag** or **Warehouse** through `BackPackSystem.GetAllItem` (AuraMono), one row per `netId` stack.
- Grid sorted by **display name** (A→Z); user picks stacks (no auto “cheapest” picker).
- Transfer sends `BackpackProtocolManager.MoveBatchBackpackItems` (`BatchMoveNetworkCommand`, max 256 stacks per request; mod chunks larger batches).
- Direction: Bag → Warehouse (`targetStorageType = 2`), Warehouse → Bag (`targetStorageType = 1`).
- Does **not** require opening the in-game bag, warehouse tab, or multi-select UI.
- Optional **Multi** mode: click stacks to build a batch, set quantity, then **Transfer**.
- Locked stacks are shown but skipped on send.
- **Warehouse Anywhere:** while the game bag UI is open, unlocks the warehouse tab client-side (does not move items by itself).

Full pipeline: [BACKPACK_AND_ITEMS.md](./BACKPACK_AND_ITEMS.md#bag--warehouse-transfer-detail).

---

## New Features — Animal Care & Daily Quests

### Wild animal feed (`WildAnimalFeedFeature`)

- Scans **backpack** via `GetAllItem`; matches food allowed for the animal **group** (fullness table per `staticId` + star).
- **Skip 5 Star Food** (default on): never uses 5★ food.
- Picks food with highest score: bond EXP (favorites weighted) + fullness contribution.
- Manual **Feed**; separate from daily quests.

### Wild animal gifts (`WildAnimalGiftFeature`)

- **Claim All Wild Gifts** (Animal Care tab): collects pending gift `netId`s from loaded ECS entities, then calls `AnimalProtocolManager.TakeGift` per target (~0.45 s between claims).
- **Pending count:** `WildAnimalProtocolManager.HaveGift()` → `IWildAnimalService.HaveGift()` (AuraMono) returns `AnimalGroup` ids with red-dot gifts.
- **Target discovery (AuraMono only):** entity scan over `TryEnumerateAuraMonoLoadedEntityObjects` — for each `netId`, `AnimalProtocolManager.GetNetworkEntity`, then:
  - **Gift boxes:** `AnimalUtil.IsGiftBox` + `AnimalUtil.GetGroup` must match a pending group.
  - **Animal-carried gifts:** `WildAnimalProtocolManager.HaveGift(EcsEntity)` + `AnimalUtil.GetGroup` in pending groups.
- **Claim:** `AnimalProtocolManager.TakeGift(uint)` → `AnimalGiftTakeNetworkCommand`.
- Does **not** use managed `EcsService.TryGet<IWildAnimalService>`, `DataCenter.TryGetComponentData`, or level-object scan (those paths fail or are redundant under BepInEx).
- Details, logs, troubleshooting: [BACKPACK_AND_ITEMS.md](./BACKPACK_AND_ITEMS.md#wild-animal-gifts-detail).

### Daily Quests

| Control | Purpose |
|---------|---------|
| **Auto submit items** | For orders in **CanSubmit** state, builds `List<ItemNetPair>` on game Mono and calls `ClientSubmitTaskItem` / `ClientSubmitNpcTaskItem`. |
| **Skip 5 Star Items** | Excludes 5★ stacks from submission (saved in config). |

**Item selection (auto submit):**

1. Enumerate **backpack + warehouse** (`EStorageType` 1 and 2).
2. Match targets via `CheckSubmitItems` (all targets) and `CheckSubmitItem`; honor `quality` on target rows.
3. Sort matches: **lowest sell price**, then **lowest star**.
4. Fill `needNum` from cheapest stacks; skip locked and (optional) 5★.

Does **not** use `AutoSubmitNpcTaskItem` success alone — that only opens NPC dialogue in vanilla UI.

Details and troubleshooting: [BACKPACK_AND_ITEMS.md](./BACKPACK_AND_ITEMS.md#daily-quests-detail).

---

## New Features — Pictures (ScreenCapture)

Decrypts `persistentDataPath/ScreenCapture` to `ScreenCaptureDecrypted` (AES, same as game `EncryptUtil`). **Encrypt changed** re-imports only files whose plain SHA256 differs from `.heartopia-helper-manifest.json`.

**Draw** files are palette index maps (`TextureFormat.R8`), not normal photos. On decrypt:

- `Draw/{id}_{w}_{h}.png` — colored RGBA preview (editable)
- `Draw/.index/{id}_{w}_{h}.png` — original index PNG for lossless roundtrip

Palette comes from the in-game `drawing_lut` texture (128 colors; cached as `ScreenCaptureDecrypted/.drawing_color_lut.png`). Edited colors outside the palette are quantized to the nearest entry on re-encrypt.

**Upload edited drawing to the server** (open the drawing at your easel first): **Extract open drawing** dumps the live canvas to `ScreenCaptureDecrypted/drawing.png`; edit it; **Upload drawing.png** pushes the pixels to the server (DrawBoard protocol) and refreshes the in-game preview/thumbnail. This is server-authoritative — editing the local cache alone does **not** change the drawing in-game.

CLI parity: `tools/screen_capture_crypto.py` (`decrypt` / `encrypt-changed` / `decode-draw` / `encode-draw`; `pip install pycryptodome pillow`). Palette files: `tools/gen_drawing_palette.py`.

**Full technical reference (types, AuraMono access, protocol): [DRAW_TECHNICAL.md](./DRAW_TECHNICAL.md).**

---

## New Features — Homeland Farm

Crop-box (planter) farming inside your homeland. All operations are **radius-based** around the player and send real server commands. Implemented in `HomelandFarmFeature.cs` (resolved via reflection + **AuraMono** native path — managed component types are absent under BepInEx).

Tab layout (top → bottom):

1. **Auto Farming** — Capture planters + Start / Stop.
2. **Farm Radius** — single slider (1–80 m) driving every operation below; **persisted to config**.
3. **Crops** — seed source (Backpack / Warehouse / Both), Refresh seeds, seed selector.
4. **Fertilizer** — fertilizer source, Refresh fertilizers, fertilizer selector.
5. **Operations** — buttons: Water in radius · Harvest · Weed · Collect seeds · **Sow** · **Fertilize** · Log diagnostics.
6. Status panel + **Stop** (cancels the running operation).

### Manual operations (radius)

| Button | Action | Owner filter |
|--------|--------|--------------|
| Water in radius | Waters crop boxes + plants (batch = watering hobby-skill cell count) | any |
| Weed | Removes the `hasWeed` flag on crops | any |
| Harvest | Collects ripe crops (`stage == 4`) | **own only** |
| Collect seeds | Collects ready plant seeds | any |
| Sow | Fills empty planter slots with the **selected seed** (batch = sprinkler/hobby cell count) | own |
| Fertilize | Applies the **selected fertilizer** to own crops | own |

Item names in the seed/fertilizer selectors resolve through the same game-table path as the Bag / Auto Sell tabs (`TableData.GetBackPackName` first), so labels match across the mod.

### Auto Farming

**Capture planters** snapshots the crop boxes in radius (like Mass Cook "Capture Stoves") and pins the working-zone center. **Start auto farm** then runs an autonomous loop (disabled until seeds are selected):

1. **Discovery first** — one radius scan builds a crop-netId cache (so it never re-sows already-occupied planters on restart).
2. **Poll** the cached crops directly (no re-scan): remove weeds, harvest ripe crops, drop them from the cache.
3. **Sow** a new generation **only when the zone holds no crops** (start, or after the whole generation is harvested) and a post-sow cooldown has elapsed — prevents re-sowing boxes the server hasn't registered yet (`MaxPlantCountLimit`).
4. **Time-scheduled weeding** — sleep is driven by the crops' exact maturity (`mature = sowTime + ripeGrowTime − growTime`, read from `CropItemData`; "now" via the game clock `GameTimeUtility.GetUnixTime()`). Coarse weeding while far from ripe; **weed every second in the final minute** before harvest.

**Stop:** manual (Stop button / Stop auto farm), or automatic once the selected seed runs out **and** the last harvest is collected.

Enable `MasterLogHomelandFarm` for per-tick logs (`Auto crop timing`, `Auto poll`, `next ripe in …`).

### Entity discovery — the scan funnel

Every farm operation (manual button, hotkey, auto-farm, capture) resolves its target net-ids through one funnel, `TryHomelandFarmCollectFarmEntityNetIds` in `HomelandFarmFeature.cs`. Sources are tried in order; cheap/cached ones first, the expensive native walk last:

| # | Source | Notes |
|---|--------|-------|
| 1 | RegisteredCache | Persisted targets from earlier discovery. Skipped for capture (`useAutoFarmCollectShortcuts:false`) and skips zero-position entries in spatial scans. |
| 2 | InteractSeeds | Current interact-target seeds. |
| 3 | **ComponentRadius (direct ECS)** | `Entities.GetComponents<CropBoxComponent/CropComponent/PlantComponent>` via the AuraMono generic-invoke path. **This is now the primary source** and returns the authoritative crop-box + crop + plant set. |
| 4 | SphereQuery / Cylinder | Entity spatial queries — return 0 on this build. |
| 5 | LevelObjectCache | In-memory level-object position cache (crop boxes are level objects). |
| 6 | AuraProximity | Walks the nearby-entity list and classifies each — the native, crash-prone path. |
| 7 | AuraEntities | Full recursive loaded-entity graph walk — the most crash-prone path. |

**Key change (June 2026):** the direct-ECS source (#3) used to be considered unsafe and was gated off, so discovery fell back to the crash-prone proximity / graph walk (#6/#7), which randomly hit uncatchable native access-violations on **visiting other players' fields** (shared local coordinates, streaming entities). The AuraMono `GetComponents<T>` path was brought up and now works reliably (see [TYPE_RESOLUTION.md → AuraMono generic `GetComponents<T>`](./TYPE_RESOLUTION.md#auramono-generic-getcomponentst-direct-ecs-query)). When #3 succeeds, a `componentRadiusSucceeded` flag now **skips both #6 and #7 on every path**, removing the native-AV exposure. The proximity / graph walk only runs as a fallback if the direct query returns nothing (e.g. a field with no crop boxes, crops, or plants).

Caps still apply as a safety bound on the fallback walk: the inspect cap and global entity/level-object enumeration caps are raised to 8192 for dense homelands.

---

## Settings Tab

Sections typically include:

| Section | Contents |
|---------|----------|
| Keybinds | All hotkeys listed in BUILD doc + feature-specific binds |
| UI Theme | Accent, text, tab, window, panel colors; opacity; scale; HSV picker |
| Localization | Language: en, es, zh-CN, pt-BR |
| Notifications | Enable, screen position (9 positions) |
| Overlay | Status overlay toggle |
| Performance | FPS bypass; LOD override (game default / better / performance / custom bias & max level) |
| Misc | Restore defaults, export-related options |

Config persisted to `%LocalLow%/HelperSettings/Config.xml` (XML serialized `UnifiedConfigData`). Persisted values include keybinds (incl. **Water + Weed Radius**), theme, radar, patrols, bird farm, and the **Homeland Farm radius** (`homelandFarmWaterRadius`, clamped 1–80, default 30).

Separate legacy-compatible JSON fragments still loaded line-by-line for some keys in older migration path.

---

## Keybind Reference

All default to **KeyCode.None** except menu toggle.

| Keybind | Action |
|---------|--------|
| Toggle Menu | Insert |
| Toggle Radar | — |
| Auto Foraging | — |
| Aura Farm | — |
| Water + Weed Radius | — |
| Auto Fish | — |
| Teleport Fishing | — |
| Bypass UI | — |
| Disable All | — |
| Inspect Player / Move | — |
| Auto Repair / Auto Eat | — |
| Join Friend / Public / My Town | — |
| Noclip | — |
| Camera Toggle | — |
| Anti-AFK | — |
| Bypass Overlap | — |
| Bird Vacuum | — |
| Game Speed 1×/2×/5×/10× | — |
| Equip Axe / Net / Rod | — |
| Auto Insect / Bird Farm | — |
| Mass Cook | — |
| Auto Puzzle | — |
| Auto Cat Play / Dog Train | — |
| Feed All Cats / Dogs | — |

Rebind by clicking the button in Settings and pressing a new key.

---

## Master Log Switches (Source Code)

Verbose logging for subsystems is controlled by `private const bool MasterLog*` flags at the top of `HeartopiaComplete.cs`. All are **`false`** in release-style defaults except `MasterLogForceOpenShop = true`.

To enable debug logs, change the relevant constant and rebuild.

---

## Source Files Not in Build

Legacy / experimental files on disk but **excluded from `buddy.csproj`**:

| File(s) | Notes |
|---------|-------|
| `AutoFishLogic.cs`, `AutoFishFarm.cs`, `AutoFishGet*.cs` | Old fishing; replaced by `AutoFishingFarm` |
| `InsectFarm.cs` | Replaced by `InsectNetFarm` |
| `MonoEcs*.cs`, `RuntimeDump.cs`, `FishingAutoDump.cs` | Research / dump tooling (see `test` branch) |

Loader entry points **`MelonLoaderPlugin.cs`**, **`BepInExPlugin.cs`**, **`ModLogger.cs`**, and **`ModCoroutines.cs`** are compiled and required for every build.

---

## Safety and Fair Play

- Many features send **real server commands** (aura farm, bird/insect catch, fishing press state) — not purely client visual.
- Bird farm includes deliberate rate limits and safety stops.
- Use private sessions; automation may violate game Terms of Service.

See root [README.md](../README.md) disclaimer.

---

## Related Documentation

- [BACKPACK_AND_ITEMS.md](./BACKPACK_AND_ITEMS.md) — inventory access, filters, sorting per feature
- [BUILD_AND_RUN.md](./BUILD_AND_RUN.md)
- [TECHNICAL.md](./TECHNICAL.md)
- [TYPE_RESOLUTION.md](./TYPE_RESOLUTION.md)
