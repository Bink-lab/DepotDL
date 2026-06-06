// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System.IO;
using System.IO.Compression;
using System.Text.RegularExpressions;

namespace DepotDL.GUI.Services
{
    public sealed class ZipImportResult
    {
        public int LuaCount { get; init; }
        public int ManifestCount { get; init; }
        public string? FirstLuaPath { get; init; }
        public string ImportDir { get; init; } = string.Empty;
        public string ManifestsDir { get; init; } = string.Empty;
    }

    public class ZipImportService
    {
        public ZipImportResult ImportZip(string zipPath)
        {
            if (!File.Exists(zipPath)) return new ZipImportResult();

            int luaCount = 0, manifestCount = 0;
            string? firstLuaPath = null;

            using var archive = ZipFile.OpenRead(zipPath);
            var importDir = BuildImportDir(zipPath, archive);
            var manifestsDir = Path.Combine(importDir, "manifests");
            Directory.CreateDirectory(importDir);
            Directory.CreateDirectory(manifestsDir);

            var importDirRoot = Path.GetFullPath(importDir + Path.DirectorySeparatorChar);
            var manifestsDirRoot = Path.GetFullPath(manifestsDir + Path.DirectorySeparatorChar);

            foreach (var entry in archive.Entries)
            {
                var ext = Path.GetExtension(entry.FullName).ToLowerInvariant();
                var fileName = Path.GetFileName(entry.FullName);
                if (string.IsNullOrEmpty(fileName)) continue;

                if (ext == ".lua")
                {
                    var target = Path.GetFullPath(Path.Combine(importDir, fileName));
                    if (!target.StartsWith(importDirRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException($"Entry resolves outside import directory: {entry.FullName}");
                    }

                    entry.ExtractToFile(target, overwrite: true);
                    luaCount++;
                    firstLuaPath ??= target;
                }
                else if (ext == ".manifest")
                {
                    var target = Path.GetFullPath(Path.Combine(manifestsDir, fileName));
                    if (!target.StartsWith(manifestsDirRoot, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException($"Entry resolves outside manifests directory: {entry.FullName}");
                    }

                    entry.ExtractToFile(target, overwrite: true);
                    manifestCount++;
                }
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
            var firstLua = archive.Entries.FirstOrDefault(e =>
                Path.GetExtension(e.FullName).Equals(".lua", StringComparison.OrdinalIgnoreCase));
            var folderName = firstLua == null
                ? Path.GetFileNameWithoutExtension(zipPath)
                : Path.GetFileNameWithoutExtension(firstLua.FullName);

            folderName = SanitizeFolderName(folderName);
            if (string.IsNullOrWhiteSpace(folderName)) folderName = "import";

            var importsRoot = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "imports") + Path.DirectorySeparatorChar);
            var importDir = Path.GetFullPath(Path.Combine(importsRoot, folderName));
            if (!importDir.StartsWith(importsRoot, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Import directory resolves outside imports root: {folderName}");
            }

            return importDir;
        }

        private static string SanitizeFolderName(string value)
        {
            var invalid = Regex.Escape(new string(Path.GetInvalidFileNameChars()));
            return Regex.Replace(value, $"[{invalid}]+", "_").Trim();
        }
    }
}
