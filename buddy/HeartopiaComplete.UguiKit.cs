using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

using UnityObject = UnityEngine.Object;
using Object = UnityEngine.Object;

namespace HeartopiaMod
{
    // ============================================================================================
    // UGUI KIT — the reusable factory library for building the mod's UI on Unity's real UI system
    // (UnityEngine.UI + Canvas hierarchy) instead of legacy IMGUI. This is the UGUI analog of
    // HeartopiaComplete.UiKitPrimitives.cs: every future tab migration composes these factories
    // instead of hand-building GameObject/Component trees. Validated by HeartopiaComplete.UguiPoc.cs,
    // which is constructed ENTIRELY from these methods.
    //
    // Vocabulary mirrors the IMGUI redesign so the two systems line up during the migration:
    //   CreateUguiPrimaryButton / CreateUguiSecondaryButton / CreateUguiDangerButton
    //     <-> DrawPrimaryActionButton / DrawSecondaryActionButton / DrawDangerActionButton
    //   Header / Body / Muted / Value labels <-> uiHeader* / uiText* / uiSubTabText* / accent roles
    // Colors are read LIVE from the same ui* theme fields the IMGUI side uses (plus the same
    // GetUiTextOnAccent / GetUiControlFill helpers), so both rendering paths stay in step. Colors
    // are snapshotted at construction time — live theme reload is NOT wired (future work).
    //
    // Hard-won interop facts baked into this file (verified against gameassembly-dumps for THIS
    // game build, Unity 2020.3.13 — do not rediscover):
    //  - UnityEngine.UI survived IL2CPP stripping intact; TMPro namespace is UNprefixed.
    //  - TMP_Dropdown / TMP_InputField are STRIPPED — Dropdown caption/items must be legacy Text
    //    (Dropdown.captionText/itemText are typed Text), which is why the kit resolves a legacy
    //    Font alongside the TMP font.
    //  - Legacy UnityEngine.UI.InputField survived intact with compiled bodies (text /
    //    SetTextWithoutNotify / characterLimit setters; nested OnChangeEvent : UnityEvent<string>
    //    is a materialized instantiation whose AddListener path the game itself exercises) —
    //    RVA evidence in the CreateUguiInputField comment.
    //  - Dropdown.Show() hardcodes its popup sub-canvas to sortingOrder 30000 — window canvases
    //    must stay BELOW that. The mod's click-blocker overlay (HeartopiaComplete.CameraInput.cs)
    //    sits at 20000 — ABOVE every game canvas but BELOW every mod UGUI window canvas, so once a
    //    window is open the overlay only ever wins raycasts OUTSIDE the window's own bounds, never
    //    inside them. Any new window's sortingOrder must stay above 20000 (the blocker) and below
    //    30000 (the Dropdown ceiling) — do not reuse 32000 anywhere, that was the pre-fix value.
    //  - Only the 3-arg RectangleContainsScreenPoint(rect, point, Camera) overload exists; pass
    //    null for the camera on ScreenSpaceOverlay canvases.
    //  - Runtime scale = Canvas.scaleFactor (NOT RectTransform.localScale: legacy Text re-rasterizes
    //    crisply under scaleFactor, but would blur under localScale). No CanvasScaler exists on kit
    //    canvases, so the manual value is never overwritten.
    //  - Drag math: mouse deltas are SCREEN pixels, anchoredPosition is CANVAS units — divide by
    //    the current scaleFactor; clamp bounds divide the screen extents the same way and must
    //    re-clamp after every scale change (bounds tighten as scale grows).
    //  - Unity 2020.3 has no full generic sharing: UnityEvent<int>.AddListener (Dropdown) is not
    //    provably compiled into this binary — CreateUguiDropdown reports wiring success via an out
    //    flag so callers can poll the value as a fallback. bool/float instances provably exist.
    //  - ClassInjector.RegisterTypeInIl2Cpp remains unused/unproven in this codebase — window drag
    //    is POLLED (ProcessUguiWindowFrame) instead of IDragHandler components, matching the F9 path.
    //  - A Sliced Image is ONE CanvasRenderer mesh (9 quads, shared vertices) — the slice-seam bug
    //    class the IMGUI menu fought cannot occur, at any scale.
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // ----------------------------------------------------------------------------------------
        // Handles
        // ----------------------------------------------------------------------------------------

        // Per-window state so multiple kit windows can coexist (drag/scale state must never live
        // in singleton fields). Plain managed class; Unity objects inside are interop references.
        private sealed class UguiWindowHandle
        {
            public GameObject Root;
            public Canvas Canvas;
            public RectTransform PanelRt;   // add content under this (it is also a Transform)
            public RectTransform TitleBarRt;
            public Image BackdropSlab;      // hardcoded near-opaque base layer (see CreateUguiWindow)
            public Image BackdropTint;      // theme window-color layer on top of the slab
            public GameObject ScaleLabel;   // created by EnableUguiWindowScaleKeys
            public Vector2 Size;
            public float Scale = 1f;
            public KeyCode ScaleUpKey = KeyCode.None;
            public KeyCode ScaleDownKey = KeyCode.None;
            public bool DragActive;
            public Vector2 DragLastMouse;
            public int DragErrorCount;
        }

        private sealed class UguiTabBarHandle
        {
            public readonly List<Image> ButtonBgs = new List<Image>();
            public readonly List<GameObject> ButtonLabels = new List<GameObject>();
            public readonly List<Image> ButtonIcons = new List<Image>(); // entry may be null
            public readonly List<GameObject> ButtonUnderlines = new List<GameObject>();
            public readonly List<GameObject> Contents = new List<GameObject>();
            public int ActiveIndex = -1;
            public System.Action<int> OnChanged;
        }

        // ----------------------------------------------------------------------------------------
        // Kit state (shared caches)
        // ----------------------------------------------------------------------------------------

        private Texture2D uguiKitRoundedTex;
        private Sprite uguiKitRoundedSprite;
        private Texture2D uguiKitRingTex;
        private Sprite uguiKitRingSprite;
        private TMP_FontAsset uguiKitTmpFont;
        private Material uguiKitTmpMaterial; // our OWN preset — never the font asset's shared one
        private Sprite uguiKitCaretSprite;   // dropdown "▼" indicator
        private Texture2D uguiKitCaretTex;
        private Font uguiKitLegacyFont;
        private bool uguiKitFontResolveAttempted;
        private Sprite[] uguiKitIconSprites; // cache: one Sprite per NavIconPngBase64 index

        private const float UguiWindowTitleBarHeight = 58f;
        // Scale range matches IMGUI's UiScaleMin/UiScaleMax (HeartopiaComplete.cs). The max was
        // 2.0 before Phase 2e; now that the shell scale-syncs to the shared persisted uiScale
        // (whose IMGUI slider goes to 3.0x), a smaller kit ceiling would silently clamp UGUI
        // windows below the IMGUI menu for anyone above 2.0x — an avoidable visual mismatch.
        // Side effect: the PoC's PageUp/PageDown stepping can now reach 3.0x too (harmless).
        private const float UguiWindowScaleMin = 0.5f;
        private const float UguiWindowScaleMax = 3.0f;
        private const float UguiWindowVisibleMargin = 80f;  // canvas units kept on-screen when dragging
        private const float UguiWindowTitleAllowance = 24f; // how far the title bar may slip past the top

        // Slider knob edge, in canvas units. MUST stay the size of both the visible Knob image and
        // its parent Handle rect — Slider treats "pointer inside handleRect" as grab-don't-jump, so
        // any gap between the two becomes an invisible dead zone. See CreateUguiSlider.
        private const float UguiSliderKnobSize = 18f;

        // Tallest a dropdown popup may get before it scrolls instead of growing. Unity's Dropdown
        // only ever SHRINKS the instantiated list to its content (`if (extraSpace > 0)`), never
        // grows it past the template — so this cap is what keeps a long list on-screen. See
        // CreateUguiDropdown.
        private const float UguiDropdownMaxPopupHeight = 232f;

        // Fixed non-theme colors (mirroring literals the IMGUI side bakes into its textures).
        private static readonly Color UguiKitDangerFill = new Color(1f, 0.42f, 0.5f, 0.14f);
        private static readonly Color UguiKitDangerRing = new Color(1f, 0.42f, 0.5f, 0.24f);
        private static readonly Color UguiKitDangerText = new Color(1f, 0.56f, 0.63f, 1f);
        private static readonly Color UguiKitSecondaryRing = new Color(1f, 1f, 1f, 0.09f);
        private static readonly Color UguiKitHandleColor = new Color(0.88f, 0.92f, 0.97f, 1f);

        // Theme accessors — LIVE ui* fields, same source the IMGUI styles read.
        private Color UguiKitAccent() { return new Color(this.uiAccentR, this.uiAccentG, this.uiAccentB, 1f); }
        private Color UguiKitHeaderColor() { return new Color(this.uiHeaderR, this.uiHeaderG, this.uiHeaderB, 1f); }
        private Color UguiKitTextColor() { return new Color(this.uiTextR, this.uiTextG, this.uiTextB, 1f); }
        private Color UguiKitMutedColor() { return new Color(this.uiSubTabTextR, this.uiSubTabTextG, this.uiSubTabTextB, 1f); }
        // Full IMGUI slider range (0.15..1): the window backdrop slab under this tint keeps the
        // window readable even at the lowest theme alpha, so no extra floor is imposed here.
        private Color UguiKitWindowBg() { return new Color(this.uiWindowR, this.uiWindowG, this.uiWindowB, Mathf.Clamp(this.uiWindowAlpha, 0.15f, 1f)); }
        private Color UguiKitPanelBg() { return new Color(this.uiPanelR, this.uiPanelG, this.uiPanelB, Mathf.Clamp(this.uiPanelAlpha, 0.3f, 1f)); }
        // BUG FIX (2026-07-22): this used to hardcode alpha to 1f, silently ignoring the theme
        // editor's own "Content Alpha" slider (uiContentAlpha) on every one of this method's ~40
        // call sites across the whole shell — the content zone of every panel stayed fully opaque
        // no matter what the user set. IMGUI's own content background reads uiContentAlpha
        // directly with no extra floor (HeartopiaComplete.UiKit.cs:988); mirrored here.
        private Color UguiKitContentBg() { return new Color(this.uiContentR, this.uiContentG, this.uiContentB, Mathf.Clamp(this.uiContentAlpha, 0.15f, 1f)); }
        private Color UguiKitControlFill()
        {
            Color c = this.GetUiControlFill(); // same helper the IMGUI button/control fills use
            return new Color(c.r, c.g, c.b, 1f);
        }

        // ----------------------------------------------------------------------------------------
        // Core construction primitives
        // ----------------------------------------------------------------------------------------

        private GameObject CreateUguiGo(string name, Transform parent)
        {
            GameObject go = new GameObject(name);
            RectTransform rt = go.AddComponent<RectTransform>();
            rt.SetParent(parent, false);
            return go;
        }

        // Position a child by top-left offset inside its parent (y grows downward here).
        private static void PlaceUguiTopLeft(GameObject go, float x, float y, float w, float h)
        {
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(w, h);
            rt.anchoredPosition = new Vector2(x, -y);
        }

        private static void StretchUguiFill(GameObject go, float left, float top, float right, float bottom)
        {
            RectTransform rt = go.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
        }

        // Adds an Image; when `sliced`, uses the shared procedural rounded-rect sprite in Unity's
        // own 9-slice ("Sliced") mode. ppuMultiplier tightens the effective corner radius for
        // small elements (higher = tighter). raycastTarget defaults OFF; interactives opt in.
        private Image AddUguiImage(GameObject go, Color color, bool sliced, float ppuMultiplier)
        {
            Image img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            if (sliced && this.EnsureUguiRoundedSprite())
            {
                img.sprite = this.uguiKitRoundedSprite;
                img.type = Image.Type.Sliced;
                // ALWAYS set the multiplier explicitly, including 1.0. The window-bg regression's
                // symptom split was exactly "images with an explicitly-set multiplier render,
                // images left on the component default may not" — a runtime-AddComponent'd
                // Image's serialized-float default is not something the kit trusts across the
                // two loaders' interop assemblies anymore.
                try { img.pixelsPerUnitMultiplier = (ppuMultiplier > 0f) ? ppuMultiplier : 1f; } catch { }
            }
            return img;
        }

