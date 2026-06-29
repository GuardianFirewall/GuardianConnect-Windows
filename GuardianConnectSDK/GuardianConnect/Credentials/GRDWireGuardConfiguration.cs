using System.Text;
using GuardianConnect.Abstractions;
using GuardianConnect.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GuardianConnect.Credentials;

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

        // Pluck the server-provided fields from the device response DTO; the
        // device keypair (private/public key) is client-side and stays on the
        // flat fields. EnsureDeviceFromLegacyFields makes Device non-null for
        // any credential, including legacy persisted ones.
        credential.EnsureDeviceFromLegacyFields();
        var device = credential.Device!;

        if (string.IsNullOrEmpty(credential.DevicePrivateKey)
            || string.IsNullOrEmpty(credential.DevicePublicKey)
            || string.IsNullOrEmpty(device.MappedIPv4Address)
            || string.IsNullOrEmpty(device.ServerPublicKey)
            || string.IsNullOrEmpty(credential.HostName))
        {
            Logger.LogError("WireGuardQuickConfigForCredential: required credential information missing.");
            return null;
        }

        var dns = string.IsNullOrWhiteSpace(dnsServers) ? DefaultDnsServers : dnsServers!;

        // Build the [Interface] address line. Include IPv6 when the server
        // assigned one (macOS drops this; see class-level remark).
        var addresses = device.MappedIPv4Address;
        if (!string.IsNullOrEmpty(device.MappedIPv6Address))
            addresses = $"{device.MappedIPv4Address}, {device.MappedIPv6Address}";

        var sb = new StringBuilder();
        sb.AppendLine("[Interface]");
        sb.AppendLine($"PrivateKey = {credential.DevicePrivateKey}");
        sb.AppendLine($"Address = {addresses}");
        sb.AppendLine($"DNS = {dns}");
        sb.AppendLine();
        sb.AppendLine("[Peer]");
        sb.AppendLine($"PublicKey = {device.ServerPublicKey}");
        sb.AppendLine("AllowedIPs = 0.0.0.0/0, ::/0");
        sb.AppendLine($"Endpoint = {credential.HostName}:{WireGuardEndpointPort}");

        var config = sb.ToString();
        Logger.LogDebug("Formatted WireGuard config:\n{Config}", config);
        return config;
    }
}
