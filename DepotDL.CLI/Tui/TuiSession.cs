// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

namespace DepotDL.CLI.Tui
{
    public class DepotInfo
    {
        public string DepotId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string OsList { get; set; } = string.Empty;
        public string OsArch { get; set; } = string.Empty;
        public string DecryptionKey { get; set; } = string.Empty;
        public string ManifestId { get; set; } = string.Empty;
    }

    public class TuiSession
    {
        public string? LuaPath { get; set; }
        public string? AppId { get; set; }
        public string? ManifestsDir { get; set; }
        public string? OutputDir { get; set; }
        public string? DownloadBaseDir { get; set; }
        public string? RyuuApiKey { get; set; }
        public string? HubcapApiKey { get; set; }
        public string? SteamWebApiKey { get; set; }
        public bool ManifestsDirConfigured { get; set; }
        public int MaxParallelDepots { get; set; } = 2;
        public bool DownloadAchievementIcons { get; set; } = true;
        public List<DepotInfo> AllDepots { get; set; } = new();
        public List<DepotInfo> SelectedDepots { get; set; } = new();
        public DateTime? LastUpdateCheckUtc { get; set; }
        public string? LastKnownReleaseTag { get; set; }
        public string? DismissedUpdateTag { get; set; }
    }
}
