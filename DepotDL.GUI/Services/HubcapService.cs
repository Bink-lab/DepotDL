// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using DepotDL.GUI.Models;

namespace DepotDL.GUI.Services
{
    public class HubcapService
    {
        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

        public async Task<ManifestDownloadResult> DownloadPackageAsync(string appId, string apiKey)
        {
            var url = $"https://hubcapmanifest.com/api/v1/manifest/{Uri.EscapeDataString(appId)}";
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;

            if (contentType.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                var body = await response.Content.ReadAsByteArrayAsync();
                var message = ReadJsonMessage(body);
                if (!response.IsSuccessStatusCode)
                    throw new InvalidOperationException(message.Length == 0
                        ? $"Hubcap request failed with HTTP {(int)response.StatusCode}."
                        : $"Hubcap: {message}");

                return new ManifestDownloadResult
                {
                    HasZip = false,
                    Message = message.Length == 0 ? "Hubcap returned JSON without a downloadable ZIP." : $"Hubcap: {message}"
                };
            }

            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException($"Hubcap request failed with HTTP {(int)response.StatusCode}.");

            var zipPath = Path.Combine(Path.GetTempPath(), $"hubcap_{SanitizeFileName(appId)}_{Guid.NewGuid():N}.zip");
            using (var stream = await response.Content.ReadAsStreamAsync())
            using (var fileStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await stream.CopyToAsync(fileStream);
            }

            if (new FileInfo(zipPath).Length == 0)
            {
                File.Delete(zipPath);
                return new ManifestDownloadResult { HasZip = false, Message = "Hubcap returned an empty response." };
            }

            return new ManifestDownloadResult { HasZip = true, ZipPath = zipPath, Message = $"Downloaded package for App {appId}" };
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
