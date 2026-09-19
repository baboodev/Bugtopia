using System;
using System.Collections.Generic;
using static Bugtopia.Launcher.Win32.Native;

namespace Bugtopia.Launcher.Win32
{
    /// <summary>
    /// A select's list: a borderless popup as wide as the select, directly under it (above it when the
    /// screen ends first), drawn in the launcher's own colours.
    ///
    /// At most <see cref="MaxRows"/> rows show at once, and fewer when the screen has less room: the
    /// release list runs to fifty builds, and listed in full it was taller than the monitor. The rest
    /// scrolls - with the wheel, the arrows, Page Up/Down, and a thin bar on the right that can be
    /// dragged or clicked. The current choice opens in view.
    ///
    /// It never takes activation - the launcher stays the active window, so the keys, and the wheel,
    /// arrive where they always do and are read here before anything else sees them, the way a menu
    /// reads them. It closes on a choice, Escape, Tab, a click anywhere else, or the launcher losing
    /// the foreground.
    /// </summary>
    internal sealed unsafe class ListPopup : Surface
    {
        private const int MaxRows = 10;

        private readonly Surface owner;
        private readonly IReadOnlyList<string> items;
        private readonly int current;
        private int highlighted;
        private int first;       // the top row on show
        private int visible;     // how many rows fit
        private float rowH, pad, border;
        private bool closed;

        // Dragging the scrollbar's thumb: where in the thumb it was taken hold of.
        private bool dragging;
        private float dragOffset;

        private ListPopup(Surface owner, IReadOnlyList<string> items, int current)
        {
            this.owner = owner;
            this.items = items;
            this.current = current;
            highlighted = Math.Max(0, current);
        }

        /// <summary>Shows the list under <paramref name="anchor"/> and returns the index chosen, or -1.</summary>
        internal static int Show(Surface owner, Button anchor, IReadOnlyList<string> items, int current)
        {
            if (items.Count == 0)
                return -1;
            return new ListPopup(owner, items, current).Run(anchor);
        }

        private bool Scrolls => items.Count > visible;

