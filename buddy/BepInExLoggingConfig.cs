using System;
using System.Reflection;

namespace HeartopiaMod
{
    // Forces two of BepInEx's OWN core settings off, once per launch:
    //
    //     [Logging]         UnityLogListening = false
    //     [Logging.Console] Enabled           = false
    //
    // Why the mod writes LOADER settings at all: `UnityLogListening` is what makes BepInEx trigger
    // IL2CPP class injection BEFORE any plugin loads. It assigns a managed delegate to
    // `Application.s_LogCallbackHandler`, which goes `DelegateSupport.ConvertDelegate` →
    // `RegisterTypeInIl2Cpp<Il2CppToMonoDelegateReference>` → `InjectorHelpers.Setup()` → all five
    // ClassInjector hooks written into `GameAssembly.dll` `.text`. Off, the mod's own hook trim can
    // land under BepInEx (5 → 3), because `Setup()` runs once, lazily, and with the listener enabled
    // it has already run by the time `Load()` is called. The console row rides along because it is
    // the other default that a hand-made BepInEx install starts with and nobody wants.
    //
    // ⚠️ BOTH VALUES ARE READ AT LOADER STARTUP, so a write here only takes effect on the NEXT
    // launch — this run keeps whatever the file said when the process started. That is exactly why
    // this is automatic instead of the checkbox it replaced: a UI switch could only ever promise the
    // same delayed effect, and it sat on a page most users open once, so installs stayed on the
    // BepInEx defaults indefinitely. Written on every launch, so it also repairs a config that was
    // reset or replaced.
    //
    // The launcher's `Payload.ApplyLoggingDefaults` writes the same keys at Prepare time. The two do
    // not conflict — same keys, same values; this one is what covers an install the launcher never
    // prepared (a hand-made BepInEx folder, or one predating that payload).
    //
    // MelonLoader has no equivalent and needs none — verified three ways: `s_LogCallbackHandler` /
    // `LogCallback` appear in `BepInEx.Unity.IL2CPP.dll` and in NEITHER `MelonLoader.dll` nor its
    // Il2Cpp support module; ML's log contains no Unity-originated lines at all; and the injected-
    // class counter (InjectionGateCanary.cs) read 2 at the first world load under ML — i.e.
    // `Il2CppToMonoDelegateReference` was not registered yet — versus 1 under BepInEx, where that
    // one class can only be the delegate reference. Under ML `Setup()` is triggered unconditionally
    // by the support module (`Main.Initialize` → `MonoEnumeratorWrapper.Register()`) instead, which
    // is not optional. So there is nothing to switch off there, and this exits on the first check.
    //
    // Pure string reflection, zero compile-time BepInEx types: the same DLL runs under MelonLoader,
    // where those assemblies are absent, and the MelonLoader-only flavor does not reference them.
    internal static class BepInExLoggingConfig
    {
        // Section/key pairs pinned to false. Values live in BepInEx's own config object, so the file
        // is rewritten by the code that owns its format (comments, types and section order kept).
        private static readonly string[][] ForcedOff =
        {
            new[] { "Logging", "UnityLogListening" },
            new[] { "Logging.Console", "Enabled" }
        };

        private static bool applied;

        // Idempotent and safe to call from any loader: everything below fails closed with a logged
        // reason rather than throwing into the plugin's Load().
        internal static void Apply()
        {
            if (applied)
            {
                return;
            }

            applied = true;

            try
            {
                Assembly core = ClassInjectorHookTrim.FindAssembly("BepInEx.Core");
                if (core == null)
                {
                    return; // not under BepInEx — nothing to write, and nothing worth a log line
                }

                Type configFileType = core.GetType("BepInEx.Configuration.ConfigFile", throwOnError: false);
                PropertyInfo coreConfigProp = configFileType?.GetProperty("CoreConfig", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                object coreConfig = coreConfigProp?.GetValue(null);
                if (coreConfig == null)
                {
                    ModLogger.Msg("[BepInExLogging] ConfigFile.CoreConfig unavailable — BepInEx.cfg left untouched.");
                    return;
                }

                // ConfigFile has a public (string section, string key) indexer. It resolves to a
                // Dictionary lookup and THROWS KeyNotFoundException when the key is absent — hence
                // the per-key try/catch below rather than a null check.
                PropertyInfo indexer = configFileType.GetProperty("Item", new[] { typeof(string), typeof(string) });
                if (indexer == null)
                {
                    ModLogger.Msg("[BepInExLogging] ConfigFile string/string indexer not found — BepInEx.cfg left untouched.");
                    return;
                }

                int changed = 0;
                for (int i = 0; i < ForcedOff.Length; i++)
                {
                    if (ForceOff(indexer, coreConfig, ForcedOff[i][0], ForcedOff[i][1]))
                    {
                        changed++;
                    }
                }

                if (changed > 0)
                {
                    ModLogger.Msg("[BepInExLogging] " + changed + " setting(s) written to BepInEx.cfg — "
                                  + "they take effect on the NEXT launch, not this one.");
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[BepInExLogging] failed: " + ex);
            }
        }

        // True when this call actually changed the value. `ConfigFile.SaveOnConfigSet` defaults to
        // true, so the setter is what persists the file — no explicit Save() call needed.
        private static bool ForceOff(PropertyInfo indexer, object coreConfig, string section, string key)
        {
            string id = section + "." + key;
            try
            {
                object entry = indexer.GetValue(coreConfig, new object[] { section, key });
                if (entry == null)
                {
                    ModLogger.Msg("[BepInExLogging] " + id + " not present — skipped.");
                    return false;
                }

                PropertyInfo boxed = entry.GetType().GetProperty("BoxedValue", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (boxed == null || !boxed.CanRead || !boxed.CanWrite)
                {
                    ModLogger.Msg("[BepInExLogging] " + id + " BoxedValue not read/write — skipped.");
                    return false;
                }

                if (boxed.GetValue(entry) is not bool current)
                {
                    ModLogger.Msg("[BepInExLogging] " + id + " is not a Boolean — skipped.");
                    return false;
                }

                if (!current)
                {
                    return false; // already off — the steady state, kept silent
                }

                boxed.SetValue(entry, false);
                ModLogger.Msg("[BepInExLogging] " + id + " = false written to BepInEx.cfg.");
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[BepInExLogging] " + id + " could not be written: " + ex.Message);
                return false;
            }
        }
    }
}
