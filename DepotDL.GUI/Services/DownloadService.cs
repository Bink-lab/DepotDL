using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DepotDL.GUI.Models;

namespace DepotDL.GUI.Services
{
    public class DownloadService
    {
        private static readonly Regex PctRx = new(@"(\d+(?:[.,]\d+)?)%");
        private static readonly Regex SpeedRx = new(@"\(([^)]+)\)\s*$");

        private const int MaxRetries = 3;
        private const int StuckTimeoutSeconds = 120;

        public string? DotnetPath { get; private set; }
        public string? DDModPath { get; private set; }

        public bool Initialize()
        {
            DotnetPath = ResolveDotnet();
            DDModPath = ResolveDDMod();
            return DotnetPath != null && DDModPath != null;
        }

        public async Task RunDownloadsAsync(
            string appId,
            List<DepotInfo> depots,
            string outputDir,
            string? manifestsDir,
            int maxParallel,
            List<DepotDownloadState> states,
            CancellationToken ct,
            string? ryuuApiKey = null,
            string? hubcapApiKey = null)
        {
            string keysFile = Path.Combine(Path.GetTempPath(), $"depotdl_keys_{Guid.NewGuid():N}.vdf");
            var providerExtractDirs = new System.Collections.Concurrent.ConcurrentBag<string>();
            try
            {
                using (var w = new StreamWriter(keysFile))
                    foreach (var d in depots)
                        if (!string.IsNullOrWhiteSpace(d.DecryptionKey))
                            await w.WriteLineAsync($"{d.DepotId};{d.DecryptionKey}");

                var manifestMap = BuildManifestMap(manifestsDir);
                string checkpointDir = Path.Combine(outputDir, ".depotdl_progress");
                Directory.CreateDirectory(checkpointDir);

                for (int i = 0; i < depots.Count; i++)
                {
                    string doneFile = Path.Combine(checkpointDir, $"{depots[i].DepotId}.done");
                    if (File.Exists(doneFile))
                    {
                        states[i].Status = DepotStatus.Skipped;
                        states[i].StatusText = "Already done";
                        states[i].Percent = 100;
                    }
                }

                var queue = new System.Collections.Concurrent.ConcurrentQueue<(DepotInfo, DepotDownloadState)>();
                for (int i = 0; i < depots.Count; i++)
                    if (states[i].Status != DepotStatus.Skipped)
                        queue.Enqueue((depots[i], states[i]));

                int workers = Math.Min(maxParallel, queue.Count);
                if (workers == 0) return;

                var providerCache = new System.Collections.Concurrent.ConcurrentDictionary<string, Lazy<Task<Dictionary<string, string>?>>>();

                Task<Dictionary<string, string>?> GetProviderManifestsAsync(string providerName, Func<Task<ManifestDownloadResult>> fetch)
                {
                    var lazy = providerCache.GetOrAdd(providerName, _ => new Lazy<Task<Dictionary<string, string>?>>(async () =>
                    {
                        var result = await fetch();
                        if (!result.HasZip || string.IsNullOrEmpty(result.ZipPath))
                        {
                            if (!string.IsNullOrEmpty(result.ZipPath)) try { File.Delete(result.ZipPath); } catch { }
                            return null;
                        }
                        var imported = new ZipImportService().ImportZip(result.ZipPath);
                        try { File.Delete(result.ZipPath); } catch { }
                        if (imported.ManifestCount == 0)
                        {
                            if (!string.IsNullOrEmpty(imported.ImportDir)) try { Directory.Delete(imported.ImportDir, true); } catch { }
                            return null;
                        }
                        if (!string.IsNullOrEmpty(imported.ImportDir)) providerExtractDirs.Add(imported.ImportDir);
                        return BuildManifestMap(imported.ManifestsDir);
                    }));
                    return lazy.Value;
                }

                var semaphore = new SemaphoreSlim(workers, workers);
                var tasks = new List<Task>();

                while (!queue.IsEmpty)
                {
                    ct.ThrowIfCancellationRequested();
                    if (!queue.TryDequeue(out var item)) break;

                    await semaphore.WaitAsync(ct);
                    var (depot, state) = item;
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            await DownloadDepotWithRetryAsync(
                                appId, depot, outputDir, keysFile, manifestMap, state, ct,
                                GetProviderManifestsAsync, ryuuApiKey, hubcapApiKey);

                            if (state.Status == DepotStatus.Done)
                                await File.WriteAllTextAsync(
                                    Path.Combine(checkpointDir, $"{depot.DepotId}.done"), "");
                        }
                        finally { semaphore.Release(); }
                    }, ct));
                }

                await Task.WhenAll(tasks);

                bool allDone = true;
                foreach (var s in states)
                    if (s.Status != DepotStatus.Done && s.Status != DepotStatus.Skipped)
                    { allDone = false; break; }
                if (allDone)
                    try { Directory.Delete(checkpointDir, true); } catch { }
            }
            finally
            {
                try { File.Delete(keysFile); } catch { }
                foreach (var dir in providerExtractDirs)
                    try { Directory.Delete(dir, true); } catch { }
                CleanupDepotDownloaderFolders(outputDir);
            }
        }

        private void CleanupDepotDownloaderFolders(string outputDir)
        {
            var candidates = new List<string>
            {
                Path.Combine(outputDir, ".DepotDownloader"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".DepotDownloader"),
            };

            if (!string.IsNullOrEmpty(DDModPath))
            {
                string? ddmodDir = Path.GetDirectoryName(DDModPath);
                if (!string.IsNullOrEmpty(ddmodDir))
                    candidates.Add(Path.Combine(ddmodDir, ".DepotDownloader"));
            }

            foreach (var path in candidates)
            {
                if (!Directory.Exists(path)) continue;
                try { Directory.Delete(path, true); }
                catch (Exception ex) { Debug.WriteLine($"[DownloadService] cleanup {path} failed: {ex.Message}"); }
            }
        }

        private async Task DownloadDepotWithRetryAsync(
            string appId, DepotInfo depot, string outputDir,
            string keysFile, Dictionary<string, string> manifestMap,
            DepotDownloadState state, CancellationToken ct,
            Func<string, Func<Task<ManifestDownloadResult>>, Task<Dictionary<string, string>?>> getProviderManifests,
            string? ryuuApiKey = null, string? hubcapApiKey = null)
        {
            for (int attempt = 1; attempt <= MaxRetries; attempt++)
            {
                if (attempt > 1)
                {
                    state.StatusText = $"Retrying ({attempt - 1}/{MaxRetries - 1})...";
                    state.Status = DepotStatus.Queued;
                    await Task.Delay(2000 * (attempt - 1), ct);
                }

                bool success = await DownloadDepotAsync(
                    appId, depot, outputDir, keysFile, manifestMap, state, ct);

                if (success) return;
                if (ct.IsCancellationRequested) return;
                if (attempt == MaxRetries)
                {
                    state.Status = DepotStatus.Failed;
                    state.StatusText = "Failed";
                }
            }

            if (state.Status == DepotStatus.Failed && (ryuuApiKey != null || hubcapApiKey != null))
            {
                var providers = new List<(string name, Func<Task<ManifestDownloadResult>> fetch)>();
                if (ryuuApiKey != null)
                    providers.Add(("Ryuu", () => new RyuuService().DownloadPackageAsync(appId, ryuuApiKey)));
                if (hubcapApiKey != null)
                    providers.Add(("Hubcap", () => new HubcapService().DownloadPackageAsync(appId, hubcapApiKey)));

                foreach (var (providerName, fetchFunc) in providers)
                {
                    if (state.Status == DepotStatus.Done) break;
                    if (ct.IsCancellationRequested)
                    {
                        state.Status = DepotStatus.Cancelled;
                        state.StatusText = "Cancelled";
                        return;
                    }

                    state.StatusText = $"Trying {providerName}...";
                    state.Status = DepotStatus.Connecting;

                    Dictionary<string, string>? providerMap;
                    try { providerMap = await getProviderManifests(providerName, fetchFunc); }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[DownloadService] {providerName} fetch failed for depot {depot.DepotId}: {ex.Message}");
                        continue;
                    }
                    if (providerMap == null) continue;

                    string? providerManifestFile = null;
                    if (!string.IsNullOrEmpty(depot.ManifestId))
                    {
                        providerMap.TryGetValue($"{depot.DepotId}_{depot.ManifestId}", out providerManifestFile);
                        if (providerManifestFile == null)
                            providerMap.TryGetValue(depot.ManifestId, out providerManifestFile);
                    }
                    if (providerManifestFile == null)
                        providerMap.TryGetValue(depot.DepotId, out providerManifestFile);

                    if (providerManifestFile == null) continue;

                    bool success = await DownloadDepotAsync(
                        appId, depot, outputDir, keysFile, manifestMap, state, ct, providerManifestFile);

                    if (success) return;
                }

                if (state.Status != DepotStatus.Done)
                {
                    state.Status = DepotStatus.Failed;
                    state.StatusText = "Failed";
                }
            }
        }

        private async Task<bool> DownloadDepotAsync(
            string appId, DepotInfo depot, string outputDir,
            string keysFile, Dictionary<string, string> manifestMap,
            DepotDownloadState state, CancellationToken ct,
            string? manifestOverridePath = null)
        {
            state.Status = DepotStatus.Connecting;
            state.StatusText = "Connecting...";
            state.SpeedText = string.Empty;

            var psi = new ProcessStartInfo
            {
                FileName = DotnetPath!,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(DDModPath)!
            };

            psi.ArgumentList.Add(DDModPath!);
            psi.ArgumentList.Add("-app"); psi.ArgumentList.Add(appId);
            psi.ArgumentList.Add("-depot"); psi.ArgumentList.Add(depot.DepotId);
            psi.ArgumentList.Add("-depotkeys"); psi.ArgumentList.Add(keysFile);
            psi.ArgumentList.Add("-max-downloads"); psi.ArgumentList.Add("64");
            psi.ArgumentList.Add("-os"); psi.ArgumentList.Add("windows");
            psi.ArgumentList.Add("-validate");
            psi.ArgumentList.Add("-dir"); psi.ArgumentList.Add(outputDir);

            if (!string.IsNullOrWhiteSpace(depot.ManifestId))
            {
                psi.ArgumentList.Add("-manifest");
                psi.ArgumentList.Add(depot.ManifestId);

                string? mf = manifestOverridePath;
                if (mf == null)
                {
                    string keyCombo = $"{depot.DepotId}_{depot.ManifestId}";
                    if (manifestMap.TryGetValue(keyCombo, out var mf1))
                        mf = mf1;
                    else if (manifestMap.TryGetValue(depot.ManifestId, out var mf2))
                        mf = mf2;
                }
                if (mf != null)
                { psi.ArgumentList.Add("-manifestfile"); psi.ArgumentList.Add(mf); }
            }
            else if (manifestOverridePath != null)
            {
                psi.ArgumentList.Add("-manifestfile");
                psi.ArgumentList.Add(manifestOverridePath);
            }

            var errors = new System.Collections.Concurrent.ConcurrentBag<string>();
            bool hadFailureSignal = false;
            bool killedByWatchdog = false;

            using var proc = new Process { StartInfo = psi };

            proc.OutputDataReceived += (_, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.Data)) return;
                if (IsFailureLine(e.Data)) { hadFailureSignal = true; errors.Add(e.Data); }
                ProcessLine(e.Data, state);
            };
            proc.ErrorDataReceived += (_, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.Data)) return;
                if (IsFailureLine(e.Data)) { hadFailureSignal = true; errors.Add(e.Data); }
            };

            proc.Start();
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();

            // Watchdog: poll state.Percent and state.Status every 15 s.
            // Reset the timer only when real progress is made (percent advances or
            // status changes). This fires even when the process floods logs but
            // the download itself is frozen.
            using var watchdogCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var watchdog = Task.Run(async () =>
            {
                double seenPct = -1;
                DepotStatus seenStatus = DepotStatus.Queued;
                long lastProgressTicks = DateTime.UtcNow.Ticks;

                while (!watchdogCts.Token.IsCancellationRequested)
                {
                    try { await Task.Delay(15_000, watchdogCts.Token); }
                    catch (OperationCanceledException) { break; }

                    double nowPct = state.Percent;
                    DepotStatus nowStatus = state.Status;

                    if (nowPct > seenPct || nowStatus != seenStatus)
                    {
                        seenPct = nowPct;
                        seenStatus = nowStatus;
                        lastProgressTicks = DateTime.UtcNow.Ticks;
                    }
                    else
                    {
                        var stale = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - lastProgressTicks);
                        if (stale.TotalSeconds > StuckTimeoutSeconds)
                        {
                            killedByWatchdog = true;
                            state.StatusText = "Stuck — killing...";
                            try { proc.Kill(true); } catch { }
                            break;
                        }
                    }
                }
            });

            try { await proc.WaitForExitAsync(ct); }
            catch (OperationCanceledException)
            {
                try { proc.Kill(true); } catch { }
                state.Status = DepotStatus.Cancelled;
                state.StatusText = "Cancelled";
                throw;
            }
            finally
            {
                watchdogCts.Cancel();
                try { await watchdog; } catch { }
            }

            if (killedByWatchdog)
            {
                state.StatusText = "No progress — retrying...";
                return false;
            }

            if (proc.ExitCode == 0 && !hadFailureSignal)
            {
                state.Status = DepotStatus.Done;
                state.StatusText = "Complete";
                state.Percent = 100;
                state.SpeedText = string.Empty;
                return true;
            }
            else
            {
                state.ErrorMessage = errors.IsEmpty ? $"Exit {proc.ExitCode}" : string.Join("; ", errors);
                return false;
            }
        }

        private static bool IsFailureLine(string line) =>
            line.Contains("AccessDenied", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("No valid depot key", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("unable to download", StringComparison.OrdinalIgnoreCase) ||
            line.Contains("missing public subsection", StringComparison.OrdinalIgnoreCase);

        private static void ProcessLine(string line, DepotDownloadState state)
        {
            if (line.StartsWith("Connecting to Steam3", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Logging anonymously", StringComparison.OrdinalIgnoreCase))
            {
                state.Status = DepotStatus.Connecting;
                state.StatusText = "Connecting...";
                return;
            }
            if (line.StartsWith("Pre-allocating", StringComparison.OrdinalIgnoreCase))
            {
                state.Status = DepotStatus.PreAllocating;
                state.StatusText = "Pre-allocating...";
                return;
            }
            if (line.StartsWith("Validating ", StringComparison.OrdinalIgnoreCase))
            {
                state.Status = DepotStatus.Validating;
                state.StatusText = "Validating";
                state.ActiveFile = Path.GetFileName(line[11..].Trim());
                return;
            }

            var pctMatch = PctRx.Match(line);
            if (pctMatch.Success &&
                double.TryParse(pctMatch.Groups[1].Value.Replace(',', '.'),
                    NumberStyles.Any, CultureInfo.InvariantCulture, out double pct))
            {
                state.Percent = pct;
                state.Status = DepotStatus.Downloading;
                state.StatusText = "Downloading";
                var sm = SpeedRx.Match(line);
                state.SpeedText = sm.Success ? sm.Groups[1].Value : string.Empty;

                // Line format: "  5.40% path/to/file.dll (speed)"
                int fileStart = pctMatch.Index + pctMatch.Length + 1;
                int fileEnd = sm.Success ? sm.Index : line.Length;
                if (fileStart < fileEnd)
                {
                    string filePath = line[fileStart..fileEnd].Trim();
                    if (!string.IsNullOrEmpty(filePath))
                        state.ActiveFile = Path.GetFileName(filePath);
                }
            }
        }

        private static Dictionary<string, string> BuildManifestMap(string? dir)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return map;
            foreach (var f in Directory.GetFiles(dir, "*.manifest"))
            {
                string n = Path.GetFileNameWithoutExtension(f);
                var parts = n.Split('_');
                if (parts.Length >= 2) { map[$"{parts[0]}_{parts[1]}"] = f; map[parts[0]] = f; }
                else map[n] = f;
            }
            return map;
        }

        private static string? ResolveDotnet()
        {
            try
            {
                var p = new Process { StartInfo = new ProcessStartInfo
                    { FileName = "dotnet", Arguments = "--list-runtimes",
                      UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true } };
                p.Start();
                string o = p.StandardOutput.ReadToEnd(); p.WaitForExit();
                if (o.Contains("Microsoft.NETCore.App 9.")) return "dotnet";
            }
            catch { }
            var local = Path.Combine(
                Environment.GetEnvironmentVariable("LOCALAPPDATA") ??
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Local"),
                "Microsoft", "dotnet", "dotnet.exe");
            return File.Exists(local) ? local : null;
        }

        private static string? ResolveDDMod()
        {
            string b = AppDomain.CurrentDomain.BaseDirectory;
            string[] c = {
                Path.Combine(b, "DepotDownloaderMod.dll"),
            };
            foreach (var p in c) if (File.Exists(p)) return Path.GetFullPath(p);
            return null;
        }
    }
}
