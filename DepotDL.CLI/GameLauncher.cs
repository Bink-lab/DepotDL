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

        public static bool EnsureGbeApplied(string appId, string gameDir, string? luaPath = null, string? steamWebApiKey = null, bool downloadAchievementIcons = true)
        {
            if (string.IsNullOrEmpty(gameDir) || !Directory.Exists(gameDir))
                return false;

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
                    var libPattern = OperatingSystem.IsMacOS() ? "*.dylib" : "*.so";
                    var steamApiLib = OperatingSystem.IsMacOS() ? "libsteam_api.dylib" : "libsteam_api.so";
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
                try { File.WriteAllText(Path.Combine(gameDir, "sff_fix_error.log"), "Failed to scan game directory for Steam API files."); } catch { }
                return false;
            }

            var primaryFile = steamApiFiles.FirstOrDefault(f => Path.GetFileName(f).ToLowerInvariant() == "steam_api64.dll")
                                ?? steamApiFiles.FirstOrDefault();
            var settingsDir = Path.Combine(primaryFile != null ? Path.GetDirectoryName(primaryFile)! : gameDir, "steam_settings");

            if (File.Exists(launchPath) && Directory.Exists(settingsDir))
            {
                var goldbergApplied = steamApiFiles.Any(f =>
                {
                    var og = Path.Combine(Path.GetDirectoryName(f)!, "OG_" + Path.GetFileName(f));
                    if (!File.Exists(og)) return false;
                    try
                    {
                        if (new FileInfo(f).Length == new FileInfo(og).Length) return false;
                    }
                    catch (IOException) { }
                    return true;
                });
                if (goldbergApplied)
                {
                    var achPath = Path.Combine(settingsDir, "achievements.json");
                    if (!string.IsNullOrWhiteSpace(steamWebApiKey) && !File.Exists(achPath))
                        FetchAchievementsAndStats(appId, settingsDir, steamWebApiKey, downloadAchievementIcons);
                    return true;
                }
            }

            var toolsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tools");
            var steamlessExe = Path.Combine(toolsPath, "steamless", "Steamless.CLI.exe");
            var goldbergDir = Path.Combine(toolsPath, "goldberg");

            if (!Directory.Exists(toolsPath))
            {
                toolsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory);
                steamlessExe = Path.Combine(toolsPath, "steamless", "Steamless.CLI.exe");
                goldbergDir = Path.Combine(toolsPath, "goldberg");
                if (!Directory.Exists(goldbergDir))
                    return false;
            }

            var forceColdClient = false;

            try
            {
                var url = $"https://store.steampowered.com/api/appdetails?appids={appId}";
                var response = _apiClient.GetStringAsync(url).GetAwaiter().GetResult();
                using var doc = JsonDocument.Parse(response);
                if (doc.RootElement.TryGetProperty(appId, out var appData) && appData.GetProperty("success").GetBoolean())
                {
                    var data = appData.GetProperty("data");
                    if (data.TryGetProperty("drm_notice", out var drmNoticeProp))
                    {
                        var drmNotice = drmNoticeProp.GetString() ?? "";
                        if (drmNotice.Contains("denuvo", StringComparison.OrdinalIgnoreCase))
                        {
                            File.WriteAllText(Path.Combine(gameDir, "sff_fix_error.log"), "ABORT: Denuvo DRM detected - cannot be bypassed by Goldberg.");
                            return false;
                        }
                        if (!string.IsNullOrEmpty(drmNotice))
                        {
                            forceColdClient = true;
                        }
                    }
                    if (data.TryGetProperty("ext_user_account_notice", out var extNoticeProp))
                    {
                        var extNotice = extNoticeProp.GetString() ?? "";
                        if (!string.IsNullOrEmpty(extNotice))
                        {
                            File.WriteAllText(Path.Combine(gameDir, "sff_fix_error.log"), $"ABORT: Game requires third-party account - may not work: {extNotice}");
                            return false;
                        }
                    }
                }
            }
            catch { }

            if (File.Exists(steamlessExe))
            {
                if (!System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows))
                {
                    // Steamless is a Windows-only tool; skip on non-Windows platforms
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
                                    var exited = proc.WaitForExit(60000); // 60 second timeout
                                    if (!exited)
                                    {
                                        try { proc.Kill(); } catch { }
                                        File.WriteAllText(Path.Combine(gameDir, "sff_fix_error.log"), $"SteamStub unpacking timed out for {exe}");
                                    }
                                    else if (proc.ExitCode == 0)
                                    {
                                        success = true;
                                    }
                                    else
                                    {
                                        File.WriteAllText(Path.Combine(gameDir, "sff_fix_error.log"), $"SteamStub unpacking failed with exit code {proc.ExitCode} for {exe}");
                                    }
                                }
                            }
                            catch (Exception procEx)
                            {
                                File.WriteAllText(Path.Combine(gameDir, "sff_fix_error.log"), $"SteamStub process error: {procEx.Message}");
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

                            // Cleanup unpacked files only on success or after timeout/failure
                            if (File.Exists(unpackedPath1)) try { File.Delete(unpackedPath1); } catch { }
                            if (File.Exists(unpackedPath2)) try { File.Delete(unpackedPath2); } catch { }
                        }
                    }
                    catch (Exception ex)
                    {
                        File.WriteAllText(Path.Combine(gameDir, "sff_fix_error.log"), $"SteamStub unpacking error: {ex.Message}");
                        return false;
                    }
                }
            }

            Directory.CreateDirectory(settingsDir);

            try
            {
                File.WriteAllText(Path.Combine(gameDir, "steam_appid.txt"), appId);

                var dlcList = new List<string>();
                if (!string.IsNullOrEmpty(luaPath) && File.Exists(luaPath))
                {
                    var luaContent = File.ReadAllText(luaPath);
                    var dlcRegex = new Regex(@"\[""(\d{4,})""\]\s*=\s*""([^""]+)""");
                    foreach (Match m in dlcRegex.Matches(luaContent))
                    {
                        var dlcId = m.Groups[1].Value;
                        var dlcName = m.Groups[2].Value;
                        if (dlcId == appId) continue;
                        dlcList.Add($"{dlcId}={dlcName}");
                    }
                }

                var configsAppLines = new List<string>
                {
                    "[app::general]",
                    "is_beta_branch=0",
                    "branch_name=public",
                    "",
                    "[app::dlcs]",
                    string.Join(Environment.NewLine, dlcList),
                    "unlock_all=1"
                };
                File.WriteAllLines(Path.Combine(settingsDir, "configs.app.ini"), configsAppLines);

                var configsMain = @"[main::general]
new_app_ticket=1
gc_token=1
block_unknown_clients=0
steam_deck=0
enable_voice_chat=0

[main::stats]
disable_leaderboards_create_unknown=0
allow_unknown_stats=1
stat_achievement_progress_functionality=1
save_only_higher_stat_achievement_progress=1

[main::connectivity]
disable_lan_only=1
disable_networking=0
listen_port=47584
offline=0
disable_sharing_stats_with_gameserver=0
share_leaderboards_over_network=0
disable_lobby_creation=0

[main::misc]
achievements_bypass=0
force_steamhttp_success=0
download_steamhttp_requests=0";
                File.WriteAllText(Path.Combine(settingsDir, "configs.main.ini"), configsMain);

                var configsOverlay = @"[overlay::general]
enable_experimental_overlay=1";
                File.WriteAllText(Path.Combine(settingsDir, "configs.overlay.ini"), configsOverlay);

                FetchAchievementsAndStats(appId, settingsDir, steamWebApiKey, downloadAchievementIcons);

                var languages = @"english
french
italian
german
spanish
arabic
japanese
koreana
polish
brazilian
russian
schinese
latam
tchinese";
                File.WriteAllText(Path.Combine(settingsDir, "supported_languages.txt"), languages);

                var controllerDir = Path.Combine(settingsDir, "controller");
                Directory.CreateDirectory(controllerDir);
                var controls = @"AxisL=LJOY=joystick_move
AxisR=RJOY=joystick_move
AnalogL=LTRIGGER=trigger
AnalogR=RTRIGGER=trigger
LUp=DUP
LDown=DDOWN
LLeft=DLEFT
LRight=DRIGHT
RUp=Y
RDown=A
RLeft=X
RRight=B
CLeft=BACK
CRight=START
LStickPush=LSTICK
RStickPush=RSTICK
LTrigTop=LBUMPER
RTrigTop=RBUMPER";
                File.WriteAllText(Path.Combine(controllerDir, "controls.txt"), controls);

                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var gseSaves = Path.Combine(appData, "GSE Saves");
                var gseSettings = Path.Combine(gseSaves, "settings");
                Directory.CreateDirectory(gseSettings);
                Directory.CreateDirectory(Path.Combine(gseSaves, appId));

                var configsUser = @"[user::general]
account_name=Player
account_steamid=76561198000000001
language=english
ip_country=US

[user::saves]
saves_folder_name=GSE Saves";
                File.WriteAllText(Path.Combine(gseSettings, "configs.user.ini"), configsUser);
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(gameDir, "sff_fix_error.log"), $"Config generation error: {ex.Message}");
                return false;
            }

            var replaced = false;

            try
            {
                if (isWindows)
                {
                    foreach (var dll in steamApiFiles)
                    {
                        var nameLower = Path.GetFileName(dll).ToLowerInvariant();

                        var backupPath = Path.Combine(Path.GetDirectoryName(dll)!, "OG_" + Path.GetFileName(dll));
                        if (File.Exists(backupPath))
                        {
                            GenerateInterfacesFile(backupPath, settingsDir);
                        }
                        else
                        {
                            GenerateInterfacesFile(dll, settingsDir);
                            File.Copy(dll, backupPath, true);
                        }

                        var sourceDll = Path.Combine(goldbergDir, nameLower);
                        if (File.Exists(sourceDll))
                        {
                            File.Copy(sourceDll, dll, true);
                            replaced = true;
                        }

                        if (forceColdClient)
                        {
                            var clientName = nameLower == "steam_api.dll" ? "steamclient.dll" : "steamclient64.dll";
                            var loaderName = nameLower == "steam_api.dll" ? "steamclient_loader_x32.exe" : "steamclient_loader_x64.exe";

                            var sourceClient = Path.Combine(goldbergDir, clientName);
                            var sourceLoader = Path.Combine(goldbergDir, loaderName);

                            var dllDir = Path.GetDirectoryName(dll)!;
                            if (File.Exists(sourceClient)) File.Copy(sourceClient, Path.Combine(dllDir, clientName), true);
                            if (File.Exists(sourceLoader)) File.Copy(sourceLoader, Path.Combine(dllDir, loaderName), true);
                        }
                    }
                }
                else
                {
                    foreach (var so in steamApiFiles)
                    {
                        var nameLower = Path.GetFileName(so).ToLowerInvariant();

                        var backupPath = Path.Combine(Path.GetDirectoryName(so)!, "OG_" + Path.GetFileName(so));
                        if (File.Exists(backupPath))
                        {
                            GenerateInterfacesFile(backupPath, settingsDir);
                        }
                        else
                        {
                            GenerateInterfacesFile(so, settingsDir);
                            File.Copy(so, backupPath, true);
                        }

                        var sourceSo = Path.Combine(goldbergDir, nameLower);
                        if (File.Exists(sourceSo))
                        {
                            File.Copy(sourceSo, so, true);
                            replaced = true;
                        }

                        if (forceColdClient)
                        {
                            // Linux cold client files (adjust names as needed for Goldberg Linux distribution)
                            var clientName = "libsteamclient.so";
                            var loaderName = "steamclient_loader";

                            var sourceClient = Path.Combine(goldbergDir, clientName);
                            var sourceLoader = Path.Combine(goldbergDir, loaderName);

                            var soDir = Path.GetDirectoryName(so)!;
                            if (File.Exists(sourceClient)) File.Copy(sourceClient, Path.Combine(soDir, clientName), true);
                            if (File.Exists(sourceLoader))
                            {
                                var destLoader = Path.Combine(soDir, loaderName);
                                File.Copy(sourceLoader, destLoader, true);
                                try
                                {
                                    var chmod = Process.Start(new ProcessStartInfo
                                    {
                                        FileName = "chmod",
                                        Arguments = $"+x \"{destLoader}\"",
                                        UseShellExecute = false,
                                        CreateNoWindow = true
                                    });
                                    chmod?.WaitForExit();
                                }
                                catch { }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                File.WriteAllText(Path.Combine(gameDir, "sff_fix_error.log"), $"Goldberg application error: {ex.Message}");
                return false;
            }

            if (!replaced)
            {
                var apiFileType = isWindows ? "steam_api DLLs" : OperatingSystem.IsMacOS() ? "libsteam_api.dylib" : "libsteam_api.so";
                File.WriteAllText(Path.Combine(gameDir, "sff_fix_error.log"), $"No {apiFileType} found to replace.");
                return false;
            }

            try
            {
                var mainExe = FindMainExe(gameDir);
                if (!string.IsNullOrEmpty(mainExe))
                {
                    var exeRel = Path.GetRelativePath(gameDir, mainExe);
                    var isWindowsPE = mainExe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

                    if (forceColdClient)
                    {
                        var is64 = exeRel.EndsWith("64.exe", StringComparison.OrdinalIgnoreCase) || mainExe.Contains("x64", StringComparison.OrdinalIgnoreCase) || mainExe.Contains("Win64", StringComparison.OrdinalIgnoreCase);
                        var loader = isWindows ? (is64 ? "steamclient_loader_x64.exe" : "steamclient_loader_x32.exe") : "steamclient_loader";
                        var loaders = Directory.GetFiles(gameDir, loader, SearchOption.AllDirectories);
                        if (loaders.Length > 0)
                        {
                            var loaderRel = Path.GetRelativePath(gameDir, loaders[0]);
                            if (OperatingSystem.IsWindows())
                            {
                                var content = $"@echo off\ncd /d \"%~dp0{Path.GetDirectoryName(loaderRel)}\"\nstart \"\" \"{Path.GetFileName(loaderRel)}\"\n";
                                File.WriteAllText(launchPath, content);
                            }
                            else
                            {
                                var loaderIsWindowsPE = loaderRel.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);
                                if (loaderIsWindowsPE)
                                {
                                    var content = $"#!/bin/sh\ncd \"$(dirname \"$0\")/{Path.GetDirectoryName(loaderRel)}\"\nexec wine \"./{Path.GetFileName(loaderRel)}\" \"$@\"\n";
                                    File.WriteAllText(launchPath, content);
                                }
                                else
                                {
                                    var content = $"#!/bin/sh\ncd \"$(dirname \"$0\")/{Path.GetDirectoryName(loaderRel)}\"\nexec \"./{Path.GetFileName(loaderRel)}\" \"$@\"\n";
                                    File.WriteAllText(launchPath, content);
                                }
                            }
                        }
                    }
                    else
                    {
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
            }
            catch { }

            return true;
        }

        private static void GenerateInterfacesFile(string dllPath, string settingsDir)
        {
            try
            {
                var data = File.ReadAllBytes(dllPath);
                var content = System.Text.Encoding.Latin1.GetString(data);

                var matches = new HashSet<string>();
                var interfaceRegex = new Regex(
                    @"(SteamClient|SteamGameServer|SteamGameServerStats|SteamUser|SteamFriends|SteamUtils|SteamMatchMaking|SteamMatchMakingServers|SteamUserStats|SteamApps|SteamNetworking|SteamRemoteStorage|SteamScreenshots|SteamHTTP|SteamController|SteamUGC|SteamAppList|SteamMusic|SteamMusicRemote|SteamHTMLSurface|SteamInventory|SteamVideo|SteamParentalSettings|SteamInput|SteamParties|SteamRemotePlay|SteamNetworkingMessages|SteamNetworkingSockets|SteamNetworkingUtils|SteamGameSearch|SteamTimeline)\d{3}");

                foreach (Match match in interfaceRegex.Matches(content))
                {
                    matches.Add(match.Value);
                }

                if (matches.Count > 0)
                {
                    var list = new List<string>(matches);
                    list.Sort();
                    var outPath = Path.Combine(settingsDir, "steam_interfaces.txt");
                    File.WriteAllLines(outPath, list);
                }
            }
            catch { }
        }

        private static void FetchAchievementsAndStats(string appId, string settingsDir, string? userKey = null, bool downloadAchievementIcons = true)
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
                    game.TryGetProperty("availableGameStats", out var availableStats))
                {
                    if (availableStats.TryGetProperty("achievements", out var achievements))
                    {
                        var achList = new List<Dictionary<string, object>>();
                        // Collect image download tasks: (url, name, isGray)
                        var downloadTasks = new List<(string Url, string Name, bool IsGray)>();

                        foreach (var ach in achievements.EnumerateArray())
                        {
                            var dict = new Dictionary<string, object>();
                            foreach (var prop in ach.EnumerateObject())
                            {
                                if (prop.Name == "hidden")
                                {
                                    if (prop.Value.ValueKind == JsonValueKind.Number)
                                    {
                                        dict[prop.Name] = prop.Value.GetInt32().ToString();
                                    }
                                    else if (prop.Value.ValueKind == JsonValueKind.True)
                                    {
                                        dict[prop.Name] = "1";
                                    }
                                    else if (prop.Value.ValueKind == JsonValueKind.False)
                                    {
                                        dict[prop.Name] = "0";
                                    }
                                    else
                                    {
                                        dict[prop.Name] = prop.Value.ToString();
                                    }
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
                                // Collect icon URLs for downloading; queue them keyed by achievement name
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

                        // Download images concurrently (up to 8 parallel), then rewrite icon paths to local relative paths
                        if (downloadAchievementIcons && downloadTasks.Count > 0)
                        {
                            var imagesDir = Path.Combine(settingsDir, "achievement_images");
                            Directory.CreateDirectory(imagesDir);

                            // Track which files were successfully downloaded
                            var downloaded = new System.Collections.Concurrent.ConcurrentDictionary<string, bool>(StringComparer.Ordinal);

                            Parallel.ForEachAsync(downloadTasks, new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (task, ct) =>
                            {
                                var filename = task.IsGray
                                    ? $"{task.Name}_gray.jpg"
                                    : $"{task.Name}.jpg";
                                var destPath = Path.Combine(imagesDir, filename);
                                try
                                {
                                    var bytes = await _dlClient.GetByteArrayAsync(task.Url, ct).ConfigureAwait(false);
                                    await File.WriteAllBytesAsync(destPath, bytes, ct).ConfigureAwait(false);
                                    downloaded[filename] = true;
                                }
                                catch { }
                            }).GetAwaiter().GetResult();

                            // Rewrite icon/icongray fields in achList to local relative paths where downloaded
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
                            var achPath = Path.Combine(settingsDir, "achievements.json");
                            var options = new JsonSerializerOptions { WriteIndented = true };
                            File.WriteAllText(achPath, JsonSerializer.Serialize(achList, options));
                        }
                    }

                    if (availableStats.TryGetProperty("stats", out var stats))
                    {
                        var statsList = new List<Dictionary<string, string>>();
                        foreach (var stat in stats.EnumerateArray())
                        {
                            var dict = new Dictionary<string, string>();
                            var name = "";
                            if (stat.TryGetProperty("name", out var nameProp))
                                name = nameProp.GetString() ?? "";

                            var type = "int";
                            if (stat.TryGetProperty("type", out var typeProp))
                            {
                                if (typeProp.ValueKind == JsonValueKind.Number)
                                {
                                    var typeVal = typeProp.GetInt32();
                                    type = typeVal switch
                                    {
                                        1 => "int",
                                        2 => "float",
                                        3 => "avgrate",
                                        _ => "int"
                                    };
                                }
                                else
                                {
                                    type = typeProp.GetString() ?? "int";
                                }
                            }

                            var defVal = "0";
                            if (stat.TryGetProperty("defaultvalue", out var defProp))
                            {
                                if (defProp.ValueKind == JsonValueKind.Number)
                                    defVal = defProp.GetDouble().ToString();
                                else
                                    defVal = defProp.ToString();
                            }

                            dict["name"] = name;
                            dict["type"] = type;
                            dict["default"] = defVal;
                            dict["global"] = "0";
                            statsList.Add(dict);
                        }

                        if (statsList.Count > 0)
                        {
                            var statsPath = Path.Combine(settingsDir, "stats.json");
                            var options = new JsonSerializerOptions { WriteIndented = true };
                            File.WriteAllText(statsPath, JsonSerializer.Serialize(statsList, options));
                        }
                    }
                }
            }
            catch { }
        }
    }
}
