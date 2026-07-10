# Packages the Universal build's reference assemblies from a local Heartopia install into
# ci/unified-ci-deps.zip for GitHub Actions. The single unified bugtopia.dll references BOTH
# loaders, so the zip needs MelonLoader.dll + the BepInEx core deps + the IL2CPP interop assemblies.
# This mirrors the reference set in buddy/buddy.csproj exactly. Requires an install that has BOTH
# MelonLoader and BepInEx.
param(
    [string]$HeartopiaDir = $env:HEARTOPIA_DIR,
    [string]$OutputZip = (Join-Path $PSScriptRoot "unified-ci-deps.zip")
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($HeartopiaDir)) {
    $HeartopiaDir = "C:\Program Files (x86)\Steam\steamapps\common\Heartopia"
}

if (-not (Test-Path $HeartopiaDir)) {
    throw "HeartopiaDir not found: $HeartopiaDir. Pass -HeartopiaDir or set HEARTOPIA_DIR."
}

# MelonLoader.dll is the only MelonLoader-specific reference; shared deps (MonoMod / Il2CppInterop)
# and the BepInEx entry assemblies come from BepInEx\core, and the IL2CPP interop from BepInEx\interop.
# No game assemblies (Assembly-CSharp / Client / EcsClient) — the mod references zero typed game symbols.
$net6Dlls = @(
    "MelonLoader.dll"
)
$bepinexCoreDlls = @(
    "0Harmony.dll",
    "MonoMod.RuntimeDetour.dll",
    "MonoMod.Utils.dll",
    "Il2CppInterop.Common.dll",
    "Il2CppInterop.Runtime.dll",
    "BepInEx.Core.dll",
    "BepInEx.Unity.IL2CPP.dll"
)
$bepinexInteropDlls = @(
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

$staging = Join-Path $env:TEMP ("heartopia-ci-deps-" + [guid]::NewGuid().ToString("N"))
$net6Out = Join-Path $staging "MelonLoader\net6"
$coreOut = Join-Path $staging "BepInEx\core"
$interopOut = Join-Path $staging "BepInEx\interop"
New-Item -ItemType Directory -Force -Path $net6Out, $coreOut, $interopOut | Out-Null

function Copy-Deps([string]$SrcDir, [string[]]$Names, [string]$DestDir) {
    if (-not (Test-Path $SrcDir)) {
        throw "Dependency source directory not found: $SrcDir (does this install have both loaders?)."
    }
    foreach ($dll in $Names) {
        $src = Join-Path $SrcDir $dll
        if (-not (Test-Path $src)) {
            throw "Missing required dependency: $src"
        }
        Copy-Item $src (Join-Path $DestDir $dll)
    }
}

Copy-Deps (Join-Path $HeartopiaDir "MelonLoader\net6") $net6Dlls $net6Out
Copy-Deps (Join-Path $HeartopiaDir "BepInEx\core") $bepinexCoreDlls $coreOut
Copy-Deps (Join-Path $HeartopiaDir "BepInEx\interop") $bepinexInteropDlls $interopOut

if (Test-Path $OutputZip) {
    Remove-Item $OutputZip -Force
}

Compress-Archive -Path (Join-Path $staging "MelonLoader"), (Join-Path $staging "BepInEx") -DestinationPath $OutputZip -Force
Remove-Item $staging -Recurse -Force

Write-Host "Created $OutputZip"
Write-Host "Upload it as an asset named unified-ci-deps.zip on the GitHub release tagged 'deps', or set repo secret MELONLOADER_CI_DEPS_URL to a direct download URL."
