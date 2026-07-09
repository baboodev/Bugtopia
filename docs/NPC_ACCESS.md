# NPC Access — позиции, netId, разговор (карта механизмов)

Собрано 2026-07-02 по итогам TalkToNpc-разбора (Quest Assistant, см.
[plans/2026-07-02-quest-assistant-progress.md](plans/2026-07-02-quest-assistant-progress.md)
§13-§23), когда рабочий позиционный хелпер был случайно написан ЗАНОВО, потому что существующий не
нашёлся поиском. **Перед добавлением любого нового NPC-механизма — сверься с этим файлом.**

## Итоговая матрица

| Задача | Рабочий механизм | Где в моде | Ограничения |
|---|---|---|---|
| Позиция NPC (в т.ч. НЕзагруженного) | AuraMono static `MapSpotProtocolManager.TryGetMapSpotPosition(SpotEnum.Npc=2, npcId, out Vector3, GameSceneId)` — **с апдейта 2026-07-09 4 параметра**: споты ключуются per-scene (`MapSpotKeyComponent(category, useId, gameSceneId)`), резолвить метод по paramCount=4 (3 — только старые билды) | `Teleport.cs` → **`TryGetLiveNpcPositionByIdMono(int npcId, out Vector3)`** | Работает для всего, что рендерит карта (server-synced map-spot сущность, движется вместе с NPC). Требует, чтобы NPC вообще был на карте текущей сцены. Сцену хелпер берёт сам: `DataCenter.LevelId` (static RoomLevelId) → маппинг `LevelConst.ToGameSceneId` + retry StarTown (игра ключует споты с TargetLevelId==0 на StarTown) |
| Позиция NPC (загруженного, Unity-скан) | `Object.FindObjectsOfType(Il2CppType.Of<NpcComponent>)` + чтение `position`/`entity.position`/`transform` | `Teleport.cs` → `PopulateLiveNpcEntriesFromUnityObjects` + `TryGetNpcTeleportPosition` | Только реально заспавненные Unity-объекты. **⚠️ МЁРТВ с апдейта 2026-07-09**: `Il2CppType.GetType("...NpcComponent")` = null (Mono-side тип больше не виден из IL2CPP-домена, FQN не менялся) — скан тихо выключен, позиции даёт только map-spot путь |
| netId NPC (только застримленного!) | AuraMono `EcsService.TryGet<INpcClientService>()` → `TryGetNpcNetId(npcId, out netId)`; fallback — скан `Entities.GetComponents<NpcComponent>` по `_componentData.staticId` | `HeartopiaComplete.QuestAssistantUi.cs` → `QuestAssistantTryGetNpcNetIdAuraMono` / `QuestAssistantTryGetNpcNetIdViaComponentScan` | ОБА пути видят только застримленные NPC (сервис = клиентский EcsFilter). **API запросить netId далёкого NPC у сервера НЕ существует** |
| Телепорт к позиции | `TeleportToLocation(Vector3)` | `Teleport.cs` (~line 1639) | Пишет `OverridePosition` + двигает `p_player_skeleton(Clone)` |
| Разговор с NPC (кредит квеста!) | AuraMono static `TalkProtocolManager.SendTalkWithNpc(npcNetId, startOrEnd, talkParam=0)` → `TalkWithPlayerCommand` (обычный `[NetworkCommand]`, БЕЗ `[VerifyEntity]`) | `HeartopiaComplete.QuestAssistantUi.cs` → `QuestAssistantTrySendTalkWithNpc` | Требует ЖИВОЙ netId → NPC должен быть застримлен → телепорт обязателен для далёких. Слать парно: start=true … start=false |
| Диалоговая панель (только UI) | AuraMono static `DialoguePanel.OpenTaskDialogue(taskNetId, netId, isStaticId, staticIdOrResId, targetName)` | `HeartopiaComplete.QuestAssistantUi.cs` → `QuestAssistantTryOpenNpcDialogue` | **НЕ кредитует квест.** Для Accepted-задачи реплики матчатся `wipItems[i].id == staticIdOrResId` (часто 0, НЕ id NPC — id для панели и id NPC держать раздельно) |
| One-click "поговорить с NPC по квесту" | резолв netId → (нет? позиция → телепорт → ждать стриминг) → talk-RPC → панель → watcher → парный end-RPC | `HeartopiaComplete.QuestAssistantUi.cs` → `QuestAssistantTalkToNpcRoutine` | Собирает всё вышеперечисленное |
| Завершить CanSubmit-квест сдачей предмета NPC | `TaskProtocolManager.ClientSubmitTaskItem` → `ClientSubmitNpcTaskItem` → `SubmitGameTaskItem2NpcCommand { GameTaskId, NpcId=STATIC, ItemNetPairs }` — обычный `[NetworkCommand]`, **без** `[VerifyEntity]` (см. декомпил) | `DailyQuestSubmitFeature.cs` → **`TrySubmitDailyQuestCheapestItemsAura(taskId, submitNpc, type, param)`** (generic, от `TableGameTask.submitTargetItem` + рюкзак); переиспользуется в `QuestAssistantOnSubmitToNpcClicked` | **NpcId = STATIC id, БЕЗ телепорта/netId/диалога — прямой синхронный вызов.** §24→§25→§26→§27: перебор через полный talk-флоу (телепорт+RPC+диалог) РАБОТАЛ, но диалоговая панель зависала (см. ниже) — лишний шаг, убран. **Правило:** прежде чем заворачивать submit-действие в talk-флоу — сверить сигнатуру wire-команды на `[VerifyEntity]`/netId-поле |
| Завершить CanSubmit-квест БЕЗ предметов (talk/flag-only, напр. `checkParamString="PlayerFeatureOpen"`) | ТОТ ЖЕ `ClientSubmitTaskItem`, но с **пустым** `List<ItemNetPair>` — vanilla `AutoSubmitNpcTaskItem` сама шлёт пустой список, когда `TableGameTask.submitTargetItem` пуст; `submitType`/`submitParam` при `submitNpc>0` игнорируются игрой (читает `submitNpc` из своей же таблицы) | `HeartopiaComplete.QuestAssistantUi.cs` → `QuestAssistantTrySubmitNoItemsAura` (§29) | **Определять ПЕРЕД действием** через `TryGetDailyQuestSubmitTargetsAura(gameTaskRow, ...)` — если `targets.Count==0`, предметов не нужно вообще, слепой вызов item-сборщика упадёт с "no submit targets" |

