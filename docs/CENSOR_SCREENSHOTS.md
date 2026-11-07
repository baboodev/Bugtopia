# Content censor

Researched 2026-10-30 from `ilspy-dumps/` and `gameassembly-dumps/`. This is the game's own moderation pipeline (`CensorModule` / `CensorSystem` / `UploadMediaToAntispamNetworkCommand`). It is not the in-game camera, and the mod does not call it.

`CensorSystem.enableCensor` is hard-wired on. The getter returns `true` and never reads the backing field. Every `if (enableCensor)` in the dumps is therefore always taken. Nothing in the managed dumps assigns the property.

`CensorModule` is a `ViewModule` with `[ModuleScope(true, GameLevel_Login, GameLevel_AnimatorTest, GameLevel_SkillPreview)]`, so it is created on every other level, including `GameLevel_Main` and `GameLevel_Craft`. `OnCreate` subscribes to `ScreenshotEvent`. `OnDestroy` unsubscribes, cancels an in-flight home photo, stops the screenshot coroutine, and calls `CensorSystem.ClearResult()`.

The live OS-screenshot path does not go through `GamePhotoMode.Photograph`, `PhotoSystem.QuickSnap`, or `CanvasSnapStateRequestedEvent`. Those are the player camera. `QuickSnap` does hide the HUD, then calls `ScreenCaptureUtil.CaptureScreenshot(rect, 1920, 1080)` and `PhotoModule.TakePhoto`. The censor's own frame grab is a different method, described in section 3.

## 1. Two jobs

| Job | Entry | New pixels? | Antispam command |
|-----|--------|-------------|------------------|
| OS screenshot | `ScreenshotEvent` → `OnScreenshot` → `CaptureScreenshot` | Yes. Full backbuffer, `ThePhotoType.Screenshot` | Yes, after the audit upload |
| Photo just saved from the camera | `PhotoSystem.SavePhoto` → `CensorPhoto(localId, w, h)` | No. Re-uploads the jpg already written by `SaveLocalTextureResize3` | No. `isSend: false`, so the command is not built |
| Publish an existing photo, painting, book picture, blueprint, or build | The `Censor*` methods in section 5 | Only the homeland / blueprint / contest paths move the camera and take new frames. Photo and painting paths upload a file that already exists | Yes, when that method's send path runs |

Every command leaves through `CensorProtocolManager.Send` → `WebRequestUtility.SendCommand(UploadMediaToAntispamNetworkCommand)`.

## 2. OS screenshot trigger

The event is the empty struct `XDTBaseService.Services.Device.ScreenshotEvent`.

Two dispatches exist:

| Caller | What it does |
|--------|----------------|
| `DeviceManager.OnScreenshotReceiver(string base64Image)` | Logs `DeviceManager OnScreenshotReceiver`, drops the string, dispatches `ScreenshotEvent`. Private. No C# caller |
| `BookModule.TriggerReadingCaptureTest()` | Dispatches the same event. No caller |

The external type is `Client.__New.ScreenshotReceiver`, a `MonoBehaviour` (`Instance` at `+0x0`, `action` at `+0x18`, `foldAction` at `+0x20`). It exists twice: the IL2CPP class in `GameApp`, and a Mono proxy in `EngineWrapper` whose every method is an internal call into that IL2CPP class. Nothing in the managed dumps calls the proxy's `OnScreenshotTaken`, `RegisterListener`, or `RegisterFoldListener`.

The Il2CppDumper RVAs in `gameassembly-dumps` (`Awake` `0x233D200`, `OnScreenshotTaken` `0x233D2E0`) do not match the installed `GameAssembly.dll`. Resolved from the GameApp `CodeGenModule` method-pointer table (index = metadata token RID − 1) on this Windows build:

| Method | Token | RVA | Body |
|--------|-------|-----|------|
| `Awake` | `0x06000001` | `0x1988E60` | Writes static `Instance`. Does not assign `action` or `foldAction` |
| `RegisterListener` | `0x06000002` | `0x5AF950` | Stores the argument at `this+0x18`. The address is a shared setter (155 method-pointer slots). A call to it is not a `ScreenshotReceiver` subscriber |
| `RegisterFoldListener` | `0x06000003` | `0x5AF940` | Same store at `this+0x20`. Also shared |
| `OnScreenshotTaken` | `0x06000004` | `0x1988F40` | If `action != null`, `action.Invoke(string)`. No capture |
| `OnFoldDetector` | `0x06000005` | `0x1988EF0` | Same for `foldAction` |

