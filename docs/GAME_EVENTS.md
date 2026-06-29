# Game Events (EventCenter) — Reference & Mod Integration

How Heartopia's in-game event bus works, how the mod can (and can't) plug into it,
and the full catalogue of event types. The flat list of all ~1450 events lives in
[GAME_EVENTS_LIST.md](GAME_EVENTS_LIST.md).

---

## 1. The bus: `XDTGame.Core.EventCenter`

Source: [`ilspy-dumps/XDTBaseService/XDTGame.Core/EventCenter.cs`](../ilspy-dumps/XDTBaseService/XDTGame.Core/EventCenter.cs).

A static, type-keyed publish/subscribe hub. Every event is a **`struct` that implements
`XDTGame.Core.IEvent`** (a marker interface, no members). Handlers are keyed by the event's
`System.Type`, so dispatch is O(1) on a `Dictionary<Type, ...>`.

```csharp
public static class EventCenter
{
    // Global listeners
    public static Delegate AddListener<T>(in Action<T> action)    where T : struct, IEvent;
    public static void     RemoveListener<T>(in Action<T> action) where T : struct, IEvent;
    public static void     DispatchEvent<T>(in T @event)          where T : struct, IEvent;

    // Per-entity listeners (keyed by netId, e.g. "this specific bird")
    public static void AddListener<T>(uint netId, in Action<T> action)    where T : struct, IEvent;
    public static void RemoveListener<T>(uint netId, in Action<T> action) where T : struct, IEvent;
    public static void DispatchEvent<T>(uint netId, in T @event)          where T : struct, IEvent;

    // Non-generic overloads (subscribe by Type + Delegate)
    public static void AddListener(Type eventType, in Delegate action);
    public static void RemoveListener(Type eventType, in Delegate action);
}
```

Internals worth knowing:
- Global handlers are stored in a `LinkedListExecutor` (a pooled singly-linked list per event
  type, newest-first via `AddHead`). Per-netId handlers live in `subExecutors[netId]`.
- `Dispatch` wraps each handler invocation in `try/catch` and logs via `DebugSystem.LogError`
  — **a throwing listener does not break the dispatch chain**, and the throw is swallowed
  into the game log, not surfaced to other systems.
- `DispatchEvent(netId, …)` is a **no-op if nobody registered for that netId** (no auto-create
  on dispatch — only `AddListener(netId, …)` creates the sub-executor).

### Canonical usage (from game code)

[`InstrumentModule.cs`](../ilspy-dumps/XDTGameUI/XDTGUI.Module.Instrument/InstrumentModule.cs)
is a textbook subscriber:

```csharp
// register
EventCenter.AddListener<InstrumentPanelOpenEvent>(new Action<InstrumentPanelOpenEvent>(OnInstrumentPanelOpen));
EventCenter.AddListener<InstrumentPanelCloseEvent>(new Action<InstrumentPanelCloseEvent>(OnInstrumentPanelClose));
// ... later, on teardown ...
EventCenter.RemoveListener<InstrumentPanelOpenEvent>(new Action<InstrumentPanelOpenEvent>(OnInstrumentPanelOpen));
```

---

## 2. Instrument open/close flow (concrete, end-to-end)

This is the chain behind the InstrumentHotkeyGuard feature, traced through the dumps:

1. The player starts/stops playing — [`PlayerInstrumentMotion.cs`](../ilspy-dumps/XDTLevelAndEntity/XDTLevelAndEntity.Gameplay.Locomotion/PlayerInstrumentMotion.cs)
   dispatches the **root** events:
   - line 250: `EventCenter.DispatchEvent<InstrumentPanelOpenEvent>(new InstrumentPanelOpenEvent { … })`
   - line 207: `EventCenter.DispatchEvent<InstrumentPanelCloseEvent>(default)`
