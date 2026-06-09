// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

namespace DepotDL.GUI.Models
{
    public class DepotInfo
    {
        public string DepotId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string OsList { get; set; } = string.Empty;
        public string OsArch { get; set; } = string.Empty;
        public string DecryptionKey { get; set; } = string.Empty;
        public string ManifestId { get; set; } = string.Empty;
        public long SizeBytes { get; set; }

        public string DisplayName => !string.IsNullOrWhiteSpace(Name)
            ? Name
            : $"Depot {DepotId}";
    }
}
