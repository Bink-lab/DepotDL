// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepotDL.CLI;
using DepotDL.GUI.Helpers;
using DepotDL.GUI.Models;
using DepotDL.GUI.Services;

namespace DepotDL.GUI.ViewModels
{
    public partial class LibraryViewModel : ViewModelBase
    {
        private readonly LibraryService _lib = new();
        private readonly SettingsService _settings = new();
        private readonly PackService _pack = new();
        private CancellationTokenSource? _packCts;

        [ObservableProperty] private ObservableCollection<LibraryGameViewModel> _games = new();
        [ObservableProperty] private string _searchText = string.Empty;
        [ObservableProperty] private bool _isEmpty;
        [ObservableProperty] private int _gameCount;
        [ObservableProperty] private string _totalSizeText = string.Empty;
        [ObservableProperty] private bool _isRemoveOverlayVisible;
        [ObservableProperty] private LibraryGameViewModel? _gameToRemove;
        [ObservableProperty] private bool _deleteFiles;
        [ObservableProperty] private bool _isRedownloadOverlayVisible;
        [ObservableProperty] private LibraryGameViewModel? _gameToRedownload;
        [ObservableProperty] private bool _isPackingGame;
        [ObservableProperty] private double _packPercent;
        [ObservableProperty] private string _packStatus = string.Empty;
        [ObservableProperty] private string _packGameName = string.Empty;
        [ObservableProperty] private bool _isOnlineFixBusy;
        [ObservableProperty] private string _onlineFixStatus = string.Empty;
        [ObservableProperty] private string _onlineFixGameName = string.Empty;

        public Action<LibraryGame>? ValidateHandler { get; set; }
        public Action<LibraryGame>? RedownloadHandler { get; set; }

        partial void OnSearchTextChanged(string value) => FilterGames();

        private CancellationTokenSource? _imageCts;

        public async Task LoadAsync()
        {
            var raw = await Task.Run(() =>
            {
                var loaded = _lib.Load();
                _lib.VerifyAll(loaded);
                return loaded;
            });

            _imageCts?.Cancel();
            _imageCts = new CancellationTokenSource();

            Games = new ObservableCollection<LibraryGameViewModel>(
                raw.Select(g => new LibraryGameViewModel(g)));

            FilterGames();
            _ = LoadImagesAsync(_imageCts.Token);
        }

        private void FilterGames()
        {
            var query = SearchText.Trim().ToLowerInvariant();
            var visibleCount = 0;
            long totalBytes = 0;
            foreach (var g in Games)
            {
                var match = string.IsNullOrEmpty(query) ||
                             g.Game.GameName.ToLowerInvariant().Contains(query) ||
                             g.Game.AppId.Contains(query);
                g.IsVisible = match;
                if (match) { visibleCount++; totalBytes += g.Game.TotalSizeBytes; }
            }
            IsEmpty = visibleCount == 0;
            GameCount = visibleCount;
            TotalSizeText = LibraryService.FormatSize(totalBytes);
        }

        private async Task LoadImagesAsync(CancellationToken ct)
        {
            var sem = new SemaphoreSlim(6, 6);
            var tasks = Games.ToList().Select(async vm =>
            {
                await sem.WaitAsync(ct);
                try { await vm.LoadImageAsync(ct); }
                catch (OperationCanceledException) { }
                finally { sem.Release(); }
            });
            try { await Task.WhenAll(tasks); }
            catch (OperationCanceledException) { }
        }

        [RelayCommand]
        private void ConfirmRemove(LibraryGameViewModel? vm)
        {
            if (vm == null) return;
            GameToRemove = vm;
            DeleteFiles = false;
            IsRemoveOverlayVisible = true;
        }

        [RelayCommand]
        private async Task DoRemove()
        {
            if (GameToRemove == null) return;
            var vm = GameToRemove;
            IsRemoveOverlayVisible = false;
            GameToRemove = null;

            var error = await Task.Run(() => _lib.Remove(vm.Game.AppId, DeleteFiles ? vm.Game.OutputDir : null));
            Games.Remove(vm);
            FilterGames();
            if (error != null)
                await DialogService.ShowErrorAsync("Delete Failed",
                    $"Game removed from library, but files could not be deleted:\n{error}");
        }

        [RelayCommand]
        private void CancelRemove()
        {
            IsRemoveOverlayVisible = false;
            GameToRemove = null;
        }

        [RelayCommand]
        private void ValidateFiles(LibraryGameViewModel? vm)
        {
            if (vm == null) return;
            ValidateHandler?.Invoke(vm.Game);
        }

        [RelayCommand]
        private void ConfirmRedownload(LibraryGameViewModel? vm)
        {
            if (vm == null) return;
            GameToRedownload = vm;
            IsRedownloadOverlayVisible = true;
        }

