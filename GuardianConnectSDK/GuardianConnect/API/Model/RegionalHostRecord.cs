using System.Text.Json.Serialization;

namespace GuardianConnect.API.Model;

// RegionalHostRecord myDeserializedClass = JsonSerializer.Deserialize<List<RegionalHostRecord>>(myJsonResponse);
/*
 * Sample json
 *  {
"hostname": "miami-2.sgw.guardianapp.com",
"display-name": "Miami, FL",
"offline": false,
"capacity-score": 0,
"server-feature-environment": 0,
"beta-capable": false
}
 */
public class RegionalHostRecord
{
    [JsonPropertyName("hostname")] public string Hostname { get; set; } = string.Empty;

    [JsonPropertyName("display-name")] public string DisplayName { get; set; } = string.Empty;

    public bool Offline { get; set; }

    [JsonPropertyName("capacity-score")] public int CapacityScore { get; set; }

    [JsonPropertyName("server-feature-environment")]
    public int ServerFeatureEnvironment { get; set; }

    [JsonPropertyName("beta-capable")] public bool BetaCapable { get; set; }

    public string HostLocation()
    {
        return DisplayName;
    }
}