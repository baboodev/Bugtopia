# Vehicles: catalogue, spawning, boarding, the dragon boat

Everything the mod knows about the game's vehicles. The source is a reading of `ilspy-dumps` plus live
checks; anything doubtful is marked as such, and anything unverified is called unverified.

---

## 1. Catalogue and ownership

**`TableCar`** (`ilspy-dumps/EcsClient/TableCar.cs`) — one row per vehicle, roughly 350 entries. The
key fields are `id` (matching the item's `staticId`), the physics (`runAcceleration`,
`runForwardMaxSpeed`, `minTurnSpeed`, `turnTime`), `loadNum` (seats), `carBox[3]` (the bounding box
used to find room to spawn), `refitInfo`, `drivingMode` and `normalPrefabId`.

The dictionary is `TableData.TableCars`, accessed through `TableData.GetCar(int id)`. It is loaded
from `ExcelTableType.Car`. This is "every car in the game".

**The player's vehicles** are backpack items in `EStorageType.Garage` (value 3), category
`EntityType.car` (80–88, `EntityTypeUtil.IsGarageItem`). The item's `staticId` equals `TableCar.id`.
They are read through `DataModule<BackPackSystem>.Instance.GetAllItem(EStorageType.Garage)` — the same
store the stock vehicle panel shows.

---

## 2. Spawning: client → server

```
VehiclePanel._GetOnVehicle → VehicleManager.GetOnVehicle(staticId)
    → CallVehicleEvent{birthPointNetId=0, staticId, getOnVehicle}   (local)
    → VehicleManager.CreateDrivingVehicle(itemId, getOn)
```

Step by step, inside:

1. `TableData.GetCar(itemId)` — a null check;
2. a position in front of the player: `VehicleUtility.CreateVehiclePosition(pos, rot, carBox, out vehiclePos, radius)`;
3. the forbidden area: `IMetaAreaService.IsPosForbiddenForVehicle(vehiclePos)` — **a pure table
   lookup**, `TableData.ForbiddenVehicleArea.Contains(areaId)`, unrelated to water;
4. `GetVehicleLevleObjectId(itemId, seat0, out levelObjectId)` — through
   `IConfigManager.GetEntityScriptData(itemId, Feature.putitem)` by the name `sit_point_1`;
5. **`VehicleProtocolManager.PlayerCallVehicle(itemId, vehiclePos, yAxis, levelObjectId, getOnVehicle)`**.

The last one simply sends a command:

```csharp
CallVehicleCommand { int StaticId; Vector3 Position; float YAxis; bool IsAutoGetOnDriveSeat; int LevelObjectId; }
```

`XDT.Scene.Shared.Modules.Vehicle`, `[NetworkCommand]`, image `EcsClient`.

**The server checks ownership**: `VehicleErrorCode.VehicleNotHave / CarUnAvailable / AreaForbid /
StaminaNotEnough`. So an ordinary `CallVehicleCommand` cannot summon somebody else's vehicle.

`VehicleProtocolManager` lives in `XDTDataAndProtocol` and is absent from interop — AuraMono only.

### The server's reply is asymmetric

`VehicleProtocolManager.ServerCallVehicleResult` dispatches:

| Outcome | What arrives |
|---|---|
| Success | ⚠️ **nothing** — checked three times live: the vehicle appeared and `UpdateCurrentVehicle` never came once |
| A typed refusal | `UITipEvent{tipId=10139}` — fast and reliable, under 100 ms |
| `AreaForbid` | ⚠️ **nothing** — the `switch` has an empty `case AreaForbid: break;` |

It reads as a deliberate "silence means success" design. So **success is confirmed by actively scanning
the world** rather than by waiting for an event: look for a live `VehicleComponent` with the right
`staticId` and our own owner. The hook on `UpdateCurrentVehicle` is kept as a harmless fast path in
case the server's behaviour ever changes.

> ⚠️ `UITipEvent.backupString` is a managed string. **Do not read it** from an event snapshot: the
> handler is invoked later, out of a ring buffer, and by then the string may already have been
> collected. Only `tipId`@0 is safe.

### Underwater the spawn is refused silently

Confirmed twice: no vehicle found after the timeout, and no refusal type either. This matches
`VehicleErrorCode.AreaForbid`. There is no separate underwater scene (there is one level,
`GameLevel_Main`, and depth is an area inside it), and `VehicleUtility.DrivingMode` knows only
`{Default, DragonBoat}` — an underwater mode does not exist at all.

---

## 3. Boarding someone else's vehicle — there is no ownership check

`VehicleProtocolManager.GetOnVehicle(vehicleNetId, seat, levelObjectId)` is **a different path from
summoning**, and it does not check the owner. Neither `VehicleComponent` nor `VehicleMainInteract`
checks one; only seat occupancy is checked (`HavePassengerInSeatIndex`). Confirmed by a live call that
worked first time.

### ⚠️ "Spawning stopped working" is physics, not broken state

`VehicleUtility.CreateVehiclePosition` — the find-room step every summon goes through — is a local
`Physics.OverlapBox`/`Raycast` directly in front of the player on the vehicle layer, and
`IsVehicleCollider` matches **any** `VehicleComponent`, whoever it belongs to.

Getting out of a vehicle **does not remove it from the world** — it stays physically parked. If the
player is standing next to it, that collider fails every subsequent summon with
`VehicleTipsEnum.HaveVehicle` / `NotEnoughSpace`, including summoning their own car from the stock
menu. It looks like "spawning is broken" and is cured by walking a few metres away or by recalling the
vehicle that is in the way
(`VehicleProtocolManager.ReCallVehicle(staticId, Vector3.zero, 0f, VehicleReCallCommandType.Destroy)`).
Nothing in the account's state is damaged by any of this.