        [RelayCommand]
        private async Task DoRedownload()
        {
            if (GameToRedownload == null) return;
            var vm = GameToRedownload;
            IsRedownloadOverlayVisible = false;
            GameToRedownload = null;

            if (Directory.Exists(vm.Game.OutputDir))
            {
                try { Directory.Delete(vm.Game.OutputDir, true); }
                catch (Exception ex)
                {
                    await DialogService.ShowErrorAsync("Delete Failed", $"Could not delete game files:\n{ex.Message}");
                    return;
                }
            }

            RedownloadHandler?.Invoke(vm.Game);
        }

        [RelayCommand]
        private void CancelRedownload()
        {
            IsRedownloadOverlayVisible = false;
            GameToRedownload = null;
        }

        [RelayCommand]
        private async Task LaunchGame(LibraryGameViewModel? vm)
        {
            if (vm == null || !Directory.Exists(vm.Game.OutputDir)) return;

            if (!vm.Game.OnlineFixApplied)
            {
                var loadedSettings = _settings.Load();
                var webApiKey = loadedSettings.SteamWebApiKey;
                var downloadAchievementIcons = loadedSettings.DownloadAchievementIcons;
                var (fixSuccess, fixError) = await Task.Run(() => GameLauncher.EnsureGbeApplied(vm.Game.AppId, vm.Game.OutputDir, vm.Game.LuaPath, webApiKey, downloadAchievementIcons));
                if (!fixSuccess)
                {
                    var errorMsg = "Failed to apply Goldberg Steam Emulator fix to the game.";
                    if (!string.IsNullOrEmpty(fixError))
                        errorMsg += "\n\nDetails:\n" + fixError;
                    await DialogService.ShowErrorAsync("Fix Game Failed", errorMsg);
                    return;
                }
            }

            var exePath = GameLauncher.FindLaunchTarget(vm.Game.OutputDir);
            if (string.IsNullOrEmpty(exePath))
            {
                await DialogService.ShowErrorAsync("Launch Failed", "Could not find any suitable executable or launch script in the game folder.");
                return;
            }

            var launchEx = GameLauncher.Launch(exePath, Path.GetDirectoryName(exePath) ?? vm.Game.OutputDir);
            if (launchEx != null)
                await DialogService.ShowErrorAsync("Launch Failed", $"Could not start process:\n{launchEx.Message}");
        }

        [RelayCommand]
        private void OpenFolder(LibraryGameViewModel? vm)
        {
            if (vm == null || !Directory.Exists(vm.Game.OutputDir)) return;
            try
            {
                if (OperatingSystem.IsWindows())
                    Process.Start("explorer.exe", vm.Game.OutputDir);
                else if (OperatingSystem.IsMacOS())
                    Process.Start("open", vm.Game.OutputDir);
                else
                    Process.Start("xdg-open", vm.Game.OutputDir);
            }
            catch { }
        }

        [RelayCommand]
        private async Task RenameGame(LibraryGameViewModel? vm)
        {
            if (vm == null) return;
            var newName = await DialogService.ShowInputAsync("Rename Game", vm.Game.GameName);
            if (newName == null || newName == vm.Game.GameName) return;
            vm.ApplyRename(newName);
            _lib.AddOrUpdate(vm.Game);
        }


        [RelayCommand]
        private async Task PackGame(LibraryGameViewModel? vm)
        {
            if (vm == null || !Directory.Exists(vm.Game.OutputDir) || IsPackingGame) return;

            PackGameName = vm.Game.GameName;
            PackPercent = 0;
            PackStatus = "Preparing...";
            IsPackingGame = true;

            _packCts = new CancellationTokenSource();
            try
            {
                var progress = new Progress<(double percent, string status)>(p =>
                {
                    PackPercent = p.percent;
                    PackStatus = p.status;
                });
                await _pack.PackAsync(vm.Game.OutputDir, progress, _packCts.Token);
                PackPercent = 100;
                PackStatus = "Done!";
                await Task.Delay(2500);
            }
            catch (OperationCanceledException)
            {
                PackStatus = "Cancelled.";
                await Task.Delay(1500);
            }
            catch (Exception ex)
            {
                PackStatus = $"{ex.GetType().Name}: {ex.Message}";
                await Task.Delay(3000);
            }
            finally
            {
                _packCts.Dispose();
                _packCts = null;
                IsPackingGame = false;
            }
        }

        [RelayCommand]
        private void CancelPack()
        {
            _packCts?.Cancel();
        }

