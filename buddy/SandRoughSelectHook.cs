using System;
using System.Runtime.InteropServices;
using MonoMod.RuntimeDetour;

namespace HeartopiaMod
{
    // Keeps the vanilla "choose a model" step from ever showing while Auto Sand Sculpture runs.
    //
    // WHY: the auto loop is pure protocol (Start + Finish, no PreStart), so the sculpt mode, the
    // swing-QTE track and its camera never open. The one thing left on screen was the model
    // choice: when the server spawns our rough, SandSculptureRoughComponent.OnSpawned writes
    // PlayerDataComponent._sandRoughNetId, and on the next FSM tick TransitionFree2SelectSandRough
    // (IsSatisfy = PlayerStateSelectSandRough.EnableEnter()) moves the player into
    // PlayerStateSelectSandRough, whose OnStateEnter pushes a focus camera onto the rough and opens
    // DialogueSimplePanel. The mod picks the model itself, but the dialog flashed for up to a poll
    // interval and then had to be swept shut (the CloseDialog state).
    //
    // HOW: detour PlayerStateSelectSandRough.EnableEnter() (instance, no args, bool) to return false
    // while the loop runs. The Free -> SelectSandRough transition then never fires: no dialog, no
    // camera push, no idle pose. The reverse transition (TransitionSelectSandRough2Free) is
    // !EnableEnter(), so a player already in the state drops straight back to Free.
    //
    // Conditional passthrough, installed once and never torn down (memory:
    // native-detours-world-change-corruption): with the loop off the body forwards to the original
    // through the trampoline, so vanilla sculpting is byte-exact.
    public partial class HeartopiaComplete
    {
        private const string SandRoughSelectWorldReadyCallbackName = "SandRoughSelectHook";

        private static readonly string[] SandRoughSelectImageNames =
        {
            "XDTLevelAndEntity", "XDTLevelAndEntity.dll",
            "Client", "Client.dll"
        };

        // Written by ProcessSandRoughSelectHookOnUpdate (main thread); read by the native hook body.
        private static volatile bool sandRoughSelectSuppressActive;

        private bool sandRoughSelectCallbackRegistered;
        private bool sandRoughSelectHookTried;

        private static NativeDetour sandRoughSelectDetour;
        private static SandRoughEnableEnterHookDelegate sandRoughSelectKeepAlive; // anti-GC
        private static SandRoughEnableEnterHookDelegate sandRoughSelectTrampoline;

