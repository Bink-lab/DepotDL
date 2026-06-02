using System.Collections.Generic;

namespace DepotDL.GUI.Models
{
    public class SteamAppDetail
    {
        public int AppId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string ShortDescription { get; set; } = string.Empty;
        public string DetailedDescription { get; set; } = string.Empty;
        public string HeaderImage { get; set; } = string.Empty;
        public string PriceText { get; set; } = string.Empty;
        public List<string> Genres { get; set; } = new();
        public List<string> Screenshots { get; set; } = new();
        public List<string> FullScreenshots { get; set; } = new();
        public int Positive { get; set; }
        public int Negative { get; set; }
        public string Owners { get; set; } = string.Empty;
        public string Developer { get; set; } = string.Empty;
        public string Publisher { get; set; } = string.Empty;
        public Dictionary<string, string> MinRequirements { get; set; } = new();
        public Dictionary<string, string> RecommendedRequirements { get; set; } = new();
    }
}