2. [`InstrumentModule`](../ilspy-dumps/XDTGameUI/XDTGUI.Module.Instrument/InstrumentModule.cs)
   listens for those and re-broadcasts higher-level mode events:
   - `InstrumentPanelOpenEvent` → `InstrumentModeStartedEvent`
   - `InstrumentPanelCloseEvent` → `InstrumentModeEndedEvent`
3. [`InstrumentPanel`](../ilspy-dumps/XDTGameUI/XDTGame.UI.Panel/InstrumentPanel.cs) is the UI
   view; `OnStart` builds the keyboard / `OnStop` tears it down. Its fields `_instrumentType`
   (`InstrumentType` enum) and `_nowKeyOption` (`MusicKeyOption` enum) are read via AuraMono
   `GetView` only on the mod's **fallback** path; the primary path now reads the `InstrumentType`
   straight from the `InstrumentPanelOpenEvent` payload (§3).

### The four instrument events

| Event | Namespace | Payload | When |
|---|---|---|---|
| `InstrumentPanelOpenEvent` | `XDTDataAndProtocol.Events` | `InstrumentType InstrumentType; uint instrumentNetId; ulong instrumentLevelObjectNetId; int staticId;` | player begins playing |
| `InstrumentPanelCloseEvent` | `XDTDataAndProtocol.Events` | *(empty, Size=1)* | player stops playing |
| `InstrumentModeStartedEvent` | `XDTGameSystem.UI` | `InstrumentType instrumentType; int staticId; uint instrumentNetId; ulong instrumentLevelObjectNetId;` | re-broadcast of open |
| `InstrumentModeEndedEvent` | `XDTGameSystem.UI` | *(empty, Size=1)* | re-broadcast of close |

Enums (for reading payloads / panel fields):
- `InstrumentType` ([dump](../ilspy-dumps/EcsClient/XDT.Scene.Shared.Modules.Music/InstrumentType.cs)):
  `None=0, Piano=1, Conga=2, KaHongDrum=3, BaYinTong=4, EtherealDrum=5, Lute=11 … Saxophone=21`.
- `MusicKeyOption` ([dump](../ilspy-dumps/EcsClient/XDT.Scene.Shared.Modules.Music/MusicKeyOption.cs)):
  `KeyMode8=0, KeyMode15a=1, KeyMode15b=2, KeyMode22=3`.

---

## 3. How the mod hooks game events — the reusable engine ✅

The mod runs under BepInEx as a **separate .NET assembly**; the game's managed types
(`XDTGame.*`, the `IEvent` structs) are **not loadable** as compile-time references and are
absent from the mod's runtime (see `memory/homeland-farm-scan-perf.md`). The game executes in
its own embedded **AuraMono** runtime. That rules out simply calling
`EventCenter.AddListener<InstrumentPanelOpenEvent>(…)` in C#.

**Solution (implemented, proven in-world):** NativeDetour the inflated generic dispatcher
`EventCenter.DispatchEvent<T>(in T)` for the concrete event type and forward to the original via
a trampoline. We intercept at the dispatcher entry, so we see **every dispatch of that type**
regardless of who (if anyone) subscribed. This is wrapped in a reusable engine:
[`HeartopiaComplete.EventHook.cs`](../buddy/HeartopiaComplete.EventHook.cs).

### Public API

```csharp
// payloadBytes = the event struct's size from the dump (how many bytes to snapshot; 0 for empty
// events like *CloseEvent). The handler runs on the Unity main thread (OnUpdate drain), so it may
// allocate / log / call AuraMono freely. The native detour body only Marshal.Copy's the payload
// into a reused buffer and forwards — it never allocates, throws, or calls into Mono.
RegisterGameEventHook(string eventFullName, int payloadBytes, Action<GameEventSnapshot> handler);

// In the handler, read scalar fields by offset (string/object fields can NOT be read this way):
void OnSomeEvent(GameEventSnapshot e) {
    int   t   = e.ReadInt32(0);
    uint  net = e.ReadUInt32(4);
    ulong id  = e.ReadUInt64(8);
    float f   = e.ReadSingle(16);
}
```

