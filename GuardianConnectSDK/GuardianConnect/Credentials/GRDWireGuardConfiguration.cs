using System.Text;
using GuardianConnect.Abstractions;
using GuardianConnect.API.Model;
using GuardianConnect.Helpers;
using GuardianConnect.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GuardianConnect.Credentials;

public static class GRDWireGuardConfiguration
{
    private const int WireGuardEndpointPort = 51821;
    private const string DefaultDnsServers = "1.1.1.1, 1.0.0.1";

    // Smart Routing Proxy resolvers. One per region; there is no SRP resolver
    // outside the US and the UK, which is what limits the feature to those hosts.
    private const string SmartRoutingProxyDnsUS = "10.183.10.11";
    private const string SmartRoutingProxyDnsUK = "10.183.10.12";

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
    /// <param name="dnsServers">
    /// DNS servers for the [Interface] block. Falls back to the Cloudflare
    /// defaults when null or blank, and is ignored entirely when Smart Routing
    /// Proxy mode applies.
    /// </param>
    /// <param name="srpServer">
    /// The gateway record for the host being dialled, supplied so this function
    /// can decide whether Smart Routing Proxy DNS mode applies. Pass null to
    /// build a config with SRP unconditionally off.
    /// </param>
    public static string? WireGuardQuickConfigForCredential(GRDCredential credential, string? dnsServers = null,
        GRDSGWServer? srpServer = null)
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

        // Smart Routing Proxy wins over both the caller-supplied value and the
        // Cloudflare default when it applies.
        var srpDns = SmartRoutingProxyDnsFor(srpServer);
        var dns = srpDns ?? (string.IsNullOrWhiteSpace(dnsServers) ? DefaultDnsServers : dnsServers!);

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

    /// <summary>
    /// Returns the Smart Routing Proxy resolver to use, or null when SRP does not
    /// apply and normal DNS selection should stand. All three SRP preconditions
    /// are evaluated here so no caller has to reason about them: the host must
    /// advertise smart-routing-enabled, the user must have switched the feature
    /// on, and the host must be in the US or the UK. The WireGuard transport
    /// requirement is implicit — the caller rejects non-WireGuard credentials
    /// before reaching this point.
    /// </summary>
    private static string? SmartRoutingProxyDnsFor(GRDSGWServer? server)
    {
        if (server is null) return null;

        if (!server.SmartProxyRoutingEnabled)
        {
            Logger.LogDebug(
                "SmartRoutingProxyDnsFor: host {Host} does not advertise smart-routing-enabled.",
                server.Hostname);
            return null;
        }

        if (!GRDVPNHelper.IsSmartRoutingProxyEnabled())
        {
            Logger.LogDebug("SmartRoutingProxyDnsFor: user preference is off.");
            return null;
        }

        // Region is nullable: the per-region host-list endpoint omits it and
        // GRDServerManager back-fills it. Without it the country is unknown and
        // there is no resolver to pick.
        var iso = server.Region?.CountryISOCode;
        if (string.IsNullOrWhiteSpace(iso))
        {
            Logger.LogWarning(
                "SmartRoutingProxyDnsFor: host {Host} advertises smart routing but carries no region; "
                + "cannot resolve an SRP DNS server. Falling back to normal DNS.",
                server.Hostname);
            return null;
        }

        if (string.Equals(iso, "US", StringComparison.OrdinalIgnoreCase))
        {
            Logger.LogInformation(
                "SmartRoutingProxyDnsFor: SRP enabled for US host {Host}.", server.Hostname);
            return SmartRoutingProxyDnsUS;
        }

        // Region data has used both the ISO 3166 code for the United Kingdom and
        // the colloquial form, so accept either.
        if (string.Equals(iso, "GB", StringComparison.OrdinalIgnoreCase)
            || string.Equals(iso, "UK", StringComparison.OrdinalIgnoreCase))
        {
            Logger.LogInformation(
                "SmartRoutingProxyDnsFor: SRP enabled for UK host {Host}.", server.Hostname);
            return SmartRoutingProxyDnsUK;
        }

        Logger.LogInformation(
            "SmartRoutingProxyDnsFor: host {Host} is in {Iso}; SRP is only available on US and UK hosts.",
            server.Hostname, iso);
        return null;
    }
}
