using System;
using UnityEngine;

namespace HeartopiaMod
{
    // Persistent HUD — keep the main StatusPanel (CommonMapBar minimap, menu_bar buttons, task bar,
    // chat, energy) open during gameplay modes that normally swap it out for a stripped mode panel.
    //
    // Why the HUD vanishes (ilspy-dumps):
    //   GameWorld keeps a gameplay-mode stack; SwapMode = prev.ModeLoseFocus() then next.ModeFocus().
    //   GameFreeMode.OnModeLoseFocus -> FreeModeHudVisibilityEvent{visible=false}
    //     -> UIEventBridge.OnFreeModeHudVisibility -> UIManager.CloseView<StatusPanel>() (whole HUD).
    //   New mode OnModeFocus -> GameModeFocusedEvent{mode} -> UIEventBridge opens the mode panel
    //     (FishingPanel / VehicleStatusPanel / SkateStatusPanel / ...), which has no map/menu buttons.
    //
    // Why reopening on top is safe:
    //   All status panels share UIPhysicalLayerType.Status + UILogicLayerType.Back, and Back-layer
    //   StartLogic does NOT close sibling Back panels -> StatusPanel coexists with the mode panel.
    //   Reopening while the close animation runs hits UIView.InterruptCloseAndReOpen (the game's own
    //   fast-reopen path), and the game's own OpenView<StatusPanel> on returning to Free is a no-op
    //   for an already-open view. Client-side UI only, nothing goes to the server.
    //
    // Trigger: GameModeFocusedEvent hook (EGameplayMode byte @0). Hook handlers run from the OnUpdate
    // ring-buffer drain (main thread, outside the game's dispatch stack), so the reopen is invoked
    // directly. Fallback: a 2 s poll that probes GetView(StatusPanel) / known mode panels — covers
    // enabling the toggle while already fishing/driving and the window before the hook installs.
    //
    // Known cosmetic limit (accepted for the first cut): StatusPanel's bottom-right skill bar renders
    // above FishingPanel's strike button (reopened panel = later sibling in the same physical layer).
    public partial class HeartopiaComplete
    {
        private const string PersistentHudModeFocusedEventName = "XDTGameSystem.UI.GameModeFocusedEvent";
        private const string PersistentHudStatusPanelTypeName = "XDTGame.UI.Panel.StatusPanel";
        private const float PersistentHudPollInterval = 2f;

        internal const bool MasterLogPersistentHud = true;

        // The panels the wanted modes open (UIEventBridge.OnGameModeFocused); any of them open while
        // StatusPanel is closed => a HUD-replacing special mode is active. Probed by the poll fallback.
        private static readonly string[] PersistentHudModePanelTypeNames =
        {
            "XDTGame.UI.Panel.FishingPanel",
            "XDTGame.UI.Panel.VehicleStatusPanel",
            "XDTGame.UI.Panel.SkateStatusPanel",
            "XDTGame.UI.Panel.RollerCoasterStatusPanel",
            "XDTGame.UI.Panel.WaterCorridorStatusPanel",
            "XDTGame.UI.Panel.CarouselStatusPanel",
            "XDTGame.UI.Panel.SightseeingStatusPanel",
        };

        private bool persistentHudEnabled;
        private bool persistentHudHookRegistered;
        private float persistentHudNextPollAt;
        private string persistentHudLastStatus = "Idle.";

        // Image-lifetime AuraMono handles (class/method pointers only — safe to cache raw). The
        // UIManager instance and System.Type objects are managed heap objects on the moving sgen GC:
        // re-resolved on every use and pinned while held across invokes.
        private IntPtr persistentHudUiManagerClass;
        private IntPtr persistentHudGetInstanceMethod;
        private IntPtr persistentHudGetViewMethod;  // UIManager.GetView(Type)
        private IntPtr persistentHudOpenViewMethod; // UIManager.OpenView(Type, Intent)

        // EGameplayMode values (byte enum, XDTDataAndProtocol.ComponentsData.EGameplayMode) whose mode
        // panel replaces the HUD but where map/menu access is still wanted. Deliberately excludes
        // immersive or edit modes (Photo, Craft, HideAndSeek, pet play, SnowSculpture, ...) where the
        // game hides the HUD for a reason.
        private static bool PersistentHudIsWantedMode(byte mode)
        {
            switch (mode)
            {
                case 4:  // Fishing
                case 7:  // Drive
                case 17: // Carousel
                case 21: // WaterCorridor
                case 23: // RollerCoaster
                case 26: // Skate
                case 34: // Sightseeing
                    return true;
                default:
                    return false;
            }
        }

