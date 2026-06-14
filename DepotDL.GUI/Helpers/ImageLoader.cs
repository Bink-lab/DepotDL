// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System.IO;
using System.Net.Http;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DepotDL.GUI.ViewModels;

namespace DepotDL.GUI.Helpers
{
    public static class ImageLoader
    {
        private static readonly string CacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DepotDL.GUI", "imagecache");

        private const int MaxCacheFiles = 500;
        private static readonly object _evictLock = new();

        private static readonly HttpClient Http = new()
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        static ImageLoader()
        {
            Http.DefaultRequestHeaders.UserAgent.ParseAdd("DepotDL/1.0");
        }

        public static async Task<Bitmap?> LoadAsync(string url, int appId, CancellationToken ct = default)
        {
            try
            {
                Directory.CreateDirectory(CacheDir);
                var cachePath = Path.Combine(CacheDir, $"{appId}.jpg");

                byte[] data;
                if (File.Exists(cachePath) && new FileInfo(cachePath).Length > 0)
                {
                    data = await File.ReadAllBytesAsync(cachePath, ct);
                }
                else
                {
                    data = await Http.GetByteArrayAsync(url, ct);
                    await File.WriteAllBytesAsync(cachePath, data, ct);
                    EvictIfNeeded();
                }

                using var ms = new MemoryStream(data);
                return new Bitmap(ms);
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                try
                {
                    var cachePath = Path.Combine(CacheDir, $"{appId}.jpg");
                    if (File.Exists(cachePath))
                        File.Delete(cachePath);
                }
                catch { }
                return null;
            }
        }

        private static void EvictIfNeeded()
        {
            lock (_evictLock)
            {
                try
                {
                    var files = new DirectoryInfo(CacheDir).GetFiles("*");
                    if (files.Length <= MaxCacheFiles) return;
                    foreach (var f in files.OrderBy(f => f.LastWriteTimeUtc).Take(files.Length - MaxCacheFiles))
                    {
                        try { f.Delete(); } catch { }
                    }
                }
                catch { }
            }
        }

        public static async Task LoadGameImageAsync(ISteamGameViewModel vm, int appId, string url, CancellationToken ct = default)
        {
            if (vm.HeaderImage != null) return;
            vm.IsImageLoading = true;
            try
            {
                var bmp = await LoadAsync(url, appId, ct);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    vm.HeaderImage = bmp;
                    vm.IsImageLoading = false;
                });
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                await Dispatcher.UIThread.InvokeAsync(() => vm.IsImageLoading = false);
            }
        }
    }
}
