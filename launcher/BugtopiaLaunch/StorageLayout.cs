using System;
using System.IO;

namespace Bugtopia.Launch
{
    /// <summary>
    /// The storage folder's shape. It is the BepInEx root's parent, and that is forced rather than
    /// chosen: BepInEx derives its root from the grandparent of the preloader DLL and hangs
    /// everything else — plugins, config, patchers, interop, unity-libs, cache, logs — off it, with
    /// no knob to point them elsewhere.
    /// </summary>
    public sealed class StorageLayout
    {
        public const string InjectDllName = "bugtopia_inject.dll";
        public const string InteropShimName = "BugtopiaInterop.dll";
        public const string InjectConfigName = "bugtopia_inject.cfg";
        public const string PluginName = "bugtopia.dll";
        public const string StampName = "launcher.json";

        public StorageLayout(string root)
        {
            if (string.IsNullOrWhiteSpace(root))
                throw new ArgumentException("Storage root is required.", nameof(root));

            Root = Path.GetFullPath(root);
            BepInExRoot = Path.Combine(Root, "BepInEx");
            Core = Path.Combine(BepInExRoot, "core");
            Plugins = Path.Combine(BepInExRoot, "plugins");
            Patchers = Path.Combine(BepInExRoot, "patchers");
            Config = Path.Combine(BepInExRoot, "config");
            Interop = Path.Combine(BepInExRoot, "interop");
            UnityLibs = Path.Combine(BepInExRoot, "unity-libs");
            Runtime = Path.Combine(Root, "dotnet");
            Bin = Path.Combine(Root, "bin");
        }

        public string Root { get; }
        public string BepInExRoot { get; }
        public string Core { get; }
        public string Plugins { get; }
        public string Patchers { get; }
        public string Config { get; }
        public string Interop { get; }
        public string UnityLibs { get; }
        public string Runtime { get; }
        public string Bin { get; }

        public string Preloader => Path.Combine(Core, "BepInEx.Unity.IL2CPP.dll");
        public string CoreClr => Path.Combine(Runtime, "coreclr.dll");
        public string InjectDll => Path.Combine(Bin, InjectDllName);

        /// <summary>The generator assembly, loaded by BepInEx's own CoreCLR rather than by us.</summary>
        public string InteropShim => Path.Combine(Bin, InteropShimName);
        public string InjectConfig => Path.Combine(Bin, InjectConfigName);
        public string Plugin => Path.Combine(Plugins, PluginName);
        public string Stamp => Path.Combine(Root, StampName);
        public string InteropHash => Path.Combine(Interop, "assembly-hash.txt");

        /// <summary>True once <see cref="Payload"/> has laid the tree out.</summary>
        public bool IsPrepared =>
            File.Exists(Preloader) && File.Exists(CoreClr) && File.Exists(InjectDll) && File.Exists(InteropShim);

        /// <summary>An interop set exists. Whether it is *current* is <see cref="Bugtopia.Interop"/>'s question.</summary>
        public bool HasInterop => File.Exists(InteropHash);
    }
}
