using System;
using System.Collections.Generic;
using static Bugtopia.Launcher.Win32.Native;

namespace Bugtopia.Launcher.Win32
{
    internal enum ButtonKind
    {
        /// <summary>.btn.secondary.compact - the buttons inside the step cards and the dialog's Cancel.</summary>
        Secondary,

        /// <summary>.btn.primary.compact - the dialog's confirm button.</summary>
        Primary,

        /// <summary>The simple screen's #play: .btn.primary with 15px 34px padding and 1rem text.</summary>
        PrimaryLarge,

        /// <summary>.btn.danger - the countdown's Cancel.</summary>
        Danger,

        /// <summary>.checkbox-label: an accent-coloured box and its label.</summary>
        Checkbox,

        /// <summary>.btn.secondary without .compact: Detect, Browse..., Default in the expert view.</summary>
        SecondaryLarge,

        /// <summary>.btn.primary without .compact: the expert view's Launch.</summary>
        PrimaryMedium,

        /// <summary>.select-dropdown: the current choice and a chevron; the list opens as a menu.</summary>
        Select,
    }

    /// <summary>
    /// A real BUTTON child window drawn by us. Real so that Tab, Space, Enter, focus and screen readers
    /// all work as they do for any Windows button; drawn by us so it looks like the page's.
    /// </summary>
    internal sealed class Button
    {
        internal nint Hwnd;
        internal int Id;
        internal ButtonKind Kind;
        internal string Text;
        internal string[] Icon;
        internal bool Checked;
        internal bool Hover;
        internal Surface Owner;

        /// <summary>Where it sits in the owner's client area, in pixels, scroll included.</summary>
        internal RECT Bounds;

        /// <summary>Shown by the layout pass in progress; whatever is not is hidden when it ends.</summary>
        internal bool Placed;
    }

    /// <summary>
    /// A top-level window painted into a back buffer: one full frame per paint, blitted in one go, so
    /// nothing flickers. The buffer doubles as the backdrop for the owner-drawn buttons, which copy the
    /// pixels behind them before drawing so their rounded corners sit on whatever the window drew there.
    /// </summary>
    internal abstract unsafe class Surface
    {
        internal const uint Bg = 0x0f172a, CardBorder = 0xffffff, Accent = 0x6366f1, AccentHover = 0x4f46e5;
        internal const uint TextMain = 0xf8fafc, TextMuted = 0x94a3b8, Warning = 0xf59e0b, Success = 0x10b981, Danger = 0xef4444;

        private static readonly Dictionary<nint, Surface> Surfaces = new Dictionary<nint, Surface>();
        private static readonly Dictionary<nint, Button> AllButtons = new Dictionary<nint, Button>();
        private static readonly HashSet<string> Registered = new HashSet<string>();
        private static Surface creating;

        internal nint Hwnd;
        internal float Scale = 1f;
        internal Fonts Fonts;

        /// <summary>Set while a dialog is open over this window: everything it draws goes under a scrim.</summary>
        internal bool Dimmed;

        protected readonly List<Button> Buttons = new List<Button>();

        private nint backDc, backBitmap, backOld;
        private int backWidth, backHeight;

        protected float S(float cssPx) => cssPx * Scale;

        /// <summary>A hairline in CSS pixels, never thinner than one device pixel.</summary>
        protected float Px(float cssPx) => MathF.Max(1, MathF.Round(cssPx * Scale));

        // ---- window ----------------------------------------------------------

        protected void Create(string className, string title, uint style, uint exStyle, int x, int y, int w, int h,
                              nint owner, bool dropShadow)
        {
            nint instance = GetModuleHandleW(null);
            if (Registered.Add(className))
            {
                fixed (char* cls = className)
                {
                    var wc = new WNDCLASSEXW
                    {
                        cbSize = (uint)sizeof(WNDCLASSEXW),
                        style = dropShadow ? CS_DROPSHADOW : 0,
                        lpfnWndProc = &WindowProc,
                        hInstance = instance,
                        hCursor = LoadCursorW(0, IDC_ARROW),
                        lpszClassName = cls,
                    };
                    RegisterClassExW(&wc);
                }
            }

            creating = this;
            fixed (char* cls = className)
            fixed (char* text = title)
                Hwnd = CreateWindowExW(exStyle, cls, text, style, x, y, w, h, owner, 0, instance, 0);
            creating = null;
        }

