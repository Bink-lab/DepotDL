namespace DepotDL.CLI
{
    internal static class DepotDownloadDefaults
    {
        public const int MaxDownloads = 64;

        public static int NormalizeMaxDownloads(int value)
        {
            return Math.Clamp(value, 1, 128);
        }
    }
}
