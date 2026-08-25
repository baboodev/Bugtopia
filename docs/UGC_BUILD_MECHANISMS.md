# UGC build mechanisms — a reference built from the decompilations

This document describes the **UGC behaviour of placeable objects** (homeland and sandbox mechanisms): switches, pressure pads, text displays, timers and so on.

**Not to be confused with:**
- [UGC_SHOP.md](./UGC_SHOP.md) — buying UGC content (books, records)
- [UGC_SYSTEM.md](./UGC_SYSTEM.md) — player-made content (recordings, books, `PlayerUgcType`)

---

## 1. Three meanings of "UGC" in the code

| Context | Namespace / types | Example |
|----------|------------------|--------|
| **UGC content** | `XDT.Scene.Shared.Modules.Ugc`, `PlayerUgcType` | a book, a music record |
| **The UGC shop** | `UgcItemStore`, `BuyUgcItemCommand` | `ShopPanel` storeId 147/148 |
| **A UGC build mechanism** | `XDT.Scene.Shared.Modules.Build`, `UgcType`, `UGCViewComponent` | Slippery Rug / PressurePad, switches, trampolines |

The Slippery Rug (the pressure pad) belongs to the **third** category: `UgcType.Springboard` plus `UgcFunctionSpringboard`.

---

## 2. The protocol core

### 2.1 `UGCProtocolManager`

`XDTDataAndProtocol.UGCProtocolManager` is the single point for sending UGC commands:

```csharp
public static void DoCommand(in UgcOperateCommand command)
{
    WebRequestUtility.SendCommand(command);
}
```

Also: `UploadScoreBoardCommand`, `CheckUgcItem`, `CanPackUgcItem`, `SyncComponent<T>` (the build batch).

### 2.2 `UgcOperateCommand` (`[NetworkCommand]`)

```csharp
public UgcType Type;
public uint NetId;              // the netId of the mechanism's ECS entity
public UgcOperateMethod OperateMethod;
public List<long> Params;       // max 10
```

### 2.3 `UgcOperateEvent` (`[NetworkEvent]`)

The server's reply: `ErrorCode`, `Type`, `NetId`, `OperateMethod`, `OperatorNetId`, `Params`.

### 2.4 The chain on the client

```
Client: UGCProtocolManager.DoCommand(UgcOperateCommand)
    → server
    → UgcOperateEvent
    → HomelandSyncSystem.OnUGCOperateEvent
        → ErrorCode.Success → EventCenter.DispatchEvent<UgcEvent>(netId, …)
        → otherwise → UgcOperateErrorEvent
```

`UgcType >= ClientOnly` (1000+): the server forces `ErrorCode.Success` — these are purely client-side mechanisms.

### 2.5 Timeline and UGC skills

The generic path out of timeline clips:

```csharp
// Action_Command_UgcOperate.Begin
UgcOperateCommand {
    Type = target.UgcType,
    NetId = target.NetId,
    OperateMethod = (UgcOperateMethod)context.ability.skillId
};
UGCProtocolManager.DoCommand(in command);
```

`UgcFunction_Player.SendUgcOperateCommand(targetNetId, type, skillId)` — the same pattern (skillId is cast to `UgcOperateMethod`).

---

## 3. `UgcType` — the full enum

Source: `EcsClient/XDT.Scene.Shared.Modules.Build/UgcType.cs`

