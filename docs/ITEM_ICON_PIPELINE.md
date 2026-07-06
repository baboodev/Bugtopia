# Item Icon Pipeline — Direct In-Game Loading

Research (July 2026): how to replace the disk PNG icon cache (Bag tab / Auto Sell tab of the
mod overlay) with icons loaded directly from the game. **Implemented, confirmed in-game and made
the ONLY tile icon source (July 2026)** in `buddy/HeartopiaComplete.GameIcons.cs`: request-once
direct loads fire from the `TryGetAutoSellItemTexture` miss path (AutoSell grid + Bag/Warehouse
grid) and the pet-feed icon resolver, always-on (no toggle). The in-game probe validated the three
research risks — the `DelegateSupport` callback mode delivers sprites and table-cased keys resolve —
after which the temporary `TEST ICONS` probe, the `Direct Game Icons` toggle, the legacy
`CACHE ICONS` open-bag harvest (`ScanBagForAutoSellItems` + star/stack UI readers) and the
loaded-sprite prime pass (`PrimeAutoSellItemTextureCache`) were all removed. Tile lookups no longer
read the disk PNG cache; `SaveCachedItemIcon` still writes fresh copies because the radar ESP icon
path reads that cache. §1 below is the pre-removal state (historical); §2–§3 record the verified
game pipeline the implementation follows.

See also: [BACKPACK_AND_ITEMS.md](./BACKPACK_AND_ITEMS.md), [TYPE_RESOLUTION.md](./TYPE_RESOLUTION.md),
[DECOMPILED_SOURCE_MAP.md](./DECOMPILED_SOURCE_MAP.md).

---

## 1. Legacy mod pipeline (removed July 2026 — kept for history)

| Piece | Location | Notes |
|-------|----------|-------|
| Disk cache | `%LocalAppDataLow%\Bugtopia\Cache\ItemIcons\<key>.png` | `GetItemIconCacheDirectory` (HeartopiaComplete.Radar.cs:4057), `TryLoadCachedItemIcon` / `SaveCachedItemIcon` (Radar.cs:4077/4187) |
| Key format | normalized sprite name | `NormalizeAutoSellMatchKey` (AutoSell.cs:1597) strips `ui_item_normal_` / `ui_item_special_` / `sprite_`, lowercases |
| Population | open-bag UI scan | `ScanBagForAutoSellItems` (AutoSell.cs:4280) harvests `Image.sprite` from the game BagPanel; user must open bag / press CACHE ICONS |
| Fallback | loaded-sprite scan | `PrimeAutoSellItemTextureCache` (AutoSell.cs:5017) / `TryResolveRadarIconFromLoadedSprites` (Radar.cs:593) — `Resources.FindObjectsOfTypeAll<Sprite>()` by name, only works if the game already loaded the sprite |
| staticId → key map | `radarStaticIdToIconKey` + `radar_species_icons.txt` | populated from AuraMono backpack descriptors (Transfer.cs:838) |
| Sprite → Texture2D | `CopySpriteTexture` (UiKitPrimitives.cs:640) | RenderTexture blit + ReadPixels (handles non-readable textures); no sub-rect crop — OK, see §2.4 |
| Consumers | AutoSell grid, Transfer (Bag/Warehouse) grid, PetFeed, AutoEatRepair, Radar ESP | all go through `TryGetAutoSellItemTexture` / `TryLoadCachedItemIcon` aliases |

Limitations: icons appear only after the player opened the bag once (per icon), PNG cache goes stale
after art updates, star/step icon variants collide on one key.

---

## 2. Game pipeline (verified in dumps)

Chain for a bag cell (all files under `ilspy-dumps/`):

