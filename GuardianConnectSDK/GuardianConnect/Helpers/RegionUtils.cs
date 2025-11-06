using GuardianConnect.API;
using GuardianConnect.API.Model;
using GuardianConnect.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GuardianConnect.Helpers
{
    public static class RegionUtils
    {
        private static Microsoft.Extensions.Logging.ILogger _logger = NullLogger.Instance;
        public static Microsoft.Extensions.Logging.ILogger Logger
        {
            get
            {
                if (_logger == NullLogger.Instance)
                {
                    _logger = StaticLoggerFactory.CreateLogger("RegionUtils");
                }
                return _logger;
            }
        }


        private static Dictionary<string, List<string>> timezonesLookup = new();
        private static Dictionary<string, GRDRegion> regionLookup = new();
        private static Dictionary<string, List<RegionalHostRecord>> _hostLookup = new();

        const string GetTimeZonesForRegionsUrl = $"https://{Common.kConnectAPIHostname}/api/v1.1/servers/timezones-for-regions";
        const string GetAllRegionsUrl = $"https://{Common.kConnectAPIHostname}/api/v1/servers/all-server-regions";

        public static List<string> RegionKeys { get; private set; } = new();
        public static Dictionary<string, string> RegionKeysByDisplay = new();
        public static string? RegionForOurActualLocation { get; set; } = null;
        public static string? KeyForCurrentlySelectedRegion { get; set; }


        public static async Task RefreshDataAsync()
        {
            _logger.LogInformation("RefreshDataAsync [0.22.19.1234]: 1. calling GetLatestRegionsList()...");
            var regions = await GetLatestRegionsList();
            _logger.LogInformation("RefreshDataAsync: 2. calling GetLatestTimeZonesForRegions()...");
            var geoDataCollection = GetLatestTimeZonesForRegions().GetAwaiter().GetResult();

            _logger.LogInformation($"Regions Collection has {regionLookup.Count} region records");
            _logger.LogInformation($"Timezone Collection has {timezonesLookup.Count} timezone records");

            _logger.LogInformation("Region Collection refreshed.");
        }

        private static async Task<List<GRDRegion>> GetLatestRegionsList()
        {
            _logger.LogInformation("GetLatestRegionsList() executing...");
            string errorMessage = string.Empty;
            int responseCode = 0;

            var regionsList = new List<GRDRegion>();

            Uri uri = new Uri(GetAllRegionsUrl);
            try
            {
                _logger.LogInformation("Getting latest Regions collection from backend...[0.22.19.1234]");
                {
                    HttpResponseMessage
                        response = HttpUtils.Client.GetAsync(uri).GetAwaiter().GetResult(); // Task short-circuit jump
                    if (response.IsSuccessStatusCode)
                    {
                        _logger.LogInformation($"GetLatestRegionsList(): Return from getting regions: Response statusCode = {response.StatusCode}");
                        string content = await response.Content.ReadAsStringAsync(); // Task short-circuit jump
                        if (string.IsNullOrEmpty(content))
                        {
                            _logger.LogInformation("GetLatestRegionsList: content returned for regions is empty");
                        }
                        else
                        {
                            var jsonOptions = new JsonSerializerOptions
                            {
                                AllowOutOfOrderMetadataProperties = true,
                                AllowTrailingCommas = true,
                                DefaultIgnoreCondition = JsonIgnoreCondition.Never,
                            };
                            regionsList = JsonSerializer.Deserialize<List<GRDRegion>>(content, GRDRegionJsonContext.Default.ListGRDRegion);
                            _logger.LogInformation($"GetLatestRegionsList: Regions Collection loaded with (ACTUAL) {regionsList.Count} items");
                            if (string.IsNullOrEmpty(regionsList[0].RegionName))
                            {
                                _logger.LogCritical( "!!!!!!!!!!!!!!!!!!! AOT/JSON BUG - INDIVIDUAL GRDRegion objects parsed empty !!!!!!!!!!!!!!!!");
                                Poof();
                            }
                            else
                            {
#if DEBUG
                                _logger.LogDebug($"GetLatestRegionsList: (ACTUAL) Region[0] '{regionsList[0].RegionName}' has display name '{regionsList[0].DisplayName}'");
                                foreach (var region in regionsList)
                                {
                                    _logger.LogInformation( $"GetLatestRegionsList: (ACTUAL) Region '{region.RegionName}' has display name '{region.DisplayName}'");
                                }
#endif
                            }
                        }

                        responseCode = (int)response.StatusCode;
                    }
                    else
                    {
                        _logger.LogInformation($"GetLatestRegionsList: Response from attempting to get latest regions is {response.StatusCode}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"GetLatestRegionsList(): Exception thrown when calling all-server-regions...: {ex.Message}. (STATIC) Using GRDRegion.StaticRegions list data");
                regionsList = GRDRegion.StaticRegions;
            }

            // First - clear out existing lookup collections
            RegionKeys = new List<string>();
            RegionKeysByDisplay = new Dictionary<string, string>();
            regionLookup = new Dictionary<string, GRDRegion>();

            // Now populate region lookup collections
            RegionKeysByDisplay.TryAdd("Automatic", "Automatic");
            _logger.LogInformation($"regionLookup pre-load has {regionLookup.Count} items.");
            var rluKeys = String.Join(',', regionLookup.Keys);
            _logger.LogDebug($"regionLookup dictionary keys are: '{rluKeys}");
            foreach (var regionRec in regionsList.OrderBy(region => region.DisplayName))
            {
                if (!regionLookup.TryAdd(regionRec.RegionName, regionRec))
                {
                    _logger.LogError($"GetLatestRegionsList: Failed to add region name/pretty-name to regionlookup dictionary for '{regionRec.RegionName}' using TryAdd");
                    try
                    {
                        regionLookup.Add(regionRec.RegionName, regionRec);
                        _logger.LogInformation($"GetLatestRegionsList: SUCCESS in adding region '{regionRec.RegionName}' to regionLookup collection.");
                    }
                    catch (Exception e)
                    {
                        _logger.LogCritical(e, $"GetLatestRegionsList: FATAL - Could not add region '{regionRec.RegionName}' object to regionLookup collection!");
                        Poof();
                    }
                }
                RegionKeys.Add(regionRec.RegionName);
                RegionKeysByDisplay.TryAdd(regionRec.DisplayName, regionRec.RegionName);
            }

            return regionsList;
        }

        private static void Poof()
        {
            //Log.CloseAndFlush();
            Environment.Exit(-1);
        }

        private static async Task<List<GeoData>> GetLatestTimeZonesForRegions()
        {
            _logger.LogInformation("GetLatestTimeZonesForRegions[0.22.19.1234]() executing...");
            string errorMessage = string.Empty;

            var geoDataCollection = new List<GeoData>();

            Uri uri = new Uri(GetTimeZonesForRegionsUrl);
            try
            {
                _logger.LogInformation("GetLatestTimeZonesForRegions: Getting time zones for regions from backend...[0.22.19.1234]");
                HttpResponseMessage?
                    response = HttpUtils.Client?.GetAsync(uri).GetAwaiter().GetResult(); // Task short-circuit jump
                if (response != null && response.IsSuccessStatusCode)
                {
                    string content = await response.Content.ReadAsStringAsync(); // Task short-circuit jump
                    geoDataCollection = JsonSerializer.Deserialize<List<GeoData>>(content, GeoDataJsonContext.Default.ListGeoData);
                    _logger.LogInformation($"GetLatestTimeZonesForRegions: Regions Refresh: Regions GeoData Collection loaded with (ACTUAL) {geoDataCollection.Count} items");
                }
                else
                {
                    if (response != null)
                    {
                        errorMessage = $"GetLatestTimeZonesForRegions: ResponseCode for getting latest regions collection: {response.StatusCode}";
                        _logger.LogError(errorMessage);
                    }
                    else
                    {
                        _logger.LogError($"Response from server at uri '{uri}' to get latest regions returned NULL");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"GetLatestTimeZonesForRegions(): Exception thrown when calling GetTimeZonesForRegions...: {ex.Message}");
                geoDataCollection = GeoData.StaticGeoDataCollection;
                _logger.LogInformation($"GetLatestTimeZonesForRegions: Regions Refresh: Regions GeoData Collection loaded with (STATIC) {geoDataCollection.Count} items");
            }

            _logger.LogInformation($"GetLatestTimeZonesForRegions: now populating timezonesLookup dictionary with {geoDataCollection.Count} entries...");
            timezonesLookup = new Dictionary<string, List<string>>();
            foreach (var geoRec in geoDataCollection)
            {
                _logger.LogDebug($"GetLatestTimeZonesForRegions: Adding '{geoRec.KeyName}' with {geoRec.Timezones.Count} timezones");
                if (timezonesLookup.TryAdd(geoRec.KeyName, geoRec.Timezones) == false)
                {
                    _logger.LogWarning($"GetLatestTimeZonesForRegions: Could not add timezones for region key '{geoRec.KeyName}");
                }
            }

            return geoDataCollection;
        }


        public static bool LookUpRegionIndexForMyTimeZone(string ourTimeZoneId, out string myRegionKey)
        {
            string containingKey = string.Empty;
            myRegionKey = "us-east"; // default

            _logger.LogInformation($"LookUpRegionIndexForMyTimeZone [0.22.19.1234] Our time zone ID = '{ourTimeZoneId}'");
            var ourKey = "us-east";
            _logger.LogInformation($"timezonesLookup.Keys.Count = {timezonesLookup.Keys.Count}.");

            try
            {
                containingKey = timezonesLookup.Keys
                    .Where(key => timezonesLookup[key].Contains(ourTimeZoneId))
                    .FirstOrDefault() ?? throw new InvalidOperationException();
                myRegionKey = containingKey;

            }
            catch (Exception e)
            {
                if (e is InvalidOperationException ioe)
                {
                    myRegionKey = string.IsNullOrEmpty(containingKey) ? "us-east" : containingKey;
                    _logger.LogWarning(ioe,
                        $"LookUPRegionIndexForMyTimeZone: Defaulting to 'us-east' as timezone not found in timezonesLookup collection!");
                    _logger.LogWarning("LookUpReginoIndexForMyTimeZone: Dumping timezonesLookup collection...");
                }
                else
                {
                    _logger.LogError(e,
                        $"LookUpRegionIndexForMyTimeZone: Exception thrown when looking up region for timezone '{ourTimeZoneId}': {e.Message}");
                    containingKey = "us-east";
                    myRegionKey = containingKey;
                }
            }

            return string.IsNullOrEmpty(containingKey);
        }

        public static string GetRegionKeyByDisplayName(string pn)
        {
            if (pn == "Automatic") return RegionForOurActualLocation ?? string.Empty;
            return regionLookup.Values.First(v => v.DisplayName == pn).RegionName;
        }

        public static string GetRegionPrettyName(string regionKey)
        {
            return regionLookup[regionKey].DisplayName;
        }

        public static async Task GetHostsForRegion(string keyName)
        {
            var regionRec = regionLookup[keyName];
            await GetHostsForRegionKey(regionRec.RegionName);
            int hostCount = _hostLookup[regionRec.RegionName].Count;
            var message = $"GetHostsForRegion(): Getting latest collection of hosts for Region {regionRec.RegionName} - {regionRec.DisplayName}. Number of hosts = {hostCount}";
            _logger.LogInformation(message);
        }

        // Get hosts for a region
        public static async Task GetHostsForRegionKey(string regionKey)
        {
            _logger.LogInformation($"GetHostsForRegionKey: Retrieving hosts for region {regionKey}.");

            HttpResponseMessage response = new HttpResponseMessage();
            string getHostsForRegionUrl = $"https://{Common.kConnectAPIHostname}/api/v1/servers/hostnames-for-region";
            Uri uri = new Uri(getHostsForRegionUrl);
            try
            {
                RegionInputParameter rip = new RegionInputParameter();
                if (!regionLookup.ContainsKey(regionKey)) return;

                rip.Region = regionLookup[regionKey].RegionName;
                _logger.LogInformation("About to do GET for Region Hosts collection retrieval");
                string ripSerialized = JsonSerializer.Serialize(rip, RegionInputParameterJsonContext.Default.RegionInputParameter);
                _logger.LogInformation($"GetHostsForRegionKey: Json string for RegionInputParameter '{ripSerialized}'");
                HttpContent content = new StringContent(ripSerialized);
                content.Headers.Remove("Content-Type");
                content.Headers.Add("Content-Type", "application/json; charset=utf-8");

                try
                {
                    response = HttpUtils.Client?.PostAsync(uri, content).GetAwaiter().GetResult() ?? throw new InvalidOperationException();
                }
                catch (HttpRequestException hrex)
                {
                    _logger.LogError(hrex, $"FAILURE: HTTP REQUEST EXCEPTION - Failed to get hosts for region {regionKey}. StatusCode={hrex.StatusCode}, Error={hrex.HttpRequestError}");
                    throw;
                }
                catch (Exception e)
                {
                    _logger.LogError(e, $"Failed to get hosts for region {regionKey}");
                    throw;
                }

                if (response.IsSuccessStatusCode)
                {
                    string respContent = await response.Content.ReadAsStringAsync();
                    List<RegionalHostRecord> regionHosts = JsonSerializer.Deserialize<List<RegionalHostRecord>>(respContent, RegionalHostRecordJsonContext.Default.ListRegionalHostRecord);
                    if (!_hostLookup.ContainsKey(regionKey)) _hostLookup.Add(regionKey, null);
                    _hostLookup[regionKey] = regionHosts;
                    var message = $"RegionUtils.GetHostForRegion [0.22.19.1234]: Added {regionHosts.Count} hosts for region '{regionKey}'";
                    _logger.LogInformation(message);
                }
                else
                {
                    var message = $"RegionUtils.GetHostForRegion: ResponseCode for getting region hosts for region '{regionKey}': {response.StatusCode}";
                    _logger.LogInformation(message);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, @"\tERROR {0}", ex.Message);
            }
        }

        public static RegionalHostRecord GetMyRegionHostRecord(string myRegionKey)
        {
            // CHANGE!! Allow for exception/ add ErrorResponse
            if (_hostLookup.Count == 0)
            {
                //throw new Exception($"Hosts Lookup collection is NOT loaded!");
                GetHostsForRegion(myRegionKey).GetAwaiter().GetResult();
            }

            if (!_hostLookup.TryGetValue(myRegionKey, out var myRegionRecord))
            {
                throw new Exception($"Hosts Lookup collection does NOT contain record for region {myRegionKey}");
            }

            //var myHost = myRegionRecord[0].Hostname;
            //return myRegionRecord[0];
            return SelectBestHostInRegion(myRegionKey);
        }

        public static RegionalHostRecord SelectBestHostInRegion(string regionKey)
        {
            if (_hostLookup.Count == 0)
            {
                GetHostsForRegion(regionKey).GetAwaiter().GetResult();
            }

            if (!_hostLookup.TryGetValue(regionKey, out var myRegionRecord))
            {
                throw new Exception($"Hosts Lookup collection does NOT contain record for region {regionKey}");
            }

            // Do random thing
            var regionHosts = _hostLookup[regionKey];
            var lightest = regionHosts.Where(h => h.CapacityScore == 0);
            var lighter = regionHosts.Where(h => h.CapacityScore == 1);

            _logger.LogInformation($"SelectBestHostInRegion: For region '{regionKey}' we have {lightest.Count()} lightest hosts, {lighter.Count()} midrange hosts out of {regionHosts.Count()} total hosts");

            if (lightest != null && lightest.Count() > 0)
                return lightest.ElementAt(Random.Shared.Next(lightest.Count() - 1));

            if (lighter != null && lighter.Count() > 0)
                return lighter.ElementAt(Random.Shared.Next(lighter.Count() - 1));

            return regionHosts.ElementAt(Random.Shared.Next(regionHosts.Count - 1));
        }
    }
}