The only direct `call` to `OnScreenshotTaken` (`0x1988F40`) is at `0x1F231B9`, inside `ScreenshotReceiverICallExport.icall_30667B74` (Unity2Mono token `0x06003334`, function RVA `0x1F230D0`). That icall is the body of the Mono proxy method. It resolves the IL2CPP component from the host handle, converts the incoming `ArrayRef` to an Il2Cpp string, and calls `OnScreenshotTaken(receiver, string)`. The embedded Mono runtime enters it through the reverse-pinvoke thunk at RVA `0x3F01E0`.

So the native call chain is the bridge, not an OS API:

`Client.__New.ScreenshotReceiver.OnScreenshotTaken(string)` (Mono proxy) → thunk `0x3F01E0` → `icall_30667B74` → IL2CPP `OnScreenshotTaken` → `action.Invoke(string)` when `action` is set.

`PlatformOperate.RegisterScreenshot`, `RegisterFoldListener`, and `UnRegisterScreenshot` (Client.dll tokens `0x060003CF`–`0x060003D1`) are all `ret 0` at RVA `0x5B87C0` on this build. `GameAssembly.dll`, `UnityPlayer.dll`, `xdt.exe`, and the game plugins do not contain the C or UTF-16 string `OnScreenshotTaken`. The one hit, in `VuplexWebViewChromium\libcef.dll`, sits in a Chromium feature-name table next to `PasswordProtection.VisualFeatureExtractionDuration` and is not this method. No `PrintScreen` / Steam / UnityPlayer call site reaches `OnScreenshotTaken`. On this Windows build the method runs only if something invokes the Mono proxy, and nothing in the dumps does.

The base64 argument is not decoded and not uploaded. When the handler decides to capture, it waits until the end of the frame and reads the game backbuffer itself.

## 3. When `OnScreenshot` takes a frame

`OnScreenshot` returns without capturing unless all of the following hold, in this order:

1. `CensorSystem.enableCensor` — always true (section 1).
2. The main camera is not closed. It dispatches `MainCameraClosedQueryEvent`. `UIEventBridge.OnMainCameraClosedQuery` answers `UIManager.IsCloseMainCamera()`, which is `MainCameraService.IsCameraClosed`. That flag is true while any visible `UIView` with `CloseMainCamera` is in the active set. A panel that closes the main camera suppresses this capture. The physical camera render can still be open when a close-suppressor is set; the censor reads the logical flag, not the physical one.
3. `GameWorld.IsLevel<GameLevel_Main>()` or `GameWorld.IsLevel<GameLevel_Craft>()`. On every other level the listener still exists, and the method returns here.
4. `Time.time - _lastScreenshotTime >= 0.5f`. `screenshotInterval` is `0.5f`. The stamp is written only when a capture coroutine is actually started. An event that fails the viewport test does not consume the interval. The previous coroutine is not stopped, so a second event 0.5 s later starts a second capture.
5. `GetShortIdsInViewport(fullViewport)` returns a non-empty dictionary. `fullViewport` is `(0, 0, 1, 1)`. This test does not use `HasCensorData`. One qualifying subject is enough.

`HasCensorData` is a different gate, used only by `GetTakePhotoData` (section 4).

### 3.1 Who counts as a subject

`GetShortIdsInViewport` unions two scans.

**Homeland owners** (`EncodeShortIdStateEnum.Build` = 1), from `Entities.fieldSystem.GetFields()`. A field is kept when:

- `field.running` and `homeOwnerId != 0`
- and either
  - `buildWorld.GetAllBuildCount() > 0` and at least one build's `entity.position` is inside the viewport, or
  - the build count is 0, `DataCenter.CurrentData.GetWorldWithFieldNetId(homeNetId)` is a non-empty world, and at least one entity transform in that world (composed through `fieldZone.rootMatrix` and parent transforms) is inside the viewport
- and `PlayerProtocolManager.TryGetPlayerEncodeShortId(homeOwnerId)` succeeds

There is no distance cap on this scan. One homeland owner in frame is enough to upload. Short ids are ordered by viewport x.

**Other entities**, from `Entities.SphereQueryEntities` around `EntityManager.selfPlayer.position` with radius `LevelScriptableConfig.takePictureConfig.canSeeDistanceMax`. The field default in `TakePictureConfig` is `10f`; the live value is whatever the loaded config has. If `selfPlayer` is null this half returns null, and a homeland owner can still trigger the shot.

Each entity is skipped when:

