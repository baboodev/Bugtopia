using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace Bugtopia.Launch
{
    /// <summary>
    /// Which build of the game an install is. Not which store sold it: Steam and TapTap Global ship
    /// the same game, with the same save folder and the same servers, and differ only in the folder
    /// they install to. TapTap CN is a different game as far as the launcher is concerned - its own
    /// save folder, no server choice, its own game code, and started through its client.
    /// </summary>
    public enum GameEdition
    {
        Global,
        TapTapCn,
    }

    /// <summary>One Heartopia install on this machine, and what it takes to run it.</summary>
    public sealed class GameInstall
    {
        /// <summary>
        /// The CN build's product name as its <c>app.info</c> spells it. Written as escapes so the
        /// file reads the same in any editor and code page; it is data, not prose.
        /// </summary>
        public const string CnProduct = "心动小镇";

        public const string GlobalProduct = "Heartopia";

        /// <summary>
        /// How the CN build is started: through its client, which signs the player in first. Started
        /// directly, xdt.exe has no session to play with.
        /// </summary>
        public const string CnLaunchUri =
            "taptap://taptap.com/app?app_id=45213&auto_launch=true&ch_src=desktop---&game_type=pc&platform=pc";

        public GameInstall(string folder, GameEdition edition, string name)
        {
            Folder = folder;
            Edition = edition;
            Name = name;
        }

        public string Folder { get; }

        public GameEdition Edition { get; }

        /// <summary>What the list calls it: Steam, TapTap Global, TapTap CN - or Heartopia, for a folder found elsewhere.</summary>
        public string Name { get; }

        /// <summary>
        /// The Unity product name: the folder the game keeps its data in under <c>%LocalLow%\xd</c>,
        /// and the registry key its settings live under.
        /// </summary>
        public string DataProduct => Edition == GameEdition.TapTapCn ? CnProduct : GlobalProduct;

        /// <summary>Only the global build lets the player pick a server; the CN one has a single region.</summary>
        public bool HasServerChoice => Edition == GameEdition.Global;

        /// <summary>A URI that starts the game through its client, or null to start the executable directly.</summary>
        public string LaunchUri => Edition == GameEdition.TapTapCn ? CnLaunchUri : null;

        /// <summary>
        /// The game ends up running as administrator, so injecting into it takes administrator rights
        /// too. Known of the CN build, whose client elevates it - nothing in xdt.exe's or the client's
        /// manifest asks for that, so it can only be known, not read. Any other build that turns out
        /// elevated is caught when the injection is refused.
        /// </summary>
        public bool RunsElevated => Edition == GameEdition.TapTapCn;

        /// <summary>A short id for the window: "global" or "cn".</summary>
        public string EditionId => Edition == GameEdition.TapTapCn ? "cn" : "global";
    }

    /// <summary>
    /// Finds the Heartopia installs without asking.
    ///
    /// Steam first, and properly: the client's own library list, not a guess at where Steam put
    /// things. A fixed list of likely folders — which is all Vugtopia does — misses the common case
    /// entirely, because a Steam library can live on any drive the user added.
    ///
    /// Every install is reported, not just the first: someone with both the Steam and the TapTap CN
    /// build picks which one to launch, and the launcher remembers the pick.
    /// </summary>
    public static class GameDetection
    {
        private const string GameFolderName = "Heartopia";

        /// <summary>Heartopia's app id in the TapTap Global launcher, which names its install folder.</summary>
        private const string TapTapAppId = "231364";

        /// <summary>The player both builds ship, for a folder with no app.info to ask.</summary>
        private const string GameExeName = "xdt.exe";

        /// <summary>The first Heartopia install found, or null.</summary>
        public static string Detect()
        {
            List<GameInstall> all = DetectAll();
            return all.Count > 0 ? all[0].Folder : null;
        }

        /// <summary>
        /// Every Heartopia install found, each once, in search order. Only folders that identify as
        /// Heartopia count: a Steam library is enumerated folder by folder, and any other IL2CPP game
        /// in it would otherwise be offered as this one.
        /// </summary>
        public static List<GameInstall> DetectAll()
        {
            var found = new List<GameInstall>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach ((string candidate, string source) in Candidates())
            {
                string full;
                try
                {
                    full = Path.GetFullPath(candidate);
                }
                catch (Exception)
                {
                    continue;
                }

                if (!seen.Add(full) || !IsHeartopia(full))
                    continue;

                string folder = RealCase(full);
                GameEdition edition = EditionOf(folder);
                found.Add(new GameInstall(folder, edition, NameFor(edition, source)));
            }
            return found;
        }

        /// <summary>
        /// A folder picked by hand, described the way a found one is, or null when it is not a game
        /// folder at all. The store is guessed from the path, since nothing else says.
        /// </summary>
        public static GameInstall Describe(string folder)
        {
            if (!IsGameFolder(folder))
                return null;

            string full = RealCase(Path.GetFullPath(folder));
            GameEdition edition = EditionOf(full);
            string source =
                full.IndexOf(@"\steamapps\", StringComparison.OrdinalIgnoreCase) >= 0 ? "Steam"
                : full.IndexOf(@"\TapTapGlobal\", StringComparison.OrdinalIgnoreCase) >= 0 ? "TapTap Global"
                : null;
            return new GameInstall(full, edition, NameFor(edition, source));
        }

        private static string NameFor(GameEdition edition, string source) =>
            edition == GameEdition.TapTapCn ? "TapTap CN" : source ?? "Heartopia";

        /// <summary>
        /// Which build a folder holds, from the product name Unity wrote into <c>app.info</c>. The
        /// game's data folder and registry key are derived from that name, so it is the one fact that
        /// decides where the saves are - wherever the folder itself happens to be.
        /// </summary>
        public static GameEdition EditionOf(string folder) =>
            ReadAppInfo(folder).Product == GameInstall.CnProduct ? GameEdition.TapTapCn : GameEdition.Global;

        /// <summary>
        /// A game folder that is Heartopia: its app.info names xd and either build - or, with no
        /// app.info to read, its player is xdt.exe.
        /// </summary>
        private static bool IsHeartopia(string folder)
        {
            if (!IsGameFolder(folder))
                return false;

            (string company, string product) = ReadAppInfo(folder);
            if (product != null)
            {
                return string.Equals(company, "xd", StringComparison.OrdinalIgnoreCase) &&
                       (product == GameInstall.GlobalProduct || product == GameInstall.CnProduct);
            }
            return File.Exists(Path.Combine(folder, GameExeName));
        }

        /// <summary>
        /// Company and product from <c>&lt;name&gt;_Data\app.info</c>: two UTF-8 lines Unity writes at
        /// build time. Both null when there is no such file.
        /// </summary>
        private static (string Company, string Product) ReadAppInfo(string folder)
        {
            try
            {
                foreach (string dir in Directory.GetDirectories(folder, "*_Data"))
                {
                    string file = Path.Combine(dir, "app.info");
                    if (!File.Exists(file))
                        continue;

                    string[] lines = File.ReadAllText(file, System.Text.Encoding.UTF8).Split('\n');
                    if (lines.Length >= 2)
                        return (lines[0].Trim(), lines[1].Trim());
                }
            }
            catch (Exception)
            {
            }
            return (null, null);
        }

        /// <summary>
        /// True when the folder holds an IL2CPP build: an <c>&lt;name&gt;.exe</c> beside an
        /// <c>&lt;name&gt;_Data</c> folder, plus GameAssembly.dll. The same rule BepInEx uses to
        /// derive its own paths, so anything accepted here will still be accepted later.
        /// </summary>
        public static bool IsGameFolder(string folder)
        {
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return false;
            if (!File.Exists(Path.Combine(folder, "GameAssembly.dll")))
                return false;

            try
            {
                foreach (string dir in Directory.GetDirectories(folder, "*_Data"))
                {
                    string name = Path.GetFileName(dir);
                    name = name.Substring(0, name.Length - "_Data".Length);
                    if (File.Exists(Path.Combine(folder, name + ".exe")))
                        return true;
                }
            }
            catch (IOException)
            {
            }
            return false;
        }

        /// <summary>
        /// Rebuilds a path with the casing the filesystem actually uses. Steam stores its own path
        /// lowercased in the registry, and everything built on top of it inherits that — harmless to
        /// Windows, but this string is shown in the UI and written to config, so it should look like
        /// the folder it names.
        /// </summary>
        private static string RealCase(string path)
        {
            try
            {
                string root = Path.GetPathRoot(path);
                if (string.IsNullOrEmpty(root))
                    return path;

                string result = root.ToUpperInvariant();
                foreach (string segment in path.Substring(root.Length)
                                               .Split(Path.DirectorySeparatorChar,
                                                      StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] matches = Directory.GetFileSystemEntries(result, segment);
                    result = matches.Length == 1 ? matches[0] : Path.Combine(result, segment);
                }
                return result;
            }
            catch (Exception)
            {
                return path;
            }
        }

        /// <summary>
        /// Every folder <see cref="Detect"/> looks in, in order. Public so a failed detection can
        /// say where it looked - "the usual folders were checked" is not something anyone can act on.
        /// </summary>
        public static IEnumerable<string> SearchPaths()
        {
            foreach ((string folder, string _) in Candidates())
                yield return folder;
        }

        /// <summary>Folders to look in, each with the store that would have put the game there.</summary>
        private static IEnumerable<(string Folder, string Source)> Candidates()
        {
            foreach (string library in SteamLibraries())
            {
                string common = Path.Combine(library, "steamapps", "common");
                if (!Directory.Exists(common))
                    continue;

                // The conventional name first, then anything else in the library — a folder can be
                // renamed, and enumerating one directory is cheap.
                yield return (Path.Combine(common, GameFolderName), "Steam");

                string[] others;
                try
                {
                    others = Directory.GetDirectories(common);
                }
                catch (IOException)
                {
                    continue;
                }

                foreach (string dir in others)
                    yield return (dir, "Steam");
            }

            // The TapTap Global launcher installs to <drive>:\TapTapGlobal\Apps\<app id>, and which
            // drive is the user's choice — so every drive is tried rather than assuming C:.
            // The CN client installs to <drive>:\TapTap\PC Games\<name>. Its settings, which would say
            // which drive, are encrypted, so the same sweep serves it too, and the library is listed
            // like a Steam one rather than trusted to hold a folder of the expected name.
            foreach (DriveInfo drive in ReadyDrives())
            {
                yield return (Path.Combine(drive.Name, "TapTapGlobal", "Apps", TapTapAppId), "TapTap Global");

                string cnLibrary = Path.Combine(drive.Name, "TapTap", "PC Games");
                if (!Directory.Exists(cnLibrary))
                    continue;

                yield return (Path.Combine(cnLibrary, GameFolderName), "TapTap CN");

                string[] others;
                try
                {
                    others = Directory.GetDirectories(cnLibrary);
                }
                catch (Exception)
                {
                    continue;
                }

                foreach (string dir in others)
                    yield return (dir, "TapTap CN");
            }

            // Other non-Steam installs.
            foreach (string root in new[] { @"C:\Program Files", @"C:\Program Files (x86)",
                                            @"C:\Games", @"D:\Games", @"E:\Games" })
            {
                yield return (Path.Combine(root, GameFolderName), null);
            }
        }

        /// <summary>
        /// Drives worth looking at: the ones that are present and not on the far end of a network.
        ///
        /// <see cref="DriveInfo.IsReady"/> is what keeps an empty card reader from being probed, and
        /// network drives are left out entirely — a disconnected one can block for seconds, and this
        /// runs while the window is coming up.
        /// </summary>
        private static IEnumerable<DriveInfo> ReadyDrives()
        {
            DriveInfo[] drives;
            try
            {
                drives = DriveInfo.GetDrives();
            }
            catch (Exception)
            {
                yield break;
            }

            foreach (DriveInfo drive in drives)
            {
                bool usable;
                try
                {
                    usable = (drive.DriveType == DriveType.Fixed || drive.DriveType == DriveType.Removable)
                             && drive.IsReady;
                }
                catch (Exception)
                {
                    usable = false;
                }

                if (usable)
                    yield return drive;
            }
        }

        /// <summary>
        /// Every Steam library folder: the client's install plus whatever
        /// <c>steamapps\libraryfolders.vdf</c> lists.
        /// </summary>
        private static IEnumerable<string> SteamLibraries()
        {
            string steam = SteamPath();
            if (steam == null)
                yield break;

            yield return steam;

            string vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdf))
                yield break;

            string text;
            try
            {
                text = File.ReadAllText(vdf);
            }
            catch (IOException)
            {
                yield break;
            }

            // Entries look like:  "path"    "D:\\SteamLibrary"
            // Scanned by hand rather than with a regex: one fixed key in one small file does not
            // justify linking the regex engine, which is about half a megabyte under NativeAOT.
            foreach (string line in text.Split('\n'))
            {
                int key = line.IndexOf("\"path\"", StringComparison.OrdinalIgnoreCase);
                if (key < 0)
                    continue;

                int open = line.IndexOf('"', key + 6);
                if (open < 0)
                    continue;
                int close = line.IndexOf('"', open + 1);
                if (close < 0)
                    continue;

                string path = line.Substring(open + 1, close - open - 1).Replace(@"\\", @"\").Trim();
                if (path.Length > 0 && !string.Equals(path, steam, StringComparison.OrdinalIgnoreCase))
                    yield return path;
            }
        }

        private static string SteamPath()
        {
            // Per-user first: it is where the running client records itself, and it needs no
            // elevation to read.
            foreach ((RegistryKey root, string subKey, string value) in new[]
                     {
                         (Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath"),
                         (Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
                         (Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath"),
                     })
            {
                try
                {
                    using RegistryKey key = root.OpenSubKey(subKey);
                    if (key?.GetValue(value) is string path && path.Length > 0)
                    {
                        // SteamPath is written with forward slashes.
                        path = path.Replace('/', '\\');
                        if (Directory.Exists(path))
                            return path;
                    }
                }
                catch (Exception)
                {
                }
            }
            return null;
        }
    }
}
