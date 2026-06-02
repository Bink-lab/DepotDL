using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepotDL.GUI.Models;
using DepotDL.GUI.Services;

namespace DepotDL.GUI.ViewModels
{
    public enum NavPage { Library, Download, Settings, Store }

    public partial class MainViewModel : ViewModelBase
    {
        private readonly SettingsService _settingsService = new();

        [ObservableProperty] private NavPage _currentPage = NavPage.Library;
        [ObservableProperty] private LibraryViewModel _library = new();
        [ObservableProperty] private DownloadViewModel _download = new();
        [ObservableProperty] private SettingsViewModel _settings = new();
        [ObservableProperty] private bool    _updateAvailable;
        [ObservableProperty] private string? _updateBannerText;
        [ObservableProperty] private string? _updateHtmlUrl;

        public StoreViewModel Store { get; }

        public MainViewModel()
        {
            Store = new StoreViewModel(this);
            Download.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(DownloadViewModel.IsDownloading))
                    OnPropertyChanged(nameof(ShowDownloadWidget));
            };

            Library.ValidateHandler = game =>
            {
                Download.PreFillFromLibraryGame(game, clearCheckpoints: false);
                CurrentPage = NavPage.Download;
            };
            Library.RedownloadHandler = game =>
            {
                Download.PreFillFromLibraryGame(game, clearCheckpoints: true);
                CurrentPage = NavPage.Download;
            };
        }

        public bool ShowDownloadWidget => Download.IsDownloading && !IsDownloadPage;

        public async Task InitializeAsync(IProgress<(double pct, string status)> progress,
            CancellationToken ct = default)
        {
            progress.Report((0.05, "Loading settings..."));
            Settings.Load();

            progress.Report((0.35, "Loading game library..."));
            await Library.LoadAsync();

            progress.Report((0.55, "Checking for updates..."));
            await CheckForUpdateAsync(ct);

            progress.Report((1.0, "Ready."));
        }

        private async Task CheckForUpdateAsync(CancellationToken ct = default)
        {
            try
            {
                var s = _settingsService.Load();
                var currentSha = UpdateCheckerService.GetCurrentSha();

                bool shouldCheck = s.LastUpdateCheckUtc == null ||
                                   (DateTime.UtcNow - s.LastUpdateCheckUtc.Value).TotalHours >= 24;

                UpdateCheckResult? result = null;

                if (shouldCheck)
                {
                    result = await UpdateCheckerService.CheckAsync(currentSha, s.UpdateChannel, ct);
                    s.LastUpdateCheckUtc = DateTime.UtcNow;
                    if (result?.LatestTag != null) s.LastKnownReleaseTag = result.LatestTag;
                    _settingsService.Save(s);
                }
                else if (UpdateCheckerService.IsUpdateAvailableFromCache(s))
                {
                    result = new UpdateCheckResult
                    {
                        UpdateAvailable = true,
                        LatestTag       = s.LastKnownReleaseTag,
                        HtmlUrl         = s.LastKnownReleaseTag != null
                            ? UpdateCheckerService.BuildReleaseUrl(s.LastKnownReleaseTag)
                            : null
                    };
                }

                if (result?.UpdateAvailable == true)
                {
                    UpdateBannerText = FormatUpdateBannerText(result.LatestTag, result.LatestSha);
                    UpdateHtmlUrl    = result.HtmlUrl;
                    UpdateAvailable  = true;
                }
            }
            catch { }
        }

        private static string FormatUpdateBannerText(string? tag, string? sha)
        {
            if (tag != null)
            {
                var p = tag.Split('-');
                if (p.Length >= 4 && p[1].Length == 8)
                {
                    var d = p[1];
                    var date = $"{d[..4]}-{d[4..6]}-{d[6..8]}";
                    return $"New build available  ·  {p[^1]}  ·  {date}";
                }
                return $"New build available  ·  {tag}";
            }
            return sha != null ? $"New build available  ·  {sha}" : "New build available";
        }

        [RelayCommand]
        private void DismissUpdate() => UpdateAvailable = false;

        [RelayCommand]
        private void OpenUpdateUrl()
        {
            if (string.IsNullOrEmpty(UpdateHtmlUrl)) return;
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName        = UpdateHtmlUrl,
                    UseShellExecute = true
                });
            }
            catch { }
        }

        [RelayCommand] private async Task NavigateLibrary()  { if (CurrentPage == NavPage.Library) return; CurrentPage = NavPage.Library; await Library.LoadAsync(); }
        [RelayCommand] private void NavigateDownload()  { CurrentPage = NavPage.Download; }
        [RelayCommand] private void NavigateSettings()  { CurrentPage = NavPage.Settings; Settings.Load(); }
        [RelayCommand] private void NavigateStore()     { CurrentPage = NavPage.Store; Store.EnsureLoaded(); }

        public bool IsLibraryPage  => CurrentPage == NavPage.Library;
        public bool IsDownloadPage => CurrentPage == NavPage.Download;
        public bool IsSettingsPage => CurrentPage == NavPage.Settings;
        public bool IsStorePage    => CurrentPage == NavPage.Store;

        public void NavigateDownloadWithAppId(string appId, ManifestProvider? provider = null)
        {
            Download.PreFillAppId(appId, provider);
            CurrentPage = NavPage.Download;
        }

        partial void OnCurrentPageChanged(NavPage value)
        {
            OnPropertyChanged(nameof(IsLibraryPage));
            OnPropertyChanged(nameof(IsDownloadPage));
            OnPropertyChanged(nameof(IsSettingsPage));
            OnPropertyChanged(nameof(IsStorePage));
            OnPropertyChanged(nameof(ShowDownloadWidget));
        }
    }
}
