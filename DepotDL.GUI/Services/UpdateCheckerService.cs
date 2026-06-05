using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DepotDL.GUI.Models;

namespace DepotDL.GUI.Services
{
    public static class UpdateCheckerService
    {
        private const string NightlyUrl = "https://api.github.com/repos/Bink-lab/DepotDL/releases?per_page=1";

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

        private static readonly Lazy<string?>   _currentSha = new(ReadCurrentSha);
        private static readonly Lazy<DateTime?> _buildTime  = new(ReadBuildTime);

        public static string?   GetCurrentSha() => _currentSha.Value;
        public static DateTime? GetBuildTime()  => _buildTime.Value;

        private static string? ReadCurrentSha()
        {
            var infoVer = Assembly.GetEntryAssembly()
                ?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
                ?.InformationalVersion;
            if (infoVer == null) return null;
            var idx = infoVer.IndexOf('+');
            if (idx < 0 || idx + 1 >= infoVer.Length) return null;
            var sha = infoVer[(idx + 1)..];
            return sha.Length > 7 ? sha[..7] : sha;
        }

        private static DateTime? ReadBuildTime()
        {
            var val = Assembly.GetEntryAssembly()
                ?.GetCustomAttributes<AssemblyMetadataAttribute>()
                .FirstOrDefault(a => a.Key == "BuildTime")?.Value;
            if (val == null) return null;
            return DateTime.TryParseExact(val, "yyyyMMdd-HHmmss",
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt)
                ? dt.ToUniversalTime() : null;
        }

        public static string BuildReleaseUrl(string tagName)
            => $"https://github.com/Bink-lab/DepotDL/releases/tag/{Uri.EscapeDataString(tagName)}";

        private static DateTime? ParseTagTime(string tag)
        {
            var p = tag.Split('-');
            if (p.Length < 3) return null;
            return DateTime.TryParseExact(p[1] + "-" + p[2], "yyyyMMdd-HHmmss",
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt)
                ? dt.ToUniversalTime() : null;
        }

        public static bool IsUpdateAvailableFromCache(AppSettings settings)
        {
            if (string.IsNullOrEmpty(settings.LastKnownReleaseTag)) return false;
            var buildTime = GetBuildTime();
            var tagTime   = ParseTagTime(settings.LastKnownReleaseTag);
            if (buildTime != null && tagTime != null)
                return tagTime > buildTime;
            return false;
        }

        public static async Task<UpdateCheckResult?> CheckAsync(string? currentSha,
            CancellationToken ct = default)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, NightlyUrl);
                req.Headers.UserAgent.ParseAdd("DepotDL-GUI/1.0");
                req.Headers.Accept.ParseAdd("application/vnd.github+json");

                using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return null;

                await using var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false);

                var arr = await JsonSerializer.DeserializeAsync<GitHubRelease[]>(stream).ConfigureAwait(false);
                var release = arr?.Length > 0 ? arr[0] : null;

                if (release?.TagName == null) return null;

                var parts = release.TagName.Split('-');
                var latestSha = parts.Length >= 4 ? parts[^1] : null;

                var buildTime = GetBuildTime();
                var tagTime   = ParseTagTime(release.TagName);
                bool updateAvailable;
                if (!string.IsNullOrEmpty(currentSha) && !string.IsNullOrEmpty(latestSha) &&
                    string.Equals(currentSha, latestSha, StringComparison.OrdinalIgnoreCase))
                    updateAvailable = false;
                else if (buildTime != null && tagTime != null)
                    updateAvailable = tagTime > buildTime;
                else
                    updateAvailable = !string.IsNullOrEmpty(currentSha) &&
                                      !string.IsNullOrEmpty(latestSha) &&
                                      !string.Equals(currentSha, latestSha, StringComparison.OrdinalIgnoreCase);

                return new UpdateCheckResult
                {
                    UpdateAvailable = updateAvailable,
                    LatestTag       = release.TagName,
                    HtmlUrl         = release.HtmlUrl ?? BuildReleaseUrl(release.TagName),
                    LatestSha       = latestSha
                };
            }
            catch
            {
                return null;
            }
        }

        private sealed class GitHubRelease
        {
            [JsonPropertyName("tag_name")] public string? TagName { get; set; }
            [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
        }
    }

    public sealed class UpdateCheckResult
    {
        public bool    UpdateAvailable { get; init; }
        public string? LatestTag       { get; init; }
        public string? HtmlUrl         { get; init; }
        public string? LatestSha       { get; init; }
    }
}
