# MCP bridge — live game access for an AI agent

A loopback socket in the mod plus a small stdio MCP server, so an agent (Claude Code) can read the
running game: mod state, the world-ready gate, the log, and — as later phases land — entities,
backpack, UI, screenshots, and hot-loaded experimental code.

**Status:** phase 1 (transport, main-thread pump, `status` / `log.tail` / `ping` / `rpc.describe`).
Design and remaining phases: `docs/plans/2026-08-15-mcp-bridge-and-plugin-host.md`.

---

## 1. Topology

```
Claude Code ──stdio (MCP/JSON-RPC)──▶ tools/BugtopiaMcp   (separate process, net10.0)
                                            │
                                            │ NDJSON over TCP 127.0.0.1:8770, token-authed
                                            ▼
                                      bugtopia.dll  ── McpBridgeFeature.cs   (socket + pump)
                                                     ── McpOps.cs             (op registry)
                                                     ── HeartopiaComplete.Mcp.cs (handlers)
```

The game side is deliberately **not** an MCP server. MCP protocol churn lives in the external
process, where a change costs a 3 s rebuild instead of a game restart plus relogin — and that
process stays answerable while the game is closed, so the tool list never vanishes from the client.

---

## 2. Turning it on

Three independent things must all be true. This is intentional: each one is a different kind of
mistake to make.

**1 — Build with the feature.** It is compiled out by default, and it is BepInEx-only.

```bash
dotnet build buddy/buddy.csproj -c Release -p:Loader=BepInEx -p:Mcp=true
```

`-p:Mcp=true` with any other `Loader` is a build error, not a silently different binary. Without the
flag, `#if FEATURE_MCP` strips the code *and* its string literals — such a build cannot be switched
on by any file on disk, because there is nothing to switch on.

**2 — Create the marker file.**

```bash
type nul > "%USERPROFILE%\AppData\LocalLow\Bugtopia\mcp"
```

Same gate class as `beta`: read **once** at startup, never re-read, and **the mod never creates it**
on any code path. The extension is ignored (`mcp`, `mcp.txt`, `mcp.on` all count) because Explorer
hides known extensions. No marker ⇒ no listener, no bound port, no op registry, and one bool test
per frame. Deleting it mid-session does not stop that session — it takes effect on the next launch.

With the marker, the mod menu grows an **Agent** tab at the bottom of the sidebar: the bridge's live
status, its write/unsafe toggles, and a sub-tab for each page a sandbox plugin has added. Without it
the tab is not in the sidebar at all and cannot be selected.

**3 — Build and register the bridge.**

```bash
dotnet build tools/BugtopiaMcp/BugtopiaMcp.csproj -c Release
```

```json
"bugtopia": {
  "type": "stdio",
  "command": "dotnet",
  "args": ["<repo>/tools/BugtopiaMcp/bin/Release/net10.0/BugtopiaMcp.dll"]
}
```

Point it at the built **DLL**, never `dotnet run` — `run` writes build output to stdout, which *is*
the MCP transport, and corrupts the first message.

On startup the mod writes `%LocalLow%/Bugtopia/McpBridge/endpoint.json` (port, token, pid, version).
The bridge reads it on its first tool call and reconnects by itself after a game restart. Note the
folder name: the marker is a *file* called `mcp` in the parent directory, so runtime state cannot
live in a folder of that name.

---

## 2b. Reaching a SECOND machine (Ideapad) over SSH

The bridge binds **loopback only**, so an SSH port-forward is the only way to reach a game running
on another box. Setup used for `Ideapad.local`:

```
this machine                                    Ideapad.local
────────────                                    ─────────────
client ──▶ 127.0.0.1:8780 ──ssh -L──────────────▶ 127.0.0.1:<bridge port>  (8770-8774)
                                                        │
                                                  bugtopia.dll
```

**Local port is 8780, not 8770.** The game usually also runs on the local machine and already owns
8770; a forward that tried to bind it would fail with the game running.

### The keep-alive script

`%LOCALAPPDATA%\BugtopiaMcpIdeapad\tunnel-ideapad.ps1` — run it once and leave it; it owns the
forward and re-establishes it by itself.

**Why it polls rather than just connecting once:** the bridge rotates its token and may pick a
different port on every game launch, and **a game restart does not drop the ssh connection** — the
forward stays up pointing at a dead port while the cached token goes stale, and every call then
fails the handshake. The first version only re-read the endpoint on reconnect and was caught by
exactly that, twice. So it re-reads the remote `endpoint.json` every 15 s: a token-only change is
rewritten in place, a port change tears the forward down and rebuilds it.

It mirrors the remote endpoint into `%LOCALAPPDATA%\BugtopiaMcpIdeapad\endpoint.json` with the
**local** port and the **remote** token — that file is what a client should read.

