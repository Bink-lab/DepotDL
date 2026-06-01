using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace DepotDL.CLI
{
    public sealed class RyuuDownloadResult
    {
        public bool HasZip { get; init; }
        public string? ZipPath { get; init; }
        public string Message { get; init; } = string.Empty;
    }

    public static class RyuuApiClient
    {
        private static readonly HttpClient Http = new()
        {
            Timeout = TimeSpan.FromMinutes(5)
        };

        public static RyuuDownloadResult DownloadPackage(string appId, string apiKey)
        {
            // Sanitize appId to prevent path traversal
            var sanitizedAppId = SanitizeFileName(appId);
            var url = $"https://generator.ryuu.lol/secure_download?appid={Uri.EscapeDataString(appId)}&auth_code={Uri.EscapeDataString(apiKey)}";
            using var response = Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
            var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;

            // Check if response is JSON by content type first
            if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                var body = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                var message = ReadJsonMessage(body);
                if (!response.IsSuccessStatusCode)
                {
                    throw new InvalidOperationException(message.Length == 0 ? $"Ryuu request failed with HTTP {(int)response.StatusCode}." : $"Ryuu: {message}");
                }

                return new RyuuDownloadResult
                {
                    HasZip = false,
                    Message = message.Length == 0 ? "Ryuu returned JSON without a downloadable ZIP." : $"Ryuu: {message}"
                };
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Ryuu request failed with HTTP {(int)response.StatusCode}.");
            }

            // Stream response to file instead of buffering in memory
            var zipPath = Path.Combine(Path.GetTempPath(), $"ryuu_{sanitizedAppId}_{Guid.NewGuid():N}.zip");
            using (var stream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
            using (var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.CopyTo(fileStream);
            }

            if (new FileInfo(zipPath).Length == 0)
            {
                File.Delete(zipPath);
                return new RyuuDownloadResult
                {
                    HasZip = false,
                    Message = "Ryuu returned an empty response."
                };
            }

            return new RyuuDownloadResult
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
