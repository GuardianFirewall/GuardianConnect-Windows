using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace Win32Calls.WireGuard;

/// <summary>
/// Parser for wg-quick text format. Resolves [Peer] Endpoint hostnames to an
/// IPEndPoint at parse time (DNS lookup, IPv4 preferred). Throws on malformed
/// input or anything the parser doesn't yet support (e.g. multiple peers).
/// </summary>
public static class WireGuardConfigParser
{
    public static WireGuardConfig Parse(string text)
    {
        if (text is null) throw new ArgumentNullException(nameof(text));

        WireGuardKey? privateKey = null;
        ushort? listenPort = null;
        var addresses = new List<IpNetwork>();
        var dnsServers = new List<IPAddress>();

        WireGuardKey? peerPublicKey = null;
        WireGuardKey? peerPresharedKey = null;
        IPEndPoint? peerEndpoint = null;
        var peerAllowedIPs = new List<IpNetwork>();
        ushort? peerKeepalive = null;
        bool inPeer = false;
        int peerCount = 0;

        string? section = null;
        int lineNo = 0;

        foreach (var rawLine in text.Split('\n'))
        {
            lineNo++;
            var line = rawLine.Trim();
            if (line.Length == 0) continue;
            if (line[0] == '#' || line[0] == ';') continue;

            if (line[0] == '[' && line[^1] == ']')
            {
                section = line[1..^1].Trim();
                if (string.Equals(section, "Peer", StringComparison.OrdinalIgnoreCase))
                {
                    if (++peerCount > 1)
                        throw new FormatException(
                            $"Line {lineNo}: only a single [Peer] is supported at Step 4a.");
                    inPeer = true;
                }
                else if (string.Equals(section, "Interface", StringComparison.OrdinalIgnoreCase))
                {
                    inPeer = false;
                }
                else
                {
                    throw new FormatException($"Line {lineNo}: unknown section '[{section}]'.");
                }
                continue;
            }

            int eq = line.IndexOf('=');
            if (eq < 0)
                throw new FormatException($"Line {lineNo}: expected 'Key = Value', got '{line}'.");

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();

            // Strip trailing inline comments
            int hash = value.IndexOf('#');
            if (hash >= 0) value = value[..hash].Trim();

            if (section is null)
                throw new FormatException($"Line {lineNo}: '{key}' appeared before any section header.");

            if (!inPeer)
            {
                // [Interface]
                switch (key.ToLowerInvariant())
                {
                    case "privatekey":
                        privateKey = WireGuardKey.FromBase64(value);
                        break;
                    case "listenport":
                        listenPort = ParseUShort(value, lineNo, "ListenPort");
                        break;
                    case "address":
                        foreach (var part in SplitCommaList(value))
                            addresses.Add(IpNetwork.Parse(part));
                        break;
                    case "dns":
                        foreach (var part in SplitCommaList(value))
                        {
                            // wg-quick allows search domains here; we only accept IPs
                            if (!IPAddress.TryParse(part, out var ip))
                                throw new FormatException(
                                    $"Line {lineNo}: DNS value '{part}' is not an IP address (search domains unsupported).");
                            dnsServers.Add(ip);
                        }
                        break;
                    case "mtu":
                    case "table":
                    case "preup":
                    case "postup":
                    case "predown":
                    case "postdown":
                    case "saveconfig":
                    case "fwmark":
                        // wg-quick directives we don't honour; silently ignore for now
                        break;
                    default:
                        throw new FormatException($"Line {lineNo}: unknown [Interface] key '{key}'.");
                }
            }
            else
            {
                // [Peer]
                switch (key.ToLowerInvariant())
                {
                    case "publickey":
                        peerPublicKey = WireGuardKey.FromBase64(value);
                        break;
                    case "presharedkey":
                        peerPresharedKey = WireGuardKey.FromBase64(value);
                        break;
                    case "endpoint":
                        peerEndpoint = ResolveEndpoint(value, lineNo);
                        break;
                    case "allowedips":
                        foreach (var part in SplitCommaList(value))
                            peerAllowedIPs.Add(IpNetwork.Parse(part));
                        break;
                    case "persistentkeepalive":
                        peerKeepalive = ParseUShort(value, lineNo, "PersistentKeepalive");
                        break;
                    default:
                        throw new FormatException($"Line {lineNo}: unknown [Peer] key '{key}'.");
                }
            }
        }

        if (privateKey is null)
            throw new FormatException("[Interface] PrivateKey is required.");
        if (peerCount == 0)
            throw new FormatException("Configuration must contain at least one [Peer].");
        if (peerPublicKey is null)
            throw new FormatException("[Peer] PublicKey is required.");
        if (peerEndpoint is null)
            throw new FormatException("[Peer] Endpoint is required.");

        return new WireGuardConfig
        {
            PrivateKey = privateKey,
            ListenPort = listenPort,
            Addresses = addresses,
            DnsServers = dnsServers,
            Peer = new WireGuardPeerConfig
            {
                PublicKey = peerPublicKey,
                PresharedKey = peerPresharedKey,
                Endpoint = peerEndpoint,
                AllowedIPs = peerAllowedIPs,
                PersistentKeepalive = peerKeepalive,
            }
        };
    }

    private static IEnumerable<string> SplitCommaList(string value)
    {
        foreach (var raw in value.Split(','))
        {
            var trimmed = raw.Trim();
            if (trimmed.Length > 0) yield return trimmed;
        }
    }

    private static ushort ParseUShort(string value, int lineNo, string fieldName)
    {
        if (!ushort.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var v))
            throw new FormatException($"Line {lineNo}: {fieldName} '{value}' is not a valid 16-bit unsigned integer.");
        return v;
    }

    private static IPEndPoint ResolveEndpoint(string value, int lineNo)
    {
        string host;
        string portStr;

        if (value.StartsWith('['))
        {
            // [ipv6]:port
            int closeBracket = value.IndexOf(']');
            if (closeBracket < 0 || closeBracket + 2 > value.Length || value[closeBracket + 1] != ':')
                throw new FormatException($"Line {lineNo}: malformed bracketed Endpoint '{value}'.");
            host = value[1..closeBracket];
            portStr = value[(closeBracket + 2)..];
        }
        else
        {
            int lastColon = value.LastIndexOf(':');
            if (lastColon < 0)
                throw new FormatException($"Line {lineNo}: Endpoint '{value}' must be 'host:port'.");
            host = value[..lastColon];
            portStr = value[(lastColon + 1)..];
        }

        if (!ushort.TryParse(portStr, NumberStyles.None, CultureInfo.InvariantCulture, out var port))
            throw new FormatException($"Line {lineNo}: Endpoint port '{portStr}' is not a valid 16-bit integer.");

        if (IPAddress.TryParse(host, out var literal))
            return new IPEndPoint(literal, port);

        // DNS resolve; prefer IPv4
        IPAddress[] resolved;
        try
        {
            resolved = Dns.GetHostAddresses(host);
        }
        catch (Exception ex)
        {
            throw new FormatException(
                $"Line {lineNo}: failed to resolve Endpoint host '{host}': {ex.Message}", ex);
        }
        if (resolved.Length == 0)
            throw new FormatException($"Line {lineNo}: DNS returned no addresses for '{host}'.");

        var v4 = Array.Find(resolved, a => a.AddressFamily == AddressFamily.InterNetwork);
        return new IPEndPoint(v4 ?? resolved[0], port);
    }
}
