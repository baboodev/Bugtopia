using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

using Object = UnityEngine.Object;

namespace HeartopiaMod
{
    // ============================================================================================
    // UGUI TOAST STACK — the UGUI twin of DrawMenuNotifications (HeartopiaComplete.UiKit.cs) and
    // the last IMGUI surface blocking OnGUI retirement. Producers are untouched: everything still
    // funnels through AddMenuNotification / AddOrUpdateMenuNotification into menuNotifications;
    // only the RENDERER moved. Architecturally this is a sibling of the Status Overlay
    // (HeartopiaComplete.UguiStatusOverlay.cs): a persistent dedicated Canvas OUTSIDE the shell
    // window (the shell root is SetActive(false) whenever the menu closes, and toasts must keep
    // showing with the menu closed — parenting under it would silently kill them), lazily built,
    // driven every frame from OnUpdate.
    //
    // Hard invariants:
    //  - THIS DRIVER OWNS THE EXPIRY SWEEP. The sweep + visibility filter used to live inside the
    //    IMGUI drawer; they are now shared helpers (SweepExpiredMenuNotifications /
    //    CollectVisibleMenuNotificationsNewestFirst, HeartopiaComplete.UiKit.cs) and the sweep is
    //    ticked HERE unconditionally — before every gate, even after a build failure or the
    //    error-count trip. If the sweep ever becomes conditional, retiring OnGUI leaves the list
    //    unpruned and pinned at AddOrUpdateMenuNotification's 6-cap forever (every new toast
    //    evicting the oldest immortal one).
    //  - sortingOrder 29800 — top of the mod band (20000 click-blocker < Overlay 29300 < Building
    //    panel 29350 < Quest Assistant 29360 < Shell 29400 < PoC 29500 < THIS < the 30000
    //    Dropdown-popup ceiling). Toasts are transient alerts and must never hide behind the mod's
    //    own windows (IMGUI drew them after the menu window, i.e. on top); an OPEN dropdown popup
    //    (30000) still wins, which is right — a popup is the user's active interaction.
    //  - ZERO raycast surface: no GraphicRaycaster on the canvas AND raycastTarget=false on every
    //    Image/label (the kit factories default to false; nothing here opts in). Toasts float
    //    over the game world — a raycastable card would newly eat clicks IMGUI never could.
    //  - Fixed pool of 6 cards, SetActive-swapped — 6 matches the producer's own hard cap
    //    (AddOrUpdateMenuNotification evicts index 0 past 6), so the pool can never be short.
    //    Nothing is created or destroyed per toast, and no Material/Texture is ever Destroy()ed
    //    (destroyed materials render solid white — this project has hit that bug before).
    //
    // Geometry/animation is DrawMenuNotifications ported literal-for-literal (box sizing, the 8
    // notificationPosition anchors, in/out slide+fade, progress drain, inner rects); canvas
    // scaleFactor = GetUiScale() so 1 canvas unit = 1 IMGUI logical unit and the math transfers
    // 1:1 (the Status Overlay precedent). Text sizing uses the PoC-proven technique
    // (BuildUguiPocToastSpike, HeartopiaComplete.UguiPoc.cs): assign the text FIRST, then measure
    // via the STRING overloads of TMP GetPreferredValues — they parse the passed string directly
    // instead of trusting the component's same-frame parse-dirty bookkeeping. All four overloads
    // are compiled into this build (gameassembly-dumps/Unity.TextMeshPro/TMPro/TMP_Text.cs).
    // ============================================================================================
    public partial class HeartopiaComplete
    {
        // Pool size == the menuNotifications cap in AddOrUpdateMenuNotification. Do not raise one
        // without the other.
        private const int UguiToastPoolSize = 6;

        // See file header for the full band map. 29800 leaves headroom (29500..29800) for future
        // mod surfaces that should sit above the shell but below toasts.
        private const int UguiToastSortingOrder = 29800;

        // IMGUI literals (DrawMenuNotifications + the baked themeToastCardStyle texture). These
        // were never theme-driven on the IMGUI side either — deliberate fixed chrome so a toast
        // reads the same on any user theme; only the per-toast accent (item.Color) varies.
        private static readonly Color UguiToastShadowColor = new Color(0f, 0f, 0f, 0.32f);
        private static readonly Color UguiToastCardFill = new Color(0.082f, 0.11f, 0.153f, 0.97f);
        private static readonly Color UguiToastCardRing = new Color(0.165f, 0.205f, 0.27f, 1f);
        private static readonly Color UguiToastTextColor = new Color(0.94f, 0.96f, 1f, 1f);

