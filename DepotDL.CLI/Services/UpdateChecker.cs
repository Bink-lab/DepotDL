// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using DepotDL.CLI.Tui;
using DepotDL.Shared;

namespace DepotDL.CLI.Services
{
    public sealed class AppUpdateInfo
    {
        public bool UpdateAvailable { get; init; }
        public string? LatestTag { get; init; }
        public string? HtmlUrl { get; init; }
        public string? LatestSha { get; init; }
    }

    internal static class UpdateChecker
    {
        public static string? GetCurrentSha() => UpdateCheckerCore.GetCurrentSha();
        public static string BuildReleaseUrl(string tag) => UpdateCheckerCore.BuildReleaseUrl(tag);

        public static bool ShouldCheck(TuiSession session)
            => session.LastUpdateCheckUtc == null ||
               (DateTime.UtcNow - session.LastUpdateCheckUtc.Value).TotalHours >= 24;

        public static void RecordCheck(TuiSession session, AppUpdateInfo? info)
        {
            session.LastUpdateCheckUtc = DateTime.UtcNow;
            if (info?.LatestTag != null) session.LastKnownReleaseTag = info.LatestTag;
        }

        public static bool IsUpdateAvailableFromCache(TuiSession session)
        {
            if (string.IsNullOrEmpty(session.LastKnownReleaseTag)) return false;
            return UpdateCheckerCore.IsNewerThanBuild(session.LastKnownReleaseTag);
        }

        public static async Task<AppUpdateInfo?> CheckAsync(string? currentSha, string channel = "Nightly", CancellationToken ct = default)
        {
            var isNightly = string.Equals(channel, "Nightly", StringComparison.OrdinalIgnoreCase);
            var info = await UpdateCheckerCore.CheckAsync(currentSha, isNightly, ct).ConfigureAwait(false);
            if (info == null) return null;
            return new AppUpdateInfo
            {
                UpdateAvailable = info.UpdateAvailable,
                LatestTag = info.LatestTag,
                HtmlUrl = info.HtmlUrl,
                LatestSha = info.LatestSha
            };
        }

        public static bool IsVelopackManaged(string channel = "Nightly")
            => UpdateCheckerCore.IsVelopackManaged(string.Equals(channel, "Nightly", StringComparison.OrdinalIgnoreCase));

        public static Task<bool> InstallUpdateAsync(string channel = "Nightly")
            => UpdateCheckerCore.InstallUpdateAsync(string.Equals(channel, "Nightly", StringComparison.OrdinalIgnoreCase));
    }
}
