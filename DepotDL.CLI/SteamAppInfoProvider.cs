using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using SteamKit2;

namespace DepotDL.CLI
{
    public sealed class DepotMetadata
    {
        public string Name { get; init; } = string.Empty;
        public string OsList { get; init; } = string.Empty;
        public string OsArch { get; init; } = string.Empty;
    }

    public static class SteamAppInfoProvider
    {
        private static readonly Dictionary<string, Dictionary<string, DepotMetadata>> MetadataCache = new(StringComparer.OrdinalIgnoreCase);

        public static Dictionary<string, DepotMetadata> LoadDepotMetadata(string appId)
        {
            if (MetadataCache.TryGetValue(appId, out var known))
            {
                return known;
            }

            var cached = LoadFromCache(appId);
            if (cached.Count > 0)
            {
                MetadataCache[appId] = cached;
                return cached;
            }

            try
            {
                var fetched = FetchFromSteam(appId);
                MetadataCache[appId] = fetched;
                return fetched;
            }
            catch
            {
                var empty = new Dictionary<string, DepotMetadata>(StringComparer.OrdinalIgnoreCase);
                MetadataCache[appId] = empty;
                return empty;
            }
        }

        private static Dictionary<string, DepotMetadata> LoadFromCache(string appId)
        {
            foreach (var path in GetCacheCandidates())
            {
                var metadata = LoadFromCacheFile(path, appId);
                if (metadata.Count > 0)
                {
                    return metadata;
                }
            }

            return new Dictionary<string, DepotMetadata>(StringComparer.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> GetCacheCandidates()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
            {
                var dir = new DirectoryInfo(start);
                while (dir != null)
                {
                    var path = Path.Combine(dir.FullName, "api_cache.json");
                    if (seen.Add(path))
                    {
                        yield return path;
                    }
                    dir = dir.Parent;
                }
            }
        }

        private static Dictionary<string, DepotMetadata> LoadFromCacheFile(string path, string appId)
        {
            var result = new Dictionary<string, DepotMetadata>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(path))
            {
                return result;
            }

            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (!doc.RootElement.TryGetProperty($"app_info_{appId}", out var entry) ||
                    !entry.TryGetProperty("data", out var data) ||
                    !data.TryGetProperty("depots", out var depots))
                {
                    return result;
                }

                foreach (var depot in depots.EnumerateObject())
                {
                    if (!ulong.TryParse(depot.Name, out _) || depot.Value.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    var name = ReadString(depot.Value, "name");
                    var osList = string.Empty;
                    var osArch = string.Empty;
                    if (depot.Value.TryGetProperty("config", out var config) && config.ValueKind == JsonValueKind.Object)
                    {
                        osList = ReadString(config, "oslist");
                        osArch = ReadString(config, "osarch");
                    }

                    result[depot.Name] = new DepotMetadata
                    {
                        Name = name,
                        OsList = osList,
                        OsArch = osArch
                    };
                }
            }
            catch
            {
            }

            return result;
        }

        private static string ReadString(JsonElement element, string name)
        {
            return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? string.Empty
                : string.Empty;
        }

        private static Dictionary<string, DepotMetadata> FetchFromSteam(string appId)
        {
            if (!uint.TryParse(appId, out var appIdUInt))
            {
                return new Dictionary<string, DepotMetadata>(StringComparer.OrdinalIgnoreCase);
            }

            var steamClient = new SteamClient();
            var manager = new CallbackManager(steamClient);
            var steamUser = steamClient.GetHandler<SteamUser>()!;
            var steamApps = steamClient.GetHandler<SteamApps>()!;
            var connected = false;
            var loggedOn = false;
            var done = false;
            var result = new Dictionary<string, DepotMetadata>(StringComparer.OrdinalIgnoreCase);
            Exception? error = null;

            manager.Subscribe<SteamClient.ConnectedCallback>(_ =>
            {
                connected = true;
                steamUser.LogOnAnonymous();
            });

            manager.Subscribe<SteamClient.DisconnectedCallback>(_ =>
            {
                done = true;
            });

            manager.Subscribe<SteamUser.LoggedOnCallback>(async callback =>
            {
                if (callback.Result != EResult.OK)
                {
                    error = new InvalidOperationException($"Steam anonymous login failed: {callback.Result}");
                    done = true;
                    return;
                }

                loggedOn = true;
                try
                {
                    var request = new SteamApps.PICSRequest(appIdUInt);
                    var job = steamApps.PICSGetProductInfo(new[] { request }, Array.Empty<SteamApps.PICSRequest>());
                    job.Timeout = TimeSpan.FromSeconds(12);
                    var resultSet = await job;
                    if (resultSet.Complete && resultSet.Results != null)
                    {
                        foreach (var callbackResult in resultSet.Results)
                        {
                            foreach (var app in callbackResult.Apps)
                            {
                                ReadDepots(app.Value.KeyValues, result);
                            }
                        }
                    }
                    done = true;
                }
                catch (Exception ex)
                {
                    error = ex;
                    done = true;
                }
            });

            steamClient.Connect();
            var start = DateTime.UtcNow;
            while (!done && DateTime.UtcNow - start < TimeSpan.FromSeconds(15))
            {
                manager.RunWaitCallbacks(TimeSpan.FromMilliseconds(100));
            }

            if (connected && loggedOn)
            {
                steamUser.LogOff();
            }
            steamClient.Disconnect();

            if (error != null)
            {
                throw error;
            }

            return result;
        }

        private static void ReadDepots(KeyValue appInfo, Dictionary<string, DepotMetadata> result)
        {
            var depots = appInfo["depots"];
            if (depots == KeyValue.Invalid)
            {
                return;
            }

            foreach (var depot in depots.Children)
            {
                if (!ulong.TryParse(depot.Name, out _))
                {
                    continue;
                }

                var config = depot["config"];
                result[depot.Name] = new DepotMetadata
                {
                    Name = depot["name"].AsString() ?? string.Empty,
                    OsList = config == KeyValue.Invalid ? string.Empty : config["oslist"].AsString() ?? string.Empty,
                    OsArch = config == KeyValue.Invalid ? string.Empty : config["osarch"].AsString() ?? string.Empty
                };
            }
        }
    }
}
