// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System.IO;

namespace DepotDL.Shared
{
    public static class DepotCheckpoint
    {
        public static string GetCheckpointDir(string outputPath) =>
            Path.Combine(outputPath, ".depotdl_progress");

        public static HashSet<string> LoadCompletedDepots(string outputPath)
        {
            try
            {
                var dir = GetCheckpointDir(outputPath);
                if (!Directory.Exists(dir)) return new HashSet<string>();
                return Directory.GetFiles(dir, "*.done")
                    .Select(f => Path.GetFileNameWithoutExtension(f))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
            catch { return new HashSet<string>(); }
        }

        public static void MarkDepotComplete(string outputPath, string depotId)
        {
            try
            {
                var dir = GetCheckpointDir(outputPath);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, $"{depotId}.done"), "");
            }
            catch { }
        }

        public static void ClearCheckpoints(string outputPath)
        {
            try
            {
                var dir = GetCheckpointDir(outputPath);
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
            catch { }
        }
    }
}
