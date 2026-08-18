# Транспорт: каталог, спавн, посадка, драконья лодка

Всё, что мод знает про транспорт игры. Источник — разбор `ilspy-dumps` плюс живые проверки; спорные
места помечены явно, непроверенное названо непроверенным.

---

## 1. Каталог и владение

**`TableCar`** (`ilspy-dumps/EcsClient/TableCar.cs`) — одна строка на транспорт, около 350 записей.
Ключевые поля: `id` (совпадает со `staticId` предмета), физика (`runAcceleration`,
`runForwardMaxSpeed`, `minTurnSpeed`, `turnTime`), `loadNum` (мест), `carBox[3]` (габарит, по нему
ищется место для спавна), `refitInfo`, `drivingMode`, `normalPrefabId`.

Словарь `TableData.TableCars`, доступ `TableData.GetCar(int id)`. Грузится из `ExcelTableType.Car`.
Это и есть «все машины в игре».

**Транспорт игрока** — предметы рюкзака в `EStorageType.Garage` (значение 3), категория
`EntityType.car` (80–88, `EntityTypeUtil.IsGarageItem`). `staticId` предмета равен `TableCar.id`.
Читается через `DataModule<BackPackSystem>.Instance.GetAllItem(EStorageType.Garage)` — та же
кладовая, что показывает штатная панель транспорта.

---

## 2. Спавн: клиент → сервер

```
VehiclePanel._GetOnVehicle → VehicleManager.GetOnVehicle(staticId)
    → событие CallVehicleEvent{birthPointNetId=0, staticId, getOnVehicle}   (локальное)
    → VehicleManager.CreateDrivingVehicle(itemId, getOn)
```

Внутри по шагам:

1. `TableData.GetCar(itemId)` — проверка на null;
2. позиция перед игроком: `VehicleUtility.CreateVehiclePosition(pos, rot, carBox, out vehiclePos, radius)`;
3. запретная зона: `IMetaAreaService.IsPosForbiddenForVehicle(vehiclePos)` — **чистый табличный
   поиск** `TableData.ForbiddenVehicleArea.Contains(areaId)`, не привязан к воде;
4. `GetVehicleLevleObjectId(itemId, seat0, out levelObjectId)` — через
   `IConfigManager.GetEntityScriptData(itemId, Feature.putitem)` по имени `sit_point_1`;
5. **`VehicleProtocolManager.PlayerCallVehicle(itemId, vehiclePos, yAxis, levelObjectId, getOnVehicle)`**.

Последний просто шлёт команду:

```csharp
CallVehicleCommand { int StaticId; Vector3 Position; float YAxis; bool IsAutoGetOnDriveSeat; int LevelObjectId; }
```

`XDT.Scene.Shared.Modules.Vehicle`, `[NetworkCommand]`, образ `EcsClient`.

**Сервер проверяет владение**: `VehicleErrorCode.VehicleNotHave / CarUnAvailable / AreaForbid /
StaminaNotEnough`. То есть обычной `CallVehicleCommand` нельзя призвать чужой транспорт.

`VehicleProtocolManager` живёт в `XDTDataAndProtocol` и в interop отсутствует — только AuraMono.

### Ответ сервера асимметричен

`VehicleProtocolManager.ServerCallVehicleResult` рассылает:

| Исход | Что приходит |
|---|---|
| Успех | ⚠️ **ничего** — проверено трижды живьём: транспорт появлялся, `UpdateCurrentVehicle` не приходил ни разу |
| Отказ по типу | `UITipEvent{tipId=10139}` — быстро и надёжно, <100 мс |
| `AreaForbid` | ⚠️ **ничего** — в `switch` пустая ветка `case AreaForbid: break;` |

Читается как намеренная схема «молчание = успех». Поэтому **успех проверяется активным сканом
мира**, а не ожиданием события: ищем живой `VehicleComponent` с нужным `staticId` и своим
владельцем. Хук на `UpdateCurrentVehicle` оставлен как безвредный быстрый путь на случай, если
поведение сервера изменится.