        private sealed class UguiToastCard
        {
            public GameObject Root;
            public RectTransform RootRt;
            public Image Shadow;
            public Image Fill;
            public Image Ring;              // may be null (ring sprite build failed — cosmetic only)
            public RectTransform ChipRt;
            public Image Chip;
            public Image Dot;
            public GameObject Label;
            public RectTransform LabelRt;
            public TextMeshProUGUI LabelTmp; // null when the kit fell back to legacy Text
            public Text LabelLegacy;         // null on the TMP path
            public GameObject Progress;
            public RectTransform ProgressRt;
            public Image ProgressImg;

            // Measurement caches (EnsureUguiToastWidthMeasured / EnsureUguiToastHeightMeasured).
            // A null key means "not validly measured" — the next frame re-measures, which is the
            // self-heal for the PoC's documented caveat (a TMP component measured before it ever
            // Awoke can misbehave; once alive, the retry lands).
            public string MeasuredText;
            public float MeasuredTextW;
            public string HeightText;
            public float HeightMeasuredAtWidth = -1f;
            public float MeasuredTextH;

            // Last-applied per-frame state, to skip redundant interop writes: steady-state cards
            // (alpha 1, no slide) then cost one progress-bar sizeDelta write per frame and
            // nothing else.
            public float LastBoxW = -1f;
            public float LastBoxH = -1f;
            public float LastX = float.MinValue;
            public float LastY = float.MinValue;
            public float LastAlpha = -1f;
            public Color LastTint = new Color(-1f, 0f, 0f, 0f);
        }

        private GameObject uguiToastRoot;
        private Canvas uguiToastCanvas;
        private UguiToastCard[] uguiToastCards;
        private bool uguiToastBuildFailed;
        private int uguiToastErrorCount;        // driver disabled at 3 (DragErrorCount idiom)
        private int uguiToastMeasureLogCount;   // rejected-measure logs capped (retries are silent)
        private float uguiToastLastScale = -1f;
        private readonly List<MenuNotification> uguiToastVisibleScratch = new List<MenuNotification>(UguiToastPoolSize);
        private readonly float[] uguiToastBoxW = new float[UguiToastPoolSize];
        private readonly float[] uguiToastBoxH = new float[UguiToastPoolSize];

        // ----------------------------------------------------------------------------------------
        // Per-frame driver (wired next to ProcessUguiStatusOverlayOnUpdate in OnUpdate)
        // ----------------------------------------------------------------------------------------
        private void ProcessUguiToastsOnUpdate()
        {
            float now = Time.unscaledTime;

            // Sweep FIRST and unconditionally — ahead of the error gate and any build state, for
            // the reason in the file header (an unswept list pins at the 6-cap forever). Own
            // try/catch so pruning survives even if the render path below is broken.
            try { this.SweepExpiredMenuNotifications(now); } catch { }

            if (this.uguiToastErrorCount >= 3)
            {
                return;
            }

            try
            {
                this.CollectVisibleMenuNotificationsNewestFirst(this.uguiToastVisibleScratch);
                if (this.uguiToastVisibleScratch.Count == 0)
                {
                    if (this.uguiToastRoot != null && this.uguiToastRoot.activeSelf)
                    {
                        this.uguiToastRoot.SetActive(false);
                    }
                    return;
                }

                if (this.uguiToastRoot == null)
                {
                    if (this.uguiToastBuildFailed)
                    {
                        return; // already failed once this session; don't retry every frame
                    }

                    this.BuildUguiToastRoot();
                    if (this.uguiToastRoot == null)
                    {
                        this.uguiToastBuildFailed = true;
                        ModLogger.Msg("[UguiToast] build failed — see errors above");
                        return;
                    }

                    // Toast chrome reads no live ui* theme fields (fixed IMGUI literals — see
                    // above), but the rebuild keeps the LABEL font/material in step with whatever
                    // the kit currently provides and gives every UGUI surface one uniform
                    // recovery path. Idempotent by name, same as Shell/PoC/Overlay.
                    this.RegisterUguiThemeRebuilder("UguiToast", new System.Action(this.RebuildUguiToastForTheme));
                }

                if (!this.uguiToastRoot.activeSelf)
                {
                    this.uguiToastRoot.SetActive(true);
                }

                // Same scale source as IMGUI's GUI.matrix (and the Status Overlay): with
                // scaleFactor = GetUiScale(), layout below runs entirely in logical units.
                float scale = this.GetUiScale();
                if (scale != this.uguiToastLastScale && this.uguiToastCanvas != null)
                {
                    this.uguiToastCanvas.scaleFactor = scale;
                    this.uguiToastLastScale = scale;
                }

                this.LayoutUguiToastCards(this.uguiToastVisibleScratch, now);
            }
            catch (Exception ex)
            {
                this.uguiToastErrorCount++;
                ModLogger.Msg("[UguiToast] update error (" + this.uguiToastErrorCount
                    + "/3, disabled at 3): " + ex.Message);
                // Degrade to hidden rather than leaving a stuck half-laid-out stack on screen
                // (the sweep above keeps running regardless, so nothing leaks).
                try
                {
                    if (this.uguiToastRoot != null)
                    {
                        this.uguiToastRoot.SetActive(false);
                    }
                }
                catch { }
            }
        }

