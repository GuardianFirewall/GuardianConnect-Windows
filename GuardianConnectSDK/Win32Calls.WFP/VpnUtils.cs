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
    private static ulong TAP_IPv4_Id;
    private static ulong TAP_IPv6_Id;
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

        Log.LogDebug("OpenWpmSession: success");
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
        Log.LogDebug("RegisterSublayer: checking if sublayer already exists...");
        /* Check sublayer exists and add one if it does not */
        if (PInvoke.FwpmSubLayerGetByKey0(engineHandle, &uuid, &sublayerPtr) != 0)
        {
            Log.LogDebug("RegisterSublayer: sublayer does not exist, adding...");
            var result = AddSublayer(engineHandle, uuid);
            if (result != 0)
            {
                Log.Error($"RegisterSublayer: Failed to add sublayer. Error: {result}");
                return result;
            }
        }
        else
        {
            Log.LogDebug("RegisterSublayer: sublayer already exists.");
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


    // TODO: IPv6 ...
    internal static unsafe uint BlockIPv6Queries(HANDLE engineHandle)
    {
        var filter = new FWPM_FILTER0();
        filter.subLayerKey = kVpnDnsSublayerGUID;
        fixed (char* p = GuardianVPNServiceFilterName)
        {
            var pSubLayerName = new PWSTR(p);
            filter.displayData.name = pSubLayerName;
        }

        filter.weight.type = FWP_DATA_TYPE.FWP_EMPTY;
        //filter.weight.Anonymous.uint8 = 0xF;
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


    // Permit IPv4 DNS queries from TAP.
    // Use a non-zero weight so that the permit filters get higher priority
    // over the block filter added with automatic weighting */
    internal static unsafe uint PermitQueriesFromTAP(HANDLE engineHandle, string connectionName)
    {
        // Filter
        var filter = new FWPM_FILTER0();
        Log.Information(
            $"PermitQueriesFromTAP: Setting filter.subLayerKey to kVpnDnsSublayerGUID {kVpnDnsSublayerGUID}");
        filter.subLayerKey = kVpnDnsSublayerGUID;
        fixed (char* p = GuardianVPNServiceFilterName)
        {
            var pSubLayerName = new PWSTR(p);
            filter.displayData.name = pSubLayerName;
        }

        filter.weight.type = FWP_DATA_TYPE.FWP_UINT8;
        filter.weight.Anonymous.uint8 = 0xE; // Higher priority than block filter

        /* Permit all IPv4 DNS queries from TAP adapter */
        filter.layerKey = PInvoke.FWPM_LAYER_ALE_AUTH_CONNECT_V4;
        filter.action.type = FWP_ACTION_TYPE.FWP_ACTION_PERMIT;
        // Filter created - continue with conditions...


        filter.numFilterConditions = 0;

        ulong filterId = 0;
        Log.LogDebug("PermitQueriesFromTAP: Calling FwpmFilterAdd0() to Permit IPv4 DNS queries from TAP...");
        var retVal = PInvoke.FwpmFilterAdd0(engineHandle, &filter, PSECURITY_DESCRIPTOR.Null, &filterId);
        if (retVal != 0)
        {
            if (retVal == 0x80320007)
                Log.Error(
                    $"PermitQueriesFromTAP: Failed to add IPv4 DNS permit filter. Error: {retVal:X8} [FWP_E_SUBLAYER_NOT_FOUND]");
            else
                Log.Error($"PermitQueriesFromTAP: Failed to add IPv4 DNS permit filter. Error: {retVal:X8}");
            return retVal;
        }

        TAP_IPv4_Id = filterId;
        Log.Information("PermitQueriesFromTAP: FwpmFilterAdd0 successfully added PermitIPv4 filter.");

        // Permit IPv6 DNS queries from TAP. Use same weight as IPv4 filter.
        filter.layerKey = PInvoke.FWPM_LAYER_ALE_AUTH_CONNECT_V6;
        Log.LogDebug("PermitQueriesFromTAP: Calling FwpmFilterAdd0() to Permit IPv6 DNS queries from TAP...");
        retVal = PInvoke.FwpmFilterAdd0(engineHandle, &filter, PSECURITY_DESCRIPTOR.Null, &filterId);
        if (retVal != 0)
        {
            Log.Error($"PermitQueriesFromTAP: Failed to add IPv6 DNS permit filter. Error: {retVal}");
            return retVal;
        }

        TAP_IPv6_Id = filterId;

        return retVal;
    }

    public static bool AddWpmFilters(HANDLE engine_handle, string name)
    {
        if (engine_handle == HANDLE.Null)
        {
            Log.Error("AddWpmFilters: Invalid engine handle.");
            return false;
        }

        Log.LogDebug("AddWpmFilters: Calling RegisterSubLayer()...");
        var result = RegisterSublayer(engine_handle, kVpnDnsSublayerGUID);
        if (result != 0)
        {
            Log.Error($"AddWpmFilters: Failed to register sublayer. Error: {result}");
            return false;
        }

        // Block all IPv4 DNS queries
        Log.LogDebug("AddWpmFilters: Calling BlockIPv4Queries()...");
        result = BlockIPv4Queries(engine_handle);
        if (result != 0)
        {
            Log.Error($"AddWpmFilters: Failed to block IPv4 DNS queries. Error: {result}");
            return false;
        }

        // Block all IPv6 DNS Queries
        Log.LogDebug("AddWpmFilters: Calling BlockIPv6Queries()...");
        result = BlockIPv6Queries(engine_handle);
        if (result != 0)
        {
            Log.Error($"AddWpmFilters: Failed to block IPv6 DNS queries. Error: {result}");
            return false;
        }

        // Permit IPv4 DNS queries from TAP adapter
        Log.LogDebug("AddWpmFilters: Calling PermitIPv4QueriesFromTAP()...");
        result = PermitQueriesFromTAP(engine_handle, name);
        if (result != 0)
        {
            Log.Error($"AddWpmFilters: Failed to permit IPv4 DNS queries from TAP adapter. Error: {result}");
            return false;
        }

        Log.LogDebug("AddWpmFilters: Added block filters for all interfaces");

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

            // Remove TAP IPv4 filter
            if (TAP_IPv4_Id != 0)
            {
                Log.LogDebug("RemoveWpmFilters: Removing TAP IPv4 filter...");
                result = PInvoke.FwpmFilterDeleteById0(engine_handle, TAP_IPv4_Id);
                if (result != 0)
                {
                    Log.Error($"RemoveWpmFilters: Failed to remove TAP IPv4 filter. Error: {result}");
                    whetherSuccessful = false;
                }

                TAP_IPv4_Id = 0;
            }

            // Remove TAP IPv6 filter
            if (TAP_IPv6_Id != 0)
            {
                Log.LogDebug("RemoveWpmFilters: Removing TAP IPv6 filter...");
                result = PInvoke.FwpmFilterDeleteById0(engine_handle, TAP_IPv6_Id);
                if (result != 0)
                {
                    Log.Error($"RemoveWpmFilters: Failed to remove TAP IPv6 filter. Error: {result}");
                    whetherSuccessful = false;
                }

                TAP_IPv6_Id = 0;
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
            Log.LogDebug("RemoveWpmFilters: Removing sublayer...");
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