| Value | Name | Comment (from the attributes) |
|----------|-----|----------------------------|
| 0 | `Player` | the player |
| 1 | `RandomGenerator` | a random generator |
| 2 | `Translate` | a chess piece / movement |
| 3 | `OperateSwitch` | a switch |
| 4 | `ResourceHarvester` | a "till" / resource collection |
| 5 | `TextDisplay` | a text display |
| 6 | `CustomRandomBox` | a custom random box |
| 7 | `OperateState` | OperateState |
| 9 | `InteractionBuff` | assigning a buff |
| 10 | `SwitchRenderState` | showing a state |
| 11 | `Shoot` | shooting |
| 12 | `CollideBroken` | breaking on collision |
| 13 | `TimerBroken` | timed breaking |
| 14 | `TimerBounce` | a timed bounce |
| 15 | `Springboard` | **the pressure pad** |
| 16 | `CollideSwitch` | a collision switch |
| 17 | `SelfDefinedSwitch` | a custom timer switch |
| 18 | `Ignite` | igniting |
| 19 | `AutoStateChange` | automatic state change |
| 20 | `Appearance` | appearance disguise |
| 21 | `Brush` | painting |
| 22 | `Drop` | dropping |
| 1000 | `ClientOnly` | client only |
| 1001 | `Clock` | a clock |
| 1002 | `StampedeInteraction` | a stepping interaction |
| 1003 | `Timer` | a timer |
| 1004 | `DoorInteraction` | a portal |
| 1005 | `JumpBed` | a trampoline |
| 1006 | `CurveLanding` | a landing curve |
| 1007 | `CollideBounce` | a bounce on collision |
| 1008 | `RealTimerBounce` | a real timed bounce |
| 1009 | `Hang` | hanging |
| 1010 | `InstantiatedClock` | an instanced clock |

The type is bound to a prefab through `UGCComponentData.FuncType` / `TableMechanisms`.

---

## 4. `UgcOperateMethod` — the operations enum

Source: `UgcOperateMethod.cs`

| Value | Name | Typical use |
|----------|-----|---------------------|
| 0 | `Default` | the default |
| 1 | `Interaction` | a general interaction |
| 2 | `Push` | push |
| 3 | `Pull` | pull |
| 4 | `Throw` | throw |
| 5 | `Turn` | turn |
| 6 | `Switch` | toggle |
| 7 | `PutCoin` | insert a coin |
| 8 | `TakeCoin` | take a coin |
| 9 | `AddBuff` | add a buff |
| 10 | `RemoveBuff` | remove a buff |
| 11 | `EnterCollide` | entering a collision |
| 12 | `LeaveCollide` | leaving a collision |
| 13 | `SwitchOpen` | open |
| 14 | `SwitchClose` | close |
| 15 | `EnterStartPoint` | the start point |
| 16 | `EnterEndPoint` | the finish point |
| 17 | `StampedeDisappear` | disappearing (stampede) |
| 18 | `UseSnowmanAppearance` | the snowman appearance |
| 19 | `CopyAppearance` | copy an appearance |
| 20 | `GetOnAppearance` | put on an appearance |
| `0x1DCEEBCD` | **`PressurePadOpen`** | **the pressure pad** |
| `0x1DCEEBD9` | **`PartyEndPoint`** | the finish in party mode |

The concrete `OperateMethod` for a mechanism is set in `TableUgcAction_*` or by the timeline skillId.

---

## 5. The implemented `UgcFunction*` classes (C# in the dumps)

| Class | UgcType / UgcClass | File |
|-------|-------------------|------|
| `UgcFunctionSpringboard` | `弹板` (Springboard) | PressurePad, colliders, timeline |
| `UgcFunction_OperateSwitch` | `OperateSwitch` | recording, metronome, `UGCOperateSwitchUpdateEvent` |
| `UgcFunction_TimerBounce` | `TimerBounce` | a timed bounce, colliders |
| `UgcFunction_RealTimerBounce` | `RealTimerBounce` | |
| `UgcFunction_RandomGenerator` | `RandomGenerator` | |
| `UgcFunction_SwitchRenderState` | `SwitchRenderState` | |
| `UgcFunction_InstantiatedClock` | `InstantiatedClock` | |
| `UgcFunction_ColorLerp` | `Brush` | painting |
| `UgcFunction_Player` | `Player` | teleport, bounce, `SendUgcOperateCommand` |
| `UgcFunctionResourceHarvester` | `资源采集器` | a resource counter |
| `UgcFunctionTextDisplay` | `文字显示器` | a text display |
| `UgcFunctionCustomRandomBox` | | a random box |
| `UgcFunctionSelfDefinedSwitch` | | a custom switch |
| `UgcFunctionCollideSwitch` | | a collision switch |
| `UgcFunctionSwitch` | | |
| `UgcFunctionFiniteState` | | a finite state machine |
| `UgcFunctionGeneric` | `通用功能` | a stub |
| `UgcFunction_Default` | `ClientOnly` | client only |

