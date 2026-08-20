using System.Text.Json.Serialization;

namespace GuardianConnect.API.Model;

public class RegionInputParameter
{
    [JsonPropertyName("region")] public string? Region { get; set; }

    [JsonPropertyName("paid")] public bool Paid { get; set; } = true;

    [JsonPropertyName("feature-environment")] public int FeatureEnvironment { get; set; }

    [JsonPropertyName("beta-capable")] public bool BetaCapable { get; set; }

    [JsonPropertyName("region-precision")] public string? RegionPrecision { get; set; }
}
