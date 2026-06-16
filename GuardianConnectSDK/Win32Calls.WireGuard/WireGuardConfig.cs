using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace Win32Calls.WireGuard;

/// <summary>
/// Parsed representation of a wg-quick-format WireGuard configuration.
///
/// Only fields that map to WireGuardNT's WIREGUARD_INTERFACE/WIREGUARD_PEER are
/// modelled here. wg-quick directives that affect Windows-side adapter setup
/// (Address, DNS) are parsed and stored but consumed at Step 4b (adapter IP /
/// DNS / route configuration), not by the wireguard.dll API.
///
/// At Step 4a, a single peer is supported. The parser throws if multiple
/// [Peer] sections appear.
/// </summary>
public sealed class WireGuardConfig
{
    public required WireGuardKey PrivateKey { get; init; }
    public ushort? ListenPort { get; init; }

    /// <summary>
    /// [Interface] Address — adapter IP(s) the tunnel will own once Step 4b is in.
    /// Each entry is a CIDR; if the wg-quick line omitted the prefix length, the
    /// parser fills in /32 (IPv4) or /128 (IPv6).
    /// </summary>
    public IReadOnlyList<IpNetwork> Addresses { get; init; } = Array.Empty<IpNetwork>();

    /// <summary>
    /// [Interface] DNS — DNS servers for the tunnel. Stored for Step 4b consumption.
    /// </summary>
    public IReadOnlyList<IPAddress> DnsServers { get; init; } = Array.Empty<IPAddress>();

    public required WireGuardPeerConfig Peer { get; init; }
}

public sealed class WireGuardPeerConfig
{
    public required WireGuardKey PublicKey { get; init; }
    public WireGuardKey? PresharedKey { get; init; }
    public required IPEndPoint Endpoint { get; init; }
    public IReadOnlyList<IpNetwork> AllowedIPs { get; init; } = Array.Empty<IpNetwork>();
    public ushort? PersistentKeepalive { get; init; }
}

/// <summary>
/// An IP address plus prefix length (CIDR). Used by both [Interface] Address and [Peer] AllowedIPs.
/// </summary>
public readonly record struct IpNetwork(IPAddress Address, int PrefixLength)
{
    public bool IsV6 => Address.AddressFamily == AddressFamily.InterNetworkV6;

    /// <summary>
    /// Parse "10.0.0.1", "10.0.0.0/24", "fd00::1", "fd00::/64". If the prefix
    /// length is omitted, defaults to /32 (IPv4) or /128 (IPv6) per wg-quick.
    /// </summary>
    public static IpNetwork Parse(string s)
    {
        if (string.IsNullOrWhiteSpace(s))
            throw new FormatException("IP network string cannot be empty.");

        s = s.Trim();
        var slash = s.IndexOf('/');
        IPAddress address;
        int prefix;

        if (slash < 0)
        {
            address = IPAddress.Parse(s);
            prefix = address.AddressFamily == AddressFamily.InterNetworkV6 ? 128 : 32;
        }
        else
        {
            address = IPAddress.Parse(s[..slash]);
            if (!int.TryParse(s[(slash + 1)..], NumberStyles.None, CultureInfo.InvariantCulture, out prefix))
                throw new FormatException($"Invalid CIDR prefix in '{s}'.");
        }

        int maxPrefix = address.AddressFamily == AddressFamily.InterNetworkV6 ? 128 : 32;
        if (prefix < 0 || prefix > maxPrefix)
            throw new FormatException($"CIDR prefix {prefix} out of range for '{s}'.");

        return new IpNetwork(address, prefix);
    }
}