        private int Run(Button anchor)
        {
            Scale = owner.Scale;
            Fonts = new Fonts(Scale);
            rowH = MathF.Round(S(8) * 2 + Fonts.Input.LineHeight);
            pad = MathF.Round(S(4));
            border = MathF.Max(1, MathF.Round(S(1)));
            int gap = (int)MathF.Round(S(4));

            var topLeft = new POINT { x = anchor.Bounds.left, y = anchor.Bounds.top };
            ClientToScreen(owner.Hwnd, &topLeft);
            int width = anchor.Bounds.Width;

            nint monitor = MonitorFromWindow(owner.Hwnd, MONITOR_DEFAULTTONEAREST);
            var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
            GetMonitorInfoW(monitor, &info);
            int below = topLeft.y + anchor.Bounds.Height + gap;
            int roomBelow = info.rcWork.bottom - below;
            int roomAbove = topLeft.y - gap - info.rcWork.top;

            // As many rows as the list has, up to the cap, and then as many as the larger side of the
            // screen can hold - so the list is never cut off by the monitor's edge.
            float chrome = pad * 2 + border * 2;
            int fitBelow = (int)MathF.Floor((roomBelow - chrome) / rowH);
            int fitAbove = (int)MathF.Floor((roomAbove - chrome) / rowH);
            int wanted = Math.Min(items.Count, MaxRows);
            bool downward = fitBelow >= wanted || fitBelow >= fitAbove;
            visible = Math.Max(1, Math.Min(wanted, downward ? fitBelow : fitAbove));
            int height = (int)MathF.Ceiling(visible * rowH + chrome);
            int y = downward ? below : topLeft.y - gap - height;

            // The current choice opens in view, a little above the middle rather than at an edge.
            first = Math.Clamp(highlighted - visible / 3, 0, Math.Max(0, items.Count - visible));

            Create("BugtopiaList", "", WS_POPUP, WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST,
                   topLeft.x, y, width, height, owner.Hwnd, dropShadow: true);
            int corner = 3;   // DWMWCP_ROUNDSMALL, where Windows 11 offers it
            DwmSetWindowAttribute(Hwnd, 33, &corner, sizeof(int));
            uint edge = ColorRef(0x232a3a);
            DwmSetWindowAttribute(Hwnd, 34, &edge, sizeof(uint));
            ShowWindow(Hwnd, SW_SHOWNOACTIVATE);
            UpdateWindow(Hwnd);

            // Closed when the launcher loses the foreground - not merely when it does not have it, which
            // would shut a list opened in a window that was never brought forward the moment it appeared.
            bool wasForeground = GetForegroundWindow() == owner.Hwnd;

            int chosen = -1;
            MSG msg;
            while (!closed && GetMessageW(&msg, 0, 0, 0) > 0)
            {
                if (msg.message == WM_KEYDOWN || msg.message == WM_SYSKEYDOWN)
                {
                    switch ((int)msg.wParam)
                    {
                        case VK_UP: Move(highlighted - 1); continue;
                        case VK_DOWN: Move(highlighted + 1); continue;
                        case VK_PRIOR: Move(Math.Max(0, highlighted - (visible - 1))); continue;
                        case VK_NEXT: Move(Math.Min(items.Count - 1, highlighted + (visible - 1))); continue;
                        case VK_HOME: Move(0); continue;
                        case VK_END: Move(items.Count - 1); continue;
                        case VK_RETURN:
                        case VK_SPACE:
                            chosen = highlighted;
                            closed = true;
                            continue;
                        case VK_ESCAPE:
                        case VK_TAB:
                            closed = true;
                            continue;
                    }
                    continue;   // no other key reaches the window behind while the list is open
                }

                // The wheel goes to whichever window Windows picks - the focused one, or the one under
                // the pointer - and none of them should scroll while the list is open: the list does.
                if (msg.message == WM_MOUSEWHEEL)
                {
                    int notches = HiWord(msg.wParam) / 120;
                    ScrollTo(first - (notches == 0 ? Math.Sign(HiWord(msg.wParam)) : notches) * 3);
                    HoverUnderPointer();
                    continue;
                }

                bool press = msg.message == WM_LBUTTONDOWN || msg.message == WM_RBUTTONDOWN ||
                             msg.message == WM_MBUTTONDOWN || msg.message == WM_LBUTTONDBLCLK ||
                             msg.message == WM_NCLBUTTONDOWN || msg.message == WM_NCRBUTTONDOWN;
                if (msg.hwnd == Hwnd)
                {
                    int mx = LoWord(msg.lParam), my = HiWord(msg.lParam);
                    if (msg.message == WM_MOUSEMOVE)
                    {
                        if (dragging)
                            DragThumb(my);
                        else if (!InBar(mx))
                            Move(RowAt(my), scroll: false);
                        continue;
                    }
                    if (msg.message == WM_LBUTTONDOWN || msg.message == WM_LBUTTONDBLCLK)
                    {
                        if (Scrolls && InBar(mx))
                            PressBar(my);
                        continue;
                    }
                    if (msg.message == WM_LBUTTONUP)
                    {
                        if (dragging)
                        {
                            dragging = false;
                            ReleaseCapture();
                            continue;
                        }
                        int row = InBar(mx) ? -1 : RowAt(my);
                        if (row >= 0)
                        {
                            chosen = row;
                            closed = true;
                        }
                        continue;
                    }
                    if (press)
                        continue;
                }
                else if (press)
                {
                    // A click elsewhere closes the list and is spent doing so, as a menu's is.
                    closed = true;
                    continue;
                }

                TranslateMessage(&msg);
                DispatchMessageW(&msg);

                if (IsWindow(owner.Hwnd) == 0)
                    closed = true;
                else if (GetForegroundWindow() == owner.Hwnd)
                    wasForeground = true;
                else if (wasForeground)
                    closed = true;
            }

            if (dragging)
                ReleaseCapture();
            if (IsWindow(Hwnd) != 0)
                DestroyWindow(Hwnd);
            return chosen;
        }

        // ---- geometry --------------------------------------------------------

        private float BarWidth => S(6);

        /// <summary>The scrollbar's track: a thin strip inside the right padding of the list.</summary>
        private (float X, float Top, float Height) Track()
        {
            RECT rc;
            GetClientRect(Hwnd, &rc);
            float top = border + pad;
            return (rc.right - border - pad / 2 - BarWidth, top, visible * rowH);
        }

        private (float Top, float Height) Thumb()
        {
            var track = Track();
            float height = MathF.Max(S(24), track.Height * visible / items.Count);
            int range = items.Count - visible;
            float top = track.Top + (range <= 0 ? 0 : (track.Height - height) * first / range);
            return (top, height);
        }

        private bool InBar(int clientX)
        {
            if (!Scrolls)
                return false;
            var track = Track();
            return clientX >= track.X - S(4);   // a little wider than it looks, so it is easy to hit
        }

        private int RowAt(int clientY)
        {
            int row = first + (int)MathF.Floor((clientY - border - pad) / rowH);
            return row >= first && row < first + visible && row < items.Count ? row : -1;
        }

        // ---- moving and scrolling -----------------------------------------------

