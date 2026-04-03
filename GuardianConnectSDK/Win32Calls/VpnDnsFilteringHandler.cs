using Windows.Win32.Foundation;
using GuardianConnect.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Win32Calls.WFP;

namespace Win32Calls;

internal class VpnDnsFilteringHandler
{
    private static ILogger _logger = NullLogger.Instance;

    internal static HANDLE engine_ = HANDLE.Null;

    public static ILogger Logger
    {
        get
        {
            if (_logger == NullLogger.Instance) _logger = StaticLoggerFactory.CreateLogger("VpnDnsFilteringHandler");
            return _logger;
        }
    }

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
        engine_ = VpnUtils.OpenWpmSession();
        if (engine_ == HANDLE.Null)
        {
            Logger.LogInformation("SetFilters: Failed to create engine.");
            return false;
        }

        var success = SetupPlatformFilters(EntryName);
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
        Logger.LogDebug("SetupPlatformFilters: Attempting to add filters...");
        return VpnUtils.AddWpmFilters(engine_, EntryName);
    }

    private static bool RemovePlatformFilters(string EntryName)
    {
        Logger.LogDebug("RemovePlatformFilters: Attempting to remove filters...");
        return VpnUtils.RemoveWpmFilters(engine_, EntryName);
    }

    internal static bool RemoveFilters(string EntryName)
    {
        var success = true;
        if (!IsActive())
        {
            Logger.LogInformation("RemoveFilters: DNS Filtering not active, skipping...");
            return true;
        }

        Logger.LogInformation($"RemoveFilters: Removing DNS filters for '{EntryName}'...");
        if (!VpnUtils.CloseWpmSession(engine_))
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
        Logger.LogDebug($"UpdateFiltersState: Calling CheckConnection('{EntryName}')");
        var connectionResult = ConnectionRoutines.CheckConnection(EntryName);
        switch (connectionResult)
        {
            case Utility.CheckConnectionResult.CONNECTED:
                Logger.LogDebug("UpdateFiltersState: GuardianVPN connected, set filters");
                if (IsActive())
                {
                    Logger.LogDebug(
                        "UpdateFiltersState: GuardianVPN connected and Filters are already installed");
                    return;
                }

                Logger.LogDebug("UpdateFiltersState: GuardianVPN connected, setting filters");
                // Enable DNS filtering
                if (!SetFilters(EntryName))
                {
                    Logger.LogDebug("UpdateFiltersState: Failed to set DNS filters");
                    RemoveFilters(EntryName);
                    ConnectionRoutines.DisconnectEntryAndRemove();
                    return;
                }

                Logger.LogDebug("UpdateFiltersState: Calling SetFiltersInstalledFlag():");
                VpnUtils.SetFiltersInstalledFlag();
                break;
            case Utility.CheckConnectionResult.DISCONNECTED:
                // Disable DNS filtering
                Logger.LogDebug("UpdateFiltersState: GuardianVPN Disconnected, remove filters");
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