using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace DepotDL.GUI.ViewModels
{
    public enum NavPage { Library, Download, Settings }

    public partial class MainViewModel : ViewModelBase
    {
        [ObservableProperty] private NavPage _currentPage = NavPage.Library;
        [ObservableProperty] private LibraryViewModel _library = new();
        [ObservableProperty] private DownloadViewModel _download = new();
        [ObservableProperty] private SettingsViewModel _settings = new();

        public MainViewModel() { }

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

        public bool IsLibraryPage  => CurrentPage == NavPage.Library;
        public bool IsDownloadPage => CurrentPage == NavPage.Download;
        public bool IsSettingsPage => CurrentPage == NavPage.Settings;

        partial void OnCurrentPageChanged(NavPage value)
        {
            OnPropertyChanged(nameof(IsLibraryPage));
            OnPropertyChanged(nameof(IsDownloadPage));
            OnPropertyChanged(nameof(IsSettingsPage));
        }
    }
}
