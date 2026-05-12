using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Win32Calls.WireGuard;

/// <summary>
/// Owns a single WireGuardNT adapter's lifetime: create -> apply config -> Up,
/// and the inverse on teardown. Activate() and Deactivate() are not thread-safe;
/// callers serialise (e.g., <c>VpnTunnelManager</c> uses a lock around them).
///
/// Step 4a scope: this class manages only the wireguard.dll surface. IP / DNS /
/// route assignment to the adapter is Step 4b; without those, the tunnel is up
/// cryptographically but carries no traffic.
/// </summary>
public sealed unsafe class WireGuardTunnel : IDisposable
{
    private const string DefaultTunnelType = "GuardianFirewall";

    private readonly string _adapterName;
    private readonly string _tunnelType;
    private nint _adapter;
    private bool _isUp;

    /// <summary>The WireGuardNT adapter's IF_LUID. 0 while inactive.</summary>
    public ulong AdapterLuid { get; private set; }

    /// <summary>True once <see cref="Activate"/> has succeeded and before <see cref="Deactivate"/> runs.</summary>
    public bool IsUp => _isUp;

    public string AdapterName => _adapterName;

    public WireGuardTunnel(string adapterName, string tunnelType = DefaultTunnelType)
    {
        if (string.IsNullOrWhiteSpace(adapterName))
            throw new ArgumentException("Adapter name required.", nameof(adapterName));
        _adapterName = adapterName;
        _tunnelType = tunnelType;
    }

    public void Activate(WireGuardConfig config)
    {
        if (config is null) throw new ArgumentNullException(nameof(config));
        if (_adapter != 0)
            throw new InvalidOperationException("Tunnel is already active; deactivate first.");

        _adapter = WireGuardInterop.WireGuardCreateAdapter(_adapterName, _tunnelType, null);
        if (_adapter == 0)
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                $"WireGuardCreateAdapter('{_adapterName}') failed.");

