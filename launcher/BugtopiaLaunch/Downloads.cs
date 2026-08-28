using System;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

namespace Bugtopia.Launch
{
    /// <summary>
    /// Fetching the two things the launcher does not carry: the BepInEx archive and the Unity base
    /// libraries.
    ///
    /// <b>This is a compile-time capability, not a runtime setting.</b> Build without
    /// <c>BUGTOPIA_ONLINE</c> and every line that touches the network is excluded from the assembly —
    /// so an offline build cannot reach out even by accident, and does not link the HTTP stack it
    /// would need to. A runtime flag could not promise either of those things.
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
        public static async Task DownloadAsync(string url, string destination, Action<string> log = null,
                                               IProgress<int> progress = null)
        {
            log ??= delegate { };

#if BUGTOPIA_ONLINE
            log("Downloading " + url);
            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            string partial = destination + ".part";

            try
            {
                using var http = new System.Net.Http.HttpClient();
                http.Timeout = TimeSpan.FromMinutes(10);
                http.DefaultRequestHeaders.UserAgent.ParseAdd("Bugtopia-Launcher");

                using System.Net.Http.HttpResponseMessage response =
                    await http.GetAsync(url, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
                if (!response.IsSuccessStatusCode)
                    throw new DownloadException($"{url} returned {(int)response.StatusCode} {response.ReasonPhrase}.");

                long? total = response.Content.Headers.ContentLength;
                using (Stream source = await response.Content.ReadAsStreamAsync())
                using (var target = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    var buffer = new byte[81920];
                    long done = 0;
                    int lastPercent = -1;
                    int read;

                    while ((read = await source.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await target.WriteAsync(buffer, 0, read);
                        done += read;

                        if (total.HasValue && total.Value > 0)
                        {
                            int percent = (int)(done * 100 / total.Value);
                            if (percent != lastPercent)
                            {
                                lastPercent = percent;
                                progress?.Report(percent);
                            }
                        }
                    }
                }

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
            await Task.CompletedTask;
            throw new DownloadException(
                "This build does not download anything. Fetch the file yourself and point the " +
                "launcher at it:\n" + url);
#endif
        }

        /// <summary>
        /// Downloads the pinned BepInEx archive and unpacks it into <paramref name="targetFolder"/>,
        /// which then serves as the source folder for <see cref="Payload.Prepare"/>.
        /// </summary>
        public static async Task<string> FetchBepInExAsync(string targetFolder, Action<string> log = null,
                                                           IProgress<int> progress = null)
        {
            log ??= delegate { };
            string zip = Path.Combine(Path.GetTempPath(), "bugtopia-bepinex-" + BepInExVersion + ".zip");

            await DownloadAsync(BepInExUrl, zip, log, progress);

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
        public static async Task FetchUnityLibrariesAsync(string unityVersion, StorageLayout storage,
                                                          Action<string> log = null, IProgress<int> progress = null)
        {
            string url = UnityLibrariesUrl(unityVersion);
            if (url == null)
                throw new DownloadException("The game's Unity version could not be determined.");

            Directory.CreateDirectory(storage.UnityLibs);
            await DownloadAsync(url, Path.Combine(storage.UnityLibs, unityVersion + ".zip"), log, progress);
        }
    }

    public sealed class DownloadException : Exception
    {
        public DownloadException(string message) : base(message) { }
    }
}
