using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.NetworkManagement.IpHelper;
using Windows.Win32.NetworkManagement.WindowsFilteringPlatform;
using Windows.Win32.Security;
using GuardianConnect.Shared;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Win32Calls.WFP
{
    public class VpnUtils
    {
        static UInt64 TAP_IPv4_Id = 0;
        static UInt64 TAP_IPv6_Id = 0;
        static UInt64 QBlock_IPv6_Id = 0;
        static UInt64 QBlock_IPv4_Id = 0;

        public static char[] adapterNameToMatch;
        static IP_ADAPTER_INFO adapterInfo = new IP_ADAPTER_INFO();

        // Microsoft-Windows-NetworkProfile
        // fbcfac3f-8459-419f-8e48-1f0b49cdb85e
        internal static readonly Guid kNetworkProfileGUID = new Guid("fbcfac3f-8459-419f-8e48-1f0b49cdb85e");

        // 754b7cbd-cad3-474e-8d2c-054413fd4509
        internal static readonly Guid kVpnDnsSublayerGUID = new Guid("754b7cbd-cad3-474e-8d2c-054413fd4509");
        private const string GRD_NETWORK_PROFILE_GUID = "fbcfac3f-8459-419f-8e48-1f0b49cdb85e";
        private const string GRD_VPN_DNSSUBLAYER_GUID = "754b7cbd-cad3-474e-8d2c-054413fd4509";


        private static char[] GuardianVPNServiceFilterName = "Guardian Firewall VPN Service Filter".ToCharArray();
        private static char[] GuardianVPNServiceFilterDesc = "Session for Guardian Firewall VPN Service".ToCharArray();
        private static char[] GuardianVpnFilterSubLayerName = "Guardian Firewall VPN Service Sublayer".ToCharArray();
        private static char[] GuardianVpnFilterSubLayerDesc = "Sublayer for Guardian Firewall VPN Service".ToCharArray();

        private static PWSTR pSessionName;
        private static PWSTR pSessionDesc;

        private static FWPM_FILTER_CONDITION0[] conditions = new FWPM_FILTER_CONDITION0[2];

        const string kGuardianVpnHelperRegistryStoragePath = "Software\\GuardianSoftware\\Vpn\\HelperService";

        private static Microsoft.Extensions.Logging.ILogger _logger;
        public static Microsoft.Extensions.Logging.ILogger Logger
        {
            get => _logger ?? StaticLoggerFactory.CreateLogger("VpnDnsFilteringHandler");
            set => _logger = value;
        }

        
        public static unsafe HANDLE OpenWpmSession()
        {
            HANDLE engine = HANDLE.Null;
            FWPM_SESSION0 session = new FWPM_SESSION0();
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

            Log.Information("OpenWpmSession: [CONNECT#4.1]");
            uint result = PInvoke.FwpmEngineOpen0(
                serverName: null,
                authnService: PInvoke.RPC_C_AUTHN_WINNT,
                authIdentity: null,
                session: &session,
                engineHandle: &engine);

            if (result != 0)
            {
                Log.Error($"OpenWpmSession: Failed to open filter engine. Error: {result}");
                return HANDLE.Null;
            }

            Log.Information("OpenWpmSession: [CONNECT#4.2]");
            return engine;
        }

        public static bool CloseWpmSession(HANDLE engine)
        {
            if (engine == HANDLE.Null)
            {
                return true;
            }
            uint result = PInvoke.FwpmEngineClose0(engine);
            if (result != 0)
            {
                Log.Error($"CloseWpmSession: Failed to close filter engine. Error: {result}");
                return false;
            }
            return true;
        }

        internal static unsafe uint AddSublayer(HANDLE engineHandle, Guid uuid)
        {
            FWPM_SESSION0 session = new FWPM_SESSION0();
            uint result = 0;

            FWPM_SUBLAYER0 subLayer = new FWPM_SUBLAYER0();
            FWPM_SUBLAYER0* ptr = &subLayer;
            subLayer.subLayerKey = uuid;
            subLayer.displayData = new FWPM_DISPLAY_DATA0();
            fixed (char* p = GuardianVpnFilterSubLayerName)
            {
                PWSTR pSubLayerName = new PWSTR(p);
                subLayer.displayData.name = pSubLayerName;
            }

            fixed (char* p = GuardianVpnFilterSubLayerDesc)
            {
                PWSTR pSublayerDesc = new PWSTR(p);
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

        // CHECK THIS! TODO - WHO WOULD CALL THIS?
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
            Log.Information("RegisterSublayer: [CONNECT#7.1] - checking if sublayer already exists...");
            /* Check sublayer exists and add one if it does not */
            if (PInvoke.FwpmSubLayerGetByKey0(engineHandle, &uuid, &sublayerPtr) != 0)
            {
                Log.Information("RegisterSublayer: [CONNECT#7.2] - sublayer does not exist, adding...");
                uint result = AddSublayer(engineHandle, uuid);
                if (result != 0)
                {
                    Log.Error($"RegisterSublayer: Failed to add sublayer. Error: {result}");
                    return result;
                }
            }
            else
            {
                Log.Information("RegisterSublayer: [CONNECT#7.3] - sublayer already exists.");
                PInvoke.FwpmFreeMemory0((void**)&sublayerPtr);
            }

            return 0;
        }

        public static unsafe int GetAdapterIndexByName()
        {
            int indexOfMatch = -1;
            uint adapterInfoSize = 0;
            uint* pAdapterInfoSize = &adapterInfoSize;
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
                    int ci = 0;
                    foreach (CHAR chr in adapterInfo.Description.AsSpan())
                    {
                        if (ci == adapterNameToMatch.Length)
                        {
                            indexOfMatch = (int)adapterInfo.ComboIndex;
                            return indexOfMatch;
                        }

                        if ((int)adapterNameToMatch[ci++] != (int)chr) break;
                        if ((int)chr == 0) break;
                    }

                    if (adapterInfo.Next == null) break;

                    adapterInfo = *(IP_ADAPTER_INFO*)adapterInfo.Next;
                }
            }

            return -1;
        }

        internal static unsafe uint BlockIPv4Queries(HANDLE engineHandle)
        {
            FWP_CONDITION_VALUE0 cv = new FWP_CONDITION_VALUE0();
            cv.type = FWP_DATA_TYPE.FWP_UINT16;
            cv.Anonymous.uint16 = 53; // DNS port

            FWPM_FILTER_CONDITION0 condition = new FWPM_FILTER_CONDITION0();
            condition.fieldKey = PInvoke.FWPM_CONDITION_IP_REMOTE_PORT;
            condition.matchType = FWP_MATCH_TYPE.FWP_MATCH_EQUAL;
            condition.conditionValue = cv;

            FWPM_FILTER0 filter = new FWPM_FILTER0();
            filter.subLayerKey = kVpnDnsSublayerGUID;
            fixed (char* p = GuardianVPNServiceFilterName)
            {
                PWSTR pSubLayerName = new PWSTR(p);
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
            UInt64 filterId = 0;

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


        // TJE - CHECK THIS!!  - WHY AREN'T WE ADDING CONDITION FOR IPv6 like we do with IPv4??
        internal static unsafe uint BlockIPv6Queries(HANDLE engineHandle)
        {
            FWPM_FILTER0 filter = new FWPM_FILTER0();
            filter.subLayerKey = kVpnDnsSublayerGUID;
            fixed (char* p = GuardianVPNServiceFilterName)
            {
                PWSTR pSubLayerName = new PWSTR(p);
                filter.displayData.name = pSubLayerName;
            }

            filter.weight.type = FWP_DATA_TYPE.FWP_EMPTY;
            //filter.weight.Anonymous.uint8 = 0xF;
            /* Block all IPv6 DNS queries */
            filter.layerKey = PInvoke.FWPM_LAYER_ALE_AUTH_CONNECT_V6;
            filter.action.type = FWP_ACTION_TYPE.FWP_ACTION_BLOCK;
            UInt64 filterId = 0;
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
            FWPM_FILTER0 filter = new FWPM_FILTER0();
            Log.Information($"PermitQueriesFromTAP: Setting filter.subLayerKey to kVpnDnsSublayerGUID {kVpnDnsSublayerGUID}");
            filter.subLayerKey = kVpnDnsSublayerGUID;
            fixed (char* p = GuardianVPNServiceFilterName)
            {
                PWSTR pSubLayerName = new PWSTR(p);
                filter.displayData.name = pSubLayerName;
            }

            filter.weight.type = FWP_DATA_TYPE.FWP_UINT8;
            filter.weight.Anonymous.uint8 = 0xE; // Higher priority than block filter

            /* Permit all IPv4 DNS queries from TAP adapter */
            filter.layerKey = PInvoke.FWPM_LAYER_ALE_AUTH_CONNECT_V4;
            filter.action.type = FWP_ACTION_TYPE.FWP_ACTION_PERMIT;
            // Filter created - continue with conditions...

#if WORKING // Original code from Brian at Brave - seems to have issues finding TAP adapter by name
// TJE TODO - FIX THIS!! - compare with C++ code to see if both return same adapter index and ComboIndex. Also compare LUID values.
            // Get TAP adapter index
            adapterNameToMatch = connectionName.ToCharArray();
            int adapterIndex = GetAdapterIndexByName();
            if (adapterIndex == -1)
            {
                Log.Error("PermitQueriesFromTAP: Failed to find TAP adapter by name.");
                return 1;
            }

            NET_LUID_LH tapluid = new NET_LUID_LH();
            var result = PInvoke.ConvertInterfaceIndexToLuid((uint)adapterIndex, &tapluid);
            if (result != WIN32_ERROR.ERROR_SUCCESS) return 1;

            //FWPM_FILTER_CONDITION0* pCondition = (FWPM_FILTER_CONDITION0*)Unsafe.AsPointer(ref VpnUtils.conditions[0]);
            fixed (FWPM_FILTER_CONDITION0* pCondition = VpnUtils.conditions)
            {
                // Condition 1
                FWP_CONDITION_VALUE0 cv1 = new FWP_CONDITION_VALUE0();
                cv1.type = FWP_DATA_TYPE.FWP_UINT16;
                cv1.Anonymous.uint16 = 53; // DNS port

                FWPM_FILTER_CONDITION0 tCondition = new FWPM_FILTER_CONDITION0();
                pCondition[0].fieldKey = PInvoke.FWPM_CONDITION_IP_REMOTE_PORT;
                pCondition[0].matchType = FWP_MATCH_TYPE.FWP_MATCH_EQUAL;
                pCondition[0].conditionValue = cv1;

                // Condition 2
                FWP_CONDITION_VALUE0 cv2 = new FWP_CONDITION_VALUE0();
                cv2.type = FWP_DATA_TYPE.FWP_UINT64;
                cv2.Anonymous.uint64 = (ulong*)(&tapluid.Value);

                pCondition[1].fieldKey = PInvoke.FWPM_CONDITION_IP_LOCAL_INTERFACE;
                pCondition[1].matchType = FWP_MATCH_TYPE.FWP_MATCH_EQUAL;
                pCondition[1].conditionValue = cv2;

                // Add conditions to filter
                filter.filterCondition = pCondition;
                filter.numFilterConditions = 2;
            }
#else
            filter.numFilterConditions = 0;
#endif

            UInt64 filterId = 0;
            Log.Information("PermitQueriesFromTAP: [CONNECT#9.2] Calling FwpmFilterAdd0() to Permit IPv4 DNS queries from TAP...");
            var retVal = PInvoke.FwpmFilterAdd0(engineHandle, &filter, PSECURITY_DESCRIPTOR.Null, &filterId);
            if (retVal != 0)
            {
                if (retVal == 0x80320007)
                {
                    Log.Error($"PermitQueriesFromTAP: Failed to add IPv4 DNS permit filter. Error: {retVal:X8} [FWP_E_SUBLAYER_NOT_FOUND]");
                }
                else
                {
                    Log.Error($"PermitQueriesFromTAP: Failed to add IPv4 DNS permit filter. Error: {retVal:X8}");
                    
                }
                return retVal;
            }

		    TAP_IPv4_Id = filterId;
            Log.Information("PermitQueriesFromTAP: FwpmFilterAdd0 successfully added PermitIPv4 filter.");

		    // Permit IPv6 DNS queries from TAP. Use same weight as IPv4 filter.
		    filter.layerKey = PInvoke.FWPM_LAYER_ALE_AUTH_CONNECT_V6;
            Log.Information("PermitQueriesFromTAP: [CONNECT#9.3] Calling FwpmFilterAdd0() to Permit IPv6 DNS queries from TAP...");
            retVal = PInvoke.FwpmFilterAdd0(engineHandle, &filter, PSECURITY_DESCRIPTOR.Null, &filterId);
            if (retVal != 0)
            {
                Log.Error($"PermitQueriesFromTAP: Failed to add IPv6 DNS permit filter. Error: {retVal}");
                return retVal;
            }

		    TAP_IPv6_Id = filterId;

            return retVal;
        }

        public static unsafe bool AddWpmFilters(HANDLE engine_handle, string name)
        {
            if (engine_handle == HANDLE.Null)
            {
                Log.Error("AddWpmFilters: Invalid engine handle.");
                return false;
            }

            Log.Information("AddWpmFilters: [CONNECT#6.1] - Calling RegisterSubLayer()...");
            uint result = RegisterSublayer(engine_handle, kVpnDnsSublayerGUID);
            if (result != 0)
            {
                Log.Error($"AddWpmFilters: Failed to register sublayer. Error: {result}");
                return false;
            }

            // Block all IPv4 DNS queries
            Log.Information("AddWpmFilters: [CONNECT#6.2] - Calling BlockIPv4Queries()...");
            result = BlockIPv4Queries(engine_handle);
            if (result != 0)
            {
                Log.Error($"AddWpmFilters: Failed to block IPv4 DNS queries. Error: {result}");
                return false;
            }

            // Block all IPv6 DNS Queries
            Log.Information("AddWpmFilters: [CONNECT#6.3] - Calling BlockIPv6Queries()...");
            result = BlockIPv6Queries(engine_handle);
            if (result != 0)
            {
                Log.Error($"AddWpmFilters: Failed to block IPv6 DNS queries. Error: {result}");
                return false;
            }

            // Permit IPv4 DNS queries from TAP adapter
            Log.Information("AddWpmFilters: [CONNECT#6.4] - Calling PermitIPv4QueriesFromTAP()...");
            result = PermitQueriesFromTAP(engine_handle, name);
            if (result != 0)
            {
                Log.Error($"AddWpmFilters: Failed to permit IPv4 DNS queries from TAP adapter. Error: {result}");
                return false;
            }

            Log.Information("AddWpmFilters: [CONNECT#6.5] Added block filters for all interfaces");

            return true;
        }

        public static unsafe bool RemoveWpmFilters(HANDLE engine_handle, string name)
        {
            // We need to fall through and try to remove all filters even if one fails.
            bool whetherSuccessful = true;
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
                    Log.Information("RemoveWpmFilters: [DISCONNECT#5.1] - Removing TAP IPv4 filter...");
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
                    Log.Information("RemoveWpmFilters: [DISCONNECT#5.2] - Removing TAP IPv6 filter...");
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
                Log.Information("RemoveWpmFilters: [DISCONNECT#5.3] - Removing sublayer...");
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
                using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(kGuardianVpnHelperRegistryStoragePath))
                {
                    if (key != null)
                    {
                        key.SetValue("FiltersInstalled", 1, Microsoft.Win32.RegistryValueKind.DWord);
                    }
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
                using (var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(kGuardianVpnHelperRegistryStoragePath))
                {
                    if (key != null)
                    {
                        key.SetValue("FiltersInstalled", 0, Microsoft.Win32.RegistryValueKind.DWord);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error($"ResetFiltersInstalledFlag: Failed to reset registry key. Exception: {ex.Message}");
            }
        }
    }
}
