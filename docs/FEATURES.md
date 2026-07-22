# Features Reference

Complete feature catalog for **Bugtopia**. Works identically under MelonLoader and BepInEx (same `HeartopiaComplete` core). Menu toggled with **Insert** by default. Labels support en, es, zh-CN, pt-BR.

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
| Mass Cook | Network cooking at stoves; remote QTE + Permanent Stove Memory |
| Puzzle | Auto puzzle solver |
| Pet Care | Feed all pets, auto cat play, auto dog train |

**New Features**

| Sub-tab | Content |
|---------|---------|
| Animal Care | Wild animal trough feed (manual), claim all wild animal gifts |
| Daily Quests | Auto-submit item delivery orders (CanSubmit) |
| Homeland Farm | Crop-box farming: auto farm, water/weed/harvest/sow/fertilize in radius, seed/fertilizer selection |
| Pictures | Decrypt / re-encrypt `ScreenCapture` cache (Photo, Draw, …). Draw files get a color preview via game `ColorLut`; index maps kept in `Draw/.index/` |
| Extras | Ice skating: network "Perfect Ice Skating" sequences (`IceSkatingSequenceFeature`) + real-time **Auto Ice Skating** bot (`AutoIceSkatingFeature`) |
| Extra | Open Craft panel; **Analog Move** gamepad-stick → character bridge (`MovementInputFeature`); **Carpet Stamp** — scan party carpets + send a single step-on/step-off (`CarpetStampFeature`); **Sanrio Gacha Finder** — locate SANRIO event gacha machines (3 Star Town scene machines + player-placed ones via UGC actor scan), auto map pins + teleport (`SanrioGachaFinderFeature`) |
| Sand Sculpture | Fully-automatic beach sand-sculpting: auto-place base + auto-sculpt correct model + auto-collect (`SandSculptureFeature`) |

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

- Axe, insect net, fishing rod, sprinkler, bird scanner, building pad

### Hotkey suppression while typing

Whenever a game text field is focused (chat, renaming, search boxes, mail, …) **all mod hotkeys are
disabled except the menu toggle** — so typing a message never accidentally triggers a feature, and
you can still open/close the mod menu. Instrument note-keys are likewise suppressed while an
instrument panel is open. Both are automatic, no configuration needed (`InstrumentHotkeyGuardFeature.cs`).

### Pad build hotkeys (`PadBuildHotkeyFeature.cs`)

Keyboard control of the building pad without touching the on-screen `BuildStatusPanel` buttons. Five rebindable keys (Settings → Keybinds, default unbound):

| Key | Action | Behaviour |
|-----|--------|-----------|
| Pad Confirm | `BuildModule.ConfirmPlacing(false)` | Places the held object (panel hold-button parity) |
| Pad Cancel | `BuildModule.CancelPlacing()` | Cancels current placing |
| Pad Rotate | `BuildModule.RotateAround()` | Rotates the held object; 250 ms debounce |
| Pad Move | `BuildModule.InteractExecuteMove()` | Picks up the focused object for moving; no-op in god mode (grab is a click there) |
| Pad Delete | Pad mode: `InteractExecutePickup()` (pack to backpack); god mode: `InteractExecuteDelete()` (wreck) | Removes the focused object |

All five are gated on `BuildModule.SubState == CraftState.Focus` — in simple Pad free-roam (no object focused/being placed) every key is a **silent no-op**. Works in both homeland build modes (Pad/TPS and god top-down view).

Implementation is a three-tier `BuildModule` resolution (managed → AuraMono `Managers.GetModule(Type)` → UI button clicks); see [TYPE_RESOLUTION.md](./TYPE_RESOLUTION.md) and [plans/2026-06-10-pad-build-api-migration.md](./plans/2026-06-10-pad-build-api-migration.md). Debug log flag: `MasterLogPadBuild`.

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

### Chat Translate Unlock

- Unlocks chat translation for messages the game refuses to translate: the game tags every
  message with the **sender's UI language** (`ChatMessageComponent.LangCode`, reported once via
  `ConnectScene_CS`), not the language of the text — so a player typing e.g. Russian on an
  English UI produces messages an English-UI receiver gets no Translate button for.
- Hooks the `ReceiveChatMessage` EventCenter event (scalar fields only) and for non-self
  messages whose langKey **equals** ours sends
  `ChatProtocolManager.RequestTranslateStream(msgId, "")` directly (AuraMono static invoke),
  skipping the client-side langKey gate. Foreign-langKey messages stay with the game's own
  translate pipeline (its in-panel Translate toggle) — never double-requested, because duplicate
  stream chunks would corrupt the game's append-only translation cache.
- Results ride the game's own stream pipeline (`RequestTranslateStreamResultEvent` →
  `ChatSystem` cache → `TranslateResultUIEvent`), so bubbles/HUD/history update natively.
  If the server itself decides the text is already in our language it answers
  `NoNeedToTranslate` (4209) — logged, nothing visible changes.
- Requires the overseas game build (translation service); server weekly char limits still apply.
  Toggle persisted in config (`chatForceTranslateEnabled`). Implementation:
  `ChatForceTranslateFeature.cs`.
