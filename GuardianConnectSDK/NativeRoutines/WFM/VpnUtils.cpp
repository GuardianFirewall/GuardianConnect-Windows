#include "pch.h"
#include "VpnUtils.h"

#include <ios>
#include <rpcdce.h>
#include <winerror.h>
#include <fwpmu.h>
#include <iostream>
#include <iphlpapi.h>
#include <ras.h>
#include <vector>

#include "../NativeRoutines.h"

namespace NativeRoutines
{
  // Microsoft-Windows-NetworkProfile
  // fbcfac3f-8459-419f-8e48-1f0b49cdb85e
  constexpr GUID kNetworkProfileGUID = {
    0xfbcfac3f,
    0x8459,
    0x419f,
    {0x8e, 0x48, 0x1f, 0x0b, 0x49, 0xcd, 0xb8, 0x5e}};

  constexpr wchar_t kGuardianVPNServiceFilter[] = L"Guardian VPN Service DNS Filter";
  constexpr wchar_t kGuardianVpnHelperRegistryStoragePath[] =
      L"Software\\GuardianSoftware\\Vpn\\HelperService";
  // 754b7cbd-cad3-474e-8d2c-054413fd4509
  constexpr GUID kVpnDnsSublayerGUID = {
    0x754b7cbd,
    0xcad3,
    0x474e,
    {0x8d, 0x2c, 0x05, 0x44, 0x13, 0xfd, 0x45, 0x09}};
    
  HANDLE VpnUtils::OpenWpmSession() {
    HANDLE engine = nullptr;
    FWPM_SESSION0 session;
    session.flags = FWPM_SESSION_FLAG_DYNAMIC;
    
    auto result =
        FwpmEngineOpen0(nullptr, RPC_C_AUTHN_WINNT, nullptr, &session, &engine);
    if (result != ERROR_SUCCESS) {
      std::cout << "Open FWP session failed, error code:" << std::hex << result;
    }
    return engine;
  }

  bool VpnUtils::CloseWpmSession(HANDLE engine) {
    auto result = FwpmEngineClose0(engine);
    bool success = result == ERROR_SUCCESS;
    if (!success) {
      std::cout << "Failed to close WPM engine, error code:" << std::hex << result;
    }
    return success;
  }

  DWORD AddSublayer(GUID uuid) {
    FWPM_SESSION0 session = {};
    HANDLE engine = nullptr;
    auto result =
        FwpmEngineOpen0(nullptr, RPC_C_AUTHN_WINNT, nullptr, &session, &engine);
    if (result == ERROR_SUCCESS) {
      std::wstring name(kGuardianVPNServiceFilter);
      FWPM_SUBLAYER0 sublayer = {};
      sublayer.subLayerKey = uuid;
      sublayer.displayData.name = const_cast<wchar_t*>(name.data());
      sublayer.displayData.description = const_cast<wchar_t*>(name.data());
      sublayer.flags = 0;
      sublayer.weight = 0x100;

      /* Add sublayer to the session */
      result = FwpmSubLayerAdd0(engine, &sublayer, nullptr);
    }
    if (engine) {
      FwpmEngineClose0(engine);
    }
    return result;
  }

  DWORD RegisterSublayer(HANDLE engine_handle, GUID uuid) {
    FWPM_SUBLAYER0* sublayer_ptr = nullptr;
    /* Check sublayer exists and add one if it does not. */
    if (FwpmSubLayerGetByKey0(engine_handle, &uuid, &sublayer_ptr) ==
        ERROR_SUCCESS) {
      std::cout << "Using existing sublayer";
      if (sublayer_ptr) {
        FwpmFreeMemory0(reinterpret_cast<void**>(&sublayer_ptr));
      }
      return ERROR_SUCCESS;
        }
    // Add a new sublayer and do not treat "already exists" as an error
    auto result = AddSublayer(uuid);
    if (result != (DWORD)FWP_E_ALREADY_EXISTS && result != ERROR_SUCCESS) {
      std::cout << "Failed to add a persistent sublayer with "
                 "BRAVEVPN_DNS_SUBLAYER UUID";
      return result;
    }
    std::cout << "Added a persistent sublayer with BRAVEVPN_DNS_SUBLAYER UUID";
    return ERROR_SUCCESS;
  }

