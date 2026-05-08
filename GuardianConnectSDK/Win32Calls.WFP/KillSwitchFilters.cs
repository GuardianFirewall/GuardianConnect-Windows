using System;
using System.Collections.Generic;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.NetworkManagement.WindowsFilteringPlatform;
using Windows.Win32.Security;
using Serilog;

namespace Win32Calls.WFP;

/// <summary>
/// Kill Switch WFP primitives. v1 = OnConnected mode, dynamic session only.
///
/// All filters live under <see cref="SublayerDynamicGuid"/> in a session created with
/// <c>FWPM_SESSION_FLAG_DYNAMIC</c>; they tear down automatically when the engine
/// handle closes (process exit, service crash, taskkill).
///
/// Designed to coexist with the existing DNS sublayer in <see cref="VpnUtils"/>: separate
/// GUID, separate filter-tracking, no shared state.
/// </summary>
public static unsafe class KillSwitchFilters
{
    /// <summary>
    /// Stable v4 GUID for the Guardian Kill Switch dynamic sublayer. Generated 2026-05-07.
    /// </summary>
    public static readonly Guid SublayerDynamicGuid = new("a8b25112-5957-4a66-93de-e632f4653537");

    // Filter weight convention (per Microsoft WFP guidance, matches the plan in §1):
    //   1 = block-all                    (lowest priority — runs after permits)
    //   2 = LAN permits                  (higher priority than block-all)
    //   3 = DNS block                    (specific port traffic)
    //   4 = specific permits             (loopback, DHCP, tunnel adapter, whitelisted apps)
    private const byte WeightBlockAll      = 1;
    private const byte WeightLanPermit     = 2;
    private const byte WeightDnsBlock      = 3;
    private const byte WeightSpecificPermit = 4;

    private const byte ProtocolUdp = 17;     // IANA: UDP
    private const ushort DhcpV4ServerPort = 67;
    private const ushort DhcpV4ClientPort = 68;

    private static readonly char[] SessionName  = "Guardian Kill Switch Session\0".ToCharArray();
    private static readonly char[] SessionDesc  = "Dynamic WFP session for Guardian Kill Switch (OnConnected mode)\0".ToCharArray();
    private static readonly char[] SublayerName = "Guardian Kill Switch Sublayer\0".ToCharArray();
    private static readonly char[] SublayerDesc = "Dynamic sublayer hosting Guardian Kill Switch filters\0".ToCharArray();
    private static readonly char[] FilterName   = "Guardian Kill Switch Filter\0".ToCharArray();
    private static readonly char[] FilterDesc   = "Filter installed by Guardian Kill Switch\0".ToCharArray();

    // -----------------------------------------------------------------------------------
    // Engine lifecycle
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// Open a WFP engine handle in dynamic-session mode. All filters added through this
    /// handle disappear when the handle closes (or the process exits).
    /// </summary>
    public static HANDLE OpenDynamicEngine()
    {
        var engine = HANDLE.Null;
        var session = new FWPM_SESSION0
        {
            flags = PInvoke.FWPM_SESSION_FLAG_DYNAMIC,
            displayData = new FWPM_DISPLAY_DATA0()
        };

        fixed (char* pName = SessionName)
        fixed (char* pDesc = SessionDesc)
        {
            session.displayData.name = new PWSTR(pName);
            session.displayData.description = new PWSTR(pDesc);

            var result = PInvoke.FwpmEngineOpen0(
                null,
                PInvoke.RPC_C_AUTHN_WINNT,
                null,
                &session,
                &engine);

            if (result != 0)
            {
                Log.Error($"KillSwitchFilters.OpenDynamicEngine: FwpmEngineOpen0 failed. Error: 0x{result:X8}");
                return HANDLE.Null;
            }
        }

        Log.Debug("KillSwitchFilters.OpenDynamicEngine: success");
        return engine;
    }

    /// <summary>Close a WFP engine handle. Idempotent for HANDLE.Null.</summary>
    public static bool CloseEngine(HANDLE engine)
    {
        if (engine == HANDLE.Null) return true;
        var result = PInvoke.FwpmEngineClose0(engine);
        if (result != 0)
        {
            Log.Error($"KillSwitchFilters.CloseEngine: FwpmEngineClose0 failed. Error: 0x{result:X8}");
            return false;
        }
        return true;
    }

