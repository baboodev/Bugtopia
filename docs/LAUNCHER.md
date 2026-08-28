# Launcher

The launcher is a single Windows exe that installs and starts the mod without putting one file in
the game folder. It is a separate product from the mod: its own projects under `launcher/`, its own
build, its own release assets. The mod does not know it exists.

- Source: `launcher/`
- Build: `launcher/build-launchers.ps1`
- Publish only: `ci/publish-launcher.ps1`
- Related: [BUILD_AND_RUN.md](BUILD_AND_RUN.md) for the mod's own build

---

## 1. Why it exists

BepInEx installs itself by putting a proxy DLL (`winhttp.dll`) beside the game and letting Windows
load it. That leaves files in a folder the game's own updater owns, cannot be undone by deleting one
thing, and is visible to anything that looks.

The launcher starts the game itself and injects the bootstrap once the IL2CPP runtime is up. The
bootstrap's configuration travels in the child process's environment block, so **nothing of ours has
to exist in the game folder** and a crash leaves nothing to clean up.

---

## 2. The five projects

| Project | Platform | What it is |
|---|---|---|
| `BugtopiaInject` | native C, x64, static CRT | the bootstrap injected into the running game |
| `BugtopiaInterop` | **net6.0** class library | generator shim, loaded by BepInEx's own CoreCLR |
| `BugtopiaLaunch` | net8.0-windows | everything that is not UI |
| `BugtopiaLauncher` | net8.0-windows, WinExe, **NativeAOT** | the application |
| `InjectCli`, `InteropGen` | net8.0 | command-line tools for working on the above |

`BugtopiaInterop` targets **net6.0 on purpose**: it is loaded into the CoreCLR 6.0.7 that BepInEx
carries, not into ours. Retargeting it breaks interop generation with a type-load error.

`BugtopiaInject` uses the static CRT (`/MT`) on purpose: it is loaded into someone else's process,
and a dependency on the VC redist being installed there is a failure mode with no good diagnostic.

---

## 3. What happens when Launch is pressed

One job, each step skipped when its result is already on disk:

1. **Find the game.** Steam's own library list out of the registry and `libraryfolders.vdf`, then
   the usual folders.
2. **Adopt or clear what is already installed.** A doorstop-style BepInEx install or a MelonLoader
   one boots from inside `il2cpp_init`, before the injection, and the bootstrap will not start a
   second runtime in one process — so an existing install replaces this launcher rather than
   coexisting with it. A BepInEx tree is *moved* into storage, interop and all; MelonLoader is
   removed. Never without a modal yes.
3. **Fetch BepInEx** (online build) or use the archive the user chose (offline build).
4. **Lay out the storage tree** — see §5.
5. **Fetch the mod** (online build only; the offline build carries it).
6. **Put the Unity base libraries in `unity-libs`.**
7. **Generate the interop assemblies** — see §4.
8. **Start the game and inject the bootstrap.** The launcher closes itself on success.

The launcher **is** the injector, so it has to stay alive from the moment the game starts until the
bootstrap is inside it. That is why it closes when the job finishes rather than on the click.

---

## 4. Interop generation without the game

`Il2CppInteropManager.Initialize()` is two halves: `GenerateInteropAssemblies()`, which is pure file
I/O, and `Il2CppInteropRuntime.Create().Start()`, which needs a live runtime. Only the first is
needed to produce the ~81 MB of interop assemblies, so `BugtopiaInterop` hosts BepInEx's own CoreCLR
and calls it by reflection — no game running, and no .NET installed on the machine.

It runs in a **child copy of the launcher exe** (`Bugtopia.exe interop --game … --storage …`) because
`coreclr_initialize` can only be called once per process and the generator caches paths in statics.

Staleness is BepInEx's own hash: MD5 over `GameAssembly.dll` plus every `unity-libs\*.dll`, recorded
in `interop\assembly-hash.txt`. The launcher's own "expired" badge is only a timestamp comparison —
the real check happens in the generator on the next launch.

---

## 5. Storage tree

Default `%LocalLow%\Bugtopia\runtime`, chosen in the launcher. Its shape is forced, not designed:
BepInEx derives its root from the **grandparent of the preloader DLL** and hangs everything off it.

```
runtime/
├── BepInEx/
│   ├── core/          ← copied from the archive
│   ├── plugins/       ← bugtopia.dll + bugtopia.version
│   ├── config/        ← BepInEx.cfg
│   ├── interop/       ← generated; assembly-hash.txt is the staleness record
│   └── unity-libs/
├── dotnet/            ← CoreCLR 6.0.7, copied from the archive
├── bin/               ← bugtopia_inject.dll, BugtopiaInterop.dll, logs
├── native/            ← Photino.Native.dll (hashed folder), bugtopia.ico
└── download/          ← unpacked BepInEx archive
```

Deleting `runtime/` undoes the whole installation.

---

## 6. The two builds

A **compile-time** switch, not a setting, so an offline build cannot reach the network even by
accident and does not link the stack it would need to.

| | offline (default) | online (`-p:BugtopiaOnline=true`) |
|---|---|---|
| Mod | embedded | fetched from the releases page |
| BepInEx | user picks the archive | downloaded |
| Network code | none in the binary | WinHTTP |
| Size | ~7.9 MB | ~4.2 MB |

