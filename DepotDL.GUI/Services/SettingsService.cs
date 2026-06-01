using System;
using System.Collections.Generic;
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
            if (int.TryParse(Get(values, "settings.max_parallel_depots"), out int mp))
                s.MaxParallelDepots = Math.Clamp(mp, 1, 8);
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
            w.WriteLine("[settings]");
            w.WriteLine($"max_parallel_depots={s.MaxParallelDepots}");
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