> ⚠️ `UITipEvent.backupString` — управляемая строка. **Не читать** из снимка события: обработчик
> вызывается отложенно из кольцевого буфера, к тому моменту строка может быть уже собрана. Безопасен
> только `tipId`@0.

### Под водой — спавн отклоняется молча

Дважды подтверждено: ни найденного транспорта после таймаута, ни отказного типа. Соответствует
`VehicleErrorCode.AreaForbid`. Отдельной подводной сцены нет (уровень один — `GameLevel_Main`,
глубина это зона внутри него), а `VehicleUtility.DrivingMode` знает только `{Default, DragonBoat}` —
подводного режима не существует в принципе.

---

## 3. Посадка в чужой транспорт — проверки владения нет

`VehicleProtocolManager.GetOnVehicle(vehicleNetId, seat, levelObjectId)` — **иной путь, чем призыв**,
и он не проверяет владельца. Ни в `VehicleComponent`, ни в `VehicleMainInteract` проверки нет,
только занятость места (`HavePassengerInSeatIndex`). Подтверждено живым вызовом с первого раза.

### ⚠️ «Спавн перестал работать» — это физика, а не сломанное состояние

`VehicleUtility.CreateVehiclePosition` — шаг поиска места, который проходит **любой** призыв, —
это локальный `Physics.OverlapBox`/`Raycast` прямо перед игроком по слою транспорта, и
`IsVehicleCollider` совпадает с **любым** `VehicleComponent`, чьим бы он ни был.

Выход из транспорта **не убирает его из мира** — он остаётся физически стоять. Если игрок стоит
рядом с ним, коллайдер срывает каждую следующую попытку призыва в `VehicleTipsEnum.HaveVehicle` /
`NotEnoughSpace`, включая призыв собственной машины через штатное меню. Выглядит как «спавн
сломался», лечится отходом на несколько метров или отзывом мешающего транспорта
(`VehicleProtocolManager.ReCallVehicle(staticId, Vector3.zero, 0f, VehicleReCallCommandType.Destroy)`).
Ничего в состоянии аккаунта при этом не портится.

---

## 4. Драконья лодка

«Сухопутная драконья лодка» — **не отдельная водная мини-игра**. Строки «旱地»/«dryland» в дампах нет
вовсе. Это обычный наземный `VehicleComponent` с
`VehicleDrivingMode == VehicleUtility.DrivingMode.DragonBoat`, который призывается, паркуется и
водится как любой другой транспорт во время фестиваля. Две роли — это просто места:

- **Рулевой** (место 0, `SelfVehicleController`): непрерывное весло влево-вправо через
  `dragonctrl_l_hold`/`dragonctrl_r_hold` → `IMonoInputManager.SendMoveValueToControl` →
  `VehicleComponent.HandleVirtualInput`, настройки в `DragonBoatSystemConfig`; тратит выносливость
  через `VehicleProtocolManager.SendVehicleInputCommand`.
- **Пассажир-барабанщик** (место >0, `RemoteSelfVehicleController`): делает QTE, восстанавливающее
  рулевому выносливость. ⚠️ Он **тоже** зовёт `VehicleManager.SetSelfEntityVehicle`, поэтому
  `GetSelfEntityVehicle()` работает и для рулевого, и для пассажира.

### Автомат QTE

Состояния: `VehicleQTEStatus { None=0, DragonBoatIdle=1, DragonBoatDrum=2, DragonBoatSmite=3,
DragonBoatRelief=4 }`, владеет сервер через
`VehicleQTEComponent{Status, CurrentStatusStartTimeMs, CurrentStatusDurationMs}`.

Цикл: **Idle** (виден призыв) → тап → **Drum** (заряд) → **Smite** (окно попадания, длительность в
`CurrentStatusDurationMs`) → тап → **Relief** → снова Idle.

Команда одна на оба тапа:
`VehicleProtocolManager.InteractVehicleQTEState(uint vehicleNetId)` →
`InteractVehicleQTECommand{ uint VehicleNetId }`. В UI оба нажатия висят на одном обработчике
`VehicleBoatWidget.OnInteractBtnClick`.

