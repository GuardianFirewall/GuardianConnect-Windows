using GuardianConnect.API;
using GuardianConnect.API.Model;
using GuardianConnect.Credentials;
using GuardianConnect.Helpers;
using GuardianConnect.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GuardianConnect.API;

public class GRDServerManager
{
    #region GRDServerManager private stuff
    private static int _latest = 1;
    private static int Active = 0;
    private static int Standby => Active ^ 1;
    private static Dictionary<int, GeoInfoCache> _geoInfoCaches = new()
        {
            { 0, new GeoInfoCache() },
            { 1, new GeoInfoCache() }
        };


    private static GeoInfoCache Live => _geoInfoCaches[Active];
    private static GeoInfoCache Alternate => _geoInfoCaches[Standby];
    private static DateTime LastUpdateChangeTime;
    private static ManualResetEventSlim RegionHostsRetrievalWaiter = new ManualResetEventSlim();

    #endregion

    private static Microsoft.Extensions.Logging.ILogger _logger = NullLogger.Instance;
    private static Microsoft.Extensions.Logging.ILogger Logger
    {
        get
        {
            if (_logger == NullLogger.Instance)
            {
                _logger = StaticLoggerFactory.CreateLogger("GRDServerManager");
                _logger.LogInformation("GRDServerManager: TEST Log");
            }
            return _logger;
        }
    }

    public GRDVPNHelper.GRDServerFeatureEnvironment FeatureEnv;
    public bool BetaCapable { get; set; }
    private static GRDRegion SelectedRegion { get; set; }

    public GRDServerManager()
    {
        FeatureEnv = GRDVPNHelper.GRDServerFeatureEnvironment.ServerFeatureEnvironmentProduction;
        BetaCapable = false;
    }

    /// Used to find and return the VPN server node we will connect to based on the results of a call to 'getGuardianHostsWithCompletion:"
    /// @param completion Completion block that will contain the selected host, hostLocation upon success or an error message upon failure.
    // This is called from GRDVPNHelper.SelectAndSetBestGuardianHost
    public static (string, string, ErrorResponse) SelectGuardianHostWithCompletion(string? selectedRegionKey)
    // CHANGE ^-----------------------^
    {
        // CONN#5
        Logger.LogInformation("GRDServerManager.SelectGuardianHostWithCompletion: [CONN#5]  selectedRegionKey: " + (selectedRegionKey ?? "null"));
        SelectedRegion = GetGRDRegionByKey(selectedRegionKey ?? GetRegionForOurTimeZone());

        Logger.LogInformation(
            $"GRDServerManager.SelectGuardianHostWithCompletion: Calling SelectBestHostInRegion for region '{SelectedRegion.RegionName}'");
        RegionalHostRecord regionHostRecord = SelectBestHostInRegion(SelectedRegion.RegionName);

        return (regionHostRecord.Hostname, regionHostRecord.HostLocation(), new ErrorResponse()); // CHANGE!!
    }

    #region region loading and collections
    // Caller can set the refresh interval. Defaulting to 1 hour.
    public static TimeSpan TimeSpanBetweenEachGeoRefresh { get; set; } = new TimeSpan(1, 0, 0); // Change to registry setting later
    public static ManualResetEventSlim InitialGeoInformationLoadComplete = new ManualResetEventSlim();

    #region public methods
    public static DateTime GetLastTimeUpdated() => LastUpdateChangeTime;

