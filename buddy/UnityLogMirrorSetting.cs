using System;
using System.Reflection;

namespace HeartopiaMod
{
    // Read/write access to BepInEx's OWN core setting `[Logging] UnityLogListening`, which controls
    // whether BepInEx mirrors Unity's log output into its log.
    //
    // Why the mod exposes a loader setting at all: that listener is what makes BepInEx trigger
    // IL2CPP class injection BEFORE any plugin loads. It assigns a managed delegate to
    // `Application.s_LogCallbackHandler`, which goes `DelegateSupport.ConvertDelegate` →
    // `RegisterTypeInIl2Cpp<Il2CppToMonoDelegateReference>` → `InjectorHelpers.Setup()` → all five
    // ClassInjector hooks written into `GameAssembly.dll` `.text`. Turning it off is therefore the
    // only way the mod's own hook trim can land under BepInEx (5 → 3), because `Setup()` runs once,
    // lazily, and today it has already run by the time `Load()` is called.
    //
    // ⚠️ Read at loader startup, so a change only takes effect on the NEXT launch. Every caller must
    // say so; silently writing a value that appears to do nothing is worse than not offering it.
    //
    // MelonLoader has no equivalent and needs none — verified three ways: `s_LogCallbackHandler` /
    // `LogCallback` appear in `BepInEx.Unity.IL2CPP.dll` and in NEITHER `MelonLoader.dll` nor its
    // Il2Cpp support module; ML's log contains no Unity-originated lines at all; and the injected-
    // class counter (InjectionGateCanary.cs) read 2 at the first world load under ML — i.e.
    // `Il2CppToMonoDelegateReference` was not registered yet — versus 1 under BepInEx, where that
    // one class can only be the delegate reference. Under ML `Setup()` is triggered unconditionally
    // by the support module (`Main.Initialize` → `MonoEnumeratorWrapper.Register()`) instead, which
    // is not optional. So there is nothing to switch off there, and the UI row hides itself.
    //
    // Pure string reflection, zero compile-time BepInEx types: the same DLL runs under MelonLoader,
    // where those assemblies are absent, and the MelonLoader-only flavor does not reference them.
    internal static class UnityLogMirrorSetting
    {
        private const string ConfigSection = "Logging";
        private const string ConfigKey = "UnityLogListening";

        private static bool resolveAttempted;
        private static object configEntry;         // BepInEx.Configuration.ConfigEntryBase
        private static PropertyInfo boxedValue;    // ConfigEntryBase.BoxedValue { get; set; }
        private static string status = "not resolved";

        internal static string Status => status;

        // Self-validating visibility gate: the row appears exactly when it can actually do
        // something. Under MelonLoader the assembly is absent, so this is false and nothing shows.
        internal static bool IsAvailable
        {
            get
            {
                Resolve();
                return configEntry != null && boxedValue != null;
            }
        }

        // Current on-disk value. Defaults to true when unreadable, matching BepInEx's own default —
        // a false here would tell the user mirroring is already off when it is not.
        internal static bool Enabled
        {
            get
            {
                try
                {
                    return !IsAvailable || boxedValue.GetValue(configEntry) is not bool b || b;
                }
                catch
                {
                    return true;
                }
            }
        }

        // Writes through BepInEx's own config object rather than editing BepInEx.cfg as text, so the
        // file is rewritten by the code that owns its format (comments, types and section order are
        // preserved). `ConfigFile.SaveOnConfigSet` defaults to true, so the setter persists it.
        internal static bool TrySet(bool value)
        {
            try
            {
                if (!IsAvailable)
                {
                    return false;
                }

                boxedValue.SetValue(configEntry, value);
                ModLogger.Msg("[UnityLogMirror] " + ConfigSection + "." + ConfigKey + " = " + value
                              + " — written to BepInEx.cfg; takes effect on the next launch.");
                return true;
            }
            catch (Exception ex)
            {
                status = "write failed: " + ex.Message;
                ModLogger.Msg("[UnityLogMirror] " + status);
                return false;
            }
        }

        private static void Resolve()
        {
            if (resolveAttempted)
            {
                return;
            }

            resolveAttempted = true;

            try
            {
                Assembly core = ClassInjectorHookTrim.FindAssembly("BepInEx.Core");
                if (core == null)
                {
                    status = "not under BepInEx";
                    return;
                }

                Type configFileType = core.GetType("BepInEx.Configuration.ConfigFile", throwOnError: false);
                PropertyInfo coreConfigProp = configFileType?.GetProperty("CoreConfig", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                object coreConfig = coreConfigProp?.GetValue(null);
                if (coreConfig == null)
                {
                    status = "ConfigFile.CoreConfig unavailable";
                    return;
                }

                // ConfigFile has a public (string section, string key) indexer. It resolves to a
                // Dictionary lookup and THROWS KeyNotFoundException when the key is absent — hence
                // the try/catch rather than a null check.
                PropertyInfo indexer = configFileType.GetProperty("Item", new[] { typeof(string), typeof(string) });
                object entry = indexer?.GetValue(coreConfig, new object[] { ConfigSection, ConfigKey });
                if (entry == null)
                {
                    status = ConfigSection + "." + ConfigKey + " not present";
                    return;
                }

                PropertyInfo boxed = entry.GetType().GetProperty("BoxedValue", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (boxed == null || !boxed.CanRead || !boxed.CanWrite)
                {
                    status = "BoxedValue not read/write";
                    return;
                }

                configEntry = entry;
                boxedValue = boxed;
                status = "ready";
            }
            catch (Exception ex)
            {
                status = "resolve failed: " + ex.Message;
            }
        }

    }
}