    /// <summary>
    /// Register the kill-switch dynamic sublayer if not already present. Idempotent —
    /// if the sublayer already exists for this session, returns success.
    /// </summary>
    public static uint EnsureDynamicSublayerRegistered(HANDLE engine)
    {
        if (engine == HANDLE.Null) return uint.MaxValue;

        FWPM_SUBLAYER0* existing = null;
        var sublayerKey = SublayerDynamicGuid;
        if (PInvoke.FwpmSubLayerGetByKey0(engine, &sublayerKey, &existing) == 0)
        {
            PInvoke.FwpmFreeMemory0((void**)&existing);
            Log.Debug("KillSwitchFilters.EnsureDynamicSublayerRegistered: sublayer already present.");
            return 0;
        }

        var sublayer = new FWPM_SUBLAYER0
        {
            subLayerKey = SublayerDynamicGuid,
            displayData = new FWPM_DISPLAY_DATA0(),
            weight = 1001
        };

        fixed (char* pName = SublayerName)
        fixed (char* pDesc = SublayerDesc)
        {
            sublayer.displayData.name = new PWSTR(pName);
            sublayer.displayData.description = new PWSTR(pDesc);

            var result = PInvoke.FwpmSubLayerAdd0(engine, &sublayer, PSECURITY_DESCRIPTOR.Null);
            if (result != 0 && result != 0x000004B7) // ERROR_ALREADY_EXISTS
            {
                Log.Error($"KillSwitchFilters.EnsureDynamicSublayerRegistered: FwpmSubLayerAdd0 failed. Error: 0x{result:X8}");
                return result;
            }
        }

        Log.Debug("KillSwitchFilters.EnsureDynamicSublayerRegistered: sublayer registered.");
        return 0;
    }

    // -----------------------------------------------------------------------------------
    // Transaction wrappers — atomic filter batching so the engine never sees a partially
    // installed kill-switch state.
    // -----------------------------------------------------------------------------------

    public static uint BeginTransaction(HANDLE engine)
    {
        var result = PInvoke.FwpmTransactionBegin0(engine, 0);
        if (result != 0)
            Log.Error($"KillSwitchFilters.BeginTransaction: FwpmTransactionBegin0 failed. Error: 0x{result:X8}");
        return result;
    }

    public static uint CommitTransaction(HANDLE engine)
    {
        var result = PInvoke.FwpmTransactionCommit0(engine);
        if (result != 0)
            Log.Error($"KillSwitchFilters.CommitTransaction: FwpmTransactionCommit0 failed. Error: 0x{result:X8}");
        return result;
    }

    public static uint AbortTransaction(HANDLE engine)
    {
        var result = PInvoke.FwpmTransactionAbort0(engine);
        if (result != 0)
            Log.Error($"KillSwitchFilters.AbortTransaction: FwpmTransactionAbort0 failed. Error: 0x{result:X8}");
        return result;
    }

    // -----------------------------------------------------------------------------------
    // Block-all filters (weight 1) — installed at IPv4/IPv6 ALE auth connect/recv accept
    // layers. The IPv6 blocks are required even though our IKEv2 RAS tunnel is IPv4-only:
    // Windows defaults to IPv6 enabled and prefers v6 (RFC 6724). Without v6 blocks, dual-
    // stack hosts leak around the tunnel by default.
    // -----------------------------------------------------------------------------------

    public static ulong AddBlockAllOutboundV4(HANDLE engine) =>
        AddSimpleFilter(engine, PInvoke.FWPM_LAYER_ALE_AUTH_CONNECT_V4,
                        FWP_ACTION_TYPE.FWP_ACTION_BLOCK, WeightBlockAll, "BlockAllOutboundV4");

    public static ulong AddBlockAllInboundV4(HANDLE engine) =>
        AddSimpleFilter(engine, PInvoke.FWPM_LAYER_ALE_AUTH_RECV_ACCEPT_V4,
                        FWP_ACTION_TYPE.FWP_ACTION_BLOCK, WeightBlockAll, "BlockAllInboundV4");

