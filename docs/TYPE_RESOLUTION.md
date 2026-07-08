# Runtime Type Resolution (IL2CPP)

How **Bugtopia** finds game classes and methods at runtime. Required reading when a feature logs `unavailable`, `Null`, or Harmony never applies.

For build/interop setup see [BUILD_AND_RUN.md](./BUILD_AND_RUN.md). For assembly locations, EcsClient, LocalLow dumps, and tools see [GAME_ASSEMBLIES_AND_TOOLS.md](./GAME_ASSEMBLIES_AND_TOOLS.md). For architecture overview see [TECHNICAL.md](./TECHNICAL.md).

For **ECS services** (`EcsService.TryGet<T>`), `ClientSystem.*` implementations, `DataModule<T>.Instance`, protocol managers, enum/list reading, and the **Daily Claims** type table see **[GAME_TYPES_AND_SERVICES.md](./GAME_TYPES_AND_SERVICES.md)**.

---

## Two layers: interop DLL vs reflection

| Layer | What it is | Role in the mod |
|-------|------------|-----------------|
| **Compile-time interop** | `Assembly-CSharp.dll`, `Client.dll`, … under `<Game>/BepInEx/interop/` or `<Game>/MelonLoader/Il2CppAssemblies/` | Lets the mod **compile** against a subset of game APIs; referenced in `buddy.csproj`. |
| **Runtime reflection** | `AppDomain.CurrentDomain.GetAssemblies()`, `Type.GetType`, `assembly.GetType`, `GetTypes()` | How the mod **actually** resolves types after the game loads. |

Important: interop assemblies are **not a full export** of the game. Many types exist only in native IL2CPP until/unless the loader generated a stub. A type can compile in theory but still return `null` from `FindLoadedType` until the right assembly is loaded—or forever if interop never included it.

The mod does **not** use hard-coded RVA offsets. Everything goes through managed reflection, Harmony, or (Aura Farm only) the embedded **Mono** API.

---

## Core API: `FindLoadedType`

**Location:** `HeartopiaComplete.FindLoadedType` in `buddy/HeartopiaComplete.cs`.

**Signature:** `FindLoadedType(params string[] names)` — pass several aliases; first match wins.

### Search order

1. **Positive cache** — `loadedTypeLookupCache` keyed by joined names.
2. **Miss cache** — if recently failed, return `null` for **30 s** (`LoadedTypeMissCacheSeconds`) to avoid scanning every frame.
3. For each name:
   - `Type.GetType(name, false)` (needs assembly-qualified name when type is not in `mscorlib`/calling assembly).
   - `assembly.GetType(name, false)` on **every** loaded assembly.
4. **Full scan:** `assembly.GetTypes()` on all assemblies; match `Type.FullName` or `Type.Name` (ordinal equality).
5. On failure: store miss cache timestamp.

### Typical name list

Always pass names from **decompilation** (ILSpy / dnSpy) plus short names and common variants:

```csharp
this.FindLoadedType(
    "XDTLevelAndEntity.Gameplay.Component.Bubble.BubbleComponent",
    "XDTLevelAndEntity.GamePlay.Component.Bubble.BubbleComponent",  // typo variant in some builds
    "Il2CppXDTLevelAndEntity.Gameplay.Component.Bubble.BubbleComponent",
    "BubbleComponent");
```

Namespaces often used:

| Prefix | Examples |
|--------|----------|
| `XDTLevelAndEntity.*` | ECS components, `Entities`, `InteractSystem`, `GameplayApi` |
| `XDTDataAndProtocol.*` | `WebRequestUtility`, `*ProtocolManager` |
| `XDT.Scene.Shared.Modules.*` | Network command structs (`EcsClient` / `Client` assembly) |
| `ScriptsRefactory.*` | Refactored duplicates of level/entity types |
| `XDTGame.*` | UI panels |
| `EcsSystem.*` | Client network managers |

---

## `FindLoadedTypeBySuffix`

**Location:** `HeartopiaComplete.FindLoadedTypeBySuffix`.

When full names drift between patches, match the **end** of `FullName` or `Name`:

```csharp
this.FindLoadedTypeBySuffix(
    "Gameplay.Component.Bubble.BubbleComponent",
    ".BubbleComponent");
```

Used by bubble radar (`FindBubbleComponentRuntimeType`) and similar resolvers.

---

## Shape-based fallback (disambiguation)

Short names like `Entities`, `Character`, or `InteractSystem` collide across assemblies. The mod resolves the **intended** type by required members.

**Example — `FindEntitiesRuntimeType`:**

1. `FindLoadedType` with full names + `"Entities"`.
2. Else scan all types named `Entities` and pick one that has static `GetComponents` or `SphereQueryEntities`.

**Example — bird photo commands (`TryResolveBirdPhotoCommandRuntimeTypes`):**

1. `FindLoadedType("…TakingBirdPhotoCommand", "TakingBirdPhotoCommand")`.
2. Else scan assemblies for `Name == "TakingBirdPhotoCommand"` and validate with `IsBirdPhotoCommandShape` (expected fields/nested types).

**Example — Aura Farm (`FindTypeBySignature`):**

If `FindTypeByName` fails, scan types whose name contains `ResourceProtocolManager` / `InteractSystem` and verify methods like `SendPickBush` or fields on `InteractSystem`.

Pattern for new features:

1. Prefer **full name** from ILSpy.
2. Add **suffix** + **member signature** check.
3. Avoid patching the first type with a matching short name.

---

## Feature-specific resolvers

