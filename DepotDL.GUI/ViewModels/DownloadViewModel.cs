// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepotDL.GUI.Models;
using DepotDL.GUI.Services;

namespace DepotDL.GUI.ViewModels
{
    public enum DownloadStep { SelectFile, ConfigureDepots, Downloading }
    public enum ManifestProvider { Ryuu, Hubcap }

    public partial class DownloadViewModel : ViewModelBase
    {
        private readonly LuaParserService _parser = new();
        private readonly DownloadService _downloader = new();
        private readonly LibraryService _library = new();
        private readonly SettingsService _settings = new();
        private readonly RyuuService _ryuu = new();
        private readonly HubcapService _hubcap = new();
        private readonly ZipImportService _zipper = new();

        private CancellationTokenSource? _cts;
        private System.Windows.Threading.DispatcherTimer? _animTimer;
        private List<DepotDownloadState>? _activeStates;
        private List<DepotInfo>? _lastSelectedDepots;

        [ObservableProperty] private DownloadStep _currentStep = DownloadStep.SelectFile;

        [ObservableProperty] private string _luaPath = string.Empty;
        [ObservableProperty] private string _luaFileName = string.Empty;
        [ObservableProperty] private string _appId = string.Empty;
        [ObservableProperty] private bool _luaLoaded;
        [ObservableProperty] private string _parseError = string.Empty;
        [ObservableProperty] private string _zipError = string.Empty;

        [ObservableProperty] private ObservableCollection<DepotSelectionItem> _depots = new();

        [ObservableProperty] private string _depotSearchText = string.Empty;
        [ObservableProperty] private bool _filterWindows = true;
        [ObservableProperty] private bool _filterLinux = true;
        [ObservableProperty] private bool _filterMacOs = true;
        [ObservableProperty] private bool _filterCommon = true;
        [ObservableProperty] private bool _filterDlc = true;

        private ICollectionView? _filteredDepots;
        public ICollectionView? FilteredDepots => _filteredDepots;
        public int FilteredCount => (_filteredDepots as System.Windows.Data.CollectionView)?.Count ?? 0;

        [ObservableProperty] private string _outputDir = string.Empty;
        [ObservableProperty] private string _manifestsDir = string.Empty;
        [ObservableProperty] private int _maxParallel = 2;
        [ObservableProperty] private bool _canStart;
        [ObservableProperty] private string _startBlockReason = string.Empty;

        [ObservableProperty] private string _ryuuAppId = string.Empty;
        [ObservableProperty] private string _ryuuApiKey = string.Empty;
        [ObservableProperty] private bool _isRyuuBusy;
        [ObservableProperty] private string _ryuuStatusText = string.Empty;
        [ObservableProperty] private string _ryuuError = string.Empty;
        [ObservableProperty] private bool _canFetchRyuu;

        [ObservableProperty] private string _hubcapAppId = string.Empty;
        [ObservableProperty] private string _hubcapApiKey = string.Empty;
        [ObservableProperty] private bool _isHubcapBusy;
        [ObservableProperty] private string _hubcapError = string.Empty;
        [ObservableProperty] private bool _canFetchHubcap;

        [ObservableProperty] private ObservableCollection<DepotDownloadState> _downloadStates = new();
        [ObservableProperty] private double _overallPercent;
        [ObservableProperty] private double _overallDisplayPercent;
        [ObservableProperty] private string _overallStatus = string.Empty;
        [ObservableProperty] private bool _isDownloading;
        [ObservableProperty] private bool _downloadComplete;
        [ObservableProperty] private bool _downloadFailed;
        [ObservableProperty] private string _completionMessage = string.Empty;

        public IAsyncRelayCommand FetchFromRyuuAsyncCommand { get; }
        public IAsyncRelayCommand FetchFromHubcapAsyncCommand { get; }
        public IAsyncRelayCommand StartDownloadAsyncCommand { get; }
        public IAsyncRelayCommand RetryFailedAsyncCommand { get; }
        public IAsyncRelayCommand<DepotDownloadState> RetryDepotAsyncCommand { get; }