> No backtick line continuations anywhere in that script; they broke it once already.

### On the Ideapad side

1. `bugtopia.dll` deployed to its `BepInEx\plugins\`.
2. The marker file `%USERPROFILE%\AppData\LocalLow\Bugtopia\mcp` — an **empty file** named `mcp`,
   next to (not inside) the `McpBridge` folder the mod writes its runtime state into.
3. Game running and past the login screen. The log then shows
   `[Mcp] listening on 127.0.0.1:8770 — N ops registered`.

### Talking to it

Point `tools/BugtopiaMcp` at the mirrored endpoint, or speak the protocol directly — it is one
NDJSON line per message over TCP, which is all a throwaway client needs when the MCP client itself
is not connected:

```python
import json, os, socket
ep = json.load(open(os.environ["LOCALAPPDATA"] + r"\BugtopiaMcpIdeapad\endpoint.json"))
s = socket.create_connection(("127.0.0.1", ep["port"]), timeout=10)
f = s.makefile("rwb")

def send(obj):
    f.write((json.dumps(obj) + "\n").encode()); f.flush()
    return json.loads(f.readline().decode())

send({"i": 0, "op": "hello", "a": {"proto": 1, "token": ep["token"]}})   # handshake first
print(send({"i": 1, "op": "player.get", "a": {}}))
```

Every reply is `{"ok":true,"r":{…}}` or `{"ok":false,"e":{…}}`. `rpc.describe` lists every op with
its argument schema.

### Deploying to it

```bash
scp buddy/bin/BepInEx/Release/bugtopia.dll coderus@Ideapad.local:C:/Users/coderus/workplace/HH/BepInEx/plugins/bugtopia.dll
```

The DLL is **locked while the game runs**. Copy it to `bugtopia.dll.new` instead, then swap once the
game closes — that keeps the transfer out of the critical path:

```bash
ssh coderus@Ideapad.local "move /Y C:\...\plugins\bugtopia.dll.new C:\...\plugins\bugtopia.dll"
```

Verify with a hash on both ends rather than trusting the copy (`Get-FileHash` on the far side).

A **sandbox plugin needs no scp at all** — `plugin.load` takes the assembly as base64 over the
bridge and hot-reloads it into the running game (§4b).

### When it will not answer

| Symptom | Cause |
|---|---|
| Handshake refused / connection closed immediately | Stale token — the game restarted. Re-read the mirrored `endpoint.json`; the poller refreshes it within 15 s |
| Connects but every op fails | The forward points at the previous launch's port; the poller rebuilds it on the next tick |
| Nothing on 8780 | The tunnel script is not running, or ssh could not authenticate (`-i ~/.ssh/lenovo`, `BatchMode=yes` hides the prompt) |
| `'<id>' is not loaded` | Sandbox plugins do not survive a game restart — `plugin.load` them again |

---

## 3. Tools (bridge side)

| Tool | Op | Notes |
|---|---|---|
| `game_status` | `status` | Mod build, loader, active pump, world gate, frame/FPS, live feature summaries. Answers on the login screen too |
| `game_log_tail` | `log.tail` | `{n≤500, filter}` — the mod's own log ring, not BepInEx's console |
| `game_ping` | `ping` | Liveness; served on the socket thread, so it answers even mid-freeze |
| `game_eval` | *composite* | Compile a C# snippet here, run it in the game, unload it — see §4c |
| `game_rpc` | *any* | Escape hatch for ops newer than the bridge. Phase 2 replaces most of its use with a `rpc.describe`-driven dynamic tool list |

## 4. Ops (game side)

| Op | Flags | Cost | Served on |
|---|---|---|---|
| `hello` | — | — | socket thread (handshake; carries the whole op catalogue) |
| `rpc.describe` | — | — | socket thread (cached) |
| `ping` | — | — | socket thread |
| `status` | Read | Cheap | main thread |
| `log.tail` | Read | Cheap | main thread |
| `player.get` | Read, NeedsWorld | Cheap | main thread |
| `entities.list` | Read, NeedsWorld | **Heavy** | main thread |
| `ui.tree` | Read | Cheap | main thread |
| `backpack.list` | Read, NeedsWorld | **Heavy** | main thread |
| `quests.list` | Read, NeedsWorld | Cheap | main thread |
| `events.tail` | Read | Cheap | main thread |
| `screenshot` | Read | **Heavy** | main thread + after-render node |
| `mono.search` | Read, NeedsWorld | **Heavy** | main thread |
| `mono.find` | Read, NeedsWorld | Cheap | main thread |
| `mono.describe` | Read, NeedsWorld | **Heavy** | main thread |
| `plugin.list` | Read | Cheap | main thread |
| `plugin.load` | Write+**Unsafe** | **Heavy** | main thread |
| `plugin.unload` | Write+**Unsafe** | Cheap | main thread |
| `plugin.call` | Write+**Unsafe** | **Heavy** | main thread |

**Snapshot ops carry `ageMs`.** `entities.list` and `backpack.list` serve a cached scan rather than
rescanning per call, because an agent polls.

`entities.list` uses a 2 s TTL. A cold scan in a loaded town costs ~5 ms at 2800 objects (~1.0 µs
per object, linear in object count), and `entities.list` reports the split so this never has to be
guessed at again — measured over five samples:

| phase | ms | share |
|---|---|---|
| `findMs` — Unity's own `FindObjectsOfType<GameObject>` | ~2.1 | 42 % |
| `loopMs` — six interop reads per object (wrapper, null-check, `activeInHierarchy`, `transform`, `position`, `name`, `GetInstanceID`) | ~2.7 | 55 % |
| `sortMs` — distance sort | ~0.18 | 4 % |

So the per-object interop reads are the largest single component but **not** the whole cost: removing
the loop entirely would still leave 2.1 ms of `FindObjectsOfType`, which nothing but a different
discovery mechanism can avoid. The TTL is the real mitigation — 40 calls at 5 Hz produced 4 rescans.

`backpack.list` has **no TTL at all**: its cache is invalidated by the game's own
`RefreshBackPackEvent` (AGENTS.md §7, "events first"), so an unchanged inventory is never rescanned
no matter how long an agent polls. That matters because the scan costs ~36–50 ms — three frames. A
large `ageMs` with `dirty:false` therefore means "nothing has changed", not "stale". Registering the
handler costs no hook slot: Auto Sell already hooks that event, and the engine appends handlers to
the existing entry (`events.tail` shows `handlers: 2` on it, which is how to verify the wiring —
a cache that never invalidates looks identical to one that never needs to).

**`entities.list` name filter changes the result set.** Without one, only recognised kinds come back
(player/insect/fish/meteor/bird/bubble — classified with the farms' own predicates, so a kind means
what the feature acting on it means). With one, any object whose name matches is included, which is
how you locate a prefab by name.

**`screenshot` reads the real backbuffer**, so the game UI is in the frame — minimap, quest tracker,
panels. The obvious alternative (aim a camera at a RenderTexture and `Render()`) needs no timing hook
but loses every `ScreenSpaceOverlay` canvas, and "which panel is open" is half of what a screenshot
is for. Reading the backbuffer means running after the frame is rendered, which is why this op is
two-phase: the handler requests the capture, returns `McpOps.Defer`, and the pump re-runs it on the
next frame (`deferredFrames: 1` in practice). The socket is still inside its 5 s wait, so the agent
sees one ordinary synchronous response.

The capture node is installed **lazily and out-of-band** via `PlayerLoopProbe.TryInsertExtraNode`.
It deliberately does not go through `PlayerLoopProbe.Install()`: that method treats a failed insert
as fatal and falls back to an injected MonoBehaviour, i.e. back to ClassInjector and its five
GameAssembly `.text` detours. An optional feature must never be able to cause that. If the
`PostLateUpdate/FinishFrameRendering` anchor is missing the op still works from `LateUpdate` and
flags the result `mayBeTorn`. A session that never asks for a screenshot never touches the player
loop at all.

Cost, measured at 1280×720 quality 72: `readMs` ~5.7, `encodeMs` ~20.5 — so the JPEG encode
dominates, not the pixel read. That is ~1.5 frames, mitigated by a 400 ms rate limit; `quality` and
`maxWidth` are the levers.

**Sandbox plugins can subscribe** through `IHostApi.Events`, and the indirection is load-bearing: the
hook engine has **no unsubscribe path** — its registration installs a native detour and this mod
never tears detours down. A handler registered directly by a plugin would sit in the engine's list
forever, and that handler is a *plugin* type, so its load context could never be collected: hot
reload would silently stop working after the first subscription. `McpEventBroker` therefore registers
one **host-owned** handler per event type, permanently, and swaps the plugin callbacks behind it —
the same treatment detours get. Unload revokes only that plugin's callbacks; the engine registration
stays. Verified: subscribe → 4 events delivered → unload → `leaked: 0`.

Each *distinct* event type costs one of the engine's 48 hook slots and is never reclaimed (~38 are
already used by shipped features); subscribing to a type a feature already hooks costs nothing.
`events.tail` reports sandbox subscriptions in their own `sandbox` block rather than mixed in with
the features' hooks.

> ⚠️ **netIds are per-session.** They are reassigned on every game start, and a command sent with a
> stale one is rejected *silently* — the send reports success and nothing happens. Read the netId
> fresh from `backpack.list` / `entities.list` in the same session you use it. The tell is
> `invalidations` and `totalObserved` staying at 0: the event never fired at all, so the fault is
> upstream of whatever you were about to debug.

**`events.tail` only sees hooked types.** The mod hooks an event type when a feature asks for one;
nothing else is observable. The response lists them under `watching`, so an empty tail is never
ambiguous between "nothing fired" and "nothing is listening". The log is appended from the hook
engine's main-thread drain — never from the detour body, where allocating or calling into Mono
deadlocks the game.

Wire errors: `world_not_ready`, `timeout`, `unknown_op`, `bad_args`, `writes_disabled`,
`unsafe_disabled`, `busy`, `internal`. The bridge rewrites each one into an actionable sentence
rather than passing the bare code through.

---

## 4b. Sandbox plugins (hot reload)

`plugin.load` puts an assembly into a **collectible** `AssemblyLoadContext` so experimental code can
be swapped without restarting the game. Contract: `HeartopiaMod.Plugins.IBugtopiaPlugin`
(`buddy/PluginContract.cs`); reference implementation: `tools/SamplePlugin`.

Bytes, not paths — `LoadFromStream` takes no file lock, so the plugin project can be rebuilt while
the previous version is still live, which is the entire point. Ship the pdb too and exceptions carry
line numbers.

**Unload only works if nothing outside the context references anything inside it**, and the
direction is easy to get backwards: a plugin holding the host is fine; the *host* holding the plugin
is what pins. So unload runs in a fixed order — stop ticking, `Unload()`, revoke coroutines, drop
every host reference, then `alc.Unload()` — and collection is then *verified* through a
`WeakReference` over three GC passes spread across frames. A context that survives is reported as
`leaked` in `plugin.list`: functionally unloaded (nothing ticks it), memory returns on restart,
reload still works because the new version gets a fresh context.

Coroutines are the classic trap: `ModCoroutines` holds the iterator, and the iterator is a *plugin*
type, so one surviving routine pins the whole assembly. That is why `host.StartCoroutine` is the only
sanctioned way to schedule work and why `Thread`/`Timer` are contract violations.

**Violations are caught statically, before the bytes reach the runtime** (`PluginValidator`, via
`System.Reflection.Metadata`): references to `0Harmony`/`MonoMod.RuntimeDetour`, use of
`ClassInjector`, `DelegateSupport`, `Thread`, `Timer`, or any `[DllImport]`. Each one would make the
context uncollectible for the rest of the session, and the failure mode without the check is
miserable — the plugin loads, works, "unloads", and the process quietly grows. All violations are
reported at once so they can be fixed in one pass.

Measured: 20 load→tick→call→unload cycles, 0 failures, 0 leaks, ≤240 ms to confirm collection.

### Loading a built plugin from disk, without shipping base64

`plugin.load` takes the assembly as **base64**, which is right for the protocol and wrong for the
iteration loop: a 19 KB probe is ~25 000 characters, and paying that on every edit-build-reload cycle
dwarfs the work itself. `game_eval` runs *inside* the game process, so it can read the file itself.

Build the plugin, then reload it with a short snippet instead of a wall of base64:

```bash
dotnet build tools/YourPlugin/YourPlugin.csproj -c Release
```

```csharp
// game_eval — usings: System.IO, System.Reflection
string dir = @"C:\...\tools\YourPlugin\bin\Release\net6.0\";
var hostType = typeof(HeartopiaMod.HeartopiaComplete).Assembly.GetType("HeartopiaMod.PluginHost");
var flags = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

