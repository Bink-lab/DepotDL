// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

namespace DepotDL.GUI.Helpers
{
    public static class PriceFormatter
    {
        public static string Format(string? raw, string freeFallback = "Free")
        {
            if (string.IsNullOrEmpty(raw) || raw == "0") return freeFallback;
            if (int.TryParse(raw, out var cents)) return $"${cents / 100.0:F2}";
            return raw;
        }
    }
}
