# UGC build-механизмы — справочник по декомпиляциям

Документ описивает **UGC-поведение размещаемых объектов** (механизмы homeland / sandbox): переключатели, пружинные плиты, текстовые дисплеи, таймеры и т.д.

**Не путать с:**
- [UGC_SHOP.md](./UGC_SHOP.md) — покупка UGC-контента (книги, пластинки)
- [UGC_SYSTEM.md](./UGC_SYSTEM.md) — пользовательский контент (записи, книги, `PlayerUgcType`)

---

## 1. Три значения «UGC» в коде

| Контекст | Namespace / типы | Пример |
|----------|------------------|--------|
| **UGC-контент** | `XDT.Scene.Shared.Modules.Ugc`, `PlayerUgcType` | Книга, музыкальная пластинка |
| **UGC-магазин** | `UgcItemStore`, `BuyUgcItemCommand` | `ShopPanel` storeId 147/148 |
| **UGC build-механизм** | `XDT.Scene.Shared.Modules.Build`, `UgcType`, `UGCViewComponent` | Slippery Rug / PressurePad, переключатели, батуты |

Slippery Rug (пружинная плита / «скользкий ковёр») относится к **третьей** категории: `UgcType.Springboard` + `UgcFunctionSpringboard`.

---

## 2. Ядро протокола

### 2.1 `UGCProtocolManager`

`XDTDataAndProtocol.UGCProtocolManager` — единая точка отправки UGC-команд:

```csharp
public static void DoCommand(in UgcOperateCommand command)
{
    WebRequestUtility.SendCommand(command);
}
```

Также: `UploadScoreBoardCommand`, `CheckUgcItem`, `CanPackUgcItem`, `SyncComponent<T>` (build batch).

### 2.2 `UgcOperateCommand` (`[NetworkCommand]`)

```csharp
public UgcType Type;
public uint NetId;              // netId ECS-сущности механизма
public UgcOperateMethod OperateMethod;
public List<long> Params;       // max 10
```

### 2.3 `UgcOperateEvent` (`[NetworkEvent]`)

Ответ сервера: `ErrorCode`, `Type`, `NetId`, `OperateMethod`, `OperatorNetId`, `Params`.

### 2.4 Цепочка на клиенте

```
Клиент: UGCProtocolManager.DoCommand(UgcOperateCommand)
    → сервер
    → UgcOperateEvent
    → HomelandSyncSystem.OnUGCOperateEvent
        → ErrorCode.Success → EventCenter.DispatchEvent<UgcEvent>(netId, …)
        → иначе → UgcOperateErrorEvent
```

`UgcType >= ClientOnly` (1000+): сервер принудительно ставит `ErrorCode.Success` — чисто клиентские механизмы.

### 2.5 Timeline / UGC-скиллы

Универсальный путь из timeline-клипов:

```csharp
// Action_Command_UgcOperate.Begin
UgcOperateCommand {
    Type = target.UgcType,
    NetId = target.NetId,
    OperateMethod = (UgcOperateMethod)context.ability.skillId
};
UGCProtocolManager.DoCommand(in command);
```

`UgcFunction_Player.SendUgcOperateCommand(targetNetId, type, skillId)` — тот же паттерн (skillId кастуется в `UgcOperateMethod`).

---

## 3. `UgcType` — полный enum

Источник: `EcsClient/XDT.Scene.Shared.Modules.Build/UgcType.cs`

| Значение | Имя | Комментарий (из атрибутов) |
|----------|-----|----------------------------|
| 0 | `Player` | Игрок |
| 1 | `RandomGenerator` | Случайный генератор |
| 2 | `Translate` | Шахматная фигура / перемещение |
| 3 | `OperateSwitch` | Переключатель |
| 4 | `ResourceHarvester` | «Касса» / сбор ресурсов |
| 5 | `TextDisplay` | Текстовый дисплей |
| 6 | `CustomRandomBox` | Пользовательская случайная коробка |
| 7 | `OperateState` | OperateState |
| 9 | `InteractionBuff` | Назначение buff |
| 10 | `SwitchRenderState` | Отображение состояния |
| 11 | `Shoot` | Стрельба |
| 12 | `CollideBroken` | Разрушение при столкновении |
| 13 | `TimerBroken` | Таймерное разрушение |
| 14 | `TimerBounce` | Таймерный отскок |
| 15 | `Springboard` | **Пружинная плита / PressurePad** |
| 16 | `CollideSwitch` | Переключатель столкновения |
| 17 | `SelfDefinedSwitch` | Пользовательский таймер-переключатель |
| 18 | `Ignite` | Поджигание |
| 19 | `AutoStateChange` | Автосмена состояния |
| 20 | `Appearance` | Маскировка внешности |
| 21 | `Brush` | Покраска |
| 22 | `Drop` | Выпадение |
| 1000 | `ClientOnly` | Только клиент |
| 1001 | `Clock` | Часы |
| 1002 | `StampedeInteraction` | Наступательное взаимодействие |
| 1003 | `Timer` | Таймер |
| 1004 | `DoorInteraction` | Портал |
| 1005 | `JumpBed` | Батут |
| 1006 | `CurveLanding` | Кривая посадки |
| 1007 | `CollideBounce` | Отскок при столкновении |
| 1008 | `RealTimerBounce` | Реальный таймерный отскок |
| 1009 | `Hang` | Подвешивание |
| 1010 | `InstantiatedClock` | Экземплярные часы |

