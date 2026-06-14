// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

using System.Runtime.InteropServices;
using DepotDL.GUI.Models;

namespace DepotDL.GUI.Helpers
{
    public static class PcSpecsHelper
    {
        private static PcSpecs? _cached;

        public static PcSpecs GetSpecs()
        {
            if (_cached != null) return _cached;

            if (OperatingSystem.IsWindows())
                _cached = GetSpecsWindows();
            else if (OperatingSystem.IsMacOS())
                _cached = GetSpecsMacOs();
            else
                _cached = GetSpecsLinux();

            return _cached;
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static PcSpecs GetSpecsWindows()
        {
#if WINDOWS
            using var cpuSearch = new System.Management.ManagementObjectSearcher("SELECT Name FROM Win32_Processor");
            using var cpuCol = cpuSearch.Get();
            string cpu = "Unknown";
            foreach (System.Management.ManagementObject obj in cpuCol)
            {
                using (obj) { cpu = obj["Name"]?.ToString() ?? "Unknown"; break; }
            }

            using var gpuSearch = new System.Management.ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
            using var gpuCol = gpuSearch.Get();
            string gpu = "Unknown";
            foreach (System.Management.ManagementObject obj in gpuCol)
            {
                using (obj) { gpu = obj["Name"]?.ToString() ?? "Unknown"; break; }
            }

            long ramMb = 0;
            using (var ramSearch = new System.Management.ManagementObjectSearcher("SELECT Capacity FROM Win32_PhysicalMemory"))
            using (var ramCol = ramSearch.Get())
            {
                foreach (System.Management.ManagementObject obj in ramCol)
                {
                    using (obj)
                    {
                        if (ulong.TryParse(obj["Capacity"]?.ToString(), out var cap))
                            ramMb += (long)(cap / (1024 * 1024));
                    }
                }
            }

            double freeGb = 0, totalGb = 0;
            using (var diskSearch = new System.Management.ManagementObjectSearcher(
                "SELECT FreeSpace, Size FROM Win32_LogicalDisk WHERE DriveType=3"))
            using (var diskCol = diskSearch.Get())
            {
                foreach (System.Management.ManagementObject obj in diskCol)
                {
                    using (obj)
                    {
                        if (ulong.TryParse(obj["FreeSpace"]?.ToString(), out var free) &&
                            ulong.TryParse(obj["Size"]?.ToString(), out var size))
                        {
                            var fGb = free / (1024.0 * 1024 * 1024);
                            if (fGb > freeGb) { freeGb = fGb; totalGb = size / (1024.0 * 1024 * 1024); }
                        }
                    }
                }
            }

            return new PcSpecs(cpu.Trim(), ramMb, gpu.Trim(),
                Math.Round(freeGb, 1), Math.Round(totalGb, 1));
#else
            return new PcSpecs("Unknown", 0, "Unknown", 0, 0);
#endif
        }

        private static PcSpecs GetSpecsMacOs()
        {
            var cpu = RunShell("sysctl", "-n machdep.cpu.brand_string") ?? "Unknown";
            var gpu = RunShell("bash", "-c \"system_profiler SPDisplaysDataType -detailLevel mini 2>/dev/null | awk -F': ' '/Chipset Model/{print $2; exit}'\"") ?? "Unknown";
            var ramStr = RunShell("sysctl", "-n hw.memsize") ?? "0";
            long ramMb = long.TryParse(ramStr.Trim(), out var rb) ? rb / (1024 * 1024) : 0;

            double freeGb = 0, totalGb = 0;
            var dfOut = RunShell("df", "-k /");
            if (dfOut != null)
            {
                var lines = dfOut.Split('\n');
                if (lines.Length > 1)
                {
                    var parts = lines[1].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 4 &&
                        long.TryParse(parts[1], out var total1024) &&
                        long.TryParse(parts[3], out var free1024))
                    {
                        totalGb = Math.Round(total1024 / (1024.0 * 1024), 1);
                        freeGb = Math.Round(free1024 / (1024.0 * 1024), 1);
                    }
                }
            }

            return new PcSpecs(cpu.Trim(), ramMb, gpu.Trim(), freeGb, totalGb);
        }

        private static PcSpecs GetSpecsLinux()
        {
            string cpu = "Unknown";
            try
            {
                var lines = File.ReadAllLines("/proc/cpuinfo");
                cpu = lines.FirstOrDefault(l => l.StartsWith("model name"))?.Split(':')[1].Trim() ?? "Unknown";
            }
            catch { }

            long ramMb = 0;
            try
            {
                var memLines = File.ReadAllLines("/proc/meminfo");
                var totalLine = memLines.FirstOrDefault(l => l.StartsWith("MemTotal:"));
                if (totalLine != null)
                {
                    var parts = totalLine.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && long.TryParse(parts[1], out var kB))
                        ramMb = kB / 1024;
                }
            }
            catch { }

            string gpu = RunShell("bash", "-c \"lspci 2>/dev/null | grep -i 'vga\\|3d\\|display' | head -1 | sed 's/.*: //'\"") ?? "Unknown";

            double freeGb = 0, totalGb = 0;
            var dfOut = RunShell("df", "-k /");
            if (dfOut != null)
            {
                var lines2 = dfOut.Split('\n');
                if (lines2.Length > 1)
                {
                    var parts = lines2[1].Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 4 &&
                        long.TryParse(parts[1], out var total1024) &&
                        long.TryParse(parts[3], out var free1024))
                    {
                        totalGb = Math.Round(total1024 / (1024.0 * 1024), 1);
                        freeGb = Math.Round(free1024 / (1024.0 * 1024), 1);
                    }
                }
            }

            return new PcSpecs(cpu, ramMb, gpu.Trim(), freeGb, totalGb);
        }

        private static string? RunShell(string cmd, string args)
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo(cmd, args)
                {
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var p = System.Diagnostics.Process.Start(psi);
                return p?.StandardOutput.ReadToEnd();
            }
            catch { return null; }
        }
    }
}
