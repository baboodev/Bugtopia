using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Bugtopia.Launch
{
    /// <summary>
    /// Well-known folders the launcher needs.
    ///
    /// LocalLow has no <see cref="Environment.SpecialFolder"/> and is asked for by known-folder id.
    /// Hand-building it from <c>%USERPROFILE%</c> is the bug the mod's own path handling was fixed to
    /// stop doing, so the shell API is used first and the string only ever serves as a fallback for
    /// the case where the API fails outright.
    /// </summary>
    public static class KnownPaths
    {
        private static readonly Guid FolderIdLocalAppDataLow = new Guid("A520A1A4-1780-4FF6-BD18-167343C5AF16");

        private static string localLow;

        /// <summary><c>%USERPROFILE%\AppData\LocalLow</c>.</summary>
        public static string LocalLow
        {
            get
            {
                if (localLow != null)
                    return localLow;

                if (SHGetKnownFolderPath(FolderIdLocalAppDataLow, 0, IntPtr.Zero, out IntPtr buffer) == 0)
                {
                    try
                    {
                        localLow = Marshal.PtrToStringUni(buffer);
                    }
                    finally
                    {
                        CoTaskMemFree(buffer);
                    }
                }

                if (string.IsNullOrEmpty(localLow))
                {
                    localLow = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        "AppData", "LocalLow");
                }

                return localLow;
            }
        }

        /// <summary><c>%LocalLow%\Bugtopia</c> — where the mod already keeps its own user data.</summary>
        public static string BugtopiaData => Path.Combine(LocalLow, "Bugtopia");

        /// <summary>The default storage root: <c>%LocalLow%\Bugtopia\runtime</c>.</summary>
        public static string DefaultStorage => Path.Combine(BugtopiaData, "runtime");

        /// <summary>Where the launcher keeps its own settings and log, beside the mod's data.</summary>
        public static string LauncherSettingsFile => Path.Combine(BugtopiaData, "launcher.json");

        public static string LauncherLogFile => Path.Combine(BugtopiaData, "launcher.log");

        /// <summary>
        /// Where the launcher unpacks its native webview shell: inside the runtime folder, beside
        /// everything else the launcher installs, rather than in <c>%TEMP%</c>. One folder holds all
        /// of it and deleting that folder undoes the install.
        ///
        /// Deliberately the default runtime path rather than whichever storage folder the user
        /// picked: this is needed to open the window at all, before any settings have been read.
        /// </summary>
        public static string NativeShellRoot => Path.Combine(DefaultStorage, "native");

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHGetKnownFolderPath(in Guid id, uint flags, IntPtr token, out IntPtr path);

        [DllImport("ole32.dll")]
        private static extern void CoTaskMemFree(IntPtr ptr);
    }
}