Registration is idempotent per event name (re-registering adds another handler to the shared
detour). Install is lazy: the engine retries each frame from `ProcessGameEventHooksOnUpdate` until
AuraMono + `XDTGame.Core.EventCenter` + the event's image are loaded, then splices the detour.
`IsGameEventHookInstalled(name)` reports when a detour is live (for an event-flag-with-poll-fallback
pattern). Reference consumer: [`InstrumentHotkeyGuardFeature`](../buddy/InstrumentHotkeyGuardFeature.cs).

### How it works (mechanism)

1. Resolve `XDTGame.Core.EventCenter` via `FindAuraMonoClassByFullName`.
2. `FindAuraMonoMethodOnHierarchy(cls, "DispatchEvent", 1)` — paramcount **1** selects the global
   `DispatchEvent<T>(in T)` (the per-netId overload `DispatchEvent<T>(uint, in T)` has 2).
3. Inflate that open generic method per concrete event class (`mono_metadata_get_generic_inst` +
   `mono_class_inflate_generic_method`), validate `AuraMonoMethodParamCountIs(inflated, 1)`.
4. Resolve own `IntPtr mono_compile_method(IntPtr)` export (the engine's shared
   `auraMonoCompileMethod` is declared `void`, losing the code ptr); compile → native code pointer.
5. `MonoMod.RuntimeDetour.NativeDetour(nativePtr, body)` + `GenerateTrampoline` — the modern
   Iced-relocating hook (same proven path as fishing `NotifyFloatInWater`), **not** the abandoned
   14-byte `BubbleMonoNativeHook` steal (see §5).

**Key ABI fact (was the open risk, now confirmed safe):** for a value-type (non-shared) generic
instantiation mono emits dedicated code with **no hidden rgctx arg**, so `DispatchEvent<T>(in T)`'s
native signature is exactly `void(IntPtr eventPtr)` — `in T` is a raw pointer to the **bare** struct
(no mono object header; by-ref, not boxed). Read payload fields by offset off `eventPtr`. Verified
in-world: instrument open/close logged real payloads (`type=12`=Wbass, netId, staticId) with no crash.

### Detour-body rules (native boundary)

The 16 fixed static slot bodies (`EventSlotBody0..15`, no closures) must **not** throw, allocate, or
call into Mono/Il2Cpp/Unity. Each only `Marshal.Copy`s `payloadBytes` into a preallocated ring
buffer and forwards via the slot's trampoline. The main-thread drain (`DrainGameEventHooks`) decodes
the snapshot and invokes handlers, where any work is allowed. Producer (body) and consumer (drain)
both run on the Unity main thread, so the ring is single-threaded in practice.

### Scope limit — do NOT mass-hook all ~1450 events

Each event type needs its own inflated `DispatchEvent<T>` + detour. Hooking hundreds at once is a
process-crash risk: per-type gsharedvt ABI roulette (some `T` may compile to shared code with a
hidden arg → signature mismatch → AV), mass JIT of every instantiation, and high-frequency bodies on
per-frame events. The engine caps at `MaxEventHookSlots` (16) concurrent hooks for this reason. Hook
the specific events a feature needs; use `MasterLogGameEvents` to discover/verify payloads first.

### Verified reusable events & payload layouts

Offsets below are by-offset reads for the engine (scalars only; ref fields noted). Verify live with
`MasterLogGameEvents` after a game patch — mono value-type layout is assumed natural/sequential.

#### "Object appeared / disappeared on the map" — the common channel

`EntityCreateEvent` / `EntityRemoveEvent` (namespace `XDTLevelAndEntity.BaseSystem.EntitiesManager`)
fire for **every** ECS entity as it streams/spawns in or out. Dispatched via
`EventCenter.DispatchEvent(in @event)` from
[`EntityManager`](../ilspy-dumps/XDTLevelAndEntity/ScriptsRefactory.LevelAndEntity.BaseSystem/EntityManager.cs)
(lines ~468 / ~492), so the engine hooks them. This is the generic "object on map" channel for
radar / aura-farm targets / cookers / gift boxes / insects / pets — subscribe once, read `netId`,
then qualify the entity by component/archetype in the handler (main thread).

