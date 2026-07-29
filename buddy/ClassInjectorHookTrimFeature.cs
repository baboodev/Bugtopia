using System;
using System.Reflection;
using System.Runtime.InteropServices;

namespace HeartopiaMod
{
    // Skips the ClassInjector VM hooks that are provably dead code in this process, so injection
    // installs fewer inline detours inside GameAssembly `.text`.
    //
    // ⚠️ THE COUNTS DIFFER PER LOADER:
    //   * BepInEx baseline is 5, and under the DEFAULT `UnityLogListening = true` this trim is a
    //     no-op there: BepInEx triggers injection itself, before plugins load, via its Unity log
    //     listener, so by the time a plugin can run the hooks are already applied (it logs
    //     "already applied — TOO LATE to suppress"). Turn that listener off — the toggle on
    //     Settings→Logging, `UnityLogMirrorSetting.cs` — and nothing has triggered `Setup()` by
    //     plugin-load time, so the trim DOES land and BepInEx goes 5 → 3. That is why
    //     `HeartopiaBepInPlugin.Load()` calls Apply() BEFORE its PlayerLoop early-return: it is the
    //     only moment the suppression can land there.
    //   * MelonLoader baseline is 6 — the same 5 plus `GarbageCollector::RunFinalizer` — and there
    //     the trim DOES land, from `OnEarlyInitializeMelon`, taking ML to 4. `Setup()` is first
    //     triggered at `Main.Initialize:78` → `MonoEnumeratorWrapper.Register()`, inside
    //     `SupportModule.Setup()` (`Core.cs:110`), while mods load at `Core.cs:108` — a measured
    //     2.2 s of margin. Prefer `OnEarlyInitializeMelon` over `OnPreSupportModule`, which ML's own
    //     error text calls deprecated.
    //
    // The dead-code claim is not just a grep: `InjectionGateCanary.cs` measures both gate
    // dictionaries at runtime, and a full session on each loader reported `inflatedGenerics=0
    // enumOverrides=0` at every world load and at shutdown. That canary keeps working after this
    // trim ships — the dictionaries are written at type-REGISTRATION time and only READ by the
    // hooks — so it stays the standing guard against a future ML update injecting a type with a
    // generic method.
    //
    // (historical — kept for the dead ends, whose CONCLUSION no longer holds) Three attempts to
    // stop causing `.text` patches at all:
    //   * Detaching the 5 hooks after injection → crash within seconds (dump coreclr_6312). Two of
    //     them translate the NEGATIVE class tokens that are the injection mechanism itself. Still
    //     true, still do not try it.
    //   * Pumping from the embedded-Mono side → faults on the first Il2CppInterop call (coreclr_25768):
    //     re-entering il2cpp from a thread executing Mono code is not a valid transition. Still true.
    //   * Pumping from a hand-built Unity PlayerLoop node with a raw `updateFunction` pointer →
    //     `SetPlayerLoop` retains the pointer, then the ENGINE access-violates in its own code before
    //     the first tick (dump xdt.exe.25792, 0xC0000005 at UnityPlayer.dll+0x7CC8A6).
    // ⚠️ The old conclusion drawn from these — "an Il2CppInterop mod must be pumped by an injected
    // MonoBehaviour" — is WRONG and was superseded. Only the THIRD attempt was fixable, and it was
    // fixed: `updateFunction` is `void(**)()`, a pointer to a writable slot, not the code pointer.
    // `PlayerLoopProbeFeature.cs` ships that pump (`MakeSlot`), it injects no managed type, and with
    // the log listener off a live BepInEx session measured ZERO `.text` patches.
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
    //    It touches no ClassInjector state at all. Nothing in ML core, ML's support module,
    //    Il2CppInterop or this mod registers an enum ⇒ provably dead code.
    //    It is also the most fragile to install (6 byte-signature patterns + an xref fallback), so
    //    skipping it removes a failure mode as well as a patch.
    //  * `GenericMethod::GetMethod` — its only gate is `ClassInjector.InflatedMethodFromContextDictionary`,
    //    populated at exactly one site, under `if (methodInfo.IsGenericMethod && !methodInfo.IsAbstract)`.
    //    No injected type in this process declares a generic instance method ⇒ provably dead code.
    //    Census under ML (the loader where this trim actually lands): ML's `MonoEnumeratorWrapper`
    //    (get_Current/MoveNext/Reset/ctors — `Register()` is static, and statics are excluded by
    //    `IsMethodEligible` along with `Finalize` and `[HideFromIl2Cpp]`), ML's `SM_Component`
    //    (the Unity message set), and Il2CppInterop's `Il2CppToMonoDelegateReference` (2 ctors and a
    //    finalizer). The mod itself injects NOTHING under ML — `HeartopiaBehaviour` and its only
    //    `AddComponent<>` call site are inside `#if LOADER_BEPINEX`, and the pump there is ML's own
    //    `SM_Component`. ⚠️ This census covers code we do not own; that is what the canary guards.
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

