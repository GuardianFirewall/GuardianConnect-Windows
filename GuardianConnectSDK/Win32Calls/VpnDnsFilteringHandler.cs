using GuardianConnect.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Win32Calls.WFP;
using Windows.Win32.Foundation;

namespace Win32Calls
{
    internal class VpnDnsFilteringHandler
    {
        private static Microsoft.Extensions.Logging.ILogger _logger = NullLogger.Instance;
        public static Microsoft.Extensions.Logging.ILogger Logger
        {
            get
            {
                if (_logger == NullLogger.Instance)
                {
                    _logger = StaticLoggerFactory.CreateLogger("VpnDnsFilteringHandler");
                }
                return _logger;
            }
        }

        internal static HANDLE engine_ = HANDLE.Null;

        internal static bool IsActive()
        {
            return engine_ != HANDLE.Null;
        }

        internal static bool SetFilters(string EntryName)
        {
            if (IsActive())
            {
                Logger.LogInformation("SetFilters: DNS Filtering already active, skipping...");
                return true;
            }
            Logger.LogInformation($"SetFilters: Setting DNS filters for '{EntryName}'...");
            engine_ = Win32Calls.WFP.VpnUtils.OpenWpmSession();
            if (engine_ == HANDLE.Null)
            {
                Logger.LogInformation("SetFilters: Failed to create engine.");
                return false;
            }

            bool success = SetupPlatformFilters(EntryName);
            if (!success)
            {
                Logger.LogError("SetFilters: SetupPlatformFilters failed.");
                // We really should try to close the engine if we failed to add filters.
                return false;
            }
            Logger.LogInformation("SetFilters: DNS Filtering set successfully.");
            return success;
        }

        private static bool SetupPlatformFilters(string EntryName)
        {
            Logger.LogInformation("SetupPlatformFilters: [CONNECT#5.1] Attempting to add filters...");
            return VpnUtils.AddWpmFilters(engine_, EntryName);
        }

        private static bool RemovePlatformFilters(string EntryName)
        {
            Logger.LogInformation("RemovePlatformFilters: [DISCONNECT#5.1] Attempting to remove filters...");
            return VpnUtils.RemoveWpmFilters(engine_, EntryName);
        }

        internal static bool RemoveFilters(string EntryName)
        {
            bool success = true;
            if (!IsActive())
            {
                Logger.LogInformation("RemoveFilters: DNS Filtering not active, skipping...");
                return true;
            }
            Logger.LogInformation($"RemoveFilters: Removing DNS filters for '{EntryName}'...");
            if (!Win32Calls.WFP.VpnUtils.CloseWpmSession(engine_))
            {
                Logger.LogInformation("RemoveFilters: Failed to close engine.");
                return false;
            }
            engine_ = HANDLE.Null;
            Logger.LogInformation("RemoveFilters: DNS Filtering removed successfully.");

#if NEEDED
            Logger.LogInformation($"RemoveFilters: Removing DNS filters for '{EntryName}'...");
            bool success = RemovePlatformFilters(EntryName);
            if (!success)
            {
                Logger.LogInformation("RemoveFilters: Failed to remove platform filters. Continuing to close WpmSession...");
            }

            if (!Win32Calls.WFP.VpnUtils.CloseWpmSession(engine_))
            {
                Logger.LogInformation("RemoveFilters: Failed to close engine.");
                return false;
            }
            engine_ = HANDLE.Null;
            Logger.LogInformation("RemoveFilters: DNS Filtering removed successfully.");
#endif
            return success;
        }

        internal static void UpdateFiltersState(string EntryName)
        {
            Logger.LogInformation($"UpdateFiltersState: Calling CheckConnection('{EntryName}')...[CONNECT#1.2.1][DISCONNECT#?.?]");
            var connectionResult = ConnectionRoutines.CheckConnection(EntryName);
            switch (connectionResult)
            {
                case Utility.CheckConnectionResult.CONNECTED:
			        Logger.LogInformation("UpdateFiltersState: GuardianVPN connected, set filters [CONNECT#1.2.2]");
                    if (IsActive())
                    {
                        Logger.LogInformation(
                            "UpdateFiltersState: GuardianVPN connected and Filters are already installed [CONNECT#1.2.2a]");
                        return;

                    }

                    Logger.LogInformation("UpdateFiltersState: GuardianVPN connected, setting filters [CONNECT#1.2.3]");
                    // Enable DNS filtering
                    if (!SetFilters(EntryName))
                    {
                        Logger.LogInformation("UpdateFiltersState: Failed to set DNS filters [CONNECT#1.2.3-FAIL]");
                        RemoveFilters(EntryName);
                        ConnectionRoutines.DisconnectEntryAndRemove();
                        return;
                    }

                    Logger.LogInformation("UpdateFiltersState: Calling SetFiltersInstalledFlag(): [CONNECT#1.2.3-OK]");
                    VpnUtils.SetFiltersInstalledFlag();
                    break;
                case Utility.CheckConnectionResult.DISCONNECTED:
                    // Disable DNS filtering
                    Logger.LogInformation("UpdateFiltersState: GuardianVPN Disconnected, remove filters [DISCONNECT#1.2.1]");
                    if (!RemoveFilters(EntryName))
                    {
                        Logger.LogInformation("UpdateFiltersState: Failed to remove DNS filters");
                        break;
                    }

                    // Reset service launch counter if dns filters successfully removed.
                    VpnUtils.ResetFiltersInstalledFlag();
                    break;
                default:
                    // Entry not found, handle accordingly
                    Logger.LogInformation($"Entry '{EntryName}' not found.");
                    break;
            }
        }

    }
}
