using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using SteamKit2;

namespace DepotDL.CLI.Services
{
    public sealed class DepotMetadata
    {
        public string Name { get; init; } = string.Empty;
        public string OsList { get; init; } = string.Empty;
        public string OsArch { get; init; } = string.Empty;
    }

    public static class SteamAppInfoProvider
    {
        private static readonly ConcurrentDictionary<string, Dictionary<string, DepotMetadata>> MetadataCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, string> AppNameCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, string> BuildIdCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> ManifestToBuildIdCache = new(StringComparer.OrdinalIgnoreCase);

        public static string GetAppName(string appId)
        {
            if (AppNameCache.TryGetValue(appId, out var name)) return name;
            return string.Empty;
        }

        public static string GetBuildId(string appId, IEnumerable<string>? manifestIds = null)
        {
            if (manifestIds != null && ManifestToBuildIdCache.TryGetValue(appId, out var map))
            {
                foreach (var gid in manifestIds)
                {
                    if (!string.IsNullOrEmpty(gid) && map.TryGetValue(gid, out var matchedBid))
                    {
                        return matchedBid;
                    }
                }
            }

            if (BuildIdCache.TryGetValue(appId, out var bid)) return bid;
            return string.Empty;
        }

        private static string ReadBuildIdJson(JsonElement element)
        {
            if (element.TryGetProperty("buildid", out var v))
            {
                return v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : v.GetRawText();
            }
            return string.Empty;
        }

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
                using var stream = File.OpenRead(path);
                using var doc = JsonDocument.Parse(stream);
                if (!doc.RootElement.TryGetProperty($"app_info_{appId}", out var entry) ||
                    !entry.TryGetProperty("data", out var data) ||
                    !data.TryGetProperty("depots", out var depots))
                {
                    return result;
                }

                if (data.TryGetProperty("common", out var common) &&
                    common.TryGetProperty("name", out var nameVal) &&
                    nameVal.ValueKind == JsonValueKind.String)
                {
                    AppNameCache[appId] = nameVal.GetString() ?? string.Empty;
                }

                if (depots.TryGetProperty("branches", out var branches) && branches.ValueKind == JsonValueKind.Object)
                {
                    if (branches.TryGetProperty("public", out var publicBranch) && publicBranch.ValueKind == JsonValueKind.Object)
                    {
                        BuildIdCache[appId] = ReadBuildIdJson(publicBranch);
                    }
                    else
                    {
                        var firstBranch = branches.EnumerateObject().FirstOrDefault();
                        if (firstBranch.Value.ValueKind == JsonValueKind.Object)
                        {
                            BuildIdCache[appId] = ReadBuildIdJson(firstBranch.Value);
                        }
                    }
                }

                ExtractManifestBuildIdsJson(appId, depots);

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
            var result = new Dictionary<string, DepotMetadata>(StringComparer.OrdinalIgnoreCase);

            var tcs = new TaskCompletionSource<Dictionary<string, DepotMetadata>>(TaskCreationOptions.RunContinuationsAsynchronously);

            manager.Subscribe<SteamClient.ConnectedCallback>(_ =>
            {
                connected = true;
                steamUser.LogOnAnonymous();
            });

            manager.Subscribe<SteamClient.DisconnectedCallback>(_ =>
            {
                tcs.TrySetException(new Exception("Disconnected from Steam."));
            });