  int GetAdapterIndexByName(const std::string& name) {
    ULONG adapter_info_size = 0;
    // Get the right buffer size in case of overflow
    if (::GetAdaptersInfo(nullptr, &adapter_info_size) != ERROR_BUFFER_OVERFLOW ||
        adapter_info_size == 0) {
      return 0;
        }

    std::vector<byte> adapters(adapter_info_size);
    if (::GetAdaptersInfo(reinterpret_cast<PIP_ADAPTER_INFO>(adapters.data()),
                          &adapter_info_size) != ERROR_SUCCESS) {
      return 0;
    }

    // The returned value is not an array of IP_ADAPTER_INFO elements but a linked
    // list of such
    PIP_ADAPTER_INFO adapter =
        reinterpret_cast<PIP_ADAPTER_INFO>(adapters.data());
    while (adapter) {
      if (std::string(adapter->Description) == name) {
        return adapter->ComboIndex;
      }
      adapter = adapter->Next;
    }

    return 0;
  }

  DWORD BlockIPv4Queries(HANDLE engine_handle) {
    std::vector<FWPM_FILTER_CONDITION0> conditions;
    FWP_CONDITION_VALUE cv;
    cv.type = FWP_UINT16;
    cv.uint16 = 53;
    FWPM_FILTER_CONDITION0_ condition;
    condition.fieldKey = FWPM_CONDITION_IP_REMOTE_PORT;
    condition.matchType = FWP_MATCH_EQUAL;
    condition.conditionValue = cv;
    conditions.push_back(condition);

    FWPM_FILTER0 filter = {};
    filter.subLayerKey = kVpnDnsSublayerGUID;
    std::wstring name(kGuardianVPNServiceFilter);
    filter.displayData.name = const_cast<wchar_t*>(name.data());
    filter.weight.type = FWP_UINT8;
    filter.weight.uint8 = 0xF;
    filter.filterCondition = conditions.data();
    filter.numFilterConditions = conditions.size();

    /* Block all IPv4 DNS queries. */
    filter.layerKey = FWPM_LAYER_ALE_AUTH_CONNECT_V4;
    filter.action.type = FWP_ACTION_BLOCK;
    filter.weight.type = FWP_EMPTY;
    filter.numFilterConditions = 1;
    UINT64 filterid = 0;
    return FwpmFilterAdd0(engine_handle, &filter, nullptr, &filterid);
  }

  // Block all IPv6 DNS queries
  DWORD BlockIPv6Queries(HANDLE engine_handle) {
    FWPM_FILTER0 Filter = {};
    Filter.subLayerKey = kVpnDnsSublayerGUID;
    std::wstring name(kGuardianVPNServiceFilter);
    Filter.displayData.name = const_cast<wchar_t*>(name.data());
    Filter.weight.type = FWP_EMPTY;
    Filter.layerKey = FWPM_LAYER_ALE_AUTH_CONNECT_V6;
    Filter.action.type = FWP_ACTION_BLOCK;
    UINT64 filterid = 0;
    return FwpmFilterAdd0(engine_handle, &Filter, nullptr, &filterid);
  }

