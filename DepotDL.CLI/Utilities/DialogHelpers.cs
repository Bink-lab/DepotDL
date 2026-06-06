// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System.Diagnostics;

namespace DepotDL.CLI.Utilities
{
    public static class DialogHelpers
    {
        public static string? OpenWindowsFileDialog(string title, string filter)
        {
            if (OperatingSystem.IsWindows())
            {
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

                    var output = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit();

                    return string.IsNullOrEmpty(output) ? null : output;
                }
                catch { return null; }
            }
            else if (OperatingSystem.IsMacOS())
            {
                return RunOsascript($"POSIX path of (choose file with prompt \"{EscapeOsascript(title)}\")");
            }
            return null;
        }

        public static List<string> OpenWindowsMultiFileDialog(string title, string filter)
        {
            var results = new List<string>();

            if (OperatingSystem.IsWindows())
            {
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

                    var output = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit();

                    if (!string.IsNullOrEmpty(output))
                    {
                        foreach (var file in output.Split(';', StringSplitOptions.RemoveEmptyEntries))
                        {
                            var trimmed = file.Trim();
                            if (File.Exists(trimmed)) results.Add(trimmed);
                        }
                    }
                }
                catch { }
            }
            else if (OperatingSystem.IsMacOS())
            {
                var script =
                    $"set output to \"\"\n" +
                    $"set chosen to choose file with prompt \"{EscapeOsascript(title)}\" with multiple selections allowed\n" +
                    $"repeat with f in chosen\n" +
                    $"set output to output & (POSIX path of f) & \";\"\n" +
                    $"end repeat\n" +
                    $"output";
                var raw = RunOsascript(script);
                if (!string.IsNullOrEmpty(raw))
                {
                    foreach (var p in raw.Split(';', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var trimmed = p.Trim();
                        if (File.Exists(trimmed)) results.Add(trimmed);
                    }
                }
            }

            return results;
        }

        public static string? OpenWindowsFolderDialog(string description)
        {
            if (OperatingSystem.IsWindows())
            {
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

                    var output = process.StandardOutput.ReadToEnd().Trim();
                    process.WaitForExit();

                    return string.IsNullOrEmpty(output) ? null : output;
                }
                catch { return null; }
            }
            else if (OperatingSystem.IsMacOS())
            {
                return RunOsascript($"POSIX path of (choose folder with prompt \"{EscapeOsascript(description)}\")");
            }
            return null;
        }

        private static string? RunOsascript(string script)
        {
            string? tmpFile = null;
            try
            {
                tmpFile = Path.Combine(Path.GetTempPath(), $"depotdl_{Guid.NewGuid():N}.applescript");
                File.WriteAllText(tmpFile, script);
                var psi = new ProcessStartInfo
                {
                    FileName = "osascript",
                    Arguments = $"\"{tmpFile}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                using var process = Process.Start(psi);
                if (process == null) return null;
                var output = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit(15000);
                return string.IsNullOrEmpty(output) ? null : output;
            }
            catch { return null; }
            finally { try { if (tmpFile != null) File.Delete(tmpFile); } catch { } }
        }

        private static string EscapeOsascript(string s) =>
            s.Replace("\\", "\\\\").Replace("\"", "\\\"");

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
                var output = checkProc.StandardOutput.ReadToEnd();
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
