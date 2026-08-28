using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bugtopia.Launch
{
    /// <summary>
    /// The folders the user picked and the BepInEx build the storage tree was laid out from.
    ///
    /// The version stamp is not decoration: the interop generator reaches into private members of
    /// <c>Il2CppInteropManager</c>, so a BepInEx the launcher has not been checked against fails late
    /// and confusingly. Comparing the stamp against what is actually in <c>core</c> turns that into a
    /// line on the status panel.
    /// </summary>
    public sealed class LauncherSettings
    {
        [JsonPropertyName("gameFolder")]
        public string GameFolder { get; set; }

        [JsonPropertyName("bepInExSource")]
        public string BepInExSource { get; set; }

        [JsonPropertyName("storage")]
        public string Storage { get; set; }

        [JsonPropertyName("unityLibsZip")]
        public string UnityLibsZip { get; set; }

        [JsonPropertyName("preparedFrom")]
        public string PreparedFrom { get; set; }

        /// <summary>
        /// A GitHub personal access token, asked for only when the API turns the launcher away for
        /// a reason a token would fix. Needs no scopes: it exists to raise the anonymous rate limit
        /// of 60 requests an hour, not to reach anything private.
        /// </summary>
        [JsonPropertyName("gitHubToken")]
        public string GitHubToken { get; set; }

        /// <summary>The newest release seen by the update check, so the notice survives a restart.</summary>
        [JsonPropertyName("latestSeen")]
        public string LatestSeen { get; set; }

        /// <summary>
        /// When the update check last reached GitHub. Kept so it runs about once a day rather than
        /// on every launch: the anonymous API allows 60 requests an hour, and a launcher that spends
        /// one of them every time it opens is a launcher that asks for a token to answer a question
        /// nobody asked.
        /// </summary>
        [JsonPropertyName("lastUpdateCheck")]
        public DateTime? LastUpdateCheck { get; set; }

        /// <summary>
        /// Show every field and dropdown rather than the few steps a first run needs. Remembered,
        /// because someone who has turned it on has said something about how they want to work.
        /// </summary>
        [JsonPropertyName("expert")]
        public bool Expert { get; set; }

        /// <summary><c>%LocalLow%\Bugtopia\runtime</c> — beside the mod's own user data.</summary>
        public static string DefaultStorage => KnownPaths.DefaultStorage;

        public static string DefaultPath => KnownPaths.LauncherSettingsFile;

        /// <summary>Never throws: a corrupt or absent file just means an unconfigured launcher.</summary>
        public static LauncherSettings Load(string path = null)
        {
            path ??= DefaultPath;
            try
            {
                if (File.Exists(path))
                {
                    return JsonSerializer.Deserialize(File.ReadAllText(path), LauncherJson.Default.LauncherSettings)
                           ?? new LauncherSettings();
                }
            }
            catch (Exception)
            {
            }
            return new LauncherSettings();
        }

        public void Save(string path = null)
        {
            path ??= DefaultPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonSerializer.Serialize(this, LauncherJson.Default.LauncherSettings));
        }
    }

    /// <summary>
    /// Source-generated serialisation. Required rather than preferred: the launcher publishes with
    /// NativeAOT, where the reflection-based serialiser has no metadata to work from.
    /// </summary>
    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(LauncherSettings))]
    [JsonSerializable(typeof(ProfileInfo))]
    public sealed partial class LauncherJson : JsonSerializerContext
    {
    }
}
