using System;
using System.Collections.Generic;
using System.IO;

namespace Bugtopia.Launch
{
    /// <summary>
    /// Fetching the three things the launcher does not carry: the BepInEx archive, the Unity base
    /// libraries, and (in an online build) the mod itself.
    ///
    /// <b>This is a compile-time capability, not a runtime setting.</b> Build without
    /// <c>BUGTOPIA_ONLINE</c> and every line that touches the network is excluded from the assembly —
    /// so an offline build cannot reach out even by accident, and does not link the stack it would
    /// need to. A runtime flag could not promise either of those things.
    ///
    /// <see cref="Enabled"/> is a constant, so the UI can hide what an offline build cannot do and
    /// the branch folds away at compile time.
    /// </summary>
    public static class Downloads
    {
#if BUGTOPIA_ONLINE
        public const bool Enabled = true;
#else
        public const bool Enabled = false;
#endif

        /// <summary>
        /// Whether the mod itself is fetched from its releases rather than carried inside this exe.
        /// Tied to the same switch: an offline build has to carry everything it installs, and an
        /// online one has no reason to ship a plugin it can fetch a newer copy of.
        /// </summary>
        public const bool PluginFromGitHub = Enabled;

        /// <summary>
        /// The pinned BepInEx build. Deliberately a specific build rather than "latest": the interop
        /// generator reflects into <c>Il2CppInteropManager</c>'s private members, so a silent bump is
        /// a silent break. Moving this means moving the expected version stamp with it.
        /// </summary>
        public const string BepInExVersion = "6.0.0-be.785";

        public const string BepInExUrl =
            "https://builds.bepinex.dev/projects/bepinex_be/785/" +
            "BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.785%2B6abdba4.zip";

        /// <summary>BepInEx's own default source for the base libraries, resolved for a Unity version.</summary>
        public static string UnityLibrariesUrl(string unityVersion) =>
            string.IsNullOrEmpty(unityVersion) ? null : "https://unity.bepinex.dev/libraries/" + unityVersion + ".zip";

        /// <summary>
        /// Downloads a file to <paramref name="destination"/>, reporting progress as whole percent.
        /// </summary>
        /// <exception cref="DownloadException">Any failure, including "this build cannot download".</exception>
        public static void Download(string url, string destination, Action<string> log = null,
                                    Action<int> progress = null)
        {
            log ??= delegate { };

#if BUGTOPIA_ONLINE
            log("Downloading " + url);
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            string partial = destination + ".part";

            try
            {
                int status;
                int lastPercent = -1;

                using (var target = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    status = WinHttp.Get(url, null, target, (done, total) =>
                    {
                        if (progress == null || total <= 0)
                            return;

                        int percent = (int)(done * 100 / total);
                        if (percent != lastPercent)
                        {
                            lastPercent = percent;
                            progress(percent);
                        }
                    });
                }

                if (status != 200)
                    throw new DownloadException(url + " returned HTTP " + status + ".");

                // Only now replace the destination, so an interrupted download never leaves a
                // half-written archive that looks complete.
                if (File.Exists(destination))
                    File.Delete(destination);
                File.Move(partial, destination);
                log("Downloaded " + Path.GetFileName(destination));
            }
            catch (DownloadException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new DownloadException("Download failed: " + ex.Message);
            }
            finally
            {
                if (File.Exists(partial))
                {
                    try
                    {
                        File.Delete(partial);
                    }
                    catch (IOException)
                    {
                    }
                }
            }
#else
            throw new DownloadException(
                "This build does not download anything. Fetch the file yourself and point the " +
                "launcher at it:\n" + url);
#endif
        }

        /// <summary>
        /// Reads a URL into memory. Used for the one response small enough to want in one piece.
        /// </summary>
        public static byte[] Fetch(string url, IEnumerable<KeyValuePair<string, string>> headers,
                                   out int status)
        {
#if BUGTOPIA_ONLINE
            using var buffer = new MemoryStream();
            status = WinHttp.Get(url, headers, buffer);
            return buffer.ToArray();
#else
            status = 0;
            throw new DownloadException("This build does not download anything.");
#endif
        }

        /// <summary>
        /// Downloads the pinned BepInEx archive and unpacks it into <paramref name="targetFolder"/>,
        /// which then serves as the source folder for <see cref="Payload.Prepare"/>.
        /// </summary>
        public static string FetchBepInEx(string targetFolder, Action<string> log = null,
                                          Action<int> progress = null)
        {
            log ??= delegate { };
            string zip = Path.Combine(Path.GetTempPath(), "bugtopia-bepinex-" + BepInExVersion + ".zip");

            Download(BepInExUrl, zip, log, progress);
            Payload.UnpackArchive(zip, targetFolder, log);

            try
            {
                File.Delete(zip);
            }
            catch (IOException)
            {
            }

            return targetFolder;
        }

        /// <summary>
        /// Downloads the Unity base libraries straight into <c>unity-libs</c>. No unpacking: BepInEx
        /// uses a zip already sitting there whose name matches the URL it would have fetched, so the
        /// file alone is the whole offline path.
        /// </summary>
        public static void FetchUnityLibraries(string unityVersion, StorageLayout storage,
                                               Action<string> log = null, Action<int> progress = null)
        {
            string url = UnityLibrariesUrl(unityVersion);
            if (url == null)
                throw new DownloadException("The game's Unity version could not be determined.");

            Directory.CreateDirectory(storage.UnityLibs);
            Download(url, Path.Combine(storage.UnityLibs, unityVersion + ".zip"), log, progress);
        }
    }

    public sealed class DownloadException : Exception
    {
        public DownloadException(string message) : base(message) { }
    }
}
