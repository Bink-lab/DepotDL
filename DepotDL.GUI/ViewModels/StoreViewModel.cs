using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DepotDL.GUI.Helpers;
using DepotDL.GUI.Models;
using DepotDL.GUI.Services;

namespace DepotDL.GUI.ViewModels
{
    public partial class StoreViewModel : ViewModelBase
    {
        private readonly MainViewModel _main;
        private readonly SteamStoreService _svc = new();
        private readonly BenchmarkService _geekbench = new();

        private List<StoreGameViewModel> _allVMs = new();
        private List<StoreGameViewModel> _filteredVMs = new();
        private CancellationTokenSource? _loadCts;
        private CancellationTokenSource? _imageCts;
        private CancellationTokenSource? _searchCts;
        private CancellationTokenSource? _specsCts;
        private bool _loaded;

        private int _pageSize = 48;
        private int _searchDebounceMs = 250;

        [ObservableProperty] private ObservableCollection<StoreGameViewModel> _displayedGames = new();
        [ObservableProperty] private string _searchText = string.Empty;
        [ObservableProperty] private int _currentPage = 1;
        [ObservableProperty] private int _totalPages = 1;
        [ObservableProperty] private int _totalGames;
        [ObservableProperty] private bool _isLoading;
        [ObservableProperty] private string _loadingStatus = string.Empty;
        [ObservableProperty] private bool _overlayVisible;
        [ObservableProperty] private bool _isPageTransitioning;
        [ObservableProperty] private bool _isReqsExpanded;
        [ObservableProperty] private bool _isDescriptionExpanded;
        [ObservableProperty] private ObservableCollection<SpecRow> _specRows = new();

        public bool ShowLoadingSpinner => IsLoading && TotalGames == 0;
        public bool ShowEmptyState => !IsLoading && TotalGames == 0 && _loaded;
        public bool HasGames => TotalGames > 0;
        [ObservableProperty] private StoreGameViewModel? _selectedGame;
        [ObservableProperty] private SteamAppDetail? _selectedDetail;
        [ObservableProperty] private bool _isDetailLoading;
        [ObservableProperty] private bool _isSpecsLoading;

        public bool HasPrev => CurrentPage > 1;
        public bool HasNext => CurrentPage < TotalPages;
        public string PageInfo => TotalPages > 0 ? $"Page {CurrentPage} of {TotalPages}" : string.Empty;
        public string GamesInfo => TotalGames > 0 ? $"{TotalGames:N0} games" : string.Empty;

        public StoreViewModel(MainViewModel main)
        {
            _main = main;
            try
            {
                var s = new SettingsService().Load();
                _pageSize = s.StorePageSize;
                _searchDebounceMs = s.SearchDebounceMs;
            }
            catch { }
        }

        partial void OnSearchTextChanged(string value) => ScheduleSearch(value);
        partial void OnIsLoadingChanged(bool value)
        {
            OnPropertyChanged(nameof(ShowLoadingSpinner));
            OnPropertyChanged(nameof(ShowEmptyState));
        }
        partial void OnTotalGamesChanged(int value)
        {
            OnPropertyChanged(nameof(ShowLoadingSpinner));
            OnPropertyChanged(nameof(ShowEmptyState));
            OnPropertyChanged(nameof(HasGames));
        }
        partial void OnCurrentPageChanged(int value)
        {
            OnPropertyChanged(nameof(HasPrev));
            OnPropertyChanged(nameof(HasNext));
            OnPropertyChanged(nameof(PageInfo));
        }
        partial void OnSelectedDetailChanged(SteamAppDetail? value)
        {
            _specsCts?.Cancel();
            _specsCts = new CancellationTokenSource();
            RebuildSpecRows(value);
            _ = EnrichSpecRowsAsync(value, _specsCts.Token);
        }
        partial void OnOverlayVisibleChanged(bool value)
        {
            if (!value)
            {
                IsReqsExpanded = false;
                IsDescriptionExpanded = false;
            }
        }

