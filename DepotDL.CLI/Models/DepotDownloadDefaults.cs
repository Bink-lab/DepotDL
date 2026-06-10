// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

namespace DepotDL.CLI.Models
{
    public static class DepotDownloadDefaults
    {
        public const int MaxDownloads = 64;

        public static int NormalizeMaxDownloads(int value)
        {
            return Math.Clamp(value, 1, 128);
        }
    }
}
