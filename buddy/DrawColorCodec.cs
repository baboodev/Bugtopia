using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace HeartopiaMod
{
    internal static class DrawColorCodec
    {
        public const byte TransparentIndexByte = 124;
        public const byte OpaqueFlag = 0x80;
        public const string CachedLutFileName = ".drawing_color_lut.png";

        // Confirmed from runtime diagnostics + ilspy-dumps: DrawingConfig.ColorLut is loaded as a
        // 128x1 RGBA32 readable Texture2D named 'drawing_lut', also bound to the drawing photo-frame
        // material 'XDT/Common/Image' via the '_ColorLutTex' shader property.
        public const string DrawingLutTextureName = "drawing_lut";
        public const string DrawingLutShaderProperty = "_ColorLutTex";

        private static Color32[] cachedLut;
        private static string cachedLutSha256;

        public static bool TryResolveLut(string destRoot, out Color32[] lut, out string lutSha256)
        {
            lut = null;
            lutSha256 = null;

            if (cachedLut != null && cachedLut.Length > 0)
            {
                lut = cachedLut;
                lutSha256 = cachedLutSha256;
                return true;
            }

            // DrawingConfig.ColorLut is a real (CPU-readable) UnityEngine.Texture2D loaded into the
            // IL2CPP/Unity heap. The owning types (DrawingConfig/IConfigManager) are embedded-Mono and
            // invisible to managed FindLoadedType, so resolve the palette from the Unity side instead.
            if (TryLoadLutFromUnityTextures(out lut, out string unityTexName) && lut != null && lut.Length > 0)
            {
                lutSha256 = TryCacheLut(destRoot, lut);
                ModLogger.Msg("[DrawColorCodec] LUT from Unity texture '" + unityTexName + "' (" + lut.Length + " colors)");
                return true;
            }

            if (TryLoadLutFromFile(GetCachedLutPath(destRoot), out lut) && lut != null && lut.Length > 0)
            {
                lutSha256 = ComputeLutSha256(lut);
                cachedLut = lut;
                cachedLutSha256 = lutSha256;
                return true;
            }

            string modDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (!string.IsNullOrWhiteSpace(modDir))
            {
                string modLut = Path.Combine(modDir, "drawing_color_lut.png");
                if (TryLoadLutFromFile(modLut, out lut) && lut != null && lut.Length > 0)
                {
                    lutSha256 = ComputeLutSha256(lut);
                    cachedLut = lut;
                    cachedLutSha256 = lutSha256;
                    return true;
                }
            }

            lut = null;
            lutSha256 = null;
            return false;
        }

        private static string GetCachedLutPath(string destRoot)
        {
            if (!string.IsNullOrWhiteSpace(destRoot))
            {
                return Path.Combine(destRoot, CachedLutFileName);
            }

            return Path.Combine(HelperPaths.Root, "ScreenCaptureDecrypted", CachedLutFileName);
        }

        public static byte[] IndexPngToColoredPng(byte[] indexPngBytes, Color32[] lut)
        {
            if (indexPngBytes == null || indexPngBytes.Length == 0 || lut == null || lut.Length == 0)
            {
                return null;
            }

            Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!tex.LoadImage(indexPngBytes))
                {
                    return null;
                }

                int w = tex.width;
                int h = tex.height;
                Color32[] src = tex.GetPixels32();
                Color32[] dst = new Color32[src.Length];
                for (int i = 0; i < src.Length; i++)
                {
                    // Mirror the game decode (XDTGameUI PaintingDetailWidget R8 branch):
                    //   alpha = (raw != 124) ? 255 : 0;  rgb = ColorLut.GetPixels()[raw & 0x7F]
                    // Transparency is keyed purely on the canvas-fill byte 124 (== GetIndex(124)),
                    // NOT on the 0x80 bit — stored opaque pixels are 0x80|index, fill is 124.
                    byte idxByte = src[i].r;
                    if (idxByte == TransparentIndexByte)
                    {
                        dst[i] = new Color32(0, 0, 0, 0);
                        continue;
                    }

                    int lutIndex = idxByte & 0x7F;
                    if (lutIndex < 0 || lutIndex >= lut.Length)
                    {
                        dst[i] = new Color32(0, 0, 0, 0);
                        continue;
                    }

                    Color32 c = lut[lutIndex];
                    dst[i] = new Color32(c.r, c.g, c.b, 255);
                }

                Texture2D outTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
                outTex.SetPixels32(dst);
                outTex.Apply();
                byte[] png = outTex.EncodeToPNG();
                UnityEngine.Object.Destroy(outTex);
                return png;
            }
            finally
            {
                UnityEngine.Object.Destroy(tex);
            }
        }

        public static byte[] ColoredPngToIndexPng(byte[] coloredPngBytes, Color32[] lut)
        {
            if (coloredPngBytes == null || coloredPngBytes.Length == 0 || lut == null || lut.Length == 0)
            {
                return null;
            }

            Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!tex.LoadImage(coloredPngBytes))
                {
                    return null;
                }

                int w = tex.width;
                int h = tex.height;
                Color32[] src = tex.GetPixels32();
                // The game stores index maps as 8-bit RGB PNGs (R=G=B=index). Encoding an R8
                // texture yields a grayscale (colortype 0) PNG, which the game loads WITHOUT
                // applying the LUT -> the drawing shows up black-and-white. Emit RGB24 to match.
                Color32[] indexPixels = new Color32[src.Length];
                for (int i = 0; i < src.Length; i++)
                {
                    Color32 p = src[i];
                    byte indexByte;
                    if (p.a < 128)
                    {
                        indexByte = TransparentIndexByte;
                    }
                    else
                    {
                        int best = FindClosestLutIndex(p, lut);
                        indexByte = lut[best].a == 0
                            ? TransparentIndexByte
                            : (byte)(OpaqueFlag | best);
                    }

                    indexPixels[i] = new Color32(indexByte, indexByte, indexByte, 255);
                }

                Texture2D outTex = new Texture2D(w, h, TextureFormat.RGB24, false);
                outTex.SetPixels32(indexPixels);
                outTex.Apply();
                byte[] png = outTex.EncodeToPNG();
                UnityEngine.Object.Destroy(outTex);
                return png;
            }
            finally
            {
                UnityEngine.Object.Destroy(tex);
            }
        }

        // Build a colored RGBA PNG from a raw R8 index byte array (CanvasPainter.pixelData order,
        // bottom-up). SetPixels32 is also bottom-up, so EncodeToPNG yields an upright image and the
        // upload path (ColoredPngToIndexBytes via GetPixels32) reads it back in the same order.
        public static byte[] IndexBytesToColoredPng(byte[] indexBytes, int width, int height, Color32[] lut)
        {
            if (indexBytes == null || width <= 0 || height <= 0 || width * height != indexBytes.Length
                || lut == null || lut.Length == 0)
            {
                return null;
            }

            Color32[] px = new Color32[indexBytes.Length];
            for (int i = 0; i < indexBytes.Length; i++)
            {
                byte b = indexBytes[i];
                if (b == TransparentIndexByte)
                {
                    px[i] = new Color32(0, 0, 0, 0);
                    continue;
                }

                int idx = b & 0x7F;
                if (idx < 0 || idx >= lut.Length)
                {
                    px[i] = new Color32(0, 0, 0, 0);
                    continue;
                }

                Color32 c = lut[idx];
                px[i] = new Color32(c.r, c.g, c.b, 255);
            }

            Texture2D outTex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            try
            {
                outTex.SetPixels32(px);
                outTex.Apply();
                return outTex.EncodeToPNG();
            }
            finally
            {
                UnityEngine.Object.Destroy(outTex);
            }
        }

        // Quantize a colored PNG to the flat R8 index byte array in the game's pixel order.
        // GetPixels32() is bottom-up, matching CanvasPainter's GetRawTextureData()/flat index
        // (y*Width+x, y=0 at the bottom) — confirmed in-game (pixel 0 = bottom-left), so no flip.
        public static byte[] ColoredPngToIndexBytes(byte[] coloredPngBytes, Color32[] lut, out int width, out int height)
        {
            width = 0;
            height = 0;
            if (coloredPngBytes == null || coloredPngBytes.Length == 0 || lut == null || lut.Length == 0)
            {
                return null;
            }

            Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!tex.LoadImage(coloredPngBytes))
                {
                    return null;
                }

                width = tex.width;
                height = tex.height;
                Color32[] src = tex.GetPixels32();
                byte[] raw = new byte[src.Length];
                for (int i = 0; i < src.Length; i++)
                {
                    Color32 p = src[i];
                    if (p.a < 128)
                    {
                        raw[i] = TransparentIndexByte;
                        continue;
                    }

                    int best = FindClosestLutIndex(p, lut);
                    raw[i] = lut[best].a == 0 ? TransparentIndexByte : (byte)(OpaqueFlag | best);
                }

                return raw;
            }
            finally
            {
                UnityEngine.Object.Destroy(tex);
            }
        }

        // Mirror of XDTLevelAndEntity.GameplaySystem.Drawing.CanvasPainter.Save: group the flat
        // pixel array by stored color byte into RLE runs for DrawingBatchOperationNetworkCommand.
        //   indexBytes : R8 flat array, length == width*height, flat index = y*width + x.
        //                Values are the stored bytes (0x80|paletteIndex opaque, 124 transparent).
        //   each run   : Start = first flat index of a consecutive same-byte run,
        //                Length = (runLength - 1)   (a single pixel => Length 0).
        // Returns null if the canvas exceeds the ushort Start/Length range (> 65536 pixels).
        public static Dictionary<byte, List<(int Start, int Length)>> BuildPixelRuns(byte[] indexBytes, int width, int height)
        {
            if (indexBytes == null || width <= 0 || height <= 0)
            {
                return null;
            }

            int n = width * height;
            if (n != indexBytes.Length || n > 65536)
            {
                return null;
            }

            Dictionary<byte, List<(int Start, int Length)>> result = new Dictionary<byte, List<(int Start, int Length)>>();
            int i = 0;
            while (i < n)
            {
                byte b = indexBytes[i];
                int start = i;
                int j = i + 1;
                while (j < n && indexBytes[j] == b && (j - start) < 65536)
                {
                    j++;
                }

                if (!result.TryGetValue(b, out List<(int Start, int Length)> list))
                {
                    list = new List<(int Start, int Length)>();
                    result[b] = list;
                }

                list.Add((start, (j - start) - 1));
                i = j;
            }

            return result;
        }

        private static bool TryLoadLutFromFile(string path, out Color32[] lut)
        {
            lut = null;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return false;
            }

            try
            {
                byte[] bytes = File.ReadAllBytes(path);
                Texture2D tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                if (!tex.LoadImage(bytes))
                {
                    UnityEngine.Object.Destroy(tex);
                    return false;
                }

                lut = tex.GetPixels32();
                UnityEngine.Object.Destroy(tex);
                return lut != null && lut.Length > 0;
            }
            catch
            {
                return false;
            }
        }

        private static string CacheResolvedLut(string destRoot, Color32[] lut)
        {
            cachedLut = lut;
            cachedLutSha256 = ComputeLutSha256(lut);
            TryDumpColorLutFromPixels(GetCachedLutPath(destRoot), lut);
            return cachedLutSha256;
        }

        private static bool TryExtractLutPixels(Texture lutTexture, out Color32[] lut)
        {
            lut = null;
            if (lutTexture == null)
            {
                return false;
            }

            try
            {
                // Prefer direct GetPixels() for a readable texture: it returns pixels in the SAME
                // row order the game uses (ColorLut.GetPixels()[raw & 0x7F]). A Blit+ReadPixels copy
                // can be vertically flipped, which would scramble a multi-row LUT's indices.
                if (lutTexture is Texture2D texture2D && texture2D.isReadable)
                {
                    Color[] pixels = texture2D.GetPixels();
                    if (TryConvertUnityColors(pixels, out lut))
                    {
                        return true;
                    }
                }

                // Non-readable: blit into a CPU-readable copy as a fallback (bag-icon scan pattern).
                return TryReadPixelsViaBlit(lutTexture, out lut);
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[DrawColorCodec] LUT pixel extract failed: " + ex.Message);
            }

            return false;
        }

        // Find DrawingConfig.ColorLut by scanning loaded Unity Texture2D objects (IL2CPP heap).
        // The real palette is a small, readable texture with >= ~126 entries (index space is
        // raw & 0x7F plus the 124/125 transparent slots). Every palette-sized candidate is logged
        // so the name match can be tightened from real output if the heuristic is ambiguous.
        private static bool TryLoadLutFromUnityTextures(out Color32[] lut, out string chosenName)
        {
            lut = null;
            chosenName = null;

            try
            {
                Texture2D[] all = Resources.FindObjectsOfTypeAll<Texture2D>();

                // 1. Exact texture name (the confirmed 'drawing_lut' palette).
                if (all != null)
                {
                    for (int i = 0; i < all.Length; i++)
                    {
                        Texture2D t = all[i];
                        if (t != null
                            && string.Equals(t.name, DrawingLutTextureName, StringComparison.OrdinalIgnoreCase)
                            && TryExtractLutPixels(t, out lut) && lut != null && lut.Length > 0)
                        {
                            chosenName = t.name;
                            return true;
                        }
                    }
                }

                // 2. The drawing photo-frame material binds the same palette to '_ColorLutTex'.
                if (TryLoadLutFromMaterialProperty(DrawingLutShaderProperty, out lut, out chosenName)
                    && lut != null && lut.Length > 0)
                {
                    return true;
                }

                // 3. Heuristic fallback (in case the asset name changes in a future patch).
                lut = null;
                chosenName = null;
                if (all == null || all.Length == 0)
                {
                    return false;
                }

                Texture2D best = null;
                int bestScore = int.MinValue;
                int candidates = 0;
                for (int i = 0; i < all.Length; i++)
                {
                    Texture2D tex = all[i];
                    if (tex == null)
                    {
                        continue;
                    }

                    int w = tex.width;
                    int h = tex.height;
                    int count = w * h;
                    bool paletteSized = count >= 126 && count <= 8192 && (w <= 32 || h <= 32);
                    if (!paletteSized)
                    {
                        continue;
                    }

                    string name = tex.name ?? string.Empty;
                    string lower = name.ToLowerInvariant();
                    int score = 0;
                    if (lower.Contains("colorlut"))
                    {
                        score += 100;
                    }
                    else if (lower.Contains("lut"))
                    {
                        score += 80;
                    }

                    if (lower.Contains("palette"))
                    {
                        score += 60;
                    }

                    if (lower.Contains("draw"))
                    {
                        score += 40;
                    }

                    if (lower.Contains("color"))
                    {
                        score += 20;
                    }

                    if (tex.isReadable)
                    {
                        score += 5;
                    }

                    candidates++;
                    ModLogger.Msg("[DrawColorCodec] LUT candidate '" + name + "' " + w + "x" + h
                        + " readable=" + tex.isReadable + " score=" + score);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = tex;
                    }
                }

                if (best == null)
                {
                    return false;
                }

                // Don't guess when nothing carries a name signal and several textures qualify.
                if (bestScore < 20 && candidates > 1)
                {
                    ModLogger.Msg("[DrawColorCodec] LUT ambiguous (" + candidates
                        + " palette-sized textures, no name hint) — not guessing; check candidates above");
                    return false;
                }

                if (!TryExtractLutPixels(best, out lut) || lut == null || lut.Length == 0)
                {
                    return false;
                }

                chosenName = best.name;
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[DrawColorCodec] Unity texture LUT scan failed: " + ex.Message);
                lut = null;
                chosenName = null;
                return false;
            }
        }

        // The drawing photo-frame material ('XDT/Common/Image') binds the palette to a named texture
        // property; pull the LUT straight off any material that has it set.
        private static bool TryLoadLutFromMaterialProperty(string propertyName, out Color32[] lut, out string chosenName)
        {
            lut = null;
            chosenName = null;
            if (string.IsNullOrEmpty(propertyName))
            {
                return false;
            }

            try
            {
                Material[] materials = Resources.FindObjectsOfTypeAll<Material>();
                if (materials == null)
                {
                    return false;
                }

                for (int i = 0; i < materials.Length; i++)
                {
                    Material mat = materials[i];
                    if (mat == null || !mat.HasProperty(propertyName))
                    {
                        continue;
                    }

                    Texture bound = mat.GetTexture(propertyName);
                    if (bound != null && TryExtractLutPixels(bound, out lut) && lut != null && lut.Length > 0)
                    {
                        chosenName = bound.name;
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[DrawColorCodec] material-property LUT scan failed: " + ex.Message);
            }

            lut = null;
            chosenName = null;
            return false;
        }

        private static bool TryReadPixelsViaBlit(Texture source, out Color32[] lut)
        {
            lut = null;
            if (source == null)
            {
                return false;
            }

            int width = source.width;
            int height = source.height;
            if (width <= 0 || height <= 0)
            {
                return false;
            }

            RenderTexture rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            Texture2D copy = null;
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                copy = new Texture2D(width, height, TextureFormat.RGBA32, false);
                copy.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                copy.Apply();
                lut = copy.GetPixels32();
                return lut != null && lut.Length > 0;
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[DrawColorCodec] LUT blit read failed: " + ex.Message);
                lut = null;
                return false;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
                if (copy != null)
                {
                    UnityEngine.Object.Destroy(copy);
                }
            }
        }

        private static bool TryConvertUnityColors(Color[] pixels, out Color32[] lut)
        {
            lut = null;
            if (pixels == null || pixels.Length == 0)
            {
                return false;
            }

            lut = new Color32[pixels.Length];
            for (int i = 0; i < pixels.Length; i++)
            {
                lut[i] = pixels[i];
            }

            return true;
        }

        private static int FindClosestLutIndex(Color32 p, Color32[] lut)
        {
            int best = 0;
            int bestDist = int.MaxValue;
            for (int i = 0; i < lut.Length; i++)
            {
                if (lut[i].a == 0)
                {
                    continue;
                }

                int dr = p.r - lut[i].r;
                int dg = p.g - lut[i].g;
                int db = p.b - lut[i].b;
                int dist = dr * dr + dg * dg + db * db;
                if (dist < bestDist)
                {
                    bestDist = dist;
                    best = i;
                }
            }

            return best;
        }

        private static Texture2D BuildLutTexture(Color32[] lut)
        {
            int width = Mathf.Max(1, lut.Length);
            Texture2D tex = new Texture2D(width, 1, TextureFormat.RGBA32, false);
            Color32[] row = new Color32[width];
            for (int i = 0; i < width; i++)
            {
                row[i] = i < lut.Length ? lut[i] : new Color32(0, 0, 0, 0);
            }

            tex.SetPixels32(row);
            tex.Apply();
            return tex;
        }

        private static string TryCacheLut(string destRoot, Color32[] lut)
        {
            return CacheResolvedLut(destRoot, lut);
        }

        private static void TryDumpColorLutFromPixels(string path, Color32[] lut)
        {
            try
            {
                Texture2D tex = BuildLutTexture(lut);
                byte[] png = tex.EncodeToPNG();
                UnityEngine.Object.Destroy(tex);
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllBytes(path, png);
            }
            catch
            {
            }
        }

        private static string ComputeLutSha256(Color32[] lut)
        {
            if (lut == null || lut.Length == 0)
            {
                return string.Empty;
            }

            byte[] raw = new byte[lut.Length * 4];
            for (int i = 0; i < lut.Length; i++)
            {
                int offset = i * 4;
                raw[offset] = lut[i].r;
                raw[offset + 1] = lut[i].g;
                raw[offset + 2] = lut[i].b;
                raw[offset + 3] = lut[i].a;
            }

            using System.Security.Cryptography.SHA256 sha = System.Security.Cryptography.SHA256.Create();
            byte[] hash = sha.ComputeHash(raw);
            System.Text.StringBuilder builder = new System.Text.StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++)
            {
                builder.Append(hash[i].ToString("x2"));
            }

            return builder.ToString();
        }
    }
}
