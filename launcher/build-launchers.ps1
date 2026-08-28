<#
.SYNOPSIS
    Builds everything the launcher needs, then publishes both of its builds.

.DESCRIPTION
    The launcher ships as a single exe with its payload inside it, and the payload comes from three
    separate builds that have nothing else in common: a native C bootstrap, a net6.0 shim that runs
    inside BepInEx's own runtime, and the mod itself. This runs all of them in order and then
    publishes the two flavours.

      offline  carries the mod and the bootstrap; downloads nothing, ever
      online   carries no mod and fetches the newest release from GitHub

    Both land in release\ as one file each. See docs/LAUNCHER.md.

.PARAMETER OutputDirectory
    Where the finished exes go. Defaults to release\ beside the repository.

.PARAMETER PluginDll
    The mod to embed in the offline build. Given one, the mod is not rebuilt - point this at a
    downloaded bugtopia-bepinex.dll to package a published release rather than a local build.

.PARAMETER SkipMod
    Do not build or embed the mod. The online build is unaffected; the offline one is then missing
    its plugin, so this is for working on the launcher itself, not for producing anything.
#>
param(
    [string]$OutputDirectory = "",
    [string]$PluginDll = "",
    [switch]$SkipMod
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$launcherDir = $PSScriptRoot

function Invoke-Step {
    param([string]$Title, [scriptblock]$Body)

    Write-Host ""
    Write-Host "== $Title" -ForegroundColor Cyan
    & $Body
    if ($LASTEXITCODE -ne 0) {
        throw "$Title failed."
    }
}

# ---- the native bootstrap --------------------------------------------------
#
# Its own vcvars search covers every Visual Studio edition back to 2019; set BUGTOPIA_VCVARS to
# override. Static CRT on purpose - this DLL is loaded into someone else's process.

Invoke-Step "Bootstrap (bugtopia_inject.dll)" {
    cmd /c "`"$launcherDir\BugtopiaInject\build.bat`""
}

# ---- the interop generator shim --------------------------------------------
#
# net6.0 is not a style choice: this assembly is loaded by the CoreCLR 6.0.7 that BepInEx carries.

Invoke-Step "Interop shim (BugtopiaInterop.dll)" {
    dotnet build "$launcherDir\BugtopiaInterop\BugtopiaInterop.csproj" -c Release --nologo
}

# ---- the mod ---------------------------------------------------------------

if ($SkipMod) {
    Write-Warning "Skipping the mod. The offline build will have no plugin inside it."
}
elseif (-not $PluginDll) {
    Invoke-Step "Mod, BepInEx flavour (bugtopia.dll)" {
        dotnet build "$repoRoot\buddy\buddy.csproj" -c Release -p:Loader=BepInEx --nologo
    }
    $PluginDll = Join-Path $repoRoot "buddy\bin\BepInEx\Release\bugtopia.dll"
}
else {
    if (-not (Test-Path $PluginDll)) {
        throw "No such plugin: $PluginDll"
    }
    Write-Host ""
    Write-Host "== Mod: using $PluginDll" -ForegroundColor Cyan
}

# ---- both launchers --------------------------------------------------------
#
# Publishing stays in the one script CI also calls, so a local build and a release build cannot
# drift apart.

$publish = Join-Path $repoRoot "ci\publish-launcher.ps1"
$arguments = @{}
if ($OutputDirectory) { $arguments.OutputDirectory = $OutputDirectory }

if ($SkipMod) {
    # Pointed at a path that cannot exist, so the csproj's Exists() condition fails and the plugin
    # is genuinely left out. Saying nothing here would fall back to the default path instead, and a
    # mod built earlier would be embedded by a switch that says it skips the mod.
    $arguments.PluginDll = Join-Path $repoRoot "launcher\.no-plugin"
    $arguments.SkipPayloadCheck = $true
}
elseif ($PluginDll) {
    $arguments.PluginDll = $PluginDll
}

Write-Host ""
& $publish @arguments
