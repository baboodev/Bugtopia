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
| Snow Sculpting | Auto snow sculpture clicking |
| Auto Buy | Cooking store purchase automation |
| Auto Sell | Inventory sell automation |
| Mass Cook | Network cooking at patrol points |
| Puzzle | Auto puzzle solver |
| Pet Care | Feed all pets, auto cat play, auto dog train |

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
3. Clicks interact / collects resources.
4. Configurable:
   - Area load delay after teleport
   - Resource click duration
   - Teleport cooldown between targets
   - Auto-repair pause during farm
   - Patrol point list (saved JSON)
   - Loot priority weights (fiddlehead, mustard, burdock, etc.)

Status strings: `IDLE`, `TELEPORTING...`, `GATHERING...`, etc.

### Aura Farm

Server-command style farming without teleporting to each node:

- Resolves game types at runtime via reflection (`ResourceProtocolManager`, `InteractSystem`, `EntityHelper`, etc.). Details: [TYPE_RESOLUTION.md](./TYPE_RESOLUTION.md).
- Sends pick/attack/hit commands for bushes, trees, and stones in radius (~8 m direct scan).
- Throttled per-target cooldown; merged target cap (32).
- Toggle independent of teleport foraging; both can conflict — UI warns when radar/foraging preconditions fail.

**Requirements:** Radar on + compatible resource toggles; internal method resolution must succeed (logged if `MasterLogAuraFarm` enabled in source).

### Chop & Mine

Tree/stone **patrol automation**:

- Records patrol points with position **and** facing rotation.
- Walks route, chops trees / mines stones via game interaction pipeline.
- Separate saved patrol file: `tree_farm_patrol_points.json` (via unified config).
- Can integrate auto-repair pause like foraging.

### Fishing (`AutoFishingFarm`)

**This is the active fishing system on `main`.** Legacy `AutoFishLogic` / `AutoFishFarm` exist as orphan files but are **not compiled**.

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

- Auto-clicks snow sculpture UI at configurable interval.
- Separate icon click interval for sculpt icons.

### Auto Buy

- Teleport → open cooking store → buy configured items → return.
- Master log flag `MasterLogAutoBuy` / `MasterLogForceOpenShop` in source.

### Auto Sell

- Scans bag (and optional alternate sources) for matching item keys.
- Filters: star rating, skip 5★, reserve count, max per stack, sell full stack, match item family.
- Interval-based loop; can hide bag items from normal UI while running.

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
- Opens pet interaction UI and selects food from inventory.

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
- Transfer sends `BackpackProtocolManager.MoveBatchBackpackItems` (`BatchMoveNetworkCommand`, max 256 stacks per request; mod chunks larger batches).
- Direction: Bag → Warehouse (`targetStorageType = 2`), Warehouse → Bag (`targetStorageType = 1`).
- Does **not** require opening the in-game bag, warehouse tab, or multi-select UI.
- Optional **Multi** mode: click stacks to build a batch, set quantity, then **Transfer**.
- Locked stacks are shown but skipped on send.

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

Config persisted to `%LocalLow%/HelperSettings/Config.xml` (XML serialized `UnifiedConfigData`).

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

- [BUILD_AND_RUN.md](./BUILD_AND_RUN.md)
- [TECHNICAL.md](./TECHNICAL.md)