            manager.Subscribe<SteamUser.LoggedOnCallback>(async callback =>
            {
                if (callback.Result != EResult.OK)
                {
                    tcs.TrySetException(new InvalidOperationException($"Steam anonymous login failed: {callback.Result}"));
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
                                var appInfo = app.Value.KeyValues;
                                var appName = appInfo["common"]["name"].AsString();
                                if (!string.IsNullOrEmpty(appName))
                                {
                                    AppNameCache[appId] = appName;
                                }
                                ReadDepots(appInfo, result);

                                var depotsNode = appInfo["depots"];
                                if (depotsNode != KeyValue.Invalid)
                                {
                                    var branchesNode = depotsNode["branches"];
                                    if (branchesNode != KeyValue.Invalid)
                                    {
                                        var publicBranch = branchesNode["public"];
                                        if (publicBranch != KeyValue.Invalid && publicBranch["buildid"] != KeyValue.Invalid)
                                        {
                                            BuildIdCache[appId] = publicBranch["buildid"].Value ?? string.Empty;
                                        }
                                        else
                                        {
                                            var firstBranch = branchesNode.Children.FirstOrDefault();
                                            if (firstBranch != null && firstBranch["buildid"] != KeyValue.Invalid)
                                            {
                                                BuildIdCache[appId] = firstBranch["buildid"].Value ?? string.Empty;
                                            }
                                        }
                                    }

                                    ExtractManifestBuildIdsKeyValue(appId, depotsNode);
                                }
                            }
                        }
                    }
                    tcs.TrySetResult(result);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });

            steamClient.Connect();
            var start = DateTime.UtcNow;
            while (!tcs.Task.IsCompleted && DateTime.UtcNow - start < TimeSpan.FromSeconds(15))
            {
                manager.RunWaitCallbacks(TimeSpan.FromMilliseconds(100));
            }

            if (connected && loggedOn)
            {
                try { steamUser.LogOff(); } catch { }
            }
            try { steamClient.Disconnect(); } catch { }

            if (tcs.Task.IsFaulted)
            {
                throw tcs.Task.Exception?.GetBaseException() ?? new Exception("Steam fetch failed.");
            }
            if (!tcs.Task.IsCompleted)
            {
                throw new TimeoutException("Steam PICS request timed out.");
            }

            return tcs.Task.Result;
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

        private static void ExtractManifestBuildIdsJson(string appId, JsonElement depots)
        {
            if (depots.ValueKind != JsonValueKind.Object) return;

            var map = ManifestToBuildIdCache.GetOrAdd(appId, _ => new(StringComparer.OrdinalIgnoreCase));

            var branchBuildIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (depots.TryGetProperty("branches", out var branches) && branches.ValueKind == JsonValueKind.Object)
            {
                foreach (var branch in branches.EnumerateObject())
                {
                    if (branch.Value.ValueKind == JsonValueKind.Object && branch.Value.TryGetProperty("buildid", out var buildIdVal))
                    {
                        var bid = buildIdVal.ValueKind == JsonValueKind.String ? buildIdVal.GetString() ?? string.Empty : buildIdVal.GetRawText();
                        if (!string.IsNullOrEmpty(bid))
                        {
                            branchBuildIds[branch.Name] = bid;
                        }
                    }
                }
            }

            foreach (var prop in depots.EnumerateObject())
            {
                if (!ulong.TryParse(prop.Name, out _) || prop.Value.ValueKind != JsonValueKind.Object)
                    continue;

                if (prop.Value.TryGetProperty("manifests", out var manifests) && manifests.ValueKind == JsonValueKind.Object)
                {
                    foreach (var branchManifest in manifests.EnumerateObject())
                    {
                        if (branchManifest.Value.ValueKind == JsonValueKind.Object &&
                            branchManifest.Value.TryGetProperty("gid", out var gidVal))
                        {
                            var gid = gidVal.ValueKind == JsonValueKind.String ? gidVal.GetString() ?? string.Empty : gidVal.GetRawText();
                            if (!string.IsNullOrEmpty(gid) && branchBuildIds.TryGetValue(branchManifest.Name, out var bid))
                            {
                                map[gid] = bid;
                            }
                        }
                    }
                }
            }
        }

        private static void ExtractManifestBuildIdsKeyValue(string appId, KeyValue depotsNode)
        {
            if (depotsNode == KeyValue.Invalid) return;

            var map = ManifestToBuildIdCache.GetOrAdd(appId, _ => new(StringComparer.OrdinalIgnoreCase));

            var branchBuildIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var branchesNode = depotsNode["branches"];
            if (branchesNode != KeyValue.Invalid)
            {
                foreach (var branch in branchesNode.Children)
                {
                    var buildIdVal = branch["buildid"];
                    if (buildIdVal != KeyValue.Invalid && !string.IsNullOrEmpty(buildIdVal.Value))
                    {
                        branchBuildIds[branch.Name ?? string.Empty] = buildIdVal.Value;
                    }
                }
            }

            foreach (var depot in depotsNode.Children)
            {
                if (!ulong.TryParse(depot.Name, out _)) continue;

                var manifestsNode = depot["manifests"];
                if (manifestsNode != KeyValue.Invalid)
                {
                    foreach (var branchManifest in manifestsNode.Children)
                    {
                        var gidVal = branchManifest["gid"];
                        if (gidVal != KeyValue.Invalid && !string.IsNullOrEmpty(gidVal.Value))
                        {
                            if (branchBuildIds.TryGetValue(branchManifest.Name ?? string.Empty, out var bid))
                            {
                                map[gidVal.Value] = bid;
                            }
                        }
                    }
                }
            }
        }
    }
}
