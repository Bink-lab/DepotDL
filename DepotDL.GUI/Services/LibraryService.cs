// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System.IO;
using System.Text.Json;
using DepotDL.GUI.Models;

namespace DepotDL.GUI.Services
{
    public class LibraryService
    {
        private static readonly string LibraryFilePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DepotDL", "library.json");

        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

        public List<LibraryGame> Load()
        {
            try
            {
                if (!File.Exists(LibraryFilePath)) return new();
                var json = File.ReadAllText(LibraryFilePath);
                return JsonSerializer.Deserialize<List<LibraryGame>>(json) ?? new();
            }
            catch { return new(); }
        }

        public void Save(List<LibraryGame> games)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LibraryFilePath)!);
                File.WriteAllText(LibraryFilePath, JsonSerializer.Serialize(games, JsonOpts));
            }
            catch { }
        }

        public void AddOrUpdate(LibraryGame game)
        {
            var lib = Load();
            var idx = lib.FindIndex(g => g.AppId == game.AppId);
            if (idx >= 0) lib[idx] = game;
            else lib.Add(game);
            Save(lib);
        }

        public string? Remove(string appId, string? filePathToDelete = null)
        {
            var lib = Load();
            lib.RemoveAll(g => g.AppId == appId);
            Save(lib);

            if (!string.IsNullOrEmpty(filePathToDelete) && Directory.Exists(filePathToDelete))
            {
                try
                {
                    Directory.Delete(filePathToDelete, recursive: true);
                }
                catch (Exception ex)
                {
                    return ex.Message;
                }
            }

            return null;
        }

        public void VerifyAll(List<LibraryGame> games)
        {
            var changed = false;
            foreach (var g in games)
            {
                var exists = Directory.Exists(g.OutputDir);
                if (g.IsVerified != exists) { g.IsVerified = exists; changed = true; }
            }
            if (changed) Save(games);
        }

        public static string FormatSize(long bytes)
        {
            if (bytes <= 0) return "—";
            if (bytes < 1024) return $"{bytes} B";
            if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
            if (bytes < 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024):F1} MB";
            return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
        }

        public static long GetDirectorySize(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return 0;
            long size = 0;
            try
            {
                var queue = new Queue<string>();
                queue.Enqueue(path);
                while (queue.Count > 0)
                {
                    var dir = queue.Dequeue();
                    try
                    {
                        foreach (var f in Directory.GetFiles(dir))
                        {
                            try { size += new FileInfo(f).Length; } catch { }
                        }
                        foreach (var sd in Directory.GetDirectories(dir))
                            queue.Enqueue(sd);
                    }
                    catch { }
                }
            }
            catch { }
            return size;
        }

    }
}