        [RelayCommand]
        private async Task InstallOnlineFix(LibraryGameViewModel? vm)
        {
            if (vm == null || !Directory.Exists(vm.Game.OutputDir) || IsOnlineFixBusy) return;

            var s = _settings.Load();
            if (string.IsNullOrWhiteSpace(s.OnlineFixUser) || string.IsNullOrWhiteSpace(s.OnlineFixPass))
            {
                await DialogService.ShowErrorAsync("OnlineFix Credentials Missing",
                    "Add your online-fix.me username and password in Settings before installing a fix.");
                return;
            }

            if (!OnlineFixService.IsChromiumInstalled())
            {
                var proceed = await DialogService.ShowConfirmAsync("Browser Engine Required",
                    "OnlineFix requires a browser engine (~170 MB) to be downloaded once.\n\nDownload it now?");
                if (!proceed) return;
            }

            OnlineFixGameName = vm.Game.GameName;
            OnlineFixStatus = "Starting...";
            IsOnlineFixBusy = true;
            try
            {
                var progress = new Progress<string>(msg => OnlineFixStatus = msg);
                var (success, error) = await OnlineFixService.ApplyAsync(
                    vm.Game.GameName, vm.Game.OutputDir, s.OnlineFixUser!, s.OnlineFixPass!, progress);

                if (success)
                {
                    vm.Game.OnlineFixApplied = true;
                    vm.RefreshOnlineFixState();
                    _lib.AddOrUpdate(vm.Game);
                    OnlineFixStatus = "Done!";
                    await Task.Delay(2000);
                }
                else
                {
                    OnlineFixStatus = $"Failed: {error}";
                    await Task.Delay(3500);
                    await DialogService.ShowErrorAsync("OnlineFix Failed", error ?? "Unknown error.");
                }
            }
            catch (Exception ex)
            {
                OnlineFixStatus = $"Error: {ex.Message}";
                await Task.Delay(3000);
            }
            finally
            {
                IsOnlineFixBusy = false;
            }
        }

        [RelayCommand]
        private void RemoveOnlineFix(LibraryGameViewModel? vm)
        {
            if (vm == null || !Directory.Exists(vm.Game.OutputDir)) return;
            try
            {
                OnlineFixService.Revert(vm.Game.OutputDir);
                vm.Game.OnlineFixApplied = false;
                vm.RefreshOnlineFixState();
                _lib.AddOrUpdate(vm.Game);
            }
            catch (Exception ex)
            {
                _ = DialogService.ShowErrorAsync("Remove OnlineFix Failed", ex.Message);
            }
        }

        [RelayCommand]
        private void RefreshSizes()
        {
            Task.Run(() =>
            {
                foreach (var vm in Games)
                {
                    if (!Directory.Exists(vm.Game.OutputDir)) continue;
                    var size = LibraryService.GetDirectorySize(vm.Game.OutputDir);
                    if (size != vm.Game.TotalSizeBytes)
                    {
                        vm.Game.TotalSizeBytes = size;
                        vm.RefreshSize();
                    }
                }
                var lib = _lib.Load();
                foreach (var g in lib)
                {
                    var vm = Games.FirstOrDefault(x => x.Game.AppId == g.AppId);
                    if (vm != null) g.TotalSizeBytes = vm.Game.TotalSizeBytes;
                }
                _lib.Save(lib.ToList());
                Dispatcher.UIThread.Post(FilterGames);
            });
        }
    }

    public partial class LibraryGameViewModel : ObservableObject, ISteamGameViewModel
    {
        public LibraryGame Game { get; }

        [ObservableProperty] private bool _isVisible = true;
        [ObservableProperty] private string _sizeText = string.Empty;
        [ObservableProperty] private Bitmap? _headerImage;
        [ObservableProperty] private bool _isImageLoading = true;
        [ObservableProperty] private string _gameName = string.Empty;

        public string DepotCount => Game.DepotIds.Count == 1
            ? "1 depot" : $"{Game.DepotIds.Count} depots";

        public string InstallDateText => Game.InstallDate == default
            ? "Unknown" : Game.InstallDate.ToString("MMM d, yyyy");

        public string BuildIdLabel => string.IsNullOrEmpty(Game.BuildId) ? string.Empty : "  ·  Build ";

        public bool FolderExists => Directory.Exists(Game.OutputDir);
        public bool OnlineFixApplied => Game.OnlineFixApplied;

        public LibraryGameViewModel(LibraryGame game)
        {
            Game = game;
            _gameName = game.GameName;
            SizeText = LibraryService.FormatSize(game.TotalSizeBytes);
        }

        public void RefreshSize() => SizeText = LibraryService.FormatSize(Game.TotalSizeBytes);
        public void RefreshOnlineFixState() => OnPropertyChanged(nameof(OnlineFixApplied));

        public void ApplyRename(string newName)
        {
            Game.GameName = newName;
            GameName = newName;
        }

        public Task LoadImageAsync(CancellationToken ct = default)
        {
            if (!int.TryParse(Game.AppId, out var appId)) return Task.CompletedTask;
            var url = $"https://api.bonker.dev/api/image-cache/app_{appId}_header.jpg";
            return ImageLoader.LoadGameImageAsync(this, appId, url, ct);
        }
    }
}