        protected void SetScale(uint dpi)
        {
            Scale = dpi / 96f;
            Fonts?.Dispose();
            Fonts = new Fonts(Scale);
        }

        [System.Runtime.InteropServices.UnmanagedCallersOnly]
        private static nint WindowProc(nint hwnd, uint msg, nint w, nint l)
        {
            if (!Surfaces.TryGetValue(hwnd, out Surface surface))
            {
                if (creating == null)
                    return DefWindowProcW(hwnd, msg, w, l);
                surface = creating;
                surface.Hwnd = hwnd;
                Surfaces[hwnd] = surface;
            }

            try
            {
                if (msg == 0x0082)   // WM_NCDESTROY
                {
                    Surfaces.Remove(hwnd);
                    surface.Destroyed();
                    return DefWindowProcW(hwnd, msg, w, l);
                }
                return surface.Handle(msg, w, l);
            }
            catch (Exception ex)
            {
                // An exception may not cross back into user32: under NativeAOT that is a fail-fast.
                surface.Report(ex);
                return DefWindowProcW(hwnd, msg, w, l);
            }
        }

        protected virtual void Destroyed()
        {
            ReleaseBackBuffer();
        }

        protected abstract void Report(Exception ex);

        /// <summary>For a popup that belongs to this window: its errors go where this window's go.</summary>
        internal void ReportError(Exception ex) => Report(ex);

        protected virtual nint Handle(uint msg, nint w, nint l)
        {
            switch (msg)
            {
                case WM_ERASEBKGND:
                    // Whatever was last drawn, rather than a flash of class brush before the paint.
                    if (backDc != 0)
                    {
                        RECT rc;
                        GetClientRect(Hwnd, &rc);
                        BitBlt(w, 0, 0, rc.Width, rc.Height, backDc, 0, 0, SRCCOPY);
                    }
                    return 1;

                case WM_PAINT:
                    Paint();
                    return 0;

                case WM_DRAWITEM:
                    DrawButton((DRAWITEMSTRUCT*)l);
                    return 1;
            }
            return DefWindowProcW(Hwnd, msg, w, l);
        }

        // ---- painting --------------------------------------------------------

        protected abstract void Render(nint dc, int width, int height);

        private void Paint()
        {
            PAINTSTRUCT ps;
            nint dc = BeginPaint(Hwnd, &ps);
            RECT rc;
            GetClientRect(Hwnd, &rc);
            if (rc.Width > 0 && rc.Height > 0)
            {
                EnsureBackBuffer(rc.Width, rc.Height);
                Render(backDc, rc.Width, rc.Height);
                BitBlt(dc, 0, 0, rc.Width, rc.Height, backDc, 0, 0, SRCCOPY);
            }
            EndPaint(Hwnd, &ps);

            // The buttons copy their backdrop out of the frame just drawn, so they follow it.
            foreach (Button b in Buttons)
                if (IsWindowVisible(b.Hwnd) != 0)
                    InvalidateRect(b.Hwnd, null, 0);
        }

        internal void Invalidate() => InvalidateRect(Hwnd, null, 0);

        private void EnsureBackBuffer(int width, int height)
        {
            if (backDc != 0 && width == backWidth && height == backHeight)
                return;
            ReleaseBackBuffer();
            backDc = CreateCompatibleDC(0);
            backBitmap = CreateDib(width, height, out _);
            backOld = SelectObject(backDc, backBitmap);
            backWidth = width;
            backHeight = height;
        }

        private void ReleaseBackBuffer()
        {
            if (backDc == 0)
                return;
            SelectObject(backDc, backOld);
            DeleteObject(backBitmap);
            DeleteDC(backDc);
            backDc = 0;
        }