        private void ProcessPersistentHudOnUpdate()
        {
            if (!this.persistentHudEnabled)
            {
                return;
            }

            this.EnsurePersistentHudHookRegistered();

            float now = Time.unscaledTime;
            if (now < this.persistentHudNextPollAt)
            {
                return;
            }

            this.persistentHudNextPollAt = now + PersistentHudPollInterval;
            try
            {
                this.PersistentHudPoll();
            }
            catch (Exception ex)
            {
                this.persistentHudLastStatus = "Poll failed: " + ex.Message;
            }
        }

        private void EnsurePersistentHudHookRegistered()
        {
            if (this.persistentHudHookRegistered)
            {
                return;
            }

            this.persistentHudHookRegistered = true;
            bool ok = this.RegisterGameEventHook(PersistentHudModeFocusedEventName, 1, this.OnPersistentHudModeFocused);
            if (MasterLogPersistentHud)
            {
                ModLogger.Msg("[PersistentHud] GameModeFocusedEvent hook register=" + ok);
            }
        }

        // Runs from the OnUpdate ring-buffer drain (main thread, outside the game's dispatch stack).
        // By the time this runs the game has already closed StatusPanel (Free lost focus before the
        // new mode gained it), so the reopen lands on a Closing/Closed view — both are safe paths.
        private void OnPersistentHudModeFocused(GameEventSnapshot e)
        {
            if (!this.persistentHudEnabled)
            {
                return;
            }

            byte mode = e.ReadByte(0);
            if (!PersistentHudIsWantedMode(mode))
            {
                return;
            }

            bool ok = this.TryPersistentHudReopenStatusPanel(out string detail);
            this.persistentHudLastStatus = (ok ? "HUD reopened" : "Reopen failed") + " (mode " + mode + ").";
            if (MasterLogPersistentHud)
            {
                ModLogger.Msg("[PersistentHud] mode=" + mode + " focused -> reopen " + (ok ? "ok (" + detail + ")" : "FAILED: " + detail));
            }
        }

        // Poll fallback. Steady state (StatusPanel already open) costs a single GetView probe; the
        // mode-panel sweep only runs while the HUD is actually closed.
        private void PersistentHudPoll()
        {
            if (!this.TryPersistentHudResolveUiManager(out IntPtr uiManagerObj))
            {
                return;
            }

            uint pin = AuraMonoPinNew(uiManagerObj);
            try
            {
                if (this.TryPersistentHudIsPanelOpen(uiManagerObj, PersistentHudStatusPanelTypeName, out bool statusOpen) && statusOpen)
                {
                    return;
                }

                foreach (string panelTypeName in PersistentHudModePanelTypeNames)
                {
                    if (!this.TryPersistentHudIsPanelOpen(uiManagerObj, panelTypeName, out bool open) || !open)
                    {
                        continue;
                    }

                    bool ok = this.TryPersistentHudInvokeOpenView(uiManagerObj, out string detail);
                    string panelShortName = panelTypeName.Substring(panelTypeName.LastIndexOf('.') + 1);
                    this.persistentHudLastStatus = (ok ? "HUD reopened" : "Reopen failed") + " (poll, " + panelShortName + ").";
                    if (MasterLogPersistentHud)
                    {
                        ModLogger.Msg("[PersistentHud] poll: " + panelShortName + " open without StatusPanel -> reopen " + (ok ? "ok" : "FAILED: " + detail));
                    }

                    return;
                }
            }
            finally
            {
                AuraMonoPinFree(pin);
            }
        }

        private bool TryPersistentHudReopenStatusPanel(out string detail)
        {
            detail = null;
            if (!this.TryPersistentHudResolveUiManager(out IntPtr uiManagerObj))
            {
                detail = "UIManager unavailable";
                return false;
            }

            uint pin = AuraMonoPinNew(uiManagerObj);
            try
            {
                // Skip OpenView when already open: Open() on an Opened view replays GotFocus/OnNewStart.
                if (this.TryPersistentHudIsPanelOpen(uiManagerObj, PersistentHudStatusPanelTypeName, out bool alreadyOpen) && alreadyOpen)
                {
                    detail = "already open";
                    return true;
                }

                if (this.TryPersistentHudInvokeOpenView(uiManagerObj, out detail))
                {
                    detail = "opened";
                    return true;
                }

                return false;
            }
            finally
            {
                AuraMonoPinFree(pin);
            }
        }

