//using Newtonsoft.Json;
using System.Text.Json.Serialization;

namespace GuardianConnect.API.Model
{
    // RegionalHostRecord myDeserializedClass = JsonSerializer.Deserialize<List<RegionalHostRecord>>(myJsonResponse);
    public class RegionalHostRecord
    {
        public string Hostname { get; set; } = string.Empty;

        [JsonPropertyName("display-name")] public string DisplayName { get; set; } = string.Empty;
        
        public bool Offline { get; set; }

        [JsonPropertyName("capacity-score")]
        public int CapacityScore { get; set; }

        [JsonPropertyName("server-feature-environment")]
        public int ServerFeatureEnvironment { get; set; }

        [JsonPropertyName("beta-capable")]
        public bool BetaCapable { get; set; }

        public string HostLocation() => DisplayName;
    }
}
