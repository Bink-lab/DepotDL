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
        [ObservableProperty] private string _speedText = string.Empty;
        [ObservableProperty] private string _statusText = "Queued";
        [ObservableProperty] private string _activeFile = string.Empty;
        [ObservableProperty] private string? _errorMessage;

        public string DisplayName => !string.IsNullOrWhiteSpace(DepotName)
            ? DepotName
            : $"Depot {DepotId}";
    }
}
