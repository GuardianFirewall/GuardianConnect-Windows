//#include "pch.h"
#include "VpnUtils.h"

#include <ios>
#include <rpcdce.h>
#include <winerror.h>
#include <fwpmu.h>
#include <iostream>
#include <iphlpapi.h>
#include <ras.h>
#include <vector>
#include <msclr/marshal_cppstd.h>
#include <string>

#include "../NativeRoutines.h"
#include "../PrintRoutines.h"

namespace NativeRoutines
{
	ref class PrintRoutines;
	using NativeRoutines::PrintRoutines;

	// Microsoft-Windows-NetworkProfile
	// fbcfac3f-8459-419f-8e48-1f0b49cdb85e
	constexpr GUID kNetworkProfileGUID = {
	  0xfbcfac3f,
	  0x8459,
	  0x419f,
	  {0x8e, 0x48, 0x1f, 0x0b, 0x49, 0xcd, 0xb8, 0x5e} };

	constexpr wchar_t kGuardianVPNServiceFilter[] = L"Guardian VPN Service DNS Filter";
	constexpr wchar_t kGuardianVpnHelperRegistryStoragePath[] =
		L"Software\\GuardianSoftware\\Vpn\\HelperService";
	// 754b7cbd-cad3-474e-8d2c-054413fd4509
	constexpr GUID kVpnDnsSublayerGUID = {
	  0x754b7cbd,
	  0xcad3,
	  0x474e,
	  {0x8d, 0x2c, 0x05, 0x44, 0x13, 0xfd, 0x45, 0x09} };

	
	    UINT64 VpnUtils::TAP_IPv4_Id;
	    UINT64 VpnUtils::TAP_IPv6_Id;
	    UINT64 VpnUtils::QBlock_IPv6_Id;
	    UINT64 VpnUtils::QBlock_IPv4_Id;

	HANDLE VpnUtils::OpenWpmSession() {
		HANDLE engine = nullptr;
		FWPM_SESSION0 session;
		memset(&session, 0, sizeof(session)); // Initialize the structure to zero
		session.flags = FWPM_SESSION_FLAG_DYNAMIC;
		session.displayData.name = L"Guardian VPN Service";
		session.displayData.description = L"Session for Guardian VPN Service";
		DWORD result = 0;

		try
		{
			PrintRoutines::Output("OpenWpmSession: [CONNECT#4.1]");
			result = FwpmEngineOpen0(nullptr, RPC_C_AUTHN_WINNT, nullptr, &session, &engine);
			if (result != ERROR_SUCCESS) {
				PrintRoutines::Output("OpenWpmSession: Failure on FwpmEngineOpen0! Error code to follow...");
				String^ errorRetCode = gcnew String(std::to_string(result).c_str());
				PrintRoutines::Output(Grd::FormatAString("OpenWpmSession: Failure on FwpmEngineOpen0! result error code is {0:x}",
					gcnew array<String^> { errorRetCode }));
			}
		}
		catch (const std::exception& e)
		{
			PrintRoutines::Output(Grd::FormatAString("OpenWpmSession: Exception caught: {0}", gcnew array<String^> { gcnew String(e.what()) }));
		}
		PrintRoutines::Output("OpenWpmSession: [CONNECT#4.2]");
		return engine;
	}

	bool VpnUtils::CloseWpmSession(HANDLE engine) {
		auto result = FwpmEngineClose0(engine);
		bool success = result == ERROR_SUCCESS;
		if (!success) {
			PrintRoutines::Output("CloseWpmSession: Failure on FwpmEngineClose0! Error code to follow...");
			String^ errorRetCode = gcnew String(std::to_string(result).c_str());
			PrintRoutines::Output(Grd::FormatAString("CloseWpmSession: Failure on FwpmEngineOpen0! result error code is {0:x}",
                gcnew array<String^> { errorRetCode}));
			std::cout << "Failed to close WPM engine, error code:" << std::hex << result;
		}
		return success;
	}

