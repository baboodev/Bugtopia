using System;
using System.Collections.Generic;
using UnityEngine;

namespace HeartopiaMod
{
    // Clean Photos — hides every surface the MOD draws for the frame in which the game reads the
    // backbuffer to take an in-game photo. Full research and the rejected alternatives:
    // docs/plans/2026-10-30-photo-snap-ui-hide.md.
    //
    // WHY THE GAME ITSELF DOES NOT HIDE US: every in-game capture flow (GamePhotoMode.Photograph,
    // PhotoSystem.QuickSnap on balloon/sightseeing/coaster/skate/hide-and-seek) dispatches
    // XDTGame.UI.CanvasSnapStateRequestedEvent{active} before the capture; UIManager.SetLayerForSnap
    // then disables every GAME physical UI layer except Bottom/Float, and ScreenCaptureUtil reads
    // the composited backbuffer after that frame rendered. The mod's canvases are not in
    // UIManager._physicalLayerDic, so they stay composited — that is the leak. Mirroring the game's
    // own hide window on the same event is the fix; no poll, no .text patch.
    //
    // FRAME TIMING (matters for the apply rules below):
    //   Photograph  — active=true, WaitForEndOfFrame, CAPTURE, WaitForEndOfFrame, active=false.
    //   QuickSnap   — active=true, WaitForEndOfFrame (metrics only), WaitForEndOfFrame, CAPTURE,
    //                 WaitForEndOfFrame, active=false.  Capture is one frame LATER.
    // The apply runs at the END of OnLateUpdate (LateThunk, after
    // PreLateUpdate/ScriptRunBehaviourLateUpdate) — still before the canvas-render subsystems that
    // compose the frame, so a hide lands in the dispatching frame's own render, which is the
    // Photograph capture frame. Both active=true dispatches come from a button handler +
    // StartCoroutine's synchronous first MoveNext, i.e. inside ScriptRunBehaviourUpdate, which
    // precedes our UpdateThunk — so the drain sets `photoSnapUiSnapWanted` that same frame.
    //
    // WHY THE HIDE IS RE-APPLIED EVERY FRAME, not once: ProcessUguiStatusOverlayOnUpdate and
    // ProcessUguiToastsOnUpdate run in OnUpdate AFTER the hook drain and call SetActive(true) on
    // their roots every frame while enabled. A one-shot hide would let them back into the backbuffer
    // before QuickSnap's one-frame-later capture. So while the snap is open: never re-read
    // activeSelf (the drivers would poison the saved value with false), just re-hide; the value to
    // restore was captured ONCE per root at the transition into the hidden state.
    public partial class HeartopiaComplete
    {
        // XDTGame.UI.CanvasSnapStateRequestedEvent (struct in the XDTLevelAndEntity image):
        // { bool active@0; bool controlJoy@1; bool controlChat@2 }. Dispatched via the global
        // DispatchEvent(in T) at both call sites — global hook, not byNetId.
        private const string CanvasSnapStateEventName = "XDTGame.UI.CanvasSnapStateRequestedEvent";
        private const int CanvasSnapStateEventPayloadBytes = 3;

        // A torn-down capture coroutine never dispatches active=false. The game re-derives its own
        // layer state elsewhere, but we would hold the mod's surfaces shut for the session, so a
        // stuck snap-active is force-restored after this many seconds. Watchdog and restore run
        // OUTSIDE any FeatureBreakerState on purpose: a breaker that stopped the tick would also
        // stop the restore.
        private const float PhotoSnapUiWatchdogSeconds = 10f;

        private bool hideModUiInPhotos = true;
        private bool hideModEspInPhotos = true;

        // Latched once. RegisterGameEventHook is metadata-only (the engine installs the detour from
        // its own world-ready gate); a refusal is permanent (slot budget), so there is no retry timer.
        private bool photoSnapUiHookRegistered;

        private bool photoSnapUiSnapWanted;
        private float photoSnapUiSnapWantedAt;
        private int photoSnapUiSnapEpoch = -1;
        private bool photoSnapUiWatchdogLogged;
        private bool photoSnapUiApplyFailureLogged;

        private enum PhotoSnapUiRootKind
        {
            Ui,  // windows, toasts, status readout — gated by hideModUiInPhotos
            Esp, // screen-space ESP overlay, world-space radar markers — gated by hideModEspInPhotos
        }

        private sealed class PhotoSnapUiRootEntry
        {
            public GameObject Root;
            public PhotoSnapUiRootKind Kind;
            public bool SavedActiveSelf; // captured at the transition into the hidden state
            public bool HiddenForSnap;
        }

        // Populated ONLY from the mod's own root-creation chokepoints (CreateUguiWindow, the toast /
        // status-overlay / mod-overlay build functions, RunRadar) — never by GameObject.Find. Main
        // thread writes on both sides; no lock.
        private readonly List<PhotoSnapUiRootEntry> photoSnapUiRoots = new List<PhotoSnapUiRootEntry>(12);

