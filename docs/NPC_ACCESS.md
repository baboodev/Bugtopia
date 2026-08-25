# NPC Access — positions, netId, talking (a map of the mechanisms)

Collected 2026-07-02 out of the TalkToNpc investigation (Quest Assistant, see
[plans/2026-07-02-quest-assistant-progress.md](plans/2026-07-02-quest-assistant-progress.md)
§13-§23), after a working positional helper was accidentally written AGAIN because the existing one
did not turn up in a search. **Before adding any new NPC mechanism, check this file first.**

## The resulting matrix

| Task | Working mechanism | Where in the mod | Limits |
|---|---|---|---|
| NPC position (including an UNLOADED one) | AuraMono static `MapSpotProtocolManager.TryGetMapSpotPosition(SpotEnum.Npc=2, npcId, out Vector3, GameSceneId)` — **4 parameters since the 2026-07-09 update**: spots are keyed per scene (`MapSpotKeyComponent(category, useId, gameSceneId)`), so resolve the method by paramCount=4 (3 is old builds only) | `Teleport.cs` → **`TryGetLiveNpcPositionByIdMono(int npcId, out Vector3)`** | Works for anything the map renders (a server-synced map-spot entity that moves with the NPC). Requires the NPC to be on the current scene's map at all. The helper resolves the scene itself: `DataCenter.LevelId` (static RoomLevelId) → the `LevelConst.ToGameSceneId` mapping + a StarTown retry (the game keys spots with TargetLevelId==0 onto StarTown) |
| NPC position (loaded, Unity scan) | `Object.FindObjectsOfType(Il2CppType.Of<NpcComponent>)` plus reading `position`/`entity.position`/`transform` | `Teleport.cs` → `PopulateLiveNpcEntriesFromUnityObjects` + `TryGetNpcTeleportPosition` | Only genuinely spawned Unity objects. **⚠️ DEAD since the 2026-07-09 update**: `Il2CppType.GetType("...NpcComponent")` = null (the Mono-side type is no longer visible from the IL2CPP domain; the FQN did not change) — the scan is silently off and positions come only from the map-spot path |
| NPC netId (streamed-in NPCs only!) | AuraMono `EcsService.TryGet<INpcClientService>()` → `TryGetNpcNetId(npcId, out netId)`; fallback is a scan of `Entities.GetComponents<NpcComponent>` by `_componentData.staticId` | `HeartopiaComplete.QuestAssistantUi.cs` → `QuestAssistantTryGetNpcNetIdAuraMono` / `QuestAssistantTryGetNpcNetIdViaComponentScan` | BOTH paths see streamed-in NPCs only (the service is a client-side EcsFilter). **There is NO API for asking the server for a distant NPC's netId** |
| Teleport to a position | `TeleportToLocation(Vector3)` | `Teleport.cs` (~line 1639) | Writes `OverridePosition` and moves `p_player_skeleton(Clone)` |
| Talking to an NPC (quest credit!) | AuraMono static `TalkProtocolManager.SendTalkWithNpc(npcNetId, startOrEnd, talkParam=0)` → `TalkWithPlayerCommand` (an ordinary `[NetworkCommand]`, WITHOUT `[VerifyEntity]`) | `HeartopiaComplete.QuestAssistantUi.cs` → `QuestAssistantTrySendTalkWithNpc` | Needs a LIVE netId → the NPC must be streamed in → a teleport is mandatory for distant ones. Send in pairs: start=true … start=false |
| The dialogue panel (UI only) | AuraMono static `DialoguePanel.OpenTaskDialogue(taskNetId, netId, isStaticId, staticIdOrResId, targetName)` | `HeartopiaComplete.QuestAssistantUi.cs` → `QuestAssistantTryOpenNpcDialogue` | **Does NOT credit the quest.** For an Accepted task the lines match on `wipItems[i].id == staticIdOrResId` (often 0, and NOT the NPC's id — keep the panel's id and the NPC's id apart) |
| One-click "talk to the quest NPC" | resolve netId → (none? position → teleport → wait for streaming) → talk RPC → panel → watcher → the paired end RPC | `HeartopiaComplete.QuestAssistantUi.cs` → `QuestAssistantTalkToNpcRoutine` | Assembles everything above |
| Finish a CanSubmit quest by handing an item to an NPC | `TaskProtocolManager.ClientSubmitTaskItem` → `ClientSubmitNpcTaskItem` → `SubmitGameTaskItem2NpcCommand { GameTaskId, NpcId=STATIC, ItemNetPairs }` — an ordinary `[NetworkCommand]`, **without** `[VerifyEntity]` (see the decompilation) | `DailyQuestSubmitFeature.cs` → **`TrySubmitDailyQuestCheapestItemsAura(taskId, submitNpc, type, param)`** (generic, driven by `TableGameTask.submitTargetItem` and the backpack); reused in `QuestAssistantOnSubmitToNpcClicked` | **NpcId is the STATIC id, with NO teleport, netId or dialogue — a direct synchronous call.** §24→§25→§26→§27: going through the full talk flow (teleport + RPC + dialogue) DID work, but the dialogue panel hung (see below) — a needless step, since removed. **Rule:** before wrapping a submit action in the talk flow, check the wire command's signature for `[VerifyEntity]` or a netId field |
| Finish a CanSubmit quest WITHOUT items (talk/flag only, e.g. `checkParamString="PlayerFeatureOpen"`) | THE SAME `ClientSubmitTaskItem`, but with an **empty** `List<ItemNetPair>` — vanilla `AutoSubmitNpcTaskItem` sends an empty list itself when `TableGameTask.submitTargetItem` is empty; with `submitNpc>0` the game ignores `submitType`/`submitParam` and reads `submitNpc` from its own table | `HeartopiaComplete.QuestAssistantUi.cs` → `QuestAssistantTrySubmitNoItemsAura` (§29) | **Determine this BEFORE acting** via `TryGetDailyQuestSubmitTargetsAura(gameTaskRow, ...)` — when `targets.Count==0` no items are needed at all, and calling the item collector blindly fails with "no submit targets" |

## Dead paths (confirmed empirically — do NOT reuse, do NOT fix by copying)

- `Teleport.cs` → `TryGetNpcNetIdViaClientService` (managed reflection over `EcsService` /
  `INpcClientService`) — `FindLoadedType` returns null, the types are Mono-only (§14-§15).
- `Teleport.cs` → `PopulateLiveNpcEntriesFromMapSpots` (managed reflection over `MapSpotsSystem`) —
  the same diagnosis; it has silently returned 0 since the day it was written (§21). The NPC teleport
  list is actually filled by the Unity scan plus `TryGetLiveNpcPositionByIdMono`.
- Reading `MapSpotData.position` from `GetMapSpots()` for Npc spots — the field is legitimately zero;
  the map UI itself calls `TryGetMapSpotPosition` for Npc/Player (see `MapSpot.GetPosition()`,
  `ilspy-dumps/XDTGameSystem/.../MapSpot.cs:384-397`) (§22).
- Wrapping an item submit (`SubmitToNpc`/CanSubmit) in the full talk flow (teleport +
  `SendTalkWithNpc` + `OpenTaskDialogue`) — it WORKED (the quest completed) but **hung `DialoguePanel`
  forever**: the panel closes only on `TalkEndEvent`, which is dispatched from INSIDE its own
  tap-through state machine (`DialogueNodeTask.TapHandler`). Opening the panel and then handing the
  items over through a separate AuraMono call bypasses the panel, so its tap flow never starts →
  `TalkEndEvent` is never dispatched → the panel hangs on its first page (§26-§27). For submit actions
  whose signature does not require a netId (see the row above), do not open a dialogue at all.

## Key facts about "talk to an NPC" quests

- Condition `InteractWithNpc`(30011): `typeParam` is the NPC's static id. But for
  `EnterDialogNode`(30501) `typeParam` is **the DIALOGUE NODE's id, NOT the NPC's** (confirmed
  2026-07-04: "Gossip: The Vast World" typeParam=10014 while the real NPC is 307 Li Zhen;
  "Princess Stella's Adventure" typeParam=10013, NPC = 106 Mrs. Joan) — resolving a netId or position
  from it fails everywhere ("no netId and no map-spot position").
  **The correct NPC id for BOTH conditions is the trackMark `markCategory=2(NPC)`.id** (for
  InteractWithNpc the typeParam simply happens to match it). The classifier prefers the NPC trackMark
  (progress doc §51) — the same id-space lesson as navpoint/§44.
- Progress is credited by **the server** when it handles `TalkWithPlayerCommand` — the client panel
  moves nothing. The game's real flow: an interact target nearby → `SendTalkWithNpc(netId, true)` →
  the `NpcTalkStartEvent` reply → and only then the UI (`TalkWithTaskNpcCommand.cs`,
  `[InteractSetting(10401)]`).
- Whether the server checks distance on the RPC is unproven (after a teleport we are always close).
