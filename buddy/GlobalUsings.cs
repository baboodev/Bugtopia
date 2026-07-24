// TextMeshPro lives in a different namespace per loader interop: MelonLoader's Il2CppAssemblies
// generate Il2CppTMPro, while the BepInEx interop (also used by the Universal build) keeps plain
// TMPro. One conditional global import here instead of per-file usings; needs LangVersion >= 10.
#if LOADER_MELON && !LOADER_BEPINEX
global using Il2CppTMPro;
#else
global using TMPro;
#endif