| Area | Resolver | Notes |
|------|----------|--------|
| Most of `HeartopiaComplete` | `FindLoadedType`, `FindLoadedTypeBySuffix` | NPC teleport, radar, birds, insects, HUD, net cook helpers, etc. |
| **Aura Farm** (`AuraFarm.cs`) | `FindTypeByName` → `FindTypeBySignature` | Preferred assemblies first (see below). |
| **Bubble radar / spawn** | `FindBubbleComponentRuntimeType`, `FindEntitiesRuntimeType` | Extra `GetTypes` scan + member checks; see `BubbleFeature.cs`. |
| **Bubble service (GM API)** | `FindLoadedBubbleServiceType` | Looks for `IBubbleService` / `BubbleClientService` with `GmGetAllBubble`. |
| **Bird farm** | `TryResolveBirdPhotoCommandRuntimeTypes` + cached `SendCommand` | Shape validation on command struct. |
| **Net cook** | `EnsureNetCookProtocolMethods`, AuraMono `CookingSystem` | Protocol types + optional Mono invoke. |
| **Homeland Farm** (`HomelandFarmFeature.cs`) | `ResolveHomelandFarmManagedType`, `HomelandFarmResolveAuraComponentClassRobust` | Managed-first, AuraMono fallback; see [worked example](#worked-example-homeland-farm-type-resolution). |
| **Pet / puzzle partials** | `FindLoadedType` in `PetFeedFeature.cs`, `PetPlayFeature.cs`, `PuzzleNetFeature.cs` | Same core API. |
| **Pad build hotkeys** (`PadBuildHotkeyFeature.cs`) | `TryGetPadBuildManagedModule` → `TryGetPadBuildAuraModule` → UI clicks | Three-tier `BuildModule` **instance** resolution; see [module instances](#resolving-module-instances-managersgetmodule--worked-example-buildmodule). |

---

## Aura Farm assembly filtering

`AuraFarm` does not scan blindly on every tick for all types. It uses:

**Preferred name fragments** (`auraPreferredAssemblyNameFragments`):

- `Assembly-CSharp`, `Il2CppAssembly-CSharp`, `XDT`, `Game`

**Excluded prefixes** (`auraExcludedAssemblyNamePrefixes`):

- `Unity`, `System`, `mscorlib`, `Harmony`, `MelonLoader`, `Il2CppSystem`, …

Search order: preferred assemblies → non-excluded → all non-excluded.

`BubbleFeature` uses a similar but separate scan (`ScanAllAssembliesForBubbleType`) when core lookups fail.

---

## Mono fallback (Aura Farm only)

When managed `Type` / `MethodInfo` is missing, Aura Farm can call the game’s **Mono** exports (`mono_class_from_name`, `mono_runtime_invoke`, …) after `EnsureAuraMonoApiReady()`.

Used for:

- Sending pick bush / attack tree / hit stone when IL2CPP stubs are incomplete.
- **Aura Farm AxeChecker** — `HandholdCylinderChecker.PhysicalSelect` and level-object shape field reads (`ownerEntity.netId`, `_data.resourceID`).
- **Meteor entity lookup** — `TryGetAuraMonoEntityObjectByNetId` prefers `Entities.GetEntity` / managed `Entities.GetEntity`; avoid `EntityUtil.GetEntity` for meteor parent walks (stale or missing on view entities).
- Net cook `PrepareCooking` via `CookingSystem` in some builds.
- Reading interact targets and entity lists when managed reflection fails.
- **Homeland-farm entity discovery** — inflating and invoking `Entities.GetComponents<T>` through Mono because the managed `Entities` method is absent (see [AuraMono generic `GetComponents<T>`](#auramono-generic-getcomponentst-direct-ecs-query)).
- **Pad build hotkeys** — resolving the `BuildModule` *instance* via `Managers.GetModule(Type)` and invoking its placing methods (see [module instances](#resolving-module-instances-managersgetmodule--worked-example-buildmodule)).

Other features (bubble, bird direct command, etc.) **do not** use Mono unless explicitly wired. Prefer `WebRequestUtility.SendCommand` for network actions.

---

## AuraMono generic `GetComponents<T>` (direct ECS query)

The homeland-farm scan needs the live set of `CropBoxComponent` / `CropComponent` / `PlantComponent` entities. The clean way is the game's own ECS query:

```csharp
// XDTLevelAndEntity.BaseSystem.EntitiesManager.Entities
public static void GetComponents<T>(ref List<T> outList) where T : ViewComponent
    => EntityWorld.GetAllComponents(outList);
```

But on this BepInEx build **the managed `Entities` type and its `GetComponents` `MethodInfo` are absent** (interop never stubbed them), so `FindLoadedType("…Entities")` resolves a shape but the generic method can't be reflected/invoked managed-side. The mod instead **inflates and invokes the generic method through the embedded Mono API** (`HomelandFarmFeature.cs` / `AuraFarm.cs`). This is the only fully-working farm discovery source on this build and replaces the crash-prone entity-graph walk.

### Required Mono exports

Bound in `HeartopiaComplete.AuraMonoEngine.cs` (`EnsureAuraMonoApiReady`) via `GetAuraMonoExport<…>`:

| Export | Use |
|--------|-----|
| `mono_class_from_name`, `mono_class_get_methods`, `mono_method_get_name` | Find the open generic `GetComponents` method on the `Entities` class. |
| `mono_class_get_type` | Component **class** → its `MonoType*` (the generic type argument). |
| **`mono_metadata_get_generic_inst`** | Build a valid `MonoGenericInst*` from `(argc, MonoType**)`. **Without this the inflate AVs** (see gotcha 2). |
| `mono_class_inflate_generic_method` | `GetComponents<T>` (open) + generic context → the closed method. |
| `mono_compile_method` | JIT the inflated method (also done lazily by `runtime_invoke`). |
| `mono_object_new`, `mono_runtime_object_init` / `Activator.CreateInstance` | Create the `List<T>` argument. |
| `mono_runtime_invoke` | Call the closed method. |
| `mono_array_addr_with_size`, `mono_array_length` | Read the resulting `List<T>` backing array. |

If any export is missing the path bails **gracefully** (logs and returns `false`) — it never dereferences a null delegate.

### Call sequence

1. **Readiness** (`TryHomelandFarmIsAuraMonoGetComponentsReady`) — Aura API attached, `Entities` class + open `GetComponents` method found, component classes resolved. Gate on **this**, not on the managed resolver (gotcha 1).
2. **Resolve component class** — `TryResolveAuraMonoFarmComponentClasses` returns the `CropBox` / `Crop` / `Plant` mono classes (`XDTLevelAndEntity.Gameplay.Component.Homeland.*`).
3. **Build generic inst** — `mono_class_get_type(componentClass)` → `MonoType*`; `mono_metadata_get_generic_inst(1, &type)` → `MonoGenericInst*`; put it in `MonoGenericContext.method_inst` (gotcha 2).
4. **Inflate + compile** — `mono_class_inflate_generic_method(openMethod, &context)` then `mono_compile_method`. Cached per component class.
5. **Create `List<T>`** and invoke `GetComponents<T>(ref list)` (gotcha 3).
6. **Enumerate** the list's backing array, read each component's `netId`.

### Three native-AV gotchas (each cost a crash to find)

These are uncatchable by C# `try/catch` — a native access-violation kills the process before BepInEx flushes the file log. They were isolated with per-step breadcrumb logs (`step1..step6`, `step3a..step3d`), now gated behind `HomelandFarmVerboseAuraGetComponentsLogs`.

1. **Wrong readiness gate.** The collector bailed at the top via the *managed* `TryEnsureHomelandFarmEntitiesGetComponentsReady` (always fails — managed types absent) **before** reaching the AuraMono branch. Fix: on the unsafe path gate on `TryHomelandFarmIsAuraMonoGetComponentsReady`.
2. **Malformed `MonoGenericInst` → AV in inflate.** `MonoGenericContext.method_inst` was set to a raw `MonoType*[]`. Mono expects a **`MonoGenericInst*`** (`{ bitfield id/argc; MonoType* type_argv[] }`), so it read `type_argc` from the wrong offset → huge garbage count → out-of-bounds walk → AV. Fix: build the inst with `mono_metadata_get_generic_inst(argc, MonoType**)` and use **its** pointer.
3. **By-ref parameter → AV in invoke.** The signature is `GetComponents<T>(ref List<T>)`. For a `ref` parameter `mono_runtime_invoke` expects `params[0]` to be a **pointer to the list pointer** (`List**`), not the list object. Passing the bare `MonoObject*` made Mono treat the object header as the ref-slot address → AV. Fix: `listSlot[0] = listObj; invokeArgs[0] = &listSlot;` then read the slot back to enumerate.

> **Rule for inflating any IL2CPP/embedded-mono generic method by raw pointer:** the generic context `method_inst` must be a real interned `MonoGenericInst*` (via `mono_metadata_get_generic_inst`), and a `ref`/`out` parameter takes a pointer-to-pointer, not the object. Get either wrong and you get a silent native crash, not an exception.

### `ViewComponent` constraint & component namespaces

`GetComponents<T> where T : ViewComponent` — the three farm component classes all derive from `ViewComponent` and live in **`XDTLevelAndEntity.Gameplay.Component.Homeland`**:

| Component | Full name |
|-----------|-----------|
| `CropBoxComponent` | `XDTLevelAndEntity.Gameplay.Component.Homeland.CropBoxComponent` |
| `CropComponent` | `XDTLevelAndEntity.Gameplay.Component.Homeland.CropComponent` |
| `PlantComponent` | `XDTLevelAndEntity.Gameplay.Component.Homeland.PlantComponent` |

**Gotcha:** `PlantComponent` is in the `.Homeland` namespace like the others, **not** a `.Plant` namespace. The resolver originally searched only `…Component.Plant.PlantComponent` → logged `PlantComponent=missing` and plants were never queried (they leaked back into the proximity walk). Adding the `.Homeland` candidate fixed it. Always confirm the real namespace from `ilspy-dumps/` — sibling components do not guarantee a shared namespace, but here all three share `.Homeland`.

### Non-fatal `icall.c:1622`

`D:\…\mono\metadata\icall.c:1622:` (= `ves_icall_System_Array_GetValue`) lines appear in the **native console** during these scans and sometimes leak into the file log. They are **non-fatal** — the scan completes. They come from a value-array `Array.GetValue` path used elsewhere (dictionary-backed discovery, water batches) and are **not** the crash. Do not chase them when diagnosing an AV; look at the last breadcrumb step instead.

### Reusable helper `TryAuraMonoGetComponentObjects` & cross-feature adoption

The inflate/invoke core is factored into one reusable helper (in `HomelandFarmFeature.cs`, shares the lower-level `TryHomelandFarmIsAuraMonoGetComponentsReady` / `…CreateAuraMonoComponentList` / `…ResolveInflatedAuraEntitiesGetComponentsMethod`):

```csharp
// componentClass = FindAuraMonoClassByFullName("…SomeViewComponent")
this.TryAuraMonoGetComponentObjects(componentClass, out List<IntPtr> components);
// each component's owner entity = its `entity` (or `_entity`) back-reference:
this.TryGetMonoObjectMember(componentObj, "entity", out IntPtr entityObj);
```

It returns the live component object pointers **without** the recursive entity-graph walk (`TryEnumerateAuraMonoLoadedEntityObjects`), which dereferences arbitrary mono entity pointers and **randomly native-AV-crashes on dense / visiting / streaming fields**. The IntPtrs are valid only in the current synchronous scope — scalarize (netId/fields) before any coroutine `yield`.

**This is now the standard entity-discovery path.** Features migrated from the graph walk to `GetComponents<T>`:

| Feature | Component(s) queried | Notes |
|---------|----------------------|-------|
| Homeland Farm | `CropBoxComponent`, `CropComponent`, `PlantComponent` | primary scan source (`ComponentRadius`) |
| Mass Cook | `CookBuildComponent`, `CookingComponent` | capture + broad refresh + nearby scan |
| Bird Farm | `BirdComponent`, `BirdScannableComponent`, `PerchBirdComponent` | gated on readiness → never walks once ready |
| Wild Animal Gift | `WildAnimalGiftComponent` | also fixed the old 4096-entity truncation undercount |
| Insect (loaded/teleport) | `InsectComponent` | net-independent loaded-insect list |
| Bubble radar | `BubbleComponent` | replaced the disabled "unsafe" walk fallback |
| Aura Farm | `CollectableMeteoriteLogicComponent` | **meteor parent lookup only** (walk kept as fallback) |

**When GetComponents fits — and when it does NOT:** it returns *every* loaded instance of the type (whole-world), so it suits **"radius scan of all X"** features (bird, gift, insect, bubble, crops). It does **not** suit **"act on what my equipped tool is pointed at"** discovery — **Aura Farm gather** (AxeChecker/interact/cylinder, range-limited by the tool) and **fishing** (visual fish-shadow targeting by prefab name + aquarium exclusion + camera scoring) intentionally stay on their proximity/visual paths; replacing them would over-target the whole world or lose visual rarity data. (Fishing instead narrowed its managed `FindObjectsOfType<GameObject>()` scene scan to `FindObjectsOfType(<FishComponent>)`.)

Still on the legacy walk (latent AV, convert when a component type fits): **Puzzle**, **PetFeed**, **SnowSculpture**. The shared `TryEnumerateAuraMonoLoadedEntityObjects` stays for them and as the fallback path in the migrated features.

---

## Resolving module instances (`Managers.GetModule`) — worked example: BuildModule

Game "modules" (`Module` / `ViewModule` subclasses: `BagModule`, `BuildModule`, `UICacheModule`, …)
are **instances** owned by `XDTGame.Framework.Managers` (assembly **XDTBaseService**). Storage:
`private static readonly Dictionary<Type, ModuleObject> _moduleDic` — note the values are
**`ModuleObject` wrappers**, the module itself is `wrapper.module`. The only sane accessors are
`GetModule<T>()` / `internal static IModule GetModule(Type)`.

`PadBuildHotkeyFeature.cs` resolves `BuildModule` with a three-tier dispatch (each tier verified):

### Tier 1 — managed (`TryGetManagedModule`)

```csharp
Type t = this.FindLoadedType("XDTGUI.Module.Build.BuildModule",
                             "Il2CppXDTGUI.Module.Build.BuildModule", "BuildModule");
this.TryGetManagedModule(t, out object module);   // Managers.GetModule(Type) via MethodInfo,
                                                  // fallback: _moduleDic as managed IDictionary
```

Works when interop has a stub for the module (true for `BagModule` / DirectBackpack). For
`BuildModule` on the current build `FindLoadedType` returns `null` — interop never generated it —
so this tier is a miss-cached no-op that self-heals after an interop regen.

### Tier 2 — AuraMono `Managers.GetModule(Type)` (the working path for BuildModule)

```csharp
// 1. Class — explicit image list; see the namespace ≠ assembly pitfall below.
IntPtr cls = this.FindAuraMonoClassInImages("XDTGUI.Module.Build", "BuildModule",
    new[] { "XDTLevelAndEntity", "XDTLevelAndEntity.dll", /* … */ });
// 2. System.Type from the class pointer — NEVER Type.GetType(string).
IntPtr typeObj = auraMonoTypeGetObject(domain, auraMonoClassGetType(cls));
// 3. Managers pinned to its real namespace+image (an unqualified "Managers" can hit a wrong class).
IntPtr managers = this.FindAuraMonoClassInImages("XDTGame.Framework", "Managers",
    new[] { "XDTBaseService", "XDTBaseService.dll", /* … */ });
// 4. Invoke internal static GetModule(Type); visibility does not matter to mono_runtime_invoke.
IntPtr getModule = this.FindAuraMonoMethodOnHierarchy(managers, "GetModule", 1);
IntPtr* args = stackalloc IntPtr[1]; args[0] = typeObj;
IntPtr module = auraMonoRuntimeInvoke(getModule, IntPtr.Zero, (IntPtr)args, ref exc);
```

Cache the module object + method ptrs; **drop the object on any invoke `exc`** (it can go stale
after GC / level switch) and re-resolve on the next use (throttled).

### Tier 3 — UI fallback

Clicking the feature's `BuildStatusPanel` buttons via `GameObject.Find` (paths from
`BuildStatusPanel_Auto.cs`). No type resolution at all; kept as the backstop.

### Dead ends (verified on this build — do not retry)

| Attempt | Why it fails |
|---------|--------------|
| `Type.GetType(fullName)` via `mono_runtime_invoke` (the `TryResolveAuraMonoModule` fallback) | **Hard-crashes the runtime** — `icall.c:1622 internal_from_name`, native abort, uncatchable. Build `System.Type` only from a class pointer (`mono_class_get_type` + `mono_type_get_object`). |
| Enumerating `Managers._moduleDic.Values` via AuraMono | `Dictionary<,>.Values` (ValueCollection) **enumerates to 0** through the boxed struct-enumerator path of `TryEnumerateAuraMonoCollectionItems`; and even if it worked, the values are `ModuleObject` wrappers, not modules. |
| `FindAuraMonoClassByFullName("XDTGUI.Module.Build.BuildModule")` | **First-image-only bug**: `FindAuraMonoClassInLikelyImages` calls `FindAuraMonoImage(list)` which returns the *first loaded* image and probes only it. The unmapped `XDTGUI.*` namespace falls into the default list starting with `XDTGameUI`, but `BuildModule` is compiled into **XDTLevelAndEntity** (namespace ≠ assembly). Use `FindAuraMonoClassInImages` (probes every image) with the right image first. |
| `TryResolveAuraMonoModule(fullName)` for any module without a static `Instance` | Walks the static-getter list, then falls into the `Type.GetType` crash above. Only safe for singleton-style classes. |

---

## Worked example: Homeland Farm type resolution

The farm (`HomelandFarmFeature.cs`) needs three families of types, each resolved differently. All resolution is centralized so the rest of the feature just reads cached `Type`/`IntPtr` fields.

### 1. Managed / protocol / command types — `ResolveHomelandFarmManagedType`

A thin wrapper over the host `FindLoadedType` that adds the Aura loader and the `Il2Cpp` prefix automatically. Signature: `ResolveHomelandFarmManagedType(string shortName, params string[] fullNames)`. Order: each full name via `FindLoadedTypeByFullName` → `FindLoadedType(fullName, shortName)`; then the Aura loader (`FindTypeByName`, also trying an `Il2Cpp`-prefixed name); finally `FindHomelandFarmRuntimeType(shortName)`.

Always pass **both namespace variants** — the game ships duplicated types under `XDTDataAndProtocol.*` and `ScriptsRefactory.DataAndProtocol.*`, and command structs additionally appear with an `EcsClient.` prefix:

```csharp
// Component data (crop vs crop-box vs plant/flower are three distinct types)
this.homelandFarmCropItemDataType = this.ResolveHomelandFarmManagedType(
    "CropItemData",
    "XDTDataAndProtocol.ComponentsData.CropItemData",
    "ScriptsRefactory.DataAndProtocol.ComponentsData.CropItemData");

// Protocol manager (sends the actual server commands: AddManure, WaterPlant, …)
this.homelandFarmCropProtocolManagerType = this.ResolveHomelandFarmManagedType(
    "CropProtocolManager",
    "XDTDataAndProtocol.ProtocolService.Plant.CropProtocolManager");

// Network command struct — note the EcsClient-prefixed fallback
this.homelandFarmManuredNetworkCommandType = this.ResolveHomelandFarmManagedType(
    "ManuredNetworkCommand",
    "XDT.Scene.Shared.Modules.Farm.ManuredNetworkCommand",
    "EcsClient.XDT.Scene.Shared.Modules.Farm.ManuredNetworkCommand");
```

Each resolved type is null-checked into a `missingTypes` list and logged once, so a single startup line tells you exactly what failed to resolve on the current build.

### 2. ECS component classes (`CropBoxComponent` / `CropComponent` / `PlantComponent`) — AuraMono

These managed types are **absent** under BepInEx on the current build, so they are resolved as native **Mono classes** (`IntPtr`) via `HomelandFarmResolveAuraComponentClassRobust(fullNames, shortName, namespaceCandidates, validator)`:

1. each full name → `FindAuraMonoClassByFullName`,
2. else `FindAuraMonoClassAcrossLoadedAssemblies(namespace, shortName)` for each namespace candidate,
3. else `FindHomelandFarmAuraClassByScanningAllImages(shortName, namespaces, validator)`.

The `validator` (e.g. `HomelandFarmAuraClassDisplayNameContains(candidate, "PlantComponent")`) disambiguates short-name collisions across images. The candidate lists must carry the **exact** namespace — all three farm components live in `XDTLevelAndEntity.Gameplay.Component.Homeland` (note: `PlantComponent` is here too, **not** in a `.Plant` namespace — getting this wrong logs `PlantComponent=missing` and silently drops flowers from the scan):

```csharp
private static readonly string[] HomelandFarmAuraPlantComponentFullNames =
{
    "XDTLevelAndEntity.Gameplay.Component.Homeland.PlantComponent",
    "XDTLevelAndEntity.GamePlay.Component.Homeland.PlantComponent",        // Gameplay/GamePlay casing
    "ScriptsRefactory.LevelAndEntity.Gameplay.Component.Homeland.PlantComponent",
    // …legacy ".Plant" namespace kept only as a last-resort fallback
};
```

These class pointers feed the [AuraMono generic `GetComponents<T>`](#auramono-generic-getcomponentst-direct-ecs-query) query above.

### 3. Protocol methods — managed first, AuraMono fallback

Once the protocol-manager `Type` (or Mono class) is known, the specific method is resolved by name + parameter count, with a native fallback:

```csharp
// Managed: static method taking a single List<uint>
this.homelandFarmCropAddManureMethod =
    this.ResolveHomelandFarmListOnlyStaticMethod(this.homelandFarmCropProtocolManagerType, "AddManure");
this.homelandFarmPlantCollectSeedMethod =
    this.GetMethodByNameAndParamCountQuiet(this.homelandFarmPlantProtocolManagerType, "SendCollectSeedCommand", 1);

// AuraMono fallback when the managed method is absent
this.homelandFarmAuraCropAddManureMethod =
    this.FindAuraMonoMethodOnHierarchy(cropProtocolClass, "AddManure", 1);
```

### Two-tier pattern & where the names come from

The farm follows a consistent **managed-first, AuraMono-fallback** policy: `EnsureHomelandFarmReflectionReady` resolves the managed types; when they are missing (`HomelandFarmPrefersAuraComponentData()` returns true), the AuraMono path takes over for component data, component classes, the fertilizer table (`TryHomelandFarmTryGetCropFertilizerTableRowAuraMono`), and protocol invokes. Every full name above is copied verbatim from `ilspy-dumps/` for this build — re-verify after a game patch (see the workflow below).

| Farm type | shortName | Full name(s) | How resolved |
|-----------|-----------|--------------|--------------|
| `CropItemData` / `CropBoxItemData` / `PlantItemData` | same | `XDTDataAndProtocol.ComponentsData.*` (+ `ScriptsRefactory.*`) | `ResolveHomelandFarmManagedType` → AuraMono component-data read |
| `CropProtocolManager` / `PlantProtocolManager` | same | `XDTDataAndProtocol.ProtocolService.Plant.*` | `ResolveHomelandFarmManagedType` |
| `ManuredNetworkCommand` / `WaterCropNetworkCommand` / `HarvestNetworkCommand` / `WeedingNetworkCommand` / `GrowCropNetworkCommand` | same | `XDT.Scene.Shared.Modules.Farm.*` (+ `EcsClient.` prefix) | `ResolveHomelandFarmManagedType` |
| `CropBoxComponent` / `CropComponent` / `PlantComponent` | same | `XDTLevelAndEntity.Gameplay.Component.Homeland.*` | `HomelandFarmResolveAuraComponentClassRobust` (Mono `IntPtr`) |
| `Entities.GetComponents<T>` | `Entities` | `XDTLevelAndEntity.BaseSystem.EntitiesManager.Entities` | AuraMono generic inflate/invoke (section above) |

---

## Integration strategies (after type is found)

### 1. Hooking or redirecting a game method — do NOT use Harmony on IL2CPP

**Prohibited:** do not add `[HarmonyPatch]` / `harmony.Patch(...)` on IL2CPP methods
(`GameAssembly.dll` / `UnityPlayer.dll` / Il2Cpp interop stubs). A Harmony detour rewrites the
target module's `.text`, which the native **Themis** anti-cheat can integrity-hash. The mod
deliberately installs **zero** such patches — don't reintroduce one. Pick a channel that stays off
the `.text` surface:

| You want to… | Use | Reference |
|--------------|-----|-----------|
| React when a game event fires | **EventCenter dispatch-detour** (`RegisterGameEventHook`) — first choice | [GAME_EVENTS.md](./GAME_EVENTS.md), `EventLog.cs` |
| Intercept / rewrite a game method | **Mono `NativeDetour` + `mono_compile_method`** on the embedded-Mono (`XDT*`/`EcsClient`) method — patches the Mono JIT heap, not module `.text` | `BuildingFreeRotateFeature.cs`, `BubbleSpawnRewriteFeature.cs` |
| Call a game method / read state | **AuraMono** `mono_runtime_invoke` + field reads | §4 below |
| Send a network command | **`WebRequestUtility.SendCommand<T>`** | §2 below |
| Drive movement / camera / build | the game's own controllers via field-writes/invokes | `PlayerMoveComponent`, `XDTCameraManager` axis, `GodControl` |
| Trigger a UI action | click the game's own button directly (`DirectClickInteractButton`) — never fake Unity input | `HeartopiaComplete.Interaction.cs` |

Harmony on **embedded-Mono** methods is also unnecessary — use `NativeDetour`. If you believe a new
IL2CPP patch is truly unavoidable, **stop and raise it first**; the entire Harmony surface was
removed on purpose (see [anti-cheat cleanup]).

### 2. `WebRequestUtility.SendCommand<T>` (network commands)

**Important:** `WebRequestUtility` and most `*NetworkCommand` structs live in **embedded Mono** (`XDTDataAndProtocol.dll`, `EcsClient.dll` images). They are **not** in IL2CPP `GameAssembly` and are often **absent from the BepInEx interop `AppDomain`**. Plain `FindLoadedType("XDT.Scene.Shared…SomeNetworkCommand")` frequently returns `null` even in-world — same class of problem as [DRAW_TECHNICAL.md](./DRAW_TECHNICAL.md) documents for draw upload.

#### Decision table (pick one path)

| Need | Path | When it works |
|------|------|----------------|
| Server action with a **protocol-manager wrapper** | **AuraMono** static invoke on `*ProtocolManager` (manager builds the command and calls `WebRequestUtility` internally) | Always in-world once AuraMono is ready. **Preferred** when the manager method exists and uses the right channel. Reference: `DrawUploadFeature.cs`, `FishingProtocolManager.UpdateFloatPosition` (Unreliable — see instant catch below). |
| Server action with a **specific `ChannelType`** (e.g. Reliable while the manager hardcodes Unreliable) | **AuraMono** inflate + invoke `WebRequestUtility.SendCommand<T>` | When managed `Type` resolution fails. Reference: **Instant Catch** in `HeartopiaComplete.Fishing.cs` (`TryEnsureInstantCatchAuraBuoySend` / `TrySendBuoyUpdateReliable`). |
| Managed `SendCommand` via reflection | `ResolveHomelandFarmManagedType` + `EnsureHomelandFarmSendCommandResolver` + `TryHomelandFarmSendCommand` | Only when command struct + `WebRequestUtility` are actually loaded into `AppDomain` (interop load succeeded — farm/cook/bird paths on some builds). |

Do **not** assume `EcsClient.` prefix alone fixes managed resolution — the assembly must be **loaded**, not just named correctly in a string.

#### Managed flow (when types are in AppDomain)

1. Resolve command struct via `ResolveHomelandFarmManagedType(shortName, "XDT.Scene.Shared.Modules….SomeNetworkCommand", "EcsClient.XDT.Scene.Shared.Modules….SomeNetworkCommand")` — command bodies decompile from **`EcsClient.dll`** (`ilspy-dumps/EcsClient/…`), namespace usually `XDT.Scene.Shared.Modules.*` or `XDT.Scene.Shared.GamePlay.*` **without** an `EcsClient.` prefix on the type itself.
2. Resolve `WebRequestUtility` via `FindHomelandFarmRuntimeType("WebRequestUtility", "XDTDataAndProtocol.ProtocolService")` (includes `NonPublic` `SendCommand` scan).
3. Resolve `ChannelType` (`XD.GameGerm.Network.ChannelType`); fallback `Reliable = 1` if enum missing (see `HomelandFarmFeature.EnsureHomelandFarmSendCommandResolver`).
4. `MakeGenericMethod(commandType)` → `Invoke(null, new object[] { commandInstance, needAuthed, channel })`.

#### AuraMono `SendCommand<T>` flow (Mono-only types — **Instant Catch pattern**)

Use when managed resolution logs `unresolved` / `reliable types unresolved` but AuraMono protocol classes resolve fine.

1. `FindAuraMonoClassByFullName("XDTDataAndProtocol.ProtocolService.WebRequestUtility")`.
2. `FindAuraMonoClassByFullName("XDT.Scene.Shared.GamePlay.Fishing.UpdateRodBuoyPositionNetworkCommand")` — if zero, `FindAuraMonoClassAcrossLoadedAssemblies("XDT.Scene.Shared.GamePlay.Fishing", "UpdateRodBuoyPositionNetworkCommand")`. Image: **`EcsClient`**.
3. `FindAuraMonoMethodOnHierarchy(webRequestClass, "SendCommand", 3)` → inflate with `mono_class_get_type` + `mono_metadata_get_generic_inst` + `mono_class_inflate_generic_method` (same pattern as `DailyClaimsFeature` / `HomelandFarmFeature` generic inflate).
4. Build the command **struct** with `mono_object_new` + `auraMonoFieldSetValue` on fields (`BuoyNetId`, `BuoyPos`, `Direction`, `SuccessLength`, `FailureLength`).
5. Static invoke inflated `SendCommand` with `(struct unboxed ptr, needAuthed: true, channel: Reliable)`. `FishingProtocolManager.UpdateFloatPosition` hardcodes `ChannelType.Unreliable`; Instant Catch needs Reliable so the spoofed `successLength` is not dropped at bite time.

Copy field names and namespaces from `ilspy-dumps/XDTDataAndProtocol/.../Fishing/FishingProtocolManager.cs` — e.g. `UpdateFloatPosition` sends `UpdateRodBuoyPositionNetworkCommand`.

#### Other SendCommand patterns

**Bubble spawn (preferred path):** Harmony **prefix** on the generic `SendCommand` definition rewrites `location` on bubble create commands — does not require `BubbleComponent` or `ActivityEventProtocolManager` types.

Same managed pattern when types load: bird photo (`TakingBirdPhotoCommand`), net cook prepare commands, `GetBubbleAward`, homeland farm water/manure commands, etc.

### 2b. Worked example — fishing Instant Catch (Mono-only reliable buoy)

| Game type | Dump / image | Mod resolution |
|-----------|--------------|----------------|
| `FishingProtocolManager` | `XDTDataAndProtocol` / `ProtocolService/Fishing/` | `FindAuraMonoClassByFullName` → `UpdateFloatPosition` (Unreliable fallback) |
| `UpdateRodBuoyPositionNetworkCommand` | `EcsClient` / `XDT.Scene.Shared.GamePlay.Fishing` | AuraMono class + field offsets; **not** `FindLoadedType` |
| `WebRequestUtility.SendCommand<T>` | `XDTDataAndProtocol` | AuraMono generic inflate; `ChannelType.Reliable` (= `1` on current build) |
| `HandHoldFishingRod.GetFloatPosition` | `XDTLevelAndEntity` | AuraMono on equipped handhold — real buoy position for geometry echo |

Files: `HeartopiaComplete.Fishing.cs` (`TryArmFishingInstantCatch`, `TrySendBuoyUpdateReliable`), `AutoFishingFarm.cs` (toggle + tick). Log tag: `[InstantCatch]`.

### 3. Reflection invoke (no Harmony)

`MethodInfo.Invoke` / `Activator.CreateInstance` on resolved types:

- `Entities.GetComponents<T>()` — **only where the managed method exists**; absent on the current homeland build, which uses the [AuraMono generic invoke](#auramono-generic-getcomponentst-direct-ecs-query) instead.
- `EntityUtil`, `InteractSystem`, `GameplayApi`
- UI/HUD components

### 4. GameObject hierarchy paths

Some UI/world hooks use `GameObject.Find("…")` with fixed paths (fragile on game updates). Documented separately in TECHNICAL.md — not type resolution, but often used together.

---

## UI panels, hooks, and IL2CPP (worked example: weather exchange shop)

Force-open shop and similar UI features are the main place where **the wrong hook style looks successful but never fires in-game**.

### Three runtime paths (pick the right one)

| Path | Who uses it | Mod can hook with |
|------|-------------|-------------------|
| **IL2CPP native** | Most `XDTGameUI` panels at runtime (`OpenWeatherExchangePanel`, `ProcessUiFunction`, `UIManager.OpenView`) | **Not** `FindLoadedType` + Harmony; **not** mono thunk detour on the panel method |
| **AuraMono `mono_runtime_invoke`** | Mod calling into game (`TryInvokeAuraMonoStaticIntIntMethod`, `FindAuraMonoClassByFullName`) | **Yes** — preferred way for the mod to **open** a panel |
| **Mono native thunk** | Some protocol managers (`ActivityEventProtocolManager`, `BubbleProtocolManager`) when the game still hits embedded Mono exports | **Yes** — `BubbleMonoNativeHook` + `mono_method_get_unmanaged_thunk` (see `BubbleFeature.TryApplyBubbleMonoNativeHooks`) |

`XDTGame.UI.Panel.*` types are usually **AuraMono-class-only** in `AppDomain` (no interop stub). `FindLoadedType("XDTGame.UI.Panel.WeatherExchangeShopPanel")` returns `null` even after entering town.

### Opening a shop panel from the mod (production pattern)

1. Decompile `ilspy-dumps/XDTGameUI/…` — find the real static entry (not `ShopPanel` for every shop).
2. Resolve class with `FindAuraMonoClassByFullName(fullName)` (image `XDTGameUI.dll`).
3. Call static method via `TryInvokeAuraMonoStaticIntIntMethod` / `TryInvokeAuraMonoStaticNullBoolMethod` / `UIManager.OpenView` AuraMono path.

**Worked example — Meteor / Starfall exchange (Doris, starfall event):**

| Item | Value |
|------|--------|
| Panel | `XDTGame.UI.Panel.WeatherExchangeShopPanel` |
| Entry | `OpenWeatherExchangePanel(int storeId, int slotId = 0)` → builds `Intent`, then `UIManager.OpenView<WeatherExchangeShopPanel>` |
| Dialogue path | `DialogueNodeBranch.ProcessUiFunction` **case 10** → `OpenWeatherExchangePanel(nodeData.UIParam)` |
| Fortune rainbow/rain (wish stars) | `ShopPanel.OpenShopPanel` — **storeId 86 / 87**, `UIFunction` **1**, not case 10 |
| Verified `storeId` (starfall exchange) | **140** — hardcoded as `MeteorStarfallExchangeStoreId` in `HeartopiaComplete.cs` |

```csharp
// Mod opens exchange shop — AuraMono invoke only
TryInvokeAuraMonoStaticIntIntMethod(
    "XDTGame.UI.Panel.WeatherExchangeShopPanel",
    "OpenWeatherExchangePanel",
    storeId: 140,
    slotId: 0,
    successStatus);
```

Same pattern as `TryOpenShopPanelByStoreId` → `ShopPanel.OpenShopPanel` for normal NPC stores.

### What **not** to do for IL2CPP UI

| Approach | Symptom | Why |
|----------|---------|-----|
| Harmony on `FindLoadedType` panel type | `panel type not found` / patch skipped | Type not in interop / `AppDomain` |
| Mono native hook on `OpenWeatherExchangePanel` thunk | Log says `[OK] Mono hook …` but **nothing on Doris → Fortune Store** | Game calls **IL2CPP** code; hook is on Mono export the game never hits |
| Install hook in `OnInitializeMelon` | `AuraMono not ready` / class not found | Aura API and `XDTGameUI` image load after world entry |
| Heuristic `storeId` from `TableStoreInfos` scoring (`1033`, `rainshop`) | Wrong NPC (Atara), empty shop, rain items | Event/fortune tables ≠ Doris weather exchange |

### When mono native hooks **do** work

Use them for **protocol / data-layer** methods the game still dispatches through embedded Mono, not for `XDTGame.UI.Panel` openers.

Reference: `buddy/BubbleFeature.cs`

1. Wait until `EnsureAuraMonoApiReady()` + `AttachAuraMonoThread()` (retry from `ProcessBubbleFeatureOnUpdate`, ~5 s).
2. `FindAuraMonoClassByFullName` → `FindAuraMonoMethodOnHierarchy` → `TryGetAuraMonoMethodNativePointer` (`mono_compile_method`, `mono_method_get_unmanaged_thunk`).
3. Install with `BubbleMonoNativeHook.TryInstall(nativePtr, hookDelegate, out trampoline)`.
4. Delegate calling convention: **`CallingConvention.Cdecl`**; match arity and types exactly (`Vector3*` for bubble spawn).

Do **not** copy this pipeline to UI panel static openers expecting in-game capture.

### Discovering unknown `storeId` / `UIParam` (debug only)

Use once per game build, then **hardcode** the verified id in source.

| Method | How | Notes |
|--------|-----|--------|
| **ILSpy dialogue branch** | `DialogueNodeBranch.cs` case **10** → `nodeData.UIParam` | Doris Fortune Store during starfall |
| **Poll open panel** | `UIManager.GetView<WeatherExchangeShopPanel>` → read `intent.GetInt("storeId")` | Reliable on IL2CPP; pattern mirrors `ModTryResolveAuraMonoBagPanel` |
| **Mono hook on panel open** | Only fires when **mod** calls via `mono_runtime_invoke`, not from game dialogue | Misleading for validation |

After capture (e.g. `storeId=140`), remove poll/hook debug code; keep `TryOpenWeatherExchangeShopPanelByStoreId` + constant.

### Hook install timing checklist

1. **Never** at mod init alone — defer until AuraMono ready (in-world).
2. **Retry** with backoff (`ProcessBubbleFeatureOnUpdate` pattern) if class/method missing on first frame.
3. **Log which tier succeeded** once: `panelHook` / `dialogueHook` / `poll` / `AuraMono invoke`.
4. **Ship** with hardcoded ids from decompile + one in-game confirmation, not runtime discovery.

### Decision flow

```mermaid
flowchart TD
  Q[Need to affect UI shop?]
  Q --> Open[Mod opens panel]
  Q --> Capture[Learn storeId when player opens in game]
  Q --> Intercept[Intercept game protocol action]

  Open --> AuraInvoke["AuraMono: FindAuraMonoClassByFullName + mono_runtime_invoke"]
  Capture --> Poll["Poll GetView + intent OR read DialogueNodeBranch case 10 in ILSpy"]
  Capture --> Hardcode[Hardcode verified storeId]
  Intercept --> ProtoHook["Mono native hook on protocol manager OR Harmony SendCommand"]

  Wrong1["Harmony on FindLoadedType XDTGame.UI.*"] -.->|fails| Q
  Wrong2["Mono thunk on OpenWeatherExchangePanel for game capture"] -.->|silent no-op| Q
```

---

## Caching and retries

| Mechanism | Purpose |
|-----------|---------|
| `loadedTypeLookupCache` | Successful `FindLoadedType` / suffix lookups |
| `loadedTypeMissCacheUntil` | 30 s backoff after failed lookup |
| Feature caches (e.g. `cachedBirdPhotoSendCommandMethod`) | Avoid repeating generic `MakeGenericMethod` |
| Periodic retry in `Update` | e.g. `ProcessBubbleFeatureOnUpdate` every 5 s until patches apply |
| `nextBubbleEntityTypeResolveAttemptAt` | Bubble radar throttles failed entity scans |

Features should **not** call `FindLoadedType` every frame without cache; use miss cache or feature-level “ready” flags.

---

## Workflow: adding or fixing a type

1. **Decompile** game DLLs (local `ilspy-dumps`, ILSpy, dnSpy). Copy **full namespace** and assembly name if shown.
2. **List aliases** in `FindLoadedType`: full name, `GamePlay` vs `Gameplay`, `Il2Cpp` prefix, short `Type.Name`.
3. If ambiguous, add **suffix + method/field shape** check (see `FindEntitiesRuntimeType`, `IsBirdPhotoCommandShape`).
4. Choose integration:
   - Client → server action? → **network command** + `SendCommand` (or Harmony on `SendCommand`).
   - Client-only behavior? → Harmony or `Invoke` on instance/static method.
5. **Log once** on failure (`ModLogger.Msg` with which alias failed). For bubble, `[BubbleFeature] Resolve probe: …` lists key types across all assemblies.
6. Test **in world** (not main menu). Many assemblies load only after entering a town.
7. After game patch: regenerate interop, rebuild mod, repeat from step 1.

---

## Common pitfalls

| Symptom | Cause | What to do |
|---------|--------|------------|
| Type always `null` in menu, works in town | Assemblies load late | Retry in `Update`; do not cache miss forever on first frame |
| `GetTypes()` empty for bubble types | Interop subset | Use `WebRequestUtility` / command structs in `Client` / `EcsClient` |
| Wrong type patched | Short name collision | Add shape check (methods/fields) |
| `Gameplay` vs `GamePlay` | Namespace typo between builds | Pass **both** in `FindLoadedType` |
| `Il2Cpp` prefix on `FullName` | Il2CppInterop wrapper | Strip prefix or include `Il2Cpp` + non-prefixed names |
| Harmony never runs | Type null → patch skipped | Fix resolution first; check BepInEx log for `[ERR]` |
| Miss cache hides new type | Failed lookup cached 30 s | Wait or call `loadedTypeMissCacheUntil.Remove` in feature init (bubble clears per lookup) |
| Only `Assembly-CSharp` searched | Narrow probe | Scan **all** non-System assemblies (default in `FindLoadedType`) |
| Silent native crash (no log) inflating a generic method via Mono | `MonoGenericContext.method_inst` was a raw `MonoType*[]` | Build a real `MonoGenericInst*` with `mono_metadata_get_generic_inst` ([detail](#three-native-av-gotchas-each-cost-a-crash-to-find)) |
| Silent native crash invoking a Mono method taking `ref`/`out` | Passed the object pointer for a by-ref param | Pass a pointer-to-pointer (`List**`); read the slot back after invoke |
| `…Component=missing` though sibling components resolve | Wrong namespace assumed (e.g. `.Plant` vs `.Homeland`) | Confirm exact namespace per type in `ilspy-dumps/` |
| AuraMono class lookup fails though the type exists in dumps | `FindAuraMonoClassByFullName` probes only the **first loaded** image; namespace ≠ assembly (e.g. `XDTGUI.Module.Build.BuildModule` lives in **XDTLevelAndEntity**) | Use `FindAuraMonoClassInImages` with an explicit image list (correct image first) |
| Native crash (`icall.c:1622 internal_from_name`) right after a module/type resolve attempt | `Type.GetType(string)` invoked through `mono_runtime_invoke` | Build `System.Type` from the class ptr: `mono_class_get_type` → `mono_type_get_object` |
| Module instance unreachable (`_moduleDic` scan finds nothing / count=0) | Dict values are `ModuleObject` wrappers; ValueCollection does not enumerate via AuraMono | Invoke `Managers.GetModule(Type)` instead ([module instances](#resolving-module-instances-managersgetmodule--worked-example-buildmodule)) |
| `[OK] Mono hook WeatherExchangeShopPanel…` but no capture in-game | UI runs on IL2CPP, not Mono thunk | Open via AuraMono invoke; discover `storeId` via ILSpy/poll, then hardcode |
| `WeatherExchange capture hook deferred: AuraMono not ready` | Hook attempted before world / AuraMono attach | Defer install to `Update` retry (see `ProcessBubbleFeatureOnUpdate`) |
| Exchange opens with wrong NPC or empty list | Wrong `storeId` heuristic (e.g. 127, 87) | Doris starfall = `WeatherExchangeShopPanel` + case 10; rain/rainbow fortune = `ShopPanel` 86/87 |

---

## Reference: high-traffic types

| Purpose | Typical type names |
|---------|-------------------|
| Send network commands | `XDTDataAndProtocol.ProtocolService.WebRequestUtility`, `XD.GameGerm.Network.ChannelType` |
| ECS query | `XDTLevelAndEntity.BaseSystem.EntitiesManager.Entities`, `EntityUtil` |
| Interact / aura | `InteractSystem`, `ResourceProtocolManager`, `EntityHelper` |
| Birds | `TakingBirdPhotoCommand`, `BirdScannableComponent`, `BirdWatchingManager` |
| Bubbles (spawn) | `CreateActivityEventPersonalRewardBubbleNetworkCommand`, `CreateBubbleNetworkCommand` |
| Bubbles (world) | `BubbleComponent`, `BubbleMoveComponent`, `ActivityEventModule` |
| Cooking | `PrepareCookingNetworkCommand`, cooking protocol / `CookingSystem` (Mono) |
| Backpack / tasks | `BackPackSystem`, `BackpackProtocolManager`, `TaskProtocolManager`, `ItemNetPair` |
| Wild animal gifts | **AuraMono class lookup only** — `WildAnimalProtocolManager`, `AnimalUtil`, `AnimalProtocolManager` (no `FindLoadedType`, no `IWildAnimalService` interop) |
| Daily claims (sign-in, town guide, mail, BP) | `EcsService`, `IOperationActivityCenterService`, `ITownGuidesService`, `IMailClientService`, `BattlePassSystem` — see [GAME_TYPES_AND_SERVICES.md](./GAME_TYPES_AND_SERVICES.md) |
| Player | `Character`, `p_player_skeleton(Clone)` via `GameObject.Find` |
| NPC shops (`ShopPanel`) | `XDTGame.UI.Panel.ShopPanel.OpenShopPanel(storeId, slotId)` — AuraMono invoke |
| Weather exchange (`UIFunction` 10) | `XDTGame.UI.Panel.WeatherExchangeShopPanel.OpenWeatherExchangePanel` — AuraMono invoke; dialogue `DialogueNodeBranch` case 10 |
| UI manager | `XDTGame.Core.UIManager` — `get_Instance`, `GetView(Type)`, `OpenView` |

Exact strings change with game version — always verify in ILSpy for your build.

---

## Related source files

| File | Resolution logic |
|------|------------------|
| `buddy/HeartopiaComplete.cs` | `FindLoadedType`, `FindLoadedTypeBySuffix`, entity/bird/HUD resolvers |
| `buddy/AuraFarm.cs` | `FindTypeByName`, `FindTypeBySignature`, Mono API |
| `buddy/BubbleFeature.cs` | SendCommand patch, bubble-specific scans |
| `buddy/BirdNetFarm.cs` | Uses host resolvers + bird caches |
| `buddy/PetFeedFeature.cs`, `PetPlayFeature.cs`, `PuzzleNetFeature.cs` | `FindLoadedType` per feature |
| `buddy/DailyQuestSubmitFeature.cs`, `buddy/WildAnimalFeedFeature.cs` | Backpack + task protocol resolution |
| `buddy/DailyClaimsFeature.cs` | `EcsService.TryGet<T>` inflation, `DataModule<BattlePassSystem>`, mail/BP state — see [GAME_TYPES_AND_SERVICES.md](./GAME_TYPES_AND_SERVICES.md) |
| `buddy/WildAnimalGiftFeature.cs` | AuraMono `FindAuraMonoClassByFullName` / `mono_runtime_invoke` only — see [BACKPACK_AND_ITEMS.md](./BACKPACK_AND_ITEMS.md#wild-animal-gifts-detail) |

---

## Related documentation

- [BUILD_AND_RUN.md](./BUILD_AND_RUN.md) — interop generation, deploy, logs
- [GAME_ASSEMBLIES_AND_TOOLS.md](./GAME_ASSEMBLIES_AND_TOOLS.md) — EcsClient, LocalLow `DotnetAssemblies`, tool checklist
- [TECHNICAL.md](./TECHNICAL.md) — architecture, Harmony, farms
- [FEATURES.md](./FEATURES.md) — what each feature expects at runtime
- [BACKPACK_AND_ITEMS.md](./BACKPACK_AND_ITEMS.md) — inventory enumeration and per-feature filters
- [GAME_TYPES_AND_SERVICES.md](./GAME_TYPES_AND_SERVICES.md) — ECS services, ClientSystem types, DataModule, Daily Claims reference
