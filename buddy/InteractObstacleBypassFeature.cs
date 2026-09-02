using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace HeartopiaMod
{
    // Interaction-area obstacle bypass — kills the client-side check behind the toast
    // "Obstacles in the interaction area!" (zhHans 交互区域存在障碍物！, ru "В зоне взаимодействия
    // есть препятствие!").
    //
    // WHERE THE LIMIT COMES FROM (all client-side; the server never sees the refusal):
    //
    //   * The toast text is Localization id 91525, and that id IS the error code:
    //     InteractErrorCode.InteractAreaUnSafe = 91525 (InteractErrorCode.cs). InteractSystem
    //     dispatches UITipEvent{ tipId = (int)code } and UIEventBridge turns it into
    //     TipDecorator.Toast(TableData.GetLocalizationText(tipId)).
    //
    //   * Prefab authoring: a LevelObject may carry help points named interact_collision_safe ..
    //     interact_collision_safe5 (LevelObjectHelpPointName) — "this is where the character will
    //     stand". The gate tests that spot with a capsule the size of the player:
    //
    //       ActorMovementComponent.CheckAreaCollisionSafe(pos, radius, out _, ignoreCondition)
    //         -> MovementComponent.CheckAreaCollisionSafe
    //              CheckHasWalkableBelow            : SphereCast down 500 m, must find ground
    //              LevelLayerManager.CheckPlayerCollisionSafe
    //                                               : OverlapCapsule(pos+r*up .. pos+(h-r)*up, r,
    //                                                 playerCollisionLayer) — ANY hit that is not
    //                                                 the player's own controller, not a trigger and
    //                                                 not ignored => not safe
    //         radius = InteractSystem.InteractCollisionSafeRadius (0.3 m default, overwritten from
    //         LevelScriptableConfig.interactionConfigPC.interactCollisionSafeRadius)
    //         ignoreCondition = levelObject.IsOwnerHandleCollision — colliders belonging to the
    //         interaction target itself do not count. Everything else does: a chair pushed against
    //         the stove, a UGC prop, a pet standing there.
    //
    //   * Three producers return 91525, and the first two both fire for one click:
    //       1. LocalPlayerComponent.CanExecuteInteraction(InteractCommand)  — the generic gate,
    //          run by InteractSystem before ANY HasTargetCommand; walks every interact slot whose
    //          key starts with "interact_collision_safe". Skipped only for CommandId 21 (Repair).
    //       2. InteractWithCookerCommand.IsExecutable(in ev)                — cooking pots/stoves,
    //          re-tests the slot on the parent LevelObject (and also returns 91525 when the target
    //          carries no CookingComponent).
    //       3. PressurePadTriggerCommand.IsExecutable()                     — UGC springboards.
    //
    // LEVER — one Mono NativeDetour per producer, each one calling the ORIGINAL through its
    // trampoline and rewriting ONLY the 91525 answer to 0 (Success). Deliberately not a constant
    // hook: CanExecuteInteraction is also where stamina, bag-full, invalid-target and
    // build-in-building are decided, and returning a blanket 0 there would silently disable all of
    // them. Remapping one code keeps every other refusal — and every other toast — intact.
    //
    // Calling the original from the callback is the established shape in this codebase (the
    // EventCenter dispatch detour, the map-spot getters, Instant Teleport, Direct Craft Send). It
    // is NOT the shape BuildingFreeRotate warns about: that crash came from re-running heavy
    // placement logic on top of the original every placing tick. Here the body does one forwarded
    // call — exactly the work vanilla would have done — an integer compare, and a counter bump.
    //
    // The detours are installed lazily behind the world-ready gate the first time the toggle is on,
    // and are never torn down (memory: native-detours-world-change-corruption). Turning the toggle
    // off clears interactObstacleBypassActive, so every body forwards and the result is byte-exact
    // vanilla; a user who never touches the toggle carries no added surface at all.
    //
    // Scope: this lifts a CLIENT gate only. The interaction itself still travels the game's own
    // command path and the server still validates it — the bypass cannot conjure an interaction the
    // server would refuse, it only stops the client refusing one before it is ever sent.
    public partial class HeartopiaComplete
    {
        // InteractErrorCode.InteractAreaUnSafe — the code AND the localization id of the toast.
        private const int InteractObstacleUnsafeCode = 91525;

        private static readonly string[] InteractObstacleImageNames =
        {
            "XDTLevelAndEntity", "XDTLevelAndEntity.dll",
        };

        internal static bool MasterLogInteractObstacle = false;

        // int(self, arg) — CanExecuteInteraction(InteractCommand) and IsExecutable(in TEvent).
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int InteractObstacleArg1HookDelegate(IntPtr self, IntPtr arg);

        // int(self) — HasTargetCommand.IsExecutable().
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate int InteractObstacleArg0HookDelegate(IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr InteractObstacleCompileMethodDelegate(IntPtr method);

        private static MonoMod.RuntimeDetour.NativeDetour interactObstacleCanExecuteDetour;
        private static InteractObstacleArg1HookDelegate interactObstacleCanExecuteKeepAlive; // anti-GC
        private static InteractObstacleArg1HookDelegate interactObstacleCanExecuteTrampoline;

        private static MonoMod.RuntimeDetour.NativeDetour interactObstacleCookerDetour;
        private static InteractObstacleArg1HookDelegate interactObstacleCookerKeepAlive; // anti-GC
        private static InteractObstacleArg1HookDelegate interactObstacleCookerTrampoline;

        private static MonoMod.RuntimeDetour.NativeDetour interactObstaclePadDetour;
        private static InteractObstacleArg0HookDelegate interactObstaclePadKeepAlive; // anti-GC
        private static InteractObstacleArg0HookDelegate interactObstaclePadTrampoline;

        // Written on the main thread, read by the native bodies. Only true while the toggle is on
        // AND at least one detour is live.
        private static volatile bool interactObstacleBypassActive;

        // Bumped from the native bodies (Interlocked only — no allocation, no logging in there).
        private static int interactObstacleClearedCount;

        private bool interactObstacleBypassEnabled;
        private bool interactObstacleCallbackRegistered;
        private bool interactObstacleCanExecuteTried;
        private bool interactObstacleCookerTried;
        private bool interactObstaclePadTried;
        private int interactObstacleReportedCount;
        private string interactObstacleStatus = "Idle.";
        private string interactObstacleLastLoggedStatus;

        private void ProcessInteractObstacleBypassOnUpdate()
        {
            if (!this.interactObstacleBypassEnabled)
            {
                // Never Undo()n: tearing a live native detour down mid-session is a documented
                // heap-corruption source (memory: native-detours-world-change-corruption). An
                // inert hook just forwards to the trampoline = vanilla.
                interactObstacleBypassActive = false;
                return;
            }

            // Hook installs run on the world-ready gate, never from a retry timer here
            // (AGENTS.md hard rule). Registration is idempotent and cheap.
            if (!this.interactObstacleCallbackRegistered)
            {
                this.interactObstacleCallbackRegistered = true;
                this.RegisterWorldReadyCallback("InteractObstacleBypass",
                    this.TryInstallInteractObstacleHooksOnWorldReady);
            }

            interactObstacleBypassActive = interactObstacleCanExecuteTrampoline != null
                || interactObstacleCookerTrampoline != null
                || interactObstaclePadTrampoline != null;

            int cleared = Volatile.Read(ref interactObstacleClearedCount);
            if (cleared != this.interactObstacleReportedCount)
            {
                this.interactObstacleReportedCount = cleared;
                this.InteractObstacleSetStatus("Cleared " + cleared + " interaction-area block(s).");
            }
        }

        // World-ready callback: true when there is nothing left to do for this world (every target
        // either hooked or permanently disarmed), false to be retried.
        private bool TryInstallInteractObstacleHooksOnWorldReady()
        {
            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    return false; // AuraMono not up yet — retry
                }

                IntPtr monoModule = this.GetAuraMonoModuleHandle();
                InteractObstacleCompileMethodDelegate compile = monoModule != IntPtr.Zero
                    ? this.GetAuraMonoExport<InteractObstacleCompileMethodDelegate>(monoModule, "mono_compile_method")
                    : null;
                if (compile == null)
                {
                    this.interactObstacleCanExecuteTried = true;
                    this.interactObstacleCookerTried = true;
                    this.interactObstaclePadTried = true;
                    ModLogger.Msg("[InteractObstacle] mono_compile_method unavailable — bypass off.");
                    return true;
                }

                bool done = this.TryInstallInteractObstacleCanExecuteHook(compile);
                done &= this.TryInstallInteractObstacleCookerHook(compile);
                done &= this.TryInstallInteractObstaclePadHook(compile);
                return done;
            }
            catch (Exception ex)
            {
                this.interactObstacleCanExecuteTried = true;
                this.interactObstacleCookerTried = true;
                this.interactObstaclePadTried = true;
                ModLogger.Msg("[InteractObstacle] hook install failed: " + ex.Message + " — bypass off.");
                return true;
            }
        }

        // The generic gate — LocalPlayerComponent.CanExecuteInteraction(InteractCommand). This is
        // the one that matters: it runs for every targeted interaction in the game.
        private bool TryInstallInteractObstacleCanExecuteHook(InteractObstacleCompileMethodDelegate compile)
        {
            if (interactObstacleCanExecuteTrampoline != null || this.interactObstacleCanExecuteTried)
            {
                return true;
            }

            IntPtr nativePtr = this.TryCompileInteractObstacleMethod(compile,
                "XDTLevelAndEntity.Gameplay.Component.Player", "LocalPlayerComponent",
                "CanExecuteInteraction", 1, ref this.interactObstacleCanExecuteTried);
            if (nativePtr == IntPtr.Zero)
            {
                return this.interactObstacleCanExecuteTried;
            }

            try
            {
                interactObstacleCanExecuteKeepAlive = InteractObstacleCanExecuteDetourBody;
                interactObstacleCanExecuteDetour =
                    new MonoMod.RuntimeDetour.NativeDetour(nativePtr, interactObstacleCanExecuteKeepAlive);
                interactObstacleCanExecuteTrampoline =
                    interactObstacleCanExecuteDetour.GenerateTrampoline<InteractObstacleArg1HookDelegate>();
                if (interactObstacleCanExecuteTrampoline == null)
                {
                    // Install rollback, not a live-detour teardown — the only case where Undo is safe.
                    try { interactObstacleCanExecuteDetour?.Undo(); } catch { }
                    interactObstacleCanExecuteDetour = null;
                    interactObstacleCanExecuteKeepAlive = null;
                    this.interactObstacleCanExecuteTried = true;
                    ModLogger.Msg("[InteractObstacle] trampoline unavailable for CanExecuteInteraction;"
                        + " detour reverted — generic gate stays vanilla.");
                    return true;
                }

                this.interactObstacleCanExecuteTried = true;
                ModLogger.Msg("[InteractObstacle] hooked LocalPlayerComponent.CanExecuteInteraction @0x"
                    + nativePtr.ToInt64().ToString("X"));
                return true;
            }
            catch (Exception ex)
            {
                try { interactObstacleCanExecuteDetour?.Undo(); } catch { }
                interactObstacleCanExecuteDetour = null;
                interactObstacleCanExecuteKeepAlive = null;
                interactObstacleCanExecuteTrampoline = null;
                this.interactObstacleCanExecuteTried = true;
                ModLogger.Msg("[InteractObstacle] CanExecuteInteraction hook failed: " + ex.Message);
                return true;
            }
        }

        // Cooking pots and stoves re-run the same slot test inside their own command.
        private bool TryInstallInteractObstacleCookerHook(InteractObstacleCompileMethodDelegate compile)
        {
            if (interactObstacleCookerTrampoline != null || this.interactObstacleCookerTried)
            {
                return true;
            }

            IntPtr nativePtr = this.TryCompileInteractObstacleMethod(compile,
                "XDTLevelAndEntity.Gameplay.Interaction", "InteractWithCookerCommand",
                "IsExecutable", 1, ref this.interactObstacleCookerTried);
            if (nativePtr == IntPtr.Zero)
            {
                return this.interactObstacleCookerTried;
            }

            try
            {
                interactObstacleCookerKeepAlive = InteractObstacleCookerDetourBody;
                interactObstacleCookerDetour =
                    new MonoMod.RuntimeDetour.NativeDetour(nativePtr, interactObstacleCookerKeepAlive);
                interactObstacleCookerTrampoline =
                    interactObstacleCookerDetour.GenerateTrampoline<InteractObstacleArg1HookDelegate>();
                if (interactObstacleCookerTrampoline == null)
                {
                    try { interactObstacleCookerDetour?.Undo(); } catch { }
                    interactObstacleCookerDetour = null;
                    interactObstacleCookerKeepAlive = null;
                    this.interactObstacleCookerTried = true;
                    ModLogger.Msg("[InteractObstacle] trampoline unavailable for InteractWithCookerCommand"
                        + ".IsExecutable; detour reverted — cookers stay vanilla.");
                    return true;
                }

                this.interactObstacleCookerTried = true;
                ModLogger.Msg("[InteractObstacle] hooked InteractWithCookerCommand.IsExecutable @0x"
                    + nativePtr.ToInt64().ToString("X"));
                return true;
            }
            catch (Exception ex)
            {
                try { interactObstacleCookerDetour?.Undo(); } catch { }
                interactObstacleCookerDetour = null;
                interactObstacleCookerKeepAlive = null;
                interactObstacleCookerTrampoline = null;
                this.interactObstacleCookerTried = true;
                ModLogger.Msg("[InteractObstacle] InteractWithCookerCommand.IsExecutable hook failed: " + ex.Message);
                return true;
            }
        }

        // UGC springboards (pressure pads) — same test, zero-argument override.
        private bool TryInstallInteractObstaclePadHook(InteractObstacleCompileMethodDelegate compile)
        {
            if (interactObstaclePadTrampoline != null || this.interactObstaclePadTried)
            {
                return true;
            }

            IntPtr nativePtr = this.TryCompileInteractObstacleMethod(compile,
                "XDTLevelAndEntity.Gameplay.Interaction.Command", "PressurePadTriggerCommand",
                "IsExecutable", 0, ref this.interactObstaclePadTried);
            if (nativePtr == IntPtr.Zero)
            {
                return this.interactObstaclePadTried;
            }

            try
            {
                interactObstaclePadKeepAlive = InteractObstaclePadDetourBody;
                interactObstaclePadDetour =
                    new MonoMod.RuntimeDetour.NativeDetour(nativePtr, interactObstaclePadKeepAlive);
                interactObstaclePadTrampoline =
                    interactObstaclePadDetour.GenerateTrampoline<InteractObstacleArg0HookDelegate>();
                if (interactObstaclePadTrampoline == null)
                {
                    try { interactObstaclePadDetour?.Undo(); } catch { }
                    interactObstaclePadDetour = null;
                    interactObstaclePadKeepAlive = null;
                    this.interactObstaclePadTried = true;
                    ModLogger.Msg("[InteractObstacle] trampoline unavailable for PressurePadTriggerCommand"
                        + ".IsExecutable; detour reverted — springboards stay vanilla.");
                    return true;
                }

                this.interactObstaclePadTried = true;
                ModLogger.Msg("[InteractObstacle] hooked PressurePadTriggerCommand.IsExecutable @0x"
                    + nativePtr.ToInt64().ToString("X"));
                return true;
            }
            catch (Exception ex)
            {
                try { interactObstaclePadDetour?.Undo(); } catch { }
                interactObstaclePadDetour = null;
                interactObstaclePadKeepAlive = null;
                interactObstaclePadTrampoline = null;
                this.interactObstaclePadTried = true;
                ModLogger.Msg("[InteractObstacle] PressurePadTriggerCommand.IsExecutable hook failed: " + ex.Message);
                return true;
            }
        }

        // Class + method resolve + JIT entry point. Returns Zero and leaves `tried` false when the
        // image simply is not up yet (retry); sets `tried` when the target is gone for good, so a
        // game update disarms that one hook with a log line instead of retrying forever.
        private IntPtr TryCompileInteractObstacleMethod(InteractObstacleCompileMethodDelegate compile,
            string nameSpace, string shortName, string methodName, int paramCount, ref bool tried)
        {
            IntPtr cls = this.FindAuraMonoClassInImages(nameSpace, shortName, InteractObstacleImageNames);
            if (cls == IntPtr.Zero)
            {
                cls = this.FindAuraMonoClassByFullName(nameSpace + "." + shortName);
            }
            if (cls == IntPtr.Zero)
            {
                return IntPtr.Zero; // image not loaded yet — retry
            }

            IntPtr method = this.FindAuraMonoMethodOnHierarchy(cls, methodName, paramCount);
            if (method == IntPtr.Zero)
            {
                tried = true;
                ModLogger.Msg("[InteractObstacle] " + shortName + "." + methodName + "(" + paramCount
                    + ") not found — that gate stays vanilla (game update?).");
                return IntPtr.Zero;
            }

            IntPtr nativePtr = compile(method);
            if (nativePtr == IntPtr.Zero)
            {
                tried = true;
                ModLogger.Msg("[InteractObstacle] mono_compile_method returned null for " + shortName + "."
                    + methodName + " — that gate stays vanilla.");
            }

            return nativePtr;
        }

        // Native->coreclr reverse-pinvoke bodies. Each one forwards to the original (exactly the
        // work vanilla does), compares one integer and, at most, bumps a counter. No allocation, no
        // logging, no game-Mono call of our own — the trampoline IS the game's own call.
        private static int InteractObstacleCanExecuteDetourBody(IntPtr self, IntPtr command)
        {
            InteractObstacleArg1HookDelegate trampoline = interactObstacleCanExecuteTrampoline;
            int code = trampoline != null ? trampoline(self, command) : 0;
            return InteractObstacleRewrite(code);
        }

        private static int InteractObstacleCookerDetourBody(IntPtr self, IntPtr arg)
        {
            InteractObstacleArg1HookDelegate trampoline = interactObstacleCookerTrampoline;
            int code = trampoline != null ? trampoline(self, arg) : 0;
            return InteractObstacleRewrite(code);
        }

        private static int InteractObstaclePadDetourBody(IntPtr self)
        {
            InteractObstacleArg0HookDelegate trampoline = interactObstaclePadTrampoline;
            int code = trampoline != null ? trampoline(self) : 0;
            return InteractObstacleRewrite(code);
        }

        // 91525 -> 0 (Success) while armed; every other code, including every other refusal the
        // same method makes, is passed through untouched.
        private static int InteractObstacleRewrite(int code)
        {
            if (code != InteractObstacleUnsafeCode || !interactObstacleBypassActive)
            {
                return code;
            }

            Interlocked.Increment(ref interactObstacleClearedCount);
            return 0;
        }

        private void InteractObstacleSetStatus(string status)
        {
            this.interactObstacleStatus = status;
            if (string.Equals(this.interactObstacleLastLoggedStatus, status, StringComparison.Ordinal))
            {
                return;
            }

            this.interactObstacleLastLoggedStatus = status;
            if (MasterLogInteractObstacle || status.StartsWith("Error", StringComparison.Ordinal))
            {
                ModLogger.Msg("[InteractObstacle] " + status);
            }
        }
    }
}
