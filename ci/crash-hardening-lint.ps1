# Crash-hardening lint - guards the invariants established in
# docs/plans/2026-06-12-crash-hardening-plan.md (phase 4).
#
# ERRORS (fail the build):
#   E1: auraMonoRuntimeInvokeRaw referenced outside the engine home (HeartopiaComplete.AuraMonoEngine.cs
#       or AuraFarm.cs) - the raw export bypasses InvokeAuraMonoChecked (null-method guard, exc check,
#       garbage-result suppression).
#   E2: "mono_runtime_invoke" re-resolved outside the engine home - same bypass via a new delegate.
#   E3: a cross-frame MonoObject* cache declared as a raw IntPtr field (name ending in Obj/obj) -
#       bdwgc collects the object once the game drops its last reference; use AuraMonoObjectCache.
#
# WARNINGS (reported, do not fail):
#   W1: an IntPtr local in a coroutine declared before a `yield return` and referenced after it -
#       raw MonoObject* must not survive a yield; scalarize to netIds/strings up front or pin via
#       AuraMonoPin. Heuristic - review flagged sites manually (class/method pointers are fine).

param([string]$Root = (Split-Path $PSScriptRoot -Parent))

$ErrorActionPreference = "Stop"
$buddyDir = Join-Path $Root "buddy"
$lintErrors = New-Object System.Collections.Generic.List[string]
$lintWarnings = New-Object System.Collections.Generic.List[string]

# Fields that legitimately stay raw IntPtr (documented in the plan):
#   warehouseAuraBagPanelTypeObj - MonoReflectionType; the runtime roots reflection objects
#   in a domain-level cache, so it cannot be collected.
$rawObjFieldAllowlist = @("warehouseAuraBagPanelTypeObj")

$files = Get-ChildItem $buddyDir -Filter *.cs -Recurse |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' }

foreach ($f in $files) {
    # The AuraMono native bridge lives in HeartopiaComplete.AuraMonoEngine.cs (the
    # mono_runtime_invoke binding + InvokeAuraMonoChecked guard) and AuraFarm.cs still
    # calls the raw invoke from its gather paths. Both are the legitimate home for the
    # raw export; everything else must go through the guarded auraMonoRuntimeInvoke.
    $isEngineHome = $f.Name -eq "AuraFarm.cs" -or $f.Name -eq "HeartopiaComplete.AuraMonoEngine.cs"
    $lines = Get-Content $f.FullName

    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        if ($line -match '^\s*//') { continue }
        $loc = "$($f.Name):$($i + 1)"

        if (-not $isEngineHome -and $line -match 'auraMonoRuntimeInvokeRaw') {
            $lintErrors.Add("E1 $loc - auraMonoRuntimeInvokeRaw bypasses the invoke guard; use auraMonoRuntimeInvoke or TryAuraInvoke.")
        }
        if (-not $isEngineHome -and $line -match '"mono_runtime_invoke"') {
            $lintErrors.Add("E2 $loc - do not re-resolve mono_runtime_invoke; the only binding lives in HeartopiaComplete.AuraMonoEngine.cs behind InvokeAuraMonoChecked.")
        }
        if ($line -match 'private\s+(static\s+)?IntPtr\s+(\w*(?:Obj|obj))\s*(=\s*IntPtr\.Zero\s*)?;') {
            $fieldName = $Matches[2]
            if ($rawObjFieldAllowlist -notcontains $fieldName) {
                $lintErrors.Add("E3 $loc - cross-frame MonoObject* field '$fieldName' must be an AuraMonoObjectCache (raw pointers are collected by bdwgc).")
            }
        }
    }

    # W1: IntPtr local held across a yield inside the same method body (heuristic).
    for ($i = 0; $i -lt $lines.Count; $i++) {
        if ($lines[$i] -notmatch 'IEnumerator\s+\w+\(') { continue }
        $depth = 0; $started = $false; $end = $i
        for ($j = $i; $j -lt $lines.Count; $j++) {
            $opens = ([regex]::Matches($lines[$j], '\{')).Count
            $closes = ([regex]::Matches($lines[$j], '\}')).Count
            $depth += $opens - $closes
            if ($opens -gt 0) { $started = $true }
            if ($started -and $depth -le 0) { $end = $j; break }
        }
        $body = $lines[$i..$end]

        $decls = @{}
        for ($k = 0; $k -lt $body.Count; $k++) {
            foreach ($m in [regex]::Matches($body[$k], '(?:^|[\s(])(?:out\s+)?IntPtr\s+(\w+)\b')) {
                $v = $m.Groups[1].Value
                if (-not $decls.ContainsKey($v)) { $decls[$v] = $k }
            }
        }
        if ($decls.Count -eq 0) { continue }

        $yields = @()
        for ($k = 0; $k -lt $body.Count; $k++) {
            if ($body[$k] -match 'yield return') { $yields += $k }
        }
        if ($yields.Count -eq 0) { continue }

        foreach ($v in $decls.Keys) {
            $declLine = $decls[$v]
            $laterYields = $yields | Where-Object { $_ -gt $declLine }
            if (-not $laterYields) { continue }
            $firstYield = ($laterYields | Measure-Object -Minimum).Minimum
            for ($k = $firstYield + 1; $k -lt $body.Count; $k++) {
                if ($body[$k] -match "\b$v\b") {
                    $lintWarnings.Add("W1 $($f.Name):$($i + 1) - coroutine local IntPtr '$v' may cross a yield (declared body+$declLine, used body+$k after yield body+$firstYield); scalarize before the yield or pin via AuraMonoPin.")
                    break
                }
            }
        }
    }
}

if ($lintWarnings.Count -gt 0) {
    Write-Host "Crash-hardening lint warnings ($($lintWarnings.Count)):"
    $lintWarnings | ForEach-Object { Write-Host "  WARN $_" }
}

if ($lintErrors.Count -gt 0) {
    Write-Host "Crash-hardening lint errors ($($lintErrors.Count)):"
    $lintErrors | ForEach-Object { Write-Host "  FAIL $_" }
    exit 1
}

Write-Host "Crash-hardening lint passed ($($files.Count) files, $($lintWarnings.Count) warning(s))."
exit 0