        public DownloadViewModel()
        {
            FetchFromRyuuAsyncCommand = new AsyncRelayCommand(FetchFromRyuuAsync, () => CanFetchRyuu);
            FetchFromHubcapAsyncCommand = new AsyncRelayCommand(FetchFromHubcapAsync, () => CanFetchHubcap);
            StartDownloadAsyncCommand = new AsyncRelayCommand(StartDownloadAsync, () => CanStart);
            RetryFailedAsyncCommand = new AsyncRelayCommand(RetryFailedAsync, () => !IsDownloading && DownloadFailed && _lastSelectedDepots != null);
            RetryDepotAsyncCommand = new AsyncRelayCommand<DepotDownloadState>(RetryDepotAsync, s => !IsDownloading && _lastSelectedDepots != null && s?.Status == DepotStatus.Failed);

            var settings = _settings.Load();
            ManifestsDir = settings.ManifestsDir ?? string.Empty;
            MaxParallel = settings.MaxParallelDepots;
            RyuuApiKey = settings.RyuuApiKey ?? string.Empty;
            HubcapApiKey = settings.HubcapApiKey ?? string.Empty;
            UpdateCanFetchRyuu();
            UpdateCanFetchHubcap();
        }

        partial void OnOutputDirChanged(string value) => UpdateCanStart();

        partial void OnDepotsChanged(ObservableCollection<DepotSelectionItem>? oldValue, ObservableCollection<DepotSelectionItem> newValue)
        {
            if (oldValue != null)
            {
                oldValue.CollectionChanged -= OnDepotsCollectionChanged;
                foreach (var item in oldValue)
                    item.PropertyChanged -= OnDepotItemPropertyChanged;
            }
            if (newValue != null)
            {
                newValue.CollectionChanged += OnDepotsCollectionChanged;
                foreach (var item in newValue)
                    item.PropertyChanged += OnDepotItemPropertyChanged;
            }
            _filteredDepots = CollectionViewSource.GetDefaultView(newValue);
            _filteredDepots.Filter = DepotFilterPredicate;
            OnPropertyChanged(nameof(FilteredDepots));
            OnPropertyChanged(nameof(FilteredCount));
            UpdateCanStart();
        }

        private bool DepotFilterPredicate(object obj)
        {
            if (obj is not DepotSelectionItem item) return false;
            if (!string.IsNullOrWhiteSpace(DepotSearchText))
            {
                var q = DepotSearchText.Trim();
                if (!item.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase) &&
                    !item.DepotId.Contains(q, StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            if (!FilterDlc && item.IsDlc) return false;
            var os = item.OsList;
            if (string.IsNullOrWhiteSpace(os))
                return FilterCommon;
            var tags = os.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (FilterWindows && tags.Any(t => t.Equals("windows", StringComparison.OrdinalIgnoreCase))) return true;
            if (FilterLinux && tags.Any(t => t.Equals("linux", StringComparison.OrdinalIgnoreCase))) return true;
            if (FilterMacOs && tags.Any(t => t.Equals("macos", StringComparison.OrdinalIgnoreCase))) return true;
            bool hasKnownTag = tags.Any(t =>
                t.Equals("windows", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("linux", StringComparison.OrdinalIgnoreCase) ||
                t.Equals("macos", StringComparison.OrdinalIgnoreCase));
            if (!hasKnownTag) return FilterCommon;
            return false;
        }

        private void RefreshFilter()
        {
            _filteredDepots?.Refresh();
            OnPropertyChanged(nameof(FilteredCount));
        }

        partial void OnDepotSearchTextChanged(string value) => RefreshFilter();
        partial void OnFilterWindowsChanged(bool value) => RefreshFilter();
        partial void OnFilterLinuxChanged(bool value) => RefreshFilter();
        partial void OnFilterMacOsChanged(bool value) => RefreshFilter();
        partial void OnFilterCommonChanged(bool value) => RefreshFilter();
        partial void OnFilterDlcChanged(bool value) => RefreshFilter();

        private void OnDepotsCollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
                foreach (DepotSelectionItem item in e.OldItems)
                    item.PropertyChanged -= OnDepotItemPropertyChanged;
            if (e.NewItems != null)
                foreach (DepotSelectionItem item in e.NewItems)
                    item.PropertyChanged += OnDepotItemPropertyChanged;
            UpdateCanStart();
        }

        private void OnDepotItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DepotSelectionItem.IsSelected))
                UpdateCanStart();
        }
        partial void OnRyuuAppIdChanged(string value) => UpdateCanFetchRyuu();
        partial void OnRyuuApiKeyChanged(string value) => UpdateCanFetchRyuu();
        partial void OnIsRyuuBusyChanged(bool value) => UpdateCanFetchRyuu();

