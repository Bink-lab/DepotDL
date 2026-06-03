using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using DepotDL.GUI.Models;

namespace DepotDL.GUI.Services
{
    public class RyuuService
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

        private const int MaxRetries = 3;

        private async Task RequestUpdateAsync(string appId, string apiKey)
        {
            var url = $"https://generator.ryuu.lol/resellerrequestupdate?appid={Uri.EscapeDataString(appId)}&auth_code={Uri.EscapeDataString(apiKey)}";
            for (int attempt = 0; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(15));
                    using var response = await Http.GetAsync(url, cts.Token);
                    if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
                    {
                        var body = await response.Content.ReadAsByteArrayAsync();
                        var msg = ReadJsonMessage(body);
                        throw new InvalidOperationException(msg.Length == 0 ? "Ryuu: game not found in database." : $"Ryuu: {msg}");
                    }
                    return;
                }
                catch (InvalidOperationException) { throw; }
                catch when (attempt < MaxRetries) { await Task.Delay(2000); }
            }
        }

        public async Task<ManifestDownloadResult> DownloadPackageAsync(string appId, string apiKey, Action<string>? onStatus = null)
        {
            onStatus?.Invoke("Updating manifests...");
            await RequestUpdateAsync(appId, apiKey);

            for (int attempt = 0; attempt <= MaxRetries; attempt++)
            {
                string attemptSuffix = attempt > 0 ? $" (retry {attempt}/{MaxRetries})" : string.Empty;

                onStatus?.Invoke($"Downloading...{attemptSuffix}");
                var url = $"https://generator.ryuu.lol/secure_download?appid={Uri.EscapeDataString(appId)}&auth_code={Uri.EscapeDataString(apiKey)}";
                try
                {
                    using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                    var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;

                    if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
                    {
                        var body = await response.Content.ReadAsByteArrayAsync();
                        var message = ReadJsonMessage(body);
                        if (!response.IsSuccessStatusCode)
                            throw new InvalidOperationException(message.Length == 0
                                ? $"Ryuu request failed with HTTP {(int)response.StatusCode}."
                                : $"Ryuu: {message}");

                        return new ManifestDownloadResult
                        {
                            HasZip = false,
                            Message = message.Length == 0 ? "Ryuu returned JSON without a downloadable ZIP." : $"Ryuu: {message}"
                        };
                    }

                    if (!response.IsSuccessStatusCode)
                        throw new InvalidOperationException($"Ryuu request failed with HTTP {(int)response.StatusCode}.");

                    var zipPath = Path.Combine(Path.GetTempPath(), $"ryuu_{SanitizeFileName(appId)}_{Guid.NewGuid():N}.zip");
                    using (var stream = await response.Content.ReadAsStreamAsync())
                    using (var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
                    {
                        await stream.CopyToAsync(fileStream);
                    }

                    if (new FileInfo(zipPath).Length == 0)
                    {
                        File.Delete(zipPath);
                        return new ManifestDownloadResult { HasZip = false, Message = "Ryuu returned an empty response." };
                    }

                    return new ManifestDownloadResult { HasZip = true, ZipPath = zipPath, Message = $"Downloaded package for App {appId}" };
                }
                catch (InvalidOperationException) { throw; }
                catch when (attempt < MaxRetries) { await Task.Delay(2000); }
            }

            throw new InvalidOperationException($"Ryuu download failed after {MaxRetries} retries.");
        }

        private static string ReadJsonMessage(byte[] body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.TryGetProperty("message", out var msg)) return msg.GetString() ?? string.Empty;
                if (root.TryGetProperty("error", out var err)) return err.GetString() ?? string.Empty;
            }
            catch { }
            return Encoding.UTF8.GetString(body).Trim();
        }

        private static string SanitizeFileName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(value.Length);
            foreach (var c in value)
            {
                if (c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar || Array.IndexOf(invalid, c) >= 0)
                    sb.Append('_');
                else
                    sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
