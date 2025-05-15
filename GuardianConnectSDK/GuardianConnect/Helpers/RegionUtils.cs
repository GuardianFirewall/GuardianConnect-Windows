using GuardianConnect.API;
using GuardianConnect.API.Model;
using GuardianConnect.Shared;
using Newtonsoft.Json;
using Serilog;

namespace GuardianConnect.Helpers
{
    public static class RegionUtils
    {
        private static Dictionary<string, List<string>> timezonesLookup = new();
        private static Dictionary<string, GRDRegion> regionLookup = new();
        private static Dictionary<string, List<RegionalHostRecord>> _hostLookup = new();
        
        const string GetTimeZonesForRegionsUrl = $"https://{Common.kConnectAPIHostname}/api/v1.1/servers/timezones-for-regions";
        const string GetAllRegionsUrl = $"https://{Common.kConnectAPIHostname}/api/v1/servers/all-server-regions";

        public static List<string> RegionKeys { get; } = new();
        public static Dictionary<string, string> RegionKeysByDisplay = new();
        public static string? RegionForOurActualLocation { get; set; } = null;
        public static string? KeyForCurrentlySelectedRegion { get; set; }

        public static string GetRegionKeyByDisplayName(string pn)
        {
            if (pn == "Automatic") return RegionForOurActualLocation ?? string.Empty;
            return regionLookup.Values.First(v => v.DisplayName == pn).RegionName;
        }

        public static string GetRegionPrettyName(string regionKey)
        {
            return regionLookup[regionKey].DisplayName;
        }

        public static bool LookUpRegionIndexForMyTimeZone(string ourTimeZoneId, out string myRegionKey)
        {
            Log.Information($"LookUpRegionIndexForMyTimeZone: Our time zone ID = '{ourTimeZoneId}'");
            string containingKey = timezonesLookup.Keys
                .Where(key => timezonesLookup[key].Contains(ourTimeZoneId))
                .FirstOrDefault() ?? throw new InvalidOperationException();
            myRegionKey = string.IsNullOrEmpty(containingKey) ? "us-east" : containingKey;

            return string.IsNullOrEmpty(containingKey);
        }

