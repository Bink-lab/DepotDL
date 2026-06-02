namespace DepotDL.GUI.Models
{
    public sealed class ManifestDownloadResult
    {
        public bool HasZip { get; init; }
        public string? ZipPath { get; init; }
        public string Message { get; init; } = string.Empty;
    }
}
