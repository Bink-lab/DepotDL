using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Diagnostics;
using System.Text;

namespace DepotDL.CLI
{
    public static class TuiDashboard
    {
        public static int RunInteractiveTui(string ddmodPath, string dotnetPath)
        {
            try { if (OperatingSystem.IsWindows()) Console.CursorVisible = false; } catch {}
            var session = new TuiSession();
            
            string defaultManifestsDir = "manifests";
            if (!Directory.Exists(defaultManifestsDir))
            {
                if (Directory.Exists("../manifests")) defaultManifestsDir = "../manifests";
                else if (Directory.Exists("depotcache")) defaultManifestsDir = "depotcache";
                else if (Directory.Exists("../depotcache")) defaultManifestsDir = "../depotcache";
            }
            session.ManifestsDir = defaultManifestsDir;
            IniSettings.LoadInto(session);

            Console.Clear();
            WriteCenteredStatusBox("DEPOTDL", new[] { "Initializing library and scanning systems" }, ConsoleColor.Cyan);

            int verified = LibraryManager.VerifyLibraryOnStartup(out int totalCount, out int missingCount);
            System.Threading.Thread.Sleep(800);

            int menuIndex = 0;
            while (true)
            {
                Console.Clear();

                string luaName = string.IsNullOrEmpty(session.LuaPath) ? "[None Loaded]" : Path.GetFileName(session.LuaPath);
                luaName = TuiText.Shorten(luaName, 21);
                string appId = string.IsNullOrEmpty(session.AppId) ? "[None]" : session.AppId;
                string depotSel = session.AllDepots.Count == 0 
                    ? "0 Depots (No Lua)" 
                    : $"{session.SelectedDepots.Count}/{session.AllDepots.Count} Selected";
                string manifestsVal = string.IsNullOrEmpty(session.ManifestsDir) ? "[Default]" : session.ManifestsDir;
                manifestsVal = TuiText.ShortenTail(manifestsVal, 21);
                string outputVal = string.IsNullOrEmpty(session.OutputDir) ? "[Auto]" : session.OutputDir;
                outputVal = TuiText.ShortenTail(outputVal, 21);
                string outputBaseVal = string.IsNullOrEmpty(session.DownloadBaseDir) ? "[Auto]" : session.DownloadBaseDir;
                outputBaseVal = TuiText.ShortenTail(outputBaseVal, 21);
                string libraryStats = $"{verified}/{totalCount} Games";

                var leftItems = new List<string>
                {
                    "1. Manage Game Library",
                    "2. Import Configs/Manifests (ZIP)",
                    "3. Choose Game Lua Config File",
                    "4. Configure Manifests Cache",
                    "5. Configure Output Folder",
                    "6. Application Settings",
                    "7. Start Download Process",
                    "8. Exit Application"
                };

                var rightStats = new List<(string Key, string Val, ConsoleColor Color)>
                {
                    ("Active Lua File:", luaName, string.IsNullOrEmpty(session.LuaPath) ? ConsoleColor.Yellow : ConsoleColor.White),
                    ("App ID Target:", appId, string.IsNullOrEmpty(session.AppId) ? ConsoleColor.DarkGray : ConsoleColor.Green),
                    ("Selected Depots:", depotSel, session.SelectedDepots.Count == 0 ? ConsoleColor.DarkGray : ConsoleColor.White),
                    ("Manifests Cache:", manifestsVal, ConsoleColor.Gray),
                    ("Download Folder:", outputVal, string.IsNullOrEmpty(session.OutputDir) ? ConsoleColor.DarkGray : ConsoleColor.Gray),
                    ("Output Base:", outputBaseVal, string.IsNullOrEmpty(session.DownloadBaseDir) ? ConsoleColor.DarkGray : ConsoleColor.Gray),
                    ("Parallel Workers:", session.MaxParallelDepots.ToString(), ConsoleColor.Cyan),
                    ("Library Index:", libraryStats, missingCount > 0 ? ConsoleColor.Yellow : ConsoleColor.Green)
                };

                using (CenterConsoleOutput(82))
                {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("╔════════════════════════════════════════╦════════════════════════════════════════╗");
                Console.Write("║ ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write(TuiText.Pad("DepotDL", 38));
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write(" ║ ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write(TuiText.Pad("ACTIVE SESSION STATUS", 38));
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine(" ║");
                Console.WriteLine("╠════════════════════════════════════════╬════════════════════════════════════════╣");

                int maxRows = Math.Max(leftItems.Count, rightStats.Count);
                for (int i = 0; i < maxRows; i++)
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write("║ ");

                    if (i < leftItems.Count)
                    {
                        if (i == menuIndex)
                        {
                            Console.BackgroundColor = ConsoleColor.Cyan;
                            Console.ForegroundColor = ConsoleColor.Black;
                            Console.Write(TuiText.Pad($"> {leftItems[i]}", 38));
                            Console.ResetColor();
                        }
                        else
                        {
                            if (i == 6 && string.IsNullOrEmpty(session.LuaPath))
                            {
                                Console.ForegroundColor = ConsoleColor.DarkGray;
                            }
                            else if (i == 6)
                            {
                                Console.ForegroundColor = ConsoleColor.Green;
                            }
                            else
                            {
                                Console.ForegroundColor = ConsoleColor.White;
                            }
                            Console.Write(TuiText.Pad($"  {leftItems[i]}", 38));
                        }
                    }
                    else
                    {
                        Console.Write(new string(' ', 38));
                    }

                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write(" ║ ");

                    if (i < rightStats.Count)
                    {
                        var stat = rightStats[i];
                        Console.ForegroundColor = ConsoleColor.DarkCyan;
                        Console.Write(TuiText.Pad(stat.Key, 17));
                        Console.ForegroundColor = stat.Color;
                        Console.Write(TuiText.Pad(stat.Val, 21));
                    }
                    else
                    {
                        Console.Write(new string(' ', 38));
                    }

                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine(" ║");
                }

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("╚════════════════════════════════════════╩════════════════════════════════════════╝");
                
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine("══════════════════════════════════════════════════════════════════════════════════");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("  [↑/↓] Navigate   ");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("│");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("   [Enter] Select   ");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("│");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("   [L] Library   ");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("│");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("   [Esc] Exit / Back");
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine("══════════════════════════════════════════════════════════════════════════════════");
                Console.ResetColor();
                }

                var key = Console.ReadKey(true).Key;
                if (key == ConsoleKey.UpArrow)
                {
                    menuIndex = (menuIndex - 1 + leftItems.Count) % leftItems.Count;
                }
                else if (key == ConsoleKey.DownArrow)
                {
                    menuIndex = (menuIndex + 1) % leftItems.Count;
                }
                else if (key == ConsoleKey.Escape)
                {
                    SaveSession(session);
                    Console.Clear();
                    try { if (OperatingSystem.IsWindows()) Console.CursorVisible = true; } catch {}
                    return 0;
                }
                else if (key == ConsoleKey.L)
                {
                    RunLibraryDashboard(session, ddmodPath, dotnetPath);
                    SaveSession(session);
                    verified = LibraryManager.VerifyLibraryOnStartup(out totalCount, out missingCount);
                }
                else if (key == ConsoleKey.Enter)
                {
                    if (menuIndex == 0)
                    {
                        RunLibraryDashboard(session, ddmodPath, dotnetPath);
                        SaveSession(session);
                        verified = LibraryManager.VerifyLibraryOnStartup(out totalCount, out missingCount);
                    }
                    else if (menuIndex == 1)
                    {
                        RunZipImportAction(session);
                    }
                    else if (menuIndex == 2)
                    {
                        RunChooseLuaAction(session);
                    }
                    else if (menuIndex == 3)
                    {
                        RunManifestCacheManager(session);
                    }
                    else if (menuIndex == 4)
                    {
                        RunConfigureOutputAction(session);
                    }
                    else if (menuIndex == 5)
                    {
                        RunApplicationSettings(session);
                        verified = LibraryManager.VerifyLibraryOnStartup(out totalCount, out missingCount);
                    }
                    else if (menuIndex == 6)
                    {
                        if (string.IsNullOrEmpty(session.LuaPath))
                        {
                            PromptText("DOWNLOAD PROCESS", "No Lua config file loaded! Please select a Lua file first. Press Enter to return.", "");
                            continue;
                        }

                        var downloadableDepots = LibraryManager.FilterDownloadableDepots(session.AllDepots, session.AppId);
                        if (downloadableDepots.Count == 0)
                        {
                            PromptText("DOWNLOAD PROCESS", "No downloadable depots with decryption keys found. Press Enter to return.", "");
                            continue;
                        }

                        var selectedDownloadableDepots = LibraryManager.FilterDownloadableDepots(session.SelectedDepots, session.AppId);
                        var chosenDepots = RunCheckboxSelector($"SELECT DEPOTS FOR APP {session.AppId}", downloadableDepots, selectedDownloadableDepots);
                        if (chosenDepots == null)
                        {
                            continue;
                        }
                        if (chosenDepots.Count == 0)
                        {
                            PromptText("DOWNLOAD PROCESS", "No depots selected for download! Press Enter to return.", "");
                            continue;
                        }
                        session.SelectedDepots = chosenDepots;

                        if (string.IsNullOrEmpty(session.OutputDir))
                        {
                            session.OutputDir = BuildOutputDir(session, GetGameName(session));
                        }
                        SaveSession(session);

                        // Pre-download summary: manifest matches + resume status
                        {
                            int matchedManifests = 0;
                            int depotsWithManifestId = session.SelectedDepots.Count(d => !string.IsNullOrEmpty(d.ManifestId));
                            if (depotsWithManifestId > 0 && !string.IsNullOrEmpty(session.ManifestsDir) && Directory.Exists(session.ManifestsDir))
                            {
                                var localNames = Directory.GetFiles(session.ManifestsDir, "*.manifest")
                                    .Select(f => Path.GetFileNameWithoutExtension(f))
                                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                                foreach (var depot in session.SelectedDepots)
                                {
                                    if (!string.IsNullOrEmpty(depot.ManifestId))
                                    {
                                        if (localNames.Contains($"{depot.DepotId}_{depot.ManifestId}") || localNames.Contains(depot.ManifestId))
                                            matchedManifests++;
                                    }
                                }
                            }

                            string outputForCheck = !string.IsNullOrEmpty(session.OutputDir) ? session.OutputDir : string.Empty;
                            int alreadyDone = 0;
                            if (!string.IsNullOrEmpty(outputForCheck))
                            {
                                var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                try
                                {
                                    var dir = Path.Combine(outputForCheck, ".depotdl_progress");
                                    if (Directory.Exists(dir))
                                        done = Directory.GetFiles(dir, "*.done").Select(f => Path.GetFileNameWithoutExtension(f)).ToHashSet(StringComparer.OrdinalIgnoreCase);
                                }
                                catch { }
                                alreadyDone = session.SelectedDepots.Count(d => done.Contains(d.DepotId));
                            }

                            var summaryLines = new List<string> { $"Depots to download: {session.SelectedDepots.Count}" };
                            if (depotsWithManifestId > 0)
                                summaryLines.Add($"Local manifests matched: {matchedManifests} / {depotsWithManifestId}");
                            if (matchedManifests < depotsWithManifestId)
                                summaryLines.Add($"{depotsWithManifestId - matchedManifests} manifest(s) will be fetched from Steam");
                            if (alreadyDone > 0)
                                summaryLines.Add($"Resume: {alreadyDone} previously-completed depot(s) will be skipped");

                            Console.Clear();
                            WriteCenteredStatusBox("STARTING DOWNLOAD", summaryLines, ConsoleColor.Cyan);
                            System.Threading.Thread.Sleep(1000);
                        }

                        Console.Clear();

                        int exitCode = Program.TriggerDownloadProcess(session.LuaPath, session.ManifestsDir, session.OutputDir, ddmodPath, dotnetPath, session.SelectedDepots, session.MaxParallelDepots);

                        if (exitCode == 0)
                        {
                            PromptText("DOWNLOAD SUCCESS", "Game downloaded and registered successfully! Press Enter.", "");
                        }
                        else
                        {
                            PromptText("DOWNLOAD FAILED", $"Download finished with exit code: {exitCode}. Press Enter.", "");
                        }
                        verified = LibraryManager.VerifyLibraryOnStartup(out totalCount, out missingCount);
                    }
                    else if (menuIndex == 7)
                    {
                        SaveSession(session);
                        Console.Clear();
                        try { if (OperatingSystem.IsWindows()) Console.CursorVisible = true; } catch {}
                        return 0;
                    }
                }
            }
        }

        private static void RunLibraryDashboard(TuiSession session, string ddmodPath, string dotnetPath)
        {
            int selectedIndex = 0;
            while (true)
            {
                Console.Clear();
                var games = LibraryManager.LoadLibrary();
                int totalMenuItems = games.Count + 2;
                if (selectedIndex >= totalMenuItems) selectedIndex = totalMenuItems - 1;
                if (selectedIndex < 0) selectedIndex = 0;

                using (CenterConsoleOutput(88))
                {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("╔════════════════════════════════════════╦════════════════╦═══════════════╦════════════╗");
                Console.Write("║ ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write(TuiText.Pad("GAME NAME", 38));
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write(" ║ ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write(TuiText.Pad("APP ID", 14));
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write(" ║ ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write(TuiText.Pad("TOTAL SIZE", 13));
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write(" ║ ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write(TuiText.Pad("STATUS", 10));
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine(" ║");
                Console.WriteLine("╠════════════════════════════════════════╬════════════════╬═══════════════╬════════════╣");

                for (int i = 0; i < totalMenuItems; i++)
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write("║ ");

                    if (i == selectedIndex)
                    {
                        Console.BackgroundColor = ConsoleColor.Cyan;
                        Console.ForegroundColor = ConsoleColor.Black;
                        
                        if (i < games.Count)
                        {
                            var g = games[i];
                            Console.Write(TuiText.Pad($"> {g.GameName}", 38));
                            Console.ResetColor();
                            Console.ForegroundColor = ConsoleColor.DarkGray;
                            Console.Write(" ║ ");
                            Console.ForegroundColor = ConsoleColor.White;
                            Console.Write(TuiText.Pad(g.AppId, 14));
                            Console.ForegroundColor = ConsoleColor.DarkGray;
                            Console.Write(" ║ ");
                            Console.ForegroundColor = ConsoleColor.White;
                            Console.Write(TuiText.Pad(FormatSize(g.TotalSizeBytes), 13));
                            Console.ForegroundColor = ConsoleColor.DarkGray;
                            Console.Write(" ║ ");
                            Console.ForegroundColor = g.IsVerified ? ConsoleColor.Green : ConsoleColor.Red;
                            Console.Write(TuiText.Pad(g.IsVerified ? "Verified" : "Missing", 10));
                        }
                        else if (i == games.Count)
                        {
                            Console.Write(TuiText.Pad("> [BATCH OPERATIONS...]", 38));
                            Console.ResetColor();
                            Console.ForegroundColor = ConsoleColor.DarkGray;
                            Console.Write(" ║ ");
                            Console.Write(TuiText.Pad("", 14));
                            Console.Write(" ║ ");
                            Console.Write(TuiText.Pad("", 13));
                            Console.Write(" ║ ");
                            Console.Write(TuiText.Pad("", 10));
                        }
                        else
                        {
                            Console.Write(TuiText.Pad("> [BACK TO DASHBOARD]", 38));
                            Console.ResetColor();
                            Console.ForegroundColor = ConsoleColor.DarkGray;
                            Console.Write(" ║ ");
                            Console.Write(TuiText.Pad("", 14));
                            Console.Write(" ║ ");
                            Console.Write(TuiText.Pad("", 13));
                            Console.Write(" ║ ");
                            Console.Write(TuiText.Pad("", 10));
                        }
                        
                        Console.ResetColor();
                    }
                    else
                    {
                        if (i < games.Count)
                        {
                            var g = games[i];
                            Console.ForegroundColor = ConsoleColor.White;
                            Console.Write(TuiText.Pad($"  {g.GameName}", 38));
                            Console.ForegroundColor = ConsoleColor.DarkGray;
                            Console.Write(" ║ ");
                            Console.ForegroundColor = ConsoleColor.Gray;
                            Console.Write(TuiText.Pad(g.AppId, 14));
                            Console.ForegroundColor = ConsoleColor.DarkGray;
                            Console.Write(" ║ ");
                            Console.ForegroundColor = ConsoleColor.Gray;
                            Console.Write(TuiText.Pad(FormatSize(g.TotalSizeBytes), 13));
                            Console.ForegroundColor = ConsoleColor.DarkGray;
                            Console.Write(" ║ ");
                            Console.ForegroundColor = g.IsVerified ? ConsoleColor.Green : ConsoleColor.Red;
                            Console.Write(TuiText.Pad(g.IsVerified ? "Verified" : "Missing", 10));
                        }
                        else if (i == games.Count)
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.Write(TuiText.Pad("  [BATCH OPERATIONS...]", 38));
                            Console.ForegroundColor = ConsoleColor.DarkGray;
                            Console.Write(" ║ ");
                            Console.Write(TuiText.Pad("", 14));
                            Console.Write(" ║ ");
                            Console.Write(TuiText.Pad("", 13));
                            Console.Write(" ║ ");
                            Console.Write(TuiText.Pad("", 10));
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Gray;
                            Console.Write(TuiText.Pad("  [BACK TO DASHBOARD]", 38));
                            Console.ForegroundColor = ConsoleColor.DarkGray;
                            Console.Write(" ║ ");
                            Console.Write(TuiText.Pad("", 14));
                            Console.Write(" ║ ");
                            Console.Write(TuiText.Pad("", 13));
                            Console.Write(" ║ ");
                            Console.Write(TuiText.Pad("", 10));
                        }
                    }

                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine(" ║");
                }

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("╚════════════════════════════════════════╩════════════════╩═══════════════╩════════════╝");

                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine("════════════════════════════════════════════════════════════════════════════════════════");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("  [↑/↓] Navigate  ");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("│");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("  [Enter] Select Game  ");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("│");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write("  [B] Batch Actions  ");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("│");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("  [Esc] Dashboard");
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine("════════════════════════════════════════════════════════════════════════════════════════");
                Console.ResetColor();
                }

                var keyInfo = Console.ReadKey(true);
                var key = keyInfo.Key;
                
                if (key == ConsoleKey.UpArrow)
                {
                    selectedIndex = (selectedIndex - 1 + totalMenuItems) % totalMenuItems;
                }
                else if (key == ConsoleKey.DownArrow)
                {
                    selectedIndex = (selectedIndex + 1) % totalMenuItems;
                }
                else if (key == ConsoleKey.Escape)
                {
                    return;
                }
                else if (key == ConsoleKey.B || keyInfo.KeyChar == 'b' || keyInfo.KeyChar == 'B')
                {
                    RunLibraryBatchActions(session, ddmodPath, dotnetPath);
                }
                else if (key == ConsoleKey.Enter)
                {
                    if (selectedIndex == totalMenuItems - 1)
                    {
                        return;
                    }
                    else if (selectedIndex == totalMenuItems - 2)
                    {
                        RunLibraryBatchActions(session, ddmodPath, dotnetPath);
                    }
                    else
                    {
                        if (RunGameDetailsMenu(games[selectedIndex], session, ddmodPath, dotnetPath))
                        {
                            return;
                        }
                    }
                }
            }
        }

        private static bool RunGameDetailsMenu(LibraryGame game, TuiSession session, string ddmodPath, string dotnetPath)
        {
            int selectedIndex = 0;
            while (true)
            {
                Console.Clear();
                var menuItems = new List<string>
                {
                    "1. Open Download Folder in File Explorer",
                    "2. Verify Files (Scan Size)",
                    "3. Deep Verify & Repair (Hash Check & Download)",
                    "4. Load & Re-download/Update Game",
                    "5. Uninstall & Delete Game Files",
                    "6. Back"
                };

                using (CenterConsoleOutput(80))
                {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
                Console.Write("║ ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write(TuiText.Pad($"GAME DETAILS: {game.GameName.ToUpper()}", 76));
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine(" ║");
                Console.WriteLine("╠══════════════════════════════════════════════════════════════════════════════╣");

                void DrawDetailRow(string label, string val, ConsoleColor valColor)
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write("║  ");
                    Console.ForegroundColor = ConsoleColor.DarkCyan;
                    Console.Write(TuiText.Pad(label, 18));
                    Console.ForegroundColor = valColor;
                    
                    Console.Write(TuiText.Pad(val, 56));
                    
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine("  ║");
                }

                DrawDetailRow("App ID Target:", game.AppId, ConsoleColor.Green);
                DrawDetailRow("Lua Config Path:", game.LuaPath, ConsoleColor.Gray);
                DrawDetailRow("Output Folder:", game.OutputDir, ConsoleColor.Gray);
                DrawDetailRow("Depot IDs:", string.Join(", ", game.DepotIds), ConsoleColor.White);
                DrawDetailRow("Install Date:", game.InstallDate.ToString("g"), ConsoleColor.White);
                DrawDetailRow("Total Size:", FormatSize(game.TotalSizeBytes), ConsoleColor.White);
                DrawDetailRow("Verification:", game.IsVerified ? "Verified (Exists on disk)" : "Missing (Directory not found)", game.IsVerified ? ConsoleColor.Green : ConsoleColor.Red);

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
                Console.ResetColor();

                for (int i = 0; i < menuItems.Count; i++)
                {
                    if (i == selectedIndex)
                    {
                        Console.BackgroundColor = ConsoleColor.Cyan;
                        Console.ForegroundColor = ConsoleColor.Black;
                        Console.WriteLine($">  {menuItems[i]}");
                        Console.ResetColor();
                    }
                    else
                    {
                        if (i == 4)
                        {
                            Console.ForegroundColor = ConsoleColor.Red;
                            Console.WriteLine($"   {menuItems[i]}");
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.White;
                            Console.WriteLine($"   {menuItems[i]}");
                        }
                        Console.ResetColor();
                    }
                }

                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine("\n══════════════════════════════════════════════════════════════════════════════════");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("  [↑/↓] Navigate   [Enter] Confirm Selected Action   [Esc] Back to Library");
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine("══════════════════════════════════════════════════════════════════════════════════");
                Console.ResetColor();
                }

                var key = Console.ReadKey(true).Key;
                if (key == ConsoleKey.UpArrow)
                {
                    selectedIndex = (selectedIndex - 1 + menuItems.Count) % menuItems.Count;
                }
                else if (key == ConsoleKey.DownArrow)
                {
                    selectedIndex = (selectedIndex + 1) % menuItems.Count;
                }
                else if (key == ConsoleKey.Escape)
                {
                    return false;
                }
                else if (key == ConsoleKey.Enter)
                {
                    if (selectedIndex == 0)
                    {
                        Console.Clear();
                        try
                        {
                            if (Directory.Exists(game.OutputDir))
                            {
                                Process.Start(new ProcessStartInfo
                                {
                                    FileName = "explorer.exe",
                                    Arguments = $"\"{game.OutputDir}\"",
                                    UseShellExecute = true
                                });
                            }
                            else
                            {
                                PromptText("OPEN FOLDER", "Directory does not exist on disk. Press Enter to return.", "");
                            }
                        }
                        catch (Exception ex)
                        {
                            PromptText("OPEN FOLDER", $"Could not open explorer: {ex.Message}. Press Enter.", "");
                        }
                    }
                    else if (selectedIndex == 1)
                    {
                        Console.Clear();
                        bool exists = Directory.Exists(game.OutputDir);
                        long size = exists ? LibraryManager.GetDirectorySize(game.OutputDir) : 0;
                        
                        game.IsVerified = exists;
                        game.TotalSizeBytes = size;
                        
                        LibraryManager.AddOrUpdateGame(game);
                        
                        if (exists)
                        {
                            PromptText("VERIFY FILES", $"Verified! Updated size: {FormatSize(size)}. Press Enter.", "");
                        }
                        else
                        {
                            PromptText("VERIFY FILES", "Directory missing! Updated database. Press Enter.", "");
                        }
                    }
                    else if (selectedIndex == 2)
                    {
                        Console.Clear();
                        var repairSession = new TuiSession
                        {
                            LuaPath = game.LuaPath,
                            OutputDir = game.OutputDir,
                            ManifestsDir = session.ManifestsDir,
                            MaxParallelDepots = session.MaxParallelDepots,
                            RyuuApiKey = session.RyuuApiKey
                        };

                        ParseLuaFileIntoSession(repairSession);

                        var restoredDepots = new List<DepotInfo>();
                        foreach (var depId in game.DepotIds)
                        {
                            var match = repairSession.AllDepots.Find(d => d.DepotId == depId);
                            if (match != null) restoredDepots.Add(match);
                        }
                        if (restoredDepots.Count > 0)
                        {
                            repairSession.SelectedDepots = restoredDepots;
                        }

                        int exitCode = Program.TriggerDownloadProcess(
                            repairSession.LuaPath, 
                            repairSession.ManifestsDir, 
                            repairSession.OutputDir, 
                            ddmodPath, 
                            dotnetPath, 
                            repairSession.SelectedDepots,
                            repairSession.MaxParallelDepots
                        );

                        if (exitCode == 0)
                        {
                            bool exists = Directory.Exists(game.OutputDir);
                            long size = exists ? LibraryManager.GetDirectorySize(game.OutputDir) : 0;
                            game.IsVerified = exists;
                            game.TotalSizeBytes = size;
                            LibraryManager.AddOrUpdateGame(game);
                            
                            PromptText("VERIFY & REPAIR SUCCESS", $"Deep verification and repair completed successfully! Cleaned and verified size: {FormatSize(size)}. Press Enter.", "");
                        }
                        else
                        {
                            PromptText("VERIFY & REPAIR FAILED", $"Deep verification and repair failed with exit code: {exitCode}. Press Enter.", "");
                        }
                    }
                    else if (selectedIndex == 3)
                    {
                        Console.Clear();
                        session.LuaPath = game.LuaPath;
                        session.OutputDir = game.OutputDir;

                        // Restore DownloadBaseDir from OutputDir so ParseLuaFileIntoSession recomputes correctly
                        var parentDir = Path.GetDirectoryName(game.OutputDir);
                        if (!string.IsNullOrEmpty(parentDir))
                        {
                            session.DownloadBaseDir = parentDir;
                        }

                        ParseLuaFileIntoSession(session);
                        
                        var restoredDepots = new List<DepotInfo>();
                        foreach (var depId in game.DepotIds)
                        {
                            var match = session.AllDepots.Find(d => d.DepotId == depId);
                            if (match != null) restoredDepots.Add(match);
                        }
                        if (restoredDepots.Count > 0)
                        {
                            session.SelectedDepots = restoredDepots;
                        }

                        PromptText("LOAD CONFIG", "Active session populated! Return to Dashboard to download. Press Enter.", "");
                        return true;
                    }
                    else if (selectedIndex == 4)
                    {
                        Console.Clear();
                        using (CenterConsoleOutput(80))
                        {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
                        Console.WriteLine("║                           CONFIRM UNINSTALLATION                             ║");
                        Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
                        Console.ResetColor();
                        Console.WriteLine($"You are about to delete: {game.GameName}");
                        Console.WriteLine($"This will recursively DELETE all files inside: {game.OutputDir}");
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine("\nWARNING: THIS ACTION CANNOT BE UNDONE!");
                        Console.ResetColor();
                        Console.Write("\nAre you absolutely sure you want to uninstall and delete files? (y/N): ");
                        }
                        
                        string? input = Console.ReadLine()?.Trim().ToLower();
                        if (input == "y" || input == "yes")
                        {
                            var deleted = LibraryManager.RobustDeleteDirectory(game.OutputDir);
                            if (deleted)
                            {
                                LibraryManager.RemoveGame(game.AppId);
                                PromptText("UNINSTALL", "Pruned game entry and files cleared. Press Enter.", "");
                            }
                            else
                            {
                                PromptText("UNINSTALL FAILED", "Could not delete directory. Files may be in use. Press Enter.", "");
                            }
                            return false;
                        }
                    }
                    else if (selectedIndex == 5)
                    {
                        return false;
                    }
                }
            }
        }

        private static void RunLibraryBatchActions(TuiSession session, string ddmodPath, string dotnetPath)
        {
            int selectedIndex = 0;
            while (true)
            {
                Console.Clear();
                var menuItems = new List<string>
                {
                    "1. Batch Verify All Games (Rescans folder sizes)",
                    "2. Batch Prune Missing Records (Clean inactive entries)",
                    "3. Batch Uninstall Selected (Bulk delete from disk)",
                    "4. Batch Download Queue (Sequential queue runner)",
                    "5. Back"
                };

                using (CenterConsoleOutput(80))
                {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
                Console.Write("║ ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write(TuiText.Pad("LIBRARY BATCH OPERATIONS", 76));
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine(" ║");
                Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
                Console.ResetColor();

                for (int i = 0; i < menuItems.Count; i++)
                {
                    if (i == selectedIndex)
                    {
                        Console.BackgroundColor = ConsoleColor.Cyan;
                        Console.ForegroundColor = ConsoleColor.Black;
                        Console.WriteLine($">  {menuItems[i]}");
                        Console.ResetColor();
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.White;
                        Console.WriteLine($"   {menuItems[i]}");
                        Console.ResetColor();
                    }
                }

                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine("\n══════════════════════════════════════════════════════════════════════════════════");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("  [↑/↓] Navigate   [Enter] Confirm Action   [Esc] Return to Library Dashboard");
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine("══════════════════════════════════════════════════════════════════════════════════");
                Console.ResetColor();
                }

                var key = Console.ReadKey(true).Key;
                if (key == ConsoleKey.UpArrow)
                {
                    selectedIndex = (selectedIndex - 1 + menuItems.Count) % menuItems.Count;
                }
                else if (key == ConsoleKey.DownArrow)
                {
                    selectedIndex = (selectedIndex + 1) % menuItems.Count;
                }
                else if (key == ConsoleKey.Escape)
                {
                    return;
                }
                else if (key == ConsoleKey.Enter)
                {
                    if (selectedIndex == 0)
                    {
                        Console.Clear();
                        var games = LibraryManager.LoadLibrary();
                        int verifiedCount = 0;
                        int missingCount = 0;
                        foreach (var g in games)
                        {
                            bool exists = Directory.Exists(g.OutputDir);
                            g.IsVerified = exists;
                            g.TotalSizeBytes = exists ? LibraryManager.GetDirectorySize(g.OutputDir) : 0;
                            if (exists) verifiedCount++; else missingCount++;
                        }
                        LibraryManager.SaveLibrary(games);
                        PromptText("BATCH VERIFY", $"Verify Complete! Verified on disk: {verifiedCount}, Missing: {missingCount}. Press Enter.", "");
                    }
                    else if (selectedIndex == 1)
                    {
                        Console.Clear();
                        var games = LibraryManager.LoadLibrary();
                        int initialCount = games.Count;
                        games.RemoveAll(g => !Directory.Exists(g.OutputDir));
                        int finalCount = games.Count;
                        LibraryManager.SaveLibrary(games);
                        PromptText("BATCH PRUNE", $"Pruned {initialCount - finalCount} missing game record(s). Press Enter.", "");
                    }
                    else if (selectedIndex == 2)
                    {
                        RunBatchUninstallScreen();
                    }
                    else if (selectedIndex == 3)
                    {
                        RunBatchDownloadScreen(session, ddmodPath, dotnetPath);
                    }
                    else if (selectedIndex == 4)
                    {
                        return;
                    }
                }
            }
        }

        private static void RunBatchUninstallScreen()
        {
            var games = LibraryManager.LoadLibrary();
            if (games.Count == 0)
            {
                PromptText("BULK UNINSTALL", "No games registered in the library. Press Enter.", "");
                return;
            }

            var selected = RunCheckboxSelectorGames("SELECT GAMES FOR BULK UNINSTALL", games);
            if (selected.Count == 0) return;

            Console.Clear();
            using (CenterConsoleOutput(80))
            {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║                      CONFIRM BULK UNINSTALLATION                            ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            Console.ResetColor();
            Console.WriteLine($"You are about to delete {selected.Count} game(s) and ALL of their files from disk:");
            foreach (var g in selected)
            {
                Console.WriteLine($"  - {g.GameName} ({g.OutputDir})");
            }
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("\nWARNING: THIS WILL RECURSIVELY DELETE ALL GAME DIRECTORIES LISTED!");
            Console.ResetColor();
            Console.Write("\nAre you absolutely sure you want to proceed? (y/N): ");
            }
            string? input = Console.ReadLine()?.Trim().ToLower();
            if (input == "y" || input == "yes")
            {
                int successCount = 0;
                int failCount = 0;
                foreach (var g in selected)
                {
                    var deleted = LibraryManager.RobustDeleteDirectory(g.OutputDir);
                    if (deleted)
                    {
                        LibraryManager.RemoveGame(g.AppId);
                        successCount++;
                    }
                    else
                    {
                        failCount++;
                    }
                }
                if (failCount == 0)
                {
                    PromptText("BULK UNINSTALL", "Bulk uninstallation complete successfully. Press Enter.", "");
                }
                else
                {
                    PromptText("BULK UNINSTALL", $"Completed with {successCount} successful, {failCount} failed. Press Enter.", "");
                }
            }
        }

        private static void RunBatchDownloadScreen(TuiSession session, string ddmodPath, string dotnetPath)
        {
            var games = LibraryManager.LoadLibrary();
            if (games.Count == 0)
            {
                PromptText("BATCH DOWNLOAD", "No games registered in the library. Press Enter.", "");
                return;
            }

            var selected = RunCheckboxSelectorGames("SELECT GAMES FOR SEQUENTIAL DOWNLOAD QUEUE", games);
            if (selected.Count == 0) return;

            Console.Clear();
            using (CenterConsoleOutput(80))
            {
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            Console.WriteLine("║                     SEQUENTIAL BATCH DOWNLOAD QUEUE                          ║");
            Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
            Console.ResetColor();
            Console.WriteLine($"You have selected {selected.Count} game(s) to download sequentially:\n");
            for (int i = 0; i < selected.Count; i++)
            {
                Console.WriteLine($"  {i + 1}. {selected[i].GameName} (App ID: {selected[i].AppId})");
            }
            Console.Write("\nPress Enter to begin the batch queue or Escape to cancel...");
            }
            var k = Console.ReadKey(true).Key;
            if (k != ConsoleKey.Enter) return;

            for (int i = 0; i < selected.Count; i++)
            {
                var game = selected[i];
                Console.Clear();
                using (CenterConsoleOutput(80))
                {
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
                Console.WriteLine($"║ {TuiText.Pad($"BATCH QUEUE: {i + 1} OF {selected.Count} - {game.GameName.ToUpper()}", 76)} ║");
                Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
                Console.ResetColor();
                }

                if (!File.Exists(game.LuaPath))
                {
                    PromptText("QUEUE RUNNER", $"Lua file not found at: {game.LuaPath}. Press Enter to skip.", "");
                    continue;
                }

                var batchSession = new TuiSession
                {
                    LuaPath = game.LuaPath,
                    OutputDir = game.OutputDir,
                    ManifestsDir = session.ManifestsDir,
                    MaxParallelDepots = session.MaxParallelDepots,
                    RyuuApiKey = session.RyuuApiKey
                };

                // Restore DownloadBaseDir from OutputDir so ParseLuaFileIntoSession recomputes correctly
                var parentDir = Path.GetDirectoryName(game.OutputDir);
                if (!string.IsNullOrEmpty(parentDir))
                {
                    batchSession.DownloadBaseDir = parentDir;
                }

                ParseLuaFileIntoSession(batchSession);

                var restoredDepots = new List<DepotInfo>();
                foreach (var depId in game.DepotIds)
                {
                    var match = batchSession.AllDepots.Find(d => d.DepotId == depId);
                    if (match != null) restoredDepots.Add(match);
                }
                if (restoredDepots.Count > 0)
                {
                    batchSession.SelectedDepots = restoredDepots;
                }

                int exitCode = Program.TriggerDownloadProcess(
                    batchSession.LuaPath, 
                    batchSession.ManifestsDir, 
                    batchSession.OutputDir, 
                    ddmodPath, 
                    dotnetPath, 
                    batchSession.SelectedDepots,
                    batchSession.MaxParallelDepots
                );

                if (exitCode == 0)
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"\n[Success] Batch step {i + 1}/{selected.Count} for {game.GameName} complete!");
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"\n[Warning] Batch step {i + 1}/{selected.Count} for {game.GameName} exited with: {exitCode}");
                }
                Console.ResetColor();

                if (i < selected.Count - 1)
                {
                    System.Threading.Thread.Sleep(1500);
                }
            }

            PromptText("BATCH DOWNLOAD QUEUE", "All sequential batch downloads finished! Press Enter.", "");
        }

        private static List<LibraryGame> RunCheckboxSelectorGames(string prompt, List<LibraryGame> options)
        {
            int index = 0;
            var selected = new bool[options.Count];

            while (true)
            {
                Console.Clear();
                using (CenterConsoleOutput(80))
                {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
                Console.Write("║ ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write(TuiText.Pad(prompt.ToUpper(), 76));
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine(" ║");
                Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
                Console.ResetColor();

                for (int i = 0; i < options.Count; i++)
                {
                    var isCurrent = i == index;
                    var isChecked = selected[i];
                    var checkbox = isChecked ? "[X]" : "[ ]";

                    if (isCurrent)
                    {
                        Console.BackgroundColor = ConsoleColor.Cyan;
                        Console.ForegroundColor = ConsoleColor.Black;
                        Console.WriteLine($">  {checkbox} {options[i].GameName} ({options[i].AppId})");
                        Console.ResetColor();
                    }
                    else
                    {
                        if (isChecked)
                        {
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"   {checkbox} {options[i].GameName} ({options[i].AppId})");
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Gray;
                            Console.WriteLine($"   {checkbox} {options[i].GameName} ({options[i].AppId})");
                        }
                        Console.ResetColor();
                    }
                }

                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine("\n══════════════════════════════════════════════════════════════════════════════════");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("  [↑/↓] Navigate  [Space] Toggle  [A] Check All  [D] Clear All  [Enter] Confirm");
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine("══════════════════════════════════════════════════════════════════════════════════");
                Console.ResetColor();
                }

                var key = Console.ReadKey(true).Key;
                if (key == ConsoleKey.UpArrow)
                {
                    index = (index - 1 + options.Count) % options.Count;
                }
                else if (key == ConsoleKey.DownArrow)
                {
                    index = (index + 1) % options.Count;
                }
                else if (key == ConsoleKey.Spacebar)
                {
                    selected[index] = !selected[index];
                }
                else if (key == ConsoleKey.A)
                {
                    for (int i = 0; i < options.Count; i++) selected[i] = true;
                }
                else if (key == ConsoleKey.D)
                {
                    for (int i = 0; i < options.Count; i++) selected[i] = false;
                }
                else if (key == ConsoleKey.Enter)
                {
                    var result = new List<LibraryGame>();
                    for (int i = 0; i < options.Count; i++)
                    {
                        if (selected[i]) result.Add(options[i]);
                    }
                    return result;
                }
                else if (key == ConsoleKey.Escape)
                {
                    return new List<LibraryGame>();
                }
            }
        }

        private static void RunManifestCacheManager(TuiSession session)
        {
            int selectedIndex = 0;
            while (true)
            {
                Console.Clear();
                string currentDir = session.ManifestsDir ?? "manifests";
                int manifestCount = 0;
                int luaCount = 0;
                long totalSize = 0;

                try
                {
                    if (Directory.Exists(currentDir))
                    {
                        var di = new DirectoryInfo(currentDir);
                        foreach (var file in di.GetFiles("*.manifest", SearchOption.TopDirectoryOnly))
                        {
                            manifestCount++;
                            totalSize += file.Length;
                        }
                    }
                    var luaFiles = FindLuaFiles();
                    luaCount = luaFiles.Count;
                }
                catch { }

                var menuItems = new List<string>
                {
                    "1. Configure/Select Manifests Cache Folder",
                    "2. Import Individual Manifest Files (Windows Explorer)",
                    "3. Import Configs & Manifests from ZIP File",
                    "4. Scan & List Detailed Cached Manifests",
                    "5. Clear Manifest Cache Folder (Perma-Delete)",
                    "6. Back"
                };

                using (CenterConsoleOutput(80))
                {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
                Console.Write("║ ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write(TuiText.Pad("MANIFEST CACHE MANAGER", 76));
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine(" ║");
                Console.WriteLine("╠══════════════════════════════════════════════════════════════════════════════╣");

                void DrawCacheRow(string label, string val, ConsoleColor valColor)
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.Write("║  ");
                    Console.ForegroundColor = ConsoleColor.DarkCyan;
                    Console.Write(TuiText.Pad(label, 22));
                    Console.ForegroundColor = valColor;
                    
                    Console.Write(TuiText.Pad(val, 50));
                    
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine("  ║");
                }

                DrawCacheRow("Cache Directory:", Path.GetFullPath(currentDir), ConsoleColor.White);
                DrawCacheRow("Manifest Files Count:", manifestCount.ToString(), ConsoleColor.Green);
                DrawCacheRow("Manifest Cache Size:", FormatSize(totalSize), ConsoleColor.Green);
                DrawCacheRow("Lua Configs Found:", luaCount.ToString(), ConsoleColor.White);

                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
                Console.ResetColor();

                for (int i = 0; i < menuItems.Count; i++)
                {
                    if (i == selectedIndex)
                    {
                        Console.BackgroundColor = ConsoleColor.Cyan;
                        Console.ForegroundColor = ConsoleColor.Black;
                        Console.WriteLine($">  {menuItems[i]}");
                        Console.ResetColor();
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.White;
                        Console.WriteLine($"   {menuItems[i]}");
                        Console.ResetColor();
                    }
                }

                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine("\n══════════════════════════════════════════════════════════════════════════════════");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("  [↑/↓] Navigate   [Enter] Confirm Selected Action   [Esc] Return to Dashboard");
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine("══════════════════════════════════════════════════════════════════════════════════");
                Console.ResetColor();
                }

                var key = Console.ReadKey(true).Key;
                if (key == ConsoleKey.UpArrow)
                {
                    selectedIndex = (selectedIndex - 1 + menuItems.Count) % menuItems.Count;
                }
                else if (key == ConsoleKey.DownArrow)
                {
                    selectedIndex = (selectedIndex + 1) % menuItems.Count;
                }
                else if (key == ConsoleKey.Escape)
                {
                    return;
                }
                else if (key == ConsoleKey.Enter)
                {
                    if (selectedIndex == 0)
                    {
                        RunConfigureManifestsFolderAction(session);
                    }
                    else if (selectedIndex == 1)
                    {
                        RunImportIndividualManifestFilesAction(session);
                    }
                    else if (selectedIndex == 2)
                    {
                        RunZipImportAction(session);
                    }
                    else if (selectedIndex == 3)
                    {
                        RunScanCachedManifestDetails(session);
                    }
                    else if (selectedIndex == 4)
                    {
                        Console.Clear();
                        using (CenterConsoleOutput(80))
                        {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
                        Console.WriteLine("║                           CLEAR MANIFEST CACHE                               ║");
                        Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
                        Console.ResetColor();
                        Console.WriteLine($"You are about to delete ALL manifest files inside: {Path.GetFullPath(currentDir)}");
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine("\nWARNING: THIS WILL PERMANENTLY DELETE ALL CACHED *.MANIFEST FILES IN THIS FOLDER!");
                        Console.ResetColor();
                        Console.Write("\nAre you sure you want to clear the manifest cache? (y/N): ");
                        }
                        string? input = Console.ReadLine()?.Trim().ToLower();
                        if (input == "y" || input == "yes")
                        {
                            try
                            {
                                int deletedCount = 0;
                                if (Directory.Exists(currentDir))
                                {
                                    var di = new DirectoryInfo(currentDir);
                                    foreach (var file in di.GetFiles("*.manifest"))
                                    {
                                        file.Delete();
                                        deletedCount++;
                                    }
                                }
                                PromptText("CLEAR CACHE", $"Cleaned! Deleted {deletedCount} manifest file(s). Press Enter.", "");
                            }
                            catch (Exception ex)
                            {
                                PromptText("CLEAR CACHE", $"Could not clear files: {ex.Message}. Press Enter.", "");
                            }
                        }
                    }
                    else if (selectedIndex == 5)
                    {
                        return;
                    }
                }
            }
        }

        private static void RunScanCachedManifestDetails(TuiSession session)
        {
            Console.Clear();
            string currentDir = session.ManifestsDir ?? "manifests";
            if (!Directory.Exists(currentDir))
            {
                PromptText("MANIFEST DETAILS", "Manifest folder does not exist yet on disk. Press Enter.", "");
                return;
            }

            var files = Directory.GetFiles(currentDir, "*.manifest");
            if (files.Length == 0)
            {
                PromptText("MANIFEST DETAILS", "No manifest files found in cache folder. Press Enter.", "");
                return;
            }

            using (CenterConsoleOutput(80))
            {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
            Console.Write("║ ");
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write(TuiText.Pad($"CACHED MANIFEST FILES ({files.Length} FOUND)", 76));
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine(" ║");
            Console.WriteLine("╠══════════════════════════════════════════════════════════════════════════════╣");
            Console.ResetColor();

            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine($"  {TuiText.Pad("Depot ID", 15)} │ {TuiText.Pad("Manifest ID", 22)} │ {TuiText.Pad("File Size", 15)} │ Filename");
            Console.WriteLine(new string('═', 78));
            Console.ResetColor();

            foreach (var file in files)
            {
                var name = Path.GetFileNameWithoutExtension(file);
                var fi = new FileInfo(file);
                var sizeStr = FormatSize(fi.Length);

                string depotId = "Unknown";
                string manifestId = "Unknown";

                var parts = name.Split('_');
                if (parts.Length >= 2)
                {
                    depotId = parts[0];
                    manifestId = parts[1];
                }
                else
                {
                    depotId = name;
                }

                Console.WriteLine($"  {TuiText.Pad(depotId, 15)} │ {TuiText.Pad(manifestId, 22)} │ {TuiText.Pad(sizeStr, 15)} │ {Path.GetFileName(file)}");
            }

            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.WriteLine("══════════════════════════════════════════════════════════════════════════════════");
            Console.ResetColor();
            Console.WriteLine("\nPress any key to return to Manifest Cache Manager.");
            }
            Console.ReadKey();
        }

        private static void RunZipImportAction(TuiSession session)
        {
            Console.Clear();
            var sourceOptions = new List<string>
            {
                "Use local .zip file",
                "Request package from Ryuu API",
                "[Cancel]"
            };

            int sourceIndex = RunSelector("SELECT ZIP SOURCE", sourceOptions);
            if (sourceIndex == -1 || sourceOptions[sourceIndex] == "[Cancel]")
            {
                return;
            }

            string? zipPath = sourceIndex == 0 ? ResolveLocalZipPath() : RequestRyuuZipPackage(session);
            if (string.IsNullOrEmpty(zipPath))
            {
                return;
            }

            ImportZipIntoSession(session, zipPath);
        }

        private static string? ResolveLocalZipPath()
        {
            string? zipPath = null;
            bool isWindows = OperatingSystem.IsWindows();

            if (isWindows)
            {
                var options = new List<string>
                {
                    "[Open Windows File Explorer...]",
                    "[Type manual zip path...]",
                    "[Cancel]"
                };
                int selIndex = RunSelector("SELECT ZIP IMPORT METHOD", options);
                if (selIndex == -1 || options[selIndex] == "[Cancel]") return null;

                if (selIndex == 0)
                {
                    Console.Clear();
                    zipPath = DialogHelpers.OpenWindowsFileDialog("Select ZIP Archive Containing Configs/Manifests", "ZIP Archives (*.zip)|*.zip|All Files (*.*)|*.*");
                }
            }

            if (zipPath == null)
            {
                zipPath = PromptText("ZIP IMPORT", "Enter manual zip file path:", "");
                if (string.IsNullOrEmpty(zipPath) || !File.Exists(zipPath))
                {
                    PromptText("ZIP IMPORT", "Invalid or non-existent zip file path. Press Enter.", "");
                    return null;
                }
            }

            return zipPath;
        }

        private static string? RequestRyuuZipPackage(TuiSession session)
        {
            string defaultAppId = session.AppId ?? "";
            string? appId = PromptText("RYUU API", "Enter Steam App ID:", defaultAppId);
            if (string.IsNullOrWhiteSpace(appId))
            {
                return null;
            }

            string? apiKey = PromptText("RYUU API", "Enter Ryuu API key:", session.RyuuApiKey ?? "");
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                PromptText("RYUU API", "Ryuu API key is required. Press Enter.", "");
                return null;
            }

            session.RyuuApiKey = apiKey.Trim();
            SaveSession(session);

            Console.Clear();
            WriteCenteredStatusBox(
                "RYUU API",
                new[] { $"Requesting ZIP package for App ID {appId.Trim()}..." },
                ConsoleColor.Cyan);

            try
            {
                var result = RyuuApiClient.DownloadPackage(appId.Trim(), session.RyuuApiKey!);
                if (!result.HasZip || string.IsNullOrEmpty(result.ZipPath))
                {
                    PromptText("RYUU API", $"{result.Message} Press Enter.", "");
                    return null;
                }

                WriteCenteredStatusBox("RYUU API", new[] { result.Message }, ConsoleColor.Green);
                return result.ZipPath;
            }
            catch (Exception ex)
            {
                PromptText("RYUU API", $"{ex.Message} Press Enter.", "");
                return null;
            }
        }

