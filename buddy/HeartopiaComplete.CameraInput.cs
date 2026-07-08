﻿using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Il2CppInterop.Runtime.Runtime;
using System;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

using UnityObject = UnityEngine.Object;
using Il2CppType = Il2CppSystem.Type;
using Il2CppFieldInfo = Il2CppSystem.Reflection.FieldInfo;
using Il2CppMethodInfo = Il2CppSystem.Reflection.MethodInfo;
using Il2CppPropertyInfo = Il2CppSystem.Reflection.PropertyInfo;
using Il2CppBindingFlags = Il2CppSystem.Reflection.BindingFlags;
using Il2CppObject = Il2CppSystem.Object;
using Object = UnityEngine.Object;


namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        private void DrawMouseLookCrosshair()
        {
            if (!this.mouseLookCaptureActive || !this.showMouseLookCrosshair)
            {
                return;
            }

            this.EnsureUiPrimitiveTextures();

            float centerX = Screen.width * 0.5f;
            float centerY = Screen.height * 0.5f;
            float dotSize = 4f;
            float gap = 6f;
            float armLength = 8f;
            float armThickness = 2f;

            Rect dot = new Rect(centerX - dotSize * 0.5f, centerY - dotSize * 0.5f, dotSize, dotSize);
            Rect top = new Rect(centerX - armThickness * 0.5f, centerY - gap - armLength, armThickness, armLength);
            Rect bottom = new Rect(centerX - armThickness * 0.5f, centerY + gap, armThickness, armLength);
            Rect left = new Rect(centerX - gap - armLength, centerY - armThickness * 0.5f, armLength, armThickness);
            Rect right = new Rect(centerX + gap, centerY - armThickness * 0.5f, armLength, armThickness);

            Color previous = GUI.color;

            GUI.color = new Color(0f, 0f, 0f, 0.4f);
            GUI.DrawTexture(new Rect(dot.x - 1f, dot.y - 1f, dot.width + 2f, dot.height + 2f), this.uiCircleTexture);
            GUI.DrawTexture(new Rect(top.x - 1f, top.y - 1f, top.width + 2f, top.height + 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(bottom.x - 1f, bottom.y - 1f, bottom.width + 2f, bottom.height + 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(left.x - 1f, left.y - 1f, left.width + 2f, left.height + 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(right.x - 1f, right.y - 1f, right.width + 2f, right.height + 2f), Texture2D.whiteTexture);

            GUI.color = new Color(1f, 1f, 1f, 0.95f);
            GUI.DrawTexture(dot, this.uiCircleTexture);
            GUI.DrawTexture(top, Texture2D.whiteTexture);
            GUI.DrawTexture(bottom, Texture2D.whiteTexture);
            GUI.DrawTexture(left, Texture2D.whiteTexture);
            GUI.DrawTexture(right, Texture2D.whiteTexture);

            GUI.color = previous;
        }

        private void UpdateGameUiClickBlockState()
        {
            // Block game clicks while the mod menu is open (+ a short grace period after it closes,
            // blockInputReleaseUntil). A click landing on the menu must never reach the game.
            //
            // Mechanism: a transparent full-screen uGUI overlay (Canvas + GraphicRaycaster + Image
            // with raycastTarget) sorted on top of every game canvas. The pointer raycast hits the
            // overlay (which has no handler → does nothing) instead of the game's world-input
            // component, so the click is neither passed through immediately NOR buffered. This
            // replaces the old EventSystem.enabled toggle, which caused a stale click to fire at the
            // current cursor when the EventSystem was re-enabled on menu close.
            //
            // autoBuy needs real game-UI clicks, so it stays excluded.
            // Also block while the cursor is over the auto move-panel (so clicking its toggles / drag
            // bar never leaks into the game world). blockInputReleaseUntil covers the release grace.
            bool overMovePanel = this.buildingMovePanelActive && this.buildingMovePanelMouseOver;
            bool overQuestAssistantWindow = this.questAssistantWindowVisible && this.questAssistantWindowMouseOver;
            bool shouldBlock = (this.showMenu || overMovePanel || overQuestAssistantWindow || Time.unscaledTime < this.blockInputReleaseUntil) && !this.autoBuyEnabled;
            if (overMovePanel || overQuestAssistantWindow)
            {
                this.blockInputReleaseUntil = Time.unscaledTime + 0.18f;
            }
            this.EnsureModClickBlockerOverlay(shouldBlock);
        }

        private void EnsureModClickBlockerOverlay(bool active)
        {
            try
            {
                if (this.modClickBlockerOverlay == null)
                {
                    if (!active)
                    {
                        return; // create lazily only when first needed
                    }

                    GameObject go = new GameObject("HeartopiaModClickBlocker");
                    Object.DontDestroyOnLoad(go);

                    Canvas canvas = go.AddComponent<Canvas>();
                    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                    canvas.overrideSorting = true;
                    canvas.sortingOrder = 32000; // above all game canvases

                    go.AddComponent<UnityEngine.UI.GraphicRaycaster>();

                    UnityEngine.UI.Image img = go.AddComponent<UnityEngine.UI.Image>();
                    img.color = new Color(0f, 0f, 0f, 0f); // fully transparent
                    img.raycastTarget = true;
                    RectTransform rt = img.rectTransform;
                    rt.anchorMin = Vector2.zero;
                    rt.anchorMax = Vector2.one;
                    rt.offsetMin = Vector2.zero;
                    rt.offsetMax = Vector2.zero;

                    this.modClickBlockerOverlay = go;
                }

                if (this.modClickBlockerOverlay != null && this.modClickBlockerOverlay.activeSelf != active)
                {
                    this.modClickBlockerOverlay.SetActive(active);
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[ClickBlocker] overlay error: " + ex.Message);
            }
        }

        private void UpdateMenuMovementInputBlock()
        {
            bool shouldBlock = ShouldBlockGameplayInput();
            if (shouldBlock == this.menuMoveInputDisabled)
            {
                return;
            }

            // InputEvent.Move == 0 (ScriptsRefactory.BaseService.Input.InputEvent).
            const int InputEventMove = 0;
            if (shouldBlock)
            {
                // Only flip our state once the disable actually lands, so we don't leave an
                // unbalanced Enable later if the input manager wasn't ready yet (retry next frame).
                if (this.TrySetMonoInputDisabled(InputEventMove, true))
                {
                    this.menuMoveInputDisabled = true;
                }
            }
            else
            {
                this.TrySetMonoInputDisabled(InputEventMove, false);
                this.menuMoveInputDisabled = false;
            }
        }

        public static bool ShouldForceMouseLookButton(int button)
        {
            return false;
        }

        public static bool ShouldForceMouseLookButtonDown(int button)
        {
            return false;
        }

        public static bool ShouldForceMouseLookButtonUp(int button)
        {
            return false;
        }

        private void UpdateCameraToggleInteractClick()
        {
            if (!this.mouseLookCaptureActive || this.showMenu)
            {
                return;
            }

            if (Time.unscaledTime < this.nextCameraToggleInteractAt)
            {
                return;
            }

            if (!Input.GetMouseButton(0))
            {
                return;
            }

            this.nextCameraToggleInteractAt = Time.unscaledTime + 0.15f;
            this.DirectClickInteractButton();
        }

        private void ResetMouseLookOrbitState()
        {
            this.mouseLookOrbitInitialized = false;
        }

        // Mouse-look free-cam: drives the game camera controller's yaw/pitch axis (see
        // HeartopiaComplete.CameraRig.cs) instead of writing Camera.main.transform. Runs from
        // OnUpdate so the axis values land before the game's XDTCameraManager.LateUpdate, which
        // then poses Camera.main from them itself — no Transform setter patches, no direct
        // camera writes, no pin frames.
        private void UpdateDirectMouseLookCamera(bool shouldCapture)
        {
            if (!shouldCapture)
            {
                this.ResetMouseLookOrbitState();
                return;
            }

            if (!this.TryResolveCameraControllerAxis(
                    out IntPtr controllerObj,
                    out IntPtr getAxisX,
                    out IntPtr getAxisY,
                    out IntPtr setAxisX,
                    out IntPtr setAxisY))
            {
                // Current camera state is not axis-capable (fixed/cutscene/transition camera) —
                // mouse-look correctly does not apply; re-seed once an axis controller returns.
                this.mouseLookOrbitInitialized = false;
                return;
            }

            // Seed the accumulators from the live axis on the capture edge (and after any camera
            // state that took the axis away) so the first mouse move continues from the current
            // camera pose instead of snapping.
            if (!this.mouseLookOrbitInitialized)
            {
                if (!this.TryReadCameraAxisValue(controllerObj, getAxisX, out float seedYaw)
                    || !this.TryReadCameraAxisValue(controllerObj, getAxisY, out float seedPitch))
                {
                    return;
                }

                this.mouseLookOrbitYaw = seedYaw;
                this.mouseLookOrbitPitch = seedPitch;
                this.mouseLookOrbitInitialized = true;
            }

            // axisX = camera yaw (tracks euler Y 1:1). axisY runs opposite to camera euler X
            // (pitch = 90 - axisValue), so mouse-up ADDS to the axis to keep the old
            // "mouse up looks up" feel. SetAxis*value Warp-clamps to the axis min/max.
            this.mouseLookOrbitYaw += Input.GetAxis("Mouse X") * 3f;
            this.mouseLookOrbitPitch += Input.GetAxis("Mouse Y") * 2.2f;
            if (!this.TryWriteCameraAxisValue(controllerObj, setAxisX, this.mouseLookOrbitYaw)
                || !this.TryWriteCameraAxisValue(controllerObj, setAxisY, this.mouseLookOrbitPitch))
            {
                this.mouseLookOrbitInitialized = false;
                return;
            }

            // Re-sync the accumulators to the Warp-clamped axis values so the pitch accumulator
            // can't wind up past the clamp (which would take miles of reverse mouse travel to
            // unwind while the on-screen camera sits pinned at the limit).
            if (this.TryReadCameraAxisValue(controllerObj, getAxisX, out float warpedYaw))
            {
                this.mouseLookOrbitYaw = warpedYaw;
            }

            if (this.TryReadCameraAxisValue(controllerObj, getAxisY, out float warpedPitch))
            {
                this.mouseLookOrbitPitch = warpedPitch;
            }
        }

        private void UpdateMouseLookState()
        {
            bool shouldCapture = this.mouseLookEnabled &&
                                 !this.showMenu &&
                                 Time.unscaledTime >= this.blockInputReleaseUntil;
            if (!shouldCapture && !this.mouseLookEnabled && !this.mouseLookWasCaptureActive && !this.mouseLookCaptureActive)
            {
                return;
            }

            if (shouldCapture && !this.mouseLookWasCaptureActive)
            {
                this.mouseLookOrbitInitialized = false;
            }

            if (shouldCapture)
            {
                Cursor.visible = false;
                Cursor.lockState = CursorLockMode.Locked;
            }
            else
            {
                Cursor.visible = true;
                Cursor.lockState = CursorLockMode.None;
            }

            this.mouseLookCaptureActive = shouldCapture;
            this.mouseLookWasCaptureActive = shouldCapture;
        }

        private void ApplyCameraFOV()
        {
            if (this.mainCamera == null)
            {
                this.mainCamera = Camera.main;
                if (this.mainCamera != null && this.originalFOV < 0f)
                {
                    this.originalFOV = this.mainCamera.fieldOfView;
                    this.liveCameraFOVBase = this.originalFOV;
                }
            }

            if (this.mainCamera != null)
            {
                float currentFOV = this.mainCamera.fieldOfView;
                if (this.liveCameraFOVBase < 0f)
                {
                    this.liveCameraFOVBase = currentFOV;
                }

                if (this.lastAppliedCustomCameraFOV < 0f || Mathf.Abs(currentFOV - this.lastAppliedCustomCameraFOV) > 0.05f)
                {
                    this.liveCameraFOVBase = currentFOV;
                }

                float customOffset = (this.originalFOV >= 0f) ? (this.cameraFOV - this.originalFOV) : 0f;
                float targetFOV = this.liveCameraFOVBase + customOffset;
                if (Mathf.Abs(currentFOV - targetFOV) > 0.01f)
                {
                    this.mainCamera.fieldOfView = targetFOV;
                }

                this.lastAppliedCustomCameraFOV = targetFOV;
            }
        }

        private void RestoreCameraFOV()
        {
            if (this.mainCamera == null || !this.mainCamera)
            {
                this.mainCamera = Camera.main;
            }

            if (this.mainCamera != null)
            {
                float restoreFOV = this.liveCameraFOVBase >= 0f ? this.liveCameraFOVBase : this.originalFOV;
                if (restoreFOV >= 0f)
                {
                    this.mainCamera.fieldOfView = restoreFOV;
                }
            }

            this.lastAppliedCustomCameraFOV = -1f;
        }

        // Auto-farm camera nudge: adds `degrees` to the game camera controller's yaw axis and
        // lets the game's own LateUpdate swing Camera.main around the player — replaces the old
        // manual orbit math + Transform setter patches + 60-frame pin (anti-cheat surface #4).
        private void RotateCameraAroundPlayer(float degrees = 180f)
        {
            if (!this.TryNudgeCameraAxisYaw(degrees))
            {
                ModLogger.Msg("[CAMERA] Failed to rotate - camera controller axis unavailable");
            }
        }

    }
}