// Unload first, so this is a true hot reload rather than an "already loaded" refusal.
try { hostType.GetMethod("TryUnload", flags).Invoke(null, new object[] { "yourid", null }); } catch { }

object[] a = new object[] { "yourid", File.ReadAllBytes(dir + "YourPlugin.dll"),
    File.Exists(dir + "YourPlugin.pdb") ? File.ReadAllBytes(dir + "YourPlugin.pdb") : null, null, null };
bool ok = (bool)hostType.GetMethod("TryLoad", flags).Invoke(null, a);
return "reloaded=" + ok + " error=" + (a[3] ?? "-");
```

Four things that will otherwise cost you a puzzled minute each:

- **`PluginHost` lives in namespace `HeartopiaMod`, not `HeartopiaMod.Plugins`.** Only the *contract*
  types are in `.Plugins`. `GetType("HeartopiaMod.Plugins.PluginHost")` returns null.
- **Reflection is required even though the snippet compiles against the mod.** An eval snippet is its
  own assembly, so it cannot see `internal` members — `PluginHost`, `IsWorldReady` and friends are
  all invisible to a direct call and fine through reflection.
- **Unload before load.** `TryLoad` refuses an id that is already loaded; the try/catch swallows the
  "not loaded" case on the first run.
- **Ship the pdb.** It is one extra `File.ReadAllBytes` and it turns plugin stack traces into line
  numbers.

This path skips nothing that `plugin.load` does — same validator, same collectible context, same
unload verification. It only changes who reads the bytes. Everything else about the loop is unchanged:
edit, `dotnet build`, run the snippet, and the previous version is gone within a frame.

⚠️ `eval` is an **unsafe** op, so this needs unsafe enabled (Agent → Bridge). If it is off, use
`plugin.load` with base64 — that is a write-tier op.

### A plugin's own page in the menu

`host.Ui.AddPage("title")` puts a sub-tab next to **Agent → Bridge** and returns an `IPluginPage`:
`AddLabel` / `AddNote` / `AddButton` / `AddToggle` / `AddSlider`, plus `Clear` and `Remove`.
`AddLabel` and `AddNote` return an `IPluginLabel` whose `SetText` updates that line in place — which
is how a page shows live values. Elements stack in the order added, inside a scroll view; there is
no layout to manage and a per-page cap (150) turns "add a row every frame" into one log line instead
of a leak.

Two things make this safe to hand to code that can vanish at any moment:

- **The page is a retained model, not GameObjects.** So `AddPage` works before the menu has ever
  been opened (the shell is built lazily on the first hotkey press), and pages survive
  `RebuildUguiShellForTheme`, which destroys and rebuilds the entire window on a colour change.
- **Every callback goes through a host-owned trampoline that is cut on unload.** A plugin's `Action`
  captures the plugin instance; handing it straight to a `Button` would be the host holding the
  plugin — the exact reference direction that makes a context uncollectible, and the nastiest
  version of it, because the button sits there looking functional. The page and its sub-tab are
  removed from `HostApi.RevokeAll`, so this happens whether or not the plugin cleaned up after
  itself.

Verified end to end: a plugin with a toggle, two buttons and a live label loaded, ticked 1974 times,
and unloaded — `'sample' unloaded and collected after 1 GC pass(es)`, page and sub-tab gone.

---

## 4c. `game_eval` — the seconds-long loop

Compile a snippet in the bridge, run it in the game, unload it. **The game side needed no new code
for this**: an eval is a sandbox plugin with a lifetime of one call, so the tool composes
`plugin.load` → `plugin.call` → `plugin.unload` over phase 4. The only game-side addition was the
`env` op, which reports where the mod assembly, the loader's `interop`/`core` folders and the
runtime live — so snippets compile against the **exact assemblies this session loaded** instead of
guessed paths.

Roslyn lives in the bridge, not the game: ~9 MB of compiler has no business inside the process, and
iterating on the code generator costs a 3 s rebuild here versus a restart plus relogin there.

The snippet becomes the body of a method with `host` (IHostApi), `mod` (HeartopiaComplete) and
`args` in scope, and `return <anything>;` comes back stringified. Prefer `host` — `mod` exposes only
the mod's *public* members, so much of its internals is not reachable.

Three things that had to be got right, each of which silently broke the tool first:

- **`MetadataReference.CreateFromFile` is lazy.** Handing it a native DLL succeeds, then fails the
  whole compilation later with CS0009 — and these folders are full of `coreclr`, `clrjit`, `dobby`,
  `msquic`. A try/catch around the call catches nothing; metadata must be checked eagerly.
- **`#line` needs `GetMappedLineSpan()`.** `GetLineSpan()` ignores the directive, which reported
  every error as "generated wrapper" and made line numbers useless.