```
staticId
  → BackpackItem.icon = GetBackpackIcon(staticId, starRate, step, netId, giftType)
      XDTGameSystem/XDTGameSystem.UISystem.BackPack/BackpackItem.cs
  → RewardUtility.GetIconName(staticId, step)          ← static, assembly XDTDataAndProtocol
      XDTDataAndProtocol/XDTGameSystem.Utilities/RewardUtility.cs:746
      (honors TableData.TableEntityOverrideIcons, blueprint/cookbook/puzzle specials,
       else TableData.GetIconId(entityType, id, step) → string)
  → NewPackWidget.SetIcon(AtlasEnum.NormalItem /*103*/, icon)
      XDTGameUI/XDTGUI.View/PackItemDecoratorNew.cs:61
  → CustomImage.SetIcon → UIManager.Instance.dynamicResource.GetSprite(AtlasSpriteID)
      XDTGameUI/XDTGUI.View.Components/CustomImage.cs:149
  → UIResourceLife.GetSprite → DynamicAtlasProxy.GetSprite(name)
      XDTLevelAndEntity/XDTGUI.Core.ResourceLife/UIResourceLife.cs:58, DynamicAtlasProxy.cs:98
      *** full asset key = "ui_item_normal_" + iconName ***
  → DynamicSpriteLoader.Load → M<IResourceManager>.Inst.LoadSpriteAsync(key, cb)
      XDTViewBase/XDTViewBase.Loader/DynamicSpriteLoader.cs:20
  → ResourceManager.LoadSpriteAsync(string, Action<Sprite>)   (Manager in Managers._serviceDic)
      XDTBaseService/XDTBaseService.Services.ResourceManager/ResourceManager.cs:276
  → ResManager.LoadSpriteAsync — icall_EB2F0B41 → IL2CPP engine side
      EngineWrapper/ScriptsRefactory.ResSystem/ResManager.cs:364
```

Key facts:

1. **The real loader is IL2CPP-side**: static `ScriptsRefactory.ResSystem.ResManager`
   (assembly **`ResManager`**, `gameassembly-dumps/ResManager/...`). Interop stubs exist under both
   loaders: `BepInEx/interop/ResManager.dll` and `MelonLoader/Il2CppAssemblies/Il2CppResManager.dll`.
   Relevant statics: `int LoadSpriteAsync(string name, Action<Sprite> cb)`, `bool HasAsset(string)`,
   `bool UnLoadAsync(Object asset)` / `bool UnLoadAsync(int token)`, `Cancel` via `UnLoadAsync(token)`.
2. **Asset key** for item icons: `"ui_item_normal_" + RewardUtility.GetIconName(staticId, step)`
   (`GetRewardItemIcon` in RewardUtility.cs:680 concatenates exactly this). `ui_item_special_` does
   **not** exist in current dumps (legacy prefix kept in mod normalization — harmless).
3. **No runtime atlas packing.** Despite the name, `DynamicAtlasProxy` receives a **standalone Sprite**
   per icon and renames it to the full key (`sprite.name = "ui_item_normal_..."`,
   DynamicAtlasProxy.cs:66) before applying it to `Image`. That is why the bag-scan harvest sees these
   names, and why `CopySpriteTexture` without a sub-rect crop produces correct icons.
4. **Refcounted lifetime.** `SpriteProxy.RemoveFrom` → `DisposeSprite` → `UnLoadAsync` once no UI uses
   the sprite. The mod must **copy the texture** (existing `CopySpriteTexture`) and must not hold the
   game's Sprite/Texture long-term.
5. There is **no `LoadSpriteSync`**; sprites are async-only. `ResManager.Tick` is pumped by
   `ResourceManager.Update` on the main thread → load callbacks fire on the main thread.