	DWORD AddSublayer(HANDLE engine_handle, GUID uuid)
	{
		FWPM_SESSION0 session = {};
		DWORD result = 0;

		std::wstring name(kGuardianVPNServiceFilter);
		FWPM_SUBLAYER0 sublayer = {};
		sublayer.subLayerKey = uuid;
		sublayer.displayData.name = const_cast<wchar_t*>(name.data());
		sublayer.displayData.description = const_cast<wchar_t*>(name.data());
		sublayer.flags = 0;
		sublayer.weight = 0x100;

		/* Add sublayer to the session */
		PrintRoutines::Output(Grd::FormatAString("AddSublayer: calling FwpmSubLayerAdd0 with sublayer name: {0}",
			gcnew array<Object^> { gcnew String(name.data()) }));
		result = FwpmSubLayerAdd0(engine_handle, &sublayer, nullptr);
		PrintRoutines::Output(Grd::FormatAString("AddSublayer: Error from call to FwpmSubLayerAdd0(): {0:X}", result));
		return result;
	}

	DWORD RegisterSublayer(HANDLE engine_handle, GUID uuid) {
		FWPM_SUBLAYER0* sublayer_ptr = nullptr;
		PrintRoutines::Output("RegisterSublayer: [CONNECT#7.1] - checking if sublayer already exists...");
		/* Check sublayer exists and add one if it does not. */
		if (FwpmSubLayerGetByKey0(engine_handle, &uuid, &sublayer_ptr) == ERROR_SUCCESS) {
			PrintRoutines::Output(
				Grd::FormatAString("RegisterSublayer: Using existing sublayer: name='{0}', description='{1}'",
					gcnew array<Object^> {
						gcnew String(sublayer_ptr->displayData.name),
						gcnew String(sublayer_ptr->displayData.description)
					}));
			if (sublayer_ptr) {
				FwpmFreeMemory0(reinterpret_cast<void**>(&sublayer_ptr));
			}
			return ERROR_SUCCESS;
		}
		// Add a new sublayer and do not treat "already exists" as an error
		PrintRoutines::Output("RegisterSublayer: [CONNECT#7.2] - calling AddSublayer()...");
		auto result = AddSublayer(engine_handle, uuid);
		PrintRoutines::Output(Grd::FormatAString("RegisterSublayer: Error from call to AddSublayer() :{0:X}", result));
		if (result != (DWORD)FWP_E_ALREADY_EXISTS && result != ERROR_SUCCESS) {
			String^ errorRetCode = gcnew String(std::to_string(result).c_str());
			PrintRoutines::Output(Grd::FormatAString("RegisterSublayer: Failed to add sublayer. result error code is {0:x}",
                gcnew array<String^> { errorRetCode}));
			return result;
		}
		PrintRoutines::Output("RegisterSublayer: [CONNECT#7.3] Added persistent sublayer");
		return ERROR_SUCCESS;
	}

