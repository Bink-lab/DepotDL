using System.Text.Json.Serialization;

namespace DepotDL.GUI.Models
{
    public class StoreGame
    {
        [JsonPropertyName("appid")]
        public int AppId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("developer")]
        public string Developer { get; set; } = string.Empty;

        [JsonPropertyName("publisher")]
        public string Publisher { get; set; } = string.Empty;

        [JsonPropertyName("owners")]
        public string Owners { get; set; } = string.Empty;

        [JsonPropertyName("players_2weeks")]
        public int PlayersIn2Weeks { get; set; }

        [JsonPropertyName("positive")]
        public int Positive { get; set; }

        [JsonPropertyName("negative")]
        public int Negative { get; set; }

        [JsonPropertyName("price")]
        public string Price { get; set; } = "0";

        [JsonPropertyName("genre")]
        public string Genre { get; set; } = string.Empty;

        public string HeaderImageUrl =>
            $"https://cdn.akamai.steamstatic.com/steam/apps/{AppId}/header.jpg";
    }
}
