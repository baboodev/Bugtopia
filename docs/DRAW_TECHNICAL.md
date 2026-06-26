# Draw (Pictures) — Technical Reference

How the mod decrypts, colorizes, edits and **uploads drawings to the server**, with the exact
classes/types and the access patterns used to reach them at runtime.

Source files:
- `buddy/DrawColorCodec.cs` — palette (LUT) resolution + index↔color codec + RLE builder.
- `buddy/DrawUploadFeature.cs` — server upload (AuraMono draw protocol), extract/upload UI actions.
- `buddy/PicturesDecryptFeature.cs` — decrypt/encrypt of the `ScreenCapture` cache, Pictures tab UI.
- Tooling: `tools/draw_color_codec.py`, `tools/screen_capture_crypto.py` (decode-draw/encode-draw),
  `tools/gen_drawing_palette.py`, `tools/draw_quantize_check.py`, `tools/draw_upload_sim.py`.

Related: [TYPE_RESOLUTION.md](./TYPE_RESOLUTION.md), [GAME_ASSEMBLIES_AND_TOOLS.md](./GAME_ASSEMBLIES_AND_TOOLS.md).

---

## 1. Two type universes (why the upload uses AuraMono, not managed reflection)

Heartopia is hybrid (see GAME_ASSEMBLIES_AND_TOOLS.md):

| Universe | Holds | Reach it with |
|----------|-------|---------------|
| **IL2CPP** (`GameAssembly.dll`) | Unity engine + objects (textures, materials) | Il2CppInterop managed wrappers, `Resources.*`, `FindLoadedType` |
| **Embedded Mono** (XDENCODE modules: `EcsClient`, `XDTLevelAndEntity`, `XDTDataAndProtocol`, `XDTGameUI`, …) | **All gameplay/protocol logic** | **AuraMono** (`mono_*` exports) |

Verified by dumps: the whole protocol layer — `WebRequestUtility` and every `*NetworkCommand` —
exists only in `ilspy-dumps/` (Mono) and is **absent from `gameassembly-dumps/` (IL2CPP) and from
`BepInEx/interop`**. So managed `FindLoadedType` + `WebRequestUtility.SendCommand` returns "type
unavailable" for draw commands. We originate draw actions by calling the Mono
`DrawBoardProtoManager` static methods directly via AuraMono (those build the command and call
`WebRequestUtility` internally). The palette is the exception: `drawing_lut` is a **Unity** texture,
reachable from the IL2CPP side.

General decision table (managed vs AuraMono `SendCommand`, Reliable channel, fishing Instant Catch):
[TYPE_RESOLUTION.md](./TYPE_RESOLUTION.md) § Integration strategies → `WebRequestUtility.SendCommand`.

---

## 2. Palette (LUT)

`DrawColorCodec.TryResolveLut(destRoot, out lut, out sha)` resolves the 128-color palette, priority:

1. in-memory cache;
2. **`TryLoadLutFromUnityTextures`** (primary): `Resources.FindObjectsOfTypeAll<Texture2D>()`,
   pick by **exact name `drawing_lut`** (constant `DrawingLutTextureName`); else the material
   property **`_ColorLutTex`** (constant `DrawingLutShaderProperty`, on material `XDT/Common/Image`);
   else a heuristic scorer (logs candidates);
3. cached `ScreenCaptureDecrypted/.drawing_color_lut.png`;
4. `drawing_color_lut.png` next to the mod DLL.

