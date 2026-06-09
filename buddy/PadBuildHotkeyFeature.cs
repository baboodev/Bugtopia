using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        private const bool PadBuildHotkeyLogsEnabled = MasterLogPadBuild;
        private const float PadBuildRotateDebounceSeconds = 0.25f;

        // Pad-build hotkeys drive the BuildStatusPanel buttons directly via UI (GameObject.Find +
        // CustomButton click / pointer simulation). The API route (BuildModule.ConfirmPlacing etc.)
        // is NOT usable on this IL2CppMono build: BuildModule's assembly is invisible to the mono
        // class/name APIs (so the Type can't be built for Managers.GetModule(Type), and Type.GetType
        // hard-crashes), and the Managers._moduleDic ValueCollection does not enumerate via AuraMono.
        // The UI path needs no module resolution, so it works in every build mode — and the move/
        // delete buttons only exist while focused, so those hotkeys are silent no-ops in free-roam.

        private static readonly string[] PadBuildPanelRootPaths =
        {
            "GameApp/startup_root(Clone)/XDUIRoot/Bottom/BuildStatusPanel(Clone)",
            "GameApp/startup_root(Clone)/XDUIRoot/Status/BuildStatusPanel(Clone)",
            "GameApp/startup_root(Clone)/XDUIRoot/Full/BuildStatusPanel(Clone)",
            "BuildStatusPanel(Clone)"
        };

        private static readonly string[] PadBuildConfirmRelativePaths =
        {
            "AniRoot@ani@queueanimation/Bottom/confirm_bar@go/confirm@swap@go",
            "AniRoot@ani@queueanimation/Bottom/confirm_bar@go/confirm@swap"
        };

        private static readonly string[] PadBuildCancelRelativePaths =
        {
            "AniRoot@ani@queueanimation/Bottom/confirm_bar@go/cancel@btn"
        };

        private static readonly string[] PadBuildRotateRelativePaths =
        {
            "AniRoot@ani@queueanimation/Bottom/skills@go/interact_rotate@btn"
        };

        private static readonly string[] PadBuildMoveRelativePaths =
        {
            "AniRoot@ani@queueanimation/Bottom/skills@go/interact_move@btn"
        };

        // Delete = remove the focused object. For furniture that's "pack furniture" (move to
        // backpack); "wreck stable" only applies to structures (walls/floors, god mode). Try the
        // pack button first, fall back to wreck — whichever is currently active gets clicked.
        private static readonly string[] PadBuildDeleteRelativePaths =
        {
            "AniRoot@ani@queueanimation/Bottom/skills@go/interact_pack_furniture@btn",
            "AniRoot@ani@queueanimation/Bottom/skills@go/interact_wreck_stable@btn"
        };

        private float padBuildRotateLastAt = -999f;

        private void ProcessPadBuildHotkeysOnUpdate()
        {
            if (this.TryGetModHotkeyDown(this.keyPadConfirm))
            {
                if (!this.TryPadBuildConfirm(out string confirmStatus))
                {
                    this.PadBuildHotkeyLog("confirm skipped: " + confirmStatus);
                }
            }

            if (this.TryGetModHotkeyDown(this.keyPadCancel))
            {
                if (!this.TryPadBuildCancel(out string cancelStatus))
                {
                    this.PadBuildHotkeyLog("cancel skipped: " + cancelStatus);
                }
            }

            if (this.TryGetModHotkeyDown(this.keyPadRotate))
            {
                if (!this.TryPadBuildRotate(out string rotateStatus))
                {
                    this.PadBuildHotkeyLog("rotate skipped: " + rotateStatus);
                }
            }

            if (this.TryGetModHotkeyDown(this.keyPadMove))
            {
                if (!this.TryPadBuildMove(out string moveStatus))
                {
                    this.PadBuildHotkeyLog("move skipped: " + moveStatus);
                }
            }

            if (this.TryGetModHotkeyDown(this.keyPadDelete))
            {
                if (!this.TryPadBuildDelete(out string deleteStatus))
                {
                    this.PadBuildHotkeyLog("delete skipped: " + deleteStatus);
                }
            }
        }

        private bool TryPadBuildConfirm(out string status)
        {
            status = string.Empty;
            if (!this.TryIsPadBuildUiActive(out status))
            {
                return false;
            }

            GameObject confirmObj = this.TryFindPadBuildUiObject(PadBuildConfirmRelativePaths);
            if (confirmObj == null)
            {
                status = "confirm button not found";
                return false;
            }

            if (!this.TrySimulatePadBuildSwapConfirm(confirmObj))
            {
                status = "confirm simulate failed";
                return false;
            }

            status = "ui confirm";
            this.PadBuildHotkeyLog(status);
            return true;
        }

        private bool TryPadBuildCancel(out string status)
        {
            status = string.Empty;
            if (!this.TryIsPadBuildUiActive(out status))
            {
                return false;
            }

            if (!this.TryClickPadBuildUiButton(PadBuildCancelRelativePaths, out status))
            {
                if (status.Length == 0)
                {
                    status = "cancel button not found";
                }

                return false;
            }

            this.PadBuildHotkeyLog(status);
            return true;
        }

        private bool TryPadBuildRotate(out string status)
        {
            status = string.Empty;
            float now = Time.unscaledTime;
            if (now - this.padBuildRotateLastAt < PadBuildRotateDebounceSeconds)
            {
                return false;
            }

            if (!this.TryIsPadBuildUiActive(out status))
            {
                return false;
            }

            if (!this.TryClickPadBuildUiButton(PadBuildRotateRelativePaths, out status))
            {
                if (status.Length == 0)
                {
                    status = "rotate button not found";
                }

                return false;
            }

            this.padBuildRotateLastAt = now;
            this.PadBuildHotkeyLog(status);
            return true;
        }

        // Move/Delete buttons only exist while an item is focused (build mode), so in simple Pad
        // free-roam the click target is absent → silent no-op, exactly as requested.
        private bool TryPadBuildMove(out string status)
        {
            status = string.Empty;
            if (!this.TryClickPadBuildUiButton(PadBuildMoveRelativePaths, out status))
            {
                if (status.Length == 0)
                {
                    status = "move button not active";
                }

                return false;
            }

            this.PadBuildHotkeyLog(status);
            return true;
        }

        private bool TryPadBuildDelete(out string status)
        {
            status = string.Empty;
            if (!this.TryClickPadBuildUiButton(PadBuildDeleteRelativePaths, out status))
            {
                if (status.Length == 0)
                {
                    status = "delete button not active";
                }

                return false;
            }

            this.PadBuildHotkeyLog(status);
            return true;
        }

        private bool TryIsPadBuildUiActive(out string status)
        {
            status = "build ui inactive";
            GameObject panelRoot = this.TryFindPadBuildPanelRoot();
            if (panelRoot == null)
            {
                return false;
            }

            GameObject confirmObj = this.TryFindPadBuildUiObject(PadBuildConfirmRelativePaths);
            if (confirmObj != null && confirmObj.activeInHierarchy)
            {
                status = "confirm visible";
                return true;
            }

            GameObject rotateObj = this.TryFindPadBuildUiObject(PadBuildRotateRelativePaths);
            if (rotateObj != null && rotateObj.activeInHierarchy)
            {
                status = "rotate visible";
                return true;
            }

            return false;
        }

        private GameObject TryFindPadBuildPanelRoot()
        {
            for (int i = 0; i < PadBuildPanelRootPaths.Length; i++)
            {
                GameObject candidate = GameObject.Find(PadBuildPanelRootPaths[i]);
                if (candidate != null && candidate.activeInHierarchy)
                {
                    return candidate;
                }
            }

            return null;
        }

        private GameObject TryFindPadBuildUiObject(string[] relativePaths)
        {
            if (relativePaths == null || relativePaths.Length == 0)
            {
                return null;
            }

            GameObject panelRoot = this.TryFindPadBuildPanelRoot();
            if (panelRoot != null)
            {
                for (int i = 0; i < relativePaths.Length; i++)
                {
                    Transform child = panelRoot.transform.Find(relativePaths[i]);
                    if (child != null)
                    {
                        return child.gameObject;
                    }
                }
            }

            for (int i = 0; i < PadBuildPanelRootPaths.Length; i++)
            {
                string panelRootPath = PadBuildPanelRootPaths[i];
                for (int j = 0; j < relativePaths.Length; j++)
                {
                    GameObject candidate = GameObject.Find(panelRootPath + "/" + relativePaths[j]);
                    if (candidate != null)
                    {
                        return candidate;
                    }
                }
            }

            for (int j = 0; j < relativePaths.Length; j++)
            {
                string leafName = relativePaths[j];
                int slash = leafName.LastIndexOf('/');
                if (slash >= 0 && slash < leafName.Length - 1)
                {
                    leafName = leafName.Substring(slash + 1);
                }

                GameObject byName = GameObject.Find(leafName);
                if (byName != null && byName.activeInHierarchy)
                {
                    return byName;
                }
            }

            return null;
        }

        // Clicks the first active+clickable button among the given paths. Iterating per path (rather
        // than TryFindPadBuildUiObject over the whole set) matters when several candidates exist in
        // the hierarchy but only one is active — e.g. delete's pack/wreck pair.
        private bool TryClickPadBuildUiButton(string[] relativePaths, out string status)
        {
            status = string.Empty;
            if (relativePaths == null)
            {
                return false;
            }

            for (int i = 0; i < relativePaths.Length; i++)
            {
                GameObject target = this.TryFindPadBuildUiObject(new[] { relativePaths[i] });
                if (target == null || !target.activeInHierarchy)
                {
                    continue;
                }

                Button button = this.ResolveClickableButton(target);
                if (button != null && button.interactable)
                {
                    button.onClick.Invoke();
                    status = "ui click " + target.name;
                    return true;
                }

                if (this.SimulateClick(target))
                {
                    status = "ui simulate " + target.name;
                    return true;
                }
            }

            return false;
        }

        private bool TrySimulatePadBuildSwapConfirm(GameObject target)
        {
            if (target == null || !target.activeInHierarchy)
            {
                return false;
            }

            try
            {
                EventSystem eventSystem = this.EnsureGameplayEventSystemAvailable();
                PointerEventData eventData = new PointerEventData(eventSystem)
                {
                    position = RectTransformUtility.WorldToScreenPoint(null, target.transform.position)
                };

                ExecuteEvents.Execute(target, eventData, ExecuteEvents.pointerDownHandler);
                ExecuteEvents.Execute(target, eventData, ExecuteEvents.pointerUpHandler);
                return true;
            }
            catch (Exception ex)
            {
                this.PadBuildHotkeyLog("confirm simulate error: " + ex.Message);
                return false;
            }
        }

        private void PadBuildHotkeyLog(string message)
        {
            if (!PadBuildHotkeyLogsEnabled)
            {
                return;
            }

            ModLogger.Msg("[PadBuild] " + message);
        }
    }
}
