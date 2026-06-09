// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System.IO;
using SteamKit2;

namespace DepotDL.CLI.Services
{
    public static class ManifestSizeReader
    {
        public static long TryGetSize(string? dir, string depotId, string manifestId)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return 0;
            if (string.IsNullOrEmpty(depotId) || string.IsNullOrEmpty(manifestId)) return 0;
            var path = Path.Combine(dir, $"{depotId}_{manifestId}.manifest");
            if (!File.Exists(path)) return 0;
            try
            {
                var manifest = DepotManifest.LoadFromFile(path);
                return manifest == null ? 0 : (long)manifest.TotalUncompressedSize;
            }
            catch { return 0; }
        }
    }
}
