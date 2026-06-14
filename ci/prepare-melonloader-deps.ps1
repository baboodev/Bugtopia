# Packages MelonLoader build references from a local Heartopia install into
# ci/melonloader-ci-deps.zip for GitHub Actions.
param(
    [string]$HeartopiaDir = $env:HEARTOPIA_DIR,
    [string]$OutputZip = (Join-Path $PSScriptRoot "melonloader-ci-deps.zip")
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($HeartopiaDir)) {
    $HeartopiaDir = "C:\Program Files (x86)\Steam\steamapps\common\Heartopia"
}

if (-not (Test-Path $HeartopiaDir)) {
    throw "HeartopiaDir not found: $HeartopiaDir. Pass -HeartopiaDir or set HEARTOPIA_DIR."
}

$net6Dlls = @(
    "0Harmony.dll",
    "MelonLoader.dll",
    "Il2CppInterop.Common.dll",
    "Il2CppInterop.Runtime.dll",
  # BuildingFreeRotateFeature.cs (NativeDetour on embedded Mono JIT entries)
    "MonoMod.RuntimeDetour.dll",
    "MonoMod.Utils.dll"
)

$interopDlls = @(
    "Assembly-CSharp.dll",
    "Il2CppClient.dll",
    "Il2Cppmscorlib.dll",
    "UnityEngine.dll",
    "UnityEngine.AssetBundleModule.dll",
    "UnityEngine.CoreModule.dll",
    "UnityEngine.ImageConversionModule.dll",
    "UnityEngine.IMGUIModule.dll",
    "UnityEngine.InputLegacyModule.dll",
    "UnityEngine.PhysicsModule.dll",
    "UnityEngine.SharedInternalsModule.dll",
    "UnityEngine.TextRenderingModule.dll",
    "UnityEngine.UI.dll",
    "UnityEngine.UIModule.dll",
    "Unity.TextMeshPro.dll"
)

$staging = Join-Path $env:TEMP ("heartopia-ci-deps-" + [guid]::NewGuid().ToString("N"))
$net6Out = Join-Path $staging "MelonLoader\net6"
$interopOut = Join-Path $staging "MelonLoader\Il2CppAssemblies"
New-Item -ItemType Directory -Force -Path $net6Out, $interopOut | Out-Null

$net6Src = Join-Path $HeartopiaDir "MelonLoader\net6"
$interopSrc = Join-Path $HeartopiaDir "MelonLoader\Il2CppAssemblies"

foreach ($dll in $net6Dlls) {
    $src = Join-Path $net6Src $dll
    if (-not (Test-Path $src)) {
        throw "Missing MelonLoader dependency: $src"
    }

    Copy-Item $src (Join-Path $net6Out $dll)
}

foreach ($dll in $interopDlls) {
    $src = Join-Path $interopSrc $dll
    if (-not (Test-Path $src)) {
        throw "Missing Il2Cpp interop dependency: $src"
    }

    Copy-Item $src (Join-Path $interopOut $dll)
}

$ecsClient = Join-Path $interopSrc "EcsClient.dll"
if (Test-Path $ecsClient) {
    Copy-Item $ecsClient (Join-Path $interopOut "EcsClient.dll")
}

if (Test-Path $OutputZip) {
    Remove-Item $OutputZip -Force
}

Compress-Archive -Path (Join-Path $staging "MelonLoader") -DestinationPath $OutputZip -Force
Remove-Item $staging -Recurse -Force

Write-Host "Created $OutputZip"
Write-Host "Upload as a GitHub release asset named melonloader-ci-deps.zip, or set repo secret MELONLOADER_CI_DEPS_URL."
