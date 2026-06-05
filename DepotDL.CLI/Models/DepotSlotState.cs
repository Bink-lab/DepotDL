using System;
using System.Collections.Generic;

namespace DepotDL.CLI.Models
{
    public class DepotSlotState
    {
        public string? DepotId { get; set; }
        public string Status { get; set; } = "Idle";
        public double? Percent { get; set; }
        public string? ActiveValidationFile { get; set; }
        public string? OutputPath { get; set; }

        public long TotalUncompressedSize { get; set; } = 0;
        public long LastSpeedTotalBytes { get; set; } = 0;
        public Dictionary<string, long> FileSizes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public DateTime? DownloadStartTime { get; set; }
        public DateTime LastSpeedUpdateTime { get; set; } = DateTime.MinValue;
        public double LastPercent { get; set; } = 0;
        public double CurrentSpeedBps { get; set; } = 0;
        public string? SpeedOverrideString { get; set; }
        public DateTime LastProgressTime { get; set; } = DateTime.MinValue;
    }
}
