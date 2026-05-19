using System.Text;
using GuardianConnect.Abstractions;
using GuardianConnect.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GuardianConnect.Credentials;

/// <summary>
/// Builds a wg-quick text config from a negotiated <see cref="GRDCredential"/>.
/// Mirrors <c>GRDWireGuardConfiguration.wireguardQuickConfigForCredential</c>
/// in the iOS/macOS SDK (Classes/Credentials/GRDWireGuardConfiguration.m)
/// line-for-line — same field order, same default DNS, same hardcoded
/// endpoint port (51821), same AllowedIPs (0.0.0.0/0, ::/0).
///
/// One intentional difference from macOS: when <c>IPv6Address</c> is
/// populated on the credential, it is included in the [Interface] Address
/// line. macOS drops it (line 31 of the Objective-C version uses
/// IPv4Address only); Windows handles dual-stack fine and there's no
/// reason to suppress it.
/// </summary>
public static class GRDWireGuardConfiguration
{
    private const int WireGuardEndpointPort = 51821;
    private const string DefaultDnsServers = "1.1.1.1, 1.0.0.1";

    private static ILogger _logger = NullLogger.Instance;
    private static ILogger Logger
    {
        get
        {
            if (_logger == NullLogger.Instance)
                _logger = StaticLoggerFactory.CreateLogger("GRDWireGuardConfiguration");
            return _logger;
        }
    }

    /// <summary>
    /// Returns the wg-quick text for this credential, or null if the
    /// credential is missing required fields (private key, public key,
    /// IPv4 address, server public key, or hostname).
    /// </summary>
    public static string? WireGuardQuickConfigForCredential(GRDCredential credential, string? dnsServers = null)
    {
        if (credential.TransportProtocol != GRDTransportProtocol.TransportProtocol.TransportWireGuard)
        {
            Logger.LogError("WireGuardQuickConfigForCredential: credential is not a WireGuard credential.");
            return null;
        }

        if (string.IsNullOrEmpty(credential.DevicePrivateKey)
            || string.IsNullOrEmpty(credential.DevicePublicKey)
            || string.IsNullOrEmpty(credential.IPv4Address)
            || string.IsNullOrEmpty(credential.ServerPublicKey)
            || string.IsNullOrEmpty(credential.HostName))
        {
            Logger.LogError("WireGuardQuickConfigForCredential: required credential information missing.");
            return null;
        }

        var dns = string.IsNullOrWhiteSpace(dnsServers) ? DefaultDnsServers : dnsServers!;

        // Build the [Interface] address line. Include IPv6 when the server
        // assigned one (macOS drops this; see class-level remark).
        var addresses = credential.IPv4Address;
        if (!string.IsNullOrEmpty(credential.IPv6Address))
            addresses = $"{credential.IPv4Address}, {credential.IPv6Address}";

        var sb = new StringBuilder();
        sb.AppendLine("[Interface]");
        sb.AppendLine($"PrivateKey = {credential.DevicePrivateKey}");
        sb.AppendLine($"Address = {addresses}");
        sb.AppendLine($"DNS = {dns}");
        sb.AppendLine();
        sb.AppendLine("[Peer]");
        sb.AppendLine($"PublicKey = {credential.ServerPublicKey}");
        sb.AppendLine("AllowedIPs = 0.0.0.0/0, ::/0");
        sb.AppendLine($"Endpoint = {credential.HostName}:{WireGuardEndpointPort}");

        var config = sb.ToString();
        Logger.LogDebug("Formatted WireGuard config:\n{Config}", config);
        return config;
    }
}
