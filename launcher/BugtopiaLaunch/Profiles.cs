using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using Microsoft.Win32;

namespace Bugtopia.Launch
{
    /// <summary>The profiles in the save folder and which one is live.</summary>
    public sealed class ProfileInfo
    {
        public List<string> Profiles { get; set; } = new List<string>();

        /// <summary>Display name of the profile occupying the <c>PC</c> folder, or empty.</summary>
        public string Active { get; set; } = "";

        /// <summary>False when the save folder does not exist — the game has never run.</summary>
        public bool Available { get; set; }
    }

    /// <summary>
    /// Save-profile switching, ported from Vugtopia's Rust implementation, which in turn replaced
    /// `HeartopiaProfileLauncher.bat`.
    ///
    /// Heartopia keeps save data under <c>%LocalLow%\xd\Heartopia\XD</c> and always reads the folder
    /// literally named <c>PC</c>. Other profiles are parked as siblings named after the profile, and
    /// <c>active_profile.txt</c> records which one currently occupies <c>PC</c>. Switching parks the
    /// active profile back under its name and renames the target to <c>PC</c>.
    ///
    /// The order of operations is deliberate and worth preserving: the target is validated *before*
    /// anything is renamed, and a failure to activate rolls the parking back, so there is no path
    /// that leaves <c>PC</c> empty and the game staring at a missing save.
    /// </summary>
    public static class Profiles
    {
        private const string ActiveFolder = "PC";
        private const string StateFile = "active_profile.txt";
        private const string ServerFile = "zone_server.txt";

        // The recommended-zone server is a Unity PlayerPrefs int in the registry, and it is global
        // rather than per-save — so it is stored beside each profile and reapplied on switch.
        // 8=Global, 9=SEA, 10=America, 12=Asia, 14=TW/HK/MO.
        private const string ServerRegistryKey = @"Software\xd\Heartopia";
        private const string ServerRegistryValue = "GetRecommendZoneServer_h3995072540";

        private static readonly char[] InvalidNameChars = { '\\', '/', ':', '*', '?', '"', '<', '>', '|' };

        /// <summary><c>%LocalLow%\xd\Heartopia\XD</c>.</summary>
        public static string BaseDirectory => Path.Combine(KnownPaths.LocalLow, "xd", "Heartopia", "XD");

        // ---- reading state ---------------------------------------------------

        private static string ReadActiveRecord(string baseDir)
        {
            try
            {
                string file = Path.Combine(baseDir, StateFile);
                return File.Exists(file) ? File.ReadAllText(file).Trim() : "";
            }
            catch (IOException)
            {
                return "";
            }
        }

        /// <summary>
        /// Display name of whatever is in <c>PC</c>: the recorded name, or the literal "PC" on first
        /// run (the folder exists but nothing has ever been recorded), or empty when there is none.
        /// </summary>
        private static string CurrentActiveName(string baseDir)
        {
            string recorded = ReadActiveRecord(baseDir);
            if (recorded.Length > 0)
                return recorded;
            return Directory.Exists(Path.Combine(baseDir, ActiveFolder)) ? ActiveFolder : "";
        }