        // bool PlayerStateSelectSandRough.EnableEnter() -> byte(IntPtr self); mono returns bool in AL.
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate byte SandRoughEnableEnterHookDelegate(IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr SandRoughSelectCompileMethodDelegate(IntPtr method);

        // True once the detour is live and suppressing — the sculpt FSM then skips its
        // CloseDialog sweep, because no dialog can open.
        private static bool SandRoughSelectDialogSuppressed => sandRoughSelectSuppressActive;

        // Per-frame: mirrors the auto-loop toggle into the flag the hook reads, and registers the
        // install on the world-ready gate the first time the loop is switched on (a user who never
        // runs Auto Sand carries no extra hook).
        private void ProcessSandRoughSelectHookOnUpdate()
        {
            if (!this.autoSandEnabled)
            {
                sandRoughSelectSuppressActive = false;
                return;
            }

            if (sandRoughSelectTrampoline == null && !this.sandRoughSelectHookTried && !this.sandRoughSelectCallbackRegistered)
            {
                // Hook installs run on the world-ready gate, never from a retry timer here
                // (AGENTS.md §1 hard rule). A fresh registration runs on the current world.
                this.sandRoughSelectCallbackRegistered = true;
                this.RegisterWorldReadyCallback(SandRoughSelectWorldReadyCallbackName, this.TryInstallSandRoughSelectHookOnWorldReady);
            }

            sandRoughSelectSuppressActive = sandRoughSelectTrampoline != null;
        }

        // Returns true when settled for this world (installed, or permanently unavailable), false
        // to be retried by the gate.
        private bool TryInstallSandRoughSelectHookOnWorldReady()
        {
            if (this.sandRoughSelectHookTried || sandRoughSelectTrampoline != null)
            {
                return true;
            }

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    return false; // AuraMono not up yet — retry
                }

                IntPtr monoModule = this.GetAuraMonoModuleHandle();
                SandRoughSelectCompileMethodDelegate compile = monoModule != IntPtr.Zero
                    ? this.GetAuraMonoExport<SandRoughSelectCompileMethodDelegate>(monoModule, "mono_compile_method")
                    : null;
                if (compile == null)
                {
                    this.sandRoughSelectHookTried = true;
                    ModLogger.Msg("[SandSculpture] mono_compile_method unavailable — model dialog left vanilla.");
                    return true;
                }

                const string nameSpace = "XDTLevelAndEntity.Gameplay.Component.Player";
                const string shortName = "PlayerStateSelectSandRough";

                IntPtr cls = this.FindAuraMonoClassInImages(nameSpace, shortName, SandRoughSelectImageNames);
                if (cls == IntPtr.Zero)
                {
                    cls = this.FindAuraMonoClassByFullName(nameSpace + "." + shortName);
                }
                if (cls == IntPtr.Zero)
                {
                    return false; // image not loaded yet — retry
                }

                IntPtr method = this.FindAuraMonoMethodOnHierarchy(cls, "EnableEnter", 0);
                if (method == IntPtr.Zero)
                {
                    this.sandRoughSelectHookTried = true;
                    ModLogger.Msg("[SandSculpture] PlayerStateSelectSandRough.EnableEnter(0) not found — model dialog left vanilla (game update?).");
                    return true;
                }

                IntPtr nativePtr = compile(method);
                if (nativePtr == IntPtr.Zero)
                {
                    return false; // JIT entry unavailable — retry
                }

                this.sandRoughSelectHookTried = true;
                sandRoughSelectKeepAlive = SandRoughEnableEnterDetourBody;
                sandRoughSelectDetour = new NativeDetour(nativePtr, sandRoughSelectKeepAlive);
                sandRoughSelectTrampoline = sandRoughSelectDetour.GenerateTrampoline<SandRoughEnableEnterHookDelegate>();
                if (sandRoughSelectTrampoline == null)
                {
                    // Install rollback, not a live-detour teardown (the only case where Undo is safe).
                    try { sandRoughSelectDetour?.Undo(); } catch { }
                    sandRoughSelectDetour = null;
                    sandRoughSelectKeepAlive = null;
                    ModLogger.Msg("[SandSculpture] trampoline unavailable for EnableEnter; detour reverted — model dialog left vanilla.");
                    return true;
                }

                ModLogger.Msg("[SandSculpture] hooked PlayerStateSelectSandRough.EnableEnter @0x" + nativePtr.ToInt64().ToString("X")
                    + " — model dialog suppressed while Auto Sand runs.");
                return true;
            }
            catch (Exception ex)
            {
                this.sandRoughSelectHookTried = true;
                try { sandRoughSelectDetour?.Undo(); } catch { }
                sandRoughSelectDetour = null;
                sandRoughSelectKeepAlive = null;
                sandRoughSelectTrampoline = null;
                ModLogger.Msg("[SandSculpture] EnableEnter hook install failed: " + ex.Message + " — model dialog left vanilla.");
                return true;
            }
        }

        // Native->coreclr reverse-pinvoke body, hit every player-FSM tick while in Free. Allocation-
        // free, no Mono calls, no logging: one volatile read, then a constant or the stock call.
        private static byte SandRoughEnableEnterDetourBody(IntPtr self)
        {
            if (sandRoughSelectSuppressActive)
            {
                return 0; // never enter SelectSandRough: no dialog, no focus camera
            }

            SandRoughEnableEnterHookDelegate trampoline = sandRoughSelectTrampoline;
            return trampoline != null ? trampoline(self) : (byte)0;
        }
    }
}
