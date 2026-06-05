using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;

namespace DepotDL.CLI
{
    internal static class StoreApiClient
    {
        private static readonly string CacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DepotDL", "cache");

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };
        private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };

        static StoreApiClient()
        {
            Http.DefaultRequestHeaders.UserAgent.ParseAdd("DepotDL/1.0");
        }

        public static List<StoreGame> GetAllGames(Action<string>? status = null)
        {
            Directory.CreateDirectory(CacheDir);
            string listPath = Path.Combine(CacheDir, "applist.json");
            string metaPath = Path.Combine(CacheDir, "applist_meta.json");

            if (File.Exists(listPath) && File.Exists(metaPath))
            {
                try
                {
                    var meta = JsonSerializer.Deserialize<CacheMeta>(File.ReadAllText(metaPath), Opts);
                    if (meta != null && (DateTime.UtcNow - meta.FetchedAt).TotalHours < 24)
                    {
                        status?.Invoke("Loading from cache...");
                        var cached = JsonSerializer.Deserialize<List<StoreGame>>(File.ReadAllText(listPath), Opts);
                        if (cached?.Count > 0) return cached;
                    }
                }
                catch { }
            }

            status?.Invoke("Fetching game list from Bonker API...");
            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    var json = Http.GetStringAsync("https://api.bonker.dev/api/applist?type=game", cts.Token).GetAwaiter().GetResult();
                    var resp = JsonSerializer.Deserialize<BonkerResponse>(json, Opts);
                    if (resp?.Apps == null || resp.Apps.Count == 0) throw new InvalidDataException("Empty response");

                    var games = resp.Apps
                        .Where(a => a.AppId > 0 && !string.IsNullOrWhiteSpace(a.Name))
                        .Select(a => new StoreGame { AppId = a.AppId, Name = a.Name })
                        .OrderBy(g => g.Name, StringComparer.OrdinalIgnoreCase)
                        .ToList();

                    File.WriteAllText(listPath, JsonSerializer.Serialize(games, Opts));
                    File.WriteAllText(metaPath, JsonSerializer.Serialize(new CacheMeta { FetchedAt = DateTime.UtcNow }, Opts));
                    return games;
                }
                catch (OperationCanceledException) { throw; }
                catch
                {
                    if (attempt == 2) throw;
                    Thread.Sleep(2000);
                }
            }
            return new List<StoreGame>();
        }

        public static StoreDetail? GetAppDetail(int appId)
        {
            Directory.CreateDirectory(CacheDir);
            string path = Path.Combine(CacheDir, $"detail_v2_{appId}.json");

            if (File.Exists(path))
            {
                var fi = new FileInfo(path);
                if ((DateTime.UtcNow - fi.LastWriteTimeUtc).TotalHours < 24)
                {
                    try
                    {
                        var cached = JsonSerializer.Deserialize<StoreDetail>(File.ReadAllText(path), Opts);
                        if (cached != null) return cached;
                    }
                    catch { }
                }
            }

            StoreDetail? spyResult = null;
            StoreDetail? storeResult = null;

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var json = Http.GetStringAsync($"https://steamspy.com/api.php?request=appdetails&appid={appId}", cts.Token).GetAwaiter().GetResult();
                var e = JsonSerializer.Deserialize<SpyEntry>(json, Opts);
                if (e != null)
                    spyResult = new StoreDetail
                    {
                        AppId = appId,
                        Name = e.Name,
                        Developer = e.Developer,
                        Publisher = e.Publisher,
                        Owners = e.Owners,
                        Positive = e.Positive,
                        Negative = e.Negative,
                        Genres = string.IsNullOrEmpty(e.Genre) ? new List<string>() : new List<string> { e.Genre },
                        PriceText = FormatPrice(e.Price),
                    };
            }
            catch { }

            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                var json = Http.GetStringAsync($"https://store.steampowered.com/api/appdetails?appids={appId}&cc=us", cts.Token).GetAwaiter().GetResult();
                var root = JsonSerializer.Deserialize<Dictionary<string, StoreEntry>>(json, Opts);
                if (root != null && root.TryGetValue(appId.ToString(), out var entry) && entry.Success && entry.Data != null)
                {
                    var d = entry.Data;
                    storeResult = new StoreDetail
                    {
                        AppId = appId,
                        Name = d.Name,
                        ShortDescription = StripHtml(d.ShortDescription),
                        PriceText = d.PriceOverview?.FinalFormatted ?? "Free to Play",
                        Genres = d.Genres?.Select(g => g.Description).ToList() ?? new List<string>(),
                    };
                }
            }
            catch { }

            if (spyResult == null && storeResult == null) return null;

            var merged = new StoreDetail
            {
                AppId = appId,
                Name = storeResult?.Name ?? spyResult?.Name ?? $"App {appId}",
                ShortDescription = storeResult?.ShortDescription ?? string.Empty,
                PriceText = storeResult?.PriceText ?? spyResult?.PriceText ?? "Unknown",
                Genres = storeResult?.Genres?.Count > 0 ? storeResult.Genres : spyResult?.Genres ?? new List<string>(),
                Positive = spyResult?.Positive ?? 0,
                Negative = spyResult?.Negative ?? 0,
                Owners = spyResult?.Owners ?? string.Empty,
                Developer = spyResult?.Developer ?? string.Empty,
                Publisher = spyResult?.Publisher ?? string.Empty,
            };

            try { File.WriteAllText(path, JsonSerializer.Serialize(merged, Opts)); }
            catch { }

            return merged;
        }

        private static string FormatPrice(string? raw)
        {
            if (string.IsNullOrEmpty(raw) || raw == "0") return "Free to Play";
            if (int.TryParse(raw, out int cents)) return $"${cents / 100.0:F2}";
            return raw;
        }

        private static string StripHtml(string? html)
        {
            if (string.IsNullOrEmpty(html)) return string.Empty;
            return System.Text.RegularExpressions.Regex.Replace(html, "<[^>]*>", " ")
                .Replace("&amp;", "&").Replace("&lt;", "<").Replace("&gt;", ">")
                .Replace("&nbsp;", " ").Replace("&quot;", "\"")
                .Replace("  ", " ").Trim();
        }

        internal class StoreGame
        {
            [JsonPropertyName("appid")] public int AppId { get; set; }
            [JsonPropertyName("name")]  public string Name { get; set; } = string.Empty;
        }

        internal class StoreDetail
        {
            [JsonPropertyName("appid")]             public int AppId { get; set; }
            [JsonPropertyName("name")]              public string Name { get; set; } = string.Empty;
            [JsonPropertyName("developer")]         public string Developer { get; set; } = string.Empty;
            [JsonPropertyName("publisher")]         public string Publisher { get; set; } = string.Empty;
            [JsonPropertyName("owners")]            public string Owners { get; set; } = string.Empty;
            [JsonPropertyName("positive")]          public int Positive { get; set; }
            [JsonPropertyName("negative")]          public int Negative { get; set; }
            [JsonPropertyName("price_text")]        public string PriceText { get; set; } = string.Empty;
            [JsonPropertyName("genres")]            public List<string> Genres { get; set; } = new();
            [JsonPropertyName("short_description")] public string ShortDescription { get; set; } = string.Empty;
        }

        private class BonkerResponse
        {
            [JsonPropertyName("apps")] public List<BonkerApp> Apps { get; set; } = new();
        }

        private class BonkerApp
        {
            [JsonPropertyName("appid")] public int AppId { get; set; }
            [JsonPropertyName("name")]  public string Name { get; set; } = string.Empty;
        }

        private class SpyEntry
        {
            [JsonPropertyName("name")]      public string Name { get; set; } = string.Empty;
            [JsonPropertyName("developer")] public string Developer { get; set; } = string.Empty;
            [JsonPropertyName("publisher")] public string Publisher { get; set; } = string.Empty;
            [JsonPropertyName("owners")]    public string Owners { get; set; } = string.Empty;
            [JsonPropertyName("positive")]  public int Positive { get; set; }
            [JsonPropertyName("negative")]  public int Negative { get; set; }
            [JsonPropertyName("price")]     public string? Price { get; set; }
            [JsonPropertyName("genre")]     public string Genre { get; set; } = string.Empty;
        }

        private class StoreEntry
        {
            [JsonPropertyName("success")] public bool Success { get; set; }
            [JsonPropertyName("data")]    public StoreData? Data { get; set; }
        }

        private class StoreData
        {
            [JsonPropertyName("name")]              public string Name { get; set; } = string.Empty;
            [JsonPropertyName("short_description")] public string ShortDescription { get; set; } = string.Empty;
            [JsonPropertyName("genres")]            public List<StoreGenre>? Genres { get; set; }
            [JsonPropertyName("price_overview")]    public StorePrice? PriceOverview { get; set; }
        }

        private class StoreGenre
        {
            [JsonPropertyName("description")] public string Description { get; set; } = string.Empty;
        }

        private class StorePrice
        {
            [JsonPropertyName("final_formatted")] public string FinalFormatted { get; set; } = string.Empty;
        }

        private class CacheMeta
        {
            public DateTime FetchedAt { get; set; }
        }
    }
}