        public void EnsureLoaded()
        {
            try
            {
                var s = new SettingsService().Load();
                _pageSize = s.StorePageSize;
                _searchDebounceMs = s.SearchDebounceMs;
            }
            catch { }
            if (_loaded || IsLoading) return;
            _loadCts?.Cancel();
            _loadCts = new CancellationTokenSource();
            _ = LoadAsync(_loadCts.Token);
        }

        private async Task LoadAsync(CancellationToken ct)
        {
            IsLoading = true;
            LoadingStatus = "Connecting to Steam...";

            try
            {
                var progress = new Progress<(int fetched, string status)>(p =>
                {
                    Application.Current.Dispatcher.Invoke(() => LoadingStatus = p.status);
                });

                var games = await _svc.GetAllGamesAsync(progress, ct);

                _allVMs = games.Select(g => new StoreGameViewModel(g)).ToList();
                TotalGames = _allVMs.Count;

                ApplyFilter(SearchText);
                _loaded = true;
                OnPropertyChanged(nameof(ShowEmptyState));
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                LoadingStatus = $"Error: {ex.Message}";
                _loaded = true;
                OnPropertyChanged(nameof(ShowEmptyState));
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void ScheduleSearch(string query)
        {
            _searchCts?.Cancel();
            _searchCts = new CancellationTokenSource();
            var cts = _searchCts;
            _ = Task.Delay(_searchDebounceMs, cts.Token)
                    .ContinueWith(_ =>
                    {
                        if (!cts.IsCancellationRequested)
                            Application.Current.Dispatcher.Invoke(() => ApplyFilter(query));
                    }, cts.Token, TaskContinuationOptions.OnlyOnRanToCompletion,
                       TaskScheduler.Default);
        }

        private void ApplyFilter(string query)
        {
            try
            {
                var s = new SettingsService().Load();
                _pageSize = s.StorePageSize;
            }
            catch { }

            query = query.Trim();
            _filteredVMs = string.IsNullOrEmpty(query)
                ? _allVMs
                : _allVMs.Where(g => g.Game.Name.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

            TotalGames = _filteredVMs.Count;
            TotalPages = Math.Max(1, (int)Math.Ceiling(_filteredVMs.Count / (double)_pageSize));
            CurrentPage = 1;
            OnPropertyChanged(nameof(GamesInfo));
            _ = ShowPageAsync(animate: false);
        }

        private async Task ShowPageAsync(bool animate = true)
        {
            _imageCts?.Cancel();
            _imageCts = new CancellationTokenSource();
            var cts = _imageCts;

            if (animate)
            {
                IsPageTransitioning = true;
                await Task.Delay(130);
            }

            int skip = (CurrentPage - 1) * _pageSize;
            var page = _filteredVMs.Skip(skip).Take(_pageSize).ToList();

            DisplayedGames = new ObservableCollection<StoreGameViewModel>(page);
            OnPropertyChanged(nameof(HasPrev));
            OnPropertyChanged(nameof(HasNext));
            OnPropertyChanged(nameof(PageInfo));

            if (animate) IsPageTransitioning = false;

            _ = Task.Run(async () =>
            {
                var sem = new SemaphoreSlim(8, 8);
                var tasks = page.Select(async vm =>
                {
                    await sem.WaitAsync(cts.Token);
                    try { await vm.LoadImageAsync(cts.Token); }
                    catch (OperationCanceledException) { }
                    finally { sem.Release(); }
                });
                try { await Task.WhenAll(tasks); }
                catch (OperationCanceledException) { }
            }, cts.Token);

            if (HasNext)
            {
                int nextSkip = CurrentPage * _pageSize;
                var nextPage = _filteredVMs.Skip(nextSkip).Take(_pageSize).ToList();
                _ = Task.Run(async () =>
                {
                    await Task.Delay(500, cts.Token);
                    var sem = new SemaphoreSlim(4, 4);
                    var tasks = nextPage.Select(async vm =>
                    {
                        await sem.WaitAsync(cts.Token);
                        try { await vm.LoadImageAsync(cts.Token); }
                        catch (OperationCanceledException) { }
                        finally { sem.Release(); }
                    });
                    try { await Task.WhenAll(tasks); }
                    catch (OperationCanceledException) { }
                }, cts.Token);
            }
        }

        [RelayCommand]
        private async Task NextPage()
        {
            if (!HasNext) return;
            CurrentPage++;
            await ShowPageAsync(animate: true);
        }

        [RelayCommand]
        private async Task PrevPage()
        {
            if (!HasPrev) return;
            CurrentPage--;
            await ShowPageAsync(animate: true);
        }

        [RelayCommand]
        private async Task OpenOverlay(StoreGameViewModel? vm)
        {
            if (vm == null) return;
            SelectedGame = vm;
            SelectedDetail = null;
            OverlayVisible = true;

            IsDetailLoading = true;
            try
            {
                SelectedDetail = await _svc.GetAppDetailAsync(vm.Game.AppId);
            }
            catch { }
            finally { IsDetailLoading = false; }
        }

        [RelayCommand]
        private void CloseOverlay() => OverlayVisible = false;

        [RelayCommand]
        private void DownloadGame()
        {
            if (SelectedGame == null) return;
            OverlayVisible = false;
            _main.NavigateDownloadWithAppId(SelectedGame.Game.AppId.ToString());
        }

        [RelayCommand]
        private void ToggleReqs() => IsReqsExpanded = !IsReqsExpanded;

        [RelayCommand]
        private void ToggleDescription() => IsDescriptionExpanded = !IsDescriptionExpanded;

        private void RebuildSpecRows(SteamAppDetail? detail)
        {
            SpecRows.Clear();
            if (detail == null) return;

            PcSpecs? userSpecs = null;
            try { userSpecs = PcSpecsHelper.GetSpecs(); } catch { }

            var rows = new List<SpecRow>();

            if (detail.MinRequirements.TryGetValue("Memory", out var minRam) ||
                detail.RecommendedRequirements.TryGetValue("Memory", out _))
            {
                detail.MinRequirements.TryGetValue("Memory", out minRam);
                detail.RecommendedRequirements.TryGetValue("Memory", out var recRam);
                long userMb = userSpecs?.RamMb ?? 0;
                long minMb = RequirementsParser.ParseRamMb(minRam ?? string.Empty);
                long recMb = RequirementsParser.ParseRamMb(recRam ?? string.Empty);
                string userVal = userMb > 0 ? $"{userMb / 1024} GB" : "Unknown";
                var status = userMb == 0 ? SpecStatus.Unknown
                           : recMb > 0 && userMb >= recMb ? SpecStatus.MeetsRecommended
                           : recMb == 0 && minMb > 0 && userMb >= minMb ? SpecStatus.MeetsRecommended
                           : minMb > 0 && userMb >= minMb ? SpecStatus.MeetsMinimum
                           : minMb > 0 ? SpecStatus.BelowMinimum
                           : SpecStatus.Unknown;
                rows.Add(new SpecRow("Memory", minRam ?? "—", recRam ?? "—", userVal, status));
            }

            if (detail.MinRequirements.TryGetValue("Storage", out var minStore) ||
                detail.RecommendedRequirements.TryGetValue("Storage", out _))
            {
                detail.MinRequirements.TryGetValue("Storage", out minStore);
                detail.RecommendedRequirements.TryGetValue("Storage", out var recStore);
                double userFree = userSpecs?.FreeStorageGb ?? 0;
                double minGb = RequirementsParser.ParseStorageGb(minStore ?? string.Empty);
                double recGb = RequirementsParser.ParseStorageGb(recStore ?? string.Empty);
                string userVal = userFree > 0 ? $"{userFree} GB free" : "Unknown";
                var status = userFree == 0 ? SpecStatus.Unknown
                           : recGb > 0 && userFree >= recGb ? SpecStatus.MeetsRecommended
                           : recGb == 0 && minGb > 0 && userFree >= minGb ? SpecStatus.MeetsRecommended
                           : minGb > 0 && userFree >= minGb ? SpecStatus.MeetsMinimum
                           : minGb > 0 ? SpecStatus.BelowMinimum
                           : SpecStatus.Unknown;
                rows.Add(new SpecRow("Storage", minStore ?? "—", recStore ?? "—", userVal, status));
            }

            if (detail.MinRequirements.TryGetValue("Processor", out var minCpu) ||
                detail.RecommendedRequirements.TryGetValue("Processor", out _))
            {
                detail.MinRequirements.TryGetValue("Processor", out minCpu);
                detail.RecommendedRequirements.TryGetValue("Processor", out var recCpu);
                string userVal = userSpecs?.CpuName ?? "Unknown";
                rows.Add(new SpecRow("Processor", minCpu ?? "—", recCpu ?? "—", userVal, SpecStatus.Unknown));
            }

            if (detail.MinRequirements.TryGetValue("Graphics", out var minGpu) ||
                detail.RecommendedRequirements.TryGetValue("Graphics", out _))
            {
                detail.MinRequirements.TryGetValue("Graphics", out minGpu);
                detail.RecommendedRequirements.TryGetValue("Graphics", out var recGpu);
                string userVal = userSpecs?.GpuName ?? "Unknown";
                rows.Add(new SpecRow("Graphics", minGpu ?? "—", recGpu ?? "—", userVal, SpecStatus.Unknown));
            }

            if (detail.MinRequirements.TryGetValue("OS", out var minOs))
            {
                detail.RecommendedRequirements.TryGetValue("OS", out var recOs);
                rows.Add(new SpecRow("OS", minOs, recOs ?? "—", "Windows 10/11", SpecStatus.Unknown));
            }

            foreach (var r in rows) SpecRows.Add(r);
        }

        private async Task EnrichSpecRowsAsync(SteamAppDetail? detail, CancellationToken ct)
        {
            if (detail == null) return;

            IsSpecsLoading = true;
            try
            {
                PcSpecs? userSpecs = null;
                try { userSpecs = PcSpecsHelper.GetSpecs(); } catch { }

                int cpuIdx = -1;
                for (int i = 0; i < SpecRows.Count; i++)
                    if (SpecRows[i].Label == "Processor") { cpuIdx = i; break; }

                int gpuIdx = -1;
                for (int i = 0; i < SpecRows.Count; i++)
                    if (SpecRows[i].Label == "Graphics") { gpuIdx = i; break; }

                if (cpuIdx < 0 && gpuIdx < 0) return;

                detail.MinRequirements.TryGetValue("Processor", out var minCpuReq);
                detail.RecommendedRequirements.TryGetValue("Processor", out var recCpuReq);
                detail.MinRequirements.TryGetValue("Graphics", out var minGpuReq);
                detail.RecommendedRequirements.TryGetValue("Graphics", out var recGpuReq);

                var minCpuName = RequirementsParser.ExtractFirstCpuModel(minCpuReq ?? string.Empty);
                var recCpuName = RequirementsParser.ExtractFirstCpuModel(recCpuReq ?? string.Empty);
                var minGpuName = RequirementsParser.ExtractFirstGpuModel(minGpuReq ?? string.Empty);
                var recGpuName = RequirementsParser.ExtractFirstGpuModel(recGpuReq ?? string.Empty);

                async Task<BenchmarkScore> SafeGetCpuScoreAsync(string name)
                {
                    try { return await _geekbench.GetCpuScoreAsync(name, ct).ConfigureAwait(false); }
                    catch { return BenchmarkScore.Unknown; }
                }

                async Task<BenchmarkScore> SafeGetGpuScoreAsync(string name)
                {
                    try { return await _geekbench.GetGpuScoreAsync(name, ct).ConfigureAwait(false); }
                    catch { return BenchmarkScore.Unknown; }
                }

                var tasks = new List<Task>();

                Task<BenchmarkScore>? userCpuTask = null, minCpuTask = null, recCpuTask = null;
                Task<BenchmarkScore>? userGpuTask = null, minGpuTask = null, recGpuTask = null;

                if (cpuIdx >= 0 && userSpecs?.CpuName != null)
                {
                    userCpuTask = SafeGetCpuScoreAsync(userSpecs.CpuName);
                    tasks.Add(userCpuTask);
                }
                if (minCpuName != null) { minCpuTask = SafeGetCpuScoreAsync(minCpuName); tasks.Add(minCpuTask); }
                if (recCpuName != null) { recCpuTask = SafeGetCpuScoreAsync(recCpuName); tasks.Add(recCpuTask); }

                if (gpuIdx >= 0 && userSpecs?.GpuName != null)
                {
                    userGpuTask = SafeGetGpuScoreAsync(userSpecs.GpuName);
                    tasks.Add(userGpuTask);
                }
                if (minGpuName != null) { minGpuTask = SafeGetGpuScoreAsync(minGpuName); tasks.Add(minGpuTask); }
                if (recGpuName != null) { recGpuTask = SafeGetGpuScoreAsync(recGpuName); tasks.Add(recGpuTask); }

                await Task.WhenAll(tasks).ConfigureAwait(false);

                ct.ThrowIfCancellationRequested();

                var userCpu = userCpuTask?.Result ?? BenchmarkScore.Unknown;
                var minCpu  = minCpuTask?.Result  ?? BenchmarkScore.Unknown;
                var recCpu  = recCpuTask?.Result  ?? BenchmarkScore.Unknown;
                var userGpu = userGpuTask?.Result ?? BenchmarkScore.Unknown;
                var minGpu  = minGpuTask?.Result  ?? BenchmarkScore.Unknown;
                var recGpu  = recGpuTask?.Result  ?? BenchmarkScore.Unknown;

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (cpuIdx >= 0 && cpuIdx < SpecRows.Count)
                    {
                        var row = SpecRows[cpuIdx];
                        var status = ScoreStatus(userCpu, minCpu, recCpu);
                        var userVal = userCpu.IsKnown
                            ? $"{userSpecs?.CpuName ?? row.UserValue}\n{userCpu.DisplayCpu}"
                            : row.UserValue;
                        SpecRows[cpuIdx] = row with { UserValue = userVal, Status = status };
                    }

                    if (gpuIdx >= 0 && gpuIdx < SpecRows.Count)
                    {
                        var row = SpecRows[gpuIdx];
                        var status = ScoreStatus(userGpu, minGpu, recGpu);
                        var userVal = userGpu.IsKnown
                            ? $"{userSpecs?.GpuName ?? row.UserValue}\n{userGpu.DisplayGpu}"
                            : row.UserValue;
                        SpecRows[gpuIdx] = row with { UserValue = userVal, Status = status };
                    }
                });
            }
            catch (OperationCanceledException) { }
            catch { }
            finally
            {
                IsSpecsLoading = false;
            }
        }

        private static SpecStatus ScoreStatus(BenchmarkScore user, BenchmarkScore min, BenchmarkScore rec)
        {
            if (!user.IsKnown) return SpecStatus.Unknown;
            if (rec.IsKnown && user.SingleCore >= rec.SingleCore) return SpecStatus.MeetsRecommended;
            if (rec.IsKnown && user.MultiCore > 0 && user.MultiCore >= rec.MultiCore) return SpecStatus.MeetsRecommended;
            if (min.IsKnown && user.SingleCore >= min.SingleCore) return SpecStatus.MeetsMinimum;
            if (min.IsKnown && user.MultiCore > 0 && user.MultiCore >= min.MultiCore) return SpecStatus.MeetsMinimum;
            if (min.IsKnown) return SpecStatus.BelowMinimum;
            return SpecStatus.Unknown;
        }

        public void Cleanup()
        {
            _loadCts?.Cancel();
            _imageCts?.Cancel();
            _searchCts?.Cancel();
            _specsCts?.Cancel();
        }
    }
}