        // Resolves the UIManager class + method pointers (cached, image lifetime) and the live
        // instance (NOT cached — managed object). Mirrors TryOpenAuraPanelByTypeNameViaMono.
        private bool TryPersistentHudResolveUiManager(out IntPtr uiManagerObj)
        {
            uiManagerObj = IntPtr.Zero;
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null || auraMonoObjectGetClass == null)
            {
                return false;
            }

            if (this.persistentHudUiManagerClass == IntPtr.Zero)
            {
                IntPtr klass = this.FindAuraMonoClassByFullName("XDTGame.Core.UIManager");
                if (klass == IntPtr.Zero)
                {
                    // Namespace != assembly: UIManager is compiled into the XDTGameUI image.
                    klass = this.FindAuraMonoClassInImages(
                        "XDTGame.Core",
                        "UIManager",
                        new string[] { "XDTGameUI", "XDTGameUI.dll", "XDTLevelAndEntity", "XDTLevelAndEntity.dll", "Client", "Client.dll" });
                }

                this.persistentHudUiManagerClass = klass;
            }

            if (this.persistentHudUiManagerClass == IntPtr.Zero)
            {
                return false;
            }

            if (this.persistentHudGetInstanceMethod == IntPtr.Zero)
            {
                this.persistentHudGetInstanceMethod = this.FindAuraMonoMethodOnHierarchy(this.persistentHudUiManagerClass, "get_Instance", 0);
            }

            if (this.persistentHudGetInstanceMethod == IntPtr.Zero)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr instance = auraMonoRuntimeInvoke(this.persistentHudGetInstanceMethod, IntPtr.Zero, IntPtr.Zero, ref exc);
            if (exc != IntPtr.Zero || instance == IntPtr.Zero)
            {
                return false;
            }

            if (this.persistentHudGetViewMethod == IntPtr.Zero || this.persistentHudOpenViewMethod == IntPtr.Zero)
            {
                IntPtr instanceClass = auraMonoObjectGetClass(instance);
                if (instanceClass == IntPtr.Zero)
                {
                    return false;
                }

                if (this.persistentHudGetViewMethod == IntPtr.Zero)
                {
                    // GetView(Type) — the only 1-arg overload (GetView<T>() is 0-arg).
                    this.persistentHudGetViewMethod = this.FindAuraMonoMethodOnHierarchy(instanceClass, "GetView", 1);
                }

                if (this.persistentHudOpenViewMethod == IntPtr.Zero)
                {
                    // OpenView(Type, Intent) — the only 2-arg overload (OpenView<T>(Intent) is 1-arg).
                    this.persistentHudOpenViewMethod = this.FindAuraMonoMethodOnHierarchy(instanceClass, "OpenView", 2);
                }
            }

            if (this.persistentHudGetViewMethod == IntPtr.Zero || this.persistentHudOpenViewMethod == IntPtr.Zero)
            {
                return false;
            }

            uiManagerObj = instance;
            return true;
        }

        // GetView(Type) != null <=> the panel is open and not Closing/Closed (GetView filters those).
        private unsafe bool TryPersistentHudIsPanelOpen(IntPtr uiManagerObj, string panelTypeName, out bool isOpen)
        {
            isOpen = false;
            if (!this.TryCreateAuraMonoSystemTypeObject(panelTypeName, out IntPtr typeObj) || typeObj == IntPtr.Zero)
            {
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[1];
            args[0] = typeObj;
            IntPtr view = auraMonoRuntimeInvoke(this.persistentHudGetViewMethod, uiManagerObj, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                return false;
            }

            isOpen = view != IntPtr.Zero;
            return true;
        }

        private unsafe bool TryPersistentHudInvokeOpenView(IntPtr uiManagerObj, out string detail)
        {
            detail = null;
            if (!this.TryCreateAuraMonoSystemTypeObject(PersistentHudStatusPanelTypeName, out IntPtr typeObj) || typeObj == IntPtr.Zero)
            {
                detail = "StatusPanel Type object unavailable";
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr* args = stackalloc IntPtr[2];
            args[0] = typeObj;
            args[1] = IntPtr.Zero; // Intent — OpenViewInternal creates one when null.
            auraMonoRuntimeInvoke(this.persistentHudOpenViewMethod, uiManagerObj, (IntPtr)args, ref exc);
            if (exc != IntPtr.Zero)
            {
                detail = "OpenView threw";
                return false;
            }

            return true;
        }
    }
}