        try
        {
            WireGuardInterop.WireGuardGetAdapterLUID(_adapter, out var luid);
            AdapterLuid = luid;

            ApplyConfiguration(config);

            if (!WireGuardInterop.WireGuardSetAdapterState(_adapter, WireGuardAdapterState.Up))
                throw new Win32Exception(Marshal.GetLastWin32Error(),
                    "WireGuardSetAdapterState(Up) failed.");

            _isUp = true;
        }
        catch
        {
            WireGuardInterop.WireGuardCloseAdapter(_adapter);
            _adapter = 0;
            AdapterLuid = 0;
            _isUp = false;
            throw;
        }
    }

    public void Deactivate()
    {
        if (_adapter == 0) return;

        if (_isUp)
        {
            // Best-effort; failing to bring it Down doesn't block CloseAdapter, but we record it.
            if (!WireGuardInterop.WireGuardSetAdapterState(_adapter, WireGuardAdapterState.Down))
            {
                // Swallow — we're tearing down regardless.
                _ = Marshal.GetLastWin32Error();
            }
            _isUp = false;
        }

        WireGuardInterop.WireGuardCloseAdapter(_adapter);
        _adapter = 0;
        AdapterLuid = 0;
    }

    public void Dispose() => Deactivate();

    private void ApplyConfiguration(WireGuardConfig config)
    {
        int allowedIpCount = config.Peer.AllowedIPs.Count;
        int totalBytes = sizeof(WireGuardInterface)
                       + sizeof(WireGuardPeer)
                       + sizeof(WireGuardAllowedIp) * allowedIpCount;

        // Bounded by config size; ~280 bytes for the Tim-NY config. Safe to stackalloc.
        byte* buffer = stackalloc byte[totalBytes];

        BuildConfigBuffer(config, buffer);

        if (!WireGuardInterop.WireGuardSetConfiguration(_adapter, (nint)buffer, (uint)totalBytes))
            throw new Win32Exception(Marshal.GetLastWin32Error(),
                "WireGuardSetConfiguration failed.");
    }

    private static void BuildConfigBuffer(WireGuardConfig config, byte* buffer)
    {
        int offset = 0;

        // Interface
        var iface = (WireGuardInterface*)(buffer + offset);
        *iface = default;
        iface->Flags = WireGuardInterfaceFlags.HasPrivateKey | WireGuardInterfaceFlags.ReplacePeers;
        for (int i = 0; i < WireGuardKey.LengthBytes; i++)
            iface->PrivateKey[i] = config.PrivateKey[i];
        if (config.ListenPort.HasValue)
        {
            iface->Flags |= WireGuardInterfaceFlags.HasListenPort;
            iface->ListenPort = config.ListenPort.Value;
        }
        iface->PeersCount = 1;
        offset += sizeof(WireGuardInterface);

        // Peer
        var peer = (WireGuardPeer*)(buffer + offset);
        *peer = default;
        peer->Flags = WireGuardPeerFlags.HasPublicKey
                    | WireGuardPeerFlags.HasEndpoint
                    | WireGuardPeerFlags.ReplaceAllowedIPs;
        for (int i = 0; i < WireGuardKey.LengthBytes; i++)
            peer->PublicKey[i] = config.Peer.PublicKey[i];
        if (config.Peer.PresharedKey is not null)
        {
            peer->Flags |= WireGuardPeerFlags.HasPresharedKey;
            for (int i = 0; i < WireGuardKey.LengthBytes; i++)
                peer->PresharedKey[i] = config.Peer.PresharedKey[i];
        }
        if (config.Peer.PersistentKeepalive.HasValue)
        {
            peer->Flags |= WireGuardPeerFlags.HasPersistentKeepalive;
            peer->PersistentKeepalive = config.Peer.PersistentKeepalive.Value;
        }
        WriteSockAddrInet(&peer->Endpoint, config.Peer.Endpoint);
        peer->AllowedIPsCount = (uint)config.Peer.AllowedIPs.Count;
        offset += sizeof(WireGuardPeer);

        // Allowed IPs (immediately follow their parent peer)
        foreach (var network in config.Peer.AllowedIPs)
        {
            var aip = (WireGuardAllowedIp*)(buffer + offset);
            *aip = default;
            aip->AddressFamily = network.IsV6
                ? WireGuardAddressFamily.InterNetworkV6
                : WireGuardAddressFamily.InterNetwork;
            aip->Cidr = (byte)network.PrefixLength;
            var addrBytes = network.Address.GetAddressBytes();
            for (int i = 0; i < addrBytes.Length; i++)
                aip->Address[i] = addrBytes[i];
            aip->Flags = WireGuardAllowedIpFlags.None;
            offset += sizeof(WireGuardAllowedIp);
        }
    }

    /// <summary>
    /// Fills a SOCKADDR_INET-shaped 28-byte buffer with the AF_INET / AF_INET6
    /// representation of an IPEndPoint. Family is native-order; port is
    /// network-order; address bytes are network-order (IPAddress.GetAddressBytes
    /// returns big-endian already).
    /// </summary>
    private static void WriteSockAddrInet(SockAddrInet* dst, IPEndPoint endpoint)
    {
        for (int i = 0; i < 28; i++) dst->Bytes[i] = 0;

        ushort family = endpoint.AddressFamily == AddressFamily.InterNetworkV6 ? (ushort)23 : (ushort)2;
        dst->Bytes[0] = (byte)(family & 0xFF);
        dst->Bytes[1] = (byte)((family >> 8) & 0xFF);

        var port = (ushort)endpoint.Port;
        dst->Bytes[2] = (byte)((port >> 8) & 0xFF);
        dst->Bytes[3] = (byte)(port & 0xFF);

        var addrBytes = endpoint.Address.GetAddressBytes();
        if (endpoint.AddressFamily == AddressFamily.InterNetwork)
        {
            // SOCKADDR_IN: family(2) port(2) addr(4) zero(8)
            for (int i = 0; i < 4; i++) dst->Bytes[4 + i] = addrBytes[i];
        }
        else
        {
            // SOCKADDR_IN6: family(2) port(2) flowinfo(4) addr(16) scope_id(4)
            // flowinfo stays 0
            for (int i = 0; i < 16; i++) dst->Bytes[8 + i] = addrBytes[i];
            var scope = (uint)endpoint.Address.ScopeId;
            dst->Bytes[24] = (byte)(scope & 0xFF);
            dst->Bytes[25] = (byte)((scope >> 8) & 0xFF);
            dst->Bytes[26] = (byte)((scope >> 16) & 0xFF);
            dst->Bytes[27] = (byte)((scope >> 24) & 0xFF);
        }
    }
}
