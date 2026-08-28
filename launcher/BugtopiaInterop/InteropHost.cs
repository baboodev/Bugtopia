using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;

namespace Bugtopia.Interop
{
    /// <summary>
    /// Hosts BepInEx's interop generator in this process, with no game running.
    ///
    /// Order matters and is not negotiable (all of it verified against BepInEx 6.0.0-be.785):
    ///   1. assembly probe over &lt;BepInEx&gt;\core must be installed before any BepInEx type is touched;
    ///   2. <c>Paths.SetExecutablePath</c> must run before <c>Il2CppInteropManager</c> is touched at all,
    ///      because that type's static constructor binds <c>ConfigFile.CoreConfig</c>, which resolves
    ///      <c>Paths.BepInExConfigPath</c> and writes BepInEx.cfg;
    ///   3. <c>UnityInfo.Initialize</c> must run before generation, which needs the Unity version.
    ///
    /// Side effect worth knowing: step 2 creates or rewrites <c>&lt;BepInEx&gt;\config\BepInEx.cfg</c>.
    /// Even <see cref="ComputeHash"/> is therefore not strictly read-only.
    /// </summary>
    public sealed class InteropHost : IDisposable
    {
        private const string HashFileName = "assembly-hash.txt";

        private static readonly object ProbeLock = new object();
        private static readonly List<string> ProbeDirectories = new List<string>();
        private static bool probeInstalled;

        private readonly InteropPaths paths;
        private readonly Action<string> log;

        private bool initialized;
        private Type pathsType;
        private Type interopManagerType;

        // BepInEx.cfg as it was before we touched anything, or null if there was none.
        private string configPath;
        private byte[] configBackup;

        public InteropHost(InteropPaths paths, Action<string> log = null)
        {
            this.paths = paths ?? throw new ArgumentNullException(nameof(paths));
            this.log = log ?? delegate { };
        }

        public InteropPaths Paths => paths;

        /// <summary>Where BepInEx will put (or has put) the generated interop assemblies.</summary>
        public string InteropDirectory
        {
            get
            {
                EnsureInitialized();
                return (string)GetProperty(interopManagerType, "IL2CPPInteropAssemblyPath").GetValue(null);
            }
        }

        public string HashFilePath => Path.Combine(InteropDirectory, HashFileName);

        /// <summary>
        /// The hash BepInEx would compute right now: MD5 over GameAssembly.dll plus every
        /// <c>unity-libs\*.dll</c> (filename + bytes). Deliberately not reimplemented here — a
        /// reimplementation that drifted would report "up to date" on a stale interop set.
        /// </summary>
        public string ComputeHash()
        {
            EnsureInitialized();
            return (string)GetMethod(interopManagerType, "ComputeHash").Invoke(null, null);
        }

        /// <summary>The hash recorded by the last successful generation, or null if never generated.</summary>
        public string ReadStoredHash()
        {
            string file = HashFilePath;
            if (!File.Exists(file))
                return null;
            string text = File.ReadAllText(file).Trim();
            return text.Length == 0 ? null : text;
        }

