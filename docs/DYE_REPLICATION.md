# Dye colour — what the other client shows

Agent entry for furniture dye. Walls, floors and ceilings are a different path: one palette byte in the bake ([HOUSE_BUILDING.md](./HOUSE_BUILDING.md) §3.1 and the colours-index section). This page is the piece that has its own `ColorPart` list (a window, a workbench).

**Rule:** a second client shows the colour only when `Color32` is byte-identical to one entry of that item's palette. One channel off is not rounded. The owner still sees the typed colour.

---

## 1. Measured 2026-10-25

Square Metal Window, `staticId` 35303, furniture type 11. Palette length 25. Index 16 (0-based) is `#3884BC`.

The owner typed the hex into the dye panel's RGB field, then Confirm → deselect the piece → Save. The visitor watched the placed window.

| Typed | Packed `ToHex` | In this window's palette | Visitor |
|---|---|---|---|
| `#3884BC` (`56,132,188,255`) | `948223231` / `0x3884BCFF` | yes, `colors[16]` | that blue |
| `#3884BD` (`56,132,189,255`) | `0x3884BDFF` | no | the part's default, a dark near-black |

`#3884BD` is one blue step from the palette entry. The visitor did not show a near-blue. They showed the unmatched fallback. The owner kept the typed colour on their own client.

Not re-read after that save: `DyeColorNewComponent.colors` on either client, and the bake row. The visual is the evidence. Do not invent a packet dump for it.

Older, different item, not a two-client check: Workbench `330001`, pure magenta `0xFF00FFFF`, 2026-09-09. The server accepted the op and charged 2 Dye (`41001`, 11 → 9). Nobody looked at a visitor. Do not use it to claim a visitor renders arbitrary RGB.

---

## 2. Why the two clients disagree

| Client | What it paints | Source |
|---|---|---|
| Owner, after Confirm | the raw `Color32` | `OptionDyeColor` → `BuildComponent.ModifyDyeColor` writes `FurnitureBaseData.dyeDolorData` only. `DyeColorClientUtil.GetDyeColor` turns `color.ToColor32()` into a shader colour. No palette test. |
| Visitor | a palette index, or the default | `DyeColorConfig.GetColorIndexByStaticId` walks `colorParts[i].colors[j]` and returns true only on `Color32` equality (`ilspy-dumps/EcsClient/.../DyeColorConfig.cs`). `DyeCollection.Add` skips the part when that returns false, so the stored index byte stays 0. Index 0 is `colors[0]`. |

`HomelandSyncSystem.OnDyeColorNewComponentUpdated` copies `DyeColorNewComponent.colors` into `FurnitureBaseData` only when that data already exists. It does not create the piece and does not rebuild the bake. A visitor with no `FurnitureBaseData` for the window cannot show a raw int even if the component arrives.

`GetDyeColor` would have painted `#3884BD` as nearly the same blue. The visitor showed the default dark colour, so that client was not applying the raw int.

---

## 3. What actually gets saved

`DyeColorPanel` opened from build mode is `fromBuild = true`.

| Action | Writes | Sends |
|---|---|---|
| Click a swatch, or type a hex | `_partDyeColorData[body] = (Color32, Coin, coin, itemId, count)` and `_guiModel.SetDyeColor(body, Color)`. Preview only. | nothing |
| Confirm (`confirm@btn` → `Save` → `_ConfirmSave`) | `List<(body, Color32)>` → `BuildSingle.GenDyeColorOption`. `DyeColorData` is `{ body, color = Color32.ToHex(), colorIndex = 0, texture = 0 }`. | nothing. `fromBuild` invokes the callback. `MakeItemEvent` is the bag path (`fromBuild == false`) only. |
| Save on `BuildStatusPanel` | walks the undo stack into `BuildDyeData` inside `SendBuildBatchOperation(version, homeNetId, GamePlayType.Building, isLast, ops)` | the batch. `BuildStatusPanel.Save` returns immediately unless `!GlobalSaving.IsOperationSaving()` and `_subMode == CraftState.Free`. A focused piece, and an open dye panel, are not Free. |
| Exit build (`ClickExit` / `DoExit`) | sandbox / blueprint leave | not this dye op |

`Color32.ToHex` is `(r<<24)\|(g<<16)\|(b<<8)\|a` (`HsvColorExt.cs`). Alpha of a dye colour is 255.

