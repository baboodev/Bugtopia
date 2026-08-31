using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace HeartopiaMod
{
    // Keep Fishing Camera — the cast no longer yanks the camera back to the buoy and hold it there
    // through bite and hook-up. Purely local presentation; nothing here touches the network or the
    // fishing state machine.
    //
    // CAMERA ONLY. The HUD belongs to PersistentHudFeature ("Keep HUD in fishing/vehicle modes") —
    // see the note in section 2 below before adding anything HUD-shaped here.
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
    //    ⚠️ HOOK SURFACE IS DELIBERATELY MINIMAL — it cost a process abort once (dump
    //    xdt.exe.35796, 2026-08-31, 0xC0000409 with RCX=7 = FATAL_APP_EXIT inside
    //    mono-2.0-sgen.dll). The crashed run's last log line was an UNHANDLED
    //    System.NullReferenceException thrown by the game itself inside
    //    FishingUtil.SendCameraEventShowFish -> FishingEvent.ShowFish -> MultipleTargets.Init ->
    //    TargetEntityGroup.GetColliders -> Entity.get_IsVisible, i.e. a stale playerProxy after the
    //    build tab churned entities. In vanilla Unity logs such an escape and the game survives;
    //    here it had to unwind through our CoreCLR detour frame, which Mono's unwinder cannot walk,
    //    so the runtime aborted. EVERY hook here carries that hazard, because all of these build
    //    TargetEntityGroup/MultipleTargets over entities that can die. So hook only what the
    //    feature needs: SendCameraEventShowFish has ZERO callers in either dump and was pure
    //    liability, and the two aim wrappers only fire while a human aims by hand, which the
    //    automation never does. Both groups removed; do not add them back for tidiness.
    //
    //    DELIBERATELY NOT HOOKED: SendCameraEventQuitFishing, SendCameraEventHookUpEmpty and
    //    SendExitAnimAtTarget. Those only POP. Leaving them live is what makes the toggle safe to
    //    flip mid-session -- anything that did get pushed while the option was off is still torn
    //    down normally. PopController() merely queues a token id, so popping one that was never
    //    pushed is a no-op; vanilla already does exactly that (HookUpEmpty zeroes the token and a
    //    later QuitFishing pops the zero).
    //
    // 2) THE HUD IS **NOT** THIS FEATURE'S JOB — see PersistentHudFeature ("Keep HUD in
    //    fishing/vehicle modes", Features -> Main). That one already reopens StatusPanel over the
    //    mode panel via UIManager.OpenView, for fishing AND vehicle/skate/coaster/etc, hides the
    //    duplicated widgets (minimap, chat, energy), and hides StatusPanel's bottom-right skill
    //    block because its RightJoyStick otherwise eats the PointerDown and the strike button never
    //    fires. A second HUD-restore path here duplicated it and reintroduced exactly that bug, so
    //    it was removed. THIS feature is camera-only.
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
        private delegate void FishingCamHudVoid2Delegate(IntPtr a, IntPtr b);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void FishingCamHudWaitingDelegate(IntPtr playerProxy, IntPtr floatTransform, IntPtr tarPos, float time);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr FishingCamHudCompileMethodDelegate(IntPtr method);

        // Slot indexes into the trampoline arrays. Each hooked method needs its own trampoline, so
        // each body is a closure over its slot; the arrays keep the lookup a plain array read.
        private const int FishingCamHudSlotBaiting = 0;
        private const int FishingCamHudSlotHookUp = 1;
        private const int FishingCamHudVoid2Slots = 2;

        // Read by the detour bodies — the ONLY thing they read. Set when the hooks install and
        // NEVER cleared: once armed, the bodies suppress unconditionally, because the forwarding
        // path is the proven process-killer (see the header). Static because the bodies must be.
        private static bool fishingCamHudArmed;

        private static readonly FishingCamHudVoid2Delegate[] fishingCamHudTramp2 =
            new FishingCamHudVoid2Delegate[FishingCamHudVoid2Slots];
        private static FishingCamHudWaitingDelegate fishingCamHudTrampWaiting;

        // Anti-GC roots for the bodies handed to MonoMod, plus the detours themselves.
        private static readonly System.Collections.Generic.List<Delegate> fishingCamHudKeepAlive =
            new System.Collections.Generic.List<Delegate>();
        private static readonly System.Collections.Generic.List<MonoMod.RuntimeDetour.NativeDetour> fishingCamHudDetours =
            new System.Collections.Generic.List<MonoMod.RuntimeDetour.NativeDetour>();


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
            FeatureLog.Toggle("FishingCamHud", value);

            if (value && this.fishingCamHudInstalledCount == 0)
            {
                FeatureLog.Life("FishingCamHud", "arming — hooks install on the next world-ready gate");
            }
            else if (!value && fishingCamHudArmed)
            {
                // Deliberate one-way arming: forwarding through the stubs is the proven
                // process-killer, so the installed hooks keep suppressing. Only the HUD-restore
                // dispatch (no detour) obeys the toggle immediately.
                FeatureLog.Life("FishingCamHud", "hooks stay suppressing until the game restarts"
                    + " (safe-teardown rule); the OFF setting takes full effect on the next launch");
            }
        }

        // ---- per-frame glue --------------------------------------------------------------------

        private void ProcessFishingCameraHudOnUpdate()
        {
            if (!this.fishingCamHudEnabled)
            {
                return;
            }

            // Hook installs run on the world-ready gate, never on a retry timer here
            // (AGENTS.md §1 hard rule). Registration is idempotent and cheap.
            if (!this.fishingCamHudCallbackRegistered)
            {
                this.fishingCamHudCallbackRegistered = true;
                this.RegisterWorldReadyCallback("FishingCameraHud", this.TryInstallFishingCameraHudHooksOnWorldReady);
            }
        }

        // ---- HUD restore (no detour) ------------------------------------------------------------

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
                if (utilClass == IntPtr.Zero)
                {
                    return false; // image not loaded yet — retry
                }

                int installed = 0;


                installed += this.TryHookFishingCamVoid2(compile, utilClass, "SendCameraEventBaiting",
                    FishingCamHudSlotBaiting) ? 1 : 0;
                installed += this.TryHookFishingCamVoid2(compile, utilClass, "SendCameraEventHookUp",
                    FishingCamHudSlotHookUp) ? 1 : 0;

                installed += this.TryHookFishingCamWaiting(compile, utilClass) ? 1 : 0;

                this.fishingCamHudHookTried = true;
                this.fishingCamHudInstalledCount = installed;
                fishingCamHudArmed = installed > 0;

                if (installed == 0)
                {
                    FeatureLog.Fail("FishingCamHud", "no fishing camera/HUD methods could be hooked — feature off (game update?).");
                    return true;
                }

                FeatureLog.Life("FishingCamHud", "installed " + installed + "/3 camera hooks (always-suppress once armed)"
                    + (installed < 3 ? " — the missing ones stay vanilla" : string.Empty));
                return true;
            }
            catch (Exception ex)
            {
                this.fishingCamHudHookTried = true;
                FeatureLog.Fail("FishingCamHud", "hook install failed: " + ex.Message + " — feature off.");
                return true;
            }
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
                if (fishingCamHudArmed)
                {
                    return; // never forward once armed — the pass-through path is the crash vector
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
                if (fishingCamHudArmed)
                {
                    return; // never forward once armed — the pass-through path is the crash vector
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