        private static bool IsActive(string baseDir, string profile)
        {
            string active = CurrentActiveName(baseDir);
            return active.Length > 0 && string.Equals(active, profile, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>A free "PC&lt;n&gt;" name to park a first-run profile that has no recorded name.</summary>
        private static string GenerateParkName(string baseDir)
        {
            for (int n = 1; n < 10000; n++)
            {
                string name = "PC" + n.ToString(CultureInfo.InvariantCulture);
                if (!Directory.Exists(Path.Combine(baseDir, name)))
                    return name;
            }
            return "PC1";
        }

        public static ProfileInfo List()
        {
            string baseDir = BaseDirectory;
            var info = new ProfileInfo();
            if (!Directory.Exists(baseDir))
                return info;

            info.Available = true;
            info.Active = CurrentActiveName(baseDir);

            foreach (string dir in Directory.GetDirectories(baseDir))
            {
                string name = Path.GetFileName(dir);
                // The raw PC folder is skipped: the profile living in it is added below under its
                // display name instead.
                if (string.Equals(name, ActiveFolder, StringComparison.OrdinalIgnoreCase))
                    continue;
                info.Profiles.Add(name);
            }

            if (info.Active.Length > 0 &&
                !info.Profiles.Exists(n => string.Equals(n, info.Active, StringComparison.OrdinalIgnoreCase)))
            {
                info.Profiles.Add(info.Active);
            }

            info.Profiles.Sort((a, b) => string.Compare(a, b, StringComparison.OrdinalIgnoreCase));
            return info;
        }

        // ---- mutating ---------------------------------------------------------

        public static string Create(string name)
        {
            string baseDir = BaseDirectory;
            name = (name ?? "").Trim();

            if (name.Length == 0)
                throw new ProfileException("Profile name cannot be empty.");
            if (name == "." || name == "..")
                throw new ProfileException("Invalid profile name.");
            if (string.Equals(name, ActiveFolder, StringComparison.OrdinalIgnoreCase))
                throw new ProfileException("Profile name \"PC\" is reserved.");
            if (name.IndexOfAny(InvalidNameChars) >= 0)
                throw new ProfileException("Profile name contains invalid characters ( \\ / : * ? \" < > | ).");

            Directory.CreateDirectory(baseDir);

            if (string.Equals(ReadActiveRecord(baseDir), name, StringComparison.OrdinalIgnoreCase))
                throw new ProfileException($"Profile \"{name}\" is already the active profile.");

            string dir = Path.Combine(baseDir, name);
            if (Directory.Exists(dir))
                throw new ProfileException($"Profile \"{name}\" already exists.");

            // An empty folder: the game generates a fresh save when it is activated.
            Directory.CreateDirectory(dir);
            return $"Profile \"{name}\" created.";
        }

        /// <summary>
        /// Makes <paramref name="target"/> the active profile, carrying the zone-server value with it.
        /// </summary>
        public static string Switch(string target)
        {
            string baseDir = BaseDirectory;
            target = (target ?? "").Trim();

            // Captured before the swap: the recorded name identifies the folder the departing
            // profile gets parked into, and the live registry value belongs to it.
            string previousActive = ReadActiveRecord(baseDir);
            bool alreadyActive = IsActive(baseDir, target);
            int? currentServer = ReadZoneServer();

            string message = SwitchFolders(baseDir, target);

            if (!alreadyActive)
            {
                if (previousActive.Length > 0 && currentServer.HasValue)
                {
                    string parked = Path.Combine(baseDir, previousActive);
                    if (Directory.Exists(parked))
                        SaveProfileServer(parked, currentServer.Value);
                }
                RestoreProfileServer(Path.Combine(baseDir, ActiveFolder));
            }

            return message;
        }

        private static string SwitchFolders(string baseDir, string target)
        {
            if (target.Length == 0)
                throw new ProfileException("No profile selected.");
            if (!Directory.Exists(baseDir))
                throw new ProfileException("Profile directory not found: " + baseDir);

            string pc = Path.Combine(baseDir, ActiveFolder);
            string active = CurrentActiveName(baseDir);

            if (Directory.Exists(pc) && string.Equals(active, target, StringComparison.OrdinalIgnoreCase))
                return $"Profile \"{target}\" is already active.";

            // PC can only ever be the active profile, never a target in its own right.
            if (string.Equals(target, ActiveFolder, StringComparison.OrdinalIgnoreCase))
                throw new ProfileException("Profile name \"PC\" is reserved.");

            // Validated up front, so a failure here never leaves PC parked with nothing activated.
            string targetDir = Path.Combine(baseDir, target);
            if (!Directory.Exists(targetDir))
                throw new ProfileException("Target profile not found: " + targetDir);

            string parkedName = "";
            if (Directory.Exists(pc))
            {
                parkedName = active.Length == 0 || string.Equals(active, ActiveFolder, StringComparison.OrdinalIgnoreCase)
                    ? GenerateParkName(baseDir)
                    : active;

                string parked = Path.Combine(baseDir, parkedName);
                if (Directory.Exists(parked))
                    throw new ProfileException("Cannot park active profile; folder already exists: " + parked);

                try
                {
                    Directory.Move(pc, parked);
                }
                catch (Exception ex)
                {
                    throw new ProfileException("Failed to park current profile: " + ex.Message);
                }
            }

            try
            {
                Directory.Move(targetDir, pc);
            }
            catch (Exception ex)
            {
                // Roll the parking back rather than leaving no active folder at all.
                if (parkedName.Length > 0)
                {
                    try
                    {
                        Directory.Move(Path.Combine(baseDir, parkedName), pc);
                    }
                    catch (IOException)
                    {
                    }
                }
                throw new ProfileException("Failed to activate target profile: " + ex.Message);
            }

            File.WriteAllText(Path.Combine(baseDir, StateFile), target);
            return $"Active profile set to \"{target}\".";
        }

        // ---- zone server ------------------------------------------------------

        /// <summary>The live registry value, or null when it has never been set.</summary>
        public static int? ReadZoneServer()
        {
            try
            {
                using RegistryKey key = Registry.CurrentUser.OpenSubKey(ServerRegistryKey);
                object value = key?.GetValue(ServerRegistryValue);
                return value is int i ? i : (int?)null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static void WriteZoneServer(int value)
        {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(ServerRegistryKey);
            if (key == null)
                throw new ProfileException("Could not open the registry key for the zone server.");
            key.SetValue(ServerRegistryValue, value, RegistryValueKind.DWord);
        }

        private static void SaveProfileServer(string profileDir, int value)
        {
            try
            {
                File.WriteAllText(Path.Combine(profileDir, ServerFile), value.ToString(CultureInfo.InvariantCulture));
            }
            catch (IOException)
            {
            }
        }

        private static int? ReadProfileServer(string profileDir)
        {
            try
            {
                string file = Path.Combine(profileDir, ServerFile);
                if (!File.Exists(file))
                    return null;
                return int.TryParse(File.ReadAllText(file).Trim(), NumberStyles.Integer,
                                    CultureInfo.InvariantCulture, out int value)
                    ? value
                    : (int?)null;
            }
            catch (IOException)
            {
                return null;
            }
        }

        private static void RestoreProfileServer(string profileDir)
        {
            int? saved = ReadProfileServer(profileDir);
            if (saved.HasValue)
            {
                try
                {
                    WriteZoneServer(saved.Value);
                }
                catch (Exception)
                {
                }
            }
        }

        /// <summary>The zone server for a profile, or -1 when unknown. Empty name = the global value.</summary>
        public static int GetServer(string profile)
        {
            string baseDir = BaseDirectory;
            profile = (profile ?? "").Trim();

            if (profile.Length == 0)
                return ReadZoneServer() ?? -1;

            int? value = IsActive(baseDir, profile)
                ? ReadZoneServer() ?? ReadProfileServer(Path.Combine(baseDir, ActiveFolder))
                : ReadProfileServer(Path.Combine(baseDir, profile));
            return value ?? -1;
        }

        /// <summary>Sets a profile's zone server, and the registry too when that profile is live.</summary>
        public static void SetServer(string profile, int value)
        {
            string baseDir = BaseDirectory;
            profile = (profile ?? "").Trim();

            if (profile.Length == 0)
            {
                WriteZoneServer(value);
                return;
            }

            bool active = IsActive(baseDir, profile);
            string dir = Path.Combine(baseDir, active ? ActiveFolder : profile);
            if (!Directory.Exists(dir))
                throw new ProfileException("Profile folder not found: " + dir);

            SaveProfileServer(dir, value);
            if (active)
                WriteZoneServer(value);
        }
    }

    public sealed class ProfileException : Exception
    {
        public ProfileException(string message) : base(message) { }
    }
}
