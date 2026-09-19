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
4. **Lay out the storage tree** — see §5. After that, the files the launcher carries — the
   bootstrap, the generator shim and, in the offline build, the mod — are rewritten only when a
   different launcher ran here last: `bin\launcher.version` records the version, commit and flavour
   (`3.0.5+c5ef461 offline-nolink`) of the one that last wrote them. A new launcher replaces every
   file that differs and moves the record on; the same launcher writes only what is missing and
   leaves the rest, so a mod DLL replaced by hand stays until the next launcher version (the log
   says it was kept; deleting it brings the launcher's copy back). The flavour is in the record
   because offline and offline-nolink of one version carry different mods. A file the running game
   holds open is left as it is and logged, and the record then stays behind for another try.
5. **Fetch or update the mod** (online build only; the offline build carries it). A newer
   release is installed on the way past — the mod and the launcher ship in the same release, so
   the tag the update check already recorded, set against the version the installed DLL declares,
   answers this without another request. The installed version is read out of the DLL, not out of
   `bugtopia.version`: that file says what was last downloaded, not what is there now. A build
   chosen by hand in expert mode is pinned and left alone, and a failed update is logged rather
   than fatal: the copy already installed works, and the game still starts.
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
├── native/            ← only on installs older launchers made: their webview shell; safe to delete
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

"None in the binary" is enforced in the source rather than left to the trimmer: `WinHttp.cs`, the
parts of `GitHub.cs` that talk to the API, the download half of `Downloads.cs`, and the update and
download paths in `Api.cs` - with the window commands that start them - sit behind
`#if BUGTOPIA_ONLINE`, which both `BugtopiaLaunch` and `BugtopiaLauncher` define for the online
build. Runtime guards on `Downloads.Enabled` were not enough: those methods stayed reachable from
the window commands and `Launch`, so NativeAOT compiled them into the offline exe and only the HTTP
client itself was trimmed. The releases page URL goes with them: `GitHub.ReleasesPage` and the
`updateVersion` / `releasesPage` fields of the window state are online-only, so a `LatestSeen` left
behind by an online build - both read the same `%LocalLow%\Bugtopia\launcher.json` - cannot make an
offline build show an update notice. To check a build, publish it with `-p:IlcGenerateMapFile=true`
and look for `MethodCode` entries for `GitHub`, `Downloads` or `WinHttp` in the map, and for
`baboodev/Bugtopia` in the exe's strings.

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

**UI.** A native Win32 window (`BugtopiaLauncher/Win32/`), nothing but P/Invoke: backgrounds, cards,
icons and gradients drawn with GDI+, text with GDI (ClearType), buttons and the checkboxes as real
owner-drawn `BUTTON` controls so Tab, Space, Enter, focus and screen readers work as they do anywhere,
paths and the log in real read-only `EDIT` controls so they can be selected and copied. The selects
open a list of their own, the question dialog is a window of its own, and the file and folder pickers
are the system `IFileOpenDialog`, called through its vtable. The window talks to `Api` in JSON over
the same commands and events the WebView2 page it replaced did - which is what let it be ported from
that page's script function for function. It replaced Photino over WebView2 because Photino's native
shell imports `URLDownloadToFileW` (to fetch the WebView2 installer) and WebView2 is a browser engine
that goes to the network on its own; the exe now imports nothing network-related, and needs nothing
installed on the machine.

**Windows APIs.** Remote-thread injection through `kernel32` (`OpenProcess`, `VirtualAllocEx`,
`WriteProcessMemory`, `CreateRemoteThread`); HTTPS through `winhttp.dll`; `SHGetKnownFolderPath` for
`%LocalLow%`; the registry for Steam's paths and the zone server. The native bootstrap adds a window
subclass rendezvous (`EnumWindows`, `GetClassNameW`, `SetWindowLongPtrW`, `CallWindowProcW`) and
hosts CoreCLR with `coreclr_initialize` / `coreclr_create_delegate`.

**Data.** `System.Text.Json` used reflection-free — `JsonDocument` and `Utf8JsonWriter`, plus a
source-generated context for settings. Zip through `System.IO.Compression`. INI (doorstop) and VDF
(Steam) are hand-scanned.

**Deliberately absent.** `HttpClient`, which cost 3.5 MB of binary against WinHTTP's nothing;
`System.Uri`, 65 KB to parse four known-good addresses; and `Regex`, 231 KB to read one version
number.

---

## 8. Build

```powershell
pwsh launcher/build-launchers.ps1
```

Builds the bootstrap, the shim and the mod's BepInEx flavour, then publishes both launchers into
`release/`. The mod is built the way CI ships it — `-c ReleaseShip -p:Loader=BepInEx
-p:ContinuousIntegrationBuild=true` — and that last switch is also what turns the csproj's
`DeployModToGame` target off, so packaging a launcher never replaces the mod installed in the game
folder. Useful switches:

```powershell
# package a published release instead of a local mod build
pwsh launcher/build-launchers.ps1 -PluginDll C:\downloads\bugtopia-bepinex.dll

# working on the launcher itself; the offline build then has no plugin in it
pwsh launcher/build-launchers.ps1 -SkipMod

# also the offline launcher carrying the BepInEx mod built without the Telegram link
pwsh launcher/build-launchers.ps1 -NoLink
```

`-NoLink` adds `Bugtopia-Launcher-<version>-offline-nolink.exe`: the offline build with the BepInEx
mod compiled `-p:Telegram=false` inside it. It publishes into its own `bin\offline-nolink\`, and the
script fails if that plugin still carries a Telegram trace - the check CI runs on the Universal
no-link DLL. CI builds it on every tag and attaches it to the release.

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

**Run the exe from `publish\`** - that is the file the scripts ship.

---

## 9. CI

`.github/workflows/melonloader-releaseship.yml` runs on `v*` tags only and builds the mod first, so
the offline launcher embeds `dist\bugtopia-bepinex.dll` — the exact file the release publishes, not
a second copy built beside it. The offline-nolink launcher embeds `dist\bugtopia-bepinex-nolink.dll`,
built with `-p:Telegram=false` and checked for Telegram traces like the Universal no-link DLL; that
DLL itself goes into the artifacts but not onto the release. All three exes go into the artifacts
and onto the release, named from the tag:

```
Bugtopia-Launcher-2.8.3-offline.exe
Bugtopia-Launcher-2.8.3-online.exe
Bugtopia-Launcher-2.8.3-offline-nolink.exe
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

**Moving a child control carries its old pixels with it.** `SetWindowPos` does not repaint a moved
window: it copies what was on screen to the new place. A scroll moves the controls one after another,
so a text box could pick up a piece of a button moved into its old place a moment before, and keep
it - the box thinks the copied pixels are its own. Every move uses `SWP_NOCOPYBITS` (`SWP_MOVECHILD`
in `Native.cs`) and every child has `WS_CLIPSIBLINGS`. Only a real screen capture of a paced scroll
shows the fault; `PrintWindow` asks each window to draw itself afresh and never does.

**Dark scrollbars, text boxes and menus are undocumented.** They come from `uxtheme.dll`'s ordinals
133/135/136 (`AllowDarkModeForWindow`, `SetPreferredAppMode`, `FlushMenuThemes`) and the
`DarkMode_Explorer` theme - what Explorer itself uses. They are looked up by ordinal with
`GetProcAddress`, never imported, so a Windows without them keeps the light parts rather than
failing to start.

**No exception may leave a window procedure.** Under NativeAOT an exception unwinding back into
user32 is a fail-fast with no log. `Surface.WindowProc` and the button subclass catch everything and
report it through `Api`.

**The window appears only once it has something to show.** It is created hidden and shown after the
first state has been drawn, so there is no empty frame first; a five-second timer shows it
regardless, so a state that never arrives cannot leave a launcher with no window. The auto-launch
countdown starts only once the window is on screen.

**The exe's version information is its own.** Left to itself, NativeAOT copies the Win32 resources
out of the managed `Bugtopia.dll` the compiler produced, and the compiler writes that file's name
into `InternalName` and `OriginalFilename` - so the published exe called itself `Bugtopia.dll`,
which is also the name VirusTotal filed it under. `IlcGenerateWin32Resources` is off and
`Bugtopia.rc` is linked in instead, with the version strings written into `launcher_version.h` at
publish time by `CompileLauncherResources`. The exe's icon, manifest and version fields are changed
there; `ApplicationIcon` in the csproj now reaches only the build-output apphost. The step needs
`rc.exe`, which comes with the Windows SDK the C++ workload installs.

**`UnityLogListening` must be off in `BepInEx.cfg`.** Left at its default, the chainloader installs a
Unity log handler before any plugin loads, which pulls in Il2CppInterop's delegate support and
applies the ClassInjector hooks the mod's own HookTrim exists to suppress. `Payload.Prepare` writes
this, along with the console and Unity-log settings, on every prepare.

**The interop generator swallows its own failures.** BepInEx catches everything inside
`GenerateInteropAssemblies` and logs it, so the only proof of success is the hash file it writes.
`InteropHost` checks for that rather than trusting the call to have thrown.
