<#
.SYNOPSIS
    Publishes both launcher flavours and collects them into one folder.

.DESCRIPTION
    The launcher ships as a single exe with everything inside it, and it ships twice:

      offline  carries the mod and the bootstrap; downloads nothing, ever
      online   carries no mod at all and fetches the newest release from GitHub

    Given -NoLinkPluginDll, a third one is published beside them:

      offline-nolink  the offline build, carrying the BepInEx mod built without the Telegram link

    They are different assemblies, not one assembly with a switch, so both are published and both
    are kept. The output is two files and nothing else - no runtime beside them, no folder to
    unpack, which is the whole point of the NativeAOT build.

    NativeAOT needs the MSVC linker, and ILCompiler's own toolchain probe cannot always find it.
    This script initialises vcvars64.bat and hands ILCompiler the environment instead, which is the
    difference between a publish that works and one that fails looking for vswhere.

.PARAMETER OutputDirectory
    Where the finished exes go. Defaults to release\ beside the repository.

.PARAMETER PluginDll
    The mod to embed in the offline build. Defaults to the local BepInEx build; CI points it at the
    same bugtopia-bepinex.dll the release ships, so the launcher carries the published file rather
    than a second copy built beside it.

.PARAMETER NoLinkPluginDll
    A BepInEx plugin built with -p:Telegram=false. Given one, the offline-nolink launcher is
    published as well, into its own bin\offline-nolink\ and obj\offline-nolink\. There is no default:
    that build shares an output folder with the regular one, so no fixed path can be trusted to hold it.

.PARAMETER VersionLabel
    Names the output files with this instead of the version resource inside them. CI passes the tag,
    which drops the commit hash a release asset has no use for.

.PARAMETER SkipPayloadCheck
    Publish even when a payload file is missing. The build only warns about those, which is right
    for day-to-day work and wrong for a release, so this script refuses by default.

.PARAMETER Win32
    Publish every flavour with the native Win32 window (-p:BugtopiaUi=win32) instead of the Photino
    page: no WebView2 and no Photino.Native.dll inside. The files are named ...-<flavour>-win32.exe and
    build into bin\<flavour>-win32\, so they sit beside the Photino builds rather than replacing them.
#>
param(
    [string]$OutputDirectory = "",
    [string]$PluginDll = "",
    [string]$NoLinkPluginDll = "",
    [string]$VersionLabel = "",
    [switch]$SkipPayloadCheck,
    [switch]$Win32
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repoRoot "launcher\BugtopiaLauncher\BugtopiaLauncher.csproj"

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot "release"
}

# ---- the pieces the launcher embeds ----------------------------------------

$payload = @(
    @{ Path = Join-Path $repoRoot "launcher\BugtopiaInject\bin\bugtopia_inject.dll"
       How  = "run launcher\BugtopiaInject\build.bat"
       Both = $true }
    @{ Path = Join-Path $repoRoot "launcher\BugtopiaInterop\bin\Release\net6.0\BugtopiaInterop.dll"
       How  = "dotnet build launcher\BugtopiaInterop -c Release"
       Both = $true }
    @{ Path = if ($PluginDll) { $PluginDll } else { Join-Path $repoRoot "buddy\bin\BepInEx\ReleaseShip\bugtopia.dll" }
       How  = "dotnet build buddy -c ReleaseShip -p:Loader=BepInEx -p:ContinuousIntegrationBuild=true"
       Both = $false }   # offline only: the online build fetches this from GitHub
)

if ($NoLinkPluginDll) {
    $payload += @{ Path = $NoLinkPluginDll
                   How  = "dotnet build buddy -c ReleaseShip -p:Loader=BepInEx -p:Telegram=false -p:ContinuousIntegrationBuild=true"
                   Both = $false }
}

