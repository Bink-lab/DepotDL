using System;
using System.Collections.Generic;
using System.IO;

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

            if (int.TryParse(Get(values, "settings.max_parallel_depots"), out var maxParallel))
            {
                session.MaxParallelDepots = Math.Clamp(maxParallel, 1, 8);
            }
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
            writer.WriteLine("[settings]");
            WriteValue(writer, "max_parallel_depots", session.MaxParallelDepots.ToString());
        }

        private static Dictionary<string, string> Load()
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(IniPath))
            {
                return values;
            }

            string section = "";
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
            for (int i = 0; i < value.Length; i++)
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
