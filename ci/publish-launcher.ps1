<#
.SYNOPSIS
    Publishes both launcher flavours and collects them into one folder.

.DESCRIPTION
    The launcher ships as a single exe with everything inside it, and it ships twice:

      offline  carries the mod and the bootstrap; downloads nothing, ever
      online   carries no mod at all and fetches the newest release from GitHub

    They are different assemblies, not one assembly with a switch, so both are published and both
    are kept. The output is two files and nothing else - no runtime beside them, no folder to
    unpack, which is the whole point of the NativeAOT build.

    NativeAOT needs the MSVC linker, and ILCompiler's own toolchain probe cannot always find it.
    This script initialises vcvars64.bat and hands ILCompiler the environment instead, which is the
    difference between a publish that works and one that fails looking for vswhere.

.PARAMETER OutputDirectory
    Where the finished exes go. Defaults to release\ beside the repository.

.PARAMETER SkipPayloadCheck
    Publish even when a payload file is missing. The build only warns about those, which is right
    for day-to-day work and wrong for a release, so this script refuses by default.
#>
param(
    [string]$OutputDirectory = "",
    [switch]$SkipPayloadCheck
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
    @{ Path = Join-Path $repoRoot "buddy\bin\BepInEx\Release\bugtopia.dll"
       How  = "dotnet build buddy -c Release -p:Loader=BepInEx"
       Both = $false }   # offline only: the online build fetches this from GitHub
)

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

$flavours = @(
    @{ Name = "offline"; Args = "" }
    @{ Name = "online";  Args = "-p:BugtopiaOnline=true" }
)

$built = @()

foreach ($flavour in $flavours) {
    Write-Host "Publishing $($flavour.Name)..." -ForegroundColor Cyan

    # vcvars is quiet on success but still prints its own vswhere grumble; the exit code is what
    # decides here, so both streams go to nul.
    $command = "`"$($vcvars.FullName)`" >nul 2>&1 && dotnet publish `"$project`" -c Release " +
               "-p:IlcUseEnvironmentalTools=true $($flavour.Args) --nologo -v minimal"

    $output = cmd /c $command
    if ($LASTEXITCODE -ne 0) {
        $output | Select-Object -Last 25 | ForEach-Object { Write-Host $_ }
        throw "Publishing the $($flavour.Name) build failed."
    }

    # vcvars sets Platform=x64, which moves the output under bin\<flavour>\x64\.
    $exe = Get-ChildItem (Join-Path $repoRoot "launcher\BugtopiaLauncher\bin\$($flavour.Name)") `
                         -Recurse -Filter "Bugtopia.exe" -ErrorAction SilentlyContinue |
           Where-Object { $_.FullName -like "*\publish\*" } |
           Sort-Object LastWriteTime -Descending |
           Select-Object -First 1

    if (-not $exe) {
        throw "The $($flavour.Name) publish produced no exe."
    }

    # 2.8.2+57579db is the informational version; the plus is legal in a filename but awkward in a
    # URL, so it becomes a dash.
    $version = $exe.VersionInfo.ProductVersion
    if ([string]::IsNullOrWhiteSpace($version)) { $version = "unversioned" }
    $version = $version.Replace("+", "-")

    $target = Join-Path $OutputDirectory ("Bugtopia-Launcher-{0}-{1}.exe" -f $version, $flavour.Name)
    Copy-Item $exe.FullName $target -Force

    $built += [pscustomobject]@{
        Flavour = $flavour.Name
        File    = Split-Path $target -Leaf
        MB      = [math]::Round($exe.Length / 1MB, 2)
        Bytes   = $exe.Length
    }
}

Write-Host ""
Write-Host $OutputDirectory -ForegroundColor Green
$built | Format-Table Flavour, File, MB, Bytes -AutoSize
