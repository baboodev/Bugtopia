using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Bugtopia.Launch
{
#if BUGTOPIA_ONLINE
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
#endif

    /// <summary>
    /// Fetching the mod itself from its releases, for builds that do not carry it.
    ///
    /// The rules are Vugtopia's, which has been doing this against the same repository for longer:
    /// list releases newest-first rather than asking for "latest" so an older build can be chosen,
    /// keep only the ones with a <c>.dll</c> asset, and download the asset unauthenticated — a token
    /// raises the API rate limit from 60 to 5000 requests an hour but is not needed for the file.
    ///
    /// Every build keeps the version helpers, which only read files on disk; everything that talks
    /// to GitHub - and every URL that names it - is compiled into an online build only.
    /// </summary>
    public static class GitHub
    {
        public const string Repository = "baboodev/Bugtopia";

        /// <summary>
        /// Written beside a plugin installed from a release. Kept for Vugtopia, which reads the same
        /// file; the launcher itself goes by <see cref="InstalledVersion"/>.
        /// </summary>
        public const string VersionMarker = "bugtopia.version";

#if BUGTOPIA_ONLINE
        /// <summary>
        /// Where a human goes to fetch a build by hand. Online only: an offline build has nothing to
        /// point anyone at, and keeping the constant would leave the URL in its binary.
        /// </summary>
        public static string ReleasesPage => "https://github.com/" + Repository + "/releases";

        private const string ApiUrl =
            "https://api.github.com/repos/" + Repository + "/releases?per_page=50";
#endif

        /// <summary>
        /// Whether <paramref name="candidate"/> names a later build than <paramref name="current"/>.
        ///
        /// Compared component by component rather than as text: "2.8.10" is later than "2.8.9" and
        /// sorts before it. A leading v is dropped, missing components count as zero, and anything
        /// after the first non-digit in a component is ignored, so 2.9.0-rc1 compares as 2.9.0.
        /// </summary>
        public static bool IsNewer(string candidate, string current)
        {
            if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(current))
                return false;

            int[] a = Numbers(candidate);
            int[] b = Numbers(current);

            for (int i = 0; i < Math.Max(a.Length, b.Length); i++)
            {
                int left = i < a.Length ? a[i] : 0;
                int right = i < b.Length ? b[i] : 0;
                if (left != right)
                    return left > right;
            }

            return false;
        }

        private static int[] Numbers(string version)
        {
            string text = version.Trim();
            if (text.Length > 0 && (text[0] == 'v' || text[0] == 'V'))
                text = text.Substring(1);

            string[] parts = text.Split('.');
            var numbers = new int[parts.Length];

            for (int i = 0; i < parts.Length; i++)
            {
                int digits = 0;
                while (digits < parts[i].Length && char.IsAsciiDigit(parts[i][digits]))
                    digits++;

                numbers[i] = digits > 0 && int.TryParse(parts[i].Substring(0, digits), out int value)
                    ? value
                    : 0;
            }

            return numbers;
        }

        /// <summary>
        /// The version the installed plugin declares about itself, e.g. <c>2.8.2+46f9cfb</c>, or null.
        ///
        /// Read out of the DLL rather than out of <see cref="VersionMarker"/>: the marker says which
        /// release was last downloaded, and anything that puts a different DLL in its place - an
        /// offline launcher, a copy by hand, a build deployed over it - leaves it saying that.
        /// Measured: a marker reading v2.8.3 beside a 3.0.0 DLL, which the update check then offered
        /// to "update" to v2.9.5. Every release since v2.0.0 stamps its version into the DLL.
        /// </summary>
        public static string InstalledVersion(StorageLayout storage) => Payload.VersionOf(storage.Plugin);

        /// <summary>
        /// Whether two versions name the same build, by the component rule <see cref="IsNewer"/> uses:
        /// a release tag <c>v2.8.3</c> and a DLL's <c>2.8.3+46f9cfb</c> are the same. False when either
        /// is missing.
        /// </summary>
        public static bool SameVersion(string a, string b) =>
            !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) && !IsNewer(a, b) && !IsNewer(b, a);

        /// <summary><c>2.8.2+46f9cfb</c> as <c>2.8.2 (46f9cfb)</c>, the way the launcher shows its own version.</summary>
        public static string DisplayVersion(string version)
        {
            if (string.IsNullOrWhiteSpace(version))
                return null;

            int plus = version.IndexOf('+');
            return plus < 0 ? version : version.Substring(0, plus) + " (" + version.Substring(plus + 1) + ")";
        }

#if BUGTOPIA_ONLINE
        /// <summary>
        /// Releases that have a plugin to install, newest first.
        /// </summary>
        /// <exception cref="GitHubException">The API refused. Check <see cref="GitHubException.NeedsToken"/>.</exception>
        public static List<ModRelease> FetchReleases(string token, Action<string> log = null)
        {
            log ??= delegate { };
            log("Checking " + Repository + " releases" + (string.IsNullOrWhiteSpace(token) ? "" : " (with token)"));

            // GitHub rejects requests with no user agent outright; the accept header pins the API
            // version's response shape.
            var headers = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("Accept", "application/vnd.github+json"),
            };

            if (!string.IsNullOrWhiteSpace(token))
            {
                headers.Add(new KeyValuePair<string, string>(
                    "Authorization", "Bearer " + token.Trim()));
            }

            byte[] body;
            int status;
            try
            {
                body = Downloads.Fetch(ApiUrl, headers, out status);
            }
            catch (DownloadException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new GitHubException("Could not reach the GitHub API: " + ex.Message, 0, false);
            }

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
                throw new GitHubException("GitHub answered " + status + hint, status, needsToken);
            }

            using var stream = new MemoryStream(body);
            return Parse(stream);
        }

        /// <summary>
        /// Downloads a release's plugin into the storage tree and records its tag beside it.
        /// </summary>
        public static void Install(ModRelease release, StorageLayout storage,
                                   Action<string> log = null, Action<int> progress = null)
        {
            log ??= delegate { };
            Directory.CreateDirectory(storage.Plugins);

            Downloads.Download(release.Url, storage.Plugin, log, progress);

            // The marker is for Vugtopia; the launcher reads the version out of the DLL. The plugin
            // is already in place, so a failure to write it costs nothing here.
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
#endif
    }
}
