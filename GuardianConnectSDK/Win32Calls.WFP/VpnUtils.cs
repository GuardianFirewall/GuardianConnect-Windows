using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.NetworkManagement.IpHelper;
using Windows.Win32.NetworkManagement.WindowsFilteringPlatform;
using Windows.Win32.Security;
using GuardianConnect.Shared;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;
using Serilog;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Win32Calls.WFP;

public class VpnUtils
{
    private const string GRD_NETWORK_PROFILE_GUID = "fbcfac3f-8459-419f-8e48-1f0b49cdb85e";
    private const string GRD_VPN_DNSSUBLAYER_GUID = "754b7cbd-cad3-474e-8d2c-054413fd4509";

    private const string kGuardianVpnHelperRegistryStoragePath = "Software\\GuardianSoftware\\Vpn\\HelperService";
    // The four LUID-scoped permit filter IDs (UDP/TCP × V4/V6) installed by
    // PermitQueriesFromTAP, tracked here so RemoveWpmFilters can delete them
    // on disconnect. Was previously two static ulongs (TAP_IPv4_Id /
    // TAP_IPv6_Id) backing the old unscoped-permit implementation; the
    // permit pipeline now installs four LUID-scoped permits via
    // TunnelDnsPermit.AddAll so we need a list.
    private static List<ulong> TAP_PermitIds = new();
    private static ulong QBlock_IPv6_Id;
    private static ulong QBlock_IPv4_Id;

    public static char[] adapterNameToMatch = new[] { '\0' };
    private static IP_ADAPTER_INFO adapterInfo;

    // Microsoft-Windows-NetworkProfile
    // fbcfac3f-8459-419f-8e48-1f0b49cdb85e
    internal static readonly Guid kNetworkProfileGUID = new("fbcfac3f-8459-419f-8e48-1f0b49cdb85e");

    // 754b7cbd-cad3-474e-8d2c-054413fd4509
    internal static readonly Guid kVpnDnsSublayerGUID = new("754b7cbd-cad3-474e-8d2c-054413fd4509");


    private static readonly char[] GuardianVPNServiceFilterName = "Guardian Firewall VPN Service Filter".ToCharArray();

    private static readonly char[] GuardianVPNServiceFilterDesc =
        "Session for Guardian Firewall VPN Service".ToCharArray();

    private static readonly char[] GuardianVpnFilterSubLayerName =
        "Guardian Firewall VPN Service Sublayer".ToCharArray();

    private static readonly char[] GuardianVpnFilterSubLayerDesc =
        "Sublayer for Guardian Firewall VPN Service".ToCharArray();

    private static PWSTR pSessionName;
    private static PWSTR pSessionDesc;

    private static FWPM_FILTER_CONDITION0[] conditions = new FWPM_FILTER_CONDITION0[2];

    private static ILogger _logger = NullLogger.Instance;

    public static ILogger Logger
    {
        get
        {
            if (_logger == NullLogger.Instance) _logger = StaticLoggerFactory.CreateLogger("VpnUtils");
            return _logger;
        }
    }

    public static unsafe HANDLE OpenWpmSession()
    {
        var engine = HANDLE.Null;
        var session = new FWPM_SESSION0();
        session.flags = PInvoke.FWPM_SESSION_FLAG_DYNAMIC;
        session.displayData = new FWPM_DISPLAY_DATA0();
        fixed (char* p = GuardianVPNServiceFilterName)
        {
            pSessionName = new PWSTR(p);
            session.displayData.name = pSessionName;
        }

        fixed (char* p = GuardianVPNServiceFilterDesc)
        {
            pSessionDesc = new PWSTR(p);
            session.displayData.description = pSessionDesc;
        }

        var result = PInvoke.FwpmEngineOpen0(
            null,
            PInvoke.RPC_C_AUTHN_WINNT,
            null,
            &session,
            &engine);

        if (result != 0)
        {
            Log.Error($"OpenWpmSession: Failed to open filter engine. Error: {result}");
            return HANDLE.Null;
        }

        Log.Debug("OpenWpmSession: success");
        return engine;
    }

    public static bool CloseWpmSession(HANDLE engine)
    {
        if (engine == HANDLE.Null) return true;
        var result = PInvoke.FwpmEngineClose0(engine);
        if (result != 0)
        {
            Log.Error($"CloseWpmSession: Failed to close filter engine. Error: {result}");
            return false;
        }

        return true;
    }

