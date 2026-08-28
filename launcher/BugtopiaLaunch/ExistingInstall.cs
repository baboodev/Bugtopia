using System;
using System.Collections.Generic;
using System.IO;

namespace Bugtopia.Launch
{
    /// <summary>
    /// Whatever already loads into this game: a doorstop-style BepInEx install — its proxy DLL, the
    /// files that configure it and the loader tree they point at — or MelonLoader, which is the same
    /// problem wearing different filenames.
    ///
    /// Both matter for one reason. Their proxy boots from inside <c>il2cpp_init</c>, long before
    /// this launcher can inject, and the bootstrap will not start a second runtime in one process.
    /// The difference is what can be done about it: a BepInEx tree is worth adopting, since its
    /// interop assemblies took minutes to build, while MelonLoader has nothing this launcher can
    /// use and simply has to go.
    ///
    /// The BepInEx tree is read out of <c>doorstop_config.ini</c> rather than assumed to sit in the
    /// game folder. <c>target_assembly</c> is an absolute path, and pointing it at a BepInEx kept
    /// somewhere else entirely is a supported setup — the developer install on this project is one —
    /// so guessing would find nothing in exactly the case that matters most.
    /// </summary>
    public sealed class ExistingInstall
    {
        /// <summary>The two names a proxy can take. Both are DLLs Windows resolves from the exe's folder.</summary>
        private static readonly string[] ProxyNames = { "winhttp.dll", "version.dll" };

        /// <summary>What the doorstop half of an install leaves beside the game exe.</summary>
        private static readonly string[] ConfigNames = { "doorstop_config.ini", ".doorstop_version", "changelog.txt" };

        /// <summary>The folder only MelonLoader creates, and the marker that it is really installed.</summary>
        private const string MelonLoaderFolder = "MelonLoader";

        /// <summary>MelonLoader's proxy. Doorstop can take the same name, so the folder decides which it is.</summary>
        private const string MelonLoaderProxy = "version.dll";

        /// <summary>
        /// Everything a MelonLoader install puts in the game folder, in the order it reads best.
        /// Collected only when <see cref="MelonLoaderFolder"/> is there: <c>Mods</c> and
        /// <c>UserData</c> on their own are leftovers that boot nothing, and are not worth deleting
        /// someone's files over.
        /// </summary>
        private static readonly string[] MelonLoaderNames =
        {
            MelonLoaderFolder, "Mods", "Plugins", "UserData", "UserLibs",
            MelonLoaderProxy, "your game content lives here.txt",
        };

        /// <summary>The proxy DLL's name, or null when nothing boots ahead of the game.</summary>
        public string ProxyName { get; private set; }

        /// <summary>Full paths of the doorstop files in the game folder.</summary>
        public IReadOnlyList<string> DoorstopFiles { get; private set; } = Array.Empty<string>();

        /// <summary>Full paths of MelonLoader's files and folders, or empty when it is not installed.</summary>
        public IReadOnlyList<string> MelonLoaderEntries { get; private set; } = Array.Empty<string>();

        /// <summary>MelonLoader is installed. There is nothing here to adopt — it has to go.</summary>
        public bool HasMelonLoader => MelonLoaderEntries.Count > 0;

        /// <summary>The <c>BepInEx</c> folder the config points at, or null.</summary>
        public string BepInExRoot { get; private set; }

        /// <summary>The <c>dotnet</c> folder holding the CoreCLR the config names, or null.</summary>
        public string RuntimeRoot { get; private set; }

        /// <summary>This mod's plugin is in that tree.</summary>
        public bool HasPlugin { get; private set; }

        /// <summary>That tree has generated interop assemblies — the part worth keeping.</summary>
        public bool HasInterop { get; private set; }

        /// <summary>Anything at all was found.</summary>
        public bool Found =>
            ProxyName != null || DoorstopFiles.Count > 0 || BepInExRoot != null || HasMelonLoader;