        internal static nint CreateDib(int width, int height, out uint* bits)
        {
            var header = new BITMAPINFOHEADER
            {
                biSize = (uint)sizeof(BITMAPINFOHEADER),
                biWidth = width,
                biHeight = -height,   // top-down
                biPlanes = 1,
                biBitCount = 32,
            };
            void* raw;
            nint bitmap = CreateDIBSection(0, &header, 0, &raw, 0, 0);
            bits = (uint*)raw;
            return bitmap;
        }

        // ---- buttons ---------------------------------------------------------

        protected Button AddButton(int id, ButtonKind kind, string text, string[] icon = null)
        {
            var button = new Button { Id = id, Kind = kind, Text = text, Icon = icon, Owner = this };
            fixed (char* cls = "BUTTON")
            fixed (char* t = text)
                button.Hwnd = CreateWindowExW(0, cls, t, WS_CHILD | WS_CLIPSIBLINGS | WS_TABSTOP | BS_OWNERDRAW, 0, 0, 0, 0,
                                              Hwnd, id, GetModuleHandleW(null), 0);
            SetWindowSubclass(button.Hwnd, &ButtonProc, 1, 0);
            AllButtons[button.Hwnd] = button;
            Buttons.Add(button);
            return button;
        }

        protected static void SetButtonText(Button b, string text)
        {
            if (b.Text == text)
                return;
            b.Text = text;
            SetText(b.Hwnd, text);   // what a screen reader announces
            InvalidateRect(b.Hwnd, null, 0);
        }

        protected static void Place(Button b, float x, float y, float w, float h, bool visible)
        {
            var bounds = new RECT
            {
                left = (int)MathF.Round(x),
                top = (int)MathF.Round(y),
                right = (int)MathF.Round(x + w),
                bottom = (int)MathF.Round(y + h),
            };
            b.Placed = visible;
            bool wasVisible = IsWindowVisible(b.Hwnd) != 0;
            bool moved = bounds.left != b.Bounds.left || bounds.top != b.Bounds.top ||
                         bounds.right != b.Bounds.right || bounds.bottom != b.Bounds.bottom;
            b.Bounds = bounds;
            if (moved || wasVisible != visible)
            {
                SetWindowPos(b.Hwnd, 0, bounds.left, bounds.top, bounds.Width, bounds.Height,
                             SWP_MOVECHILD | (visible ? SWP_SHOWWINDOW : SWP_HIDEWINDOW));
            }
        }

        protected static void Enable(Button b, bool enabled)
        {
            if ((IsWindowEnabled(b.Hwnd) != 0) != enabled)
                EnableWindow(b.Hwnd, enabled ? 1 : 0);
        }

        protected Button ButtonFor(nint hwnd) =>
            AllButtons.TryGetValue(hwnd, out Button b) && b.Owner == this ? b : null;

        /// <summary>Called when the pointer enters or leaves a button, for hover effects drawn by the window.</summary>
        protected virtual void HoverChanged(Button button)
        {
        }

