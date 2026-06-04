# Runtime Type Resolution (IL2CPP)

How **Heartopia Helper** finds game classes and methods at runtime. Required reading when a feature logs `unavailable`, `Null`, or Harmony never applies.

For build/interop setup see [BUILD_AND_RUN.md](./BUILD_AND_RUN.md). For assembly locations, EcsClient, LocalLow dumps, and tools see [GAME_ASSEMBLIES_AND_TOOLS.md](./GAME_ASSEMBLIES_AND_TOOLS.md). For architecture overview see [TECHNICAL.md](./TECHNICAL.md).

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
| **Pet / puzzle partials** | `FindLoadedType` in `PetFeedFeature.cs`, `PetPlayFeature.cs`, `PuzzleNetFeature.cs` | Same core API. |

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
- Net cook `PrepareCooking` via `CookingSystem` in some builds.
- Reading interact targets and entity lists when managed reflection fails.

Other features (bubble, bird direct command, etc.) **do not** use Mono unless explicitly wired. Prefer `WebRequestUtility.SendCommand` for network actions.

---

## Integration strategies (after type is found)

### 1. Harmony patch

Resolve `Type` → `GetMethod` → `harmonyInstance.Patch(...)`.

Used for movement, transforms, bubble spawn helpers, net cook world registration, etc.

Requires a stable method signature. IL2CPP generic/instance methods may need exact parameter types (`Vector3`, not `object`).

### 2. `WebRequestUtility.SendCommand<T>` (network commands)

**Flow:**

1. Resolve command struct: `FindLoadedType("XDT.Scene.Shared.Modules….SomeNetworkCommand", "SomeNetworkCommand")`.
2. Resolve `WebRequestUtility` and `ChannelType`.
3. `GetMethods` → find static generic `SendCommand` with **3 parameters**.
4. `MakeGenericMethod(commandType)` and `Invoke(null, new object[] { commandInstance, reliable, channel })`.

**Bubble spawn (preferred path):** Harmony **prefix** on the generic `SendCommand` definition rewrites `location` on bubble create commands — does not require `BubbleComponent` or `ActivityEventProtocolManager` types.

Same pattern: bird photo (`TakingBirdPhotoCommand`), net cook prepare commands, `GetBubbleAward`, etc.

### 3. Reflection invoke (no Harmony)

`MethodInfo.Invoke` / `Activator.CreateInstance` on resolved types:

- `Entities.GetComponents<T>()`
- `EntityUtil`, `InteractSystem`, `GameplayApi`
- UI/HUD components

### 4. GameObject hierarchy paths

Some UI/world hooks use `GameObject.Find("…")` with fixed paths (fragile on game updates). Documented separately in TECHNICAL.md — not type resolution, but often used together.

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
| Player | `Character`, `p_player_skeleton(Clone)` via `GameObject.Find` |

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

---

## Related documentation

- [BUILD_AND_RUN.md](./BUILD_AND_RUN.md) — interop generation, deploy, logs
- [GAME_ASSEMBLIES_AND_TOOLS.md](./GAME_ASSEMBLIES_AND_TOOLS.md) — EcsClient, LocalLow `DotnetAssemblies`, tool checklist
- [TECHNICAL.md](./TECHNICAL.md) — architecture, Harmony, farms
- [FEATURES.md](./FEATURES.md) — what each feature expects at runtime
- [BACKPACK_AND_ITEMS.md](./BACKPACK_AND_ITEMS.md) — inventory enumeration and per-feature filters