- it has `StaticEntityData`
- it has no decoded row from `TableData.TryDecodeTypeEntityData`
- the test point is outside the viewport. The point is the `head` socket when the entity has one, otherwise `entity.position`

Viewport test, for every point: `IXDTCameraManager.GetCamera().WorldToViewportPoint`. The point is inside when `x` and `y` are strictly inside the rect and `z > 0`. Points on the rect edge and points behind the camera are out.

| `EntityType` | State written | Owner id |
|--------------|---------------|----------|
| `player` | `Player` (2) | `entity.netId` |
| `car` | `Player` (2) | `LevelEntityComponentData.ownerId` |
| `placedfireworks` | `Fire` (3) | `DynamicComponentData.ownerId` |
| `drawingboard` | `DrawBoard` (4) | `DrawingBoardComponentData.ownerNetId` |

A `player` is also recorded a second time as `DrawCloth` (5) when `AvatarComponentData.GetAvatarPhotoIds` is true, which means at least one avatar recipe has a non-empty `PhotoId`.

The owner id is turned into an encode short id by `PlayerProtocolManager.TryGetPlayerEncodeShortId`. A miss drops that record. Order is viewport x.

`FilterShortId` then drops buckets:

| State | Kept when |
|-------|-----------|
| `Player` (2) | Some single `EntityType` in that bucket has count >= 5. Players and cars are counted apart, so four players plus four cars do not qualify |
| `Fire` (3) | At least 10 fireworks |
| `DrawBoard` (4), `DrawCloth` (5) | No minimum. One is enough |
| `Build` (1) | Not passed through this filter. The homeland scan already added it |

An empty result after the union means no capture.

### 3.2 The grab

`CaptureScreenshot` (`CensorModule`):

1. `using (default(CloseMemoryless))` — `Dispose` is empty.
2. `yield return null`
3. `yield return new WaitForEndOfFrame()`
4. `ScreenCaptureUtil.CaptureScreenshot()` with no rect. That is the parameterless native method (IL2CPP RVA `0xE568F0`), not the `(Rect, width, height)` overload `QuickSnap` uses. The C# body is not in the dump. The call site passes no crop, so the whole frame is the input.
5. `new PhotoCensorUploader(shortIds, photo)`, which sets `ThePhotoType.Screenshot` and uploads the texture (section 6).
6. `yield return null`, then the `using` ends.

`WaitForEndOfFrame` runs after cameras and screen-space UI for that frame. Anything already composited into the backbuffer, including the game HUD and a mod overlay canvas, is in the texture. This path never dispatches `CanvasSnapStateRequestedEvent`, so the game does not hide its HUD for it.

The texture is destroyed in `CensorUploaderBase.OnSucceed` / `OnFailed` after the upload callback.

### 3.3 Command for this path

`PhotoCensorUploader.FillParam` for the screenshot constructor:

| Field | Value |
|-------|--------|
| `ImagesInfo` | One entry. `ImageUrl` is the OBS url of the audit jpg. `paramsList` is omitted (`_isNeedParams` stays false) |
| `EncodeShortIds` | The viewport dictionary, one `EncodeShortIdInfo` per short id. `State` is the enum int |
| `ImgFrom` | `6` (`UgcScreenshot`) when `BookSystem.IsBookReadingPanelOpen` and `CurrentReadingUgcBookNetId != 0`. Otherwise `3` (`Photo`). When the book branch is taken, `paramsList` is replaced with `["1", ugcBookPublishId]` |
| `PhotoType` | `2` (`Screenshot`) |
| `TaskType` and every build counter | Left at 0 |

`ImagesInfo` is capped at 10 and `EncodeShortIds` at 20 on the network reader. This path sends one image and does not trim the short-id list before send.

## 4. Saving a camera photo

`PhotoStatusPanel.InputSnap` (button release, and only when `ShowUIPanel` is not open) calls `CanvasUtility.Snap`. That reaches `PhotoSystem.QuickSnap`, which hides HUD canvases, captures a 16:9 rect at 1920×1080, restores the HUD, and calls `PhotoModule.TakePhoto`. `PhotoModule.TakePetPhoto` also calls `TakePhoto`.

`PhotoModule.TakePhoto` fills `PhotoInfo` from the framed entity (other player, npc, pet, homeland, pet-time, wild animal), area, time of day, weather, then calls `CensorModule.GetTakePhotoData(texture.width, texture.height)` and, when that returns a non-empty list, stores it on `photoInfo.EncodeShortIds`. The `finally` always calls `PhotoSystem.SavePhoto`.

