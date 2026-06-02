using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace DepotDL.CLI
{
    public static class HubcapApiClient
    {
        private static readonly HttpClient Http = new()
        {
            Timeout = TimeSpan.FromMinutes(5)
        };

        public static ManifestDownloadResult DownloadPackage(string appId, string apiKey)
        {
            var sanitizedAppId = SanitizeFileName(appId);
            var url = $"https://hubcapmanifest.com/api/v1/manifest/{Uri.EscapeDataString(appId)}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);

            using var response = Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
            var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;

            if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                var body = response.Content.ReadAsByteArrayAsync().GetAwaiter().GetResult();
                var message = ReadJsonMessage(body);
                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException(message.Length == 0 ? $"Hubcap request failed with HTTP {(int)response.StatusCode}." : $"Hubcap: {message}");

                return new ManifestDownloadResult
                {
                    HasZip = false,
                    Message = message.Length == 0 ? "Hubcap returned JSON without a downloadable ZIP." : $"Hubcap: {message}"
                };
            }

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Hubcap request failed with HTTP {(int)response.StatusCode}.");

            var zipPath = Path.Combine(Path.GetTempPath(), $"hubcap_{sanitizedAppId}_{Guid.NewGuid():N}.zip");
            using (var stream = response.Content.ReadAsStreamAsync().GetAwaiter().GetResult())
            using (var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.CopyTo(fileStream);
            }

            if (new FileInfo(zipPath).Length == 0)
            {
                File.Delete(zipPath);
                return new ManifestDownloadResult { HasZip = false, Message = "Hubcap returned an empty response." };
            }

            return new ManifestDownloadResult { HasZip = true, ZipPath = zipPath, Message = $"Downloaded Hubcap package to {zipPath}" };
        }

        private static string ReadJsonMessage(byte[] body)
        {
            try
            {
                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;
                if (root.TryGetProperty("message", out var message)) return message.GetString() ?? string.Empty;
                if (root.TryGetProperty("error", out var error)) return error.GetString() ?? string.Empty;
            }
            catch { }
            return Encoding.UTF8.GetString(body).Trim();
        }

        private static string SanitizeFileName(string fileName)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(fileName.Length);
            foreach (var c in fileName)
            {
                if (c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar || Array.IndexOf(invalidChars, c) >= 0)
                    sb.Append('_');
                else
                    sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