    public static async Task LongRunningRefreshTask(CancellationToken cancellationToken)
    {
        var RefreshMinutesStr =
            RegistrySettings.RetrieveGuardianUserSettings("MinutesBetweenGeoRefreshChecks");
        if (String.IsNullOrEmpty(RefreshMinutesStr))
        {
            TimeSpanBetweenEachGeoRefresh = new TimeSpan(1, 0, 0); // 1 hour default
        }
        else
        {
            TimeSpanBetweenEachGeoRefresh = TimeSpan.FromMinutes(Convert.ToDouble(RefreshMinutesStr));
        }

        await Task.Factory.StartNew(async () =>
        {
            Logger.LogInformation(
                $"GRDServerManager.LongRunningRefreshTask: Kicking off RefreshDataAsync task to run every {TimeSpanBetweenEachGeoRefresh} period");
            do
            {
                await RefreshDataAsync(); // sub-second - no need to pass cancellation token
                Logger.LogInformation($"GRDServerManager.LongRunningRefreshTask: RefreshDataAsync completed. ");
                if (_geoInfoCaches[Standby].Checksum() != _geoInfoCaches[Active].Checksum())
                {
                    Logger.LogInformation(
                        "GRDServerManager.LongRunningRefreshTask: The latest refresh has changes. Toggling ACTIVE to point LIVE to newest data.");
                    Logger.LogInformation($"Pre-Switch: Latest(on index {Standby}): {Alternate.Checksum()}, Active (on index {Active}): {Live.Checksum()}");
                    SetActiveToLatest();
                    Logger.LogInformation($"Active Switched (to index {Active}): Latest (now on index {Standby}): {Alternate.Checksum()}, Active: {Live.Checksum()}");
                }

                InitialGeoInformationLoadComplete.Set();
                await Task.Delay(TimeSpanBetweenEachGeoRefresh, cancellationToken);

            } while (cancellationToken.IsCancellationRequested == false);
        }, cancellationToken);
    }


    public static async Task RefreshDataAsync()
    {
        DateTime startTime = DateTime.Now;
        InitializeAlternate();
        Logger.LogInformation("RefreshDataAsync: 1. calling RefreshStandbyRegionsList()...");
        var regions = await RefreshStandbyRegionsLists();
        Logger.LogInformation("RefreshDataAsync: 2. calling GetLatestTimeZonesForRegions()...");
        await GetLatestTimeZonesForRegions();
        Logger.LogInformation($"Latest Regions Collection has {Alternate.regionLookup.Count} region records");
        Logger.LogInformation($"Latest Timezone Collection has {Alternate.timezonesLookup.Count} timezone records");

        Alternate.ComputeHash();
        Logger.LogInformation($"Checksum : {_geoInfoCaches[1].Checksum()}");
        DateTime endTime = DateTime.Now;
        Logger.LogInformation($"Region Collection refreshed. Checksum = {Alternate.Checksum()}");
        Logger.LogInformation($"Total GRDServerManager.RefreshDataAsync execution time = {((endTime - startTime).TotalMilliseconds) / 1000} seconds");
    }

    public static List<string> GetSortedRegionKeys() => Live.RegionKeysByDisplay.Keys.ToList();

    public static void SwapActiveGeoInfoCache()
    {
        Toggle();
        SetActiveToLatest();
        Logger.LogInformation("SwapActiveGeoInfoCache: Swapped active GeoInfoCache to latest.");
    }

    public static bool LookUpRegionIndexForMyTimeZone(string ourTimeZoneId, out string myRegionKey)
    {
        string containingKey = string.Empty;
        myRegionKey = "us-east"; // default

        Logger.LogInformation($"LookUpRegionIndexForMyTimeZone:  Our time zone ID = '{ourTimeZoneId}'");
        var ourKey = "us-east";
        Logger.LogInformation($"timezonesLookup.Keys.Count = {Live.timezonesLookup.Keys.Count}.");

        try
        {
            containingKey = Live.timezonesLookup.Keys
                .Where(key => Live.timezonesLookup[key].Contains(ourTimeZoneId))
                .FirstOrDefault() ?? throw new InvalidOperationException();
            myRegionKey = containingKey;

        }
        catch (Exception e)
        {
            if (e is InvalidOperationException ioe)
            {
                myRegionKey = string.IsNullOrEmpty(containingKey) ? "us-east" : containingKey;
                Logger.LogWarning(ioe,
                    $"LookUPRegionIndexForMyTimeZone: Defaulting to 'us-east' as timezone not found in timezonesLookup collection!");
                Logger.LogWarning("LookUpReginoIndexForMyTimeZone: Dumping timezonesLookup collection...");
            }
            else
            {
                Logger.LogError(e,
                    $"LookUpRegionIndexForMyTimeZone: Exception thrown when looking up region for timezone '{ourTimeZoneId}': {e.Message}");
                containingKey = "us-east";
                myRegionKey = containingKey;
            }
        }

        return string.IsNullOrEmpty(containingKey);
    }

