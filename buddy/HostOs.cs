using System;
using System.Runtime.InteropServices;

namespace HeartopiaMod
{
    // Host OS as Wine reports it. Native Windows has no wine_get_host_version export, so the
    // probe is false there. On Wine the export returns the uname sysname: "Darwin" on macOS,
    // "Linux" on Linux. Porting Kit, CrossOver and Whisky are all Wine-on-Darwin.
    internal static class HostOs
    {
        private static bool probed;
        private static bool darwin;

        internal static bool IsDarwin
        {
            get
            {
                if (!probed)
                {
                    probed = true;
                    darwin = ProbeDarwin();
                    if (darwin)
                    {
                        ModLogger.Msg("[Host] Darwin — Toggle Menu default is Backslash.");
                    }
                }

                return darwin;
            }
        }

        // Mac keyboards have no Insert. Backslash is the menu key there; Windows stays on Insert.
        internal static UnityEngine.KeyCode DefaultToggleMenuKey
        {
            get { return IsDarwin ? UnityEngine.KeyCode.Backslash : UnityEngine.KeyCode.Insert; }
        }

        private static bool ProbeDarwin()
        {
            try
            {
                IntPtr ntdll = GetModuleHandle("ntdll.dll");
                if (ntdll == IntPtr.Zero)
                {
                    return false;
                }

                IntPtr proc = GetProcAddress(ntdll, "wine_get_host_version");
                if (proc == IntPtr.Zero)
                {
                    return false;
                }

                WineGetHostVersion fn = Marshal.GetDelegateForFunctionPointer<WineGetHostVersion>(proc);
                IntPtr sysname = IntPtr.Zero;
                unsafe
                {
                    fn(&sysname, null);
                }

                if (sysname == IntPtr.Zero)
                {
                    return false;
                }

                string name = Marshal.PtrToStringAnsi(sysname);
                return string.Equals(name, "Darwin", StringComparison.Ordinal);
            }
            catch (Exception ex)
            {
                ModLogger.Msg("[Host] Darwin probe failed: " + ex.Message);
                return false;
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private unsafe delegate void WineGetHostVersion(IntPtr* sysname, IntPtr* release);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);
    }
}