Confirmed: `drawing_lut` is **128×1 RGBA32, readable**. Pixels are read with **`GetPixels32()`
directly** (readable; matches the game's `ColorLut.GetPixels()` order). `TryExtractLutPixels` falls
back to a `Graphics.Blit`+`ReadPixels` copy for non-readable textures.

> The Mono `DrawingConfig.ColorLut` (image `EcsClient`, `XDT.Scene.Shared.Data.Scriptable`) is the
> real source but Mono-only; we don't use it at runtime — the same texture is reachable on the Unity
> side as `drawing_lut`.

---

## 3. Index ↔ color codec (`DrawColorCodec`)

On-disk Draw files are **palette-index maps**, not RGB. Stored byte semantics (from
`XDTGameUI` `PaintingDetailWidget` / `DrawingPanel.GetLutColor`/`GetIndex`):

- opaque pixel byte = `0x80 | paletteIndex`
- transparent (canvas fill) byte = **124**
- **decode**: `alpha = (raw != 124) ? 255 : 0`; `rgb = lut[raw & 0x7F]`
- **encode**: `lut[idx].a == 0 ? 124 : (0x80 | idx)`; fully-transparent source pixel → 124

Methods:
- `IndexPngToColoredPng(indexPng, lut)` / `ColoredPngToIndexPng(coloredPng, lut)`
- `ColoredPngToIndexBytes(coloredPng, lut, out w, out h)` → flat R8 bytes (GetPixels32 order)
- `IndexBytesToColoredPng(indexBytes, w, h, lut)` → upright colored PNG
- `BuildPixelRuns(indexBytes, w, h)` → `Dictionary<byte, List<(int Start,int Length)>>` (see §6)

**Container format gotcha:** the index PNG must be **8-bit RGB (PNG colortype 2, R=G=B=index)**, NOT
grayscale (colortype 0). A grayscale PNG is loaded by the game **without** applying the LUT → the
drawing shows up black-and-white. C# emits `TextureFormat.RGB24`; Python uses `.convert("RGB")`.

**Orientation:** Unity `GetPixels32()` / `GetRawTextureData()` are **bottom-up** (row 0 = bottom);
flat index `i = y*Width + x`, pixel 0 = **bottom-left** (verified by sending a single pixel). Building
runs from `GetPixels32()` already matches the game → **no vertical flip**. (PIL is top-down, so the
Python codec would need a flip for upload parity.)

---

## 4. Decrypt / encrypt of the `ScreenCapture` cache

`PicturesDecryptFeature` (AES-256-CBC, key/iv = game `EncryptUtil`):
- Decrypt all → `ScreenCaptureDecrypted/`. Draw files become `Draw/<id>.png` (colored, editable)
  plus `Draw/.index/<id>.png` (original index map, for lossless roundtrip).
- Encrypt changed → re-quantizes edited colored files to the palette, rebuilds the index map, AES,
  writes back to `ScreenCapture/Draw/<id>.png`.

This only changes the **local download cache** — NOT the server. Server upload is §5–§7.

---

## 5. Server drawing model

The server stores a painting as a stream of **pixel operations** over the DrawBoard protocol; the
local cache + an OBS preview image are secondary.

| Step | Mono entry (`XDTDataAndProtocol.ProtocolService.DrawBoard.DrawBoardProtoManager`, static) |
|------|------------------------------------------------------------------------------------------|
| Start session | `SendStartDrawing(drawNetId, canvasId, artworkNetId, name, templateId)` → `StartDrawingNetworkCommand` |
| Send pixels | `DrawingOperation(Dictionary<byte,List<SequentialInfo>>, stepNum, drawingPart)` → `DrawingBatchOperationNetworkCommand` |
| End session | `SendExitDrawing()` → empty `ExitDrawingNetworkCommand` |

Command structs (image **`EcsClient`**, ns `XDT.Scene.Shared.Modules.DrawBoard`):

```csharp
enum StarDrawingType { None, Canvas, Template, Artwork }   // note spelling "Star"
struct StartDrawingNetworkCommand { StarDrawingType Type; uint DrawBoardNetId; int CanvasId;
                                    uint DrawManualArtworkNetId; int TemplateId; string DrawManualArtworkName; }
struct DrawingBatchOperationNetworkCommand { int StepNum; int DrawingPart;
                                    Dictionary<byte,List<DrawingBatchOperationSequentialInfo>> PixelDataToSequentialInfos; }
struct DrawingBatchOperationSequentialInfo { ushort Start; ushort Length; }   // RLE run
struct ExitDrawingNetworkCommand { }
```

When a drawing is **open for editing**, the game has already issued `StartDrawing`, so the mod only
needs to send `DrawingOperation` to the active session — **no net ids required for the operation**,
and `SendExitDrawing`/preview upload are handled by the game on close.

---

## 6. RLE encoding (mirror of `CanvasPainter.Save`)

`DrawColorCodec.BuildPixelRuns`: group the flat index array by stored byte; per byte, the
consecutive same-byte runs become `(Start, Length)` where **`Length = runLength − 1`** (a single
pixel → `Length = 0`), `Start` = first flat index. Both `Start` and `Length` are `ushort`, so the
canvas must be ≤ 65536 px (drawings are small, e.g. 150×150).

Reference: `XDTLevelAndEntity.GameplaySystem.Drawing.CanvasPainter.Save(int step)`
→ `DrawBoardProtoManager.DrawingOperation(dict, step, currentPart)`.

---

## 7. Class/type access (AuraMono) — the exact recipe

All helpers below are existing methods on `HeartopiaComplete` (defined in `AuraFarm.cs` /
`HeartopiaComplete.cs`); `DrawUploadFeature.cs` is a partial of the same class and calls them.

**Readiness:** `EnsureAuraMonoApiReady()` + `AttachAuraMonoThread()` (only valid in-world).

### Resolve a Mono class / DataModule singleton
```csharp
IntPtr cls = FindAuraMonoClassByFullName("Namespace.TypeName");     // classPtr (0 if missing)
IntPtr inst = TryGetAuraMonoDataModuleInstance(cls);                // DataModule<T>.Instance (get_Instance)
```

### Invoke a Mono method
```csharp
IntPtr m = FindAuraMonoMethodOnHierarchy(classPtr, "MethodName", paramCount);
IntPtr exc = IntPtr.Zero;
IntPtr ret = auraMonoRuntimeInvoke(m, instanceObjOrZeroForStatic, argsBlock, ref exc);
// 0-arg convenience: TryInvokeAuraMonoZeroArg(obj, out IntPtr ret, "MethodName")
```
**Argument marshalling** (`argsBlock` = `IntPtr*` of length N):
- value-type arg → pointer to a local (`args[i] = (IntPtr)(&intVal)`) — or `auraMonoObjectUnbox(box)` for a boxed struct;
- reference-type arg (string/object) → the MonoObject pointer **directly** (`args[i] = stringObj`).

### Read return values
- string: `TryReadMonoString(monoStringObj, out string s)`
- int/uint: `TryUnboxMonoInt32(boxed, out int)` / `TryUnboxMonoUInt32(boxed, out uint)`
- build a Mono string arg: `auraMonoStringNew(auraMonoRootDomain, "text")`

### Entity by netId → component
```csharp
TryGetAuraMonoEntityObjectByNetId(netId, out IntPtr entity);
TryAuraMonoEntityGetComponent(entity, componentClassPtr, out IntPtr component);
```

### Read/write a Mono field (incl. arrays)
```csharp
IntPtr field = auraMonoClassGetFieldFromName(classPtr, "fieldName");
IntPtr val   = auraMonoFieldGetValueObject(auraMonoRootDomain, field, obj);   // object/array
auraMonoFieldSetValue(obj, field, (IntPtr)(&value));                          // value type
// byte[] field:
int len   = (int)auraMonoArrayLength(arr);
IntPtr a  = auraMonoArrayAddrWithSize(arr, 1 /*elem size*/, UIntPtr.Zero);
Marshal.Copy(a, managed, 0, len);     // read   |   Marshal.Copy(managed, 0, a, len);  // write
```

### Build a Mono generic collection (the crash-safe way)
`mono_class_bind_generic_parameters` **crashes** on this build. Instead construct closed generics by
**assembly-qualified type-name** via `Type.GetType` + `Activator.CreateInstance`
(`CreateMonoObjectByTypeName`):
```
"System.Collections.Generic.List`1[[XDT.Scene.Shared.Modules.DrawBoard.DrawingBatchOperationSequentialInfo, EcsClient]]"
"System.Collections.Generic.Dictionary`2[[System.Byte],[<the List type above>]]"
```
Then: alloc a struct via `auraMonoObjectNew(domain, seqInfoClass)` + set `Start`/`Length` with
`auraMonoFieldSetValue`; `List.Add(auraMonoObjectUnbox(structObj))`; `Dictionary.set_Item(&keyByte,
listObj)`; finally `DrawBoardProtoManager.DrawingOperation(dictObj, &step, &part)` (static).

> `D:\...\mono\metadata\icall.c:1622:` lines in the log during these calls are the **non-fatal**
> `Array.GetValue` noise (see TYPE_RESOLUTION.md), not a crash.

---

## 8. Types & members used by the upload feature

| Full name | Image | Members used | Notes |
|-----------|-------|--------------|-------|
| `XDTLevelAndEntity.Game.Module.Draw.DrawSystem` | XDTLevelAndEntity | `get_Instance` (DataModule), `GetInteractionNetId()`→uint (private), `GetNetId(string)`→uint, `GetFirstPhotoId()`→string, `get_SelectedPaintingId`→string | `SelectedPaintingId` is **empty during edit**; `GetInteractionNetId` returns the focused board net id (0 unless a drawing is open) |
| `XDTLevelAndEntity.Gameplay.Component.Drawing.DrawingBoardComponent` | XDTLevelAndEntity | `get_CurrentPainting`→CanvasPainter, `get_DrawPhotoId`→string, `get_AuditPhotoId`→string | `DrawPhotoId` is **empty during edit**; resolved off the focused board entity |
| `XDTLevelAndEntity.GameplaySystem.Drawing.CanvasPainter` | XDTLevelAndEntity | `pixelData` (byte[] field), `get_Width`/`get_Height`→int, `Apply()`, `Save(int)` | `Texture` is R8; `pixelData = Texture.GetRawTextureData()` (bottom-up) |
| `XDTDataAndProtocol.ProtocolService.DrawBoard.DrawBoardProtoManager` | XDTDataAndProtocol | static `DrawingOperation(dict,int,int)`, `SendStartDrawing(...)`, `SendExitDrawing()` | builds + sends the network command internally |
| `XDT.Scene.Shared.Modules.DrawBoard.*` | EcsClient | command structs + `DrawingBatchOperationSequentialInfo` + enum `StarDrawingType` | used as type-name strings for generic construction |
| `drawing_lut` (Texture2D), material `XDT/Common/Image` prop `_ColorLutTex` | Unity/IL2CPP | `GetPixels32()` | the 128-color palette |

---

## 9. Extract / Upload workflow (implemented)

Two buttons in the **Pictures** tab (open the drawing at the easel first):

1. **Extract open drawing** (`DrawExtractOpenDrawing`):
   focused board (`DrawSystem.GetInteractionNetId`) → `DrawingBoardComponent` →
   `CurrentPainting` (CanvasPainter) → read `pixelData` + size → `IndexBytesToColoredPng` →
   write `ScreenCaptureDecrypted/drawing.png` (one predictable file).
2. Edit `drawing.png` externally (palette colors, hard pencil, alpha strictly 0/255).
3. **Upload drawing.png** (`DrawUploadSendForOpenDrawing`):
   `ColoredPngToIndexBytes` → `BuildPixelRuns` → `DrawingOperation` to the active session
   (size-guarded vs the open canvas). Then writes our bytes back into the live `CanvasPainter.pixelData`
   + `Apply()` so the canvas texture = our image → on close the game uploads the preview and writes
   the local `ScreenCapture` cache (`mTexture => canvasPainter.Texture`; DrawingPanel `OnPanelClose`)
   → the drawing-list thumbnail updates.
4. Close the drawing **without manual strokes**, reopen to verify.

Constraint: editing a drawing requires it to be your own and open at your easel (server validates the
board entity + ownership).

---

## 10. Gotchas / follow-ups

- Index PNG must be **RGB**, not grayscale (else in-game B/W) — §3.
- Build runs from **GetPixels32** (bottom-up) — no flip; pixel 0 = bottom-left — §3.
- `SelectedPaintingId` and board `DrawPhotoId` are **empty during edit** → read the live canvas — §7/§9.
- `mono_class_bind_generic_parameters` crashes → build generics by **type-name** — §7.
- `icall.c:1622` log lines are non-fatal — §7.
- Deploy: the running game **locks `helper.dll`** — build/copy while the game is closed.
- **Follow-ups (not done):** split the single big `DrawingOperation` into stepped chunks for
  anti-cheat (see BEHAVIORAL_ANTI_CHEAT.md); localize the two button labels.