Тип привязывается к префабу через `UGCComponentData.FuncType` / `TableMechanisms`.

---

## 4. `UgcOperateMethod` — enum операций

Источник: `UgcOperateMethod.cs`

| Значение | Имя | Типичное применение |
|----------|-----|---------------------|
| 0 | `Default` | По умолчанию |
| 1 | `Interaction` | Общее взаимодействие |
| 2 | `Push` | Толкнуть |
| 3 | `Pull` | Потянуть |
| 4 | `Throw` | Бросить |
| 5 | `Turn` | Повернуть |
| 6 | `Switch` | Переключить |
| 7 | `PutCoin` | Положить монету |
| 8 | `TakeCoin` | Взять монету |
| 9 | `AddBuff` | Добавить buff |
| 10 | `RemoveBuff` | Снять buff |
| 11 | `EnterCollide` | Вход в коллизию |
| 12 | `LeaveCollide` | Выход из коллизии |
| 13 | `SwitchOpen` | Открыть |
| 14 | `SwitchClose` | Закрыть |
| 15 | `EnterStartPoint` | Точка старта |
| 16 | `EnterEndPoint` | Точка финиша |
| 17 | `StampedeDisappear` | Исчезновение (stampede) |
| 18 | `UseSnowmanAppearance` | Облик снеговика |
| 19 | `CopyAppearance` | Копировать облик |
| 20 | `GetOnAppearance` | Надеть облик |
| `0x1DCEEBCD` | **`PressurePadOpen`** | **Пружинная плита** |
| `0x1DCEEBD9` | **`PartyEndPoint`** | Финиш в party-режиме |

Конкретный `OperateMethod` для механизма задаётся в `TableUgcAction_*` / timeline skillId.

---

## 5. Реализованные `UgcFunction*` (C# в дампах)

| Класс | UgcType / UgcClass | Файл |
|-------|-------------------|------|
| `UgcFunctionSpringboard` | `弹板` (Springboard) | PressurePad, коллайдеры, timeline |
| `UgcFunction_OperateSwitch` | `OperateSwitch` | Запись, метроном, `UGCOperateSwitchUpdateEvent` |
| `UgcFunction_TimerBounce` | `TimerBounce` | Таймерный отскок, коллайдеры |
| `UgcFunction_RealTimerBounce` | `RealTimerBounce` | |
| `UgcFunction_RandomGenerator` | `RandomGenerator` | |
| `UgcFunction_SwitchRenderState` | `SwitchRenderState` | |
| `UgcFunction_InstantiatedClock` | `InstantiatedClock` | |
| `UgcFunction_ColorLerp` | `Brush` | Покраска |
| `UgcFunction_Player` | `Player` | Телепорт, bounce, `SendUgcOperateCommand` |
| `UgcFunctionResourceHarvester` | `资源采集器` | Счётчик ресурсов |
| `UgcFunctionTextDisplay` | `文字显示器` | Текстовый дисплей |
| `UgcFunctionCustomRandomBox` | | Случайная коробка |
| `UgcFunctionSelfDefinedSwitch` | | Пользовательский переключатель |
| `UgcFunctionCollideSwitch` | | Переключатель столкновения |
| `UgcFunctionSwitch` | | |
| `UgcFunctionFiniteState` | | Конечный автомат |
| `UgcFunctionGeneric` | `通用功能` | Заглушка |
| `UgcFunction_Default` | `ClientOnly` | Клиент-only |

`HomelandSyncSystem` при обновлении `UgcStateComponent` мапит state → component data для:
- `ResourceHarvester` → `UgcResourceHarvester`
- `Springboard` → `UgcPressurePad`
- `SelfDefinedSwitch` → `UgcSelfDefinedSwitch`
- `CollideSwitch` → `UgcCollideSwitch`

