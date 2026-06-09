// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using CommunityToolkit.Mvvm.ComponentModel;

namespace DepotDL.GUI.Models
{
    public enum DepotStatus
    {
        Idle,
        Queued,
        Connecting,
        PreAllocating,
        Downloading,
        Validating,
        Done,
        Failed,
        Cancelled,
        Skipped
    }

    public partial class DepotDownloadState : ObservableObject
    {
        [ObservableProperty] private string _depotId = string.Empty;
        [ObservableProperty] private string _depotName = string.Empty;
        [ObservableProperty] private DepotStatus _status = DepotStatus.Idle;
        [ObservableProperty] private double _percent = 0;
        [ObservableProperty] private double _displayPercent = 0;
        [ObservableProperty] private string _speedText = string.Empty;
        [ObservableProperty] private string _statusText = "Queued";
        [ObservableProperty] private string _activeFile = string.Empty;
        [ObservableProperty] private string? _errorMessage;

        public long DownloadedUncompressedBytes { get; set; }

        public string DisplayName => !string.IsNullOrWhiteSpace(DepotName)
            ? DepotName
            : $"Depot {DepotId}";
    }
}
