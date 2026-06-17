# Crash dump collection and stack analysis

How to capture a **full process dump** when Heartopia (`xdt.exe`) crashes with **BepInEx** (CoreCLR 6), install the analysis tools, and extract a **managed stack** to send for debugging.

Use this when the game hard-crashes (no C# exception in `LogOutput.log`) and you need to report a native AV with a `clrstack`.

---

## Prerequisites

| Item | Notes |
|------|--------|
| Windows x64 | WER LocalDumps is Windows-only |
| **Administrator** | Registry keys live under `HKLM` |
| Heartopia + BepInEx | This guide targets the BepInEx / .NET 6 host (`dotnet\coreclr.dll`) |
| .NET SDK 6+ | For `dotnet tool install` |
| Disk space | Full dumps are **~5 GB** each; keep `DumpCount` low |

Replace placeholders in this doc:

| Placeholder | Example |
|-------------|---------|
| `USERNAME` | Your Windows login |
| `<HeartopiaDir>` | `C:\Program Files (x86)\Steam\steamapps\common\Heartopia` |
| `<DumpFolder>` | `C:\Users\USERNAME\Documents\WERDumps` |

---

## 1. Enable Windows Error Reporting (WER) local dumps

Run **Command Prompt or PowerShell as Administrator**:

```bat
reg add "HKLM\SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\xdt.exe" /v DumpType /t REG_DWORD /d 2 /f
reg add "HKLM\SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\xdt.exe" /v DumpFolder /t REG_EXPAND_SZ /d "C:\Users\USERNAME\Documents\WERDumps" /f
reg add "HKLM\SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\xdt.exe" /v DumpCount /t REG_DWORD /d 5 /f
```

Create the folder first (non-admin is fine):

```powershell
New-Item -ItemType Directory -Force -Path "C:\Users\USERNAME\Documents\WERDumps"
```

| Value | Meaning |
|-------|---------|
| `DumpType = 2` | **Full** user-mode dump (required for `clrstack`; mini-dumps often lack enough CLR state) |
| `DumpFolder` | Where WER writes `xdt.exe.<pid>.dmp` |
| `DumpCount` | Rolling retention (old dumps deleted) |

**Optional — disable WER local dumps later:**

```bat
reg delete "HKLM\SOFTWARE\Microsoft\Windows\Windows Error Reporting\LocalDumps\xdt.exe" /f
```

---

## 2. Copy `createdump.exe` into the game `dotnet` folder

BepInEx ships a trimmed `dotnet` folder. Placing `createdump.exe` next to `coreclr.dll` helps the runtime produce usable dumps on some setups.

1. Find your installed **Microsoft.NETCore.App 6.0.x** runtime:

   ```powershell
   dotnet --list-runtimes
   ```

   Example line: `Microsoft.NETCore.App 6.0.36 [C:\Program Files\dotnet\shared\Microsoft.NETCore.App\6.0.36]`

2. Copy `createdump.exe` into the game:

   ```powershell
   Copy-Item `
     "C:\Program Files\dotnet\shared\Microsoft.NETCore.App\6.0.36\createdump.exe" `
     "<HeartopiaDir>\dotnet\createdump.exe"
   ```

   Adjust the `6.0.36` path to match `dotnet --list-runtimes`.

Verify the game folder contains at least:

```
<HeartopiaDir>\dotnet\coreclr.dll
<HeartopiaDir>\dotnet\mscordaccore.dll
<HeartopiaDir>\dotnet\mscordaccore_amd64_amd64_6.0.*.dll
<HeartopiaDir>\dotnet\createdump.exe   ← after copy
```

---

## 3. Install `dotnet-dump` (version 8 — not 9 or 10)

The game runs **CoreCLR 6.0.722**. **`dotnet-dump` 9.x / 10.x** often fails to load `mscordaccore` for these dumps (`Failed to load data access module`).

Install **8.0.452401** globally:

```powershell
dotnet tool uninstall -g dotnet-dump
dotnet tool install -g dotnet-dump --version 8.0.452401
dotnet-dump --version
```

If `dotnet-dump` is not on PATH, use the full path from:

```powershell
dotnet tool list -g
```

---

## 4. Reproduce the crash

1. Deploy the mod build you are testing.
2. Play until the crash happens.
3. Check `<DumpFolder>` for a new file, typically:

   ```
   xdt.exe.<pid>.dmp
   ```

   WER may also write a secondary `coreclr_<pid>_*.dmp` — prefer **`xdt.exe.<pid>.dmp`** (it usually has the exception stream).

4. Copy the dump somewhere stable before it is rotated out.

---

## 5. Extract the managed stack (`clrstack`)

Point DAC/DBI at the **game's** `dotnet` folder (not your global SDK):

```powershell
$dump = "C:\Users\USERNAME\Documents\WERDumps\xdt.exe.40584.dmp"

$script = @'
setclrpath "C:\Program Files (x86)\Steam\steamapps\common\Heartopia\dotnet"
setthread 0
clrstack
quit
'@

$script | dotnet-dump analyze $dump
```

Replace the dump path and `setclrpath` with your real paths.

### Useful extra commands (same session)

```text
threads          # crash thread is marked with *
setthread 0      # switch to faulting thread if needed
clrstack -all    # all managed threads
crashinfo        # when available
```

### If `clrstack` still fails

- Confirm `setclrpath` points at `<HeartopiaDir>\dotnet` and `mscordaccore*.dll` exists there.
- Confirm `dotnet-dump` is **8.0.452401**, not 9+.
- Use the **`xdt.exe.<pid>.dmp`** file, not `coreclr_*.dmp`.

---

## 6. Quick native-only look (no DAC)

Repo scripts work without symbols — faulting module + rough native stack:

```powershell
cd <repo>\tools

.\Read-MinidumpException.ps1 -DumpPath "C:\Users\USERNAME\Documents\WERDumps\xdt.exe.40584.dmp"

.\Read-MinidumpStack.ps1 -DumpPath "C:\Users\USERNAME\Documents\WERDumps\xdt.exe.40584.dmp" -MaxHits 80
```

These do **not** replace `clrstack` for mod debugging.

---

## 7. What to send when reporting a crash

Include as much of the following as possible:

| Artifact | Path / command |
|----------|----------------|
| **Managed stack** | Full `clrstack` output from §5 (crash thread) |
| **Mod log tail** | `<HeartopiaDir>\BepInEx\LogOutput.log` — last ~200 lines before crash |
| **Game + loader versions** | Heartopia build, BepInEx version |
| **Mod build** | Git commit or `helper.dll` build date |
| **Repro steps** | What features were on (e.g. Pad Build + noclip) |
| **Dump file** | Optional — large; upload separately if asked |

Paste `clrstack` in a fenced code block. Example of useful lines:

```text
HeartopiaMod.HeartopiaComplete.OnUpdate() [HeartopiaComplete.cs @ 2108]
HeartopiaMod.HeartopiaComplete.TryGetModHotkeyDown(...) [...]
...
ILStubClass.IL_STUB_PInvoke(IntPtr)
```

---

## 8. Windows Event Viewer (optional)

Event **Application** log, IDs **1000** / **1001**, confirms fault module and offset without the dump:

```powershell
Get-WinEvent -FilterHashtable @{ LogName = 'Application'; Id = 1000, 1001 } -MaxEvents 10 |
  Where-Object { $_.Message -match 'xdt' } |
  Select-Object TimeCreated, Message
```

---

## Related

- [BUILD_AND_RUN.md](./BUILD_AND_RUN.md) — deploy, `LogOutput.log` location
- [AGENTS.md](../AGENTS.md) — AuraMono crash patterns and anti-patterns. Once you have the `clrstack`, **§11 "Fixing a stale-pointer AuraMono crash"** gives the exact fix (pin enumerated items + `FreeAuraMonoPins`; `mono_gc_disable` is a no-op on this build).
- `tools/Read-MinidumpException.ps1`, `tools/Read-MinidumpStack.ps1` — native dump helpers
