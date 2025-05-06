#pragma once
#include <windows.h>
#include <string>

namespace NativeRoutines
{
    //static
	class VpnUtils
    {
    public:
	    static UINT64 TAP_IPv4_Id;
	    static UINT64 TAP_IPv6_Id;
	    static UINT64 QBlock_IPv6_Id;
	    static UINT64 QBlock_IPv4_Id;

        // Sets helper's flag to indicate filters successfully installed.
        static void SetFiltersInstalledFlag();
        // Resets helper's filters installed flag.
        static void ResetFiltersInstalledFlag();
        // Register and setup DNS filters layer to the system, if the layer is already
        // registered reuses existing.
        static bool AddWpmFilters(HANDLE engine_handle, System::String^ name);
        static bool RemoveWpmFilters(HANDLE engine_handle, System::String^ name);
        // Opens a session to a filter engine.
        static HANDLE OpenWpmSession();
        // Closes a session to a filter engine.
        static bool CloseWpmSession(HANDLE engine);
        // Subscribes for RAS connection notification of any os vpn entry.
        static bool SubscribeRasConnectionNotification(HANDLE event_handle);
        // Configure VPN Service autorestart.
        static bool ConfigureServiceAutoRestart(const std::wstring& service_name,
                                         const std::wstring& brave_vpn_entry);
        static SC_HANDLE SCM;

    };
}