- Sub-toggles:
  - **Debug Log** (`chatTranslateVerboseLog`) — per-message decision trace (sender langKey vs
    yours, message text, and every server stream result incl. game-initiated) to the mod log.
    On completion logs the finished translation (`orig -> translated`), not just errors.
  - **Force ALL Languages** (`chatTranslateForceAllLangs`) — also request foreign-langKey messages,
    but only when the in-game Translate toggle is off (never double-request the game's own path).
  - **Postcard Bypass (test)** (`chatTranslatePostcardBypass`) — experimental route around the
    server's langCode gate. Blocked chat messages are translated via the **postcard** endpoint
    (`MailProtocolManager.RequestTranslatePostCard`, no source-langCode gate — it detects language
    from the supplied text), serialized one at a time. Needs ≥1 postcard in the mailbox to borrow a
    MailId; the translated text is read synchronously via a dedicated detour on
    `DispatchEvent<PostCardTranslateResultEvent>` and logged. Consumes the shared weekly translate
    quota. Implementation: `ChatForceTranslateFeature.Postcard.cs`.

### Game UI — Custom UI Timings (Self → Game UI sub-tab)

- Editable display durations for the game's tip/toast popups: item-obtained bubbles
  (`LightToastPanel`, game default 2.5 s), text toasts, buff icon toasts, and the four
  `HarvestTipPanel` banners (pictorial / achievement / cat-buff / task).
- Overrides float fields of the live `TipShowTimeConfig` (`ConfigManager.TipConfig.TipShowTimeConfig`)
  via AuraMono `mono_field_set_value`; re-applied on a 0.5 s throttle so config reloads can't revert it.
- Originals captured before the first write; disabling the toggle restores them.
- Sliders 0.5–15 s + "Reset to game defaults" button; persisted in config
  (`gameUiTimingsEnabled` / `gameUiTimingSeconds`). Implementation: `GameUiTimingsFeature.cs`.

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
- **Foraging + Aura Farm node-hop wait:** when START FORAGING teleports to a radar node with Aura Farm on,
  the hop to the next node waits for **confirmation of the collect** instead of a fixed 3 s dwell
  (long teleports need world-streaming time before the aura can even see the target).
  - **Fast path (`CollectColdEvent`):** the game dispatches
    `ScriptsRefactory.DataAndProtocol.Events.CollectColdEvent` per resource; multi-charge bushes
    (3 charges on a ~2.5 s server timer) emit decrementing `availableNum` while being drained and
    ONE final event with a real `endUnixTimeMs` when they flip to cooldown — that final event is
    the collect confirmation. The node's bush is bound by the charge-decrement pattern (captured
    aura owner ids are often aggregate level-object ids that never appear in events). On confirm
    the real cooldown is stamped into the radar (`ApplyLiveResourceCooldownByPosition`) so the
    ESP marker flips on the next 2 s rescan; the hop follows 0.25 s later. No aura-quiet gating —
    the aura re-spams every in-radius bush continuously, so quiet never happens in berry fields.
    `CollectObjectShowEvent(show=false)` covers despawn-style gathers.
  - **Authoritative live layer:** the map feature's mono collectable scan (position + `inCold` +
    `coldEndTime`) runs every ~2 s while the radar or foraging is active and is synced into the
    radar's local cooldown dicts (`SyncLiveResourceColdStates`) — markers/ESP show REAL server
    cooldowns shortly after radar enable, already-cold nodes are skipped before teleporting, and
    after a long teleport the wait resolves the node's true state as soon as its entity streams
    in (cold → immediate skip; warm → event flow; not streamed yet → keep waiting).
  - **Radar fallback:** node's marker flips to `[CD]` or is hidden by the cooldown stamp after the
    aura actually sent a command, with the aura idle ≥1.25 s (covers meteors and captures that
    never happened). Managed entity probing (`inCold`/`coldEndTime` reflection) is registered but
    dead on this build — XDT* entity resolution is Mono-only.
  - Bounded by the **Collect Wait Max** slider (4–30 s, default 12, `auraCollectWaitTimeout`);
    priority-anchor dwells keep the old fixed delay.

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
7. **Instant Catch** (optional): spoof buoy `successLength` via AuraMono Reliable `SendCommand` — no avatar teleport. See [TYPE_RESOLUTION.md](./TYPE_RESOLUTION.md) § 2b.

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

**Tab:** Features → **Snow Sculpting** (`UguiShellFeaturesSnowSculptingSubIndex`). Source: `buddy/SnowSculptureFeature.cs` (partial `HeartopiaComplete`); UI in `buddy/HeartopiaComplete.UguiFeaturesPuzzleSnowContent.cs`.

#### Auto Snow Sculpture

- Fixed report interval **10 ms**.
- **Self-starting, pure protocol (no interact / no `ExecuteHasTargetCommand`).** When no sculpture is active it resolves the snow base net id by, in order: (1) the cached base from a prior cycle this session, (2) the current interact target's level object → `ownerNetId` (when standing next to it), (3) a **50 m scan** of loaded entities — distance-filtered, then `EntityUtil.GetEntityResId` + `TableData.GetSnowbase(staticId)` to confirm it's a base, nearest wins. It then finds a snowball in the bag (`BackPackSystem.GetItemNetId(5100)`) and sends `PutSnowBall` + `StartSculpting`. A base needs exactly one snowball (`Idle→Prepared→Started`). Pure-protocol start does not open the panel or set the client `TargetNetId`, so the just-started base is treated as the active target **optimistically** and rounds begin; premature/redundant fills/starts are harmlessly rejected by the server. Base state is never read (avoids an unsafe struct-out component read). The old **Auto Click Icon** toggle (whose `ExecuteHasTargetCommand` spammed mono `icall.c` warnings) and the whole interact path were **removed**.
- **Pure API, no UI/panel interaction.** Per interval it resolves the active snow base (`SnowSculptureStatus.TargetNetId` via AuraMono / player status) and sends a perfect-round score with `SnowSculptureProtocolManager.ReportSculptingScore(baseNetId, score)` (AuraMono first, managed `ReportSculptingScore` / `SendCommand` fallback). The QTE panel's `OnPressDown` / `_lightButtons` path was **removed**.
- Round counter resets when the target base changes. After **20 rounds** (`SnowSculptureMaxRound`) it finalizes: `StopSculpting(baseNetId)` (`StopSnowSculptingNetworkCommand`) so the server computes the sculpture from the accumulated score, waits `SnowApiFinalizeDelaySeconds` (2 s) for `state==Idle && finishedStatidId!=0`, then `GatherSnowSculpture(baseNetId)` (`SnowSculptingTakeNetworkCommand`) to take it — after which it auto-disables and toasts. Both calls are AuraMono-first with managed fallback and are fire-and-forget. The on-screen `SnowSculpturePanel` is **not** closed by the mod (no UI interaction); it dismisses on its own timer.
- Status box: round progress `n/20` + cumulative count + last API status line.