        [System.Runtime.InteropServices.UnmanagedCallersOnly]
        private static nint ButtonProc(nint hwnd, uint msg, nint w, nint l, nuint id, nuint data)
        {
            if (AllButtons.TryGetValue(hwnd, out Button b))
            {
                try
                {
                    switch (msg)
                    {
                        case WM_MOUSEMOVE:
                            if (!b.Hover)
                            {
                                b.Hover = true;
                                var tme = new TRACKMOUSEEVENT { cbSize = (uint)sizeof(TRACKMOUSEEVENT), dwFlags = TME_LEAVE, hwndTrack = hwnd };
                                TrackMouseEvent(&tme);
                                InvalidateRect(hwnd, null, 0);
                                b.Owner.HoverChanged(b);
                            }
                            break;
                        case WM_MOUSELEAVE:
                            b.Hover = false;
                            InvalidateRect(hwnd, null, 0);
                            b.Owner.HoverChanged(b);
                            break;
                        case WM_SETCURSOR:
                            SetCursor(LoadCursorW(0, IDC_HAND));
                            return 1;
                        case WM_ERASEBKGND:
                            return 1;
                        case 0x0082:   // WM_NCDESTROY
                            AllButtons.Remove(hwnd);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    b.Owner.Report(ex);
                }
            }
            return DefSubclassProc(hwnd, msg, w, l);
        }

        private void DrawButton(DRAWITEMSTRUCT* di)
        {
            Button b = ButtonFor(di->hwndItem);
            if (b == null)
                return;

            int w = di->rcItem.Width, h = di->rcItem.Height;
            if (w <= 0 || h <= 0)
                return;

            nint dc = CreateCompatibleDC(di->hDC);
            nint bitmap = CreateDib(w, h, out uint* bits);
            nint old = SelectObject(dc, bitmap);

            // The window's own pixels behind the button, so the corners are not squares of guesswork.
            if (backDc != 0)
                BitBlt(dc, 0, 0, w, h, backDc, b.Bounds.left, b.Bounds.top, SRCCOPY);

            uint* backdrop = null;
            bool disabled = (di->itemState & ODS_DISABLED) != 0;
            if (disabled)
            {
                backdrop = (uint*)System.Runtime.InteropServices.NativeMemory.Alloc((nuint)(w * h * 4));
                Buffer.MemoryCopy(bits, backdrop, w * h * 4, w * h * 4);
            }

            bool focus = (di->itemState & ODS_FOCUS) != 0 && (di->itemState & ODS_NOFOCUSRECT) == 0;
            bool pressed = (di->itemState & ODS_SELECTED) != 0;
            PaintButton(dc, b, w, h, pressed, focus);

            // .btn:disabled { opacity: 0.4 } - the drawn button at 40% over what was behind it.
            if (disabled)
            {
                int count = w * h;
                for (int i = 0; i < count; i++)
                {
                    uint p = bits[i], q = backdrop[i];
                    uint r = (uint)(((p >> 16) & 0xFF) * 2 / 5 + ((q >> 16) & 0xFF) * 3 / 5);
                    uint g = (uint)(((p >> 8) & 0xFF) * 2 / 5 + ((q >> 8) & 0xFF) * 3 / 5);
                    uint bl = (uint)((p & 0xFF) * 2 / 5 + (q & 0xFF) * 3 / 5);
                    bits[i] = 0xFF000000 | (r << 16) | (g << 8) | bl;
                }
                System.Runtime.InteropServices.NativeMemory.Free(backdrop);
            }

            if (Dimmed)
            {
                nint g = Gdip.Begin(dc);
                Gdip.FillRect(g, Gdip.Argb(0x090d16, 0.72f), 0, 0, w, h);
                Gdip.End(g);
            }

            BitBlt(di->hDC, 0, 0, w, h, dc, 0, 0, SRCCOPY);
            SelectObject(dc, old);
            DeleteObject(bitmap);
            DeleteDC(dc);
        }

        /// <summary>Size of a button's box for its kind and text, in pixels.</summary>
        internal (float Width, float Height) Measure(Button b)
        {
            switch (b.Kind)
            {
                case ButtonKind.Checkbox:
                    return (S(16) + S(8) + Fonts.Measure(Fonts.Check, b.Text), MathF.Max(S(16), Fonts.Check.LineHeight));
                case ButtonKind.PrimaryLarge:
                    return (S(34) * 2 + S(17) + S(8) + Fonts.Measure(Fonts.Play, b.Text), S(15) * 2 + Fonts.Play.LineHeight);
                case ButtonKind.Danger:
                    return (S(20) * 2 + Fonts.Measure(Fonts.Button, b.Text), S(11) * 2 + Fonts.Button.LineHeight);
                case ButtonKind.Primary:
                    return (S(14) * 2 + Fonts.Measure(Fonts.Button, b.Text), S(10) * 2 + Fonts.Button.LineHeight);
                case ButtonKind.PrimaryMedium:
                    return (S(20) * 2 + IconRun(b) + Fonts.Measure(Fonts.Button, b.Text), S(11) * 2 + Fonts.Button.LineHeight);
                case ButtonKind.Select:
                {
                    // Width is the layout's to decide (flex: 1); the height is the select's own.
                    float border = MathF.Max(1, MathF.Round(S(1)));
                    return (S(14) + S(38) + border * 2 + Fonts.Measure(Fonts.Input, b.Text), S(10) * 2 + border * 2 + Fonts.Input.LineHeight);
                }
                case ButtonKind.SecondaryLarge:
                {
                    float border = MathF.Max(1, MathF.Round(S(1)));
                    return (S(20) * 2 + border * 2 + IconRun(b) + Fonts.Measure(Fonts.Button, b.Text),
                            S(11) * 2 + border * 2 + Fonts.Button.LineHeight);
                }
                default:
                {
                    // .btn.secondary carries a 1px border on top of its padding.
                    float border = MathF.Max(1, MathF.Round(S(1)));
                    return (S(14) * 2 + border * 2 + IconRun(b) + Fonts.Measure(Fonts.Button, b.Text),
                            S(10) * 2 + border * 2 + Fonts.Button.LineHeight);
                }
            }
        }

        /// <summary>The icon and the 8px gap after it - or just the icon, for a button that is only an icon.</summary>
        private float IconRun(Button b) =>
            b.Icon == null ? 0 : S(17) + (string.IsNullOrEmpty(b.Text) ? 0 : S(8));

        private void PaintButton(nint dc, Button b, int w, int h, bool pressed, bool focus)
        {
            float radius = S(10);
            nint g = Gdip.Begin(dc);
            Font font = Fonts.Button;
            uint textColor = TextMain;

            switch (b.Kind)
            {
                case ButtonKind.Checkbox:
                {
                    // A label with too little room wraps rather than running under its neighbour; the box
                    // then sits on its first line, not in the middle of the paragraph.
                    float box = S(16), labelW = w - S(24);
                    TextBlock wrapped = Fonts.Measure(Fonts.Check, b.Text) <= labelW + 0.5f
                        ? null
                        : TextBlock.Layout(new List<TextRun> { new TextRun(b.Text) }, Fonts.Check, Fonts.Check,
                                           labelW, Fonts.Check.LineHeight);
                    float labelTop = wrapped == null ? 0 : (h - wrapped.Height) / 2;
                    float top = MathF.Max(0, labelTop + (wrapped == null ? h : Fonts.Check.LineHeight) / 2 - box / 2);
                    if (b.Checked)
                    {
                        Gdip.FillRoundRect(g, Gdip.Argb(b.Hover ? AccentHover : Accent), 0, top, box, box, S(3));
                        Gdip.StrokeIcon(g, CheckMark, S(1.5f), top + S(1.5f), box - S(3), Gdip.Argb(0xffffff), 3f);
                    }
                    else
                    {
                        Gdip.FillRoundRect(g, Gdip.Argb(0xffffff), 0, top, box, box, S(3));
                        Gdip.StrokeRoundRect(g, Gdip.Argb(b.Hover ? 0x4f4f4fu : 0x767676u), 0, top, box, box, S(3), MathF.Max(1, S(1)));
                    }
                    if (focus)
                        Gdip.StrokeRoundRect(g, Gdip.Argb(0xa5b4fc), -S(2), top - S(2), box + S(4), box + S(4), S(4), MathF.Max(2, S(2)));
                    Gdip.End(g);

                    if (wrapped == null)
                    {
                        DrawLabel(dc, Fonts.Check, b.Text, S(24), 0, labelW, h, TextMain, false);
                    }
                    else
                    {
                        SetBkMode(dc, TRANSPARENT);
                        wrapped.Draw(dc, S(24), labelTop, ColorRef(TextMain), ColorRef(TextMain));
                    }
                    return;
                }

                case ButtonKind.Select:
                {
                    float border = MathF.Max(1, MathF.Round(S(1)));
                    Gdip.FillRoundRect(g, Gdip.Argb(Bg, 0.8f), 0, 0, w, h, radius);
                    Gdip.StrokeRoundRect(g, b.Hover ? Gdip.Argb(0x475569, 0.7f) : Gdip.Argb(CardBorder, 0.08f),
                                         0, 0, w, h, radius, border);
                    if (focus)
                        Gdip.StrokeRoundRect(g, Gdip.Argb(0xa5b4fc), 0, 0, w, h, radius, MathF.Max(2, S(2)));
                    // The page's chevron: 14px, 13px in from the right, stroked in the muted colour.
                    Gdip.StrokeIcon(g, Chevron, w - S(13) - S(14), (h - S(14)) / 2, S(14), Gdip.Argb(TextMuted), 2.5f);
                    Gdip.End(g);

                    float textX = border + S(14);
                    DrawLabel(dc, Fonts.Input, EllipsisFit(Fonts.Input, b.Text, w - textX - S(38)), textX, 0, w, h, TextMain, false);
                    return;
                }

                case ButtonKind.Secondary:
                case ButtonKind.SecondaryLarge:
                {
                    uint fill = b.Hover ? Gdip.Argb(0x475569, 0.7f) : Gdip.Argb(0x334155, 0.5f);
                    Gdip.FillRoundRect(g, fill, 0, 0, w, h, radius);
                    Gdip.StrokeRoundRect(g, Gdip.Argb(CardBorder, 0.08f), 0, 0, w, h, radius, MathF.Max(1, MathF.Round(S(1))));
                    break;
                }

                case ButtonKind.Danger:
                    Gdip.FillRoundRectGradient(g, 0, 0, w, h, radius, Gdip.Argb(Danger), Gdip.Argb(0xdc2626), true);
                    textColor = 0xffffff;
                    break;

                default:
                    Gdip.FillRoundRectGradient(g, 0, 0, w, h, radius, Gdip.Argb(Accent), Gdip.Argb(AccentHover), true);
                    textColor = 0xffffff;
                    if (b.Kind == ButtonKind.PrimaryLarge)
                        font = Fonts.Play;
                    break;
            }

            if (pressed)
                Gdip.FillRoundRect(g, Gdip.Argb(0x000000, 0.12f), 0, 0, w, h, radius);
            if (focus)
                Gdip.StrokeRoundRect(g, Gdip.Argb(0xa5b4fc), 0, 0, w, h, radius, MathF.Max(2, S(2)));

            float textWidth = Fonts.Measure(font, b.Text);
            float iconSize = b.Icon != null ? S(17) : 0;
            float iconGap = b.Icon != null && !string.IsNullOrEmpty(b.Text) ? S(8) : 0;
            float left = (w - (iconSize + iconGap + textWidth)) / 2;
            if (b.Icon != null)
                Gdip.StrokeIcon(g, b.Icon, left, (h - iconSize) / 2, iconSize, Gdip.Argb(textColor), 2f);
            Gdip.End(g);

            DrawLabel(dc, font, b.Text, left + iconSize + iconGap, 0, textWidth + 1, h, textColor, false);
        }

        internal static readonly string[] CheckMark = { "M20 6L9 17L4 12" };
        internal static readonly string[] Chevron = { "M6 9L12 15L18 9" };

        /// <summary>The text, cut with an ellipsis when it would not fit - a select shows one line, never two.</summary>
        protected static string EllipsisFit(Font font, string text, float width)
        {
            if (string.IsNullOrEmpty(text) || Fonts.Measure(font, text) <= width)
                return text;
            const string dots = "…";
            int fit = Fonts.Fit(font, text, width - Fonts.Measure(font, dots));
            return fit <= 0 ? dots : text.Substring(0, fit) + dots;
        }

        /// <summary>One line of GDI text, vertically centred in a box.</summary>
        protected static void DrawLabel(nint dc, Font font, string text, float x, float y, float w, float h, uint rgb, bool centre)
        {
            if (string.IsNullOrEmpty(text))
                return;
            SetBkMode(dc, TRANSPARENT);
            SelectObject(dc, font.Handle);
            SetTextColor(dc, ColorRef(rgb));
            float width = Fonts.Measure(font, text);
            float left = centre ? x + (w - width) / 2 : x;
            float top = y + (h - font.Height) / 2;
            fixed (char* p = text)
                ExtTextOutW(dc, (int)MathF.Round(left), (int)MathF.Round(top), 0, null, p, (uint)text.Length, null);
        }

        /// <summary>0xRRGGBB, as the colours are written in CSS, to the 0x00BBGGRR GDI takes.</summary>
        internal static uint ColorRef(uint rgb) => Rgb((int)((rgb >> 16) & 0xFF), (int)((rgb >> 8) & 0xFF), (int)(rgb & 0xFF));
    }
}
