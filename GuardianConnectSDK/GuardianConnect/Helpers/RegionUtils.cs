using GuardianConnect.API;
using GuardianConnect.API.Model;
using GuardianConnect.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Serilog;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GuardianConnect.Helpers
{
    public static class RegionUtils
    {
        const string GetTimeZonesForRegionsUrl = $"https://{Common.kConnectAPIHostname}/api/v1.1/servers/timezones-for-regions";
        const string GetAllRegionsUrl = $"https://{Common.kConnectAPIHostname}/api/v1/servers/all-server-regions";

        private static Microsoft.Extensions.Logging.ILogger _logger = NullLogger.Instance;
        private static Microsoft.Extensions.Logging.ILogger Logger
        {
            get
            {
                if (_logger == NullLogger.Instance)
                {
                    _logger = StaticLoggerFactory.CreateLogger("RegionUtils");
                    _logger.LogInformation("RegionUtils: TEST Log");
                }
                return _logger;
            }
        }

        //private static int _toggleValue = 0;
        private static int _latest = 1;
        private static int Active = 0;
        private static int Inactive => Active ^ 1;
        private static Dictionary<int, GeoInfoCache> _geoInfoCaches = new Dictionary<int, GeoInfoCache>()
        {
            { 0, new GeoInfoCache() },
            { 1, new GeoInfoCache() }
        };


        private static GeoInfoCache Live => _geoInfoCaches[Active];
        private static GeoInfoCache Alternate => _geoInfoCaches[Inactive];
        private static DateTime LastUpdateChangeTime;
        private static ManualResetEventSlim RegionHostsRetrievalWaiter = new ManualResetEventSlim();

        // Caller can set the refresh interval. Defaulting to 1 hour.
        public static TimeSpan TimeSpanBetweenEachGeoRefresh { get; set; } = new TimeSpan(1, 0, 0); // Change to registry setting later
        public static ManualResetEventSlim InitialGeoInformationLoadComplete = new ManualResetEventSlim();

#if NOTNEEDEDNOW
        public static string? RegionForOurActualLocation { get; set; } = null;
        public static string? KeyForCurrentlySelectedRegion { get; set; }
#endif

        #region public methods

        public static DateTime GetLastTimeUpdated() => LastUpdateChangeTime;

        public static async Task LongRunningRefreshTask(CancellationToken cancellationToken)
        {
            var RefreshMinutesStr =
                RegistrySettings.RetrieveGuardianUserSettings("TimepanBetweenEachGeoRefreshMinutes");
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
                    $"RegionUtils.LongRunningRefreshTask: Kicking off RefreshDataAsync task to run every {TimeSpanBetweenEachGeoRefresh} period");
                do
                {
                    await RefreshDataAsync(); // sub-second - no need to pass cancellation token
                    Log.Information($"RegionUtils.LongRunningRefreshTask: RefreshDataAsync completed. ");
                    if (_geoInfoCaches[Inactive].Checksum() != _geoInfoCaches[Active].Checksum())
                    {
                        Log.Information(
                            "RegionUtils.LongRunningRefreshTask: The latest refresh has changes. Toggling ACTIVE to point LIVE to newest data.");
                        Log.Information($"Latest: {Alternate.Checksum()}, Active: {Live.Checksum()}");
                        SetActiveToLatest();
#if DEBUG
                    _geoInfoCaches[Inactive].Sha[0] = 0;
#endif
                        Log.Information($"Latest: {Alternate.Checksum()}, Active: {Live.Checksum()}");
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
            Logger.LogInformation("RefreshDataAsync: 1. calling RefreshInactiveRegionsList()...");
            var regions = await RefreshInactiveRegionsLists();
            Logger.LogInformation("RefreshDataAsync: 2. calling GetLatestTimeZonesForRegions()...");
            await GetLatestTimeZonesForRegions();
            Logger.LogInformation($"Latest Regions Collection has {Alternate.regionLookup.Count} region records");
            Logger.LogInformation($"Latest Timezone Collection has {Alternate.timezonesLookup.Count} timezone records");

            Alternate.ComputeHash();
            Logger.LogInformation($"Checksum : {_geoInfoCaches[1].Checksum()}");
            DateTime endTime = DateTime.Now;
            Logger.LogInformation($"Region Collection refreshed. Checksum = {Alternate.Checksum()}");
            Log.Information($"Total RegionUtils.RefreshDataAsync execution time = {((endTime - startTime).TotalMilliseconds)/1000} seconds");
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
            if (pn == "Automatic") return GRDVPNHelper.RegionKeyForOurTimeZone ?? string.Empty;
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

        private static void InitializeAlternate() => _geoInfoCaches[Inactive] = new GeoInfoCache();


        private static async Task<List<GRDRegion>> RefreshInactiveRegionsLists()
        {
            Logger.LogInformation("RefreshInactiveRegionsLists() executing...");
            string errorMessage = string.Empty;
            int responseCode = 0;

            var regionsList = new List<GRDRegion>();

            Uri uri = new Uri(GetAllRegionsUrl);
            try
            {
                Logger.LogInformation("RefreshInactiveRegionsLists: Getting latest Regions collection from backend...");
                {
                    HttpResponseMessage response = HttpUtils.Client.GetAsync(uri).GetAwaiter().GetResult(); // Task short-circuit jump
                    if (response.IsSuccessStatusCode)
                    {
                        Logger.LogInformation($"RefreshInactiveRegionsLists: Return from getting regions: Response statusCode = {response.StatusCode}");
                        string content = await response.Content.ReadAsStringAsync(); // Task short-circuit jump
                        if (string.IsNullOrEmpty(content))
                        {
                            Logger.LogInformation("RefreshInactiveRegionsLists: content returned for regions is empty");
                        }
                        else
                        {
                            Alternate.contentstrings.Add(content);
                            var jsonOptions = new JsonSerializerOptions
                            {
                                AllowOutOfOrderMetadataProperties = true,
                                AllowTrailingCommas = true,
                                DefaultIgnoreCondition = JsonIgnoreCondition.Never,
                            };
                            regionsList = JsonSerializer.Deserialize<List<GRDRegion>>(content, GRDRegionJsonContext.Default.ListGRDRegion);
                            Logger.LogInformation($"RefreshInactiveRegionsLists: Regions Collection loaded with (ACTUAL) {regionsList.Count} items");
                        }

                        responseCode = (int)response.StatusCode;
                    }
                    else
                    {
                        Logger.LogInformation($"RefreshInactiveRegionsLists: Response from attempting to get latest regions is {response.StatusCode}");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"RefreshInactiveRegionsLists(): Exception thrown when calling all-server-regions...: {ex.Message}. (STATIC) Using GRDRegion.StaticRegions list data");
                regionsList = GRDRegion.StaticRegions;
            }
#if DEBUG
            regionsList.Add(new GRDRegion()
            {
                BestHost = "BESTHOST",
                BestHostLocation = "SOMEWHERE",
                Continent = "ANTARCTICA",
                DisplayName = "Debug Region",
                RegionName = "debug-region"
            });
#endif

            // Populate region lookup collections
            Alternate.RegionKeysByDisplay.TryAdd("Automatic", "Automatic");
            Logger.LogInformation($"RefreshInactiveRegionsLists: regionLookup pre-load has {Alternate.regionLookup.Count} items.");
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
                        Poof();
                    }
                }
                Alternate.RegionKeys.Add(regionRec.RegionName);
                Alternate.RegionKeysByDisplay.TryAdd(regionRec.DisplayName, regionRec.RegionName);
// DEFER                await GetHostsForRegion(regionRec.RegionName);
            }

            return regionsList;
        }

        private static void Poof()
        {
            //Log.CloseAndFlush();
            Environment.Exit(-1);
        }

        private static async Task GetLatestTimeZonesForRegions()
        {
            Logger.LogInformation("GetLatestTimeZonesForRegions executing...");
            string errorMessage = string.Empty;

            var geoDataCollection = new List<GeoData>();

            Uri uri = new Uri(GetTimeZonesForRegionsUrl);
            try
            {
                Logger.LogInformation("GetLatestTimeZonesForRegions: Getting time zones for regions from backend...");
                HttpResponseMessage?
                    response = HttpUtils.Client?.GetAsync(uri).GetAwaiter().GetResult(); // Task short-circuit jump
                if (response != null && response.IsSuccessStatusCode)
                {
                    string content = await response.Content.ReadAsStringAsync(); // Task short-circuit jump
                    Alternate.contentstrings.Add(content);
                    geoDataCollection = JsonSerializer.Deserialize<List<GeoData>>(content, GeoDataJsonContext.Default.ListGeoData);
                    Logger.LogInformation($"GetLatestTimeZonesForRegions: Regions Refresh: Regions GeoData Collection loaded with (ACTUAL) {geoDataCollection.Count} items");
                }
                else
                {
                    if (response != null)
                    {
                        errorMessage = $"GetLatestTimeZonesForRegions: ResponseCode for getting latest regions collection: {response.StatusCode}";
                        Logger.LogError(errorMessage);
                    }
                    else
                    {
                        Logger.LogError($"Response from server at uri '{uri}' to get latest regions returned NULL");
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"GetLatestTimeZonesForRegions(): Exception thrown when calling GetTimeZonesForRegions...: {ex.Message}");
                geoDataCollection = GeoData.StaticGeoDataCollection;
                Logger.LogInformation($"GetLatestTimeZonesForRegions: Regions Refresh: Regions GeoData Collection loaded with (STATIC) {geoDataCollection.Count} items");
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
        //internal static async Task GetHostsForRegion(string regionKey)
        internal static async Task GetHostsForRegion(string regionKey)
        {
            var message = "";
            Logger.LogInformation($"GetHostsForRegion: Retrieving hosts for region {regionKey}.");
            var regionRec = Live.regionLookup[regionKey];
            Logger.LogInformation($"GetHostsForRegion: Calling GetHostsForRegionKey with key = '{regionKey}");

            HttpResponseMessage response = new HttpResponseMessage();
            string getHostsForRegionUrl = $"https://{Common.kConnectAPIHostname}/api/v1/servers/hostnames-for-region";
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
                    message = $"RegionUtils.GetHostForRegion: Added {regionHosts.Count} hosts for region '{regionKey}'";
                    Logger.LogInformation(message);
                }
                else
                {
                    message = $"RegionUtils.GetHostForRegion: ResponseCode for getting region hosts for region '{regionKey}': {response.StatusCode}";
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

#if NOTNEEDEDNOW
        internal static RegionalHostRecord GetMyRegionHostRecord(string myRegionKey)
        {
            // CHANGE!! Allow for exception/ add ErrorResponse
            if (Alternate._hostLookup.Count == 0)
            {
                //throw new Exception($"Hosts Lookup collection is NOT loaded!");
                GetHostsForRegion(myRegionKey).GetAwaiter().GetResult();
            }

            if (!Alternate._hostLookup.TryGetValue(myRegionKey, out var myRegionRecord))
            {
                throw new Exception($"Hosts Lookup collection does NOT contain record for region {myRegionKey}");
            }

            //var myHost = myRegionRecord[0].Hostname;
            //return myRegionRecord[0];
            return SelectBestHostInRegion(myRegionKey);
        }
#endif

        internal static RegionalHostRecord SelectBestHostInRegion(string regionKey)
        {
            RegionHostsRetrievalWaiter.Reset();
            if (!Live._hostLookup.ContainsKey(regionKey) || Live._hostLookup[regionKey].Count == 0)
            {
                Logger.LogInformation($"RegionUtils.SelectBestHostInRegion: Region '{regionKey}' needs host list refresh... calling GetHostsForRegion to update now");
                Task.Factory.StartNew(async () =>
                {
                    GetHostsForRegion(regionKey);
                });
                Logger.LogInformation("RegionUtil.SelectBestHostInRegion: Waiting for GetHostsForRegion to return results...");
                RegionHostsRetrievalWaiter.Wait(5 * 1000);
                
                Logger.LogInformation( $"RegionUtils.SelectBestHostInRegion: Return from GetHostsForRegion - region '{regionKey}' host list refresh complete.");
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
    }
#endregion
}
