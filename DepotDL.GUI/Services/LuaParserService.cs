using System.Collections.Generic;
using System.IO;
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
            @"^\s*setManifestid\s*\(\s*(\d+)\s*,\s*[""'](\d+)[""']\s*\)",
            RegexOptions.Multiline | RegexOptions.IgnoreCase);

        public (string AppId, List<DepotInfo> Depots) Parse(string luaPath)
        {
            string content = File.ReadAllText(luaPath);
            return ParseContent(content);
        }

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