---

## 6. PressurePad / Slippery Rug (`UgcType.Springboard`)

### 6.1 Идентичность объекта

| Поле | Значение | Примечание |
|------|----------|------------|
| Shop `itemId=150043` | Строка `TableStoreGroup` | Каталог магазина |
| `rewardType=2` | `RewardType.Item` | |
| `rewards='2:260242x1'` | 1× `TableEntity` staticId **260242** | |
| Размещённый объект | UGC build с механизмом **PressurePad** | `UgcClass("弹板")` |

**Оговорка:** строки `Slippery Rug` / `260242` в C# не захардкожены. Связь staticId → PressurePad подтверждается рантаймом:
- `TableData.TableMechanisms[staticId].ugcSkills`
- `TableData.TableUgcAction_Script_PressurePads`
- на entity: `UgcPressurePadComponent` `[Persistent("ugPrPa")]`

### 6.2 ECS-данные

**Клиентский view-data** — `UgcPressurePad`:

```csharp
bool isTriggeredBySelf;
bool isOn;
byte strength;
```

**Серверный persistent** — `UgcPressurePadComponent`:

```csharp
bool isOn;
byte strength;
```

### 6.3 Runtime pipeline — ON ENTER

```
[нога игрока → Trigger collider level-object]
    ↓ UGC timeline clip
Action_Script_PressurePad.Begin()
    → PressurePadComponent.OnPlayerTriggered(localPlayer)
        ├─ PressurePadStatus.PressurePadNetId = padNetId
        ├─ PressurePadStatus.Triggered = isTriggeredBySelf
        └─ if isTriggeredBySelf → SwitchEvent()
                → UgcOperateCommand { Springboard, NetId, PressurePadOpen }
    ↓ dirty status
PlayerSyncStatus → PressurePadStatus_Field_0/1.OnSend
    → Entities.SyncSystem.Send ► СЕРВЕР
    ↓ локально
TransitionFree2Launched ← PlayerStateLaunched.IsStateSatisfy()
    → PlayerState.Launched (скольжение/запуск)
```

**Условия `OnPlayerTriggered`:**
- `currentState.CanJump` (или null state)
- `IsTriggerState` (`isOn == true`)

**Два режима `isTriggeredBySelf`:**

| Режим | `Triggered` при шаге | `UgcOperateCommand` | Launch |
|-------|---------------------|---------------------|--------|
| `true` | `true` | да (`SwitchEvent`) | сразу через `IsStateSatisfy` |
| `false` | `false` | нет при шаге | после `UgcEvent(PressurePadOpen)` → `OnUGCEvent` сканирует trigger и ставит `Triggered=true` |

**F-интеракт (4012):** `PressurePadTriggerCommand` → подход + timeline → `UgcOperateCommand(PressurePadOpen)` без прямого шага.

### 6.4 Runtime pipeline — ON EXIT

```
PlayerStateLaunched.Reset()
    → PressurePadStatus.PressurePadNetId = 0
    → SyncCommand ► СЕРВЕР
```

`Triggered` при `Reset()` **не** обнуляется — только `PressurePadNetId`. Полный сброс: `PressurePadStatus.Reset()` при респавне (`PlayerSyncStatus`).

### 6.5 Синхронизация статуса игрока

`PressurePadStatus` — `[NetId(39, 300)]`, dirty-tracked:

| Поле | Sync field | Описание |
|------|------------|----------|
| `PressurePadNetId` | `PressurePadStatus_Field_0` | netId плиты |
| `Triggered` | `PressurePadStatus_Field_1` | флаг триггера |

Отправка: `PressurePadStatus_Field_0.OnSend` / `_Field_1.OnSend` → `Entities.SyncSystem.Send`.

### 6.6 Локальная физика запуска

`PlayerStateLaunched.StartJump()`:
- читает `PressurePadComponent.Direction` (поворот от угла entity)
- `moveComponent.SetMoveSpeed(StrengthX, StrengthY)` из `PressurePadConfig.ElasticConfigs[strength]`

Физика **не** шлёт отдельных команд — только воспроизводит синхронизированный status.

### 6.7 `OnUGCEvent` — запуск соседей

После серверного `PressurePadOpen` все клиенты получают `UgcEvent`. `PressurePadComponent.OnUGCEvent`:
- проверяет `OperateMethod == PressurePadOpen`
- для локального игрока в trigger-bounds: ставит `PressurePadNetId` + `Triggered=true`