When `UgcStateComponent` updates, `HomelandSyncSystem` maps state → component data for:
- `ResourceHarvester` → `UgcResourceHarvester`
- `Springboard` → `UgcPressurePad`
- `SelfDefinedSwitch` → `UgcSelfDefinedSwitch`
- `CollideSwitch` → `UgcCollideSwitch`

---

## 6. PressurePad / Slippery Rug (`UgcType.Springboard`)

### 6.1 The object's identity

| Field | Value | Note |
|------|----------|------------|
| Shop `itemId=150043` | a `TableStoreGroup` row | the shop catalogue |
| `rewardType=2` | `RewardType.Item` | |
| `rewards='2:260242x1'` | 1× `TableEntity` staticId **260242** | |
| The placed object | a UGC build with the **PressurePad** mechanism | `UgcClass("弹板")` |

**Caveat:** the strings `Slippery Rug` and `260242` are not hardcoded in the C#. The staticId → PressurePad link is confirmed at runtime:
- `TableData.TableMechanisms[staticId].ugcSkills`
- `TableData.TableUgcAction_Script_PressurePads`
- on the entity: `UgcPressurePadComponent` `[Persistent("ugPrPa")]`

### 6.2 The ECS data

**Client-side view data** — `UgcPressurePad`:

```csharp
bool isTriggeredBySelf;
bool isOn;
byte strength;
```

**Server-side persistent data** — `UgcPressurePadComponent`:

```csharp
bool isOn;
byte strength;
```

### 6.3 Runtime pipeline — ON ENTER

```
[the player's foot → the Trigger collider level object]
    ↓ UGC timeline clip
Action_Script_PressurePad.Begin()
    → PressurePadComponent.OnPlayerTriggered(localPlayer)
        ├─ PressurePadStatus.PressurePadNetId = padNetId
        ├─ PressurePadStatus.Triggered = isTriggeredBySelf
        └─ if isTriggeredBySelf → SwitchEvent()
                → UgcOperateCommand { Springboard, NetId, PressurePadOpen }
    ↓ dirty status
PlayerSyncStatus → PressurePadStatus_Field_0/1.OnSend
    → Entities.SyncSystem.Send ► SERVER
    ↓ locally
TransitionFree2Launched ← PlayerStateLaunched.IsStateSatisfy()
    → PlayerState.Launched (sliding / launch)
```

**Conditions for `OnPlayerTriggered`:**
- `currentState.CanJump` (or a null state)
- `IsTriggerState` (`isOn == true`)

**The two `isTriggeredBySelf` modes:**

| Mode | `Triggered` on stepping | `UgcOperateCommand` | Launch |
|-------|---------------------|---------------------|--------|
| `true` | `true` | yes (`SwitchEvent`) | immediately, through `IsStateSatisfy` |
| `false` | `false` | not on stepping | after `UgcEvent(PressurePadOpen)` → `OnUGCEvent` scans the trigger and sets `Triggered=true` |

**The F interact (4012):** `PressurePadTriggerCommand` → approach plus timeline → `UgcOperateCommand(PressurePadOpen)` with no actual step.

### 6.4 Runtime pipeline — ON EXIT

```
PlayerStateLaunched.Reset()
    → PressurePadStatus.PressurePadNetId = 0
    → SyncCommand ► SERVER
```

