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
            var parts = tag.Split('-');
            var tagSha = ShortSha(parts.Length >= 4 ? parts[^1] : null);
            return IsUpdate(ShortSha(GetCurrentSha()), tagSha, GetBuildTime(), ParseTagTime(tag));
        }

        // SHA identity decides "same build" before time does, because the CI release
        // job stamps a tag timestamp a few minutes AFTER the assembly's embedded
        // BuildTime for the very same commit — comparing time first would nag about
        // the build that is already installed. Time only resolves direction once the
        // commit actually differs, so an older release never reads as an upgrade.
        private static bool IsUpdate(string? currentSha, string? latestSha, DateTime? buildTime, DateTime? tagTime)
        {
            if (!string.IsNullOrEmpty(currentSha) && !string.IsNullOrEmpty(latestSha))
            {
                if (string.Equals(currentSha, latestSha, StringComparison.OrdinalIgnoreCase))
                    return false;
                if (buildTime != null && tagTime != null)
                    return tagTime > buildTime;
                return true;
            }
            if (buildTime != null && tagTime != null)
                return tagTime > buildTime;
            return false;
        }

        public static async Task<UpdateInfo?> CheckAsync(string? currentSha, CancellationToken ct = default)
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, ReleasesUrl);
                req.Headers.UserAgent.ParseAdd("DepotDL/1.0");
                req.Headers.Accept.ParseAdd("application/vnd.github+json");

                using var resp = await Http.SendAsync(req, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode)
                {
                    Log($"update check HTTP {(int)resp.StatusCode}");
                    return null;
                }

                await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                var arr = await JsonSerializer.DeserializeAsync<GitHubRelease[]>(stream, cancellationToken: ct).ConfigureAwait(false);
                if (arr == null || arr.Length == 0) return null;

                var release = arr
                    .OrderByDescending(r => ParseTagTime(r.TagName ?? "") ?? DateTime.MinValue)
                    .FirstOrDefault();

                if (release?.TagName == null) return null;

                var parts = release.TagName.Split('-');
                var latestSha = ShortSha(parts.Length >= 4 ? parts[^1] : null);

                var updateAvailable = IsUpdate(ShortSha(currentSha), latestSha,
                    GetBuildTime(), ParseTagTime(release.TagName));

                return new UpdateInfo
                {
                    UpdateAvailable = updateAvailable,
                    LatestTag = release.TagName,
                    HtmlUrl = release.HtmlUrl ?? BuildReleaseUrl(release.TagName),
                    LatestSha = latestSha
                };
            }
            catch (Exception ex)
            {
                Log($"update check failed: {ex.Message}");
                return null;
            }
        }

        // The project publishes only prerelease (nightly) builds, so the Velopack
        // feed must always be opened with prerelease support enabled.
        private static UpdateManager MakeManager()
            => new(new GithubSource(GithubRepo, null, prerelease: true));

        public static bool IsVelopackManaged()
        {
            try { return MakeManager().IsInstalled; }
            catch { return false; }
        }

        // Returns false when Velopack has no applicable package for this platform
        // (e.g. a GitHub tag exists but carries no Velopack assets for this OS/arch),
        // so callers can fall back to a manual download instead of failing silently.
        public static async Task<bool> InstallUpdateAsync(Action<int>? onProgress = null, CancellationToken ct = default)
        {
            try
            {
                var mgr = MakeManager();
                if (!mgr.IsInstalled) return false;
                var info = await mgr.CheckForUpdatesAsync().WaitAsync(ct).ConfigureAwait(false);
                if (info == null) return false;
                await mgr.DownloadUpdatesAsync(info, onProgress, ct).ConfigureAwait(false);
                mgr.ApplyUpdatesAndRestart(info);
                return true;
            }
            catch (Exception ex)
            {
                Log($"update install failed: {ex.Message}");
                return false;
            }
        }

        private static void Log(string msg)
        {
            try { Console.Error.WriteLine($"[DepotDL.Update] {msg}"); } catch { }
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
