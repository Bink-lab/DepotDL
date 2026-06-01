using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace DepotDL.CLI
{
    public static class DialogHelpers
    {
        public static string? OpenWindowsFileDialog(string title, string filter)
        {
            if (!OperatingSystem.IsWindows()) return null;

            try
            {
                var psCommand = $"[System.Reflection.Assembly]::LoadWithPartialName('System.Windows.Forms') | Out-Null; " +
                                $"$f = New-Object System.Windows.Forms.OpenFileDialog; " +
                                $"$f.Filter = '{filter}'; " +
                                $"$f.Title = '{title}'; " +
                                $"if ($f.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {{ Write-Host $f.FileName }}";

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psCommand}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return null;

                string output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();

                return string.IsNullOrEmpty(output) ? null : output;
            }
            catch
            {
                return null;
            }
        }

        public static List<string> OpenWindowsMultiFileDialog(string title, string filter)
        {
            var results = new List<string>();
            if (!OperatingSystem.IsWindows()) return results;

            try
            {
                var psCommand = $"[System.Reflection.Assembly]::LoadWithPartialName('System.Windows.Forms') | Out-Null; " +
                                $"$f = New-Object System.Windows.Forms.OpenFileDialog; " +
                                $"$f.Filter = '{filter}'; " +
                                $"$f.Title = '{title}'; " +
                                $"$f.Multiselect = $true; " +
                                $"if ($f.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {{ Write-Host ($f.FileNames -join ';') }}";

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psCommand}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return results;

                string output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();

                if (!string.IsNullOrEmpty(output))
                {
                    var files = output.Split(';', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var file in files)
                    {
                        var trimmed = file.Trim();
                        if (File.Exists(trimmed))
                        {
                            results.Add(trimmed);
                        }
                    }
                }
            }
            catch { }

            return results;
        }

        public static string? OpenWindowsFolderDialog(string description)
        {
            if (!OperatingSystem.IsWindows()) return null;

            try
            {
                var psCommand = $"[System.Reflection.Assembly]::LoadWithPartialName('System.Windows.Forms') | Out-Null; " +
                                $"$f = New-Object System.Windows.Forms.FolderBrowserDialog; " +
                                $"$f.Description = '{description}'; " +
                                $"if ($f.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK) {{ Write-Host $f.SelectedPath }}";

                var psi = new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psCommand}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return null;

                string output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();

                return string.IsNullOrEmpty(output) ? null : output;
            }
            catch
            {
                return null;
            }
        }

        public static string? ResolveDotnetPath(string? customPath)
        {
            if (!string.IsNullOrEmpty(customPath) && File.Exists(customPath))
            {
                return customPath;
            }

            try
            {
                var checkProc = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "dotnet",
                        Arguments = "--list-runtimes",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        CreateNoWindow = true
                    }
                };
                checkProc.Start();
                string output = checkProc.StandardOutput.ReadToEnd();
                checkProc.WaitForExit();
                if (output.Contains("Microsoft.NETCore.App 9."))
                {
                    return "dotnet";
                }
            }
            catch { }

            if (OperatingSystem.IsWindows())
            {
                var localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA") ??
                                   Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Local");
                var winLocalDotnet = Path.Combine(localAppData, "Microsoft", "dotnet", "dotnet.exe");
                if (File.Exists(winLocalDotnet))
                {
                    return winLocalDotnet;
                }
            }
            else
            {
                var unixLocalDotnet = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "dotnet");
                if (File.Exists(unixLocalDotnet))
                {
                    return unixLocalDotnet;
                }
            }

            return null;
        }

        public static string? ResolveDDModPath(string? customPath)
        {
            if (!string.IsNullOrEmpty(customPath))
            {
                return Path.GetFullPath(customPath);
            }

            var baseDir = AppDomain.CurrentDomain.BaseDirectory;

            string[] candidates = {
                Path.Combine(baseDir, "DepotDownloaderMod.dll"),
                Path.Combine(baseDir, "third_party", "DDMod", "DepotDownloaderMod.dll"),
                Path.Combine(baseDir, "..", "third_party", "DDMod", "DepotDownloaderMod.dll"),
                Path.Combine(baseDir, "..", "..", "third_party", "DDMod", "DepotDownloaderMod.dll"),
                Path.Combine(baseDir, "..", "..", "..", "third_party", "DDMod", "DepotDownloaderMod.dll"),
                Path.Combine(baseDir, "..", "..", "..", "..", "third_party", "DDMod", "DepotDownloaderMod.dll"),
                Path.Combine(baseDir, "..", "..", "..", "..", "..", "third_party", "DDMod", "DepotDownloaderMod.dll")
            };

            foreach (var candidate in candidates)
            {
                if (File.Exists(candidate))
                {
                    return Path.GetFullPath(candidate);
                }
            }

            return null;
        }
    }
}
