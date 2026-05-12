using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using Windows.Win32;
using Windows.Win32.NetworkManagement.IpHelper;
using Windows.Win32.NetworkManagement.Ndis;
using Windows.Win32.Networking.WinSock;

namespace Win32Calls.WFP;

/// <summary>
/// Adapter-level network configuration primitives: IP assignment, routes,
/// DNS servers, interface metric. Used after a transport (e.g. WireGuard)
/// has brought its adapter up cryptographically — these calls give the OS
/// routing stack the information it needs for traffic to flow.
///
/// All entry points return Win32 error codes (0 = success). Callers
/// translate to <c>Win32Exception</c> if they want to throw.
///
/// SOCKADDR_INET-shaped fields are written as raw bytes against the
/// 28-byte SOCKADDR_INET layout (family LE, port BE, address BE for v4
/// or flowinfo+addr+scope for v6). This avoids depending on CsWin32's
/// nested union field names which differ across metadata versions.
/// </summary>
public static unsafe partial class AdapterIpDnsRoutes
{
    private const ushort AfInet = 2;    // AF_INET
    private const ushort AfInet6 = 23;  // AF_INET6

    // ---------------------------------------------------------------------
    // Unicast IP assignment
    // ---------------------------------------------------------------------

    public static int AddUnicastAddress(ulong luid, IPAddress address, byte prefixLength)
    {
        var row = default(MIB_UNICASTIPADDRESS_ROW);
        PInvoke.InitializeUnicastIpAddressEntry(out row);

        row.InterfaceLuid = new NET_LUID_LH { Value = luid };
        WriteSockAddrInet((byte*)&row.Address, address);
        row.OnLinkPrefixLength = prefixLength;

        return (int)PInvoke.CreateUnicastIpAddressEntry(in row);
    }

    public static int RemoveUnicastAddress(ulong luid, IPAddress address, byte prefixLength)
    {
        var row = default(MIB_UNICASTIPADDRESS_ROW);
        PInvoke.InitializeUnicastIpAddressEntry(out row);

        row.InterfaceLuid = new NET_LUID_LH { Value = luid };
        WriteSockAddrInet((byte*)&row.Address, address);
        row.OnLinkPrefixLength = prefixLength;

        return (int)PInvoke.DeleteUnicastIpAddressEntry(in row);
    }

    // ---------------------------------------------------------------------
    // Routes
    // ---------------------------------------------------------------------

    /// <summary>
    /// Add a route through the adapter. NextHop is left "on-link" (zero
    /// address) which is correct for a point-to-point tunnel adapter.
    /// Metric 0 means "use the interface metric only" — callers usually
    /// pair this with <see cref="SetInterfaceMetric"/> set to a low value
    /// so the tunnel beats the physical NIC's default route.
    /// </summary>
    public static int AddRoute(ulong luid, IPAddress destination, byte prefixLength, uint metric = 0)
    {
        var row = default(MIB_IPFORWARD_ROW2);
        PInvoke.InitializeIpForwardEntry(out row);

        row.InterfaceLuid = new NET_LUID_LH { Value = luid };
        WriteSockAddrInet((byte*)&row.DestinationPrefix.Prefix, destination);
        row.DestinationPrefix.PrefixLength = prefixLength;

        // NextHop: same family as destination, all-zero address (on-link).
        var nhBytes = (byte*)&row.NextHop;
        for (int i = 0; i < 28; i++) nhBytes[i] = 0;
        ushort nhFamily = destination.AddressFamily == AddressFamily.InterNetworkV6 ? AfInet6 : AfInet;
        nhBytes[0] = (byte)(nhFamily & 0xFF);
        nhBytes[1] = (byte)((nhFamily >> 8) & 0xFF);

        row.Metric = metric;
        // Leave row.Protocol at InitializeIpForwardEntry's default (NlRouteProtocolOther).
        // wg-quick on Windows doesn't set it either; setting NetMgmt requires casting
        // through the CsWin32-generated NL_ROUTE_PROTOCOL enum and isn't load-bearing.

        return (int)PInvoke.CreateIpForwardEntry2(in row);
    }

    public static int RemoveRoute(ulong luid, IPAddress destination, byte prefixLength)
    {
        var row = default(MIB_IPFORWARD_ROW2);
        PInvoke.InitializeIpForwardEntry(out row);

        row.InterfaceLuid = new NET_LUID_LH { Value = luid };
        WriteSockAddrInet((byte*)&row.DestinationPrefix.Prefix, destination);
        row.DestinationPrefix.PrefixLength = prefixLength;

        var nhBytes = (byte*)&row.NextHop;
        for (int i = 0; i < 28; i++) nhBytes[i] = 0;
        ushort nhFamily = destination.AddressFamily == AddressFamily.InterNetworkV6 ? AfInet6 : AfInet;
        nhBytes[0] = (byte)(nhFamily & 0xFF);
        nhBytes[1] = (byte)((nhFamily >> 8) & 0xFF);

        return (int)PInvoke.DeleteIpForwardEntry2(in row);
    }

    // ---------------------------------------------------------------------
    // Interface metric
    // ---------------------------------------------------------------------

    /// <summary>
    /// Set the adapter's per-family interface metric. Sets both IPv4 and
    /// IPv6 to the same value. Lower wins; 1 is the minimum.
    /// </summary>
    public static int SetInterfaceMetric(ulong luid, uint metric)
    {
        var v4 = SetMetricForFamily(luid, isV6: false, metric);
        if (v4 != 0) return v4;
        var v6 = SetMetricForFamily(luid, isV6: true, metric);
        return v6;
    }