- **The interop folder self-conflicts.** `UnityHelper.dll` declares its own `UnityEngine.Object`, so
  the most obvious snippet of all — `UnityEngine.Object.FindObjectsOfType<T>()` — died with CS0433.
  `buddy.csproj` dodges this by referencing ~20 hand-picked interop DLLs; a snippet needs wider
  reach, so the compiler builds a preferred set first (runtime + Unity modules + `bugtopia.dll`),
  remembers its public type names, and drops any later assembly that shadows them (41 skipped here,
  340 references kept).

Measured: 857 ms cold, **15–16 ms warm** (the reference set is built once per bridge process); a
compile error comes back as a tool *result* with snippet line numbers, so it is fixed without ever
touching the game; a snippet that throws is reported as `plugin_error` and unloaded anyway.

---

## 4d. Write ops and crash forensics

`ui.find` lists clickable elements with full hierarchy paths; `ui.click` presses one, addressed by
path (preferred) or by a name that must be **unique** — it refuses an ambiguous match, because
pressing the first of several same-named buttons is a bug that looks like success.

Two things learned by testing it rather than trusting it:

- **Dispatch via `Button.onClick.Invoke()`, not `SimulateClick`.** `SimulateClick`'s ExecuteEvents
  cascade counts a *handled* `pointerDown` as success, and a `Button` consumes `pointerDown` without
  activating — so the op reported clicks that never happened. `onClick.Invoke()` is the path the
  mod's own working features already use (`OpenInventory`, `ClickFirstFriendJoinButton`).
  `SimulateClick` remains the fallback for clickable things that are not Buttons.
