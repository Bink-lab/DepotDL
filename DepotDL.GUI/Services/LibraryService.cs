using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using DepotDL.GUI.Models;

namespace DepotDL.GUI.Services
{
    public class LibraryService
    {
        private static readonly string LibraryFilePath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "library.json");

        private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

        public List<LibraryGame> Load()
        {
            try
            {
                if (!File.Exists(LibraryFilePath)) return new();
                string json = File.ReadAllText(LibraryFilePath);
                return JsonSerializer.Deserialize<List<LibraryGame>>(json) ?? new();
            }
            catch { return new(); }
        }

        public void Save(List<LibraryGame> games)
        {
            try
            {
                File.WriteAllText(LibraryFilePath, JsonSerializer.Serialize(games, JsonOpts));
            }
            catch { }
        }

        public void AddOrUpdate(LibraryGame game)
        {
            var lib = Load();
            int idx = lib.FindIndex(g => g.AppId == game.AppId);
            if (idx >= 0) lib[idx] = game;
            else lib.Add(game);
            Save(lib);
        }

        public void Remove(string appId)
        {
            var lib = Load();
            lib.RemoveAll(g => g.AppId == appId);
            Save(lib);
        }

        public void VerifyAll(List<LibraryGame> games)
        {
            bool changed = false;
            foreach (var g in games)
            {
                bool exists = Directory.Exists(g.OutputDir);
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
                    string dir = queue.Dequeue();
                    try
                    {
                        foreach (string f in Directory.GetFiles(dir))
                        {
                            try { size += new FileInfo(f).Length; } catch { }
                        }
                        foreach (string sd in Directory.GetDirectories(dir))
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
