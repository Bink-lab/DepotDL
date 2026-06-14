// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System.Globalization;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Velopack;
using Velopack.Sources;

namespace DepotDL.Shared
{
    public sealed class UpdateInfo
    {
        public bool UpdateAvailable { get; init; }
        public string? LatestTag { get; init; }
        public string? HtmlUrl { get; init; }
        public string? LatestSha { get; init; }
    }

    public static class UpdateCheckerCore
    {
        private const string ReleasesUrl = "https://api.github.com/repos/Bink-lab/DepotDL/releases?per_page=100";
        private const string GithubRepo = "https://github.com/Bink-lab/DepotDL";

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };
        private static readonly Lazy<string?> _currentSha = new(ReadCurrentSha);
        private static readonly Lazy<DateTime?> _buildTime = new(ReadBuildTime);

        public static string? GetCurrentSha() => _currentSha.Value;
        public static DateTime? GetBuildTime() => _buildTime.Value;

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

        internal static DateTime? ParseTagTime(string tag)
        {
            var p = tag.Split('-');
            if (p.Length < 3) return null;
            return DateTime.TryParseExact(p[1] + "-" + p[2], "yyyyMMdd-HHmmss",
                CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt)
                ? dt.ToUniversalTime() : null;
        }

        private static string? ShortSha(string? sha)
            => string.IsNullOrEmpty(sha) ? sha : (sha.Length > 7 ? sha[..7] : sha);

        public static bool IsNewerThanBuild(string tag)
        {
            var buildTime = GetBuildTime();
            var tagTime = ParseTagTime(tag);
            if (buildTime != null && tagTime != null)
                return tagTime > buildTime;
            var parts = tag.Split('-');
            var tagSha = ShortSha(parts.Length >= 4 ? parts[^1] : null);
            var currentSha = ShortSha(GetCurrentSha());
            if (!string.IsNullOrEmpty(currentSha) && !string.IsNullOrEmpty(tagSha))
                return !string.Equals(currentSha, tagSha, StringComparison.OrdinalIgnoreCase);
            return false;
        }

        // Must match Velopack's GithubSource(prerelease) definition of "nightly",
        // otherwise the checker can flag an update the installer can't deliver.
        private static bool IsNightlyRelease(GitHubRelease r) => r.Prerelease;

        public static async Task<UpdateInfo?> CheckAsync(string? currentSha, bool isNightly, CancellationToken ct = default)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, ReleasesUrl);
                req.Headers.UserAgent.ParseAdd("DepotDL/1.0");
                req.Headers.Accept.ParseAdd("application/vnd.github+json");

                using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return null;

                await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                var arr = await JsonSerializer.DeserializeAsync<GitHubRelease[]>(stream, cancellationToken: ct).ConfigureAwait(false);
                if (arr == null || arr.Length == 0) return null;

                var filtered = isNightly
                    ? arr.Where(IsNightlyRelease)
                    : arr.Where(r => !IsNightlyRelease(r));

                var release = filtered
                    .OrderByDescending(r => ParseTagTime(r.TagName ?? "") ?? DateTime.MinValue)
                    .FirstOrDefault();

                if (release?.TagName == null) return null;

                var parts = release.TagName.Split('-');
                var latestSha = ShortSha(parts.Length >= 4 ? parts[^1] : null);
                var currentShaShort = ShortSha(currentSha);

                var buildTime = GetBuildTime();
                var tagTime = ParseTagTime(release.TagName);
                bool updateAvailable;
                if (!string.IsNullOrEmpty(currentShaShort) && !string.IsNullOrEmpty(latestSha))
                    updateAvailable = !string.Equals(currentShaShort, latestSha, StringComparison.OrdinalIgnoreCase);
                else if (buildTime != null && tagTime != null)
                    updateAvailable = tagTime > buildTime;
                else
                    updateAvailable = false;

                return new UpdateInfo
                {
                    UpdateAvailable = updateAvailable,
                    LatestTag = release.TagName,
                    HtmlUrl = release.HtmlUrl ?? BuildReleaseUrl(release.TagName),
                    LatestSha = latestSha
                };
            }
            catch
            {
                return null;
            }
        }

        private static UpdateManager MakeManager(bool isNightly)
            => new(new GithubSource(GithubRepo, null, isNightly));

        public static bool IsVelopackManaged(bool isNightly = true)
        {
            try { return MakeManager(isNightly).IsInstalled; }
            catch { return false; }
        }

        // Returns false when Velopack has no applicable package for this channel
        // (e.g. a GitHub tag exists but carries no Velopack assets), so callers
        // can fall back to a manual download instead of failing silently.
        public static async Task<bool> InstallUpdateAsync(bool isNightly = true, Action<int>? onProgress = null, CancellationToken ct = default)
        {
            var mgr = MakeManager(isNightly);
            var info = await mgr.CheckForUpdatesAsync().WaitAsync(ct).ConfigureAwait(false);
            if (info == null) return false;
            await mgr.DownloadUpdatesAsync(info, onProgress, ct).ConfigureAwait(false);
            mgr.ApplyUpdatesAndRestart(info);
            return true;
        }

        private sealed class GitHubRelease
        {
            [JsonPropertyName("tag_name")] public string? TagName { get; set; }
            [JsonPropertyName("html_url")] public string? HtmlUrl { get; set; }
            [JsonPropertyName("prerelease")] public bool Prerelease { get; set; }
            [JsonPropertyName("name")] public string? Name { get; set; }
        }
    }
}