                // Deliberately counts ClassInjector hooks ONLY (5 on both loaders). The total `.text`
                // detour count is loader-specific — ML carries RunFinalizer on top — so stating it
                // here would be wrong on one of them.
                ModLogger.Msg("[HookTrim] " + Status + " ClassInjector hook(s) before injection — "
                              + (5 - done) + " of 5 will install");
            }
            catch (Exception ex)
            {
                Status = "failed: " + ex.Message;
                ModLogger.Msg("[HookTrim] " + Status + " — continuing with all 5 hooks");
            }
        }

        // Reports what ACTUALLY happened to each hook — the 5 shared ClassInjector ones plus ML's
        // `RunFinalizerPatch` — without consulting `_isApplied`: that field is the lie this class
        // tells, so it can only ever confirm itself.
        //
        // Two independent signals instead:
        //   * `_method` is assigned INSIDE ApplyHook(), after the target is located. Suppression
        //     makes ApplyHook() return at its opening `if (!_isApplied)`, so a suppressed hook has a
        //     null `_method`. Non-null therefore means ApplyHook really ran.
        //   * for those that ran, `_method` round-trips back to the patched function address, so the
        //     prologue can be read directly: an installed Dobby detour opens `FF 25` (rip-relative
        //     jmp) or `E9` (rel32), exactly as the scene-hook cleanup proved byte-for-byte.
        //
        // Deliberately NOT calling the hooks' own FindTargetMethod(): it reaches
        // `Il2CppInteropRuntime.Instance` → `BaseHost.GetInstance<T>()`, which THROWS (it does not
        // return null) until the runtime is created, and re-scanning a patched prologue by signature
        // is the documented trap anyway — once patched, the prologue IS a jmp.
        // Unconditionally re-runnable — deliberately NOT one-shot. With `UnityLogListening=false`
        // plus the PlayerLoop pump, nothing triggers `InjectorHelpers.Setup()` at startup, so an
        // early reading shows all five NOT INSTALLED (measured: zero `.text` patches). The hooks
        // then appear the moment anything converts a managed delegate to il2cpp. Catching that
        // 0 → 3 transition needs a reading on BOTH sides of it, which a one-shot flag would hide.
        internal static void Verify()
        {
            try
            {
                Assembly interop = FindAssembly("Il2CppInterop.Runtime");
                Type helpers = interop?.GetType("Il2CppInterop.Runtime.Injection.InjectorHelpers", throwOnError: false);
                if (helpers == null)
                {
                    ModLogger.Msg("[HookTrim] verify: InjectorHelpers unavailable");
                    return;
                }

                string[] all =
                {
                    "GetFieldDefaultValueHook",
                    "GenericMethodGetMethodHook",
                    "GetTypeInfoFromTypeDefinitionIndexHook",
                    "FromIl2CppTypeHook",
                    "FromNameHook",
                    "RunFinalizerPatch",          // ML only; reported as absent under BepInEx
                };

                for (int i = 0; i < all.Length; i++)
                {
                    ModLogger.Msg("[HookTrim] verify: " + all[i] + " = " + DescribeHook(helpers, all[i]));
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[HookTrim] verify failed: " + ex.Message);
            }
        }

        private static string DescribeHook(Type helpers, string fieldName)
        {
            try
            {
                FieldInfo holder = helpers.GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                object hook = holder?.GetValue(null);
                if (hook == null)
                {
                    return "absent in this Il2CppInterop build";
                }

                FieldInfo methodField = FindFieldOnHierarchy(hook.GetType(), "_method");
                if (methodField?.GetValue(hook) is not Delegate target)
                {
                    return "NOT INSTALLED (ApplyHook never ran)";
                }

                IntPtr addr = Marshal.GetFunctionPointerForDelegate(target);
                return addr == IntPtr.Zero
                    ? "installed, address unavailable"
                    : "INSTALLED @0x" + addr.ToString("X") + " [" + PrologueHex(addr, 6) + "]";
            }
            catch (Exception ex)
            {
                return "unreadable: " + ex.Message;
            }
        }

        // Shared with MelonSceneHookCleanup.cs, which reads prologues for the same reason (proving a
        // detour is present or gone). One formatter so both subsystems' log lines stay greppable —
        // they used to differ only in their unreadable-sentinel.
        internal static string PrologueHex(IntPtr addr, int count)
        {
            try
            {
                char[] hex = new char[count * 2];
                for (int i = 0; i < count; i++)
                {
                    byte b = Marshal.ReadByte(addr, i);
                    hex[i * 2] = HexDigit(b >> 4);
                    hex[(i * 2) + 1] = HexDigit(b & 0xF);
                }

                return new string(hex);
            }
            catch
            {
                return "<unreadable>";
            }
        }

        private static char HexDigit(int nibble)
        {
            return (char)(nibble < 10 ? '0' + nibble : 'A' + (nibble - 10));
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

        // Shared: the loader-glue types are all reached by walking loaded assemblies by simple name,
        // and this used to be copy-pasted into every one of them.
        internal static Assembly FindAssembly(string simpleName)
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