        // Theme-change rebuild (registered via RegisterUguiThemeRebuilder): destroy + null; the
        // driver lazily rebuilds on the next frame with something to show. No state to preserve —
        // cards re-derive everything from menuNotifications, and the measurement caches die with
        // the cards (fresh font ⇒ stale measures anyway).
        private void RebuildUguiToastForTheme()
        {
            try
            {
                if (this.uguiToastRoot == null)
                {
                    return; // never built — nothing stale (a first build reads fresh state)
                }

                try { Object.Destroy(this.uguiToastRoot); } catch { }
                this.uguiToastRoot = null;
                this.uguiToastCanvas = null;
                this.uguiToastCards = null;
                this.uguiToastLastScale = -1f;
                ModLogger.Msg("[UguiToast] rebuilt for theme change (lazy)");
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiToast] theme rebuild error: " + ex.Message);
            }
        }

        // ----------------------------------------------------------------------------------------
        // Construction
        // ----------------------------------------------------------------------------------------

        private void BuildUguiToastRoot()
        {
            GameObject go = null;
            try
            {
                this.EnsureUguiFonts();

                go = new GameObject("BugtopiaUguiToasts");
                Object.DontDestroyOnLoad(go);
                go.SetActive(false);

                Canvas canvas = go.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.overrideSorting = true;
                canvas.sortingOrder = UguiToastSortingOrder; // band rationale: file header
                canvas.scaleFactor = this.GetUiScale();
                // NO GraphicRaycaster — hard guarantee this canvas can never participate in
                // pointer raycasts. Combined with raycastTarget=false on every graphic below,
                // toasts are pure rendered pixels, exactly like their IMGUI predecessor.

                UguiToastCard[] cards = new UguiToastCard[UguiToastPoolSize];
                for (int i = 0; i < UguiToastPoolSize; i++)
                {
                    cards[i] = this.BuildUguiToastCard(go.transform, i);
                }

                this.uguiToastRoot = go;
                this.uguiToastCanvas = canvas;
                this.uguiToastCards = cards;
                this.uguiToastLastScale = this.GetUiScale();
                ModLogger.Msg("[UguiToast] built (sortingOrder " + UguiToastSortingOrder
                    + ", no raycaster, pool " + UguiToastPoolSize + ")");
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[UguiToast] BuildUguiToastRoot error: " + ex.Message);
                try
                {
                    if (go != null)
                    {
                        Object.Destroy(go);
                    }
                }
                catch { }
                this.uguiToastRoot = null;
                this.uguiToastCanvas = null;
                this.uguiToastCards = null;
            }
        }

