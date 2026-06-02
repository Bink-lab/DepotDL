namespace DepotDL.GUI.Helpers
{
    public static class PriceFormatter
    {
        public static string Format(string? raw, string freeFallback = "Free")
        {
            if (string.IsNullOrEmpty(raw) || raw == "0") return freeFallback;
            if (int.TryParse(raw, out int cents)) return $"${cents / 100.0:F2}";
            return raw;
        }
    }
}
