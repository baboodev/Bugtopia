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

    With -NoLink, a third:

      offline-nolink  the offline build, carrying the BepInEx mod built without the Telegram link

    Both land in release\ as one file each, and nothing is installed into the game folder: the mod
    is built the way CI ships it, with its deploy step switched off. See docs/LAUNCHER.md.

.PARAMETER OutputDirectory
    Where the finished exes go. Defaults to release\ beside the repository.

.PARAMETER PluginDll
    The mod to embed in the offline build. Given one, the mod is not rebuilt - point this at a
    downloaded bugtopia-bepinex.dll to package a published release rather than a local build.

.PARAMETER NoLink
    Also build the BepInEx mod with -p:Telegram=false and publish the offline launcher carrying it.
    That plugin is always built from this tree, -PluginDll or not: no release asset holds it.

.PARAMETER SkipMod
    Do not build or embed the mod. The online build is unaffected; the offline one is then missing
    its plugin, so this is for working on the launcher itself, not for producing anything.

.PARAMETER Win32
    Publish every launcher - offline-nolink too, with -NoLink - with the native Win32 window instead
    of the Photino page, so no WebView2 and no Photino.Native.dll ride inside. The files end in
    -win32.exe and do not replace the Photino ones. The payload is built the same way either way.
#>
param(
    [string]$OutputDirectory = "",
    [string]$PluginDll = "",
    [switch]$NoLink,
    [switch]$SkipMod,
    [switch]$Win32
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

# ---- the mod without the Telegram link -------------------------------------
#
# Before the regular build, because both land in buddy\bin\BepInEx\ReleaseShip\: this one is copied
# out first, and whatever sat in that folder is put back, so the regular name never holds a no-link
# plugin for a later publish to pick up by default. CI stages its Universal no-link build the same way.

$noLinkPluginDll = $null

if ($NoLink) {
    if ($SkipMod) {
        throw "-NoLink builds the mod; it cannot be combined with -SkipMod."
    }

    $shared = Join-Path $repoRoot "buddy\bin\BepInEx\ReleaseShip"
    $staging = Join-Path $repoRoot "buddy\bin\BepInEx\ReleaseShip-nolink"
    $backup = Join-Path $staging "previous"
    New-Item -ItemType Directory -Force $backup | Out-Null
    Get-ChildItem $backup -File -ErrorAction SilentlyContinue | Remove-Item -Force
    foreach ($name in "bugtopia.dll", "bugtopia.pdb") {
        $file = Join-Path $shared $name
        if (Test-Path $file) { Copy-Item $file $backup -Force }
    }

    Invoke-Step "Mod, BepInEx flavour without the Telegram link (bugtopia.dll)" {
        dotnet build "$repoRoot\buddy\buddy.csproj" `
            -c ReleaseShip `
            -p:Loader=BepInEx `
            -p:Telegram=false `
            -p:ContinuousIntegrationBuild=true `
            --nologo
    }

    $noLinkPluginDll = Join-Path $staging "bugtopia.dll"
    Copy-Item (Join-Path $shared "bugtopia.dll") $noLinkPluginDll -Force

    foreach ($name in "bugtopia.dll", "bugtopia.pdb") {
        $saved = Join-Path $backup $name
        $file = Join-Path $shared $name
        if (Test-Path $saved) { Copy-Item $saved $file -Force }
        elseif (Test-Path $file) { Remove-Item $file -Force }
    }

    # The guarantee the CI workflow checks on its Universal no-link build, checked the same way:
    # string literals are UTF-16 and can start at either byte alignment, so both are decoded.
    $bytes = [System.IO.File]::ReadAllBytes($noLinkPluginDll)
    $text8 = [System.Text.Encoding]::UTF8.GetString($bytes)
    $text16a = [System.Text.Encoding]::Unicode.GetString($bytes)
    $text16b = [System.Text.Encoding]::Unicode.GetString($bytes, 1, $bytes.Length - 1)
    $bad = @()
    foreach ($needle in 't.me', 'bugtopiamod', 'Telegram', 'Bugtopia News', 'OpenURL') {
        if ($text8.Contains($needle)) { $bad += "$needle (utf-8)" }
        if ($text16a.Contains($needle) -or $text16b.Contains($needle)) { $bad += "$needle (utf-16)" }
    }
    if ($bad.Count -gt 0) {
        throw "The no-link plugin still contains: $($bad -join ', '). Something referencing the link sits outside #if FEATURE_TELEGRAM."
    }
    Write-Host "No Telegram traces in the no-link plugin." -ForegroundColor Green
}

# ---- the mod ---------------------------------------------------------------

if ($SkipMod) {
    Write-Warning "Skipping the mod. The offline build will have no plugin inside it."
}
elseif (-not $PluginDll) {
    # The configuration CI ships, so what a local build packages is the same binary a release
    # carries. ContinuousIntegrationBuild=true is not about CI here: it is the switch that turns
    # off the csproj's DeployModToGame target. Packaging a launcher has no business replacing the
    # mod already installed in the game folder.
    Invoke-Step "Mod, BepInEx flavour (bugtopia.dll)" {
        dotnet build "$repoRoot\buddy\buddy.csproj" `
            -c ReleaseShip `
            -p:Loader=BepInEx `
            -p:ContinuousIntegrationBuild=true `
            --nologo
    }
    $PluginDll = Join-Path $repoRoot "buddy\bin\BepInEx\ReleaseShip\bugtopia.dll"
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

if ($noLinkPluginDll) {
    $arguments.NoLinkPluginDll = $noLinkPluginDll
}

if ($Win32) {
    $arguments.Win32 = $true
}

Write-Host ""
& $publish @arguments
