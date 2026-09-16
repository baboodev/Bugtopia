using System;
using System.Collections.Generic;
using static Bugtopia.Launcher.Win32.Native;

namespace Bugtopia.Launcher.Win32
{
    /// <summary>
    /// A select's list: a borderless popup as wide as the select, directly under it (above it when the
    /// screen ends first), drawn in the launcher's own colours.
    ///
    /// It never takes activation - the launcher stays the active window, so the keys arrive where they
    /// always do and are read here before anything else sees them, the way a menu reads them. It closes on
    /// a choice, Escape, Tab, a click anywhere else, or the launcher losing the foreground.
    /// </summary>
    internal sealed unsafe class ListPopup : Surface
    {
        private readonly Surface owner;
        private readonly IReadOnlyList<string> items;
        private readonly int current;
        private int highlighted;
        private float rowH, pad;
        private bool closed;

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

        private int Run(Button anchor)
        {
            Scale = owner.Scale;
            Fonts = new Fonts(Scale);
            rowH = MathF.Round(S(8) * 2 + Fonts.Input.LineHeight);
            pad = MathF.Round(S(4));
            float border = MathF.Max(1, MathF.Round(S(1)));

            var topLeft = new POINT { x = anchor.Bounds.left, y = anchor.Bounds.top };
            ClientToScreen(owner.Hwnd, &topLeft);
            int width = anchor.Bounds.Width;
            int height = (int)MathF.Ceiling(items.Count * rowH + pad * 2 + border * 2);
            int gap = (int)MathF.Round(S(4));

            nint monitor = MonitorFromWindow(owner.Hwnd, MONITOR_DEFAULTTONEAREST);
            var info = new MONITORINFO { cbSize = (uint)sizeof(MONITORINFO) };
            GetMonitorInfoW(monitor, &info);
            int below = topLeft.y + anchor.Bounds.Height + gap;
            int y = below + height <= info.rcWork.bottom ? below : Math.Max(info.rcWork.top, topLeft.y - gap - height);

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
                        case VK_HOME:
                        case VK_PRIOR: Move(0); continue;
                        case VK_END:
                        case VK_NEXT: Move(items.Count - 1); continue;
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

                bool press = msg.message == WM_LBUTTONDOWN || msg.message == WM_RBUTTONDOWN ||
                             msg.message == WM_MBUTTONDOWN || msg.message == WM_LBUTTONDBLCLK ||
                             msg.message == WM_NCLBUTTONDOWN || msg.message == WM_NCRBUTTONDOWN;
                if (msg.hwnd == Hwnd)
                {
                    if (msg.message == WM_MOUSEMOVE)
                    {
                        Move(RowAt(HiWord(msg.lParam)));
                        continue;
                    }
                    if (msg.message == WM_LBUTTONUP)
                    {
                        int row = RowAt(HiWord(msg.lParam));
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

            if (IsWindow(Hwnd) != 0)
                DestroyWindow(Hwnd);
            return chosen;
        }

        private int RowAt(int clientY)
        {
            float border = MathF.Max(1, MathF.Round(S(1)));
            int row = (int)MathF.Floor((clientY - border - pad) / rowH);
            return row >= 0 && row < items.Count ? row : -1;
        }

        private void Move(int row)
        {
            if (row < 0 || row >= items.Count || row == highlighted)
                return;
            highlighted = row;
            Invalidate();
        }

        protected override void Report(Exception ex) => owner.ReportError(ex);

        protected override nint Handle(uint msg, nint w, nint l)
        {
            if (msg == WM_MOUSEACTIVATE)
                return 3;   // MA_NOACTIVATE: the launcher keeps the foreground and the keyboard
            return base.Handle(msg, w, l);
        }

        protected override void Render(nint dc, int width, int height)
        {
            float border = MathF.Max(1, MathF.Round(S(1)));
            nint g = Gdip.Begin(dc);
            Gdip.FillRect(g, Gdip.Argb(Bg), 0, 0, width, height);
            Gdip.FillRect(g, Gdip.Argb(CardBorder, 0.08f), 0, 0, width, border);
            Gdip.FillRect(g, Gdip.Argb(CardBorder, 0.08f), 0, height - border, width, border);
            Gdip.FillRect(g, Gdip.Argb(CardBorder, 0.08f), 0, 0, border, height);
            Gdip.FillRect(g, Gdip.Argb(CardBorder, 0.08f), width - border, 0, border, height);

            float rowX = border + pad, rowW = width - (border + pad) * 2;
            for (int i = 0; i < items.Count; i++)
            {
                float top = border + pad + i * rowH;
                if (i == highlighted)
                    Gdip.FillRoundRect(g, Gdip.Argb(Accent, 0.28f), rowX, top, rowW, rowH, S(6));
                if (i == current)
                    Gdip.StrokeIcon(g, CheckMark, rowX + rowW - S(10) - S(14), top + (rowH - S(14)) / 2, S(14), Gdip.Argb(0xa5b4fc), 2.5f);
            }
            Gdip.End(g);

            for (int i = 0; i < items.Count; i++)
            {
                float top = border + pad + i * rowH;
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