        partial void OnHubcapAppIdChanged(string value) => UpdateCanFetchHubcap();
        partial void OnHubcapApiKeyChanged(string value) => UpdateCanFetchHubcap();
        partial void OnIsHubcapBusyChanged(bool value) => UpdateCanFetchHubcap();

        private void UpdateCanFetchRyuu()
        {
            CanFetchRyuu = !IsRyuuBusy && !string.IsNullOrWhiteSpace(RyuuAppId);
            FetchFromRyuuAsyncCommand.NotifyCanExecuteChanged();
        }

        private void UpdateCanFetchHubcap()
        {
            CanFetchHubcap = !IsHubcapBusy && !string.IsNullOrWhiteSpace(HubcapAppId);
            FetchFromHubcapAsyncCommand.NotifyCanExecuteChanged();
        }

        private async Task FetchFromRyuuAsync()
        {
            IsRyuuBusy = true;
            RyuuStatusText = "Fetching...";
            RyuuError = string.Empty;
            try
            {
                var settings = _settings.Load();
                settings.RyuuApiKey = RyuuApiKey.Trim();
                _settings.Save(settings);

                var result = await _ryuu.DownloadPackageAsync(RyuuAppId.Trim(), RyuuApiKey.Trim(),
                    status => RyuuStatusText = status);
                if (!result.HasZip || string.IsNullOrEmpty(result.ZipPath))
                {
                    RyuuError = result.Message;
                    return;
                }

                var imported = _zipper.ImportZip(result.ZipPath);
                try { File.Delete(result.ZipPath); } catch { }

                if (!string.IsNullOrEmpty(imported.ManifestsDir) && imported.ManifestCount > 0)
                    ManifestsDir = imported.ManifestsDir;

                if (!string.IsNullOrEmpty(imported.FirstLuaPath))
                    LoadLuaFile(imported.FirstLuaPath);
                else
                    RyuuError = "ZIP contained no Lua configuration file.";
            }
            catch (Exception ex)
            {
                RyuuError = ex.Message;
            }
            finally
            {
                IsRyuuBusy = false;
            }
        }

        private async Task FetchFromHubcapAsync()
        {
            IsHubcapBusy = true;
            HubcapError = string.Empty;
            try
            {
                var settings = _settings.Load();
                settings.HubcapApiKey = HubcapApiKey.Trim();
                _settings.Save(settings);

                var result = await _hubcap.DownloadPackageAsync(HubcapAppId.Trim(), HubcapApiKey.Trim());
                if (!result.HasZip || string.IsNullOrEmpty(result.ZipPath))
                {
                    HubcapError = result.Message;
                    return;
                }

                var imported = _zipper.ImportZip(result.ZipPath);
                try { File.Delete(result.ZipPath); } catch { }

                if (!string.IsNullOrEmpty(imported.ManifestsDir) && imported.ManifestCount > 0)
                    ManifestsDir = imported.ManifestsDir;

                if (!string.IsNullOrEmpty(imported.FirstLuaPath))
                    LoadLuaFile(imported.FirstLuaPath);
                else
                    HubcapError = "ZIP contained no Lua configuration file.";
            }
            catch (Exception ex)
            {
                HubcapError = ex.Message;
            }
            finally
            {
                IsHubcapBusy = false;
            }
        }