- **The response says `dispatched`, never `clicked`.** Nothing on this side can know whether the
  game acted; confirming the effect (`ui.tree`, `screenshot`) is the caller's job.

**`mono.search` is the discovery step** — find types by substring across every loaded image when you
do *not* already know the name. It decodes the TypeDef table and string heap directly rather than
calling `mono_class_get`, which would materialise a MonoClass for every type and load thousands
nobody asked for; searching therefore loads nothing. Measured: 38 369 types across 65 images in
~7 ms. Truncation is reported explicitly, because concluding "there is no such type" from a capped
list is precisely the failure this op exists to prevent.

It also explains the resolver blind spot noted below: `mono.search` shows that
`XDTLevelAndEntity.Core.World.*` types live in the **`XDTDataAndProtocol`** image, so the hint
table's assumption that a namespace prefix predicts the image is simply false there.

**`mono.find` / `mono.describe` answer from the running image**, which is what makes the alias ritual
of AGENTS.md §7 unnecessary for new work: state the type once, in any spelling, and `mono.find`
tries the variants (`Gameplay`↔`GamePlay`, `Il2Cpp` prefix, `ScriptsRefactory`) and reports which one
actually resolved. `mono.describe` then returns fields (name, type, offset, static) and methods
(name, arity) plus the base chain — from the loaded image, so it cannot be stale the way a
decompilation dump can.