    private static int SetMetricForFamily(ulong luid, bool isV6, uint metric)
    {
        var row = default(MIB_IPINTERFACE_ROW);
        row.Family = (ADDRESS_FAMILY)(isV6 ? AfInet6 : AfInet);
        row.InterfaceLuid = new NET_LUID_LH { Value = luid };

        var get = (int)PInvoke.GetIpInterfaceEntry(ref row);
        if (get != 0) return get;

        row.UseAutomaticMetric = false;
        row.Metric = metric;

        // SitePrefixLength is in/out and undocumented behavior on Set — clear it.
        row.SitePrefixLength = 0;

        return (int)PInvoke.SetIpInterfaceEntry(ref row);
    }

    // ---------------------------------------------------------------------
    // DNS
    // ---------------------------------------------------------------------

    /// <summary>
    /// Sets DNS servers on the adapter. IPv4 and IPv6 servers are split and
    /// applied separately via SetInterfaceDnsSettings. Pass an empty list
    /// to clear.
    /// </summary>
    public static int SetDnsServers(ulong luid, IReadOnlyList<IPAddress> dnsServers)
    {
        var luidStruct = new NET_LUID_LH { Value = luid };
        Guid guid;
        var convertResult = (int)PInvoke.ConvertInterfaceLuidToGuid(in luidStruct, out guid);
        if (convertResult != 0) return convertResult;

        var v4Servers = new List<string>();
        var v6Servers = new List<string>();
        foreach (var server in dnsServers)
        {
            if (server.AddressFamily == AddressFamily.InterNetwork) v4Servers.Add(server.ToString());
            else if (server.AddressFamily == AddressFamily.InterNetworkV6) v6Servers.Add(server.ToString());
        }

        // Always call both: passing empty NameServer clears that family.
        var v4 = SetDnsForFamily(guid, string.Join(",", v4Servers), isV6: false);
        if (v4 != 0) return v4;
        var v6 = SetDnsForFamily(guid, string.Join(",", v6Servers), isV6: true);
        return v6;
    }

    public static int ClearDnsSettings(ulong luid) =>
        SetDnsServers(luid, Array.Empty<IPAddress>());

    private static int SetDnsForFamily(Guid interfaceGuid, string nameServers, bool isV6)
    {
        var nsPtr = Marshal.StringToHGlobalUni(nameServers);
        try
        {
            var settings = new DNS_INTERFACE_SETTINGS_V1
            {
                Version = DnsInterfaceSettingsVersion1,
                Flags = DnsSettingNameServer | (isV6 ? DnsSettingIpv6 : 0UL),
                NameServer = nsPtr,
            };
            return (int)SetInterfaceDnsSettings(interfaceGuid, ref settings);
        }
        finally
        {
            Marshal.FreeHGlobal(nsPtr);
        }
    }

    // ---------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------

    private static void WriteSockAddrInet(byte* dst, IPAddress address)
    {
        for (int i = 0; i < 28; i++) dst[i] = 0;

        ushort family = address.AddressFamily == AddressFamily.InterNetworkV6 ? AfInet6 : AfInet;
        dst[0] = (byte)(family & 0xFF);
        dst[1] = (byte)((family >> 8) & 0xFF);
        // port at [2..3] stays 0

        var addrBytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            // SOCKADDR_IN: addr bytes at offset 4..8, network byte order (GetAddressBytes already big-endian)
            for (int i = 0; i < 4; i++) dst[4 + i] = addrBytes[i];
        }
        else
        {
            // SOCKADDR_IN6: flowinfo at 4..8 (0); addr at 8..24; scope_id at 24..28
            for (int i = 0; i < 16; i++) dst[8 + i] = addrBytes[i];
            var scope = (uint)address.ScopeId;
            dst[24] = (byte)(scope & 0xFF);
            dst[25] = (byte)((scope >> 8) & 0xFF);
            dst[26] = (byte)((scope >> 16) & 0xFF);
            dst[27] = (byte)((scope >> 24) & 0xFF);
        }
    }

    // ---------------------------------------------------------------------
    // Hand-written P/Invoke for SetInterfaceDnsSettings (dnsapi.dll).
    // CsWin32's dnsapi surface is unused elsewhere; avoiding the metadata
    // expansion by declaring just what we need here.
    // ---------------------------------------------------------------------

    private const uint DnsInterfaceSettingsVersion1 = 1;
    private const ulong DnsSettingNameServer = 0x01;
    private const ulong DnsSettingIpv6       = 0x10;

    [StructLayout(LayoutKind.Sequential)]
    private struct DNS_INTERFACE_SETTINGS_V1
    {
        public uint Version;
        public ulong Flags;
        public IntPtr Domain;
        public IntPtr NameServer;
        public IntPtr SearchList;
        public uint RegistrationEnabled;
        public uint RegisterAdapterName;
        public uint EnableLLMNR;
        public uint QueryAdapterName;
        public IntPtr ProfileNameServer;
    }

    [LibraryImport("dnsapi.dll", EntryPoint = "SetInterfaceDnsSettings")]
    private static partial uint SetInterfaceDnsSettings(
        Guid interfaceId, ref DNS_INTERFACE_SETTINGS_V1 settings);
}
