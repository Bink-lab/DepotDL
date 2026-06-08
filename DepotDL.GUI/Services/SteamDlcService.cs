// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DepotDL.GUI.Services
{
    public static class SteamDlcService
    {
        private static readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(15) };
        private static readonly SemaphoreSlim _lock = new(1, 1);
        private static HashSet<string>? _dlcIds;

        public static async Task<HashSet<string>> GetDlcIdsAsync()
        {
            var cached = Volatile.Read(ref _dlcIds);
            if (cached != null) return cached;
            await _lock.WaitAsync();
            try
            {
                if (_dlcIds != null) return _dlcIds;
                var json = await _http.GetStringAsync("https://api.bonker.dev/api/applist?type=dlc");
                var resp = JsonSerializer.Deserialize<BonkerResponse>(json, _opts);
                var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (resp?.Apps != null)
                    foreach (var app in resp.Apps)
                        if (app.AppId > 0)
                            set.Add(app.AppId.ToString());
                _dlcIds = set;
                return set;
            }
            catch
            {
                return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            }
            finally
            {
                _lock.Release();
            }
        }

        public static bool IsDlc(string id) =>
            _dlcIds != null && _dlcIds.Contains(id);

        private static readonly JsonSerializerOptions _opts = new() { PropertyNameCaseInsensitive = true };

        private class BonkerResponse
        {
            [JsonPropertyName("apps")] public List<BonkerApp> Apps { get; set; } = new();
        }

        private class BonkerApp
        {
            [JsonPropertyName("appid")] public int AppId { get; set; }
        }
    }
}