6. Star/quality: `GetStep` (`HomelandProtocolManager.QualityToStep`) matters only for a few entity
   types (homeland blueprint steps etc.); most items have step 0 — same icon for all star rates
   (matches the mod's observed "same icon, multiple star versions").

---

## 3. Recommended design (not yet implemented)

### staticId → iconName

- Primary: AuraMono static invoke `XDTGameSystem.Utilities.RewardUtility.GetIconName(int, int)`
  (image `XDTDataAndProtocol`, static — no instance, string return; cache per staticId).
- Already-available shortcut: the AuraMono backpack enumeration already reads the `icon` member of
  `BackpackItem` into descriptors (Transfer.cs / GetManagedBackpackItemDescriptor) — keep using it
  where a live item object is at hand; keep exact case (icon names are lowercase in practice, but do
  not force-lowercase the **load** key — mod dictionary keys stay normalized/lowercased as today).

### iconName → Sprite (two options)

- **Option A — interop direct (preferred, no AuraMono):** resolve
  `ScriptsRefactory.ResSystem.ResManager` via `FindLoadedType("ScriptsRefactory.ResSystem.ResManager",
  "Il2CppScriptsRefactory.ResSystem.ResManager")`; `HasAsset(key)` first, then
  `LoadSpriteAsync(key, callback)`.
  - Callback = managed handler converted to `Il2CppSystem.Action<Sprite>` (Il2CppInterop
    `DelegateSupport`). **Root the converted delegate** (static map token→delegate) until the callback
    fires — a collected delegate thunk is a native crash. New pattern for this codebase (no
    DelegateSupport use yet) — if it misbehaves, fall back to callback-null variant below.
  - Degraded variant (no delegate): call `LoadSpriteAsync(key, null)` fire-and-forget, then pick the
    sprite up by name via the **existing** loaded-sprite rescan 0.5–1 s later (verify null-callback
    tolerance in-game first; the icall wraps the handle, Mono side passes it opaquely).
- **Option B — AuraMono Mono-side:** get `IResourceManager` instance via the proven
  `Managers._serviceDic` pattern (dictionary key = `System.Type` via `mono_type_get_object`, then
  `.manager` — see the reach-manager-via-servicedic pattern), invoke
  `LoadSpriteAsync(string, Action<Sprite>)` with a **null** callback (fire-and-forget; we cannot pass
  a CoreCLR delegate into embedded Mono), then pick up by name via rescan. Do **not** go through
  `UIManager.Instance.dynamicResource` (`UIManager.get_Instance` is null on this build; SpriteProxy
  refcounting is a trap).

### Sprite → mod texture

Unchanged: `CopySpriteTexture` → in-memory dict (`autoSellBagItemTextures`) under today's normalized
keys; then `ResManager.UnLoadAsync(token)` to release the load ref. Disk PNG cache becomes an optional
L2 (instant icons before world load) or can be removed.

### Integration points

`TryGetAutoSellItemTexture` miss → request-by-staticId (once, with failure throttle — reuse
`autoSellLoadedSpriteResolveFailures` / `FeatureBreakerState` patterns); UI keeps showing initials
until the callback lands. Same hook for Transfer grid, PetFeed, AutoEatRepair, Radar item icons.
Cancel pending tokens on world change.

### Risks / in-game probes before wiring everything

1. Does `LoadSpriteAsync` fire the callback with `null`/at all on a bad key? (Guard with `HasAsset`,
   add a timeout.)
2. Key case sensitivity (`ResIndex.ResInfo.GetStringHashCode`) — probe `HasAsset` with exact vs
   lowercased key once.
3. DelegateSupport conversion pattern under **both** loaders (BepInEx first; MelonLoader namespaces
   differ only by `Il2Cpp` prefix).
4. `GetIconName` special types (currency uses `currency_<id>`, avatar/expression/etc. have their own
   patterns) — for bag items the plain `GetIconName` path is the one BagPanel itself uses.

## 4. Implementation notes (Phase 1, `HeartopiaComplete.GameIcons.cs`)

- Request-once state machine: `gameIconPendingByKey` (dedupe + 24-load cap) / `gameIconFailedRetryAt`
  (45 s cooldown) / 12 s timeout with token release; per-frame drain `ProcessGameIconLoads()` runs
  from `OnUpdate` behind a `FeatureBreakerState`, no-op while idle.
- Callback mode: managed closure → `DelegateSupport.ConvertDelegate` (via `MakeGenericMethod` on the
  reflected `Action<Sprite>` parameter type); both delegates stay rooted in the pending record until
  the callback fires. First conversion failure flips the session to **poll pickup** mode
  (`LoadSpriteAsync(key, null)` + name-prefix scan of `Resources.FindObjectsOfTypeAll<Sprite>` every
  0.6 s while anything is pending).
- Completion: `CopySpriteTexture` copy → stored under all alias keys in `autoSellBagItemTextures` +
  `SaveCachedItemIcon` (disk stays as L2) → `UnLoadAsync(token)`. World-epoch change cancels and
  releases all pending tokens.
- `RewardUtility.GetIconName` results are cached per staticId and fed into
  `RememberRadarStaticIdIconMapping` (so radar/pet-feed alias lookups benefit).
- Always-on, no config. Tile lookups are memory-dict → direct request (no disk reads); the
  `SaveCachedItemIcon` PNG write stays because radar ESP reads that cache. The UI-star cache
  readers (`TryGetAutoSellCachedUiStar`) remain in the sell path but their only writer (the bag-UI
  scanner) is gone, so they always miss and star detection uses the direct-scan `starRate` /
  `step` / `QualityComponent` sources. `MasterLogGameIcons = true` re-enables verbose `[GameIcons]`
  per-load logging if a future build needs re-diagnosis.
