using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEngine;

namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        private const string PhotoWorkFileName = "photo.png";
        private const string PhotoWorkIdFileName = "photo.id";
        private const int PhotoUploadWidth = 1564;
        private const int PhotoUploadHeight = 880;
        private const int PhotoRawWidth = 1920;
        private const int PhotoRawHeight = 1080;

        // Same sizes SaveLocalTextureResize3 writes. 1920 is included only when the edited
        // image is already that size — a 1564 download has no raw file and must not be upscaled.
        private static readonly int[] PhotoCacheWidths = { 1920, 1564, 512, 256 };
        private static readonly int[] PhotoCacheHeights = { 1080, 880, 288, 144 };
        private const string PhotoDetailPanelFullName = "XDTGame.UI.Panel.PhotoDetailPanel";
        private const string PhotoSystemFullName = "XDTGameSystem.GameplaySystem.Photo.PhotoSystem";

        private static readonly UTF8Encoding PhotoIdFileEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

        private string GetPhotoWorkFilePath()
        {
            string root = this.GetScreenCaptureDecryptedRootPath();
            return Path.Combine(root, PhotoWorkFileName);
        }

        private string GetPhotoWorkIdPath()
        {
            string root = this.GetScreenCaptureDecryptedRootPath();
            return Path.Combine(root, PhotoWorkIdFileName);
        }

        private string GetPhotoCachePath(string photoId, int width, int height)
        {
            return Path.Combine(this.TryGetScreenCaptureRootPath(), "Photo", photoId + "_" + width + "_" + height + ".jpg");
        }

        private static string FormatPhotoWorkId(string photoId, int width, int height)
        {
            return photoId + "\n" + width + "x" + height;
        }

        // A photo.id from the first version is the id alone and means 1564×880.
        private static bool TryParsePhotoWorkId(string text, out string photoId, out int width, out int height)
        {
            photoId = null;
            width = PhotoUploadWidth;
            height = PhotoUploadHeight;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string[] lines = text.Replace("\r", string.Empty).Split('\n');
            photoId = lines[0].Trim();
            if (string.IsNullOrEmpty(photoId))
            {
                return false;
            }

            if (lines.Length < 2 || string.IsNullOrWhiteSpace(lines[1]))
            {
                return true;
            }

            string[] parts = lines[1].Trim().Split('x');
            if (parts.Length != 2 || !int.TryParse(parts[0], out width) || !int.TryParse(parts[1], out height)
                || width <= 0 || height <= 0)
            {
                photoId = null;
                return false;
            }

            return true;
        }

        private bool TryPickPhotoExtractSource(string photoId, out string cacheFile, out int width, out int height)
        {
            string raw = this.GetPhotoCachePath(photoId, PhotoRawWidth, PhotoRawHeight);
            if (File.Exists(raw))
            {
                cacheFile = raw;
                width = PhotoRawWidth;
                height = PhotoRawHeight;
                return true;
            }

            string upload = this.GetPhotoCachePath(photoId, PhotoUploadWidth, PhotoUploadHeight);
            if (File.Exists(upload))
            {
                cacheFile = upload;
                width = PhotoUploadWidth;
                height = PhotoUploadHeight;
                return true;
            }

            cacheFile = null;
            width = 0;
            height = 0;
            return false;
        }

        // Sizes larger than the source are skipped by the caller. Matching size is encoded as-is.
        private static byte[] EncodePhotoJpeg(Texture2D source, int width, int height)
        {
            if (source.width == width && source.height == height)
            {
                return source.EncodeToJPG();
            }

            source.filterMode = FilterMode.Bilinear;
            RenderTexture rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            Texture2D scaled = null;
            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;
                scaled = new Texture2D(width, height, TextureFormat.RGBA32, false);
                scaled.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                scaled.Apply();
                return scaled.EncodeToJPG();
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
                if (scaled != null)
                {
                    UnityEngine.Object.Destroy(scaled);
                }
            }
        }

        internal void PhotoRevealWorkFile()
        {
            string path = this.GetPhotoWorkFilePath();
            if (!File.Exists(path))
            {
                this.picturesLastStatus = "Photo: Extract first";
                return;
            }

            // explorer.exe returns immediately and often exits 1 even when the window opened.
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = "/select,\"" + path + "\"",
                    UseShellExecute = true,
                });
            }
            catch (Exception ex)
            {
                this.picturesLastStatus = "Photo: Extract first";
                ModLogger.Msg("[PhotoExtract] reveal failed: " + ex.Message);
                return;
            }

            this.picturesLastStatus = "Photo: opened " + path;
        }

        private unsafe bool TryReadOpenPhotoId(out string photoId, out string status)
        {
            photoId = null;
            status = "Photo: enter the world first";
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null || auraMonoObjectGetClass == null
                || auraMonoClassGetType == null || auraMonoTypeGetObject == null
                || this.auraMonoRootDomain == IntPtr.Zero)
            {
                return false;
            }

            IntPtr panelClass = this.FindAuraMonoClassByFullName(PhotoDetailPanelFullName);
            if (panelClass == IntPtr.Zero)
            {
                status = "Photo: detail panel type missing";
                return false;
            }

            if (!this.ModTryResolveAuraMonoUIManager(out IntPtr uiManagerObj, out IntPtr uiManagerClass))
            {
                return false;
            }

            IntPtr monoType = auraMonoClassGetType(panelClass);
            IntPtr typeObj = monoType != IntPtr.Zero ? auraMonoTypeGetObject(this.auraMonoRootDomain, monoType) : IntPtr.Zero;
            if (typeObj == IntPtr.Zero)
            {
                status = "Photo: detail panel type missing";
                return false;
            }

            IntPtr getView = this.FindAuraMonoMethodOnHierarchy(uiManagerClass, "GetView", 1);
            if (getView == IntPtr.Zero)
            {
                status = "Photo: GetView missing";
                return false;
            }

            IntPtr exc = IntPtr.Zero;
            IntPtr* getArgs = stackalloc IntPtr[1];
            getArgs[0] = typeObj;
            IntPtr panelObj = auraMonoRuntimeInvoke(getView, uiManagerObj, (IntPtr)getArgs, ref exc);
            if (exc != IntPtr.Zero)
            {
                status = "Photo: GetView failed";
                return false;
            }

            if (panelObj == IntPtr.Zero)
            {
                status = "Photo: open a photo in the album";
                return false;
            }

            uint panelPin = AuraMonoPinNew(panelObj);
            try
            {
                if (!this.TryGetMonoStringMember(panelObj, "_photoId", out photoId) || string.IsNullOrEmpty(photoId))
                {
                    photoId = null;
                    status = "Photo: open photo has no id";
                    return false;
                }

                status = "ok";
                return true;
            }
            finally
            {
                if (panelPin != 0U)
                {
                    AuraMonoPinFree(panelPin);
                }
            }
        }

        internal void PhotoExtractOpenPhoto()
        {
            if (!this.TryReadOpenPhotoId(out string photoId, out string status))
            {
                this.picturesLastStatus = status;
                ModLogger.Msg("[PhotoExtract] extract: " + status);
                return;
            }

            if (!this.TryPickPhotoExtractSource(photoId, out string cacheFile, out int sourceWidth, out int sourceHeight))
            {
                this.picturesLastStatus = "Photo: no 1920x1080 or 1564x880 cache file";
                return;
            }

            if (!this.TryReadDecryptedPlainFromSource(cacheFile, out byte[] jpeg, out _))
            {
                this.picturesLastStatus = "Photo: decrypt failed";
                return;
            }

            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                if (!texture.LoadImage(jpeg, false))
                {
                    this.picturesLastStatus = "Photo: decrypt failed";
                    return;
                }

                if (texture.width != sourceWidth || texture.height != sourceHeight)
                {
                    this.picturesLastStatus = "Photo: cache image is " + texture.width + "x" + texture.height
                        + ", expected " + sourceWidth + "x" + sourceHeight;
                    return;
                }

                byte[] png = texture.EncodeToPNG();
                if (png == null || png.Length == 0)
                {
                    this.picturesLastStatus = "Photo: encode failed";
                    return;
                }

                string destRoot = this.GetScreenCaptureDecryptedRootPath();
                Directory.CreateDirectory(destRoot);
                File.WriteAllBytes(this.GetPhotoWorkFilePath(), png);
                File.WriteAllText(this.GetPhotoWorkIdPath(), FormatPhotoWorkId(photoId, sourceWidth, sourceHeight), PhotoIdFileEncoding);
                this.picturesLastStatus = "Photo: extracted " + photoId + " " + sourceWidth + "x" + sourceHeight + " -> photo.png";
                ModLogger.Msg("[PhotoExtract] " + this.picturesLastStatus);
            }
            catch (Exception ex)
            {
                this.picturesLastStatus = "Photo: extract failed";
                ModLogger.Msg("[PhotoExtract] extract failed: " + ex.Message);
            }
            finally
            {
                UnityEngine.Object.Destroy(texture);
            }
        }

        internal void PhotoUploadWorkFile()
        {
            if (this.picturesTaskCoroutine != null)
            {
                this.picturesLastStatus = "Photo: Pictures task still running";
                return;
            }

            if (!this.TryReadOpenPhotoId(out string photoId, out string status))
            {
                this.picturesLastStatus = status;
                return;
            }

            string idPath = this.GetPhotoWorkIdPath();
            string workPath = this.GetPhotoWorkFilePath();
            if (!File.Exists(idPath) || !File.Exists(workPath))
            {
                this.picturesLastStatus = "Photo: no photo.png — Extract first";
                return;
            }

            string savedText;
            try
            {
                savedText = File.ReadAllText(idPath, PhotoIdFileEncoding);
            }
            catch (Exception ex)
            {
                this.picturesLastStatus = "Photo: no photo.png — Extract first";
                ModLogger.Msg("[PhotoExtract] id read failed: " + ex.Message);
                return;
            }

            if (!TryParsePhotoWorkId(savedText, out string savedId, out int sourceWidth, out int sourceHeight)
                || !string.Equals(savedId, photoId, StringComparison.Ordinal))
            {
                this.picturesLastStatus = "Photo: photo.png is not the open photo — Extract it first";
                return;
            }

            if (!this.TryReadPhotoUploadAllowed(photoId, out status))
            {
                this.picturesLastStatus = status;
                ModLogger.Msg("[PhotoExtract] " + status);
                return;
            }

            string sizeStatus = "Photo: photo.png must be " + sourceWidth + "x" + sourceHeight;
            List<string> cachePaths = new List<string>();
            List<byte[]> cacheBytes = new List<byte[]>();
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                byte[] png = File.ReadAllBytes(workPath);
                if (!texture.LoadImage(png, false)
                    || texture.width != sourceWidth || texture.height != sourceHeight)
                {
                    this.picturesLastStatus = sizeStatus;
                    return;
                }

                for (int i = 0; i < PhotoCacheWidths.Length; i++)
                {
                    int width = PhotoCacheWidths[i];
                    int height = PhotoCacheHeights[i];
                    if (width > sourceWidth || height > sourceHeight)
                    {
                        continue;
                    }

                    byte[] jpeg = EncodePhotoJpeg(texture, width, height);
                    if (jpeg == null || jpeg.Length == 0)
                    {
                        this.picturesLastStatus = "Photo: encode failed";
                        return;
                    }

                    byte[] encrypted = this.EncryptGamePhotoBytes(jpeg);
                    if (encrypted == null || encrypted.Length == 0)
                    {
                        this.picturesLastStatus = "Photo: encode failed";
                        return;
                    }

                    cachePaths.Add(this.GetPhotoCachePath(photoId, width, height));
                    cacheBytes.Add(encrypted);
                }
            }
            catch (Exception ex)
            {
                this.picturesLastStatus = sizeStatus;
                ModLogger.Msg("[PhotoExtract] png read failed: " + ex.Message);
                return;
            }
            finally
            {
                UnityEngine.Object.Destroy(texture);
            }

            if (cachePaths.Count == 0)
            {
                this.picturesLastStatus = "Photo: encode failed";
                return;
            }

            if (!this.TryReadPhotoUploadAllowed(photoId, out status))
            {
                this.picturesLastStatus = status;
                ModLogger.Msg("[PhotoExtract] " + status);
                return;
            }

            try
            {
                for (int i = 0; i < cachePaths.Count; i++)
                {
                    string cacheDir = Path.GetDirectoryName(cachePaths[i]);
                    if (!string.IsNullOrEmpty(cacheDir))
                    {
                        Directory.CreateDirectory(cacheDir);
                    }

                    File.WriteAllBytes(cachePaths[i], cacheBytes[i]);
                }
            }
            catch (Exception ex)
            {
                this.picturesLastStatus = "Photo: encode failed";
                ModLogger.Msg("[PhotoExtract] cache write failed: " + ex.Message);
                return;
            }

            if (!this.TryInvokePhotoSetFavourite(photoId, out status))
            {
                this.picturesLastStatus = status;
                ModLogger.Msg("[PhotoExtract] " + status);
                return;
            }

            this.picturesLastStatus = "Photo: upload started for " + photoId + " (game like path)";
            ModLogger.Msg("[PhotoExtract] " + this.picturesLastStatus);
        }

        // Refuses before any cache write. SetFavourite on an already-liked photo sends a delete.
        private unsafe bool TryReadPhotoUploadAllowed(string photoId, out string status)
        {
            status = "Photo: enter the world first";
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null || auraMonoObjectGetClass == null
                || auraMonoStringNew == null || this.auraMonoRootDomain == IntPtr.Zero)
            {
                return false;
            }

            // Namespace is XDTGameSystem.*, image is XDTLevelAndEntity. The hint lookup
            // stops at the first loaded XDTGameSystem image and never sees this class.
            IntPtr photoSystemClass = this.FindAuraMonoClassAnySpelling(PhotoSystemFullName);
            if (photoSystemClass == IntPtr.Zero)
            {
                status = "Photo: photo system missing";
                return false;
            }

            IntPtr photoSystem = this.TryGetAuraMonoDataModuleInstance(photoSystemClass);
            if (photoSystem == IntPtr.Zero)
            {
                status = "Photo: photo system missing";
                return false;
            }

            uint systemPin = AuraMonoPinNew(photoSystem);
            uint photosPin = 0U;
            uint keyPin = 0U;
            uint entryPin = 0U;
            try
            {
                if (this.TryGetMonoBoolMember(photoSystem, "albumIsUploading", out bool uploading) && uploading)
                {
                    status = "Photo: album upload already running";
                    return false;
                }

                if (!this.TryGetMonoObjectMember(photoSystem, "_photos", out IntPtr photos) || photos == IntPtr.Zero)
                {
                    status = "Photo: photo is not in the album";
                    return false;
                }

                photosPin = AuraMonoPinNew(photos);
                IntPtr photosClass = auraMonoObjectGetClass(photos);
                IntPtr containsKey = this.FindAuraMonoMethodOnHierarchy(photosClass, "ContainsKey", 1);
                IntPtr getItem = this.FindAuraMonoMethodOnHierarchy(photosClass, "get_Item", 1);
                if (containsKey == IntPtr.Zero || getItem == IntPtr.Zero)
                {
                    status = "Photo: photo is not in the album";
                    return false;
                }

                IntPtr key = auraMonoStringNew(this.auraMonoRootDomain, photoId);
                if (key == IntPtr.Zero)
                {
                    status = "Photo: photo is not in the album";
                    return false;
                }

                keyPin = AuraMonoPinNew(key);

                IntPtr exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = key;
                IntPtr containsBoxed = auraMonoRuntimeInvoke(containsKey, photos, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero || !this.TryUnboxMonoBoolean(containsBoxed, out bool contains) || !contains)
                {
                    status = "Photo: photo is not in the album";
                    return false;
                }

                exc = IntPtr.Zero;
                IntPtr entry = auraMonoRuntimeInvoke(getItem, photos, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero || entry == IntPtr.Zero)
                {
                    status = "Photo: photo is not in the album";
                    return false;
                }

                entryPin = AuraMonoPinNew(entry);
                if (this.TryGetMonoBoolMember(entry, "isFavourite", out bool favourite) && favourite)
                {
                    status = "Photo: already liked — unlike it in the album first (like would delete it)";
                    return false;
                }

                IntPtr limitGetter = this.FindAuraMonoMethodOnHierarchy(photoSystemClass, "get_IsLikedPhotoLimit", 0);
                if (limitGetter != IntPtr.Zero)
                {
                    exc = IntPtr.Zero;
                    IntPtr limitBoxed = auraMonoRuntimeInvoke(limitGetter, photoSystem, IntPtr.Zero, ref exc);
                    if (exc == IntPtr.Zero && this.TryUnboxMonoBoolean(limitBoxed, out bool limited) && limited)
                    {
                        status = "Photo: liked photo limit reached";
                        return false;
                    }
                }

                status = "ok";
                return true;
            }
            finally
            {
                if (entryPin != 0U)
                {
                    AuraMonoPinFree(entryPin);
                }

                if (keyPin != 0U)
                {
                    AuraMonoPinFree(keyPin);
                }

                if (photosPin != 0U)
                {
                    AuraMonoPinFree(photosPin);
                }

                if (systemPin != 0U)
                {
                    AuraMonoPinFree(systemPin);
                }
            }
        }

        private unsafe bool TryInvokePhotoSetFavourite(string photoId, out string status)
        {
            status = "Photo: enter the world first";
            if (!this.EnsureAuraMonoApiReady() || !this.AttachAuraMonoThread()
                || auraMonoRuntimeInvoke == null || auraMonoStringNew == null
                || this.auraMonoRootDomain == IntPtr.Zero)
            {
                return false;
            }

            IntPtr photoSystemClass = this.FindAuraMonoClassAnySpelling(PhotoSystemFullName);
            IntPtr photoSystem = photoSystemClass != IntPtr.Zero
                ? this.TryGetAuraMonoDataModuleInstance(photoSystemClass)
                : IntPtr.Zero;
            if (photoSystem == IntPtr.Zero)
            {
                status = "Photo: photo system missing";
                return false;
            }

            uint systemPin = AuraMonoPinNew(photoSystem);
            uint keyPin = 0U;
            try
            {
                IntPtr setFavourite = this.FindAuraMonoMethodOnHierarchy(photoSystemClass, "SetFavourite", 1);
                if (setFavourite == IntPtr.Zero)
                {
                    status = "Photo: SetFavourite missing";
                    return false;
                }

                IntPtr key = auraMonoStringNew(this.auraMonoRootDomain, photoId);
                if (key == IntPtr.Zero)
                {
                    status = "Photo: SetFavourite missing";
                    return false;
                }

                keyPin = AuraMonoPinNew(key);

                IntPtr exc = IntPtr.Zero;
                IntPtr* args = stackalloc IntPtr[1];
                args[0] = key;
                auraMonoRuntimeInvoke(setFavourite, photoSystem, (IntPtr)args, ref exc);
                if (exc != IntPtr.Zero)
                {
                    status = "Photo: SetFavourite failed";
                    return false;
                }

                status = "ok";
                return true;
            }
            finally
            {
                if (keyPin != 0U)
                {
                    AuraMonoPinFree(keyPin);
                }

                if (systemPin != 0U)
                {
                    AuraMonoPinFree(systemPin);
                }
            }
        }
    }
}
