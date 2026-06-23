// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DepotDL.CLI
{
    public static class GameLauncher
    {
        private static readonly string[] ExeSkipPatterns = new[]
        {
            "unins", "setup", "install", "redist", "crash", "report",
            "update", "patch", "vc_", "dotnet", "directx", "dxsetup",
            "steamclient_loader", "UnityCrash", "helper",
            "tool", "config", "benchmark", "editor", "prerequisite",
            "prereq", "physx", "vcredist", "uplay", "easyanticheat",
            "battleye", "anticheat", "game_shipping"
        };

        private static readonly HttpClient _apiClient = new() { Timeout = TimeSpan.FromSeconds(5) };
        private static readonly HttpClient _dlClient = new() { Timeout = TimeSpan.FromSeconds(30) };

        private static readonly string[] LinuxSkipPatterns = new[]
        {
            "unins", "setup", "install", "crash", "report", "update", "patch", "helper", "tool"
        };

        private static readonly string[] LinuxSkipExtensions = new[]
        {
            ".so", ".py", ".sh", ".txt", ".ini", ".json", ".cfg", ".pak", ".dll", ".png", ".jpg", ".jpeg", ".zip", ".tar", ".gz"
        };

        public static string? FindLaunchTarget(string gameDir)
        {
            if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir))
                return null;

            if (OperatingSystem.IsWindows())
            {
                try
                {
                    var batPath = Path.Combine(gameDir, "Launch.bat");
                    if (File.Exists(batPath)) return batPath;

                    var bats = Directory.GetFiles(gameDir, "Launch*.bat", SearchOption.TopDirectoryOnly);
                    Array.Sort(bats);
                    if (bats.Length > 0) return bats[0];
                }
                catch { }
            }
            else
            {
                try
                {
                    var shPath = Path.Combine(gameDir, "launch.sh");
                    if (File.Exists(shPath)) return shPath;

                    var wineShPath = Path.Combine(gameDir, "launch_wine.sh");
                    if (File.Exists(wineShPath)) return wineShPath;

                    var shs = Directory.GetFiles(gameDir, "launch*.sh", SearchOption.TopDirectoryOnly);
                    Array.Sort(shs);
                    if (shs.Length > 0) return shs[0];
                }
                catch { }
            }

            if (OperatingSystem.IsWindows())
            {
                return FindMainExe(gameDir);
            }
            else
            {
                var native = FindMainLinuxBinary(gameDir);
                if (native != null) return native;

                return FindMainExe(gameDir);
            }
        }

        public static string? FindMainExe(string gameDir)
        {
            string? bestPath = null;
            long bestSize = 0;

            try
            {
                var files = Directory.GetFiles(gameDir, "*.exe", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    var nameLower = Path.GetFileName(file).ToLowerInvariant();
                    if (nameLower.EndsWith(".unpacked.exe", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var skip = false;
                    foreach (var pattern in ExeSkipPatterns)
                    {
                        if (nameLower.Contains(pattern))
                        {
                            skip = true;
                            break;
                        }
                    }

                    if (skip) continue;

                    try
                    {
                        var info = new FileInfo(file);
                        var size = info.Length;
                        if (size > bestSize)
                        {
                            bestSize = size;
                            bestPath = file;
                        }
                    }
                    catch { }
                }
            }
            catch { }

            return bestPath;
        }

        public static string? FindMainLinuxBinary(string gameDir)
        {
            string? bestPath = null;
            long bestSize = 0;

            try
            {
                var files = Directory.GetFiles(gameDir, "*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    var ext = Path.GetExtension(file).ToLowerInvariant();
                    var skipExt = false;
                    foreach (var skip in LinuxSkipExtensions)
                    {
                        if (ext == skip) { skipExt = true; break; }
                    }
                    if (skipExt) continue;

                    var nameLower = Path.GetFileName(file).ToLowerInvariant();
                    var skipName = false;
                    foreach (var skip in LinuxSkipPatterns)
                    {
                        if (nameLower.Contains(skip)) { skipName = true; break; }
                    }
                    if (skipName) continue;

                    try
                    {
                        var info = new FileInfo(file);
                        using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
                        {
                            var magic = new byte[4];
                            if (fs.Read(magic, 0, 4) == 4)
                            {
                                if (magic[0] == 0x7f && magic[1] == 0x45 && magic[2] == 0x4c && magic[3] == 0x46)
                                {
                                    if (info.Length > bestSize)
                                    {
                                        bestSize = info.Length;
                                        bestPath = file;
                                    }
                                }
                            }
                        }
                    }
                    catch { }
                }
            }
            catch { }

            return bestPath;
        }

        public static Exception? Launch(string target, string workingDir)
        {
            try
            {
                if (!OperatingSystem.IsWindows() && File.Exists(target))
                {
                    try
                    {
                        var chmod = Process.Start(new ProcessStartInfo
                        {
                            FileName = "chmod",
                            Arguments = $"+x \"{target}\"",
                            UseShellExecute = false,
                            CreateNoWindow = true
                        });
                        chmod?.WaitForExit();
                    }
                    catch { }
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = target,
                    WorkingDirectory = workingDir,
                    UseShellExecute = true
                });
                return null;
            }
            catch (Exception ex)
            {
                return ex;
            }
        }

        public static (bool Success, string? Error) EnsureStarApplied(string appId, string gameDir, string? luaPath = null, string? steamWebApiKey = null, bool downloadAchievementIcons = true)
        {
            if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir))
                return (false, null);

            var isWindows = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows);
            var launchScript = OperatingSystem.IsWindows() ? "Launch.bat" : "launch.sh";
            var launchPath = Path.Combine(gameDir, launchScript);

            var steamApiFiles = new List<string>();
            var scanFailed = false;
            try
            {
                if (isWindows)
                {
                    foreach (var f in Directory.GetFiles(gameDir, "*.dll", SearchOption.AllDirectories))
                    {
                        var fn = Path.GetFileName(f).ToLowerInvariant();
                        if (fn == "steam_api.dll" || fn == "steam_api64.dll") steamApiFiles.Add(f);
                    }
                }
                else
                {
                    var steamApiLib = OperatingSystem.IsMacOS() ? "libsteam_api.dylib" : "libsteam_api.so";
                    var libPattern = OperatingSystem.IsMacOS() ? "*.dylib" : "*.so";
                    foreach (var f in Directory.GetFiles(gameDir, libPattern, SearchOption.AllDirectories))
                    {
                        var fn = Path.GetFileName(f).ToLowerInvariant();
                        if (fn == steamApiLib) steamApiFiles.Add(f);
                    }
                }
            }
            catch
            {
                scanFailed = true;
            }

            if (scanFailed)
            {
                const string scanErr = "Failed to scan game directory for Steam API files.";
                AppLogger.Error("GameLauncher", scanErr);
                return (false, scanErr);
            }

            var primaryFile = steamApiFiles.FirstOrDefault(f => Path.GetFileName(f).ToLowerInvariant() == "steam_api64.dll")
                                ?? steamApiFiles.FirstOrDefault();

            if (File.Exists(launchPath))
            {
                var starApplied = steamApiFiles.Any(f =>
                {
                    var bak = f + ".bak";
                    if (!File.Exists(bak)) return false;
                    try
                    {
                        if (new FileInfo(f).Length == new FileInfo(bak).Length) return false;
                    }
                    catch (IOException) { }
                    return true;
                });
                if (starApplied)
                {
                    if (!string.IsNullOrWhiteSpace(steamWebApiKey) && primaryFile != null)
                    {
                        var starDir = Path.Combine(Path.GetDirectoryName(primaryFile)!, "STAR");
                        var achPath = Path.Combine(starDir, "achievements.json");
                        if (!File.Exists(achPath))
                            FetchAchievements(appId, starDir, steamWebApiKey, downloadAchievementIcons);
                    }
                    return (true, null);
                }
            }

            var toolsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools");
            var steamlessExe = Path.Combine(toolsPath, "steamless", "Steamless.CLI.exe");
            var starToolsDir = Path.Combine(toolsPath, "star");

            if (!Directory.Exists(toolsPath))
            {
                toolsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory);
                steamlessExe = Path.Combine(toolsPath, "steamless", "Steamless.CLI.exe");
                starToolsDir = Path.Combine(toolsPath, "star");
                if (!Directory.Exists(starToolsDir))
                    return (false, "STAR tools not found in application directory.");
            }

            if (File.Exists(steamlessExe))
            {
                if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                {
                }
                else
                {
                    try
                    {
                        var exeFiles = Directory.GetFiles(gameDir, "*.exe", SearchOption.AllDirectories);
                        foreach (var exe in exeFiles)
                        {
                            var nameLower = Path.GetFileName(exe).ToLowerInvariant();
                            if (nameLower.EndsWith(".unpacked.exe", StringComparison.OrdinalIgnoreCase))
                                continue;

                            var skip = false;
                            foreach (var pattern in ExeSkipPatterns)
                            {
                                if (nameLower.Contains(pattern)) { skip = true; break; }
                            }
                            if (skip) continue;

                            var unpackedPath1 = Path.Combine(Path.GetDirectoryName(exe)!, Path.GetFileNameWithoutExtension(exe) + ".unpacked.exe");
                            var unpackedPath2 = exe + ".unpacked.exe";

                            if (File.Exists(unpackedPath1)) File.Delete(unpackedPath1);
                            if (File.Exists(unpackedPath2)) File.Delete(unpackedPath2);

                            Process? proc = null;
                            var success = false;
                            try
                            {
                                var startInfo = new ProcessStartInfo
                                {
                                    FileName = steamlessExe,
                                    Arguments = $"--quiet --exp \"{exe}\"",
                                    WorkingDirectory = Path.GetDirectoryName(steamlessExe)!,
                                    UseShellExecute = false,
                                    CreateNoWindow = true
                                };
                                proc = Process.Start(startInfo);
                                if (proc != null)
                                {
                                    var exited = proc.WaitForExit(60000);
                                    if (!exited)
                                    {
                                        try { proc.Kill(); } catch { }
                                        AppLogger.Error("GameLauncher", $"SteamStub unpacking timed out for {exe}");
                                    }
                                    else if (proc.ExitCode == 0)
                                    {
                                        success = true;
                                    }
                                    else
                                    {
                                        AppLogger.Error("GameLauncher", $"SteamStub unpacking failed with exit code {proc.ExitCode} for {exe}");
                                    }
                                }
                            }
                            catch (Exception procEx)
                            {
                                AppLogger.Error("GameLauncher", $"SteamStub process error: {procEx.Message}");
                            }
                            finally
                            {
                                proc?.Dispose();
                            }

                            if (success)
                            {
                                string? actualUnpacked = null;
                                if (File.Exists(unpackedPath1)) actualUnpacked = unpackedPath1;
                                else if (File.Exists(unpackedPath2)) actualUnpacked = unpackedPath2;

                                if (actualUnpacked != null)
                                {
                                    var backupPath = exe + ".steamstub.bak";
                                    if (!File.Exists(backupPath))
                                    {
                                        File.Copy(exe, backupPath, true);
                                    }
                                    File.Copy(actualUnpacked, exe, true);
                                }
                            }

                            if (File.Exists(unpackedPath1)) try { File.Delete(unpackedPath1); } catch { }
                            if (File.Exists(unpackedPath2)) try { File.Delete(unpackedPath2); } catch { }
                        }
                    }
                    catch (Exception ex)
                    {
                        var steamlessErr = $"SteamStub unpacking error: {ex.Message}";
                        AppLogger.Error("GameLauncher", steamlessErr);
                        return (false, steamlessErr);
                    }
                }
            }

            var dlcLines = new List<string>();
            if (!string.IsNullOrEmpty(luaPath) && File.Exists(luaPath))
            {
                var luaContent = File.ReadAllText(luaPath);
                var dlcRegex = new Regex(@"\[""(\d{4,})""\]\s*=\s*""([^""]+)""");
                foreach (Match m in dlcRegex.Matches(luaContent))
                {
                    var dlcId = m.Groups[1].Value;
                    var dlcName = m.Groups[2].Value.Replace("\r", " ").Replace("\n", " ").Replace("=", "-").Trim();
                    if (dlcId == appId) continue;
                    dlcLines.Add($"dlc.{dlcId} = {dlcName}");
                }
            }

            var processedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var dll in steamApiFiles)
            {
                var dllDir = Path.GetDirectoryName(dll)!;
                if (!processedDirs.Add(dllDir)) continue;

                var starDir = Path.Combine(dllDir, "STAR");
                try
                {
                    Directory.CreateDirectory(starDir);

                    File.WriteAllText(Path.Combine(dllDir, "steam_appid.txt"), appId);

                    File.WriteAllText(Path.Combine(starDir, "identity.star"),
                        "display_name = Player\r\nxuid = 76561197960265728\r\nlocale = english\r\n");

                    var gameStarLines = new List<string>
                    {
                        "beta = false",
                        "branch = public",
                        "dlc.unlock_all = 1"
                    };
                    gameStarLines.AddRange(dlcLines);
                    File.WriteAllText(Path.Combine(starDir, "game.star"), string.Join("\r\n", gameStarLines) + "\r\n");

                    File.WriteAllText(Path.Combine(starDir, "overlay.star"), "enabled = true\r\n");

                    File.WriteAllText(Path.Combine(starDir, "languages.star"),
                        "[languages]\r\nenglish = 1\r\nfrench = 1\r\nitalian = 1\r\ngerman = 1\r\nspanish = 1\r\narabic = 1\r\njapanese = 1\r\nkoreana = 1\r\npolish = 1\r\nbrazilian = 1\r\nrussian = 1\r\nschinese = 1\r\nlatam = 1\r\ntchinese = 1\r\n");
                }
                catch (Exception ex)
                {
                    var configErr = $"STAR config generation error: {ex.Message}";
                    AppLogger.Error("GameLauncher", configErr);
                    return (false, configErr);
                }
            }

            if (primaryFile != null)
            {
                var primaryStarDir = Path.Combine(Path.GetDirectoryName(primaryFile)!, "STAR");
                FetchAchievements(appId, primaryStarDir, steamWebApiKey, downloadAchievementIcons);
            }

            var replaced = false;

            try
            {
                if (isWindows)
                {
                    foreach (var dll in steamApiFiles)
                    {
                        var nameLower = Path.GetFileName(dll).ToLowerInvariant();
                        var bakPath = dll + ".bak";

                        if (!File.Exists(bakPath))
                            File.Copy(dll, bakPath, true);

                        var sourceDll = Path.Combine(starToolsDir, nameLower);
                        if (File.Exists(sourceDll))
                        {
                            File.Copy(sourceDll, dll, true);
                            replaced = true;
                        }
                    }
                }
                else
                {
                    foreach (var so in steamApiFiles)
                    {
                        var bakPath = so + ".bak";
                        if (!File.Exists(bakPath))
                            File.Copy(so, bakPath, true);

                        var nameLower = Path.GetFileName(so).ToLowerInvariant();
                        var sourceSo = Path.Combine(starToolsDir, nameLower);
                        if (File.Exists(sourceSo))
                        {
                            File.Copy(sourceSo, so, true);
                            replaced = true;
                        }
                        else
                        {
                            AppLogger.Error("GameLauncher", $"STAR does not ship a replacement for {nameLower} on this platform; skipping DLL replacement.");
                        }
                    }

                    if (!replaced && steamApiFiles.Count > 0)
                    {
                        AppLogger.Error("GameLauncher", "STAR has no replacement .so for this platform; configs written but no DLL swapped.");
                        replaced = true;
                    }
                }
            }
            catch (Exception ex)
            {
                var starErr = $"STAR application error: {ex.Message}";
                AppLogger.Error("GameLauncher", starErr);
                return (false, starErr);
            }

            if (!replaced && isWindows)
            {
                var apiFileType = "steam_api DLLs";
                var noApiErr = $"No {apiFileType} found to replace.";
                AppLogger.Error("GameLauncher", noApiErr);
                return (false, noApiErr);
            }

            try
            {
                var mainExe = FindMainExe(gameDir);
                if (!string.IsNullOrEmpty(mainExe))
                {
                    var exeRel = Path.GetRelativePath(gameDir, mainExe);
                    var isWindowsPE = mainExe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

                    if (OperatingSystem.IsWindows())
                    {
                        var content = $"@echo off\ncd /d \"%~dp0\"\nstart \"\" \"{exeRel}\"\n";
                        File.WriteAllText(launchPath, content);
                    }
                    else
                    {
                        if (isWindowsPE)
                        {
                            var content = $"#!/bin/sh\ncd \"$(dirname \"$0\")\"\nexec wine \"./{exeRel}\" \"$@\"\n";
                            File.WriteAllText(launchPath, content);
                        }
                        else
                        {
                            var content = $"#!/bin/sh\ncd \"$(dirname \"$0\")\"\nexec \"./{exeRel}\" \"$@\"\n";
                            File.WriteAllText(launchPath, content);
                        }
                    }
                }
            }
            catch { }

            return (true, null);
        }

        private static void FetchAchievements(string appId, string starDir, string? userKey = null, bool downloadAchievementIcons = true)
        {
            if (string.IsNullOrWhiteSpace(userKey))
                return;
            try
            {
                var apiKey = userKey.Trim();
                var url = $"https://api.steampowered.com/ISteamUserStats/GetSchemaForGame/v2/?key={apiKey}&appid={appId}&l=english";
                var response = _apiClient.GetStringAsync(url).GetAwaiter().GetResult();
                using var doc = JsonDocument.Parse(response);
                if (doc.RootElement.TryGetProperty("game", out var game) &&
                    game.TryGetProperty("availableGameStats", out var availableStats) &&
                    availableStats.TryGetProperty("achievements", out var achievements))
                {
                    var achList = new List<Dictionary<string, object>>();
                    var downloadTasks = new List<(string Url, string Name, bool IsGray)>();

                    foreach (var ach in achievements.EnumerateArray())
                    {
                        var dict = new Dictionary<string, object>();
                        foreach (var prop in ach.EnumerateObject())
                        {
                            if (prop.Name == "hidden")
                            {
                                if (prop.Value.ValueKind == JsonValueKind.Number)
                                    dict[prop.Name] = prop.Value.GetInt32().ToString();
                                else if (prop.Value.ValueKind == JsonValueKind.True)
                                    dict[prop.Name] = "1";
                                else if (prop.Value.ValueKind == JsonValueKind.False)
                                    dict[prop.Name] = "0";
                                else
                                    dict[prop.Name] = prop.Value.ToString();
                            }
                            else
                            {
                                if (prop.Value.ValueKind == JsonValueKind.Number)
                                    dict[prop.Name] = prop.Value.GetDouble();
                                else if (prop.Value.ValueKind == JsonValueKind.True)
                                    dict[prop.Name] = true;
                                else if (prop.Value.ValueKind == JsonValueKind.False)
                                    dict[prop.Name] = false;
                                else
                                    dict[prop.Name] = prop.Value.GetString() ?? "";
                            }
                        }

                        if (downloadAchievementIcons)
                        {
                            var achName = dict.TryGetValue("name", out var n) ? n as string : null;
                            if (!string.IsNullOrEmpty(achName))
                            {
                                if (dict.TryGetValue("icon", out var iconVal) && iconVal is string iconUrl && !string.IsNullOrEmpty(iconUrl))
                                    downloadTasks.Add((iconUrl, achName, false));
                                if (dict.TryGetValue("icongray", out var grayVal) && grayVal is string grayUrl && !string.IsNullOrEmpty(grayUrl))
                                    downloadTasks.Add((grayUrl, achName, true));
                            }
                        }

                        achList.Add(dict);
                    }

                    if (downloadAchievementIcons && downloadTasks.Count > 0)
                    {
                        var imagesDir = Path.Combine(starDir, "achievement_images");
                        Directory.CreateDirectory(imagesDir);

                        var downloaded = new System.Collections.Concurrent.ConcurrentDictionary<string, bool>(StringComparer.Ordinal);

                        Parallel.ForEachAsync(downloadTasks, new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (task, ct) =>
                        {
                            var filename = task.IsGray ? $"{task.Name}_gray.jpg" : $"{task.Name}.jpg";
                            var destPath = Path.Combine(imagesDir, filename);
                            try
                            {
                                var bytes = await _dlClient.GetByteArrayAsync(task.Url, ct).ConfigureAwait(false);
                                await File.WriteAllBytesAsync(destPath, bytes, ct).ConfigureAwait(false);
                                downloaded[filename] = true;
                            }
                            catch { }
                        }).GetAwaiter().GetResult();

                        foreach (var dict in achList)
                        {
                            var achName = dict.TryGetValue("name", out var n) ? n as string : null;
                            if (string.IsNullOrEmpty(achName)) continue;

                            var normalFile = $"{achName}.jpg";
                            var grayFile = $"{achName}_gray.jpg";

                            if (downloaded.ContainsKey(normalFile))
                                dict["icon"] = $"achievement_images/{normalFile}";
                            if (downloaded.ContainsKey(grayFile))
                                dict["icongray"] = $"achievement_images/{grayFile}";
                        }
                    }

                    if (achList.Count > 0)
                    {
                        var achPath = Path.Combine(starDir, "achievements.json");
                        var options = new JsonSerializerOptions { WriteIndented = true };
                        File.WriteAllText(achPath, JsonSerializer.Serialize(achList, options));
                    }
                }
            }
            catch { }
        }
    }
}
