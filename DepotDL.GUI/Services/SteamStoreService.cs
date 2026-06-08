// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using DepotDL.GUI.Helpers;
using DepotDL.GUI.Models;

namespace DepotDL.GUI.Services
{
    public class SteamStoreService
    {
        private static readonly string CacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DepotDL.GUI", "cache");

        private static readonly string AllGamesPath = Path.Combine(CacheDir, "applist.json");
        private static readonly string MetaPath = Path.Combine(CacheDir, "applist_meta.json");

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(25) };
        private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };

        private readonly int _cacheHours = 24;

        public SteamStoreService()
        {
            try
            {
                var s = new SettingsService().Load();
                _cacheHours = s.StoreCacheHours;
            }
            catch { }
        }

        static SteamStoreService()
        {
            Http.DefaultRequestHeaders.UserAgent.ParseAdd("DepotDL/1.0");
        }

        public async Task<List<StoreGame>> GetAllGamesAsync(
            IProgress<(int fetched, string status)>? progress = null,
            CancellationToken ct = default)
        {
            Directory.CreateDirectory(CacheDir);
            await Task.Run(PruneExpiredDetailCache, ct);

            if (File.Exists(AllGamesPath) && File.Exists(MetaPath))
            {
                var meta = await ReadMetaAsync();
                if (meta != null && (DateTime.UtcNow - meta.FetchedAt).TotalHours < _cacheHours)
                {
                    progress?.Report((0, "Loading from cache..."));
                    var cached = await LoadCachedGamesAsync();
                    if (cached.Count > 0)
                    {
                        progress?.Report((cached.Count, $"Loaded {cached.Count:N0} games from cache"));
                        return cached;
                    }
                }
            }

            return await FetchAllPagesAsync(progress, ct);
        }

        private async Task<List<StoreGame>> FetchAllPagesAsync(
            IProgress<(int fetched, string status)>? progress,
            CancellationToken ct)
        {
            progress?.Report((0, "Fetching app list..."));

            for (var attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    var resp = await Http.GetAsync("https://api.bonker.dev/api/applist?type=game", ct);
                    resp.EnsureSuccessStatusCode();

                    var json = await resp.Content.ReadAsStringAsync(ct);
                    var result = JsonSerializer.Deserialize<BonkerAppListResponse>(json, Opts);
                    if (result?.Apps == null || result.Apps.Count == 0)
                        throw new InvalidDataException("Empty app list response");

                    var sorted = result.Apps
                        .Where(a => a.AppId > 0 && !string.IsNullOrWhiteSpace(a.Name))
                        .Select(a => new StoreGame { AppId = a.AppId, Name = a.Name })
                        .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    await SaveCachedGamesAsync(sorted);
                    progress?.Report((sorted.Count, $"Done — {sorted.Count:N0} games indexed"));
                    return sorted;
                }
                catch (OperationCanceledException) { throw; }
                catch
                {
                    if (attempt == 2) throw;
                    await Task.Delay(2000, ct);
                }
            }

            return new List<StoreGame>();
        }

        public async Task<SteamAppDetail?> GetAppDetailAsync(int appId, CancellationToken ct = default)
        {
            Directory.CreateDirectory(CacheDir);
            var path = Path.Combine(CacheDir, $"detail_v2_{appId}.json");

            if (File.Exists(path))
            {
                var fi = new FileInfo(path);
                if ((DateTime.UtcNow - fi.LastWriteTimeUtc).TotalHours < _cacheHours)
                {
                    try
                    {
                        var text = await File.ReadAllTextAsync(path, ct);
                        var cached = JsonSerializer.Deserialize<SteamAppDetail>(text, Opts);
                        if (cached != null) return cached;
                    }
                    catch { }
                }
            }

            var spyTask = FetchSpyDetailAsync(appId, ct);
            var storeTask = FetchStoreDetailAsync(appId, ct);
            await Task.WhenAll(spyTask, storeTask);

            var spy = spyTask.Result;
            var store = storeTask.Result;

            var detail = new SteamAppDetail
            {
                AppId = appId,
                Name = store?.Name ?? spy?.Name ?? $"App {appId}",
                ShortDescription = store?.ShortDescription ?? string.Empty,
                DetailedDescription = store?.DetailedDescription ?? string.Empty,
                HeaderImage = store?.HeaderImage
                    ?? $"https://api.bonker.dev/api/image-cache/app_{appId}_header.jpg",
                PriceText = store?.PriceText ?? FormatPrice(spy?.RawPrice),
                Genres = store?.Genres ?? spy?.Genres ?? new List<string>(),
                Screenshots = store?.Screenshots ?? new List<string>(),
                FullScreenshots = store?.FullScreenshots ?? new List<string>(),
                Positive = spy?.Positive ?? 0,
                Negative = spy?.Negative ?? 0,
                Owners = spy?.Owners ?? string.Empty,
                Developer = spy?.Developer ?? string.Empty,
                Publisher = spy?.Publisher ?? string.Empty,
                MinRequirements = store?.MinRequirements ?? new Dictionary<string, string>(),
                RecommendedRequirements = store?.RecommendedRequirements ?? new Dictionary<string, string>()
            };

            try { await File.WriteAllTextAsync(path, JsonSerializer.Serialize(detail, Opts), ct); }
            catch { }

            return detail;
        }

        private async Task<SpyDetailResult?> FetchSpyDetailAsync(int appId, CancellationToken ct)
        {
            try
            {
                var json = await Http.GetStringAsync(
                    $"https://steamspy.com/api.php?request=appdetails&appid={appId}", ct);
                var e = JsonSerializer.Deserialize<SpyDetailEntry>(json, Opts);
                if (e == null) return null;
                return new SpyDetailResult
                {
                    Name = e.Name,
                    Developer = e.Developer,
                    Publisher = e.Publisher,
                    Owners = e.Owners,
                    Positive = e.Positive,
                    Negative = e.Negative,
                    RawPrice = e.Price,
                    Genres = string.IsNullOrEmpty(e.Genre)
                        ? new List<string>()
                        : new List<string> { e.Genre }
                };
            }
            catch { return null; }
        }

        private async Task<SteamAppDetail?> FetchStoreDetailAsync(int appId, CancellationToken ct)
        {
            try
            {
                var json = await Http.GetStringAsync(
                    $"https://store.steampowered.com/api/appdetails?appids={appId}&cc=us", ct);

                var root = JsonSerializer.Deserialize<Dictionary<string, StoreApiEntry>>(json, Opts);
                if (root == null || !root.TryGetValue(appId.ToString(), out var entry)
                    || !entry.Success || entry.Data == null)
                    return null;

                var d = entry.Data;
                return new SteamAppDetail
                {
                    AppId = appId,
                    Name = d.Name,
                    ShortDescription = d.ShortDescription,
                    DetailedDescription = RequirementsParser.StripHtml(d.DetailedDescription),
                    HeaderImage = d.HeaderImage,
                    PriceText = d.PriceOverview?.FinalFormatted ?? "Free to Play",
                    Genres = d.Genres?.Select(g => g.Description).ToList() ?? new List<string>(),
                    Screenshots = d.Screenshots?.Select(s => s.PathThumbnail).ToList() ?? new List<string>(),
                    FullScreenshots = d.Screenshots?.Select(s => s.PathFull).ToList() ?? new List<string>(),
                    MinRequirements = RequirementsParser.Parse(d.PcRequirements?.Minimum ?? string.Empty),
                    RecommendedRequirements = RequirementsParser.Parse(d.PcRequirements?.Recommended ?? string.Empty)
                };
            }
            catch { return null; }
        }

        private void PruneExpiredDetailCache()
        {
            try
            {
                foreach (var f in Directory.GetFiles(CacheDir, "detail_v2_*.json"))
                {
                    try
                    {
                        if ((DateTime.UtcNow - new FileInfo(f).LastWriteTimeUtc).TotalHours >= _cacheHours)
                            File.Delete(f);
                    }
                    catch { }
                }
            }
            catch { }
        }

        private static string FormatPrice(string? raw) => PriceFormatter.Format(raw, "Free to Play");

        private async Task<List<StoreGame>> LoadCachedGamesAsync()
        {
            try
            {
                var json = await File.ReadAllTextAsync(AllGamesPath);
                return JsonSerializer.Deserialize<List<StoreGame>>(json, Opts) ?? new List<StoreGame>();
            }
            catch { return new List<StoreGame>(); }
        }

        private async Task SaveCachedGamesAsync(List<StoreGame> games)
        {
            try
            {
                await File.WriteAllTextAsync(AllGamesPath, JsonSerializer.Serialize(games, Opts));
                await File.WriteAllTextAsync(MetaPath,
                    JsonSerializer.Serialize(new CacheMeta { FetchedAt = DateTime.UtcNow }, Opts));
            }
            catch { }
        }

        private async Task<CacheMeta?> ReadMetaAsync()
        {
            try
            {
                var json = await File.ReadAllTextAsync(MetaPath);
                return JsonSerializer.Deserialize<CacheMeta>(json, Opts);
            }
            catch { return null; }
        }

        private class BonkerAppListResponse
        {
            [JsonPropertyName("apps")] public List<BonkerApp> Apps { get; set; } = new();
        }

        private class BonkerApp
        {
            [JsonPropertyName("appid")] public int AppId { get; set; }
            [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        }

        private class SpyDetailEntry
        {
            [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
            [JsonPropertyName("developer")] public string Developer { get; set; } = string.Empty;
            [JsonPropertyName("publisher")] public string Publisher { get; set; } = string.Empty;
            [JsonPropertyName("owners")] public string Owners { get; set; } = string.Empty;
            [JsonPropertyName("positive")] public int Positive { get; set; }
            [JsonPropertyName("negative")] public int Negative { get; set; }
            [JsonPropertyName("price")] public string? Price { get; set; }
            [JsonPropertyName("genre")] public string Genre { get; set; } = string.Empty;
        }

        private class SpyDetailResult
        {
            public string Name { get; set; } = string.Empty;
            public string Developer { get; set; } = string.Empty;
            public string Publisher { get; set; } = string.Empty;
            public string Owners { get; set; } = string.Empty;
            public int Positive { get; set; }
            public int Negative { get; set; }
            public string? RawPrice { get; set; }
            public List<string> Genres { get; set; } = new();
        }

        private class StoreApiEntry
        {
            [JsonPropertyName("success")] public bool Success { get; set; }
            [JsonPropertyName("data")] public StoreData? Data { get; set; }
        }

        private class StoreData
        {
            [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
            [JsonPropertyName("short_description")] public string ShortDescription { get; set; } = string.Empty;
            [JsonPropertyName("detailed_description")] public string DetailedDescription { get; set; } = string.Empty;
            [JsonPropertyName("header_image")] public string HeaderImage { get; set; } = string.Empty;
            [JsonPropertyName("genres")] public List<StoreGenre>? Genres { get; set; }
            [JsonPropertyName("price_overview")] public StorePrice? PriceOverview { get; set; }
            [JsonPropertyName("screenshots")] public List<StoreScreenshot>? Screenshots { get; set; }
            [JsonPropertyName("pc_requirements")] public StoreRequirements? PcRequirements { get; set; }
        }

        private class StoreRequirements
        {
            [JsonPropertyName("minimum")] public string Minimum { get; set; } = string.Empty;
            [JsonPropertyName("recommended")] public string Recommended { get; set; } = string.Empty;
        }

        private class StoreGenre
        {
            [JsonPropertyName("description")] public string Description { get; set; } = string.Empty;
        }

        private class StorePrice
        {
            [JsonPropertyName("final_formatted")] public string FinalFormatted { get; set; } = string.Empty;
        }

        private class StoreScreenshot
        {
            [JsonPropertyName("path_thumbnail")] public string PathThumbnail { get; set; } = string.Empty;
            [JsonPropertyName("path_full")] public string PathFull { get; set; } = string.Empty;
        }

        private class CacheMeta
        {
            public DateTime FetchedAt { get; set; }
        }
    }
}
