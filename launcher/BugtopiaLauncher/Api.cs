using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Bugtopia.Launch;

namespace Bugtopia.Launcher
{
    /// <summary>
    /// The bridge between the page and everything the launcher can do.
    ///
    /// JSON is read with <see cref="JsonDocument"/> and written with <see cref="Utf8JsonWriter"/>
    /// rather than serialised from objects: both are reflection-free, so the whole surface survives
    /// NativeAOT without a serializer context per payload shape.
    /// </summary>
    internal sealed class Api
    {
        private readonly Action<string> send;
        private readonly IDialogs dialogs;
        private readonly LauncherSettings settings;
        private bool busy;

        internal Api(Action<string> send, IDialogs dialogs)
        {
            this.send = send;
            this.dialogs = dialogs;
            settings = LauncherSettings.Load();

            // A saved path that no longer resolves is worse than none: it makes the status panel
            // explain a folder the user has since moved. Re-detect instead.
            if (!GameDetection.IsGameFolder(settings.GameFolder))
                DetectAndStore(announce: false);
        }

        /// <summary>
        /// Looks for the install and remembers it. Quiet on startup — a detection nobody asked for
        /// should not put a line in the log every launch — and spoken when the button was pressed.
        /// </summary>
        private string DetectAndStore(bool announce)
        {
            string found = GameDetection.Detect();
            if (found != null)
            {
                settings.GameFolder = found;
                SafeSave();
                if (announce)
                    Log("Found the game at " + found);
            }
            else if (announce)
            {
                Log("No Heartopia install found - Steam libraries and the usual folders were checked.");
            }
            return found;
        }

        // ---- dispatch --------------------------------------------------------

        private bool greeted;

        internal void Dispatch(string message)
        {
            string id = null;
            try
            {
                // The first request that arrives is proof the page loaded and the bridge works in
                // both directions — worth recording, because a launcher that fails before this has
                // no other way to say so.
                if (!greeted)
                {
                    greeted = true;
                    Log("ui ready");
                }

                using JsonDocument doc = JsonDocument.Parse(message);
                JsonElement root = doc.RootElement;
                id = root.TryGetProperty("id", out JsonElement idEl) ? idEl.GetString() : null;
                string cmd = root.GetProperty("cmd").GetString();
                JsonElement args = root.TryGetProperty("args", out JsonElement a) ? a : default;

                Handle(id, cmd, args);
            }
            catch (Exception ex)
            {
                Fail(id, ex.Message);
            }
        }

        private void Handle(string id, string cmd, JsonElement args)
        {
            switch (cmd)
            {
                case "state":
                    Reply(id, WriteState);
                    break;

                case "setPaths":
                    settings.GameFolder = Str(args, "game") ?? settings.GameFolder;
                    settings.BepInExSource = Str(args, "source") ?? settings.BepInExSource;
                    settings.Storage = Str(args, "storage") ?? settings.Storage;
                    settings.UnityLibsZip = Str(args, "unityLibsZip") ?? settings.UnityLibsZip;
                    SafeSave();
                    Reply(id, WriteState);
                    break;

                case "detectGame":
                    Reply(id, w => WriteValue(w, DetectAndStore(announce: true)));
                    break;

                case "pickFolder":
                    Reply(id, w => WriteValue(w, dialogs.PickFolder(Str(args, "title"), Str(args, "current"))));
                    break;

                case "pickFile":
                    Reply(id, w => WriteValue(w, dialogs.PickFile(
                        Str(args, "title"), "Zip archives", new[] { "zip" })));
                    break;

                case "openUrl":
                    OpenUrl(Str(args, "url"));
                    Reply(id, w => WriteValue(w, null));
                    break;

                case "prepare":
                    RunJob(id, "Prepare", Prepare);
                    break;

                case "downloadBepInEx":
                    RunJob(id, "Download BepInEx", DownloadBepInExAsync);
                    break;

                case "downloadUnityLibs":
                    RunJob(id, "Download Unity libraries", DownloadUnityLibsAsync);
                    break;

                case "generateInterop":
                    bool force = args.ValueKind == JsonValueKind.Object &&
                                 args.TryGetProperty("force", out JsonElement f) && f.ValueKind == JsonValueKind.True;
                    RunJob(id, "Generate interop", () => GenerateInterop(force));
                    break;

                case "play":
                    RunJob(id, "Play", Play);
                    break;

                case "profiles":
                    Reply(id, w => WriteProfiles(w, Profiles.List()));
                    break;

                case "profileCreate":
                    Log(Profiles.Create(Str(args, "name")));
                    Reply(id, w => WriteProfiles(w, Profiles.List()));
                    break;

                case "profileSwitch":
                    Log(Profiles.Switch(Str(args, "name")));
                    Reply(id, w => WriteProfiles(w, Profiles.List()));
                    break;

                case "serverGet":
                    Reply(id, w => w.WriteNumberValue(Profiles.GetServer(Str(args, "profile"))));
                    break;

                case "serverSet":
                    Profiles.SetServer(Str(args, "profile"), args.GetProperty("value").GetInt32());
                    Log("Zone server set.");
                    Reply(id, w => w.WriteNumberValue(Profiles.GetServer(Str(args, "profile"))));
                    break;

                default:
                    Fail(id, "Unknown command: " + cmd);
                    break;
            }
        }

