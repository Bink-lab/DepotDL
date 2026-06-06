// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System.IO.Compression;
using System.Text.RegularExpressions;

namespace DepotDL.CLI.Utilities
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
            var luaCount = 0;
            var manifestCount = 0;
            string? firstLuaPath = null;
            var importDir = string.Empty;
            var manifestsDir = string.Empty;

            try
            {
                if (!File.Exists(zipPath))
                {
                    return new ZipImportResult();
                }

                using (var archive = ZipFile.OpenRead(zipPath))
                {
                    importDir = BuildImportDir(zipPath, archive);
                    manifestsDir = Path.Combine(importDir, "manifests");
                    Directory.CreateDirectory(importDir);
                    Directory.CreateDirectory(manifestsDir);

                    foreach (var entry in archive.Entries)
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
            var folderName = firstLua == null
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