    public static string GetRegionKeyByDisplayName(string pn)
    {
        if (pn == "Automatic") return "Automatic";
        return Live.regionLookup.Values.First(v => v.DisplayName == pn).RegionName;
    }

    public static string GetRegionPrettyName(string regionKey)
    {
        return Live.regionLookup[regionKey].DisplayName;
    }

    public static GRDRegion GetGRDRegionByKey(string regionKey)
    {
        return Live.regionLookup[regionKey];
    }
    #endregion

    #region private methods
    private static int Toggle() => Interlocked.Exchange(ref _latest, _latest ^ 1);

    private static void SetActiveToLatest()
    {
        Active = _latest;
        Toggle();
        LastUpdateChangeTime = DateTime.Now;
    }

    private static void InitializeAlternate() => _geoInfoCaches[Standby] = new GeoInfoCache();


    private static async Task<List<GRDRegion>> RefreshStandbyRegionsLists()
    {
        Logger.LogInformation("RefreshStandbyRegionsLists() executing...");
        ErrorResponse errorResponse = new ErrorResponse();
        string errorMessage = string.Empty;
        int responseCode = 0;

        var regionsList = new List<GRDRegion>();
        try
        {
            errorResponse = await GRDHousekeepingAPI.RequestServerRegions();
            var response = errorResponse.HttpResponse;
            if (response.IsSuccessStatusCode)
            {
                Logger.LogInformation($"RefreshStandbyRegionsLists: Return from RequestServerRegions: Response statusCode = {response.StatusCode}");
                string content = errorResponse.Data.ToString();
                if (string.IsNullOrEmpty(content))
                {
                    Logger.LogInformation("RefreshStandbyRegionsLists: content returned for regions is empty");
                }
                else
                {
                    Logger.LogInformation("RefreshStandbyRegionsLists: Successfully retrieved latest regions from backend.");
                    Alternate.contentstrings.Add(content);
                    var jsonOptions = new JsonSerializerOptions
                    {
                        AllowOutOfOrderMetadataProperties = true,
                        AllowTrailingCommas = true,
                        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
                    };
                    regionsList = JsonSerializer.Deserialize<List<GRDRegion>>(content, GRDRegionJsonContext.Default.ListGRDRegion);
                    Logger.LogInformation($"RefreshStandbyRegionsLists: Regions Collection loaded with (ACTUAL) {regionsList.Count} items");
                }

                responseCode = (int)response.StatusCode;
            }
            else
            {
                Logger.LogInformation($"RefreshStandbyRegionsLists: Response for getting latest regions is {response.StatusCode}");
                regionsList = GRDRegion.StaticRegions;
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, $"RefreshStandbyRegionsLists(): Exception thrown when calling all-server-regions...: {ex.Message}. (STATIC) Using GRDRegion.StaticRegions list data");
        }

        // Populate region lookup collections
        Alternate.RegionKeysByDisplay.TryAdd("Automatic", "Automatic");
        Alternate.regionLookup.Add("Automatic",
            new GRDRegion
            {
                RegionName = "Automatic",
                DisplayName = "Automatic",
                BestHost = string.Empty,
                BestHostLocation = string.Empty,
                Continent = string.Empty,
                CountryISOCode = string.Empty
            });
        Logger.LogInformation($"RefreshStandbyRegionsLists: regionLookup pre-load has {Alternate.regionLookup.Count} items.");
        var rluKeys = String.Join(',', Alternate.regionLookup.Keys);
        Logger.LogDebug($"regionLookup dictionary keys are: '{rluKeys}");
        foreach (var regionRec in regionsList.OrderBy(region => region.DisplayName))
        {
            if (!Alternate.regionLookup.TryAdd(regionRec.RegionName, regionRec))
            {
                Logger.LogError($"GetLatestRegionsList: Failed to add region name/pretty-name to regionlookup dictionary for '{regionRec.RegionName}' using TryAdd");
                try
                {
                    Alternate.regionLookup.Add(regionRec.RegionName, regionRec);
                    Logger.LogInformation($"GetLatestRegionsList: SUCCESS in adding region '{regionRec.RegionName}' to regionLookup collection.");
                }
                catch (Exception e)
                {
                    Logger.LogCritical(e, $"GetLatestRegionsList: FATAL - Could not add region '{regionRec.RegionName}' object to regionLookup collection!");
                    throw;
                }
            }
            Alternate.RegionKeys.Add(regionRec.RegionName);
            Alternate.RegionKeysByDisplay.TryAdd(regionRec.DisplayName, regionRec.RegionName);
        }

        return regionsList;
    }