События (предпочитать первое):

| Событие | Область | Размер | Поля |
|---|---|---|---|
| `ScriptsRefactory.DataAndProtocol.Events.PlayerVehicleQTEEvent` | **глобальное** | 24 | `vehicleNetId(uint)@0, Status(int)@4, StartTimeMs(long)@8, DurationMs(long)@16` |
| `XDTDataAndProtocol.Events.VehicleQTEEvent` | по netId | 16 | `Status(int)@0, StartTimeMs(long)@8` — без длительности |
| `VehicleQTEEffectUIEvent` | глобальное | — | `SourcePlayerNetId@0, TargetPlayerNetId@4`, косметика |

⚠️ `PlayerVehicleQTEEvent` рассылается по **всем** лодкам в округе — фильтровать по своему
`vehicleNetId` обязательно; штатный виджет делает ровно это. И у `GameEventSnapshot` нет
`ReadInt64` — читать как `(long)e.ReadUInt64(offset)`.

Свой `vehicleNetId`: `VehicleManager.Instance.GetSelfEntityVehicle()` → `entity` → `netId`
(готовый образец — `VehicleTeleportFeature.cs`).

### Гонка: контрольные точки — это обычные квесты

⚠️ **Не путать с `VehicleCheckPointComponent`** — тот относится к другой мини-игре («Radio Help
Event», `VehicleRadioComponent`, `GMSearchRadioHelpEvent`).

Точки гонки — это `GameTask`: `GameTaskType.DragonboatRaceTarget`,
`SubmitTaskType.DragonboatRaceTarget = 1000563`, маркер `TrackType.DragonboatRaceVehicle = 22`.
Привязка «цепочка → задача» лежит в табличных данных (`TableGameTask`), которых в дампах нет.

Сдача полностью обобщённая: `TaskProtocolManager.ClientSubmitTaskItem(...)` →
`SubmitGameTaskItem2WorldObjectCommand`. В команде **нет поля позиции**, а в `GameTaskErrorCode` нет
кода про расстояние.

**Но живая проверка показала, что голая сдача не обходит контрольную точку.** Во время реальной
гонки отслеживаемая задача была в состоянии `Accepted(3)`, а не `CanSubmit(4)`; вызов проходил
механически, игра отвечала «Unable to complete Quest». Гейт состояния настоящий и серверный.

`TaskProtocolManager.GmFinishTask` существует, но почти наверняка закрыт серверной GM-проверкой, как
и прочие `Gm*` команды — см. память про тупик с GM-режимом.

---

## 5. Что из этого уже есть в моде

| Возможность | Где |
|---|---|
| Призыв в обход штатного пути | `VehicleBypassFeature.TryVehicleBypassForceSummon(itemId, getOn, out error)` |
| Каталог + фильтр «только мои» | `SpawnVehicleFeature.cs` |
| Скан живого транспорта в мире, с именем владельца | `SpawnVehicleFeature.TryScanLiveVehicles` |
| Спуфинг позиции транспорта | `VehicleTeleportFeature.cs` |
| Контекст транспорта для ноклипа | `NoclipFeature.EnsureNoclipVehicleAuraMono` |

Имя владельца берётся из `DataCenter.TryGetComponentData<LevelEntityComponentData>().ownerId` —
у самого `VehicleComponent` поля владельца нет; дальше через ту же цепочку разрешения имён, что
использует карта.

---

## 6. Общее правило, выученное здесь

⚠️ **Хук события надо регистрировать заметно раньше действия, результат которого он ловит.**
`RegisterGameEventHook` только добавляет запись в таблицу; сам детур навешивается **лениво**, за
несколько последующих кадров `OnUpdate` (резолв класса → инфлейт `DispatchEvent<T>` →
`mono_compile_method` → `NativeDetour`). Быстрый ответ сервера успевает прийти раньше, чем детур
встанет, поэтому первое же нажатие в сессии гарантированно теряет свой результат.

Регистрировать безусловно из `OnUpdate`, а не в том же вызове, что шлёт команду.
