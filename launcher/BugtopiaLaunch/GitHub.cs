using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace Bugtopia.Launch
{
    /// <summary>One downloadable build of the mod.</summary>
    public sealed class ModRelease
    {
        /// <summary>The release tag, e.g. <c>v2.8.2</c>. Recorded beside the plugin once installed.</summary>
        public string Tag { get; set; } = "";

        public string AssetName { get; set; } = "";

        /// <summary>Direct asset URL. Public-repo assets download without authentication.</summary>
        public string Url { get; set; } = "";
    }

    /// <summary>
    /// Raised when the GitHub API answers with something other than success. <see cref="NeedsToken"/>
    /// marks the two cases a token can actually fix, which is what makes it worth asking for one.
    /// </summary>
    public sealed class GitHubException : Exception
    {
        public GitHubException(string message, int status, bool needsToken) : base(message)
        {
            Status = status;
            NeedsToken = needsToken;
        }

        public int Status { get; }

        public bool NeedsToken { get; }
    }

    /// <summary>
    /// Fetching the mod itself from its releases, for builds that do not carry it.
    ///
    /// The rules are Vugtopia's, which has been doing this against the same repository for longer:
    /// list releases newest-first rather than asking for "latest" so an older build can be chosen,
    /// keep only the ones with a <c>.dll</c> asset, and download the asset unauthenticated — a token
    /// raises the API rate limit from 60 to 5000 requests an hour but is not needed for the file.
    /// </summary>
    public static class GitHub
    {
        public const string Repository = "baboodev/Bugtopia";

        /// <summary>Records which release is installed. Same filename Vugtopia writes, so both agree.</summary>
        public const string VersionMarker = "bugtopia.version";

        public static string ReleasesPage => "https://github.com/" + Repository + "/releases";

        private const string ApiUrl =
            "https://api.github.com/repos/" + Repository + "/releases?per_page=50";

        /// <summary>The tag recorded beside an installed plugin, or null.</summary>
        public static string InstalledTag(StorageLayout storage)
        {
            try
            {
                string marker = Path.Combine(storage.Plugins, VersionMarker);
                if (!File.Exists(marker))
                    return null;

                string tag = File.ReadAllText(marker).Trim();
                return tag.Length > 0 ? tag : null;
            }
            catch (IOException)
            {
                return null;
            }
        }

        /// <summary>
        /// Releases that have a plugin to install, newest first.
        /// </summary>
        /// <exception cref="GitHubException">The API refused. Check <see cref="GitHubException.NeedsToken"/>.</exception>
        public static async Task<List<ModRelease>> FetchReleasesAsync(string token, Action<string> log = null)
        {
            log ??= delegate { };
            log("Checking " + Repository + " releases" + (string.IsNullOrWhiteSpace(token) ? "" : " (with token)"));

#if BUGTOPIA_ONLINE
            using var http = new System.Net.Http.HttpClient();
            http.Timeout = TimeSpan.FromMinutes(2);

            // GitHub rejects requests with no user agent outright.
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Bugtopia-Launcher");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            if (!string.IsNullOrWhiteSpace(token))
                http.DefaultRequestHeaders.Add("Authorization", "Bearer " + token.Trim());

            System.Net.Http.HttpResponseMessage response;
            try
            {
                response = await http.GetAsync(ApiUrl);
            }
            catch (Exception ex)
            {
                throw new GitHubException("Could not reach the GitHub API: " + ex.Message, 0, false);
            }

            using (response)
            {
                int status = (int)response.StatusCode;
                if (status != 200)
                {
                    bool needsToken = status == 401 || status == 403 || status == 429;
                    string hint = status switch
                    {
                        401 => " - that token was not accepted.",
                        403 or 429 => " - the rate limit for unauthenticated requests is 60 an hour; " +
                                      "a token raises it to 5000.",
                        404 => " - no such repository, or it is private and the token cannot see it.",
                        _ => ".",
                    };
                    throw new GitHubException(
                        "GitHub answered " + status + hint, status, needsToken);
                }

                using Stream body = await response.Content.ReadAsStreamAsync();
                return Parse(body);
            }
#else
            await Task.CompletedTask;
            throw new DownloadException(
                "This build does not download anything. Fetch the mod yourself from " +
                ReleasesPage + " and put it in " + "BepInEx" + Path.DirectorySeparatorChar + "plugins.");
#endif
        }

        /// <summary>
        /// Downloads a release's plugin into the storage tree and records its tag beside it.
        /// </summary>
        public static async Task InstallAsync(ModRelease release, StorageLayout storage,
                                              Action<string> log = null, IProgress<int> progress = null)
        {
            log ??= delegate { };
            Directory.CreateDirectory(storage.Plugins);

            await Downloads.DownloadAsync(release.Url, storage.Plugin, log, progress);

            // The marker is a convenience, not a guarantee: the plugin is already in place, so a
            // failure to write it costs nothing but the version shown in the UI.
            try
            {
                File.WriteAllText(Path.Combine(storage.Plugins, VersionMarker), release.Tag);
            }
            catch (IOException)
            {
            }

            log("Installed " + release.AssetName + " " + release.Tag);
        }

        /// <summary>
        /// Reads the releases array, keeping the assets that are actually the plugin.
        ///
        /// Hand-walked with <see cref="JsonDocument"/> rather than deserialised into types: it is
        /// reflection-free, which NativeAOT needs, and the shape being read is four fields deep in
        /// a response with a great many more.
        /// </summary>
        internal static List<ModRelease> Parse(Stream json)
        {
            var releases = new List<ModRelease>();

            using JsonDocument doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return releases;

            foreach (JsonElement release in doc.RootElement.EnumerateArray())
            {
                if (!release.TryGetProperty("assets", out JsonElement assets) ||
                    assets.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                string tag = release.TryGetProperty("tag_name", out JsonElement t) ? t.GetString() : null;
                ModRelease chosen = null;

                foreach (JsonElement asset in assets.EnumerateArray())
                {
                    string name = asset.TryGetProperty("name", out JsonElement n) ? n.GetString() : null;
                    string url = asset.TryGetProperty("browser_download_url", out JsonElement u)
                        ? u.GetString()
                        : null;

                    if (name == null || url == null ||
                        !name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    int rank = Rank(name);
                    if (rank < 0)
                        continue;

                    if (chosen == null || rank < Rank(chosen.AssetName))
                        chosen = new ModRelease { Tag = tag ?? "", AssetName = name, Url = url };
                }

                if (chosen != null)
                    releases.Add(chosen);
            }

            return releases;
        }

        /// <summary>
        /// How well an asset suits a BepInEx install: lower is better, negative means never.
        ///
        /// A release ships four DLLs - one per loader, plus two universal builds - and the first in
        /// the list is not reliably the right one. Picking by name is what keeps a MelonLoader
        /// plugin out of a BepInEx plugins folder. Releases up to v2.1.7 shipped a single
        /// bugtopia.dll instead, which is why that name is still accepted.
        /// </summary>
        private static int Rank(string assetName)
        {
            if (assetName.IndexOf("bepinex", StringComparison.OrdinalIgnoreCase) >= 0)
                return 0;
            if (assetName.IndexOf("melonloader", StringComparison.OrdinalIgnoreCase) >= 0)
                return -1;
            if (assetName.IndexOf("universal", StringComparison.OrdinalIgnoreCase) >= 0)
                return 1;
            if (string.Equals(assetName, StorageLayout.PluginName, StringComparison.OrdinalIgnoreCase))
                return 2;

            return -1;
        }
    }
}