        /// <summary>
        /// True once the tree is where this launcher keeps it and nothing boots ahead of the
        /// injection — the state migration is trying to reach.
        /// </summary>
        public bool IsAdopted(StorageLayout storage) =>
            ProxyName == null && DoorstopFiles.Count == 0 && !HasMelonLoader &&
            (BepInExRoot == null || SamePath(BepInExRoot, storage.BepInExRoot));

        /// <summary>Looks the game folder over. Never throws: an unreadable folder is simply no install.</summary>
        public static ExistingInstall Detect(string gameFolder)
        {
            var found = new ExistingInstall();
            if (string.IsNullOrWhiteSpace(gameFolder) || !Directory.Exists(gameFolder))
                return found;

            var files = new List<string>();
            try
            {
                bool melon = Directory.Exists(Path.Combine(gameFolder, MelonLoaderFolder));
                if (melon)
                {
                    var melonEntries = new List<string>();
                    foreach (string name in MelonLoaderNames)
                    {
                        string path = Path.Combine(gameFolder, name);
                        if (File.Exists(path) || Directory.Exists(path))
                            melonEntries.Add(path);
                    }
                    found.MelonLoaderEntries = melonEntries;
                }

                foreach (string name in ProxyNames)
                {
                    string path = Path.Combine(gameFolder, name);
                    if (!File.Exists(path))
                        continue;

                    found.ProxyName ??= name;

                    // version.dll is MelonLoader's own when its folder is there, and is listed with
                    // the rest of it rather than in both places.
                    if (!melon || !string.Equals(name, MelonLoaderProxy, StringComparison.OrdinalIgnoreCase))
                        files.Add(path);
                }

                foreach (string name in ConfigNames)
                {
                    string path = Path.Combine(gameFolder, name);
                    if (File.Exists(path))
                        files.Add(path);
                }
                found.DoorstopFiles = files;

                string ini = Path.Combine(gameFolder, "doorstop_config.ini");
                found.BepInExRoot = RootOfLoader(ReadIniValue(ini, "target_assembly"))
                                    ?? ExistingDirectory(Path.Combine(gameFolder, "BepInEx"));
                found.RuntimeRoot = ExistingDirectory(DirectoryOfFile(ReadIniValue(ini, "coreclr_path")))
                                    ?? ExistingDirectory(Path.Combine(gameFolder, "dotnet"));

                if (found.BepInExRoot != null)
                {
                    found.HasPlugin = File.Exists(
                        Path.Combine(found.BepInExRoot, "plugins", StorageLayout.PluginName));
                    found.HasInterop = File.Exists(
                        Path.Combine(found.BepInExRoot, "interop", "assembly-hash.txt"));
                }
            }
            catch (Exception)
            {
                // Detection is advisory. Whatever was collected before the failure still stands.
            }

            return found;
        }

        /// <summary>
        /// Moves the loader tree into storage and removes what made the game boot it, in that order.
        ///
        /// The order is the one whose failures are survivable. Moving first means a move that fails
        /// leaves the old install untouched and still working; the delete that follows can only fail
        /// into a proxy pointing at a target that is no longer there, which doorstop reports and
        /// steps over, leaving the injection a clear field. Deleting first would put a failed move
        /// between the user and a working game.
        /// </summary>
        public void AdoptInto(StorageLayout storage, Action<string> log)
        {
            log ??= delegate { };

            MoveTree(BepInExRoot, storage.BepInExRoot, log);
            MoveTree(RuntimeRoot, storage.Runtime, log);

            foreach (string path in DoorstopFiles)
                Remove(path, log);

            foreach (string path in MelonLoaderEntries)
                Remove(path, log);
        }

        /// <summary>
        /// Deletes one file or folder. A failure is reported rather than thrown: the caller
        /// re-detects afterwards and refuses to start the game while anything can still boot ahead
        /// of the injection, which is the only outcome that actually matters here.
        /// </summary>
        private static void Remove(string path, Action<string> log)
        {
            try
            {
                if (Directory.Exists(path))
                    Directory.Delete(path, recursive: true);
                else
                    File.Delete(path);

                log("  deleted " + path);
            }
            catch (Exception ex)
            {
                log("  could NOT delete " + path + ": " + ex.Message);
            }
        }

