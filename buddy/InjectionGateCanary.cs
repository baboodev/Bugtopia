using System;
using System.Reflection;

namespace HeartopiaMod
{
    // Measures whether the two Il2CppInterop ClassInjector hooks that look like dead code REALLY are.
    //
    // Background: of the 5 ClassInjector VM hooks that injection installs as inline detours inside
    // GameAssembly `.text`, two are pure pass-throughs unless a specific dictionary has entries:
    //
    //   * `Class::GetFieldDefaultValue`   gates on `EnumInjector.s_DefaultValueOverrides`
    //   * `GenericMethod::GetMethod`      gates on `ClassInjector.InflatedMethodFromContextDictionary`
    //
    // Both dictionaries stay empty unless an enum is registered, or an injected type declares a
    // generic instance method. A static census of every type injected in this process says neither
    // ever happens — but that census is a claim about code we do not own (MelonLoader's
    // `SM_Component` and `MonoEnumeratorWrapper`, Il2CppInterop's `Il2CppToMonoDelegateReference`),
    // and a wrong claim would fail LATE and QUIETLY. So measure it instead of believing it.
    //
    // ⭐ Why this stays useful forever, not just as a one-off check: the dictionaries are WRITTEN by
    // ClassInjector at type-REGISTRATION time and only READ by the hooks. Suppressing a hook
    // therefore does not blind this counter — it keeps reporting the true gate state even once the
    // hook is gone. That makes it the standing guard against a future MelonLoader update injecting a
    // type with a generic method, which is the one residual risk of trimming those hooks.
    //
    // Everything here is plain CoreCLR reflection over managed loader objects — no il2cpp, no Mono,
    // no game types. It runs from the world-ready gate (never a bare timer), and fails closed: any
    // field it cannot resolve is reported as unknown rather than assumed to be zero.
    internal static class InjectionGateCanary
    {
        internal const string WorldReadyCallbackName = "InjectionGateCanary";

        private static int lastInflatedGenerics = -1;
        private static int lastEnumOverrides = -1;
        private static int lastInjectedClasses = -1;
        private static bool warned;

        // Latest reading, for callers that want to carry it on their own summary line rather than
        // trigger another block of output.
        internal static string Counts = "not sampled";

        // World-ready callback contract: true = done for this world epoch, re-run on the next one.
        internal static bool SampleOnWorldReady()
        {
            Sample("world-ready");
            return true;
        }

        internal static void SampleOnShutdown()
        {
            Sample("shutdown");
        }

        // Taken right after the UGUI shell is BUILT (its first open) because that is the mod's
        // BIGGEST batch of managed→il2cpp delegate conversions — `onClick.AddListener` →
        // `DelegateSupport` → `RegisterTypeInIl2Cpp<Il2CppToMonoDelegateReference>` →
        // `InjectorHelpers.Setup()`.
        //
        // ⚠️ Not the only site, and no longer the whole story. Every managed→il2cpp delegate in this
        // mod now goes through `HookFreeDelegate.cs`, which builds the il2cpp delegate WITHOUT an
        // injected class — the UGUI kit (`TryWireUguiEvent` + `WireUguiClick`), the click listeners
        // `HeartopiaComplete.Farm.cs` puts on the game's own collect buttons, and the async
        // sprite-load callback in `HeartopiaComplete.GameIcons.cs`. Each keeps its original
        // `AddListener`/`ConvertDelegate` call as a fail-closed FALLBACK, so a reading of
        // `injectedClasses > 0` here means some wire fell back — check `[HookFreeDelegate]`'s
        // "fell back" count, which names it. The last two sites fire without the menu ever being
        // opened, so they are why a session could cross the threshold with nothing on screen.
        // Nothing in the ESP/radar overlay converts a delegate at all (retained-mode UGUI, no
        // callbacks), which is why the overlays were always free.
        internal static void SampleAfterShellBuilt()
        {
            Sample("ugui-shell-built");
        }

        // Silent unless something MOVED. All three sample points fire on a healthy session and the
        // numbers never change, so logging every reading buried the log in identical blocks. The
        // positive confirmation lives on one line in `[HookFreeDelegate] shell built: …` instead.
        private static void Sample(string when)
        {
            try
            {
                Assembly interop = ClassInjectorHookTrim.FindAssembly("Il2CppInterop.Runtime");
                if (interop == null)
                {
                    return;
                }

                int inflated = CountOfStaticCollection(interop, "Il2CppInterop.Runtime.Injection.ClassInjector", "InflatedMethodFromContextDictionary");
                int enums = CountOfStaticCollection(interop, "Il2CppInterop.Runtime.Injection.EnumInjector", "s_DefaultValueOverrides");
                int injected = CountOfStaticCollection(interop, "Il2CppInterop.Runtime.Injection.InjectorHelpers", "s_InjectedClasses");

                bool changed = inflated != lastInflatedGenerics
                               || enums != lastEnumOverrides
                               || injected != lastInjectedClasses;

                lastInflatedGenerics = inflated;
                lastEnumOverrides = enums;
                lastInjectedClasses = injected;

                Counts = "inflatedGenerics=" + Describe(inflated)
                         + " enumOverrides=" + Describe(enums)
                         + " injectedClasses=" + Describe(injected);

                if (changed)
                {
                    ModLogger.Msg("[InjectionGate] " + Counts + " (" + when + ")");

                    // Pair every reading with the hook states, so the log always shows the two
                    // together and a 0 → 3 transition cannot be misattributed to something else.
                    ClassInjectorHookTrim.Verify();
                }

                // Non-zero on either gated dictionary means the corresponding hook is NOT dead code in
                // this process, and suppressing it would be unsafe. Say so once, loudly — this is the
                // whole reason the counter exists.
                if (!warned && (inflated > 0 || enums > 0))
                {
                    warned = true;
                    ModLogger.Msg("[InjectionGate] ⚠ A GATED DICTIONARY IS NON-EMPTY — the matching "
                                  + "ClassInjector hook is live infrastructure here, NOT dead code. Do not "
                                  + "suppress it (inflatedGenerics gates GenericMethod::GetMethod, "
                                  + "enumOverrides gates Class::GetFieldDefaultValue).");
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[InjectionGate] failed: " + ex.Message);
            }
        }

        private static string Describe(int count)
        {
            return count < 0 ? "?" : count.ToString();
        }

        // -1 = could not resolve (unknown), never silently 0. Reading the field runs the declaring
        // type's static ctor, which is why this only ever runs at world-ready: by then ClassInjector
        // and friends are long since initialised, so nothing is being forced early.
        private static int CountOfStaticCollection(Assembly interop, string typeName, string fieldName)
        {
            try
            {
                Type t = interop.GetType(typeName, throwOnError: false);
                FieldInfo f = t?.GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                object value = f?.GetValue(null);
                if (value == null)
                {
                    return -1;
                }

                PropertyInfo count = value.GetType().GetProperty("Count", BindingFlags.Instance | BindingFlags.Public);
                return count?.GetValue(value) is int n ? n : -1;
            }
            catch
            {
                return -1;
            }
        }

    }
}
