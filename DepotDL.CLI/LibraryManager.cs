using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DepotDL.CLI
{
    public class LibraryGame
    {
        public string GameName { get; set; } = string.Empty;
        public string AppId { get; set; } = string.Empty;
        public string LuaPath { get; set; } = string.Empty;
        public string OutputDir { get; set; } = string.Empty;
        public List<string> DepotIds { get; set; } = new();
        public DateTime InstallDate { get; set; }
        public long TotalSizeBytes { get; set; }
        public bool IsVerified { get; set; } = true;
    }

    public static class LibraryManager
    {
        private static readonly string LibraryFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "library.json");

        public static List<LibraryGame> LoadLibrary()
        {
            try
            {
                if (!File.Exists(LibraryFilePath)) return new List<LibraryGame>();
                string json = File.ReadAllText(LibraryFilePath);
                return JsonSerializer.Deserialize<List<LibraryGame>>(json) ?? new List<LibraryGame>();
            }
            catch
            {
                return new List<LibraryGame>();
            }
        }

        public static void SaveLibrary(List<LibraryGame> games)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(games, options);
                File.WriteAllText(LibraryFilePath, json);
            }
            catch { }
        }

        public static void AddOrUpdateGame(LibraryGame game)
        {
            var library = LoadLibrary();
            int idx = library.FindIndex(g => g.AppId == game.AppId);
            if (idx >= 0)
            {
                library[idx] = game;
            }
            else
            {
                library.Add(game);
            }
            SaveLibrary(library);
        }

        public static void RemoveGame(string appId)
        {
            var library = LoadLibrary();
            library.RemoveAll(g => g.AppId == appId);
            SaveLibrary(library);
        }

        public static int VerifyLibraryOnStartup(out int totalCount, out int missingCount)
        {
            var library = LoadLibrary();
            totalCount = library.Count;
            missingCount = 0;

            bool changed = false;
            foreach (var game in library)
            {
                bool exists = Directory.Exists(game.OutputDir);
                if (game.IsVerified != exists)
                {
                    game.IsVerified = exists;
                    changed = true;
                }
                if (!exists)
                {
                    missingCount++;
                }
            }

            if (changed)
            {
                SaveLibrary(library);
            }

            return totalCount - missingCount;
        }

        public static long GetDirectorySize(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return 0;
            long size = 0;
            try
            {
                var queue = new Queue<string>();
                queue.Enqueue(path);

                while (queue.Count > 0)
                {
                    string currentDir = queue.Dequeue();
                    try
                    {
                        foreach (string file in Directory.GetFiles(currentDir))
                        {
                            try
                            {
                                size += new FileInfo(file).Length;
                            }
                            catch { }
                        }

                        foreach (string subDir in Directory.GetDirectories(currentDir))
                        {
                            queue.Enqueue(subDir);
                        }
                    }
                    catch { }
                }
            }
            catch { }
            return size;
        }

        public static bool RobustDeleteDirectory(string path)
        {
            if (!Directory.Exists(path)) return true;

            for (int i = 0; i < 10; i++)
            {
                try
                {
                    ClearReadOnlyAttributes(new DirectoryInfo(path));
                    Directory.Delete(path, recursive: true);
                    return true; // Success!
                }
                catch
                {
                    System.Threading.Thread.Sleep(150);
                }
            }
            return false;
        }

        public static Dictionary<string, DepotInfo> ParseLuaConfig(string luaContent, out string appId, string? luaPath = null)
        {
            appId = string.Empty;
            var depots = new Dictionary<string, DepotInfo>();

            var appIdRegex = new Regex(@"^\s*addappid\s*\(\s*(\d+)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            var appIdMatch = appIdRegex.Match(luaContent);
            if (appIdMatch.Success)
            {
                appId = appIdMatch.Groups[1].Value;
            }

            if (string.IsNullOrEmpty(appId)) return depots;

            var keyRegex = new Regex(@"^\s*addappid\s*\(\s*(\d+)\s*,\s*(\d+)\s*,\s*(?:""|')(\S+)(?:""|')\s*\)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            var keyMatches = keyRegex.Matches(luaContent);
            foreach (Match match in keyMatches)
            {
                var depotId = match.Groups[1].Value;
                var key = match.Groups[3].Value;
                if (!depots.TryGetValue(depotId, out var depot))
                {
                    depot = new DepotInfo { DepotId = depotId };
                    depots[depotId] = depot;
                }
                depot.DecryptionKey = key;
            }

            var manifestRegex = new Regex(@"^\s*setManifestid\s*\(\s*(\d+)\s*,\s*[""'](\d+)[""']\s*\)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
            var manifestMatches = manifestRegex.Matches(luaContent);
            foreach (Match match in manifestMatches)
            {
                var depotId = match.Groups[1].Value;
                var manifestId = match.Groups[2].Value;
                if (!depots.TryGetValue(depotId, out var depot))
                {
                    depot = new DepotInfo { DepotId = depotId };
                    depots[depotId] = depot;
                }
                depot.ManifestId = manifestId;
            }

            if (!string.IsNullOrWhiteSpace(luaPath))
            {
                var metadata = SteamAppInfoProvider.LoadDepotMetadata(appId);
                foreach (var depot in depots.Values)
                {
                    if (!metadata.TryGetValue(depot.DepotId, out var meta))
                    {
                        continue;
                    }
                    depot.Name = meta.Name;
                    depot.OsList = meta.OsList;
                    depot.OsArch = meta.OsArch;
                }
            }

            return depots;
        }

        public static bool IsDownloadableDepot(DepotInfo depot, string? appId = null)
        {
            return !string.IsNullOrWhiteSpace(depot.DepotId) &&
                !string.Equals(depot.DepotId, appId, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(depot.DecryptionKey);
        }

        public static Dictionary<string, DepotInfo> FilterDownloadableDepots(Dictionary<string, DepotInfo> depots, string? appId = null)
        {
            var result = new Dictionary<string, DepotInfo>();
            foreach (var depot in depots.Values)
            {
                if (IsDownloadableDepot(depot, appId))
                {
                    result[depot.DepotId] = depot;
                }
            }
            return result;
        }

        public static List<DepotInfo> FilterDownloadableDepots(List<DepotInfo> depots, string? appId = null)
        {
            var result = new List<DepotInfo>();
            foreach (var depot in depots)
            {
                if (IsDownloadableDepot(depot, appId))
                {
                    result.Add(depot);
                }
            }
            return result;
        }

        private static void ClearReadOnlyAttributes(DirectoryInfo directory)
        {
            if (!directory.Exists) return;

            foreach (var file in directory.GetFiles())
            {
                try
                {
                    if (file.IsReadOnly)
                    {
                        file.IsReadOnly = false;
                    }
                }
                catch { }
            }

            foreach (var subdir in directory.GetDirectories())
            {
                ClearReadOnlyAttributes(subdir);
            }
        }
    }
}
