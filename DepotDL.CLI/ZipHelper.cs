using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;

namespace DepotDL.CLI
{
    public sealed class ZipImportResult
    {
        public int LuaCount { get; init; }
        public int ManifestCount { get; init; }
        public string? FirstLuaPath { get; init; }
        public string ImportDir { get; init; } = string.Empty;
        public string ManifestsDir { get; init; } = string.Empty;
    }

    public static class ZipHelper
    {
        public static ZipImportResult ImportZip(string zipPath)
        {
            int luaCount = 0;
            int manifestCount = 0;
            string? firstLuaPath = null;
            string importDir = string.Empty;
            string manifestsDir = string.Empty;

            try
            {
                if (!File.Exists(zipPath))
                {
                    return new ZipImportResult();
                }

                using (ZipArchive archive = ZipFile.OpenRead(zipPath))
                {
                    importDir = BuildImportDir(zipPath, archive);
                    manifestsDir = Path.Combine(importDir, "manifests");
                    Directory.CreateDirectory(importDir);
                    Directory.CreateDirectory(manifestsDir);

                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        var ext = Path.GetExtension(entry.FullName).ToLower();
                        if (ext == ".lua")
                        {
                            var fileName = Path.GetFileName(entry.FullName);
                            if (string.IsNullOrEmpty(fileName)) continue;

                            var targetPath = Path.Combine(importDir, fileName);
                            entry.ExtractToFile(targetPath, overwrite: true);

                            luaCount++;
                            if (firstLuaPath == null)
                            {
                                firstLuaPath = Path.GetFullPath(targetPath);
                            }
                        }
                        else if (ext == ".manifest")
                        {
                            var fileName = Path.GetFileName(entry.FullName);
                            if (string.IsNullOrEmpty(fileName)) continue;

                            var targetPath = Path.Combine(manifestsDir, fileName);
                            entry.ExtractToFile(targetPath, overwrite: true);
                            manifestCount++;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Error during ZIP extraction] {ex.Message}");
            }

            return new ZipImportResult
            {
                LuaCount = luaCount,
                ManifestCount = manifestCount,
                FirstLuaPath = firstLuaPath,
                ImportDir = importDir,
                ManifestsDir = manifestsDir
            };
        }

        private static string BuildImportDir(string zipPath, ZipArchive archive)
        {
            var firstLua = archive.Entries.FirstOrDefault(entry => Path.GetExtension(entry.FullName).Equals(".lua", StringComparison.OrdinalIgnoreCase));
            string folderName = firstLua == null
                ? Path.GetFileNameWithoutExtension(zipPath)
                : Path.GetFileNameWithoutExtension(firstLua.FullName);

            folderName = SanitizeFolderName(folderName);
            if (string.IsNullOrWhiteSpace(folderName))
            {
                folderName = "import";
            }

            return Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "imports", folderName));
        }

        private static string SanitizeFolderName(string value)
        {
            var invalid = Regex.Escape(new string(Path.GetInvalidFileNameChars()));
            return Regex.Replace(value, $"[{invalid}]+", "_").Trim();
        }
    }
}
