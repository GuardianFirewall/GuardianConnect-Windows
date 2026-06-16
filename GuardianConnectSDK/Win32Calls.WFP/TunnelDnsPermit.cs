using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.NetworkManagement.WindowsFilteringPlatform;
using Windows.Win32.Security;
using Serilog;

namespace Win32Calls.WFP;

/// <summary>
/// LUID-keyed DNS permit filters for the active VPN tunnel adapter,
/// regardless of transport. Lives in the VPN DNS sublayer
/// (<see cref="VpnDnsSublayerGuid"/>) so the filters it installs compose
/// correctly with the matching block-all-DNS filters that
/// <c>VpnUtils.AddWpmFilters</c> (IKEv2) and
/// <c>WireGuardDnsBlockPermit.Install</c> (WireGuard) install in the
/// same sublayer.
///
/// Filters added here match: UDP/TCP traffic to remote port 53 leaving
/// on the supplied LUID's interface — i.e. DNS queries the host sends
/// out through the VPN tunnel adapter. They are permits, intended to
/// coexist with a separate "block all DNS" filter at the same layer;
/// the permits' more specific conditions (3 conditions: protocol +
/// port + interface) cause WFP to prefer them over the block (1
/// condition: port) at equal weight.
///
/// Filter display name + description + label strings are also generalized
/// away from "WireGuard"-named text.
///
/// Modelled on KillSwitchFilters.AddPermitDnsXxxOnTunnelVx (same
/// condition shape: FWPM_CONDITION_IP_PROTOCOL + IP_REMOTE_PORT=53 +
/// IP_LOCAL_INTERFACE=LUID at ALE_AUTH_CONNECT_{V4,V6}); the
/// difference is the sublayer.
/// </summary>
public static unsafe class TunnelDnsPermit
{
    /// <summary>
    /// VPN DNS sublayer GUID. Must match <c>VpnUtils.kVpnDnsSublayerGUID</c>
    /// so these filters live next to the matching block-all-DNS filters.
    /// </summary>
    internal static readonly Guid VpnDnsSublayerGuid =
        new("754b7cbd-cad3-474e-8d2c-054413fd4509");

    private const ushort DnsPort = 53;
    private const byte ProtocolUdp = 17;
    private const byte ProtocolTcp = 6;

    private static readonly char[] FilterName =
        "Guardian Tunnel DNS Permit\0".ToCharArray();
    private static readonly char[] FilterDesc =
        "Permit DNS leaving on the VPN tunnel adapter\0".ToCharArray();

    // -----------------------------------------------------------------------------
    // Public API — four wrappers, one per (family, protocol) combination.
    // Returns the filter ID (track for later FwpmFilterDeleteById0). Returns 0 on
    // failure; check Marshal.GetLastWin32Error or the structured logs.
    // -----------------------------------------------------------------------------

    public static ulong AddPermitDnsUdpV4(HANDLE engine, ulong luid) =>
        AddFilter(engine, PInvoke.FWPM_LAYER_ALE_AUTH_CONNECT_V4, ProtocolUdp,
                  luid, "PermitDnsUdpOnTunnelV4");

    public static ulong AddPermitDnsTcpV4(HANDLE engine, ulong luid) =>
        AddFilter(engine, PInvoke.FWPM_LAYER_ALE_AUTH_CONNECT_V4, ProtocolTcp,
                  luid, "PermitDnsTcpOnTunnelV4");

    public static ulong AddPermitDnsUdpV6(HANDLE engine, ulong luid) =>
        AddFilter(engine, PInvoke.FWPM_LAYER_ALE_AUTH_CONNECT_V6, ProtocolUdp,
                  luid, "PermitDnsUdpOnTunnelV6");

    public static ulong AddPermitDnsTcpV6(HANDLE engine, ulong luid) =>
        AddFilter(engine, PInvoke.FWPM_LAYER_ALE_AUTH_CONNECT_V6, ProtocolTcp,
                  luid, "PermitDnsTcpOnTunnelV6");

    /// <summary>
    /// Install all four (UDP/TCP × V4/V6) permits in one call. Returns the
    /// list of filter IDs added. Callers track and pass back to
    /// <see cref="RemoveAll"/> for cleanup on disconnect.
    /// </summary>
    public static List<ulong> AddAll(HANDLE engine, ulong luid)
    {
        var ids = new List<ulong>(4);
        TrackId(ids, AddPermitDnsUdpV4(engine, luid));
        TrackId(ids, AddPermitDnsTcpV4(engine, luid));
        TrackId(ids, AddPermitDnsUdpV6(engine, luid));
        TrackId(ids, AddPermitDnsTcpV6(engine, luid));
        return ids;
    }