        // One card, children in draw order (UGUI paints depth-first): shadow under fill under
        // ring under chip/dot under text under progress — the exact IMGUI paint order. Build-time
        // rects are throwaway; LayoutUguiToastCards owns every per-frame rect. The card root GO
        // carries NO Graphic of its own so the inflated shadow child can draw behind the fill
        // (children always paint after their parent's graphic).
        private UguiToastCard BuildUguiToastCard(Transform parent, int index)
        {
            UguiToastCard card = new UguiToastCard();

            GameObject root = this.CreateUguiGo("Toast" + index, parent);
            PlaceUguiTopLeft(root, 0f, 0f, 260f, 42f);
            root.SetActive(false);
            card.Root = root;
            card.RootRt = root.GetComponent<RectTransform>();

            // Drop shadow, INFLATED 1px past the card on every side (stretch offsets -1): the
            // card sprite's antialiased outer pixel row is semi-transparent, and over a bright
            // scene it reads as a bright stripe unless it blends against this dark halo instead
            // of the game (same fix DrawMenuNotifications carries). ppu 10/14 puts the sprite's
            // baked radius-10 corners at an effective radius 14 = the card's 13 + the 1px
            // inflate, keeping the halo's corner arcs CONCENTRIC with the card's — the IMGUI
            // side documents how a 1px radius mismatch shows up as an uneven gap at the corners.
            GameObject shadow = this.CreateUguiGo("Shadow", root.transform);
            StretchUguiFill(shadow, -1f, -1f, -1f, -1f);
            card.Shadow = this.AddUguiImage(shadow, UguiToastShadowColor, true, 10f / 14f);

            // Card body: fill + hairline ring, both at ppu 10/13 → effective corner radius 13,
            // matching the IMGUI baked texture exactly. (The kit ring's 1.5px band widens to
            // ~1.95px under this multiplier — accepted: corner-radius parity is the visible
            // trait, the ring is a subtle hairline either way.)
            GameObject fill = this.CreateUguiGo("Fill", root.transform);
            StretchUguiFill(fill, 0f, 0f, 0f, 0f);
            card.Fill = this.AddUguiImage(fill, UguiToastCardFill, true, 10f / 13f);
            this.AddUguiRingOverlay(fill, UguiToastCardRing, 10f / 13f);
            Transform ringT = fill.transform.Find("Ring"); // the overlay's fixed child name
            card.Ring = ringT != null ? ringT.GetComponent<Image>() : null;

            // Icon chip (26x26, tinted by the notification color) + status dot. Chip ppu 1.4 →
            // effective radius ~7.1, matching IMGUI's uiRoundedRectSprite stretched to 26px
            // (radius 9/32 of the box ≈ 7.3). Dot is the shell LIVE-rail circle recipe verbatim
            // (8px sliced at ppu 2.5 → border sum == box → pure radius-4 circle); the flat-image
            // rule for thin elements is about BARS whose border sum exceeds one axis, not this
            // deliberate degenerate-square construction the shell already ships.
            GameObject chip = this.CreateUguiGo("Chip", root.transform);
            PlaceUguiTopLeft(chip, 12f, 7f, 26f, 26f);
            card.ChipRt = chip.GetComponent<RectTransform>();
            card.Chip = this.AddUguiImage(chip, Color.clear, true, 1.4f);
            GameObject dot = this.CreateUguiGo("Dot", chip.transform);
            PlaceUguiTopLeft(dot, 9f, 9f, 8f, 8f);
            card.Dot = this.AddUguiImage(dot, Color.clear, true, 2.5f);

            // Message: 12pt bold, vertically centered left-aligned in a rect the LAYOUT sizes
            // (h-16 tall) — parity with the IMGUI GUIStyle (fontSize 12, bold, MiddleLeft,
            // wordWrap; the kit label already wraps and ellipsizes past its rect height, which
            // is what the 92px text-height clamp needs).
            GameObject label = this.CreateUguiLabel(root.transform, "Message", string.Empty, 12f, UguiToastTextColor, false);
            this.TrySetUguiLabelBold(label);
            PlaceUguiTopLeft(label, 48f, 6f, 200f, 26f);
            card.Label = label;
            card.LabelRt = label.GetComponent<RectTransform>();
            card.LabelTmp = label.GetComponent<TextMeshProUGUI>();
            card.LabelLegacy = card.LabelTmp == null ? label.GetComponent<Text>() : null;

            // Progress bar: FLAT image (no sprite). At 2.5px tall a 9-slice is the degenerate-
            // border trap (border sum far exceeds the height); IMGUI's capsule ends are sub-pixel
            // at this size anyway, so a flat quad is visually identical.
            GameObject progress = this.CreateUguiGo("Progress", root.transform);
            PlaceUguiTopLeft(progress, 12f, 36f, 100f, 2.5f);
            card.Progress = progress;
            card.ProgressRt = progress.GetComponent<RectTransform>();
            card.ProgressImg = this.AddUguiImage(progress, Color.clear, false, 1f);

            return card;
        }