They build into `bin\<flavour>\` and `obj\<flavour>\` so both exist at once. Two things that needs,
both in `launcher/Directory.Build.props`: an explicit `DefaultItemExcludes` (the SDK excludes only
the *current* flavour's obj from source globs, so the generated `AssemblyInfo` from both would be
compiled) and an explicit import of the parent `Directory.Build.props` (MSBuild imports only the
nearest one).

---

## 7. Technologies

**Runtime.** .NET 8 published with NativeAOT — one file, no runtime on the machine. `TrimMode=full`,
`IlcOptimizationPreference=Size`, `InvariantGlobalization`, and `EventSourceSupport`,
`MetadataUpdaterSupport`, `HttpActivityPropagationSupport` all off.

**UI.** Photino.NET 4.0.16 over Photino.Native 4.0.22 → WebView2, driven by plain P/Invoke. The
WebView2 Runtime is the only thing the machine needs. The page is one embedded `ui.html`: HTML, CSS
and vanilla JS, no framework and no web fonts (an offline build has to look the same with no
network). Messages cross on `window.external.sendMessage` / `receiveMessage` as JSON; modals are the
native `<dialog>` element.

**Windows APIs.** Remote-thread injection through `kernel32` (`OpenProcess`, `VirtualAllocEx`,
`WriteProcessMemory`, `CreateRemoteThread`); HTTPS through `winhttp.dll`; `SHGetKnownFolderPath` for
`%LocalLow%`; the registry for Steam's paths and the zone server. The native bootstrap adds a window
subclass rendezvous (`EnumWindows`, `GetClassNameW`, `SetWindowLongPtrW`, `CallWindowProcW`) and
hosts CoreCLR with `coreclr_initialize` / `coreclr_create_delegate`.

**Data.** `System.Text.Json` used reflection-free — `JsonDocument` and `Utf8JsonWriter`, plus a
source-generated context for settings. Zip through `System.IO.Compression`. SHA-256 as the cache key
for the unpacked native shell. INI (doorstop) and VDF (Steam) are hand-scanned.

**Deliberately absent.** `HttpClient`, which cost 3.5 MB of binary against WinHTTP's nothing;
`System.Uri`, 65 KB to parse four known-good addresses; and `Regex`, 231 KB to read one version
number.

---

## 8. Build

```powershell
pwsh launcher/build-launchers.ps1
```

Builds the bootstrap, the shim and the mod's BepInEx flavour, then publishes both launchers into
`release/`. Useful switches:

```powershell
# package a published release instead of a local mod build
pwsh launcher/build-launchers.ps1 -PluginDll C:\downloads\bugtopia-bepinex.dll

# working on the launcher itself; the offline build then has no plugin in it
pwsh launcher/build-launchers.ps1 -SkipMod
```

`ci/publish-launcher.ps1` is the publish half on its own — CI calls it directly, so a local build
and a release build cannot drift apart. It refuses to publish when a payload file is missing or when
a launcher is running from the build output, both of which the build only warns about.

### Prerequisites

- .NET SDK 8+ (and the .NET 6 targeting pack for the shim)
- **MSVC with the C++ workload** — NativeAOT needs the linker, and the bootstrap needs `cl.exe`.
  Set `BUGTOPIA_VCVARS` to override the toolchain the batch file finds.
- The mod's own prerequisites, unless `-PluginDll` or `-SkipMod` is used

### If ILCompiler cannot find the linker

In a shell where its `vswhere` probe does not run, initialise `vcvars64.bat` first and add
`-p:IlcUseEnvironmentalTools=true`. Note that vcvars sets `Platform=x64`, which moves the output
under `bin\<flavour>\x64\`. Both scripts already do this.

**Always run the exe from `publish\`.** `bin\…\native\` holds the binary without its native shell,
and a missing P/Invoke target under NativeAOT fail-fasts with `0xC0000409` instead of saying what is
missing.

---

## 9. CI

`.github/workflows/melonloader-releaseship.yml` runs on `v*` tags only and builds the mod first, so
the offline launcher embeds `dist\bugtopia-bepinex.dll` — the exact file the release publishes, not
a second copy built beside it. Both exes go into the artifacts and onto the release, named from the
tag:

```
Bugtopia-Launcher-2.8.3-offline.exe
Bugtopia-Launcher-2.8.3-online.exe
```

---

## 10. Logs

| What | Where |
|---|---|
| The launcher | `%LocalLow%\Bugtopia\launcher.log` |
| The bootstrap, from inside the game | `<storage>\bin\bugtopia_inject.log` |
| The interop generator | `<storage>\bin\interopgen.log` |
| BepInEx itself | `<storage>\BepInEx\LogOutput.log` |

The launcher writes every line it shows to its own file first, so a launcher that dies before the
window opens still leaves an account of it.

---

## 11. Traps

**A proxy in the game folder wins.** `winhttp.dll` or `version.dll` beside the exe boots BepInEx
during `il2cpp_init`, before any injection, and the bootstrap then refuses to start a second
runtime. This is what the adoption step exists to resolve.

**The page's initial string must stay small.** Whatever is handed to `LoadRawString` goes to the
native side at window creation, and a page carrying the logo as a data URI — 58 KB against 26 KB —
access-violates inside `Photino.Native.dll` on **every** start. Anything large reaches the page over
the message bridge after load instead.

**`UnityLogListening` must be off in `BepInEx.cfg`.** Left at its default, the chainloader installs a
Unity log handler before any plugin loads, which pulls in Il2CppInterop's delegate support and
applies the ClassInjector hooks the mod's own HookTrim exists to suppress. `Payload.Prepare` writes
this, along with the console and Unity-log settings, on every prepare.

**The interop generator swallows its own failures.** BepInEx catches everything inside
`GenerateInteropAssemblies` and logs it, so the only proof of success is the hash file it writes.
`InteropHost` checks for that rather than trusting the call to have thrown.
