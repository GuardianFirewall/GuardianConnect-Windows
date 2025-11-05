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
    public (string, string, ErrorResponse) SelectGuardianHostWithCompletion()
    // CHANGE ^-----------------------^
    {
        // CONN#5
        _logger.LogInformation("CONN#5");

        // TJE - taking first host in our region for now.
        RegionalHostRecord regionHostRecord = new RegionalHostRecord();

        if (RegionUtils.KeyForCurrentlySelectedRegion != null)
            regionHostRecord = RegionUtils.GetMyRegionHostRecord(RegionUtils.KeyForCurrentlySelectedRegion);

        // CHANGE
        return (regionHostRecord.Hostname, regionHostRecord.HostLocation(), new ErrorResponse() ); // CHANGE!!
    }
}
