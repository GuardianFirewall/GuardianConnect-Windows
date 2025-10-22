using System.Text.Json.Serialization;

namespace GuardianConnect.API.Model
{
    public class RegionInputParameter
    {
        public RegionInputParameter() { }
        [JsonPropertyName("region")] public string? Region { get; set; }
    }
}