Both carry `public EntityData Value` — a **readonly struct** (`ScriptsRefactory.DataAndProtocol.ComponentsData.EntityData`),
inlined into the event, so its fields are readable by offset. `payloadBytes = 32`:

| field | type | offset |
|---|---|---|
| `level` | `EGameLevel` (sbyte) | 0 |
| `entityId` | uint | 4 |
| **`netId`** | **uint** | **8** ← key |
| `field` | uint | 12 |
| `tag` | `EntityTag` (ulong) | 16 |
| `archetypeId` | short bucket@24 / short index@26 | 24 |
| `_priority` | int | 28 |

Note: high-frequency in dense towns — only process while a feature needs it and keep the per-event
qualify check light. There is **no** dedicated `DataCreated<CookBuildComponent>` ("cooker appeared")
event; cookers are detected by filtering `EntityCreateEvent` netIds for `CookBuildComponent`.

#### Other verified payloads

| Event | Namespace | `payloadBytes` | Fields (offset) | Notes |
|---|---|---|---|---|
| `InstrumentPanelOpenEvent` | `XDTDataAndProtocol.Events` | 24 | `InstrumentType`(int)@0, `instrumentNetId`(uint)@4, `instrumentLevelObjectNetId`(ulong)@8, `staticId`(int)@16 | hooked by InstrumentHotkeyGuard |
| `InstrumentPanelCloseEvent` | `XDTDataAndProtocol.Events` | 0 | *(empty)* | dispatch-only signal |
| `CollectObjectShowEvent` | `ScriptsRefactory.DataAndProtocol.Events` | 8 | `netId`(uint)@0, `show`(bool)@4 | **fully scalar** — collectable show/hide |
| `BottomDialogEvent` | `ScriptsRefactory.DataAndProtocol.Events` | 12 | `message`(string ref)@0 **unreadable**, `active`(bool)@8 | read `active` only; `clickCallback`(Action ref) is the real confirm action but ref-unreadable |
| `UIPanelOpenEvent` / `UIPanelCloseEvent` | `XDTGame.Framework.UI` | — | `panelType`(System.Type ref)@0 **unreadable** | universal panel open/close, but needs a mono-`Type*`→name resolver to be useful |

#### Per-component events (`DataCreated<T>` / `DataRemoved<T>`)

`ScriptsRefactory.DataAndProtocol.Events.DataCreated<T>` (`struct DataCreated<T> { T Value; }`) is
dispatched **per component type** by the various `ClientSystem.*SyncSystem`s
(`DataCreated<BlockComponent>`, `DataCreated<PetEntityData>`, …) via
`EventCenter.DispatchEvent<DataCreated<TComp>>`. More precise than `EntityCreateEvent`, **but** it is
a nested generic — to hook it you must build the `DataCreated<TComp>` instantiation via
`mono_class_bind_generic_parameters` (`auraMonoClassBindGenericParameters`) rather than resolving by
name. Deferred until a feature needs the precision.

---

## 4. Event catalogue (by namespace)

~1450 event structs total. The big buckets:

| Namespace | Count | What's in it |
|---|---|---|
| `XDTDataAndProtocol.Events` | 810 | server/protocol-driven gameplay events (the bulk) |
| `XDTGameSystem.UI` | 316 | UI mode/panel focus + system UI events |
| `ScriptsRefactory.DataAndProtocol.Events` | 170 | older protocol-layer events (birds, pets, animation, …) |
| `XDTGame.UI` | 31 | UI bridge events (game-mode focus, blueprint, etc.) |
| `XDTDataAndProtocol.Events.GameSetting` | 15 | settings-changed notifications |
| `XDTDataAndProtocol.Events.Player` | 14 | player-state events |
| *(others)* | ~96 | build/competition, party, energy, homeland, navigation, … |