### 6.8 Коллайдеры и timeline

`UgcFunctionSpringboard`:
- `UpdateColliderState()` — переключает `Collider_anim_on` / `Collider_anim_off` по `isOn`
- timeline: `ugcview_sbulletboard_on/off` или `ugcview_pbulletboard_on/off` (если skill `500100045` в `TableMechanisms`)

### 6.9 Модель авторитетности

| Аспект | Кто авторитетен |
|--------|-----------------|
| `isOn` плиты | **Сервер** (через `UgcOperateCommand`) |
| «Игрок на плите» | **Клиент-владелец** публикует `PressurePadStatus` → сервер реплицирует |
| Физика скольжения | **Локальный клиент** (`PlayerStateLaunched`) |

Сервер **не** детектит позицию ноги сам — клиент объявляет триггер через status-sync.

### 6.10 Эмуляция для мода

**Переключить плиту (без касания):**

```csharp
WebRequestUtility.SendCommand(new UgcOperateCommand {
    Type = UgcType.Springboard,
    NetId = padNetId,
    OperateMethod = UgcOperateMethod.PressurePadOpen
});
```

**Эмуляция «нога на плите» + launch:**

```csharp
localPlayer.Status.PressurePadStatus.PressurePadNetId = padNetId;
localPlayer.Status.PressurePadStatus.Triggered = true;
// dirty → автоматический sync; TransitionFree2Launched подхватит
```

Нужен живой `padNetId` и `isOn=true` для штатного `OnPlayerTriggered`.

---

## 7. Другие известные UGC-взаимодействия

### 7.1 `OperateSwitch` — переключатель (запись, метроном)

**Тип:** `UgcType.OperateSwitch` (3)

**Известный вызов** (остановка записи на фонографе):

```csharp
UgcOperateCommand {
    Type = UgcType.OperateSwitch,
    NetId = entity.netId,
    OperateMethod = (UgcOperateMethod)100200010u  // stop record
};
```

Источник: `AudioRecordProtocolManager.DoStopCommand`.

**Поведение:** `UgcFunction_OperateSwitch` — `IsOn`, timeline on/off, связь с `AudioRecordComponent`, `AudioJiePaiComponent` (метроном), события `UGCOperateSwitchUpdateEvent`, `UgcOperateSwitchOpenEvent`, `OnUgcRecordStatusChangeEvent`.

### 7.2 `TextDisplay` — текстовый дисплей

**Тип:** `UgcType.TextDisplay` (5)

UI: `InfoBoardPanel` отправляет `UgcOperateCommand { Type = TextDisplay, … }`.

Класс: `UgcFunctionTextDisplay` (`文字显示器`).

### 7.3 `CustomRandomBox` — блокнот / случайная коробка

**Тип:** `UgcType.CustomRandomBox` (6)

UI: `NoteBoxEditPanel` → `UgcOperateCommand { Type = CustomRandomBox }`.

### 7.4 `ResourceHarvester` — «касса»

**Тип:** `UgcType.ResourceHarvester` (4)

`UgcFunctionResourceHarvester` — отображение счётчика ресурсов на рендерере.

### 7.5 `TimerBounce` / `RealTimerBounce` — таймерный отскок

**Типы:** 14, 1008

`UgcFunction_TimerBounce` — состояния 0→1→2, переключение коллайдеров `Collider0`/`Collider1`/`Trigger`, timeline `ugcview_settimeelasticcolumn_expansion`.

### 7.6 `CollideBounce` — отскок при столкновении

**Тип:** `UgcType.CollideBounce` (1007)

`UgcFunction_Player` проверяет `UGCComponentData.FuncType == CollideBounce` при обработке столкновений.

### 7.7 `PartyEndPoint` — финиш party

**OperateMethod:** `0x1DCEEBD9`

Обрабатывается в `PartyModule`, `TrackingPanel` при `UgcEvent.operateMethod == PartyEndPoint`.

### 7.8 Water Corridor — отдельный status-sync (не `UgcOperateCommand`)

Водный коридор использует **`SwitchPlayerUgcStatusCommand`**, не `UgcOperateCommand`:

```csharp
// CharacterProtocolManager
RequestUgcStatusCommand(levelObjectNetId)  // Type=1
CancelUgcStatusCommand()                   // Type=0
```

Цепочка: `WaterCorridorCommand` → `EnterUgcStatusState` → `PlayerState.WaterCorridor`.

Статус: `UgcStatus.LevelObjectNetId` (отдельно от `PressurePadStatus`).

### 7.9 UGC-интеракт скиллы на мебели