`GetTakePhotoData` builds the same viewport dictionary as section 3, but the rect is the centered crop `(photoWidth / Screen.width)` by `(photoHeight / Screen.height)`, each axis clamped to 1. It returns serialized short ids only when `HasCensorData` is true:

- any `Build`, `Fire`, `DrawBoard`, or `DrawCloth` key is present, or
- the `Player` key is seen 5 times

The dictionary has one key per state, so the player arm never reaches 5. A frame that contains only players, even five or more, does not get `EncodeShortIds` on the saved photo. A single homeland, firework bucket that survived the filter, drawing board, or photo-cloth does.

`SavePhoto` writes the texture with `LocalTextureCacheUtility.SaveLocalTextureResize3`, stores `PhotoData`, sends `PhotoProtocolManager.TakePhoto`, then calls `CensorPhoto(localId, width, height)`.

That overload calls `GetShortIdsInPhoto` (the cropped viewport, without `HasCensorData`). When the dictionary is non-empty it constructs `PhotoCensorUploader(ThePhotoType.TakePhoto, localId, shortIds, isSend: false)`. `isSend: false` still calls `UploadSystem.UploadTexture(localId, UploadTextureType.TakePhoto, isAudit: true)`, and `OnSucceed` does not call `UploadSucceed`, so `UploadMediaToAntispamNetworkCommand` is not sent. The jpg is copied into the audit bucket. The short ids ride along only later, when a publish path loads `PhotoInfo.EncodeShortIds`.

## 5. Publish and build paths

`CensorPhoto(serverId, localId, needExtraInfo)` does not take a frame. When `needExtraInfo` is true and `PhotoSystem.TryGetPhotoInfo(localId)` hits, it deserializes `photoInfo.EncodeShortIds`. It then constructs `PhotoCensorUploader(ThePhotoType.PhotoFrameOrBG, serverId, shortIds, isSend: true)`. For any photo type other than `TakePhoto` the constructor ignores `isSend`, sets `_count = 1`, and calls `UploadSucceed(serverId)` directly: the string is treated as an object id already on OBS. `FillParam` sets `PhotoType = 3` (`PhotoFrameOrBG`), `_isNeedParams = true` so `paramsList` is the object id, and `ImgFrom` is 6 or 3 by the same book-reading rule as section 3.3.

`needExtraInfo` is `!DrawSystem.IsDrawingPhotoId(localId)` at every call site except the profile background, which passes `true` unconditionally.

| User action | Method | Censor call | Extra condition |
|-------------|--------|-------------|-----------------|
| Notebook save, photo tab (`NotebookSidePanel.OnClickSaveBtn`) | Drawing id: `UploadAndSavePhoto` → `CensorPainting(id, UploadTextureType.Notebook)`. Album photo: upload as `PhotoFrame`, then `PhotoUploadFinished` → `CensorPhoto` | Painting path does not wait; the `UniTask` is discarded | Album path bails out while `photoFrameIsUploading` (loc 92465, "Inserting Photo") |
| Photo frame confirm (`PhotoFrameEditPhotoPanel`) | Drawing id skips the upload and calls `PhotoUploadFinished` with the painting id. Album photo uploads as `PhotoFrame` first | `CensorPhoto` after `HomelandProtocolManager.EditPhotoFrame` | Same uploading toast |
| Profile background (`PersonalInformationEditPhotoPanel`) | `UploadTexture(..., UploadTextureType.Other, isAudit: false)` then `CensorPhoto(serverId, localId, needExtraInfo: true)` | Always asks for stored short ids | Unchanged background (`EditBackgroundPhoto` of the current url) does not censor |
| Party photo (`PartyEditPhotoPanel._SaveBtnOnClickHandler`) | A new `Texture2D` uploads and calls the `PhotoUpdateFinished(string, Vector2)` overload, which does not censor. An album id uploads with thumbnail and calls `PhotoUpdateFinished(string, (Vector2, string))` | `CensorPhoto` only for the album-id arm | |
| Dance UGC cover (`DanceUgcPublishTarget.PrepareCustomCoverAsync`) | Official cover: no censor. Painting: `CensorCover(assetId, assetId)` immediately. Photo: upload as `DanceScoreCover`, then `CensorCover(obsId, localId)` | `CensorPhoto` | |
| Record cover (`RecordMusicPanel.UploadTextureBeforeMakeRecordIfNeed`) | `draw`: `CenserTexture(id, id)` then `MakeRecord`. `photo`: upload as `RecordCover`, then `CenserTexture`. `officialRecordCover`: `MakeRecord` only | `CensorPhoto` for draw and photo | |
| Micro-homeland show photo (`MicroHomelandEditPhotoPanelLogic.OnPhotoUploaded`) | After `UploadTextureWithThumbnail(..., MicroHomeland, isAudit: false)` and `SetMicroHomelandPhoto` | `CensorPhoto` | |
| UGC marker note (`UgcMarkerNoteEditPanelLogic`) | A non-drawing photo id is stored as pending. `OnPlaceUgcMarkerFinished` on success calls `SubmitPendingPhotoReview` | `CensorUgcPhotoAsync(..., PlayerUgcType.Marker, markerId)` | Runs once (`_photoReviewSubmitted`). Empty server id, `PlayerUgcType.Invalid`, or `Guid.Empty` return immediately. A drawing photo is not queued |

