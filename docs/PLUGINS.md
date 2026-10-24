# Plugins

A plugin is a `net6.0` class library loaded into the game process next to the mod. It has the same
reach as the mod. The collectible load context exists so a plugin can be unloaded and replaced; it
is not a permission boundary. A plugin can send commands, read game state, and fault the process.

The contract types live in `bugtopia.dll`, namespace `HeartopiaMod.Plugins`
(`buddy/PluginContract.cs`). A plugin references that dll with `<Private>false</Private>` and does
not copy it. `PluginSdk.Version` is **3**.

`IHostApi.Mod` returns the live `HeartopiaComplete`. That object is an unstable escape hatch: a
member on it can disappear in any build. Code that should keep working across mod updates calls
`IHostApi` only.

## Folder

```
%LocalLow%/Bugtopia/Plugins/
  HelloBugtopia.dll
  HelloBugtopia.pdb          optional, same stem, gives line numbers in stack traces
  HelloBugtopia.enabled      written by the Enabled toggle; without it the dll is not loaded
  HelloBugtopia.disabled     optional hard skip; wins over .enabled
  state/<id>.json            IHostApi.State
  resident.json              who is loaded; the host rewrites it on load and unload
  quarantine.json            sha256 values refused after a killed session
  README.txt                 written once by the host
```

Both loaders use this folder. Do not put a plugin in `BepInEx/plugins` or `Mods/`: those loaders
would try to load it as one of their own plugins.

Rules:

- One `*.dll` in the folder root. The file name without `.dll` is the id.
- An id is 1–64 characters: letters, digits, `.`, `_`, `-`.
- `bugtopia.dll` and `*.resources.dll` are skipped.
- Load order is ordinal by file name.
- A plugin is one assembly. It can use assemblies the game and the mod already have loaded
  (`bugtopia`, Unity modules). A dependency dropped beside it is not loaded.
- Ids are shared with `plugin.load`. A disk file named `sample.dll` and an MCP load of id `sample`
  are the same slot.

A dll in the folder is not loaded on its own. The first load is the **Enabled** toggle on the
Plugins tab: that writes `<id>.enabled` and loads immediately. The next launch loads that dll
once, at mod start, before a world exists. Later world entries do not reload. Unchecking deletes
`<id>.enabled` and unloads. A `<id>.disabled` sibling skips the dll even when `.enabled` is present.
**Plugins** is its own sidebar tab, shown only while that folder contains at least one plugin dll (a
`.disabled` or `.enabled` sibling still counts). With an empty folder the tab is not in the menu. Opening
the menu rebuilds the sidebar if a dll has appeared or the last one has been removed.
**Plugins → Reload** unloads disk-loaded plugins and scans again, loading only the ones that are
turned on. Plugins loaded only through `plugin.load` stay.

## Enable, quarantine

The Enabled toggle on Plugins is the opt-in. It writes or deletes `<id>.enabled` and loads or
unloads immediately.

`resident.json` records the loaded set and whether that session installed a quit signal
(`Application.quitting`). If the process is killed, the file is still there and `quitSignal` is
true: those sha256 values are appended to `quarantine.json` and the next launch will not autoload
them. An ordinary quit clears the list, so a normal exit does not quarantine anything. If the quit
signal could not be installed, a surviving file is logged and nothing is quarantined.

To load a quarantined dll anyway: Plugins → **Load anyway**, or delete its line in
`quarantine.json`. `plugin.load` with `force: true` clears that sha from the same file and loads.

## Contract

One public parameterless-constructible type per assembly implements `IBugtopiaPlugin`. Every method
runs on the Unity main thread.

| Method | When |
|---|---|
| `Load(IHostApi host)` | Once, after the assembly is in memory. An enabled disk plugin gets this at mod start, before a world exists. Throwing aborts the load and unloads the context. |
| `Tick()` | Every frame, including the login screen. A tick that throws repeatedly is cooled down by the host's circuit breaker. |
| `Unload()` | Once, before the context is dropped. Best-effort. |
| `Call(string method, string argsJson)` | `plugin.call`. Return JSON, or null if the name is not handled. |

`IHostApi`:

| Member | |
|---|---|
| `Id`, `PluginDirectory` | Id as above. Directory is the absolute plugins folder. |
| `Log`, `Toast` | Mod log, prefixed with the id. On-screen toast. |
| `IsWorldReady`, `WorldEpoch` | False on the login screen and during loading. Resolving or invoking game Mono before it is true faults the process. Drop per-world caches when the epoch changes. |
| `TryGetPlayerPosition` | Local player position, or false. |
| `StartCoroutine` | The only sanctioned scheduler. Stopped on unload. |
| `State` | `IDictionary<string, string>`, written to `state/<id>.json`. A newline in a key or value is refused and logged. Flushed at least on unload, and about once a second while ticking if it changed. |
| `Mono` | Embedded Mono. See below. |
| `Events` | EventCenter subscriptions. Revoked on unload. |
| `Ui` | A page on the Plugins tab, next to Installed. Removed on unload. |

`IUiApi.AddPage` returns an `IPluginPage`: `AddLabel`, `AddNote`, `AddButton`, `AddToggle`,
`AddSlider`, `Clear`, `Remove`. Labels return `IPluginLabel.SetText` for live values. Elements stack
in order. Cap is 150 elements per page. Callbacks go through a host trampoline that is cut on
unload, which is why a button may close over the plugin instance.

`IEventsApi.Subscribe(eventFullName, payloadBytes, handler, out error)` copies `payloadBytes` of the
event struct for the handler. Each distinct event type that no shipped feature already hooks spends
one of the engine's 48 slots and is never returned. A handler registered on the engine directly
would pin the load context forever; the host owns the registration and only the callback is the
plugin's.