        /// <summary>Highlights a row; from the keyboard, also scrolls it into view.</summary>
        private void Move(int row, bool scroll = true)
        {
            if (row < 0 || row >= items.Count)
                return;
            bool changed = row != highlighted;
            highlighted = row;
            if (scroll)
            {
                if (row < first)
                    first = row;
                else if (row >= first + visible)
                    first = row - visible + 1;
            }
            if (changed || scroll)
                Invalidate();
        }

        private void ScrollTo(int top)
        {
            int clamped = Math.Clamp(top, 0, Math.Max(0, items.Count - visible));
            if (clamped == first)
                return;
            first = clamped;
            Invalidate();
        }

        /// <summary>After a scroll the row under a resting pointer has changed; the highlight follows it.</summary>
        private void HoverUnderPointer()
        {
            POINT pt;
            GetCursorPos(&pt);
            ScreenToClient(Hwnd, &pt);
            RECT rc;
            GetClientRect(Hwnd, &rc);
            if (pt.x >= 0 && pt.x < rc.right && pt.y >= 0 && pt.y < rc.bottom && !InBar(pt.x))
                Move(RowAt(pt.y), scroll: false);
        }

        private void PressBar(int clientY)
        {
            var thumb = Thumb();
            if (clientY >= thumb.Top && clientY < thumb.Top + thumb.Height)
            {
                dragging = true;
                dragOffset = clientY - thumb.Top;
                SetCapture(Hwnd);
            }
            else
            {
                // On the track: a page towards the click, as a scrollbar does.
                ScrollTo(first + (clientY < thumb.Top ? -(visible - 1) : visible - 1));
            }
        }

        private void DragThumb(int clientY)
        {
            var track = Track();
            var thumb = Thumb();
            float travel = track.Height - thumb.Height;
            if (travel <= 0)
                return;
            float fraction = Math.Clamp((clientY - dragOffset - track.Top) / travel, 0f, 1f);
            ScrollTo((int)MathF.Round(fraction * (items.Count - visible)));
        }

        // ---- window ----------------------------------------------------------

        protected override void Report(Exception ex) => owner.ReportError(ex);

        protected override nint Handle(uint msg, nint w, nint l)
        {
            if (msg == WM_MOUSEACTIVATE)
                return 3;   // MA_NOACTIVATE: the launcher keeps the foreground and the keyboard
            return base.Handle(msg, w, l);
        }

        protected override void Render(nint dc, int width, int height)
        {
            nint g = Gdip.Begin(dc);
            Gdip.FillRect(g, Gdip.Argb(Bg), 0, 0, width, height);
            Gdip.FillRect(g, Gdip.Argb(CardBorder, 0.08f), 0, 0, width, border);
            Gdip.FillRect(g, Gdip.Argb(CardBorder, 0.08f), 0, height - border, width, border);
            Gdip.FillRect(g, Gdip.Argb(CardBorder, 0.08f), 0, 0, border, height);
            Gdip.FillRect(g, Gdip.Argb(CardBorder, 0.08f), width - border, 0, border, height);

            // Rows stop short of the scrollbar when there is one.
            float rowX = border + pad;
            float rowW = width - (border + pad) * 2 - (Scrolls ? BarWidth + S(4) : 0);
            int last = Math.Min(items.Count, first + visible);
            for (int i = first; i < last; i++)
            {
                float top = border + pad + (i - first) * rowH;
                if (i == highlighted)
                    Gdip.FillRoundRect(g, Gdip.Argb(Accent, 0.28f), rowX, top, rowW, rowH, S(6));
                if (i == current)
                    Gdip.StrokeIcon(g, CheckMark, rowX + rowW - S(10) - S(14), top + (rowH - S(14)) / 2, S(14), Gdip.Argb(0xa5b4fc), 2.5f);
            }

            if (Scrolls)
            {
                var track = Track();
                var thumb = Thumb();
                Gdip.FillRoundRect(g, Gdip.Argb(0xffffff, 0.05f), track.X, track.Top, BarWidth, track.Height, BarWidth / 2);
                Gdip.FillRoundRect(g, Gdip.Argb(0x94a3b8, dragging ? 0.7f : 0.45f), track.X, thumb.Top, BarWidth, thumb.Height, BarWidth / 2);
            }
            Gdip.End(g);

            for (int i = first; i < last; i++)
            {
                float top = border + pad + (i - first) * rowH;
                float textX = rowX + S(10);
                float room = rowW - S(10) - S(14) - S(20);
                DrawLabel(dc, Fonts.Input, EllipsisFit(Fonts.Input, items[i], room), textX, top, room, rowH,
                          i == current ? TextMain : 0xcbd5e1, false);
            }
        }

        protected override void Destroyed()
        {
            base.Destroyed();
            Fonts?.Dispose();
            Fonts = null;
            closed = true;
        }
    }
}
