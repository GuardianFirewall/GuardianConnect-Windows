using System.Text.Json.Serialization;

namespace GuardianConnect.Credentials;

/// <summary>
/// The host's reply to <c>POST /api/v1.3/device</c>, for EITHER transport
/// protocol. A superset of both protocols' response shapes — the server only
/// populates the subset relevant to the negotiated protocol, so every field is
/// nullable. Carried verbatim on <see cref="GRDCredential.Device"/> so the
/// usage points (config build, IKEv2 dial, ActiveConnectionPossible) can pluck
/// exactly what the active protocol needs without a protocol-forked mapping
/// step.
///
/// Note the device keypair (private/public key) is NOT part of this object:
/// for WireGuard those are generated client-side and the private key never
/// comes back from the host, so they live as flat fields on the credential
/// (<see cref="GRDCredential.DevicePrivateKey"/> / <c>DevicePublicKey</c>).
///
/// Mirrors the Android SDK's <c>NewVPNDeviceResponse</c> + the protocol-keyed
/// <c>GRDCredential.createGRDCredential</c> factory.
/// </summary>
public sealed class VPNDeviceResponse
{
    // IKEv2
    [JsonPropertyName("eap-username")] public string? EapUsername { get; set; }
    [JsonPropertyName("eap-password")] public string? EapPassword { get; set; }

    // WireGuard
    [JsonPropertyName("server-public-key")] public string? ServerPublicKey { get; set; }
    [JsonPropertyName("mapped-ipv4-address")] public string? MappedIPv4Address { get; set; }
    [JsonPropertyName("mapped-ipv6-address")] public string? MappedIPv6Address { get; set; }
    [JsonPropertyName("client-id")] public string? ClientId { get; set; }

    // Shared
    [JsonPropertyName("api-auth-token")] public string? ApiAuthToken { get; set; }
}
