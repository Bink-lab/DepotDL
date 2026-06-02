using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using DepotDL.GUI.ViewModels;

namespace DepotDL.GUI.Helpers
{
    public static class ImageLoader
    {
        private static readonly string CacheDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DepotDL.GUI", "imagecache");

        private static readonly HttpClient Http = new()
        {
            Timeout = TimeSpan.FromSeconds(15)
        };

        static ImageLoader()
        {
            Http.DefaultRequestHeaders.UserAgent.ParseAdd("DepotDL/1.0");
        }

        public static async Task<BitmapImage?> LoadAsync(string url, int appId, CancellationToken ct = default)
        {
            try
            {
                Directory.CreateDirectory(CacheDir);
                string cachePath = Path.Combine(CacheDir, $"{appId}.jpg");

                byte[] data;
                if (File.Exists(cachePath) && new FileInfo(cachePath).Length > 0)
                {
                    data = await File.ReadAllBytesAsync(cachePath, ct);
                }
                else
                {
                    data = await Http.GetByteArrayAsync(url, ct);
                    await File.WriteAllBytesAsync(cachePath, data, ct);
                }

                using var ms = new MemoryStream(data);
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                try
                {
                    string cachePath = Path.Combine(CacheDir, $"{appId}.jpg");
                    if (File.Exists(cachePath))
                        File.Delete(cachePath);
                }
                catch { }
                return null;
            }
        }

        public static async Task LoadGameImageAsync(ISteamGameViewModel vm, int appId, string url, CancellationToken ct = default)
        {
            if (vm.HeaderImage != null) return;
            vm.IsImageLoading = true;
            try
            {
                var bmp = await LoadAsync(url, appId, ct);
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    vm.HeaderImage = bmp;
                    vm.IsImageLoading = false;
                });
            }
            catch (OperationCanceledException) { throw; }
            catch
            {
                await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => vm.IsImageLoading = false);
            }
        }
    }
}