### 5.1 `CensorUgcPhotoAsync`

Does not take a frame. Resolves `IObsSDKManager.GetObsUrl(photoServerId)`. Loads short ids from `PhotoInfo` when the local id is known. Sends one `UploadImgInfo` whose `paramsList` is `[(int)ugcType, ugcId]`. `ImgFrom = 6`, `PhotoType = 3`. Failures are logged and swallowed.

### 5.2 `CensorPainting`

Loads the drawing texture from `DrawSystem.GetDrawTexture`. Converts R8 to RGBA with `DrawingConfig.ColorLut` via `DrawSystem.R8ToRGBATexture`. Uploads with `isAudit: true` and `filename = ParsePrimaryId(photoId)` (the substring between `@` and `#`; empty when either marker is missing). On success, resolves the OBS url and sends `ImgFrom = 3`, `PhotoType = 4` (`Drawing`), `paramsList = [objectId]`. Then `DrawSystem.RestoreDrawCache(photoId)`.

### 5.3 `CensorBookPicture`

Called from `BookPublishPanel._SaveAndPublish` when `BookModule.IsUgcBookPictureUploadSwitchOn()` and `BookProtocolManager.SendQueryBookGuid` succeeds. Cover and chapter pictures tagged `photo` or `painting` are collected. `official` pictures are skipped. Also called from `ReportPanel.HandleBookCoverReport` for a reported book cover (`photo` or `painting`). A reported book page uses `PhotoModule.PostReportPhoto` instead and does not enter `CensorModule`.

The `bookGuid` argument is unused.

Photos: entries whose id contains `"png"` are skipped. Each other id is uploaded as `UploadTextureType.Book`, `isAudit: true`, then sent with `ImgFrom = 3`, `PhotoType = 3`, `paramsList = [objectId]`.

Paintings: same LUT conversion and upload as `CensorPainting`, `UploadTextureType.Book`, then `ImgFrom = 3`, `PhotoType = 4`.

### 5.4 Homeland, blueprint, micro-homeland, contest

These four move the main camera and take new frames. Each one calls `CancelTakePhoto()` first, which cancels and disposes `_takePhotoCts`.

Shared early exit for homeland, micro-homeland, blueprint, and micro-homeland blueprint: `BuildModule` is non-null and `IsBuildOwner()` is false. `IsBuildOwner` is false only in `CraftType.Multi` when `OwnerPlayerNetId` is not the local player. A single-player craft is always the owner. Contest does not check ownership.

`TakeHomePhoto` (`PhotoModule`) is what actually grabs pixels:

- Sets the photo rect to the full screen (`isForce_16_9` is false on every censor call, so there is no 16:9 crop).
- Requires a main camera. Switches it to `CameraRuntimeMode.CutScene`, applies the game FOV, forces main-camera rendering on, and disables `LayerDistanceCulling`.
- Resolves `target` or `HomelandEntitySystem.GetSelfField()`. A missing homeland or `fieldLevelObject` produces an empty list and no upload.
- When `isHideTree` is true (micro-homeland edit only), disables dynamic instance-block trees for the grab and restores them after.
- For each `HomeSystemConfig.photoPositions4Censor` entry, moves the main camera to that position in the field rect and takes one frame.
- For each `planeHeights4Censor` value that is `<= topmostBuildY`, moves the camera to local `(0, photoHeight4Censor, 0)` with rotation `(90, 90, 0)` (top-down), turns on build layers up to that height (`Floor(height / 0.5) + hideLayerOffset`), and takes one frame. Afterwards `SetVisibleLayer(MaxLayerNum, switchOn: false)`.
- Each frame is `Texture2D(Screen.width, Screen.height, RGB24)` via `ReadPixels` from `OnPostRender` (`RenderPipelineManagerExternal` end-camera callback).
- Restores camera position, rotation, and `CameraRuntimeMode.Normal`, re-enables distance culling.

