// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using PuppeteerSharp;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace DepotDL.GUI.Services
{
    public static class OnlineFixService
    {
        private const string OnlineFixBase = "https://online-fix.me";
        private const string ArchivePassword = "online-fix.me";
        private const string MarkerFileName = ".onlinefix_applied";
        private const double MatchThreshold = 0.5;

        private static string ChromiumCacheDir =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools", "chromium");

        private static void Log(string message) =>
            DepotDL.CLI.AppLogger.Debug("OnlineFix", message);

        public static bool IsChromiumInstalled()
        {
            try
            {
                var fetcher = new BrowserFetcher(new BrowserFetcherOptions { Path = ChromiumCacheDir });
                return fetcher.GetInstalledBrowsers().Any();
            }
            catch { return false; }
        }

        public static async Task EnsureChromiumAsync(IProgress<string>? progress = null, CancellationToken ct = default)
        {
            progress?.Report("Downloading browser engine (~170 MB, please wait)...");
            Directory.CreateDirectory(ChromiumCacheDir);
            ct.ThrowIfCancellationRequested();
            var fetcher = new BrowserFetcher(new BrowserFetcherOptions { Path = ChromiumCacheDir });
            await fetcher.DownloadAsync();
            progress?.Report("Browser engine ready.");
        }

        public static bool IsGoldbergApplied(string gameDir)
        {
            try
            {
                return Directory.GetFiles(gameDir, "OG_*.dll", SearchOption.AllDirectories).Any()
                    || Directory.GetFiles(gameDir, "OG_*.so", SearchOption.AllDirectories).Any();
            }
            catch { return false; }
        }

        public static void RemoveGoldberg(string gameDir)
        {
            var ogFiles = new List<string>();
            try { ogFiles.AddRange(Directory.GetFiles(gameDir, "OG_*.dll", SearchOption.AllDirectories)); } catch { }
            try { ogFiles.AddRange(Directory.GetFiles(gameDir, "OG_*.so", SearchOption.AllDirectories)); } catch { }

            foreach (var og in ogFiles)
            {
                var dir = Path.GetDirectoryName(og)!;
                var orig = Path.Combine(dir, Path.GetFileName(og)[3..]);
                try { File.Copy(og, orig, true); File.Delete(og); } catch { }
            }

            var settingsDir = Path.Combine(gameDir, "steam_settings");
            if (Directory.Exists(settingsDir))
                try { Directory.Delete(settingsDir, true); } catch { }

            string[] artifacts = {
                "steamclient.dll", "steamclient64.dll",
                "steamclient_loader_x32.exe", "steamclient_loader_x64.exe",
                "Launch.bat", "launch.sh"
            };
            foreach (var name in artifacts)
            {
                try
                {
                    foreach (var f in Directory.GetFiles(gameDir, name, SearchOption.AllDirectories))
                        try { File.Delete(f); } catch { }
                }
                catch { }
            }
        }

        public static bool IsOnlineFixApplied(string gameDir) =>
            File.Exists(Path.Combine(gameDir, MarkerFileName));

        public static async Task<(bool Success, string? Error)> ApplyAsync(
            string gameName, string gameDir, string user, string pass,
            IProgress<string>? progress, CancellationToken ct = default)
        {
            if (!IsChromiumInstalled())
            {
                try { await EnsureChromiumAsync(progress, ct); }
                catch (OperationCanceledException) { return (false, "Cancelled."); }
                catch (Exception ex) { return (false, $"Failed to download browser engine: {ex.Message}"); }
            }

            ct.ThrowIfCancellationRequested();

            if (IsGoldbergApplied(gameDir))
            {
                progress?.Report("Removing Goldberg emulator...");
                RemoveGoldberg(gameDir);
            }

            Log($"=== ApplyAsync START: game=\"{gameName}\" dir=\"{gameDir}\" ===");
            string? archiveUrl = null;
            var rawBrowserCookies = new List<(string Name, string Value)>();
            var userAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0.0.0 Safari/537.36";
            string? btnHref = null;

            try
            {
                progress?.Report("Launching browser...");
                var fetcher = new BrowserFetcher(new BrowserFetcherOptions { Path = ChromiumCacheDir });
                var installed = fetcher.GetInstalledBrowsers().FirstOrDefault();
                if (installed == null)
                    return (false, "Browser engine not found after download.");

                await using var browser = await Puppeteer.LaunchAsync(new LaunchOptions
                {
                    Headless = true,
                    ExecutablePath = installed.GetExecutablePath(),
                    Args = new[] { "--no-sandbox", "--disable-gpu", "--disable-dev-shm-usage", "--window-size=1280,800" }
                });

                await using var page = await browser.NewPageAsync();

                try
                {
                    userAgent = await page.EvaluateExpressionAsync<string>("navigator.userAgent");
                }
                catch { }

                // Search
                progress?.Report($"Searching online-fix.me for \"{gameName}\"...");
                var encoded = Uri.EscapeDataString(gameName);
                var searchUrl = $"{OnlineFixBase}/index.php?do=search&subaction=search&story={encoded}";
                Log($"Search URL: {searchUrl}");
                await page.GoToAsync(searchUrl,
                    new NavigationOptions { WaitUntil = new[] { WaitUntilNavigation.DOMContentLoaded }, Timeout = 20000 });

                ct.ThrowIfCancellationRequested();

                // Find best match in search results
                string? bestHref = null;
                double bestRatio = 0;
                try
                {
                    var anchors = await page.EvaluateFunctionAsync<string[][]>(
                        @"() => {
                            const container = document.getElementById('dle-content') || document.body;
                            return Array.from(container.querySelectorAll('a'))
                                .filter(a => a.href && a.href.includes('/games/'))
                                .map(a => [(a.innerText || a.textContent || '').trim(), a.href]);
                        }");
                    if (anchors != null)
                    {
                        Log($"Search results: {anchors.Length} anchor(s) with /games/ href");
                        var lowerGame = gameName.ToLowerInvariant();
                        foreach (var a in anchors)
                        {
                            var text = a[0].ToLowerInvariant();
                            var href = a[1];
                            if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(href)) continue;
                            var ratio = ComputeRatio(lowerGame, text);
                            Log($"  candidate: ratio={ratio:P0} text=\"{text}\" href={href}");
                            if (ratio > bestRatio) { bestRatio = ratio; bestHref = href; }
                        }
                    }
                }
                catch { }

                Log($"Best match: ratio={bestRatio:P0} href={bestHref ?? "(none)"}");
                if (bestHref == null || bestRatio < MatchThreshold)
                    return (false, $"No match found on online-fix.me for \"{gameName}\". Best ratio: {bestRatio:P0}.");

                progress?.Report($"Found match ({bestRatio:P0}). Opening game page...");
                Log($"Navigating to game page: {bestHref}");
                await page.GoToAsync(bestHref,
                    new NavigationOptions { WaitUntil = new[] { WaitUntilNavigation.DOMContentLoaded }, Timeout = 20000 });

                ct.ThrowIfCancellationRequested();

                // Login if form is present
                var loginPresent = false;
                try
                {
                    loginPresent = await page.EvaluateFunctionAsync<bool>(
                        "() => !!document.querySelector(\"[name='login_name']\")");
                }
                catch { }

                Log($"Login form present: {loginPresent}");
                if (loginPresent)
                {
                    progress?.Report("Authenticating...");
                    try
                    {
                        await page.TypeAsync("[name='login_name']", user);
                        await page.TypeAsync("[name='login_password']", pass);
                        await page.Keyboard.PressAsync("Enter");
                        await page.WaitForNavigationAsync(new NavigationOptions
                        {
                            WaitUntil = new[] { WaitUntilNavigation.DOMContentLoaded },
                            Timeout = 15000
                        });
                        Log($"Post-login URL: {page.Url}");
                    }
                    catch { }
                    ct.ThrowIfCancellationRequested();
                }

                // Find download button href
                progress?.Report("Finding download link...");
                try
                {
                    btnHref = await page.EvaluateFunctionAsync<string?>(
                        @"() => {
                            const el = Array.from(document.querySelectorAll('a, button'))
                                .find(e => e.textContent?.includes('Скачать фикс с сервера'));
                            return el ? (el.href || el.getAttribute('href')) : null;
                        }");
                }
                catch { }

                Log($"Download button href: {btnHref ?? "(not found)"}");
                if (string.IsNullOrEmpty(btnHref))
                    return (false, "Could not find the OnlineFix download button on the game page.");

                var uploadsBase = "https://uploads.online-fix.me";
                try
                {
                    var pageCookies = await page.GetCookiesAsync(OnlineFixBase);
                    Log($"Browser cookies for {OnlineFixBase}: {pageCookies.Length} cookie(s): {string.Join(", ", pageCookies.Select(c => c.Name))}");
                    foreach (var c in pageCookies)
                        rawBrowserCookies.Add((c.Name, c.Value));
                }
                catch { }

                // Navigate browser to uploads domain so Cloudflare issues a cf_clearance for that subdomain.
                // The main-site cf_clearance is domain-specific and will not authenticate uploads.online-fix.me:2053.
                progress?.Report("Authenticating with file server...");
                Log($"Navigating browser to uploads domain: {btnHref}");
                try
                {
                    await page.GoToAsync(btnHref, new NavigationOptions
                    {
                        WaitUntil = new[] { WaitUntilNavigation.Networkidle0 },
                        Timeout = 20000
                    });
                    var uploadsCookies = await page.GetCookiesAsync(uploadsBase);
                    Log($"Browser cookies for {uploadsBase}: {uploadsCookies.Length} cookie(s): {string.Join(", ", uploadsCookies.Select(c => c.Name))}");
                    foreach (var c in uploadsCookies)
                    {
                        var idx = rawBrowserCookies.FindIndex(x => x.Name == c.Name);
                        if (idx >= 0) rawBrowserCookies[idx] = (c.Name, c.Value);
                        else rawBrowserCookies.Add((c.Name, c.Value));
                    }
                }
                catch (Exception ex) { Log($"Uploads domain navigation failed: {ex.Message}"); }

                progress?.Report("Connecting to file server...");
                var navHandler = new HttpClientHandler { AllowAutoRedirect = true, UseCookies = false };
                using var navHttp = new HttpClient(navHandler) { Timeout = TimeSpan.FromSeconds(30) };
                navHttp.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", userAgent);
                navHttp.DefaultRequestHeaders.TryAddWithoutValidation("Referer", OnlineFixBase + "/");
                var initCookieHeader = string.Join("; ", rawBrowserCookies.Select(c => $"{c.Name}={c.Value}"));
                if (!string.IsNullOrEmpty(initCookieHeader))
                    navHttp.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", initCookieHeader);

                Log($"HttpClient path: GET {btnHref}");
                for (var attempt = 0; attempt < 3 && archiveUrl == null; attempt++)
                {
                    ct.ThrowIfCancellationRequested();
                    if (attempt > 0) await Task.Delay(3000, ct);
                    try
                    {
                        using var resp = await navHttp.GetAsync(btnHref, HttpCompletionOption.ResponseHeadersRead, ct);
                        var httpFinalUrl = resp.RequestMessage?.RequestUri?.AbsoluteUri ?? btnHref;
                        Log($"  attempt {attempt + 1}: status={(int)resp.StatusCode} finalUrl={httpFinalUrl}");
                        if (!resp.IsSuccessStatusCode) continue;

                        var html = await resp.Content.ReadAsStringAsync(ct);
                        Log($"  response body length={html.Length} chars");

                        var subdirs = Regex.Matches(html, @"href=""([^""]+/)""")
                            .Select(m => m.Groups[1].Value)
                            .Where(s => !s.StartsWith("..") && (
                                s.Contains("fix", StringComparison.OrdinalIgnoreCase) ||
                                s.Contains("repair", StringComparison.OrdinalIgnoreCase)))
                            .ToList();
                        Log($"  subdirs to scan: [{string.Join(", ", subdirs)}]");
                        foreach (var sub in subdirs)
                        {
                            ct.ThrowIfCancellationRequested();
                            try
                            {
                                var subUrl = new Uri(new Uri(httpFinalUrl), sub).AbsoluteUri;
                                Log($"  scanning subdir: {subUrl}");
                                using var subResp = await navHttp.GetAsync(subUrl, ct);
                                Log($"  subdir status={(int)subResp.StatusCode}");
                                MergeSetCookieHeaders(subResp, rawBrowserCookies);
                                var subHtml = await subResp.Content.ReadAsStringAsync(ct);
                                archiveUrl = FindBestArchive(subHtml, subUrl);
                                Log($"  subdir FindBestArchive: {archiveUrl ?? "(null)"}");
                                if (archiveUrl != null) break;
                            }
                            catch { }
                        }

                        if (archiveUrl == null)
                        {
                            archiveUrl = FindBestArchive(html, httpFinalUrl);
                            Log($"  FindBestArchive result: {archiveUrl ?? "(null)"}");
                        }

                        MergeSetCookieHeaders(resp, rawBrowserCookies);
                        Log($"  cookies after listing: [{string.Join(", ", rawBrowserCookies.Select(c => c.Name))}]");
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { Log($"  attempt {attempt + 1} exception: {ex.Message}"); }
                }

                // Fallback: let the browser navigate so it can handle any JS or auth challenges
                if (archiveUrl == null)
                {
                    progress?.Report("Trying browser fallback for file server...");
                    Log($"Browser fallback: navigating to {btnHref}");
                    try
                    {
                        await page.GoToAsync(btnHref, new NavigationOptions
                        {
                            WaitUntil = new[] { WaitUntilNavigation.Networkidle0 },
                            Timeout = 30000
                        });
                        ct.ThrowIfCancellationRequested();

                        var bFinalUrl = page.Url;
                        Log($"Browser fallback landed on: {bFinalUrl}");
                        if (bFinalUrl.Contains("uploads.online-fix.me", StringComparison.OrdinalIgnoreCase))
                        {
                            var bHtml = await page.GetContentAsync();
                            Log($"Browser fallback page body length={bHtml.Length}");
                            try
                            {
                                var uc = await page.GetCookiesAsync(uploadsBase);
                                Log($"Browser uploads cookies: {uc.Length} cookie(s): {string.Join(", ", uc.Select(c => c.Name))}");
                                foreach (var c in uc)
                                {
                                    var idx = rawBrowserCookies.FindIndex(x => x.Name == c.Name);
                                    if (idx >= 0) rawBrowserCookies[idx] = (c.Name, c.Value);
                                    else rawBrowserCookies.Add((c.Name, c.Value));
                                }
                            }
                            catch { }
                            var bSubdirs = Regex.Matches(bHtml, @"href=""([^""]+/)""")
                                .Select(m => m.Groups[1].Value)
                                .Where(s => !s.StartsWith("..") && (
                                    s.Contains("fix", StringComparison.OrdinalIgnoreCase) ||
                                    s.Contains("repair", StringComparison.OrdinalIgnoreCase)));
                            foreach (var sub in bSubdirs)
                            {
                                ct.ThrowIfCancellationRequested();
                                try
                                {
                                    var subUrl = new Uri(new Uri(bFinalUrl), sub).AbsoluteUri;
                                    Log($"Browser fallback scanning subdir: {subUrl}");
                                    await page.GoToAsync(subUrl, new NavigationOptions
                                    {
                                        WaitUntil = new[] { WaitUntilNavigation.DOMContentLoaded },
                                        Timeout = 15000
                                    });
                                    var subHtml = await page.GetContentAsync();
                                    archiveUrl = FindBestArchive(subHtml, subUrl);
                                    Log($"Browser fallback subdir FindBestArchive: {archiveUrl ?? "(null)"}");
                                    if (archiveUrl != null) break;
                                }
                                catch { }
                            }

                            if (archiveUrl == null)
                            {
                                archiveUrl = FindBestArchive(bHtml, bFinalUrl);
                                Log($"Browser fallback FindBestArchive: {archiveUrl ?? "(null)"}");
                            }
                        }
                        else
                        {
                            Log("Browser fallback: did not land on uploads.online-fix.me, skipping HTML parse");
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { Log($"Browser fallback exception: {ex.Message}"); }
                }

            }
            catch (OperationCanceledException) { return (false, "Cancelled."); }
            catch (Exception ex) { return (false, $"Browser navigation failed: {ex.Message}"); }

            Log($"downloadUrl (archive): {archiveUrl ?? "(null)"}");
            if (string.IsNullOrEmpty(archiveUrl))
                return (false, "No .rar/.zip/.7z archive found on the file server.");

            ct.ThrowIfCancellationRequested();

            // Download archive — separate client so Timeout can be set freely
            var tempFile = Path.Combine(Path.GetTempPath(), $"onlinefix_{Guid.NewGuid():N}.rar");
            try
            {
                progress?.Report("Downloading fix archive...");
                var dlHandler = new HttpClientHandler { AllowAutoRedirect = true, UseCookies = false };
                using var dlHttp = new HttpClient(dlHandler) { Timeout = Timeout.InfiniteTimeSpan };
                dlHttp.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", userAgent);
                dlHttp.DefaultRequestHeaders.TryAddWithoutValidation("Referer", btnHref);
                var cookieHeader = string.Join("; ", rawBrowserCookies.Select(c => $"{c.Name}={c.Value}"));
                if (!string.IsNullOrEmpty(cookieHeader))
                    dlHttp.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", cookieHeader);
                Log($"dl Cookie header: {cookieHeader}");

                Log($"Downloading: GET {archiveUrl}");
                HttpResponseMessage? dlResp = null;
                for (var dlAttempt = 0; dlAttempt < 3; dlAttempt++)
                {
                    if (dlAttempt > 0)
                    {
                        progress?.Report($"Retrying download (attempt {dlAttempt + 1}/3)...");
                        await Task.Delay(3000, ct);
                    }
                    dlResp = await dlHttp.GetAsync(archiveUrl, HttpCompletionOption.ResponseHeadersRead, ct);
                    Log($"  dl attempt {dlAttempt + 1}: status={(int)dlResp.StatusCode}");
                    if (dlResp.IsSuccessStatusCode) break;
                    if (dlAttempt == 2)
                    {
                        Log($"  download failed after 3 attempts: {(int)dlResp.StatusCode}");
                        return (false, $"Download failed: HTTP {(int)dlResp.StatusCode}");
                    }
                    dlResp.Dispose();
                    dlResp = null;
                }
                using var _ = dlResp!;

                var total = dlResp!.Content.Headers.ContentLength ?? 0;
                long downloaded = 0;
                await using var fs = File.Create(tempFile);
                await using var stream = await dlResp.Content.ReadAsStreamAsync(ct);
                var buf = new byte[1024 * 1024];
                int read;
                while ((read = await stream.ReadAsync(buf, ct)) > 0)
                {
                    await fs.WriteAsync(buf.AsMemory(0, read), ct);
                    downloaded += read;
                    if (total > 0)
                        progress?.Report($"Downloading fix archive... {downloaded * 100 / total}%");
                }
            }
            catch (OperationCanceledException)
            {
                TryDelete(tempFile);
                return (false, "Cancelled.");
            }
            catch (Exception ex)
            {
                TryDelete(tempFile);
                return (false, $"Download failed: {ex.Message}");
            }

            ct.ThrowIfCancellationRequested();

            // Extract archive
            progress?.Report("Extracting fix...");
            var tempExtractDir = Path.Combine(Path.GetTempPath(), $"onlinefix_{Guid.NewGuid():N}");
            try
            {
                Directory.CreateDirectory(tempExtractDir);
                var extractRoot = Path.GetFullPath(tempExtractDir);
                using var archive = ArchiveFactory.Open(tempFile, new SharpCompress.Readers.ReaderOptions
                {
                    Password = ArchivePassword,
                    LookForHeader = true
                });
                foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
                {
                    var destPath = Path.GetFullPath(Path.Combine(extractRoot, entry.Key ?? ""));
                    if (!destPath.StartsWith(extractRoot, StringComparison.OrdinalIgnoreCase)) continue;
                    entry.WriteToDirectory(extractRoot, new ExtractionOptions
                    {
                        ExtractFullPath = true,
                        Overwrite = true
                    });
                }
            }
            catch (Exception ex)
            {
                TryDelete(tempFile);
                TryDeleteDir(tempExtractDir);
                return (false, $"Extraction failed: {ex.Message}");
            }
            finally
            {
                TryDelete(tempFile);
            }

            ct.ThrowIfCancellationRequested();

            // Copy files to game dir, backing up collisions
            progress?.Report("Applying fix to game folder...");
            try
            {
                var (added, backedUp) = CopyWithBackup(tempExtractDir, gameDir);
                var markerLines = new List<string> { DateTime.UtcNow.ToString("O") };
                markerLines.AddRange(added.Select(r => "+" + r));
                markerLines.AddRange(backedUp.Select(r => "~" + r));
                File.WriteAllLines(Path.Combine(gameDir, MarkerFileName), markerLines);
            }
            catch (Exception ex)
            {
                TryDeleteDir(tempExtractDir);
                return (false, $"Failed to apply files: {ex.Message}");
            }
            finally
            {
                TryDeleteDir(tempExtractDir);
            }

            progress?.Report("OnlineFix applied.");
            return (true, null);
        }

        public static void Revert(string gameDir)
        {
            var markerPath = Path.Combine(gameDir, MarkerFileName);
            try
            {
                var lines = File.ReadAllLines(markerPath);
                foreach (var line in lines.Skip(1))
                {
                    if (string.IsNullOrWhiteSpace(line) || line.Length < 2) continue;
                    var kind = line[0];
                    var rel = line[1..];
                    if (kind == '+')
                    {
                        TryDelete(Path.Combine(gameDir, rel));
                    }
                    else if (kind == '~')
                    {
                        var dst = Path.Combine(gameDir, rel);
                        var bak = dst + ".bak";
                        try { if (File.Exists(bak)) { File.Copy(bak, dst, true); File.Delete(bak); } } catch { }
                    }
                }
            }
            catch { }
            TryDelete(markerPath);
        }

        // ── helpers ──────────────────────────────────────────────────────────────

        private static string? FindBestArchive(string html, string baseUrl)
        {
            string? best = null;
            var bestScore = 0;
            foreach (Match m in Regex.Matches(html, @"href=""([^""]+\.(?:rar|zip|7z))""", RegexOptions.IgnoreCase))
            {
                var href = m.Groups[1].Value;
                var abs = href.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? href
                    : new Uri(new Uri(baseUrl), href).AbsoluteUri;
                var lower = Uri.UnescapeDataString(abs).ToLowerInvariant();
                var score = 0;
                if (lower.Contains("fix")) score += 10;
                if (lower.Contains("repair")) score += 10;
                if (lower.Contains("generic")) score += 5;
                if (score > bestScore) { bestScore = score; best = abs; }
            }
            return best;
        }

        private static (List<string> Added, List<string> BackedUp) CopyWithBackup(string srcDir, string dstDir)
        {
            var added = new List<string>();
            var backedUp = new List<string>();
            foreach (var srcFile in Directory.GetFiles(srcDir, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(srcDir, srcFile);
                var dst = Path.Combine(dstDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
                if (File.Exists(dst))
                {
                    var bak = dst + ".bak";
                    if (!File.Exists(bak))
                        File.Copy(dst, bak, false);
                    backedUp.Add(rel);
                }
                else
                {
                    added.Add(rel);
                }
                File.Copy(srcFile, dst, true);
            }
            return (added, backedUp);
        }

        private static double ComputeRatio(string query, string candidate)
        {
            if (string.IsNullOrEmpty(query) || string.IsNullOrEmpty(candidate)) return 0;

            // Exact match
            if (candidate == query) return 1.0;

            // Query is a prefix of candidate (e.g. "overcooked" in "overcooked! all you can eat по сети")
            // Score scales with how much of the candidate the query covers
            if (candidate.StartsWith(query, StringComparison.Ordinal))
                return 0.8 + 0.2 * ((double)query.Length / candidate.Length);

            // Candidate is a prefix of query (game name is more specific than site title)
            if (query.StartsWith(candidate, StringComparison.Ordinal))
                return 0.8 + 0.2 * ((double)candidate.Length / query.Length);

            // Query appears anywhere in candidate
            if (candidate.Contains(query, StringComparison.Ordinal))
                return 0.6 + 0.2 * ((double)query.Length / candidate.Length);

            // Levenshtein fallback for fuzzy matches
            var dist = LevenshteinDistance(query, candidate);
            var maxLen = Math.Max(query.Length, candidate.Length);
            return maxLen == 0 ? 1.0 : 1.0 - (double)dist / maxLen;
        }

        private static int LevenshteinDistance(string a, string b)
        {
            var d = new int[a.Length + 1, b.Length + 1];
            for (var i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (var j = 0; j <= b.Length; j++) d[0, j] = j;
            for (var i = 1; i <= a.Length; i++)
                for (var j = 1; j <= b.Length; j++)
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            return d[a.Length, b.Length];
        }

        private static void MergeSetCookieHeaders(HttpResponseMessage resp, List<(string Name, string Value)> jar)
        {
            if (!resp.Headers.TryGetValues("Set-Cookie", out var setCookieHeaders)) return;
            foreach (var header in setCookieHeaders)
            {
                // Set-Cookie: name=value; Path=/; ...
                var nameValue = header.Split(';')[0].Trim();
                var eq = nameValue.IndexOf('=');
                if (eq <= 0) continue;
                var name = nameValue[..eq].Trim();
                var value = nameValue[(eq + 1)..].Trim();
                var idx = jar.FindIndex(x => x.Name == name);
                if (idx >= 0) jar[idx] = (name, value);
                else jar.Add((name, value));
            }
        }

        private static void TryDelete(string path) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
        private static void TryDeleteDir(string path) { try { if (Directory.Exists(path)) Directory.Delete(path, true); } catch { } }
    }
}
