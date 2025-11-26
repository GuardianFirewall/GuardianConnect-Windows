using GuardianConnect.API.Model;
using GuardianConnect.Helpers;
using GuardianConnect.Shared;
using Microsoft.Extensions.Logging;

namespace GuardianConnect.API;

public class GRDServerManager
{
    private Microsoft.Extensions.Logging.ILogger<GRDServerManager> _logger;

    public GRDHousekeepingAPI Housekeeping { get; set; }
    public GRDVPNHelper.GRDServerFeatureEnvironment FeatureEnv;
    public bool BetaCapable { get; set; }
    public GRDRegion SelectedRegion { get; set; }

    public GRDServerManager()
    {
        _logger = StaticLoggerFactory.CreateLogger<GRDServerManager>();
        _logger.LogInformation("GRDServerManager TEST Log");

        Housekeeping = new GRDHousekeepingAPI();
        FeatureEnv = GRDVPNHelper.GRDServerFeatureEnvironment.ServerFeatureEnvironmentProduction;
        BetaCapable = false;
    }

    /// Used to find and return the VPN server node we will connect to based on the results of a call to 'getGuardianHostsWithCompletion:"
    /// @param completion Completion block that will contain the selected host, hostLocation upon success or an error message upon failure.
    // This is called from GRDVPNHelper.SelectAndSetBestGuardianHost
    public (string, string, ErrorResponse) SelectGuardianHostWithCompletion(string? selectedRegionKey)
    // CHANGE ^-----------------------^
    {
        // CONN#5
        _logger.LogInformation("GRDServerManager.SelectGuardianHostWithCompletion: CONN#5");


        _logger.LogInformation("GRDServerManager.SelectGuardianHostWithCompletion: selectedRegionKey: " + (selectedRegionKey ?? "null"));
            SelectedRegion = RegionUtils.GetGRDRegionByKey(selectedRegionKey ?? GRDVPNHelper.RegionKeyForOurTimeZone);

        // TJE - taking first host in our region for now.
        _logger.LogInformation(
            $"GRDServerManager.SelectGuardianHostWithCompletion: Calling RegionUtils.SelectBestHostInRegion for region '{SelectedRegion.RegionName}'");
        RegionalHostRecord regionHostRecord = RegionUtils.SelectBestHostInRegion(SelectedRegion.RegionName);

        return (regionHostRecord.Hostname, regionHostRecord.HostLocation(), new ErrorResponse() ); // CHANGE!!
    }
}
