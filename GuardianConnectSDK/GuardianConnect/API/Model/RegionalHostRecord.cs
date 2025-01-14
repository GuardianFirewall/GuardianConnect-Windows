using Newtonsoft.Json;

namespace GuardianConnect.API.Model
{
    // RegionalHostRecord myDeserializedClass = JsonConvert.DeserializeObject<List<RegionalHostRecord>>(myJsonResponse);
    public class RegionalHostRecord
    {
        public string Hostname { get; set; } = string.Empty;

        [JsonProperty("display-name")] public string DisplayName { get; set; } = string.Empty;
        
        public bool Offline { get; set; }

        [JsonProperty("capacity-score")]
        public int CapacityScore { get; set; }

        [JsonProperty("server-feature-environment")]
        public int ServerFeatureEnvironment { get; set; }

        [JsonProperty("beta-capable")]
        public bool BetaCapable { get; set; }

        public string HostLocation() => DisplayName;
    }
}
