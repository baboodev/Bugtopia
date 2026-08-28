using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Bugtopia.Launch
{
    /// <summary>
    /// The Play flow: start the game ourselves, wait until its IL2CPP runtime is up, inject the
    /// bootstrap. See docs/plans/2026-08-27-bepinex-injector.md section 5.3.
    ///
    /// Owning process creation is what makes the design's central property possible — the bootstrap's
    /// configuration travels in the child's environment block, so not one file of ours has to exist
    /// in the game folder, and a crash leaves nothing behind to clean up.
    /// </summary>
    public static class GameSession
    {
        /// <summary>
        /// Finds the Unity player the way BepInEx will: the executable whose name matches a sibling
        /// <c>&lt;name&gt;_Data</c> folder. Deriving it the same way means a mismatch surfaces here,
        /// with a sentence about it, rather than inside BepInEx's path resolution later.
        /// </summary>
        public static string FindGameExe(string gameFolder)
        {
            if (string.IsNullOrWhiteSpace(gameFolder) || !Directory.Exists(gameFolder))
                throw new LaunchException("Game folder does not exist: " + gameFolder);

            string found = null;
            foreach (string dir in Directory.GetDirectories(gameFolder, "*_Data"))
            {
                string name = Path.GetFileName(dir);
                name = name.Substring(0, name.Length - "_Data".Length);
                string exe = Path.Combine(gameFolder, name + ".exe");
                if (!File.Exists(exe))
                    continue;
                if (found != null)
                    throw new LaunchException("Several Unity players in " + gameFolder + ".");
                found = exe;
            }

            if (found == null)
                throw new LaunchException("No Unity player in " + gameFolder + " (expected <name>.exe beside <name>_Data).");
            if (!File.Exists(Path.Combine(gameFolder, "GameAssembly.dll")))
                throw new LaunchException("GameAssembly.dll is missing — this is not an IL2CPP build.");

            return found;
        }

        /// <summary>
        /// The game's Unity version in the three-component form BepInEx's base-libraries URL uses
        /// (<c>2020.3.13</c>), or null.
        ///
        /// Read from UnityPlayer.dll's file version first, and from the version string at the head of
        /// <c>&lt;name&gt;_Data\globalgamemanagers</c> second — the same file BepInEx reads. Either is
        /// enough to build the download link without loading any of BepInEx.
        /// </summary>
        public static string ReadUnityVersion(string gameFolder)
        {
            try
            {
                string player = Path.Combine(gameFolder, "UnityPlayer.dll");
                if (File.Exists(player))
                {
                    var info = FileVersionInfo.GetVersionInfo(player);
                    if (info.FileMajorPart > 0)
                        return $"{info.FileMajorPart}.{info.FileMinorPart}.{info.FileBuildPart}";
                }
            }
            catch (Exception)
            {
            }

            try
            {
                foreach (string dir in Directory.GetDirectories(gameFolder, "*_Data"))
                {
                    string managers = Path.Combine(dir, "globalgamemanagers");
                    if (!File.Exists(managers))
                        continue;

                    byte[] head = new byte[256];
                    using (FileStream stream = File.OpenRead(managers))
                        head = ReadExactly(stream, head);

                    // e.g. "2020.3.13f1"; the zip is named by the part before the release suffix.
                    string text = System.Text.Encoding.ASCII.GetString(head);
                    var match = System.Text.RegularExpressions.Regex.Match(text, @"(\d+\.\d+\.\d+)[fpab]\d+");
                    if (match.Success)
                        return match.Groups[1].Value;
                }
            }
            catch (Exception)
            {
            }

            return null;
        }

        private static byte[] ReadExactly(Stream stream, byte[] buffer)
        {
            int read = 0;
            while (read < buffer.Length)
            {
                int got = stream.Read(buffer, read, buffer.Length - read);
                if (got <= 0)
                    break;
                read += got;
            }
            if (read == buffer.Length)
                return buffer;
            var trimmed = new byte[read];
            Array.Copy(buffer, trimmed, read);
            return trimmed;
        }

        /// <summary>BepInEx's own default source for the Unity base libraries, resolved for this game.</summary>
        public static string UnityLibrariesUrl(string unityVersion)
        {
            return string.IsNullOrEmpty(unityVersion)
                ? null
                : "https://unity.bepinex.dev/libraries/" + unityVersion + ".zip";
        }

        /// <summary>
        /// Starts the game with <c>BUGTOPIA_STORAGE</c> in its environment.
        ///
        /// Note this does not create the process suspended. The design considered it, but the suspend
        /// only pays for itself when something has to happen before the entry point, and nothing does:
        /// there is no pre-init hook worth having, because GameAssembly.dll is loaded dynamically and
        /// intercepting il2cpp_init needs a patch either way. Without that, a suspend followed by an
        /// immediate resume is ceremony, and dropping it removes the whole CreateProcessW /
        /// environment-block P/Invoke path.
        /// </summary>
        public static Process Start(string gameExe, StorageLayout storage)
        {
            if (!File.Exists(gameExe))
                throw new LaunchException("No such executable: " + gameExe);

            var info = new ProcessStartInfo(gameExe)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(gameExe),
            };
            info.Environment["BUGTOPIA_STORAGE"] = storage.Root;

            Process process = Process.Start(info);
            if (process == null)
                throw new LaunchException("The game process did not start.");
            return process;
        }

        /// <summary>
        /// Waits until the game is far enough along to be injected: GameAssembly.dll mapped and a
        /// window belonging to the process. The bootstrap itself waits for the IL2CPP domain, which
        /// cannot be observed from outside the process.
        /// </summary>
        /// <returns>False on timeout, with <paramref name="reason"/> naming what never happened.</returns>
        public static bool WaitUntilReady(Process game, TimeSpan timeout, out string reason,
                                          Action<string> log = null)
        {
            log ??= delegate { };
            DateTime started = DateTime.UtcNow;
            DateTime deadline = started + timeout;
            DateTime nextHeartbeat = started + TimeSpan.FromSeconds(10);
            bool sawGameAssembly = false;
            bool sawWindow = false;
            string moduleFailure = null;

            while (DateTime.UtcNow < deadline)
            {
                // Refresh FIRST, every time. Process caches both the module list and the window
                // handle until this is called, so re-reading them without it returns the same
                // snapshot forever — and a snapshot taken before GameAssembly.dll is mapped can
                // never become true. That is a wait that cannot succeed and does not say why.
                game.Refresh();

                if (game.HasExited)
                {
                    reason = $"the game exited on its own after {(DateTime.UtcNow - started).TotalSeconds:F0}s " +
                             $"(exit code {game.ExitCode})";
                    return false;
                }

                if (!sawGameAssembly && HasModule(game, "GameAssembly.dll", ref moduleFailure))
                {
                    sawGameAssembly = true;
                    log("GameAssembly.dll is mapped.");
                }

                if (!sawWindow && game.MainWindowHandle != IntPtr.Zero)
                {
                    sawWindow = true;
                    log("Game window is up.");
                }

                // The window is the signal that matters — the bootstrap needs it for the main-thread
                // rendezvous, and it waits for the IL2CPP domain itself, which cannot be observed
                // from out here. GameAssembly is corroboration, and is skipped when the module list
                // is unreadable rather than blocking a launch that would have worked.
                if (sawWindow && (sawGameAssembly || moduleFailure != null))
                {
                    reason = null;
                    return true;
                }

                if (DateTime.UtcNow >= nextHeartbeat)
                {
                    log($"still waiting ({(DateTime.UtcNow - started).TotalSeconds:F0}s): " +
                        $"GameAssembly={(sawGameAssembly ? "yes" : "no")}, window={(sawWindow ? "yes" : "no")}" +
                        (moduleFailure != null ? ", modules unreadable: " + moduleFailure : ""));
                    nextHeartbeat = DateTime.UtcNow + TimeSpan.FromSeconds(10);
                }

                Thread.Sleep(200);
            }

            reason = $"timed out after {timeout.TotalSeconds:F0}s with " +
                     $"GameAssembly={(sawGameAssembly ? "yes" : "no")}, window={(sawWindow ? "yes" : "no")}" +
                     (moduleFailure != null ? " (modules unreadable: " + moduleFailure + ")" : "");
            return false;
        }

        /// <summary>
        /// Looks for a module, recording *why* the list could not be read rather than reporting the
        /// same "no" for "absent" and "could not tell" — enumeration legitimately throws while a
        /// process is still initialising, and can keep throwing for a protected one.
        /// </summary>
        private static bool HasModule(Process process, string moduleName, ref string failure)
        {
            try
            {
                foreach (ProcessModule module in process.Modules)
                {
                    if (string.Equals(module.ModuleName, moduleName, StringComparison.OrdinalIgnoreCase))
                    {
                        failure = null;
                        return true;
                    }
                }
                failure = null;
            }
            catch (Exception ex)
            {
                failure = ex.GetType().Name + ": " + ex.Message;
            }
            return false;
        }
    }

    public sealed class LaunchException : Exception
    {
        public LaunchException(string message) : base(message) { }
    }
}