        // Rounded-outline ("ring") overlay on a stretched child GO — the UGUI equivalent of the
        // 1.2px rings the IMGUI theme bakes into its button/panel textures. One Graphic per GO,
        // so the ring cannot share the fill Image's GameObject.
        private void AddUguiRingOverlay(GameObject go, Color ringColor, float ppuMultiplier)
        {
            try
            {
                if (!this.EnsureUguiRingSprite())
                {
                    return; // fill-only look; purely cosmetic
                }
                GameObject ringGo = this.CreateUguiGo("Ring", go.transform);
                StretchUguiFill(ringGo, 0f, 0f, 0f, 0f);
                Image ring = ringGo.AddComponent<Image>();
                ring.sprite = this.uguiKitRingSprite;
                ring.type = Image.Type.Sliced;
                ring.color = ringColor;
                ring.raycastTarget = false;
                // Always set explicitly — see AddUguiImage.
                try { ring.pixelsPerUnitMultiplier = (ppuMultiplier > 0f) ? ppuMultiplier : 1f; } catch { }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiKit] ring overlay failed: " + ex.Message);
            }
        }

        // Procedural rounded-rect sprite with a 9-slice border — SDF math shared with the IMGUI
        // theme textures (HeartopiaComplete.UiKitPrimitives.cs). One 32px sprite skins every
        // control at every size; Sliced mode keeps corners un-stretched and seam-free.
        // Down-chevron for dropdowns. Generated rather than drawn with a "▼" glyph on purpose: the
        // caret must look identical no matter which font the user picks in Settings -> UI Theme,
        // and U+25BC is not guaranteed to exist in every font asset (a missing glyph would render
        // as a blank or a tofu box, i.e. a dropdown with no affordance again).
        private bool EnsureUguiCaretSprite()
        {
            if (this.uguiKitCaretSprite != null)
            {
                return true;
            }

            try
            {
                const int w = 16;
                const int h = 10;
                Texture2D tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Bilinear;
                // Solid triangle pointing DOWN. Row y=0 is the bottom in Unity texture space, so
                // the apex sits at y=0 and the base spans the top row; coverage is computed as a
                // soft edge (half-pixel feather) so the diagonals do not read as stair-steps.
                for (int y = 0; y < h; y++)
                {
                    // Half-width of the triangle at this row, widening towards the top.
                    float halfSpan = (y + 0.5f) / h * (w * 0.5f);
                    for (int x = 0; x < w; x++)
                    {
                        float dx = Mathf.Abs((x + 0.5f) - (w * 0.5f));
                        float a = Mathf.Clamp01(halfSpan - dx + 0.5f);
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                    }
                }
                tex.Apply();
                this.uguiKitCaretTex = tex;

                this.uguiKitCaretSprite = Sprite.Create(
                    tex, new Rect(0f, 0f, w, h), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                if (this.uguiKitCaretSprite != null)
                {
                    this.uguiKitCaretSprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
                }
                return this.uguiKitCaretSprite != null;
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiKit] caret sprite build failed: " + ex.Message);
                return false;
            }
        }

        private bool EnsureUguiRoundedSprite()
        {
            if (this.uguiKitRoundedSprite != null)
            {
                return true;
            }

            try
            {
                const int size = 32;
                const float radius = 10f;
                Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Bilinear;
                float half = size * 0.5f;
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float outside = UguiRoundedRectOutsideDistance(x, y, half, radius);
                        float a = Mathf.Clamp01(radius - outside + 0.5f);
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
                    }
                }
                tex.Apply();
                this.uguiKitRoundedTex = tex;

                const float border = 10f; // >= radius so every rounded corner lives in a slice corner
                this.uguiKitRoundedSprite = Sprite.Create(
                    tex,
                    new Rect(0f, 0f, size, size),
                    new Vector2(0.5f, 0.5f),
                    100f,
                    0,
                    SpriteMeshType.FullRect,
                    new Vector4(border, border, border, border));
                if (this.uguiKitRoundedSprite != null)
                {
                    this.uguiKitRoundedSprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
                }
                return this.uguiKitRoundedSprite != null;
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiKit] rounded sprite build failed (square-image fallback): " + ex.Message);
                return false;
            }
        }

        // Same contour as the rounded sprite, but only a ~1.5px edge band — used for rings.
        private bool EnsureUguiRingSprite()
        {
            if (this.uguiKitRingSprite != null)
            {
                return true;
            }

            try
            {
                const int size = 32;
                const float radius = 10f;
                const float ringWidth = 1.5f;
                Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
                tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
                tex.wrapMode = TextureWrapMode.Clamp;
                tex.filterMode = FilterMode.Bilinear;
                float half = size * 0.5f;
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float outside = UguiRoundedRectOutsideDistance(x, y, half, radius);
                        float aOuter = Mathf.Clamp01(radius - outside + 0.5f);
                        float aInner = Mathf.Clamp01(radius - outside - ringWidth + 0.5f);
                        tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(aOuter - aInner)));
                    }
                }
                tex.Apply();
                this.uguiKitRingTex = tex;

                const float border = 10f;
                this.uguiKitRingSprite = Sprite.Create(
                    tex,
                    new Rect(0f, 0f, size, size),
                    new Vector2(0.5f, 0.5f),
                    100f,
                    0,
                    SpriteMeshType.FullRect,
                    new Vector4(border, border, border, border));
                if (this.uguiKitRingSprite != null)
                {
                    this.uguiKitRingSprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
                }
                return this.uguiKitRingSprite != null;
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiKit] ring sprite build failed: " + ex.Message);
                return false;
            }
        }

        private static float UguiRoundedRectOutsideDistance(int x, int y, float half, float radius)
        {
            float dx = Mathf.Abs(x + 0.5f - half) - (half - radius);
            float dy = Mathf.Abs(y + 0.5f - half) - (half - radius);
            return new Vector2(Mathf.Max(dx, 0f), Mathf.Max(dy, 0f)).magnitude;
        }

        // ----------------------------------------------------------------------------------------
        // Fonts + labels
        // ----------------------------------------------------------------------------------------

        // Resolve fonts once per session. TMP for everything possible; legacy Font is REQUIRED
        // for Dropdown labels (TMP_Dropdown is stripped from this game build).
        private void EnsureUguiFonts()
        {
            if (this.uguiKitFontResolveAttempted)
            {
                return;
            }
            this.uguiKitFontResolveAttempted = true;

            // BUG FIX (2026-07-22): this used to take the FIRST TMP_FontAsset the scan returned and
            // latch it for the process. Which one that is depends on what happens to be loaded the
            // first time the menu opens, so the whole UI silently changed typeface between runs —
            // logs show "TMP=LiberationSans SDF" when opened at the login screen vs
            // "TMP=FZY4JW_SDF" (a GAME font) when opened in-world. That caused two reported bugs at
            // once: the game font's material carries an OUTLINE (game UI text is outlined so it
            // reads over the world), and its Latin glyphs are far wider (~9px/char vs ~6px), so
            // labels this layout was measured against started overrunning their rects.
            // The font is now HARD-PINNED to LiberationSans SDF — no user choice (the picker was
            // built, then removed once OS fonts proved impossible and the only real alternatives
            // were the game's own outlined, much wider assets). LiberationSans is TMP's built-in:
            // always loaded, clean material, and the exact metrics every rect in this kit was sized
            // against. First-found stays as a last resort ONLY so the UI still renders text if the
            // built-in ever goes missing; it is not a preference.
            try
            {
                var found = Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.Of<TMP_FontAsset>());
                if (found != null)
                {
                    TMP_FontAsset firstSeen = null;
                    TMP_FontAsset liberation = null;
                    for (int i = 0; i < found.Length; i++)
                    {
                        TMP_FontAsset fa = (found[i] != null) ? found[i].TryCast<TMP_FontAsset>() : null;
                        if (fa == null)
                        {
                            continue;
                        }
                        if (firstSeen == null)
                        {
                            firstSeen = fa;
                        }
                        string faName = fa.name ?? string.Empty;
                        if (faName.IndexOf("LiberationSans", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            liberation = fa;
                            break; // pinned target found — stop looking
                        }
                    }
                    this.uguiKitTmpFont = liberation ?? firstSeen;
                    if (liberation == null && firstSeen != null)
                    {
                        ModLogger.Msg("[UguiKit] LiberationSans SDF not found — falling back to '"
                            + (firstSeen.name ?? "?") + "' (text metrics may not match the layout).");
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiKit] TMP font scan failed: " + ex.Message);
            }

            if (this.uguiKitTmpFont == null)
            {
                try { this.uguiKitTmpFont = TMP_Settings.defaultFontAsset; }
                catch (Exception ex) { ModLogger.Msg("[UguiKit] TMP_Settings.defaultFontAsset failed: " + ex.Message); }
            }

            // Our OWN material preset, cloned from whichever asset won above. TMP draws outline /
            // glow / drop-shadow from MATERIAL properties, and a game font asset's shared material
            // has them dialled in for readability over the world — which is where the reported
            // outline came from. Zeroing them on the SHARED material would restyle the game's own
            // text, so clone once and hand every kit label the clone instead (one instance for the
            // whole UI, not one per label). SetFloat on a property this shader lacks is a no-op, so
            // the list is safe to over-specify. If the clone fails we simply keep the stock look.
            if (this.uguiKitTmpFont != null)
            {
                try
                {
                    Material srcMat = this.uguiKitTmpFont.material;
                    if (srcMat != null)
                    {
                        Material flat = new Material(srcMat);
                        flat.name = srcMat.name + " (Bugtopia flat)";
                        flat.SetFloat("_OutlineWidth", 0f);
                        flat.SetFloat("_OutlineSoftness", 0f);
                        flat.SetFloat("_GlowPower", 0f);
                        flat.SetFloat("_GlowOuter", 0f);
                        flat.SetFloat("_UnderlayDilate", 0f);
                        flat.SetFloat("_UnderlaySoftness", 0f);
                        flat.SetFloat("_UnderlayOffsetX", 0f);
                        flat.SetFloat("_UnderlayOffsetY", 0f);
                        this.uguiKitTmpMaterial = flat;
                    }
                }
                catch (Exception ex)
                {
                    ModLogger.Msg("[UguiKit] TMP flat material clone failed (keeping stock look): " + ex.Message);
                }
            }

            try { this.uguiKitLegacyFont = Resources.GetBuiltinResource<Font>("Arial.ttf"); }
            catch (Exception ex) { ModLogger.Msg("[UguiKit] builtin Arial load failed: " + ex.Message); }

            if (this.uguiKitLegacyFont == null)
            {
                try { this.uguiKitLegacyFont = Font.CreateDynamicFontFromOSFont("Arial", 16); }
                catch { }
            }

            if (this.uguiKitLegacyFont == null)
            {
                try
                {
                    var fonts = Resources.FindObjectsOfTypeAll(Il2CppInterop.Runtime.Il2CppType.Of<Font>());
                    if (fonts != null)
                    {
                        for (int i = 0; i < fonts.Length; i++)
                        {
                            Font f = (fonts[i] != null) ? fonts[i].TryCast<Font>() : null;
                            if (f != null)
                            {
                                this.uguiKitLegacyFont = f;
                                break;
                            }
                        }
                    }
                }
                catch { }
            }

            string tmpName = "<null>";
            string legacyName = "<null>";
            try { if (this.uguiKitTmpFont != null) tmpName = this.uguiKitTmpFont.name; } catch { }
            try { if (this.uguiKitLegacyFont != null) legacyName = this.uguiKitLegacyFont.name; } catch { }
            ModLogger.Msg("[UguiKit] fonts resolved: TMP=" + tmpName + " legacy=" + legacyName
                + " flatMaterial=" + (this.uguiKitTmpMaterial != null));
        }

        // ⚠️ DEAD END — Windows-installed fonts CANNOT be offered as a font choice on this build.
        // Tried and reverted 2026-07-22; the log said it plainly:
        //     [UguiKit] TMP_FontAsset.CreateFontAsset returned null for 'Courier New'.
        // Font.CreateDynamicFontFromOSFont SUCCEEDS, so the failure is entirely on the TMP side:
        // TMP_FontAsset.CreateFontAsset(Font) begins with FontEngine.LoadFontFace(font, size), and
        // that overload needs the Font object to carry EMBEDDED font data ("Include Font Data" in
        // the importer). A dynamic OS font in a player build carries none — it is just a handle the
        // legacy dynamic-font renderer resolves per glyph — so the face never loads and the call
        // returns null. Verified against this build's interop metadata: UnityEngine.TextCoreModule
        // has FontEngine.LoadFontFace with BOTH the byte[] (sourceFontFile) and familyName/styleName
        // overloads, but Unity.TextMeshPro has NO 'familyName' symbol at all — i.e. TMP's by-name
        // CreateFontAsset overload does not exist here, and TMP exposes no public way to build an
        // asset around a face you loaded yourself (CreateFontAssetInstance is private). So even
        // loading the TTF bytes off Font.GetPathsToOSFonts() leads nowhere.
        // The only route left would be rendering labels with legacy UnityEngine.UI.Text (whose
        // dynamic Font path does support OS fonts) instead of TMP — a whole-kit downgrade in text
        // quality, deliberately not taken. Ask before attempting this again.

        // TMP label when a TMP font resolved, legacy Text otherwise — fallback is logged, never
        // silent. Callers position the returned GO (PlaceUguiTopLeft / StretchUguiFill).
        private GameObject CreateUguiLabel(Transform parent, string name, string text, float size, Color color, bool centered)
        {
            this.EnsureUguiFonts();
            GameObject go = this.CreateUguiGo(name, parent);
            if (this.uguiKitTmpFont != null)
            {
                try
                {
                    TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
                    tmp.font = this.uguiKitTmpFont;
                    if (this.uguiKitTmpMaterial != null)
                    {
                        // Outline/glow/shadow-free preset (EnsureUguiFonts) — assigning the SHARED
                        // material keeps all kit labels on one material, so this stays a single
                        // draw-call batch instead of instancing a material per label.
                        try { tmp.fontSharedMaterial = this.uguiKitTmpMaterial; } catch { }
                    }
                    tmp.fontSize = size;
                    tmp.color = color;
                    // Wrap by default (2026-07-22): a label that outgrows its rect should flow onto
                    // a second line wherever the rect has the height for one, and only fall back to
                    // the "…" below when it genuinely has nowhere to go. Single-line labels are
                    // unaffected — wrapping only does anything once the text exceeds the width.
                    // (TMP 2.0 renamed this to textWrappingMode/TextWrappingModes; enableWordWrapping
                    // is the [FormerlySerializedAs] alias and still the one that exists on this
                    // build's stripped TMP, so it stays.)
                    tmp.enableWordWrapping = true;
                    // BUG FIX (2026-07-22): TMP's DEFAULT overflowMode is Overflow — text longer
                    // than its rect keeps rendering straight past the edge and draws OVER whatever
                    // sits next to it. Reported on Auto Sell (the selected-item key line rendering
                    // under the "Auto" checkbox, the star-info hint spilling out of the 205px left
                    // column and across "Keep Per Item"), but it was never an Auto Sell bug: those
                    // rects are correctly sized and stop short of their neighbours, and EVERY label
                    // in the app had the same behaviour, so any string long enough would collide.
                    // Ellipsis keeps the glyphs inside the rect and trims with "…" instead, which
                    // makes a too-long label a readability question rather than a layout collision.
                    // Applies to wrapped labels too (TrySetUguiLabelWrapped): they wrap on width as
                    // before and only ellipsize past the rect HEIGHT.
                    tmp.overflowMode = TextOverflowModes.Ellipsis;
                    tmp.alignment = centered ? TextAlignmentOptions.Center : TextAlignmentOptions.MidlineLeft;
                    tmp.raycastTarget = false;
                    tmp.text = text;
                    return go;
                }
                catch (Exception ex)
                {
                    ModLogger.Msg("[UguiKit] TMP label '" + name + "' failed, using legacy Text: " + ex.Message);
                    if (go.GetComponent<TextMeshProUGUI>() != null)
                    {
                        // A half-initialized TMP graphic already claimed the CanvasRenderer; a
                        // second Graphic on the same GO is invalid — keep the broken TMP.
                        return go;
                    }
                }
            }

            try
            {
                this.CreateUguiLegacyText(go, text, (int)size, color, centered ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft);
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiKit] legacy label '" + name + "' failed too: " + ex.Message);
            }
            return go;
        }

        private Text CreateUguiLegacyText(GameObject go, string text, int size, Color color, TextAnchor anchor)
        {
            Text t = go.AddComponent<Text>();
            if (this.uguiKitLegacyFont != null)
            {
                t.font = this.uguiKitLegacyFont;
            }
            t.fontSize = size;
            t.color = color;
            t.alignment = anchor;
            t.supportRichText = false;
            t.raycastTarget = false;
            t.text = text;
            return t;
        }

        // Label role variants — same text-color vocabulary as the IMGUI redesign.
        private GameObject CreateUguiHeaderLabel(Transform parent, string name, string text, float size = 15f)
        {
            GameObject go = this.CreateUguiLabel(parent, name, text, size, this.UguiKitHeaderColor(), false);
            this.TrySetUguiLabelBold(go);
            return go;
        }

        private GameObject CreateUguiBodyLabel(Transform parent, string name, string text, float size = 13f)
        {
            return this.CreateUguiLabel(parent, name, text, size, this.UguiKitTextColor(), false);
        }

        private GameObject CreateUguiMutedLabel(Transform parent, string name, string text, float size = 12f)
        {
            return this.CreateUguiLabel(parent, name, text, size, this.UguiKitMutedColor(), false);
        }

        // Value/readout role (numeric displays). No monospace font exists in this game's assets,
        // so the role is carried by accent color rather than a mono face.
        private GameObject CreateUguiValueLabel(Transform parent, string name, string text, float size = 13f)
        {
            return this.CreateUguiLabel(parent, name, text, size, this.UguiKitAccent(), false);
        }

        private void TrySetUguiLabelBold(GameObject label)
        {
            if (label == null)
            {
                return;
            }
            try
            {
                TextMeshProUGUI tmp = label.GetComponent<TextMeshProUGUI>();
                if (tmp != null)
                {
                    tmp.fontStyle = FontStyles.Bold; // enum verified present in this build's TMP
                    return;
                }
                Text txt = label.GetComponent<Text>();
                if (txt != null)
                {
                    txt.fontStyle = FontStyle.Bold;
                }
            }
            catch { }
        }

        private void SetUguiLabelText(GameObject label, string text)
        {
            if (label == null)
            {
                return;
            }
            try
            {
                TextMeshProUGUI tmp = label.GetComponent<TextMeshProUGUI>();
                if (tmp != null)
                {
                    tmp.text = text;
                    return;
                }
                Text txt = label.GetComponent<Text>();
                if (txt != null)
                {
                    txt.text = text;
                }
            }
            catch { }
        }

        private void SetUguiLabelColor(GameObject label, Color color)
        {
            if (label == null)
            {
                return;
            }
            try
            {
                TextMeshProUGUI tmp = label.GetComponent<TextMeshProUGUI>();
                if (tmp != null)
                {
                    tmp.color = color;
                    return;
                }
                Text txt = label.GetComponent<Text>();
                if (txt != null)
                {
                    txt.color = color;
                }
            }
            catch { }
        }

        // ----------------------------------------------------------------------------------------
        // Event wiring (generic UnityEvent<T> — value-typed generics may lack AOT instances)
        // ----------------------------------------------------------------------------------------

        private bool TryWireUguiEvent(UnityEngine.Events.UnityEvent<bool> evt, System.Action<bool> handler, string what)
        {
            try
            {
                evt.AddListener(handler);
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiKit] " + what + " listener wiring failed: " + ex.Message);
                return false;
            }
        }

        private bool TryWireUguiEvent(UnityEngine.Events.UnityEvent<float> evt, System.Action<float> handler, string what)
        {
            try
            {
                evt.AddListener(handler);
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiKit] " + what + " listener wiring failed: " + ex.Message);
                return false;
            }
        }

        private bool TryWireUguiEvent(UnityEngine.Events.UnityEvent<int> evt, System.Action<int> handler, string what)
        {
            try
            {
                evt.AddListener(handler);
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiKit] " + what + " listener wiring failed: " + ex.Message);
                return false;
            }
        }

        // Reference-type generic (UnityEvent<string>, e.g. InputField.onValueChanged) — IL2CPP
        // shares the canonical instantiation for reference types, so this is lower-risk than the
        // value-typed int/float overloads above; the try/catch + log stays for uniformity.
        private bool TryWireUguiEvent(UnityEngine.Events.UnityEvent<string> evt, System.Action<string> handler, string what)
        {
            try
            {
                evt.AddListener(handler);
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiKit] " + what + " listener wiring failed: " + ex.Message);
                return false;
            }
        }

        // ----------------------------------------------------------------------------------------
        // Live theme reload
        // ----------------------------------------------------------------------------------------
        // The IMGUI theme editor stays live through the whole migration, but kit surfaces
        // snapshot theme colors at construction — so registered surfaces rebuild (state-
        // preserving) when the theme changes. Trigger: the SAME signal the IMGUI side rebuilds
        // its styles from — the uiThemeStylesDirty consumption block in EnsureThemeStyles
        // (every editor path — sliders, hex apply, HSV picker, Reset — funnels into that flag),
        // plus the two LoadUiTheme branches which call InvalidateThemeCache directly. NOT hooked
        // inside InvalidateThemeCache itself: that also runs for style re-bakes that are not
        // theme changes (first draw / teardown recovery, EnsureThemeStyles line ~972).

        private readonly List<KeyValuePair<string, System.Action>> uguiKitThemeRebuilders = new List<KeyValuePair<string, System.Action>>();
        private bool uguiKitThemeRebuildQueued;
        private float uguiKitThemeRebuildAt;

        // Called from the IMGUI theme-change choke points (HeartopiaComplete.UiKit.cs).
        private void MarkUguiKitThemeDirty()
        {
            if (this.uguiKitThemeRebuilders.Count == 0)
            {
                return; // nothing built yet — a first build reads fresh colors anyway
            }
            this.uguiKitThemeRebuildQueued = true;
            // Trailing debounce: a slider drag re-marks continuously (the IMGUI side already
            // throttles its own rebuild to ~0.1s); rebuild once, shortly after the LAST change.
            this.uguiKitThemeRebuildAt = Time.unscaledTime + 0.35f;
        }

        // Idempotent by name — re-registering replaces the callback.
        private void RegisterUguiThemeRebuilder(string name, System.Action rebuild)
        {
            if (string.IsNullOrEmpty(name) || rebuild == null)
            {
                return;
            }
            for (int i = 0; i < this.uguiKitThemeRebuilders.Count; i++)
            {
                if (this.uguiKitThemeRebuilders[i].Key == name)
                {
                    this.uguiKitThemeRebuilders[i] = new KeyValuePair<string, System.Action>(name, rebuild);
                    return;
                }
            }
            this.uguiKitThemeRebuilders.Add(new KeyValuePair<string, System.Action>(name, rebuild));
        }

        // Called from OnUpdate. Two comparisons while idle.
        private void ProcessUguiKitThemeOnUpdate()
        {
            if (!this.uguiKitThemeRebuildQueued || Time.unscaledTime < this.uguiKitThemeRebuildAt)
            {
                return;
            }
            this.uguiKitThemeRebuildQueued = false;
            for (int i = 0; i < this.uguiKitThemeRebuilders.Count; i++)
            {
                try
                {
                    this.uguiKitThemeRebuilders[i].Value();
                }
                catch (Exception ex)
                {
                    ModLogger.Msg("[UguiKit] theme rebuild '" + this.uguiKitThemeRebuilders[i].Key + "' failed: " + ex.Message);
                }
            }
            ModLogger.Msg("[UguiKit] theme change applied to " + this.uguiKitThemeRebuilders.Count + " UGUI surface(s)");
        }

        // Session-state snapshot for a state-preserving rebuild (position / scale / visibility).
        // Tab selections are surface-specific and belong to each surface's own rebuilder.
        private sealed class UguiWindowRestoreState
        {
            public Vector2 Position;
            public float Scale = 1f;
            public bool Visible;
        }

        private UguiWindowRestoreState CaptureUguiWindowState(UguiWindowHandle win)
        {
            UguiWindowRestoreState state = new UguiWindowRestoreState();
            try
            {
                if (win != null)
                {
                    state.Scale = win.Scale;
                    state.Visible = this.IsUguiWindowVisible(win);
                    if (win.PanelRt != null)
                    {
                        state.Position = win.PanelRt.anchoredPosition;
                    }
                }
            }
            catch { }
            return state;
        }

        private void RestoreUguiWindowState(UguiWindowHandle win, UguiWindowRestoreState state)
        {
            if (win == null || state == null)
            {
                return;
            }
            try
            {
                this.SetUguiWindowScale(win, state.Scale); // re-syncs canvas scaleFactor + label + clamp
                if (win.PanelRt != null)
                {
                    win.PanelRt.anchoredPosition = state.Position;
                }
                this.ClampUguiWindowPosition(win);
                this.SetUguiWindowVisible(win, state.Visible);
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiKit] window state restore failed: " + ex.Message);
            }
        }

        // ----------------------------------------------------------------------------------------
        // Window container
        // ----------------------------------------------------------------------------------------

        // Canvas (ScreenSpaceOverlay, own sorting) + sliced window panel + title bar with title /
        // subtitle labels. Returns a handle; add content under handle.PanelRt with PlaceUguiTopLeft.
        // The window starts INACTIVE — call SetUguiWindowVisible(win, true) when ready.
        // sortingOrder MUST stay below 30000 (Dropdown popup) — see file header.
        // Pass a null/empty title to own the top strip yourself (e.g. the shell's combined
        // logo + per-tab-header row): the TitleBar strip is still created at titleBarHeight and
        // remains the drag region, but no kit labels are placed in it.
        private UguiWindowHandle CreateUguiWindow(string goName, string title, string subtitle, Vector2 size, int sortingOrder,
            float titleBarHeight = UguiWindowTitleBarHeight)
        {
            this.EnsureUguiFonts();

            UguiWindowHandle win = new UguiWindowHandle();
            GameObject canvasGo = new GameObject(goName);
            Object.DontDestroyOnLoad(canvasGo);
            canvasGo.SetActive(false);

            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.overrideSorting = true;
            canvas.sortingOrder = sortingOrder;
            canvas.scaleFactor = win.Scale;
            canvasGo.AddComponent<UnityEngine.UI.GraphicRaycaster>();

            GameObject panel = this.CreateUguiGo("Panel", canvasGo.transform);
            // Backdrop composed the way DrawWindow ACTUALLY paints it (HeartopiaComplete.UiKit.cs
            // ~line 345), not just the raw uiWindow* fields: a hardcoded near-opaque slab with the
            // theme window color tinted over it. IMGUI draws the theme pass TWICE ("two passes
            // ≈ 0.998 opacity"); the tint layer here uses the equivalent 1-(1-a)^2 alpha. The
            // original single-pass theme-alpha Image was the refactor's fidelity break: it made
            // the whole window backdrop hang off one theme value that IMGUI itself never trusts
            // as the sole background.
            Image slab = this.AddUguiImage(panel, new Color(0.165f, 0.205f, 0.27f, 0.92f), true, 1f);
            slab.raycastTarget = true; // clicks on the window body must not leak to the game
            RectTransform panelRt = panel.GetComponent<RectTransform>();
            panelRt.anchorMin = new Vector2(0.5f, 0.5f);
            panelRt.anchorMax = new Vector2(0.5f, 0.5f);
            panelRt.pivot = new Vector2(0.5f, 0.5f);
            panelRt.sizeDelta = size;
            panelRt.anchoredPosition = Vector2.zero;

            GameObject tintGo = this.CreateUguiGo("WindowTint", panel.transform);
            StretchUguiFill(tintGo, 0f, 0f, 0f, 0f);
            Color windowTint = this.UguiKitWindowBg();
            windowTint.a = 1f - (1f - windowTint.a) * (1f - windowTint.a); // IMGUI double-pass equivalent
            Image tint = this.AddUguiImage(tintGo, windowTint, true, 1f);

            GameObject titleBar = this.CreateUguiGo("TitleBar", panel.transform);
            PlaceUguiTopLeft(titleBar, 0f, 0f, size.x, titleBarHeight);

            if (!string.IsNullOrEmpty(title))
            {
                GameObject titleLabel = this.CreateUguiLabel(titleBar.transform, "Title", title, 18f, this.UguiKitHeaderColor(), false);
                this.TrySetUguiLabelBold(titleLabel);
                PlaceUguiTopLeft(titleLabel, 24f, 14f, size.x - 48f, 26f);
                if (!string.IsNullOrEmpty(subtitle))
                {
                    GameObject subtitleLabel = this.CreateUguiMutedLabel(titleBar.transform, "Subtitle", subtitle, 12f);
                    PlaceUguiTopLeft(subtitleLabel, 24f, 40f, size.x - 48f, 18f);
                }
            }

            win.Root = canvasGo;
            win.Canvas = canvas;
            win.PanelRt = panelRt;
            win.TitleBarRt = titleBar.GetComponent<RectTransform>();
            win.BackdropSlab = slab;
            win.BackdropTint = tint;
            win.Size = size;
            return win;
        }

        private void SetUguiWindowVisible(UguiWindowHandle win, bool visible)
        {
            if (win == null || win.Root == null)
            {
                return;
            }
            try
            {
                if (win.Root.activeSelf != visible)
                {
                    win.Root.SetActive(visible);
                }
            }
            catch { }
        }

        private bool IsUguiWindowVisible(UguiWindowHandle win)
        {
            try
            {
                return win != null && win.Root != null && win.Root.activeSelf;
            }
            catch
            {
                return false;
            }
        }

        // True while the window is visible and the mouse is over its panel rect — the standard
        // pointer-over check for FLOATING input-ownership surfaces (see the registry in
        // HeartopiaComplete.CameraInput.cs). Only the 3-arg RectangleContainsScreenPoint overload
        // exists in this build; camera is null for ScreenSpaceOverlay (see file header + the drag
        // hit test in ProcessUguiWindowDrag). The rect's world transform already contains the
        // canvas scale, so no scale correction is needed.
        private bool IsUguiWindowPointerOver(UguiWindowHandle win)
        {
            try
            {
                if (!this.IsUguiWindowVisible(win) || win.PanelRt == null)
                {
                    return false;
                }
                Vector3 m = Input.mousePosition;
                return RectTransformUtility.RectangleContainsScreenPoint(win.PanelRt, new Vector2(m.x, m.y), null);
            }
            catch
            {
                return false;
            }
        }

        // Opt-in runtime scale: binds keys and adds a "Scale: 1.0x" readout to the title bar.
        // Multiple windows should use DIFFERENT keys (the frame driver is per-window).
        private void EnableUguiWindowScaleKeys(UguiWindowHandle win, KeyCode scaleUp, KeyCode scaleDown)
        {
            if (win == null || win.TitleBarRt == null)
            {
                return;
            }
            win.ScaleUpKey = scaleUp;
            win.ScaleDownKey = scaleDown;
            if (win.ScaleLabel == null)
            {
                win.ScaleLabel = this.CreateUguiMutedLabel(win.TitleBarRt.gameObject.transform, "ScaleLabel",
                    "Scale: " + win.Scale.ToString("0.0") + "x", 11f);
                PlaceUguiTopLeft(win.ScaleLabel, win.Size.x - 114f, 16f, 90f, 18f);
            }
        }

        // Per-frame driver: title-bar drag (polled — no ClassInjector components) + scale keys.
        // Call from an OnUpdate path. No-ops in a few comparisons while the window is hidden.
        private void ProcessUguiWindowFrame(UguiWindowHandle win)
        {
            if (!this.IsUguiWindowVisible(win))
            {
                return;
            }

            this.ProcessUguiWindowDrag(win);

            if ((win.ScaleUpKey != KeyCode.None || win.ScaleDownKey != KeyCode.None)
                && string.IsNullOrEmpty(this.keyBindingActive))
            {
                try
                {
                    if (win.ScaleUpKey != KeyCode.None && Input.GetKeyDown(win.ScaleUpKey))
                    {
                        this.SetUguiWindowScale(win, win.Scale + 0.1f);
                    }
                    else if (win.ScaleDownKey != KeyCode.None && Input.GetKeyDown(win.ScaleDownKey))
                    {
                        this.SetUguiWindowScale(win, win.Scale - 0.1f);
                    }
                }
                catch (Exception ex)
                {
                    ModLogger.Msg("[UguiKit] scale key error: " + ex.Message);
                }
            }
        }

        private void ProcessUguiWindowDrag(UguiWindowHandle win)
        {
            if (win.PanelRt == null || win.TitleBarRt == null || win.DragErrorCount >= 3)
            {
                return;
            }

            try
            {
                if (!win.DragActive)
                {
                    if (Input.GetMouseButtonDown(0))
                    {
                        Vector3 m = Input.mousePosition;
                        Vector2 mouse = new Vector2(m.x, m.y);
                        // Only the 3-arg overload exists in this build; camera is null for a
                        // ScreenSpaceOverlay canvas. The rect's world transform already contains
                        // the canvas scale, so the hit test needs no scale correction.
                        if (RectTransformUtility.RectangleContainsScreenPoint(win.TitleBarRt, mouse, null))
                        {
                            win.DragActive = true;
                            win.DragLastMouse = mouse;
                        }
                    }
                    return;
                }

                if (!Input.GetMouseButton(0))
                {
                    win.DragActive = false;
                    return;
                }

                Vector3 now3 = Input.mousePosition;
                Vector2 now = new Vector2(now3.x, now3.y);
                Vector2 delta = new Vector2(now.x - win.DragLastMouse.x, now.y - win.DragLastMouse.y);
                win.DragLastMouse = now;
                if (delta.x == 0f && delta.y == 0f)
                {
                    return;
                }

                // Mouse delta is SCREEN pixels; anchoredPosition is CANVAS units — divide by the
                // current scaleFactor or the window outruns/lags the cursor at non-1.0 scale.
                float s = (win.Scale >= 0.1f) ? win.Scale : 1f;
                Vector2 pos = win.PanelRt.anchoredPosition;
                pos.x += delta.x / s;
                pos.y += delta.y / s;
                win.PanelRt.anchoredPosition = pos;
                this.ClampUguiWindowPosition(win);
            }
            catch (Exception ex)
            {
                win.DragErrorCount++;
                win.DragActive = false;
                ModLogger.Msg("[UguiKit] drag error (" + win.DragErrorCount + "/3, disabled at 3): " + ex.Message);
            }
        }

        private void SetUguiWindowScale(UguiWindowHandle win, float requested)
        {
            if (win == null)
            {
                return;
            }
            // Round to one decimal so repeated 0.1f steps can't accumulate float drift.
            win.Scale = Mathf.Round(Mathf.Clamp(requested, UguiWindowScaleMin, UguiWindowScaleMax) * 10f) / 10f;
            try
            {
                if (win.Canvas != null)
                {
                    win.Canvas.scaleFactor = win.Scale;
                }
                // Screen extent in CANVAS units shrinks as scale grows — a window in bounds at
                // 1.0x can be out of reach at 2.0x, so re-clamp on every change.
                this.ClampUguiWindowPosition(win);
                this.SetUguiLabelText(win.ScaleLabel, "Scale: " + win.Scale.ToString("0.0") + "x");
                ModLogger.Msg("[UguiKit] " + (win.Root != null ? win.Root.name : "?") + " scaleFactor -> " + win.Scale.ToString("0.0"));
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiKit] scale apply failed: " + ex.Message);
            }
        }

        // anchoredPosition is in canvas units; at scaleFactor s the screen spans Screen/s canvas
        // units, so the bounds divide by s. Window half-sizes are pre-scale canvas units.
        private void ClampUguiWindowPosition(UguiWindowHandle win)
        {
            if (win == null || win.PanelRt == null)
            {
                return;
            }

            float s = (win.Scale >= 0.1f) ? win.Scale : 1f;
            float halfW = Screen.width / s * 0.5f;
            float halfH = Screen.height / s * 0.5f;
            float halfPanelW = win.Size.x * 0.5f;
            float halfPanelH = win.Size.y * 0.5f;
            float maxX = halfW + halfPanelW - UguiWindowVisibleMargin;
            float maxUp = halfH + UguiWindowTitleAllowance - halfPanelH; // title bar stays reachable
            float maxDown = halfH + halfPanelH - UguiWindowVisibleMargin;
            Vector2 pos = win.PanelRt.anchoredPosition;
            pos.x = Mathf.Clamp(pos.x, -maxX, maxX);
            pos.y = Mathf.Clamp(pos.y, -maxDown, maxUp);
            win.PanelRt.anchoredPosition = pos;
        }

        // ----------------------------------------------------------------------------------------
        // Buttons (3 tiers — mirrors DrawPrimary/Secondary/DangerActionButton)
        // ----------------------------------------------------------------------------------------

        private GameObject CreateUguiPrimaryButton(Transform parent, string name, string label, System.Action onClick)
        {
            Color accent = this.UguiKitAccent();
            // IMGUI primary is an accent->accent2 gradient; UGUI v1 is flat accent (a baked
            // gradient sprite can restore the gradient later if wanted).
            return this.CreateUguiButtonCore(parent, name, label, accent, default(Color), false,
                this.GetUiTextOnAccent(accent), true, onClick);
        }

        private GameObject CreateUguiSecondaryButton(Transform parent, string name, string label, System.Action onClick)
        {
            return this.CreateUguiButtonCore(parent, name, label, this.UguiKitControlFill(), UguiKitSecondaryRing, true,
                this.UguiKitTextColor(), false, onClick);
        }

        private GameObject CreateUguiDangerButton(Transform parent, string name, string label, System.Action onClick)
        {
            return this.CreateUguiButtonCore(parent, name, label, UguiKitDangerFill, UguiKitDangerRing, true,
                UguiKitDangerText, false, onClick);
        }

        private GameObject CreateUguiButtonCore(Transform parent, string name, string label, Color fill,
            Color ringColor, bool ring, Color textColor, bool bold, System.Action onClick)
        {
            GameObject go = this.CreateUguiGo(name, parent);
            Image bg = this.AddUguiImage(go, fill, true, 1.5f);
            bg.raycastTarget = true;
            Button btn = go.AddComponent<Button>();
            btn.targetGraphic = bg;
            if (ring)
            {
                this.AddUguiRingOverlay(go, ringColor, 1.5f);
            }
            GameObject lbl = this.CreateUguiLabel(go.transform, "Label", label, 13f, textColor, true);
            if (bold)
            {
                this.TrySetUguiLabelBold(lbl);
            }
            StretchUguiFill(lbl, 4f, 0f, 4f, 0f);
            if (onClick != null)
            {
                // Non-generic UnityEvent: System.Action converts implicitly (codebase-proven).
                btn.onClick.AddListener(onClick);
            }
            return go;
        }

        // Enable/disable a kit button (first use: Research tab's SELECT ITEM while an analyzer is
        // busy — the UGUI analog of IMGUI's GUI.enabled = false around a button). The kit never
        // configures Button.colors, so Unity's DEFAULT ColorBlock applies: interactable = false
        // multiplies disabledColor (greyed, ~50% alpha) into the target graphic automatically.
        // Only the background Image dims — the label child is not the targetGraphic — which is the
        // normal Unity disabled look, not a bug.
        private void SetUguiButtonInteractable(GameObject buttonGo, bool interactable)
        {
            if (buttonGo == null)
            {
                return;
            }
            try
            {
                Button btn = buttonGo.GetComponent<Button>();
                if (btn != null && btn.interactable != interactable)
                {
                    btn.interactable = interactable;
                }
            }
            catch { }
        }

        // ----------------------------------------------------------------------------------------
        // Toggles
        // ----------------------------------------------------------------------------------------

        // Checkbox: small square + accent checkmark faded by the Toggle itself. The whole row is
        // clickable (transparent raycast surface). Caller positions the returned Toggle's GO.
        private Toggle CreateUguiCheckbox(Transform parent, string name, string label, bool initial, System.Action<bool> onChanged)
        {
            GameObject row = this.CreateUguiGo(name, parent);
            Image rowHit = this.AddUguiImage(row, new Color(0f, 0f, 0f, 0f), false, 1f);
            rowHit.raycastTarget = true;

            Toggle tog = row.AddComponent<Toggle>();

            GameObject box = this.CreateUguiGo("Box", row.transform);
            PlaceUguiTopLeft(box, 0f, 1f, 22f, 22f);
            Image boxImg = this.AddUguiImage(box, this.UguiKitControlFill(), true, 2f);
            boxImg.raycastTarget = true;

            GameObject check = this.CreateUguiGo("Check", box.transform);
            RectTransform checkRt = check.GetComponent<RectTransform>();
            checkRt.anchorMin = new Vector2(0.5f, 0.5f);
            checkRt.anchorMax = new Vector2(0.5f, 0.5f);
            checkRt.pivot = new Vector2(0.5f, 0.5f);
            checkRt.sizeDelta = new Vector2(12f, 12f);
            checkRt.anchoredPosition = Vector2.zero;
            Image checkImg = this.AddUguiImage(check, this.UguiKitAccent(), true, 2f);

            GameObject lbl = this.CreateUguiBodyLabel(row.transform, "Label", label, 14f);
            RectTransform lblRt = lbl.GetComponent<RectTransform>();
            lblRt.anchorMin = new Vector2(0f, 1f);
            lblRt.anchorMax = new Vector2(1f, 1f);
            lblRt.pivot = new Vector2(0f, 1f);
            lblRt.anchoredPosition = new Vector2(32f, 0f);
            lblRt.sizeDelta = new Vector2(-32f, 24f); // stretch to the row's right edge, minus the box

            tog.targetGraphic = boxImg;
            tog.graphic = checkImg; // Toggle fades this in/out itself
            tog.toggleTransition = Toggle.ToggleTransition.Fade;
            tog.isOn = initial;
            if (onChanged != null)
            {
                this.TryWireUguiEvent(tog.onValueChanged, onChanged, name);
            }
            return tog;
        }

        // On/off switch: pill background + sliding handle, visuals driven from onValueChanged
        // (closure-captured — no per-instance fields needed). Caller positions the returned GO.
        private Toggle CreateUguiSwitch(Transform parent, string name, string label, bool initial, System.Action<bool> onChanged)
        {
            GameObject row = this.CreateUguiGo(name, parent);
            Image rowHit = this.AddUguiImage(row, new Color(0f, 0f, 0f, 0f), false, 1f);
            rowHit.raycastTarget = true;

            Toggle tog = row.AddComponent<Toggle>();

            GameObject pill = this.CreateUguiGo("Pill", row.transform);
            PlaceUguiTopLeft(pill, 0f, 0f, 46f, 24f);
            Image pillImg = this.AddUguiImage(pill, this.UguiKitControlFill(), true, 1f);
            pillImg.raycastTarget = true;

            GameObject handle = this.CreateUguiGo("Handle", pill.transform);
            PlaceUguiTopLeft(handle, 3f, 3f, 18f, 18f);
            this.AddUguiImage(handle, UguiKitHandleColor, true, 1f);
            RectTransform handleRt = handle.GetComponent<RectTransform>();

            GameObject lbl = this.CreateUguiBodyLabel(row.transform, "Label", label, 14f);
            RectTransform lblRt = lbl.GetComponent<RectTransform>();
            lblRt.anchorMin = new Vector2(0f, 1f);
            lblRt.anchorMax = new Vector2(1f, 1f);
            lblRt.pivot = new Vector2(0f, 1f);
            lblRt.anchoredPosition = new Vector2(58f, 0f);
            lblRt.sizeDelta = new Vector2(-58f, 24f);

            Color accent = this.UguiKitAccent();
            Color offFill = this.UguiKitControlFill();
            System.Action<bool> applyVisual = delegate (bool v)
            {
                try
                {
                    if (pillImg != null)
                    {
                        pillImg.color = v ? accent : offFill;
                    }
                    if (handleRt != null)
                    {
                        handleRt.anchoredPosition = new Vector2(v ? 25f : 3f, -3f);
                    }
                }
                catch { }
                if (onChanged != null)
                {
                    try { onChanged(v); }
                    catch (Exception ex) { ModLogger.Msg("[UguiKit] " + name + " onChanged threw: " + ex.Message); }
                }
            };

            tog.targetGraphic = pillImg;
            tog.isOn = initial;
            this.TryWireUguiEvent(tog.onValueChanged, applyVisual, name);
            applyVisual(initial); // apply the initial visual state (fires onChanged once, by design)
            return tog;
        }

        // ----------------------------------------------------------------------------------------
        // Slider
        // ----------------------------------------------------------------------------------------

        // Accent-filled horizontal slider. Value labels are the caller's job (create one and
        // update it inside onChanged). Caller positions the returned Slider's GO.
        private Slider CreateUguiSlider(Transform parent, string name, float min, float max, float initial,
            bool wholeNumbers, System.Action<float> onChanged)
        {
            GameObject sliderGo = this.CreateUguiGo(name, parent);
            Slider slider = sliderGo.AddComponent<Slider>();

            // BUG FIX (2026-07-22): the only raycastable graphic spanning the slider's full width
            // used to be the 8px-tall track — clicking anywhere in the caller's placed row above
            // or below that thin strip (commonly ~20px tall across most call sites) hit nothing,
            // so click-to-jump felt unreliable. A transparent hit Image directly on sliderGo picks
            // up whatever size the caller places the slider at (same "invisible full-row raycast
            // surface" idiom CreateUguiCheckbox's row hit already uses) — Unity's EventSystem
            // still bubbles the resulting pointer events up to the Slider component on this same
            // GameObject regardless of which child graphic the raycast actually landed on.
            Image hitArea = this.AddUguiImage(sliderGo, new Color(0f, 0f, 0f, 0f), false, 1f);
            hitArea.raycastTarget = true;

            GameObject track = this.CreateUguiGo("Background", sliderGo.transform);
            RectTransform trackRt = track.GetComponent<RectTransform>();
            trackRt.anchorMin = new Vector2(0f, 0.5f);
            trackRt.anchorMax = new Vector2(1f, 0.5f);
            trackRt.pivot = new Vector2(0.5f, 0.5f);
            trackRt.sizeDelta = new Vector2(0f, 8f);
            trackRt.anchoredPosition = Vector2.zero;
            Image trackImg = this.AddUguiImage(track, this.UguiKitControlFill(), true, 2.5f);
            trackImg.raycastTarget = true;

            GameObject fillArea = this.CreateUguiGo("Fill Area", sliderGo.transform);
            RectTransform fillAreaRt = fillArea.GetComponent<RectTransform>();
            fillAreaRt.anchorMin = new Vector2(0f, 0.5f);
            fillAreaRt.anchorMax = new Vector2(1f, 0.5f);
            fillAreaRt.pivot = new Vector2(0.5f, 0.5f);
            fillAreaRt.sizeDelta = new Vector2(-16f, 8f);
            // BUG FIX (2026-07-22): the accent fill started 4px right of the track's left edge.
            // Track spans 0..W; this rect is inset 8 per side (sizeDelta -16, centered) so it began
            // at 8, and Fill below overhangs it by 4 per side (its sizeDelta 8, split by a 0.5
            // pivot) — netting a fill that starts at 4, not 0. Shifting this rect left by exactly
            // that 4px overhang cancels it: fill left = 8 - 4 - 4 = 0, flush with the track.
            // It also lands the fill's RIGHT edge on 8 + value*(W-16), which is precisely where
            // Handle Slide Area (inset 8/-8) puts the knob's center — so the fill now ends under
            // the knob at every value instead of only near the middle. This is the same
            // inset-then-counter-shift trick stock Unity's own slider prefab uses.
            fillAreaRt.anchoredPosition = new Vector2(-4f, 0f);

            GameObject fill = this.CreateUguiGo("Fill", fillArea.transform);
            RectTransform fillRt = fill.GetComponent<RectTransform>();
            fillRt.anchorMin = Vector2.zero;
            fillRt.anchorMax = Vector2.one;
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;
            fillRt.sizeDelta = new Vector2(8f, 0f);
            this.AddUguiImage(fill, this.UguiKitAccent(), true, 2.5f);

            GameObject handleArea = this.CreateUguiGo("Handle Slide Area", sliderGo.transform);
            RectTransform handleAreaRt = handleArea.GetComponent<RectTransform>();
            handleAreaRt.anchorMin = Vector2.zero;
            handleAreaRt.anchorMax = Vector2.one;
            handleAreaRt.offsetMin = new Vector2(8f, 0f);
            handleAreaRt.offsetMax = new Vector2(-8f, 0f);

            // BUG FIX (2026-07-22), corrected: my first attempt pinned handleRt's anchors to a
            // point at construction time, assuming Slider only ever repositions X for a
            // LeftToRight direction and leaves Y alone. Verified wrong (user reported "no change"
            // after that fix deployed) — Slider.UpdateVisuals unconditionally sets BOTH anchorMin
            // and anchorMax to (0,1) on the axis PERPENDICULAR to its direction, on every visual
            // update, overwriting any construction-time anchor immediately (this is Unity's actual
            // stock behavior, not a bug in Slider itself — it's just the wrong rect to put a
            // stretched Image directly on). So sizeDelta.y=18 kept landing as an offset on top of
            // the ever-restretched handleArea height, same tall-pill result either way.
            // Real fix: decouple, the same way the (reverted) scrollbar attempt tried to — handleRt
            // stays exactly what Slider expects to manage (X pinned to value, Y perpetually
            // stretched; fine, since nothing is drawn on it directly anymore); a small FIXED-size
            // "Knob" child sits centered inside it instead, immune to the stretch, using the same
            // square + ppuMultiplier 1 recipe CreateUguiSwitch's round toggle knob already uses and
            // reads circular. That fixed the SHAPE, but silently created the dead-zone bug below.
            GameObject handle = this.CreateUguiGo("Handle", handleArea.transform);
            RectTransform handleRt = handle.GetComponent<RectTransform>();
            // BUG FIX (2026-07-22), same round: moving the graphic onto a Knob child also removed
            // the only reason handleRt's own size had ever been set, so it was left at the fresh
            // RectTransform default (~100 wide) while the visible knob shrank to 18. That gap is
            // invisible but not inert: Slider.OnPointerDown does a pure-geometry
            // RectangleContainsScreenPoint(handleRect) test to decide grab-the-handle vs
            // jump-to-click, so a ~50-per-side halo around the knob silently swallowed clicks
            // (matching the reported "only registers 2-3 knob-widths away, regardless of height" —
            // regardless of height because Slider stretches this rect's perpendicular axis full).
            // The same halo also explains the inconsistent drags: a press inside it is a grab, and
            // Slider stores the press offset within handleRect as m_Offset and honours it for the
            // whole drag, which is why the knob then tracked the cursor visibly offset instead of
            // under it. Sizing handleRt to the knob makes grab-vs-jump match what the user sees.
            // Slider drives only this rect's Anchors (DrivenTransformProperties.Anchors), so an
            // explicit sizeDelta is safe here; y=0 keeps the grab band exactly the slider's height.
            handleRt.sizeDelta = new Vector2(UguiSliderKnobSize, 0f);

            GameObject handleKnob = this.CreateUguiGo("Knob", handle.transform);
            RectTransform handleKnobRt = handleKnob.GetComponent<RectTransform>();
            handleKnobRt.anchorMin = new Vector2(0.5f, 0.5f);
            handleKnobRt.anchorMax = new Vector2(0.5f, 0.5f);
            handleKnobRt.pivot = new Vector2(0.5f, 0.5f);
            handleKnobRt.sizeDelta = new Vector2(UguiSliderKnobSize, UguiSliderKnobSize);
            handleKnobRt.anchoredPosition = Vector2.zero;
            Image handleImg = this.AddUguiImage(handleKnob, UguiKitHandleColor, true, 1f);
            handleImg.raycastTarget = true;

            slider.fillRect = fillRt;
            slider.handleRect = handleRt;
            slider.targetGraphic = handleImg;
            slider.direction = Slider.Direction.LeftToRight;
            slider.minValue = min;
            slider.maxValue = max;
            slider.wholeNumbers = wholeNumbers;
            slider.value = initial;
            if (onChanged != null)
            {
                this.TryWireUguiEvent(slider.onValueChanged, onChanged, name);
            }
            return slider;
        }

        // ----------------------------------------------------------------------------------------
        // Dropdown (stock UnityEngine.UI.Dropdown — template hierarchy hand-built)
        // ----------------------------------------------------------------------------------------

        // listenerWired=false means UnityEvent<int>.AddListener threw (possible missing AOT
        // instance on this 2020.3 build) — the caller must then poll .value per frame to observe
        // selection changes. Caption/item labels are legacy Text by hard requirement (TMP_Dropdown
        // stripped). Caller positions the returned Dropdown's GO; the popup drops below it.
        private Dropdown CreateUguiDropdown(Transform parent, string name, string[] options, int initialIndex,
            System.Action<int> onChanged, out bool listenerWired)
        {
            listenerWired = true;

            GameObject ddGo = this.CreateUguiGo(name, parent);
            Image ddBg = this.AddUguiImage(ddGo, this.UguiKitControlFill(), true, 1.5f);
            ddBg.raycastTarget = true;
            Dropdown dd = ddGo.AddComponent<Dropdown>();
            dd.targetGraphic = ddBg;

            GameObject capGo = this.CreateUguiGo("CaptionText", ddGo.transform);
            StretchUguiFill(capGo, 10f, 4f, 24f, 4f);
            Text caption = this.CreateUguiLegacyText(capGo, "", 14, this.UguiKitTextColor(), TextAnchor.MiddleLeft);

            // BUG FIX (2026-07-22): the caption above has always reserved 24px on the right for an
            // indicator, but nothing was ever drawn there — so a dropdown was visually just a
            // filled box and read as a text field. Fill that reserved gutter with the chevron.
            if (this.EnsureUguiCaretSprite())
            {
                GameObject caretGo = this.CreateUguiGo("Caret", ddGo.transform);
                RectTransform caretRt = caretGo.GetComponent<RectTransform>();
                caretRt.anchorMin = new Vector2(1f, 0.5f);
                caretRt.anchorMax = new Vector2(1f, 0.5f);
                caretRt.pivot = new Vector2(1f, 0.5f);
                caretRt.sizeDelta = new Vector2(11f, 7f);
                caretRt.anchoredPosition = new Vector2(-9f, 0f);
                Image caretImg = caretGo.AddComponent<Image>();
                caretImg.sprite = this.uguiKitCaretSprite;
                caretImg.color = this.UguiKitMutedColor();
                caretImg.raycastTarget = false; // clicks belong to the Dropdown's own background
            }

            int optionCount = (options != null) ? options.Length : 0;
            GameObject tpl = this.CreateUguiGo("Template", ddGo.transform);
            RectTransform tplRt = tpl.GetComponent<RectTransform>();
            tplRt.anchorMin = new Vector2(0f, 0f);
            tplRt.anchorMax = new Vector2(1f, 0f);
            tplRt.pivot = new Vector2(0.5f, 1f);
            tplRt.anchoredPosition = new Vector2(0f, 2f);
            // BUG FIX (2026-07-22): this was Max(1, optionCount) * 28 + 4 — the template grew to
            // fit EVERY option, so Auto Buy's 20-entry shop list produced a 564px popup. Unity's
            // Dropdown.Show then found it hanging past the root canvas and called
            // RectTransformUtility.FlipLayoutOnAxis to open it upward instead — where it was just
            // as oversized, so it ran off the top of the screen and became unusable. Show() never
            // re-checks after flipping, so the only real fix is to stop the popup being that tall.
            // Capping it means the list scrolls (ScrollRect below) rather than growing; Show()
            // still SHRINKS the popup to its content when the list is short (`extraSpace > 0`), so
            // small dropdowns stay compact and only long ones scroll.
            tplRt.sizeDelta = new Vector2(0f,
                Mathf.Min(Mathf.Max(1, optionCount) * 28f + 4f, UguiDropdownMaxPopupHeight));
            Color popupBg = this.UguiKitPanelBg();
            popupBg.a = 0.99f; // the floating list must stay readable over the game world
            Image tplBg = this.AddUguiImage(tpl, popupBg, true, 1.5f);
            tplBg.raycastTarget = true;
            // Dropdown.SetupTemplate adds Canvas/GraphicRaycaster/CanvasGroup to the template but
            // NOT a ScrollRect — Unity's stock prefab ships one, and ours never did, which is why
            // nothing bounded the list's height. The Canvas it adds sits ABOVE the mask in the
            // hierarchy, so RectMask2D still clips Content normally (no nested-canvas boundary
            // between the mask and the items it clips). Show() instantiates this whole subtree, and
            // Unity remaps component references that point INSIDE it — so content/viewport/
            // verticalScrollbar below all rebind to the clones automatically.
            ScrollRect tplScroll = tpl.AddComponent<ScrollRect>();

            GameObject viewport = this.CreateUguiGo("Viewport", tpl.transform);
            StretchUguiFill(viewport, 2f, 2f, 14f, 2f); // right inset leaves room for the scrollbar
            viewport.AddComponent<RectMask2D>(); // no Image: the template bg already paints behind

            GameObject content = this.CreateUguiGo("Content", viewport.transform);
            RectTransform contentRt = content.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.anchoredPosition = new Vector2(0f, -2f);
            contentRt.sizeDelta = new Vector2(0f, 28f);

            GameObject item = this.CreateUguiGo("Item", content.transform);
            RectTransform itemRt = item.GetComponent<RectTransform>();
            itemRt.anchorMin = new Vector2(0f, 0.5f);
            itemRt.anchorMax = new Vector2(1f, 0.5f);
            itemRt.pivot = new Vector2(0.5f, 0.5f);
            itemRt.sizeDelta = new Vector2(0f, 26f);
            Toggle itemToggle = item.AddComponent<Toggle>();

            GameObject itemBg = this.CreateUguiGo("Item Background", item.transform);
            StretchUguiFill(itemBg, 2f, 1f, 2f, 1f);
            Image itemBgImg = this.AddUguiImage(itemBg, this.UguiKitContentBg(), true, 2f);
            itemBgImg.raycastTarget = true;

            GameObject itemCheck = this.CreateUguiGo("Item Checkmark", item.transform);
            RectTransform itemCheckRt = itemCheck.GetComponent<RectTransform>();
            itemCheckRt.anchorMin = new Vector2(0f, 0.5f);
            itemCheckRt.anchorMax = new Vector2(0f, 0.5f);
            itemCheckRt.pivot = new Vector2(0f, 0.5f);
            itemCheckRt.anchoredPosition = new Vector2(8f, 0f);
            itemCheckRt.sizeDelta = new Vector2(10f, 10f);
            Image itemCheckImg = this.AddUguiImage(itemCheck, this.UguiKitAccent(), true, 2.5f);

            GameObject itemLabelGo = this.CreateUguiGo("Item Label", item.transform);
            StretchUguiFill(itemLabelGo, 26f, 1f, 6f, 1f);
            Text itemLabel = this.CreateUguiLegacyText(itemLabelGo, "Option", 13, this.UguiKitTextColor(), TextAnchor.MiddleLeft);

            itemToggle.targetGraphic = itemBgImg;
            itemToggle.graphic = itemCheckImg;
            itemToggle.toggleTransition = Toggle.ToggleTransition.Fade;

            // Popup scrollbar — same shape as CreateUguiScrollView's, including AutoHide so a short
            // list (the common case) shows no scrollbar at all.
            GameObject popupSbGo = this.CreateUguiGo("Scrollbar", tpl.transform);
            RectTransform popupSbRt = popupSbGo.GetComponent<RectTransform>();
            popupSbRt.anchorMin = new Vector2(1f, 0f);
            popupSbRt.anchorMax = new Vector2(1f, 1f);
            popupSbRt.pivot = new Vector2(1f, 0.5f);
            popupSbRt.anchoredPosition = new Vector2(-3f, 0f);
            popupSbRt.sizeDelta = new Vector2(8f, -6f);
            Image popupSbBg = this.AddUguiImage(popupSbGo, this.UguiKitControlFill(), true, 2.5f);
            popupSbBg.raycastTarget = true;
            Scrollbar popupSb = popupSbGo.AddComponent<Scrollbar>();

            GameObject popupSlidingArea = this.CreateUguiGo("Sliding Area", popupSbGo.transform);
            StretchUguiFill(popupSlidingArea, 1f, 1f, 1f, 1f);

            GameObject popupSbHandle = this.CreateUguiGo("Handle", popupSlidingArea.transform);
            RectTransform popupSbHandleRt = popupSbHandle.GetComponent<RectTransform>();
            popupSbHandleRt.anchorMin = Vector2.zero;
            popupSbHandleRt.anchorMax = Vector2.one;
            popupSbHandleRt.offsetMin = Vector2.zero;
            popupSbHandleRt.offsetMax = Vector2.zero;
            Image popupSbHandleImg = this.AddUguiImage(popupSbHandle, this.UguiKitAccent(), true, 2.5f);
            popupSbHandleImg.raycastTarget = true;

            popupSb.handleRect = popupSbHandleRt;
            popupSb.targetGraphic = popupSbHandleImg;
            popupSb.direction = Scrollbar.Direction.BottomToTop;

            tplScroll.content = contentRt;
            tplScroll.viewport = viewport.GetComponent<RectTransform>();
            tplScroll.verticalScrollbar = popupSb;
            tplScroll.horizontal = false;
            tplScroll.vertical = true;
            tplScroll.movementType = ScrollRect.MovementType.Clamped;
            tplScroll.scrollSensitivity = 25f;
            tplScroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            dd.template = tplRt;
            dd.captionText = caption;
            dd.itemText = itemLabel;

            var optionData = new Il2CppSystem.Collections.Generic.List<Dropdown.OptionData>();
            for (int i = 0; i < optionCount; i++)
            {
                optionData.Add(new Dropdown.OptionData(options[i]));
            }
            dd.AddOptions(optionData);
            dd.value = Mathf.Clamp(initialIndex, 0, Mathf.Max(0, optionCount - 1));
            dd.RefreshShownValue();
            tpl.SetActive(false); // template must be inactive or Dropdown logs a warning

            if (onChanged != null)
            {
                listenerWired = this.TryWireUguiEvent(dd.onValueChanged, onChanged, name);
            }
            return dd;
        }

        // ----------------------------------------------------------------------------------------
        // Input field (legacy UnityEngine.UI.InputField — TMP_InputField is stripped, see header)
        // ----------------------------------------------------------------------------------------

        // Single-line text input skinned like the kit's other controls (control fill + body text
        // + visible caret). Verified against gameassembly-dumps for THIS build before first use
        // (Phase 3 Teleport round): InputField survived IL2CPP stripping with real compiled
        // bodies — text get RVA 0x8C3830 / set 0x1B20AA0, SetTextWithoutNotify 0x1B1D640,
        // characterLimit / lineType / textComponent setters all present — and its nested
        // OnChangeEvent : UnityEvent<string> is a materialized instantiation (ctor RVA 0x2445C10)
        // whose AddListener path compiled game code already exercises (the Mono-bridge
        // InputFieldExports.AddListener → icall_9CA9E241 @ RVA 0xB53F90 wires onValueChanged with
        // a UnityAction<string>). Reference-type UnityEvent<T> shares the canonical instantiation
        // (unlike Dropdown's value-typed UnityEvent<int>), so wiring is expected to hold; it
        // still goes through TryWireUguiEvent (logged on failure), and live-reactive callers
        // should keep a cheap gated-frame poll comparing .text against their last applied value
        // (the uguiPocDropdownPollFallback idiom) as insurance — that same poll doubles as the
        // external-change detector when the backing field is edited from the IMGUI twin.
        // onValueChanged may be null for fields only read at click time (read .text off the
        // returned component when the button fires). characterLimit <= 0 = unlimited.
        private InputField CreateUguiInputField(Transform parent, string name, string initialText,
            int characterLimit, System.Action<string> onValueChanged)
        {
            GameObject go = this.CreateUguiGo(name, parent);
            Image bg = this.AddUguiImage(go, this.UguiKitControlFill(), true, 1.5f);
            bg.raycastTarget = true;
            InputField field = go.AddComponent<InputField>();
            field.targetGraphic = bg;

            // Value text must be legacy Text (InputField.textComponent is typed Text) and must be
            // assigned BEFORE .text so the initial value renders.
            this.EnsureUguiFonts();
            GameObject textGo = this.CreateUguiGo("Text", go.transform);
            StretchUguiFill(textGo, 8f, 3f, 8f, 3f);
            Text valueText = this.CreateUguiLegacyText(textGo, "", 13, this.UguiKitTextColor(), TextAnchor.MiddleLeft);

            field.textComponent = valueText;
            field.lineType = InputField.LineType.SingleLine;
            if (characterLimit > 0)
            {
                field.characterLimit = characterLimit;
            }
            // Unity's default caret is near-black — invisible on the kit's dark control fill.
            try
            {
                field.customCaretColor = true;
                field.caretColor = this.UguiKitTextColor();
            }
            catch { }
            field.text = initialText ?? string.Empty;

            if (onValueChanged != null)
            {
                this.TryWireUguiEvent(field.onValueChanged, onValueChanged, name);
            }
            return field;
        }

        // ----------------------------------------------------------------------------------------
        // List row (text + trailing action buttons — the Teleport-round list primitive)
        // ----------------------------------------------------------------------------------------

        private const int UguiListRowTierPrimary = 0;
        private const int UguiListRowTierSecondary = 1;
        private const int UguiListRowTierDanger = 2;

        private struct UguiListRowButtonSpec
        {
            public string Label;
            public int Tier;             // UguiListRowTier*
            public float Width;          // <= 0 = fill the space fixed-width siblings leave (labelless rows only)
            public bool Enabled;         // false = built disabled via SetUguiButtonInteractable
            public System.Action OnClick;
        }

        private sealed class UguiListRowHandle
        {
            public GameObject Root;
            public GameObject Label;    // non-clickable primary label; null for whole-row-button rows
            public GameObject SubLabel; // optional second line; null when subText was null/empty
            public readonly List<GameObject> Buttons = new List<GameObject>(); // trailing buttons, or [0] = the whole-row button
        }

        // ONE flexible row builder covering the four IMGUI list-row shapes the Teleport tab uses
        // (HeartopiaComplete.UguiTeleportContent.cs) — deliberately NOT a generic CRUD
        // abstraction (the plan assigns that to a later tab):
        //  (a) wholeRowButton=true, enabled:   the entire row is one Secondary-tier button
        //      (IMGUI's plain GUI.Button rows); subText becomes the button label's second line
        //      (the 2-line name+coords buttons).
        //  (d) wholeRowButton=true, !enabled:  same, disabled — IMGUI's GUI.enabled=false rows
        //      (NPC placeholder rows); wholeRowClick may be null.
        //  (b) wholeRowButton=false, 1 spec:   left label (+ optional colored sub-line under it)
        //      + one right-aligned button (Live Vehicles rows).
        //  (c) wholeRowButton=false, 2+ specs: same with more trailing buttons (Garage rows);
        //      when primaryText is null the specs own the whole width and one spec may use
        //      Width <= 0 to fill (Custom's wide name button + fixed Del).
        private UguiListRowHandle CreateUguiListRow(Transform parent, string name,
            float x, float y, float w, float h,
            string primaryText, string subText, Color? subTextColor,
            bool wholeRowButton, bool wholeRowEnabled, System.Action wholeRowClick,
            UguiListRowButtonSpec[] trailingButtons)
        {
            UguiListRowHandle row = new UguiListRowHandle();

            if (wholeRowButton)
            {
                string label = string.IsNullOrEmpty(subText) ? primaryText : (primaryText + "\n" + subText);
                GameObject wholeBtn = this.CreateUguiSecondaryButton(parent, name, label, wholeRowClick);
                PlaceUguiTopLeft(wholeBtn, x, y, w, h);
                if (!wholeRowEnabled)
                {
                    this.SetUguiButtonInteractable(wholeBtn, false);
                }
                row.Root = wholeBtn;
                row.Buttons.Add(wholeBtn);
                return row;
            }

            GameObject rowGo = this.CreateUguiGo(name, parent);
            PlaceUguiTopLeft(rowGo, x, y, w, h);
            row.Root = rowGo;

            const float gap = 6f;
            int count = (trailingButtons != null) ? trailingButtons.Length : 0;
            float fixedTotal = 0f;
            int fillCount = 0;
            for (int i = 0; i < count; i++)
            {
                if (trailingButtons[i].Width > 0f)
                {
                    fixedTotal += trailingButtons[i].Width;
                }
                else
                {
                    fillCount++;
                }
            }
            bool hasLabel = primaryText != null;
            float gapsTotal = (count > 1) ? gap * (count - 1) : 0f;
            // Fill specs only make sense on labelless rows (Custom's wide name button); with a
            // label present they fall back to a fixed 90px so the label keeps its space.
            float fillWidth = (!hasLabel && fillCount > 0)
                ? Mathf.Max(40f, (w - fixedTotal - gapsTotal) / fillCount)
                : 90f;
            float buttonsTotal = fixedTotal + fillCount * fillWidth + gapsTotal;

            if (hasLabel)
            {
                float labelW = Mathf.Max(20f, w - buttonsTotal - ((count > 0) ? gap : 0f));
                if (!string.IsNullOrEmpty(subText))
                {
                    row.Label = this.CreateUguiBodyLabel(rowGo.transform, "Label", primaryText, 12f);
                    PlaceUguiTopLeft(row.Label, 0f, 1f, labelW, 18f);
                    Color subColor = subTextColor ?? this.UguiKitMutedColor();
                    row.SubLabel = this.CreateUguiLabel(rowGo.transform, "Sub", subText, 10f, subColor, false);
                    PlaceUguiTopLeft(row.SubLabel, 0f, h - 17f, labelW, 16f);
                }
                else
                {
                    row.Label = this.CreateUguiBodyLabel(rowGo.transform, "Label", primaryText, 12f);
                    PlaceUguiTopLeft(row.Label, 0f, 0f, labelW, h);
                }
            }

            float bx = w - buttonsTotal;
            float btnH = hasLabel ? Mathf.Min(26f, h) : h;
            float by = hasLabel ? Mathf.Max(0f, (h - btnH) * 0.5f) : 0f;
            for (int i = 0; i < count; i++)
            {
                UguiListRowButtonSpec spec = trailingButtons[i];
                float bw = (spec.Width > 0f) ? spec.Width : fillWidth;
                GameObject btn;
                if (spec.Tier == UguiListRowTierPrimary)
                {
                    btn = this.CreateUguiPrimaryButton(rowGo.transform, "Btn" + i, spec.Label, spec.OnClick);
                }
                else if (spec.Tier == UguiListRowTierDanger)
                {
                    btn = this.CreateUguiDangerButton(rowGo.transform, "Btn" + i, spec.Label, spec.OnClick);
                }
                else
                {
                    btn = this.CreateUguiSecondaryButton(rowGo.transform, "Btn" + i, spec.Label, spec.OnClick);
                }
                PlaceUguiTopLeft(btn, bx, by, bw, btnH);
                if (!spec.Enabled)
                {
                    this.SetUguiButtonInteractable(btn, false);
                }
                row.Buttons.Add(btn);
                bx += bw + gap;
            }

            return row;
        }

        // ----------------------------------------------------------------------------------------
        // ScrollView
        // ----------------------------------------------------------------------------------------

        // Viewport (RectMask2D clipped) + Content + vertical Scrollbar. Returns the root GO (for
        // PlaceUguiTopLeft) and the Content transform via out param — add arbitrary rows under it
        // with PlaceUguiTopLeft, then keep contentHeight in sync (SetUguiScrollContentHeight).
        private GameObject CreateUguiScrollView(Transform parent, string name, float contentHeight, out Transform contentOut)
        {
            GameObject svGo = this.CreateUguiGo(name, parent);
            this.AddUguiImage(svGo, this.UguiKitPanelBg(), true, 1f);
            ScrollRect sr = svGo.AddComponent<ScrollRect>();

            GameObject viewport = this.CreateUguiGo("Viewport", svGo.transform);
            StretchUguiFill(viewport, 4f, 4f, 18f, 4f);
            Image vpImg = this.AddUguiImage(viewport, this.UguiKitContentBg(), false, 1f);
            vpImg.raycastTarget = true; // drag/wheel events land here and bubble to the ScrollRect
            viewport.AddComponent<RectMask2D>();

            GameObject content = this.CreateUguiGo("Content", viewport.transform);
            RectTransform contentRt = content.GetComponent<RectTransform>();
            contentRt.anchorMin = new Vector2(0f, 1f);
            contentRt.anchorMax = new Vector2(1f, 1f);
            contentRt.pivot = new Vector2(0.5f, 1f);
            contentRt.anchoredPosition = Vector2.zero;
            contentRt.sizeDelta = new Vector2(0f, Mathf.Max(1f, contentHeight));

            GameObject sbGo = this.CreateUguiGo("Scrollbar", svGo.transform);
            RectTransform sbRt = sbGo.GetComponent<RectTransform>();
            sbRt.anchorMin = new Vector2(1f, 0f);
            sbRt.anchorMax = new Vector2(1f, 1f);
            sbRt.pivot = new Vector2(1f, 0.5f);
            sbRt.anchoredPosition = new Vector2(-4f, 0f);
            sbRt.sizeDelta = new Vector2(10f, -8f);
            Image sbBg = this.AddUguiImage(sbGo, this.UguiKitControlFill(), true, 2.5f);
            sbBg.raycastTarget = true;
            Scrollbar sb = sbGo.AddComponent<Scrollbar>();

            GameObject slidingArea = this.CreateUguiGo("Sliding Area", sbGo.transform);
            StretchUguiFill(slidingArea, 1f, 1f, 1f, 1f);

            GameObject sbHandle = this.CreateUguiGo("Handle", slidingArea.transform);
            RectTransform sbHandleRt = sbHandle.GetComponent<RectTransform>();
            sbHandleRt.anchorMin = Vector2.zero;
            sbHandleRt.anchorMax = Vector2.one;
            sbHandleRt.offsetMin = Vector2.zero;
            sbHandleRt.offsetMax = Vector2.zero;
            Image sbHandleImg = this.AddUguiImage(sbHandle, this.UguiKitAccent(), true, 2.5f);
            sbHandleImg.raycastTarget = true;

            sb.handleRect = sbHandleRt;
            sb.targetGraphic = sbHandleImg;
            sb.direction = Scrollbar.Direction.BottomToTop;

            sr.content = contentRt;
            sr.viewport = viewport.GetComponent<RectTransform>();
            sr.verticalScrollbar = sb; // setter subscribes internally (compiled game code)
            sr.horizontal = false;
            sr.vertical = true;
            sr.movementType = ScrollRect.MovementType.Clamped;
            sr.scrollSensitivity = 25f;
            // BUG FIX (2026-07-22): the scrollbar used to stay visible even when all content fit
            // the viewport (nothing to scroll) — AutoHide is Unity's own built-in "hide when
            // Scrollbar.size >= 1" behavior. Not AutoHideAndExpandViewport: dozens of already-
            // shipped callers hardcode "viewport insets: 4 left + 18 right" for their own width
            // math, which would go stale if the viewport silently widened on hide.
            sr.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            contentOut = content.transform;
            return svGo;
        }

        private void SetUguiScrollContentHeight(Transform content, float height)
        {
            if (content == null)
            {
                return;
            }
            try
            {
                RectTransform rt = content.GetComponent<RectTransform>();
                if (rt != null)
                {
                    rt.sizeDelta = new Vector2(rt.sizeDelta.x, Mathf.Max(1f, height));
                }
            }
            catch { }
        }

        // ----------------------------------------------------------------------------------------
        // Virtualized icon grid (fixed-pool recycle core — the Bag/Warehouse-round primitive)
        // ----------------------------------------------------------------------------------------
        // UGUI is retained-mode, so a grid COULD naively build one GameObject tree per item — but
        // hundreds of cells (Image + several Text children each) cost real construction time on
        // every scan/rebuild, and any rebuild-on-selection-change repeats that stutter per click.
        // This core therefore keeps a FIXED pool of cell slots sized to the viewport
        // (poolRows = Ceil(viewportH / cellH) + 1 buffer row — the same visibleRowCount formula
        // the IMGUI grids use) and, as the ScrollRect content moves, only REPOSITIONS each slot
        // and reassigns which data index it represents. Slots are never destroyed/recreated after
        // construction; consumers build their cell visuals under each slot Root once, then rebind
        // content when a slot's BoundIndex (or their own data) changes.
        //
        // Division of labor (mirrors the list-row primitive: core stays data-agnostic):
        //  - Core: scroll view shell, content height (rows * cellH), scroll-position polling,
        //    slot positioning/activation, BoundIndex assignment.
        //  - Consumer: per-slot visuals, binding, and its own change detection (a per-slot
        //    signature compare that includes BoundIndex catches recycling automatically).
        // First consumer: Bag/Warehouse (HeartopiaComplete.UguiBagWarehouseContent.cs). The Auto
        // Sell tab's independent IMGUI copy of the same grid math migrates onto this later.

        private sealed class UguiVirtualGridSlot
        {
            public GameObject Root;
            public int BoundIndex = -1; // data index this slot currently represents; -1 = hidden
        }

        private sealed class UguiVirtualGridHandle
        {
            public GameObject Root;          // kit scroll view root — position with PlaceUguiTopLeft
            public Transform Content;
            public RectTransform ContentRt;
            public int Columns;
            public float CellW;              // column stride in content units (derived from width)
            public float CellH;              // row stride
            public int PoolRows;
            public readonly List<UguiVirtualGridSlot> Slots = new List<UguiVirtualGridSlot>();
            public int LastItemCount = -1;   // content-height update guard
        }

        // Builds the scroll shell + the fixed slot pool. `width` must be the width the caller will
        // place the grid at (cell stride derives from it; the kit scroll view's viewport insets
        // are 4 left + 18 right). `poolViewportHeight` sizes the pool — pass the LARGEST viewport
        // height the grid can occupy so later relayouts (e.g. a conditional bar above shrinking
        // the grid) never need more slots than exist. Slot roots start empty and inactive; the
        // consumer adds visuals under each and drives binding via UpdateUguiVirtualGridAssignments.
        private UguiVirtualGridHandle CreateUguiVirtualGrid(Transform parent, string name, float width,
            int columns, float cellH, float poolViewportHeight)
        {
            UguiVirtualGridHandle grid = new UguiVirtualGridHandle();
            grid.Columns = Mathf.Max(1, columns);
            grid.CellH = Mathf.Max(1f, cellH);
            grid.CellW = Mathf.Max(1f, (width - 22f) / grid.Columns); // viewport insets: 4 left + 18 right

            Transform content;
            grid.Root = this.CreateUguiScrollView(parent, name, 1f, out content);
            grid.Content = content;
            grid.ContentRt = (content != null) ? content.GetComponent<RectTransform>() : null;
            // Cells paint their own fills; the viewport's ContentBg would sit flush under them and
            // erase the contrast IMGUI gets from content-on-panel — clear it (alpha-0 Images still
            // raycast, so wheel/drag scrolling keeps working; the root keeps its PanelBg).
            try
            {
                if (content != null && content.parent != null)
                {
                    Image viewportBg = content.parent.GetComponent<Image>();
                    if (viewportBg != null)
                    {
                        viewportBg.color = Color.clear;
                    }
                }
            }
            catch { }

            grid.PoolRows = Mathf.Max(2, Mathf.CeilToInt(poolViewportHeight / grid.CellH) + 1); // +1 buffer row
            int poolSize = grid.PoolRows * grid.Columns;
            for (int k = 0; k < poolSize; k++)
            {
                UguiVirtualGridSlot slot = new UguiVirtualGridSlot();
                slot.Root = this.CreateUguiGo("Cell" + k, content);
                // IMGUI cellRect construction verbatim: (col*cellW+2, row*cellH+2, cellW-8, cellH-8).
                PlaceUguiTopLeft(slot.Root, 2f, 2f, grid.CellW - 8f, grid.CellH - 8f);
                slot.Root.SetActive(false);
                grid.Slots.Add(slot);
            }
            return grid;
        }

        // Per-frame driver: sync content height to the data size, poll the scroll position, and
        // point each pool slot at the data index it should now represent (reposition + activate),
        // hiding slots past the end. Never destroys or creates GameObjects. The consumer rebinds
        // slot content afterwards — a changed BoundIndex is its signal.
        private void UpdateUguiVirtualGridAssignments(UguiVirtualGridHandle grid, int itemCount)
        {
            if (grid == null || grid.Root == null)
            {
                return;
            }

            int rows = Mathf.CeilToInt(itemCount / (float)grid.Columns);
            if (itemCount != grid.LastItemCount)
            {
                grid.LastItemCount = itemCount;
                // Content height = rows * cellH — the ScrollRect's range matches the FULL data
                // set even though only PoolRows * Columns cells physically exist.
                this.SetUguiScrollContentHeight(grid.Content, Mathf.Max(1f, rows * grid.CellH));
            }

            float scrollY = 0f;
            try
            {
                if (grid.ContentRt != null)
                {
                    scrollY = grid.ContentRt.anchoredPosition.y; // top-pivot content: 0..(contentH - viewportH)
                }
            }
            catch { }

            // IMGUI's own visibility math: firstVisibleRow = floor(scrollY / cellH), clamped.
            int firstRow = Mathf.Max(0, Mathf.FloorToInt(scrollY / grid.CellH));
            firstRow = Mathf.Min(firstRow, Mathf.Max(0, rows - 1));
            int firstIndex = Mathf.Clamp(firstRow * grid.Columns, 0, Mathf.Max(0, itemCount));

            for (int k = 0; k < grid.Slots.Count; k++)
            {
                UguiVirtualGridSlot slot = grid.Slots[k];
                if (slot == null || slot.Root == null)
                {
                    continue;
                }
                int desired = firstIndex + k;
                if (desired >= itemCount)
                {
                    slot.BoundIndex = -1;
                    if (slot.Root.activeSelf)
                    {
                        slot.Root.SetActive(false);
                    }
                    continue;
                }
                if (slot.BoundIndex != desired)
                {
                    slot.BoundIndex = desired;
                    int row = desired / grid.Columns;
                    int col = desired % grid.Columns;
                    PlaceUguiTopLeft(slot.Root, col * grid.CellW + 2f, row * grid.CellH + 2f,
                        grid.CellW - 8f, grid.CellH - 8f);
                }
                if (!slot.Root.activeSelf)
                {
                    slot.Root.SetActive(true);
                }
            }
        }

        // ----------------------------------------------------------------------------------------
        // Tab bar (segmented control)
        // ----------------------------------------------------------------------------------------

        // tabNames.Length buttons laid left-to-right from (x, y); iconIndices may be null, or per
        // tab a NavIconPngBase64 index (-1 = no icon). contents[i] is SetActive'd when tab i is
        // selected. tabWidths (optional) overrides tabWidth per tab — the IMGUI sub-tab bar sizes
        // buttons by label length, so bars with many/long labels need non-uniform widths to fit.
        //
        // STYLE (2026-07-22, user-chosen): underline tabs, not filled pills. Inactive = muted text
        // on no visible box; active = accent text + a 2px accent underline; a hairline rule runs
        // under the whole bar to the parent's right edge so the active tab reads as sitting ON the
        // line. Each tab keeps a full-bounds Image purely as the raycast target and hover wash —
        // it is NO LONGER the selection indicator, so SelectUguiTab must not repaint it.
        private UguiTabBarHandle CreateUguiTabBar(Transform parent, float x, float y, float tabWidth, float tabHeight,
            float spacing, string[] tabNames, int[] iconIndices, GameObject[] contents, int initialIndex,
            System.Action<int> onChanged, float[] tabWidths = null, float labelFontSize = 13f)
        {
            UguiTabBarHandle bar = new UguiTabBarHandle();
            bar.OnChanged = onChanged;
            if (contents != null)
            {
                for (int i = 0; i < contents.Length; i++)
                {
                    bar.Contents.Add(contents[i]);
                }
            }

            float cx = x;
            int count = (tabNames != null) ? tabNames.Length : 0;

            // Hairline under the whole bar, from the bar's own x to the parent's right edge (the
            // kit cannot know the parent's pixel width at build time, so this is anchor-stretched
            // rather than computed). Built BEFORE the tabs so they draw over it — an active tab's
            // 2px accent underline must cover this 1px line locally, which is what produces the
            // "tab sits on the line" read. Deliberately not SetAsFirstSibling: that would also sink
            // it behind any backdrop the caller already parented here.
            if (count > 0)
            {
                GameObject rule = this.CreateUguiGo("TabRule", parent);
                RectTransform ruleRt = rule.GetComponent<RectTransform>();
                ruleRt.anchorMin = new Vector2(0f, 1f);
                ruleRt.anchorMax = new Vector2(1f, 1f);
                ruleRt.pivot = new Vector2(0f, 1f);
                ruleRt.offsetMin = new Vector2(x, -(y + tabHeight));
                ruleRt.offsetMax = new Vector2(0f, -(y + tabHeight - 1f));
                this.AddUguiImage(rule, UguiKitSecondaryRing, false, 1f);
            }

            for (int i = 0; i < count; i++)
            {
                int tabIndex = i; // capture a copy — the loop variable must not leak into the closure
                float thisWidth = (tabWidths != null && i < tabWidths.Length && tabWidths[i] > 0f) ? tabWidths[i] : tabWidth;
                GameObject tabGo = this.CreateUguiGo("Tab_" + tabNames[i], parent);
                PlaceUguiTopLeft(tabGo, cx, y, thisWidth, tabHeight);
                cx += thisWidth + spacing;

                // White base + an explicit ColorBlock whose normal/selected alpha is 0: ColorTint
                // MULTIPLIES this color, so a transparent base could never brighten on hover. An
                // alpha-0 Image still raycasts (no alphaHitTestMinimumThreshold set), so the whole
                // tab stays clickable while invisible. selectedColor must also be transparent —
                // Unity keeps the last-clicked button "selected", which would otherwise strand a
                // wash on it after the pointer leaves. This is the kit's ONLY custom ColorBlock;
                // it is wrapped because a failure here should cost the hover, not the tab bar.
                Image bg = this.AddUguiImage(tabGo, Color.white, true, 1.5f);
                bg.raycastTarget = true;
                Button btn = tabGo.AddComponent<Button>();
                btn.targetGraphic = bg;
                try
                {
                    ColorBlock tabColors = btn.colors;
                    tabColors.normalColor = new Color(1f, 1f, 1f, 0f);
                    tabColors.highlightedColor = new Color(1f, 1f, 1f, 0.07f);
                    tabColors.pressedColor = new Color(1f, 1f, 1f, 0.13f);
                    tabColors.selectedColor = new Color(1f, 1f, 1f, 0f);
                    // No disabledColor: it is read-only on this build's Il2CppInterop ColorBlock
                    // binding (the other five setters exist). Harmless — tab bars never disable a
                    // tab, and the default disabled tint on an alpha-0 base is invisible anyway.
                    tabColors.colorMultiplier = 1f;
                    btn.colors = tabColors;
                }
                catch (Exception ex)
                {
                    ModLogger.Msg("[UguiKit] tab ColorBlock failed (hover wash disabled): " + ex.Message);
                    bg.color = new Color(1f, 1f, 1f, 0f);
                }

                Image icon = null;
                float labelLeft = 10f;
                int iconIndex = (iconIndices != null && i < iconIndices.Length) ? iconIndices[i] : -1;
                if (iconIndex >= 0)
                {
                    icon = this.CreateUguiIcon(tabGo.transform, iconIndex, 16f, this.UguiKitMutedColor());
                    if (icon != null)
                    {
                        RectTransform iconRt = icon.rectTransform;
                        iconRt.anchorMin = new Vector2(0f, 0.5f);
                        iconRt.anchorMax = new Vector2(0f, 0.5f);
                        iconRt.pivot = new Vector2(0f, 0.5f);
                        iconRt.anchoredPosition = new Vector2(9f, 0f);
                        labelLeft = 30f;
                    }
                }

                GameObject label = this.CreateUguiLabel(tabGo.transform, "Label", tabNames[i], labelFontSize, this.UguiKitMutedColor(), icon == null);
                // BUG FIX (2026-07-22): an icon-less tab label is TextAlignmentOptions.Center, and
                // TMP centers on the LABEL's own rect — which was inset 10 on the left but only 4
                // on the right, so every centered tab label sat (10-4)/2 = 3px right of the button's
                // true center. Symmetric insets for the centered case; icon tabs stay asymmetric on
                // purpose (they are MidlineLeft, and labelLeft is deliberately clearing the icon).
                StretchUguiFill(label, (icon == null) ? 4f : labelLeft, 2f, 4f, 2f);

                // Active-tab underline: 2px, pinned to the tab's bottom edge, inset 6 per side so it
                // underlines the label rather than the whole hit box. FLAT (sliced:false) on
                // purpose — a 2px-tall 9-slice is the degenerate-border case this project has been
                // bitten by repeatedly; below ~12px the kit always draws unrounded.
                GameObject underline = this.CreateUguiGo("Underline", tabGo.transform);
                RectTransform underlineRt = underline.GetComponent<RectTransform>();
                underlineRt.anchorMin = new Vector2(0f, 0f);
                underlineRt.anchorMax = new Vector2(1f, 0f);
                underlineRt.pivot = new Vector2(0.5f, 0f);
                underlineRt.offsetMin = new Vector2(6f, 0f);
                underlineRt.offsetMax = new Vector2(-6f, 2f);
                this.AddUguiImage(underline, this.UguiKitAccent(), false, 1f);
                SetUguiGoActive(underline, false); // SelectUguiTab below lights the initial one

                bar.ButtonBgs.Add(bg);
                bar.ButtonLabels.Add(label);
                bar.ButtonIcons.Add(icon);
                bar.ButtonUnderlines.Add(underline);
                btn.onClick.AddListener(new System.Action(() => this.SelectUguiTab(bar, tabIndex)));
            }

            this.SelectUguiTab(bar, initialIndex);
            return bar;
        }

        private void SelectUguiTab(UguiTabBarHandle bar, int index)
        {
            if (bar == null)
            {
                return;
            }

            try
            {
                if (index == bar.ActiveIndex)
                {
                    return; // re-clicking the active tab is a no-op
                }
                bar.ActiveIndex = index;

                for (int i = 0; i < bar.Contents.Count; i++)
                {
                    GameObject content = bar.Contents[i];
                    if (content != null)
                    {
                        content.SetActive(i == index);
                    }
                }

                // Underline style: the accent underline plus accent-vs-muted text carry the whole
                // selection state. ButtonBgs are deliberately NOT repainted here — each one is a
                // white base whose visible alpha comes from the Button's own hover ColorBlock, so
                // writing a colour into it would strand a permanent fill on the tab.
                Color accent = this.UguiKitAccent();
                Color muted = this.UguiKitMutedColor();
                for (int i = 0; i < bar.ButtonBgs.Count; i++)
                {
                    bool active = i == index;
                    if (i < bar.ButtonUnderlines.Count)
                    {
                        SetUguiGoActive(bar.ButtonUnderlines[i], active);
                    }
                    if (i < bar.ButtonLabels.Count)
                    {
                        this.SetUguiLabelColor(bar.ButtonLabels[i], active ? accent : muted);
                    }
                    if (i < bar.ButtonIcons.Count && bar.ButtonIcons[i] != null)
                    {
                        bar.ButtonIcons[i].color = active ? accent : muted;
                    }
                }

                if (bar.OnChanged != null)
                {
                    try { bar.OnChanged(index); }
                    catch (Exception ex) { ModLogger.Msg("[UguiKit] tab onChanged threw: " + ex.Message); }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiKit] tab switch error: " + ex.Message);
            }
        }

        // ----------------------------------------------------------------------------------------
        // Icons
        // ----------------------------------------------------------------------------------------

        // Wraps a baked NavIcons PNG (HeartopiaComplete.NavIcons.cs, EnsureNavIconTexture — the
        // SAME cached Texture2D instances the IMGUI sidebar draws) as a UGUI Image. Sprites are
        // cached per index. FullRect mesh on purpose (no Tight-mesh alpha outline computation).
        // Returns null on failure. Default rect: size x size, centered — caller repositions.
        private Image CreateUguiIcon(Transform parent, int navIconIndex, float size, Color tint)
        {
            try
            {
                Sprite sprite = this.TryGetUguiIconSprite(navIconIndex);
                if (sprite == null)
                {
                    return null;
                }

                GameObject iconGo = this.CreateUguiGo("Icon" + navIconIndex, parent);
                RectTransform rt = iconGo.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.anchoredPosition = Vector2.zero;
                rt.sizeDelta = new Vector2(size, size);
                Image img = iconGo.AddComponent<Image>();
                img.sprite = sprite;
                img.type = Image.Type.Simple;
                img.color = tint; // white line art tints via Image.color exactly like GUI.color
                img.raycastTarget = false;
                return img;
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiKit] icon " + navIconIndex + " failed: " + ex.Message);
                return null;
            }
        }

        private Sprite TryGetUguiIconSprite(int navIconIndex)
        {
            try
            {
                if (navIconIndex < 0 || navIconIndex >= NavIconPngBase64.Length)
                {
                    return null;
                }
                if (this.uguiKitIconSprites == null)
                {
                    this.uguiKitIconSprites = new Sprite[NavIconPngBase64.Length];
                }
                if (this.uguiKitIconSprites[navIconIndex] != null)
                {
                    return this.uguiKitIconSprites[navIconIndex];
                }

                Texture2D tex = this.EnsureNavIconTexture(navIconIndex);
                if (tex == null)
                {
                    ModLogger.Msg("[UguiKit] nav icon " + navIconIndex + " texture unavailable");
                    return null;
                }

                Sprite sprite = Sprite.Create(
                    tex,
                    new Rect(0f, 0f, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f),
                    100f,
                    0,
                    SpriteMeshType.FullRect,
                    Vector4.zero);
                if (sprite != null)
                {
                    sprite.hideFlags = HideFlags.DontUnloadUnusedAsset;
                    this.uguiKitIconSprites[navIconIndex] = sprite;
                }
                return sprite;
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiKit] icon sprite " + navIconIndex + " failed: " + ex.Message);
                return null;
            }
        }
    }
}