`colorIndex` on the option stays 0 for a palette click and for a typed hex. The index the visitor uses is computed later by `GetColorIndexByStaticId`, from the `Color32`, not from the field the client sent.

The upload carries colours. It does not carry the dye item id. Cost is separate: `CraftBank.HandleDyeCost` skips the dye item for `EntityType` wall, quarterwall and floor. A window is none of those, so `DyeColorCosts[(staticId, body)]` still applies at save. The panel's own gate (`_dyeColorUseItemNotEnough`, `_currencyCost`) reads the tuple in `_partDyeColorData`. A tuple of zeros makes Confirm proceed. That is not the save-time check.

Order that uploaded the palette colour: Confirm, click empty ground so the window is not selected, Save, leave build after the save finishes.

---

## 4. Mod picker — current code

`FurnitureDyeFeature.cs`, `FurnitureDyePanelFeature.cs`, `HeartopiaComplete.UguiColorPicker.cs`. Panel source calls `TryApplyFurnitureDyePanel` only. It writes the dictionary and does not send.

Three mismatches with the open panel:

1. **The highlighted swatch deletes the staged colour.** `GetItemDisplayData` reads `_dyeColorItems`, not the dictionary. The cell for the colour the panel was opened with stays highlighted. Its toggle calls `UpdatePartDyeColor(true)`, which `Remove`s that body. Confirm then sees an empty dictionary and only closes. Staging a colour that equals `colors[i]` makes `RenderColorList` highlight cell `i` (measured: writing `#3884BC` highlighted cell 16). Clicking that cell removes the entry. Confirm must be pressed without a second click on it.

2. **The status string says Confirm uploads.** It does not. See §3. The header comment that names `MakeItemEvent` describes the bag path.

3. **A missed cost lookup writes zeros and calls `SetDyeColorCost`.** The label says free. `HandleDyeCost` still uses `DyeColorCosts` for a window.

Fix plan, not implemented: [superpowers/plans/2026-10-24-dye-panel-picker.md](./superpowers/plans/2026-10-24-dye-panel-picker.md). The replication check that this page closes: [superpowers/plans/2026-10-25-dye-replicate.md](./superpowers/plans/2026-10-25-dye-replicate.md).

A free RGB from the picker will show on the owner and not on the visitor. To be visible to the visitor the staged `Color32` has to be an exact palette entry of that `staticId` and `body`.

---

## 5. Do not

- `GetComponents(DyeColorNewComponent)`. Value type. One `TryGet` / `GetDyeColorsData` on a known netId is safe. Enumeration is not.
- Click the P button (`interact_dye_furniture`). It goes to `InteractExecuteEdit` → `Free_Edit` and hangs the game.
- Call `SendBuildBatchOperation` by hand. The 5-arg overload that takes `int version` is the one; the others default version to 0. `JointBuildingClientService.GetCurJointBuildingOprVersion()` has been measured at 0, and a send was correctly not made.
- Treat "the owner sees it" or "the client logged the upload" as "the visitor will see it".
- Treat a near palette colour as close enough.

---

## 6. Types

| What | Where |
|---|---|
| `DyeColorPanel` | `XDTGame.UI.Panel` — `ilspy-dumps/XDTGameUI` |
| `DyeColorData` | `XDT.Scene.Shared.Modules.DyeColor` — `body`, `colorIndex`, `color`, `texture` |
| `Color32.ToHex` / `int.ToColor32` | `HsvColorExt.cs` in `EcsClient` |
| `GetColorIndexByStaticId` | `DyeColorConfig.cs` in `EcsClient` |
| `DyeCollection.Add` | `XDTLevelAndEntity...CombineData/DyeCollection.cs` |
| `GetDyeColor` | `DyeColorClientUtil.cs` |
| `OnDyeColorNewComponentUpdated` | `HomelandSyncSystem.cs` |
| `BuildStatusPanel.Save` | `XDTGameUI/.../BuildStatusPanel.cs` — Free only |
| `HandleDyeCost` | `CraftBank.cs` |
| `GenDyeColorOption` | `BuildSingle.cs` |
| Mod write | `FurnitureDyePanelFeature.cs` `TryApplyFurnitureDyePanel` |
