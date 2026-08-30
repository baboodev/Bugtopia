using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace HeartopiaMod
{
    // Keep Camera And HUD While Fishing — the cast no longer yanks the camera back to the buoy and
    // no longer swaps your HUD out for the fishing panel. Purely local presentation; nothing here
    // touches the network or the fishing state machine.
    //
    // TWO INDEPENDENT MECHANISMS, hence two groups of hooks.
    //
    // 1) THE CAMERA. PlayerStateFishing drives it through the thin wrappers in
    //    XDTLevelAndEntity.GameplaySystem.FishingUtil, which forward to FishingEvent, which
    //    PUSHES a camera controller onto XDTCameraManager:
    //      OnTransitEnter      -> SendCameraEventWaiting  -> FishingEvent.Waiting
    //                             -> PushController<FollowCameraController>(CameraControllerPath.Fishing)   <- the pull-back
    //      SetFishingBaitState -> SendCameraEventBaiting  -> PushController<ZoneFollowCameraController>(FishingBattle)
    //      SetBattleResult     -> SendCameraEventHookUp   -> PushController<FollowCameraController>(FishingHookUp)
    //      (manual aiming)     -> SendCameraEventAimAt / SendCameraEventUpdateAimAtTarget -> FishingAimAt
    //                          -> SendCameraEventShowFish -> ShowingCamera
    //    We suppress exactly the PUSH side. XDTCameraManager.PushController is generic on the
    //    controller type, so there is no single non-generic chokepoint below FishingUtil -- these
    //    six wrappers ARE the chokepoint, and they are the right layer because suppressing them
    //    leaves FishingEvent's own bookkeeping (_cameraControllerTokenId, _fishingIdleTarget,
    //    _fishingBattleTarget) untouched: nothing was pushed, so nothing needs popping.
    //
    //    DELIBERATELY NOT HOOKED: SendCameraEventQuitFishing, SendCameraEventHookUpEmpty and
    //    SendExitAnimAtTarget. Those only POP. Leaving them live is what makes the toggle safe to
    //    flip mid-session -- anything that did get pushed while the option was off is still torn
    //    down normally. PopController() merely queues a token id, so popping one that was never
    //    pushed is a no-op; vanilla already does exactly that (HookUpEmpty zeroes the token and a
    //    later QuitFishing pops the zero).
    //
    // 2) THE HUD -- TWO SEPARATE PANELS, and the important one is not on GameFishingMode at all.
    //
    //    2a) The MAIN HUD is StatusPanel, and it is owned by GameFreeMode, not by fishing. Entering
    //        any other mode makes GameBaseMode.IGameMode.OnLoseFocus() call
    //        GameFreeMode.OnModeLoseFocus(), which dispatches FreeModeHudVisibilityEvent{visible=false};
    //        UIEventBridge.OnFreeModeHudVisibility answers with CloseView<StatusPanel>() (or
    //        MicroHomelandStatusPanel on that level). THIS is what takes your interface away at the
    //        cast. Note GameFreeMode never dispatches GameModeLostFocusEvent, so the
    //        `case Free: CloseView<StatusPanel>()` arm in OnGameModeLostFocus is dead for this path
    //        -- OnModeLoseFocus is the single closer.
    //
    //        Suppressing it is safe for its own bookkeeping: _mainHudVisible simply stays true, and
    //        when Free regains focus OnModeFocus re-dispatches visible=true onto an already-open
    //        panel. But GameFreeMode.OnModeLoseFocus fires for EVERY mode you enter -- craft, drive,
    //        photo, pet -- so blanket-suppressing it would leave the main HUD stacked under those
    //        modes' own panels. Hence this one hook alone is gated on
    //        fishingCamHudSuppressFreeHudHide (a FISHING-CONTEXT window: an auto-fishing session is
    //        running, or the mod is about to cast), not on the plain feature flag.
    //
    //    2b) FishingPanel is the fishing-specific panel, opened by
    //        UIEventBridge.OnGameModeFocused (case Fishing) in answer to the
    //        GameModeFocusedEvent{Fishing} that GameFishingMode.OnModeFocus dispatches. It sits on
    //        UIPhysicalLayerType.Status -- the same physical layer as StatusPanel -- so it must be
    //        suppressed too, or it would just take the main HUD's place after we kept it. That
    //        method's whole body is the dispatch plus CheckTrackingPanel(visible:false) and it does
    //        not call base, so skipping it is self-contained; OnModeLoseFocus's
    //        CloseView<FishingPanel>() is then a no-op on a panel that never opened.
    //
    //    OnModeFocus is resolved with mono_class_get_method_from_name on GameFishingMode ITSELF,
    //    never through FindAuraMonoMethodOnHierarchy: that helper walks parents, and silently
    //    landing on the base GameMode.OnModeFocus would blank the focus handler for EVERY game mode.
    //
    // WHAT THIS COSTS: with the panel suppressed you also lose the fishing panel's own widgets --
    // the main-hold button and the rod durability readout. That is fine for the automated flow,
    // which drives the reel through PlayerStateFishing.SetStateButtonPressed /
    // FishingProtocolManager.FishingRodPull over AuraMono and never through the UI, but a player
    // fishing by hand with this on will not have the hold button. Hence: opt-in, off by default.
    //
    // DETOUR BODIES OBEY THE HARD RULE (see NotifyFloatInWaterDetourBody in
    // HeartopiaComplete.Fishing.cs, and the crash it cost us in StealthFishingFeature): a static
    // field read plus either an early return or the trampoline forward. No Mono API, no Unity, no
    // logging, no argument inspection. Every argument is forwarded verbatim as it arrived --
    // Entity is a class (pointer) and Vector3 is a 12-byte struct that Win64 passes by reference,
    // so IntPtr is byte-identical in both cases and we never dereference either.
    //
    // Installs on the world-ready gate; never Undo()n once live (memory:
    // native-detours-world-change-corruption) -- with the flag off every body is a pass-through,
    // and nothing is installed until the toggle is first switched on.
    public partial class HeartopiaComplete
    {
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void FishingCamHudVoid1Delegate(IntPtr a);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void FishingCamHudVoid2Delegate(IntPtr a, IntPtr b);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void FishingCamHudWaitingDelegate(IntPtr playerProxy, IntPtr floatTransform, IntPtr tarPos, float time);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr FishingCamHudCompileMethodDelegate(IntPtr method);

        // Slot indexes into the trampoline arrays. Each hooked method needs its own trampoline, so
        // each body is a closure over its slot; the arrays keep the lookup a plain array read.
        private const int FishingCamHudSlotShowFish = 0;
        private const int FishingCamHudSlotModeFocus = 1;
        private const int FishingCamHudSlotFreeHudHide = 2;
        private const int FishingCamHudVoid1Slots = 3;

        private const int FishingCamHudSlotBaiting = 0;
        private const int FishingCamHudSlotHookUp = 1;
        private const int FishingCamHudSlotAimAt = 2;
        private const int FishingCamHudSlotUpdateAimAt = 3;
        private const int FishingCamHudVoid2Slots = 4;

        // Read by the detour bodies. Static because the bodies must be.
        private static bool fishingCamHudActive;

        // Narrower gate, used ONLY by the GameFreeMode.OnModeLoseFocus hook: that method runs for
        // every mode transition out of Free, so it may only be suppressed while we are actually in
        // a fishing context. Recomputed once per frame on the main loop (below) -- the detour body
        // just reads the bool, it cannot call Unity.
        private static bool fishingCamHudSuppressFreeHudHide;

        private static readonly FishingCamHudVoid1Delegate[] fishingCamHudTramp1 =
            new FishingCamHudVoid1Delegate[FishingCamHudVoid1Slots];
        private static readonly FishingCamHudVoid2Delegate[] fishingCamHudTramp2 =
            new FishingCamHudVoid2Delegate[FishingCamHudVoid2Slots];
        private static FishingCamHudWaitingDelegate fishingCamHudTrampWaiting;

        // Anti-GC roots for the bodies handed to MonoMod, plus the detours themselves.
        private static readonly System.Collections.Generic.List<Delegate> fishingCamHudKeepAlive =
            new System.Collections.Generic.List<Delegate>();
        private static readonly System.Collections.Generic.List<MonoMod.RuntimeDetour.NativeDetour> fishingCamHudDetours =
            new System.Collections.Generic.List<MonoMod.RuntimeDetour.NativeDetour>();

        // Arming window for the cast the mod itself triggers, in case the mode transition does not
        // complete inside the EnterFishing call. Main-loop state; never read from a detour body.
        private const float FishingCamHudCastArmSeconds = 1.5f;
        private float fishingCamHudSuppressUntil;

        private bool fishingCamHudEnabled;
        private bool fishingCamHudCallbackRegistered;
        private bool fishingCamHudHookTried;
        private int fishingCamHudInstalledCount;

        // ---- public surface (UI + config) ------------------------------------------------------

        public bool GetFishingCameraHudKeepEnabled()
        {
            return this.fishingCamHudEnabled;
        }

        public void SetFishingCameraHudKeepEnabled(bool value)
        {
            if (this.fishingCamHudEnabled == value)
            {
                return;
            }

            this.fishingCamHudEnabled = value;
            fishingCamHudActive = value;
            FeatureLog.Toggle("FishingCamHud", value);

            if (value && this.fishingCamHudInstalledCount == 0)
            {
                FeatureLog.Life("FishingCamHud", "arming — hooks install on the next world-ready gate");
            }
        }

        // Called right before the mod hands the game an EnterFishing. GameBaseMode.IGameMode
        // .OnLoseFocus -> GameFreeMode.OnModeLoseFocus should run inside that call, but the mode
        // stack is a hierarchical state machine and may settle a frame later, so this opens a short
        // window rather than relying on a try/finally around the invoke.
        internal void ArmFishingCameraHudCastWindow()
        {
            if (!this.fishingCamHudEnabled)
            {
                return;
            }

            try
            {
                this.fishingCamHudSuppressUntil = Time.unscaledTime + FishingCamHudCastArmSeconds;
            }
            catch
            {
                return;
            }

            fishingCamHudSuppressFreeHudHide = true;
        }

        // ---- per-frame glue --------------------------------------------------------------------

        private void ProcessFishingCameraHudOnUpdate()
        {
            if (!this.fishingCamHudEnabled)
            {
                fishingCamHudActive = false;
                fishingCamHudSuppressFreeHudHide = false;
                return;
            }

            fishingCamHudActive = true;

            // The main-HUD hook's gate. GameFreeMode.OnModeLoseFocus runs for every transition out
            // of Free, so it is suppressed only inside a fishing context: an auto-fishing session is
            // running, or the mod armed a cast in the last FishingCamHudCastArmSeconds. Computed
            // here, on the main loop, because the detour body may not call Unity.
            bool inFishingContext;
            try
            {
                inFishingContext = AutoFishingFarm.IsEnabled
                    || Time.unscaledTime < this.fishingCamHudSuppressUntil;
            }
            catch
            {
                inFishingContext = false;
            }

            fishingCamHudSuppressFreeHudHide = inFishingContext;

            // Hook installs run on the world-ready gate, never on a retry timer here
            // (AGENTS.md §1 hard rule). Registration is idempotent and cheap.
            if (!this.fishingCamHudCallbackRegistered)
            {
                this.fishingCamHudCallbackRegistered = true;
                this.RegisterWorldReadyCallback("FishingCameraHud", this.TryInstallFishingCameraHudHooksOnWorldReady);
            }
        }

        // ---- install ---------------------------------------------------------------------------

        private bool TryInstallFishingCameraHudHooksOnWorldReady()
        {
            if (this.fishingCamHudHookTried || this.fishingCamHudInstalledCount > 0)
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
                FishingCamHudCompileMethodDelegate compile = monoModule != IntPtr.Zero
                    ? this.GetAuraMonoExport<FishingCamHudCompileMethodDelegate>(monoModule, "mono_compile_method")
                    : null;
                if (compile == null)
                {
                    this.fishingCamHudHookTried = true;
                    FeatureLog.Fail("FishingCamHud", "mono_compile_method unavailable — feature off.");
                    return true;
                }

                // NOT XDT.Scene.Shared.GamePlay.Fishing.FishingUtil — that is a different class of
                // the same short name (fish AI maths). The camera wrappers live in GameplaySystem.
                IntPtr utilClass = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.GameplaySystem.FishingUtil");
                IntPtr modeClass = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.Game.GameMode.GameFishingMode");
                IntPtr freeModeClass = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.Game.GameMode.GameFreeMode");
                if (utilClass == IntPtr.Zero || modeClass == IntPtr.Zero || freeModeClass == IntPtr.Zero)
                {
                    return false; // images not loaded yet — retry
                }

                int installed = 0;

                installed += this.TryHookFishingCamVoid1(compile, utilClass, "SendCameraEventShowFish", 1,
                    FishingCamHudSlotShowFish, hierarchy: true) ? 1 : 0;
                installed += this.TryHookFishingCamVoid1(compile, modeClass, "OnModeFocus", 0,
                    FishingCamHudSlotModeFocus, hierarchy: false) ? 1 : 0;
                installed += this.TryHookFishingCamVoid1(compile, freeModeClass, "OnModeLoseFocus", 0,
                    FishingCamHudSlotFreeHudHide, hierarchy: false, gateOnFishingContext: true) ? 1 : 0;

                installed += this.TryHookFishingCamVoid2(compile, utilClass, "SendCameraEventBaiting",
                    FishingCamHudSlotBaiting) ? 1 : 0;
                installed += this.TryHookFishingCamVoid2(compile, utilClass, "SendCameraEventHookUp",
                    FishingCamHudSlotHookUp) ? 1 : 0;
                installed += this.TryHookFishingCamVoid2(compile, utilClass, "SendCameraEventAimAt",
                    FishingCamHudSlotAimAt) ? 1 : 0;
                installed += this.TryHookFishingCamVoid2(compile, utilClass, "SendCameraEventUpdateAimAtTarget",
                    FishingCamHudSlotUpdateAimAt) ? 1 : 0;

                installed += this.TryHookFishingCamWaiting(compile, utilClass) ? 1 : 0;

                this.fishingCamHudHookTried = true;
                this.fishingCamHudInstalledCount = installed;

                if (installed == 0)
                {
                    FeatureLog.Fail("FishingCamHud", "no fishing camera/HUD methods could be hooked — feature off (game update?).");
                    return true;
                }

                FeatureLog.Life("FishingCamHud", "installed " + installed + "/8 hooks"
                    + (installed < 8 ? " — the missing ones stay vanilla" : string.Empty));
                return true;
            }
            catch (Exception ex)
            {
                this.fishingCamHudHookTried = true;
                FeatureLog.Fail("FishingCamHud", "hook install failed: " + ex.Message + " — feature off.");
                return true;
            }
        }

        // hierarchy:false resolves on the declaring class ONLY. Mandatory for the virtual
        // OnModeFocus override: walking parents would hand back GameMode.OnModeFocus and suppress
        // the focus handler of every game mode in the game, not just fishing.
        private bool TryHookFishingCamVoid1(FishingCamHudCompileMethodDelegate compile, IntPtr klass,
            string methodName, int paramCount, int slot, bool hierarchy, bool gateOnFishingContext = false)
        {
            IntPtr method = hierarchy
                ? this.FindAuraMonoMethodOnHierarchy(klass, methodName, paramCount)
                : (auraMonoClassGetMethodFromName != null
                    ? auraMonoClassGetMethodFromName(klass, methodName, paramCount)
                    : IntPtr.Zero);
            IntPtr nativePtr = this.CompileFishingCamHudMethod(compile, method, methodName);
            if (nativePtr == IntPtr.Zero)
            {
                return false;
            }

            int slotCopy = slot;
            bool narrowGate = gateOnFishingContext;
            FishingCamHudVoid1Delegate body = delegate (IntPtr a)
            {
                // Two static-bool reads and a captured constant — still nothing but field reads.
                if (narrowGate ? fishingCamHudSuppressFreeHudHide : fishingCamHudActive)
                {
                    return;
                }

                FishingCamHudVoid1Delegate orig = fishingCamHudTramp1[slotCopy];
                if (orig != null)
                {
                    orig(a);
                }
            };

            return this.ApplyFishingCamHudDetour(nativePtr, methodName, body,
                d => fishingCamHudTramp1[slotCopy] = d.GenerateTrampoline<FishingCamHudVoid1Delegate>(),
                () => fishingCamHudTramp1[slotCopy] != null,
                () => fishingCamHudTramp1[slotCopy] = null);
        }

        private bool TryHookFishingCamVoid2(FishingCamHudCompileMethodDelegate compile, IntPtr klass,
            string methodName, int slot)
        {
            IntPtr method = this.FindAuraMonoMethodOnHierarchy(klass, methodName, 2);
            IntPtr nativePtr = this.CompileFishingCamHudMethod(compile, method, methodName);
            if (nativePtr == IntPtr.Zero)
            {
                return false;
            }

            int slotCopy = slot;
            FishingCamHudVoid2Delegate body = delegate (IntPtr a, IntPtr b)
            {
                if (fishingCamHudActive)
                {
                    return;
                }

                FishingCamHudVoid2Delegate orig = fishingCamHudTramp2[slotCopy];
                if (orig != null)
                {
                    orig(a, b);
                }
            };

            return this.ApplyFishingCamHudDetour(nativePtr, methodName, body,
                d => fishingCamHudTramp2[slotCopy] = d.GenerateTrampoline<FishingCamHudVoid2Delegate>(),
                () => fishingCamHudTramp2[slotCopy] != null,
                () => fishingCamHudTramp2[slotCopy] = null);
        }

        private bool TryHookFishingCamWaiting(FishingCamHudCompileMethodDelegate compile, IntPtr klass)
        {
            const string methodName = "SendCameraEventWaiting";
            IntPtr method = this.FindAuraMonoMethodOnHierarchy(klass, methodName, 4);
            IntPtr nativePtr = this.CompileFishingCamHudMethod(compile, method, methodName);
            if (nativePtr == IntPtr.Zero)
            {
                return false;
            }

            FishingCamHudWaitingDelegate body = delegate (IntPtr playerProxy, IntPtr floatTransform, IntPtr tarPos, float time)
            {
                if (fishingCamHudActive)
                {
                    return;
                }

                FishingCamHudWaitingDelegate orig = fishingCamHudTrampWaiting;
                if (orig != null)
                {
                    orig(playerProxy, floatTransform, tarPos, time);
                }
            };

            return this.ApplyFishingCamHudDetour(nativePtr, methodName, body,
                d => fishingCamHudTrampWaiting = d.GenerateTrampoline<FishingCamHudWaitingDelegate>(),
                () => fishingCamHudTrampWaiting != null,
                () => fishingCamHudTrampWaiting = null);
        }

        private IntPtr CompileFishingCamHudMethod(FishingCamHudCompileMethodDelegate compile, IntPtr method, string methodName)
        {
            if (method == IntPtr.Zero)
            {
                FeatureLog.Fail("FishingCamHud", methodName + " not found — that one stays vanilla (game update?).");
                return IntPtr.Zero;
            }

            IntPtr nativePtr = compile(method);
            if (nativePtr == IntPtr.Zero)
            {
                FeatureLog.Fail("FishingCamHud", "mono_compile_method returned null for " + methodName + " — that one stays vanilla.");
            }

            return nativePtr;
        }

        // Shared apply + rollback. A hook whose trampoline cannot be generated is reverted rather
        // than left in place: without a way to call the original it would swallow the call forever,
        // which with the toggle OFF would silently break vanilla fishing.
        private bool ApplyFishingCamHudDetour(IntPtr nativePtr, string methodName, Delegate body,
            Action<MonoMod.RuntimeDetour.NativeDetour> generateTrampoline, Func<bool> hasTrampoline, Action clearTrampoline)
        {
            MonoMod.RuntimeDetour.NativeDetour detour = null;
            try
            {
                fishingCamHudKeepAlive.Add(body);
                detour = new MonoMod.RuntimeDetour.NativeDetour(nativePtr, body);
                generateTrampoline(detour);
                if (!hasTrampoline())
                {
                    detour.Undo(); // install rollback, not a live-detour teardown
                    fishingCamHudKeepAlive.Remove(body);
                    FeatureLog.Fail("FishingCamHud", "trampoline unavailable for " + methodName + "; detour reverted.");
                    return false;
                }

                fishingCamHudDetours.Add(detour);
                return true;
            }
            catch (Exception ex)
            {
                try { detour?.Undo(); } catch { }
                clearTrampoline();
                fishingCamHudKeepAlive.Remove(body);
                FeatureLog.Fail("FishingCamHud", "hooking " + methodName + " failed: " + ex.Message);
                return false;
            }
        }
    }
}
