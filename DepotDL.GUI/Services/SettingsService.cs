using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using DepotDL.GUI.Models;

namespace DepotDL.GUI.Services
{
    public class SettingsService
    {
        private static readonly string IniPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DepotDL.CLI", "DepotDL.CLI.ini");

        public AppSettings Load()
        {
            var s = new AppSettings();
            if (!File.Exists(IniPath)) return s;

            var values = ParseIni(IniPath);
            s.ManifestsDir = Get(values, "paths.manifests_dir");
            s.DownloadBaseDir = Get(values, "paths.download_base_dir")
                ?? Get(values, "session.download_base_dir");
            s.RyuuApiKey = Get(values, "ryuu.api_key");
            s.HubcapApiKey = Get(values, "hubcap.api_key");
            s.SteamWebApiKey = Get(values, "steam.web_api_key");
            if (int.TryParse(Get(values, "settings.max_parallel_depots"), out int mp))
                s.MaxParallelDepots = Math.Clamp(mp, 1, 8);

            if (bool.TryParse(Get(values, "settings.download_achievement_icons"), out bool dai))
                s.DownloadAchievementIcons = dai;
            else
                s.DownloadAchievementIcons = true;

            if (int.TryParse(Get(values, "settings.store_cache_hours"), out int sch))
                s.StoreCacheHours = Math.Clamp(sch, 1, 168);
            else s.StoreCacheHours = 24;

            if (int.TryParse(Get(values, "settings.gpu_cache_days"), out int gcd))
                s.GpuCacheDays = Math.Clamp(gcd, 1, 30);
            else s.GpuCacheDays = 7;

            if (int.TryParse(Get(values, "settings.store_page_size"), out int sps))
                s.StorePageSize = Math.Clamp(sps, 12, 120);
            else s.StorePageSize = 48;

            if (int.TryParse(Get(values, "settings.search_debounce_ms"), out int sd))
                s.SearchDebounceMs = Math.Clamp(sd, 0, 2000);
            else s.SearchDebounceMs = 250;

            if (double.TryParse(Get(values, "settings.scroll_sensitivity"), out double ss))
                s.ScrollSensitivity = Math.Clamp(ss, 0.1, 10.0);
            else s.ScrollSensitivity = 1.5;

            if (int.TryParse(Get(values, "settings.scroll_duration_ms"), out int sdms))
                s.ScrollDurationMs = Math.Clamp(sdms, 50, 1000);
            else s.ScrollDurationMs = 230;

            if (DateTime.TryParse(Get(values, "settings.last_update_check"), CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var luc))
                s.LastUpdateCheckUtc = luc;

            s.LastKnownReleaseTag = Get(values, "settings.last_known_release_tag");

            return s;
        }

        public void Save(AppSettings s)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(IniPath)!);
            using var w = new StreamWriter(IniPath, false);
            w.WriteLine("[paths]");
            w.WriteLine($"manifests_dir={Escape(s.ManifestsDir ?? "")}");
            w.WriteLine($"download_base_dir={Escape(s.DownloadBaseDir ?? "")}");
            w.WriteLine();
            w.WriteLine("[ryuu]");
            w.WriteLine($"api_key={Escape(s.RyuuApiKey ?? "")}");
            w.WriteLine();
            w.WriteLine("[hubcap]");
            w.WriteLine($"api_key={Escape(s.HubcapApiKey ?? "")}");
            w.WriteLine();
            w.WriteLine("[steam]");
            w.WriteLine($"web_api_key={Escape(s.SteamWebApiKey ?? "")}");
            w.WriteLine();
            w.WriteLine("[settings]");
            w.WriteLine($"max_parallel_depots={s.MaxParallelDepots}");
            w.WriteLine($"download_achievement_icons={s.DownloadAchievementIcons.ToString().ToLowerInvariant()}");
            w.WriteLine($"store_cache_hours={s.StoreCacheHours}");
            w.WriteLine($"gpu_cache_days={s.GpuCacheDays}");
            w.WriteLine($"store_page_size={s.StorePageSize}");
            w.WriteLine($"search_debounce_ms={s.SearchDebounceMs}");
            w.WriteLine($"scroll_sensitivity={s.ScrollSensitivity}");
            w.WriteLine($"scroll_duration_ms={s.ScrollDurationMs}");
            w.WriteLine($"last_update_check={Escape(s.LastUpdateCheckUtc?.ToString("O") ?? "")}");
            w.WriteLine($"last_known_release_tag={Escape(s.LastKnownReleaseTag ?? "")}");
        }

        private static Dictionary<string, string> ParseIni(string path)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string section = "";
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#') continue;
                if (line[0] == '[' && line[^1] == ']') { section = line[1..^1].Trim(); continue; }
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                d[$"{section}.{line[..eq].Trim()}"] = Unescape(line[(eq + 1)..].Trim());
            }
            return d;
        }

        private static string? Get(Dictionary<string, string> d, string key)
            => d.TryGetValue(key, out var v) && v.Length > 0 ? v : null;

        private static string Escape(string v)
            => v.Replace("\\", "\\\\").Replace("\r", "\\r").Replace("\n", "\\n");

        private static string Unescape(string v)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < v.Length; i++)
            {
                if (v[i] == '\\' && i + 1 < v.Length)
                {
                    sb.Append((v[++i]) switch { 'r' => '\r', 'n' => '\n', '\\' => '\\', char c => c });
                    continue;
                }
                sb.Append(v[i]);
            }
            return sb.ToString();
        }
    }
}