        private void RegisterPhotoSnapUiRoot(GameObject root, PhotoSnapUiRootKind kind)
        {
            if (root == null)
            {
                return;
            }

            for (int i = 0; i < this.photoSnapUiRoots.Count; i++)
            {
                if (this.photoSnapUiRoots[i].Root == root)
                {
                    return; // idempotent — a re-run build of the same surface must not duplicate
                }
            }

            this.photoSnapUiRoots.Add(new PhotoSnapUiRootEntry { Root = root, Kind = kind });
        }

        private void EnsurePhotoSnapUiHookRegistered()
        {
            if (this.photoSnapUiHookRegistered)
            {
                return;
            }

            this.photoSnapUiHookRegistered = true;

            if (!this.RegisterGameEventHook(
                    CanvasSnapStateEventName, CanvasSnapStateEventPayloadBytes, this.OnCanvasSnapStateEventRequested))
            {
                ModLogger.Warning("[CleanPhotos] CanvasSnapStateRequestedEvent hook refused —"
                    + " mod UI will still appear in in-game photos.");
            }
        }

        // Runs on the main thread from the OnUpdate hook drain (ProcessGameEventHooksOnUpdate), the
        // same frame the game dispatched active=true. Records intent only — the Unity side-effects are
        // applied in ProcessPhotoSnapUiOnLateUpdate, before that frame's canvas render.
        private void OnCanvasSnapStateEventRequested(GameEventSnapshot e)
        {
            bool active = e.ReadBool(0);
            if (active && !this.photoSnapUiSnapWanted)
            {
                this.photoSnapUiSnapWantedAt = Time.time;
                this.photoSnapUiSnapEpoch = AuraMonoWorldEpoch;
                this.photoSnapUiWatchdogLogged = false;
            }

            this.photoSnapUiSnapWanted = active;
        }

        // Called as the LAST line of OnLateUpdate. After ProcessUguiOverlayFrame (which re-activates
        // children — invisible while the root is off anyway) and after the OnUpdate feature drivers,
        // yet still in PreLateUpdate, before the canvas render subsystems compose the frame.
        private void ProcessPhotoSnapUiOnLateUpdate()
        {
            try
            {
                this.EnsurePhotoSnapUiHookRegistered();

                bool wanted = this.photoSnapUiSnapWanted;
                if (wanted && (AuraMonoWorldEpoch != this.photoSnapUiSnapEpoch
                    || Time.time - this.photoSnapUiSnapWantedAt > PhotoSnapUiWatchdogSeconds))
                {
                    this.photoSnapUiSnapWanted = false;
                    wanted = false;
                    if (!this.photoSnapUiWatchdogLogged)
                    {
                        this.photoSnapUiWatchdogLogged = true;
                        ModLogger.Warning("[CleanPhotos] snap-active never closed (world change or torn-down"
                            + " capture coroutine) — mod UI force-restored.");
                    }
                }

                bool gateUi = this.hideModUiInPhotos;
                bool gateEsp = this.hideModEspInPhotos;
                List<PhotoSnapUiRootEntry> roots = this.photoSnapUiRoots;

                // Reverse walk: rebuilt/destroyed roots are removed while iterating.
                for (int i = roots.Count - 1; i >= 0; i--)
                {
                    PhotoSnapUiRootEntry entry = roots[i];
                    if (entry.Root == null)
                    {
                        roots.RemoveAt(i); // destroyed by a theme rebuild or a world change
                        continue;
                    }

                    bool gated = entry.Kind == PhotoSnapUiRootKind.Ui ? gateUi : gateEsp;
                    if (wanted && gated)
                    {
                        if (!entry.HiddenForSnap)
                        {
                            // Transition for this root: save its real state ONCE — including a root
                            // created mid-snap (theme rebuild), whose activeSelf at first hide IS its
                            // pre-snap state.
                            entry.SavedActiveSelf = entry.Root.activeSelf;
                            entry.HiddenForSnap = true;
                        }

                        if (entry.Root.activeSelf)
                        {
                            entry.Root.SetActive(false); // re-hide: the OnUpdate drivers may have re-shown it
                        }
                    }
                    else if (entry.HiddenForSnap)
                    {
                        // Transition to shown: replay the saved state. A root the user had closed
                        // stays closed. A window opened via Insert DURING the snap restores to its
                        // PRE-SNAP state (usually closed) — the snap window is two or three frames
                        // and re-showing the menu into a just-captured backbuffer is exactly what
                        // this feature exists to prevent.
                        entry.Root.SetActive(entry.SavedActiveSelf);
                        entry.HiddenForSnap = false;
                    }
                }
            }
            catch (Exception ex)
            {
                if (!this.photoSnapUiApplyFailureLogged)
                {
                    this.photoSnapUiApplyFailureLogged = true;
                    ModLogger.Warning("[CleanPhotos] apply failed: " + ex.Message);
                }
            }
        }
    }
}
