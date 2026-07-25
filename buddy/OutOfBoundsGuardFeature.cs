using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace HeartopiaMod
{
    // Disable the game's out-of-bounds ("you left the map") auto-teleport.
    //
    // The OOB watchdog is 100% CLIENT-side — the server never corrects the local player's position
    // (it is client-authoritative: BasePlayerComponent.TrySendSelfTransform just posts
    // MoveParentTransformNetworkCommand, which has no reply/reject). The single detector lives in
    // XDTLevelAndEntity.Gameplay.Component.Fish.PlayerFishingShipComponent.Tick (the component sits
    // on every player, not just fishing-boat riders):
    //
    //     var cfg = TableData.GetGameSceneConfig(GameWorld.gameLevel.GetSceneId());
    //     var position = localPlayerComponent.entity.position;
    //     if (outside [cfg.detectMin, cfg.detectMax] && !TryBackToShip())
    //         LeaveShipThenTeleportToSafePos();
    //
    // detectMin/Max come from the client table GameSceneConfig, e.g. StarTown [-512,9,-512]..
    // [512,60,512], SeaWorld [-170,-80,-170]..[170,0,170] (that y<=0 ceiling is the "pulled back
    // when you surface in the open sea" case). LeaveShipThenTeleportToSafePos ends in
    // EntityHelper.TransferToSafePos -> SafePointModule KD-tree -> LocalPlayerComponent.Transfer,
    // i.e. a plain local Transform write; no network call anywhere in the chain.
    //
    // Both branch members are hooked (Mono NativeDetour, the SeaCleanBanner/WarehouseHomeland
    // conditional-passthrough shape — never a Harmony/IL2CPP .text patch):
    //   * TryBackToShip()  -> return 1 while the toggle is on. That is the PRIMARY cut: a true
    //     short-circuits `&& !TryBackToShip()`, so no snap-to-boat AND no safe-pos teleport.
    //   * LeaveShipThenTeleportToSafePos() -> no-op while the toggle is on. Backstop in case a
    //     future build reorders the condition; harmless otherwise (it is unreachable then).
    // Both are private instance methods with no args -> native void/byte (IntPtr self); the bodies
    // read one volatile bool and either return or forward the untouched `this` to the trampoline,
    // so with the toggle off behaviour is byte-exact vanilla and teardown calls are as safe as
    // stock (native-detours world-change rule: opt-in toggle + allocation-free body).
    //
    // NOT touched (deliberately): the Settings "reset position" button shares TransferToSafePos but
    // enters through TeleportModule, not through these two methods — it keeps working; and
    // MovementUtil.MakeSurePositionSafty (the min/max clamp applied to interaction TARGET positions)
    // is a separate mechanism this toggle leaves alone.
    public partial class HeartopiaComplete
    {
        // Toggle (persisted; default OFF = vanilla OOB rescue). Drawn in Self -> Main.
        private bool disableOobTeleportEnabled;

        // Written by ProcessOutOfBoundsGuardOnUpdate (main thread); read by the native hook bodies.
        private static volatile bool oobGuardActive;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void OobLeaveShipHookDelegate(IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate byte OobBackToShipHookDelegate(IntPtr self);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr OobGuardCompileMethodDelegate(IntPtr method);

        private static MonoMod.RuntimeDetour.NativeDetour oobLeaveShipDetour;
        private static OobLeaveShipHookDelegate oobLeaveShipHookKeepAlive; // anti-GC
        private static OobLeaveShipHookDelegate oobLeaveShipTrampoline;

        private static MonoMod.RuntimeDetour.NativeDetour oobBackToShipDetour;
        private static OobBackToShipHookDelegate oobBackToShipHookKeepAlive; // anti-GC
        private static OobBackToShipHookDelegate oobBackToShipTrampoline;

        private bool oobGuardHookTried;
        private float oobGuardNextAttemptAt = -999f;

        private const string OobGuardWorldReadyCallbackName = "OutOfBoundsGuardHook";

        private static readonly string[] OobGuardImageNames =
        {
            "XDTLevelAndEntity", "XDTLevelAndEntity.dll",
            "Client", "Client.dll",
            "Assembly-CSharp", "Assembly-CSharp.dll"
        };

        // True once at least the primary cut (TryBackToShip) is live — used by the UI status line
        // and to avoid claiming the feature is active while the hook is still pending.
        internal bool OutOfBoundsGuardHookInstalled => oobBackToShipTrampoline != null;

        // Called every frame from OnUpdate. Mirrors the toggle into the static flag the hook bodies
        // read, and installs the hooks lazily while the feature is enabled.
        private void ProcessOutOfBoundsGuardOnUpdate()
        {
            if (!this.disableOobTeleportEnabled)
            {
                oobGuardActive = false; // installed hooks (if any) forward -> vanilla OOB rescue
                return;
            }

            this.EnsureOutOfBoundsGuardHooks();
            oobGuardActive = oobBackToShipTrampoline != null || oobLeaveShipTrampoline != null;
        }

        // Installs both detours once. Idempotent; transient failures (AuraMono not up,
        // XDTLevelAndEntity not loaded, JIT entry not ready) retry on a 5 s cadence behind the
        // world-ready gate; permanent failures burn the tried-flag with one log line. Never throws.
        private void EnsureOutOfBoundsGuardHooks()
        {
            if (this.oobGuardHookTried || oobBackToShipTrampoline != null)
            {
                return;
            }

            this.RegisterWorldReadyCallback(OobGuardWorldReadyCallbackName, this.TryOutOfBoundsGuardWorldReadyInstall);

            // PlayerFishingShipComponent only exists once a world is up; before that the lookup can
            // only fail, so don't spin the 5 s retry against the login screen.
            if (!this.IsWorldReady)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now < this.oobGuardNextAttemptAt)
            {
                return;
            }
            this.oobGuardNextAttemptAt = now + 5f;

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    return; // AuraMono not up yet — retry later
                }

                IntPtr monoModule = this.GetAuraMonoModuleHandle();
                OobGuardCompileMethodDelegate compile = monoModule != IntPtr.Zero
                    ? this.GetAuraMonoExport<OobGuardCompileMethodDelegate>(monoModule, "mono_compile_method")
                    : null;
                if (compile == null)
                {
                    this.oobGuardHookTried = true;
                    ModLogger.Msg("[OobGuard] mono_compile_method unavailable — OOB-teleport disable off.");
                    return;
                }

                IntPtr cls = this.FindAuraMonoClassInImages(
                    "XDTLevelAndEntity.Gameplay.Component.Fish", "PlayerFishingShipComponent", OobGuardImageNames);
                if (cls == IntPtr.Zero)
                {
                    cls = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.Gameplay.Component.Fish.PlayerFishingShipComponent");
                }
                if (cls == IntPtr.Zero)
                {
                    cls = this.FindAuraMonoClassByFullName("XDTLevelAndEntity.GamePlay.Component.Fish.PlayerFishingShipComponent");
                }
                if (cls == IntPtr.Zero)
                {
                    return; // image not loaded yet — retry on the 5 s cadence
                }

                IntPtr backToShip = this.FindAuraMonoMethodOnHierarchy(cls, "TryBackToShip", 0);
                IntPtr leaveShip = this.FindAuraMonoMethodOnHierarchy(cls, "LeaveShipThenTeleportToSafePos", 0);
                if (backToShip == IntPtr.Zero && leaveShip == IntPtr.Zero)
                {
                    this.oobGuardHookTried = true;
                    ModLogger.Msg("[OobGuard] PlayerFishingShipComponent.TryBackToShip/LeaveShipThenTeleportToSafePos "
                        + "not found — OOB-teleport disable off (game update?).");
                    return;
                }

                bool primary = this.TryInstallOobBackToShipHook(compile, backToShip);
                bool backstop = this.TryInstallOobLeaveShipHook(compile, leaveShip);

                if (!primary && !backstop)
                {
                    return; // JIT entries not ready — retry on the cadence
                }

                this.oobGuardHookTried = true;
                ModLogger.Msg("[OobGuard] OOB auto-teleport hooks installed (TryBackToShip=" + (primary ? "yes" : "no")
                    + ", LeaveShipThenTeleportToSafePos=" + (backstop ? "yes" : "no")
                    + ") — out-of-bounds rescue suppressed while the toggle is on.");
            }
            catch (Exception ex)
            {
                this.oobGuardHookTried = true;
                this.UndoOutOfBoundsGuardHooks();
                ModLogger.Msg("[OobGuard] hook install failed: " + ex.Message + " — OOB-teleport disable off.");
            }
        }

        // World-ready wrapper (LoadingClosedEvent): returns true when there is nothing left to
        // install, so the gate stops calling it.
        private bool TryOutOfBoundsGuardWorldReadyInstall()
        {
            if (this.oobGuardHookTried || oobBackToShipTrampoline != null)
            {
                return true;
            }

            this.oobGuardNextAttemptAt = -999f;
            this.EnsureOutOfBoundsGuardHooks();
            return this.oobGuardHookTried || oobBackToShipTrampoline != null;
        }

        private bool TryInstallOobBackToShipHook(OobGuardCompileMethodDelegate compile, IntPtr method)
        {
            if (oobBackToShipTrampoline != null)
            {
                return true;
            }
            if (method == IntPtr.Zero)
            {
                return false;
            }

            IntPtr nativePtr = compile(method);
            if (nativePtr == IntPtr.Zero)
            {
                return false;
            }

            oobBackToShipHookKeepAlive = OobBackToShipDetourBody;
            oobBackToShipDetour = new MonoMod.RuntimeDetour.NativeDetour(nativePtr, oobBackToShipHookKeepAlive);
            oobBackToShipTrampoline = oobBackToShipDetour.GenerateTrampoline<OobBackToShipHookDelegate>();
            if (oobBackToShipTrampoline == null)
            {
                // Without a passthrough the body would claim "handled" even with the toggle off.
                try { oobBackToShipDetour?.Undo(); } catch { }
                oobBackToShipDetour = null;
                oobBackToShipHookKeepAlive = null;
                ModLogger.Msg("[OobGuard] trampoline unavailable for TryBackToShip; detour reverted.");
                return false;
            }

            return true;
        }

        private bool TryInstallOobLeaveShipHook(OobGuardCompileMethodDelegate compile, IntPtr method)
        {
            if (oobLeaveShipTrampoline != null)
            {
                return true;
            }
            if (method == IntPtr.Zero)
            {
                return false;
            }

            IntPtr nativePtr = compile(method);
            if (nativePtr == IntPtr.Zero)
            {
                return false;
            }

            oobLeaveShipHookKeepAlive = OobLeaveShipDetourBody;
            oobLeaveShipDetour = new MonoMod.RuntimeDetour.NativeDetour(nativePtr, oobLeaveShipHookKeepAlive);
            oobLeaveShipTrampoline = oobLeaveShipDetour.GenerateTrampoline<OobLeaveShipHookDelegate>();
            if (oobLeaveShipTrampoline == null)
            {
                try { oobLeaveShipDetour?.Undo(); } catch { }
                oobLeaveShipDetour = null;
                oobLeaveShipHookKeepAlive = null;
                ModLogger.Msg("[OobGuard] trampoline unavailable for LeaveShipThenTeleportToSafePos; detour reverted.");
                return false;
            }

            return true;
        }

        private void UndoOutOfBoundsGuardHooks()
        {
            try { oobBackToShipDetour?.Undo(); } catch { }
            try { oobLeaveShipDetour?.Undo(); } catch { }
            oobBackToShipDetour = null;
            oobLeaveShipDetour = null;
            oobBackToShipHookKeepAlive = null;
            oobLeaveShipHookKeepAlive = null;
            oobBackToShipTrampoline = null;
            oobLeaveShipTrampoline = null;
        }

        // Reverse-pinvoke bodies compiled game code calls instead of the originals. Allocation-free:
        // one volatile bool read, then either the short-circuit value or a pure trampoline forward
        // of the untouched `this` pointer (never dereferenced here — safe during teardown).

        // Returning 1 = "already handled": Tick's `&& !TryBackToShip()` short-circuits, so neither
        // the snap-to-boat nor LeaveShipThenTeleportToSafePos runs.
        private static byte OobBackToShipDetourBody(IntPtr self)
        {
            if (oobGuardActive)
            {
                return 1;
            }

            OobBackToShipHookDelegate trampoline = oobBackToShipTrampoline;
            return trampoline != null ? trampoline(self) : (byte)0;
        }

        private static void OobLeaveShipDetourBody(IntPtr self)
        {
            if (oobGuardActive)
            {
                return; // swallow: no leave-ship + safe-point teleport
            }

            OobLeaveShipHookDelegate trampoline = oobLeaveShipTrampoline;
            if (trampoline != null)
            {
                trampoline(self);
            }
        }
    }
}