        /// <summary>True when the interop set on disk matches the current game files.</summary>
        public bool IsUpToDate()
        {
            string stored = ReadStoredHash();
            return stored != null && string.Equals(stored, ComputeHash(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Runs BepInEx's generation. Minutes on a cold run.
        ///
        /// BepInEx swallows generation failures — <c>GenerateInteropAssemblies</c> catches everything and
        /// only logs — so success is verified here by the one side effect that is written last inside
        /// its try block: the hash file. No hash file, or a hash file that does not match, means the
        /// run failed however cheerful it looked.
        /// </summary>
        /// <param name="force">Delete the recorded hash first, so an up-to-date set is rebuilt anyway.</param>
        public void Generate(bool force = false)
        {
            EnsureInitialized();

            if (force)
            {
                string file = HashFilePath;
                if (File.Exists(file))
                {
                    log("Forcing regeneration: removing " + file);
                    File.Delete(file);
                }
            }

            string expected = ComputeHash();
            log("Target hash: " + expected);
            log("Running BepInEx interop generation (this can take several minutes)...");

            GetMethod(interopManagerType, "GenerateInteropAssemblies").Invoke(null, null);

            string stored = ReadStoredHash();
            if (stored == null)
            {
                throw new InteropSetupException(
                    "Generation did not write " + HashFilePath + ". BepInEx logs generation failures " +
                    "instead of throwing, so the reason is not visible from here. Two usual causes: " +
                    "the unity-libs download failed (no network, and no matching .zip in " +
                    Path.Combine(paths.BepInExRoot, "unity-libs") + "), or [IL2CPP] UpdateInteropAssemblies " +
                    "is set to false in BepInEx.cfg, which turns regeneration into a warning.");
            }

            // Recompute rather than trusting `expected`: generation populates unity-libs, and
            // unity-libs feeds the hash, so the value legitimately changes across a cold run.
            string actual = ComputeHash();
            if (!string.Equals(stored, actual, StringComparison.OrdinalIgnoreCase))
            {
                throw new InteropSetupException(
                    "Generation finished but the recorded hash does not match the current game files " +
                    "(recorded " + stored + ", expected " + actual + "). The interop set must be " +
                    "treated as unusable.");
            }

            log("Interop generation complete: " + InteropDirectory);
            log("Hash: " + stored);
        }

        private void EnsureInitialized()
        {
            if (initialized)
                return;

            // Both folders, in this order: core\ first so BepInEx's own assemblies always win, then
            // dotnet\ for the libraries that ship beside the runtime rather than in core.
            InstallAssemblyProbe(paths.CoreDirectory);
            InstallAssemblyProbe(paths.RuntimeDirectory);

            BackUpConfig();

            Assembly core = LoadCoreAssembly("BepInEx.Core.dll");
            Assembly unityCommon = LoadCoreAssembly("BepInEx.Unity.Common.dll");
            Assembly il2cpp = LoadCoreAssembly("BepInEx.Unity.IL2CPP.dll");

            pathsType = GetType(core, "BepInEx.Paths");

            // Step 2 — everything BepInEx derives hangs off this call. dllSearchPath is string[].
            GetMethod(pathsType, "SetExecutablePath").Invoke(
                null,
                new object[] { paths.GameExe, paths.BepInExRoot, null, false, null });
            log("BepInEx root: " + GetProperty(pathsType, "BepInExRootPath").GetValue(null));

            // Step 3 — internal, and it reads the version straight off the game files.
            Type unityInfo = GetType(unityCommon, "BepInEx.Unity.Common.UnityInfo");
            GetMethod(unityInfo, "Initialize").Invoke(
                null,
                new object[]
                {
                    GetProperty(pathsType, "ExecutablePath").GetValue(null),
                    GetProperty(pathsType, "GameDataPath").GetValue(null)
                });
            log("Unity version: " + GetProperty(unityInfo, "Version").GetValue(null));

            interopManagerType = GetType(il2cpp, "BepInEx.Unity.IL2CPP.Il2CppInteropManager");

            initialized = true;
        }

        /// <summary>
        /// Snapshots BepInEx.cfg so <see cref="Dispose"/> can put it back.
        ///
        /// Why this is needed: <c>ConfigFile.CoreConfig</c> is constructed with <c>saveOnInit: true</c>,
        /// so merely resolving BepInEx paths rewrites the file. Setting *values* survive — orphaned
        /// entries are kept — but every descriptive comment belonging to an entry this process never
        /// binds is dropped, because those <c>Bind()</c> calls only happen inside a real game start.
        /// Measured against the live install: values identical, comments gone. Cosmetic, but it is
        /// someone's working config and this tool has no business degrading it.
        /// </summary>
        private void BackUpConfig()
        {
            configPath = Path.Combine(paths.BepInExRoot, "config", "BepInEx.cfg");
            try
            {
                // A missing file means a fresh install being set up: let BepInEx write its defaults
                // and keep them.
                configBackup = File.Exists(configPath) ? File.ReadAllBytes(configPath) : null;
            }
            catch (IOException)
            {
                configBackup = null;
            }
        }

        /// <summary>Restores the BepInEx.cfg snapshot taken during initialisation, if it changed.</summary>
        public void Dispose()
        {
            if (configBackup == null || configPath == null)
                return;

            try
            {
                if (!File.Exists(configPath) || !BytesEqual(File.ReadAllBytes(configPath), configBackup))
                {
                    File.WriteAllBytes(configPath, configBackup);
                    log("Restored " + configPath + " (BepInEx rewrites it on path initialisation).");
                }
            }
            catch (IOException ex)
            {
                log("Warning: could not restore " + configPath + ": " + ex.Message);
            }
            finally
            {
                configBackup = null;
            }
        }

        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length)
                return false;
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i] != b[i])
                    return false;
            }
            return true;
        }

        private Assembly LoadCoreAssembly(string fileName)
        {
            string path = Path.Combine(paths.CoreDirectory, fileName);
            if (!File.Exists(path))
                throw new InteropSetupException("Missing BepInEx assembly: " + path);

            try
            {
                return AssemblyLoadContext.Default.LoadFromAssemblyPath(path);
            }
            catch (Exception ex)
            {
                throw new InteropSetupException("Could not load " + path + ": " + ex.Message, ex);
            }
        }

        /// <summary>
        /// Lets BepInEx's own dependency graph (Cecil, AsmResolver, Cpp2IL, Iced, MonoMod, ...) resolve
        /// out of core\. Registered on the Default context, so it is installed once per process.
        /// </summary>
        private static void InstallAssemblyProbe(string coreDirectory)
        {
            lock (ProbeLock)
            {
                if (!ProbeDirectories.Contains(coreDirectory))
                    ProbeDirectories.Add(coreDirectory);

                if (probeInstalled)
                    return;

                AssemblyLoadContext.Default.Resolving += (context, name) =>
                {
                    if (name.Name == null || name.Name.EndsWith(".resources", StringComparison.Ordinal))
                        return null;

                    foreach (string dir in SnapshotProbeDirectories())
                    {
                        string candidate = Path.Combine(dir, name.Name + ".dll");
                        if (File.Exists(candidate))
                            return context.LoadFromAssemblyPath(candidate);
                    }
                    return null;
                };

                // dobby.dll and friends. Generation should not need them, but a missing native library
                // surfaces as a confusing TypeInitializationException rather than a clear miss.
                AssemblyLoadContext.Default.ResolvingUnmanagedDll += (assembly, name) =>
                {
                    foreach (string dir in SnapshotProbeDirectories())
                    {
                        foreach (string candidate in new[]
                                 {
                                     Path.Combine(dir, name),
                                     Path.Combine(dir, name + ".dll")
                                 })
                        {
                            if (File.Exists(candidate))
                                return NativeLibrary.Load(candidate);
                        }
                    }
                    return IntPtr.Zero;
                };

                probeInstalled = true;
            }
        }

        private static string[] SnapshotProbeDirectories()
        {
            lock (ProbeLock)
                return ProbeDirectories.ToArray();
        }

        // Reflection accessors. Every failure names the member, because these are private/internal
        // BepInEx members: when a BepInEx bump moves one, the message must say which one moved.

        private static Type GetType(Assembly assembly, string fullName)
        {
            Type type = assembly.GetType(fullName, throwOnError: false);
            if (type == null)
            {
                throw new InteropSetupException(
                    "BepInEx build mismatch: type " + fullName + " not found in " +
                    assembly.GetName().Name + ". This tool is pinned to the BepInEx build documented " +
                    "in docs/plans/2026-08-27-bepinex-injector.md.");
            }
            return type;
        }

        private static MethodInfo GetMethod(Type type, string name)
        {
            MethodInfo method = type.GetMethod(
                name,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (method == null)
            {
                throw new InteropSetupException(
                    "BepInEx build mismatch: static method " + type.FullName + "." + name + " not found.");
            }
            return method;
        }

        private static PropertyInfo GetProperty(Type type, string name)
        {
            PropertyInfo property = type.GetProperty(
                name,
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (property == null)
            {
                throw new InteropSetupException(
                    "BepInEx build mismatch: static property " + type.FullName + "." + name + " not found.");
            }
            return property;
        }
    }
}