        private static void ImportZipIntoSession(TuiSession session, string zipPath)
        {
            Console.Clear();
            WriteCenteredStatusBox(
                "ZIP IMPORT",
                new[] { $"Scanning and extracting archive: {TuiText.ShortenTail(zipPath, 58)}" },
                ConsoleColor.Cyan);
            
            var result = ZipHelper.ImportZip(zipPath);
            if (!string.IsNullOrEmpty(result.ManifestsDir))
            {
                session.ManifestsDir = result.ManifestsDir;
                session.ManifestsDirConfigured = false;
            }
            
            WriteCenteredStatusBox(
                "EXTRACTION COMPLETE",
                new[]
                {
                    $"Import Folder: {TuiText.ShortenTail(result.ImportDir, 56)}",
                    $"Extracted Lua Configurations: {result.LuaCount}",
                    $"Extracted Steam Manifests: {result.ManifestCount}"
                },
                ConsoleColor.Green);
            
            if (!string.IsNullOrEmpty(result.FirstLuaPath))
            {
                session.LuaPath = result.FirstLuaPath;
                ParseLuaFileIntoSession(session);
                SaveSession(session);
                WriteCenteredStatusBox(
                    "AUTO-LOAD",
                    new[] { $"Loaded game configuration: {Path.GetFileName(result.FirstLuaPath)}" },
                    ConsoleColor.Green);
            }
            
            WriteCenteredText("Press any key to return.", ConsoleColor.Cyan);
            Console.ReadKey();
        }

