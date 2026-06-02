using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using SteamKit2;

namespace DepotDL.GUI.Services
{
    public sealed class SteamDepotMeta
    {
        public string Name { get; init; } = string.Empty;
        public string OsList { get; init; } = string.Empty;
        public string OsArch { get; init; } = string.Empty;
    }

    public static class SteamMetadataService
    {
        private static readonly Dictionary<string, Dictionary<string, SteamDepotMeta>> _cache =
            new(StringComparer.OrdinalIgnoreCase);

        public static async Task<Dictionary<string, SteamDepotMeta>> GetDepotMetaAsync(string appId)
        {
            if (_cache.TryGetValue(appId, out var hit)) return hit;

            var fromFile = LoadFromFiles(appId);
            if (fromFile.Count > 0) { _cache[appId] = fromFile; return fromFile; }

            try
            {
                var fetched = await Task.Run(() => FetchFromSteam(appId));
                _cache[appId] = fetched;
                return fetched;
            }
            catch
            {
                var empty = new Dictionary<string, SteamDepotMeta>(StringComparer.OrdinalIgnoreCase);
                _cache[appId] = empty;
                return empty;
            }
        }

        private static Dictionary<string, SteamDepotMeta> LoadFromFiles(string appId)
        {
            foreach (var path in GetCachePaths())
            {
                var result = TryLoad(path, appId);
                if (result.Count > 0) return result;
            }
            return new(StringComparer.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> GetCachePaths()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var root in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            {
                var dir = new DirectoryInfo(root);
                while (dir != null)
                {
                    var p = Path.Combine(dir.FullName, "api_cache.json");
                    if (seen.Add(p)) yield return p;
                    dir = dir.Parent;
                }
            }
        }

        private static Dictionary<string, SteamDepotMeta> TryLoad(string path, string appId)
        {
            var result = new Dictionary<string, SteamDepotMeta>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(path)) return result;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (!doc.RootElement.TryGetProperty($"app_info_{appId}", out var entry) ||
                    !entry.TryGetProperty("data", out var data) ||
                    !data.TryGetProperty("depots", out var depots))
                    return result;

                foreach (var depot in depots.EnumerateObject())
                {
                    if (!ulong.TryParse(depot.Name, out _) || depot.Value.ValueKind != JsonValueKind.Object)
                        continue;
                    var name = Str(depot.Value, "name");
                    var osList = string.Empty;
                    var osArch = string.Empty;
                    if (depot.Value.TryGetProperty("config", out var cfg) && cfg.ValueKind == JsonValueKind.Object)
                    {
                        osList = Str(cfg, "oslist");
                        osArch = Str(cfg, "osarch");
                    }
                    result[depot.Name] = new SteamDepotMeta { Name = name, OsList = osList, OsArch = osArch };
                }
            }
            catch { }
            return result;
        }

        private static string Str(JsonElement el, string key) =>
            el.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() ?? string.Empty : string.Empty;

        private static Dictionary<string, SteamDepotMeta> FetchFromSteam(string appId)
        {
            if (!uint.TryParse(appId, out var appIdUInt))
                return new(StringComparer.OrdinalIgnoreCase);

            var result = new Dictionary<string, SteamDepotMeta>(StringComparer.OrdinalIgnoreCase);
            var client = new SteamClient();
            var mgr = new CallbackManager(client);
            var user = client.GetHandler<SteamUser>()!;
            var apps = client.GetHandler<SteamApps>()!;
            var done = false;
            Exception? err = null;

            mgr.Subscribe<SteamClient.ConnectedCallback>(_ => user.LogOnAnonymous());
            mgr.Subscribe<SteamClient.DisconnectedCallback>(_ => done = true);
            mgr.Subscribe<SteamUser.LoggedOnCallback>(async cb =>
            {
                if (cb.Result != EResult.OK) { done = true; return; }
                try
                {
                    var req = new SteamApps.PICSRequest(appIdUInt);
                    var job = apps.PICSGetProductInfo(new[] { req }, Array.Empty<SteamApps.PICSRequest>());
                    job.Timeout = TimeSpan.FromSeconds(12);
                    var res = await job;
                    if (res.Complete && res.Results != null)
                        foreach (var r in res.Results)
                            foreach (var app in r.Apps)
                                ReadDepots(app.Value.KeyValues, result);
                }
                catch (Exception ex) { err = ex; }
                finally { done = true; }
            });

            client.Connect();
            var start = DateTime.UtcNow;
            while (!done && DateTime.UtcNow - start < TimeSpan.FromSeconds(15))
                mgr.RunWaitCallbacks(TimeSpan.FromMilliseconds(100));

            try { user.LogOff(); } catch { }
            try { client.Disconnect(); } catch { }

            if (err != null) throw err;
            return result;
        }

        private static void ReadDepots(KeyValue kv, Dictionary<string, SteamDepotMeta> result)
        {
            var depots = kv["depots"];
            if (depots == KeyValue.Invalid) return;
            foreach (var depot in depots.Children)
            {
                if (!ulong.TryParse(depot.Name, out _)) continue;
                var config = depot["config"];
                result[depot.Name] = new SteamDepotMeta
                {
                    Name = depot["name"].AsString() ?? string.Empty,
                    OsList = config == KeyValue.Invalid ? string.Empty : config["oslist"].AsString() ?? string.Empty,
                    OsArch = config == KeyValue.Invalid ? string.Empty : config["osarch"].AsString() ?? string.Empty,
                };
            }
        }
    }
}
