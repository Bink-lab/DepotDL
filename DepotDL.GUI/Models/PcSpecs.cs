namespace DepotDL.GUI.Models
{
    public record PcSpecs(
        string CpuName,
        long   RamMb,
        string GpuName,
        double FreeStorageGb,
        double TotalStorageGb);
}
