using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DepotDL.GUI.ViewModels
{
    public enum NavPage { Library, Download, Settings, Store }

    public partial class MainViewModel : ViewModelBase
    {
        [ObservableProperty] private NavPage _currentPage = NavPage.Library;
        [ObservableProperty] private LibraryViewModel _library = new();
        [ObservableProperty] private DownloadViewModel _download = new();
        [ObservableProperty] private SettingsViewModel _settings = new();

        public StoreViewModel Store { get; }

        public MainViewModel()
        {
            Store = new StoreViewModel(this);
            Download.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(DownloadViewModel.IsDownloading))
                    OnPropertyChanged(nameof(ShowDownloadWidget));
            };
        }

        public bool ShowDownloadWidget => Download.IsDownloading && !IsDownloadPage;

        public async Task InitializeAsync(IProgress<(double pct, string status)> progress)
        {
            progress.Report((0.05, "Loading settings..."));
            Settings.Load();

            progress.Report((0.35, "Loading game library..."));
            await Library.LoadAsync();

            progress.Report((1.0, "Ready."));
        }

        [RelayCommand] private void NavigateLibrary()  { CurrentPage = NavPage.Library; Library.Load(); }
        [RelayCommand] private void NavigateDownload()  { CurrentPage = NavPage.Download; }
        [RelayCommand] private void NavigateSettings()  { CurrentPage = NavPage.Settings; Settings.Load(); }
        [RelayCommand] private void NavigateStore()     { CurrentPage = NavPage.Store; Store.EnsureLoaded(); }

        public bool IsLibraryPage  => CurrentPage == NavPage.Library;
        public bool IsDownloadPage => CurrentPage == NavPage.Download;
        public bool IsSettingsPage => CurrentPage == NavPage.Settings;
        public bool IsStorePage    => CurrentPage == NavPage.Store;

        public void NavigateDownloadWithAppId(string appId)
        {
            Download.PreFillAppId(appId);
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
