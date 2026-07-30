using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.NetworkManagement.WindowsFilteringPlatform;
using Windows.Win32.Security;
using Serilog;

namespace Win32Calls.WFP;

/// <summary>
/// Always-on DNS-leak protection for the WireGuard transport. Installs a
/// block-all DNS pair in the VPN DNS sublayer plus four LUID-scoped permits
/// (UDP/TCP × V4/V6) so DNS leaving on the WireGuard adapter passes while
/// queries that would otherwise leak out via the physical NIC are blocked.
///
/// Mirrors the role of <see cref="VpnUtils.AddWpmFilters"/> for the IKEv2
/// path. The IKEv2 permit (<c>VpnUtils.PermitQueriesFromTAP</c>) has no
/// interface condition, so it permits DNS on any adapter; IKEv2 doesn't
/// leak in practice because the RAS PPP connection raises the physical
/// NIC's interface metric so far that Windows multi-homed DNS skips it.
/// Wintun (used by WireGuard) doesn't trigger that side effect, so the WG
/// path needs the real LUID-scoped permit primitives from
/// <see cref="TunnelDnsPermit"/>.
/// </summary>
public static unsafe class WireGuardDnsBlockPermit
{
    private const ushort DnsPort = 53;

    private static readonly char[] BlockFilterName =
        "Guardian WireGuard DNS Block\0".ToCharArray();
    private static readonly char[] BlockFilterDesc =
        "Block DNS leaving on non-WireGuard interfaces\0".ToCharArray();

    /// <summary>Opaque handle returned by <see cref="Install"/>; pass to <see cref="Uninstall"/>.</summary>
    public sealed class Installation
    {
        internal FWPM_ENGINE_HANDLE Engine;
        internal readonly List<ulong> FilterIds = new();
    }

    /// <summary>
    /// Opens a dynamic WFP engine, registers the VPN DNS sublayer, and
    /// installs the block + permit filter set scoped to <paramref name="adapterLuid"/>.
    /// Returns null on failure (engine left closed, no filters installed).
    /// </summary>
    public static Installation? Install(ulong adapterLuid)
    {
        var engine = VpnUtils.OpenWpmSession();
        if (engine == FWPM_ENGINE_HANDLE.Null)
        {
            Log.Error("WireGuardDnsBlockPermit.Install: OpenWpmSession failed.");
            return null;
        }

        var rv = VpnUtils.RegisterSublayer(engine, VpnUtils.kVpnDnsSublayerGUID);
        if (rv != 0)
        {
            Log.Error("WireGuardDnsBlockPermit.Install: RegisterSublayer failed: 0x{Code:X8}", rv);
            VpnUtils.CloseWpmSession(engine);
            return null;
        }

        var install = new Installation { Engine = engine };

        try
        {
            // Block-all DNS in the VPN DNS sublayer. Same shape as
            // VpnUtils.BlockIPv4Queries but returns the filter ID so we can
            // delete on disconnect without stomping on the IKEv2 static fields.
            TrackId(install, AddBlockDns(engine, PInvoke.FWPM_LAYER_ALE_AUTH_CONNECT_V4, "BlockDnsV4"));
            TrackId(install, AddBlockDns(engine, PInvoke.FWPM_LAYER_ALE_AUTH_CONNECT_V6, "BlockDnsV6"));

            // LUID-scoped permits — beat the block via more-specific conditions
            // (3 conditions vs 1) at equal weight (FWP_EMPTY).
            install.FilterIds.AddRange(TunnelDnsPermit.AddAll(engine, adapterLuid));

            Log.Information(
                "WireGuardDnsBlockPermit.Install: {Count} filters installed for LUID 0x{Luid:X16}",
                install.FilterIds.Count, adapterLuid);
            return install;
        }
        catch (Exception ex)
        {
            Log.Error(ex, "WireGuardDnsBlockPermit.Install: filter install threw; rolling back.");
            Uninstall(install);
            return null;
        }
    }

    /// <summary>
    /// Deletes installed filters and closes the WFP engine. Best-effort:
    /// continues past individual failures.
    /// </summary>
    public static void Uninstall(Installation install)
    {
        if (install.Engine == FWPM_ENGINE_HANDLE.Null) return;

        foreach (var id in install.FilterIds)
        {
            if (id == 0) continue;
            var rv = PInvoke.FwpmFilterDeleteById0(install.Engine, id);
            if (rv != 0)
                Log.Warning("WireGuardDnsBlockPermit.Uninstall: FwpmFilterDeleteById0({Id}) failed: 0x{Code:X8}", id, rv);
        }
        install.FilterIds.Clear();

        VpnUtils.CloseWpmSession(install.Engine);
        install.Engine = FWPM_ENGINE_HANDLE.Null;
        Log.Information("WireGuardDnsBlockPermit.Uninstall: complete.");
    }

    // -----------------------------------------------------------------------------

    private static void TrackId(Installation install, ulong id)
    {
        if (id != 0) install.FilterIds.Add(id);
    }

    private static ulong AddBlockDns(FWPM_ENGINE_HANDLE engine, Guid layerKey, string label)
    {
        var portVal = new FWP_CONDITION_VALUE0
        {
            type = FWP_DATA_TYPE.FWP_UINT16,
            Anonymous = { uint16 = DnsPort }
        };

        var condition = new FWPM_FILTER_CONDITION0
        {
            fieldKey = PInvoke.FWPM_CONDITION_IP_REMOTE_PORT,
            matchType = FWP_MATCH_TYPE.FWP_MATCH_EQUAL,
            conditionValue = portVal
        };

        var filter = default(FWPM_FILTER0);
        filter.subLayerKey = VpnUtils.kVpnDnsSublayerGUID;
        filter.layerKey = layerKey;
        filter.action.type = FWP_ACTION_TYPE.FWP_ACTION_BLOCK;
        filter.numFilterConditions = 1;
        filter.filterCondition = &condition;
        // weight stays at default FWP_EMPTY — TunnelDnsPermit's more-specific
        // 3-condition permits (proto + port + local-interface) win arbitration.

        fixed (char* pName = BlockFilterName)
        fixed (char* pDesc = BlockFilterDesc)
        {
            filter.displayData.name = new PWSTR(pName);
            filter.displayData.description = new PWSTR(pDesc);

            ulong filterId = 0;
            var rv = PInvoke.FwpmFilterAdd0(engine, &filter, PSECURITY_DESCRIPTOR.Null, &filterId);
            if (rv != 0)
            {
                Log.Error("WireGuardDnsBlockPermit.AddBlockDns[{Label}]: FwpmFilterAdd0 failed: 0x{Code:X8}", label, rv);
                return 0;
            }
            Log.Debug("WireGuardDnsBlockPermit.AddBlockDns[{Label}]: id={Id}", label, filterId);
            return filterId;
        }
    }
}