    /// <summary>
    /// Delete previously-installed permit filters by ID. Continues past
    /// individual failures (logs them); returns false if any deletion failed.
    /// </summary>
    public static bool RemoveAll(HANDLE engine, IEnumerable<ulong> filterIds)
    {
        var allOk = true;
        foreach (var id in filterIds)
        {
            if (id == 0) continue;
            var rv = PInvoke.FwpmFilterDeleteById0(engine, id);
            if (rv != 0)
            {
                Log.Warning("TunnelDnsPermit.RemoveAll: FwpmFilterDeleteById0({Id}) failed: 0x{Code:X8}", id, rv);
                allOk = false;
            }
        }
        return allOk;
    }

    // -----------------------------------------------------------------------------

    private static void TrackId(List<ulong> ids, ulong id)
    {
        if (id != 0) ids.Add(id);
    }

    private static ulong AddFilter(HANDLE engine, Guid layerKey, byte protocol,
                                   ulong luid, string label)
    {
        // FWPM_CONDITION_IP_LOCAL_INTERFACE takes a UINT64 LUID via pointer;
        // the condition value's union member holds the pointer so we keep
        // the storage on the stack while the call runs.
        ulong luidStorage = luid;

        var protoVal = new FWP_CONDITION_VALUE0
        {
            type = FWP_DATA_TYPE.FWP_UINT8,
            Anonymous = { uint8 = protocol }
        };
        var portVal = new FWP_CONDITION_VALUE0
        {
            type = FWP_DATA_TYPE.FWP_UINT16,
            Anonymous = { uint16 = DnsPort }
        };
        var luidVal = new FWP_CONDITION_VALUE0
        {
            type = FWP_DATA_TYPE.FWP_UINT64,
            Anonymous = { uint64 = &luidStorage }
        };

        var conditions = stackalloc FWPM_FILTER_CONDITION0[3];
        conditions[0] = new FWPM_FILTER_CONDITION0
        {
            fieldKey = PInvoke.FWPM_CONDITION_IP_PROTOCOL,
            matchType = FWP_MATCH_TYPE.FWP_MATCH_EQUAL,
            conditionValue = protoVal
        };
        conditions[1] = new FWPM_FILTER_CONDITION0
        {
            fieldKey = PInvoke.FWPM_CONDITION_IP_REMOTE_PORT,
            matchType = FWP_MATCH_TYPE.FWP_MATCH_EQUAL,
            conditionValue = portVal
        };
        conditions[2] = new FWPM_FILTER_CONDITION0
        {
            fieldKey = PInvoke.FWPM_CONDITION_IP_LOCAL_INTERFACE,
            matchType = FWP_MATCH_TYPE.FWP_MATCH_EQUAL,
            conditionValue = luidVal
        };

        var filter = default(FWPM_FILTER0);
        filter.layerKey = layerKey;
        filter.subLayerKey = VpnDnsSublayerGuid;
        filter.action.type = FWP_ACTION_TYPE.FWP_ACTION_PERMIT;
        filter.numFilterConditions = 3;
        filter.filterCondition = conditions;
        // weight stays at default (FWP_EMPTY) — WFP places it by recency / specificity
        // within the sublayer. Matches VpnUtils.PermitQueriesFromTAP's convention.

        fixed (char* pName = FilterName)
        fixed (char* pDesc = FilterDesc)
        {
            filter.displayData.name = new PWSTR(pName);
            filter.displayData.description = new PWSTR(pDesc);

            ulong filterId = 0;
            var rv = PInvoke.FwpmFilterAdd0(engine, &filter, PSECURITY_DESCRIPTOR.Null, &filterId);
            if (rv != 0)
            {
                Log.Error("TunnelDnsPermit.AddFilter[{Label}]: FwpmFilterAdd0 failed: 0x{Code:X8}", label, rv);
                return 0;
            }
            Log.Debug("TunnelDnsPermit.AddFilter[{Label}]: id={Id}, luid=0x{Luid:X16}", label, filterId, luid);
            return filterId;
        }
    }
}
