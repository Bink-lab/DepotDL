namespace DepotDL.GUI.Models
{
    public class AppSettings
    {
        public string? ManifestsDir { get; set; }
        public string? DownloadBaseDir { get; set; }
        public string? RyuuApiKey { get; set; }
        public int MaxParallelDepots { get; set; } = 2;
    }
}