        // ----------------------------------------------------------------------------------------
        // Per-frame layout — DrawMenuNotifications' two passes (measure sizes, then place/animate)
        // ported with its literals intact. All coordinates are logical units (canvas scaleFactor
        // handles the pixel mapping).
        // ----------------------------------------------------------------------------------------
        private void LayoutUguiToastCards(List<MenuNotification> visible, float now)
        {
            float screenW = this.GetLogicalScreenWidth();
            float screenH = this.GetLogicalScreenHeight();
            // 260 was the IMGUI drawer's `area.width` (the fixed Rect passed at Gui.cs:124) —
            // the minimum card width; 78 = 48px left gutter (chip zone) + 30px right padding.
            float maxWidth = Mathf.Clamp(screenW * 0.44f, 260f, 520f);

            int count = Mathf.Min(visible.Count, UguiToastPoolSize);

            // Pass 1: sizes. Cards are activated here so their TMP components are alive by the
            // time they measure (first-frame measures on a never-awoken component are the PoC's
            // documented caveat; the caches self-heal by re-measuring next frame regardless).
            float totalHeight = 0f;
            for (int i = 0; i < count; i++)
            {
                UguiToastCard card = this.uguiToastCards[i];
                MenuNotification item = visible[i];
                SetUguiGoActive(card.Root, true);

                string msg = item.Message ?? string.Empty;
                this.EnsureUguiToastWidthMeasured(card, msg);
                float boxWidth = Mathf.Round(Mathf.Clamp(card.MeasuredTextW + 78f, 260f, maxWidth));
                float textWidth = boxWidth - 60f;
                this.EnsureUguiToastHeightMeasured(card, msg, textWidth);
                float textHeight = Mathf.Clamp(card.MeasuredTextH, 18f, 92f);
                float boxHeight = Mathf.Round(Mathf.Clamp(textHeight + 20f, 42f, 112f));

                this.uguiToastBoxW[i] = boxWidth;
                this.uguiToastBoxH[i] = boxHeight;
                totalHeight += boxHeight;
                if (i < count - 1)
                {
                    totalHeight += 10f; // vertical gap between cards
                }
            }

            // Park the unused tail of the pool.
            for (int i = count; i < UguiToastPoolSize; i++)
            {
                SetUguiGoActive(this.uguiToastCards[i].Root, false);
            }

            // Pass 2: place + animate, newest first (the collector already reversed the list).
            float xMargin = 20f;
            float topY = 14f;
            float middleY = Mathf.Max(14f, (screenH - totalHeight) * 0.5f);
            float bottomY = Mathf.Max(14f, screenH - totalHeight - 20f);
            float y;
            switch (this.notificationPosition)
            {
                case 1: // Middle Left
                case 6: // Middle Right
                    y = middleY;
                    break;
                case 2: // Bottom Left
                case 4: // Bottom Center
                case 7: // Bottom Right
                    y = bottomY;
                    break;
                default: // Top Left / Top Center / Top Right
                    y = topY;
                    break;
            }

            for (int i = 0; i < count; i++)
            {
                UguiToastCard card = this.uguiToastCards[i];
                MenuNotification item = visible[i];
                float boxWidth = this.uguiToastBoxW[i];
                float boxHeight = this.uguiToastBoxH[i];

                // Duration is producer-clamped to >= 0.1, the Max is belt-and-braces against a
                // hand-rolled entry ever reaching here with 0.
                float remain = Mathf.Clamp01((item.ExpireAt - now) / Mathf.Max(0.1f, item.Duration));

                // Smooth in/out: 0.12s fade/slide in, 0.18s out; alpha == anim; the slide eases
                // the card 18px toward its screen edge while faded.
                float inAnim = Mathf.Clamp01((now - item.CreatedAt) / 0.12f);
                float outAnim = Mathf.Clamp01((item.ExpireAt - now) / 0.18f);
                float anim = Mathf.Min(inAnim, outAnim);
                float alpha = anim;
                float slide = (1f - anim) * 18f;

                float boxX;
                switch (this.notificationPosition)
                {
                    case 0: // left column slides in from the left edge
                    case 1:
                    case 2:
                        boxX = xMargin - slide;
                        break;
                    case 3: // center column: no slide (nowhere natural to slide from)
                    case 4:
                        boxX = (screenW - boxWidth) * 0.5f;
                        break;
                    default: // right column slides in from the right edge
                        boxX = screenW - boxWidth - xMargin + slide;
                        break;
                }

                this.ApplyUguiToastCard(card, item, boxX, y, boxWidth, boxHeight, alpha, remain);

                y += boxHeight + 10f;
                if (y > screenH - 42f)
                {
                    // Off-screen overflow: IMGUI broke out of its draw loop here; the pooled
                    // equivalent is parking the remaining cards.
                    for (int j = i + 1; j < count; j++)
                    {
                        SetUguiGoActive(this.uguiToastCards[j].Root, false);
                    }
                    break;
                }
            }
        }

