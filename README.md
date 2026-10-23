# Bugtopia

Automation and utility mod for [Heartopia](https://store.steampowered.com/app/heartopia). Supports **MelonLoader** or **BepInEx IL2CPP** (build once per loader — use **only one** in the game).

**Updates:** [Telegram channel @bugtopiamod](https://t.me/bugtopiamod)

## Quick start

1. Install [MelonLoader](https://melonloader.co/download.html) **or** [BepInEx](https://docs.bepinex.dev/) for Heartopia.
2. Build or download `bugtopia.dll` (see [docs/BUILD_AND_RUN.md](docs/BUILD_AND_RUN.md)).
3. Deploy:
   - **MelonLoader:** `<Game>/Mods/bugtopia.dll`
   - **BepInEx:** `<Game>/BepInEx/plugins/bugtopia.dll`
4. Launch the game. Press **Insert** to toggle the menu.

Recommended: play in a **private town** when using automation.

## Build (both loaders)

```bat
cd buddy
build-all.bat
```

Configure your game path: copy `buddy/Directory.Build.props.example` → `buddy/Directory.Build.props`.

That build also compiles every project under `plugins/`, and every project under `internal-plugins/` when that folder exists. Output is each project's `bin/Release`. Nothing is copied into the game folder and nothing is enabled for you. `-p:BuildPlugins=false` skips the plugins.

## Plugins

A plugin is a separate dll the mod loads from `%LocalLow%/Bugtopia/Plugins`. Do not put it in `Mods/` or `BepInEx/plugins/`. The Plugins tab (Insert) appears once that folder has a dll. A dll stays unloaded until **Enabled** is turned on; later launches load it again. Contract and the folder layout: [docs/PLUGINS.md](docs/PLUGINS.md).

A release attaches the tracked plugins, built against `bugtopia-bepinex.dll`:

| File | Role |
|------|------|
| `BugtopiaMcp.dll` | Agent bridge. Adds an **MCP Bridge** page. Writes and unsafe stay off until switched on for that session. [docs/MCP_BRIDGE.md](docs/MCP_BRIDGE.md) |
| `HelloBugtopia.dll` | Sample plugin |

Copy the dll (and the pdb, if you want line numbers) into the Plugins folder and enable it. `buddy/build-mcp.bat` builds `BugtopiaMcp` and copies it there; it still does not enable it.

`internal-plugins/` is local and gitignored. The same mod build compiles it when the folder is present, and skips it when it is not. Those projects are not in the release.

## Documentation

| Document | Contents |
|----------|----------|
| [AGENTS.md](AGENTS.md) | **AI / developer onboarding** — docs map, type resolution, decompilations, build |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | Game + mod architecture, source maps, interop/reflection/AuraMono matrix |
| [docs/DECOMPILED_SOURCE_MAP.md](docs/DECOMPILED_SOURCE_MAP.md) | Offline dumps: `ilspy-dumps/` (Mono) + `gameassembly-dumps/` (IL2CPP) |
| [docs/BUILD_AND_RUN.md](docs/BUILD_AND_RUN.md) | Prerequisites, build, deploy, logs |
| [docs/FEATURES.md](docs/FEATURES.md) | Menu tabs and features |
| [docs/PLUGINS.md](docs/PLUGINS.md) | Disk plugins: folder, enable, contract, build |
| [docs/MCP_BRIDGE.md](docs/MCP_BRIDGE.md) | Agent bridge (`BugtopiaMcp`) and the stdio server |
| [docs/BACKPACK_AND_ITEMS.md](docs/BACKPACK_AND_ITEMS.md) | Inventory scan, filters, sorting (auto sell, transfer, daily quests, feed) |
| [docs/TECHNICAL.md](docs/TECHNICAL.md) | Architecture, patches, config |
| [docs/TYPE_RESOLUTION.md](docs/TYPE_RESOLUTION.md) | How the mod resolves IL2CPP types (`FindLoadedType`, SendCommand, Mono) |
| [docs/GAME_ASSEMBLIES_AND_TOOLS.md](docs/GAME_ASSEMBLIES_AND_TOOLS.md) | EcsClient, interop, Mono/IL2CPP dumps, Il2CppDumper workflow, tools |

## Credits

kaikai2020 and contributors from the [UnknownCheats thread](https://www.unknowncheats.me/forum/other-games/736498-heartopia-buddy-teleport-auto-farm.html).

Rayyy2 for original [Heartopia Helper](https://github.com/Rayyy2/Heartopia-Helper) sourcce code

## Disclaimer

This project is for educational and research purposes only. You are solely responsible for any account restrictions or penalties resulting from use of this software.
