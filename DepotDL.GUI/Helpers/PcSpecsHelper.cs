using System;
using System.Management;
using DepotDL.GUI.Models;

namespace DepotDL.GUI.Helpers
{
    public static class PcSpecsHelper
    {
        private static PcSpecs? _cached;

        public static PcSpecs GetSpecs()
        {
            if (_cached != null) return _cached;

            string cpu = QueryFirst("SELECT Name FROM Win32_Processor", "Name") ?? "Unknown";
            string gpu = QueryFirst("SELECT Name FROM Win32_VideoController", "Name") ?? "Unknown";

            long ramMb = 0;
            using (var search = new ManagementObjectSearcher("SELECT Capacity FROM Win32_PhysicalMemory"))
            using (var collection = search.Get())
            {
                foreach (ManagementObject obj in collection)
                {
                    using (obj)
                    {
                        if (ulong.TryParse(obj["Capacity"]?.ToString(), out ulong cap))
                            ramMb += (long)(cap / (1024 * 1024));
                    }
                }
            }

            double freeGb = 0, totalGb = 0;
            using (var search = new ManagementObjectSearcher(
                "SELECT FreeSpace, Size FROM Win32_LogicalDisk WHERE DriveType=3"))
            using (var collection = search.Get())
            {
                foreach (ManagementObject obj in collection)
                {
                    using (obj)
                    {
                        if (ulong.TryParse(obj["FreeSpace"]?.ToString(), out ulong free) &&
                            ulong.TryParse(obj["Size"]?.ToString(), out ulong size))
                        {
                            double fGb = free / (1024.0 * 1024 * 1024);
                            if (fGb > freeGb)
                            {
                                freeGb = fGb;
                                totalGb = size / (1024.0 * 1024 * 1024);
                            }
                        }
                    }
                }
            }

            _cached = new PcSpecs(
                CpuName: cpu.Trim(),
                RamMb: ramMb,
                GpuName: gpu.Trim(),
                FreeStorageGb: Math.Round(freeGb, 1),
                TotalStorageGb: Math.Round(totalGb, 1));
            return _cached;
        }

        private static string? QueryFirst(string query, string property)
        {
            try
            {
                using var search = new ManagementObjectSearcher(query);
                using var collection = search.Get();
                foreach (ManagementObject obj in collection)
                {
                    using (obj)
                    {
                        return obj[property]?.ToString();
                    }
                }
            }
            catch { }
            return null;
        }
    }
}
