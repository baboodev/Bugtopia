# Build and Run Guide

How to build **Bugtopia**, deploy it for **MelonLoader** or **BepInEx**, and verify that it loads.

---

## Overview

| Item | Value |
|------|-------|
| Mod loaders | [MelonLoader](https://melonloader.co/download.html) **or** [BepInEx IL2CPP](https://docs.bepinex.dev/) |
| Target framework | .NET **6.0** (x64) |
| Output assembly | **`bugtopia.dll`** (same name for both loaders) |
| Core logic | `HeartopiaComplete` (plain class, not tied to a loader) |
| Loader entry points | `MelonLoaderPlugin.cs` / `BepInExPlugin.cs` |
| Shared abstractions | `ModLogger.cs`, `ModCoroutines.cs` |
| Project file | `buddy/buddy.csproj` |
| Solution | `buddy/buddy.sln` (single project) |
| Plugin version string | `1.0.0` |

One codebase compiles twice with MSBuild property **`Loader`**:

| `Loader` value | Define | References from |
|----------------|--------|-----------------|
| `MelonLoader` (default) | `MELONLOADER` | `MelonLoader/net6/`, `MelonLoader/Il2CppAssemblies/` |
| `BepInEx` | `BEPINEX` | `BepInEx/core/`, `BepInEx/interop/` |

**Use only one loader in the game at a time.** Do not install MelonLoader and BepInEx together.

---

## Prerequisites

1. **Heartopia** with the chosen mod loader installed and run at least once (generates interop assemblies).
2. **.NET SDK 6+** (`dotnet --version`).
3. **Windows** (Win32 APIs for input and paths).
4. Optional: Visual Studio 2022 with .NET desktop workload.

### Required folders after first game launch

**MelonLoader build:**

```
<HeartopiaDir>/MelonLoader/net6/
<HeartopiaDir>/MelonLoader/Il2CppAssemblies/
```

**BepInEx build:**

```
<HeartopiaDir>/BepInEx/core/
<HeartopiaDir>/BepInEx/interop/
```

---

## Game Directory (`HeartopiaDir`)

Default in `buddy.csproj`:

```xml
<HeartopiaDir Condition="'$(HeartopiaDir)' == ''">D:\SteamLibrary\steamapps\common\Heartopia</HeartopiaDir>
```

### Local override

Copy `buddy/Directory.Build.props.example` → `buddy/Directory.Build.props`:

```xml
<Project>
  <PropertyGroup>
    <HeartopiaDir>C:\TapTapGlobal\Apps\231364</HeartopiaDir>
  </PropertyGroup>
</Project>
```

This file is git-ignored. Do not commit machine-specific paths.

| Distribution | Example path |
|--------------|--------------|
| Steam | `C:\Program Files (x86)\Steam\steamapps\common\Heartopia` |
| TapTap | `C:\TapTapGlobal\Apps\231364` |

---

## Building

### Unified build (default — one DLL for both loaders)

```bat
cd buddy
build-all.bat
```

Output — a single DLL that works under both MelonLoader and BepInEx (needs both loaders installed to build):

```
buddy/bin/Universal/Release/bugtopia.dll
```

### Single loader (needs only that loader installed)

```powershell
cd buddy
dotnet build buddy.csproj -c Release -p:Loader=BepInEx       # -> bin/BepInEx/Release/
dotnet build buddy.csproj -c Release -p:Loader=MelonLoader   # -> bin/MelonLoader/Release/
```

One-off custom game path: add `-p:HeartopiaDir="C:\Games\Heartopia"`. Debug builds go to `bin\<Loader>\Debug\`.

---

## Deployment

| Loader | Copy to |
|--------|---------|
| MelonLoader | `<HeartopiaDir>/Mods/bugtopia.dll` |
| BepInEx | `<HeartopiaDir>/BepInEx/plugins/bugtopia.dll` |

Optional: copy `bugtopia.pdb` next to the DLL for debugging.

### Build version

Each build stamps `ModBuildVersion.Display` into the DLL (About tab, MelonLoader/BepInEx metadata):

| Build context | Version shown |
|---------------|---------------|
| Any commit | `{nearest-or-exact-tag} ({short-sha})`, e.g. `1.8.0 (4a31f61)` |
| Exact tag on HEAD | same format; `IsTaggedRelease = true` |
| Commit after nearest tag | numeric from nearest tag + current SHA, e.g. `1.8.0 (4a31f61)` on `v1.8.0~1` |

Override numeric part only: `-p:ModDisplayVersion=1.8.0` (SHA still taken from `HEAD`).

`BepInPlugin` / MelonLoader metadata use `ModBuildVersion.Numeric` (semver). About UI uses `ModBuildVersion.Display`.

Generated at compile time: `buddy/obj/<Loader>/<Configuration>/ModVersion.g.cs` via `ci/resolve-build-version.ps1`.

No installer in-repo — manual copy only.

### Upgrading from Heartopia Helper (`helper.dll`)

1. Remove the old `helper.dll` from `Mods/` or `BepInEx/plugins/` (do not run both).
2. Deploy `bugtopia.dll`.
3. Settings in `%LocalLow%/HelperSettings/` are copied to `%LocalLow%/Bugtopia/` automatically on first run.
4. BepInEx backup log is now `UserData/bugtopia.log` (was `helper.log`).

### BepInEx logging (optional)

Merge settings from `buddy/BepInEx.logging.cfg.snippet` into `BepInEx/config/BepInEx.cfg` for console + disk logs.

Mod backup log (BepInEx only): `<HeartopiaDir>/UserData/bugtopia.log`

---

## First Run Checklist

1. Launch the game (with your loader active).
2. Check logs:

   | Loader | Primary log |
   |--------|-------------|
   | MelonLoader | `MelonLoader/Latest.log` |
   | BepInEx | `BepInEx/LogOutput.log` |

3. Expect lines like:

   ```
   Bugtopia initialized!
   === Attempting Harmony Patches ===
   [OK] Successfully patched CharacterController.Move!
   ...
   AutoFish subsystem disabled.
   === Patch Attempt Complete ===
   ```

   BepInEx also logs: `HeartopiaBehaviour Awake — Update/OnGUI active on BepInEx manager.`

4. Press **Insert** (default) to open the mod menu.
5. Settings persist under `%LocalLow%/Bugtopia/` (see [TECHNICAL.md](./TECHNICAL.md)).

---

## Configuration Data Location

```
%USERPROFILE%\AppData\LocalLow\Bugtopia\
```

Main file: `Config.xml` (XML-serialized `UnifiedConfigData`).

Legacy `{GameFolder}/UserData/` is migrated once on startup if present.

---

## Compiled Source Files

| File | Role |
|------|------|
| `HeartopiaComplete.cs` | Core mod logic + hotkey dispatch (~6.2k lines; the rest of the class is split across `HeartopiaComplete.*.cs` partials) |
| `HeartopiaComplete.Ugui*.cs` | The entire mod menu: uGUI kit, shell chrome, per-tab content, toasts (35 files, ~32k lines) |
| `MelonLoaderPlugin.cs` | MelonLoader entry (`#if MELONLOADER`) |
| `BepInExPlugin.cs` | BepInEx entry + `HeartopiaBehaviour` (`#if BEPINEX`) |
| `ModLogger.cs` | Unified logging (MelonLogger / BepInEx + file) |
| `ModCoroutines.cs` | Unified coroutines (MelonCoroutines / Il2Cpp host) |
| `AuraFarm.cs`, `AutoFishingFarm.cs`, `InsectNetFarm.cs`, `BirdNetFarm.cs` | Automation farms |
| `PetFeedFeature.cs`, `PetPlayFeature.cs`, `PuzzleNetFeature.cs` | Feature partials |
| `HeartopiaResourceVisualEsp.cs`, `HeartopiaDebugEsp.cs` | ESP overlays |
| `HelperPaths.cs`, `LocalizationManager.cs` | Paths + i18n |
| Harmony patches | `CharacterControllerPatch`, `Transform*Patch`, `InputGetKey*` |
| `Properties/AssemblyInfo.cs` | Assembly metadata |

Embedded: `Assets/tree.png`, `Assets/rare_tree.png`.

Orphan `.cs` files in `buddy/` (legacy fish, ECS dump, etc.) are **not** in the project — see [TECHNICAL.md](./TECHNICAL.md).

---

## Troubleshooting Build

| Symptom | Fix |
|---------|-----|
| Missing `Assembly-CSharp.dll` | Wrong `HeartopiaDir`; run game once with loader installed |
| Missing `BepInEx/core/*.dll` | Install BepInEx IL2CPP and launch game once |
| Missing `MelonLoader/net6/*.dll` | Install MelonLoader and launch game once |
| `dotnet` not found | Install .NET SDK 6+ |
| CS errors in orphan files | They are excluded from csproj — ignore or remove from disk |

---

## Troubleshooting Runtime

| Symptom | Fix |
|---------|-----|
| Mod not loaded | Wrong deploy path or wrong DLL name (`bugtopia.dll`) |
| Both loaders installed | Remove one; conflicts are likely |
| Menu won't open | Check keybind in Settings (default Insert) |
| Harmony `[ERR]` lines | Game update broke patches — rebuild against new interop |
| Feature says type `unavailable` / `Null` | See [TYPE_RESOLUTION.md](./TYPE_RESOLUTION.md); enter world, check probe logs, verify names in ILSpy |
| Auto fishing inactive | Use **Resource Gathering → Fishing** (`AutoFishingFarm`); legacy `AutoFishLogic` is not compiled |
| BepInEx: no UI | Check `LogOutput.log` and `UserData/bugtopia.log` |

**BepInEx log (Steam default):** `<Game>/BepInEx/LogOutput.log` — e.g. `C:\Program Files (x86)\Steam\steamapps\common\Heartopia\BepInEx\LogOutput.log`

---

## Version Notes

| Source | Version |
|--------|---------|
| Git tag / release | **v1.4.7** |
| Plugin metadata | **1.0.0** |

When reporting bugs, include game patch, loader name + version, and git commit.

---

## Related Documentation

- [AGENTS.md](../AGENTS.md) — agent/developer onboarding (build, types, dumps)
- [FEATURES.md](./FEATURES.md) — UI and features
- [TECHNICAL.md](./TECHNICAL.md) — architecture and config schema
- [TYPE_RESOLUTION.md](./TYPE_RESOLUTION.md) — runtime type lookup (`FindLoadedType`, SendCommand, Mono)
- [GAME_ASSEMBLIES_AND_TOOLS.md](./GAME_ASSEMBLIES_AND_TOOLS.md) — EcsClient, interop generation, `DotnetAssemblies` dumps, required tools
