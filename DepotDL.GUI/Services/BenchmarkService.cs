using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DepotDL.GUI.Models;

namespace DepotDL.GUI.Services
{
    public sealed class BenchmarkService
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

        private readonly Dictionary<string, (BenchmarkScore score, DateTime fetchedAt)> _cache = new();
        private readonly SemaphoreSlim _cacheLock = new(1, 1);
        private readonly TimeSpan _cacheTtl = TimeSpan.FromHours(6);

        public BenchmarkService()
        {
            try
            {
                var s = new SettingsService().Load();
                _cacheTtl = TimeSpan.FromHours(s.StoreCacheHours / 4.0);
            }
            catch { }
        }

        private static List<CpuEntry>? _cpuDb;
        private static readonly SemaphoreSlim _cpuDbLock = new(1, 1);

        private static List<GpuEntry>? _gpuDb;
        private static readonly SemaphoreSlim _gpuDbLock = new(1, 1);
        private static TimeSpan GetGpuCacheTtl()
        {
            try
            {
                var s = new SettingsService().Load();
                return TimeSpan.FromDays(s.GpuCacheDays);
            }
            catch { return TimeSpan.FromDays(7); }
        }
        private static readonly string GpuCachePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DepotDL", "dbgpu_cache.json");

        static BenchmarkService()
        {
            Http.DefaultRequestHeaders.UserAgent.ParseAdd("DepotDL/1.0");
            Http.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        }

        public Task<BenchmarkScore> GetCpuScoreAsync(string hardwareName, CancellationToken ct = default)
            => FetchWithCacheAsync($"cpu:{hardwareName}", ct, async token =>
            {
                var normalized = NormalizeCpuName(hardwareName);
                if (string.IsNullOrEmpty(normalized)) return BenchmarkScore.Unknown;
                var db = await LoadCpuDbAsync(token).ConfigureAwait(false);
                var entry = FindBestCpuMatch(db, normalized);
                return entry != null ? new BenchmarkScore(entry.Score, entry.MulticoreScore) : BenchmarkScore.Unknown;
            });

        public Task<BenchmarkScore> GetGpuScoreAsync(string hardwareName, CancellationToken ct = default)
            => FetchWithCacheAsync($"gpu:{hardwareName}", ct, async token =>
            {
                var normalized = NormalizeGpuName(hardwareName);
                if (string.IsNullOrEmpty(normalized)) return BenchmarkScore.Unknown;
                var db = await LoadGpuDbAsync(token).ConfigureAwait(false);
                var entry = FindBestGpuMatch(db, normalized);
                if (entry == null) return BenchmarkScore.Unknown;
                int derived = ComputeGpuScore(entry);
                return derived > 0 ? new BenchmarkScore(derived, 0) : BenchmarkScore.Unknown;
            });

        private static async Task<List<CpuEntry>> LoadCpuDbAsync(CancellationToken ct)
        {
            await _cpuDbLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_cpuDb != null) return _cpuDb;

                var asm = Assembly.GetExecutingAssembly();
                var resourceName = asm.GetManifestResourceNames()
                    .FirstOrDefault(n => n.EndsWith("cpu_benchmarks.json", StringComparison.OrdinalIgnoreCase));

                if (resourceName == null) return _cpuDb = new List<CpuEntry>();

                using var stream = asm.GetManifestResourceStream(resourceName)!;
                using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

