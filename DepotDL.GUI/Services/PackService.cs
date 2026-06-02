using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace DepotDL.GUI.Services
{
    public class PackService
    {
        public async Task<string> PackAsync(
            string outputDir,
            IProgress<(double percent, string status)> progress,
            CancellationToken ct = default,
            CompressionLevel compression = CompressionLevel.Optimal)
        {
            string dir = outputDir.TrimEnd('\\', '/');
            string parentDir = Path.GetDirectoryName(dir) ?? dir;
            string folderName = Path.GetFileName(dir);
            string zipPath = Path.Combine(parentDir, folderName + ".zip");
            string tmpPath = zipPath + ".tmp";

            await Task.Run(() =>
            {
                if (File.Exists(tmpPath)) File.Delete(tmpPath);

                var files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).ToList();
                int total = files.Count;
                int done = 0;
                int lastPct = -1;

                progress.Report((0, $"0 / {total:N0} files"));

                using (var archive = ZipFile.Open(tmpPath, ZipArchiveMode.Create))
                {
                    foreach (string file in files)
                    {
                        ct.ThrowIfCancellationRequested();
                        string entryName = Path.GetRelativePath(dir, file);
                        archive.CreateEntryFromFile(file, entryName, compression);
                        done++;
                        int pctInt = total == 0 ? 100 : (int)((double)done / total * 100.0);
                        if (pctInt != lastPct)
                        {
                            lastPct = pctInt;
                            int snap = done;
                            progress.Report((pctInt, $"{snap:N0} / {total:N0} files"));
                        }
                    }
                }

                if (File.Exists(zipPath)) File.Delete(zipPath);
                File.Move(tmpPath, zipPath);
            }, ct);

            return zipPath;
        }
    }
}
