# Packages MelonLoader (and, when present, BepInEx) build references from a local
# Heartopia install into ci/melonloader-ci-deps.zip for GitHub Actions.
# The BepInEx set feeds the artifacts-only BepInEx ReleaseShip CI build; when the
# local install has no BepInEx, a MelonLoader-only zip is produced and the CI
# BepInEx step self-skips.
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
  # HeartopiaComplete.Fishing.cs cast-skip (Animator.StringToHash)
    "UnityEngine.AnimationModule.dll",
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

# --- BepInEx flavor (buddy.csproj Loader=BepInEx reference set) ---------------
$bepinexCoreDlls = @(
    "0Harmony.dll",
    "MonoMod.RuntimeDetour.dll",
    "MonoMod.Utils.dll",
    "BepInEx.Core.dll",
    "BepInEx.Unity.IL2CPP.dll",
    "Il2CppInterop.Common.dll",
    "Il2CppInterop.Runtime.dll"
)

# Same Unity module list as MelonLoader, but the game assembly is Client.dll
# (BepInEx interop naming) instead of Il2CppClient.dll.
$bepinexInteropDlls = @(
    "Assembly-CSharp.dll",
    "Client.dll",
    "Il2Cppmscorlib.dll",
    "UnityEngine.dll",
    "UnityEngine.AnimationModule.dll",
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

$bepinexCoreSrc = Join-Path $HeartopiaDir "BepInEx\core"
$bepinexInteropSrc = Join-Path $HeartopiaDir "BepInEx\interop"
$zipRoots = @((Join-Path $staging "MelonLoader"))

if ((Test-Path $bepinexCoreSrc) -and (Test-Path $bepinexInteropSrc)) {
    $bepinexCoreOut = Join-Path $staging "BepInEx\core"
    $bepinexInteropOut = Join-Path $staging "BepInEx\interop"
    New-Item -ItemType Directory -Force -Path $bepinexCoreOut, $bepinexInteropOut | Out-Null

    foreach ($dll in $bepinexCoreDlls) {
        $src = Join-Path $bepinexCoreSrc $dll
        if (-not (Test-Path $src)) {
            throw "Missing BepInEx core dependency: $src"
        }

        Copy-Item $src (Join-Path $bepinexCoreOut $dll)
    }

    foreach ($dll in $bepinexInteropDlls) {
        $src = Join-Path $bepinexInteropSrc $dll
        if (-not (Test-Path $src)) {
            throw "Missing BepInEx interop dependency: $src"
        }

        Copy-Item $src (Join-Path $bepinexInteropOut $dll)
    }

    $bepinexEcsClient = Join-Path $bepinexInteropSrc "EcsClient.dll"
    if (Test-Path $bepinexEcsClient) {
        Copy-Item $bepinexEcsClient (Join-Path $bepinexInteropOut "EcsClient.dll")
    }

    $zipRoots += (Join-Path $staging "BepInEx")
    Write-Host "BepInEx dependencies included."
}
else {
    Write-Warning "BepInEx not found under $HeartopiaDir - packaging MelonLoader deps only (the CI BepInEx build will self-skip)."
}

if (Test-Path $OutputZip) {
    Remove-Item $OutputZip -Force
}

Compress-Archive -Path $zipRoots -DestinationPath $OutputZip -Force
Remove-Item $staging -Recurse -Force

Write-Host "Created $OutputZip"
Write-Host "Upload as a GitHub release asset named melonloader-ci-deps.zip, or set repo secret MELONLOADER_CI_DEPS_URL."
