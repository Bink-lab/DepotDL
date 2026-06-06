// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System.Text;
using System.Text.Json;
using DepotDL.CLI.Models;

namespace DepotDL.CLI.Services
{
    public static class RyuuApiClient
    {
        private static readonly HttpClient Http = new()
        {
            Timeout = TimeSpan.FromMinutes(5)
        };

        public static void RequestUpdate(string appId, string apiKey)
        {
            var url = $"https://generator.ryuu.lol/resellerrequestupdate?appid={Uri.EscapeDataString(appId)}&auth_code={Uri.EscapeDataString(apiKey)}";
            try
            {
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(15));
                using var response = Http.GetAsync(url, cts.Token).GetAwaiter().GetResult();
                if (response.StatusCode == System.Net.HttpStatusCode.BadRequest)
                {
                    var body = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                    var msg = ReadJsonMessage(body);
                    throw new InvalidOperationException(msg.Length == 0 ? "Ryuu: game not found in database." : $"Ryuu: {msg}");
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (InvalidOperationException) { throw; }
            catch { }
        }

        public static ManifestDownloadResult DownloadPackage(string appId, string apiKey)
        {
            RequestUpdate(appId, apiKey);
            var sanitizedAppId = SanitizeFileName(appId);
            var url = $"https://generator.ryuu.lol/secure_download?appid={Uri.EscapeDataString(appId)}&auth_code={Uri.EscapeDataString(apiKey)}";
            using var response = Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
            var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;

            if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                var body = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                var message = ReadJsonMessage(body);
                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException(message.Length == 0 ? $"Ryuu request failed with HTTP {(int)response.StatusCode}." : $"Ryuu: {message}");
                }

                return new ManifestDownloadResult
                {
                    HasZip = false,
                    Message = message.Length == 0 ? "Ryuu returned JSON without a downloadable ZIP." : $"Ryuu: {message}"
                };
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Ryuu request failed with HTTP {(int)response.StatusCode}.");
            }

            var zipPath = Path.Combine(Path.GetTempPath(), $"ryuu_{sanitizedAppId}_{Guid.NewGuid():N}.zip");
            using (var stream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
            using (var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.CopyTo(fileStream);
            }

            if (new FileInfo(zipPath).Length == 0)
            {
                File.Delete(zipPath);
                return new ManifestDownloadResult
                {
                    HasZip = false,
                    Message = "Ryuu returned an empty response."
                };
            }

            return new ManifestDownloadResult
            {
                HasZip = true,
                ZipPath = zipPath,
                Message = $"Downloaded Ryuu package to {zipPath}"
            };
        }

        private static string ReadJsonMessage(byte[] body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                if (root.TryGetProperty("message", out var message))
                {
                    return message.GetString() ?? string.Empty;
                }

                if (root.TryGetProperty("error", out var error))
                {
                    return error.GetString() ?? string.Empty;
                }
            }
            catch
            {
            }

            return Encoding.UTF8.GetString(body).Trim();
        }

        private static string SanitizeFileName(string fileName)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(fileName.Length);

            foreach (var c in fileName)
            {
                if (c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar || Array.IndexOf(invalidChars, c) >= 0)
                {
                    sb.Append('_');
                }
                else
                {
                    sb.Append(c);
                }
            }

            return sb.ToString();
        }
    }
}