`HomeSystemConfig` defaults: `photoPositions4Censor` and `planeHeights4Censor` empty, `photoHeight4Censor = 60f`. The live lists are the loaded scriptable. Both lists empty means zero photos and no uploader.

Sensitive counts (`StatisticsSensitive`) run only when a command will be sent. They walk the build enumerator and skip `FakeDestroyed` and `IsPlace` pieces. Against `HomeSystemConfig`:

- `sensitivePercent` = `sensitiveBuildingMaterialIds` hits × 100 / `allBuildCount` (integer). The denominator is the count passed in, including skipped pieces.
- `alertLevel1TotalCount` / `alertLevel2TotalCount` = hits in `alertLevel1Ids` / `alertLevel2Ids`.
- `alertLevel1MaxCount` / `alertLevel2MaxCount` = the highest count of a single static id in that list.

Those become `BuildingMaterialCount`, `BuildDangerStructureCount`, `AlertLevel1*`, `AlertLevel2*` on the command. `PhotoType` stays 0.

Daily gate `CheckNeedCensor(key)` returns false when either:

- `PlayerPrefs` int `key` is a non-zero unix time, and `GameTimeUtility` says that time and `GetCurrentGameTimeMs()` share year, month, and day, or
- `CensorSystem.GetCensorUploader(key)` is non-null and `IsUploading(key)` (`_counter < _count`)

Keys, all suffixed with `PlayerDataCenter.GetLoginInfo().ShortId`:

| Key prefix | Shared by |
|------------|-----------|
| `HomelandCensorTimestamp` | `CensorHomeland` and `CensorBlueprint` |
| `MicroHomelandCensorTimestamp` | `CensorMicroHomeland` and `CensorMicroHomelandBlueprint` |
| `UgcBuildContestCensorTimestamp` | Written by contest success. Contest does not read the gate before sending |

When the gate says no, the method still takes the photos and constructs the uploader with `isSend: false`. Those jpgs are uploaded to the audit bucket and the antispam command is not sent. The daily stamp is not rewritten. When the gate says yes, the uploader is stored on `CensorSystem` under that key and `OnCensorSucceed` writes `PlayerPrefs` `(int)GameTimeUtility.GetUnixTime()` after the command is sent. Unix time 0 is not stored. The log line says the review passed; the write happens when the client finishes the upload, before any server verdict.

`Done()` sends the command when `_counter >= _count` and at least half the urls were collected (`_urls.Count >= _count / 2`, integer division), and `_urls` is non-null. A single failed upload leaves `_urls` null, so a one-image job that fails does not send. A multi-photo job can send with only half the frames.

| Call | Who starts it | Gate beyond owner / daily | `ImgFrom` | Upload type | `paramsList` |
|------|---------------|---------------------------|-----------|-------------|--------------|
| `CensorHomeland(changeStep, topY, count, enumerator)` | `SandboxCraftModule.CensorAction` while leaving `GameLevel_Craft` (`OnRelease` sequence: save, censor, bake; action timeout 30 s). Runs when it is not the blueprint-verify arm and `buildWorld.GetChangeStep() > 0` and `CheckMicroHomeland` is false | `changeStep` is `BuildOperationTimes`. Trees stay visible | `Homeland` (1) | `Homeland` | none |
| `CensorMicroHomeland(itemNetId, ...)` | Same action, when `CheckMicroHomeland` returns an item net id | Trees hidden. Subdirectory is the item guid when it is not empty. `AddParam(itemNetId)` | `MicroHomeland` (4) | `MicroHomeland` | item net id |
| `CensorBlueprint(blueprintId, ignoreNeedCensorCheck = false)` | Leaving craft after blueprint verify, with `ignoreNeedCensorCheck: true`. Also `BuildModule.ShowLoading` after a successful blueprint apply (`BlueprintApplyPanel.ApplyBlueprint`), with the daily gate | Verify arm: `BlueprintVerifyStatusPanel.ClickExit` calls `MarkBlueprintVerifyExitPendingCensor` only when `TableHomeLandSandBoxPlan.type == 1`, and `TryConsumeBlueprintVerifyPendingCensor` matches that id once. `BuildOperationTimes` is 0. Trees stay visible | `Blueprint` (2) | `Homeland` (inherited) | blueprint id |
| `CensorMicroHomelandBlueprint(itemNetId, blueprintId)` | `ShowLoading` when the apply is inside a micro-homeland | Daily gate. Subdirectory is the item guid. Params are item net id then blueprint id | `MicroHomelandBlueprint` (5) | `MicroHomeland` | both ids |
| `CensorUgcBuildContest(ugcType, workId, contestId, topY, count, enumerator, target)` | `StarTownReconnect.CensorAction` during the reconnect fade-in (timeout 30 s) | Level is `BuildCompetitionView`. `LevelExtraParam` parses as the community guid. `IHomelandClientService.TryGetBuildRootNetId` and the field exist. `CommunityWorkGameDataCenter` has the work and `CommunityWorkInfoComponent`. `IsExhibitionPhase(contestId)` (`ContestPhaseType.Exhibition`). Homeland component for that build root exists. Call site passes `ugcType = 3` and `contestId = CommunityParams`. No daily gate and no owner check. Trees stay visible. `BuildOperationTimes` is 0 | `UgcBuildContest` (7) | `UgcBuildContest` | ugc type, work id, contest id |

