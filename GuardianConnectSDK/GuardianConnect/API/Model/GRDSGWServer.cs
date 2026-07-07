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

    /// Whether this server supports smart-proxy routing. Maps to iOS
    /// GRDSGWServer.smartProxyRoutingEnabled (wire key "smart-routing-enabled").
    [JsonPropertyName("smart-routing-enabled")]
    public bool SmartProxyRoutingEnabled { get; set; }

    /// The gateway's public IPv4 address, as published by the host in the
    /// hostnames-for-region / all-hostnames responses. When present, the IKEv2
    /// dial uses this instead of the FQDN (the OS then needs no DNS at dial
    /// time); registration/HTTPS stays on the FQDN for TLS SAN validation.
    /// Distinct from the WG "mapped-ipv4-address" (the client's tunnel IP).
    [JsonPropertyName("ipv4-address")]
    public string Ipv4Address { get; set; } = string.Empty;

    /// The gateway's public IPv6 address. Currently empty on the wire; carried
    /// for forward-compat alongside <see cref="Ipv4Address"/>.
    [JsonPropertyName("ipv6-address")]
    public string Ipv6Address { get; set; } = string.Empty;

    /// The region that owns this host. Mirrors iOS GRDSGWServer.region. The
    /// servers/all-hostnames responses nest a "region" object per host, so this
    /// binds directly from JSON (e.g. GetAllHostnamesAsync).
    /// The per-region host-list endpoint omits it (region is the query context),
    /// so GRDServerManager.GetHostsForRegion stamps it as a fallback.
    [JsonPropertyName("region")]
    public GRDRegion? Region { get; set; }

    public string HostLocation()
    {
        return DisplayName;
    }
}