using System;
using System.Collections.Generic;

namespace Bugtopia.Launcher.Win32
{
    /// <summary>One GDI font at one size, with the line metrics layout needs.</summary>
    internal sealed class Font
    {
        internal nint Handle;

        /// <summary>The CSS font-size in device pixels.</summary>
        internal float Em;

        /// <summary>Glyph box height (tmHeight): what a line of this font occupies with no leading.</summary>
        internal float Height;

        /// <summary>CSS "line-height: normal" - ascent, descent and the font's own external leading.</summary>
        internal float LineHeight;
    }

    /// <summary>
    /// Every font the window uses, at one DPI. The sizes are the page's rem values over a 16px root,
    /// so the two builds set type at the same sizes.
    /// </summary>
    internal sealed unsafe class Fonts : IDisposable
    {
        private static nint measureDc;
        private static string textFace, displayFace, monoFace;

        internal readonly Font Title, Subtitle, SubtitleLink, Check, StepTitle, Detail, DetailLink,
                               Button, Play, Banner, Hint, Kbd, ModalTitle, ModalText, Input,
                               Label, InputHint, StatusLabel, StatusValue, Log;

        private readonly List<Font> all = new List<Font>();

        internal Fonts(float scale)
        {
            ResolveFaces();
            Title = Make(displayFace, 30.4f, 700, false, scale);        // .brand h1: 1.9rem, 700
            Subtitle = Make(textFace, 13.12f, 400, false, scale);       // .subtitle: 0.82rem
            SubtitleLink = Make(textFace, 13.12f, 400, true, scale);
            Check = Make(textFace, 13.6f, 400, false, scale);           // .checkbox-label: 0.85rem
            StepTitle = Make(textFace, 14.4f, 600, false, scale);       // .step-title: 0.9rem, 600
            Detail = Make(textFace, 12.48f, 400, false, scale);         // .step-detail: 0.78rem
            DetailLink = Make(textFace, 12.48f, 400, true, scale);
            Button = Make(textFace, 14.4f, 600, false, scale);          // .btn: 0.9rem, 600
            Play = Make(textFace, 16f, 600, false, scale);              // simple #play: 1rem
            Banner = Make(textFace, 13.6f, 400, false, scale);          // .message-banner: 0.85rem
            Hint = Make(textFace, 12f, 400, false, scale);              // .launch-hint: 0.75rem
            Kbd = Make(textFace, 11.52f, 600, false, scale);            // .launch-hint kbd: 0.72rem, 600
            ModalTitle = Make(textFace, 16.8f, 700, false, scale);      // .modal-title: 1.05rem, 700
            ModalText = Make(textFace, 13.76f, 400, false, scale);      // .modal-text: 0.86rem
            Input = Make(textFace, 14.08f, 400, false, scale);          // input[type=text]: 0.88rem
            Label = Make(textFace, 13.12f, 500, false, scale);          // .input-label: 0.82rem, 500
            InputHint = Make(textFace, 11.52f, 400, false, scale);      // .input-hint: 0.72rem
            StatusLabel = Make(textFace, 11.52f, 400, false, scale);    // .status-label: 0.72rem
            StatusValue = Make(textFace, 14.72f, 600, false, scale);    // .status-value: 0.92rem, 600
            Log = Make(monoFace, 11.84f, 400, false, scale);            // #log: 0.74rem, Cascadia Mono / Consolas
        }

        public void Dispose()
        {
            foreach (Font f in all)
                Native.DeleteObject(f.Handle);
            all.Clear();
        }

        private static nint MeasureDc
        {
            get
            {
                if (measureDc == 0)
                    measureDc = Native.CreateCompatibleDC(0);
                return measureDc;
            }
        }

        /// <summary>
        /// The page asked for Segoe UI Variable and fell back to Segoe UI; so does this. GDI substitutes
        /// silently for a face it does not have, so whether it took is read back rather than assumed.
        /// </summary>
        private static void ResolveFaces()
        {
            if (textFace != null)
                return;
            textFace = Installed("Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";
            displayFace = Installed("Segoe UI Variable Display") ? "Segoe UI Variable Display" : "Segoe UI";
            monoFace = Installed("Cascadia Mono") ? "Cascadia Mono" : "Consolas";
        }

        private static bool Installed(string face)
        {
            nint font;
            fixed (char* f = face)
                font = Native.CreateFontW(-16, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, f);
            nint old = Native.SelectObject(MeasureDc, font);
            char* actual = stackalloc char[64];
            int length = Native.GetTextFaceW(MeasureDc, 64, actual);
            Native.SelectObject(MeasureDc, old);
            Native.DeleteObject(font);
            return length > 0 && string.Equals(new string(actual, 0, Math.Max(0, length - 1)), face, StringComparison.OrdinalIgnoreCase)
                || length > 0 && string.Equals(new string(actual, 0, length), face, StringComparison.OrdinalIgnoreCase);
        }

        private Font Make(string face, float cssPx, int weight, bool underline, float scale)
        {
            float em = cssPx * scale;
            nint handle;
            fixed (char* f = face)
                handle = Native.CreateFontW(-(int)MathF.Round(em), 0, 0, 0, weight, 0, underline ? 1u : 0u, 0,
                                            1 /* DEFAULT_CHARSET */, 0, 0, 5 /* CLEARTYPE_QUALITY */, 0, f);

            nint old = Native.SelectObject(MeasureDc, handle);
            Native.TEXTMETRICW tm;
            Native.GetTextMetricsW(MeasureDc, &tm);
            Native.SelectObject(MeasureDc, old);

            var font = new Font
            {
                Handle = handle,
                Em = em,
                Height = tm.tmHeight,
                LineHeight = tm.tmHeight + tm.tmExternalLeading,
            };
            all.Add(font);
            return font;
        }

        internal static float Measure(Font font, string text)
        {
            if (string.IsNullOrEmpty(text))
                return 0;
            nint old = Native.SelectObject(MeasureDc, font.Handle);
            Native.SIZE size;
            fixed (char* p = text)
                Native.GetTextExtentPoint32W(MeasureDc, p, text.Length, &size);
            Native.SelectObject(MeasureDc, old);
            return size.cx;
        }

        /// <summary>How many leading characters of <paramref name="text"/> fit in <paramref name="width"/>.</summary>
        internal static int Fit(Font font, string text, float width)
        {
            if (string.IsNullOrEmpty(text) || width <= 0)
                return 0;
            nint old = Native.SelectObject(MeasureDc, font.Handle);
            int fit;
            Native.SIZE size;
            fixed (char* p = text)
                Native.GetTextExtentExPointW(MeasureDc, p, text.Length, (int)width, &fit, null, &size);
            Native.SelectObject(MeasureDc, old);
            return fit;
        }
    }