These are **Read** tier, and deliberately so: the crash family this project fights is object
pointers that SGen moves, and nothing here touches one. Class, method and field handles are metadata
that lives as long as the image (AGENTS.md §9 allows caching them raw), and no invocation happens.
Measured: a full describe of `BackPackSystem` (15 fields, 69 methods) takes 0.2 ms.

The arity matters more than it looks — every AuraMono invoke resolves a method by name *and*
parameter count, and the wrong overload is a documented way to fault the process. Verified against
the mod's own code: `GetAllItem` exists only at arity 1, so the arity-0 fallback in
`CollectAutoSellBackpackEntriesMono` is dead (harmless as future-proofing, but now known rather than
assumed).

The same reach is on `IHostApi.Mono` for `eval` snippets and sandbox plugins — and it is the whole
object graph, not just invocation: `FindClass`, `FindMethod`, `Invoke`, `GetComponents`,
`EnumerateCollection`, `TryGetField`, `GetUInt`/`GetInt`/`GetString`, `Pin`/`Unpin`. Without it a
snippet saw only the mod's *public* surface, which excludes every AuraMono helper — it could not
touch a single game type.

`GetComponents` and `EnumerateCollection` return an `IMonoObjects` that **owns its pins and frees
them on Dispose**, so the correct usage is also the shortest one (`using`). That is not decoration:
enumerating into bare pointers and then reading members off them is the documented mid-loop crash,
because each read allocates and SGen moves what you are still holding. Both **fail closed** when
pinning is unavailable rather than handing back pointers that work until the first GC, and an empty
result is a success with zero rows — never an error.

Two limits of the shared code were found by using this and are worth knowing before relying on it:

- **`TryAuraMonoGetComponentObjects` returns false for an empty result.** Fine for a feature (nothing
  to act on either way), misleading for anything that reports the outcome — so the MCP path uses an
  overload with `out infrastructureOk` and distinguishes "found none" from "could not run".
- **`FindAuraMonoClassByFullName` is hint-driven and has blind spots.** It maps a namespace prefix to
  a short list of likely images, then falls back to managed reflection over
  `Il2CppMonoGame.MonoHost`. Measured: it resolves *nothing* under `XDTLevelAndEntity.Core.World.*`,
  including `ViewComponent` — the live base class of components enumerated moments earlier in the
  same session. `mono.find` therefore ends with an exhaustive `mono_assembly_foreach` sweep over
  every loaded image. Any feature reaching into that namespace needs the same treatment.

**Sending server commands.** `IHostApi.Mono.SendCommand` is the first *generic*
`WebRequestUtility.SendCommand<T>` in this mod — seven features each carried their own copy of the
resolve → inflate → allocate → set fields → unbox → invoke sequence, and five separately defined
`ChannelType.Reliable = 1`. Five of those features now share this one (CraftDirectSend,
QuestAssistant, CarpetStamp, DailyClaims, StealthBlock), migrated one at a time with an in-game test
each, and all five channel constants collapsed onto `AuraChannelReliable`. The fishing buoy update
and the vehicle call stayed put: both set `Vector3` fields and branch on `SendCommand`'s return code,
and this helper maps fields by runtime type and discards the return value — so they need it extended
before they can move. SnowSculpture stayed put too, for a different reason: it is a managed
`MethodInfo.MakeGenericMethod` path, not AuraMono. Its four fatal invariants are in the header of
`buddy/HeartopiaComplete.AuraSendCommand.cs`; the least obvious is that `mono_field_set_value` wants
the *address* of a value-type value but the *pointer itself* for a string field — swapping them
corrupts silently.