  // Permit IPv4 DNS queries from TAP.
  // Use a non-zero weight so that the permit filters get higher priority
  // over the block filter added with automatic weighting */
  DWORD PermitQueriesFromTAP(HANDLE engine_handle,
                             const std::string& connection_name) {
    auto index = GetAdapterIndexByName(connection_name);
    if (!index) {
      std::cout << "Failed to get index for adapter:" << connection_name;
      return ERROR_INVALID_PARAMETER;
    }

    NET_LUID tapluid = {};
    auto result = ConvertInterfaceIndexToLuid(index, &tapluid);
    if (result) {
      std::cout << "Convert interface index to luid failed:" << std::hex << result;
      return result;
    }

    std::vector<FWPM_FILTER_CONDITION0> conditions;
    FWP_CONDITION_VALUE cv;
    // Condition 1
    cv.type = FWP_UINT16;
    cv.uint16 = 53;
    FWPM_FILTER_CONDITION0_ condition;
    condition.fieldKey = FWPM_CONDITION_IP_REMOTE_PORT;
    condition.matchType = FWP_MATCH_EQUAL;
    condition.conditionValue = cv;
    conditions.push_back(condition);
    
    // Condition 2
    cv.type = FWP_UINT64;
    cv.uint64 = &tapluid.Value;
    condition.fieldKey = FWPM_CONDITION_IP_LOCAL_INTERFACE;
    condition.matchType = FWP_MATCH_EQUAL;
    condition.conditionValue = cv;
    conditions.push_back(condition);
    //
    
    FWPM_FILTER0 Filter = {};
    Filter.subLayerKey = kVpnDnsSublayerGUID;
    std::wstring name(kGuardianVPNServiceFilter);
    Filter.displayData.name = const_cast<wchar_t*>(name.data());
    Filter.weight.type = FWP_UINT8;
    Filter.weight.uint8 = 0xE;
    Filter.layerKey = FWPM_LAYER_ALE_AUTH_CONNECT_V4;
    Filter.action.type = FWP_ACTION_PERMIT;
    Filter.filterCondition = conditions.data();
    Filter.numFilterConditions = conditions.size();

    UINT64 filterid = 0;
    result = FwpmFilterAdd0(engine_handle, &Filter, nullptr, &filterid);
    if (result) {
      std::cout << "Add filter to permit IPv4 DNS traffic through TAP failed:"
              << std::hex << result;
      return result;
    }

    // Permit IPv6 DNS queries from TAP. Use same weight as IPv4 filter.
    Filter.layerKey = FWPM_LAYER_ALE_AUTH_CONNECT_V6;

    result = FwpmFilterAdd0(engine_handle, &Filter, nullptr, &filterid);
    if (result) {
      std::cout << "Add filter to permit IPv6 DNS traffic through TAP failed:"
              << std::hex << result;
    }
    return result;
  }

  bool VpnUtils::AddWpmFilters(HANDLE engine_handle, String^ connection_name) {
    if (!engine_handle) {
      std::cout << "Engine handle cannot be null";
      return false;
    }
    auto result = RegisterSublayer(engine_handle, kVpnDnsSublayerGUID);
    if (result != ERROR_SUCCESS) {
      std::cout << "Open FWP session failed, error code:" << std::hex << result;
      return false;
    }

    // Block all IPv4 DNS queries.
    result = BlockIPv4Queries(engine_handle);
    if (result != ERROR_SUCCESS) {
      std::cout << "Add filter to block IPv4 DNS traffic failed:" << std::hex
              << result;
      return false;
    }

    // Block all IPv6 DNS queries.
    result = BlockIPv6Queries(engine_handle);
    if (result != ERROR_SUCCESS) {
      std::cout << "Add filter to block IPv6 DNS traffic failed:" << std::hex
              << result;
      return false;
    }

    // Permit IPv4 DNS queries from TAP.
    std::string *standardString;
    Grd::MarshalString(connection_name, *standardString);
    result = PermitQueriesFromTAP(engine_handle, *standardString);
    if (result != ERROR_SUCCESS) {
      std::cout << "Add filter to permit IPv4 and IPv6 DNS queries from TAP failed:"
              << std::hex << result;
      return false;
    }

    std::cout << "Added block filters for all interfaces";

    return true;
  }

