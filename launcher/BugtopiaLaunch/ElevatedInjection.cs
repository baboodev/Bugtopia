using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Bugtopia.Launch
{
    /// <summary>
    /// The injection, done by a copy of the launcher running as administrator - for a game that runs
    /// as administrator itself, which an ordinary process cannot open. The TapTap CN client starts the
    /// game that way, although neither its own executable nor xdt.exe asks for it in its manifest.
    ///
    /// Only this last step is elevated. Everything before it - the storage tree, the interop
    /// generation, asking the client to start the game - stays in the launcher as the user started it,
    /// so none of the files it writes become the administrator's, and the client is not started with
    /// rights it did not ask for.
    ///
    /// The helper is started through the shell's "runas" verb, which is what brings up the UAC prompt;
    /// output cannot be redirected through that, so it reports through a log file the launcher reads as
    /// it grows, and is told to stop through a file the launcher creates. Its exit code is the verdict.
    /// </summary>
    public static class ElevatedInjection
    {
        /// <summary>The first argument that makes the launcher exe this helper instead of a window.</summary>
        public const string Verb = "inject";

        public const int ResultInjected = 0;
        public const int ResultFailed = 1;
        public const int ResultStopped = 2;

        private const int ErrorCancelled = 1223;

        public static string LogFile(StorageLayout storage) => Path.Combine(storage.Bin, "inject_elevated.log");

        public static string StopFile(StorageLayout storage) => Path.Combine(storage.Bin, "inject_elevated.stop");

        /// <summary>True when this process already has administrator rights, and needs no helper.</summary>
        public static bool IsElevated => Environment.IsPrivilegedProcess;

        // ---- the launcher's side -------------------------------------------------

        /// <summary>
        /// Starts the helper as administrator, which is when Windows asks. Returns once it is running:
        /// it waits for the game itself.
        /// </summary>
        /// <exception cref="LaunchException">The prompt was refused, or the helper could not start.</exception>
        public static Process Start(string gameExe, StorageLayout storage)
        {
            string log = LogFile(storage), stop = StopFile(storage);
            Directory.CreateDirectory(storage.Bin);
            TryDelete(log);
            TryDelete(stop);

            var info = new ProcessStartInfo(Environment.ProcessPath)
            {
                UseShellExecute = true,
                Verb = "runas",
                Arguments = Verb + " --exe " + Quote(gameExe) + " --dll " + Quote(storage.InjectDll) +
                            " --log " + Quote(log) + " --stop " + Quote(stop),
            };

            try
            {
                return Process.Start(info) ?? throw new LaunchException("The elevated helper did not start.");
            }
            catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
            {
                throw new LaunchException(
                    "Administrator rights were refused. The game runs as administrator, and only a " +
                    "process with the same rights can load the mod into it.");
            }
            catch (Win32Exception ex)
            {
                throw new LaunchException("Could not start the elevated helper: " + ex.Message);
            }
        }

        /// <summary>Asks a running helper to give up waiting. It notices within half a second.</summary>
        public static void RequestStop(StorageLayout storage)
        {
            try
            {
                File.WriteAllText(StopFile(storage), "stop");
            }
            catch (Exception)
            {
            }
        }

        /// <summary>Leaves the stop request behind for nobody: the next helper would read it at once.</summary>
        public static void Cleanup(StorageLayout storage) => TryDelete(StopFile(storage));

        /// <summary>
        /// Reads whatever the helper has logged since <paramref name="position"/> and hands each complete
        /// line on. A line still being written is left for the next call.
        /// </summary>
        public static void ForwardLog(StorageLayout storage, ref long position, Action<string> log)
        {
            try
            {
                using var stream = new FileStream(LogFile(storage), FileMode.Open, FileAccess.Read,
                                                  FileShare.ReadWrite | FileShare.Delete);
                if (stream.Length <= position)
                    return;

                stream.Seek(position, SeekOrigin.Begin);
                var buffer = new byte[stream.Length - position];
                int read = stream.Read(buffer, 0, buffer.Length);
                string text = System.Text.Encoding.UTF8.GetString(buffer, 0, read);

                int end = text.LastIndexOf('\n');
                if (end < 0)
                    return;

                foreach (string line in text.Substring(0, end).Split('\n'))
                    log("  [admin] " + line.TrimEnd('\r'));
                position += System.Text.Encoding.UTF8.GetByteCount(text.Substring(0, end + 1));
            }
            catch (IOException)
            {
                // Not created yet - the helper is still starting.
            }
        }

        // ---- the helper's side ---------------------------------------------------

        /// <summary>
        /// Waits for the game, then injects into it. No deadline on the wait - the launcher's Stop
        /// waiting is the way out - and, as with any game the launcher did not start, a game that never
        /// becomes ready is left running.
        /// </summary>
        public static int Run(string gameExe, string dll, string logFile, string stopFile)
        {
            using var writer = new StreamWriter(new FileStream(logFile, FileMode.Create, FileAccess.Write,
                                                               FileShare.ReadWrite | FileShare.Delete))
            {
                AutoFlush = true,
            };
            void Log(string line) => writer.WriteLine(line);

            try
            {
                Log("Running " + (IsElevated ? "as administrator" : "WITHOUT administrator rights") +
                    " (pid " + Environment.ProcessId + "), waiting for " + gameExe + ".");

                Process game;
                while ((game = GameSession.FindRunning(gameExe)) == null)
                {
                    if (File.Exists(stopFile))
                    {
                        Log("Stopped waiting for the game.");
                        return ResultStopped;
                    }
                    Thread.Sleep(500);
                }
                Log("The game is running (pid " + game.Id + ").");

                if (Injector.IsModuleLoaded(game, dll))
                {
                    Log("The bootstrap is already in that process - nothing to inject.");
                    return ResultInjected;
                }

                if (!GameSession.WaitUntilReady(game, TimeSpan.FromMinutes(2), out string reason, Log))
                {
                    Log("The game never became ready: " + reason + ".");
                    return ResultFailed;
                }

                Injector.Inject(game, dll);
                Log("Injected.");
                return ResultInjected;
            }
            catch (Exception ex)
            {
                Log("Failed: " + ex.Message);
                return ResultFailed;
            }
        }

        private static string Quote(string value) => "\"" + value + "\"";

        private static void TryDelete(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (Exception)
            {
            }
        }
    }
}