    public static ulong AddBlockAllOutboundV6(HANDLE engine) =>
        AddSimpleFilter(engine, PInvoke.FWPM_LAYER_ALE_AUTH_CONNECT_V6,
                        FWP_ACTION_TYPE.FWP_ACTION_BLOCK, WeightBlockAll, "BlockAllOutboundV6");

    public static ulong AddBlockAllInboundV6(HANDLE engine) =>
        AddSimpleFilter(engine, PInvoke.FWPM_LAYER_ALE_AUTH_RECV_ACCEPT_V6,
                        FWP_ACTION_TYPE.FWP_ACTION_BLOCK, WeightBlockAll, "BlockAllInboundV6");

    // -----------------------------------------------------------------------------------
    // Loopback permits (weight 4) — per-layer permit gated on the loopback flag.
    // -----------------------------------------------------------------------------------

    public static ulong AddPermitLoopbackOutboundV4(HANDLE engine) =>
        AddLoopbackFilter(engine, PInvoke.FWPM_LAYER_ALE_AUTH_CONNECT_V4, "PermitLoopbackOutboundV4");

    public static ulong AddPermitLoopbackInboundV4(HANDLE engine) =>
        AddLoopbackFilter(engine, PInvoke.FWPM_LAYER_ALE_AUTH_RECV_ACCEPT_V4, "PermitLoopbackInboundV4");

    public static ulong AddPermitLoopbackOutboundV6(HANDLE engine) =>
        AddLoopbackFilter(engine, PInvoke.FWPM_LAYER_ALE_AUTH_CONNECT_V6, "PermitLoopbackOutboundV6");

    public static ulong AddPermitLoopbackInboundV6(HANDLE engine) =>
        AddLoopbackFilter(engine, PInvoke.FWPM_LAYER_ALE_AUTH_RECV_ACCEPT_V6, "PermitLoopbackInboundV6");

    // -----------------------------------------------------------------------------------
    // DHCP v4 permits (weight 4) — DHCP client sends UDP/68 -> server UDP/67; server
    // replies UDP/67 -> client UDP/68.
    // -----------------------------------------------------------------------------------

    public static ulong AddPermitDhcpOutboundV4(HANDLE engine)
    {
        // Outbound UDP to remote port 67 (DHCP server)
        var protoVal = new FWP_CONDITION_VALUE0
        {
            type = FWP_DATA_TYPE.FWP_UINT8,
            Anonymous = { uint8 = ProtocolUdp }
        };
        var portVal = new FWP_CONDITION_VALUE0
        {
            type = FWP_DATA_TYPE.FWP_UINT16,
            Anonymous = { uint16 = DhcpV4ServerPort }
        };
        var conditions = stackalloc FWPM_FILTER_CONDITION0[2];
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

        return AddFilterWithConditions(engine, PInvoke.FWPM_LAYER_ALE_AUTH_CONNECT_V4,
                                       FWP_ACTION_TYPE.FWP_ACTION_PERMIT, WeightSpecificPermit,
                                       conditions, 2, "PermitDhcpOutboundV4");
    }

    public static ulong AddPermitDhcpInboundV4(HANDLE engine)
    {
        // Inbound UDP to local port 68 (DHCP client receiving server reply)
        var protoVal = new FWP_CONDITION_VALUE0
        {
            type = FWP_DATA_TYPE.FWP_UINT8,
            Anonymous = { uint8 = ProtocolUdp }
        };
        var portVal = new FWP_CONDITION_VALUE0
        {
            type = FWP_DATA_TYPE.FWP_UINT16,
            Anonymous = { uint16 = DhcpV4ClientPort }
        };
        var conditions = stackalloc FWPM_FILTER_CONDITION0[2];
        conditions[0] = new FWPM_FILTER_CONDITION0
        {
            fieldKey = PInvoke.FWPM_CONDITION_IP_PROTOCOL,
            matchType = FWP_MATCH_TYPE.FWP_MATCH_EQUAL,
            conditionValue = protoVal
        };
        conditions[1] = new FWPM_FILTER_CONDITION0
        {
            fieldKey = PInvoke.FWPM_CONDITION_IP_LOCAL_PORT,
            matchType = FWP_MATCH_TYPE.FWP_MATCH_EQUAL,
            conditionValue = portVal
        };

        return AddFilterWithConditions(engine, PInvoke.FWPM_LAYER_ALE_AUTH_RECV_ACCEPT_V4,
                                       FWP_ACTION_TYPE.FWP_ACTION_PERMIT, WeightSpecificPermit,
                                       conditions, 2, "PermitDhcpInboundV4");
    }

