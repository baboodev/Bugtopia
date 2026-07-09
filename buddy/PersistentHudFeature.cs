using System;
using System.Collections.Generic;
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

        // Scene path of the Status physical layer that hosts every status panel instance.
        private const string PersistentHudStatusLayerPath = "GameApp/startup_root(Clone)/XDUIRoot/Status/";
        private const float PersistentHudDedupeInterval = 0.3f;

        // Duplicate-widget suppression: with StatusPanel reopened on top, the mode panels' own
        // copies of the shared HUD widgets (energy bar, minimap, task bar, chat) double up on
        // screen. Paths are relative to each panel's root instance and were taken from the
        // XDTGame.Auto/*_Auto.cs node tables; only blocks whose EVERY child duplicates a
        // StatusPanel widget are listed (e.g. SkateStatusPanel's top_left holds the unique skate
        // exit button, so only its chat block is hidden; FishingPanel's buff list lives inside
        // energy_bar@go, so only the progress bar underneath it is hidden).
        private static readonly KeyValuePair<string, string[]>[] PersistentHudDuplicateNodesByPanel =
        {
            new KeyValuePair<string, string[]>("VehicleStatusPanel", new[]
            {
                "AniRoot@ani@queueanimation/top_left_layout@go",    // map + energy + self-room timer
                "AniRoot@ani@queueanimation/middle_left_layout@go", // taskbar
                "AniRoot@ani@queueanimation/bottom_left_layout@go", // chat
            }),
            new KeyValuePair<string, string[]>("FishingPanel", new[]
            {
                "energy_bar@go/energy_progress@go", // keep buff@go@list (fishing buffs) visible
            }),
            new KeyValuePair<string, string[]>("SkateStatusPanel", new[]
            {
                "AniRoot@ani@queueanimation/bottom_left_layout@go", // chat
            }),
            new KeyValuePair<string, string[]>("RollerCoasterStatusPanel", new[]
            {
                "AniRoot@ani@queueanimation/top_left_layout@go",
                "AniRoot@ani@queueanimation/middle_left_layout@go",
                "AniRoot@ani@queueanimation/bottom_left_layout@go",
            }),
            new KeyValuePair<string, string[]>("WaterCorridorStatusPanel", new[]
            {
                "AniRoot@ani/top_left_layout@go",    // note: no @queueanimation on this prefab
                "AniRoot@ani/bottom_left_layout@go",
            }),
            new KeyValuePair<string, string[]>("CarouselStatusPanel", new[]
            {
                "AniRoot@ani@queueanimation/top_left_layout@go",
                "AniRoot@ani@queueanimation/middle_left_layout@go",
                "AniRoot@ani@queueanimation/bottom_left_layout@go",
            }),
            // SightseeingStatusPanel: no duplicated widgets.
        };

        // StatusPanel's own tool-skill block (bottom-right). Its main button is a RightJoyStick
        // sitting at the exact screen spot of every mode panel's primary control (FishingPanel's
        // strike button, VehicleStatusPanel's exit/boat controls). Reopened StatusPanel is the later
        // sibling -> renders on top -> that joystick eats the PointerDown and the strike never fires
        // (field report: manual fishing impossible with the toggle on). Hidden while any mode panel
        // is active; the block only contains skill_bar@w@go, menu/emoji/camera rows stay.
        private const string PersistentHudStatusSkillBlockPath = "AniRoot@ani@queueanimation/right_layout@ani/middle_right_layout@go";

        // SkillWidget.MainJoyState.Null (Fishing=0, SweepNet=1, Handhold=2, Empty=3, SeaClean=4, Null=5).
        private const int PersistentHudMainJoyStateNull = 5;

        private bool persistentHudEnabled;
        private bool persistentHudHookRegistered;
        private float persistentHudNextPollAt;
        private float persistentHudNextDedupeAt;
        private bool persistentHudSkillJoySuppressed;
        private string persistentHudLastStatus = "Idle.";

        // Nodes we disabled on the mode panels, with the panel root they belong to. Restored the
        // moment StatusPanel is no longer up (reopen failed, full-screen view stole focus, mode
        // exited) so the native panels are never left stripped without the full HUD on screen —
        // panel instances are cached by the UI layer, a leaked SetActive(false) would persist
        // into the next vanilla open.
        private readonly List<KeyValuePair<GameObject, GameObject>> persistentHudHiddenNodes = new List<KeyValuePair<GameObject, GameObject>>();

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
                if (this.persistentHudHiddenNodes.Count > 0)
                {
                    try { this.PersistentHudRestoreHiddenNodes(); } catch { }
                }

                // Toggle falling edge: hand the skill joystick back to the game's own recompute so
                // it leaves in a consistent vanilla state (whatever mode the current tool implies).
                if (this.persistentHudSkillJoySuppressed)
                {
                    this.persistentHudSkillJoySuppressed = false;
                    try { this.TryPersistentHudSetSkillJoy(suppress: false); } catch { }
                }

                return;
            }

            this.EnsurePersistentHudHookRegistered();

            float now = Time.unscaledTime;
            if (now >= this.persistentHudNextDedupeAt)
            {
                this.persistentHudNextDedupeAt = now + PersistentHudDedupeInterval;
                try
                {
                    this.PersistentHudApplyDedupe();
                }
                catch (Exception ex)
                {
                    if (MasterLogPersistentHud)
                    {
                        ModLogger.Msg("[PersistentHud] dedupe failed: " + ex.Message);
                    }
                }
            }

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

        // GameObject.Find with a full path only matches ACTIVE objects — exactly the semantics we
        // want: a closed/unfocused panel (root deactivated) reads as "not there".
        private static GameObject PersistentHudFindPanelRoot(string panelName)
        {
            return GameObject.Find(PersistentHudStatusLayerPath + panelName + "(Clone)")
                ?? GameObject.Find(PersistentHudStatusLayerPath + panelName);
        }

        // Hide the mode panels' duplicated HUD widgets while our reopened StatusPanel is visible;
        // restore them the moment it is not. Runs every 0.3 s: the game's own widget refreshes
        // (HudEntryWidget.Refresh on feature events) can re-enable inner nodes, and panels are
        // toggled active/inactive on focus changes — re-asserting beats chasing every code path.
        private void PersistentHudApplyDedupe()
        {
            GameObject statusRoot = PersistentHudFindPanelRoot("StatusPanel");
            if (statusRoot == null)
            {
                // Full HUD not on screen (reopen pending/failed, full-screen view has focus, or a
                // non-wanted mode) — never leave the native panels stripped in that state.
                this.PersistentHudRestoreHiddenNodes();
                return;
            }

            // Drop destroyed entries; restore entries whose panel went away or inactive (mode
            // exited / lost focus) so cached panel instances reopen pristine.
            for (int i = this.persistentHudHiddenNodes.Count - 1; i >= 0; i--)
            {
                GameObject panelRoot = this.persistentHudHiddenNodes[i].Key;
                GameObject node = this.persistentHudHiddenNodes[i].Value;
                if (node == null)
                {
                    this.persistentHudHiddenNodes.RemoveAt(i);
                    continue;
                }

                if (panelRoot == null || !panelRoot.activeInHierarchy)
                {
                    node.SetActive(true);
                    this.persistentHudHiddenNodes.RemoveAt(i);
                }
            }

            GameObject activeModePanelRoot = null;
            foreach (KeyValuePair<string, string[]> entry in PersistentHudDuplicateNodesByPanel)
            {
                GameObject panelRoot = PersistentHudFindPanelRoot(entry.Key);
                if (panelRoot == null)
                {
                    continue;
                }

                if (activeModePanelRoot == null)
                {
                    activeModePanelRoot = panelRoot;
                }

                foreach (string childPath in entry.Value)
                {
                    this.PersistentHudHideNode(panelRoot, panelRoot, childPath, entry.Key);
                }
            }

            // SightseeingStatusPanel has no duplicate widgets but still owns the bottom-right spot.
            if (activeModePanelRoot == null)
            {
                activeModePanelRoot = PersistentHudFindPanelRoot("SightseeingStatusPanel");
            }

            // A mode panel is up: get StatusPanel's tool-skill joystick out of the way of the mode's
            // primary control (fishing strike / vehicle exit). Owner = the mode panel root, so the
            // standard restore path re-enables the skill bar the moment the mode exits.
            if (activeModePanelRoot != null)
            {
                this.PersistentHudHideNode(activeModePanelRoot, statusRoot, PersistentHudStatusSkillBlockPath, "StatusPanel");

                // Hiding the node is NOT enough: SkillWidget's OnStart already ran RefreshSkillJoy
                // -> rod in hand -> MainJoyFishing.Enter -> SetFKeyHoldHandholdActive(true) ->
                // PushInputListener(MainInteraction) ABOVE LocalPlayerComponent's handler. That
                // listener outlives SetActive(false) and eats the fishing-strike input (both the
                // panel button's SendValueToControl and the F key). SetMainJoyState(Null) runs the
                // widget's own Leave() path — exactly what a vanilla panel close does — popping the
                // listener. Re-asserted every tick (no-op inside the widget once already Null)
                // because game events (handhold changes) can re-enter the fishing joy mode.
                if (this.TryPersistentHudSetSkillJoy(suppress: true) && !this.persistentHudSkillJoySuppressed)
                {
                    this.persistentHudSkillJoySuppressed = true;
                    if (MasterLogPersistentHud)
                    {
                        ModLogger.Msg("[PersistentHud] StatusPanel skill joy suppressed (MainJoyState.Null)");
                    }
                }
            }
            else if (this.persistentHudSkillJoySuppressed)
            {
                // Back to Free with StatusPanel still open: recompute the joy mode from the current
                // tool via the widget's own RefreshSkillJoy (the OnStart path) so casting works again.
                if (this.TryPersistentHudSetSkillJoy(suppress: false))
                {
                    this.persistentHudSkillJoySuppressed = false;
                    if (MasterLogPersistentHud)
                    {
                        ModLogger.Msg("[PersistentHud] StatusPanel skill joy restored (RefreshSkillJoy)");
                    }
                }
            }
        }

        // Drives StatusPanel's SkillWidget through its own private API via AuraMono:
        //   suppress=true  -> SetMainJoyState(MainJoyState.Null)  (Leave(): pops the MainInteraction
        //                     listener, clears joystick handlers — the vanilla panel-close path)
        //   suppress=false -> RefreshSkillJoy()                   (recompute from the current tool —
        //                     the vanilla OnStart path; safe to call even if nothing was suppressed)
        // Chain: UIManager.GetView(StatusPanel) -> .ui (StatusPanel_Auto) -> .skill_bar_widget.
        // All intermediate objects are managed heap objects — pinned across the nested invokes.
        private unsafe bool TryPersistentHudSetSkillJoy(bool suppress)
        {
            try
            {
                if (!this.TryPersistentHudResolveUiManager(out IntPtr uiManagerObj))
                {
                    return false;
                }

                uint uiManagerPin = AuraMonoPinNew(uiManagerObj);
                try
                {
                    if (!this.TryCreateAuraMonoSystemTypeObject(PersistentHudStatusPanelTypeName, out IntPtr typeObj) || typeObj == IntPtr.Zero)
                    {
                        return false;
                    }

                    IntPtr exc = IntPtr.Zero;
                    IntPtr view;
                    {
                        IntPtr* args = stackalloc IntPtr[1];
                        args[0] = typeObj;
                        view = auraMonoRuntimeInvoke(this.persistentHudGetViewMethod, uiManagerObj, (IntPtr)args, ref exc);
                    }

                    if (exc != IntPtr.Zero || view == IntPtr.Zero)
                    {
                        return false;
                    }

                    uint viewPin = AuraMonoPinNew(view);
                    try
                    {
                        if (!this.TryGetMonoObjectMember(view, "ui", out IntPtr autoObj) || autoObj == IntPtr.Zero)
                        {
                            return false;
                        }

                        uint autoPin = AuraMonoPinNew(autoObj);
                        try
                        {
                            if (!this.TryGetMonoObjectMember(autoObj, "skill_bar_widget", out IntPtr widget) || widget == IntPtr.Zero)
                            {
                                return false;
                            }

                            uint widgetPin = AuraMonoPinNew(widget);
                            try
                            {
                                IntPtr widgetClass = auraMonoObjectGetClass(widget);
                                if (widgetClass == IntPtr.Zero)
                                {
                                    return false;
                                }

                                exc = IntPtr.Zero;
                                if (suppress)
                                {
                                    IntPtr setState = this.FindAuraMonoMethodOnHierarchy(widgetClass, "SetMainJoyState", 1);
                                    if (setState == IntPtr.Zero)
                                    {
                                        return false;
                                    }

                                    int state = PersistentHudMainJoyStateNull;
                                    IntPtr* args = stackalloc IntPtr[1];
                                    args[0] = (IntPtr)(&state);
                                    auraMonoRuntimeInvoke(setState, widget, (IntPtr)args, ref exc);
                                }
                                else
                                {
                                    IntPtr refresh = this.FindAuraMonoMethodOnHierarchy(widgetClass, "RefreshSkillJoy", 0);
                                    if (refresh == IntPtr.Zero)
                                    {
                                        return false;
                                    }

                                    auraMonoRuntimeInvoke(refresh, widget, IntPtr.Zero, ref exc);
                                }

                                return exc == IntPtr.Zero;
                            }
                            finally
                            {
                                AuraMonoPinFree(widgetPin);
                            }
                        }
                        finally
                        {
                            AuraMonoPinFree(autoPin);
                        }
                    }
                    finally
                    {
                        AuraMonoPinFree(viewPin);
                    }
                }
                finally
                {
                    AuraMonoPinFree(uiManagerPin);
                }
            }
            catch (Exception ex)
            {
                if (MasterLogPersistentHud)
                {
                    ModLogger.Msg("[PersistentHud] skill joy " + (suppress ? "suppress" : "restore") + " failed: " + ex.Message);
                }

                return false;
            }
        }

        // Disables ownerRoot-lifetimed searchRoot/childPath and tracks it for restore. The owner is
        // the panel whose visibility justifies the hide — once it deactivates, the node comes back.
        private void PersistentHudHideNode(GameObject ownerRoot, GameObject searchRoot, string childPath, string label)
        {
            Transform child = searchRoot.transform.Find(childPath);
            if (child == null)
            {
                return;
            }

            GameObject node = child.gameObject;
            if (!node.activeSelf)
            {
                return;
            }

            node.SetActive(false);
            if (!this.PersistentHudIsNodeTracked(node))
            {
                this.persistentHudHiddenNodes.Add(new KeyValuePair<GameObject, GameObject>(ownerRoot, node));
            }

            if (MasterLogPersistentHud)
            {
                ModLogger.Msg("[PersistentHud] hid " + label + "/" + childPath);
            }
        }

        private bool PersistentHudIsNodeTracked(GameObject node)
        {
            for (int i = 0; i < this.persistentHudHiddenNodes.Count; i++)
            {
                if (ReferenceEquals(this.persistentHudHiddenNodes[i].Value, node)
                    || this.persistentHudHiddenNodes[i].Value == node)
                {
                    return true;
                }
            }

            return false;
        }

        private void PersistentHudRestoreHiddenNodes()
        {
            for (int i = this.persistentHudHiddenNodes.Count - 1; i >= 0; i--)
            {
                GameObject node = this.persistentHudHiddenNodes[i].Value;
                if (node != null)
                {
                    node.SetActive(true);
                }
            }

            if (MasterLogPersistentHud && this.persistentHudHiddenNodes.Count > 0)
            {
                ModLogger.Msg("[PersistentHud] restored " + this.persistentHudHiddenNodes.Count + " hidden duplicate node(s)");
            }

            this.persistentHudHiddenNodes.Clear();
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
