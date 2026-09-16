using System;
using static Bugtopia.Launcher.Win32.Native;

namespace Bugtopia.Launcher.Win32
{
    /// <summary>
    /// The page's &lt;dialog class="modal"&gt;: a title, the question, an optional text field, and
    /// Cancel before Continue - Cancel first, so it is what Enter and the initial focus land on.
    ///
    /// The question sits in a read-only edit box rather than being drawn: the questions list paths and
    /// files, and those have to be selectable and copyable, as they were on the page.
    /// </summary>
    internal sealed unsafe class ConfirmWindow : Surface
    {
        private const int IdNo = 201, IdYes = 202;

        private readonly Win32Host host;
        private readonly string ticket, title, text, confirmLabel, placeholder, initial;
        private nint textBox, input, brush;
        private Button no, yes;
        private bool answered;
        private float inputX, inputY, inputW, inputH, titleH;

        internal ConfirmWindow(Win32Host host, string ticket, string title, string text, string confirmLabel,
                               string placeholder, string initial)
        {
            this.host = host;
            this.ticket = ticket;
            this.title = title;
            this.text = text;
            this.confirmLabel = confirmLabel;
            this.placeholder = placeholder;
            this.initial = initial;
        }

        private bool WantsText => placeholder != null;

        internal void Open()
        {
            Scale = host.Scale;
            Fonts = new Fonts(Scale);
            brush = CreateSolidBrush(ColorRef(Bg));

            RECT owner;
            GetWindowRect(host.Hwnd, &owner);
            RECT ownerClient;
            GetClientRect(host.Hwnd, &ownerClient);

            float border = MathF.Max(1, MathF.Round(S(1)));
            float width = MathF.Max(S(320), MathF.Min(S(560), ownerClient.Width - S(64)));
            float padX = S(26), innerW = width - padX * 2 - border * 2;

            Create("BugtopiaConfirm", title, WS_POPUP | WS_CLIPCHILDREN, WS_EX_CONTROLPARENT, 0, 0, (int)width, 100,
                   host.Hwnd, dropShadow: true);
            int corner = 2;   // DWMWCP_ROUND, where Windows 11 offers it
            DwmSetWindowAttribute(Hwnd, 33, &corner, sizeof(int));
            uint edge = ColorRef(0x232a3a);   // the page's rgba(255,255,255,0.08) border over #0f172a
            DwmSetWindowAttribute(Hwnd, 34, &edge, sizeof(uint));

            fixed (char* cls = "EDIT")
            fixed (char* body = (text ?? "").Replace("\r\n", "\n").Replace("\n", "\r\n"))
                textBox = CreateWindowExW(0, cls, body, WS_CHILD | WS_VISIBLE | ES_MULTILINE | ES_READONLY | ES_AUTOVSCROLL,
                                          0, 0, (int)innerW, 10, Hwnd, 0, GetModuleHandleW(null), 0);
            SendMessageW(textBox, WM_SETFONT, Fonts.ModalText.Handle, 1);
            SendMessageW(textBox, EM_SETMARGINS, 3, 0);
            DarkControl(textBox);

            // Tall enough for every line, up to 46% of the window behind (the page's max-height: 46vh).
            int lines = Math.Max(1, (int)SendMessageW(textBox, EM_GETLINECOUNT, 0, 0));
            float textH = lines * Fonts.ModalText.Height;
            float maxText = ownerClient.Height * 0.46f;
            if (textH > maxText)
            {
                textH = maxText;
                ShowScrollBar(textBox, SB_VERT, 1);
            }

            no = AddButton(IdNo, ButtonKind.Secondary, "Cancel");
            yes = AddButton(IdYes, ButtonKind.Primary, confirmLabel);

            float y = border + S(24);
            titleH = Fonts.ModalTitle.LineHeight;
            y += titleH + S(14);
            SetWindowPos(textBox, 0, (int)(border + padX), (int)y, (int)innerW, (int)MathF.Ceiling(textH), SWP_NOZORDER | SWP_NOACTIVATE);
            y += textH + S(14);

            if (WantsText)
            {
                inputX = border + padX;
                inputY = y;
                inputW = innerW;
                inputH = border * 2 + S(10) * 2 + Fonts.Input.LineHeight;
                fixed (char* cls = "EDIT")
                fixed (char* value = initial ?? "")
                    input = CreateWindowExW(0, cls, value, WS_CHILD | WS_VISIBLE | WS_TABSTOP | ES_AUTOHSCROLL,
                                            (int)(inputX + border + S(14)), (int)(inputY + border + S(10)),
                                            (int)(inputW - (border + S(14)) * 2), (int)Fonts.Input.LineHeight,
                                            Hwnd, 0, GetModuleHandleW(null), 0);
                SendMessageW(input, WM_SETFONT, Fonts.Input.Handle, 1);
                fixed (char* cue = placeholder)
                    SendMessageW(input, EM_SETCUEBANNER, 1, (nint)cue);
                y += inputH + S(14);
            }

            var noSize = Measure(no);
            var yesSize = Measure(yes);
            float rowH = MathF.Max(noSize.Height, yesSize.Height);
            float right = width - border - padX;
            Place(yes, right - yesSize.Width, y + (rowH - yesSize.Height) / 2, yesSize.Width, yesSize.Height, true);
            Place(no, right - yesSize.Width - S(10) - noSize.Width, y + (rowH - noSize.Height) / 2, noSize.Width, noSize.Height, true);
            y += rowH + S(20) + border;

            // Centred over the launcher, which is what it is a question about.
            int h = (int)MathF.Ceiling(y);
            int x = owner.left + (owner.Width - (int)width) / 2;
            int top = owner.top + (owner.Height - h) / 2;
            SetWindowPos(Hwnd, 0, x, top, (int)width, h, SWP_NOZORDER);
            ShowWindow(Hwnd, SW_SHOW);
            SetForegroundWindow(Hwnd);

            if (WantsText)
            {
                SetFocus(input);
                SendMessageW(input, EM_SETSEL, 0, -1);
            }
            else
            {
                SetFocus(no.Hwnd);
            }
        }