        [RelayCommand]
        private void BrowseLuaFile()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select Lua Config File",
                Filter = "Lua files (*.lua)|*.lua|All files (*.*)|*.*",
                DefaultExt = ".lua"
            };
            if (dialog.ShowDialog() == true)
                LoadLuaFile(dialog.FileName);
        }

        [RelayCommand]
        private void BrowseZipFile()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select ZIP Archive",
                Filter = "ZIP archives (*.zip)|*.zip|All files (*.*)|*.*",
                DefaultExt = ".zip"
            };
            if (dialog.ShowDialog() == true)
                ImportZipFile(dialog.FileName);
        }

        public void ImportZipFile(string path)
        {
            if (!File.Exists(path)) return;
            ZipError = string.Empty;
            try
            {
                var imported = _zipper.ImportZip(path);

                if (!string.IsNullOrEmpty(imported.ManifestsDir) && imported.ManifestCount > 0)
                    ManifestsDir = imported.ManifestsDir;

                if (!string.IsNullOrEmpty(imported.FirstLuaPath))
                    LoadLuaFile(imported.FirstLuaPath);
                else
                    ZipError = "ZIP contained no Lua configuration file.";
            }
            catch (Exception ex)
            {
                ZipError = ex.Message;
            }
        }

        public void LoadLuaFile(string path)
        {
            if (!File.Exists(path)) return;
            try
            {
                LuaPath = path;
                LuaFileName = Path.GetFileName(path);
                ParseError = string.Empty;

                var (appId, depots) = _parser.Parse(path);
                if (string.IsNullOrEmpty(appId))
                {
                    ParseError = "Could not find AppID in Lua file.";
                    LuaLoaded = false;
                    return;
                }

                AppId = appId;
                Depots = new ObservableCollection<DepotSelectionItem>(
                    depots.Select(d => new DepotSelectionItem(d)));

                var settings2 = _settings.Load();
                if (settings2.AutoSelectOsByOs)
                    ApplySmartOsFilter(Depots);

                _ = EnrichDepotsOsAsync(appId, Depots.ToList());
                _ = EnrichDepotsDlcAsync(appId, Depots.ToList());

                LuaLoaded = true;

                var settings = _settings.Load();
                if (!string.IsNullOrWhiteSpace(settings.DownloadBaseDir))
                {
                    var folderName = SteamMetadataService.GetAppName(appId);
                    if (string.IsNullOrEmpty(folderName))
                    {
                        folderName = Path.GetFileNameWithoutExtension(path);
                    }
                    if (string.IsNullOrEmpty(folderName))
                    {
                        folderName = appId;
                    }
                    folderName = SanitizeFolderName(folderName);
                    OutputDir = Path.Combine(settings.DownloadBaseDir, folderName);
                }

                CurrentStep = DownloadStep.ConfigureDepots;
                UpdateCanStart();
            }
            catch (Exception ex)
            {
                ParseError = $"Parse error: {ex.Message}";
                LuaLoaded = false;
            }
        }

        [RelayCommand]
        private void BrowseOutputDir()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select Download Output Folder"
            };
            if (dialog.ShowDialog() == true)
                OutputDir = dialog.FolderName;
        }

        [RelayCommand]
        private void BrowseManifestsDir()
        {
            var dialog = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Select Manifests Cache Folder"
            };
            if (dialog.ShowDialog() == true)
                ManifestsDir = dialog.FolderName;
        }

        [RelayCommand]
        private void SelectAllDepots()
        {
            if (_filteredDepots == null) return;
            foreach (DepotSelectionItem d in _filteredDepots) d.IsSelected = true;
            UpdateCanStart();
        }

        [RelayCommand]
        private void SelectNoDepots()
        {
            if (_filteredDepots == null) return;
            foreach (DepotSelectionItem d in _filteredDepots) d.IsSelected = false;
            UpdateCanStart();
        }

        private static void ApplySmartOsFilter(IEnumerable<DepotSelectionItem> items)
        {
            var currentOs = OperatingSystem.IsWindows() ? "windows"
                             : OperatingSystem.IsLinux() ? "linux"
                             : OperatingSystem.IsMacOS() ? "macos"
                             : string.Empty;

            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.OsList))
                    continue;

                var tags = item.OsList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var hasCurrentOs = !string.IsNullOrEmpty(currentOs) &&
                                    tags.Any(t => t.Equals(currentOs, StringComparison.OrdinalIgnoreCase));
                if (!hasCurrentOs)
                    item.IsSelected = false;
            }
        }

        private async Task EnrichDepotsOsAsync(string appId, List<DepotSelectionItem> items)
        {
            try
            {
                var meta = await SteamMetadataService.GetDepotMetaAsync(appId);
                if (meta.Count == 0) return;

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (AppId != appId) return;
                    var settings = _settings.Load();
                    foreach (var item in items)
                    {
                        if (!meta.TryGetValue(item.DepotId, out var m)) continue;
                        if (string.IsNullOrWhiteSpace(item.Depot.Name))
                        {
                            item.Depot.Name = m.Name;
                            item.DisplayName = item.Depot.DisplayName;
                        }
                        item.OsList = m.OsList;
                        item.OsArch = m.OsArch;
                    }
                    if (settings.AutoSelectOsByOs)
                        ApplySmartOsFilter(items);

                    var realGameName = SteamMetadataService.GetAppName(appId);
                    if (!string.IsNullOrWhiteSpace(realGameName) && !string.IsNullOrWhiteSpace(settings.DownloadBaseDir))
                    {
                        var sanitizedGameName = SanitizeFolderName(realGameName);
                        var oldDefaultFolder = !string.IsNullOrEmpty(LuaPath) ? Path.GetFileNameWithoutExtension(LuaPath) : string.Empty;
                        if (string.IsNullOrEmpty(oldDefaultFolder)) oldDefaultFolder = appId;
                        oldDefaultFolder = SanitizeFolderName(oldDefaultFolder);

                        var currentFolder = Path.GetFileName(OutputDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                        if (string.Equals(currentFolder, oldDefaultFolder, StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(currentFolder, appId, StringComparison.OrdinalIgnoreCase))
                        {
                            OutputDir = Path.Combine(settings.DownloadBaseDir, sanitizedGameName);
                        }
                    }

                    UpdateCanStart();
                });
            }
            catch { }
        }

        private async Task EnrichDepotsDlcAsync(string appId, List<DepotSelectionItem> items)
        {
            try
            {
                var dlcIds = await SteamDlcService.GetDlcIdsAsync();
                if (dlcIds.Count == 0) return;
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (AppId != appId) return;
                    foreach (var item in items)
                        item.IsDlc = dlcIds.Contains(item.DepotId);
                    RefreshFilter();
                });
            }
            catch { }
        }

        private void UpdateCanStart()
        {
            if (!LuaLoaded)
            {
                StartBlockReason = string.Empty;
            }
            else if (string.IsNullOrWhiteSpace(OutputDir))
            {
                StartBlockReason = "Output directory is required.";
            }
            else if (!Depots.Any(d => d.IsSelected))
            {
                StartBlockReason = "No depots selected.";
            }
            else
            {
                StartBlockReason = string.Empty;
            }

            CanStart = LuaLoaded &&
                       !string.IsNullOrWhiteSpace(OutputDir) &&
                       Depots.Any(d => d.IsSelected);
            StartDownloadAsyncCommand.NotifyCanExecuteChanged();
        }

        private async Task StartDownloadAsync()
        {
            if (!CanStart) return;

            if (!_downloader.Initialize())
            {
                DialogService.ShowError("Missing Dependencies",
                    "Could not find .NET 9 runtime or DepotDownloaderMod.dll.\n" +
                    "Make sure .NET 9 is installed and DepotDownloaderMod.dll is adjacent to this application.");
                return;
            }

            var selectedDepots = Depots.Where(d => d.IsSelected).Select(d => d.Depot).ToList();
            _lastSelectedDepots = selectedDepots;

            var states = selectedDepots.Select(d => new DepotDownloadState
            {
                DepotId = d.DepotId,
                DepotName = d.DisplayName
            }).ToList();

            Directory.CreateDirectory(OutputDir);
            DownloadStates = new ObservableCollection<DepotDownloadState>(states);
            CurrentStep = DownloadStep.Downloading;
            IsDownloading = true;
            DownloadComplete = false;
            DownloadFailed = false;
            OverallStatus = "Starting...";

            await ExecuteDownloadRunAsync(selectedDepots, states);
        }

        private static void ResetDepotState(DepotDownloadState s)
        {
            s.Status = DepotStatus.Queued;
            s.StatusText = "Queued";
            s.Percent = 0;
            s.DisplayPercent = 0;
            s.SpeedText = string.Empty;
            s.ActiveFile = string.Empty;
            s.ErrorMessage = null;
        }

        private async Task RetryFailedAsync()
        {
            if (IsDownloading || _lastSelectedDepots == null) return;

            var states = DownloadStates.ToList();
            foreach (var s in states.Where(s => s.Status == DepotStatus.Failed))
                ResetDepotState(s);

            IsDownloading = true;
            DownloadComplete = false;
            DownloadFailed = false;
            OverallStatus = "Retrying failed depots...";

            await ExecuteDownloadRunAsync(_lastSelectedDepots, states);
        }

        private async Task RetryDepotAsync(DepotDownloadState? state)
        {
            if (state == null || IsDownloading || _lastSelectedDepots == null) return;

            ResetDepotState(state);

            IsDownloading = true;
            DownloadComplete = false;
            DownloadFailed = false;
            OverallStatus = $"Retrying {state.DisplayName}...";

            await ExecuteDownloadRunAsync(_lastSelectedDepots, DownloadStates.ToList());
        }

        private async Task ExecuteDownloadRunAsync(List<DepotInfo> depots, List<DepotDownloadState> states)
        {
            _cts = new CancellationTokenSource();
            _activeStates = states;

            _animTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(16)
            };
            _animTimer.Tick += OnAnimationTick;
            _animTimer.Start();

            try
            {
                await _downloader.RunDownloadsAsync(
                    AppId, depots, OutputDir,
                    string.IsNullOrWhiteSpace(ManifestsDir) ? null : ManifestsDir,
                    MaxParallel, states, _cts.Token,
                    ryuuApiKey: string.IsNullOrWhiteSpace(RyuuApiKey) ? null : RyuuApiKey.Trim(),
                    hubcapApiKey: string.IsNullOrWhiteSpace(HubcapApiKey) ? null : HubcapApiKey.Trim());

                var anyFailed = states.Any(s => s.Status == DepotStatus.Failed);
                DownloadComplete = !anyFailed;
                DownloadFailed = anyFailed;
                OverallPercent = anyFailed ? OverallPercent : 100;
                OverallStatus = anyFailed ? "Completed with errors" : "All depots downloaded!";
                CompletionMessage = anyFailed
                    ? $"{states.Count(s => s.Status == DepotStatus.Failed)} depot(s) failed."
                    : $"{states.Count(s => s.Status is DepotStatus.Done or DepotStatus.Skipped)} depot(s) complete.";

                if (!anyFailed)
                {
                    var gameName = SteamMetadataService.GetAppName(AppId);
                    if (string.IsNullOrWhiteSpace(gameName)) gameName = Path.GetFileNameWithoutExtension(LuaPath);
                    if (string.IsNullOrWhiteSpace(gameName)) gameName = $"App {AppId}";
                    var game = new LibraryGame
                    {
                        AppId = AppId,
                        GameName = gameName,
                        LuaPath = LuaPath,
                        OutputDir = OutputDir,
                        DepotIds = depots.Select(d => d.DepotId).ToList(),
                        InstallDate = DateTime.Now,
                        TotalSizeBytes = LibraryService.GetDirectorySize(OutputDir),
                        IsVerified = true,
                        BuildId = SteamMetadataService.GetBuildId(AppId, depots.Select(d => d.ManifestId).ToList())
                    };
                    _library.AddOrUpdate(game);
                }
            }
            catch (OperationCanceledException)
            {
                OverallStatus = "Download cancelled";
                CompletionMessage = "Download was cancelled.";
            }
            catch (Exception ex)
            {
                DownloadFailed = true;
                OverallStatus = "Download failed";
                CompletionMessage = ex.Message;
            }
            finally
            {
                _animTimer?.Stop();
                _animTimer = null;
                _activeStates = null;
                IsDownloading = false;

                OverallDisplayPercent = OverallPercent;
                foreach (var state in DownloadStates)
                    state.DisplayPercent = state.Percent;

                RetryFailedAsyncCommand.NotifyCanExecuteChanged();
                RetryDepotAsyncCommand.NotifyCanExecuteChanged();
            }
        }

        private void OnAnimationTick(object? sender, EventArgs e)
        {
            const double lerpFactor = 0.15;

            if (_activeStates != null)
                OverallPercent = _activeStates.Average(s => s.Percent);

            OverallDisplayPercent += (OverallPercent - OverallDisplayPercent) * lerpFactor;

            foreach (var state in DownloadStates)
                state.DisplayPercent += (state.Percent - state.DisplayPercent) * lerpFactor;
        }

        [RelayCommand]
        private void CancelDownload()
        {
            _cts?.Cancel();
        }

        [RelayCommand]
        private void StartNew() => ResetState();

        private void ResetState()
        {
            _cts?.Cancel();
            _lastSelectedDepots = null;
            CurrentStep = DownloadStep.SelectFile;
            LuaPath = string.Empty;
            LuaFileName = string.Empty;
            AppId = string.Empty;
            LuaLoaded = false;
            ParseError = string.Empty;
            ZipError = string.Empty;
            RyuuError = string.Empty;
            HubcapError = string.Empty;
            Depots.Clear();
            DepotSearchText = string.Empty;
            FilterWindows = true;
            FilterLinux = true;
            FilterMacOs = true;
            FilterCommon = true;
            FilterDlc = true;
            DownloadStates.Clear();
            IsDownloading = false;
            DownloadComplete = false;
            DownloadFailed = false;
            OverallPercent = 0;
            OverallDisplayPercent = 0;
            RetryFailedAsyncCommand.NotifyCanExecuteChanged();
            RetryDepotAsyncCommand.NotifyCanExecuteChanged();
        }

        public void PreFillFromLibraryGame(LibraryGame game, bool clearCheckpoints)
        {
            if (IsDownloading) return;
            ResetState();

            if (!File.Exists(game.LuaPath))
            {
                DialogService.ShowError("Lua Not Found",
                    $"The Lua configuration file for this game could not be found:\n{game.LuaPath}");
                return;
            }

            LoadLuaFile(game.LuaPath);
            if (!LuaLoaded) return;

            OutputDir = game.OutputDir;

            var depotIdSet = new HashSet<string>(game.DepotIds, StringComparer.OrdinalIgnoreCase);
            foreach (var d in Depots)
                d.IsSelected = depotIdSet.Count == 0 || depotIdSet.Contains(d.DepotId);

            if (clearCheckpoints)
            {
                var checkpointDir = Path.Combine(game.OutputDir, ".depotdl_progress");
                if (Directory.Exists(checkpointDir))
                    Directory.Delete(checkpointDir, true);
            }

            UpdateCanStart();
            _ = StartDownloadAsync();
        }

        public void PreFillAppId(string appId, ManifestProvider? provider = null)
        {
            if (IsDownloading) return;
            _cts?.Cancel();
            CurrentStep = DownloadStep.SelectFile;
            LuaPath = string.Empty;
            LuaFileName = string.Empty;
            AppId = string.Empty;
            LuaLoaded = false;
            ParseError = string.Empty;
            ZipError = string.Empty;
            RyuuError = string.Empty;
            Depots.Clear();
            DownloadStates.Clear();
            DownloadComplete = false;
            DownloadFailed = false;
            OverallPercent = 0;
            OverallDisplayPercent = 0;

            RyuuAppId = appId;
            HubcapAppId = appId;
            HubcapError = string.Empty;

            var settings = _settings.Load();

            if (provider == ManifestProvider.Ryuu && !string.IsNullOrWhiteSpace(settings.RyuuApiKey))
            {
                RyuuApiKey = settings.RyuuApiKey;
                _ = FetchFromRyuuAsync();
            }
            else if (provider == ManifestProvider.Hubcap && !string.IsNullOrWhiteSpace(settings.HubcapApiKey))
            {
                HubcapApiKey = settings.HubcapApiKey;
                _ = FetchFromHubcapAsync();
            }
            else if (provider == null)
            {
                if (!string.IsNullOrWhiteSpace(settings.RyuuApiKey))
                {
                    RyuuApiKey = settings.RyuuApiKey;
                    _ = FetchFromRyuuAsync();
                }
                else if (!string.IsNullOrWhiteSpace(settings.HubcapApiKey))
                {
                    HubcapApiKey = settings.HubcapApiKey;
                    _ = FetchFromHubcapAsync();
                }
            }
        }

        private static string SanitizeFolderName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;
            var invalid = Path.GetInvalidFileNameChars();
            foreach (var c in invalid)
            {
                name = name.Replace(c, ' ');
            }
            return string.Join(" ", name.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries)).Trim();
        }
    }

    public partial class DepotSelectionItem : ObservableObject
    {
        public DepotInfo Depot { get; }
        [ObservableProperty] private bool _isSelected = true;
        [ObservableProperty] private bool _isDlc;
        [ObservableProperty] private string _osList = string.Empty;
        [ObservableProperty] private string _osArch = string.Empty;
        [ObservableProperty] private string _displayName = string.Empty;

        public string DepotId => Depot.DepotId;

        public string OsText
        {
            get
            {
                if (string.IsNullOrWhiteSpace(OsList) && string.IsNullOrWhiteSpace(OsArch))
                    return string.Empty;
                var os = string.IsNullOrWhiteSpace(OsList) ? "any" : OsList.Replace(",", "/");
                return string.IsNullOrWhiteSpace(OsArch) ? os : $"{os} {OsArch}";
            }
        }

        partial void OnOsListChanged(string value) => OnPropertyChanged(nameof(OsText));
        partial void OnOsArchChanged(string value) => OnPropertyChanged(nameof(OsText));

        public DepotSelectionItem(DepotInfo depot)
        {
            Depot = depot;
            _osList = depot.OsList;
            _osArch = depot.OsArch;
            _displayName = depot.DisplayName;
        }
    }
}