        private static void RunChooseLuaAction(TuiSession session)
        {
            var luaFiles = FindLuaFiles();
            var options = new List<string>();
            bool isWindows = OperatingSystem.IsWindows();

            if (isWindows)
            {
                options.Add("[Open Windows File Explorer...]");
            }

            options.AddRange(luaFiles);
            options.Add("[Type manual path...]");
            options.Add("[Cancel]");

            int selIndex = RunSelector("SELECT GAME LUA CONFIGURATION FILE", options);
            if (selIndex == -1 || options[selIndex] == "[Cancel]")
            {
                return;
            }

            string? selectedPath = null;
            if (isWindows && options[selIndex] == "[Open Windows File Explorer...]")
            {
                Console.Clear();
                selectedPath = DialogHelpers.OpenWindowsFileDialog("Select Game Lua Configuration File", "Lua Files (*.lua)|*.lua|All Files (*.*)|*.*");
                if (string.IsNullOrEmpty(selectedPath))
                {
                    PromptText("SELECT CONFIG", "No file selected. Press Enter.", "");
                    return;
                }
            }
            else if (options[selIndex] == "[Type manual path...]")
            {
                selectedPath = PromptText("SELECT CONFIG", "Enter custom Lua configuration file path:", "");
                if (string.IsNullOrEmpty(selectedPath) || !File.Exists(selectedPath))
                {
                    PromptText("SELECT CONFIG", "Invalid or non-existent file path. Press Enter.", "");
                    return;
                }
            }
            else
            {
                selectedPath = options[selIndex];
            }

            if (!string.IsNullOrEmpty(selectedPath))
            {
                session.LuaPath = selectedPath;
                ParseLuaFileIntoSession(session);
                SaveSession(session);
            }
        }