#### Auto Click Icon

- Configurable interval (default 50 ms).
- Replaces UI clicks on the tracking interact icon with the game interact pipeline:
  1. Collect interact targets (`InteractSystem` via AuraMono + static helper).
  2. `ConfirmExecuteHasTargetCommand` for snow commands **15** (start sculpt) and **14** (put snowball) — skip if confirm dialog required. Gather (**16**) is intentionally excluded: Auto Snow owns the take (StopSculpting → GatherSnowSculpture via protocol), so running it here would be a redundant gather (and an extra mono `icall.c` burst).
  3. `PlayerInteraction.ExecuteHasTargetCommand(levelObjectId, commandId)` (managed + AuraMono).
- Skips while the snow sculpture QTE panel is already open.
- Decompiled reference: `InteractTrackCellModel.TriggerOnClickByView` → same `ExecuteHasTargetCommand` call.

#### Move snowballs to backpack

- Button **Move snowballs to backpack** on the same tab.
- Scans **warehouse only** (`EStorageType` **2**), filters stacks with **`staticId == 5100`** (Snowball). Locked stacks are skipped.
- Sends `BackpackProtocolManager.MoveBatchBackpackItems` → bag (`targetStorageType` **1**), chunked at 256 stacks per batch (same as Bag/Warehouse transfer). See [BACKPACK_AND_ITEMS.md](./BACKPACK_AND_ITEMS.md).

#### Debug

- `MasterLogSnowSculpture` in `SnowSculptureFeature.cs` (default **false**) — `[SnowSculpture]` lines in `bugtopia.log`.

### Sand Sculpting

**Tab:** New Features → **Sand Sculpture** (`UguiShellSandSculptureSubIndex`). Source: `buddy/SandSculptureFeature.cs` (partial `HeartopiaComplete`); UI in `buddy/HeartopiaComplete.UguiSandSculptureContent.cs`.

#### Auto Sand Sculpture

- **Pure protocol, AuraMono-only** (the vanilla swing QTE is fully client-simulated in `SandSwingTrackCellModel` and only reports totals). FSM per cycle:
  1. **FindBase** — scan streamed `SandSculpturesComponent` views (`TryAuraMonoGetComponentObjects`), nearest base within **60 m**; reads `_componentData { baseStaticId, roughStaticId }` from the boxed struct via field offsets. If none is found and **Auto-place base** is on, goes to **PlaceBase** (below) instead of just re-scanning.
  2. **StartSculpt** — rounds = `TableData.GetSandrough(roughStaticId).interval.Length`; quality gate `FeatureOpenSystem.IsFeatureOpen(SandSculptingQuality=300041)`; sends `StartMakingSandSculpture` only. **`CanStartSandSculpture` (PreStart) is never sent** — its `PreStartSandSculptureEvent(Ok)` reply makes the vanilla client *join* the sculpt mode (`GameSandMode` → `TrackModule` opens the swing-QTE track) and the player FSM sends a duplicate Start → in-game "Error" tip + a stuck QTE track. The server accepts Start+Finish without the handshake, so the whole PreStart path (and its `TrackModule.EndSand` cleanup) was removed.
  3. **SendFinish** — after the configurable **Finish delay** slider (default 3 s) sends `FinishMakingSandSculpture(base, successCount, perfectCount)`: all-perfect `(0, rounds)` when quality is open, else the vanilla-max `(1, 0)`.
  4. **WaitRough** — polls (0.5 s, 12 s timeout) for the own `SandSculptureRoughComponent` (`ownerNetId == self`), reads `options[]` from its component data, then `ChooseSandSculptureProduct(roughNetId, productId)` (**rough** netId, not the base). **Never spoils, and prioritizes the collection:** the 14 real sculptures are `SandfinishedItem` ids **1–14**, decoys are **>14** (incl. name-trap duplicates 17/25/26) and picking a decoy yields `sandfinished 600299` = a **spoiled** sculpture. A draft carries **one or more** valid ids (≤14) + decoy fillers. When only one is valid there is no choice; when 2–3 are valid and **Prefer rare / uncollected models** is on, the mod ranks the valid ids by `(collectValue DESC, rarity DESC, id ASC)` — collectValue from the Pictorial (`PictorialSystem.TryGetPictorialData` → `_state`/`_starRate`: 2 = entry not collected, 1 = star below the current hobby-level cap, 0 = done), rarity from `Entity._rarity`, star cap from `HobbySystem.GetHobbyLevel(140)` (L1-2→3★, L3-4→4★, L5→5★). Fully **fail-closed** to smallest-valid-id. Map `SandfinishedItem 1..14 → Sandfinished 600100..600113` is a verified hardcoded array (`.research-record/sand-5star-priority.md`). Clears `PlayerDataComponent.SetSandRoughData(0)` afterwards.
  5. **CloseDialog** — the rough spawns next to the player, so the vanilla FSM opens the `DialogueSimplePanel` "choose model" dialog; our protocol Choose bypassed its callback, so nothing closes it. Polls (0.25 s, 3 s timeout) `UIManager.GetView(DialogueSimplePanel)` (Type from the class pointer — never `Type.GetType`) and invokes its protected `CloseSelf()`.
