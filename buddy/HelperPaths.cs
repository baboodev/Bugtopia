using System;
using System.IO;
using System.Runtime.InteropServices;

namespace HeartopiaMod
{
    internal static class HelperPaths
    {
        private const string AppFolderName = "Bugtopia";
        private const string LegacyAppFolderName = "HelperSettings";
        private static readonly Guid LocalAppDataLowFolderId = new Guid("A520A1A4-1780-4FF6-BD18-167343C5AF16");
        private static bool legacyMigrationAttempted;
        private static bool helperSettingsMigrationAttempted;

        public static string Root
        {
            get
            {
                TryMigrateLegacyHelperSettings();
                return EnsureDirectory(Path.Combine(GetLocalLowDirectory(), AppFolderName));
            }
        }

        public static string GetDirectory(params string[] parts)
        {
            string path = Root;
            if (parts != null)
            {
                foreach (string part in parts)
                {
                    if (!string.IsNullOrWhiteSpace(part))
                    {
                        path = Path.Combine(path, part);
                    }
                }
            }

            return EnsureDirectory(path);
        }

        public static string GetFile(string fileName, params string[] folders)
        {
            return Path.Combine(GetDirectory(folders), fileName);
        }

        public static void TryMigrateLegacyUserData(string baseDirectory)
        {
            if (legacyMigrationAttempted)
            {
                return;
            }

            legacyMigrationAttempted = true;
            TryMigrateLegacyHelperSettings();

            try
            {
                if (string.IsNullOrWhiteSpace(baseDirectory))
                {
                    return;
                }

                string legacyRoot = Path.Combine(baseDirectory, "UserData");
                if (!Directory.Exists(legacyRoot))
                {
                    return;
                }

                CopyDirectoryIfMissing(legacyRoot, Root);
            }
            catch
            {
            }
        }

        private static void TryMigrateLegacyHelperSettings()
        {
            if (helperSettingsMigrationAttempted)
            {
                return;
            }

            helperSettingsMigrationAttempted = true;

            try
            {
                string localLow = GetLocalLowDirectory();
                string legacyRoot = Path.Combine(localLow, LegacyAppFolderName);
                if (!Directory.Exists(legacyRoot))
                {
                    return;
                }

                string destinationRoot = Path.Combine(localLow, AppFolderName);
                CopyDirectoryIfMissing(legacyRoot, destinationRoot);
            }
            catch
            {
            }
        }

        private static string GetLocalLowDirectory()
        {
            if (TryGetKnownFolderPath(LocalAppDataLowFolderId, out string localLowPath) && !string.IsNullOrWhiteSpace(localLowPath))
            {
                return localLowPath;
            }

            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (!string.IsNullOrWhiteSpace(localAppData))
            {
                string appData = Directory.GetParent(localAppData)?.FullName;
                if (!string.IsNullOrWhiteSpace(appData))
                {
                    return Path.Combine(appData, "LocalLow");
                }

                return localAppData;
            }

            return AppDomain.CurrentDomain.BaseDirectory;
        }

        private static bool TryGetKnownFolderPath(Guid folderId, out string path)
        {
            path = null;
            IntPtr pathPtr = IntPtr.Zero;
            try
            {
                int hr = SHGetKnownFolderPath(ref folderId, 0, IntPtr.Zero, out pathPtr);
                if (hr != 0 || pathPtr == IntPtr.Zero)
                {
                    return false;
                }

                path = Marshal.PtrToStringUni(pathPtr);
                return !string.IsNullOrWhiteSpace(path);
            }
            catch
            {
                return false;
            }
            finally
            {
                if (pathPtr != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(pathPtr);
                }
            }
        }

        private static string EnsureDirectory(string path)
        {
            if (!Directory.Exists(path))
            {
                Directory.CreateDirectory(path);
            }

            return path;
        }

        private static void CopyDirectoryIfMissing(string sourceDirectory, string destinationDirectory)
        {
            EnsureDirectory(destinationDirectory);

            foreach (string sourceFile in Directory.GetFiles(sourceDirectory))
            {
                string destinationFile = Path.Combine(destinationDirectory, Path.GetFileName(sourceFile));
                if (!File.Exists(destinationFile))
                {
                    File.Copy(sourceFile, destinationFile, false);
                }
            }

            foreach (string sourceChild in Directory.GetDirectories(sourceDirectory))
            {
                string destinationChild = Path.Combine(destinationDirectory, Path.GetFileName(sourceChild));
                CopyDirectoryIfMissing(sourceChild, destinationChild);
            }
        }

        [DllImport("shell32.dll")]
        private static extern int SHGetKnownFolderPath(ref Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);
    }
}