`IMonoApi` is the AuraMono surface: `FindClass`, `FindMethod` (name **and** parameter count),
`Invoke`, `GetComponents`, `EnumerateCollection`, field readers, `Pin` / `Unpin`, `SendCommand`,
`ValidateCommand`.

- An object `IntPtr` from `Invoke` is a `MonoObject*` the moving GC relocates. Read it immediately.
  Do not store one across a frame or a `yield`.
- `GetComponents` and `EnumerateCollection` return `IMonoObjects`. Use it in a `using` and copy
  scalars out inside the loop.
- `SendCommand` sends a real command to the live server. `ValidateCommand` does the same work and
  stops before the send.

`IHostApi` gained `PluginDirectory` in version 3, and `State` is persisted from that version on.
Adding a member to `IHostApi` does not break a plugin that does not call it. Adding a method to
`IBugtopiaPlugin` does break old plugins; do not do that without a reason that is worth a rebuild
of every plugin.

## What the host rejects

`PluginValidator` reads the assembly metadata and refuses the load if any of these are present.
Each one would keep the load context alive after unload:

| | Why |
|---|---|
| Reference to `0Harmony` or `MonoMod.RuntimeDetour` | A detour targets a trampoline inside the plugin. Unload jumps into freed memory. |
| `Il2CppInterop.Runtime.Injection.ClassInjector` | Injected types are permanent. |
| `Il2CppInterop.Runtime.DelegateSupport` | The wrapper is cached in a static. |
| `System.Threading.Thread`, `System.Threading.Timer`, `System.Timers.Timer` | A live thread keeps the context alive. Use `StartCoroutine`. |
| Any `[DllImport]` | The native module handle is cached in the context. |

The direction that matters: the plugin may hold the host. The host must not hold the plugin. The
host drops its references, then unloads, then checks collection over a few frames. A context that
survives is `leaked` in `plugin.list`: nothing ticks it, the memory returns on restart, and a new
load of the same id still works.

## Shipped plugins

| Project | Release file | Role |
|---------|--------------|------|
| `plugins/BugtopiaMcp` | `BugtopiaMcp.dll` | Agent bridge. Page title **MCP Bridge**. [MCP_BRIDGE.md](./MCP_BRIDGE.md) |
| `plugins/HelloBugtopia` | `HelloBugtopia.dll` | Template |

The release workflow builds these once against `bugtopia-bepinex.dll` and attaches the dll. `internal-plugins/` is not in the repository, so it is not built or published.

## Build

`buddy/build-all.bat` and `dotnet build buddy/buddy.csproj` compile every `plugins/*/*.csproj`, and every `internal-plugins/*/*.csproj` when that folder exists, against the `bugtopia.dll` just produced. `-p:BuildPlugins=false` skips them. A missing `internal-plugins` folder is not an error. Each dll, and its pdb when the build produced one, is copied into `%LocalLow%/Bugtopia/Plugins`. The build does not write `.enabled`. A locked file (the game still has it loaded) warns and leaves the previous copy in place.

One project on its own, using the template:

```powershell
dotnet build plugins/HelloBugtopia/HelloBugtopia.csproj -c Release
```

Copy only the dll. The pdb is optional: same file name, and it is what turns a stack trace into
file and line numbers. Without it the plugin runs the same and the log still names the methods.

```powershell
Copy-Item plugins\HelloBugtopia\bin\Release\net6.0\HelloBugtopia.dll -Destination "$env:USERPROFILE\AppData\LocalLow\Bugtopia\Plugins" -Force
```

Enter a world and turn **Enabled** on under Plugins. The log line is `[Plugin] loaded 'HelloBugtopia'`. The
Plugins tab shows the page. Restarting the game loads it again and increments `runs` in
`state/HelloBugtopia.json`. Without `HelloBugtopia.enabled` it stays unloaded. A sibling
`HelloBugtopia.disabled` skips it on the next start. A dll that is already loaded is replaced by
**Plugins → Reload**, or by toggling Enabled off and on.

`BugtopiaDll` and `InteropDir` on the csproj override the reference paths. `InteropDir` must be the
loader interop folder that contains `UnityEngine.CoreModule.dll`.

### Internal plugins

`internal-plugins/` is gitignored. Projects there are not listed in `buddy.csproj` and are never
compiled into `bugtopia.dll`. Each subdirectory is one local plugin. The mod build compiles them
when the folder exists and copies the dll with the others. Building one by hand is the same
command, with that project's name, and that path does not copy:

```powershell
dotnet build internal-plugins/Disconnect/Disconnect.csproj -c Release
Copy-Item internal-plugins\Disconnect\bin\Release\net6.0\Disconnect.dll -Destination "$env:USERPROFILE\AppData\LocalLow\Bugtopia\Plugins" -Force
```

`Disconnect` returns to the city-select screen by calling `GameWorld.EnterLevel(0, null)`, the same
transition as the in-game settings exit. The button is on its Plugins page.

## Agent path

`plugin.load` / `plugin.loadFile` / `plugin.unload` / `plugin.call` load the same host. `plugin.load`
sends the assembly as base64. `plugin.loadFile` reads `%LocalLow%/Bugtopia/Plugins/<id>.dll` and a
sibling pdb. That protocol is [MCP_BRIDGE.md](./MCP_BRIDGE.md) §4b. The bridge itself is the
`BugtopiaMcp` plugin. `bugtopia.dll` has no socket. `buddy/build-all.bat` compiles it and copies
it into the Plugins folder. It does not write `.enabled`.