| Класс | InteractId | Описание |
|-------|------------|----------|
| `HasTargetUgcInteract` | `UGCInteract` (4) | Каст `InteractionSkill` с префаба |
| `UgcManusInteraction` | `UgcManusInteract` (5) | Ручной UGC-скилл (`USpell`) |

Требуют `UGCViewComponent.running` и параметр interact → skillId из `TableMechanisms`.

### 7.10 Skate floor — НЕ UGC build

`SkateConfig.SkateFloorList` + `SkateCommand` + `GameSkateMode` — **отдельная** механика (`furnitureType == 36`). Не путать с PressurePad.

---

## 8. Сравнение каналов синхронизации

| Механизм | Команда / sync | Статус игрока | Событие объекта |
|----------|----------------|---------------|-----------------|
| PressurePad | `UgcOperateCommand` (опц.) | `PressurePadStatus` | `UgcEvent` |
| OperateSwitch | `UgcOperateCommand` | — | `UGCOperateSwitchUpdateEvent` |
| WaterCorridor | `SwitchPlayerUgcStatusCommand` | `UgcStatus` | — |
| Timeline generic | `UgcOperateCommand` | зависит от skill | `UgcEvent` |
| UGC-магазин | `BuyUgcItemCommand` | — | `OnBuyUgcShopItemSuccessEvent` |

---

## 9. События (справочник)

| Событие | Когда |
|---------|-------|
| `UgcEvent` | Успешный `UgcOperateEvent` на netId объекта |
| `UgcOperateErrorEvent` | Ошибка operate |
| `UgcOperateSendEvent` | Перед отправкой (локально) |
| `UGCOperateSwitchUpdateEvent` | Смена `IsOn` у OperateSwitch |
| `UgcOperateSwitchOpenEvent` | OperateSwitch включён рядом с игроком |
| `StructureUgcEvent` | Create/Update/Remove build + `UgcBurdenEnum` |
| `TriggerPressureEvent` | UI-триггер (отдельно от PressurePad network path) |

`PressureCommand` / `PressureEvent` (`XDT.Scene.Shared.Entity`) — generic `[NetworkCommand]`/`[NetworkEvent]` с полем `int Id`; **на разобранной цепочке PressurePad не используются**.

---

## 10. UGC burden (лимит постройки)

Отдельная подсистема: `UgcBurdenEnum` (рисунки, фото, доски), `OverLoadData`, `BuildBurdenSystem`, `StructureUgcEvent`.

Не связана с gameplay-эффектом PressurePad, но использует префикс «Ugc» в build-контексте.

---

## 11. Интеграция в мод

| Задача | API |
|--------|-----|
| Любой UGC operate | `UGCProtocolManager.DoCommand(in UgcOperateCommand)` |
| PressurePad toggle | `Type=Springboard`, `OperateMethod=PressurePadOpen` |
| Record stop switch | `Type=OperateSwitch`, `OperateMethod=100200010` |
| Эмуляция step-on | `PressurePadStatus.PressurePadNetId` + `.Triggered=true` |
| Water corridor | `CharacterProtocolManager.RequestUgcStatusCommand` |
| Резолв pad netId | `LevelObjectManager.GetLevelObject`, `EntityHelper` |
| Проверка типа механизма | `DataCenter.TryGetComponentData<UGCComponentData>` → `FuncType` |
| Проверка PressurePad ECS | `entity.TryGet<UgcPressurePadComponent>()` |

**Алиасы `FindLoadedType`:**

```
XDT.Scene.Shared.Modules.Build.UgcOperateCommand
XDT.Scene.Shared.Modules.Build.UgcType
XDT.Scene.Shared.Modules.Build.UgcOperateMethod
XDTDataAndProtocol.UGCProtocolManager
XDTLevelAndEntity.Gameplay.Component.Homeland.PressurePadComponent
XDTLevelAndEntity.Gameplay.Component.Player.PressurePadStatus
XDT.Scene.Shared.Modules.Build.SwitchPlayerUgcStatusCommand
```

**Рантайм-проверка staticId → механизм:**
1. `TableData.TableMechanisms[staticId]`
2. Разместить предмет → `DUMP ALL ITEMS` / `HomelandUtility` + `UgcPressurePadComponent`
3. Harmony на `WebRequestUtility.SendCommand` + лог `PressurePadStatus`

---

## 12. Пути в `ilspy-dumps/`

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

*Источник: `ilspy-dumps/`, верифицировано по C#-телам. Связь `staticId 260242` → PressurePad требует рантайм-проверки таблиц.*
