# Packages the build reference assemblies from a local Heartopia install into ci/unified-ci-deps.zip
# for GitHub Actions. CI builds all three flavors (Loader=MelonLoader / BepInEx / Universal), so the
# zip carries the FULL reference set for both loaders — mirroring buddy/buddy.csproj:
#   * BepInEx flavor  -> BepInEx\core + BepInEx\interop
#   * MelonLoader flavor -> MelonLoader\net6 + MelonLoader\Il2CppAssemblies
#   * Universal flavor -> BepInEx\core + BepInEx\interop + MelonLoader\net6\MelonLoader.dll
# No game assemblies (Assembly-CSharp / Client / EcsClient): the mod references zero typed game symbols.
# Requires an install that has BOTH MelonLoader and BepInEx.
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

# Shared loader deps (identical identities in both installs) + each loader's entry assembly.
$melonNet6Dlls = @(
    "0Harmony.dll",
    "MonoMod.RuntimeDetour.dll",
    "MonoMod.Utils.dll",
    "Il2CppInterop.Common.dll",
    "Il2CppInterop.Runtime.dll",
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
# IL2CPP interop — same file list under BepInEx\interop and MelonLoader\Il2CppAssemblies.
$interopDlls = @(
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
$melonNet6Out = Join-Path $staging "MelonLoader\net6"
$melonInteropOut = Join-Path $staging "MelonLoader\Il2CppAssemblies"
$bepinexCoreOut = Join-Path $staging "BepInEx\core"
$bepinexInteropOut = Join-Path $staging "BepInEx\interop"
New-Item -ItemType Directory -Force -Path $melonNet6Out, $melonInteropOut, $bepinexCoreOut, $bepinexInteropOut | Out-Null

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

Copy-Deps (Join-Path $HeartopiaDir "MelonLoader\net6") $melonNet6Dlls $melonNet6Out
Copy-Deps (Join-Path $HeartopiaDir "MelonLoader\Il2CppAssemblies") $interopDlls $melonInteropOut
Copy-Deps (Join-Path $HeartopiaDir "BepInEx\core") $bepinexCoreDlls $bepinexCoreOut
Copy-Deps (Join-Path $HeartopiaDir "BepInEx\interop") $interopDlls $bepinexInteropOut

if (Test-Path $OutputZip) {
    Remove-Item $OutputZip -Force
}

Compress-Archive -Path (Join-Path $staging "MelonLoader"), (Join-Path $staging "BepInEx") -DestinationPath $OutputZip -Force
Remove-Item $staging -Recurse -Force

Write-Host "Created $OutputZip"
Write-Host "Upload it as an asset named unified-ci-deps.zip on the GitHub release tagged 'deps', or set repo secret MELONLOADER_CI_DEPS_URL to a direct download URL."