        /// <summary>
        /// Moves one folder, declining rather than merging when the destination already holds files.
        /// A storage tree that is already laid out is the one the user has been running; silently
        /// burying it under an older copy would be the wrong way to resolve that.
        /// </summary>
        private static void MoveTree(string source, string destination, Action<string> log)
        {
            if (source == null || !Directory.Exists(source))
                return;

            if (SamePath(source, destination))
            {
                log("  already in place: " + destination);
                return;
            }

            if (Directory.Exists(destination) && HasAnyEntry(destination))
            {
                log("  kept the existing " + destination + " and left " + source + " alone");
                return;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(destination));

            try
            {
                Directory.Move(source, destination);
            }
            catch (IOException)
            {
                // Directory.Move cannot cross volumes, and storage defaults to %LocalLow% while the
                // install may be anywhere.
                CopyTree(source, destination);
                Directory.Delete(source, recursive: true);
            }

            log("  moved " + source + " -> " + destination);
        }

        private static void CopyTree(string source, string destination)
        {
            Directory.CreateDirectory(destination);

            foreach (string file in Directory.EnumerateFiles(source))
                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);

            foreach (string dir in Directory.EnumerateDirectories(source))
                CopyTree(dir, Path.Combine(destination, Path.GetFileName(dir)));
        }

        private static bool HasAnyEntry(string folder)
        {
            foreach (string unused in Directory.EnumerateFileSystemEntries(folder))
                return true;
            return false;
        }

        /// <summary>
        /// The BepInEx root behind a <c>target_assembly</c>. BepInEx derives its own root from the
        /// grandparent of that DLL — <c>&lt;root&gt;\BepInEx\core\BepInEx.Unity.IL2CPP.dll</c> — so
        /// this reads the path the same way the loader itself will.
        /// </summary>
        private static string RootOfLoader(string targetAssembly)
        {
            string core = DirectoryOfFile(targetAssembly);
            if (core == null)
                return null;

            DirectoryInfo root = Directory.GetParent(core);
            return root != null ? ExistingDirectory(root.FullName) : null;
        }

        private static string DirectoryOfFile(string file)
        {
            if (string.IsNullOrWhiteSpace(file))
                return null;
            try
            {
                return Path.GetDirectoryName(Path.GetFullPath(file));
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string ExistingDirectory(string path) =>
            path != null && Directory.Exists(path) ? Path.GetFullPath(path) : null;

        internal static bool SamePath(string a, string b)
        {
            if (a == null || b == null)
                return false;
            try
            {
                return string.Equals(Path.GetFullPath(a).TrimEnd(Path.DirectorySeparatorChar),
                                     Path.GetFullPath(b).TrimEnd(Path.DirectorySeparatorChar),
                                     StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// One key out of a doorstop ini. Hand-scanned rather than parsed: the file has a handful of
        /// keys, no duplicate names across its sections, and pulling in a parser for it would cost
        /// more than it is worth in a NativeAOT binary.
        /// </summary>
        private static string ReadIniValue(string path, string key)
        {
            if (!File.Exists(path))
                return null;

            string[] lines;
            try
            {
                lines = File.ReadAllLines(path);
            }
            catch (IOException)
            {
                return null;
            }

            foreach (string line in lines)
            {
                string text = line.Trim();
                if (text.Length == 0 || text[0] == '#' || text[0] == ';')
                    continue;

                int equals = text.IndexOf('=');
                if (equals < 0)
                    continue;

                if (!string.Equals(text.Substring(0, equals).Trim(), key, StringComparison.OrdinalIgnoreCase))
                    continue;

                string value = text.Substring(equals + 1).Trim();
                return value.Length > 0 ? value : null;
            }

            return null;
        }
    }
}
