// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System.Globalization;
using DepotDL.CLI.Tui;

namespace DepotDL.CLI
{
    public static class IniSettings
    {
        private static readonly string IniPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DepotDL.CLI",
            "DepotDL.CLI.ini");

        public static void LoadInto(TuiSession session)
        {
            var values = Load();

            var manifestsDir = Get(values, "paths.manifests_dir");
            if (!string.IsNullOrWhiteSpace(manifestsDir))
            {
                session.ManifestsDir = manifestsDir;
                session.ManifestsDirConfigured = true;
            }

            session.DownloadBaseDir = Get(values, "paths.download_base_dir") ?? session.DownloadBaseDir;
            session.DownloadBaseDir = Get(values, "session.download_base_dir") ?? session.DownloadBaseDir;
            session.RyuuApiKey = Get(values, "ryuu.api_key") ?? session.RyuuApiKey;
            session.HubcapApiKey = Get(values, "hubcap.api_key") ?? session.HubcapApiKey;
            session.SteamWebApiKey = Get(values, "steam.web_api_key") ?? session.SteamWebApiKey;

            if (int.TryParse(Get(values, "settings.max_parallel_depots"), out var maxParallel))
            {
                session.MaxParallelDepots = Math.Clamp(maxParallel, 1, 8);
            }

            if (bool.TryParse(Get(values, "settings.download_achievement_icons"), out var downloadAch))
            {
                session.DownloadAchievementIcons = downloadAch;
            }
            else
            {
                session.DownloadAchievementIcons = true;
            }

            if (DateTime.TryParse(Get(values, "settings.last_update_check"), CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var lastCheck))
                session.LastUpdateCheckUtc = lastCheck;

            session.LastKnownReleaseTag = Get(values, "settings.last_known_release_tag");
            session.DismissedUpdateTag = Get(values, "settings.dismissed_update_tag");
            var savedChannel = Get(values, "settings.update_channel");
            if (!string.IsNullOrEmpty(savedChannel))
                session.UpdateChannel = savedChannel;
        }

        public static void Save(TuiSession session)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(IniPath)!);
            using var writer = new StreamWriter(IniPath, false);
            writer.WriteLine("[paths]");
            WriteValue(writer, "manifests_dir", session.ManifestsDirConfigured ? session.ManifestsDir : null);
            WriteValue(writer, "download_base_dir", session.DownloadBaseDir);
            writer.WriteLine();
            writer.WriteLine("[ryuu]");
            WriteValue(writer, "api_key", session.RyuuApiKey);
            writer.WriteLine();
            writer.WriteLine("[hubcap]");
            WriteValue(writer, "api_key", session.HubcapApiKey);
            writer.WriteLine();
            writer.WriteLine("[steam]");
            WriteValue(writer, "web_api_key", session.SteamWebApiKey);
            writer.WriteLine();
            writer.WriteLine("[settings]");
            WriteValue(writer, "max_parallel_depots", session.MaxParallelDepots.ToString());
            WriteValue(writer, "download_achievement_icons", session.DownloadAchievementIcons.ToString().ToLowerInvariant());
            WriteValue(writer, "last_update_check", session.LastUpdateCheckUtc?.ToString("O") ?? string.Empty);
            WriteValue(writer, "last_known_release_tag", session.LastKnownReleaseTag ?? string.Empty);
            WriteValue(writer, "dismissed_update_tag", session.DismissedUpdateTag ?? string.Empty);
            WriteValue(writer, "update_channel", session.UpdateChannel);
        }

        private static Dictionary<string, string> Load()
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(IniPath))
            {
                return values;
            }

            var section = "";
            foreach (var rawLine in File.ReadAllLines(IniPath))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith(";", StringComparison.Ordinal) || line.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                if (line.StartsWith("[", StringComparison.Ordinal) && line.EndsWith("]", StringComparison.Ordinal))
                {
                    section = line[1..^1].Trim();
                    continue;
                }

                var equalsIndex = line.IndexOf('=');
                if (equalsIndex <= 0)
                {
                    continue;
                }

                var key = line[..equalsIndex].Trim();
                var value = Unescape(line[(equalsIndex + 1)..].Trim());
                values[$"{section}.{key}"] = value;
            }

            return values;
        }

        private static string? Get(Dictionary<string, string> values, string key)
        {
            return values.TryGetValue(key, out var value) && value.Length > 0 ? value : null;
        }

        private static void WriteValue(StreamWriter writer, string key, string? value)
        {
            writer.WriteLine($"{key}={Escape(value ?? string.Empty)}");
        }

        private static string Escape(string value)
        {
            return value.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n");
        }

        private static string Unescape(string value)
        {
            var result = new System.Text.StringBuilder();
            for (var i = 0; i < value.Length; i++)
            {
                if (value[i] == '\\' && i + 1 < value.Length)
                {
                    var next = value[++i];
                    result.Append(next switch
                    {
                        'r' => '\r',
                        'n' => '\n',
                        '\\' => '\\',
                        _ => next
                    });
                    continue;
                }

                result.Append(value[i]);
            }

            return result.ToString();
        }
    }
}