  HANDLE OpenWpmSession() {
    FWPM_SESSION0 session;
    session.flags = FWPM_SESSION_FLAG_DYNAMIC;
    HANDLE engine = nullptr;
    auto result =
        FwpmEngineOpen0(nullptr, RPC_C_AUTHN_WINNT, nullptr, &session, &engine);
    if (result != ERROR_SUCCESS) {
      std::cout << "Open FWP session failed, error code:" << std::hex << result;
    }
    return engine;
  }

  bool CloseWpmSession(HANDLE engine) {
    auto result = FwpmEngineClose0(engine);
    bool success = result == ERROR_SUCCESS;
    if (!success) {
      std::cout << "Failed to close WPM engine, error code:" << std::hex << result;
    }
    return success;
  }

  bool VpnUtils::SubscribeRasConnectionNotification(HANDLE event_handle) {
    // As we pass INVALID_HANDLE_VALUE, we can get connected or disconnected
    // event from any os vpn entry. It's filtered by
    // VpnDnsHandler::OnObjectSignaled().
    auto result = RasConnectionNotificationW(
        static_cast<HRASCONN>(INVALID_HANDLE_VALUE), event_handle,
        RASCN_Connection | RASCN_Disconnection);
    bool success = result == ERROR_SUCCESS;
    if (!success) {
      std::cout
          << "Failed to subscribe for RAS connection notifications, error code:"
          << std::hex << result;
    }
    return success;
  }

#if CFGSVCINCODE
  bool VpnUtils::ConfigureServiceAutoRestart(const std::wstring& service_name,
                                   const std::wstring& brave_vpn_entry) {
    //ScopedScHandle scm(::OpenSCManager(nullptr, nullptr, SC_MANAGER_CONNECT));
    SCM(::OpenSCManager(nullptr, nullptr, SC_MANAGER_CONNECT));
    if (SCM == nullptr) {
      std::cerr << "::OpenSCManager failed. service_name: " << service_name.data()
                 << ", error: " << std::hex << GetLastError();
      return false;
    }
    HANDLE service(
        ::OpenService(SCM, service_name.c_str(), SERVICE_ALL_ACCESS));
    if (SCM == nullptr) {
      std::cerr << "::OpenService failed. service_name: " << service_name.data()
                 << ", error: " << std::hex << GetLastError();
      return false;
    }

#if false
    if (!brave_vpn::SetServiceFailureActions(service.Get())) {
      std::cerr << "SetServiceFailureActions failed:" << std::hex
                 << HRESULTFromLastError();
      return false;
    }
    if (!SetServiceTriggerForVPNConnection(service.Get(), brave_vpn_entry)) {
      std::cerr << "SetServiceTriggerForVPNConnection failed:" << std::hex
                 << HRESULTFromLastError();
      return false;
    }
#endif
    return true;
  }
#endif

  void VpnUtils::SetFiltersInstalledFlag() {
    String^ regValuePath = L"\\Software\\GuardianVPN";
    String^ regFiltersPath = L"filters";
    Microsoft::Win32::RegistryKey^ key = (Microsoft::Win32::Registry::LocalMachine)->OpenSubKey(regValuePath, true);
                                                    
    if (key == nullptr) {
      std::cout << "Failed to open vpn service storage";
      return;
    }
    DWORD launch = 1;
    key->SetValue(regFiltersPath, launch);
  }

  void VpnUtils::ResetFiltersInstalledFlag() {
    String^ regValuePath = L"\\Software\\GuardianVPN";
    String^ regFiltersPath = L"filters";
    Microsoft::Win32::RegistryKey^ key = (Microsoft::Win32::Registry::LocalMachine)->OpenSubKey(regValuePath, true);
                                                    
    if (key == nullptr) {
      std::cout << "Failed to open vpn service storage";
      return;
    }
    key->DeleteValue(regFiltersPath);
  }
}