`ShowLoading` waits for `PlayerUseHouseBluePrintEvent` for the local player, bakes, then censors only when `_lastErrorCode == BluePrintErrorCode.Success`.

## 6. Upload details

`UploadSystem` with `isAudit: true`:

- Object id prefix `audit/` instead of `normal/`, suffix `.jpg` instead of `.png`. Shape: `audit/{encodeShortId}/{UploadTextureType}/{fileName}.jpg`. An empty file name becomes `long.MaxValue - DateTime.Now.ToFileTime()` so audit names sort opposite to normal ones. A name that contains a slash or a dot is rejected.
- The texture is fit inside `AuditMaxSize` `(750, 421)` via `ScreenCaptureUtil.SaveTextureAndResize` when it is larger. Encode is JPG (`GetStreamAndDealTexture` uses JPG when the audit flag is set).
- A non-audit upload used by some panels before the censor call (profile background, micro-homeland show photo, the first party texture) is a separate object. The censor command then references the id that upload returned.

`CensorUploaderBase.UploadSucceed` turns the object id into an OBS url with `IObsSDKManager.GetObsUrl` and only then lets `CheckFinish` reach `Done()`.

## 7. What the server answer does on the client

`CensorSyncSystem.Init` listens for `NetworkEvent<NotifyToClientAntispamTaskResultEvent>` and re-dispatches it as `Event<NotifyToClientAntispamTaskResultEvent>`. The matching listener for `UploadMediaToAntispamNetworkEvent` is empty.

`CensorSystem.OnNotifyToClientAntispamTaskResult` stores the payload. It is not shown immediately. The next `CensorModule.OnAfterLevelLoaded` reads `TryGetResult` and, 3000 ms later (`UniTask.Delay`), dispatches `UiAlertSingleConfirmShowEvent` for each hit that is set. Several hits produce several dialogs. The payload is cleared after the dialogs are scheduled. `OnDestroy` also clears it, so a teardown of `CensorModule` before the next level's `OnAfterLevelLoaded` drops a stored result.

The client method does not delete files or reset the build. It only shows the string. The lists name what the server already cleared.

| Payload | Loc id | English text |
|---------|--------|----------------|
| `NeedCleanHomeLand` | 93428 | Violation Notice. Your violating Home has been reset. All related Furniture and Building Materials have been fully returned. |
| `CleanBluePrintIds` (non-empty) | 93430 | Violation Notice. Your violating Blueprint [{0}] has been reset. `{0}` is the ids joined with `,` |
| `CleanPhotosIds` (non-empty) | 93432 | Violation Notice. Some of your Photos were found violating rules and have been deleted. |
| `CleanMicroHomeItems` (non-empty) | 93435 | Violation Notice. Your violating Island has been reset. All related Furniture and Building Materials have been fully returned. |
| `CleanArtworks` (non-empty) | 94329 | Violation Notice. Your violating Painting has been reset. |

`RecordCensorPassedNode` / `RecordCensorPassedTabNode` are red-point nodes for `RedPointEnum.UgcRecordCensorPassedTab`. They are a server badge, not a capture.

## 8. Enums on the command

`UploadMediaToAntispamNetworkCommand.ImgFromType`: `None=0`, `Homeland=1`, `Blueprint=2`, `Photo=3`, `MicroHomeland=4`, `MicroHomelandBlueprint=5`, `UgcScreenshot=6`, `UgcBuildContest=7`.

