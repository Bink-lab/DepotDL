// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System.IO;

namespace DepotDL.CLI
{
    internal static class AppLogger
    {
        private static readonly string LogPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "depotdl.log");
        private static readonly object _lock = new();
        private const long MaxLogBytes = 2 * 1024 * 1024;

        static AppLogger()
        {
            try
            {
                if (File.Exists(LogPath) && new FileInfo(LogPath).Length > MaxLogBytes)
                    File.Delete(LogPath);
            }
            catch { }
        }

        public static void Info(string category, string message) => Write("INFO", category, message);
        public static void Warn(string category, string message) => Write("WARN", category, message);
        public static void Error(string category, string message) => Write("ERROR", category, message);
        public static void Debug(string category, string message) => Write("DEBUG", category, message);

        private static void Write(string level, string category, string message)
        {
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{level}] [{category}] {message}";
            lock (_lock)
            {
                try { File.AppendAllText(LogPath, line + Environment.NewLine); }
                catch { }
            }
        }
    }
}
