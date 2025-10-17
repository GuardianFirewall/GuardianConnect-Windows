using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Win32.Foundation;
using Serilog;
using Win32Calls.WFP;

namespace Win32Calls
{
    internal class VpnDnsFilteringHandler
    {
        internal static HANDLE engine_ = HANDLE.Null;

        internal static bool IsActive()
        {
            return engine_ != HANDLE.Null;
        }

        internal static bool SetFilters(string EntryName)
        {
            if (IsActive())
            {
                Log.Information("SetFilters: DNS Filtering already active, skipping...");
                return true;
            }
            Log.Information($"SetFilters: Setting DNS filters for '{EntryName}'...");
            engine_ = Win32Calls.WFP.VpnUtils.OpenWpmSession();
            if (engine_ == HANDLE.Null)
            {
                Log.Information("SetFilters: Failed to create engine.");
                return false;
            }

            bool success = SetupPlatformFilters(EntryName);
            if (!success)
            {
                Log.Error("SetFilters: SetupPlatformFilters failed.");
                // We really should try to close the engine if we failed to add filters.
                return false;
            }
            Log.Information("SetFilters: DNS Filtering set successfully.");
            return success;
        }

        private static bool SetupPlatformFilters(string EntryName)
        {
            Log.Information("SetupPlatformFilters: [CONNECT#5.1] Attempting to add filters...");
            return VpnUtils.AddWpmFilters(engine_, EntryName);
        }

        private static bool RemovePlatformFilters(string EntryName)
        {
            Log.Information("RemovePlatformFilters: [DISCONNECT#5.1] Attempting to remove filters...");
            return VpnUtils.RemoveWpmFilters(engine_, EntryName);
        }

        internal static bool RemoveFilters(string EntryName)
        {
            bool success = true;
            if (!IsActive())
            {
                Log.Information("RemoveFilters: DNS Filtering not active, skipping...");
                return true;
            }
            Log.Information($"RemoveFilters: Removing DNS filters for '{EntryName}'...");
            if (!Win32Calls.WFP.VpnUtils.CloseWpmSession(engine_))
            {
                Log.Information("RemoveFilters: Failed to close engine.");
                return false;
            }
            engine_ = HANDLE.Null;
            Log.Information("RemoveFilters: DNS Filtering removed successfully.");

#if NEEDED
            Log.Information($"RemoveFilters: Removing DNS filters for '{EntryName}'...");
            bool success = RemovePlatformFilters(EntryName);
            if (!success)
            {
                Log.Information("RemoveFilters: Failed to remove platform filters. Continuing to close WpmSession...");
            }

            if (!Win32Calls.WFP.VpnUtils.CloseWpmSession(engine_))
            {
                Log.Information("RemoveFilters: Failed to close engine.");
                return false;
            }
            engine_ = HANDLE.Null;
            Log.Information("RemoveFilters: DNS Filtering removed successfully.");
#endif
            return success;
        }

        internal static void UpdateFiltersState(string EntryName)
        {
            Log.Information($"UpdateFiltersState: Calling CheckConnection('{EntryName}')...[CONNECT#1.2.1][DISCONNECT#?.?]");
            var connectionResult = ConnectionRoutines.CheckConnection(EntryName);
            switch (connectionResult)
            {
                case Utility.CheckConnectionResult.CONNECTED:
			        Log.Information("UpdateFiltersState: GuardianVPN connected, set filters [CONNECT#1.2.2]");
                    if (IsActive())
                    {
                        Log.Information(
                            "UpdateFiltersState: GuardianVPN connected and Filters are already installed [CONNECT#1.2.2a]");
                        return;

                    }

                    Log.Information("UpdateFiltersState: GuardianVPN connected, setting filters [CONNECT#1.2.3]");
                    // Enable DNS filtering
                    if (!SetFilters(EntryName))
                    {
                        Log.Information("UpdateFiltersState: Failed to set DNS filters [CONNECT#1.2.3-FAIL]");
                        RemoveFilters(EntryName);
                        ConnectionRoutines.DisconnectEntry();
                        return;
                    }

                    Log.Information("UpdateFiltersState: Calling SetFiltersInstalledFlag(): [CONNECT#1.2.3-OK]");
                    VpnUtils.SetFiltersInstalledFlag();
                    break;
                case Utility.CheckConnectionResult.DISCONNECTED:
                    // Disable DNS filtering
                    Log.Information("UpdateFiltersState: GuardianVPN Disconnected, remove filters [DISCONNECT#1.2.1]");
                    if (!RemoveFilters(EntryName))
                    {
                        Log.Information("UpdateFiltersState: Failed to remove DNS filters");
                        break;
                    }

                    // Reset service launch counter if dns filters successfully removed.
                    VpnUtils.ResetFiltersInstalledFlag();
                    break;
                default:
                    // Entry not found, handle accordingly
                    Log.Information($"Entry '{EntryName}' not found.");
                    break;
            }
        }

    }
}