        private static void RunConfigureManifestsFolderAction(TuiSession session)
        {
            var options = new List<string>();
            bool isWindows = OperatingSystem.IsWindows();

            if (isWindows)
            {
                options.Add("[Open Windows Folder Selector...]");
            }
            options.Add("[Type manual folder path...]");
            options.Add("[Cancel]");

            int selIndex = RunSelector("CONFIGURE MANIFESTS CACHE FOLDER", options);
            if (selIndex == -1 || options[selIndex] == "[Cancel]")
            {
                return;
            }

            if (isWindows && options[selIndex] == "[Open Windows Folder Selector...]")
            {
                Console.Clear();
                var selectedPath = DialogHelpers.OpenWindowsFolderDialog("Select Manifests Folder");
                if (string.IsNullOrEmpty(selectedPath))
                {
                    PromptText("MANIFESTS CACHE", "No folder selected. Press Enter.", "");
                    return;
                }
                session.ManifestsDir = selectedPath;
                session.ManifestsDirConfigured = true;
                SaveSession(session);
            }
            else if (options[selIndex] == "[Type manual folder path...]")
            {
                var selectedPath = PromptText("MANIFESTS CACHE", "Folder containing *.manifest files:", session.ManifestsDir ?? "manifests");
                if (selectedPath == null)
                {
                    return;
                }
                session.ManifestsDir = selectedPath;
                session.ManifestsDirConfigured = true;
                SaveSession(session);
            }
        }

