namespace DepotDL.GUI.Models
{
    public record BenchmarkScore(int SingleCore, int MultiCore)
    {
        public static readonly BenchmarkScore Unknown = new(0, 0);
        public bool IsKnown => SingleCore > 0;
        public string DisplayCpu => $"{SingleCore:N0} SC | {MultiCore:N0} MC";
        public string DisplayGpu => SingleCore > 0 ? $"{SingleCore:N0} compute" : string.Empty;
    }
}
