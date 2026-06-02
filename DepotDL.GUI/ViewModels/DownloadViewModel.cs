using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepotDL.GUI.Models;
using DepotDL.GUI.Services;

namespace DepotDL.GUI.ViewModels
{
    public enum DownloadStep { SelectFile, ConfigureDepots, Downloading }

    public partial class DownloadViewModel : ViewModelBase
    {
        private readonly LuaParserService _parser = new();
        private readonly DownloadService _downloader = new();
        private readonly LibraryService _library = new();
        private readonly SettingsService _settings = new();
        private readonly RyuuService _ryuu = new();
        private readonly ZipImportService _zipper = new();

        private CancellationTokenSource? _cts;
        private System.Windows.Threading.DispatcherTimer? _animTimer;
        private List<DepotDownloadState>? _activeStates;

        [ObservableProperty] private DownloadStep _currentStep = DownloadStep.SelectFile;

        [ObservableProperty] private string _luaPath = string.Empty;
        [ObservableProperty] private string _luaFileName = string.Empty;
        [ObservableProperty] private string _appId = string.Empty;
        [ObservableProperty] private bool _luaLoaded;
        [ObservableProperty] private string _parseError = string.Empty;

        [ObservableProperty] private ObservableCollection<DepotSelectionItem> _depots = new();
        [ObservableProperty] private string _outputDir = string.Empty;
        [ObservableProperty] private string _manifestsDir = string.Empty;
        [ObservableProperty] private int _maxParallel = 2;
        [ObservableProperty] private bool _canStart;

        [ObservableProperty] private string _ryuuAppId = string.Empty;
        [ObservableProperty] private string _ryuuApiKey = string.Empty;
        [ObservableProperty] private bool _isRyuuBusy;
        [ObservableProperty] private string _ryuuError = string.Empty;
        [ObservableProperty] private bool _canFetchRyuu;

        [ObservableProperty] private ObservableCollection<DepotDownloadState> _downloadStates = new();
        [ObservableProperty] private double _overallPercent;
        [ObservableProperty] private double _overallDisplayPercent;
        [ObservableProperty] private string _overallStatus = string.Empty;
        [ObservableProperty] private bool _isDownloading;
        [ObservableProperty] private bool _downloadComplete;
        [ObservableProperty] private bool _downloadFailed;
        [ObservableProperty] private string _completionMessage = string.Empty;

        public IAsyncRelayCommand FetchFromRyuuAsyncCommand { get; }
        public IAsyncRelayCommand StartDownloadAsyncCommand { get; }

        public DownloadViewModel()
        {
            FetchFromRyuuAsyncCommand = new AsyncRelayCommand(FetchFromRyuuAsync, () => CanFetchRyuu);
            StartDownloadAsyncCommand = new AsyncRelayCommand(StartDownloadAsync, () => CanStart);

            var settings = _settings.Load();
            ManifestsDir = settings.ManifestsDir ?? string.Empty;
            MaxParallel = settings.MaxParallelDepots;
            RyuuApiKey = settings.RyuuApiKey ?? string.Empty;
            UpdateCanFetchRyuu();
        }

        partial void OnOutputDirChanged(string value) => UpdateCanStart();
        partial void OnDepotsChanged(ObservableCollection<DepotSelectionItem> value) => UpdateCanStart();
        partial void OnRyuuAppIdChanged(string value) => UpdateCanFetchRyuu();
        partial void OnRyuuApiKeyChanged(string value) => UpdateCanFetchRyuu();
        partial void OnIsRyuuBusyChanged(bool value) => UpdateCanFetchRyuu();

        private void UpdateCanFetchRyuu()
        {
            CanFetchRyuu = !IsRyuuBusy && !string.IsNullOrWhiteSpace(RyuuAppId);
            if (FetchFromRyuuAsyncCommand is AsyncRelayCommand cmd)
                cmd.NotifyCanExecuteChanged();
        }

