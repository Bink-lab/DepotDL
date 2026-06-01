using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepotDL.GUI.Models;
using DepotDL.GUI.Services;

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

        public void Load()
        {
            var raw = _lib.Load();
            _lib.VerifyAll(raw);

            Games = new ObservableCollection<LibraryGameViewModel>(
                raw.Select(g => new LibraryGameViewModel(g)));
            FilterGames();
            UpdateStats();
        }

        public async Task LoadAsync()
        {
            var raw = await Task.Run(() =>
            {
                var loaded = _lib.Load();
                _lib.VerifyAll(loaded);
                return loaded;
            });
            Games = new ObservableCollection<LibraryGameViewModel>(
                raw.Select(g => new LibraryGameViewModel(g)));
            FilterGames();
            UpdateStats();
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
            _lib.Remove(vm.Game.AppId);
            Games.Remove(vm);
            UpdateStats();
            FilterGames();
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

    public partial class LibraryGameViewModel : ObservableObject
    {
        public LibraryGame Game { get; }

        [ObservableProperty] private bool _isVisible = true;
        [ObservableProperty] private string _sizeText = string.Empty;

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
    }
}