    // -----------------------------------------------------------------------------------
    // Tunnel-adapter permits (weight 4) — permit any traffic where the local interface
    // matches the VPN adapter's IF_LUID. Caller is responsible for resolving the LUID
    // post-connect.
    // -----------------------------------------------------------------------------------

    public static ulong AddPermitTunnelLuidOutboundV4(HANDLE engine, ulong luid) =>
        AddTunnelLuidFilter(engine, PInvoke.FWPM_LAYER_ALE_AUTH_CONNECT_V4, luid,
                            "PermitTunnelLuidOutboundV4");

    public static ulong AddPermitTunnelLuidInboundV4(HANDLE engine, ulong luid) =>
        AddTunnelLuidFilter(engine, PInvoke.FWPM_LAYER_ALE_AUTH_RECV_ACCEPT_V4, luid,
                            "PermitTunnelLuidInboundV4");

    public static ulong AddPermitTunnelLuidOutboundV6(HANDLE engine, ulong luid) =>
        AddTunnelLuidFilter(engine, PInvoke.FWPM_LAYER_ALE_AUTH_CONNECT_V6, luid,
                            "PermitTunnelLuidOutboundV6");

    public static ulong AddPermitTunnelLuidInboundV6(HANDLE engine, ulong luid) =>
        AddTunnelLuidFilter(engine, PInvoke.FWPM_LAYER_ALE_AUTH_RECV_ACCEPT_V6, luid,
                            "PermitTunnelLuidInboundV6");

    // -----------------------------------------------------------------------------------
    // LAN permits (weight 2) — opt-in. Installs permit filters for the standard private +
    // link-local + multicast/broadcast ranges on both outbound (ALE_AUTH_CONNECT) and
    // inbound (ALE_AUTH_RECV_ACCEPT) layers, both V4 and V6.
    //
    // Range list (per plan §3.3):
    //   V4: 10.0.0.0/8, 172.16.0.0/12, 192.168.0.0/16, 169.254.0.0/16, 224.0.0.0/4, 255.255.255.255/32
    //   V6: fe80::/10, fc00::/7
    //
    // Returns the filter IDs added (caller tracks them for later DeleteFiltersById).
    // -----------------------------------------------------------------------------------

