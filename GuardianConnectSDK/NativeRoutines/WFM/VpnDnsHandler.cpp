//#include "pch.h"
#include <msclr/marshal.h>
#include "VpnDnsHandler.h"
#include "VpnUtils.h"
#include "../PrintRoutines.h"

namespace  NativeRoutines
{
	ref class PrintRoutines;
	using NativeRoutines::PrintRoutines;
	using namespace System;
	using namespace System::Runtime::InteropServices;

	HANDLE VpnDnsHandler::engine_ = nullptr;

	//class VpnDnsHandler
	//{
	constexpr int kCheckConnectionIntervalInSeconds = 3;
	bool VpnDnsHandler::SetupPlatformFilters(String^ name) {
		PrintRoutines::Output("SetupPlatformFilters: [CONNECT#5.1] Attempting to add filters...");
		return VpnUtils::AddWpmFilters(engine_, name);
	}

	bool VpnDnsHandler::RemovePlatformFilters(String^ name)
	{
		PrintRoutines::Output("RemovePlatformFilters: [DISCONNECT#?.?] Attempting to remove filters...");
		return VpnUtils::RemoveWpmFilters(engine_, name);
	}
	bool VpnDnsHandler::CloseEngineSession() {
		PrintRoutines::Output("CloseEngineSession: Attempting to close engine session...");
		return VpnUtils::CloseWpmSession(engine_);
	}


	bool VpnDnsHandler::SetFilters(String^ connection_name) {
		PrintRoutines::Output(
			Grd::FormatAString("SetFilters:[CONNECT#3.1] Connection is '{0}'", gcnew array<Object^> { connection_name }));
		if (IsActive()) {
			PrintRoutines::Output(
				Grd::FormatAString(
					"SetFilters: Filters ARE active already for: {0}", gcnew array<Object^> {connection_name }));
			return true;
		}
		PrintRoutines::Output(
			Grd::FormatAString(
				"SetFilters:[CONNECT#3.2] Filters are NOT currently active for: {0}. Proceeding to activate them.",
				gcnew array<Object^> {connection_name }));

		engine_ = VpnUtils::OpenWpmSession();
		if (!engine_) {
			PrintRoutines::Output("Failed to open engine session");
			return false;
		}

		if (!SetupPlatformFilters(connection_name)) {
			PrintRoutines::Output("SetupPlatformFilters failed so attempting to remove all filters as cleanup...");
			if (!RemoveFilters(connection_name)) {
				PrintRoutines::Output("Failed to remove DNS filters");
			}
			return false;
		}

		PrintRoutines::Output(Grd::FormatAString("SetFilters: Filters are now set and active for: {0}", gcnew array<Object^> {connection_name }));
		return true;
	}

	bool VpnDnsHandler::IsActive() const {
		return engine_ != nullptr;
	}

	bool VpnDnsHandler::RemoveFilters(String^ connection_name) {
		bool success;
		PrintRoutines::Output("RemoveFilters: Attempting to remove any active DNS filters...");
		PrintRoutines::Output(Grd::FormatAString("{0}: {1}", gcnew array<Object^>
		{
			*__func__,
				connection_name
		}
		));
		if (!IsActive()) {
			PrintRoutines::Output("No active filters");
			return true;
		}

		success = RemovePlatformFilters(connection_name); 
		if (!success) {
			PrintRoutines::Output("Failed to remove platform filters!");	
		}
		success = CloseEngineSession();
		if (success) {
			engine_ = nullptr;
			PrintRoutines::Output("Closed engine session");
		}
		else
		{
			PrintRoutines::Output("Failed to close engine session");	
		}
		return success;
	}

	Utility::CheckConnectionResult VpnDnsHandler::GetVpnEntryStatus() {
		PrintRoutines::Output(Grd::FormatAString("GetVpnEntryStatus: Calling ConnectionRoutines::CheckConnection for {0} ...",
			gcnew array<String^> {  ConnectionRoutines::ConnectedEntry }));
		return ConnectionRoutines::CheckConnection(ConnectionRoutines::ConnectedEntry);
	}

	void VpnDnsHandler::DisconnectVPN() {
		auto result = false;

		// TODO - stop the RAS Connection Watcher Thread
		//
		
		result = ConnectionRoutines::DisconnectEntry(ConnectionRoutines::ConnectedEntry);
		if (!result) {
			PrintRoutines::Output(Grd::FormatAString("Failed to disconnect entry:{0}. Result = {1}({2})",
				ConnectionRoutines::ConnectedEntry, result, gcnew array<Object^> {GetLastError() }));
		}
	}

	void VpnDnsHandler::UpdateFiltersState() {
		PrintRoutines::Output("UpdateFiltersState: Calling GetVpnEntryStatus()...[CONNECT#1.2.1][DISCONNECT#?.?]");
		switch (GetVpnEntryStatus()) {
		case Utility::CheckConnectionResult::CONNECTED:
			PrintRoutines::Output("UpdateFiltersState: GuardianVPN connected, set filters [CONNECT#1.2.2]");
			if (IsActive()) {
				PrintRoutines::Output("UpdateFiltersState: GuardianVPN connected and Filters are already installed [CONNECT#1.2.2a]");
				return;
			}
			PrintRoutines::Output("UpdateFiltersState: GuardianVPN connected, setting filters [CONNECT#1.2.3]");
			if (!SetFilters(ConnectionRoutines::ConnectedEntry))
			{
				PrintRoutines::Output("UpdateFiltersState: Failed to set DNS filters [CONNECT#1.2.3-FAIL]");
				DisconnectVPN();
				return;
			}
			PrintRoutines::Output("UpdateFiltersState: Calling SetFiltersInstalledFlag(): [CONNECT#1.2.3-OK]");
			VpnUtils::SetFiltersInstalledFlag();
			break;
		case Utility::CheckConnectionResult::DISCONNECTED:
			PrintRoutines::Output("UpdateFiltersState: GuardianVPN Disconnected, remove filters [DISCONNECT#1.2.1]");
			if (!RemoveFilters(ConnectionRoutines::ConnectedEntry))
			{
				PrintRoutines::Output("UpdateFiltersState: Failed to remove DNS filters");
				break;
			}
			// Reset service launch counter if dns filters successfully removed.
			VpnUtils::ResetFiltersInstalledFlag();
			break;
		default:
			PrintRoutines::Output(Grd::FormatAString("GuardianVPN is connecting, try later after {0} seconds",
				gcnew array<Object^> {kCheckConnectionIntervalInSeconds}));
			break;
		}
	}

	void VpnDnsHandler::OnObjectSignaled(HANDLE object) {
		PrintRoutines::Output(Grd::FormatAString("{0}", gcnew array<Object^> {*__func__}));
		// We receive events from all connections in the system and filter here
		// only expected brave vpn event.
		if (object != event_handle_for_vpn_) {
			return;
		}
		UpdateFiltersState();
	}


}