    internal static unsafe uint AddSublayer(HANDLE engineHandle, Guid uuid)
    {
        uint result = 0;

        var subLayer = new FWPM_SUBLAYER0();
        var ptr = &subLayer;
        subLayer.subLayerKey = uuid;
        subLayer.displayData = new FWPM_DISPLAY_DATA0();
        fixed (char* p = GuardianVpnFilterSubLayerName)
        {
            var pSubLayerName = new PWSTR(p);
            subLayer.displayData.name = pSubLayerName;
        }

        fixed (char* p = GuardianVpnFilterSubLayerDesc)
        {
            var pSublayerDesc = new PWSTR(p);
            subLayer.displayData.description = pSublayerDesc;
        }

        /* Add sublayer to the session */
        result = PInvoke.FwpmSubLayerAdd0(engineHandle, ptr, PSECURITY_DESCRIPTOR.Null);

        if (result != 0)
        {
            if (result == 0x000004b7) // ERROR_ALREADY_EXISTS
            {
                Log.Information($"AddSublayer: Sublayer already exists. Error: {result}");
                return 0;
            }

            Log.Error($"AddSublayer: Failed to add sublayer. Error: {result}");
            return result;
        }

        return result;
    }

    internal static unsafe uint RemoveSublayer(HANDLE engineHandle, Guid uuid)
    {
        uint result = 0;
        result = PInvoke.FwpmSubLayerDeleteByKey0(engineHandle, &uuid);
        if (result != 0)
        {
            Log.Error($"RemoveSublayer: Failed to remove sublayer. Error: {result}");
            return result;
        }

        return result;
    }

    internal static unsafe uint RegisterSublayer(HANDLE engineHandle, Guid uuid)
    {
        FWPM_SUBLAYER0* sublayerPtr = null;
        Log.Debug("RegisterSublayer: checking if sublayer already exists...");
        /* Check sublayer exists and add one if it does not */
        if (PInvoke.FwpmSubLayerGetByKey0(engineHandle, &uuid, &sublayerPtr) != 0)
        {
            Log.Debug("RegisterSublayer: sublayer does not exist, adding...");
            var result = AddSublayer(engineHandle, uuid);
            if (result != 0)
            {
                Log.Error($"RegisterSublayer: Failed to add sublayer. Error: {result}");
                return result;
            }
        }
        else
        {
            Log.Debug("RegisterSublayer: sublayer already exists.");
            PInvoke.FwpmFreeMemory0((void**)&sublayerPtr);
        }

        return 0;
    }

    public static unsafe int GetAdapterIndexByName()
    {
        var indexOfMatch = -1;
        uint adapterInfoSize = 0;
        var pAdapterInfoSize = &adapterInfoSize;
        if (PInvoke.GetAdaptersInfo(null, pAdapterInfoSize) != (uint)WIN32_ERROR.ERROR_BUFFER_OVERFLOW ||
            adapterInfoSize == 0)
        {
            Log.Error("GetAdapterIndexByName: Failed to get adapter info size.");
            return -1;
        }

        fixed (IP_ADAPTER_INFO* adapterInfoPtr = &adapterInfo)
        {
            if (PInvoke.GetAdaptersInfo(adapterInfoPtr, pAdapterInfoSize) != 0)
            {
                Log.Error("GetAdapterIndexByName: Failed to get adapter info.");
                return -1;
            }

            while (true)
            {
                var ci = 0;
                foreach (var chr in adapterInfo.Description.AsSpan())
                {
                    if (ci == adapterNameToMatch.Length)
                    {
                        indexOfMatch = (int)adapterInfo.ComboIndex;
                        return indexOfMatch;
                    }

                    if (adapterNameToMatch[ci++] != chr) break;
                    if (chr == 0) break;
                }

                if (adapterInfo.Next == null) break;

                adapterInfo = *adapterInfo.Next;
            }
        }

        return -1;
    }