        // ---- jobs ------------------------------------------------------------

        private void RunJob(string id, string name, Func<Task> work)
        {
            if (busy)
            {
                Fail(id, "Another operation is still running.");
                return;
            }

            busy = true;
            Event("busy", true);
            Log("=== " + name + " ===");

            _ = Task.Run(async () =>
            {
                string error = null;
                try
                {
                    await work();
                    Log(name + ": done.");
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    Log(name + " FAILED: " + ex.Message);
                }

                busy = false;
                Event("busy", false);
                if (error == null)
                    Reply(id, WriteState);
                else
                    Fail(id, error);
            });
        }

        private void RunJob(string id, string name, Action work) =>
            RunJob(id, name, () => { work(); return Task.CompletedTask; });

        private void Prepare()
        {
            StorageLayout storage = RequireStorage();
            string source = Require(settings.BepInExSource, "the unpacked BepInEx folder");

            Payload.Prepare(source, storage, CarriedFiles(), Log);
            if (!string.IsNullOrWhiteSpace(settings.UnityLibsZip))
                Payload.InstallUnityLibs(settings.UnityLibsZip, storage, Log);

            Payload.ValidateSource(source, out string coreDir, out _);
            settings.PreparedFrom = Payload.ReadBepInExVersion(coreDir);
            SafeSave();
        }

        private async Task DownloadBepInExAsync()
        {
            StorageLayout storage = RequireStorage();
            // Unpacked beside the storage tree, so Prepare has a source folder and the user has
            // something to point at if they ever want to redo it by hand.
            string target = Path.Combine(storage.Root, "download", "bepinex");
            await Downloads.FetchBepInExAsync(target, Log, new Progress<int>(ReportPercent));

            settings.BepInExSource = target;
            SafeSave();
        }

        private async Task DownloadUnityLibsAsync()
        {
            StorageLayout storage = RequireStorage();
            string game = Require(settings.GameFolder, "the game folder");
            string version = GameSession.ReadUnityVersion(game)
                             ?? throw new InvalidOperationException("The game's Unity version could not be determined.");

            await Downloads.FetchUnityLibrariesAsync(version, storage, Log, new Progress<int>(ReportPercent));
        }

        /// <summary>Drives the banner's progress fill; only whole percents arrive here.</summary>
        private void ReportPercent(int percent)
        {
            send(Json(w =>
            {
                w.WriteStartObject();
                w.WriteString("event", "progress");
                w.WriteNumber("value", percent);
                w.WriteEndObject();
            }));
        }

        /// <summary>
        /// Runs the generator in a child copy of this exe. It cannot run here: CoreCLR initialises
        /// once per process, and the window has to be able to do this again.
        /// </summary>
        private void GenerateInterop(bool force)
        {
            StorageLayout storage = RequireStorage();
            string game = Require(settings.GameFolder, "the game folder");

            if (!storage.IsPrepared)
                throw new InvalidOperationException("Run Prepare first.");

            Log("Running BepInEx's own generator; a cold run takes several minutes.");

            var info = new ProcessStartInfo(Environment.ProcessPath)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
            };
            info.ArgumentList.Add(Program.VerbInterop);
            info.ArgumentList.Add("--game");
            info.ArgumentList.Add(game);
            info.ArgumentList.Add("--storage");
            info.ArgumentList.Add(storage.Root);
            info.ArgumentList.Add(force ? "--force" : "--generate");

