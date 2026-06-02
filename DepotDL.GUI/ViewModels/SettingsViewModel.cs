using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepotDL.GUI.Models;
using DepotDL.GUI.Services;

namespace DepotDL.GUI.ViewModels
{
    public partial class SettingsViewModel : ViewModelBase
    {
        private readonly SettingsService _svc = new();

        [ObservableProperty] private string _manifestsDir = string.Empty;
        [ObservableProperty] private string _downloadBaseDir = string.Empty;
        [ObservableProperty] private string _ryuuApiKey = string.Empty;
        [ObservableProperty] private int _maxParallelDepots = 2;
        [ObservableProperty] private int _storeCacheHours = 24;
        [ObservableProperty] private int _gpuCacheDays = 7;
        [ObservableProperty] private int _storePageSize = 48;
        [ObservableProperty] private int _searchDebounceMs = 250;
        [ObservableProperty] private double _scrollSensitivity = 1.5;
        [ObservableProperty] private int _scrollDurationMs = 230;
        [ObservableProperty] private bool _smartOsFilter;
        [ObservableProperty] private bool _saveSuccess;

        public void Load()
        {
            var s = _svc.Load();
            ManifestsDir = s.ManifestsDir ?? string.Empty;
            DownloadBaseDir = s.DownloadBaseDir ?? string.Empty;
            RyuuApiKey = s.RyuuApiKey ?? string.Empty;
            MaxParallelDepots = s.MaxParallelDepots;
            StoreCacheHours = s.StoreCacheHours;
            GpuCacheDays = s.GpuCacheDays;
            StorePageSize = s.StorePageSize;
            SearchDebounceMs = s.SearchDebounceMs;
            ScrollSensitivity = s.ScrollSensitivity;
            ScrollDurationMs = s.ScrollDurationMs;
            SmartOsFilter = s.SmartOsFilter;
        }

        [RelayCommand]
        private void BrowseManifestsDir()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select Manifests Cache Folder",
                InitialDirectory = ManifestsDir
            };
            if (dialog.ShowDialog() == true)
                ManifestsDir = dialog.FolderName;
        }

        [RelayCommand]
        private void BrowseDownloadBaseDir()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select Default Download Base Folder",
                InitialDirectory = DownloadBaseDir
            };
            if (dialog.ShowDialog() == true)
                DownloadBaseDir = dialog.FolderName;
        }

        [RelayCommand]
        private void Save()
        {
            _svc.Save(new AppSettings
            {
                ManifestsDir = string.IsNullOrWhiteSpace(ManifestsDir) ? null : ManifestsDir,
                DownloadBaseDir = string.IsNullOrWhiteSpace(DownloadBaseDir) ? null : DownloadBaseDir,
                RyuuApiKey = string.IsNullOrWhiteSpace(RyuuApiKey) ? null : RyuuApiKey,
                MaxParallelDepots = MaxParallelDepots,
                StoreCacheHours = StoreCacheHours,
                GpuCacheDays = GpuCacheDays,
                StorePageSize = StorePageSize,
                SearchDebounceMs = SearchDebounceMs,
                ScrollSensitivity = ScrollSensitivity,
                ScrollDurationMs = ScrollDurationMs,
                SmartOsFilter = SmartOsFilter
            });

            DepotDL.GUI.Helpers.SmoothScroll.ResetCache();
            SaveSuccess = true;

            System.Windows.Threading.DispatcherTimer timer = new()
            {
                Interval = System.TimeSpan.FromSeconds(2)
            };
            timer.Tick += (_, _) => { SaveSuccess = false; timer.Stop(); };
            timer.Start();
        }
    }
}
