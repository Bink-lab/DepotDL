// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System.IO;
using System.IO.Compression;

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
            var dir = outputDir.TrimEnd('\\', '/');
            var parentDir = Path.GetDirectoryName(dir) ?? dir;
            var folderName = Path.GetFileName(dir);
            var zipPath = Path.Combine(parentDir, folderName + ".zip");
            var tmpPath = zipPath + ".tmp";

            await Task.Run(() =>
            {
                if (File.Exists(tmpPath)) File.Delete(tmpPath);

                var files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).ToList();
                var total = files.Count;
                var done = 0;
                var lastPct = -1;

                progress.Report((0, $"0 / {total:N0} files"));

                try
                {
                    using (var archive = ZipFile.Open(tmpPath, ZipArchiveMode.Create))
                    {
                        foreach (var file in files)
                        {
                            ct.ThrowIfCancellationRequested();
                            var entryName = Path.GetRelativePath(dir, file);
                            archive.CreateEntryFromFile(file, entryName, compression);
                            done++;
                            var pctInt = total == 0 ? 100 : (int)((double)done / total * 100.0);
                            if (pctInt != lastPct)
                            {
                                lastPct = pctInt;
                                var snap = done;
                                progress.Report((pctInt, $"{snap:N0} / {total:N0} files"));
                            }
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    if (File.Exists(tmpPath)) File.Delete(tmpPath);
                    throw;
                }

                if (File.Exists(zipPath)) File.Delete(zipPath);
                File.Move(tmpPath, zipPath);
            }, ct);

            return zipPath;
        }
    }
}
