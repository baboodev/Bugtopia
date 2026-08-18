# Game actions and animations

How the engine's **action** vocabulary works, what it takes to make one play, and which ones are
safe to cast. Companion to [GAME_EVENTS.md](GAME_EVENTS.md) (reacting to the game) — this file is
about *driving* the character.

Emotes are a different subsystem with its own protocol; see
[§ Emotes vs actions](#emotes-vs-actions) below.

---

## 1. The vocabulary

The engine declares **306 actions** through `[ActionConfiguration]` attributes:

```csharp
[ActionConfiguration(ActionId.AxeAttackTree, typeof(PlayerAxeAttackTreeAction), CastFlags.Combo, InterruptFlags.Null, true)]
public sealed class PlayerAxeAttackTree : PlayerAxeAttackArg { }
```

Three things hang off one ActionId:

| Piece | Where | Role |
|---|---|---|
| `ActionId` | `XDTDataAndProtocol.ComponentsData.ActionId` | The number on the wire |
| **Context** (`*Arg` / `*Param` / `*Context`) | `…Gameplay.Action`, `ScriptsRefactory.LevelAndEntity.Gameplay.Action`, … | The payload: the fields the action reads |
| **Action** (`*Action`) | same namespaces | The behaviour: drives the animator, sends the server command |

Casting is `LocalPlayerComponent.Cast(ActionContext)` and it returns an `ActionErrorCode`
(0 = accepted). **Accepted is not the same as animated** — see below.

`ilspy-dumps/` is the source of truth for context field lists. They change between game versions;
re-read them after an update rather than trusting a table.

---

## 2. What makes an action actually play

A blank context is **accepted with code 0 and animates nothing**. Each action reads a field or two
before it will drive the animator. Three gates cover the whole catalogue:

| Field | Why | Fill with |
|---|---|---|
| `maxComboTime` | 0 means "no swings" — the clip sets its trigger and cancels in the same tick | the swing count (3) |
| `controllerFullName` | names the clip family; with none, no asset resolves and nothing renders | `(charType << 24) \| (poseType << 14) \| (override << 9) \| shortName` |
| `socialType` | which social clip | 1 for the plain wave |

`controllerFullName` needs the **tool currently in hand**: the swing controller is
`(stand, <tool override>, lumbering | mining)`, and with empty hands that combination has no asset —
the cast succeeds and nothing renders. Read the override live with
`LocalPlayerComponent.GetControllerOverrideType()` (5 = empty hands, 15 = axe); equip with
`ToolSystem.SetHandhold(int toolId)`, which is a **server round trip** (~1 s), so take the tool once
per run, never per action.

`ControllerShortName` ordinals seen so far: stand 1, lumbering 159, mining 160, gardening 278.

### Vector3 fields are places, and an empty one is under the map

⚠️ **This is the single most destructive mistake in this area.** A context's `Vector3` is not
"unset" when left at zero — it is a real world position, and (0,0,0) is under the terrain. 26 of the
swept actions drop the character 140–160 m when their position field is left blank.

Fill every **place** field with the player's own position, and leave **direction** fields at zero:

```
place      position, target, targetPos, floatTargetPos, dstPosition, endPosition
direction  faceDir, faceDirection, direction   (Vector2 — these are vectors, not places)
```

Read the position off `LocalPlayerComponent.get_entity → Entity.get_position` — **not** a
name-resolved anchor, which can land on a REMOTE player and fling the character across the map to
somebody else (see the `player-resolve-and-input-block` note).

---

## 3. Combos: one cast is one whole swing

Repeating a swing is not "cast it three times quickly". `ActionClipWeaponComb` (the axe base) only
leaves its behave once the animator has gone `Start → Back0X → End → Idle`, roughly **1.5 s**, and a
re-cast before that does NOT queue a second swing — the framework routes it into `OnReplay`, which
advances the combo only when `_canCombo` is set:

```csharp
_maxComboTime = maxComboTime;              // OnBehaveStart
_canCombo = combTimes < _maxComboTime;     // OnHit
```

and `PlayerAxeAttackAction.maxComboTime` reads the **target's** `CollectableObjectComponent
.maxComboCount`, returning **0 when there is no target**. So with `levelObjectNetId = 0` (the
side-effect-free way to cast), `_canCombo` is never set and **every early re-cast is accepted with
code 0 and does nothing**.

Consequences:

* Pace repeated swings **by the clip**, not by a constant. `AnimationComponent.IsAnimState(
  AnimStateHash.Idle)` is the same test the action itself finishes on.
* A real combo needs a real target — and a real target means a real hit: `OnAttackHit` reaches
  `OnHitAction`, which sends `SendAttackTreeCommand` and spends durability.
* Gather is gated differently: its combo count is a **context field** (`maxComboTime`), which is why
  one cast plays the whole sequence there.

---

## 4. Side-effect-free casting

`levelObjectNetId = 0` is the lever. The send sits behind a target lookup:

```csharp
LevelObject t = GetInteractTarget();                       // GetLevelObject(levelObjectNetId)
if (t != null && Entities.GetEntityRef(t.ownerNetId).TryGet(out var e))
    OnHitAction(e, isCombo);                               // the server command lives in here
```

With 0 the lookup returns null, nothing is sent, no durability is spent and no resource is touched —
verified live: the swing renders, the tree is untouched.

For real work, use the game's own interact entry points instead of hand-filled contexts:
`PlayerInteraction.ExecuteHandholdButton(bool down, int interactId)` for handhold interactions
(the axe path — `PlayerAxeHitCommand` derives from `HandholdInteract`, so
`ExecuteHasTargetCommand` can only answer Invalid for it) and `ExecuteHasTargetCommand` for
`HasTargetCommand`s.

---

## 5. Classifying an action by its context's SHAPE

Never by its name — `LieOnConfig` reads as harmless and parks the character in a bath.

| Class | Tell | Behaviour |
|---|---|---|
| `safe` | nothing in the context hijacks the character | plays and returns |
| `tool` | carries `controllerFullName` | needs a tool in hand or nothing renders |
| `target` | carries `levelObjectNetId` | plays in the air, hits nothing |
| `teleport` | carries a `Vector3` | **fills a world position — see §2** |
| `locking` | a `*Motion` / `Occupy` / state-enter context | takes locomotion until something gives it back |
| `dead` | the id is reserved, no context type claims it | not castable |

⚠️ The class is a heuristic over the decompiled shape and it is **wrong for some rows**: the sweep
found `PlayerMatrixApply` (220) tagged `safe` while dropping the character 139 m.

---

## 6. Measured results (sweep, 2026-08-18)

184 of the 268 castable actions were cast at a standing character and measured. Full per-action
detail and the method are in the sweep report; the operational summary:

* **144 safe** — accepted, character unmoved, walking immediately afterwards. This set is the
  catalogue behind the mod's Action Panel (`buddy/ActionPanelFeature.cs`).
* **26 drop the character under the map** — the §2 failure. Recoverable with a warp back.
* **16 hold the body ~12 s** and release on their own. Not broken; that is the animation.
* **3 wedge locomotion permanently** — relog only:
  `FeedWildAnimalMotion` (238), `PlayerFeedPetReady` (247), `PlayerStartBookEdit` (604).
* **82 `locking`** were not swept: they take locomotion by construction.

`PlayerStateFeedPetReady` shows why the wedges are unrecoverable: it hides the game UI on enter and
exits only when the pet it waits for despawns. With no pet, there is no exit.

---

## 7. Diagnosing "the character is stuck"

⚠️ **`LocalPlayerComponent.playerState` lies.** It answers `1 (Free)` for a character that will not
take a step. The only honest test is to actually move: inject an axis through
`LocalPlayerComponent.OnLeftJoystickPerformed(Vector2)` (re-sent per frame — the joystick queue holds
one value per player tick) and measure displacement.

⚠️ **Do not "recover" and immediately re-measure.** The recovery levers below are themselves casts
that hold the body for a moment, so a test run straight after one lands inside a window the test just
re-opened. A harness that did this reported 14 consecutive locks that were one action's 12-second
animation. **Wait ~75 s, touching nothing, before calling anything a lock.**

Recovery ladder, cheapest first:

1. `ToolSystem.SetHandhold(0)` — drop the prop
2. `PlayerUpperFreeMotionArg` + `PlayerFreeMotionArg` — restore both body parts' free motion
3. `ActorActionGraph.EndCasting()` — ends the behave that is playing. Needed **before** 2 when an
   action is still inside its own behave: the free-motion pair only REPLACES the motion, so the
   action carries on and re-asserts itself
4. `EntityHelper.AutoMoveTransfer(Vector3)` — warp back to a known mark (the game's own teleport)
5. `EntityHelper.Transfer2BornPos()` — spawn point, last resort
6. `PlayerFsMachine.SwitchState(GetStateIndex(PlayerState.Free))` — forces the FSM; reports success
   and still does not free the three wedging actions above

Reaching the FSM: `LocalPlayerComponent.character → Character.bodyFsMachine` (public field).
Resolve `SwitchState` / `get_Current` on the **concrete** `PlayerFsMachine`, never on the open
generic `FSMStateMachine\`1` — invoking the uninflated definition throws, and
`PlayerStateBase.get_State` is abstract, so invoking that slot raises `BadImageFormatException`.

---

## Emotes vs actions

Different subsystem, different protocol. An emote needs **two halves**: the server command
(`SendSingleActionNetworkCommand` / `SendExpressionNetworkCommand`) *and* the local play, which is
echo-gated in `SocialReqTask`. The server command alone animates **nobody** — see the
`single-action-emote-protocol` and `emote-unlock-feature` notes and
`buddy/EmoteUnlockFeature.cs`.

Replication of ordinary actions is automatic: `PlayerSyncStatus` → `SendCastEvent` →
`ClientShowActionNetworkCommand`, replayed remotely through `RemotePlayerComponent.Handle`. Nothing
extra is needed to make a bystander see a cast action.
