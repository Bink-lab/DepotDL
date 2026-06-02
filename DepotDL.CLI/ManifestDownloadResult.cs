namespace DepotDL.CLI
{
    public sealed class ManifestDownloadResult
    {
        public bool HasZip { get; init; }
        public string? ZipPath { get; init; }
        public string Message { get; init; } = string.Empty;
    }
}
