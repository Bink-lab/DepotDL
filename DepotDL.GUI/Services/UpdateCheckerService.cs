// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using DepotDL.GUI.Models;
using DepotDL.Shared;

namespace DepotDL.GUI.Services
{
    public static class UpdateCheckerService
    {
        public static string? GetCurrentSha() => UpdateCheckerCore.GetCurrentSha();
        public static string BuildReleaseUrl(string tagName) => UpdateCheckerCore.BuildReleaseUrl(tagName);

        public static bool IsUpdateAvailableFromCache(AppSettings settings)
        {
            if (string.IsNullOrEmpty(settings.LastKnownReleaseTag)) return false;
            return UpdateCheckerCore.IsNewerThanBuild(settings.LastKnownReleaseTag);
        }

        public static async Task<UpdateCheckResult?> CheckAsync(string? currentSha,
            UpdateChannel channel = UpdateChannel.Nightly,
            CancellationToken ct = default)
        {
            var info = await UpdateCheckerCore.CheckAsync(currentSha, channel == UpdateChannel.Nightly, ct).ConfigureAwait(false);
            if (info == null) return null;
            return new UpdateCheckResult
            {
                UpdateAvailable = info.UpdateAvailable,
                LatestTag = info.LatestTag,
                HtmlUrl = info.HtmlUrl,
                LatestSha = info.LatestSha,
                Channel = channel
            };
        }

        public static bool IsVelopackManaged(UpdateChannel channel = UpdateChannel.Nightly)
            => UpdateCheckerCore.IsVelopackManaged(channel == UpdateChannel.Nightly);

        public static async Task InstallUpdateAsync(UpdateChannel channel = UpdateChannel.Nightly,
            Action<int>? onProgress = null, CancellationToken ct = default)
            => await UpdateCheckerCore.InstallUpdateAsync(channel == UpdateChannel.Nightly, onProgress, ct).ConfigureAwait(false);
    }

    public sealed class UpdateCheckResult
    {
        public bool UpdateAvailable { get; init; }
        public string? LatestTag { get; init; }
        public string? HtmlUrl { get; init; }
        public string? LatestSha { get; init; }
        public UpdateChannel Channel { get; init; }
    }
}
