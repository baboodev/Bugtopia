using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UnityEngine;

namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        private const string PicturesManifestFileName = ".bugtopia-manifest.json";
        private const string PicturesLegacyManifestFileName = ".heartopia-helper-manifest.json";
        private const int PicturesManifestVersion = 2;
        private const string PicturesContentPhoto = "photo";
        private const string PicturesContentDrawIndex = "draw-index";
        private const string PicturesContentDrawColored = "draw-colored";

        private static readonly byte[] PicturesDecryptAesKey =
        {
            18, 52, 86, 120, 144, 171, 205, 239, 254, 220,
            186, 152, 118, 84, 50, 16, 18, 52, 86, 120,
            144, 171, 205, 239, 254, 220, 186, 152, 118, 84,
            50, 16
        };

        private static readonly byte[] PicturesDecryptAesIv =
        {
            1, 35, 69, 103, 137, 171, 205, 239, 254, 220,
            186, 152, 118, 84, 50, 16
        };

        private object picturesTaskCoroutine = null;
        private string picturesLastStatus = "Idle.";
        private readonly List<string> picturesChangedRelativePaths = new List<string>();
        private Vector2 picturesChangedScrollPos = Vector2.zero;
        private bool picturesChangedListDirty = true;

        private sealed class PicturesManifest
        {
            public int Version { get; set; } = PicturesManifestVersion;

            public string SourceRoot { get; set; }

            public string DecryptedRoot { get; set; }

            public string BatchFinishedUtc { get; set; }

            public Dictionary<string, PicturesManifestEntry> Files { get; set; } = new Dictionary<string, PicturesManifestEntry>(StringComparer.OrdinalIgnoreCase);
        }

        private sealed class PicturesManifestEntry
        {
            public string PlainSha256 { get; set; }

            public long PlainSize { get; set; }

            public bool WasEncrypted { get; set; }

            public string ContentKind { get; set; } = PicturesContentPhoto;

            public string IndexPlainSha256 { get; set; }

            public string LutSha256 { get; set; }
        }

        private float DrawPicturesTab(float startY)
        {
            const float left = 40f;
            const float width = 520f;
            const float btnW = 248f;
            const float btnH = 32f;
            const float btnGap = 8f;

            float y = startY + 8f;
            Color textColor = new Color(this.uiTextR, this.uiTextG, this.uiTextB);
            GUIStyle labelStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                wordWrap = true
            };
            labelStyle.normal.textColor = textColor;

            GUIStyle bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 11,
                alignment = TextAnchor.UpperLeft,
                wordWrap = true
            };
            bodyStyle.normal.textColor = textColor;

            string sourcePath = this.TryGetScreenCaptureRootPath();
            string destPath = this.GetScreenCaptureDecryptedRootPath(sourcePath);
            PicturesManifest manifest = this.TryLoadPicturesManifest(destPath);
            if (this.picturesChangedListDirty)
            {
                this.RefreshPicturesChangedList(manifest, destPath);
                this.picturesChangedListDirty = false;
            }

            const float pad = 16f;
            const float rowGap = 10f;
            const float statusH = 56f;
            float innerW = width - pad * 2f;
            string pathsText = this.LF("pictures.paths", sourcePath, destPath);
            float pathsH = Mathf.Ceil(bodyStyle.CalcHeight(new GUIContent(pathsText), innerW)) + 4f;
            float rowPairW = btnW * 2f + btnGap;
            float scrollH = this.picturesChangedRelativePaths.Count > 0
                ? Mathf.Min(this.picturesChangedRelativePaths.Count, 6) * 18f + 4f
                : 0f;
            float hintH = Mathf.Ceil(bodyStyle.CalcHeight(new GUIContent(this.L("pictures.draw_hint")), innerW)) + 4f;
            float sectionHeight = 10f + 22f + pathsH + rowGap
                + hintH + rowGap
                + btnH + rowGap + btnH + rowGap + btnH + rowGap
                + 20f + scrollH + 8f + statusH + pad;

            Rect sectionRect = new Rect(left, y, width, sectionHeight);
            GUI.Box(sectionRect, string.Empty, this.themePanelStyle ?? GUI.skin.box);
            this.DrawCardOutline(sectionRect, 1f);

            float innerX = sectionRect.x + pad;
            float cursorY = sectionRect.y + 10f;

            GUI.Label(new Rect(innerX, cursorY, innerW, 22f), this.L("pictures.title"), labelStyle);
            cursorY += 22f;

            GUI.Label(new Rect(innerX, cursorY, innerW, pathsH), pathsText, bodyStyle);
            cursorY += pathsH + rowGap;

            GUI.Label(new Rect(innerX, cursorY, innerW, hintH), this.L("pictures.draw_hint"), bodyStyle);
            cursorY += hintH + rowGap;

            bool busy = this.picturesTaskCoroutine != null;
            bool hasManifest = manifest != null && manifest.Files != null && manifest.Files.Count > 0;
            GUI.enabled = !busy;
            if (GUI.Button(new Rect(innerX, cursorY, btnW, btnH), this.L("pictures.decrypt_all"), this.themePrimaryButtonStyle ?? GUI.skin.button))
            {
                this.StartPicturesDecryptAll(false);
            }

            if (GUI.Button(new Rect(innerX + btnW + btnGap, cursorY, btnW, btnH), this.L("pictures.encrypt_changed"), this.themePrimaryButtonStyle ?? GUI.skin.button))
            {
                this.StartPicturesEncryptChanged(false);
            }

            cursorY += btnH + rowGap;

            if (GUI.Button(new Rect(innerX, cursorY, rowPairW, btnH), this.L("pictures.scan_changed"), GUI.skin.button))
            {
                this.picturesChangedListDirty = true;
                manifest = this.TryLoadPicturesManifest(destPath);
                this.RefreshPicturesChangedList(manifest, destPath);
                this.picturesChangedListDirty = false;
                hasManifest = manifest != null && manifest.Files != null && manifest.Files.Count > 0;
                this.picturesLastStatus = hasManifest
                    ? this.LF("pictures.changed_count", this.picturesChangedRelativePaths.Count)
                    : this.L("pictures.manifest_missing");
            }

            cursorY += btnH + rowGap;

            // Draw extract/upload (open the drawing in-game first). Extract -> drawing.png, edit, Upload.
            if (GUI.Button(new Rect(innerX, cursorY, btnW, btnH), "Extract open drawing", GUI.skin.button))
            {
                this.DrawExtractOpenDrawing();
            }

            if (GUI.Button(new Rect(innerX + btnW + btnGap, cursorY, btnW, btnH), "Upload drawing.png", GUI.skin.button))
            {
                this.DrawUploadSendForOpenDrawing();
            }

            GUI.enabled = true;
            cursorY += btnH + rowGap;

            string changedHeader = hasManifest
                ? this.LF("pictures.changed_count", this.picturesChangedRelativePaths.Count)
                : this.L("pictures.manifest_missing");
            GUI.Label(new Rect(innerX, cursorY, innerW, 20f), changedHeader, bodyStyle);
            cursorY += 20f;

            if (this.picturesChangedRelativePaths.Count > 0)
            {
                Rect scrollRect = new Rect(innerX - 4f, cursorY, innerW + 8f, scrollH);
                float contentHeight = this.picturesChangedRelativePaths.Count * 18f;
                this.picturesChangedScrollPos = GUI.BeginScrollView(scrollRect, this.picturesChangedScrollPos, new Rect(0f, 0f, scrollRect.width - 18f, contentHeight));
                for (int i = 0; i < this.picturesChangedRelativePaths.Count; i++)
                {
                    GUI.Label(new Rect(0f, i * 18f, scrollRect.width - 18f, 18f), this.picturesChangedRelativePaths[i], bodyStyle);
                }

                GUI.EndScrollView();
                cursorY = scrollRect.yMax + 8f;
            }

            string status = string.IsNullOrWhiteSpace(this.picturesLastStatus) ? "Idle." : this.picturesLastStatus;
            GUI.Label(new Rect(innerX, cursorY, innerW, statusH), status, bodyStyle);

            return sectionRect.yMax + 24f;
        }

        private void StartPicturesDecryptAll(bool silent)
        {
            if (this.picturesTaskCoroutine != null)
            {
                if (!silent)
                {
                    this.AddMenuNotification(this.L("pictures.busy"), new Color(0.45f, 0.88f, 1f));
                }

                return;
            }

            this.picturesLastStatus = this.L("pictures.decrypting");
            this.picturesTaskCoroutine = ModCoroutines.Start(this.PicturesDecryptAllRoutine(silent));
        }

        private void StartPicturesEncryptChanged(bool silent)
        {
            if (this.picturesTaskCoroutine != null)
            {
                if (!silent)
                {
                    this.AddMenuNotification(this.L("pictures.busy"), new Color(0.45f, 0.88f, 1f));
                }

                return;
            }

            this.picturesLastStatus = this.L("pictures.encrypting");
            this.picturesTaskCoroutine = ModCoroutines.Start(this.PicturesEncryptChangedRoutine(silent));
        }

        private IEnumerator PicturesDecryptAllRoutine(bool silent)
        {
            string sourceRoot = this.TryGetScreenCaptureRootPath();
            if (string.IsNullOrWhiteSpace(sourceRoot) || !Directory.Exists(sourceRoot))
            {
                this.picturesLastStatus = this.L("pictures.source_missing");
                if (!silent)
                {
                    this.AddMenuNotification(this.picturesLastStatus, new Color(1f, 0.55f, 0.45f));
                }

                this.picturesTaskCoroutine = null;
                yield break;
            }

            string destRoot = this.GetScreenCaptureDecryptedRootPath(sourceRoot);
            string[] files;
            PicturesManifest manifest = this.TryLoadPicturesManifest(destRoot) ?? new PicturesManifest();
            if (manifest.Files == null)
            {
                manifest.Files = new Dictionary<string, PicturesManifestEntry>(StringComparer.OrdinalIgnoreCase);
            }

            manifest.SourceRoot = sourceRoot;
            manifest.DecryptedRoot = destRoot;

            try
            {
                Directory.CreateDirectory(destRoot);
                files = Directory.GetFiles(sourceRoot, "*", SearchOption.AllDirectories);
            }
            catch (Exception ex)
            {
                this.picturesLastStatus = "Decrypt failed: " + ex.Message;
                if (!silent)
                {
                    this.AddMenuNotification(this.picturesLastStatus, new Color(1f, 0.55f, 0.45f));
                }

                ModLogger.Msg("[Pictures] decrypt error: " + ex.Message);
                this.picturesTaskCoroutine = null;
                yield break;
            }

            int decrypted = 0;
            int copiedPlain = 0;
            int failed = 0;
            int skipped = 0;
            int drawPreviews = 0;
            int processed = 0;
            Color32[] drawLut = null;
            string drawLutSha256 = null;
            bool hasDrawLut = false;
            yield return this.CoTryResolveDrawColorLut(destRoot, (resolved, lut, sha) =>
            {
                hasDrawLut = resolved;
                drawLut = lut;
                drawLutSha256 = sha;
            });
            if (!hasDrawLut)
            {
                ModLogger.Msg("[Pictures] Draw LUT unavailable — Draw files will stay grayscale index PNG");
            }

            for (int i = 0; i < files.Length; i++)
            {
                string sourceFile = files[i];
                string relativePath = this.NormalizePicturesRelativePath(sourceFile.Substring(sourceRoot.Length));
                string destFile = Path.Combine(destRoot, relativePath);
                if (this.ShouldSkipPicturesDecrypt(manifest, relativePath, destFile))
                {
                    skipped++;
                    processed++;
                    if (processed % 24 == 0)
                    {
                        this.picturesLastStatus = this.LF("pictures.progress", processed, files.Length, decrypted, copiedPlain, failed, skipped);
                        yield return null;
                    }

                    continue;
                }

                string destDir = Path.GetDirectoryName(destFile);
                if (!string.IsNullOrWhiteSpace(destDir))
                {
                    try
                    {
                        Directory.CreateDirectory(destDir);
                    }
                    catch
                    {
                        failed++;
                        processed++;
                        continue;
                    }
                }

                if (!this.TryReadDecryptedPlainFromSource(sourceFile, out byte[] plainBytes, out bool wasEncrypted))
                {
                    failed++;
                    processed++;
                    continue;
                }

                if (this.IsDrawRelativePath(relativePath) && hasDrawLut)
                {
                    if (this.TryWriteDrawDecryptedFiles(
                            manifest,
                            destRoot,
                            relativePath,
                            destFile,
                            plainBytes,
                            wasEncrypted,
                            drawLut,
                            drawLutSha256))
                    {
                        if (wasEncrypted)
                        {
                            decrypted++;
                        }
                        else
                        {
                            copiedPlain++;
                        }

                        drawPreviews++;
                    }
                    else
                    {
                        failed++;
                    }
                }
                else
                {
                    try
                    {
                        File.WriteAllBytes(destFile, plainBytes);
                        if (wasEncrypted)
                        {
                            decrypted++;
                            this.TryAddPicturesManifestEntry(manifest, relativePath, destFile, wasEncrypted: true);
                        }
                        else
                        {
                            copiedPlain++;
                            this.TryAddPicturesManifestEntry(manifest, relativePath, destFile, wasEncrypted: false);
                        }
                    }
                    catch
                    {
                        failed++;
                    }
                }

                processed++;
                if (processed % 24 == 0)
                {
                    this.picturesLastStatus = this.LF("pictures.progress", processed, files.Length, decrypted, copiedPlain, failed, skipped);
                    yield return null;
                }
            }

            this.PrunePicturesManifestMissingSources(manifest, sourceRoot);
            manifest.BatchFinishedUtc = DateTime.UtcNow.ToString("o");
            this.TrySavePicturesManifest(manifest, destRoot);

            int totalOk = decrypted + copiedPlain;
            this.picturesLastStatus = drawPreviews > 0
                ? this.LF("pictures.done_draw", totalOk, decrypted, copiedPlain, failed, skipped, drawPreviews, destRoot)
                : this.LF("pictures.done", totalOk, decrypted, copiedPlain, failed, skipped, destRoot);
            if (!silent)
            {
                Color color = failed > 0
                    ? new Color(1f, 0.75f, 0.45f)
                    : new Color(0.45f, 1f, 0.55f);
                this.AddMenuNotification(this.LF("pictures.done_short", totalOk, skipped, destRoot), color);
            }

            ModLogger.Msg("[Pictures] decrypt done source=" + sourceRoot + " dest=" + destRoot
                + " decrypted=" + decrypted + " plain=" + copiedPlain + " failed=" + failed
                + " skipped=" + skipped + " manifest=" + manifest.Files.Count);
            this.picturesChangedListDirty = true;
            this.picturesTaskCoroutine = null;
        }

        private IEnumerator PicturesEncryptChangedRoutine(bool silent)
        {
            string sourceRoot = this.TryGetScreenCaptureRootPath();
            string destRoot = this.GetScreenCaptureDecryptedRootPath(sourceRoot);
            PicturesManifest manifest = this.TryLoadPicturesManifest(destRoot);
            if (manifest == null || manifest.Files == null || manifest.Files.Count == 0)
            {
                this.picturesLastStatus = this.L("pictures.manifest_missing");
                if (!silent)
                {
                    this.AddMenuNotification(this.picturesLastStatus, new Color(1f, 0.55f, 0.45f));
                }

                this.picturesTaskCoroutine = null;
                yield break;
            }

            if (string.IsNullOrWhiteSpace(manifest.SourceRoot) || !Directory.Exists(manifest.SourceRoot))
            {
                manifest.SourceRoot = sourceRoot;
            }

            if (string.IsNullOrWhiteSpace(manifest.DecryptedRoot))
            {
                manifest.DecryptedRoot = destRoot;
            }

            List<string> changedPaths = new List<string>();
            this.CollectPicturesChangedPaths(manifest, manifest.DecryptedRoot, changedPaths);
            if (changedPaths.Count == 0)
            {
                this.picturesLastStatus = this.L("pictures.no_changes");
                if (!silent)
                {
                    this.AddMenuNotification(this.picturesLastStatus, new Color(0.45f, 0.88f, 1f));
                }

                this.picturesChangedRelativePaths.Clear();
                this.picturesTaskCoroutine = null;
                yield break;
            }

            Color32[] drawLut = null;
            string drawLutSha256 = null;
            bool hasDrawLut = false;
            yield return this.CoTryResolveDrawColorLut(manifest.DecryptedRoot, (resolved, lut, sha) =>
            {
                hasDrawLut = resolved;
                drawLut = lut;
                drawLutSha256 = sha;
            });

            int imported = 0;
            int failed = 0;
            int processed = 0;
            for (int i = 0; i < changedPaths.Count; i++)
            {
                string relativePath = changedPaths[i];
                if (!manifest.Files.TryGetValue(relativePath, out PicturesManifestEntry entry))
                {
                    failed++;
                    processed++;
                    continue;
                }

                if (this.TryImportChangedFile(manifest, relativePath, entry, drawLut, drawLutSha256, hasDrawLut))
                {
                    imported++;
                }
                else
                {
                    failed++;
                }

                processed++;
                if (processed % 12 == 0)
                {
                    this.picturesLastStatus = this.LF("pictures.encrypt_progress", processed, changedPaths.Count, imported, failed);
                    yield return null;
                }
            }

            manifest.BatchFinishedUtc = DateTime.UtcNow.ToString("o");
            this.TrySavePicturesManifest(manifest, manifest.DecryptedRoot);
            this.RefreshPicturesChangedList(manifest, manifest.DecryptedRoot);

            this.picturesLastStatus = this.LF("pictures.encrypt_done", imported, failed, manifest.SourceRoot);
            if (!silent)
            {
                Color color = failed > 0
                    ? new Color(1f, 0.75f, 0.45f)
                    : new Color(0.45f, 1f, 0.55f);
                this.AddMenuNotification(this.LF("pictures.encrypt_done_short", imported), color);
            }

            ModLogger.Msg("[Pictures] encrypt changed imported=" + imported + " failed=" + failed + " target=" + manifest.SourceRoot);
            this.picturesTaskCoroutine = null;
        }

        private void RefreshPicturesChangedList(PicturesManifest manifest, string destRoot)
        {
            this.picturesChangedRelativePaths.Clear();
            if (manifest == null || manifest.Files == null || manifest.Files.Count == 0)
            {
                return;
            }

            string root = string.IsNullOrWhiteSpace(manifest.DecryptedRoot) ? destRoot : manifest.DecryptedRoot;
            this.CollectPicturesChangedPaths(manifest, root, this.picturesChangedRelativePaths);
        }

        private void CollectPicturesChangedPaths(PicturesManifest manifest, string destRoot, List<string> output)
        {
            if (manifest?.Files == null || output == null)
            {
                return;
            }

            foreach (KeyValuePair<string, PicturesManifestEntry> pair in manifest.Files)
            {
                string relativePath = pair.Key;
                PicturesManifestEntry entry = pair.Value;
                if (this.IsDrawIndexRelativePath(relativePath)
                    || string.Equals(entry.ContentKind, PicturesContentDrawIndex, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                string decryptedFile = Path.Combine(destRoot, relativePath);
                if (!File.Exists(decryptedFile))
                {
                    continue;
                }

                if (!this.TryComputePicturesFileSha256(decryptedFile, out string hash))
                {
                    continue;
                }

                if (!string.Equals(hash, entry.PlainSha256, StringComparison.OrdinalIgnoreCase))
                {
                    output.Add(relativePath);
                }
            }
        }

        private bool ShouldSkipPicturesDecrypt(PicturesManifest manifest, string relativePath, string destFile)
        {
            if (manifest?.Files == null || !manifest.Files.ContainsKey(relativePath))
            {
                return false;
            }

            if (!File.Exists(destFile))
            {
                return false;
            }

            if (this.IsDrawRelativePath(relativePath)
                && string.Equals(manifest.Files[relativePath].ContentKind, PicturesContentDrawColored, StringComparison.OrdinalIgnoreCase))
            {
                string indexRelative = this.GetDrawIndexRelativePath(relativePath);
                string indexFile = Path.Combine(Path.GetDirectoryName(destFile) ?? string.Empty, ".index", Path.GetFileName(destFile));
                return File.Exists(indexFile);
            }

            return true;
        }

        private void PrunePicturesManifestMissingSources(PicturesManifest manifest, string sourceRoot)
        {
            if (manifest?.Files == null || string.IsNullOrWhiteSpace(sourceRoot))
            {
                return;
            }

            List<string> staleKeys = new List<string>();
            foreach (string relativePath in manifest.Files.Keys)
            {
                string sourceFile = Path.Combine(sourceRoot, relativePath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(sourceFile))
                {
                    staleKeys.Add(relativePath);
                }
            }

            for (int i = 0; i < staleKeys.Count; i++)
            {
                string staleKey = staleKeys[i];
                manifest.Files.Remove(staleKey);
                if (this.IsDrawRelativePath(staleKey))
                {
                    manifest.Files.Remove(this.GetDrawIndexRelativePath(staleKey));
                }
            }
        }

        private void TryAddPicturesManifestEntry(PicturesManifest manifest, string relativePath, string destFile, bool wasEncrypted)
        {
            if (manifest?.Files == null || !File.Exists(destFile))
            {
                return;
            }

            if (!this.TryComputePicturesFileSha256(destFile, out string hash))
            {
                return;
            }

            FileInfo info = new FileInfo(destFile);
            manifest.Files[relativePath] = new PicturesManifestEntry
            {
                PlainSha256 = hash,
                PlainSize = info.Length,
                WasEncrypted = wasEncrypted,
                ContentKind = PicturesContentPhoto
            };
        }

        private void TryAddDrawManifestEntries(
            PicturesManifest manifest,
            string coloredRelativePath,
            string coloredFile,
            string indexRelativePath,
            string indexFile,
            bool wasEncrypted,
            string lutSha256)
        {
            if (manifest?.Files == null)
            {
                return;
            }

            if (!this.TryComputePicturesFileSha256(coloredFile, out string coloredHash)
                || !this.TryComputePicturesFileSha256(indexFile, out string indexHash))
            {
                return;
            }

            FileInfo coloredInfo = new FileInfo(coloredFile);
            FileInfo indexInfo = new FileInfo(indexFile);
            manifest.Files[coloredRelativePath] = new PicturesManifestEntry
            {
                PlainSha256 = coloredHash,
                PlainSize = coloredInfo.Length,
                WasEncrypted = wasEncrypted,
                ContentKind = PicturesContentDrawColored,
                IndexPlainSha256 = indexHash,
                LutSha256 = lutSha256 ?? string.Empty
            };
            manifest.Files[indexRelativePath] = new PicturesManifestEntry
            {
                PlainSha256 = indexHash,
                PlainSize = indexInfo.Length,
                WasEncrypted = wasEncrypted,
                ContentKind = PicturesContentDrawIndex,
                LutSha256 = lutSha256 ?? string.Empty
            };
        }

        private bool TryWriteDrawDecryptedFiles(
            PicturesManifest manifest,
            string destRoot,
            string relativePath,
            string coloredDestFile,
            byte[] indexPngBytes,
            bool wasEncrypted,
            Color32[] lut,
            string lutSha256)
        {
            try
            {
                string indexRelative = this.GetDrawIndexRelativePath(relativePath);
                string indexDestFile = Path.Combine(destRoot, indexRelative.Replace('/', Path.DirectorySeparatorChar));
                string indexDir = Path.GetDirectoryName(indexDestFile);
                if (!string.IsNullOrWhiteSpace(indexDir))
                {
                    Directory.CreateDirectory(indexDir);
                }

                File.WriteAllBytes(indexDestFile, indexPngBytes);

                byte[] coloredPng = DrawColorCodec.IndexPngToColoredPng(indexPngBytes, lut);
                if (coloredPng == null || coloredPng.Length == 0)
                {
                    return false;
                }

                string coloredDir = Path.GetDirectoryName(coloredDestFile);
                if (!string.IsNullOrWhiteSpace(coloredDir))
                {
                    Directory.CreateDirectory(coloredDir);
                }

                File.WriteAllBytes(coloredDestFile, coloredPng);
                this.TryAddDrawManifestEntries(
                    manifest,
                    relativePath,
                    coloredDestFile,
                    indexRelative,
                    indexDestFile,
                    wasEncrypted,
                    lutSha256);
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[Pictures] draw preview failed " + relativePath + ": " + ex.Message);
                return false;
            }
        }

        private bool TryImportChangedFile(
            PicturesManifest manifest,
            string relativePath,
            PicturesManifestEntry entry,
            Color32[] drawLut,
            string drawLutSha256,
            bool hasDrawLut)
        {
            string decryptedFile = Path.Combine(manifest.DecryptedRoot, relativePath);
            string targetFile = Path.Combine(manifest.SourceRoot, relativePath);
            if (!File.Exists(decryptedFile))
            {
                return false;
            }

            if (string.Equals(entry.ContentKind, PicturesContentDrawColored, StringComparison.OrdinalIgnoreCase))
            {
                if (!hasDrawLut)
                {
                    ModLogger.Msg("[Pictures] Draw LUT missing for encrypt: " + relativePath);
                    return false;
                }

                byte[] coloredBytes = File.ReadAllBytes(decryptedFile);
                byte[] indexPng = DrawColorCodec.ColoredPngToIndexPng(coloredBytes, drawLut);
                if (indexPng == null || indexPng.Length == 0)
                {
                    return false;
                }

                string indexRelative = this.GetDrawIndexRelativePath(relativePath);
                string indexFile = Path.Combine(manifest.DecryptedRoot, indexRelative.Replace('/', Path.DirectorySeparatorChar));
                string indexDir = Path.GetDirectoryName(indexFile);
                if (!string.IsNullOrWhiteSpace(indexDir))
                {
                    Directory.CreateDirectory(indexDir);
                }

                File.WriteAllBytes(indexFile, indexPng);
                if (!this.TryEncryptScreenCaptureFile(indexFile, targetFile, entry.WasEncrypted, out _))
                {
                    return false;
                }

                entry.PlainSha256 = this.ComputePicturesSha256Hex(coloredBytes);
                entry.PlainSize = coloredBytes.LongLength;
                entry.IndexPlainSha256 = this.ComputePicturesSha256Hex(indexPng);
                entry.LutSha256 = drawLutSha256 ?? string.Empty;
                manifest.Files[relativePath] = entry;

                if (manifest.Files.TryGetValue(indexRelative, out PicturesManifestEntry indexEntry))
                {
                    indexEntry.PlainSha256 = entry.IndexPlainSha256;
                    indexEntry.PlainSize = indexPng.LongLength;
                    indexEntry.LutSha256 = entry.LutSha256;
                    manifest.Files[indexRelative] = indexEntry;
                }

                return true;
            }

            if (this.IsDrawIndexRelativePath(relativePath))
            {
                if (!this.TryEncryptScreenCaptureFile(decryptedFile, targetFile, entry.WasEncrypted, out byte[] indexBytes))
                {
                    return false;
                }

                entry.PlainSha256 = this.ComputePicturesSha256Hex(indexBytes);
                entry.PlainSize = indexBytes.LongLength;
                manifest.Files[relativePath] = entry;

                if (hasDrawLut)
                {
                    string coloredRelative = this.GetDrawColoredRelativePath(relativePath);
                    if (!string.IsNullOrWhiteSpace(coloredRelative))
                    {
                        string coloredFile = Path.Combine(manifest.DecryptedRoot, coloredRelative.Replace('/', Path.DirectorySeparatorChar));
                        byte[] coloredPng = DrawColorCodec.IndexPngToColoredPng(indexBytes, drawLut);
                        if (coloredPng != null && coloredPng.Length > 0)
                        {
                            File.WriteAllBytes(coloredFile, coloredPng);
                            if (manifest.Files.TryGetValue(coloredRelative, out PicturesManifestEntry coloredEntry))
                            {
                                coloredEntry.PlainSha256 = this.ComputePicturesSha256Hex(coloredPng);
                                coloredEntry.PlainSize = coloredPng.LongLength;
                                coloredEntry.IndexPlainSha256 = entry.PlainSha256;
                                coloredEntry.LutSha256 = drawLutSha256 ?? string.Empty;
                                manifest.Files[coloredRelative] = coloredEntry;
                            }
                        }
                    }
                }

                return true;
            }

            if (!this.TryEncryptScreenCaptureFile(decryptedFile, targetFile, entry.WasEncrypted, out byte[] plainBytes))
            {
                return false;
            }

            entry.PlainSha256 = this.ComputePicturesSha256Hex(plainBytes);
            entry.PlainSize = plainBytes.LongLength;
            manifest.Files[relativePath] = entry;
            return true;
        }

        private bool IsDrawRelativePath(string relativePath)
        {
            return !string.IsNullOrWhiteSpace(relativePath)
                && relativePath.StartsWith("Draw/", StringComparison.OrdinalIgnoreCase)
                && relativePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                && !this.IsDrawIndexRelativePath(relativePath);
        }

        private bool IsDrawIndexRelativePath(string relativePath)
        {
            return !string.IsNullOrWhiteSpace(relativePath)
                && relativePath.StartsWith("Draw/.index/", StringComparison.OrdinalIgnoreCase)
                && relativePath.EndsWith(".png", StringComparison.OrdinalIgnoreCase);
        }

        private string GetDrawIndexRelativePath(string drawColoredRelative)
        {
            if (!this.IsDrawRelativePath(drawColoredRelative))
            {
                return drawColoredRelative;
            }

            string fileName = Path.GetFileName(drawColoredRelative);
            return "Draw/.index/" + fileName;
        }

        private string GetDrawColoredRelativePath(string drawIndexRelative)
        {
            if (!this.IsDrawIndexRelativePath(drawIndexRelative))
            {
                return null;
            }

            string fileName = Path.GetFileName(drawIndexRelative);
            return "Draw/" + fileName;
        }

        private bool TryReadDecryptedPlainFromSource(string sourceFile, out byte[] plain, out bool wasEncrypted)
        {
            plain = null;
            wasEncrypted = false;
            try
            {
                byte[] input = File.ReadAllBytes(sourceFile);
                if (input == null || input.Length == 0)
                {
                    return false;
                }

                if (this.LooksLikeImageBytes(input))
                {
                    plain = input;
                    wasEncrypted = false;
                    return true;
                }

                if (this.TryDecryptGamePhotoBytes(input, out byte[] decrypted) && this.LooksLikeImageBytes(decrypted))
                {
                    plain = decrypted;
                    wasEncrypted = true;
                    return true;
                }

                if (this.TryInvokeGameDecryptBytes(input, out byte[] gameDecrypted) && this.LooksLikeImageBytes(gameDecrypted))
                {
                    plain = gameDecrypted;
                    wasEncrypted = true;
                    return true;
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[Pictures] read failed " + sourceFile + ": " + ex.Message);
            }

            return false;
        }

        private bool TryEncryptScreenCaptureFile(string decryptedFile, string targetFile, bool wasEncrypted, out byte[] plainBytes)
        {
            plainBytes = null;
            try
            {
                plainBytes = File.ReadAllBytes(decryptedFile);
                if (plainBytes == null || plainBytes.Length == 0 || !this.LooksLikeImageBytes(plainBytes))
                {
                    return false;
                }

                byte[] output = wasEncrypted ? this.EncryptGamePhotoBytes(plainBytes) : plainBytes;
                if (output == null || output.Length == 0)
                {
                    return false;
                }

                string targetDir = Path.GetDirectoryName(targetFile);
                if (!string.IsNullOrWhiteSpace(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                File.WriteAllBytes(targetFile, output);
                return true;
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[Pictures] encrypt failed " + decryptedFile + ": " + ex.Message);
                return false;
            }
        }

        private byte[] EncryptGamePhotoBytes(byte[] plain)
        {
            if (plain == null || plain.Length == 0)
            {
                return null;
            }

            if (this.TryInvokeGameEncryptBytes(plain, out byte[] gameEncrypted))
            {
                return gameEncrypted;
            }

            try
            {
                using Aes aes = Aes.Create();
                aes.Key = PicturesDecryptAesKey;
                aes.IV = PicturesDecryptAesIv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                using MemoryStream output = new MemoryStream();
                using CryptoStream cryptoStream = new CryptoStream(output, aes.CreateEncryptor(), CryptoStreamMode.Write);
                cryptoStream.Write(plain, 0, plain.Length);
                cryptoStream.FlushFinalBlock();
                return output.ToArray();
            }
            catch
            {
                return null;
            }
        }

        private bool TryInvokeGameEncryptBytes(byte[] plain, out byte[] encrypted)
        {
            encrypted = null;
            try
            {
                Type encryptUtilType = this.FindLoadedTypeByFullName("Client.BaseService.Utility.File.EncryptUtil");
                if (encryptUtilType == null)
                {
                    return false;
                }

                MethodInfo encryptMethod = encryptUtilType.GetMethod(
                    "EncryptBytes",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(byte[]) },
                    null);
                if (encryptMethod == null)
                {
                    return false;
                }

                object result = encryptMethod.Invoke(null, new object[] { plain });
                if (result is byte[] bytes && bytes.Length > 0)
                {
                    encrypted = bytes;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private PicturesManifest TryLoadPicturesManifest(string destRoot)
        {
            try
            {
                string path = this.ResolvePicturesManifestPath(destRoot);
                if (string.IsNullOrWhiteSpace(path))
                {
                    return null;
                }

                string json;
                using (StreamReader reader = new StreamReader(path, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true), detectEncodingFromByteOrderMarks: true))
                {
                    json = reader.ReadToEnd();
                }
                PicturesManifest manifest = JsonSerializer.Deserialize<PicturesManifest>(json);
                if (manifest?.Files == null)
                {
                    return null;
                }

                this.NormalizePicturesManifest(manifest);
                return manifest;
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[Pictures] manifest load failed: " + ex.Message);
                return null;
            }
        }

        private void TrySavePicturesManifest(PicturesManifest manifest, string destRoot)
        {
            try
            {
                string path = this.GetPicturesManifestPath(destRoot);
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrWhiteSpace(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                this.NormalizePicturesManifest(manifest);
                JsonSerializerOptions options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(manifest, options);
                File.WriteAllText(path, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[Pictures] manifest save failed: " + ex.Message);
            }
        }

        private string GetPicturesManifestPath(string destRoot)
        {
            return Path.Combine(destRoot, PicturesManifestFileName);
        }

        private string ResolvePicturesManifestPath(string destRoot)
        {
            string path = this.GetPicturesManifestPath(destRoot);
            if (File.Exists(path))
            {
                return path;
            }

            string legacyPath = Path.Combine(destRoot, PicturesLegacyManifestFileName);
            return File.Exists(legacyPath) ? legacyPath : null;
        }

        private void NormalizePicturesManifest(PicturesManifest manifest)
        {
            if (manifest?.Files == null)
            {
                return;
            }

            manifest.Version = PicturesManifestVersion;
            foreach (PicturesManifestEntry entry in manifest.Files.Values)
            {
                if (string.IsNullOrWhiteSpace(entry.ContentKind))
                {
                    entry.ContentKind = PicturesContentPhoto;
                }
            }
        }

        private string NormalizePicturesRelativePath(string relativePath)
        {
            return relativePath
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Replace('\\', '/');
        }

        private bool TryComputePicturesFileSha256(string filePath, out string hashHex)
        {
            hashHex = null;
            try
            {
                byte[] bytes = File.ReadAllBytes(filePath);
                hashHex = this.ComputePicturesSha256Hex(bytes);
                return !string.IsNullOrWhiteSpace(hashHex);
            }
            catch
            {
                return false;
            }
        }

        private string ComputePicturesSha256Hex(byte[] bytes)
        {
            if (bytes == null)
            {
                return string.Empty;
            }

            using SHA256 sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(bytes);
            StringBuilder builder = new StringBuilder(hash.Length * 2);
            for (int i = 0; i < hash.Length; i++)
            {
                builder.Append(hash[i].ToString("x2"));
            }

            return builder.ToString();
        }

        private enum PicturesDecryptFileResult
        {
            Failed,
            Decrypted,
            CopiedPlain
        }

        private PicturesDecryptFileResult TryDecryptScreenCaptureFile(string sourceFile, string destFile)
        {
            try
            {
                byte[] input = File.ReadAllBytes(sourceFile);
                if (input == null || input.Length == 0)
                {
                    return PicturesDecryptFileResult.Failed;
                }

                if (this.LooksLikeImageBytes(input))
                {
                    File.WriteAllBytes(destFile, input);
                    return PicturesDecryptFileResult.CopiedPlain;
                }

                if (this.TryDecryptGamePhotoBytes(input, out byte[] decrypted) && this.LooksLikeImageBytes(decrypted))
                {
                    File.WriteAllBytes(destFile, decrypted);
                    return PicturesDecryptFileResult.Decrypted;
                }

                if (this.TryInvokeGameDecryptBytes(input, out byte[] gameDecrypted) && this.LooksLikeImageBytes(gameDecrypted))
                {
                    File.WriteAllBytes(destFile, gameDecrypted);
                    return PicturesDecryptFileResult.Decrypted;
                }
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[Pictures] file failed " + sourceFile + ": " + ex.Message);
            }

            return PicturesDecryptFileResult.Failed;
        }

        private bool TryDecryptGamePhotoBytes(byte[] encrypted, out byte[] plain)
        {
            plain = null;
            if (encrypted == null || encrypted.Length == 0)
            {
                return false;
            }

            try
            {
                using Aes aes = Aes.Create();
                aes.Key = PicturesDecryptAesKey;
                aes.IV = PicturesDecryptAesIv;
                aes.Mode = CipherMode.CBC;
                aes.Padding = PaddingMode.PKCS7;
                using MemoryStream input = new MemoryStream(encrypted);
                using CryptoStream cryptoStream = new CryptoStream(input, aes.CreateDecryptor(), CryptoStreamMode.Read);
                using MemoryStream output = new MemoryStream();
                cryptoStream.CopyTo(output);
                plain = output.ToArray();
                return plain != null && plain.Length > 0;
            }
            catch
            {
                plain = null;
                return false;
            }
        }

        private bool TryInvokeGameDecryptBytes(byte[] encrypted, out byte[] plain)
        {
            plain = null;
            try
            {
                Type encryptUtilType = this.FindLoadedTypeByFullName("Client.BaseService.Utility.File.EncryptUtil");
                if (encryptUtilType == null)
                {
                    return false;
                }

                MethodInfo decryptMethod = encryptUtilType.GetMethod(
                    "DecryptBytes",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new[] { typeof(byte[]) },
                    null);
                if (decryptMethod == null)
                {
                    return false;
                }

                object result = decryptMethod.Invoke(null, new object[] { encrypted });
                if (result is byte[] bytes && bytes.Length > 0)
                {
                    plain = bytes;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private bool LooksLikeImageBytes(byte[] data)
        {
            if (data == null || data.Length < 4)
            {
                return false;
            }

            if (data[0] == 0xFF && data[1] == 0xD8)
            {
                return true;
            }

            return data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47;
        }

        private string TryGetScreenCaptureRootPath()
        {
            try
            {
                Type utilType = this.FindLoadedTypeByFullName("Client.BaseService.Utility.File.ScreenCaptureUtil");
                if (utilType != null)
                {
                    PropertyInfo cachePathProperty = utilType.GetProperty("CACHE_PATH", BindingFlags.Static | BindingFlags.Public);
                    if (cachePathProperty != null)
                    {
                        object value = cachePathProperty.GetValue(null);
                        if (value is string path && !string.IsNullOrWhiteSpace(path))
                        {
                            return path;
                        }
                    }
                }
            }
            catch
            {
            }

            return Path.Combine(Application.persistentDataPath, "ScreenCapture");
        }

        private string GetScreenCaptureDecryptedRootPath(string screenCaptureRoot)
        {
            if (!string.IsNullOrWhiteSpace(screenCaptureRoot))
            {
                string parent = Directory.GetParent(screenCaptureRoot)?.FullName;
                if (!string.IsNullOrWhiteSpace(parent))
                {
                    return Path.Combine(parent, "ScreenCaptureDecrypted");
                }
            }

            return Path.Combine(Application.persistentDataPath, "ScreenCaptureDecrypted");
        }

        private bool TryResolveDrawColorLut(string destRoot, out Color32[] lut, out string lutSha256)
        {
            return DrawColorCodec.TryResolveLut(destRoot, out lut, out lutSha256);
        }

        private IEnumerator CoTryResolveDrawColorLut(string destRoot, Action<bool, Color32[], string> done)
        {
            // The 'drawing_lut' texture may load a few frames after the drawing UI opens, so retry
            // briefly before giving up.
            for (int attempt = 0; attempt < 60; attempt++)
            {
                if (this.TryResolveDrawColorLut(destRoot, out Color32[] lut, out string lutSha256))
                {
                    ModLogger.Msg("[Pictures] Draw LUT ready (" + lut.Length + " colors)");
                    done(true, lut, lutSha256);
                    yield break;
                }

                yield return null;
            }

            done(false, null, null);
        }
    }
}
