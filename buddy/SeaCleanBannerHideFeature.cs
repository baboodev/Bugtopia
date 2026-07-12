using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace HeartopiaMod
{
    // Hide the "Crystal Clear" sea-clean completion banner (and the other cleanliness-stage banners)
    // so it stops covering the screen during auto-cleaning.
    //
    // The banner is the game's OWN UI: SeaCleanCleanlinessPartLogic (XDTGameUI) calls the static
    // XDTGame.UI.Panel.SeaCleanCleanBannerPanel.Open(title, iconPictureName, desc) whenever a
    // cleanliness stage with showBanner=true is reached (title = localized bannerTitleTextId, e.g.
    // "Crystal Clear" for the fully-cleaned stage; SeaCleanCleanlinessPartLogic.cs:159). It is an
    // AuraMono-only UI class (XDTGame.UI.Panel.*, compiled into XDTGameUI.dll, no interop stub), so
    // we hook the Mono method: AuraMono resolve + mono_compile_method + MonoMod NativeDetour with a
    // trampoline — the mod's proven conditional-passthrough shape (cf. HeartopiaComplete.Transfer.cs
    // IsPlayerInHomeLand spoof, WarehouseHomeland). While the toggle is on the hook returns
    // immediately (no panel opens); while off it forwards to the original via the trampoline, so
    // vanilla behavior is byte-exact.
    //
    // Open is `static void (string, string, string)` -> native void(IntPtr,IntPtr,IntPtr), no `this`.
    // The body reads one volatile bool and either returns or forwards the three (untouched)
    // reference-type pointers to the trampoline — allocation-free, no deref, safe during teardown.
    public partial class HeartopiaComplete
    {
        // Toggle (persisted; default OFF = banner shows as vanilla). Drawn in the Auto Sea Clean tab.
        private bool hideSeaCleanBannerEnabled;

        // Written by ProcessSeaCleanBannerHideOnUpdate (main thread); read by the native hook body.
        private static volatile bool seaCleanBannerHideActive;

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void SeaCleanBannerOpenHookDelegate(IntPtr title, IntPtr iconPictureName, IntPtr desc);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate IntPtr SeaCleanBannerCompileMethodDelegate(IntPtr method);

        private static MonoMod.RuntimeDetour.NativeDetour seaCleanBannerDetour;
        private static SeaCleanBannerOpenHookDelegate seaCleanBannerHookKeepAlive; // anti-GC
        private static SeaCleanBannerOpenHookDelegate seaCleanBannerTrampoline;
        private bool seaCleanBannerHookTried;
        private float seaCleanBannerNextAttemptAt = -999f;

        private static readonly string[] SeaCleanBannerImageNames =
        {
            "XDTGameUI", "XDTGameUI.dll", "Client", "Client.dll"
        };

        // Called every frame from OnUpdate. Mirrors the toggle into the static flag the hook reads,
        // and installs the hook lazily while the feature is enabled (once XDTGameUI is loaded).
        private void ProcessSeaCleanBannerHideOnUpdate()
        {
            if (!this.hideSeaCleanBannerEnabled)
            {
                seaCleanBannerHideActive = false; // installed hook (if any) forwards -> vanilla banner
                return;
            }

            this.EnsureSeaCleanBannerHideHook();
            seaCleanBannerHideActive = seaCleanBannerTrampoline != null; // suppress only once the hook is live
        }

        // Installs the Open detour once (WarehouseHomeland conditional-passthrough shape). Idempotent;
        // transient failures (XDTGameUI not loaded yet, JIT entry not ready) retry on a 5s cadence,
        // permanent failures burn the tried-flag and leave the feature idle with one log line. If the
        // trampoline can't be generated the detour is reverted — without a passthrough the body would
        // swallow the banner even when the toggle is off. Never throws out.
        private void EnsureSeaCleanBannerHideHook()
        {
            if (seaCleanBannerTrampoline != null || this.seaCleanBannerHookTried)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (now < this.seaCleanBannerNextAttemptAt)
            {
                return;
            }
            this.seaCleanBannerNextAttemptAt = now + 5f;

            try
            {
                if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread())
                {
                    return; // AuraMono not up yet — retry later
                }

                IntPtr monoModule = this.GetAuraMonoModuleHandle();
                SeaCleanBannerCompileMethodDelegate compile = monoModule != IntPtr.Zero
                    ? this.GetAuraMonoExport<SeaCleanBannerCompileMethodDelegate>(monoModule, "mono_compile_method")
                    : null;
                if (compile == null)
                {
                    this.seaCleanBannerHookTried = true;
                    ModLogger.Msg("[SeaCleanBanner] mono_compile_method unavailable — banner-hide off.");
                    return;
                }

                IntPtr cls = this.FindAuraMonoClassInImages("XDTGame.UI.Panel", "SeaCleanCleanBannerPanel", SeaCleanBannerImageNames);
                if (cls == IntPtr.Zero)
                {
                    cls = this.FindAuraMonoClassByFullName("XDTGame.UI.Panel.SeaCleanCleanBannerPanel");
                }
                if (cls == IntPtr.Zero)
                {
                    return; // XDTGameUI image not loaded yet (main menu) — retry on the 5s cadence
                }

                IntPtr method = this.FindAuraMonoMethodOnHierarchy(cls, "Open", 3);
                if (method == IntPtr.Zero)
                {
                    this.seaCleanBannerHookTried = true;
                    ModLogger.Msg("[SeaCleanBanner] SeaCleanCleanBannerPanel.Open(3) not found — banner-hide off (game update?).");
                    return;
                }

                IntPtr nativePtr = compile(method);
                if (nativePtr == IntPtr.Zero)
                {
                    return; // JIT entry unavailable — retry on the cadence
                }

                this.seaCleanBannerHookTried = true;
                seaCleanBannerHookKeepAlive = SeaCleanBannerOpenDetourBody;
                seaCleanBannerDetour = new MonoMod.RuntimeDetour.NativeDetour(nativePtr, seaCleanBannerHookKeepAlive);
                seaCleanBannerTrampoline = seaCleanBannerDetour.GenerateTrampoline<SeaCleanBannerOpenHookDelegate>();
                if (seaCleanBannerTrampoline == null)
                {
                    try { seaCleanBannerDetour?.Undo(); } catch { }
                    seaCleanBannerDetour = null;
                    seaCleanBannerHookKeepAlive = null;
                    ModLogger.Msg("[SeaCleanBanner] trampoline unavailable for Open; detour reverted — banner-hide off.");
                    return;
                }

                ModLogger.Msg("[SeaCleanBanner] Hooked SeaCleanCleanBannerPanel.Open @0x" + nativePtr.ToInt64().ToString("X")
                    + " — Crystal Clear / cleanliness-stage banners suppressed while the toggle is on.");
            }
            catch (Exception ex)
            {
                this.seaCleanBannerHookTried = true;
                try { seaCleanBannerDetour?.Undo(); } catch { }
                seaCleanBannerDetour = null;
                seaCleanBannerHookKeepAlive = null;
                seaCleanBannerTrampoline = null;
                ModLogger.Msg("[SeaCleanBanner] Open hook install failed: " + ex.Message + " — banner-hide off.");
            }
        }

        // Reverse-pinvoke body compiled game code calls instead of the original Open. Allocation-free:
        // one volatile bool read. When suppressing, return without opening the panel; otherwise
        // forward the three (untouched) pointers to the original via the trampoline. Safe during
        // world-change teardown — the forward is exactly the stock call.
        private static void SeaCleanBannerOpenDetourBody(IntPtr title, IntPtr iconPictureName, IntPtr desc)
        {
            if (seaCleanBannerHideActive)
            {
                return; // swallow: no Crystal Clear banner
            }

            SeaCleanBannerOpenHookDelegate trampoline = seaCleanBannerTrampoline;
            if (trampoline != null)
            {
                trampoline(title, iconPictureName, desc);
            }
        }
    }
}
