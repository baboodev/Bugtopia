#if FEATURE_MCP
using System;
using System.Reflection;

namespace HeartopiaMod
{
    // ============================================================================================
    // "Did the game QUIT, or was it KILLED?" — the one question crash attribution rests on.
    //
    // The obvious answer was `AppDomain.CurrentDomain.ProcessExit`. MEASURED 2026-08-15: it does not
    // run when this game quits normally. Unity tears the process down without letting CoreCLR shut
    // down, so a clean exit is indistinguishable from a native fault — and anything built on that
    // event silently never fires. (`PlayerLoopProbe` hangs its teardown there too; harmlessly, since
    // it only nulls slots the dying process no longer needs.) WER dump correlation is no fallback
    // either: this game leaves no dumps in the standard folder.
    //
    // What remains is Unity's own `Application.quitting`, which fires on an orderly shutdown and
    // cannot fire when the process is killed. Subscribing to it needs an il2cpp delegate, and the
    // ordinary route — `DelegateSupport.ConvertDelegate` — drags in ClassInjector and its five
    // GameAssembly `.text` detours, the exact surface this mod spent so much effort removing. So it
    // goes through HookFreeDelegate instead, which builds the same delegate with no injection.
    //
    // FAIL-CLOSED: if any step fails the flag stays false, and the forensics falls back to treating
    // a resident record as context rather than evidence. A quit signal that might not be there is
    // worse than none, because it would quarantine innocent plugins after every ordinary exit.
    // ============================================================================================
    internal static class McpQuitSignal
    {
        internal static bool Armed;
        internal static string Status = "not installed";
        internal static bool Quitting;

        internal static bool Install()
        {
            if (Armed)
            {
                return true;
            }

            try
            {
                MethodInfo add = typeof(UnityEngine.Application).GetMethod(
                    "add_quitting", BindingFlags.Public | BindingFlags.Static);
                if (add == null)
                {
                    Status = "UnityEngine.Application has no add_quitting in this interop build";
                    return Fail();
                }

                ParameterInfo[] parameters = add.GetParameters();
                if (parameters.Length != 1)
                {
                    Status = "add_quitting takes " + parameters.Length + " parameters, expected 1";
                    return Fail();
                }

                // The delegate type is READ FROM THE SIGNATURE, never assumed to be
                // Il2CppSystem.Action: if the interop generator ever names it differently, this
                // fails cleanly instead of building a delegate of the wrong shape and corrupting
                // the call.
                Type delegateType = parameters[0].ParameterType;
                object il2cppDelegate = HookFreeDelegate.ForVoidOfType(delegateType, OnQuitting);
                if (il2cppDelegate == null)
                {
                    Status = "could not build a hook-free " + delegateType.Name;
                    return Fail();
                }

                add.Invoke(null, new object[] { il2cppDelegate });
                Armed = true;
                Status = "subscribed to Application.quitting via " + delegateType.Name;
                ModLogger.Msg("[Mcp] quit signal: " + Status + " — a resident-plugin record surviving "
                              + "to the next launch now means the process was KILLED, not closed.");
                return true;
            }
            catch (Exception ex)
            {
                Status = ex.GetType().Name + ": " + ex.Message;
                return Fail();
            }
        }

        private static bool Fail()
        {
            Armed = false;
            ModLogger.Warning("[Mcp] quit signal unavailable (" + Status
                + ") — resident-plugin records stay informational, not crash evidence.");
            return false;
        }

        // Runs on Unity's shutdown path, from a native callback: nothing may escape it.
        private static void OnQuitting()
        {
            try
            {
                Quitting = true;
                McpForensics.ClearResident();
                ModLogger.Msg("[Mcp] clean shutdown — resident-plugin record cleared.");
            }
            catch
            {
            }
        }
    }
}
#endif
