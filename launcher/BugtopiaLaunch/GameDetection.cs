using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace Bugtopia.Launch
{
    /// <summary>
    /// Finds the Heartopia install without asking.
    ///
    /// Steam first, and properly: the client's own library list, not a guess at where Steam put
    /// things. A fixed list of likely folders — which is all Vugtopia does — misses the common case
    /// entirely, because a Steam library can live on any drive the user added.
    /// </summary>
    public static class GameDetection
    {
        private const string GameFolderName = "Heartopia";

        /// <summary>
        /// The first candidate that really is an IL2CPP Heartopia install, or null.
        /// </summary>
        public static string Detect()
        {
            foreach (string candidate in Candidates())
            {
                if (IsGameFolder(candidate))
                    return RealCase(Path.GetFullPath(candidate));
            }
            return null;
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

        private static IEnumerable<string> Candidates()
        {
            foreach (string library in SteamLibraries())
            {
                string common = Path.Combine(library, "steamapps", "common");
                if (!Directory.Exists(common))
                    continue;

                // The conventional name first, then anything else in the library — a folder can be
                // renamed, and enumerating one directory is cheap.
                yield return Path.Combine(common, GameFolderName);

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
                    yield return dir;
            }

            // Non-Steam installs, including the TapTap Global launcher's app folder.
            yield return @"C:\TapTapGlobal\Apps\231364";
            foreach (string root in new[] { @"C:\Program Files", @"C:\Program Files (x86)",
                                            @"C:\Games", @"D:\Games", @"E:\Games" })
            {
                yield return Path.Combine(root, GameFolderName);
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