	int GetAdapterIndexByName(const std::string& name) {
		ULONG adapter_info_size = 0;
		// Get the right buffer size in case of overflow
		if (GetAdaptersInfo(nullptr, &adapter_info_size) != ERROR_BUFFER_OVERFLOW ||
			adapter_info_size == 0) {
			return 0;
		}

		std::vector<byte> adapters(adapter_info_size);
		if (GetAdaptersInfo(reinterpret_cast<PIP_ADAPTER_INFO>(adapters.data()),
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
		filter.numFilterConditions = static_cast<UINT32>(conditions.size());

		/* Block all IPv4 DNS queries. */
		filter.layerKey = FWPM_LAYER_ALE_AUTH_CONNECT_V4;
		filter.action.type = FWP_ACTION_BLOCK;
		filter.weight.type = FWP_EMPTY;
		filter.numFilterConditions = 1;
		UINT64 filterid = 0;
        PrintRoutines::Output(Grd::FormatAString("BlockIPv4Queries: [CONNECT#8.1] Calling FwpmFilterAdd0 with filter name: {0}",
			gcnew array<Object^> { gcnew String(name.data()) }));

		DWORD retValue = FwpmFilterAdd0(engine_handle, &filter, nullptr, &filterid);
		String^ errorRetCode = gcnew String(std::to_string(retValue).c_str());
		if (retValue == ERROR_SUCCESS)
		{
			PrintRoutines::Output("BlockIPv4Queries: [CONNECT#8.2] FwpmFilterAdd0 returned SUCCESS!");
			VpnUtils::QBlock_IPv4_Id = filterid;
		}
		else
		{
			PrintRoutines::Output( Grd::FormatAString(
				"BlockIPv4Queries: Failure on FwpmFilterAdd0! result error code is {0:x}",
				gcnew array<String^> { errorRetCode }));
		}
		return retValue;
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
		
		PrintRoutines::Output("BlockIPv6Queries: [CONNECT#9.2] Calling FwpmFilterAdd0 ...");
		DWORD retVal = FwpmFilterAdd0(engine_handle, &Filter, nullptr, &filterid);
		VpnUtils::QBlock_IPv4_Id = filterid;

		return retVal;
	}

	// Permit IPv4 DNS queries from TAP.
	// Use a non-zero weight so that the permit filters get higher priority
	// over the block filter added with automatic weighting */
	DWORD PermitQueriesFromTAP(HANDLE engine_handle,
		String^ connection_name_string) {
		std::string& connection_name = msclr::interop::marshal_as<std::string>(connection_name_string);
			
		PrintRoutines::Output(Grd::FormatAString("PermitQueriesFromTAP: [CONNECT#9.1] Calling GetAdapterIndexByName()"));
		auto index = GetAdapterIndexByName(connection_name);
		if (!index) {
			PrintRoutines::Output(Grd::FormatAString("PermitQueriesFromTAP: Failure to GetAdapterIndexByName('{0}') - ERROR_INVALID_PARAMETER",
				gcnew array<String^> { connection_name_string }));
			std::cout << "Failed to get index for adapter:" << connection_name;
			return ERROR_INVALID_PARAMETER;
		}

		NET_LUID tapluid = {};
		auto result = ConvertInterfaceIndexToLuid(index, &tapluid);
		if (result) {
			PrintRoutines::Output("PermitQueriesFromTAP: Failure in call to ConvertInterfaceIndexToLuid()");
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
		Filter.numFilterConditions = static_cast<UINT32>(conditions.size());

		UINT64 filterid = 0;
		PrintRoutines::Output("PermitQueriesFromTAP: [CONNECT#9.2] Calling FwpmFilterAdd0() to Permit IPv4 DNS queries from TAP...");
		result = FwpmFilterAdd0(engine_handle, &Filter, nullptr, &filterid);
		if (result) {
			PrintRoutines::Output(Grd::FormatAString("PermitQueriesFromTap: Error from call to FwpmFilterAdd0() for IP4: {0:X}", result));
			std::cout << "Add filter to permit IPv4 DNS traffic through TAP failed:"
				<< std::hex << result;
			return result;
		}
		VpnUtils::TAP_IPv4_Id = filterid;

		// Permit IPv6 DNS queries from TAP. Use same weight as IPv4 filter.
		Filter.layerKey = FWPM_LAYER_ALE_AUTH_CONNECT_V6;

		PrintRoutines::Output("PermitQueriesFromTAP: [CONNECT#9.3] Calling FwpmFilterAdd0() to permit IPv6 DNS queries from TAP...");
		result = FwpmFilterAdd0(engine_handle, &Filter, nullptr, &filterid);
		if (result) {
			PrintRoutines::Output(Grd::FormatAString("PermitQueriesFromTap: Error from call to FwpmFilterAdd0() for IP6: {0:X}", result));
			std::cout << "Add filter to permit IPv6 DNS traffic through TAP failed:"
				<< std::hex << result;
		}
		VpnUtils::TAP_IPv6_Id = filterid;
		return result;
	}

	bool VpnUtils::AddWpmFilters(HANDLE engine_handle, String^ connection_name) {
		if (!engine_handle) {
			PrintRoutines::Output("AddWpmFilters: Error! engine_handle should NOT be null at this stage!!");
			std::cout << "Engine handle cannot be null";
			return false;
		}
		
		PrintRoutines::Output("AddWpmFilters: [CONNECT#6.1] - Calling RegisterSublayer()...");
		auto result = RegisterSublayer(engine_handle, kVpnDnsSublayerGUID);
		if (result != ERROR_SUCCESS) {
			PrintRoutines::Output(Grd::FormatAString("AddWpmFilters: Error from call to RegisterSublayer(): {0:X}", result));
			std::cout << "Open FWP session failed, error code:" << std::hex << result;
			return false;
		}

		// Block all IPv4 DNS queries.
		PrintRoutines::Output("AddWpmFilters: [CONNECT#6.2] - Calling BlockIPv4Queries()...");
		result = BlockIPv4Queries(engine_handle);
		if (result != ERROR_SUCCESS) {
			PrintRoutines::Output(Grd::FormatAString("AddWpmFilters: Error from call to BlockIPv4Queries(): {0:X}", result));
			return false;
		}

		// Block all IPv6 DNS queries.
		PrintRoutines::Output("AddWpmFilters: [CONNECT#6.3] - Calling BlockIPv6Queries()...");
		result = BlockIPv6Queries(engine_handle);
		if (result != ERROR_SUCCESS) {
			PrintRoutines::Output(Grd::FormatAString("AddWpmFilters: Error from call to BlockIPv6Queries(): {0:X}", result));
			return false;
		}

		// Permit IPv4 DNS queries from TAP.
		PrintRoutines::Output("AddWpmFilters: [CONNECT#6.4] - Calling PermitQueriesFromTAP()...");
		result = PermitQueriesFromTAP(engine_handle, connection_name);
		if (result != ERROR_SUCCESS) {
			PrintRoutines::Output(Grd::FormatAString("AddWpmFilters: Error from call to PermitQueriesFromTAP(): {0:X}", result));
			return false;
		}

		PrintRoutines::Output("AddWpmFilters: [CONNECT#6.5] Added block filters for all interfaces");
		std::cout << "Added block filters for all interfaces";

		return true;
	}

	bool VpnUtils::RemoveWpmFilters(HANDLE engine_handle, String^ connection_name) {
		bool success = false;
		DWORD retVal;

		// #1 - remove TAP Filters permitting queries on IPv4 and IPv6
		PrintRoutines::Output("RemoveWpmFilters: Removing TAP_IPv6...");
		retVal = FwpmFilterDeleteById(engine_handle, TAP_IPv6_Id);
		if (retVal != ERROR_SUCCESS)
		{
			PrintRoutines::Output(Grd::FormatAString("RemoveWpmFilters: [DISCONNECT#?.?] FwpmFilterDeleteById0 of TAPIPv6 FAIL! return value = {0:x}", retVal));
		}
		PrintRoutines::Output("RemoveWpmFilters: Removing TAP_IPv4...");
		retVal = FwpmFilterDeleteById(engine_handle, TAP_IPv4_Id);
		if (retVal != ERROR_SUCCESS)
		{
			PrintRoutines::Output(Grd::FormatAString("RemoveWpmFilters: [DISCONNECT#?.?] FwpmFilterDeleteById0 of TAPIPv4 FAIL! return value = {0:x}", retVal));
		}

		// #2 - remove IPv6 Queries Block Filter
		PrintRoutines::Output("RemoveWpmFilters: Removing QBlock_IPv6...");
		retVal = FwpmFilterDeleteById(engine_handle, QBlock_IPv6_Id);
		if (retVal != ERROR_SUCCESS)
		{
			PrintRoutines::Output(Grd::FormatAString("RemoveWpmFilters: [DISCONNECT#?.?] FwpmFilterDeleteById0 QBLOCKv6 FAIL! return value = {0:x}", retVal));
		}

		// #3 - remove IPv4 Queries Block Filter
		PrintRoutines::Output("RemoveWpmFilters: Removing QBlock_IPv4...");
		retVal = FwpmFilterDeleteById(engine_handle, QBlock_IPv4_Id);
		if (retVal != ERROR_SUCCESS)
		{
			PrintRoutines::Output(Grd::FormatAString("RemoveWpmFilters: [DISCONNECT#?.?] FwpmFilterDeleteById0 QBLOCKv4 FAIL! return value = {0:x}", retVal));
		}

		// #4 Remove Sublayer
		PrintRoutines::Output("RemoveWpmFilters: Removing SubLayer ...");
		retVal = FwpmSubLayerDeleteByKey0(engine_handle, &kVpnDnsSublayerGUID);
		if (retVal != ERROR_SUCCESS)
		{
			PrintRoutines::Output(Grd::FormatAString("RemoveWpmFilters: [DISCONNECT#?.?] FwpmSubLayerDeleteByKey0 FAIL! return value = {0:x}", retVal));
		}

		return success;
	}

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
