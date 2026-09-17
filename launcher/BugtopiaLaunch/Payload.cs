using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;

namespace Bugtopia.Launch
{
    /// <summary>A file the launcher carries and writes into the storage tree.</summary>
    public sealed class PayloadFile
    {
        public PayloadFile(string relativePath, Func<Stream> open, bool required = true)
        {
            RelativePath = relativePath;
            Open = open;
            Required = required;
        }

        /// <summary>Where it goes, relative to the storage root (e.g. <c>BepInEx\plugins\bugtopia.dll</c>).</summary>
        public string RelativePath { get; }

        public Func<Stream> Open { get; }

        /// <summary>A missing optional file is reported and skipped; a missing required one fails Prepare.</summary>
        public bool Required { get; }

        /// <summary>Reads a file the launcher embedded as a resource.</summary>
        public static PayloadFile FromResource(Assembly assembly, string resourceName, string relativePath,
                                               bool required = true)
        {
            return new PayloadFile(relativePath, () => assembly.GetManifestResourceStream(resourceName), required);
        }

        /// <summary>Reads a file from disk — how the tooling uses it before anything is embedded.</summary>
        public static PayloadFile FromDisk(string sourcePath, string relativePath, bool required = true)
        {
            return new PayloadFile(
                relativePath,
                () => File.Exists(sourcePath) ? File.OpenRead(sourcePath) : null,
                required);
        }
    }

    /// <summary>
    /// Lays the storage tree out from a user-supplied BepInEx archive plus the files the launcher
    /// carries. See docs/plans/2026-08-27-bepinex-injector.md section 5.1.
    /// </summary>
    public static class Payload
    {
        /// <summary>
        /// Checks that a folder the user picked really is an unpacked
        /// <c>BepInEx-Unity.IL2CPP-win-x64</c> archive, and says which file is missing when it is not.
        /// This is the check that catches the likeliest mistake — downloading the Unity.Mono or
        /// NET.Framework flavour, neither of which has both of these.
        /// </summary>
        public static void ValidateSource(string sourceFolder, out string coreDir, out string runtimeDir)
        {
            if (string.IsNullOrWhiteSpace(sourceFolder) || !Directory.Exists(sourceFolder))
                throw new PayloadException("BepInEx folder does not exist: " + sourceFolder);

            string root = Path.GetFullPath(sourceFolder);

            // Accept both the archive root and the BepInEx folder inside it.
            coreDir = Path.Combine(root, "BepInEx", "core");
            if (!File.Exists(Path.Combine(coreDir, "BepInEx.Unity.IL2CPP.dll")))
                coreDir = Path.Combine(root, "core");

            if (!File.Exists(Path.Combine(coreDir, "BepInEx.Unity.IL2CPP.dll")))
            {
                throw new PayloadException(
                    "No BepInEx.Unity.IL2CPP.dll under " + root + ". This must be the " +
                    "BepInEx-Unity.IL2CPP-win-x64 archive — the Unity.Mono and NET.Framework builds " +
                    "do not contain it.");
            }

            // dotnet\ sits beside BepInEx\ in the archive; when the user picked the BepInEx folder
            // itself, it is one level up.
            runtimeDir = Path.Combine(root, "dotnet");
            if (!File.Exists(Path.Combine(runtimeDir, "coreclr.dll")))
            {
                DirectoryInfo parent = Directory.GetParent(root);
                if (parent != null)
                    runtimeDir = Path.Combine(parent.FullName, "dotnet");
            }

            if (!File.Exists(Path.Combine(runtimeDir, "coreclr.dll")))
            {
                throw new PayloadException(
                    "No dotnet\\coreclr.dll beside " + root + ". It ships in the same archive as " +
                    "BepInEx and holds the runtime the bootstrap hosts; unpack the whole archive, " +
                    "not just the BepInEx folder.");
            }
        }