$missing = @()
foreach ($item in $payload) {
    if (-not (Test-Path $item.Path)) {
        $missing += "  {0}`n      {1}" -f $item.Path.Replace("$repoRoot\", ""), $item.How
    }
}

if ($missing.Count -gt 0) {
    $message = "Payload missing:`n" + ($missing -join "`n")
    if ($SkipPayloadCheck) {
        Write-Warning $message
    }
    else {
        throw "$message`n`n  A release built without these is broken. Build them, or pass -SkipPayloadCheck."
    }
}

# ---- nothing may be holding what we are about to overwrite -----------------
#
# A launcher started from the build output keeps its own exe open, and the publish then spends ten
# seconds retrying a file copy before failing with a wall of MSBuild noise that never says the word
# "close".

$binRoot = Join-Path $repoRoot "launcher\BugtopiaLauncher\bin"
$running = Get-Process -Name Bugtopia -ErrorAction SilentlyContinue |
           Where-Object { $_.Path -and $_.Path.StartsWith($binRoot, [StringComparison]::OrdinalIgnoreCase) }

if ($running) {
    $list = ($running | ForEach-Object { "  pid {0}  {1}" -f $_.Id, $_.Path }) -join "`n"
    throw "A launcher is running from the build output and holds its own exe:`n$list`n`n  Close it and run this again."
}

# ---- the MSVC toolchain ----------------------------------------------------

$vcvars = Get-ChildItem "C:\Program Files*\Microsoft Visual Studio\*\*\VC\Auxiliary\Build\vcvars64.bat" `
                        -ErrorAction SilentlyContinue |
          Sort-Object FullName -Descending |
          Select-Object -First 1

if (-not $vcvars) {
    throw "vcvars64.bat not found. NativeAOT needs the MSVC linker - install the Desktop development with C++ workload."
}

# ---- publish ---------------------------------------------------------------

New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null

# The window the launchers are built with. Everything that names or finds a build carries the suffix,
# so a Win32 publish and a Photino publish into the same folder keep each other's files.
$uiSuffix = if ($Win32) { "-win32" } else { "" }
$uiArg = if ($Win32) { "-p:BugtopiaUi=win32" } else { "" }

# Clear this script's own earlier output. The names carry a commit hash, so without this the folder
# fills up with builds from other commits and stops saying which two are the release. Only the files
# of the window being built: the other kind is not this run's to remove.
Get-ChildItem $OutputDirectory -Filter "Bugtopia-Launcher-*.exe" -ErrorAction SilentlyContinue |
    Where-Object { $_.BaseName.EndsWith("-win32") -eq [bool]$Win32 } |
    Remove-Item -Force

$pluginArg = if ($PluginDll) { "-p:PluginDllPath=`"$PluginDll`"" } else { "" }

$flavours = @(
    @{ Name = "offline"; Args = $pluginArg }
    @{ Name = "online";  Args = "-p:BugtopiaOnline=true" }
)

# The offline compile with a different plugin inside. Its own flavour name gives it its own bin\ and
# obj\ (launcher/Directory.Build.props), so it never shares incremental state with the offline build.
if ($NoLinkPluginDll) {
    $flavours += @{ Name = "offline-nolink"; Args = "-p:BugtopiaFlavour=offline-nolink -p:PluginDllPath=`"$NoLinkPluginDll`"" }
}

$built = @()

foreach ($flavour in $flavours) {
    Write-Host "Publishing $($flavour.Name)..." -ForegroundColor Cyan

    # vcvars is quiet on success but still prints its own vswhere grumble; the exit code is what
    # decides here, so both streams go to nul.
    $name = $flavour.Name + $uiSuffix
    $command = "`"$($vcvars.FullName)`" >nul 2>&1 && dotnet publish `"$project`" -c Release " +
               "-p:IlcUseEnvironmentalTools=true $($flavour.Args) $uiArg --nologo -v minimal"

    $output = cmd /c $command
    if ($LASTEXITCODE -ne 0) {
        $output | Select-Object -Last 25 | ForEach-Object { Write-Host $_ }
        throw "Publishing the $name build failed."
    }

    # vcvars sets Platform=x64, which moves the output under bin\<flavour>\x64\. The Win32 window
    # builds into bin\<flavour>-win32\ (launcher/Directory.Build.props).
    $exe = Get-ChildItem (Join-Path $repoRoot "launcher\BugtopiaLauncher\bin\$name") `
                         -Recurse -Filter "Bugtopia.exe" -ErrorAction SilentlyContinue |
           Where-Object { $_.FullName -like "*\publish\*" } |
           Sort-Object LastWriteTime -Descending |
           Select-Object -First 1

    if (-not $exe) {
        throw "The $name publish produced no exe."
    }

    # 2.8.2+57579db is the informational version; the plus is legal in a filename but awkward in a
    # URL, so it becomes a dash. A caller that knows better - CI, which has the tag - says so.
    $version = if ($VersionLabel) { $VersionLabel } else { $exe.VersionInfo.ProductVersion }
    if ([string]::IsNullOrWhiteSpace($version)) { $version = "unversioned" }
    $version = $version.Replace("+", "-")

    $target = Join-Path $OutputDirectory ("Bugtopia-Launcher-{0}-{1}.exe" -f $version, $name)
    Copy-Item $exe.FullName $target -Force

    $built += [pscustomobject]@{
        Flavour = $name
        File    = Split-Path $target -Leaf
        MB      = [math]::Round($exe.Length / 1MB, 2)
        Bytes   = $exe.Length
    }
}

Write-Host ""
Write-Host $OutputDirectory -ForegroundColor Green
$built | Format-Table Flavour, File, MB, Bytes -AutoSize