`ValidateCommand` does everything except the send. It exists because the first question about a
command is "did I get the type and field names right", and on this path the usual way to find
out — try it — costs a real, un-undoable change to a live account. It is a separate method rather
than a flag so nobody sends by forgetting a boolean. Unsupported field types are refused rather than
coerced, for the same reason.

Verified live end to end by eating an apple: `mono.describe` gave the field
(`XDT.Scene.Shared.Modules.Cooking.EatFoodNetworkCommand.FoodNetId`, a **netId** not a staticId),
the send returned ok, the backpack went 12→11, and the event-driven cache invalidated itself.
Incidentally that disproved a comment in `HeartopiaComplete.AutoEatRepair.cs` which expected the bare
command to be silently rejected; that comment has since been corrected. Auto Eat still invokes
`CharacterProtocolManager.EatFood` rather than sending the command itself — the two are identical on
the wire (`EatFood` is a one-line `SendCommand` wrapper), and the invoke is the smaller AuraMono
operation.

Worked example, discovered entirely from the running process without opening a decompilation: a
component carries no `netId` at all — it is `component → <entity>k__BackingField → _netId`, and
`_netId` is a `SharedData.NetId` **struct** whose `value` field holds the number. Guessing
`netId`/`_netId` on the component returns 0 silently, which is exactly the failure `mono.describe`
exists to prevent.

**`activeInHierarchy` is not visibility**, and treating it as such let `ui.click` press buttons
belonging to closed panels. This game keeps a closed panel's hierarchy alive, so `ui.find` filters on
*reachability* instead — meaning visible **and** clickable — checking each way Unity can hide
something without deactivating it, in this order:

| Check | Catches |
|---|---|
| `lossyScale` ≈ 0 | The cheapest hide there is, and invisible to every other check |
| `CanvasGroup` chain — `alpha`, `blocksRaycasts`, `interactable` | Honours `ignoreParentGroups`, so the walk stops where Unity stops it |
| `Canvas.enabled` | A disabled canvas draws nothing, children included |
| On-screen rect | Only for `ScreenSpaceOverlay`, where a RectTransform's position is already in pixels; for camera/world canvases it is in world units and the comparison would be meaningless |

The point of checking all of them is that the response carries **`hiddenBy` with the specific reason**
— so the mechanism a panel uses is data rather than a guess. Measured here: the bag panel hides
itself with `CanvasGroup interactable=false`, leaving alpha and `blocksRaycasts` untouched.

`ui.find` returns only reachable elements by default (`visibleOnly:false` shows the rest, each with
its reason), and `ui.click` refuses an unreachable one unless given `allowHidden:true`.

**Crash forensics** (`McpForensics.cs`). The bridge deliberately hands an agent things that can kill
the process, and a native AV leaves no exception, no stack and no log line. So write/unsafe ops write
`McpBridge/lastop.json` *before* running and clear it *after* — a non-empty file at startup is proof
of what the previous session died in. Cheap read ops are excluded: they cannot crash anything, an
agent polls them, and `Breadcrumbs.Phase` already covers them.

**A plugin that dies in its per-frame `Tick` is not covered by `lastop.json`** — `Tick` runs outside
the op pump, and arming it would mean a file write every frame per plugin. That gap is closed by a
second, *resident* record (`resident.json`, written once per load and once per unload) naming which
plugins were loaded. If the process dies, they are suspects.

That only means anything with a reliable answer to "did the game quit, or was it killed?", and
finding one took two attempts:

- `AppDomain.ProcessExit` — the obvious choice, and **measurably useless here**: it does not run when
  this game quits normally, because Unity tears the process down without a CoreCLR shutdown. Building
  on it would have quarantined innocent plugins after every ordinary exit. (`PlayerLoopProbe` hangs
  its teardown on the same event, harmlessly.) WER dump correlation is no fallback either — this game
  leaves no dumps in the standard folder.
- **`Application.quitting`** — Unity's own shutdown event: fires on an orderly quit, cannot fire when
  the process is killed. Verified: a normal close logs `clean shutdown` and empties the record.
  Subscribing needs an il2cpp delegate, and the ordinary route (`DelegateSupport.ConvertDelegate`)
  drags in ClassInjector and its five `.text` detours — so it goes through `HookFreeDelegate`
  instead, with the delegate type **read from `add_quitting`'s signature** rather than assumed.

The record stamps `"quitSignal": true/false` — whether *that* session could have cleared it. Its
survival is evidence only when the flag is true; otherwise it is just "what was loaded", because a
session without the signal leaves the same file on an ordinary quit. Everything fails closed: any
step failing leaves the flag false and the record informational.

