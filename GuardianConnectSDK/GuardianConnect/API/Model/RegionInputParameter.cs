using System.Text.Json.Serialization;

namespace GuardianConnect.API.Model;

public class RegionInputParameter
{
    [JsonPropertyName("region")] public string? Region { get; set; }
}