                var list = new List<CpuEntry>();
                if (doc.RootElement.TryGetProperty("devices", out var devices))
                {
                    foreach (var el in devices.EnumerateArray())
                    {
                        var name = el.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                        var sc = el.TryGetProperty("score", out var s) ? s.GetInt32() : 0;
                        var mc = el.TryGetProperty("multicore_score", out var m) ? m.GetInt32() : 0;
                        if (!string.IsNullOrEmpty(name) && sc > 0)
                            list.Add(new CpuEntry(NormalizeCpuName(name), sc, mc));
                    }
                }
                return _cpuDb = list;
            }
            finally { _cpuDbLock.Release(); }
        }

        private static CpuEntry? FindBestCpuMatch(List<CpuEntry> db, string normalized)
            => FindBestMatch(db, normalized, e => e.NormalizedName);

        private static async Task<List<GpuEntry>> LoadGpuDbAsync(CancellationToken ct)
        {
            await _gpuDbLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_gpuDb != null) return _gpuDb;

                if (File.Exists(GpuCachePath) &&
                    DateTime.UtcNow - File.GetLastWriteTimeUtc(GpuCachePath) < GetGpuCacheTtl())
                {
                    var cached = await File.ReadAllTextAsync(GpuCachePath, ct).ConfigureAwait(false);
                    _gpuDb = ParseGpuJson(cached);
                    if (_gpuDb.Count > 0) return _gpuDb;
                }

                var json = await FetchGpuJsonAsync(ct).ConfigureAwait(false);
                if (json != null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(GpuCachePath)!);
                    await File.WriteAllTextAsync(GpuCachePath, json, ct).ConfigureAwait(false);
                    _gpuDb = ParseGpuJson(json);
                }
                else if (File.Exists(GpuCachePath))
                {
                    var cached = await File.ReadAllTextAsync(GpuCachePath, ct).ConfigureAwait(false);
                    _gpuDb = ParseGpuJson(cached);
                }

                return _gpuDb ?? new List<GpuEntry>();
            }
            catch { return _gpuDb ?? new List<GpuEntry>(); }
            finally { _gpuDbLock.Release(); }
        }

        private static async Task<string?> FetchGpuJsonAsync(CancellationToken ct)
        {
            try
            {
                var apiUrl = "https://api.github.com/repos/painebenjamin/dbgpu/releases/latest";
                var releaseJson = await Http.GetStringAsync(apiUrl, ct).ConfigureAwait(false);
                using var releaseDoc = JsonDocument.Parse(releaseJson);

                string? assetUrl = null;
                if (releaseDoc.RootElement.TryGetProperty("assets", out var assets))
                {
                    foreach (var asset in assets.EnumerateArray())
                    {
                        var browser = asset.TryGetProperty("browser_download_url", out var u)
                            ? u.GetString() : null;
                        if (browser != null && browser.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                        {
                            assetUrl = browser;
                            break;
                        }
                    }
                }

                if (assetUrl == null) return null;
                return await Http.GetStringAsync(assetUrl, ct).ConfigureAwait(false);
            }
            catch { return null; }
        }

        private static List<GpuEntry> ParseGpuJson(string json)
        {
            var list = new List<GpuEntry>();
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var arr = root.ValueKind == JsonValueKind.Array
                    ? root
                    : root.TryGetProperty("devices", out var d) ? d : default;

                if (arr.ValueKind != JsonValueKind.Array) return list;

                foreach (var el in arr.EnumerateArray())
                {
                    var name = el.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    var shaders = el.TryGetProperty("shading_units", out var s)
                        ? (s.ValueKind == JsonValueKind.Number ? s.GetInt32() : 0) : 0;
                    var boost = el.TryGetProperty("boost_clock_mhz", out var b)
                        ? (b.ValueKind == JsonValueKind.Number ? (int)b.GetDouble() : 0) : 0;
                    var baseClock = el.TryGetProperty("base_clock_mhz", out var bc)
                        ? (bc.ValueKind == JsonValueKind.Number ? (int)bc.GetDouble() : 0) : 0;
                    if (!string.IsNullOrEmpty(name))
                        list.Add(new GpuEntry(NormalizeGpuName(name), name, shaders, boost, baseClock));
                }
            }
            catch { }
            return list;
        }

        private static GpuEntry? FindBestGpuMatch(List<GpuEntry> db, string normalized)
            => FindBestMatch(db, normalized, e => e.NormalizedName);

        private static T? FindBestMatch<T>(List<T> db, string normalized, Func<T, string> getNormalizedName) where T : class
        {
            var exact = db.FirstOrDefault(e => getNormalizedName(e) == normalized);
            if (exact != null) return exact;

            var queryTokens = Tokenize(normalized);
            if (queryTokens.Count == 0) return null;

            return db
                .Select(e => (entry: e, score: TokenOverlap(getNormalizedName(e), queryTokens)))
                .Where(x => x.score > 0)
                .OrderByDescending(x => x.score)
                .FirstOrDefault()
                .entry;
        }

        private static int ComputeGpuScore(GpuEntry e)
        {
            int clock = e.BoostClockMhz > 0 ? e.BoostClockMhz : e.BaseClockMhz;
            if (e.ShadingUnits > 0 && clock > 0)
                return (int)((long)e.ShadingUnits * clock / 1000);
            if (clock > 0)
                return clock;
            return 0;
        }
        private async Task<BenchmarkScore> FetchWithCacheAsync(
            string key,
            CancellationToken ct,
            Func<CancellationToken, Task<BenchmarkScore>> fetchFunc)
        {
            await _cacheLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_cache.TryGetValue(key, out var cached) &&
                    DateTime.UtcNow - cached.fetchedAt < _cacheTtl)
                    return cached.score;
            }
            finally { _cacheLock.Release(); }

            var result = await fetchFunc(ct).ConfigureAwait(false);

            await _cacheLock.WaitAsync(ct).ConfigureAwait(false);
            try { _cache[key] = (result, DateTime.UtcNow); }
            finally { _cacheLock.Release(); }

            return result;
        }

        private static HashSet<string> Tokenize(string s)
            => new(s.Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.OrdinalIgnoreCase);

        private static int TokenOverlap(string entryName, HashSet<string> queryTokens)
        {
            var entryTokens = Tokenize(entryName);
            return entryTokens.Count(queryTokens.Contains);
        }

        internal static string NormalizeCpuName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            var s = raw;
            s = Regex.Replace(s, @"\(R\)|\(TM\)|\(C\)|®|™", string.Empty, RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bCPU\s*@\s*[\d.]+\s*GHz\b", string.Empty, RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\bProcessor\b", string.Empty, RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\s{2,}", " ").Trim().ToLowerInvariant();
            return s;
        }

        internal static string NormalizeGpuName(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
            var s = raw;
            s = Regex.Replace(s, @"\(R\)|\(TM\)|\(C\)|®|™", string.Empty, RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\b(nvidia|amd|intel|geforce|radeon)\b", string.Empty, RegexOptions.IgnoreCase);
            s = Regex.Replace(s, @"\s{2,}", " ").Trim().ToLowerInvariant();
            return s;
        }

        private sealed record CpuEntry(string NormalizedName, int Score, int MulticoreScore);
        private sealed record GpuEntry(string NormalizedName, string OriginalName, int ShadingUnits, int BoostClockMhz, int BaseClockMhz);
    }
}