Verified end to end against a real native AV (a plugin faulting from `Tick`): the kill left
`lastop.json` empty and `resident.json` intact, and the next startup reported
`previousSessionKilled: true` with `previousSessionCrashed: false` — the two fields diverging exactly
as intended, since there was no operation to blame. That crash immediately earned its cost: the
startup attribution was broken and no other test could have shown it. The lastop branch returned
early when its file was empty, skipping the resident read at the end of the same method — so a crash
in `Tick`, the only case the resident record exists for, was the only case that never reached it.
The two reads are now separate methods with no shared control flow.

Whatever was running is auto-**quarantined** by sha256, and `plugin.load` refuses those bytes unless
`force: true` — otherwise an agent that auto-reloads its work walks back into the same crash every
launch. `session.report` returns the record, the quarantine list and the newest crash dumps (feed the
first straight into the `crash-dump-stack` skill); it also releases a sha via `unquarantine`. The
bridge prepends the whole story to the **first** tool result after any reconnect, once.

---

## 5. Invariants for anyone adding an op

**No game access on the socket thread. Ever.** Socket threads parse and enqueue; handlers run on the
Unity main thread from `McpBridge.Drain()`, called out of `OnUpdate` right after the world-ready
tick. Calling into il2cpp or embedded Mono off-thread needs a thread attach and exposes SGen to a
thread CoreCLR does not coordinate with — the crash family in AGENTS.md §11. Even logging obeys it:
socket threads use `McpBridge.LogFromWorker`, drained on the main thread, because the BepInEx sink
writes to a shared `StreamWriter`.

Everything else follows from that:

- **Register in `RegisterMcpOps()`** (`HeartopiaComplete.Mcp.cs`) with flags, cost and an argument
  schema. The registry is filled before the listener starts and never mutated, so socket threads
  only ever read a frozen map. `rpc.describe` publishes it, so a new op needs no bridge rebuild.
- **`McpOpFlags.NeedsWorld`** for anything that resolves, inflates or invokes game code. The pump
  refuses it with `world_not_ready` instead of letting it run before a world exists (AGENTS.md §1).
- **`McpOpCost.Heavy`** for scans and allocations. The pump runs at most one Heavy op per frame
  inside a 3 ms budget; the rest wait, well within the 5 s call timeout.
- **`McpOpException(code, message)`** for expected failures. Anything else that throws is logged
  *and* returned as `internal` — never wire-only.
- Handlers return a JSON fragment built with `McpJsonWriter`. Parsing (network input) is
  `System.Text.Json`, materialized to plain BCL types on the socket thread so no `JsonElement` and
  no disposed `JsonDocument` can cross into the queue.

---

## 6. Security

This is a remote-code-execution channel into the game process. It is treated as one:

- IPv4 loopback only, `ExclusiveAddressUse`, ports 8770–8774.
- 32-byte random token per session, in `endpoint.json`; required in `hello`, compared in constant
  time, and a bad token gets a 1 s delay and a closed socket rather than an error to probe.
- Depth-capped queue (64) and a 4-connection cap.
- The tiers are a **ladder: unsafe implies write.** An unsafe op runs arbitrary code and arbitrary
  code writes whatever it likes, so requiring both for `plugin.load` was false granularity that only
  produced a confusing refusal for anyone who granted the scarier privilege alone.
- `AllowWrites` / `AllowUnsafe` default to **off even with the marker present** — the marker
  authorises the channel, not the privileges. They are turned on by a human, in the menu's
  **Agent → Bridge** page, and that whole tab only exists while the bridge is listening.
  Session-scoped with no config key, so a privilege cannot survive a restart unnoticed. The two
  checkboxes move together in both directions, because the op gate treats them as the ladder above:
  showing "writes off" while unsafe was on would misdescribe what the bridge actually accepts.
- No new IL2CPP `.text` patches: AGENTS.md §1 still holds.

---

## 7. Troubleshooting

| Symptom | Cause |
|---|---|
| `no endpoint file at …` | Game not running, or built without `-p:Mcp=true`, or the marker file is missing |
| `no answer on 127.0.0.1:8770 — the endpoint file is stale` | The game exited without a clean shutdown; the file is removed on `Stop()` |
| `the game rejected the handshake (stale token)` | The game restarted and rotated its token — the bridge re-reads `endpoint.json` on its next reconnect, so just retry |
| `world_not_ready` | Login or loading screen. `status`, `ping`, `log.tail` still work |
| `timeout` | The main thread is not ticking: loading, stalled, or the mod's breaker disabled the pump — check `log.tail` for `[Mcp]` |
| Tools missing in the client | The bridge was launched with `dotnet run` (build output corrupts stdout), or the DLL path in `.mcp.json` is wrong |