    private static async Task GetLatestTimeZonesForRegions()
    {
        Logger.LogInformation("GetLatestTimeZonesForRegions executing...");
        string errorMessage = string.Empty;

        var geoDataCollection = new List<GeoData>();

        var response = await GRDHousekeepingAPI.RequestLatestTimeZonesForRegions();

        if (response.IsError)
        {
            Logger.LogError($"GetLatestTimeZonesForRegions: Error retrieving latest timezones for regions: {response.Response}");
            geoDataCollection = GeoData.StaticGeoDataCollection;
        }
        else
        {
            Logger.LogInformation("GetLatestTimeZonesForRegions: Successfully retrieved latest timezones for regions from backend.");
            string content = response.Data.ToString();
            Alternate.contentstrings.Add(content);
            geoDataCollection = JsonSerializer.Deserialize<List<GeoData>>(content, GeoDataJsonContext.Default.ListGeoData);
            Logger.LogInformation($"GetLatestTimeZonesForRegions: Timezones Collection loaded with (ACTUAL) {geoDataCollection.Count} items");
        }

        Logger.LogInformation($"GetLatestTimeZonesForRegions: now populating timezonesLookup dictionary with {geoDataCollection.Count} entries...");
        Alternate.timezonesLookup = new Dictionary<string, List<string>>();
        foreach (var geoRec in geoDataCollection)
        {
            Logger.LogDebug($"GetLatestTimeZonesForRegions: Adding '{geoRec.KeyName}' with {geoRec.Timezones.Count} timezones");
            if (Alternate.timezonesLookup.TryAdd(geoRec.KeyName, geoRec.Timezones) == false)
            {
                Logger.LogWarning($"GetLatestTimeZonesForRegions: Could not add timezones for region key '{geoRec.KeyName}");
            }
        }
    }

    // Get hosts for a region - NOTE: THIS ACTS ON LIVE DATASET - as we don't need to preload hosts until needed
    // ALSO: We DON'T add this to HASH calculation as hosts are an AT-MOMENT-OF-USE data set
    internal static async Task GetHostsForRegion(string regionKey)
    {
        var message = "";
        Logger.LogInformation($"GetHostsForRegion: Retrieving hosts for region {regionKey}.");
        var regionRec = Live.regionLookup[regionKey];
        Logger.LogInformation($"GetHostsForRegion: Calling GetHostsForRegionKey with key = '{regionKey}");

        HttpResponseMessage response = new HttpResponseMessage();
        string getHostsForRegionUrl = $"https://{Common.DefaultConnectAPIHostname}/api/v1/servers/hostnames-for-region";
        Uri uri = new Uri(getHostsForRegionUrl);
        try
        {
            RegionInputParameter rip = new RegionInputParameter();
            rip.Region = regionRec.RegionName;

            Logger.LogInformation("About to do GET for Region Hosts collection retrieval");
            string ripSerialized = JsonSerializer.Serialize(rip, RegionInputParameterJsonContext.Default.RegionInputParameter);
            HttpContent content = new StringContent(ripSerialized);
            content.Headers.Remove("Content-Type");
            content.Headers.Add("Content-Type", "application/json; charset=utf-8");

            try
            {
                response = HttpUtils.Client?.PostAsync(uri, content).GetAwaiter().GetResult() ?? throw new InvalidOperationException();
            }
            catch (HttpRequestException hrex)
            {
                Logger.LogError(hrex, $"FAILURE: HTTP REQUEST EXCEPTION - Failed to get hosts for region {regionKey}. StatusCode={hrex.StatusCode}, Error={hrex.HttpRequestError}");
                throw;
            }
            catch (Exception e)
            {
                Logger.LogError(e, $"Failed to get hosts for region {regionKey}");
                throw;
            }

            if (response.IsSuccessStatusCode)
            {
                string respContent = await response.Content.ReadAsStringAsync();
                List<RegionalHostRecord> regionHosts = JsonSerializer.Deserialize<List<RegionalHostRecord>>(respContent, RegionalHostRecordJsonContext.Default.ListRegionalHostRecord);
                if (!Live._hostLookup.ContainsKey(regionKey))
                {
                    Live._hostLookup.Add(regionKey, null);
                }
                Live._hostLookup[regionKey] = regionHosts;
                message = $"GRDServerManager.GetHostForRegion: Added {regionHosts.Count} hosts for region '{regionKey}'";
                Logger.LogInformation(message);
            }
            else
            {
                message = $"GRDServerManager.GetHostForRegion: ResponseCode for getting region hosts for region '{regionKey}': {response.StatusCode}";
                Logger.LogInformation(message);
            }

            int hostCount = Live._hostLookup[regionRec.RegionName].Count;
            message = $"GetHostsForRegion(): Getting latest collection of hosts for Region {regionRec.RegionName} - {regionRec.DisplayName}. Number of hosts = {hostCount}";
            Logger.LogInformation(message);

        }
        catch (Exception ex)
        {
            Logger.LogError(ex, @"\tERROR {0}", ex.Message);
        }

        RegionHostsRetrievalWaiter.Set();
    }

