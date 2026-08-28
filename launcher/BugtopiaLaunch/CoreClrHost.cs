using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Bugtopia.Launch
{
    /// <summary>
    /// Runs the interop generator inside the CoreCLR that BepInEx already ships.
    ///
    /// A NativeAOT process has no CLR and cannot load a managed assembly, so the generator cannot run
    /// in-process the way it did under a JIT host. It does not need to: <c>&lt;storage&gt;\dotnet</c>
    /// is a complete .NET 6 runtime the user already installed, and hosting it is the same three C
    /// functions the game-side bootstrap uses. The launcher therefore needs no runtime of its own,
    /// which is most of why it fits in two megabytes.
    ///
    /// <b>Once per process.</b> <c>coreclr_initialize</c> cannot be called twice, and the generator
    /// caches paths in statics besides, so this always runs in a short-lived child process that exits
    /// afterwards — see the launcher's headless verb.
    /// </summary>
    public static unsafe class CoreClrHost
    {
        private const string EntryAssembly = "BugtopiaInterop";
        private const string EntryType = "Bugtopia.Interop.InteropEntry";
        private const string EntryMethod = "Run";

        /// <summary>Mirrors <c>InteropEntry</c>'s constants; duplicated so this assembly needs no reference to it.</summary>
        public const int ModeCheck = 0;
        public const int ModeGenerate = 1;
        public const int ModeGenerateForce = 2;

        public const int ResultUpToDate = 0;
        public const int ResultStale = 1;
        public const int ResultGenerated = 2;
        public const int ResultError = 3;

        /// <summary>
        /// Hosts CoreCLR and calls the generator. Returns one of the Result constants; details are in
        /// <c>&lt;storage&gt;\bin\interopgen.log</c>, which the caller is expected to surface.
        /// </summary>
        /// <exception cref="HostException">The runtime or the shim could not be started at all.</exception>
        public static int Run(string gameFolder, StorageLayout storage, int mode)
        {
            if (!File.Exists(storage.CoreClr))
                throw new HostException("Missing runtime: " + storage.CoreClr + " — run Prepare first.");

            string shim = Path.Combine(storage.Bin, "BugtopiaInterop.dll");
            if (!File.Exists(shim))
                throw new HostException("Missing generator: " + shim + " — run Prepare first.");

            IntPtr module = NativeLibrary.Load(storage.CoreClr);
            var initialize = (delegate* unmanaged[Stdcall]<byte*, byte*, int, byte**, byte**, void**, uint*, int>)
                NativeLibrary.GetExport(module, "coreclr_initialize");
            var createDelegate = (delegate* unmanaged[Stdcall]<void*, uint, byte*, byte*, byte*, void**, int>)
                NativeLibrary.GetExport(module, "coreclr_create_delegate");

            // The shim sits in bin\ rather than core\, so it has to be on the TPA list explicitly.
            string tpa = BuildTrustedPlatformAssemblies(storage, shim);

            using var exePath = new Utf8(Environment.ProcessPath ?? storage.Root);
            using var domain = new Utf8("bugtopia-interop");
            using var keyTpa = new Utf8("TRUSTED_PLATFORM_ASSEMBLIES");
            using var keyAppPaths = new Utf8("APP_PATHS");
            using var keyNative = new Utf8("NATIVE_DLL_SEARCH_DIRECTORIES");
            using var valTpa = new Utf8(tpa);
            using var valAppPaths = new Utf8(storage.Core + ";" + storage.Bin);
            using var valNative = new Utf8(storage.Runtime + ";" + gameFolder);

            void* host = null;
            uint domainId = 0;
            int hr;

            byte** keys = stackalloc byte*[3];
            byte** values = stackalloc byte*[3];
            keys[0] = keyTpa.Pointer; values[0] = valTpa.Pointer;
            keys[1] = keyAppPaths.Pointer; values[1] = valAppPaths.Pointer;
            keys[2] = keyNative.Pointer; values[2] = valNative.Pointer;

            hr = initialize(exePath.Pointer, domain.Pointer, 3, keys, values, &host, &domainId);
            if (hr < 0)
                throw new HostException($"coreclr_initialize failed: 0x{hr:X8}");

            using var asmName = new Utf8(EntryAssembly);
            using var typeName = new Utf8(EntryType);
            using var methodName = new Utf8(EntryMethod);

            void* entry = null;
            hr = createDelegate(host, domainId, asmName.Pointer, typeName.Pointer, methodName.Pointer, &entry);
            if (hr < 0 || entry == null)
                throw new HostException($"coreclr_create_delegate({EntryType}.{EntryMethod}) failed: 0x{hr:X8}");

            using var gameArg = new Utf8(gameFolder);
            using var bepInExArg = new Utf8(storage.BepInExRoot);
            using var logArg = new Utf8(storage.Bin);

            var run = (delegate* unmanaged[Stdcall]<byte*, byte*, byte*, int, int>)entry;
            return run(gameArg.Pointer, bepInExArg.Pointer, logArg.Pointer, mode);
        }

        private static string BuildTrustedPlatformAssemblies(StorageLayout storage, string shim)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var builder = new StringBuilder();

            void Add(string path)
            {
                if (!seen.Add(Path.GetFileName(path)))
                    return;
                if (builder.Length > 0)
                    builder.Append(';');
                builder.Append(path);
            }

            // Runtime first: its System.* must win over anything of the same name in core\.
            foreach (string dir in new[] { storage.Runtime, storage.Core })
            {
                if (!Directory.Exists(dir))
                    continue;
                foreach (string file in Directory.GetFiles(dir, "*.dll"))
                    Add(file);
            }
            Add(shim);

            return builder.ToString();
        }

        /// <summary>A NUL-terminated UTF-8 copy of a string; CoreCLR's hosting API is UTF-8 throughout.</summary>
        private sealed class Utf8 : IDisposable
        {
            private IntPtr handle;

            public Utf8(string value)
            {
                byte[] bytes = Encoding.UTF8.GetBytes(value ?? "");
                handle = Marshal.AllocHGlobal(bytes.Length + 1);
                Marshal.Copy(bytes, 0, handle, bytes.Length);
                Marshal.WriteByte(handle, bytes.Length, 0);
            }

            public byte* Pointer => (byte*)handle;

            public void Dispose()
            {
                if (handle != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(handle);
                    handle = IntPtr.Zero;
                }
            }
        }
    }

    public sealed class HostException : Exception
    {
        public HostException(string message) : base(message) { }
    }
}