        // Writes one card's rects + colors, skipping anything unchanged since the last frame —
        // a settled card (alpha 1, no slide, static text) costs exactly one sizeDelta write per
        // frame (the draining progress bar), keeping the per-frame interop chatter trivial.
        private void ApplyUguiToastCard(UguiToastCard card, MenuNotification item,
            float x, float y, float w, float h, float alpha, float remain)
        {
            bool sizeChanged = w != card.LastBoxW || h != card.LastBoxH;
            if (sizeChanged)
            {
                card.RootRt.sizeDelta = new Vector2(w, h);
                // Inner rects, IMGUI verbatim: message (x+48, y+6, w-60, h-16); icon chip
                // (x+12, y+(h-26)*0.5-1, 26, 26); progress baseline at box.yMax-6, x+12.
                // Shadow/fill/ring are stretched children and follow the root for free.
                card.LabelRt.sizeDelta = new Vector2(w - 60f, h - 16f);
                card.ChipRt.anchoredPosition = new Vector2(12f, -((h - 26f) * 0.5f - 1f));
                card.ProgressRt.anchoredPosition = new Vector2(12f, -(h - 6f));
                card.LastBoxW = w;
                card.LastBoxH = h;
            }

            if (x != card.LastX || y != card.LastY)
            {
                card.RootRt.anchoredPosition = new Vector2(x, -y);
                card.LastX = x;
                card.LastY = y;
            }

            // Progress drain — width moves every frame by design; IMGUI only drew it past 5px
            // (a shorter capsule degenerated), kept for parity.
            float progressW = (w - 24f) * remain;
            bool progressVisible = progressW > 5f;
            SetUguiGoActive(card.Progress, progressVisible);
            if (progressVisible)
            {
                card.ProgressRt.sizeDelta = new Vector2(progressW, 2.5f);
            }

            // Colors: only during fade-in/out or when a keyed update recolors the toast in place.
            Color tint = item.Color;
            if (alpha != card.LastAlpha || tint != card.LastTint)
            {
                card.Shadow.color = new Color(0f, 0f, 0f, UguiToastShadowColor.a * alpha);
                card.Fill.color = new Color(UguiToastCardFill.r, UguiToastCardFill.g, UguiToastCardFill.b, UguiToastCardFill.a * alpha);
                if (card.Ring != null)
                {
                    card.Ring.color = new Color(UguiToastCardRing.r, UguiToastCardRing.g, UguiToastCardRing.b, alpha);
                }
                card.Chip.color = new Color(tint.r, tint.g, tint.b, 0.16f * alpha);
                card.Dot.color = new Color(tint.r, tint.g, tint.b, 0.95f * alpha);
                card.ProgressImg.color = new Color(tint.r, tint.g, tint.b, 0.85f * alpha);
                this.SetUguiLabelColor(card.Label, new Color(UguiToastTextColor.r, UguiToastTextColor.g, UguiToastTextColor.b, alpha));
                card.LastAlpha = alpha;
                card.LastTint = tint;
            }
        }

        // ----------------------------------------------------------------------------------------
        // Text measurement — the PoC technique (assign first, measure the STRING) with per-card
        // caches. On a rejected measure the cache key stays null (retry next frame — see the
        // caveat note on UguiToastCard) and layout proceeds on fallback values that resolve to
        // the minimum card (260x42), so a broken measure degrades to a small card, never garbage
        // or a dead stack. Reject logging is capped; the retries themselves are silent.
        // ----------------------------------------------------------------------------------------