        private static async Task<List<GRDRegion>> GetLatestRegionsList()
        {
            Log.Information("GetLatestRegionsList() executing...");
            string errorMessage = string.Empty;
            int responseCode = 0;

            var regionsList = new List<GRDRegion>();

            Uri uri = new Uri(GetAllRegionsUrl);
            try
            {
                Log.Information("GET'ing latest Regions collection from backend...");
                {
                    HttpResponseMessage
                        response = HttpUtils.Client.GetAsync(uri).GetAwaiter().GetResult(); // Task short-circuit jump
                    if (response.IsSuccessStatusCode)
                    {
                        string content = await response.Content.ReadAsStringAsync(); // Task short-circuit jump
                        regionsList = JsonConvert.DeserializeObject<List<GRDRegion>>(content) ?? new List<GRDRegion>();
                        Log.Information($"Regions Refresh: Regions Collection loaded with {regionsList.Count} items");
                        responseCode = (int)response.StatusCode;
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Information(
                    $"GetLatestRegionsList(): Exception thrown when calling all-server-regions...: {ex.Message}");
                regionsList = new List<GRDRegion>();
            }

            RegionKeysByDisplay.TryAdd("Automatic", "Automatic");
            foreach (var regionRec in regionsList.OrderBy(region => region.DisplayName))
            {
                regionLookup.TryAdd(regionRec.RegionName, regionRec);
                RegionKeys.Add(regionRec.RegionName);
                RegionKeysByDisplay.TryAdd(regionRec.DisplayName, regionRec.RegionName);
            }

            return regionsList;
        }

        private static async Task<List<GeoData>> GetLatestTimeZonesForRegions()
        {
            Log.Information("RefreshDataAsync() executing...");
            string errorMessage = string.Empty;

            var geoDataCollection = new List<GeoData>();

            Uri uri = new Uri(GetTimeZonesForRegionsUrl);
            try
            {
                Log.Information("GET'ing latest Regions collection from backend...");
                HttpResponseMessage?
                    response = HttpUtils.Client?.GetAsync(uri).GetAwaiter().GetResult(); // Task short-circuit jump
                if (response != null && response.IsSuccessStatusCode)
                {
                    string content = await response.Content.ReadAsStringAsync(); // Task short-circuit jump
                    geoDataCollection = JsonConvert.DeserializeObject<List<GeoData>>(content) ?? new List<GeoData>();
                    Log.Information(
                        $"Regions Refresh: Regions GeoData Collection loaded with {geoDataCollection.Count} items");
                }
            }
            catch (Exception ex)
            {
                Log.Information(
                    $"RefreshDataAsync(): Exception thrown when calling GetTimeZonesForRegions...: {ex.Message}");
                geoDataCollection = new List<GeoData>();
            }

            foreach (var geoRec in geoDataCollection)
            {
                Log.Verbose( $"GetLatestGeoData: Adding '{geoRec.KeyName}' with {geoRec.Timezones.Count} timezones");
                timezonesLookup.TryAdd(geoRec.KeyName, geoRec.Timezones);
            }

            return geoDataCollection;
        }

        public static async Task RefreshDataAsync()
        {
            var regions = await GetLatestRegionsList();
            var geoDataCollection = GetLatestTimeZonesForRegions().GetAwaiter().GetResult();

            Log.Information($"Regions Collection has {regionLookup.Count} region records"); 
            Log.Information($"Timezone Collection has {timezonesLookup.Count} timezone records"); 
            
            Log.Information("Region Collection refreshed.");
        }

        public static async Task GetHostsForRegion(string keyName)
        {
            var regionRec = regionLookup[keyName];
            await GetHostsForRegionKey(regionRec.RegionName);
            int hostCount = _hostLookup[regionRec.RegionName].Count;
            var message = $"GetHostsForRegion(): Getting latest collection of hosts for Region {regionRec.RegionName} - {regionRec.DisplayName}. Number of hosts = {hostCount}";
            Log.Information(message);
        }

        // Get hosts for a region
        public static async Task GetHostsForRegionKey(string regionKey)
        {
            Log.Information($"GetHostsForRegionKey: Retrieving hosts for region {regionKey}.");

            HttpResponseMessage response = new HttpResponseMessage();
            string getHostsForRegionUrl = $"https://{Common.kConnectAPIHostname}/api/v1/servers/hostnames-for-region";
            Uri uri = new Uri(getHostsForRegionUrl);
            try
            {
                RegionInputParameter rip = new RegionInputParameter();
                if (!regionLookup.ContainsKey(regionKey)) return;

                rip.Region = regionLookup[regionKey].RegionName;
                Log.Information("About to do GET for Region Hosts collection retrieval");
                HttpContent content = new StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(rip));
                content.Headers.Remove("Content-Type");
                content.Headers.Add("Content-Type", "application/json; charset=utf-8");

                try
                {
                    response = HttpUtils.Client?.PostAsync(uri, content).GetAwaiter().GetResult() ?? throw new InvalidOperationException();
                }
                catch (HttpRequestException hrex)
                {
                    Log.Error(hrex, $"FAILURE: HTTP REQUEST EXCEPTION - Failed to get hosts for region {regionKey}. StatusCode={hrex.StatusCode}, Error={hrex.HttpRequestError}");
                    throw;
                }
                catch (Exception e)
                {
                    Log.Error(e, $"Failed to get hosts for region {regionKey}");
                    throw;
                }

                if (response.IsSuccessStatusCode)
                {
                    string respContent = await response.Content.ReadAsStringAsync();
                    List<RegionalHostRecord> regionHosts = JsonConvert.DeserializeObject<List<RegionalHostRecord>>(respContent);
                    if (!_hostLookup.ContainsKey(regionKey)) _hostLookup.Add(regionKey, null);
                    _hostLookup[regionKey] = regionHosts;
                    var message = $"RegionUtils.GetHostForRegion: Added {regionHosts.Count} hosts for region '{regionKey}'";
                    Log.Information(message);
                }
                else
                {
                    var message = $"RegionUtils.GetHostForRegion: ResponseCode for getting region hosts for region '{regionKey}': {response.StatusCode}";
                    Log.Information(message);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, @"\tERROR {0}", ex.Message);
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

            Log.Information($"SelectBestHostInRegion: For region '{regionKey}' we have {lightest.Count()} lightest hosts, {lighter.Count()} midrange hosts out of {regionHosts.Count()} total hosts");

            if (lightest != null && lightest.Count() > 0)
                return lightest.ElementAt(Random.Shared.Next(lightest.Count()-1));

            if (lighter != null && lighter.Count() > 0)
                return lighter.ElementAt(Random.Shared.Next(lighter.Count()-1));

            return regionHosts.ElementAt(Random.Shared.Next(regionHosts.Count-1));
        }
    }
}