Full enumerated list grouped by namespace: **[GAME_EVENTS_LIST.md](GAME_EVENTS_LIST.md)**.

### Regenerating the list

Run from the repo root (Git Bash / WSL):

```bash
grep -rl ": IEvent" ilspy-dumps/ | while read f; do
  ns=$(grep -m1 "^namespace " "$f" | sed 's/^namespace //; s/;.*//')
  grep -E "struct [A-Za-z0-9_]+ : .*\bIEvent\b" "$f" \
    | sed -E 's/.*struct ([A-Za-z0-9_]+).*/\1/' \
    | while read s; do echo "${ns:-(global)}.$s"; done
done | sort -u > events.txt
```

---

## 5. InstrumentHotkeyGuard — history & final design

**Original bug (historical).** Mod hotkeys still fired while playing (`SetHandhold toolId=1/3/5` =
Axe/Rod/Net equips) because the guard only blocked keys in the instrument's **note layout**, and
(a) `pianoSemitone` couldn't be read via managed reflection on the absent `GameSettingSystem` so the
KeyMode22 piano layout was wrong, and (b) any hotkey bound *outside* the layout (the equip keys) was
never in the blocking set. Fixed by **blocking all mod hotkeys except the menu toggle while an
instrument is open**, and by driving "is open" from events instead of a per-frame poll — see the
event-driven implementation below.

### Implemented (final) — event-driven

`InstrumentHotkeyGuardFeature` now drives the "instrument open" state from the event engine (§3):
it `RegisterGameEventHook`s `InstrumentPanelOpenEvent` (→ flag true, captures `InstrumentType`) and
`InstrumentPanelCloseEvent` (→ flag false). `IsInstrumentPanelOpen()` reads the flag once the detour
is installed (zero per-frame cost, no native-AV exposure); while a hook hasn't installed yet it falls
back to the legacy throttled `GetView` poll. While the panel is open it **blocks every mod hotkey
except the menu toggle** — this also fixes the original bug where hotkeys bound outside the note
layout (e.g. the Axe/Rod/Net equip keys) still fired while playing. The per-key layout matching and
`pianoSemitone` (`PlayerPrefs.GetInt("PianoSemitone", 0)`) logic now only feed the fallback path.

### Native detour — the working mechanism vs the abandoned one

- ✅ **NativeDetour on inflated `DispatchEvent<T>`** (the engine in §3) — installed once per event,
  forwards via a trampoline, no per-frame cost. Proven, no crash.
- ❌ **`BubbleMonoNativeHook` on `InstrumentPanel.OnStart`/`OnStop`** — earlier attempt; **crashed**
  on first open. Root cause: that hook steals a **fixed 14 bytes** of prologue and cannot relocate
  RIP-relative instructions (`CreateBubble`'s prologue happened to be relocatable, `OnStart`/`OnStop`'s
  was not). The engine avoids this by using MonoMod's Iced-relocating `NativeDetour`. (Also note
  `mono_method_get_unmanaged_thunk` is a native-call wrapper that normal managed/vtable calls never
  traverse — intercept the `mono_compile_method` code pointer instead, which the engine does.)

### Other event routes (not pursued)

- **EventCenter `AddListener<T>` subscription via AuraMono** (true listener registration): would need
  to construct a managed `Action<EventStruct>` delegate around a native callback and pass it to the
  non-generic `AddListener(Type, Delegate)` overload — and dispatch does `node.data as Action<T>`, so
  any delegate that isn't exactly `Action<EventStruct>` is silently skipped. Strictly more moving
  parts than the dispatcher detour for the same outcome. Not pursued.
- **mono vtable slot swap**: safer than byte-patching but needs vtable internals not currently
  exported.