        private void EnsureUguiToastWidthMeasured(UguiToastCard card, string msg)
        {
            if (string.Equals(card.MeasuredText, msg, StringComparison.Ordinal))
            {
                return; // cache hit (also covers the empty string once stored)
            }

            // Assign FIRST — this is also the render path's only text write; measurement and
            // display can never disagree about which string is current.
            this.SetUguiLabelText(card.Label, msg);
            card.MeasuredText = null;
            card.HeightText = null;
            card.MeasuredTextW = 182f; // fallback: 260 min box - 78 padding
            card.MeasuredTextH = 18f;  // fallback: min text height

            if (msg.Length == 0)
            {
                card.MeasuredText = msg; // nothing to measure; the min-box fallbacks ARE correct
                card.HeightText = msg;
                card.HeightMeasuredAtWidth = 0f;
                card.MeasuredTextW = 0f;
                return;
            }

            try
            {
                float textW = 0f;
                if (card.LabelTmp != null)
                {
                    // String overload = single-line preferred size of THIS string (the IMGUI
                    // equivalent was style.CalcSize) — bold/size come from the component state.
                    Vector2 pref = card.LabelTmp.GetPreferredValues(msg);
                    textW = pref.x;
                }
                else if (card.LabelLegacy != null)
                {
                    // Legacy Text preferredWidth is the unconstrained single-line width.
                    textW = card.LabelLegacy.preferredWidth;
                }

                // Sanity gate (PoC): reject non-finite/non-positive/absurd values — a leaked TMP
                // internal 32767 "large margin" sentinel would otherwise become the box width.
                if (textW > 0f && textW < 2000f && !float.IsNaN(textW) && !float.IsInfinity(textW))
                {
                    card.MeasuredTextW = textW;
                    card.MeasuredText = msg;
                }
                else if (this.uguiToastMeasureLogCount < 3)
                {
                    this.uguiToastMeasureLogCount++;
                    ModLogger.Msg("[UguiToast] width measure rejected: " + textW.ToString("0.##"));
                }
            }
            catch (Exception ex)
            {
                if (this.uguiToastMeasureLogCount < 3)
                {
                    this.uguiToastMeasureLogCount++;
                    ModLogger.Msg("[UguiToast] width measure failed: " + ex.Message);
                }
            }
        }

        private void EnsureUguiToastHeightMeasured(UguiToastCard card, string msg, float textWidth)
        {
            if (string.Equals(card.HeightText, msg, StringComparison.Ordinal)
                && Mathf.Abs(card.HeightMeasuredAtWidth - textWidth) <= 0.5f)
            {
                return; // cache hit: same string wrapped at (effectively) the same width
            }

            card.HeightText = null;
            card.MeasuredTextH = 18f; // fallback: min text height → min 42 box

            if (msg.Length == 0)
            {
                card.HeightText = msg;
                card.HeightMeasuredAtWidth = textWidth;
                return;
            }

            try
            {
                float textH = 0f;
                if (card.LabelTmp != null)
                {
                    // (string, width, 0) overload = wrapped height of THIS string at the given
                    // width (the IMGUI equivalent was style.CalcHeight(content, textWidth)).
                    Vector2 pref = card.LabelTmp.GetPreferredValues(msg, textWidth, 0f);
                    textH = pref.y;
                }
                else if (card.LabelLegacy != null)
                {
                    // Legacy preferredHeight wraps at the CURRENT rect width — set it first.
                    // Pass 2 re-applies the same width, so this early write is never stale.
                    card.LabelRt.sizeDelta = new Vector2(textWidth, card.LabelRt.sizeDelta.y);
                    textH = card.LabelLegacy.preferredHeight;
                }

                if (textH > 0f && textH < 500f && !float.IsNaN(textH) && !float.IsInfinity(textH))
                {
                    card.MeasuredTextH = textH;
                    card.HeightText = msg;
                    card.HeightMeasuredAtWidth = textWidth;
                }
                else if (this.uguiToastMeasureLogCount < 3)
                {
                    this.uguiToastMeasureLogCount++;
                    ModLogger.Msg("[UguiToast] height measure rejected: " + textH.ToString("0.##"));
                }
            }
            catch (Exception ex)
            {
                if (this.uguiToastMeasureLogCount < 3)
                {
                    this.uguiToastMeasureLogCount++;
                    ModLogger.Msg("[UguiToast] height measure failed: " + ex.Message);
                }
            }
        }
    }
}