    internal static unsafe uint BlockIPv4Queries(HANDLE engineHandle)
    {
        var cv = new FWP_CONDITION_VALUE0();
        cv.type = FWP_DATA_TYPE.FWP_UINT16;
        cv.Anonymous.uint16 = 53; // DNS port

        var condition = new FWPM_FILTER_CONDITION0();
        condition.fieldKey = PInvoke.FWPM_CONDITION_IP_REMOTE_PORT;
        condition.matchType = FWP_MATCH_TYPE.FWP_MATCH_EQUAL;
        condition.conditionValue = cv;

        var filter = new FWPM_FILTER0();
        filter.subLayerKey = kVpnDnsSublayerGUID;
        fixed (char* p = GuardianVPNServiceFilterName)
        {
            var pSubLayerName = new PWSTR(p);
            filter.displayData.name = pSubLayerName;
        }

        filter.weight.type = FWP_DATA_TYPE.FWP_UINT8;
        filter.weight.Anonymous.uint8 = 0xF;
        filter.filterCondition = &condition;
        filter.numFilterConditions = 1;

        /* Block all IPv4 DNS queries */
        filter.layerKey = PInvoke.FWPM_LAYER_ALE_AUTH_CONNECT_V4;
        filter.action.type = FWP_ACTION_TYPE.FWP_ACTION_BLOCK;
        filter.weight.type = FWP_DATA_TYPE.FWP_EMPTY;
        ulong filterId = 0;

        Log.Information("BlockIPv4Queries: Calling FwpmFilterAdd0 to add Block of IPV4Queries...");
        var retVal = PInvoke.FwpmFilterAdd0(engineHandle, &filter, PSECURITY_DESCRIPTOR.Null, &filterId);
        if (retVal != 0)
        {
            Log.Error($"BlockIPv4Queries: Failed to add IPv4 DNS block filter. Error: {retVal}");
            return retVal;
        }

        QBlock_IPv4_Id = filterId;
        Log.Information("BlockIPv4Queries: FwpmFilterAdd0 successfully added BlockIPv4 filter.");
        return retVal;
    }


    internal static unsafe uint BlockIPv6Queries(HANDLE engineHandle)
    {
        // Mirror BlockIPv4Queries: scope to port 53 explicitly. Previously,
        // this filter had numFilterConditions = 0, which would mean "block
        // every V6 packet at this layer in this sublayer" — only worked
        // because the V6 permit alongside it was equally unscoped and
        // cancelled it via higher weight. With the V6 permit now properly
        // LUID-scoped (PermitQueriesFromTAP -> TunnelDnsPermit.AddAll),
        // the V6 block also needs its own scoping so it doesn't break all
        // V6 traffic in this sublayer.
        var cv = new FWP_CONDITION_VALUE0();
        cv.type = FWP_DATA_TYPE.FWP_UINT16;
        cv.Anonymous.uint16 = 53; // DNS port

        var condition = new FWPM_FILTER_CONDITION0();
        condition.fieldKey = PInvoke.FWPM_CONDITION_IP_REMOTE_PORT;
        condition.matchType = FWP_MATCH_TYPE.FWP_MATCH_EQUAL;
        condition.conditionValue = cv;

        var filter = new FWPM_FILTER0();
        filter.subLayerKey = kVpnDnsSublayerGUID;
        fixed (char* p = GuardianVPNServiceFilterName)
        {
            var pSubLayerName = new PWSTR(p);
            filter.displayData.name = pSubLayerName;
        }

        filter.weight.type = FWP_DATA_TYPE.FWP_EMPTY;
        filter.filterCondition = &condition;
        filter.numFilterConditions = 1;

        /* Block all IPv6 DNS queries */
        filter.layerKey = PInvoke.FWPM_LAYER_ALE_AUTH_CONNECT_V6;
        filter.action.type = FWP_ACTION_TYPE.FWP_ACTION_BLOCK;
        ulong filterId = 0;
        Log.Information("BlockIPv6Queries: Calling FwpmFilterAdd0 to add Block of IPV6Queries...");
        var retVal = PInvoke.FwpmFilterAdd0(engineHandle, &filter, PSECURITY_DESCRIPTOR.Null, &filterId);
        if (retVal != 0)
        {
            Log.Error($"BlockIPv6Queries: Failed to add IPv6 DNS block filter. Error: {retVal}");
            return retVal;
        }

        QBlock_IPv6_Id = filterId;
        Log.Information("BlockIPv6Queries: FwpmFilterAdd0 successfully added BlockIPv6 filter.");
        return retVal;
    }