    /// <summary>A stretch of text, and the address it opens when it is a link.</summary>
    internal readonly struct TextRun
    {
        internal readonly string Text;
        internal readonly string Url;

        internal TextRun(string text, string url = null)
        {
            Text = text ?? "";
            Url = url;
        }
    }

    /// <summary>Wrapped text: pieces placed on lines, ready to draw and to hit-test for links.</summary>
    internal sealed class TextBlock
    {
        internal sealed class Piece
        {
            internal float X, Width;
            internal int Line;
            internal string Text;
            internal string Url;
            internal Font Font;
        }

        internal readonly List<Piece> Pieces = new List<Piece>();
        internal int Lines;
        internal float LineHeight;
        internal float Width;

        internal float Height => Lines * LineHeight;

        /// <summary>
        /// Lays runs out in lines no wider than <paramref name="maxWidth"/>. Breaks at spaces, and inside
        /// a word only when the word cannot fit on a line of its own - CSS overflow-wrap: anywhere, which
        /// is what keeps a long path from running off the card.
        /// </summary>
        internal static TextBlock Layout(IReadOnlyList<TextRun> runs, Font font, Font linkFont, float maxWidth, float lineHeight)
        {
            var block = new TextBlock { LineHeight = lineHeight };
            float x = 0;
            int line = 0;
            bool any = false;
            maxWidth = MathF.Max(1, maxWidth);

            void Add(string text, float width, TextRun run, Font f)
            {
                Piece last = block.Pieces.Count > 0 ? block.Pieces[^1] : null;
                if (last != null && last.Line == line && ReferenceEquals(last.Font, f) && last.Url == run.Url)
                {
                    last.Text += text;
                    last.Width += width;
                }
                else
                {
                    block.Pieces.Add(new Piece { X = x, Width = width, Line = line, Text = text, Url = run.Url, Font = f });
                }
                x += width;
                block.Width = MathF.Max(block.Width, x);
                any = true;
            }

            void NewLine()
            {
                line++;
                x = 0;
            }

            foreach (TextRun run in runs)
            {
                Font f = run.Url != null ? linkFont : font;
                foreach (string token in Tokens(run.Text))
                {
                    float w = Fonts.Measure(f, token);
                    if (token[0] == ' ')
                    {
                        if (x == 0)
                            continue;              // no space at the start of a line
                        if (x + w > maxWidth)
                        {
                            NewLine();
                            continue;
                        }
                        Add(token, w, run, f);
                        continue;
                    }

                    if (x + w <= maxWidth)
                    {
                        Add(token, w, run, f);
                        continue;
                    }

                    if (x > 0)
                        NewLine();
                    if (w <= maxWidth)
                    {
                        Add(token, w, run, f);
                        continue;
                    }

                    string rest = token;
                    while (rest.Length > 0)
                    {
                        int fit = Fonts.Fit(f, rest, maxWidth - x);
                        if (fit <= 0)
                        {
                            if (x > 0)
                            {
                                NewLine();
                                continue;
                            }
                            fit = 1;
                        }
                        string part = rest.Substring(0, fit);
                        Add(part, Fonts.Measure(f, part), run, f);
                        rest = rest.Substring(fit);
                        if (rest.Length > 0)
                            NewLine();
                    }
                }
            }

            block.Lines = any ? line + 1 : 0;
            return block;
        }

        private static IEnumerable<string> Tokens(string text)
        {
            int i = 0;
            while (i < text.Length)
            {
                int start = i;
                bool space = text[i] == ' ';
                while (i < text.Length && (text[i] == ' ') == space)
                    i++;
                yield return text.Substring(start, i - start);
            }
        }

        internal unsafe void Draw(nint dc, float x, float y, uint color, uint linkColor)
        {
            foreach (Piece piece in Pieces)
            {
                Native.SelectObject(dc, piece.Font.Handle);
                Native.SetTextColor(dc, piece.Url != null ? linkColor : color);
                float top = y + piece.Line * LineHeight + (LineHeight - piece.Font.Height) / 2;
                fixed (char* p = piece.Text)
                    Native.ExtTextOutW(dc, (int)MathF.Round(x + piece.X), (int)MathF.Round(top), 0, null, p, (uint)piece.Text.Length, null);
            }
        }

        /// <summary>The link under a point relative to where the block was drawn, or null.</summary>
        internal string LinkAt(float px, float py)
        {
            foreach (Piece piece in Pieces)
            {
                if (piece.Url == null)
                    continue;
                float top = piece.Line * LineHeight;
                if (px >= piece.X && px < piece.X + piece.Width && py >= top && py < top + LineHeight)
                    return piece.Url;
            }
            return null;
        }
    }
}
