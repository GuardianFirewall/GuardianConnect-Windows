using System.Text.Json.Serialization;
using GuardianConnect.API;

namespace GuardianConnect.API.Model;

// Secure-gateway server record (SGW). Cross-platform parity with iOS/Android GRDSGWServer.

// GRDSGWServer myDeserializedClass = JsonSerializer.Deserialize<List<GRDSGWServer>>(myJsonResponse);
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
public class GRDSGWServer
{
    [JsonPropertyName("hostname")] public string Hostname { get; set; } = string.Empty;

    [JsonPropertyName("display-name")] public string DisplayName { get; set; } = string.Empty;

    public bool Offline { get; set; }

    [JsonPropertyName("capacity-score")] public int CapacityScore { get; set; }

    [JsonPropertyName("server-feature-environment")]
    public int ServerFeatureEnvironment { get; set; }

    [JsonPropertyName("beta-capable")] public bool BetaCapable { get; set; }

    [JsonPropertyName("ipv4-address")]
    public string IPv4Address { get; set; } = string.Empty;

    [JsonPropertyName("ipv6-address")]
    public string IPv6Address { get; set; } = string.Empty;

    /// Whether this server supports smart-proxy routing. Maps to iOS
    /// GRDSGWServer.smartProxyRoutingEnabled (wire key "smart-routing-enabled").
    [JsonPropertyName("smart-routing-enabled")]
    public bool SmartProxyRoutingEnabled { get; set; }

    /// Whether this host accepts a multi-hop registration. Each city has exactly
    /// one such host, which also serves as that city's exit; registering with a
    /// multihop-exit-region on any other host returns HTTP 400.
    [JsonPropertyName("multihop-entry-enabled")]
    public bool MultihopEntryEnabled { get; set; }

    /// The multi-hop exit slug for this host's own city (e.g. "miami"). The
    /// gateway refuses an exit equal to the entry host's own city. Populated by
    /// the per-region host list.
    [JsonPropertyName("region-multihop-exit-name")]
    public string RegionMultihopExitName { get; set; } = string.Empty;

    /// The region that owns this host. Mirrors iOS GRDSGWServer.region. Binds
    /// directly from JSON when a host list nests a "region" object per host.
    /// The per-region host-list endpoint omits it (region is the query context),
    /// so GRDServerManager.GetHostsForRegion stamps it as a fallback.
    [JsonPropertyName("region")]
    public GRDRegion? Region { get; set; }

    public string HostLocation()
    {
        return DisplayName;
    }
}