    // Install LUID-scoped DNS permits for the IKEv2 tunnel adapter.
    // Previously, this added two unscoped permits (V4 + V6) with
    // numFilterConditions = 0 and a higher weight than the matching
    // block filters — i.e. the permits fired for every packet at the
    // layer, not just DNS, and not just on the tunnel adapter. The WFP
    // pipeline contributed zero leak protection; IKEv2 stayed leak-free
    // only because the Windows RAS PPP connection raises the physical
    // adapter's interface metric to ~4245, which makes the multi-homed
    // DNS resolver skip it. See WorkProgression/IKEv2DnsLeakFix.md for
    // the full postmortem.
    //
    // The fix uses the existing TunnelDnsPermit.AddAll primitive —
    // it's name-flavoured but generic: four 3-condition permits (UDP/TCP
    // x V4/V6) scoped to FWPM_CONDITION_IP_LOCAL_INTERFACE = <tunnel
    // LUID> + IP_REMOTE_PORT = 53 + IP_PROTOCOL = UDP|TCP. WFP arbitration
    // prefers the more-specific (3 conditions) permit over the
    // less-specific (1 condition: port-53) block at equal weight.
    internal static uint PermitQueriesFromTAP(HANDLE engineHandle, string connectionName)
    {
        // Resolve the IKEv2 tunnel adapter LUID by name first, then fall
        // back to the same description / IF_TYPE_PPP strategies
        // KillSwitchService uses. The IKEv2 RAS connection is up by the
        // time SetFilters runs (called from VpnDnsFilteringHandler.UpdateFiltersState
        // on the CONNECTED branch), so at least one strategy should match.
        Log.Information(
            "PermitQueriesFromTAP: resolving IKEv2 tunnel LUID (RAS entry='{Entry}')",
            connectionName);

        ulong? tunnelLuid = null;
        if (!string.IsNullOrEmpty(connectionName))
            tunnelLuid = AdapterLuidResolver.FindTunnelLuidByEntryName(connectionName);
        tunnelLuid ??= AdapterLuidResolver.FindFirstUpAdapterByDescriptionContains("WAN Miniport (IKEv2)");
        tunnelLuid ??= AdapterLuidResolver.FindFirstUpPppAdapter();

        if (tunnelLuid == null)
        {
            Log.Error(
                "PermitQueriesFromTAP: tunnel LUID not resolved by any strategy. " +
                "Refusing to install unscoped permits — IKEv2 connect will fail closed. " +
                "Diagnostic dump of up adapters follows.");
            Log.Error(AdapterLuidResolver.DumpUpAdapters());
            return 1; // non-zero = failure; AddWpmFilters returns false; SetFilters reports failure
        }

        Log.Information(
            "PermitQueriesFromTAP: resolved IKEv2 tunnel LUID 0x{Luid:X16}", tunnelLuid.Value);

        var ids = TunnelDnsPermit.AddAll(engineHandle, tunnelLuid.Value);
        if (ids.Count != 4)
        {
            Log.Error(
                "PermitQueriesFromTAP: TunnelDnsPermit.AddAll installed {Count}/4 permits; " +
                "rolling back partial install.", ids.Count);
            TunnelDnsPermit.RemoveAll(engineHandle, ids);
            return 1;
        }

        TAP_PermitIds = ids;
        Log.Information(
            "PermitQueriesFromTAP: installed 4 LUID-scoped DNS permits (luid=0x{Luid:X16}).",
            tunnelLuid.Value);
        return 0;
    }