            using Process child = Process.Start(info);
            string stderr = child.StandardError.ReadToEnd();
            child.WaitForExit();

            foreach (string line in ReadGeneratorLog(storage))
                Log("  " + line);

            if (child.ExitCode == CoreClrHost.ResultError)
            {
                throw new InvalidOperationException(
                    (string.IsNullOrWhiteSpace(stderr) ? "Generation failed." : stderr.Trim()) +
                    " See " + Path.Combine(storage.Bin, "interopgen.log"));
            }
        }

        private static IEnumerable<string> ReadGeneratorLog(StorageLayout storage)
        {
            string path = Path.Combine(storage.Bin, "interopgen.log");
            if (!File.Exists(path))
                yield break;

            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length > 0)
                    yield return line;
            }
        }

        private void Play()
        {
            StorageLayout storage = RequireStorage();
            string game = Require(settings.GameFolder, "the game folder");

            if (!storage.IsPrepared)
                throw new InvalidOperationException("Run Prepare first.");
            if (!storage.HasInterop)
                throw new InvalidOperationException("Generate the interop assemblies first.");

            string exe = GameSession.FindGameExe(game);
            Log("Starting " + exe);

            Process process = GameSession.Start(exe, storage);
            Log("Started (pid " + process.Id + "); waiting for the IL2CPP runtime.");

            if (!GameSession.WaitUntilReady(process, TimeSpan.FromMinutes(2), out string reason, Log))
            {
                if (!process.HasExited)
                    process.Kill();
                throw new InvalidOperationException("The game never became ready: " + reason + ".");
            }

            Injector.Inject(process, storage.InjectDll);
            Log("Injected. The bootstrap's own account is in " + Path.Combine(storage.Bin, "bugtopia_inject.log"));
        }

        /// <summary>The files this exe carries, written into the storage tree by Prepare.</summary>
        private static IEnumerable<PayloadFile> CarriedFiles()
        {
            Assembly self = typeof(Api).Assembly;
            yield return PayloadFile.FromResource(self, "payload.bugtopia_inject.dll",
                                                  Path.Combine("bin", StorageLayout.InjectDllName));
            yield return PayloadFile.FromResource(self, "payload.BugtopiaInterop.dll",
                                                  Path.Combine("bin", StorageLayout.InteropShimName));
            yield return PayloadFile.FromResource(self, "payload.bugtopia.dll",
                                                  Path.Combine("BepInEx", "plugins", StorageLayout.PluginName));
        }

        // ---- state -----------------------------------------------------------

        private void WriteState(Utf8JsonWriter w)
        {
            w.WriteStartObject();
            w.WriteString("game", settings.GameFolder ?? "");
            w.WriteString("source", settings.BepInExSource ?? "");
            w.WriteString("storage", string.IsNullOrWhiteSpace(settings.Storage)
                ? LauncherSettings.DefaultStorage
                : settings.Storage);
            w.WriteString("unityLibsZip", settings.UnityLibsZip ?? "");
            w.WriteString("defaultStorage", LauncherSettings.DefaultStorage);
            w.WriteBoolean("downloads", Downloads.Enabled);
            w.WriteString("bepInExVersion", Downloads.BepInExVersion);
            w.WriteString("bepInExUrl", Downloads.BepInExUrl);
            w.WriteString("preparedFrom", settings.PreparedFrom ?? "");

            string game = settings.GameFolder;
            string unity = string.IsNullOrWhiteSpace(game) ? null : GameSession.ReadUnityVersion(game);
            w.WriteString("unityVersion", unity ?? "");
            w.WriteString("unityLibsUrl", Downloads.UnityLibrariesUrl(unity) ?? "");
            w.WriteBoolean("gameOk", !string.IsNullOrWhiteSpace(game) && Directory.Exists(game) && unity != null);

            // Only the proxy DLL can boot BepInEx ahead of the injection and trip the bootstrap's
            // one-CLR-per-process guard; a leftover ini on its own loads nothing.
            string proxy = null;
            if (!string.IsNullOrWhiteSpace(game))
            {
                foreach (string name in new[] { "winhttp.dll", "version.dll" })
                {
                    if (File.Exists(Path.Combine(game, name)))
                    {
                        proxy = name;
                        break;
                    }
                }
            }
            w.WriteString("loaderProxy", proxy ?? "");

            bool prepared = false, hasInterop = false;
            try
            {
                var storage = new StorageLayout(settings.Storage ?? LauncherSettings.DefaultStorage);
                prepared = storage.IsPrepared;
                hasInterop = storage.HasInterop;
            }
            catch (Exception)
            {
            }
            w.WriteBoolean("prepared", prepared);
            w.WriteBoolean("hasInterop", hasInterop);
            w.WriteBoolean("busy", busy);
            w.WriteEndObject();
        }

        private static void WriteProfiles(Utf8JsonWriter w, ProfileInfo info)
        {
            w.WriteStartObject();
            w.WriteBoolean("available", info.Available);
            w.WriteString("active", info.Active ?? "");
            w.WriteStartArray("profiles");
            foreach (string name in info.Profiles)
                w.WriteStringValue(name);
            w.WriteEndArray();
            w.WriteEndObject();
        }

        // ---- helpers ---------------------------------------------------------

        private StorageLayout RequireStorage()
        {
            string root = string.IsNullOrWhiteSpace(settings.Storage)
                ? LauncherSettings.DefaultStorage
                : settings.Storage;
            return new StorageLayout(root);
        }

        private static string Require(string value, string what)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new InvalidOperationException("Set " + what + " first.");
            return value;
        }

        private void SafeSave()
        {
            try
            {
                settings.Save();
            }
            catch (Exception ex)
            {
                Log("Could not save settings: " + ex.Message);
            }
        }

        private static string Str(JsonElement args, string name) =>
            args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out JsonElement v) &&
            v.ValueKind == JsonValueKind.String
                ? v.GetString()
                : null;

        private static void OpenUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return;
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Exception)
            {
            }
        }

        internal void ReportUnhandled(Exception ex) => Log("Unexpected error: " + ex.Message);

        // ---- wire ------------------------------------------------------------

        private void Reply(string id, Action<Utf8JsonWriter> writeData)
        {
            send(Json(w =>
            {
                w.WriteStartObject();
                w.WriteString("id", id);
                w.WriteBoolean("ok", true);
                w.WritePropertyName("data");
                writeData(w);
                w.WriteEndObject();
            }));
        }

        private void Fail(string id, string error)
        {
            send(Json(w =>
            {
                w.WriteStartObject();
                w.WriteString("id", id);
                w.WriteBoolean("ok", false);
                w.WriteString("error", error);
                w.WriteEndObject();
            }));
        }

        private void Event(string name, bool value)
        {
            send(Json(w =>
            {
                w.WriteStartObject();
                w.WriteString("event", name);
                w.WriteBoolean("value", value);
                w.WriteEndObject();
            }));
        }

        /// <summary>Everything the panel shows also goes to a file — before the mod is up there is no other account.</summary>
        private void Log(string text)
        {
            string line = DateTime.Now.ToString("HH:mm:ss") + "  " + text;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(KnownPaths.LauncherLogFile));
                File.AppendAllText(KnownPaths.LauncherLogFile, line + Environment.NewLine);
            }
            catch (IOException)
            {
            }

            send(Json(w =>
            {
                w.WriteStartObject();
                w.WriteString("event", "log");
                w.WriteString("text", line);
                w.WriteEndObject();
            }));
        }

        private static string Json(Action<Utf8JsonWriter> write)
        {
            using var buffer = new MemoryStream();
            using (var writer = new Utf8JsonWriter(buffer))
                write(writer);
            return Encoding.UTF8.GetString(buffer.ToArray());
        }

        private static void WriteValue(Utf8JsonWriter w, string value)
        {
            if (value == null)
                w.WriteNullValue();
            else
                w.WriteStringValue(value);
        }
    }
}