    public static List<ulong> AddPermitLanAll(HANDLE engine)
    {
        var ids = new List<ulong>();
        var v4Outbound = PInvoke.FWPM_LAYER_ALE_AUTH_CONNECT_V4;
        var v4Inbound  = PInvoke.FWPM_LAYER_ALE_AUTH_RECV_ACCEPT_V4;
        var v6Outbound = PInvoke.FWPM_LAYER_ALE_AUTH_CONNECT_V6;
        var v6Inbound  = PInvoke.FWPM_LAYER_ALE_AUTH_RECV_ACCEPT_V6;

        // V4 ranges (addr, mask) in host byte order
        (uint addr, uint mask, string name)[] v4Ranges =
        {
            (0x0A000000u, 0xFF000000u, "10.0.0.0/8"),
            (0xAC100000u, 0xFFF00000u, "172.16.0.0/12"),
            (0xC0A80000u, 0xFFFF0000u, "192.168.0.0/16"),
            (0xA9FE0000u, 0xFFFF0000u, "169.254.0.0/16"),
            (0xE0000000u, 0xF0000000u, "224.0.0.0/4"),
            (0xFFFFFFFFu, 0xFFFFFFFFu, "255.255.255.255/32"),
        };
        foreach (var r in v4Ranges)
        {
            TrackId(ids, AddPermitV4Subnet(engine, v4Outbound, r.addr, r.mask, $"PermitLanOutboundV4 {r.name}"));
            TrackId(ids, AddPermitV4Subnet(engine, v4Inbound,  r.addr, r.mask, $"PermitLanInboundV4 {r.name}"));
        }

        // V6 ranges (16-byte addr, prefix length)
        (byte[] addr, byte prefix, string name)[] v6Ranges =
        {
            (new byte[] { 0xFE, 0x80, 0,0,0,0,0,0, 0,0,0,0,0,0,0,0 }, (byte)10, "fe80::/10"),
            (new byte[] { 0xFC, 0x00, 0,0,0,0,0,0, 0,0,0,0,0,0,0,0 }, (byte)7,  "fc00::/7"),
        };
        foreach (var r in v6Ranges)
        {
            TrackId(ids, AddPermitV6Subnet(engine, v6Outbound, r.addr, r.prefix, $"PermitLanOutboundV6 {r.name}"));
            TrackId(ids, AddPermitV6Subnet(engine, v6Inbound,  r.addr, r.prefix, $"PermitLanInboundV6 {r.name}"));
        }

        return ids;
    }

    private static void TrackId(List<ulong> ids, ulong id)
    {
        if (id != 0) ids.Add(id);
    }

    // -----------------------------------------------------------------------------------
    // Cleanup
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// Delete a batch of previously-installed filters by ID. Returns true if all deletions
    /// succeeded; logs and continues past individual failures so the rest get cleaned up.
    /// </summary>
    public static bool DeleteFiltersById(HANDLE engine, IEnumerable<ulong> filterIds)
    {
        if (engine == HANDLE.Null) return false;
        var allSucceeded = true;
        foreach (var id in filterIds)
        {
            if (id == 0) continue;
            var result = PInvoke.FwpmFilterDeleteById0(engine, id);
            if (result != 0)
            {
                Log.Error($"KillSwitchFilters.DeleteFiltersById: FwpmFilterDeleteById0({id}) failed. Error: 0x{result:X8}");
                allSucceeded = false;
            }
        }
        return allSucceeded;
    }

    // -----------------------------------------------------------------------------------
    // Internal helpers
    // -----------------------------------------------------------------------------------

    private static ulong AddSimpleFilter(HANDLE engine, Guid layerKey, FWP_ACTION_TYPE action,
                                         byte weight, string label)
    {
        return AddFilterWithConditions(engine, layerKey, action, weight, null, 0, label);
    }

    private static ulong AddLoopbackFilter(HANDLE engine, Guid layerKey, string label)
    {
        // Condition on the FWPM_CONDITION_FLAGS field; permit when IS_LOOPBACK is set.
        var flagsVal = new FWP_CONDITION_VALUE0
        {
            type = FWP_DATA_TYPE.FWP_UINT32,
            Anonymous = { uint32 = PInvoke.FWP_CONDITION_FLAG_IS_LOOPBACK }
        };
        var condition = new FWPM_FILTER_CONDITION0
        {
            fieldKey = PInvoke.FWPM_CONDITION_FLAGS,
            matchType = FWP_MATCH_TYPE.FWP_MATCH_FLAGS_ANY_SET,
            conditionValue = flagsVal
        };

        return AddFilterWithConditions(engine, layerKey, FWP_ACTION_TYPE.FWP_ACTION_PERMIT,
                                       WeightSpecificPermit, &condition, 1, label);
    }

    private static ulong AddPermitV4Subnet(HANDLE engine, Guid layerKey, uint addr, uint mask, string label)
    {
        FWP_V4_ADDR_AND_MASK addrMask;
        addrMask.addr = addr;
        addrMask.mask = mask;

        var val = new FWP_CONDITION_VALUE0
        {
            type = FWP_DATA_TYPE.FWP_V4_ADDR_MASK,
            Anonymous = { v4AddrMask = &addrMask }
        };
        var condition = new FWPM_FILTER_CONDITION0
        {
            fieldKey = PInvoke.FWPM_CONDITION_IP_REMOTE_ADDRESS,
            matchType = FWP_MATCH_TYPE.FWP_MATCH_EQUAL,
            conditionValue = val
        };

        return AddFilterWithConditions(engine, layerKey, FWP_ACTION_TYPE.FWP_ACTION_PERMIT,
                                       WeightLanPermit, &condition, 1, label);
    }