        /// <summary>
        /// Unpacks a BepInEx archive, replacing whatever is in the target folder.
        ///
        /// Taking the zip rather than an unpacked folder is what lets the simple screen ask for the
        /// file exactly as it was downloaded: no unpacking step to explain, and no chance of
        /// pointing at the wrong level inside the result.
        /// </summary>
        public static void UnpackArchive(string zipPath, string targetFolder, Action<string> log = null)
        {
            log ??= delegate { };
            if (string.IsNullOrWhiteSpace(zipPath) || !File.Exists(zipPath))
                throw new PayloadException("No such archive: " + zipPath);

            log("Unpacking " + zipPath);
            Directory.CreateDirectory(targetFolder);
            ZipFile.ExtractToDirectory(zipPath, targetFolder, overwriteFiles: true);
            log("Unpacked to " + targetFolder);
        }

        /// <summary>Reads the BepInEx build out of the archive, for the version stamp and the status line.</summary>
        public static string ReadBepInExVersion(string coreDir)
        {
            string dll = Path.Combine(coreDir, "BepInEx.Core.dll");
            if (!File.Exists(dll))
                return null;
            try
            {
                return System.Diagnostics.FileVersionInfo.GetVersionInfo(dll).ProductVersion;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Copies <c>BepInEx\core</c> and <c>dotnet</c> into storage and writes the carried files.
        ///
        /// Both directories are copied rather than referenced in place: BepInEx will write its config,
        /// its logs, the plugin and 81 MB of interop next to <c>core</c>, and that must not land in
        /// whatever folder the user unpacked their download to. Copying <c>dotnet</c> too is not
        /// forced — only our own bootstrap reads it, by absolute path — but it makes the storage
        /// folder self-contained and the source folder disposable.
        /// </summary>
        public static void Prepare(string sourceFolder, StorageLayout storage,
                                   IEnumerable<PayloadFile> files, Action<string> log = null)
        {
            log ??= delegate { };
            ValidateSource(sourceFolder, out string coreDir, out string runtimeDir);

            foreach (string dir in new[]
                     {
                         storage.Root, storage.BepInExRoot, storage.Plugins, storage.Patchers,
                         storage.Config, storage.UnityLibs, storage.Bin
                     })
            {
                Directory.CreateDirectory(dir);
            }

            // An adopted install is its own source: its core and dotnet already sit where they are
            // being copied to, and File.Copy onto itself throws. Skipping is not a special case so
            // much as the honest answer to "copy this here".
            if (ExistingInstall.SamePath(coreDir, storage.Core))
            {
                log("  core: already in place");
            }
            else
            {
                log("Copying BepInEx core...");
                CopyDirectory(coreDir, storage.Core, log);
            }

            if (ExistingInstall.SamePath(runtimeDir, storage.Runtime))
            {
                log("  dotnet: already in place");
            }
            else
            {
                log("Copying the .NET runtime...");
                CopyDirectory(runtimeDir, storage.Runtime, log);
            }

            foreach (PayloadFile file in files ?? Array.Empty<PayloadFile>())
            {
                string target = Path.Combine(storage.Root, file.RelativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(target));

                using Stream source = file.Open();
                if (source == null)
                {
                    if (file.Required)
                        throw new PayloadException("Missing payload file: " + file.RelativePath);
                    log("  skipped (not carried): " + file.RelativePath);
                    continue;
                }

                using FileStream destination = File.Create(target);
                source.CopyTo(destination);
                log("  " + file.RelativePath);
            }

            // The bootstrap prefers BUGTOPIA_STORAGE, which Play sets on the child process. This file
            // is the fallback that also makes a hand-driven injection work.
            File.WriteAllText(storage.InjectConfig, "storage=" + storage.Root + Environment.NewLine);

            ApplyLoggingDefaults(storage, log);

            log("Prepared: " + storage.Root);
        }

        /// <summary>
        /// Writes the carried files storage is missing, and - when <paramref name="replaceExisting"/>
        /// - replaces the ones that differ. Returns false when a file could not be written.
        ///
        /// <see cref="Prepare"/> writes them once, when the tree is laid out, and a prepared tree is
        /// never laid out again - so without this a newer launcher went on starting the bootstrap
        /// and the mod an older one had left. The caller replaces only when a different launcher ran
        /// here last (<see cref="StorageLayout.CarriedStamp"/>): replacing whatever differed on every
        /// launch also undid a mod DLL put in place by hand, every time. Once a launcher has run, what
        /// is installed is left alone, and a copy that no longer matches is logged as kept.
        ///
        /// A file the running game holds open cannot be replaced; that is logged and the installed
        /// copy is used, as with a failed mod update. The bytes go to a side file first and are moved
        /// over the old one, so a failed write never leaves half a DLL behind.
        /// </summary>
        public static bool RefreshCarried(StorageLayout storage, IEnumerable<PayloadFile> files,
                                          Action<string> log = null, bool replaceExisting = true)
        {
            log ??= delegate { };
            bool complete = true;

            foreach (PayloadFile file in files ?? Array.Empty<PayloadFile>())
            {
                string target = Path.Combine(storage.Root, file.RelativePath);
                string pending = target + ".new";

                try
                {
                    bool existed = File.Exists(target);
                    using (Stream carried = file.Open())
                    {
                        // Not carried by this build: the online one has no mod inside it.
                        if (carried == null || (existed && SameContent(carried, target)))
                            continue;
                    }

                    if (existed && !replaceExisting)
                    {
                        string kept = VersionOf(target);
                        log("  kept " + file.RelativePath + (kept != null ? " (" + kept + ")" : "") +
                            ": it differs from this launcher's copy, which already ran here - delete it " +
                            "to have that copy written again");
                        continue;
                    }

                    string before = existed ? VersionOf(target) : null;

                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    using (Stream source = file.Open())
                    using (FileStream destination = File.Create(pending))
                    {
                        source.CopyTo(destination);
                    }
                    File.Move(pending, target, overwrite: true);

                    string after = VersionOf(target);
                    log("  " + (existed ? "replaced " : "written ") + file.RelativePath +
                        (before != null && after != null ? ": " + before + " -> " + after
                         : after != null ? ": " + after
                         : ""));
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    TryDelete(pending);
                    complete = false;
                    log("  could not update " + file.RelativePath + " (" + ex.Message +
                        ") - the installed copy stays");
                }
            }
            return complete;
        }

        /// <summary>
        /// The version a PE file declares - product version first, since that carries the commit -
        /// or null when there is no file or it declares none.
        /// </summary>
        public static string VersionOf(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return null;

                var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);
                string version = !string.IsNullOrWhiteSpace(info.ProductVersion) ? info.ProductVersion : info.FileVersion;
                return string.IsNullOrWhiteSpace(version) ? null : version.Trim();
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static bool SameContent(Stream carried, string path)
        {
            using FileStream installed = File.OpenRead(path);
            if (carried.CanSeek && carried.Length != installed.Length)
                return false;

            var a = new byte[81920];
            var b = new byte[81920];
            while (true)
            {
                int readA = carried.ReadAtLeast(a, a.Length, throwOnEndOfStream: false);
                int readB = installed.ReadAtLeast(b, b.Length, throwOnEndOfStream: false);
                if (readA != readB)
                    return false;
                if (readA == 0)
                    return true;
                if (!a.AsSpan(0, readA).SequenceEqual(b.AsSpan(0, readB)))
                    return false;
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// Copies a directory's files, skipping the XML documentation that ships beside BepInEx's
        /// assemblies — nothing reads it at runtime and it is four files of pure noise in the tree.
        /// </summary>
        private static void CopyDirectory(string source, string destination, Action<string> log)
        {
            Directory.CreateDirectory(destination);
            int copied = 0, skipped = 0;

            foreach (string file in Directory.EnumerateFiles(source))
            {
                if (string.Equals(Path.GetExtension(file), ".xml", StringComparison.OrdinalIgnoreCase))
                {
                    skipped++;
                    continue;
                }

                File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
                copied++;
            }

            foreach (string dir in Directory.EnumerateDirectories(source))
                CopyDirectory(dir, Path.Combine(destination, Path.GetFileName(dir)), log);

            log($"  {Path.GetFileName(destination)}: {copied} files" +
                (skipped > 0 ? $" ({skipped} XML docs skipped)" : ""));
        }

        /// <summary>
        /// Settings a fresh BepInEx.cfg gets wrong for this mod, written during Prepare.
        ///
        /// <c>UnityLogListening</c> is the one that matters. Left at its default, the chainloader
        /// installs a Unity log handler before any plugin loads, and that pulls in Il2CppInterop's
        /// delegate support — which applies the ClassInjector hooks the mod's own HookTrim exists to
        /// suppress. Measured: with it on, HookTrim reports "TOO LATE" and 5 of 5 hooks install; with
        /// it off, it suppresses 2 and only 3 install. The console is off because this launcher has
        /// its own log pane and a stray console window is noise.
        /// </summary>
        private static readonly (string Section, string Key, string Value)[] LoggingDefaults =
        {
            ("Logging", "UnityLogListening", "false"),
            ("Logging.Console", "Enabled", "false"),
            ("Logging.Disk", "WriteUnityLog", "false"),
        };

        /// <summary>
        /// Applies <see cref="LoggingDefaults"/> to BepInEx.cfg, in place and without disturbing
        /// anything else. BepInEx keeps values for entries it has not bound yet, so writing these
        /// before its first run is enough — and editing rather than replacing means a config the user
        /// has since tuned keeps every other choice they made.
        /// </summary>
        public static void ApplyLoggingDefaults(StorageLayout storage, Action<string> log = null)
        {
            log ??= delegate { };
            string path = Path.Combine(storage.Config, "BepInEx.cfg");
            Directory.CreateDirectory(storage.Config);

            List<string> lines = File.Exists(path)
                ? new List<string>(File.ReadAllLines(path))
                : new List<string>();

            foreach ((string section, string key, string value) in LoggingDefaults)
            {
                if (SetIniValue(lines, section, key, value))
                    log($"  BepInEx.cfg [{section}] {key} = {value}");
            }

            File.WriteAllLines(path, lines);
        }

        /// <summary>Sets one key inside one section, adding either if missing. True when it changed.</summary>
        private static bool SetIniValue(List<string> lines, string section, string key, string value)
        {
            string header = "[" + section + "]";
            int sectionStart = -1;

            for (int i = 0; i < lines.Count; i++)
            {
                if (string.Equals(lines[i].Trim(), header, StringComparison.OrdinalIgnoreCase))
                {
                    sectionStart = i;
                    break;
                }
            }

            if (sectionStart < 0)
            {
                if (lines.Count > 0 && lines[lines.Count - 1].Trim().Length > 0)
                    lines.Add("");
                lines.Add(header);
                lines.Add(key + " = " + value);
                return true;
            }

            for (int i = sectionStart + 1; i < lines.Count; i++)
            {
                string line = lines[i];
                if (line.TrimStart().StartsWith("[", StringComparison.Ordinal))
                    break;

                int equals = line.IndexOf('=');
                if (equals < 0 || line.TrimStart().StartsWith("#", StringComparison.Ordinal))
                    continue;

                if (!string.Equals(line.Substring(0, equals).Trim(), key, StringComparison.OrdinalIgnoreCase))
                    continue;

                string current = line.Substring(equals + 1).Trim();
                if (string.Equals(current, value, StringComparison.OrdinalIgnoreCase))
                    return false;

                lines[i] = key + " = " + value;
                return true;
            }

            // Section exists, key does not: put it directly under the header.
            lines.Insert(sectionStart + 1, key + " = " + value);
            return true;
        }

        /// <summary>
        /// Puts a user-downloaded Unity base-libraries zip where BepInEx will find it. BepInEx uses a
        /// zip already present in unity-libs whose name matches the resolved URL instead of
        /// downloading, so copying the file in is the whole of the offline path — no config edit.
        /// </summary>
        public static void InstallUnityLibs(string zipPath, StorageLayout storage, Action<string> log = null)
        {
            log ??= delegate { };
            if (string.IsNullOrWhiteSpace(zipPath))
                return;
            if (!File.Exists(zipPath))
                throw new PayloadException("No such Unity base libraries zip: " + zipPath);

            Directory.CreateDirectory(storage.UnityLibs);
            string target = Path.Combine(storage.UnityLibs, Path.GetFileName(zipPath));
            File.Copy(zipPath, target, overwrite: true);
            log("Unity base libraries: " + target);
        }
    }

    public sealed class PayloadException : Exception
    {
        public PayloadException(string message) : base(message) { }
    }
}