`Reset()` does **not** clear `Triggered` — only `PressurePadNetId`. A full reset happens through `PressurePadStatus.Reset()` on respawn (`PlayerSyncStatus`).

### 6.5 Synchronising the player's status

`PressurePadStatus` — `[NetId(39, 300)]`, dirty-tracked:

| Field | Sync field | Description |
|------|------------|----------|
| `PressurePadNetId` | `PressurePadStatus_Field_0` | the pad's netId |
| `Triggered` | `PressurePadStatus_Field_1` | the trigger flag |

Sending: `PressurePadStatus_Field_0.OnSend` / `_Field_1.OnSend` → `Entities.SyncSystem.Send`.

### 6.6 The local launch physics

`PlayerStateLaunched.StartJump()`:
- reads `PressurePadComponent.Direction` (the rotation from the entity's angle)
- `moveComponent.SetMoveSpeed(StrengthX, StrengthY)` from `PressurePadConfig.ElasticConfigs[strength]`

The physics sends **no** commands of its own — it merely replays the synchronised status.

### 6.7 `OnUGCEvent` — launching bystanders

After the server's `PressurePadOpen` every client receives a `UgcEvent`. `PressurePadComponent.OnUGCEvent`:
- checks `OperateMethod == PressurePadOpen`
- for a local player inside the trigger bounds: sets `PressurePadNetId` and `Triggered=true`

### 6.8 Colliders and the timeline

`UgcFunctionSpringboard`:
- `UpdateColliderState()` toggles `Collider_anim_on` / `Collider_anim_off` from `isOn`
- timeline: `ugcview_sbulletboard_on/off` or `ugcview_pbulletboard_on/off` (if skill `500100045` is in `TableMechanisms`)

### 6.9 The authority model

| Aspect | Who is authoritative |
|--------|-----------------|
| the pad's `isOn` | **the server** (through `UgcOperateCommand`) |
| "a player is on the pad" | **the owning client** publishes `PressurePadStatus` → the server replicates it |
| the sliding physics | **the local client** (`PlayerStateLaunched`) |

The server does **not** detect the foot's position itself — the client declares the trigger through the status sync.

### 6.10 Emulating this from the mod

**Toggle the pad (without touching it):**

```csharp
WebRequestUtility.SendCommand(new UgcOperateCommand {
    Type = UgcType.Springboard,
    NetId = padNetId,
    OperateMethod = UgcOperateMethod.PressurePadOpen
});
```

**Emulating "a foot on the pad" plus the launch:**

```csharp
localPlayer.Status.PressurePadStatus.PressurePadNetId = padNetId;
localPlayer.Status.PressurePadStatus.Triggered = true;
// dirty → an automatic sync; TransitionFree2Launched picks it up
```

A live `padNetId` and `isOn=true` are needed for the stock `OnPlayerTriggered`.

---

## 7. Other known UGC interactions

### 7.1 `OperateSwitch` — the switch (recording, metronome)

**Type:** `UgcType.OperateSwitch` (3)

**The known call** (stopping a recording on a phonograph):

```csharp
UgcOperateCommand {
    Type = UgcType.OperateSwitch,
    NetId = entity.netId,
    OperateMethod = (UgcOperateMethod)100200010u  // stop record
};
```

Source: `AudioRecordProtocolManager.DoStopCommand`.

**Behaviour:** `UgcFunction_OperateSwitch` — `IsOn`, timeline on/off, links to `AudioRecordComponent` and `AudioJiePaiComponent` (the metronome), and the events `UGCOperateSwitchUpdateEvent`, `UgcOperateSwitchOpenEvent`, `OnUgcRecordStatusChangeEvent`.

### 7.2 `TextDisplay` — the text display

**Type:** `UgcType.TextDisplay` (5)

UI: `InfoBoardPanel` sends `UgcOperateCommand { Type = TextDisplay, … }`.

Class: `UgcFunctionTextDisplay` (`文字显示器`).

### 7.3 `CustomRandomBox` — the notepad / random box

**Type:** `UgcType.CustomRandomBox` (6)

UI: `NoteBoxEditPanel` → `UgcOperateCommand { Type = CustomRandomBox }`.

### 7.4 `ResourceHarvester` — the "till"

**Type:** `UgcType.ResourceHarvester` (4)

`UgcFunctionResourceHarvester` shows a resource counter on the renderer.

### 7.5 `TimerBounce` / `RealTimerBounce` — the timed bounce

**Types:** 14, 1008

`UgcFunction_TimerBounce` — states 0→1→2, toggling the `Collider0`/`Collider1`/`Trigger` colliders, timeline `ugcview_settimeelasticcolumn_expansion`.

### 7.6 `CollideBounce` — the bounce on collision

**Type:** `UgcType.CollideBounce` (1007)

`UgcFunction_Player` checks `UGCComponentData.FuncType == CollideBounce` while handling collisions.

### 7.7 `PartyEndPoint` — the party finish

**OperateMethod:** `0x1DCEEBD9`

Handled in `PartyModule` and `TrackingPanel` when `UgcEvent.operateMethod == PartyEndPoint`.

### 7.8 The Water Corridor — a separate status sync (not `UgcOperateCommand`)

The water corridor uses **`SwitchPlayerUgcStatusCommand`**, not `UgcOperateCommand`:

```csharp
// CharacterProtocolManager
RequestUgcStatusCommand(levelObjectNetId)  // Type=1
CancelUgcStatusCommand()                   // Type=0
```

The chain: `WaterCorridorCommand` → `EnterUgcStatusState` → `PlayerState.WaterCorridor`.

The status: `UgcStatus.LevelObjectNetId` (separate from `PressurePadStatus`).

### 7.9 UGC interact skills on furniture

| Class | InteractId | Description |
|-------|------------|----------|
| `HasTargetUgcInteract` | `UGCInteract` (4) | casts an `InteractionSkill` from the prefab |
| `UgcManusInteraction` | `UgcManusInteract` (5) | a manual UGC skill (`USpell`) |

Both require `UGCViewComponent.running` and an interact parameter → a skillId from `TableMechanisms`.

### 7.10 The skate floor is NOT a UGC build

`SkateConfig.SkateFloorList` plus `SkateCommand` and `GameSkateMode` are a **separate** mechanic (`furnitureType == 36`). Do not confuse it with the PressurePad.

---

## 8. Comparing the synchronisation channels

| Mechanism | Command / sync | Player status | Object event |
|----------|----------------|---------------|-----------------|
| PressurePad | `UgcOperateCommand` (optional) | `PressurePadStatus` | `UgcEvent` |
| OperateSwitch | `UgcOperateCommand` | — | `UGCOperateSwitchUpdateEvent` |
| WaterCorridor | `SwitchPlayerUgcStatusCommand` | `UgcStatus` | — |
| Generic timeline | `UgcOperateCommand` | depends on the skill | `UgcEvent` |
| The UGC shop | `BuyUgcItemCommand` | — | `OnBuyUgcShopItemSuccessEvent` |

---

## 9. Events (a reference)

| Event | When |
|---------|-------|
| `UgcEvent` | a successful `UgcOperateEvent` on the object's netId |
| `UgcOperateErrorEvent` | an operate failure |
| `UgcOperateSendEvent` | before sending (locally) |
| `UGCOperateSwitchUpdateEvent` | an OperateSwitch's `IsOn` changed |
| `UgcOperateSwitchOpenEvent` | an OperateSwitch was turned on near the player |
| `StructureUgcEvent` | Create/Update/Remove of a build plus `UgcBurdenEnum` |
| `TriggerPressureEvent` | a UI trigger (separate from the PressurePad network path) |

`PressureCommand` / `PressureEvent` (`XDT.Scene.Shared.Entity`) are a generic `[NetworkCommand]`/`[NetworkEvent]` pair with an `int Id` field; **they are not used anywhere on the PressurePad chain examined here**.

---

## 10. UGC burden (the build limit)

A separate subsystem: `UgcBurdenEnum` (drawings, photos, boards), `OverLoadData`, `BuildBurdenSystem`, `StructureUgcEvent`.

It has nothing to do with the PressurePad's gameplay effect, but it uses the "Ugc" prefix in a build context.

---

## 11. Integrating this into the mod

| Task | API |
|--------|-----|
| Any UGC operate | `UGCProtocolManager.DoCommand(in UgcOperateCommand)` |
| PressurePad toggle | `Type=Springboard`, `OperateMethod=PressurePadOpen` |
| Record stop switch | `Type=OperateSwitch`, `OperateMethod=100200010` |
| Emulating a step-on | `PressurePadStatus.PressurePadNetId` + `.Triggered=true` |
| Water corridor | `CharacterProtocolManager.RequestUgcStatusCommand` |
| Resolving a pad netId | `LevelObjectManager.GetLevelObject`, `EntityHelper` |
| Checking a mechanism's type | `DataCenter.TryGetComponentData<UGCComponentData>` → `FuncType` |
| Checking the PressurePad ECS | `entity.TryGet<UgcPressurePadComponent>()` |

**`FindLoadedType` aliases:**

```
XDT.Scene.Shared.Modules.Build.UgcOperateCommand
XDT.Scene.Shared.Modules.Build.UgcType
XDT.Scene.Shared.Modules.Build.UgcOperateMethod
XDTDataAndProtocol.UGCProtocolManager
XDTLevelAndEntity.Gameplay.Component.Homeland.PressurePadComponent
XDTLevelAndEntity.Gameplay.Component.Player.PressurePadStatus
XDT.Scene.Shared.Modules.Build.SwitchPlayerUgcStatusCommand
```

**Checking staticId → mechanism at runtime:**
1. `TableData.TableMechanisms[staticId]`
2. place the item → `DUMP ALL ITEMS` / `HomelandUtility` plus `UgcPressurePadComponent`
3. Harmony on `WebRequestUtility.SendCommand` plus a `PressurePadStatus` log line

---

## 12. Paths in `ilspy-dumps/`

```
XDTDataAndProtocol/XDTDataAndProtocol/UGCProtocolManager.cs

EcsClient/XDT.Scene.Shared.Modules.Build/
  UgcType.cs, UgcOperateMethod.cs, UgcOperateCommand.cs, UgcOperateEvent.cs
  UgcPressurePad.cs, SwitchPlayerUgcStatusCommand.cs

EcsClient/EcsClient.XDT.Scene.Shared.Modules.BuildNew/UgcPressurePadComponent.cs

XDTLevelAndEntity/XDTGame.UGC/
  UgcFunctionSpringboard.cs, UgcFunction_OperateSwitch.cs, UgcFunction_*.cs
  Action_Script_PressurePad.cs, Action_Command_UgcOperate.cs
  HasTargetUgcInteract.cs, UgcManusInteraction.cs

XDTLevelAndEntity/.../PressurePadComponent.cs
XDTLevelAndEntity/.../PressurePadStatus.cs, PressurePadStatus_Field_0.cs, _Field_1.cs
XDTLevelAndEntity/.../PlayerStateLaunched.cs, TransitionFree2Launched.cs
XDTLevelAndEntity/.../PressurePadTriggerCommand.cs
XDTLevelAndEntity/.../WaterCorridorCommand.cs

EcsSystem/.../HomelandSyncSystem.cs (OnUGCOperateEvent, UgcState → component map)
```

---

*Source: `ilspy-dumps/`, verified against the C# bodies. The `staticId 260242` → PressurePad link still needs a runtime check against the tables.*
