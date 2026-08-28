using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Bugtopia.Launch
{
    /// <summary>
    /// Loads a native DLL into a running process the ordinary way: allocate the path in the target,
    /// then run <c>LoadLibraryW</c> on a remote thread. The loader then does what it always does —
    /// runs DllMain, resolves imports, registers the module — which is what keeps debuggers and crash
    /// dumps meaningful. Manual mapping would only hide the module from the loader list, which buys
    /// nothing here (see the plan's section 10).
    /// </summary>
    public static class Injector
    {
        private const uint ProcessCreateThread = 0x0002;
        private const uint ProcessQueryInformation = 0x0400;
        private const uint ProcessVmOperation = 0x0008;
        private const uint ProcessVmWrite = 0x0020;
        private const uint ProcessVmRead = 0x0010;

        private const uint MemCommit = 0x1000;
        private const uint MemReserve = 0x2000;
        private const uint MemRelease = 0x8000;
        private const uint PageReadWrite = 0x04;

        private const int ErrorAccessDenied = 5;

        /// <summary>True when a module of that file name is already mapped in the target.</summary>
        public static bool IsModuleLoaded(Process target, string dllPath)
        {
            string wanted = Path.GetFileName(dllPath);
            try
            {
                foreach (ProcessModule module in target.Modules)
                {
                    if (string.Equals(module.ModuleName, wanted, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch (Exception)
            {
                // Enumerating modules fails on a still-initialising or more-privileged process.
                // Not knowing is not the same as knowing it is absent, but the caller's next step
                // (injecting) reports its own failure clearly enough.
            }
            return false;
        }

        /// <exception cref="InjectionException">Anything that stopped the DLL from loading, with the reason.</exception>
        public static void Inject(Process target, string dllPath)
        {
            if (target == null)
                throw new ArgumentNullException(nameof(target));

            dllPath = Path.GetFullPath(dllPath);
            if (!File.Exists(dllPath))
                throw new InjectionException("No such DLL: " + dllPath);

            IntPtr process = OpenProcess(
                ProcessCreateThread | ProcessQueryInformation | ProcessVmOperation |
                ProcessVmWrite | ProcessVmRead,
                false, target.Id);

            if (process == IntPtr.Zero)
            {
                int error = Marshal.GetLastWin32Error();
                throw new InjectionException(error == ErrorAccessDenied
                    ? "OpenProcess was denied. The game is running with higher privileges than this " +
                      "launcher — start both the same way."
                    : "OpenProcess failed with error " + error + ".");
            }

            IntPtr remote = IntPtr.Zero;
            try
            {
                byte[] pathBytes = Encoding.Unicode.GetBytes(dllPath + "\0");

                remote = VirtualAllocEx(process, IntPtr.Zero, (uint)pathBytes.Length,
                                        MemCommit | MemReserve, PageReadWrite);
                if (remote == IntPtr.Zero)
                    throw new InjectionException("VirtualAllocEx failed with error " + Marshal.GetLastWin32Error() + ".");

                if (!WriteProcessMemory(process, remote, pathBytes, (uint)pathBytes.Length, out _))
                    throw new InjectionException("WriteProcessMemory failed with error " + Marshal.GetLastWin32Error() + ".");

                IntPtr loadLibrary = GetProcAddress(GetModuleHandle("kernel32.dll"), "LoadLibraryW");
                if (loadLibrary == IntPtr.Zero)
                    throw new InjectionException("Could not resolve LoadLibraryW.");

                IntPtr thread = CreateRemoteThread(process, IntPtr.Zero, 0, loadLibrary, remote, 0, out _);
                if (thread == IntPtr.Zero)
                    throw new InjectionException("CreateRemoteThread failed with error " + Marshal.GetLastWin32Error() + ".");

                try
                {
                    // DllMain returns promptly — it only spawns the bootstrap thread — so this waits
                    // on LoadLibraryW, not on the bootstrap.
                    WaitForSingleObject(thread, 30000);
                    if (GetExitCodeThread(thread, out uint exitCode) && exitCode == 0)
                    {
                        // A truncated HMODULE: zero is a real failure, non-zero proves nothing about
                        // the discarded high bits.
                        throw new InjectionException(
                            "LoadLibraryW returned NULL in the target. The usual causes are a bitness " +
                            "mismatch or a missing dependency of the injected DLL.");
                    }
                }
                finally
                {
                    CloseHandle(thread);
                }
            }
            finally
            {
                if (remote != IntPtr.Zero)
                    VirtualFreeEx(process, remote, 0, MemRelease);
                CloseHandle(process);
            }
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, uint size,
                                                    uint allocationType, uint protect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualFreeEx(IntPtr process, IntPtr address, uint size, uint freeType);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] buffer,
                                                      uint size, out IntPtr written);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern IntPtr GetModuleHandle(string name);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Ansi)]
        private static extern IntPtr GetProcAddress(IntPtr module, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateRemoteThread(IntPtr process, IntPtr attributes, uint stackSize,
                                                        IntPtr startAddress, IntPtr parameter,
                                                        uint creationFlags, out IntPtr threadId);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetExitCodeThread(IntPtr thread, out uint exitCode);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);
    }

    public sealed class InjectionException : Exception
    {
        public InjectionException(string message) : base(message) { }
    }
}