- **Auto-place base from backpack** (toggle, **off by default**): when FindBase finds nothing, place a fresh base ~0.8 m in front of the player via the game's generic build-placement command directly (no build-mode UI / camera), then rescan. **Out of sand bases in the bag stops the whole feature** (`autoSandEnabled = false`, not just auto-place) — no base can appear by retrying and the loop has nothing to sculpt. Other (non-terminal) failures disable just auto-place after 3 attempts. Path (`.research-record/sand-base-placement-helper.md`): scan bag for a `TableSandbase` item (`TableData.GetSandbase(staticId) != null`) → build a boxed `BuildPlaceData { BuildType=0, TransformData { LevelObjectNetId = putZoneId, LocalPos = root-local target, Angle, VirtualLinkLevelObjectNetId=[putZoneId] } }` where `buildRootNetId = LocalPlayerComponent.inFieldNetId` and `putZoneId = (ulong)root | (1<<32)` → `HomelandProtocolManager.SendBuildBatchOperation(bagItemNetId, buildRootNetId, IBuildData)` (the 3-param one-shot). No dedicated sand-place command exists; base placement goes through the generic build system. **No server position check** (build-overlap is bypassable).
- **Auto-collect finished sculptures** (toggle, **off by default**): independently of the sculpt FSM, scan `SandFinishComponent` views, and for each own freshly-made one (`SandFinishComponentData.onNewMake == true`) take it into the backpack via `CharacterProtocolManager.PoseDeleteBuild(entityNetId)` (→ `TakeItemNetworkCommand`, exactly what vanilla `GardenSandCommand`/interact 85 does). One at a time (`PoseDeleteBuild` gates on a single in-flight build option cleared by the server's `BuildOptionRespondEvent`), throttled ~1.5 s between takes. **Off by default** because a continuous every-tick scan caught a mono teardown window and AV'd (2026-07-09); the pre-flight `BackPackSystem.GetBlankCount()` check that faulted was removed — a full bag now just gets a harmless server-side reject.
- A base failing 3 times (start/finish rejected → rough timeout) is **blacklisted** for the session; "Reset base blacklist" button clears it. "Close stuck dialog" button force-closes a stray model dialog.
- **Hotkey:** Settings → Keybinds → **Auto Sand Sculpture** (rebindable, default unbound) toggles the main auto-sculpt on/off.
- UI (all localized — en/es/zh-CN/pt-BR/th): the auto-sculpt toggle, auto-place + auto-collect toggles, finish-delay slider, a compact status box (state / done / collected / last status / place / collect), and the two buttons. **Quality note:** the mod maxes the controllable QTE input (all-perfect), but the actual star cap is gated by the player's **Lepka/sand hobby level (theme 140, L1–5): 5★ only at L5** — see memory `sand-sculpting-qte-model`.

#### Debug

- `MasterLogSandSculpture` in `SandSculptureFeature.cs` (default **true** while the feature is fresh) — verbose `[SandSculpture]` lines; deduped status logging stays on regardless.

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
- **Icons:** item tiles (Auto Sell grid + Bag / Warehouse transfer grid + pet-feed food list) and
  the radar / Resource Visual ESP markers load icons **directly from the game**, always-on:
  staticId → `RewardUtility.GetIconName` → `ResManager.LoadSpriteAsync("ui_item_normal_…")`,
  copied into the shared in-memory texture cache on arrival (confirmed in-game July 2026). The
  legacy `CACHE ICONS` open-bag harvest and the entire disk PNG cache
  (`…\Bugtopia\Cache\ItemIcons`) were removed; only the dll-embedded `tree`/`rare_tree` radar
  icons remain baked in. Details: [ITEM_ICON_PIPELINE.md](./ITEM_ICON_PIPELINE.md).

See [BACKPACK_AND_ITEMS.md](./BACKPACK_AND_ITEMS.md#auto-sell-detail).

### Mass Cook (Net Cook)

- Patrol-based mass cooking at saved cooking patrol points (position + rotation per station).
- Scans radius for cook targets; optional mini-game-only mode.
- Config: interval, scan radius, wait at spot, cooking speed.
- Coroutine warmup on mod init (`NetCookCoroutineWarmupRoutine`).
- **Ingredient model (Move Ingredients / max-quantity):** a recipe is a flat list of material slots, **one slot = one unit** (a recipe needing 3 wheat has 3 slots). A slot is either a **specific item** (ingredient id ≥ 100) or an **"any &lt;category&gt;" slot** (id &lt; 100 = a `FoodMaterialType`, e.g. "any fish" = `Fish`). Category slots carry `materialId == 0` and only set `materialType`. Requirements track `IsCategory`/`MaterialType`; `BuildNetCookDemands` aggregates per-dish counts (specific items vs categories), and warehouse move + max-quantity match category slots via `NetCookItemMatchesCategory` → `CookingSystem.CheckFoodTypeSatisfied` (cached). Cooking itself relies on the game's `AutoFill` to fill slots from the bag, so the mod only needs to move enough matching items. See [cooking ingredient details](DECOMPILED_SOURCE_MAP.md#312-cooking-net-cook--mass-cook).

#### Remote cooking (QTE at distance)

Cook commands (`PrepareCooking`/`StartCooking`/`InteractWithCooker`/`ContinueCooking`) are server-side and keyed only on the stable `LevelObjectNetId`, so they work at any distance. The blocker was **status visibility**: the QTE relief (`InteractWithCooker` on `Danger`) needs the live cooking status, but the mod read it from the **view** event `UpdateCookingStatusEvent`, which dies the moment the stove streams out (~17–85 m). Sending relief outside the Danger window instantly burns the dish, so blind relief is not an option — accurate remote status is mandatory.

- **Status source:** a MonoMod `NativeDetour` on the static `CookingProtocolManager.OnUpdateCookerStatus` — the ECS→data chokepoint the sync bridge calls for **every** server status update, independent of the streamed view. Confirmed in-world that `Danger`/`Cooking`/`Failed` arrive there at 85 m+ (Danger holds ~12 s, ample relief time). The detour body is allocation-free (records scalars into a ring + forwards via the trampoline); a main-thread drain feeds a **`levelObjectNetId`-keyed status cache** (`netCookStatusByLevelObject`). `TryGetNetCookTargetCookingStatus` reads this cache first (stable across stream-out/in, unlike the view `CookerNetId`). Installs whenever cooking is active; logging gated behind **Status Diagnostics**.
- **Remote completion:** the post-collect Idle reset travels via `ComponentRemoved<CookingStatusComponent>` (not `OnUpdateCookerStatus`), so the lo-cache never sees it at distance. The global `CookResultEvent` (`interaction == TakeFood`) is hooked as the remote "dish finished" signal → seeds the lo-cache to Idle and flags the stove collected so drain can remove it. Captured-but-never-cooked idle stoves (no status source at all at distance) are removed in drain once `Phase == 0`. Together these let a finite remote mass cook drain to zero and auto-stop.

#### Permanent Stove Memory (`Remember Stoves` toggle)

Reuse a captured stove set on every start **without re-scanning**, so you can run Mass Cook anywhere without standing in your homeland.

- Captured stoves live in the in-memory registry `netCookRegisteredTargets`. With the toggle **ON**, restore (`TryResolveNetCookContextsFromRegisteredCache`) bypasses the distance/position culls (`RemoveOutOfRangeNetCookTargets` skipped; missing world position no longer drops a stove) — cooking uses the stable `LevelObjectNetId`, not position. After a cook finishes, the cook context persists so the next start restores the **full** remembered set instead of cooking only the last stove.
- **Reset Capture** clears the registry (the "forget" action — use after switching homelands).
- The toggle is saved to config. The registry itself is **session-only**: view/lo netIds are reassigned across game restarts, so re-capture once per login.

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

**My Pets (per-pet Play / Wash)**

- `Show My Pets` lists owned cats/dogs (PetFeed scan, `IsMine` only) with live energy (vitality) / food (fullness) / growth (chemistry) from `PetSystem.GetPetComponentData`; per-row message shows detailed session progress.
- **Play** — cat: protocol-only headless tease session (`MeowProtocolManager.BeginTease`, no game mode → no status panels; instant QTE answers via the event hook; `CatPlayExitForUiEvent`/`CatPlayPromoteForUiEvent` suppress-forwarded while active; Stop cancels silently via `CancelTease`); dog: headless training (`PrepareTease` → `BeginTease(learningId)`), picking the first **unfinished** learning motion, else the first unlocked one (`PetSystem.GetMotionDatas` + `TableDogLearningMotion.unlockGrowth`). Dog rounds are answered ONLY after the per-netId `TeaseDogPlayEvent` (+0.5 s) — the same answer-window signal `DogPlayStatusPanel` uses; the performance motion syncs seconds earlier, so motion-based timing bounces off the closed window. Choice = shared resolver core `TryResolveDogQteChoiceForLearning` (dog motion vs table motion / requireLearningId), also used by Auto Dog Train. `[PetPlay][dog]` trace lines log every server event / motion change / send while a dog session runs.
- **Wash** — headless bath loop: `PetBathinghBegin` → `PetBathingRoundStart` per round driven by `PetBathClickResultClientEvent` / `PetBathRoundEndClientEvent`; no `PetBathPanel`.
- Pre-checks mirror the game's interact gates: hungry (fullness 0), dog energy (`vitality < TableDogThemes.teaseVitalityPointDecrease`); server rejections map to row messages ("already clean", "no stamina", "only at home", …).
- **Train until all learned (+energy food)** toggle: with it on, Play runs a loop — repeat headless sessions until every unlocked action is learned; when the pet is hungry/out of energy it auto-feeds the ENERGY item (the feed "hobby tool": `TableDogfooditem.isHobbyTool` / `TableCatfooditem.catHobbyTool` — Energy Dog Food / Energy Fish Jerky) via `BeginFeed`'s `HobbyToolNetId` slot, then continues. Stops when done, when energy food runs out, after 3 fruitless feeds/failed sessions, or via the row `Stop`.
- One headless session at a time; the active pet's row shows `Stop`. `PetTeaseBeginResultEvent` is suppressed during dog sessions so `TrackingDogPlay` never opens the result panel.

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

### Game map display ("Game" mode)

Native in-game markers instead of (or alongside) the ESP overlay — see [RADAR_GAME_MAP.md](RADAR_GAME_MAP.md).

| Setting | Description |
|---------|-------------|
| ESP Overlay / Game Map | Route selected resources to the screen overlay or to the native map |
| Map Markers (nearest) | Cap on how many nearest resources are tracked (1–30, default 5) |
| Show on big map | Also place markers on the big map (default **off**; on adds the game's tracked rectangle frame) |
| Player Avatars (all) | Real avatar photos on map player markers for every player, not just friends (default **off**; opt-in detours) |

- Per-resource native icons (timber/stone/bamboo/fruit/mushroom), rare tiers (e.g. Rare Timber), players' native pin, Bird/Fish/Insect category icons.
- Shows on the minimap + in-world tracking pointers; big map is opt-in.
- Cooldown/depleted resources are hidden (authoritative via `CollectableObjectComponent.inCold`).

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
- **Warehouse Anywhere:** while the game bag UI is open, unlocks the **full** bag/warehouse page away from home: warehouse tab, single-item move, long-press multi-select and the multi-select panel with the full-stack toggle (scoped `IsPlayerInHomeLand` spoof active only while the bag is open; does not move items by itself).

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

Decrypts `persistentDataPath/ScreenCapture` to `ScreenCaptureDecrypted` (AES, same as game `EncryptUtil`). **Encrypt changed** re-imports only files whose plain SHA256 differs from `.bugtopia-manifest.json`.

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

## New Features — Extras (Ice Skating)

The **Ice Skating** sub-tab (`UguiShellIceSkatingSubIndex`) hosts two independent ice-skating tools, laid out top → bottom in `buddy/HeartopiaComplete.UguiIceSkatingContent.cs`: the network-sequence buttons first, then the auto-skating controls underneath. Backends are `IceSkatingSequenceFeature.cs` and `AutoIceSkatingFeature.cs`:

1. **Perfect Ice Skating** (network sequences) — `IceSkatingSequenceFeature.cs`.
2. **Auto Ice Skating** (real-time bot) — `AutoIceSkatingFeature.cs`.

### Perfect Ice Skating (network sequences)

Server-command driven runs that don't require you to be skating in real time:

| Button | Action |
|--------|--------|
| Challenge (5 perfect, 1500) | Runs a scripted challenge sequence aiming for a perfect score (~1500). `Runs` field sets repetitions. |
| Perfect Drill | Repeated perfect-action drill. `Runs` field sets repetitions. |

A run count field (`DrawIceSkatingRunCountField`, clamped to `IceSkatingSequenceMaxRunCount`) sits beside each button. Only one sequence runs at a time (`iceSkatingSequenceCoroutine` gate). Loader log tag: `[IceSkatingSeq]`.

### Auto Ice Skating (real-time bot)

`AutoIceSkatingFeature.cs` — a `partial class HeartopiaComplete` split. Watches the local player's `GameSkateMode` each frame and **chains skate tricks automatically while you still control movement**. Toggle it on the Extras tab or via the **Auto Ice Skating** hotkey (Settings → Keybinds → PLAYER, default unbound).

**Type resolution.** Managed reflection is tried first; if the managed types are absent (the BepInEx IL2CPP build), it falls back to the **AuraMono** native path (`mono_runtime_invoke`). Both execution paths are kept in full parity. Resolved surfaces: `LocalPlayerComponent.GetGameMode<GameSkateMode>` (or `Character.GetMode<…>`), `GameSkateMode` (`SkillTrigger`, `CanTriggerUltimate`, `CalculateSpeedRate`, `IsReceiver`, `GetRatioInConfiguredPhase`, `actived`, `Energy`, `SkateSkills`, `UltimateSkill`, `_currentCastAction`, `_skateActions`, `ChallengeInfo`), and `TableData` (`GetSkateAction`, `GetSkateActionType`, `GetSkateActionState`, `GetPairSkateUltimate`). Resolution retries every 5 s until the player enters the rink; a circuit breaker disables the tick after repeated exceptions.

**Decision logic per frame** (`TickAutoIceSkating` / `TickAutoIceSkatingAura`):

1. **Not skating / inactive / pair-receiver** → idle. Pair *receiver* is manual-only (the partner drives). After entering the ice there is a short warm-up, and at challenge start a 3 s countdown is skipped.
2. **Performing an action** → first try an ultimate (see below); otherwise honour the **Perfect move** setting.
3. **Idle (no action playing)** → try an ultimate, else pick and trigger the next simple move.

**Simple-move selection** (`PickAutoIceSkatingBestSkill`): for each skill in the current tree it reads whether the action is **new** (challenge novelty bonus, `ChallengeData.IsNewAction`) and its **duration** (sum of phase spans from `TableSkateActionState.phase`). Ranking:

- **Prefer new move** on (default): new actions outrank used ones; ties broken by shortest duration.
- **Prefer new move** off: ranked purely by shortest duration.

**Perfect-move timing** (`TryAutoIceSkatingTryPerfectInterrupt*`):

- **Perfect move** on (default): the next move is triggered inside the perfect window (`GetRatioInConfiguredPhase` over the action's `prefectPhase`), so each chained trick scores its perfect bonus.
- **Perfect move** off: the next move is chained **as soon as the game allows an interrupt** (the game's `SkillTrigger` still gates blend time / non-interruptible phases) — faster chaining, no waiting for perfect.

**Ultimate selection** (`TryAutoIceSkatingSelectUltimate*`): scans the skill tree, resolves each branch's ultimate via `GetSkateActionType(actionType).ultimateActionId`, scores it, and picks the **shortest ultimate whose final score ≥ the Ultimate-cost slider**. Energy gate:

- **Only x2 ultimate** on (default): an ultimate is cast only at energy tier ≥ x2 (≥ 200 energy).
- **Only x2 ultimate** off: tier x1 (≥ 100) is allowed.
- **Last 30s ultimate** on (default): when the challenge timer drops below 30 s, the gate falls to x1 regardless of the above — spend stored energy before it's wasted at time-up. The score floor (slider) still applies.

Ultimate scoring mirrors the game's `GameSkateMode.SettleActionEnergy` / `CalculateBaseScore`: `final = (score + bonus·if-new) × (1 + prefectScoreRatio) × speedRate × repeat-decay`, where `speedRate` is 0.5–2.0 from `CalculateSpeedRate()` (your real speed) and pair skates override score/bonus via `TablePairSkateUltimate`. Keep your speed high for the ×2 multiplier. The scan result is cached briefly (≈0.35 s) keyed by the skill-set hash.

**Controls** (Extras tab, under the network buttons):

| Control | Default | Meaning |
|---------|---------|---------|
| Auto Ice Skating (toggle) | off | Master enable. |
| Ultimate cost (min score) — slider | 900 | Minimum final score an ultimate must reach to be cast. Range 0–2000, step 50. |
| Only x2 ultimate (skip x1) | on | Require energy tier x2 for ultimates. |
| Last 30s ultimate | on | Allow x1 ultimate when challenge timer < 30 s. |
| Perfect move | on | Off → chain moves as soon as available, not waiting for the perfect window. |
| Prefer new move | on | Off → pick simple moves purely by shortest duration. |

All controls and the hotkey are **persisted** in the keybind config (`KeybindConfigData`); defaults come from field initializers, so configs predating this feature upgrade to on / 900.

**Debug logging.** `MasterLogAutoIceSkating` (top of `HeartopiaComplete.cs`, default `false`). When enabled, every trigger and every ultimate skip logs the full property dump of the action(s) — `id type dur score bonus new prefScore energy prefEnergy iconTip pair ult` — and selection logs list each candidate the same way, so you can see exactly how moves differ (duration, score, bonuses) and why an ultimate was skipped (`below-min` / `ok`).

**Crash-hardening.** `GameSkateMode.ChallengeInfo` is a **struct** (`ChallengeData`) read raw into a stack buffer via `mono_field_get_value`. `mono_field_get_offset` is boxed-relative (includes the 16-byte object header), so the Aura path subtracts `2 * IntPtr.Size` for `UsedActions` / `Timestamp` / `Duration`; a missing subtraction read `Timestamp` as a fake pointer and hard-crashed mono at challenge start. Pointers read from the raw buffer are also alignment/`>= 0x10000` checked before any `mono_object_get_class` (native AVs are uncatchable). See `memory/auramono-struct-field-offsets.md`.

---

## New Features — Extra (Analog Move)

**Tab:** New Features → Extra. **Source:** `MovementInputFeature.cs`.

### Analog Move (gamepad stick)

Drives the local character from an analog axis "as if from the joystick", without teleport/noclip.
Heartopia is mobile-first: movement runs through the new **Input System** `Move` action
(`InputEvent.Move == 0`), **not** legacy `Input.GetAxis`. A connected gamepad's stick is **not bound**
in the action map, so the native stick does nothing — and legacy `Input.GetAxis("Horizontal"/"Vertical")`
returns 0 under "Input System (New)". This bridge fixes that.

- **Source:** Xbox left stick read directly via **Win32 XInput** (`XInputGetState`, `xinput1_4.dll` →
  `xinput9_1_0.dll` fallback; left-thumb radial deadzone 7849/32767, normalized 0..1). WASD/arrows also
  drive it. Raw joystick space (x = right, y = forward) — the game applies the camera-relative transform.
- **Injection:** the resolved axis is fed into `LocalPlayerComponent.OnLeftJoystickPerformed(Vector2)`
  (deterministic — the player's own tick consumes its `_joystickQueue` → `SetMoveJoystick` → real
  velocity + server sync). Fallback: `MonoInputManager.SendMoveValueToControl(Vector2)`.
- **Merge / safety:** yields to the on-screen touch joystick; respects the mod's menu movement block
  (`ShouldBlockGameplayInput` / the game's `IsInputDisabled(Move)`); per-frame tick guarded by a
  `FeatureBreakerState`. Movement uses the genuine move component at legitimate speed, so it passes the
  server `MovementAntiCheating` checks and leaves no `InputCheatManager` touch trace.

Pipeline details: **[TECHNICAL.md § Analog movement bridge](./TECHNICAL.md)**; resolver facts in
`memory/analog-move-injection.md`.

---

## New Features — Extra (Carpet Stamp)

**Tab:** New Features → Extra. **Source:** `CarpetStampFeature.cs`.

Research tool for the party **stampede carpets** — Slippery Rug `260242` (`p_mechanism_party_acceleratecarpet_1`, +20% move speed) and Slime Rug `260243` (−20%), plus the start/end point rugs `260240`/`260241` (listed scan-only).

- **Scan Carpets** — walks the static `UGCWorld._uActors` dictionary (one `UActor` per UGC mechanism on the map) via AuraMono with GC pins, reads each actor's `StaticId` / `UgcType` / entity position, and lists everything with `UgcType.StampedeInteraction` (1002) or a known carpet staticId, sorted by distance. Every actor found is logged (`[CarpetStamp]`), carpet or not.
- **Step On** (row button / **Step On Nearest**) — sends a single self-reported step-on: `UgcOperateCommand { Type = actor UgcType, NetId = carpet, OperateMethod = (UgcOperateMethod)enterSkillId }` through the AuraMono-inflated `WebRequestUtility.SendCommand<UgcOperateCommand>` (Reliable). The server runs the skill's `UgcServerAction` itself — Slippery Rug: **AddBuff 1003** (+20% speed, no expiry until removed).
- **Step Off** — completes the cycle like a real exit: both `PlayerExit` skills in `ugcSkills` order (AddBuff 1005, +20% for 3 s linger, then RemoveBuff 1003).
- **Logging** — always on, no toggle: resolution pointers, dictionary walk per-actor lines (netId/staticId/ugcType/pos/dist), full command payloads, invoke results/exceptions.

Skill ids are per-staticId constants recovered from the decrypted `cn.bytes` tables (`Mechanism.ugcSkills` → `Ugcskill` → `UgcServerAction`/`BuffConfig`); the game-side pipeline this replays is `UGCTriggerCase → LocalPlayerComponent.TriggerEnter → PhysInteractionSystem → PhysEventSkill → Action_Command_UgcOperate`. Research: `/ugc-mechanism-carpet-interaction.md`.

---

## Research Tab

A practical panel for the Research Institute (research store is StoreId **142**). Opening the tab auto-prepares everything: it polls the server-sync instrument cache immediately, arms the level spoof, and force-spawns the institute's client entities (all silent, main-town only) — so the list and buttons are ready without any setup. System map: `.research-record/RESEARCH_STORE_REPORT.md`.

**Instruments** (live from the server-sync cache — works from any location):

| Element | What it does |
|---------|--------------|
| Per-analyzer row | `Analyzer N · Lv L` + status: **idle**, **researching &lt;item&gt; · Xh Ym** (countdown interpolated from the last poll), or **DONE · &lt;item&gt;**. Item names via `TableData.GetEntity`. |
| SELECT ITEM (per row) | Opens that analyzer's `ResearchInstrumentPanel` (the research-item picker). **Greyed out while the analyzer is busy** (still researching). Full start needs the client instrument — the tab force-spawns it on open, so it works from anywhere in the main town; a busy/idle instrument opens fully, netId is the real server netId. |
| Completion alerts | Background poll (5 s, from OnUpdate) fires a menu notification the moment a running research crosses its completeTime (`Analyzer N finished researching <item> — ready to collect`). One-shot per completion; always on. |

**Panel shortcuts:**

| Button | What it does |
|--------|--------------|
| RESEARCH STORE | Opens `ResearchShopPanel` via `UIManager.OpenView`. The level spoof (auto-armed on open) makes it render from any location. |
| CONTROL CONSOLE | Opens `ResearchControlPanel` (the platform panel) the same way. |

Behind the scenes (no UI, automatic): the level spoof NativeDetours `ResearchSystem.GetResearchLevel()`/`GetResearchExp()` to return the server-sync values when the client read is 0 (so panels don't NRE off the building); force-spawn sets `DynamicMapItemService.creatResearchStuff=true` + dispatches `ShowHideResearchStuffEvent{isShow=true}` to materialise the client entities with their real server netIds. Reads only — research upgrades stay disabled. Session-only, world-gated, AuraMono-gated.

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

Config persisted to `%LocalLow%/Bugtopia/Config.xml` (XML serialized `UnifiedConfigData`). Persisted values include keybinds (incl. **Water + Weed Radius**), theme, radar, patrols, bird farm, and the **Homeland Farm radius** (`homelandFarmWaterRadius`, clamped 1–80, default 30).

Separate legacy-compatible JSON fragments still loaded line-by-line for some keys in older migration path.

---

## Keybind Reference

All default to **KeyCode.None** except menu toggle. Grouped as in Settings → Keybinds:

| Section | Keybinds |
|---------|----------|
| CORE | Toggle Menu (**Insert**), Toggle Radar, Bypass UI, Disable All, Inspect Player, Inspect Move |
| AUTOMATION | Auto Foraging, Aura Farm, Water + Weed Radius, Auto Insect Farm, Auto Bird Farm, Fish Shadow Net, Mass Cook, Auto Puzzle, Auto Cat Play, Auto Dog Train, Auto Pet Wash, Feed All Cats, Feed All Dogs, Auto Snow Sculpture, Auto Sand Sculpture, Bird Vacuum, Spawn Bubble, Auto Repair, Auto Eat |
| PLAYER | Noclip, Camera Toggle, Auto Ice Skating, Join My Town, Anti AFK, Bypass Overlap |
| SPEED & TOOLS | Game Speed 1×/2×/5×/10×, Equip Axe / Net / Rod / Sprinkler / Bird Scanner / Pad, Pad Confirm / Cancel / Rotate / Move / Delete |

Rebind by clicking the button in Settings and pressing a new key. Mouse buttons are bindable too. Layout note: panel section heights are sized by row count (`BeginKeybindSection` rowCount) and the scroll height by `CalculateSettingsTabHeight` — both must be bumped when adding rows, or the new rows render outside the panel/scroll.

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
