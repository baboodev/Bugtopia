using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace BugtopiaMcp
{
    // ────────────────────────────────────────────────────────────────────────────────────────────
    // Making `eval` usable when the game runs on ANOTHER machine.
    //
    // THE FAILURE THIS EXISTS FOR. EvalCompiler takes its reference set from the game's own `env`
    // op — modAssembly / interopDir / coreDir / runtimeDir are paths as the GAME sees them. Roslyn
    // then compiles HERE, in this process. So when the game lives on a second box (the Ideapad runs
    // as its own user, C:\Users\coderus\…), every reported path is missing locally, AddDirectory
    // skips a non-existent directory without complaint, and the compile ends up with zero
    // references — which does not surface as "wrong machine". It surfaces as
    //      error CS0518: Predefined type 'System.Object' is not defined or imported
    //      error CS0246: The type or namespace name 'System' could not be found
    // on every snippet, including `return host.IsWorldReady;`. An agent reading that debugs its own
    // snippet, then the mod, then the game, and never finds the actual cause, which is a path list.
    //
    // WHAT THIS DOES. If the reported set is not usable on THIS machine, build an equivalent one
    // from what this checkout has: the mod assembly the repo last built, the interop folder this
    // machine's own Heartopia install generated, and a shared-framework folder of the same .NET
    // generation the game's plugin host runs on (net6.0) — that last one is what actually rescues
    // the CS0518 family, because it is where System.Private.CoreLib lives.
    //
    // WHAT IT COSTS, SAID OUT LOUD. The primary path (references from the game) compiles against
    // the EXACT assemblies the session loaded. A fallback compile does not: the locally built
    // bugtopia.dll may be a different commit than the one the game is running, and a local interop
    // folder may come from a different game build. Il2CppInterop assemblies carry no version, so
    // the runtime will not reject a mismatch — it will simply fail to find the member when the
    // snippet runs. So the fallback is loud: the source is named in the response of every eval,
    // and the safe way to write a remote snippet is `host` (the plugin contract, binary-compatible
    // by PluginSdk.Version) plus reflection over `mod`, rather than direct calls into the mod's
    // surface. The alternative that does not exist: silently returning zero references.
    // ────────────────────────────────────────────────────────────────────────────────────────────
    internal static class EvalReferenceFallback
    {
        /// Explicit escape hatch: BUGTOPIA_EVAL_REFS="<dir>;<dir>;…" wins over everything, because a
        /// machine with an unusual layout should not have to wait for a rebuild of the bridge.
        private const string OverrideVariable = "BUGTOPIA_EVAL_REFS";

        internal static EnvPaths Build(EnvPaths reported, out string source)
        {
            source = "game (as reported by `env`)";
            if (UsableLocally(reported))
            {
                return reported;
            }

            List<string> notes = new List<string>();

            // 1) The operator knows best, when the operator says anything at all.
            string[] overrides = (Environment.GetEnvironmentVariable(OverrideVariable) ?? string.Empty)
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

            // 2) Otherwise read this checkout: buddy/Directory.Build.props is where the game folder
            //    and the out-of-folder BepInEx live for whoever is running this bridge.
            string repoRoot = FindRepoRoot();
            string buildProps = repoRoot == null ? null : Path.Combine(repoRoot, "buddy", "Directory.Build.props");
            string propsInterop = PropValue(buildProps, "BepInExDir");
            string propsGame = PropValue(buildProps, "HeartopiaDir");

            EnvPaths local = new EnvPaths
            {
                ModAssembly = Pick(overrides, notes, "mod", FirstFile(
                    repoRoot == null ? null : Path.Combine(repoRoot, "buddy", "bin"),
                    "bugtopia.dll",
                    new[] { Path.Combine("BepInEx", "Release"), Path.Combine("Universal", "Release"),
                           Path.Combine("MelonLoader", "Release") }),
                    propsGame == null ? null : Path.Combine(propsGame, "BepInEx", "plugins", "bugtopia.dll")),

                InteropDir = Pick(overrides, notes, "interop", FirstExisting(new[]
                {
                    propsInterop == null ? null : Path.Combine(propsInterop, "interop"),
                    propsGame == null ? null : Path.Combine(propsGame, "BepInEx", "interop"),
                    propsGame == null ? null : Path.Combine(propsGame, "MelonLoader", "Il2CppAssemblies"),
                })),

                // In this checkout BepInEx lives outside the game folder (BepInExDir/core); a game
                // installed with in-folder BepInEx keeps the same layout under HeartopiaDir — try both.
                CoreDir = Pick(overrides, notes, "core", FirstExisting(new[]
                {
                    propsInterop == null ? null : Path.Combine(propsInterop, "core"),
                    propsGame == null ? null : Path.Combine(propsGame, "BepInEx", "core"),
                })),

                RuntimeDir = Pick(overrides, notes, "bcl", SharedFrameworkDir()),
            };

            // Report WHICH set was used, not just which one was asked for: a compile that quietly
            // substituted local copies while claiming the game's assemblies is the exact lie this
            // whole file exists to avoid.
            local.Source = "LOCAL FALLBACK: " + string.Join(", ", notes.ToArray());
            source = local.Source;
            return local;
        }

        private static bool UsableLocally(EnvPaths paths)
        {
            return paths != null && !string.IsNullOrEmpty(paths.ModAssembly) && File.Exists(paths.ModAssembly)
                && !string.IsNullOrEmpty(paths.InteropDir) && Directory.Exists(paths.InteropDir)
                && !string.IsNullOrEmpty(paths.RuntimeDir) && Directory.Exists(paths.RuntimeDir);
        }

        // One note per slot so the response names what the snippet was actually built against —
        // "compiled against 214 references" is only honest if you can also say which 214.
        private static string Pick(string[] overrides, List<string> notes, string slot, params string[] candidates)
        {
            foreach (string dir in overrides)
            {
                if (Directory.Exists(dir) && Contains(slot, dir))
                {
                    notes.Add(slot + "=" + dir + " (override)");
                    return dir;
                }
            }

            foreach (string candidate in candidates)
            {
                if (string.IsNullOrEmpty(candidate))
                {
                    continue;
                }

                if (File.Exists(candidate))
                {
                    notes.Add(slot + "=" + candidate);
                    return candidate;
                }

                if (Directory.Exists(candidate))
                {
                    notes.Add(slot + "=" + candidate + " (" + CountDlls(candidate) + " dll)");
                    return candidate;
                }
            }

            notes.Add(slot + "=none");
            return null;
        }

        private static bool Contains(string slot, string dir)
        {
            switch (slot)
            {
                case "mod":
                    return File.Exists(Path.Combine(dir, "bugtopia.dll"));
                case "bcl":
                    return File.Exists(Path.Combine(dir, "System.Private.CoreLib.dll"));
                default:
                    return CountDlls(dir) > 0;
            }
        }

        private static int CountDlls(string dir)
        {
            try
            {
                return Directory.GetFiles(dir, "*.dll").Length;
            }
            catch (Exception)
            {
                return 0;
            }
        }

        // The bridge is launched as <repo>/tools/BugtopiaMcp/bin/Release/net10.0/BugtopiaMcp.dll, so
        // the repo root is found by walking up until buddy/buddy.csproj appears — which keeps working
        // if the output folder depth changes.
        private static string FindRepoRoot()
        {
            try
            {
                DirectoryInfo dir = new DirectoryInfo(Path.GetDirectoryName(typeof(EvalReferenceFallback).Assembly.Location));
                for (int hop = 0; dir != null && hop < 8; hop++, dir = dir.Parent)
                {
                    if (File.Exists(Path.Combine(dir.FullName, "buddy", "buddy.csproj")))
                    {
                        return dir.FullName;
                    }
                }
            }
            catch (Exception)
            {
                // No repo root means no fallback candidates, which the caller reports as slot=none.
            }

            return null;
        }

        // A plain <Key>value</Key> read, not an MSBuild evaluation: the two keys this needs
        // (BepInExDir, HeartopiaDir) are literal paths in this repo's props file, and pulling in
        // Microsoft.Build would mean a second heavy package for two strings.
        private static string PropValue(string propsFile, string key)
        {
            if (string.IsNullOrEmpty(propsFile) || !File.Exists(propsFile))
            {
                return null;
            }

            try
            {
                Match match = Regex.Match(File.ReadAllText(propsFile), "<" + key + ">([^<]+)</" + key + ">");
                return match.Success ? match.Groups[1].Value.Trim() : null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static string FirstFile(string root, string fileName, string[] preferredRelativeDirs)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                return null;
            }

            foreach (string relative in preferredRelativeDirs)
            {
                string candidate = Path.Combine(root, relative, fileName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            try
            {
                foreach (string found in Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories))
                {
                    return found;
                }
            }
            catch (Exception)
            {
                return null;
            }

            return null;
        }

        private static string FirstExisting(string[] candidates)
        {
            foreach (string candidate in candidates)
            {
                if (!string.IsNullOrEmpty(candidate) && Directory.Exists(candidate))
                {
                    return candidate;
                }
            }

            return null;
        }

        // The BCL side of the reported `runtimeDir` (the game's own runtime\dotnet\). A shared
        // framework folder of the SAME generation as the game's plugin host (net6.0) is the closest
        // local equivalent, and it is the one that fixes CS0518 — System.Private.CoreLib is where the
        // predefined types come from. Newest matching band first, so a 6.0.36 beats a 6.0.26 and a
        // 10.0.8 is only used when nothing 6.x is installed (that mismatch is visible in the source
        // line the response prints).
        private static string SharedFrameworkDir()
        {
            string root = DotnetRoot();
            if (root == null)
            {
                return null;
            }

            string shared = Path.Combine(root, "shared", "Microsoft.NETCore.App");
            if (!Directory.Exists(shared))
            {
                return null;
            }

            List<string> versions = new List<string>();
            try
            {
                versions.AddRange(Directory.GetDirectories(shared));
            }
            catch (Exception)
            {
                return null;
            }

            // Order by parsed Version (review 2026-11-01): an ordinal sort+reverse ranks "6.0.9"
            // above "6.0.10" and "6.0.4" above "6.0.36" — it would pick an OLDER patch, re-opening
            // the "member compiles but is missing at runtime" mismatch this file exists to
            // minimize. Non-version directory names are skipped.
            var sorted = new List<KeyValuePair<string, Version>>(versions.Count);
            foreach (string dir in versions)
            {
                if (Version.TryParse(Path.GetFileName(dir), out Version ver))
                {
                    sorted.Add(new KeyValuePair<string, Version>(dir, ver));
                }
            }
            sorted.Sort((a, b) => b.Value.CompareTo(a.Value));

            foreach (string band in new[] { "6.", "8.", "9.", "10." })
            {
                foreach (KeyValuePair<string, Version> entry in sorted)
                {
                    string name = Path.GetFileName(entry.Key);
                    if (name.StartsWith(band, StringComparison.Ordinal)
                        && File.Exists(Path.Combine(entry.Key, "System.Private.CoreLib.dll")))
                    {
                        return entry.Key;
                    }
                }
            }

            return null;
        }

        private static string DotnetRoot()
        {
            try
            {
                if (!string.IsNullOrEmpty(Environment.ProcessPath))
                {
                    string parent = Path.GetDirectoryName(Environment.ProcessPath);
                    if (parent != null && Directory.Exists(Path.Combine(parent, "shared")))
                    {
                        return parent;
                    }
                }
            }
            catch (Exception)
            {
                // fall through to the environment
            }

            string fromEnv = Environment.GetEnvironmentVariable("DOTNET_ROOT");
            if (!string.IsNullOrEmpty(fromEnv) && Directory.Exists(fromEnv))
            {
                return fromEnv;
            }

            string programFiles = Environment.GetEnvironmentVariable("ProgramFiles");
            string guess = programFiles == null ? null : Path.Combine(programFiles, "dotnet");
            return guess != null && Directory.Exists(guess) ? guess : null;
        }
    }
}