    internal static RegionalHostRecord SelectBestHostInRegion(string regionKey)
    {
        RegionHostsRetrievalWaiter.Reset();
        if (!Live._hostLookup.ContainsKey(regionKey) || Live._hostLookup[regionKey].Count == 0)
        {
            Logger.LogInformation($"GRDServerManager.SelectBestHostInRegion: Region '{regionKey}' needs host list refresh... calling GetHostsForRegion to update now");
            Task.Factory.StartNew(async () =>
            {
                GetHostsForRegion(regionKey);
            });
            Logger.LogInformation("RegionUtil.SelectBestHostInRegion: Waiting for GetHostsForRegion to return results...");
            RegionHostsRetrievalWaiter.Wait(5 * 1000);

            Logger.LogInformation($"GRDServerManager.SelectBestHostInRegion: Return from GetHostsForRegion - region '{regionKey}' host list refresh complete.");
        }

        if (!Live._hostLookup.TryGetValue(regionKey, out var myRegionRecord))
        {
            throw new Exception($"Hosts Lookup collection does NOT contain record for region {regionKey}");
        }

        // Do random thing
        var regionHosts = Live._hostLookup[regionKey];
        var lightest = regionHosts.Where(h => h.CapacityScore == 0);
        var lighter = regionHosts.Where(h => h.CapacityScore == 1);

        Logger.LogInformation($"SelectBestHostInRegion: For region '{regionKey}' we have {lightest.Count()} lightest hosts, {lighter.Count()} midrange hosts out of {regionHosts.Count()} total hosts");

        if (lightest != null && lightest.Count() > 0)
            return lightest.ElementAt(Random.Shared.Next(lightest.Count() - 1));

        if (lighter != null && lighter.Count() > 0)
            return lighter.ElementAt(Random.Shared.Next(lighter.Count() - 1));

        return regionHosts.ElementAt(Random.Shared.Next(regionHosts.Count - 1));
    }

    private static string GetLocalTimeZone()
    {
        // Get some time and timezone stuff
        var localTimeZoneInfo = TimeZoneInfo.Local;
        var inanaId = TimeZoneInfo.TryConvertWindowsIdToIanaId(localTimeZoneInfo.Id, out string otzID);
        Logger.LogInformation($"GetLocalTimeZone(): Local time zone is {TimeZoneInfo.Local}. Our Time Zone ID: '{otzID}'");

        return otzID;
    }

    private static string GetRegionForOurTimeZone()
    {
        string ourTimeZoneId = GetLocalTimeZone();
        // Let's just tuck away our fixed region where we are
        var timezoneMissing = GRDServerManager.LookUpRegionIndexForMyTimeZone(ourTimeZoneId, out string regionKeyForOurTimeZone);
        if (timezoneMissing)
        {
            Logger.LogWarning($"Our time zone ID '{ourTimeZoneId}' was NOT FOUND in our Regions' Time Zones Lookup tables!!");
            ourTimeZoneId = "America/New_York";
        }

        return regionKeyForOurTimeZone;
    }

#endregion - private methods

#endregion
}
