using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
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

                case "useArchive":
                    string archive = Str(args, "path");
                    RunJob(id, "Unpack BepInEx", () => UseArchive(archive));
                    break;

                case "setExpert":
                    settings.Expert = args.ValueKind == JsonValueKind.Object &&
                                      args.TryGetProperty("value", out JsonElement e) &&
                                      e.ValueKind == JsonValueKind.True;
                    SafeSave();
                    dialogs.Resize(WindowWidth, WindowHeight(settings.Expert));
                    Reply(id, WriteState);
                    break;

                case "logo":
                    Reply(id, w => WriteValue(w, DataUri("logo.png", "image/png")));
                    break;

                case "openUrl":
                    OpenUrl(Str(args, "url"));
                    Reply(id, w => WriteValue(w, null));
                    break;

                case "prepare":
                    RunJob(id, "Prepare", Prepare);
                    break;

                case "downloadBepInEx":
                    RunJob(id, "Download BepInEx", DownloadBepInEx);
                    break;

                case "modReleases":
                    RunJob(id, "Load the release list", () =>
                    {
                        knownReleases = FetchReleases();
                        Log(knownReleases.Count + " releases with a plugin.");
                    });
                    break;

                case "installMod":
                    string wanted = Str(args, "tag");
                    RunJob(id, "Install the mod", () => InstallMod(
                        RequireStorage(), string.IsNullOrWhiteSpace(wanted) ? "Installing the newest build"
                                                                            : "Installing " + wanted,
                        wanted));
                    break;

                case "downloadUnityLibs":
                    RunJob(id, "Download Unity libraries", DownloadUnityLibs);
                    break;

                case "generateInterop":
                    bool force = args.ValueKind == JsonValueKind.Object &&
                                 args.TryGetProperty("force", out JsonElement f) && f.ValueKind == JsonValueKind.True;
                    RunJob(id, "Generate interop", () => GenerateInterop(force));
                    break;

                case "play":
                    // Closes on success only. A launch that failed has something to say, and the
                    // window is the only place it can say it.
                    RunJob(id, "Launch", Launch, dialogs.Close);
                    break;

                case "confirmResult":
                    Answer(Str(args, "ticket"),
                           args.TryGetProperty("ok", out JsonElement ok) && ok.ValueKind == JsonValueKind.True,
                           Str(args, "value"));
                    Reply(id, w => WriteValue(w, null));
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

        /// <summary>The window the page is drawn in. Simple mode has a third less to show.</summary>
        internal const int WindowWidth = 667;

        // Simple mode is sized to what it actually shows, and an online build shows one row more:
        // the mod it fetches rather than carries.
        internal static int WindowHeight(bool expert) =>
            expert ? 760 : Downloads.PluginFromGitHub ? 620 : 540;

        /// <summary>Which view the window should open in, read before the window exists.</summary>
        internal bool Expert => settings.Expert;

        private void RunJob(string id, string name, Action work, Action then = null)
        {
            if (busy)
            {
                Fail(id, "Another operation is still running.");
                return;
            }

            busy = true;
            Event("busy", true);
            Log("=== " + name + " ===");

            _ = Task.Run(() =>
            {
                string error = null;
                try
                {
                    work();
                    Log(name + ": done.");
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    Log(name + " FAILED: " + ex.Message);
                }

                busy = false;
                Progress("")(0);   // whatever card was filling, stop
                Event("busy", false);
                if (error == null)
                {
                    Reply(id, WriteState);
                    then?.Invoke();
                }
                else
                {
                    Fail(id, error);
                }
            });
        }




        // ---- asking ----------------------------------------------------------

        /// <summary>A question put to the page. One at a time: <see cref="busy"/> serialises jobs.</summary>
        private sealed class Question
        {
            internal string Ticket;

            /// <summary>What the page said, or null when it was declined. Empty for a plain yes.</summary>
            internal TaskCompletionSource<string> Answer;
        }

        private volatile Question asked;
        private int askCount;

        /// <summary>Releases the page has been shown, once someone asked for the list.</summary>
        private List<ModRelease> knownReleases = new List<ModRelease>();

        /// <summary>
        /// Asks the page a yes-or-no and blocks the job until it answers.
        ///
        /// In the page rather than a native message box: the questions this launcher asks list
        /// paths and files, and have to read like the rest of the window rather than like a system
        /// error. Blocking is the point — the answer gates everything after it — and costs nothing,
        /// because jobs already run off the window thread.
        /// </summary>
        private bool Confirm(string title, string text, string confirmLabel) =>
            Ask(title, text, confirmLabel, null, null) != null;

        /// <summary>
        /// The same dialog with a text field. Returns what was typed, or null when it was
        /// dismissed; an empty string means the field was left blank on purpose.
        /// </summary>
        private string AskText(string title, string text, string confirmLabel, string placeholder,
                               string initial)
        {
            return Ask(title, text, confirmLabel, placeholder ?? "", initial ?? "");
        }

        private string Ask(string title, string text, string confirmLabel, string placeholder,
                           string initial)
        {
            var question = new Question
            {
                Ticket = "q" + (++askCount),
                Answer = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously),
            };
            asked = question;

            send(Json(w =>
            {
                w.WriteStartObject();
                w.WriteString("event", "confirm");
                w.WriteString("ticket", question.Ticket);
                w.WriteString("title", title);
                w.WriteString("text", text);
                w.WriteString("confirmLabel", confirmLabel);
                if (placeholder != null)
                {
                    w.WriteString("placeholder", placeholder);
                    w.WriteString("initial", initial ?? "");
                }
                w.WriteEndObject();
            }));

            // No timeout: the dialog is modal and the user may take as long as they like. Should
            // the window go away first this thread simply stays parked, which costs nothing — it is
            // a background thread and does not hold the process open.
            return question.Answer.Task.GetAwaiter().GetResult();
        }

        /// <summary>The page's answer. A ticket that is not the outstanding one is ignored.</summary>
        private void Answer(string ticket, bool ok, string value)
        {
            Question question = asked;
            if (question == null || !string.Equals(ticket, question.Ticket, StringComparison.Ordinal))
                return;

            asked = null;
            question.Answer.TrySetResult(ok ? value ?? "" : null);
        }

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

        /// <summary>
        /// Takes a downloaded archive as the BepInEx source, unpacking it into storage. Validated
        /// here rather than at Prepare, so picking the wrong flavour is answered while the file
        /// picker is still the thing on the user's mind.
        /// </summary>
        private void UseArchive(string zipPath)
        {
            StorageLayout storage = RequireStorage();
            string target = Path.Combine(storage.Root, "download", "bepinex");

            Payload.UnpackArchive(zipPath, target, Log);
            Payload.ValidateSource(target, out string coreDir, out _);

            settings.BepInExSource = target;
            SafeSave();
            Log("BepInEx " + (Payload.ReadBepInExVersion(coreDir) ?? "archive") + " is ready to install.");
        }

        private void DownloadBepInEx()
        {
            StorageLayout storage = RequireStorage();
            // Unpacked beside the storage tree, so Prepare has a source folder and the user has
            // something to point at if they ever want to redo it by hand.
            string target = Path.Combine(storage.Root, "download", "bepinex");
            Downloads.FetchBepInEx(target, Log, Progress("bepinex"));

            settings.BepInExSource = target;
            SafeSave();
        }

        private void DownloadUnityLibs()
        {
            StorageLayout storage = RequireStorage();
            string game = Require(settings.GameFolder, "the game folder");
            string version = GameSession.ReadUnityVersion(game)
                             ?? throw new InvalidOperationException("The game's Unity version could not be determined.");

            Downloads.FetchUnityLibraries(version, storage, Log, Progress("interop"));
        }

        /// <summary>
        /// A progress reporter for one step of the screen. The fill is drawn in the card that is
        /// downloading, so what is filling and what it is filling for are the same thing on screen.
        /// </summary>
        private Action<int> Progress(string target)
        {
            return percent => send(Json(w =>
            {
                w.WriteStartObject();
                w.WriteString("event", "progress");
                w.WriteString("target", target);
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

            // Launch runs this on every start, so say which of the three it is about to be: a cold
            // generation, a forced rebuild, or the hash check that usually finds nothing to do.
            Log(!storage.HasInterop
                ? "Generating the interop assemblies with BepInEx's own generator; a cold run takes " +
                  "several minutes."
                : force
                    ? "Regenerating the interop assemblies; this takes several minutes."
                    : "Checking the interop assemblies against the game.");

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

        // ---- launch ----------------------------------------------------------

        /// <summary>
        /// Everything a launch needs, in the order it needs it, skipping whatever is already on
        /// disk. On a fresh machine that is: find the game, fetch BepInEx, lay out the storage tree,
        /// put the Unity base libraries in place, generate the interop assemblies, start the game.
        /// On every launch after, only the last step really runs — each of the others reduces to
        /// check that says it is already done, and the generator's own check is what notices a game
        /// update and rebuilds the interop for it.
        ///
        /// One job rather than a queue of the individual commands: a chain that stops halfway should
        /// say which step it was on and leave the rest unattempted, which a UI-driven sequence of
        /// separate jobs cannot promise. The buttons stay for running one step deliberately.
        /// </summary>
        private void Launch()
        {
            StorageLayout storage = RequireStorage();
            string game = EnsureGame();

            bool adoptedInterop = AdoptExistingInstall(storage, game);

            if (!storage.IsPrepared)
            {
                EnsureBepInExSource(storage);
                Prepare();
            }

            EnsurePlugin(storage);
            EnsureUnityLibraries(storage, game);

            // An adopted set was loading this game a moment ago, so it is taken as current and the
            // generator is not run at all: that would cost a CoreCLR boot and three passes over
            // GameAssembly.dll to reach the same answer. Only this launch skips it — the next one
            // checks as usual, so a set that predates a game update is caught then rather than never.
            if (adoptedInterop)
                Log("Keeping the interop assemblies that came with the adopted install.");
            else
                GenerateInterop(force: false);

            Play();
        }

        /// <summary>
        /// The game folder, found now if what is stored is not one. The constructor already tries
        /// this once, but a game installed since the window opened should not need a restart.
        /// </summary>
        private string EnsureGame()
        {
            if (GameDetection.IsGameFolder(settings.GameFolder))
                return settings.GameFolder;

            return DetectAndStore(announce: true)
                   ?? throw new InvalidOperationException(
                       "No Heartopia install found - Steam libraries and the usual folders were " +
                       "checked. Point at it with Browse.");
        }

        /// <summary>
        /// Takes over an install that is already loading the mod, with the user's say-so, and
        /// refuses to start the game while one is still in the way.
        ///
        /// Not tidiness: a proxy DLL in the game folder boots BepInEx from inside
        /// <c>il2cpp_init</c>, long before the injection can happen, and the bootstrap will not
        /// start a second runtime in the same process. An old install does not sit alongside this
        /// launcher — it replaces it. Moving the tree instead of deleting it keeps the expensive
        /// part, the generated interop assemblies, so adopting one is far cheaper than starting over.
        /// </summary>
        /// <returns>True when the storage tree now has interop that did not have to be generated.</returns>
        private bool AdoptExistingInstall(StorageLayout storage, string game)
        {
            ExistingInstall install = ExistingInstall.Detect(game);
            if (!install.Found || install.IsAdopted(storage))
                return false;

            Log("Found an existing install: " +
                (install.HasMelonLoader ? "MelonLoader" : install.BepInExRoot ?? "no loader tree") +
                (install.ProxyName != null ? ", started by " + install.ProxyName : "") +
                (install.HasInterop ? ", interop present" : ""));

            if (!Confirm("Existing mod install", ConfirmAdopt(install, storage),
                         install.BepInExRoot != null ? "Move and clean up" : "Remove and continue"))
            {
                throw new InvalidOperationException(
                    "Left the existing install alone. The game still loads the mod from it - start " +
                    "the game the way you have been, or press Launch again and accept the move.");
            }

            Log("Adopting the existing install:");
            install.AdoptInto(storage, Log);

            // Whatever the individual steps reported, this is the question that decides whether the
            // game can be started: is anything still able to boot ahead of the injection?
            string blocker = ExistingInstall.Detect(game).ProxyName;
            if (blocker != null)
            {
                throw new InvalidOperationException(
                    blocker + " is still in the game folder and would boot BepInEx before the " +
                    "injection. Close the game and anything else holding that file, then try again.");
            }

            // Only a tree that was just adopted vouches for its interop. Clearing MelonLoader away
            // says nothing about whatever happens to be sitting in storage already.
            return install.BepInExRoot != null && storage.HasInterop;
        }

        /// <summary>
        /// What the confirmation asks. It names every path and every file rather than summarising:
        /// this moves a folder the user may keep deliberately where it is, and deletes files from
        /// the game folder, so the dialog has to be enough on its own to say no to.
        /// </summary>
        private static string ConfirmAdopt(ExistingInstall install, StorageLayout storage)
        {
            var text = new StringBuilder();
            text.Append(install.BepInExRoot != null
                ? "The game is already set up to load the mod."
                : "Another mod loader is installed in the game folder.");

            if (install.BepInExRoot != null)
            {
                text.Append("\n\nMove\n    ").Append(install.BepInExRoot);
                if (install.RuntimeRoot != null)
                    text.Append("\n    ").Append(install.RuntimeRoot);
                text.Append("\ninto\n    ").Append(storage.Root);

                if (install.HasInterop)
                    text.Append("\n\nThe generated interop assemblies come with it, so nothing has to be rebuilt.");
            }

            if (install.DoorstopFiles.Count > 0)
            {
                text.Append("\n\nDelete from the game folder:");
                Names(text, install.DoorstopFiles);
                text.Append("\n\nThese are what start the old loader. While they are there it boots " +
                            "before this launcher can inject, and the mod cannot load twice.");
            }

            if (install.HasMelonLoader)
            {
                text.Append("\n\nRemove MelonLoader from the game folder:");
                Names(text, install.MelonLoaderEntries);
                text.Append("\n\nIt boots before this launcher can inject, and nothing in it can be " +
                            "carried over. Any MelonLoader mods, and the settings under UserData, go " +
                            "with it.");
            }

            return text.ToString();
        }

        /// <summary>Lists paths by name, one per line, with a separator marking the folders.</summary>
        private static void Names(StringBuilder text, IReadOnlyList<string> paths)
        {
            foreach (string path in paths)
            {
                text.Append("\n    ").Append(Path.GetFileName(path));
                if (Directory.Exists(path))
                    text.Append(Path.DirectorySeparatorChar);
            }
        }

        /// <summary>
        /// An unpacked BepInEx archive to lay the tree out from, fetched when the user has not
        /// supplied one. No <see cref="Downloads.Enabled"/> guard: an offline build's download
        /// throws before it touches anything, with the URL to fetch by hand — which is exactly the
        /// message this case needs, and a guard here would only be unreachable code in one flavour.
        /// </summary>
        private void EnsureBepInExSource(StorageLayout storage)
        {
            if (IsUsableSource(settings.BepInExSource))
                return;

            // An adopted install is its own source: core and dotnet are already in storage, so
            // Prepare has only the carried files left to write. This is also the path that lets
            // someone with an existing install and no archive get going at all.
            if (IsUsableSource(storage.Root))
            {
                Log("Using the adopted tree as the BepInEx source.");
                settings.BepInExSource = storage.Root;
                SafeSave();
                return;
            }

            DownloadBepInEx();
        }

        /// <summary>Whether a folder passes the same rules <see cref="Prepare"/> will apply to it.</summary>
        private static bool IsUsableSource(string folder)
        {
            try
            {
                Payload.ValidateSource(folder, out _, out _);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// The mod itself. An offline build carries it and Prepare has already written it; an online
        /// build fetches it from its releases, where a missing plugin is simply what a fresh install
        /// looks like rather than something to fail over.
        /// </summary>
        private void EnsurePlugin(StorageLayout storage)
        {
            if (!Downloads.PluginFromGitHub || File.Exists(storage.Plugin))
                return;

            InstallMod(storage, "No mod installed yet");
        }

        /// <summary>
        /// Fetches the release list and installs the newest build.
        ///
        /// A token is asked for only when the API turns us away for a reason a token would fix -
        /// a bad one, or the anonymous rate limit - and a token that then works is remembered, so
        /// this is a question the user answers once rather than every launch.
        /// </summary>
        private void InstallMod(StorageLayout storage, string why, string tag = null)
        {
            Log(why + "; fetching it from " + GitHub.Repository + ".");

            List<ModRelease> releases = FetchReleases();
            ModRelease chosen = null;

            if (!string.IsNullOrWhiteSpace(tag))
            {
                foreach (ModRelease release in releases)
                {
                    if (string.Equals(release.Tag, tag, StringComparison.OrdinalIgnoreCase))
                    {
                        chosen = release;
                        break;
                    }
                }

                if (chosen == null)
                    throw new InvalidOperationException("No release tagged " + tag + ".");
            }
            else if (releases.Count > 0)
            {
                chosen = releases[0];
            }

            if (chosen == null)
            {
                throw new InvalidOperationException(
                    "No release of " + GitHub.Repository + " has a plugin to install.");
            }

            GitHub.Install(chosen, storage, Log, Progress("mod"));
        }

        /// <summary>
        /// The release list, asking for a GitHub token if the API turns us away for a reason a
        /// token would fix - a bad one, or the anonymous rate limit - and remembering one that then
        /// works, so this is a question the user answers once rather than every launch.
        /// </summary>
        private List<ModRelease> FetchReleases()
        {
            try
            {
                return GitHub.FetchReleases(settings.GitHubToken, Log);
            }
            catch (GitHubException refused) when (refused.NeedsToken)
            {
                string token = AskText(
                    "GitHub",
                    refused.Message + "\n\nA personal access token will get past this. It needs no " +
                    "scopes at all - it only raises the request limit - and it is kept in the " +
                    "launcher's settings file for next time.",
                    "Try again",
                    "ghp_...",
                    settings.GitHubToken);

                if (string.IsNullOrWhiteSpace(token))
                {
                    throw new InvalidOperationException(
                        refused.Message + " Releases can also be downloaded by hand from " +
                        GitHub.ReleasesPage + ".");
                }

                List<ModRelease> releases = GitHub.FetchReleases(token, Log);
                settings.GitHubToken = token;
                SafeSave();
                return releases;
            }
        }

        /// <summary>
        /// Puts the Unity base libraries in unity-libs before the generator looks for them.
        ///
        /// BepInEx fetches them itself when they are missing, so this is not required — but it
        /// happens inside the hosted generator, where it is a silent multi-minute pause with no
        /// progress and nothing in the log until it ends. Doing it here makes the wait legible.
        /// </summary>
        private void EnsureUnityLibraries(StorageLayout storage, string game)
        {
            if (HasUnityLibraries(storage))
                return;

            // Prepare installs a chosen zip itself, so reaching this means the tree was laid out
            // before the user picked one.
            if (!string.IsNullOrWhiteSpace(settings.UnityLibsZip) && File.Exists(settings.UnityLibsZip))
            {
                Payload.InstallUnityLibs(settings.UnityLibsZip, storage, Log);
                return;
            }

            if (!Downloads.Enabled || GameSession.ReadUnityVersion(game) == null)
            {
                Log("No Unity base libraries yet; BepInEx will fetch them itself during generation.");
                return;
            }

            DownloadUnityLibs();
        }

        /// <summary>
        /// True when unity-libs holds either the zip BepInEx would have downloaded or the assemblies
        /// it unpacks from it.
        /// </summary>
        private static bool HasUnityLibraries(StorageLayout storage)
        {
            return Directory.Exists(storage.UnityLibs) &&
                   (Directory.GetFiles(storage.UnityLibs, "*.dll").Length > 0 ||
                    Directory.GetFiles(storage.UnityLibs, "*.zip").Length > 0);
        }

        private void Play()
        {
            StorageLayout storage = RequireStorage();
            string game = Require(settings.GameFolder, "the game folder");

            if (!storage.IsPrepared)
                throw new InvalidOperationException("Run Prepare first.");
            if (!storage.HasInterop)
                throw new InvalidOperationException("Generate the interop assemblies first.");

            // Launch clears this before it gets here; the button on its own does not, so say what
            // will happen rather than letting the injection fail its guard unexplained.
            string proxy = ExistingInstall.Detect(game).ProxyName;
            if (proxy != null)
            {
                Log("Warning: " + proxy + " is still in the game folder. Another loader will boot " +
                    "from it before the injection, and the bootstrap refuses to start a second " +
                    "runtime - press Launch to clear that install out of the way.");
            }

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

        /// <summary>
        /// An embedded file as a data URI, or null when this build does not carry it. Fetched by
        /// the page rather than baked into it — see <c>PhotinoHost.LoadUi</c> for why the initial
        /// page string has to stay small.
        /// </summary>
        private static string DataUri(string resource, string mediaType)
        {
            using Stream stream = typeof(Api).Assembly.GetManifestResourceStream(resource);
            if (stream == null)
                return null;

            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return "data:" + mediaType + ";base64," + Convert.ToBase64String(buffer.ToArray());
        }

        /// <summary>The files this exe carries, written into the storage tree by Prepare.</summary>
        private static IEnumerable<PayloadFile> CarriedFiles()
        {
            Assembly self = typeof(Api).Assembly;
            yield return PayloadFile.FromResource(self, "payload.bugtopia_inject.dll",
                                                  Path.Combine("bin", StorageLayout.InjectDllName));
            yield return PayloadFile.FromResource(self, "payload.BugtopiaInterop.dll",
                                                  Path.Combine("bin", StorageLayout.InteropShimName));

            // Optional rather than branched on: an online build does not embed the mod at all, so
            // the resource is simply not there and Prepare says so and carries on. A branch would
            // be unreachable code in one flavour or the other.
            yield return PayloadFile.FromResource(self, "payload.bugtopia.dll",
                                                  Path.Combine("BepInEx", "plugins", StorageLayout.PluginName),
                                                  required: !Downloads.PluginFromGitHub);
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
            w.WriteString("version", HeartopiaMod.ModBuildVersion.Display);
            w.WriteBoolean("pluginFromGitHub", Downloads.PluginFromGitHub);
            w.WriteString("releasesPage", GitHub.ReleasesPage);
            w.WriteBoolean("downloads", Downloads.Enabled);
            w.WriteBoolean("expert", settings.Expert);
            w.WriteString("bepInExVersion", Downloads.BepInExVersion);
            w.WriteString("bepInExUrl", Downloads.BepInExUrl);
            w.WriteString("preparedFrom", settings.PreparedFrom ?? "");

            string game = settings.GameFolder;
            string unity = string.IsNullOrWhiteSpace(game) ? null : GameSession.ReadUnityVersion(game);
            w.WriteString("unityVersion", unity ?? "");
            w.WriteString("unityLibsUrl", Downloads.UnityLibrariesUrl(unity) ?? "");
            w.WriteBoolean("gameOk", !string.IsNullOrWhiteSpace(game) && Directory.Exists(game) && unity != null);

            bool prepared = false, hasInterop = false;
            StorageLayout storage = null;
            try
            {
                storage = RequireStorage();
                prepared = storage.IsPrepared;
                hasInterop = storage.HasInterop;
            }
            catch (Exception)
            {
            }
            w.WriteBoolean("prepared", prepared);
            w.WriteBoolean("hasInterop", hasInterop);
            w.WriteBoolean("hasPlugin", storage != null && File.Exists(storage.Plugin));
            w.WriteString("pluginVersion", storage == null ? "" : GitHub.InstalledTag(storage) ?? "");
            w.WriteBoolean("interopStale", hasInterop && IsInteropStale(storage, game));

            w.WriteStartArray("modReleases");
            foreach (ModRelease release in knownReleases)
            {
                w.WriteStartObject();
                w.WriteString("tag", release.Tag);
                w.WriteString("asset", release.AssetName);
                w.WriteEndObject();
            }
            w.WriteEndArray();

            // An install already loading the mod. Reported in full rather than as a flag because the
            // page has to be able to say which folder it found and what moving it would preserve.
            ExistingInstall existing = ExistingInstall.Detect(game);
            w.WriteStartObject("existing");
            w.WriteBoolean("found", existing.Found && !(storage != null && existing.IsAdopted(storage)));
            w.WriteString("proxy", existing.ProxyName ?? "");
            w.WriteString("root", existing.BepInExRoot ?? "");
            w.WriteBoolean("melon", existing.HasMelonLoader);
            w.WriteBoolean("plugin", existing.HasPlugin);
            w.WriteBoolean("interop", existing.HasInterop);
            w.WriteEndObject();
            w.WriteBoolean("busy", busy);
            w.WriteEndObject();
        }

        /// <summary>
        /// Whether the interop assemblies are older than the game they were built from.
        ///
        /// A timestamp comparison, not the real answer: the authoritative check is an MD5 over
        /// GameAssembly.dll and every unity-libs assembly, which is BepInEx's to compute and takes a
        /// hosted runtime to ask for. This is the cheap signal that can be had on every state read,
        /// and it is right about the case that actually happens - the game updated since. The
        /// generator still does the real check on the next launch, so a wrong answer here costs a
        /// line of text, not a stale install.
        /// </summary>
        private static bool IsInteropStale(StorageLayout storage, string game)
        {
            if (storage == null || string.IsNullOrWhiteSpace(game))
                return false;

            try
            {
                return File.GetLastWriteTimeUtc(Path.Combine(game, "GameAssembly.dll")) >
                       File.GetLastWriteTimeUtc(storage.InteropHash);
            }
            catch (Exception)
            {
                return false;
            }
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
