#if LOADER_BEPINEX
using System;
using System.Reflection;

namespace HeartopiaMod
{
    // Reduces the IL2CPP `.text` footprint the mod is responsible for from 5 inline detours to 3,
    // WITHOUT giving up the injected MonoBehaviour that the mod genuinely needs as its pump.
    //
    // Why this is the endgame: two attempts to reach zero both failed, and both failures are
    // instructive rather than fixable.
    //   * Detaching the 5 hooks after injection → crash within seconds (dump coreclr_6312). Two of
    //     them translate the NEGATIVE class tokens that are the injection mechanism itself.
    //   * Pumping from the embedded-Mono side → faults on the first Il2CppInterop call (coreclr_25768):
    //     re-entering il2cpp from a thread executing Mono code is not a valid transition.
    //   * Pumping from a hand-built Unity PlayerLoop node with a raw `updateFunction` pointer →
    //     `SetPlayerLoop` retains the pointer, then the ENGINE access-violates in its own code before
    //     the first tick (dump xdt.exe.25792, 0xC0000005 at UnityPlayer.dll+0x7CC8A6).
    // An Il2CppInterop mod must be pumped by an injected MonoBehaviour. What is still on the table is
    // installing FEWER of the hooks that injection pulls in.
    //
    // `Hook<T>.ApplyHook()` opens with `if (!_isApplied)`, so pre-setting that private field to true
    // makes `InjectorHelpers.Setup()` skip the hook entirely — it is never installed, and Dobby never
    // writes those bytes. Must run BEFORE the first `ClassInjector.RegisterTypeInIl2Cpp`, because
    // `Setup()` is what installs them and it runs once, lazily, on that first call.
    //
    // WHICH ONES ARE SAFE — proven from the hook bodies, not assumed:
    //  * `Class::GetFieldDefaultValue` — its only gate is `EnumInjector.GetDefaultValueOverride`,
    //    a lookup in `EnumInjector.s_DefaultValueOverrides`, written ONLY by
    //    `EnumInjector.CreateOrUpdateFieldDefaultValue` (reachable only from RegisterEnumInIl2Cpp).
    //    It touches no ClassInjector state at all. This mod injects no enums ⇒ provably dead code.
    //    It is also the most fragile to install (6 byte-signature patterns + an xref fallback), so
    //    skipping it removes a failure mode as well as a patch.
    //  * `GenericMethod::GetMethod` — its only gate is `ClassInjector.InflatedMethodFromContextDictionary`,
    //    populated at exactly one site, under `if (methodInfo.IsGenericMethod && !methodInfo.IsAbstract)`.
    //    `HeartopiaBehaviour` declares no generic methods ⇒ provably dead code.
    //
    // WHICH ONES MUST STAY:
    //  * `MetadataCache::GetTypeInfoFromTypeDefinitionIndex` and `Class::FromIl2CppType` map the
    //    negative type indices from `InjectorHelpers.CreateClassToken` back to the injected class.
    //    These ARE the mechanism; removing them is what crashed in coreclr_6312.
    //  * `Class::FromName` is a fallback-on-null (it calls the original first and only consults
    //    `s_ClassNameLookup` when il2cpp returned null). Almost certainly unused here, since
    //    AddComponent resolves by class pointer rather than by name — but "almost certainly" is not
    //    the standard the other two meet, so it is opt-in via TrimFromNameHook and off by default.
    internal static class ClassInjectorHookTrim
    {
        // Opt-in for the third, lower-confidence trim. Leave false unless you are testing it.
        private const bool TrimFromNameHook = false;

        internal static string Status = "not run";
        internal static int Suppressed;

        private static readonly string[] AlwaysSafe =
        {
            "GetFieldDefaultValueHook",     // EnumInjector-only; we inject no enums
            "GenericMethodGetMethodHook",   // generic methods on injected types; we declare none
        };

        // Call BEFORE the first AddComponent / RegisterTypeInIl2Cpp.
        internal static void Apply()
        {
            try
            {
                Assembly interop = FindAssembly("Il2CppInterop.Runtime");
                if (interop == null)
                {
                    Status = "Il2CppInterop.Runtime not loaded";
                    ModLogger.Msg("[HookTrim] " + Status + " — skipping (all 5 hooks will install)");
                    return;
                }

                Type helpers = interop.GetType("Il2CppInterop.Runtime.Injection.InjectorHelpers", throwOnError: false);
                if (helpers == null)
                {
                    Status = "InjectorHelpers not found";
                    ModLogger.Msg("[HookTrim] " + Status + " — skipping (all 5 hooks will install)");
                    return;
                }

                int done = 0;
                for (int i = 0; i < AlwaysSafe.Length; i++)
                {
                    if (Suppress(helpers, AlwaysSafe[i]))
                    {
                        done++;
                    }
                }

                if (TrimFromNameHook && Suppress(helpers, "FromNameHook"))
                {
                    done++;
                }

                Suppressed = done;
                Status = "suppressed " + done;
                ModLogger.Msg("[HookTrim] " + Status + " ClassInjector hook(s) before injection — "
                              + (5 - done) + " will install instead of 5");
            }
            catch (Exception ex)
            {
                Status = "failed: " + ex.Message;
                ModLogger.Msg("[HookTrim] " + Status + " — continuing with all 5 hooks");
            }
        }

        // Marks the hook as already-applied so ApplyHook() no-ops. Fail-closed: any miss just means
        // that hook installs normally, which is exactly today's behaviour.
        private static bool Suppress(Type helpers, string fieldName)
        {
            try
            {
                FieldInfo holder = helpers.GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                object hook = holder?.GetValue(null);
                if (hook == null)
                {
                    ModLogger.Msg("[HookTrim] " + fieldName + ": not present — nothing to suppress");
                    return false;
                }

                FieldInfo applied = FindFieldOnHierarchy(hook.GetType(), "_isApplied");
                if (applied == null || applied.FieldType != typeof(bool))
                {
                    ModLogger.Msg("[HookTrim] " + fieldName + ": _isApplied not found — left enabled");
                    return false;
                }

                if (applied.GetValue(hook) is bool already && already)
                {
                    ModLogger.Msg("[HookTrim] " + fieldName + ": already applied — TOO LATE to suppress");
                    return false;
                }

                applied.SetValue(hook, true);
                ModLogger.Msg("[HookTrim] " + fieldName + ": suppressed");
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[HookTrim] " + fieldName + " failed: " + ex.Message);
                return false;
            }
        }

        private static Assembly FindAssembly(string simpleName)
        {
            Assembly[] all = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < all.Length; i++)
            {
                try
                {
                    if (string.Equals(all[i].GetName().Name, simpleName, StringComparison.OrdinalIgnoreCase))
                    {
                        return all[i];
                    }
                }
                catch
                {
                }
            }

            return null;
        }

        private static FieldInfo FindFieldOnHierarchy(Type t, string name)
        {
            while (t != null && t != typeof(object))
            {
                FieldInfo f = t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (f != null)
                {
                    return f;
                }

                t = t.BaseType;
            }

            return null;
        }
    }
}
#endif
