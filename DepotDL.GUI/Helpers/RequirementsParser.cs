// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System.Text.RegularExpressions;

namespace DepotDL.GUI.Helpers
{
    public static partial class RequirementsParser
    {
        [GeneratedRegex(@"<[^>]+>")]
        private static partial Regex HtmlTagRegex();

        [GeneratedRegex(@"<li[^>]*><strong>([^<:]+):</strong>\s*(.*?)(?:<br\s*/?>|(?=</li>))", RegexOptions.Singleline)]
        private static partial Regex ListItemRegex();

        public static Dictionary<string, string> Parse(string html)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(html)) return result;

            foreach (Match m in ListItemRegex().Matches(html))
            {
                var key = m.Groups[1].Value.Trim();
                var value = HtmlTagRegex().Replace(m.Groups[2].Value, "").Trim();
                if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value))
                    result[key] = value;
            }

            return result;
        }

        public static string StripHtml(string html)
        {
            if (string.IsNullOrWhiteSpace(html)) return string.Empty;
            var text = HtmlTagRegex().Replace(html, " ");
            text = Regex.Replace(text, @"\s{2,}", " ").Trim();
            return text;
        }

        public static long ParseRamMb(string value)
        {
            var m = Regex.Match(value, @"(\d+)\s*GB", RegexOptions.IgnoreCase);
            if (m.Success && long.TryParse(m.Groups[1].Value, out var gb)) return gb * 1024;
            m = Regex.Match(value, @"(\d+)\s*MB", RegexOptions.IgnoreCase);
            if (m.Success && long.TryParse(m.Groups[1].Value, out var mb)) return mb;
            return 0;
        }

        public static double ParseStorageGb(string value)
        {
            var m = Regex.Match(value, @"([\d.]+)\s*GB", RegexOptions.IgnoreCase);
            if (m.Success && double.TryParse(m.Groups[1].Value, out var gb)) return gb;
            m = Regex.Match(value, @"([\d.]+)\s*MB", RegexOptions.IgnoreCase);
            if (m.Success && double.TryParse(m.Groups[1].Value, out var mb)) return mb / 1024.0;
            return 0;
        }

        public static string? ExtractFirstCpuModel(string requirementText)
        {
            if (string.IsNullOrWhiteSpace(requirementText)) return null;

            var m = Regex.Match(requirementText,
                @"Intel(?:\s+Core)?\s+i[3579][-\s]\d{3,5}[A-Z0-9]*",
                RegexOptions.IgnoreCase);
            if (m.Success) return m.Value;

            m = Regex.Match(requirementText,
                @"AMD\s+Ryzen\s+[357]\s+\d{3,4}[A-Z0-9]*(?:\s+\d+X\d+D)?",
                RegexOptions.IgnoreCase);
            if (m.Success) return m.Value;

            m = Regex.Match(requirementText,
                @"Intel\s+Xeon\s+\w+(?:-\w+)?",
                RegexOptions.IgnoreCase);
            if (m.Success) return m.Value;

            m = Regex.Match(requirementText,
                @"AMD\s+FX(?:-\d+)?",
                RegexOptions.IgnoreCase);
            if (m.Success) return m.Value;

            m = Regex.Match(requirementText,
                @"Intel\s+i[3579](?:\s+\d{3,5}[A-Z0-9]*)?",
                RegexOptions.IgnoreCase);
            if (m.Success) return m.Value;

            return null;
        }

        public static string? ExtractFirstGpuModel(string requirementText)
        {
            if (string.IsNullOrWhiteSpace(requirementText)) return null;

            var m = Regex.Match(requirementText,
                @"NVIDIA\s+(?:GeForce\s+)?(?:RTX|GTX|GT)\s+\d{3,4}(?:\s+(?:Ti|Super|XT|XTX))?\b",
                RegexOptions.IgnoreCase);
            if (m.Success) return m.Value;

            m = Regex.Match(requirementText,
                @"AMD\s+Radeon\s+RX\s+\d{3,4}(?:\s+(?:XT|XTX))?\b",
                RegexOptions.IgnoreCase);
            if (m.Success) return m.Value;

            m = Regex.Match(requirementText,
                @"AMD\s+Radeon\s+R[579]\s+\d{3}",
                RegexOptions.IgnoreCase);
            if (m.Success) return m.Value;

            m = Regex.Match(requirementText,
                @"Intel\s+Arc\s+A\d{3}",
                RegexOptions.IgnoreCase);
            if (m.Success) return m.Value;

            return null;
        }
    }
}
