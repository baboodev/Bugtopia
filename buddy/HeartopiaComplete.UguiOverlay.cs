using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace HeartopiaMod
{
    // Retained-mode replacement for the last three IMGUI surfaces (ESP tags, debug ESP, mouse-look
    // crosshair). This retired `OnGUI` entirely; the injected MonoBehaviour still stays, because it
    // remains the only source of a per-frame tick that Il2CppInterop calls are legal from.
    //
    // WHY POOLING: the old comment in HeartopiaComplete.Gui.cs argued UGUI would be "pure churn" for
    // per-frame ESP. That is only true if you instantiate/destroy per frame. We never do: elements are
    // created once, parked inactive, and re-leased each frame — so a steady-state frame does nothing
    // but write properties on existing components. IMGUI by contrast re-emits every quad every frame,
    // so this is if anything cheaper.
    //
    // COORDINATES: the canvas is ScreenSpaceOverlay with NO CanvasScaler, so 1 unit == 1 pixel and
    // `Screen.width/height` mean the same thing they did under IMGUI. Every element is anchored to
    // the TOP-LEFT with pivot (0,1), so an IMGUI `Rect(x, y, w, h)` (y measured downward from the top)
    // maps to `anchoredPosition = (x, -y)` and `sizeDelta = (w, h)` — a 1:1 port with no flipping at
    // the call sites.
    //
    // INPUT SAFETY: the canvas gets NO GraphicRaycaster and every element sets `raycastTarget = false`,
    // so this overlay can never intercept a click. That matters — it sits above the game's UI, and the
    // click-blocker overlay (sortingOrder 20000, HeartopiaComplete.CameraInput.cs) is a separate,
    // deliberately-raycastable surface. We stay just below it and far below the mod's own windows
    // (29400/29500) so menus always draw over the ESP.
    public partial class HeartopiaComplete
    {
        private const int UguiOverlaySortingOrder = 19000;
        private const string UguiOverlayRootName = "HeartopiaModOverlay";

        private GameObject uguiOverlayRoot;
        private Transform uguiOverlayLayer;
        private readonly List<RawImage> uguiOverlayImagePool = new List<RawImage>();
        private readonly List<Text> uguiOverlayTextPool = new List<Text>();
        private int uguiOverlayImagesUsed;
        private int uguiOverlayTextsUsed;
        private bool uguiOverlayHardFailed;
        private bool uguiOverlayFrameOpen;

        // ---- lifecycle -------------------------------------------------------------------------

        private bool EnsureUguiOverlayCanvas()
        {
            if (this.uguiOverlayHardFailed)
            {
                return false;
            }

            // A world change can destroy the root even though it is DontDestroyOnLoad (and the pooled
            // children go with it), so re-check every frame and rebuild from scratch when it is gone.
            if (this.uguiOverlayRoot != null && this.uguiOverlayLayer != null)
            {
                return true;
            }

            try
            {
                this.uguiOverlayImagePool.Clear();
                this.uguiOverlayTextPool.Clear();
                this.uguiOverlayImagesUsed = 0;
                this.uguiOverlayTextsUsed = 0;

                GameObject go = new GameObject(UguiOverlayRootName);
                UnityEngine.Object.DontDestroyOnLoad(go);

                Canvas canvas = go.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.overrideSorting = true;
                canvas.sortingOrder = UguiOverlaySortingOrder;
                // Deliberately NO GraphicRaycaster and no CanvasScaler: no input, no scaling.

                this.uguiOverlayRoot = go;
                this.uguiOverlayLayer = go.transform;
                ModLogger.Msg("[UguiOverlay] canvas built (sortingOrder " + UguiOverlaySortingOrder + ")");
                return true;
            }
            catch (Exception ex)
            {
                this.uguiOverlayHardFailed = true;
                this.uguiOverlayRoot = null;
                this.uguiOverlayLayer = null;
                ModLogger.Msg("[UguiOverlay] canvas build failed, overlay disabled: " + ex.Message);
                return false;
            }
        }

        // ---- frame protocol --------------------------------------------------------------------
        // BeginUguiOverlayFrame() -> any number of UguiOverlayDraw* calls -> EndUguiOverlayFrame().
        // End() parks everything that was not leased this frame, which is what makes a shrinking
        // number of markers actually disappear.

        private bool BeginUguiOverlayFrame()
        {
            if (!this.EnsureUguiOverlayCanvas())
            {
                return false;
            }

            this.uguiOverlayImagesUsed = 0;
            this.uguiOverlayTextsUsed = 0;
            this.uguiOverlayFrameOpen = true;
            return true;
        }

        private void EndUguiOverlayFrame()
        {
            if (!this.uguiOverlayFrameOpen)
            {
                return;
            }

            this.uguiOverlayFrameOpen = false;

            for (int i = this.uguiOverlayImagesUsed; i < this.uguiOverlayImagePool.Count; i++)
            {
                RawImage img = this.uguiOverlayImagePool[i];
                if (img != null && img.gameObject.activeSelf)
                {
                    img.gameObject.SetActive(false);
                }
            }

            for (int i = this.uguiOverlayTextsUsed; i < this.uguiOverlayTextPool.Count; i++)
            {
                Text t = this.uguiOverlayTextPool[i];
                if (t != null && t.gameObject.activeSelf)
                {
                    t.gameObject.SetActive(false);
                }
            }
        }

        // ---- IMGUI-shaped primitives -----------------------------------------------------------

        // Equivalent of GUI.DrawTexture(rect, texture) with GUI.color applied.
        private void UguiOverlayDrawTexture(Rect rect, Texture texture, Color color)
        {
            RawImage img = this.AcquireUguiOverlayImage();
            if (img == null)
            {
                return;
            }

            img.texture = texture;
            img.color = color;
            PlaceUguiOverlayRect(img.rectTransform, rect);
            img.rectTransform.localRotation = Quaternion.identity;
        }

        // Equivalent of GUI.DrawTexture(rect, texture, ScaleMode.ScaleToFit, true): letterbox the
        // texture inside `rect` preserving aspect. RawImage has no ScaleMode, so we shrink the rect.
        private void UguiOverlayDrawTextureFit(Rect rect, Texture texture, Color color)
        {
            if (texture == null || texture.width <= 0 || texture.height <= 0 || rect.width <= 0f || rect.height <= 0f)
            {
                return;
            }

            float scale = Mathf.Min(rect.width / texture.width, rect.height / texture.height);
            float w = texture.width * scale;
            float h = texture.height * scale;
            Rect fitted = new Rect(rect.x + (rect.width - w) * 0.5f, rect.y + (rect.height - h) * 0.5f, w, h);
            this.UguiOverlayDrawTexture(fitted, texture, color);
        }

        // Replacement for the IMGUI connector line, which drew a horizontal quad and span it with
        // GUIUtility.RotateAroundPivot(angle, from). Here the quad's LOCAL +X is aimed straight at
        // `to` and the pivot sits on `from`, so no angle-sign bookkeeping is needed: in overlay space
        // an IMGUI point (x, y) is (x, -y), hence the -dy in the atan2.
        private void UguiOverlayDrawLine(Vector2 from, Vector2 to, Color color, float thickness)
        {
            RawImage img = this.AcquireUguiOverlayImage();
            if (img == null)
            {
                return;
            }

            float dx = to.x - from.x;
            float dy = to.y - from.y;
            float length = Mathf.Sqrt(dx * dx + dy * dy);
            if (length <= 0.01f)
            {
                return;
            }

            img.texture = Texture2D.whiteTexture;
            img.color = color;
            RectTransform rt = img.rectTransform;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 0.5f);          // rotate about `from`, centred on the stroke
            rt.sizeDelta = new Vector2(length, thickness);
            rt.anchoredPosition = new Vector2(from.x, -from.y);
            rt.localRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(-dy, dx) * Mathf.Rad2Deg);
        }

        // Equivalent of GUI.Label(rect, text, style).
        private void UguiOverlayDrawLabel(Rect rect, string text, Color color, int fontSize, TextAnchor anchor, bool bold = false)
        {
            Text t = this.AcquireUguiOverlayText();
            if (t == null)
            {
                return;
            }

            t.text = text ?? string.Empty;
            t.color = color;
            t.fontSize = fontSize;
            t.alignment = anchor;
            t.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
            PlaceUguiOverlayRect(t.rectTransform, rect);
            t.rectTransform.localRotation = Quaternion.identity;
        }

        private static void PlaceUguiOverlayRect(RectTransform rt, Rect rect)
        {
            // Top-left anchored, top-left pivot => IMGUI's downward Y maps to -y directly.
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(rect.width, rect.height);
            rt.anchoredPosition = new Vector2(rect.x, -rect.y);
        }

        // ---- pools -----------------------------------------------------------------------------

        private RawImage AcquireUguiOverlayImage()
        {
            try
            {
                if (this.uguiOverlayImagesUsed < this.uguiOverlayImagePool.Count)
                {
                    RawImage existing = this.uguiOverlayImagePool[this.uguiOverlayImagesUsed];
                    if (existing != null)
                    {
                        this.uguiOverlayImagesUsed++;
                        if (!existing.gameObject.activeSelf)
                        {
                            existing.gameObject.SetActive(true);
                        }

                        return existing;
                    }

                    // Destroyed behind our back — drop the whole pool and rebuild lazily.
                    this.uguiOverlayImagePool.RemoveAt(this.uguiOverlayImagesUsed);
                }

                GameObject go = new GameObject("ovl_img");
                go.transform.SetParent(this.uguiOverlayLayer, false);
                RawImage img = go.AddComponent<RawImage>();
                img.raycastTarget = false;
                this.uguiOverlayImagePool.Add(img);
                this.uguiOverlayImagesUsed++;
                return img;
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiOverlay] image lease failed: " + ex.Message);
                return null;
            }
        }

        private Text AcquireUguiOverlayText()
        {
            try
            {
                if (this.uguiOverlayTextsUsed < this.uguiOverlayTextPool.Count)
                {
                    Text existing = this.uguiOverlayTextPool[this.uguiOverlayTextsUsed];
                    if (existing != null)
                    {
                        this.uguiOverlayTextsUsed++;
                        if (!existing.gameObject.activeSelf)
                        {
                            existing.gameObject.SetActive(true);
                        }

                        return existing;
                    }

                    this.uguiOverlayTextPool.RemoveAt(this.uguiOverlayTextsUsed);
                }

                GameObject go = new GameObject("ovl_txt");
                go.transform.SetParent(this.uguiOverlayLayer, false);
                Text t = go.AddComponent<Text>();
                // Same font the IMGUI overlays inherited via GUI.skin.font, so glyph coverage
                // (incl. CJK) matches what the ESP rendered before.
                if (this.uguiKitLegacyFont != null)
                {
                    t.font = this.uguiKitLegacyFont;
                }

                t.supportRichText = false;
                t.raycastTarget = false;
                t.horizontalOverflow = HorizontalWrapMode.Overflow;
                t.verticalOverflow = VerticalWrapMode.Overflow;
                this.uguiOverlayTextPool.Add(t);
                this.uguiOverlayTextsUsed++;
                return t;
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiOverlay] text lease failed: " + ex.Message);
                return null;
            }
        }

        // ---- per-frame driver ------------------------------------------------------------------
        // Runs from OnLateUpdate: the ESP projects world positions through Camera.main, so it must
        // sample AFTER the camera has moved this frame — the same reason Unity puts camera-dependent
        // work in LateUpdate. Under IMGUI this was implicit (OnGUI runs after everything).
        private void ProcessUguiOverlayFrame()
        {
            try
            {
                if (!this.BeginUguiOverlayFrame())
                {
                    return;
                }

                // Same order the IMGUI OnGUI used: both ESP overlays, then the crosshair on top.
                this.DrawResourceVisualEspOverlay();
                this.DrawVisualDebugEspOverlay();
                this.DrawMouseLookCrosshairUgui();
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiOverlay] frame failed: " + ex.Message);
            }
            finally
            {
                this.EndUguiOverlayFrame();
            }
        }
    }
}
