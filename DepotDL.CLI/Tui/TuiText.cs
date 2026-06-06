// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

namespace DepotDL.CLI.Tui
{
    internal static class TuiText
    {
        public static string Pad(string? value, int width)
        {
            return Shorten(value, width).PadRight(width);
        }

        public static string Shorten(string? value, int maxLength)
        {
            value ??= string.Empty;
            if (value.Length <= maxLength) return value;
            if (maxLength <= 3) return value[..Math.Max(0, maxLength)];

            return value[..(maxLength - 3)] + "...";
        }

        public static string ShortenTail(string? value, int maxLength)
        {
            value ??= string.Empty;
            if (value.Length <= maxLength) return value;
            if (maxLength <= 3) return value[^Math.Max(0, maxLength)..];

            return "..." + value[^Math.Max(0, maxLength - 3)..];
        }

        public static string ShortenPath(string? path, int maxLength)
        {
            path ??= string.Empty;
            if (path.Length <= maxLength) return path;

            var fileName = Path.GetFileName(path);
            if (fileName.Length + 4 < maxLength)
            {
                return "..." + Path.DirectorySeparatorChar + fileName;
            }

            return "..." + path[^Math.Max(0, maxLength - 3)..];
        }
    }
}