---

## 4. The dragon boat

The "dry-land dragon boat" is **not a separate water mini-game**. The strings "旱地"/"dryland" do not
appear in the dumps at all. It is an ordinary land `VehicleComponent` with
`VehicleDrivingMode == VehicleUtility.DrivingMode.DragonBoat`, summoned, parked and driven like any
other vehicle during the festival. The two roles are simply seats:

- **The helmsman** (seat 0, `SelfVehicleController`): continuous left-right paddling through
  `dragonctrl_l_hold`/`dragonctrl_r_hold` → `IMonoInputManager.SendMoveValueToControl` →
  `VehicleComponent.HandleVirtualInput`, with the settings in `DragonBoatSystemConfig`; it spends
  stamina through `VehicleProtocolManager.SendVehicleInputCommand`.
- **The drummer passenger** (seat >0, `RemoteSelfVehicleController`): runs a QTE that restores the
  helmsman's stamina. ⚠️ It **also** calls `VehicleManager.SetSelfEntityVehicle`, so
  `GetSelfEntityVehicle()` works for the helmsman and the passenger alike.

### The QTE state machine

The states are `VehicleQTEStatus { None=0, DragonBoatIdle=1, DragonBoatDrum=2, DragonBoatSmite=3,
DragonBoatRelief=4 }`, owned by the server through
`VehicleQTEComponent{Status, CurrentStatusStartTimeMs, CurrentStatusDurationMs}`.

The cycle: **Idle** (the prompt is visible) → tap → **Drum** (charging) → **Smite** (the hit window,
its length in `CurrentStatusDurationMs`) → tap → **Relief** → Idle again.

One command serves both taps:
`VehicleProtocolManager.InteractVehicleQTEState(uint vehicleNetId)` →
`InteractVehicleQTECommand{ uint VehicleNetId }`. In the UI both presses hang off the same handler,
`VehicleBoatWidget.OnInteractBtnClick`.

The events (prefer the first):

| Event | Scope | Size | Fields |
|---|---|---|---|
| `ScriptsRefactory.DataAndProtocol.Events.PlayerVehicleQTEEvent` | **global** | 24 | `vehicleNetId(uint)@0, Status(int)@4, StartTimeMs(long)@8, DurationMs(long)@16` |
| `XDTDataAndProtocol.Events.VehicleQTEEvent` | per netId | 16 | `Status(int)@0, StartTimeMs(long)@8` — no duration |
| `VehicleQTEEffectUIEvent` | global | — | `SourcePlayerNetId@0, TargetPlayerNetId@4`, cosmetic |

⚠️ `PlayerVehicleQTEEvent` is dispatched for **every** boat in the vicinity — filtering by our own
`vehicleNetId` is mandatory, and the stock widget does exactly that. Also, `GameEventSnapshot` has no
`ReadInt64` — read it as `(long)e.ReadUInt64(offset)`.

Our own `vehicleNetId`: `VehicleManager.Instance.GetSelfEntityVehicle()` → `entity` → `netId` (there
is a ready example in `VehicleTeleportFeature.cs`).

### The race: checkpoints are ordinary quests

⚠️ **Do not confuse this with `VehicleCheckPointComponent`** — that belongs to a different mini-game
(the "Radio Help Event", `VehicleRadioComponent`, `GMSearchRadioHelpEvent`).

Race points are `GameTask`s: `GameTaskType.DragonboatRaceTarget`,
`SubmitTaskType.DragonboatRaceTarget = 1000563`, marker `TrackType.DragonboatRaceVehicle = 22`. The
chain-to-task binding lives in table data (`TableGameTask`) that is not in the dumps.

Submission is entirely generic: `TaskProtocolManager.ClientSubmitTaskItem(...)` →
`SubmitGameTaskItem2WorldObjectCommand`. The command has **no position field**, and `GameTaskErrorCode`
has no code about distance.

**But a live check showed that a bare submit does not bypass a checkpoint.** During a real race the
tracked task was in state `Accepted(3)` rather than `CanSubmit(4)`; the call went through mechanically
and the game answered "Unable to complete Quest". The state gate is real and server-side.

`TaskProtocolManager.GmFinishTask` exists, but is almost certainly closed by a server-side GM check
like every other `Gm*` command — see the memory about the GM-mode dead end.

---

## 5. What of this the mod already has

| Capability | Where |
|---|---|
| Summoning around the stock path | `VehicleBypassFeature.TryVehicleBypassForceSummon(itemId, getOn, out error)` |
| The catalogue plus a "mine only" filter | `SpawnVehicleFeature.cs` |
| Scanning live vehicles in the world, with the owner's name | `SpawnVehicleFeature.TryScanLiveVehicles` |
| Spoofing a vehicle's position | `VehicleTeleportFeature.cs` |
| Vehicle context for noclip | `NoclipFeature.EnsureNoclipVehicleAuraMono` |

The owner's name comes from `DataCenter.TryGetComponentData<LevelEntityComponentData>().ownerId` —
`VehicleComponent` itself has no owner field — and then through the same name-resolution chain the map
uses.

---

## 6. The general rule learned here

⚠️ **An event hook has to be registered noticeably earlier than the action whose result it catches.**
`RegisterGameEventHook` only adds a row to a table; the detour itself is attached **lazily**, over the
next several `OnUpdate` frames (resolve the class → inflate `DispatchEvent<T>` → `mono_compile_method`
→ `NativeDetour`). A fast server reply arrives before the detour is in place, so the very first press
of a session is guaranteed to lose its result.

Register unconditionally from `OnUpdate`, not in the same call that sends the command.