## Мёртвые пути (подтверждено эмпирически — НЕ переиспользовать, НЕ чинить копированием)

- `Teleport.cs` → `TryGetNpcNetIdViaClientService` (managed reflection на `EcsService`/
  `INpcClientService`) — `FindLoadedType` возвращает null, типы Mono-only (§14-§15).
- `Teleport.cs` → `PopulateLiveNpcEntriesFromMapSpots` (managed reflection на `MapSpotsSystem`) —
  тот же диагноз, молча возвращает 0 с момента написания (§21). Список NPC-телепортов реально
  наполняется Unity-сканом + `TryGetLiveNpcPositionByIdMono`.
- Чтение `MapSpotData.position` из `GetMapSpots()` для Npc-спотов — поле легитимно нулевое; сам UI
  карты для Npc/Player зовёт `TryGetMapSpotPosition` (см. `MapSpot.GetPosition()`,
  `ilspy-dumps/XDTGameSystem/.../MapSpot.cs:384-397`) (§22).
- Заворачивать item-submit (`SubmitToNpc`/CanSubmit) в полный talk-флоу (телепорт + `SendTalkWithNpc`
  + `OpenTaskDialogue`) — РАБОТАЛО (квест завершался), но **вешало `DialoguePanel` навсегда**: панель
  закрывается только по `TalkEndEvent`, который диспатчится ИЗНУТРИ её собственного tap-through
  state machine (`DialogueNodeTask.TapHandler`); открыть панель и передать предметы отдельным
  AuraMono-вызовом в обход панели означает её tap-flow никогда не запускается → `TalkEndEvent`
  никогда не диспатчится → панель зависает на первой странице (§26-§27). Для submit-действий,
  сигнатура которых не требует netId (см. строку выше) — не открывать диалог вообще.

## Ключевые факты про квесты "поговори с NPC"

- Условие `InteractWithNpc`(30011): `typeParam` = static id NPC. Но у `EnterDialogNode`(30501)
  `typeParam` = **id ДИАЛОГОВОГО УЗЛА, НЕ NPC** (подтверждено 2026-07-04: "Gossip: The Vast World"
  typeParam=10014, реальный NPC = 307 Li Zhen; "Princess Stella's Adventure" typeParam=10013, NPC =
  106 Mrs. Joan) — резолв netId/позиции по нему падает везде («no netId and no map-spot position»).
  **Правильный id NPC для ОБОИХ условий — trackMark `markCategory=2(NPC)`.id** (для InteractWithNpc
  typeParam с ним просто совпадает). Классификатор предпочитает NPC-trackMark (progress doc §51) —
  тот же id-space урок, что navpoint/§44.
- Прогресс кредитует **сервер** при обработке `TalkWithPlayerCommand` — клиентская панель ничего
  не двигает. Реальный флоу игры: interact-target рядом → `SendTalkWithNpc(netId, true)` → ответ
  `NpcTalkStartEvent` → только потом UI (`TalkWithTaskNpcCommand.cs`, `[InteractSetting(10401)]`).
- Проверяет ли сервер дистанцию у RPC — не доказано (после телепорта мы всегда рядом).
