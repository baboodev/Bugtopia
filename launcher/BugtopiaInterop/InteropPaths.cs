using System;
using System.Collections.Generic;
using System.IO;

namespace Bugtopia.Interop
{
    /// <summary>
    /// The two locations the generator needs, resolved and validated up front so every later failure
    /// is about BepInEx behaviour rather than about a wrong path.
    /// </summary>
    public sealed class InteropPaths
    {
        /// <summary>Full path to the Unity player executable (e.g. <c>...\Heartopia\xdt.exe</c>).</summary>
        public string GameExe { get; }

        /// <summary>Folder holding <see cref="GameExe"/>.</summary>
        public string GameRoot { get; }

        /// <summary>The <c>&lt;name&gt;_Data</c> folder next to the executable.</summary>
        public string GameDataDirectory { get; }

        /// <summary>
        /// The BepInEx root — the folder that directly contains <c>core</c>. BepInEx derives every
        /// other path (plugins, config, interop, unity-libs, cache, logs) from this one, so it is
        /// also where generation will write.
        /// </summary>
        public string BepInExRoot { get; }

        /// <summary><c>&lt;BepInExRoot&gt;\core</c>.</summary>
        public string CoreDirectory { get; }

        /// <summary>
        /// BepInEx's private CoreCLR folder (<c>dotnet</c>, the archive's second top-level directory).
        /// Required, and not obvious: several assemblies BepInEx's own code binds against — notably
        /// <c>Microsoft.Extensions.Logging</c>, which <c>Il2CppInteropManager</c>'s static constructor
        /// needs — ship there rather than in <c>core</c>. Probing only <c>core</c> fails.
        /// </summary>
        public string RuntimeDirectory { get; }

        private InteropPaths(string gameExe, string gameDataDirectory, string bepInExRoot, string runtimeDirectory)
        {
            GameExe = gameExe;
            GameRoot = Path.GetDirectoryName(gameExe);
            GameDataDirectory = gameDataDirectory;
            BepInExRoot = bepInExRoot;
            CoreDirectory = Path.Combine(bepInExRoot, "core");
            RuntimeDirectory = runtimeDirectory;
        }

        /// <summary>
        /// Resolves both locations. <paramref name="bepInExFolder"/> may be either the BepInEx root
        /// itself or a storage folder containing one — both spellings are accepted because the UI
        /// asks the user for a storage folder while BepInEx thinks in terms of its root.
        /// </summary>
        /// <exception cref="InteropSetupException">Any path that cannot be used, with the reason.</exception>
        /// <param name="runtimeFolder">
        /// Optional override for BepInEx's <c>dotnet</c> folder. When omitted it is looked for next to
        /// the BepInEx root, which is how the archive unpacks.
        /// </param>
        public static InteropPaths Resolve(string gameFolder, string bepInExFolder, string runtimeFolder = null)
        {
            if (string.IsNullOrWhiteSpace(gameFolder))
                throw new InteropSetupException("Game folder was not specified.");
            if (string.IsNullOrWhiteSpace(bepInExFolder))
                throw new InteropSetupException("BepInEx folder was not specified.");

            gameFolder = Path.GetFullPath(gameFolder.Trim());
            bepInExFolder = Path.GetFullPath(bepInExFolder.Trim());

            if (!Directory.Exists(gameFolder))
                throw new InteropSetupException("Game folder does not exist: " + gameFolder);

            string gameExe = FindUnityPlayer(gameFolder, out string gameData);
            string bepInExRoot = FindBepInExRoot(bepInExFolder);
            string runtime = FindRuntimeDirectory(bepInExRoot, runtimeFolder);
            return new InteropPaths(gameExe, gameData, bepInExRoot, runtime);
        }

        /// <summary>
        /// Finds the Unity player the same way BepInEx does: the executable whose name matches a
        /// sibling <c>&lt;name&gt;_Data</c> folder. Matching BepInEx's own rule matters — it is what
        /// <c>Paths.SetExecutablePath</c> will re-derive, and a mismatch there throws far from here.
        /// </summary>
        private static string FindUnityPlayer(string gameFolder, out string gameDataDirectory)
        {
            var found = new List<string>();
            string dataDir = null;

            foreach (string dir in Directory.GetDirectories(gameFolder, "*_Data"))
            {
                string name = Path.GetFileName(dir);
                name = name.Substring(0, name.Length - "_Data".Length);
                string exe = Path.Combine(gameFolder, name + ".exe");
                if (File.Exists(exe))
                {
                    found.Add(exe);
                    dataDir = dir;
                }
            }

            if (found.Count == 1)
            {
                if (!File.Exists(Path.Combine(gameFolder, "GameAssembly.dll")))
                {
                    throw new InteropSetupException(
                        "GameAssembly.dll is missing from " + gameFolder +
                        " — this does not look like an IL2CPP build.");
                }

                string metadata = Path.Combine(dataDir, "il2cpp_data", "Metadata", "global-metadata.dat");
                if (!File.Exists(metadata))
                    throw new InteropSetupException("global-metadata.dat is missing: " + metadata);

                gameDataDirectory = dataDir;
                return found[0];
            }

            if (found.Count == 0)
            {
                throw new InteropSetupException(
                    "No Unity player found in " + gameFolder +
                    " (looked for an <name>.exe next to an <name>_Data folder).");
            }

            throw new InteropSetupException(
                "Several Unity players found in " + gameFolder + ": " + string.Join(", ", found) +
                ". Point at a folder with exactly one.");
        }

        private static string FindBepInExRoot(string folder)
        {
            string direct = Path.Combine(folder, "core", "BepInEx.Unity.IL2CPP.dll");
            if (File.Exists(direct))
                return folder;

            string nested = Path.Combine(folder, "BepInEx");
            if (File.Exists(Path.Combine(nested, "core", "BepInEx.Unity.IL2CPP.dll")))
                return nested;

            throw new InteropSetupException(
                "No BepInEx install under " + folder +
                " (expected core\\BepInEx.Unity.IL2CPP.dll here or under a BepInEx subfolder). " +
                "If this is a freshly unpacked archive, make sure it is the Unity.IL2CPP win-x64 " +
                "flavour, not Unity.Mono or NET.Framework.");
        }

        private static string FindRuntimeDirectory(string bepInExRoot, string explicitFolder)
        {
            if (!string.IsNullOrWhiteSpace(explicitFolder))
            {
                string full = Path.GetFullPath(explicitFolder.Trim());
                if (!Directory.Exists(full))
                    throw new InteropSetupException("Runtime folder does not exist: " + full);
                return full;
            }

            DirectoryInfo parent = Directory.GetParent(bepInExRoot);
            if (parent != null)
            {
                string candidate = Path.Combine(parent.FullName, "dotnet");
                if (File.Exists(Path.Combine(candidate, "Microsoft.Extensions.Logging.dll")))
                    return candidate;
            }

            throw new InteropSetupException(
                "BepInEx's 'dotnet' folder was not found next to " + bepInExRoot +
                ". It ships in the same archive as BepInEx and holds assemblies the generator binds " +
                "against (Microsoft.Extensions.Logging among them). Unpack it alongside BepInEx, or " +
                "pass its location explicitly.");
        }
    }

    /// <summary>A path or install-layout problem — always actionable, never a bug report.</summary>
    public sealed class InteropSetupException : Exception
    {
        public InteropSetupException(string message) : base(message) { }
        public InteropSetupException(string message, Exception inner) : base(message, inner) { }
    }
}
