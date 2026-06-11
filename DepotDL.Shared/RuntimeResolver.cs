// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System.Diagnostics;
using System.IO;

namespace DepotDL.Shared
{
    public static class RuntimeResolver
    {
        public static string? ResolveDotnetPath(string? customPath = null)
        {
            if (!string.IsNullOrEmpty(customPath) && File.Exists(customPath))
                return customPath;

            try
            {
                var p = new Process
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
                p.Start();
                var output = p.StandardOutput.ReadToEnd();
                p.WaitForExit();
                if (output.Contains("Microsoft.NETCore.App 9.")) return "dotnet";
            }
            catch { }

            if (OperatingSystem.IsWindows())
            {
                var localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA") ??
                                   Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Local");
                var winPath = Path.Combine(localAppData, "Microsoft", "dotnet", "dotnet.exe");
                if (File.Exists(winPath)) return winPath;
            }
            else
            {
                var unixPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet", "dotnet");
                if (File.Exists(unixPath)) return unixPath;
            }

            return null;
        }

        public static string? ResolveDDModPath(string? customPath = null)
        {
            if (!string.IsNullOrEmpty(customPath))
                return Path.GetFullPath(customPath);

            var candidate = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DepotDownloaderMod.dll");
            return File.Exists(candidate) ? Path.GetFullPath(candidate) : null;
        }
    }
}