    private static ulong AddPermitV6Subnet(HANDLE engine, Guid layerKey, byte[] addr16, byte prefixLength, string label)
    {
        if (addr16.Length != 16) throw new ArgumentException("V6 address must be 16 bytes", nameof(addr16));

        FWP_V6_ADDR_AND_MASK v6;
        v6.prefixLength = prefixLength;
        for (int i = 0; i < 16; i++) v6.addr[i] = addr16[i];

        var val = new FWP_CONDITION_VALUE0
        {
            type = FWP_DATA_TYPE.FWP_V6_ADDR_MASK,
            Anonymous = { v6AddrMask = &v6 }
        };
        var condition = new FWPM_FILTER_CONDITION0
        {
            fieldKey = PInvoke.FWPM_CONDITION_IP_REMOTE_ADDRESS,
            matchType = FWP_MATCH_TYPE.FWP_MATCH_EQUAL,
            conditionValue = val
        };

        return AddFilterWithConditions(engine, layerKey, FWP_ACTION_TYPE.FWP_ACTION_PERMIT,
                                       WeightLanPermit, &condition, 1, label);
    }

    private static ulong AddTunnelLuidFilter(HANDLE engine, Guid layerKey, ulong luid, string label)
    {
        // FWPM_CONDITION_IP_LOCAL_INTERFACE expects a UINT64 holding the IF_LUID. The
        // condition value's union member uint64 is a pointer, so we pin a stack ulong.
        ulong luidStorage = luid;
        var luidVal = new FWP_CONDITION_VALUE0
        {
            type = FWP_DATA_TYPE.FWP_UINT64,
            Anonymous = { uint64 = &luidStorage }
        };
        var condition = new FWPM_FILTER_CONDITION0
        {
            fieldKey = PInvoke.FWPM_CONDITION_IP_LOCAL_INTERFACE,
            matchType = FWP_MATCH_TYPE.FWP_MATCH_EQUAL,
            conditionValue = luidVal
        };

        return AddFilterWithConditions(engine, layerKey, FWP_ACTION_TYPE.FWP_ACTION_PERMIT,
                                       WeightSpecificPermit, &condition, 1, label);
    }

    private static ulong AddFilterWithConditions(HANDLE engine, Guid layerKey,
                                                 FWP_ACTION_TYPE action, byte weight,
                                                 FWPM_FILTER_CONDITION0* conditions,
                                                 uint conditionCount, string label)
    {
        if (engine == HANDLE.Null)
        {
            Log.Error($"KillSwitchFilters.{label}: invalid engine handle.");
            return 0;
        }

        var filter = new FWPM_FILTER0
        {
            subLayerKey = SublayerDynamicGuid,
            layerKey = layerKey,
            displayData = new FWPM_DISPLAY_DATA0(),
            action = { type = action },
            weight = { type = FWP_DATA_TYPE.FWP_UINT8, Anonymous = { uint8 = weight } },
            filterCondition = conditions,
            numFilterConditions = conditionCount
        };

        ulong filterId = 0;
        fixed (char* pName = FilterName)
        fixed (char* pDesc = FilterDesc)
        {
            filter.displayData.name = new PWSTR(pName);
            filter.displayData.description = new PWSTR(pDesc);

            var result = PInvoke.FwpmFilterAdd0(engine, &filter, PSECURITY_DESCRIPTOR.Null, &filterId);
            if (result != 0)
            {
                Log.Error($"KillSwitchFilters.{label}: FwpmFilterAdd0 failed. Error: 0x{result:X8}");
                return 0;
            }
        }

        Log.Debug($"KillSwitchFilters.{label}: added filterId={filterId}");
        return filterId;
    }
}
