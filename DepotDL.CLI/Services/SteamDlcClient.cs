// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace DepotDL.CLI.Services
{
    internal static class SteamDlcClient
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
        private static readonly JsonSerializerOptions Opts = new() { PropertyNameCaseInsensitive = true };

        private static Task<HashSet<string>>? _fetchTask;
        private static readonly object _taskLock = new();

        public static void Prefetch()
        {
            if (Volatile.Read(ref _fetchTask) != null) return;
            lock (_taskLock)
            {
                if (_fetchTask != null) return;
                _fetchTask = Task.Run(FetchAsync);
            }
        }

        private static async Task<HashSet<string>> FetchAsync()
        {
            var json = await Http.GetStringAsync("https://api.bonker.dev/api/applist?type=dlc");
            var resp = JsonSerializer.Deserialize<BonkerResponse>(json, Opts);
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (resp?.Apps != null)
                foreach (var app in resp.Apps)
                    if (app.AppId > 0)
                        set.Add(app.AppId.ToString());
            return set;
        }

        public static bool IsDlc(string id)
        {
            var t = Volatile.Read(ref _fetchTask);
            return t is { IsCompletedSuccessfully: true } && t.Result.Contains(id);
        }

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