`ThePhotoType`: `None=0`, `TakePhoto=1`, `Screenshot=2`, `PhotoFrameOrBG=3`, `Drawing=4`, `DrawingUgc=5`. `DrawingUgc` is not written by `CensorModule`.

`EncodeShortIdStateEnum`: `Build=1`, `Player=2`, `Fire=3`, `DrawBoard=4`, `DrawCloth=5`.

`UploadTextureType` values used here: `TakePhoto`, `Homeland`, `Other`, `MicroHomeland`, `Book`, `Notebook`, `UgcBuildContest`, plus the pre-upload types `PhotoFrame`, `RecordCover`, `DanceScoreCover`.

## 9. Source files

| Piece | Path |
|-------|------|
| Listener, capture, every `Censor*` method, viewport scan | `ilspy-dumps/XDTLevelAndEntity/XDTLevelAndEntity.Game.Module.Censor/CensorModule.cs` |
| Screenshot and published-photo command | `ilspy-dumps/XDTLevelAndEntity/XDTLevelAndEntity.Game.Module.Censor/PhotoCensorUploader.cs` |
| Shared upload / half-success send | `ilspy-dumps/XDTLevelAndEntity/XDTLevelAndEntity.Game.Module.Censor/CensorUploaderBase.cs` |
| Homeland and the subclasses (blueprint, micro, contest) | `ilspy-dumps/XDTLevelAndEntity/XDTLevelAndEntity.Game.Module.Censor/` |
| `enableCensor`, stored server result | `ilspy-dumps/XDTGameSystem/XDTGameSystem.GameplaySystem.Censor/CensorSystem.cs` |
| `Send` → `WebRequestUtility.SendCommand` | `ilspy-dumps/XDTDataAndProtocol/XDTDataAndProtocol.ProtocolService.Censor/CensorProtocolManager.cs` |
| Network event → `EventCenter` | `ilspy-dumps/EcsSystem/EcsSystem.ClientSystem.Censor/CensorSyncSystem.cs` |
| Command, enums, result payload | `ilspy-dumps/EcsClient/XDT.Scene.Shared.Modules.Antispam/` |
| Event | `ilspy-dumps/XDTBaseService/XDTBaseService.Services.Device/ScreenshotEvent.cs` |
| Managed dispatch | `ilspy-dumps/XDTBaseService/XDTBaseService.Services.Device/DeviceManager.cs` |
| Native-facing callback | `gameassembly-dumps/GameApp/Client.__New/ScreenshotReceiver.cs` |
| Mono wrapper | `ilspy-dumps/EngineWrapper/Client.__New/ScreenshotReceiver.cs` |
| Backbuffer read | `ilspy-dumps/EngineWrapper/Client.BaseService.Utility.File/ScreenCaptureUtil.cs` |
| Audit object id and 750×421 fit | `ilspy-dumps/EcsClient/XDT.Scene.Shared.Utils/UploadSystemUtil.cs`, `ilspy-dumps/XDTLevelAndEntity/XDTGUI.Module.Upload/UploadSystem.cs` |
| Home camera move and `ReadPixels` | `ilspy-dumps/XDTLevelAndEntity/XDTLevelAndEntity.Game.Module.Photo/PhotoModule.cs` (`TakeHomePhoto`) |
| Camera-photo save | `ilspy-dumps/XDTLevelAndEntity/XDTGameSystem.GameplaySystem.Photo/PhotoSystem.cs` (`SavePhoto`, `QuickSnap`) |
| Craft-exit censor | `ilspy-dumps/XDTLevelAndEntity/XDTLevelAndEntity.GameplaySystem.HomeLand/SandboxCraftModule.cs` |
| Blueprint-apply censor | `ilspy-dumps/XDTLevelAndEntity/XDTGUI.Module.Build/BuildModule.cs` (`ShowLoading`) |
| Contest censor | `ilspy-dumps/XDTLevelAndEntity/XDTLevelAndEntity.GameLevels/StarTownReconnect.cs` |
| Main-camera-closed answer | `ilspy-dumps/XDTGameUI/XDTGame.Core/MainCameraService.cs`, `ilspy-dumps/XDTGameUI/XDTGame.UI/UIEventBridge.cs` |
| Censor camera positions and sensitive id lists | `ilspy-dumps/EcsClient/EcsClient.XDT.Scene.Shared.Data.Scriptable/HomeSystemConfig.cs` |
| Player query radius default | `ilspy-dumps/XDTLevelAndEntity/ScriptsRefactory.LevelAndEntity.Utils/TakePictureConfig.cs` |
