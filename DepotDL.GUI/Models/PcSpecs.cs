// This file is subject to the terms and conditions defined
// in file 'LICENSE', which is part of this source code package.

namespace DepotDL.GUI.Models
{
    public record PcSpecs(
        string CpuName,
        long RamMb,
        string GpuName,
        double FreeStorageGb,
        double TotalStorageGb);
}