    public static bool AddWpmFilters(HANDLE engine_handle, string name)
    {
        if (engine_handle == HANDLE.Null)
        {
            Log.Error("AddWpmFilters: Invalid engine handle.");
            return false;
        }

        Log.Debug("AddWpmFilters: Calling RegisterSubLayer()...");
        var result = RegisterSublayer(engine_handle, kVpnDnsSublayerGUID);
        if (result != 0)
        {
            Log.Error($"AddWpmFilters: Failed to register sublayer. Error: {result}");
            return false;
        }

        // Block all IPv4 DNS queries
        Log.Debug("AddWpmFilters: Calling BlockIPv4Queries()...");
        result = BlockIPv4Queries(engine_handle);
        if (result != 0)
        {
            Log.Error($"AddWpmFilters: Failed to block IPv4 DNS queries. Error: {result}");
            return false;
        }

        // Block all IPv6 DNS Queries
        Log.Debug("AddWpmFilters: Calling BlockIPv6Queries()...");
        result = BlockIPv6Queries(engine_handle);
        if (result != 0)
        {
            Log.Error($"AddWpmFilters: Failed to block IPv6 DNS queries. Error: {result}");
            return false;
        }

        // Permit IPv4 DNS queries from TAP adapter
        Log.Debug("AddWpmFilters: Calling PermitIPv4QueriesFromTAP()...");
        result = PermitQueriesFromTAP(engine_handle, name);
        if (result != 0)
        {
            Log.Error($"AddWpmFilters: Failed to permit IPv4 DNS queries from TAP adapter. Error: {result}");
            return false;
        }

        Log.Debug("AddWpmFilters: Added block filters for all interfaces");

        return true;
    }

    public static bool RemoveWpmFilters(HANDLE engine_handle, string name)
    {
        // We need to fall through and try to remove all filters even if one fails.
        var whetherSuccessful = true;
        try
        {
            if (engine_handle == HANDLE.Null)
            {
                Log.Error("RemoveWpmFilters: Invalid engine handle.");
                whetherSuccessful = false;
            }

            uint result = 0;

            // Remove the four LUID-scoped DNS permits installed by
            // PermitQueriesFromTAP via TunnelDnsPermit.AddAll
            // (UDP/TCP × V4/V6). Previously, this section removed two
            // static TAP_IPv4_Id / TAP_IPv6_Id filter IDs (unscoped
            // permits). The new install path returns a list; RemoveAll
            // walks it and continues past individual failures.
            if (TAP_PermitIds.Count > 0)
            {
                Log.Debug(
                    "RemoveWpmFilters: Removing {Count} LUID-scoped DNS permit filters...",
                    TAP_PermitIds.Count);
                if (!TunnelDnsPermit.RemoveAll(engine_handle, TAP_PermitIds))
                {
                    Log.Error("RemoveWpmFilters: at least one LUID-scoped DNS permit removal failed.");
                    whetherSuccessful = false;
                }
                TAP_PermitIds = new List<ulong>();
            }

            Log.Information("RemoveWpmFilters: Removing QBlock_IPv6...");
            result = PInvoke.FwpmFilterDeleteById0(engine_handle, QBlock_IPv6_Id);
            if (result != 0)
            {
                Log.Error($"RemoveWpmFilters: Failed to remove QBlock_IPv6 filter. Error: {result}");
                whetherSuccessful = false;
            }

            Log.Information("RemoveWpmFilters: Removing QBlock_IPv4...");
            result = PInvoke.FwpmFilterDeleteById0(engine_handle, QBlock_IPv4_Id);
            if (result != 0)
            {
                Log.Error($"RemoveWpmFilters: Failed to remove QBlock_IPv4 filter. Error: {result}");
                whetherSuccessful = false;
            }

            // Remove sublayer
            Log.Debug("RemoveWpmFilters: Removing sublayer...");
            result = RemoveSublayer(engine_handle, kVpnDnsSublayerGUID);
            if (result != 0)
            {
                Log.Error($"RemoveWpmFilters: Failed to remove sublayer. Error: {result}");
                whetherSuccessful = false;
            }


            Log.Information("RemoveWpmFilters: Successfully removed WPM filters.");
        }
        catch (Exception e)
        {
            Log.Error(e, $"Exception thrown while trying to remove WpmFilters. '{e.Message}");
            whetherSuccessful = false;
        }

        return whetherSuccessful;
    }

    public static void SetFiltersInstalledFlag()
    {
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(kGuardianVpnHelperRegistryStoragePath))
            {
                if (key != null) key.SetValue("FiltersInstalled", 1, RegistryValueKind.DWord);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"SetFiltersInstalledFlag: Failed to set registry key. Exception: {ex.Message}");
        }
    }

    public static void ResetFiltersInstalledFlag()
    {
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(kGuardianVpnHelperRegistryStoragePath))
            {
                if (key != null) key.SetValue("FiltersInstalled", 0, RegistryValueKind.DWord);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"ResetFiltersInstalledFlag: Failed to reset registry key. Exception: {ex.Message}");
        }
    }
}