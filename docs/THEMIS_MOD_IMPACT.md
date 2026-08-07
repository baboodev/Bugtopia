# How Themis affects Bugtopia right now — readable report (2026-08-07)

Plain-language assessment of what the current Themis build (`themis_x64.dll`, MD5 `6E8A…B0B31`,
2026-08-05) can and can't see about the mod as it ships today. Grounded in the mod's actual
behaviour, not theory. Full technical detail: `docs/BEHAVIORAL_ANTI_CHEAT.md` §10.12–10.15.

## Bottom line

**The new findings do not change the mod's risk posture.** The single dominant, irreducible vector
is unchanged: *an unsigned injected module is detectable in principle* — now confirmed via **three
independent detectors** (generic module/memory scan, code-integrity hash, and — newly found —
Authenticode signature verification). Everything the mod does *behaviourally* stays **below** the
two radars that would otherwise matter (the automation-tool blocklist and the injected-input check),
because it drives the game through **in-process game APIs**, not external tools or OS-level fake input.

The existing hardening — **zero `.text` patches, Mono-side detours, BepInEx over MelonLoader,
game-API calls instead of synthetic input, no debugger** — is exactly the right response and is now
validated against the fuller surface. **No code change is required for safety.** The optional
input-vector cleanup has since been done (2026-08-08); the remaining leverage is a few
*user-behaviour* rules.

## Risk matrix

| Themis vector | What it catches | Mod exposure today | Status |
|---|---|---|---|
| Module enum + memory scan | presence of an injected module | `bugtopia.dll` is loaded (BepInEx/doorstop) | ⚠️ **Inherent** — detectable in principle, always was |
| Code-integrity hash (`.text`) | patched game/engine code | mod does **zero** `.text` patches (Mono-side only) | ✅ Clean |
| **Signature/catalog verify** (new) | unsigned / tampered modules | `bugtopia.dll` is unsigned | ⚠️ New detector of the **same fact** (module presence) |
| Page-write watch (`ZwGetWriteWatch`) | written code pages | no module `.text` writes; only Mono JIT heap | ✅ Low |
| **Process-name blocklist** (new) | AutoHotkey / Cheat Engine / TinyTask / macro tools as separate processes | mod is in-process; **not a listed tool** | ✅ Not matched — *unless the user runs those tools* |
| **Input-source** (`GetCurrentInputMessageSource`) | OS-level synthetic input | game-API input only; the `PostMessage(ESC)` fallback + all dead `keybd_event`/`mouse_event`/`PostMessage` P/Invokes removed 2026-08-08 | ✅ Clean |
| Anti-debug (DR regs, IsDebuggerPresent, hide-from-debugger) | a live debugger on the game | mod attaches none; the crash handler (`SetUnhandledExceptionFilter`) was removed this session | ✅ Clean |
| VM / sandbox detection | running in VMware/VBox/Sandboxie | user's environment | ✅ N/A on bare metal |
| Fingerprint `oneid` → `RiskControlBan` | device-level ban | tied to hardware/install, not mod behaviour | ⚠️ **Device-level**, loader-agnostic |

## The vectors that actually matter

**1. Unsigned injected module — the dominant, irreducible risk.**
Themis can flag the mod three ways: it enumerates modules and scans memory (sees a foreign module),
it integrity-hashes code regions (the mod keeps this clean by never patching `.text`), and — the new
finding — it hashes files and checks **Authenticode catalog signatures** (`CryptCATAdmin*` / `WTHelper*`),
which an unsigned `bugtopia.dll` fails. The user-facing verdict for this is literally
*"The game's plugin might be corrupted. Please try using repair game and restart!"*. **This cannot be
fixed from the mod** — we can't sign as Microsoft, and hiding module presence from a signature check
isn't realistic. The mitigations we already run (zero `.text` patches → clean integrity hash; fewest
loader hooks → BepInEx 0 vs MelonLoader 5) shrink the *other two* detectors' surface but not this one.
Net: **the picture is sharper, the risk is the same as it always was.**

**2. Automation blocklist — the mod is NOT on it, but the user's other tools might be.**
Themis hunts specific *external processes*: AutoHotkey, Cheat Engine, TinyTask, MouseClick,
KeymouseGo, MacroRecorder, MacroCreator, and "multi-client" managers. Bugtopia is **none of these** —
it's an in-process DLL that calls the game's own APIs, so it doesn't match. **But if the user runs any
of those tools alongside the game, Themis fires the automation warning regardless of the mod.** This
is a user-behaviour rule, not a code issue.

**3. Injected-input check — now fully cleared.**
`GetCurrentInputMessageSource` lets Themis tell real device input from injected input. The mod's
automation is entirely **game-API level** (invoking the game's own input/interaction handlers),
which this check doesn't see. As of 2026-08-08 there is **no OS-level synthetic input left at all**:
the dead `keybd_event` / `mouse_event` / click-via-`PostMessage` P/Invokes were removed, and so was
the last `SendEscMessage` fallback — its only caller (`ForceCloseMenuIfOpen`) turned out to be
orphaned too, so the whole ESC-`PostMessage` chain and its P/Invokes were deleted rather than
replaced. A repo-wide grep confirms nothing in the mod now calls `PostMessage` / `keybd_event` /
`mouse_event` / `SendInput`.

**4. Device ban (`oneid`) — unaffected by anything here.**
The device fingerprint is computed once and cached in `HKCU\Software\Themis`; a flagged device stays
flagged across reinstalls and loader swaps. No mod change touches this; only a fresh/burner device does.

## Recommendations

**Code (optional hardening — not required for safety):**
- ✅ **Done (2026-08-08).** Removed the **dead** OS-input P/Invokes (`keybd_event`, `mouse_event`,
  `SendEnterMessage`, `SendLeftClickInputTap`, `SendLeftClickMessage`) and then the `SendEscMessage`
  fallback too — its sole caller `ForceCloseMenuIfOpen` was itself dead, so the ESC-`PostMessage`
  chain was deleted outright instead of swapped for a game-API menu-close. The
  `GetCurrentInputMessageSource` vector is now fully cleared.
- Keep **BepInEx** as the default loader (already the case): measured 0 mod-contributed `.text`
  patches vs MelonLoader's 5.

**User behaviour (bigger levers than any code change):**
- **Don't run** AutoHotkey / Cheat Engine / TinyTask / macro-recorder / auto-clicker tools while the
  game is open — they match the blocklist directly and trigger the automation warning/suspension.
- **Don't run the game in a VM** and **don't attach a debugger** to the live client.
- Use a **burner account** for any threshold testing (verdicts are client-signalled but the ban is
  server-side).

## What stays out of reach
The exact heartbeat-hashed regions and the `oneid` MD5 field order remain behind **VMProtect** — the
obfuscator Themis is packed with. Reading them means beating VMProtect (heavy) or a live trace (which
Themis actively detects — account risk). Not needed for the assessment above.
