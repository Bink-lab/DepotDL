using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace DepotDL.CLI
{
    public class DepotSlotState
    {
        public string? DepotId { get; set; }
        public string Status { get; set; } = "Idle";
        public double? Percent { get; set; }
        public string? ActiveValidationFile { get; set; }
        public string? OutputPath { get; set; }

        public long TotalUncompressedSize { get; set; } = 0;
        public long LastSpeedTotalBytes { get; set; } = 0;
        public Dictionary<string, long> FileSizes { get; } = new(StringComparer.OrdinalIgnoreCase);
        public DateTime? DownloadStartTime { get; set; }
        public DateTime LastSpeedUpdateTime { get; set; } = DateTime.MinValue;
        public double LastPercent { get; set; } = 0;
        public double CurrentSpeedBps { get; set; } = 0;
        public string? SpeedOverrideString { get; set; }
    }

    public class Program
    {
        private static string? _tempKeysPath;

        private static DepotSlotState[] _slots = Array.Empty<DepotSlotState>();
        private static readonly ConcurrentDictionary<string, bool> _depotResultLog = new();
        private static readonly object _drawLock = new object();
        private static readonly Queue<Action> _pendingLogs = new Queue<Action>();
        private static bool _isTty = false;
        private static DateTime _lastDrawTime = DateTime.MinValue;
        private static readonly TimeSpan _drawThrottleInterval = TimeSpan.FromMilliseconds(100);
        private static int[] _lastSlotLengths = Array.Empty<int>();

        static int Main(string[] args)
        {
            Console.Title = "DepotDL.CLI - Steam Depot Downloader Orchestrator";

            if (args.Length == 0)
            {
                // Resolve runtimes
                var dotnetPath = DialogHelpers.ResolveDotnetPath(null);
                var ddmodPath = DialogHelpers.ResolveDDModPath(null);

                if (dotnetPath == null)
                {
                    Console.Clear();
                    WriteColored("[Error] .NET 9 SDK or Runtime could not be found.", ConsoleColor.Red);
                    WriteColored("Please install the .NET 9 runtime to run this tool.", ConsoleColor.Red);
                    Console.WriteLine("\nPress any key to exit.");
                    Console.ReadKey();
                    return 1;
                }

                if (ddmodPath == null || !File.Exists(ddmodPath))
                {
                    Console.Clear();
                    WriteColored("[Error] DepotDownloaderMod.dll not found in default search locations.", ConsoleColor.Red);
                    WriteColored("Please ensure DepotDownloaderMod.dll is in SFF/third_party/DDMod/ or adjacent to this tool.", ConsoleColor.Red);
                    Console.WriteLine("\nPress any key to exit.");
                    Console.ReadKey();
                    return 1;
                }

                return TuiDashboard.RunInteractiveTui(ddmodPath, dotnetPath);
            }

            // Standard CLI Mode
            return RunCliMode(args);
        }

        private static int RunCliMode(string[] args)
        {
            void LogError(string message) => WriteColored(message, ConsoleColor.Red);

            string? luaPath = null;
            string? manifestsDir = null;
            string? outputPath = null;
            string? ddmodPath = null;
            string? dotnetPath = null;
            int maxDownloads = DepotDownloadDefaults.MaxDownloads;
            int maxParallelDepots = 2;
            bool showHelp = false;

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--lua":
                    case "-l":
                        if (i + 1 < args.Length) luaPath = args[++i];
                        break;
                    case "--manifests-dir":
                    case "-m":
                        if (i + 1 < args.Length) manifestsDir = args[++i];
                        break;
                    case "--output":
                    case "-o":
                        if (i + 1 < args.Length) outputPath = args[++i];
                        break;
                    case "--ddmod":
                    case "-d":
                        if (i + 1 < args.Length) ddmodPath = args[++i];
                        break;
                    case "--dotnet":
                    case "-n":
                        if (i + 1 < args.Length) dotnetPath = args[++i];
                        break;
                    case "--max-downloads":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out var parsedMaxDownloads))
                        {
                            maxDownloads = DepotDownloadDefaults.NormalizeMaxDownloads(parsedMaxDownloads);
                        }
                        break;
                    case "--max-parallel-depots":
                    case "-p":
                        if (i + 1 < args.Length && int.TryParse(args[++i], out var parsedMaxParallel))
                        {
                            maxParallelDepots = Math.Clamp(parsedMaxParallel, 1, 8);
                        }
                        break;
                    case "--help":
                    case "-h":
                        showHelp = true;
                        break;
                }
            }

            if (showHelp || string.IsNullOrEmpty(luaPath))
            {
                PrintUsage();
                return showHelp ? 0 : 1;
            }

            if (!File.Exists(luaPath))
            {
                LogError($"[Error] Lua file not found: {luaPath}");
                return 1;
            }

            // Resolve runtimes and libraries
            dotnetPath = DialogHelpers.ResolveDotnetPath(dotnetPath);
            if (dotnetPath == null)
            {
                LogError("[Error] .NET 9 SDK or Runtime could not be found.");
                LogError("Please install .NET 9 or specify the path to dotnet executable using '--dotnet <path>'.");
                return 1;
            }

            ddmodPath = DialogHelpers.ResolveDDModPath(ddmodPath);
            if (ddmodPath == null || !File.Exists(ddmodPath))
            {
                LogError($"[Error] DepotDownloaderMod.dll not found.");
                LogError("Please specify the path to DepotDownloaderMod.dll using '--ddmod <path>'.");
                return 1;
            }

            return ProcessDownload(luaPath, manifestsDir, outputPath, ddmodPath, dotnetPath, maxDownloads: maxDownloads, maxParallelDepots: maxParallelDepots);
        }

        public static int TriggerDownloadProcess(string luaPath, string? manifestsDir, string? outputPath, string ddmodPath, string dotnetPath, List<DepotInfo>? selectedDepots, int maxParallelDepots = 2)
        {
            return ProcessDownload(luaPath, manifestsDir, outputPath, ddmodPath, dotnetPath, selectedDepots, maxParallelDepots: maxParallelDepots);
        }

        private static int ProcessDownload(string luaPath, string? manifestsDir, string? outputPath, string ddmodPath, string dotnetPath, List<DepotInfo>? selectedDepots = null, int maxDownloads = DepotDownloadDefaults.MaxDownloads, int maxParallelDepots = 2)
        {
            void LogError(string message) => WriteColored(message, ConsoleColor.Red);

            bool initialCursorVisible = true;
            try { if (OperatingSystem.IsWindows()) { initialCursorVisible = Console.CursorVisible; Console.CursorVisible = false; } } catch {}

            // Register console cancel handlers for safe VDF cleanup
            AppDomain.CurrentDomain.ProcessExit += (s, e) => SafeCleanupKeys();
            Console.CancelKeyPress += (s, e) => SafeCleanupKeys();

            try
            {
                string luaContent = File.ReadAllText(luaPath);

                string appId;
                var allParsedDepots = LibraryManager.ParseLuaConfig(luaContent, out appId);

                if (string.IsNullOrEmpty(appId))
                {
                    LogError("[Error] Could not find Steam AppID in Lua file.");
                    return 1;
                }

                var depots = new Dictionary<string, DepotInfo>();
                if (selectedDepots != null)
                {
                    foreach (var sel in selectedDepots)
                    {
                        if (allParsedDepots.TryGetValue(sel.DepotId, out var parsedDepot))
                        {
                            var finalDepot = new DepotInfo
                            {
                                DepotId = sel.DepotId,
                                Name = !string.IsNullOrEmpty(sel.Name) ? sel.Name : parsedDepot.Name,
                                OsList = !string.IsNullOrEmpty(sel.OsList) ? sel.OsList : parsedDepot.OsList,
                                OsArch = !string.IsNullOrEmpty(sel.OsArch) ? sel.OsArch : parsedDepot.OsArch
                            };
                            finalDepot.DecryptionKey = !string.IsNullOrEmpty(sel.DecryptionKey) ? sel.DecryptionKey : parsedDepot.DecryptionKey;
                            finalDepot.ManifestId = !string.IsNullOrEmpty(sel.ManifestId) ? sel.ManifestId : parsedDepot.ManifestId;
                            depots[sel.DepotId] = finalDepot;
                        }
                        else
                        {
                            depots[sel.DepotId] = sel;
                        }
                    }
                }
                else
                {
                    depots = allParsedDepots;
                }

                depots = LibraryManager.FilterDownloadableDepots(depots, appId);

                if (depots.Count == 0)
                {
                    LogError("[Error] No downloadable depots with decryption keys found.");
                    return 1;
                }

                if (string.IsNullOrEmpty(outputPath))
                {
                    outputPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "downloads", $"App_{appId}");
                }
                outputPath = Path.GetFullPath(outputPath);

                var completedDepots = LoadCompletedDepots(outputPath);
                int skippedCount = 0;
                if (completedDepots.Count > 0)
                {
                    var toSkip = depots.Keys.Where(k => completedDepots.Contains(k)).ToList();
                    foreach (var k in toSkip)
                    {
                        depots.Remove(k);
                        skippedCount++;
                    }
                }

                if (depots.Count == 0 && skippedCount > 0)
                {
                    DownloadTui.LeftPad = TuiDashboard.GetCenterLeftPad(80);
                    WriteColored($"[Resume] All {skippedCount} depot(s) already complete. Nothing to download.", ConsoleColor.Green);
                    return 0;
                }

                var manifestFiles = new Dictionary<string, string>();
                string? manifestScanStatus = null;
                if (!string.IsNullOrEmpty(manifestsDir) && Directory.Exists(manifestsDir))
                {
                    var files = Directory.GetFiles(manifestsDir, "*.manifest");
                    foreach (var file in files)
                    {
                        var name = Path.GetFileNameWithoutExtension(file);
                        var parts = name.Split('_');
                        if (parts.Length >= 2)
                        {
                            var depotId = parts[0];
                            var manifestId = parts[1];
                            manifestFiles[$"{depotId}_{manifestId}"] = file;
                        }
                        else
                        {
                            manifestFiles[name] = file;
                        }
                    }
                    manifestScanStatus = $"{files.Length} local manifests in {TuiText.ShortenTail(manifestsDir, 45)}";
                }

                var tempKeysContent = new List<string>();
                foreach (var depot in depots.Values)
                {
                    if (!string.IsNullOrEmpty(depot.DecryptionKey))
                    {
                        tempKeysContent.Add($"{depot.DepotId};{depot.DecryptionKey}");
                    }
                }

                _tempKeysPath = Path.Combine(Path.GetTempPath(), $"depotdl_keys_{Guid.NewGuid():N}.vdf");
                File.WriteAllLines(_tempKeysPath, tempKeysContent);

                Directory.CreateDirectory(outputPath);
                DownloadTui.LeftPad = TuiDashboard.GetCenterLeftPad(80);
                DownloadTui.WriteHeader(appId, depots.Count, outputPath);
                if (skippedCount > 0)
                {
                    DownloadTui.WriteSetup("Resumed", $"{skippedCount} already-complete depot(s) skipped", ConsoleColor.DarkGreen);
                }
                if (manifestScanStatus != null)
                {
                    DownloadTui.WriteSetup("Manifest Cache", manifestScanStatus, ConsoleColor.Gray);
                }
                DownloadTui.WriteSetup("Keys File", Path.GetFileName(_tempKeysPath), ConsoleColor.DarkGray);

                _isTty = !Console.IsOutputRedirected;
                _depotResultLog.Clear();

                var depotQueue = new ConcurrentQueue<DepotInfo>(depots.Values);
                int totalDepots = depots.Count;
                int successfulDepots = 0;
                bool hadErrors = false;
                var allOkLock = new object();

                int numWorkers = Math.Clamp(maxParallelDepots, 1, Math.Min(8, totalDepots));

                lock (_drawLock)
                {
                    _slots = new DepotSlotState[numWorkers];
                    _lastSlotLengths = new int[numWorkers];
                    for (int s = 0; s < numWorkers; s++)
                    {
                        _slots[s] = new DepotSlotState();
                    }
                }

                if (_isTty)
                {
                    for (int s = 0; s < numWorkers; s++)
                    {
                        Console.WriteLine();
                    }
                }

                var workerTasks = new List<Task>();

                for (int i = 0; i < numWorkers; i++)
                {
                    int slotId = i;
                    workerTasks.Add(Task.Run(() =>
                    {
                        while (depotQueue.TryDequeue(out var depot))
                        {
                            lock (_drawLock)
                            {
                                var s = _slots[slotId];
                                s.DepotId = depot.DepotId;
                                s.Status = "Initializing...";
                                s.Percent = null;
                                s.ActiveValidationFile = null;
                                s.OutputPath = outputPath;
                                s.TotalUncompressedSize = 0;
                                s.LastSpeedTotalBytes = 0;
                                s.FileSizes.Clear();
                                s.DownloadStartTime = null;
                                s.LastSpeedUpdateTime = DateTime.MinValue;
                                s.LastPercent = 0;
                                s.CurrentSpeedBps = 0;
                                s.SpeedOverrideString = null;
                            }
                            DrawSlots(force: true);

                            bool depotOk = false;

                            var argsList = new List<string>
                            {
                                ddmodPath,
                                "-app", appId,
                                "-depot", depot.DepotId,
                                "-depotkeys", _tempKeysPath,
                                "-max-downloads", DepotDownloadDefaults.NormalizeMaxDownloads(maxDownloads).ToString(CultureInfo.InvariantCulture),
                                "-os", "windows",
                                "-validate",
                                "-dir", outputPath
                            };

                            if (!string.IsNullOrEmpty(depot.ManifestId))
                            {
                                argsList.Add("-manifest");
                                argsList.Add(depot.ManifestId);

                                var keyCombo = $"{depot.DepotId}_{depot.ManifestId}";
                                if (manifestFiles.TryGetValue(keyCombo, out var manifestPath))
                                {
                                    argsList.Add("-manifestfile");
                                    argsList.Add(manifestPath);
                                    lock (_drawLock)
                                    {
                                        _pendingLogs.Enqueue(() => DownloadTui.WriteStatus("Manifest", $"Using local file {Path.GetFileName(manifestPath)}", ConsoleColor.Green));
                                    }
                                }
                                else if (manifestFiles.TryGetValue(depot.ManifestId, out var manifestPathById))
                                {
                                    argsList.Add("-manifestfile");
                                    argsList.Add(manifestPathById);
                                    lock (_drawLock)
                                    {
                                        _pendingLogs.Enqueue(() => DownloadTui.WriteStatus("Manifest", $"Using local file {Path.GetFileName(manifestPathById)}", ConsoleColor.Green));
                                    }
                                }
                                else
                                {
                                    lock (_drawLock)
                                    {
                                        _pendingLogs.Enqueue(() => DownloadTui.WriteStatus("Manifest", "No local match; DepotDownloaderMod will fetch it", ConsoleColor.Yellow));
                                    }
                                }
                            }

                            int maxRetries = 3;
                            int retryCount = 0;
                            while (retryCount < maxRetries)
                            {
                                if (retryCount > 0)
                                {
                                    lock (_drawLock)
                                    {
                                        _slots[slotId].Status = $"Retrying ({retryCount}/{maxRetries - 1})...";
                                        _slots[slotId].Percent = null;
                                        _slots[slotId].ActiveValidationFile = null;
                                    }
                                    DrawSlots(force: true);
                                    System.Threading.Thread.Sleep(2000 * retryCount);
                                }

                                var psi = new ProcessStartInfo
                                {
                                    FileName = dotnetPath,
                                    UseShellExecute = false,
                                    RedirectStandardOutput = true,
                                    RedirectStandardError = true,
                                    CreateNoWindow = true,
                                    WorkingDirectory = Path.GetDirectoryName(ddmodPath) ?? AppDomain.CurrentDomain.BaseDirectory
                                };
                                foreach (var arg in argsList)
                                {
                                    psi.ArgumentList.Add(arg);
                                }

                                var depotOutputErrors = new List<string>();
                                string? lastOutputLine = null;

                                using (var process = new Process { StartInfo = psi })
                                {
                                    process.OutputDataReceived += (sender, lineEventArgs) =>
                                    {
                                        if (!string.IsNullOrWhiteSpace(lineEventArgs.Data))
                                        {
                                            lastOutputLine = lineEventArgs.Data;
                                        }
                                        if (IsDepotDownloadFailure(lineEventArgs.Data))
                                        {
                                            lock (depotOutputErrors)
                                            {
                                                depotOutputErrors.Add(lineEventArgs.Data!);
                                            }
                                        }
                                        ProcessProgressLine(slotId, lineEventArgs.Data);
                                    };
                                    process.ErrorDataReceived += (sender, lineEventArgs) =>
                                    {
                                        if (!string.IsNullOrEmpty(lineEventArgs.Data))
                                        {
                                            lastOutputLine = lineEventArgs.Data;
                                            if (IsDepotDownloadFailure(lineEventArgs.Data))
                                            {
                                                lock (depotOutputErrors)
                                                {
                                                    depotOutputErrors.Add(lineEventArgs.Data);
                                                }
                                            }
                                        }
                                    };

                                    process.Start();
                                    process.BeginOutputReadLine();
                                    process.BeginErrorReadLine();
                                    process.WaitForExit();

                                    if (process.ExitCode != 0 || depotOutputErrors.Count > 0)
                                    {
                                        depotOk = false;
                                        string reason;
                                        if (depotOutputErrors.Count > 0)
                                        {
                                            reason = depotOutputErrors[depotOutputErrors.Count - 1];
                                        }
                                        else if (!string.IsNullOrWhiteSpace(lastOutputLine))
                                        {
                                            var msgPart = lastOutputLine;
                                            int nlIdx = msgPart.IndexOf('\n');
                                            if (nlIdx > 0) msgPart = msgPart.Substring(0, nlIdx);
                                            reason = TuiText.Shorten($"{msgPart} (exit code {process.ExitCode})", 60);
                                        }
                                        else
                                        {
                                            reason = $"DepotDownloaderMod exited with code {process.ExitCode}";
                                        }

                                        retryCount++;
                                        if (retryCount < maxRetries)
                                        {
                                            string depotId = depot.DepotId;
                                            lock (_drawLock)
                                            {
                                                _pendingLogs.Enqueue(() => DownloadTui.WriteStatus("Retry", $"Depot {depotId} failed (attempt {retryCount}/{maxRetries}). Error: {reason}. Retrying...", ConsoleColor.Yellow));
                                            }
                                            DrawSlots(force: true);
                                            continue;
                                        }

                                        string finalDepotId = depot.DepotId;
                                        lock (_drawLock)
                                        {
                                            _pendingLogs.Enqueue(() => DownloadTui.WriteStatus("Failed", $"Depot {finalDepotId}: {reason} (after {maxRetries} attempts)", ConsoleColor.Red));
                                        }
                                        break;
                                    }
                                    else
                                    {
                                        depotOk = true;
                                        string depotId = depot.DepotId;
                                        MarkDepotComplete(outputPath, depotId);
                                        lock (_drawLock)
                                        {
                                            _pendingLogs.Enqueue(() => DownloadTui.WriteStatus("Complete", $"Depot {depotId} downloaded successfully", ConsoleColor.Green));
                                        }
                                        break;
                                    }
                                }
                            }

                            lock (allOkLock)
                            {
                                _depotResultLog[depot.DepotId] = depotOk;
                                if (depotOk)
                                {
                                    successfulDepots++;
                                }
                                else
                                {
                                    hadErrors = true;
                                }
                            }

                            lock (_drawLock)
                            {
                                var s = _slots[slotId];
                                s.DepotId = null;
                                s.Status = "Idle";
                                s.Percent = null;
                                s.ActiveValidationFile = null;
                                s.OutputPath = null;
                                s.TotalUncompressedSize = 0;
                                s.LastSpeedTotalBytes = 0;
                                s.FileSizes.Clear();
                                s.DownloadStartTime = null;
                                s.LastSpeedUpdateTime = DateTime.MinValue;
                                s.LastPercent = 0;
                                s.CurrentSpeedBps = 0;
                                s.SpeedOverrideString = null;
                            }
                            DrawSlots(force: true);
                        }
                    }));
                }

                Task.WaitAll(workerTasks.ToArray());

                // Flush any remaining pending logs and clear the slot lines
                lock (_drawLock)
                {
                    if (_isTty && _slots != null)
                    {
                        try
                        {
                            int startTop = Math.Max(0, Console.CursorTop - _slots.Length);
                            Console.SetCursorPosition(0, startTop);
                            while (_pendingLogs.Count > 0)
                            {
                                ClearCurrentLine();
                                _pendingLogs.Dequeue()();
                            }
                            for (int s = 0; s < _slots.Length; s++)
                            {
                                ClearCurrentLine();
                                Console.WriteLine();
                            }
                            Console.SetCursorPosition(0, startTop);
                        }
                        catch
                        {
                            while (_pendingLogs.Count > 0) _pendingLogs.Dequeue()();
                        }
                    }
                    else
                    {
                        while (_pendingLogs.Count > 0) _pendingLogs.Dequeue()();
                    }
                }

                DownloadTui.WriteFinal(!hadErrors, totalDepots, successfulDepots, outputPath);

                if (!_isTty && _depotResultLog.Count > 0)
                {
                    Console.WriteLine();
                    Console.WriteLine("  ┌────────────┬──────────────┐");
                    Console.WriteLine("  │ Depot ID   │ Result       │");
                    Console.WriteLine("  ├────────────┼──────────────┤");
                    foreach (var kv in _depotResultLog.OrderBy(x => x.Key))
                    {
                        var orig = Console.ForegroundColor;
                        Console.ForegroundColor = kv.Value ? ConsoleColor.Green : ConsoleColor.Red;
                        Console.WriteLine($"  │ {kv.Key,-10} │ {(kv.Value ? "OK" : "FAILED"),-12} │");
                        Console.ForegroundColor = orig;
                    }
                    Console.WriteLine("  └────────────┴──────────────┘");
                    Console.WriteLine();
                }

                if (!hadErrors)
                {
                    ClearCheckpoints(outputPath);
                }

                if (hadErrors)
                {
                    return 1;
                }

                try
                {
                    string gameName = Path.GetFileNameWithoutExtension(luaPath);
                    if (string.IsNullOrEmpty(gameName)) gameName = $"App_{appId}";

                    var depotIdsList = new List<string>();
                    foreach (var depot in depots.Keys)
                    {
                        depotIdsList.Add(depot);
                    }

                    var libGame = new LibraryGame
                    {
                        GameName = gameName,
                        AppId = appId,
                        LuaPath = Path.GetFullPath(luaPath),
                        OutputDir = outputPath,
                        DepotIds = depotIdsList,
                        InstallDate = DateTime.Now,
                        TotalSizeBytes = LibraryManager.GetDirectorySize(outputPath),
                        IsVerified = true
                    };
                    LibraryManager.AddOrUpdateGame(libGame);
                    DownloadTui.WriteStatus("Library", $"Registered '{gameName}' in the local library index", ConsoleColor.Green);
                }
                catch (Exception ex)
                {
                    DownloadTui.WriteStatus("Library", $"Failed to update library index: {ex.Message}", ConsoleColor.Yellow);
                }

                return 0;
            }
            catch (Exception ex)
            {
                LogError($"[Fatal Error] An unhandled exception occurred: {ex.Message}");
                LogError(ex.StackTrace ?? "");
                Console.ReadKey();
                return 1;
            }
            finally
            {
                try { if (OperatingSystem.IsWindows()) { Console.CursorVisible = initialCursorVisible; } } catch {}
                SafeCleanupKeys();
                SafeCleanupDepotDownloaderFolder(outputPath, ddmodPath);
            }
        }

        private static bool IsDepotDownloadFailure(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;

            return line.Contains("AccessDenied", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("No valid depot key", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("unable to download", StringComparison.OrdinalIgnoreCase) ||
                line.Contains("missing public subsection or manifest section", StringComparison.OrdinalIgnoreCase);
        }

        private static void SafeCleanupKeys()
        {
            if (!string.IsNullOrEmpty(_tempKeysPath) && File.Exists(_tempKeysPath))
            {
                try
                {
                    File.Delete(_tempKeysPath);
                    _tempKeysPath = null;
                }
                catch { }
            }
        }

        private static void WriteColored(string text, ConsoleColor color)
        {
            var orig = Console.ForegroundColor;
            Console.ForegroundColor = color;
            Console.WriteLine(text);
            Console.ForegroundColor = orig;
        }

        private static void ProcessProgressLine(int slotId, string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;

            try
            {
                // Suppress noisy status messages from DepotDownloaderMod
                if (line.StartsWith("Using depot keys from", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("No username given", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Connecting to Steam3", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Logging anonymously into Steam3", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Using Steam3 suggested CellID", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Got AppInfo for", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Using app branch", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Disconnected from Steam", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Processing depot", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Already have manifest", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Manifest ", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Downloading depot", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Total downloaded:", StringComparison.OrdinalIgnoreCase) ||
                    line.StartsWith("Pre-allocating", StringComparison.OrdinalIgnoreCase) ||
                    Regex.IsMatch(line, @"^Depot \d+ - Downloaded"))
                {
                    // Update slot status for connecting/pre-allocating states
                    if (line.StartsWith("Connecting to Steam3", StringComparison.OrdinalIgnoreCase) ||
                        line.StartsWith("Logging anonymously", StringComparison.OrdinalIgnoreCase))
                    {
                        lock (_drawLock)
                        {
                            _slots[slotId].Status = "Connecting...";
                            _slots[slotId].Percent = null;
                        }
                        DrawSlots(force: true);
                    }
                    else if (line.StartsWith("Pre-allocating", StringComparison.OrdinalIgnoreCase))
                    {
                        var slot = _slots[slotId];
                        lock (_drawLock)
                        {
                            slot.Status = "Pre-allocating...";
                            slot.Percent = null;
                        }
                        DrawSlots(force: true);

                        string rawPath = line.Substring(14).Trim();
                        string resolvedPath = rawPath;
                        if (!Path.IsPathRooted(resolvedPath) && !string.IsNullOrEmpty(slot.OutputPath))
                        {
                            resolvedPath = Path.Combine(slot.OutputPath, rawPath);
                        }

                        Task.Run(async () =>
                        {
                            for (int attempt = 0; attempt < 5; attempt++)
                            {
                                try
                                {
                                    if (File.Exists(resolvedPath))
                                    {
                                        long size = new FileInfo(resolvedPath).Length;
                                        if (size > 0)
                                        {
                                            lock (_drawLock)
                                            {
                                                if (slot.FileSizes.TryAdd(rawPath, size))
                                                {
                                                    slot.TotalUncompressedSize += size;
                                                }
                                            }
                                            break;
                                        }
                                    }
                                }
                                catch {}
                                await Task.Delay(20);
                            }
                        });
                    }
                    return;
                }

                if (line.StartsWith("Validating ", StringComparison.OrdinalIgnoreCase))
                {
                    lock (_drawLock)
                    {
                        _slots[slotId].ActiveValidationFile = Path.GetFileName(line.Substring(11).Trim());
                        _slots[slotId].Status = "Validating";
                        _slots[slotId].Percent = null;
                    }
                    DrawSlots(force: true);
                    return;
                }

                var pctMatch = Regex.Match(line, @"(\d+(?:[.,]\d+)?)%");
                if (pctMatch.Success)
                {
                    string pctStr = pctMatch.Groups[1].Value.Replace(',', '.');
                    if (double.TryParse(pctStr, NumberStyles.Any, CultureInfo.InvariantCulture, out double percentage))
                    {
                        lock (_drawLock)
                        {
                            var slot = _slots[slotId];
                            slot.Percent = percentage;
                            slot.Status = "Downloading";

                            var speedMatch = Regex.Match(line, @"\(([^)]+)\)\s*$");
                            if (speedMatch.Success)
                            {
                                slot.SpeedOverrideString = speedMatch.Groups[1].Value;
                            }
                            else
                            {
                                slot.SpeedOverrideString = null;
                            }

                            DateTime now = DateTime.UtcNow;
                            if (slot.DownloadStartTime == null)
                            {
                                slot.DownloadStartTime = now;
                                slot.LastSpeedUpdateTime = now;
                                slot.LastPercent = percentage;
                            }
                            else
                            {
                                double timeDiffSec = (now - slot.LastSpeedUpdateTime).TotalSeconds;
                                if (timeDiffSec >= 0.5)
                                {
                                    long currentTotalBytes = slot.TotalUncompressedSize;
                                    double bytesDiff = currentTotalBytes - slot.LastSpeedTotalBytes;
                                    if (bytesDiff > 0)
                                    {
                                        double speedBps = bytesDiff / timeDiffSec;

                                        if (slot.CurrentSpeedBps == 0)
                                        {
                                            slot.CurrentSpeedBps = speedBps;
                                        }
                                        else
                                        {
                                            slot.CurrentSpeedBps = (slot.CurrentSpeedBps * 0.7) + (speedBps * 0.3);
                                        }
                                    }
                                    else if (percentage >= 100.0)
                                    {
                                        slot.CurrentSpeedBps = 0;
                                    }

                                    slot.LastSpeedTotalBytes = currentTotalBytes;
                                    slot.LastSpeedUpdateTime = now;
                                    slot.LastPercent = percentage;
                                }
                            }
                        }
                        DrawSlots();
                    }
                }
                // All other stdout lines are silently ignored — errors come through stderr
                // and are collected in depotOutputErrors for the final summary.
            }
            catch
            {
            }
        }

        private static void ClearCurrentLine()
        {
            try
            {
                int width = Console.WindowWidth - 1;
                if (width > 0)
                {
                    Console.Write(new string(' ', width) + "\r");
                }
            }
            catch {}
        }

        private static string FormatSpeed(double speedBps)
        {
            if (speedBps <= 0) return "0 B/s";
            if (speedBps < 1024) return $"{speedBps:F0} B/s";
            if (speedBps < 1024 * 1024) return $"{speedBps / 1024.0:F1} KB/s";
            return $"{speedBps / (1024.0 * 1024.0):F1} MB/s";
        }

        private static void DrawSlotLine(int slotId)
        {
            // Matches the DownloadTui.WriteStatus layout:
            //   pad + "  │ " + Pad(label, 16) + " │ " + message
            var slot = _slots[slotId];

            if (slot.Percent != null && slot.Status == "Downloading" && slot.LastSpeedUpdateTime != DateTime.MinValue)
            {
                double timeSinceUpdate = (DateTime.UtcNow - slot.LastSpeedUpdateTime).TotalSeconds;
                if (timeSinceUpdate > 3.0)
                {
                    if (timeSinceUpdate > 6.0)
                    {
                        slot.CurrentSpeedBps = 0;
                    }
                    else
                    {
                        slot.CurrentSpeedBps *= 0.5;
                    }
                }
            }

            string pad = new string(' ', DownloadTui.LeftPad);
            int charsWritten = pad.Length;

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(pad + "  │ ");
            charsWritten += 4;

            Console.ForegroundColor = ConsoleColor.DarkCyan;
            string slotLabel = TuiText.Pad($"Slot {slotId + 1}", 16);
            Console.Write(slotLabel);
            charsWritten += 16;

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(" │ ");
            charsWritten += 3;

            if (slot.DepotId == null)
            {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("Waiting...");
                charsWritten += 10;
            }
            else
            {
                // Build the status message portion to match WriteStatus value style
                Console.ForegroundColor = ConsoleColor.White;
                string depotLabel = TuiText.Pad(slot.DepotId, 8);
                Console.Write(depotLabel);
                charsWritten += 8;

                if (slot.Percent != null)
                {
                    double pct = slot.Percent.Value;
                    int barWidth = 30;
                    int filled = (int)Math.Round(pct / 100.0 * barWidth);
                    if (filled < 0) filled = 0;
                    if (filled > barWidth) filled = barWidth;

                    string filledBar = new string('\u2588', filled);
                    string emptyBar = new string('\u2591', barWidth - filled);

                    Console.ForegroundColor = ConsoleColor.Green;
                    string pctStr = $"{pct,5:F1}%";
                    Console.Write(pctStr);
                    charsWritten += pctStr.Length;

                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write(" [");
                    charsWritten += 2;

                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.Write(filledBar);
                    charsWritten += filledBar.Length;

                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write(emptyBar);
                    charsWritten += emptyBar.Length;

                    Console.Write("] ");
                    charsWritten += 2;

                    Console.ForegroundColor = ConsoleColor.Gray;
                    string speedStr = "";
                    if (!string.IsNullOrEmpty(slot.SpeedOverrideString))
                    {
                        speedStr = $" ({slot.SpeedOverrideString})";
                    }
                    else if (slot.CurrentSpeedBps > 0)
                    {
                        speedStr = $" ({FormatSpeed(slot.CurrentSpeedBps)})";
                    }
                    string etaStr = "";
                    if (slot.Status == "Downloading" && pct > 0.5 && pct < 99.5 && slot.DownloadStartTime != null)
                    {
                        double etaSec = 0;
                        if (slot.TotalUncompressedSize > 0 && slot.CurrentSpeedBps > 0)
                        {
                            etaSec = slot.TotalUncompressedSize * (100.0 - pct) / 100.0 / slot.CurrentSpeedBps;
                        }
                        else if (pct > 1.0)
                        {
                            double elapsed = (DateTime.UtcNow - slot.DownloadStartTime.Value).TotalSeconds;
                            etaSec = elapsed * (100.0 - pct) / pct;
                        }
                        if (etaSec > 5 && etaSec < 86400)
                            etaStr = $"  ETA {FormatEta(etaSec)}";
                    }
                    string statusText = slot.Status + speedStr + etaStr;
                    Console.Write(statusText);
                    charsWritten += statusText.Length;
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Gray;
                    Console.Write(slot.Status);
                    charsWritten += slot.Status.Length;
                }

                if (!string.IsNullOrEmpty(slot.ActiveValidationFile))
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    string valStr = $"  {TuiText.Shorten(slot.ActiveValidationFile, 20)}";
                    Console.Write(valStr);
                    charsWritten += valStr.Length;
                }
            }

            // Pad the rest of the line with spaces if it's shorter than the previous write for this slot.
            int lastLen = _lastSlotLengths[slotId];
            if (charsWritten < lastLen)
            {
                Console.Write(new string(' ', lastLen - charsWritten));
            }
            _lastSlotLengths[slotId] = charsWritten;

            Console.ResetColor();
        }

        /// <summary>
        /// Clears the slot lines at the bottom (used before printing final results).
        /// Must be called while holding _drawLock or when workers have finished.
        /// </summary>
        private static void ClearSlotLines()
        {
            if (!_isTty || _slots == null) return;
            try
            {
                int startTop = Math.Max(0, Console.CursorTop - _slots.Length);
                Console.SetCursorPosition(0, startTop);
                for (int s = 0; s < _slots.Length; s++)
                {
                    ClearCurrentLine();
                    Console.WriteLine();
                    _lastSlotLengths[s] = 0;
                }
                Console.SetCursorPosition(0, startTop);
            }
            catch { }
        }

        private static void DrawSlots(bool force = false)
        {
            lock (_drawLock)
            {
                if (!force && _pendingLogs.Count == 0 && DateTime.UtcNow - _lastDrawTime < _drawThrottleInterval)
                {
                    return;
                }
                _lastDrawTime = DateTime.UtcNow;

                if (_isTty && _slots != null)
                {
                    try
                    {
                        // Move cursor up to where slot lines start
                        int startTop = Math.Max(0, Console.CursorTop - _slots.Length);
                        Console.SetCursorPosition(0, startTop);

                        // Flush any pending permanent log lines (completion/failure messages)
                        while (_pendingLogs.Count > 0)
                        {
                            Action logAction = _pendingLogs.Dequeue();
                            ClearCurrentLine();
                            logAction();
                        }

                        // Redraw the slot lines without clear-blanking to avoid flickering.
                        // Any leftover characters are cleared via character-based padding inside DrawSlotLine.
                        for (int s = 0; s < _slots.Length; s++)
                        {
                            DrawSlotLine(s);
                            Console.WriteLine();
                        }
                    }
                    catch
                    {
                        // Fallback: just flush pending logs without cursor control
                        while (_pendingLogs.Count > 0)
                        {
                            _pendingLogs.Dequeue()();
                        }
                    }
                }
                else
                {
                    // Non-TTY: just flush logs sequentially
                    while (_pendingLogs.Count > 0)
                    {
                        _pendingLogs.Dequeue()();
                    }
                }
            }
        }

        private static void SafeCleanupDepotDownloaderFolder(string? outputPath, string ddmodPath)
        {
            var pathsToTry = new List<string>();
            
            if (!string.IsNullOrEmpty(outputPath))
            {
                pathsToTry.Add(Path.Combine(outputPath, ".DepotDownloader"));
            }
            
            pathsToTry.Add(Path.Combine(Directory.GetCurrentDirectory(), ".DepotDownloader"));
            
            string? ddmodDir = Path.GetDirectoryName(ddmodPath);
            if (!string.IsNullOrEmpty(ddmodDir))
            {
                pathsToTry.Add(Path.Combine(ddmodDir, ".DepotDownloader"));
            }
            
            pathsToTry.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ".DepotDownloader"));

            foreach (var path in pathsToTry)
            {
                LibraryManager.RobustDeleteDirectory(path);
            }
        }

        private static string GetCheckpointDir(string outputPath) =>
            Path.Combine(outputPath, ".depotdl_progress");

        private static HashSet<string> LoadCompletedDepots(string outputPath)
        {
            try
            {
                var dir = GetCheckpointDir(outputPath);
                if (!Directory.Exists(dir)) return new HashSet<string>();
                return Directory.GetFiles(dir, "*.done")
                    .Select(f => Path.GetFileNameWithoutExtension(f))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
            catch { return new HashSet<string>(); }
        }

        private static void MarkDepotComplete(string outputPath, string depotId)
        {
            try
            {
                var dir = GetCheckpointDir(outputPath);
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, $"{depotId}.done"), "");
            }
            catch { }
        }

        private static void ClearCheckpoints(string outputPath)
        {
            try
            {
                var dir = GetCheckpointDir(outputPath);
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
            catch { }
        }

        private static string FormatEta(double seconds)
        {
            if (seconds < 60) return $"{(int)seconds}s";
            if (seconds < 3600) return $"{(int)(seconds / 60)}m {(int)(seconds % 60)}s";
            return $"{(int)(seconds / 3600)}h {(int)((seconds % 3600) / 60)}m";
        }

        private static void PrintUsage()
        {
            var orig = Console.ForegroundColor;
            Console.ForegroundColor = ConsoleColor.White;
            Console.WriteLine("Usage: DepotDL.CLI --lua <path-to-lua-file> [options]");
            Console.ForegroundColor = ConsoleColor.Gray;
            Console.WriteLine("\nRequired:");
            Console.WriteLine("  -l, --lua <path>            Path to the game Lua configuration file.");
            Console.WriteLine("\nOptions:");
            Console.WriteLine("  -m, --manifests-dir <dir>   Path to folder containing pre-downloaded *.manifest files.");
            Console.WriteLine("  -o, --output <dir>          Path to directory where files will be downloaded (Defaults to './downloads/App_<appid>').");
            Console.WriteLine("  -d, --ddmod <path>          Direct path to DepotDownloaderMod.dll (Auto-resolved if omitted).");
            Console.WriteLine("  -n, --dotnet <path>         Direct path to dotnet executable (Auto-resolved if omitted).");
            Console.WriteLine($"      --max-downloads <n>     Parallel chunk downloads per depot. Default: {DepotDownloadDefaults.MaxDownloads}, max: 128.");
            Console.WriteLine("  -p, --max-parallel-depots <n> Max parallel depot downloads (1 to 8). Default: 2.");
            Console.WriteLine("  -h, --help                  Show this usage help screen.");
            Console.WriteLine("\nNote: Launching DepotDL.CLI without arguments opens the interactive TUI dashboard mode.");
            Console.ForegroundColor = orig;
        }
    }
}