        private static void RunImportIndividualManifestFilesAction(TuiSession session)
        {
            if (!OperatingSystem.IsWindows())
            {
                PromptText("IMPORT MANIFEST", "Individual manifest selection requires Windows. Press Enter.", "");
                return;
            }

            Console.Clear();
            var selectedFiles = DialogHelpers.OpenWindowsMultiFileDialog("Select Manifest Files", "Manifest Files (*.manifest)|*.manifest|All Files (*.*)|*.*");
            if (selectedFiles.Count == 0)
            {
                PromptText("IMPORT MANIFEST", "No manifest files selected. Press Enter.", "");
                return;
            }

            var targetDir = session.ManifestsDir ?? "manifests";
            Directory.CreateDirectory(targetDir);

            int copiedCount = 0;
            foreach (var file in selectedFiles)
            {
                var dest = Path.Combine(targetDir, Path.GetFileName(file));
                try
                {
                    File.Copy(file, dest, overwrite: true);
                    copiedCount++;
                }
                catch (Exception ex)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"Failed to copy {Path.GetFileName(file)}: {ex.Message}");
                    Console.ResetColor();
                }
            }

            PromptText("IMPORT MANIFEST", $"Successfully imported {copiedCount} manifest files into '{targetDir}'! Press Enter.", "");
        }

        private static void RunConfigureOutputAction(TuiSession session)
        {
            var options = new List<string>();
            bool isWindows = OperatingSystem.IsWindows();

            if (isWindows)
            {
                options.Add("[Open Windows Folder Selector...]");
            }
            options.Add("[Type manual path...]");
            options.Add("[Cancel]");

            int selIndex = RunSelector("CONFIGURE OUTPUT DOWNLOAD FOLDER", options);
            if (selIndex == -1 || options[selIndex] == "[Cancel]")
            {
                return;
            }

            string? selectedPath = null;
            if (isWindows && options[selIndex] == "[Open Windows Folder Selector...]")
            {
                Console.Clear();
                selectedPath = DialogHelpers.OpenWindowsFolderDialog("Select Output Download Folder");
                if (string.IsNullOrEmpty(selectedPath))
                {
                    PromptText("OUTPUT PATH", "No folder selected. Press Enter.", "");
                    return;
                }
            }
            else
            {
                string defaultOut = string.IsNullOrEmpty(session.OutputDir) 
                    ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "downloads", GetGameName(session)) 
                    : session.OutputDir;
                selectedPath = PromptText("OUTPUT PATH", "Output folder for downloaded game files:", defaultOut);
                if (selectedPath == null)
                {
                    return;
                }
            }

            if (!string.IsNullOrEmpty(selectedPath))
            {
                string gameName = GetGameName(session);
                session.DownloadBaseDir = NormalizeDownloadBaseDir(selectedPath, gameName);
                session.OutputDir = Path.Combine(session.DownloadBaseDir, gameName);
                SaveSession(session);
            }
        }

        private static void ParseLuaFileIntoSession(TuiSession session)
        {
            if (string.IsNullOrEmpty(session.LuaPath) || !File.Exists(session.LuaPath)) return;

            string luaContent = File.ReadAllText(session.LuaPath);

            string appId;
            var parsedDepots = LibraryManager.ParseLuaConfig(luaContent, out appId, session.LuaPath);

            if (string.IsNullOrEmpty(appId)) return;
            session.AppId = appId;

            session.AllDepots = new List<DepotInfo>(parsedDepots.Values);
            session.SelectedDepots = LibraryManager.FilterDownloadableDepots(session.AllDepots, appId);

            string gameName = Path.GetFileNameWithoutExtension(session.LuaPath);
            if (string.IsNullOrEmpty(gameName)) gameName = $"App_{appId}";

            session.OutputDir = BuildOutputDir(session, gameName);
        }

        private static string BuildOutputDir(TuiSession session, string gameName)
        {
            string baseDir = string.IsNullOrEmpty(session.DownloadBaseDir)
                ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "downloads")
                : session.DownloadBaseDir;

            return Path.Combine(baseDir, gameName);
        }

        private static string NormalizeDownloadBaseDir(string selectedPath, string gameName)
        {
            string trimmedPath = selectedPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string lastFolder = Path.GetFileName(trimmedPath);

            if (string.Equals(lastFolder, gameName, StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetDirectoryName(trimmedPath) ?? trimmedPath;
            }

            return trimmedPath;
        }

        private static void SaveSession(TuiSession session)
        {
            IniSettings.Save(session);
        }

        internal static IDisposable CenterConsoleOutput(int contentWidth)
        {
            var writer = Console.Out;
            Console.SetOut(new CenteredTextWriter(writer, GetLeftPad(contentWidth)));
            return new ConsoleOutputScope(writer);
        }

        internal static int GetCenterLeftPad(int contentWidth)
        {
            return GetLeftPad(contentWidth);
        }

        private sealed class ConsoleOutputScope : IDisposable
        {
            private readonly TextWriter _previousWriter;

            public ConsoleOutputScope(TextWriter previousWriter)
            {
                _previousWriter = previousWriter;
            }

            public void Dispose()
            {
                try { Console.Out.Flush(); } catch { }
                Console.SetOut(_previousWriter);
            }
        }

        private sealed class CenteredTextWriter : TextWriter
        {
            private readonly TextWriter _inner;
            private readonly string _indent;
            private bool _atLineStart = true;

            public CenteredTextWriter(TextWriter inner, int indent)
            {
                _inner = inner;
                _indent = new string(' ', Math.Max(0, indent));
            }

            public override Encoding Encoding => _inner.Encoding;

            public override void Write(char value)
            {
                if (_atLineStart && value != '\n' && value != '\r')
                {
                    _inner.Write(_indent);
                    _atLineStart = false;
                }

                _inner.Write(value);
                if (value == '\n')
                {
                    _atLineStart = true;
                }
            }

            public override void Write(string? value)
            {
                if (value == null)
                {
                    return;
                }

                foreach (char ch in value)
                {
                    Write(ch);
                }
            }

            public override void Flush()
            {
                _inner.Flush();
            }
        }

        private static int GetConsoleWidth()
        {
            try
            {
                return Math.Max(Console.WindowWidth, 80);
            }
            catch
            {
                return 80;
            }
        }

        private static int GetLeftPad(int width)
        {
            return Math.Max(0, (GetConsoleWidth() - width) / 2);
        }

        private static void WriteCenteredLine(string text, ConsoleColor color)
        {
            Console.ForegroundColor = color;
            Console.Write(new string(' ', GetLeftPad(text.Length)));
            Console.WriteLine(text);
            Console.ResetColor();
        }

        private static void WriteCenteredText(string text, ConsoleColor color)
        {
            Console.WriteLine();
            WriteCenteredLine(text, color);
        }

        private static void WriteCenteredHeader(string title, int width)
        {
            WriteCenteredBox(title, Array.Empty<string>(), ConsoleColor.White, width, false);
        }

        private static void WriteCenteredStatusBox(string title, IEnumerable<string> rows, ConsoleColor rowColor)
        {
            var lines = rows.Select(row => TuiText.ShortenTail(row, 64)).ToList();
            int width = Math.Min(72, Math.Max(34, lines.Append(title).Max(line => line.Length) + 6));
            WriteCenteredBox(title, lines, rowColor, width, true);
        }

        private static void WriteCenteredBox(string title, IReadOnlyList<string> rows, ConsoleColor rowColor, int width, bool separator)
        {
            int leftPad = GetLeftPad(width);
            string horizontalBorder = new string('═', width - 2);
            string titleLine = title.Length > width - 6 ? title.Substring(0, width - 6) : title;
            int titlePadLeft = (width - 2 - titleLine.Length) / 2;
            int titlePadRight = width - 2 - titleLine.Length - titlePadLeft;

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(new string(' ', leftPad));
            Console.WriteLine("╔" + horizontalBorder + "╗");
            Console.Write(new string(' ', leftPad));
            Console.Write("║");
            Console.Write(new string(' ', titlePadLeft));
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write(titleLine);
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(new string(' ', titlePadRight));
            Console.WriteLine("║");
            Console.Write(new string(' ', leftPad));
            Console.WriteLine((separator && rows.Count > 0 ? "╠" : "╚") + horizontalBorder + (separator && rows.Count > 0 ? "╣" : "╝"));

            foreach (var line in rows)
            {
                Console.Write(new string(' ', leftPad));
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write("║ ");
                Console.ForegroundColor = rowColor;
                Console.Write(TuiText.Pad(line, width - 4));
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine(" ║");
            }

            if (rows.Count > 0)
            {
                Console.Write(new string(' ', leftPad));
                Console.WriteLine("╚" + horizontalBorder + "╝");
            }
            Console.ResetColor();
        }

        private static int RunSelector(string prompt, List<string> options)
        {
            int index = 0;
            while (true)
            {
                Console.Clear();
                int selectorWidth = Math.Min(78, Math.Max(42, options.Append(prompt).Max(option => option.Length) + 8));
                int leftPad = GetLeftPad(selectorWidth);
                WriteCenteredHeader(prompt.ToUpper(), selectorWidth);
                Console.WriteLine();

                for (int i = 0; i < options.Count; i++)
                {
                    Console.Write(new string(' ', leftPad));
                    if (i == index)
                    {
                        Console.BackgroundColor = ConsoleColor.Cyan;
                        Console.ForegroundColor = ConsoleColor.Black;
                        Console.Write(TuiText.Pad($">  {options[i]}", selectorWidth));
                        Console.WriteLine();
                        Console.ResetColor();
                    }
                    else
                    {
                        Console.ForegroundColor = ConsoleColor.White;
                        Console.WriteLine(TuiText.Pad($"   {options[i]}", selectorWidth));
                        Console.ResetColor();
                    }
                }

                Console.WriteLine();
                WriteCenteredLine("══════════════════════════════════════════════════════════════════════════════════", ConsoleColor.DarkCyan);
                WriteCenteredLine("[↑/↓] Navigate   [Enter] Select Option   [Esc] Cancel / Back", ConsoleColor.Cyan);
                WriteCenteredLine("══════════════════════════════════════════════════════════════════════════════════", ConsoleColor.DarkCyan);

                var key = Console.ReadKey(true).Key;
                if (key == ConsoleKey.UpArrow)
                {
                    index = (index - 1 + options.Count) % options.Count;
                }
                else if (key == ConsoleKey.DownArrow)
                {
                    index = (index + 1) % options.Count;
                }
                else if (key == ConsoleKey.Enter)
                {
                    return index;
                }
                else if (key == ConsoleKey.Escape)
                {
                    return -1;
                }
            }
        }

        private static List<DepotInfo>? RunCheckboxSelector(string prompt, List<DepotInfo> options, List<DepotInfo> currentlySelected)
        {
            int index = 0;
            var selected = new bool[options.Count];
            for (int i = 0; i < options.Count; i++)
            {
                selected[i] = currentlySelected.Exists(d => d.DepotId == options[i].DepotId);
            }
            bool showEmptyWarning = false;

            while (true)
            {
                bool _showEmptyWarning = showEmptyWarning;
                showEmptyWarning = false;

                Console.Clear();
                using (CenterConsoleOutput(80))
                {
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
                Console.Write("║ ");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.Write(TuiText.Pad(prompt.ToUpper(), 76));
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.WriteLine(" ║");
                Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
                Console.ResetColor();

                for (int i = 0; i < options.Count; i++)
                {
                    var isCurrent = i == index;
                    var isChecked = selected[i];
                    var checkbox = isChecked ? "[X]" : "[ ]";

                    if (isCurrent)
                    {
                        Console.BackgroundColor = ConsoleColor.Cyan;
                        Console.ForegroundColor = ConsoleColor.Black;
                        Console.WriteLine($">  {checkbox} {FormatDepotSelectionLine(options[i])}");
                        Console.ResetColor();
                    }
                    else
                    {
                        if (isChecked)
                        {
                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"   {checkbox} {FormatDepotSelectionLine(options[i])}");
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.Gray;
                            Console.WriteLine($"   {checkbox} {FormatDepotSelectionLine(options[i])}");
                        }
                        Console.ResetColor();
                    }
                }

                if (_showEmptyWarning)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine("\n  [!] Select at least one depot before confirming.");
                    Console.ResetColor();
                }

                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine("\n══════════════════════════════════════════════════════════════════════════════════");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine("  [↑/↓] Navigate  [Space] Toggle  [A] Check All  [D] Clear All  [Enter] Confirm");
                Console.ForegroundColor = ConsoleColor.DarkCyan;
                Console.WriteLine("══════════════════════════════════════════════════════════════════════════════════");
                Console.ResetColor();
                }

                var key = Console.ReadKey(true).Key;
                if (key == ConsoleKey.UpArrow)
                {
                    index = (index - 1 + options.Count) % options.Count;
                }
                else if (key == ConsoleKey.DownArrow)
                {
                    index = (index + 1) % options.Count;
                }
                else if (key == ConsoleKey.Spacebar)
                {
                    selected[index] = !selected[index];
                }
                else if (key == ConsoleKey.A)
                {
                    for (int i = 0; i < options.Count; i++) selected[i] = true;
                }
                else if (key == ConsoleKey.D)
                {
                    for (int i = 0; i < options.Count; i++) selected[i] = false;
                }
                else if (key == ConsoleKey.Enter)
                {
                    var result = new List<DepotInfo>();
                    for (int i = 0; i < options.Count; i++)
                    {
                        if (selected[i]) result.Add(options[i]);
                    }
                    if (result.Count == 0)
                    {
                        showEmptyWarning = true;
                        continue;
                    }
                    return result;
                }
                else if (key == ConsoleKey.Escape)
                {
                    return null;
                }
            }
        }

        private static List<string> FindLuaFiles()
        {
            var files = new List<string>();
            try
            {
                files.AddRange(Directory.GetFiles(".", "*.lua", SearchOption.TopDirectoryOnly));
                foreach (var sub in Directory.GetDirectories("."))
                {
                    var name = Path.GetFileName(sub).ToLower();
                    if (name.StartsWith(".") || name == "bin" || name == "obj" || name == "dist" || name == "node_modules" || name == "venv" || name == ".git")
                        continue;

                    files.AddRange(Directory.GetFiles(sub, "*.lua", SearchOption.TopDirectoryOnly));
                }

                var importsDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "imports");
                if (Directory.Exists(importsDir))
                {
                    foreach (var gameDir in Directory.GetDirectories(importsDir))
                    {
                        files.AddRange(Directory.GetFiles(gameDir, "*.lua", SearchOption.TopDirectoryOnly));
                    }
                }
            }
            catch { }
            return files;
        }

        private static string FormatDepotSelectionLine(DepotInfo depot)
        {
            var parts = new List<string> { $"Depot {depot.DepotId}" };
            if (!string.IsNullOrWhiteSpace(depot.Name))
            {
                parts.Add(TuiText.Shorten(depot.Name, 24));
            }

            var os = FormatDepotOs(depot);
            if (!string.IsNullOrWhiteSpace(os))
            {
                parts.Add($"OS: {os}");
            }

            if (!string.IsNullOrWhiteSpace(depot.ManifestId))
            {
                parts.Add($"Manifest ID: {depot.ManifestId}");
            }

            return string.Join(" | ", parts);
        }

        private static string FormatDepotOs(DepotInfo depot)
        {
            if (string.IsNullOrWhiteSpace(depot.OsList) && string.IsNullOrWhiteSpace(depot.OsArch))
            {
                return string.Empty;
            }

            var os = string.IsNullOrWhiteSpace(depot.OsList) ? "any" : depot.OsList.Replace(",", "/");
            if (string.IsNullOrWhiteSpace(depot.OsArch))
            {
                return os;
            }

            return $"{os} {depot.OsArch}";
        }

        private static string? PromptText(string title, string prompt, string defaultValue, bool mask = false)
        {
            Console.Clear();
            int consoleWidth = 80;
            try { consoleWidth = Console.WindowWidth; } catch { }
            if (consoleWidth < 40) consoleWidth = 80;

            int boxWidth = Math.Min(70, consoleWidth - 4);
            int leftPad = (consoleWidth - boxWidth) / 2;

            string horizontalBorder = new string('═', boxWidth - 2);
            string titleLine = title.Length > boxWidth - 6 ? title.Substring(0, boxWidth - 6) : title;
            int titlePadLeft = (boxWidth - 2 - titleLine.Length) / 2;
            int titlePadRight = boxWidth - 2 - titleLine.Length - titlePadLeft;

            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(new string(' ', leftPad));
            Console.Write("╔" + horizontalBorder + "╗\n");

            Console.Write(new string(' ', leftPad));
            Console.Write("║");
            Console.ResetColor();
            Console.Write(new string(' ', titlePadLeft));
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.Write(titleLine);
            Console.ResetColor();
            Console.Write(new string(' ', titlePadRight));
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write("║\n");

            Console.Write(new string(' ', leftPad));
            Console.Write("╠" + horizontalBorder + "╣\n");

            Console.Write(new string(' ', leftPad));
            Console.Write("║ ");
            Console.ForegroundColor = ConsoleColor.DarkCyan;
            Console.Write(TuiText.Pad(prompt, boxWidth - 4));
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(" ║\n");

            if (!string.IsNullOrEmpty(defaultValue))
            {
                Console.Write(new string(' ', leftPad));
                Console.Write("║ ");
                Console.ForegroundColor = ConsoleColor.DarkGray;
                string displayVal = mask ? new string('*', Math.Min(12, defaultValue.Length)) : defaultValue;
                Console.Write(TuiText.Pad("Current: " + displayVal, boxWidth - 4));
                Console.ForegroundColor = ConsoleColor.DarkGray;
                Console.Write(" ║\n");
            }

            Console.Write(new string(' ', leftPad));
            Console.Write("║ ");
            Console.ForegroundColor = ConsoleColor.White;
            Console.Write(TuiText.Pad("> ", boxWidth - 4));
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.Write(" ║\n");

            Console.Write(new string(' ', leftPad));
            Console.Write("╚" + horizontalBorder + "╝\n");
            Console.ResetColor();

            int inputRow = Console.CursorTop - 2;
            int inputCol = leftPad + 4;
            Console.SetCursorPosition(inputCol, inputRow);

            string? result = ReadLineWithEscape(mask);
            if (result == null) return null;

            return string.IsNullOrEmpty(result) ? defaultValue : result.Trim();
        }

        private static string? ReadLineWithEscape(bool mask = false)
        {
            var builder = new StringBuilder();
            while (true)
            {
                var keyInfo = Console.ReadKey(true);
                if (keyInfo.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine();
                    return builder.ToString();
                }
                else if (keyInfo.Key == ConsoleKey.Escape)
                {
                    Console.WriteLine();
                    return null;
                }
                else if (keyInfo.Key == ConsoleKey.Backspace)
                {
                    if (builder.Length > 0)
                    {
                        builder.Remove(builder.Length - 1, 1);
                        if (Console.CursorLeft > 0)
                        {
                            Console.Write("\b \b");
                        }
                    }
                }
                else if (keyInfo.KeyChar >= 32)
                {
                    builder.Append(keyInfo.KeyChar);
                    if (mask)
                    {
                        Console.Write('*');
                    }
                    else
                    {
                        Console.Write(keyInfo.KeyChar);
                    }
                }
            }
        }

        private static void RunApplicationSettings(TuiSession session)
        {
            int selectedIndex = 0;
            while (true)
            {
                Console.Clear();
                string maxParallelStr = session.MaxParallelDepots.ToString();
                string manifestsDirStr = session.ManifestsDir ?? "[Not Configured]";
                string outputDirStr = session.DownloadBaseDir ?? "[Auto]";
                string maskedApiKey = string.IsNullOrEmpty(session.RyuuApiKey) 
                    ? "[Not Configured]" 
                    : new string('*', Math.Min(12, session.RyuuApiKey.Length));

                var menuItems = new List<string>
                {
                    $"1. Max Parallel Depot Downloads: {maxParallelStr}",
                    $"2. Manifests Cache Folder       : {TuiText.ShortenTail(manifestsDirStr, 40)}",
                    $"3. Download Base Folder         : {TuiText.ShortenTail(outputDirStr, 40)}",
                    $"4. Ryuu API Key                 : {maskedApiKey}",
                    "5. Back"
                };

                using (CenterConsoleOutput(80))
                {
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine("╔══════════════════════════════════════════════════════════════════════════════╗");
                    Console.Write("║ ");
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.Write(TuiText.Pad("APPLICATION CONFIGURATION SETTINGS", 76));
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine(" ║");
                    Console.WriteLine("╠══════════════════════════════════════════════════════════════════════════════╣");

                    void DrawSettingRow(string label, string val, ConsoleColor valColor)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.Write("║  ");
                        Console.ForegroundColor = ConsoleColor.DarkCyan;
                        Console.Write(TuiText.Pad(label, 26));
                        Console.ForegroundColor = valColor;
                        Console.Write(TuiText.Pad(val, 46));
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        Console.WriteLine("  ║");
                    }

                    DrawSettingRow("Max Parallel Depots:", maxParallelStr, ConsoleColor.Green);
                    DrawSettingRow("Manifests Cache Path:", TuiText.ShortenTail(manifestsDirStr, 44), session.ManifestsDirConfigured ? ConsoleColor.White : ConsoleColor.Yellow);
                    DrawSettingRow("Download Base Path:", TuiText.ShortenTail(outputDirStr, 44), ConsoleColor.White);
                    DrawSettingRow("Ryuu API Key:", maskedApiKey, string.IsNullOrEmpty(session.RyuuApiKey) ? ConsoleColor.Yellow : ConsoleColor.Green);

                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine("╚══════════════════════════════════════════════════════════════════════════════╝");
                    Console.ResetColor();

                    for (int i = 0; i < menuItems.Count; i++)
                    {
                        if (i == selectedIndex)
                        {
                            Console.BackgroundColor = ConsoleColor.Cyan;
                            Console.ForegroundColor = ConsoleColor.Black;
                            Console.WriteLine($">  {menuItems[i]}");
                            Console.ResetColor();
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.White;
                            Console.WriteLine($"   {menuItems[i]}");
                            Console.ResetColor();
                        }
                    }

                    Console.ForegroundColor = ConsoleColor.DarkCyan;
                    Console.WriteLine("\n══════════════════════════════════════════════════════════════════════════════════");
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine("  [↑/↓] Navigate   [Enter] Modify Selected Setting   [Esc] Save & Return");
                    Console.ForegroundColor = ConsoleColor.DarkCyan;
                    Console.WriteLine("══════════════════════════════════════════════════════════════════════════════════");
                    Console.ResetColor();
                }

                var key = Console.ReadKey(true).Key;
                if (key == ConsoleKey.UpArrow)
                {
                    selectedIndex = (selectedIndex - 1 + menuItems.Count) % menuItems.Count;
                }
                else if (key == ConsoleKey.DownArrow)
                {
                    selectedIndex = (selectedIndex + 1) % menuItems.Count;
                }
                else if (key == ConsoleKey.Escape)
                {
                    SaveSession(session);
                    return;
                }
                else if (key == ConsoleKey.Enter)
                {
                    if (selectedIndex == 0)
                    {
                        var input = PromptText("MAX PARALLEL DEPOTS", "Enter number of parallel workers (1 to 8):", session.MaxParallelDepots.ToString());
                        if (input != null && int.TryParse(input, out var val))
                        {
                            session.MaxParallelDepots = Math.Clamp(val, 1, 8);
                            SaveSession(session);
                        }
                    }
                    else if (selectedIndex == 1)
                    {
                        RunConfigureManifestsFolderAction(session);
                    }
                    else if (selectedIndex == 2)
                    {
                        RunConfigureOutputAction(session);
                    }
                    else if (selectedIndex == 3)
                    {
                        var input = PromptText("RYUU API KEY", "Enter Ryuu API Key:", session.RyuuApiKey ?? "", mask: true);
                        if (input != null)
                        {
                            session.RyuuApiKey = string.IsNullOrWhiteSpace(input) ? null : input.Trim();
                            SaveSession(session);
                        }
                    }
                    else if (selectedIndex == 4)
                    {
                        SaveSession(session);
                        return;
                    }
                }
            }
        }

        private static string GetGameName(TuiSession session)
        {
            if (!string.IsNullOrEmpty(session.LuaPath))
            {
                string name = Path.GetFileNameWithoutExtension(session.LuaPath);
                if (!string.IsNullOrEmpty(name))
                    return name;
            }
            if (!string.IsNullOrEmpty(session.AppId))
            {
                return $"App_{session.AppId}";
            }
            return "Game";
        }

        private static string FormatSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            double val = bytes;
            int order = 0;
            while (val >= 1024 && order < suffixes.Length - 1)
            {
                order++;
                val /= 1024;
            }
            return $"{val:F2} {suffixes[order]}";
        }
    }
}
