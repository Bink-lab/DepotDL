using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepotDL.GUI.Helpers;
using DepotDL.GUI.Models;
using DepotDL.GUI.Services;
using DepotDL.GUI.Views;

namespace DepotDL.GUI.ViewModels
{
    public partial class LibraryViewModel : ViewModelBase
    {
        private readonly LibraryService _lib = new();

        [ObservableProperty] private ObservableCollection<LibraryGameViewModel> _games = new();
        [ObservableProperty] private string _searchText = string.Empty;
        [ObservableProperty] private bool _isEmpty;
        [ObservableProperty] private int _gameCount;
        [ObservableProperty] private string _totalSizeText = string.Empty;

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
            UpdateStats();
            LoadImagesAsync(_imageCts.Token);
        }

        private void LoadImagesAsync(CancellationToken ct)
        {
            var vms = Games.ToList();
            _ = Task.Run(async () =>
            {
                var sem = new SemaphoreSlim(6, 6);
                var tasks = vms.Select(async vm =>
                {
                    await sem.WaitAsync(ct);
                    try { await vm.LoadImageAsync(ct); }
                    catch (OperationCanceledException) { }
                    finally { sem.Release(); }
                });
                try { await Task.WhenAll(tasks); }
                catch (OperationCanceledException) { }
            }, ct);
        }

        private void FilterGames()
        {
            var all = Games;
            string q = SearchText.Trim().ToLowerInvariant();
            foreach (var g in all)
                g.IsVisible = string.IsNullOrEmpty(q) ||
                              g.Game.GameName.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                              g.Game.AppId.Contains(q, StringComparison.OrdinalIgnoreCase);

            IsEmpty = !all.Any(g => g.IsVisible);
        }

        private void UpdateStats()
        {
            GameCount = Games.Count;
            long total = Games.Sum(g => g.Game.TotalSizeBytes);
            TotalSizeText = total > 0 ? LibraryService.FormatSize(total) : "";
        }

        [RelayCommand]
        private void RemoveGame(LibraryGameViewModel? vm)
        {
            if (vm == null) return;

            var dialog = new RemoveGameDialog(vm.Game.GameName)
            {
                Owner = Application.Current.MainWindow
            };

            if (dialog.ShowDialog() == true)
            {
                string? error = _lib.Remove(vm.Game.AppId, dialog.DeleteFiles ? vm.Game.OutputDir : null);
                Games.Remove(vm);
                UpdateStats();
                FilterGames();

                if (error != null)
                    DialogService.ShowError("Delete Failed",
                        $"Game removed from library, but files could not be deleted:\n{error}");
            }
        }

        [RelayCommand]
        private void OpenFolder(LibraryGameViewModel? vm)
        {
            if (vm == null || !Directory.Exists(vm.Game.OutputDir)) return;
            try { Process.Start("explorer.exe", vm.Game.OutputDir); } catch { }
        }

        [RelayCommand]
        private void RefreshSizes()
        {
            Task.Run(() =>
            {
                foreach (var vm in Games)
                {
                    if (!Directory.Exists(vm.Game.OutputDir)) continue;
                    long size = LibraryService.GetDirectorySize(vm.Game.OutputDir);
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
                System.Windows.Application.Current.Dispatcher.Invoke(UpdateStats);
            });
        }
    }

    public partial class LibraryGameViewModel : ObservableObject, ISteamGameViewModel
    {
        public LibraryGame Game { get; }

        [ObservableProperty] private bool _isVisible = true;
        [ObservableProperty] private string _sizeText = string.Empty;
        [ObservableProperty] private BitmapImage? _headerImage;
        [ObservableProperty] private bool _isImageLoading = true;

        public string DepotCount => Game.DepotIds.Count == 1
            ? "1 depot" : $"{Game.DepotIds.Count} depots";

        public string InstallDateText => Game.InstallDate == default
            ? "Unknown" : Game.InstallDate.ToString("MMM d, yyyy");

        public bool FolderExists => Directory.Exists(Game.OutputDir);

        public LibraryGameViewModel(LibraryGame game)
        {
            Game = game;
            SizeText = LibraryService.FormatSize(game.TotalSizeBytes);
        }

        public void RefreshSize() => SizeText = LibraryService.FormatSize(Game.TotalSizeBytes);

        public Task LoadImageAsync(CancellationToken ct = default)
        {
            if (!int.TryParse(Game.AppId, out int appId)) return Task.CompletedTask;
            string url = $"https://cdn.akamai.steamstatic.com/steam/apps/{appId}/header.jpg";
            return ImageLoader.LoadGameImageAsync(this, appId, url, ct);
        }
    }
}
