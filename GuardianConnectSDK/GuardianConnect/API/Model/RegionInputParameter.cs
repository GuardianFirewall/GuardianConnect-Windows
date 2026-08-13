using System.Text.Json.Serialization;

namespace GuardianConnect.API.Model;

// Request body for POST /api/v1.3/servers/hostnames-for-region. v1 of that
// endpoint accepted "region" alone; v1.3 takes the full set below and is the
// only version that returns "smart-routing-enabled" and the nested "region"
// object on each host record.
public class RegionInputParameter
{
    [JsonPropertyName("region")] public string? Region { get; set; }

    /// Requests the paid host pool. Guardian Firewall has no free tier on
    /// Windows, so this is always true.
    [JsonPropertyName("paid")] public bool Paid { get; set; } = true;

    /// GRDVPNHelper.GRDServerFeatureEnvironment as its integer value.
    [JsonPropertyName("feature-environment")] public int FeatureEnvironment { get; set; }

    [JsonPropertyName("beta-capable")] public bool BetaCapable { get; set; }

    /// One of "default", "city", "country", "city-by-country". Windows maps the
    /// device time zone to the default regions, so "default" is what keeps the
    /// returned host set aligned with the region keys this client uses.
    [JsonPropertyName("region-precision")] public string? RegionPrecision { get; set; }
}
