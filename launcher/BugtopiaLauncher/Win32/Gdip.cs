using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Bugtopia.Launcher.Win32
{
    /// <summary>
    /// GDI+ through its flat C API, for what GDI cannot do: antialiased rounded shapes, translucent
    /// fills and gradients, and the logo. Text stays with GDI, whose ClearType is the sharper of the two.
    /// </summary>
    internal static unsafe class Gdip
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct StartupInput
        {
            public uint GdiplusVersion;
            public nint DebugEventCallback;
            public int SuppressBackgroundThread, SuppressExternalCodecs;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct RectF { public float X, Y, Width, Height; }

        [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern int GdiplusStartup(nuint* token, StartupInput* input, nint output);
        [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern int GdipCreateFromHDC(nint dc, nint* graphics);
        [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipDeleteGraphics(nint graphics);
        [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern int GdipSetSmoothingMode(nint graphics, int mode);
        [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern int GdipSetPixelOffsetMode(nint graphics, int mode);
        [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern int GdipSetInterpolationMode(nint graphics, int mode);
        [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern int GdipCreatePath(int fillMode, nint* path);
        [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipDeletePath(nint path);
        [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern int GdipAddPathArc(nint path, float x, float y, float w, float h, float start, float sweep);
        [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern int GdipAddPathLine(nint path, float x1, float y1, float x2, float y2);
        [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern int GdipAddPathBezier(nint path, float x1, float y1, float x2, float y2, float x3, float y3, float x4, float y4);
        [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern int GdipAddPathEllipse(nint path, float x, float y, float w, float h);
        [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern int GdipStartPathFigure(nint path);
        [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern int GdipClosePathFigure(nint path);
        [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern int GdipCreateSolidFill(uint argb, nint* brush);
        [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipDeleteBrush(nint brush);
        [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern int GdipFillPath(nint graphics, nint brush, nint path);
        [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern int GdipFillRectangle(nint graphics, nint brush, float x, float y, float w, float h);
        [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern int GdipFillEllipse(nint graphics, nint brush, float x, float y, float w, float h);
        [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern int GdipCreatePen1(uint argb, float width, int unit, nint* pen);
        [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern int GdipDeletePen(nint pen);
        [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern int GdipSetPenStartCap(nint pen, int cap);
        [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern int GdipSetPenEndCap(nint pen, int cap);
        [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern int GdipSetPenLineJoin(nint pen, int join);
        [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern int GdipDrawPath(nint graphics, nint pen, nint path);
        [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern int GdipCreateLineBrushFromRect(RectF* rect, uint color1, uint color2, int mode, int wrap, nint* brush);
        [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern int GdipSetLinePresetBlend(nint brush, uint* colors, float* positions, int count);
        [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern int GdipCreatePathGradientFromPath(nint path, nint* brush);
        [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern int GdipSetPathGradientCenterColor(nint brush, uint argb);
        [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern int GdipSetPathGradientSurroundColorsWithCount(nint brush, uint* argb, int* count);
        [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern int GdipSetClipPath(nint graphics, nint path, int combine);
        [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern int GdipResetClip(nint graphics);
        [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern int GdipCreateBitmapFromStream(nint stream, nint* bitmap);
        [DllImport("gdiplus.dll", ExactSpelling = true)] internal static extern int GdipDisposeImage(nint image);
        [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern int GdipGetImageWidth(nint image, uint* width);
        [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern int GdipGetImageHeight(nint image, uint* height);
        [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern int GdipDrawImageRect(nint graphics, nint image, float x, float y, float w, float h);
        [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern int GdipResetWorldTransform(nint graphics);
        [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern int GdipTranslateWorldTransform(nint graphics, float dx, float dy, int order);
        [DllImport("gdiplus.dll", ExactSpelling = true)] private static extern int GdipScaleWorldTransform(nint graphics, float sx, float sy, int order);

        private static bool started;

        internal static void Startup()
        {
            if (started)
                return;
            var input = new StartupInput { GdiplusVersion = 1 };
            nuint token;
            started = GdiplusStartup(&token, &input, 0) == 0;
        }

        /// <summary>ARGB from a CSS-style colour: 0xRRGGBB and an opacity.</summary>
        internal static uint Argb(uint rgb, float alpha = 1f) =>
            ((uint)Math.Clamp((int)MathF.Round(alpha * 255f), 0, 255) << 24) | (rgb & 0xFFFFFF);

        // ---- graphics --------------------------------------------------------

        internal static nint Begin(nint dc)
        {
            nint g;
            GdipCreateFromHDC(dc, &g);
            GdipSetSmoothingMode(g, 4);        // AntiAlias
            GdipSetPixelOffsetMode(g, 4);      // Half: pixel centres, so edges land where CSS puts them
            GdipSetInterpolationMode(g, 7);    // HighQualityBicubic
            return g;
        }

        internal static void End(nint graphics)
        {
            if (graphics != 0)
                GdipDeleteGraphics(graphics);
        }

        // ---- paths -----------------------------------------------------------

        internal static nint RoundRect(float x, float y, float w, float h, float r)
        {
            nint path;
            GdipCreatePath(0, &path);
            r = MathF.Max(0, MathF.Min(r, MathF.Min(w, h) / 2));
            if (r <= 0.01f)
            {
                GdipAddPathLine(path, x, y, x + w, y);
                GdipAddPathLine(path, x + w, y, x + w, y + h);
                GdipAddPathLine(path, x + w, y + h, x, y + h);
            }
            else
            {
                float d = r * 2;
                GdipAddPathArc(path, x, y, d, d, 180, 90);
                GdipAddPathArc(path, x + w - d, y, d, d, 270, 90);
                GdipAddPathArc(path, x + w - d, y + h - d, d, d, 0, 90);
                GdipAddPathArc(path, x, y + h - d, d, d, 90, 90);
            }
            GdipClosePathFigure(path);
            return path;
        }

        internal static void FillRoundRect(nint g, uint argb, float x, float y, float w, float h, float r)
        {
            nint path = RoundRect(x, y, w, h, r);
            nint brush;
            GdipCreateSolidFill(argb, &brush);
            GdipFillPath(g, brush, path);
            GdipDeleteBrush(brush);
            GdipDeletePath(path);
        }

        /// <summary>A 1px CSS border: stroked on the half-pixel inside the box, as a border-box draws it.</summary>
        internal static void StrokeRoundRect(nint g, uint argb, float x, float y, float w, float h, float r, float width)
        {
            float half = width / 2;
            nint path = RoundRect(x + half, y + half, w - width, h - width, r - half);
            nint pen;
            GdipCreatePen1(argb, width, 0, &pen);
            GdipDrawPath(g, pen, path);
            GdipDeletePen(pen);
            GdipDeletePath(path);
        }

        internal static void FillRect(nint g, uint argb, float x, float y, float w, float h)
        {
            nint brush;
            GdipCreateSolidFill(argb, &brush);
            GdipFillRectangle(g, brush, x, y, w, h);
            GdipDeleteBrush(brush);
        }

        internal static void FillEllipse(nint g, uint argb, float x, float y, float w, float h)
        {
            nint brush;
            GdipCreateSolidFill(argb, &brush);
            GdipFillEllipse(g, brush, x, y, w, h);
            GdipDeleteBrush(brush);
        }

        /// <summary>A linear gradient across a round rect, at CSS's 90deg (left to right) or 135deg.</summary>
        internal static void FillRoundRectGradient(nint g, float x, float y, float w, float h, float r,
                                                   uint from, uint to, bool diagonal)
        {
            nint path = RoundRect(x, y, w, h, r);
            // A pixel wider than the shape on each side: GDI+ wraps a line brush at its edges, and a
            // brush exactly the size of the fill shows the far colour as a seam along the near edge.
            var rect = new RectF { X = x - 1, Y = y - 1, Width = w + 2, Height = h + 2 };
            nint brush;
            GdipCreateLineBrushFromRect(&rect, from, to, diagonal ? 2 : 0, 0, &brush);
            GdipFillPath(g, brush, path);
            GdipDeleteBrush(brush);
            GdipDeletePath(path);
        }

        /// <summary>A horizontal multi-stop gradient, clipped to a round rect - the card sweep.</summary>
        internal static void FillSweep(nint g, float clipX, float clipY, float clipW, float clipH, float radius,
                                       float bandX, float bandW, ReadOnlySpan<uint> colors, ReadOnlySpan<float> positions)
        {
            nint clip = RoundRect(clipX, clipY, clipW, clipH, radius);
            GdipSetClipPath(g, clip, 0);
            var rect = new RectF { X = bandX, Y = clipY, Width = bandW, Height = clipH };
            nint brush;
            GdipCreateLineBrushFromRect(&rect, colors[0], colors[^1], 0, 0, &brush);
            fixed (uint* c = colors)
            fixed (float* p = positions)
                GdipSetLinePresetBlend(brush, c, p, colors.Length);
            GdipFillRectangle(g, brush, bandX + 0.5f, clipY, bandW - 1, clipH);
            GdipDeleteBrush(brush);
            GdipResetClip(g);
            GdipDeletePath(clip);
        }

        /// <summary>CSS radial-gradient(circle, colour 0%, transparent at radius).</summary>
        internal static void FillGlow(nint g, float cx, float cy, float radius, uint centre)
        {
            nint path;
            GdipCreatePath(0, &path);
            GdipAddPathEllipse(path, cx - radius, cy - radius, radius * 2, radius * 2);
            nint brush;
            GdipCreatePathGradientFromPath(path, &brush);
            GdipSetPathGradientCenterColor(brush, centre);
            uint surround = centre & 0x00FFFFFF;
            int count = 1;
            GdipSetPathGradientSurroundColorsWithCount(brush, &surround, &count);
            GdipFillPath(g, brush, path);
            GdipDeleteBrush(brush);
            GdipDeletePath(path);
        }

        /// <summary>
        /// A soft box-shadow under a round rect, built from translucent layers growing outwards. Not a
        /// true Gaussian blur, but at this size the eye cannot tell, and it costs a handful of fills.
        /// </summary>
        internal static void Shadow(nint g, float x, float y, float w, float h, float r, float blur, uint rgb, float alpha)
        {
            const int layers = 8;
            float step = blur / layers;
            for (int i = layers; i >= 1; i--)
            {
                float grow = step * i;
                float a = alpha * (1f - (float)i / (layers + 1)) / layers * 2.2f;
                FillRoundRect(g, Argb(rgb, a), x - grow / 2, y - grow / 2, w + grow, h + grow, r + grow / 2);
            }
        }

        // ---- images ----------------------------------------------------------

        internal static nint LoadImage(byte[] bytes, out float width, out float height)
        {
            width = height = 0;
            if (bytes == null || bytes.Length == 0)
                return 0;

            nint stream;
            fixed (byte* p = bytes)
                stream = Native.SHCreateMemStream(p, (uint)bytes.Length);
            if (stream == 0)
                return 0;

            nint image;
            int status = GdipCreateBitmapFromStream(stream, &image);
            Native.Release(stream);
            if (status != 0)
                return 0;

            uint w, h;
            GdipGetImageWidth(image, &w);
            GdipGetImageHeight(image, &h);
            width = w;
            height = h;
            return image;
        }

        internal static void DrawImage(nint g, nint image, float x, float y, float w, float h) =>
            GdipDrawImageRect(g, image, x, y, w, h);

        // ---- icons -----------------------------------------------------------

        /// <summary>
        /// Strokes an SVG icon drawn on a 24-unit grid - the same path data the page used, so the icons
        /// are the page's icons rather than lookalikes. Round caps and joins, as the markup asked for.
        /// </summary>
        internal static void StrokeIcon(nint g, string[] paths, float x, float y, float size, uint argb, float strokeWidth)
        {
            float scale = size / 24f;
            GdipTranslateWorldTransform(g, x, y, 0);
            GdipScaleWorldTransform(g, scale, scale, 0);

            nint pen;
            GdipCreatePen1(argb, strokeWidth, 0, &pen);
            GdipSetPenStartCap(pen, 2);
            GdipSetPenEndCap(pen, 2);
            GdipSetPenLineJoin(pen, 2);
            foreach (string d in paths)
            {
                if (d.StartsWith("dot:", StringComparison.Ordinal))
                {
                    // A zero-length round-capped line in SVG is a dot; GDI+ draws nothing for it.
                    string[] xy = d.Substring(4).Split(',');
                    float cx = float.Parse(xy[0], CultureInfo.InvariantCulture);
                    float cy = float.Parse(xy[1], CultureInfo.InvariantCulture);
                    FillEllipse(g, argb, cx - strokeWidth / 2, cy - strokeWidth / 2, strokeWidth, strokeWidth);
                    continue;
                }

                nint path = SvgPath.Build(d);
                GdipDrawPath(g, pen, path);
                GdipDeletePath(path);
            }
            GdipDeletePen(pen);
            GdipResetWorldTransform(g);
        }

        /// <summary>The subset of SVG path syntax the icons use: M L H V C S A Z, either case.</summary>
        private static class SvgPath
        {
            internal static nint Build(string d)
            {
                nint path;
                GdipCreatePath(0, &path);

                var tokens = Tokenize(d);
                int i = 0;
                char cmd = 'M';
                float cx = 0, cy = 0, startX = 0, startY = 0, lastCtrlX = 0, lastCtrlY = 0;
                bool lastWasCurve = false, figureOpen = false;

                float Next() => float.Parse(tokens[i++], CultureInfo.InvariantCulture);

                while (i < tokens.Count)
                {
                    if (char.IsLetter(tokens[i][0]))
                        cmd = tokens[i++][0];

                    bool rel = char.IsLower(cmd);
                    switch (char.ToUpperInvariant(cmd))
                    {
                        case 'M':
                        {
                            float x = Next(), y = Next();
                            if (rel) { x += cx; y += cy; }
                            GdipStartPathFigure(path);
                            cx = startX = x;
                            cy = startY = y;
                            figureOpen = true;
                            lastWasCurve = false;
                            // Further pairs after a moveto are implicit linetos.
                            cmd = rel ? 'l' : 'L';
                            break;
                        }
                        case 'L':
                        {
                            float x = Next(), y = Next();
                            if (rel) { x += cx; y += cy; }
                            GdipAddPathLine(path, cx, cy, x, y);
                            cx = x;
                            cy = y;
                            lastWasCurve = false;
                            break;
                        }
                        case 'H':
                        {
                            float x = Next();
                            if (rel) x += cx;
                            GdipAddPathLine(path, cx, cy, x, cy);
                            cx = x;
                            lastWasCurve = false;
                            break;
                        }
                        case 'V':
                        {
                            float y = Next();
                            if (rel) y += cy;
                            GdipAddPathLine(path, cx, cy, cx, y);
                            cy = y;
                            lastWasCurve = false;
                            break;
                        }
                        case 'C':
                        {
                            float x1 = Next(), y1 = Next(), x2 = Next(), y2 = Next(), x = Next(), y = Next();
                            if (rel) { x1 += cx; y1 += cy; x2 += cx; y2 += cy; x += cx; y += cy; }
                            GdipAddPathBezier(path, cx, cy, x1, y1, x2, y2, x, y);
                            lastCtrlX = x2;
                            lastCtrlY = y2;
                            cx = x;
                            cy = y;
                            lastWasCurve = true;
                            break;
                        }
                        case 'S':
                        {
                            float x2 = Next(), y2 = Next(), x = Next(), y = Next();
                            if (rel) { x2 += cx; y2 += cy; x += cx; y += cy; }
                            float x1 = lastWasCurve ? 2 * cx - lastCtrlX : cx;
                            float y1 = lastWasCurve ? 2 * cy - lastCtrlY : cy;
                            GdipAddPathBezier(path, cx, cy, x1, y1, x2, y2, x, y);
                            lastCtrlX = x2;
                            lastCtrlY = y2;
                            cx = x;
                            cy = y;
                            lastWasCurve = true;
                            break;
                        }
                        case 'A':
                        {
                            float rx = Next(), ry = Next(), rotation = Next(), large = Next(), sweep = Next(), x = Next(), y = Next();
                            if (rel) { x += cx; y += cy; }
                            AddArc(path, cx, cy, rx, ry, rotation, large != 0, sweep != 0, x, y);
                            cx = x;
                            cy = y;
                            lastWasCurve = false;
                            break;
                        }
                        case 'Z':
                            if (figureOpen)
                                GdipClosePathFigure(path);
                            cx = startX;
                            cy = startY;
                            figureOpen = false;
                            lastWasCurve = false;
                            break;
                        default:
                            i++;
                            break;
                    }
                }
                return path;
            }

            private static List<string> Tokenize(string d)
            {
                var tokens = new List<string>();
                int i = 0;
                while (i < d.Length)
                {
                    char c = d[i];
                    if (char.IsWhiteSpace(c) || c == ',')
                    {
                        i++;
                        continue;
                    }
                    if (char.IsLetter(c) && c != 'e' && c != 'E')
                    {
                        tokens.Add(c.ToString());
                        i++;
                        continue;
                    }

                    // A number: optional sign, digits with at most one point, optional exponent. A second
                    // point or a sign starts the next number, which is how ".71-.84" reads as two.
                    int start = i;
                    if (c == '-' || c == '+')
                        i++;
                    bool point = false;
                    while (i < d.Length && (char.IsDigit(d[i]) || (d[i] == '.' && !point)))
                    {
                        if (d[i] == '.')
                            point = true;
                        i++;
                    }
                    if (i < d.Length && (d[i] == 'e' || d[i] == 'E'))
                    {
                        i++;
                        if (i < d.Length && (d[i] == '-' || d[i] == '+'))
                            i++;
                        while (i < d.Length && char.IsDigit(d[i]))
                            i++;
                    }
                    if (i == start)
                    {
                        i++;   // not part of any number or command; skip rather than loop on it
                        continue;
                    }
                    tokens.Add(d.Substring(start, i - start));
                }
                return tokens;
            }

            /// <summary>SVG's endpoint arc as cubic Béziers - the conversion in SVG 1.1 appendix F.6.</summary>
            private static void AddArc(nint path, float x1, float y1, float rx, float ry, float angleDeg,
                                       bool largeArc, bool sweep, float x2, float y2)
            {
                if (rx == 0 || ry == 0)
                {
                    GdipAddPathLine(path, x1, y1, x2, y2);
                    return;
                }

                double phi = angleDeg * Math.PI / 180, cosPhi = Math.Cos(phi), sinPhi = Math.Sin(phi);
                double dx = (x1 - x2) / 2.0, dy = (y1 - y2) / 2.0;
                double x1p = cosPhi * dx + sinPhi * dy, y1p = -sinPhi * dx + cosPhi * dy;
                double arx = Math.Abs(rx), ary = Math.Abs(ry);

                double lambda = x1p * x1p / (arx * arx) + y1p * y1p / (ary * ary);
                if (lambda > 1)
                {
                    double s = Math.Sqrt(lambda);
                    arx *= s;
                    ary *= s;
                }

                double num = arx * arx * ary * ary - arx * arx * y1p * y1p - ary * ary * x1p * x1p;
                double den = arx * arx * y1p * y1p + ary * ary * x1p * x1p;
                double coef = Math.Sqrt(Math.Max(0, num / den)) * (largeArc == sweep ? -1 : 1);
                double cxp = coef * arx * y1p / ary, cyp = -coef * ary * x1p / arx;
                double ccx = cosPhi * cxp - sinPhi * cyp + (x1 + x2) / 2.0;
                double ccy = sinPhi * cxp + cosPhi * cyp + (y1 + y2) / 2.0;

                double Angle(double ux, double uy, double vx, double vy)
                {
                    double a = Math.Atan2(ux * vy - uy * vx, ux * vx + uy * vy);
                    return a;
                }

                double theta1 = Angle(1, 0, (x1p - cxp) / arx, (y1p - cyp) / ary);
                double delta = Angle((x1p - cxp) / arx, (y1p - cyp) / ary, (-x1p - cxp) / arx, (-y1p - cyp) / ary);
                if (!sweep && delta > 0) delta -= 2 * Math.PI;
                else if (sweep && delta < 0) delta += 2 * Math.PI;

                int segments = (int)Math.Ceiling(Math.Abs(delta) / (Math.PI / 2));
                double step = delta / segments;
                double k = 4.0 / 3.0 * Math.Tan(step / 4);

                double px = x1, py = y1;
                for (int s = 0; s < segments; s++)
                {
                    double a1 = theta1 + s * step, a2 = a1 + step;
                    double cos1 = Math.Cos(a1), sin1 = Math.Sin(a1), cos2 = Math.Cos(a2), sin2 = Math.Sin(a2);

                    (double X, double Y) Map(double ex, double ey) =>
                        (cosPhi * arx * ex - sinPhi * ary * ey + ccx, sinPhi * arx * ex + cosPhi * ary * ey + ccy);

                    var c1 = Map(cos1 - k * sin1, sin1 + k * cos1);
                    var c2 = Map(cos2 + k * sin2, sin2 - k * cos2);
                    var end = Map(cos2, sin2);
                    GdipAddPathBezier(path, (float)px, (float)py, (float)c1.X, (float)c1.Y,
                                      (float)c2.X, (float)c2.Y, (float)end.X, (float)end.Y);
                    px = end.X;
                    py = end.Y;
                }
            }
        }
    }
}