        private async Task FetchFromRyuuAsync()
        {
            IsRyuuBusy = true;
            RyuuError = string.Empty;
            try
            {
                var settings = _settings.Load();
                settings.RyuuApiKey = RyuuApiKey.Trim();
                _settings.Save(settings);

                var result = await _ryuu.DownloadPackageAsync(RyuuAppId.Trim(), RyuuApiKey.Trim());
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

                if (_settings.Load().SmartOsFilter)
                    ApplySmartOsFilter();

                LuaLoaded = true;

                var settings = _settings.Load();
                if (!string.IsNullOrWhiteSpace(settings.DownloadBaseDir))
                    OutputDir = Path.Combine(settings.DownloadBaseDir, $"App_{appId}");

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
            foreach (var d in Depots) d.IsSelected = true;
            UpdateCanStart();
        }

        [RelayCommand]
        private void SelectNoDepots()
        {
            foreach (var d in Depots) d.IsSelected = false;
            UpdateCanStart();
        }

        private void ApplySmartOsFilter()
        {
            string currentOs = OperatingSystem.IsWindows() ? "windows"
                             : OperatingSystem.IsLinux()   ? "linux"
                             : OperatingSystem.IsMacOS()   ? "macos"
                             : string.Empty;

            foreach (var item in Depots)
            {
                var osList = item.Depot.OsList;
                if (string.IsNullOrWhiteSpace(osList))
                    continue;

                var tags = osList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                bool hasCurrentOs = !string.IsNullOrEmpty(currentOs) &&
                                    tags.Any(t => t.Equals(currentOs, StringComparison.OrdinalIgnoreCase));
                if (!hasCurrentOs)
                    item.IsSelected = false;
            }
        }

        private void UpdateCanStart()
        {
            CanStart = LuaLoaded &&
                       !string.IsNullOrWhiteSpace(OutputDir) &&
                       Depots.Any(d => d.IsSelected);
            if (StartDownloadAsyncCommand is AsyncRelayCommand cmd)
                cmd.NotifyCanExecuteChanged();
        }

        private async Task StartDownloadAsync()
        {
            if (!CanStart) return;

            if (!_downloader.Initialize())
            {
                MessageBox.Show(
                    "Could not find .NET 9 runtime or DepotDownloaderMod.dll.\n" +
                    "Make sure .NET 9 is installed and DepotDownloaderMod.dll is adjacent to this application.",
                    "Missing Dependencies", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var selectedDepots = Depots.Where(d => d.IsSelected).Select(d => d.Depot).ToList();
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
                    AppId, selectedDepots, OutputDir,
                    string.IsNullOrWhiteSpace(ManifestsDir) ? null : ManifestsDir,
                    MaxParallel, states, _cts.Token);

                bool anyFailed = states.Any(s => s.Status == DepotStatus.Failed);
                DownloadComplete = !anyFailed;
                DownloadFailed = anyFailed;
                OverallPercent = anyFailed ? OverallPercent : 100;
                OverallStatus = anyFailed ? "Completed with errors" : "All depots downloaded!";
                CompletionMessage = anyFailed
                    ? $"{states.Count(s => s.Status == DepotStatus.Failed)} depot(s) failed."
                    : $"{states.Count(s => s.Status == DepotStatus.Done)} depot(s) complete.";

                if (!anyFailed)
                {
                    string gameName = Path.GetFileNameWithoutExtension(LuaPath);
                    if (string.IsNullOrWhiteSpace(gameName)) gameName = $"App {AppId}";
                    var game = new LibraryGame
                    {
                        AppId = AppId,
                        GameName = gameName,
                        LuaPath = LuaPath,
                        OutputDir = OutputDir,
                        DepotIds = selectedDepots.Select(d => d.DepotId).ToList(),
                        InstallDate = DateTime.Now,
                        TotalSizeBytes = LibraryService.GetDirectorySize(OutputDir),
                        IsVerified = true
                    };
                    _library.AddOrUpdate(game);
                }
            }
            catch (OperationCanceledException)
            {
                OverallStatus = "Download cancelled";
                CompletionMessage = "Download was cancelled.";
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
        private void StartNew()
        {
            _cts?.Cancel();
            CurrentStep = DownloadStep.SelectFile;
            LuaPath = string.Empty;
            LuaFileName = string.Empty;
            AppId = string.Empty;
            LuaLoaded = false;
            ParseError = string.Empty;
            Depots.Clear();
            DownloadStates.Clear();
            IsDownloading = false;
            DownloadComplete = false;
            DownloadFailed = false;
            OverallPercent = 0;
            OverallDisplayPercent = 0;
        }

        public void PreFillAppId(string appId)
        {
            if (IsDownloading) return;
            _cts?.Cancel();
            CurrentStep = DownloadStep.SelectFile;
            LuaPath = string.Empty;
            LuaFileName = string.Empty;
            AppId = string.Empty;
            LuaLoaded = false;
            ParseError = string.Empty;
            RyuuError = string.Empty;
            Depots.Clear();
            DownloadStates.Clear();
            DownloadComplete = false;
            DownloadFailed = false;
            OverallPercent = 0;
            OverallDisplayPercent = 0;

            RyuuAppId = appId;

            var settings = _settings.Load();
            if (!string.IsNullOrWhiteSpace(settings.RyuuApiKey))
            {
                RyuuApiKey = settings.RyuuApiKey;
                _ = FetchFromRyuuAsync();
            }
        }
    }

    public partial class DepotSelectionItem : ObservableObject
    {
        public DepotInfo Depot { get; }
        [ObservableProperty] private bool _isSelected = true;

        public string DisplayName => Depot.DisplayName;
        public string DepotId => Depot.DepotId;
        public string OsText => string.IsNullOrWhiteSpace(Depot.OsList) ? "—" : Depot.OsList;

        public DepotSelectionItem(DepotInfo depot) => Depot = depot;
    }
}
