using System;

namespace DepotDL.GUI.Models
{
    public enum UpdateChannel { Nightly, Production }

    public class AppSettings
    {
        public string? ManifestsDir { get; set; }
        public string? DownloadBaseDir { get; set; }
        public string? RyuuApiKey { get; set; }
        public string? HubcapApiKey { get; set; }
        public string? SteamWebApiKey { get; set; }
        public int MaxParallelDepots { get; set; } = 2;
        public int StoreCacheHours { get; set; } = 24;
        public int GpuCacheDays { get; set; } = 7;
        public int StorePageSize { get; set; } = 48;
        public int SearchDebounceMs { get; set; } = 250;
        public double ScrollSensitivity { get; set; } = 1.5;
        public int ScrollDurationMs { get; set; } = 230;
        public bool AutoSelectOsByOs { get; set; } = true;
        public bool DownloadAchievementIcons { get; set; } = true;
        public UpdateChannel UpdateChannel       { get; set; } = UpdateChannel.Nightly;
        public DateTime?     LastUpdateCheckUtc  { get; set; }
        public string?       LastKnownReleaseTag { get; set; }
    }
}
