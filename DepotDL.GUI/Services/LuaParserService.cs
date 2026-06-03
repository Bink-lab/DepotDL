using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using DepotDL.GUI.Models;

namespace DepotDL.GUI.Services
{
    public class LuaParserService
    {
        private static readonly Regex AppIdRx = new(
            @"^\s*addappid\s*\(\s*(\d+)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
        private static readonly Regex KeyRx = new(
            @"^\s*addappid\s*\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(?:""|')(\S+)(?:""|')\s*\)",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);
        private static readonly Regex ManifestRx = new(
            @"^\s*setManifestid\s*\(\s*(\d+)\s*,\s*[""'](\d+)[""'](?:[^)]*)\)",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);

        public (string AppId, List<DepotInfo> Depots) Parse(string luaPath)
        {
            string content = File.ReadAllText(luaPath);
            var result = ParseContent(content);
            if (!string.IsNullOrEmpty(result.AppId))
                EnrichWithOsMetadata(result.AppId, result.Depots);
            return result;
        }

        private static void EnrichWithOsMetadata(string appId, List<DepotInfo> depots)
        {
            var metadata = LoadOsMetadata(appId);
            if (metadata.Count == 0) return;
            foreach (var depot in depots)
            {
                if (!metadata.TryGetValue(depot.DepotId, out var meta)) continue;
                if (string.IsNullOrWhiteSpace(depot.Name)) depot.Name = meta.name;
                depot.OsList = meta.osList;
                depot.OsArch = meta.osArch;
            }
        }

        private static Dictionary<string, (string name, string osList, string osArch)> LoadOsMetadata(string appId)
        {
            var result = new Dictionary<string, (string, string, string)>(StringComparer.OrdinalIgnoreCase);
            var path = Path.Combine(AppContext.BaseDirectory, "api_cache.json");
            if (File.Exists(path))
                LoadFromCacheFile(path, appId, result);
            return result;
        }

        private static void LoadFromCacheFile(string path, string appId, Dictionary<string, (string, string, string)> result)
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (!doc.RootElement.TryGetProperty($"app_info_{appId}", out var entry) ||
                    !entry.TryGetProperty("data", out var data) ||
                    !data.TryGetProperty("depots", out var depots))
                    return;
                foreach (var depot in depots.EnumerateObject())
                {
                    if (!ulong.TryParse(depot.Name, out _) || depot.Value.ValueKind != JsonValueKind.Object) continue;
                    var name = ReadStr(depot.Value, "name");
                    var osList = string.Empty;
                    var osArch = string.Empty;
                    if (depot.Value.TryGetProperty("config", out var config) && config.ValueKind == JsonValueKind.Object)
                    {
                        osList = ReadStr(config, "oslist");
                        osArch = ReadStr(config, "osarch");
                    }
                    result[depot.Name] = (name, osList, osArch);
                }
            }
            catch (Exception ex) { Debug.WriteLine($"[LuaParserService] api_cache.json parse failed: {ex.Message}"); }
        }

        private static string ReadStr(JsonElement el, string name) =>
            el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
                ? v.GetString() ?? string.Empty : string.Empty;

        public (string AppId, List<DepotInfo> Depots) ParseContent(string content)
        {
            string appId = string.Empty;
            var map = new Dictionary<string, DepotInfo>();

            var m = AppIdRx.Match(content);
            if (m.Success) appId = m.Groups[1].Value;

            if (string.IsNullOrEmpty(appId))
                return (appId, new List<DepotInfo>());

            foreach (Match km in KeyRx.Matches(content))
            {
                string id = km.Groups[1].Value;
                if (!map.TryGetValue(id, out var d)) { d = new DepotInfo { DepotId = id }; map[id] = d; }
                d.DecryptionKey = km.Groups[3].Value;
            }

            foreach (Match mm in ManifestRx.Matches(content))
            {
                string id = mm.Groups[1].Value;
                if (!map.TryGetValue(id, out var d)) { d = new DepotInfo { DepotId = id }; map[id] = d; }
                d.ManifestId = mm.Groups[2].Value;
            }

            var result = new List<DepotInfo>();
            foreach (var depot in map.Values)
            {
                if (!string.IsNullOrWhiteSpace(depot.DecryptionKey) &&
                    depot.DepotId != appId)
                {
                    result.Add(depot);
                }
            }

            return (appId, result);
        }
    }
}