        protected override void Report(Exception ex) => host.ReportFromDialog(ex);

        protected override void Render(nint dc, int width, int height)
        {
            float border = MathF.Max(1, MathF.Round(S(1)));
            nint g = Gdip.Begin(dc);
            Gdip.FillRect(g, Gdip.Argb(Bg), 0, 0, width, height);
            // A square edge of our own for Windows 10, which neither rounds nor colours the frame.
            Gdip.FillRect(g, Gdip.Argb(CardBorder, 0.08f), 0, 0, width, border);
            Gdip.FillRect(g, Gdip.Argb(CardBorder, 0.08f), 0, height - border, width, border);
            Gdip.FillRect(g, Gdip.Argb(CardBorder, 0.08f), 0, 0, border, height);
            Gdip.FillRect(g, Gdip.Argb(CardBorder, 0.08f), width - border, 0, border, height);

            if (WantsText)
            {
                bool focused = GetFocus() == input;
                Gdip.FillRoundRect(g, Gdip.Argb(Bg, 0.6f), inputX, inputY, inputW, inputH, S(10));
                Gdip.StrokeRoundRect(g, focused ? Gdip.Argb(Accent) : Gdip.Argb(CardBorder, 0.08f),
                                     inputX, inputY, inputW, inputH, S(10), border);
            }

            RECT r = yes.Bounds;
            Gdip.Shadow(g, r.left, r.top + S(4), r.Width, r.Height, S(10), S(14), Accent, 0.4f);
            Gdip.End(g);

            DrawLabel(dc, Fonts.ModalTitle, title, border + S(26), border + S(24), width, titleH, TextMain, false);
        }

        protected override nint Handle(uint msg, nint w, nint l)
        {
            switch (msg)
            {
                case WM_CTLCOLORSTATIC:   // the read-only question
                    SetTextColor(w, ColorRef(TextMuted));
                    SetBkColor(w, ColorRef(Bg));
                    return brush;

                case WM_CTLCOLOREDIT:     // the text field
                    SetTextColor(w, ColorRef(TextMain));
                    SetBkColor(w, ColorRef(Bg));
                    return brush;

                case WM_COMMAND:
                {
                    int id = LoWord(w), code = HiWord(w);
                    if (l != 0 && code == (int)BN_CLICKED)
                    {
                        if (id == IdYes) Finish(true);
                        else if (id == IdNo) Finish(false);
                        return 0;
                    }
                    if (l != 0 && l == input && (code == 0x0100 || code == 0x0200))   // EN_SETFOCUS, EN_KILLFOCUS
                    {
                        Invalidate();   // the field's border follows its focus
                        return 0;
                    }
                    if (l == 0 && id == IDCANCEL)
                    {
                        Finish(false);
                        return 0;
                    }
                    if (l == 0 && id == IDOK)
                    {
                        // Enter presses the focused button; anywhere else it is the form's first button,
                        // Cancel - as it was on the page.
                        Button focused = ButtonFor(GetFocus());
                        Finish(focused == yes);
                        return 0;
                    }
                    break;
                }

                case WM_CLOSE:
                    Finish(false);
                    return 0;
            }
            return base.Handle(msg, w, l);
        }

        /// <summary>Answers exactly once, however the dialog was dismissed.</summary>
        private void Finish(bool ok)
        {
            if (answered)
                return;
            answered = true;
            string value = ok && WantsText ? WindowText(input).Trim() : "";

            // The owner is enabled before this window goes, so activation returns to it rather than to
            // whatever window happens to be next in the z-order.
            host.ConfirmClosed(this, ticket, ok, value);
            DestroyWindow(Hwnd);
        }

        protected override void Destroyed()
        {
            base.Destroyed();
            Fonts?.Dispose();
            if (brush != 0)
                DeleteObject(brush);
            brush = 0;
        }
    }
}
