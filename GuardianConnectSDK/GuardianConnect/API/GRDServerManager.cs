using System.Text.Json;
using System.Text.Json.Serialization;
using GuardianConnect.API.Model;
using GuardianConnect.Helpers;
using GuardianConnect.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GuardianConnect.API;

public class GRDServerManager
{
    private static ILogger _logger = NullLogger.Instance;

    public GRDVPNHelper.GRDServerFeatureEnvironment FeatureEnv;

    /// Feature environment and beta preference for the host-list request.
    /// GetHostsForRegion is static while FeatureEnv / BetaCapable are instance
    /// members, so these carry the same values the constructor assigns. Nothing
    /// overrides those at runtime today; if that changes, thread the instance
    /// values through instead of widening these.
    private static readonly GRDVPNHelper.GRDServerFeatureEnvironment HostRequestFeatureEnvironment =
        GRDVPNHelper.GRDServerFeatureEnvironment.ServerFeatureEnvironmentProduction;

    private const bool HostRequestBetaCapable = false;

    public GRDServerManager()
    {
        FeatureEnv = GRDVPNHelper.GRDServerFeatureEnvironment.ServerFeatureEnvironmentProduction;
        BetaCapable = false;
    }

    private static ILogger Logger
    {
        get
        {
            if (_logger == NullLogger.Instance)
            {
                _logger = StaticLoggerFactory.CreateLogger("GRDServerManager");
            }

            return _logger;
        }
    }

    public bool BetaCapable { get; set; }
    private static GRDRegion? SelectedRegion { get; set; }

    /// Finds and returns the VPN server node we will connect to for the given region.
    /// Returns the full <see cref="GRDSGWServer"/> so the connect flow can carry the
    /// selected server as one object rather than re-flattening to loose
    /// hostname/display strings. Callers read <c>.Hostname</c> / <c>.HostLocation()</c>
    /// at the point of use.
    public static (GRDSGWServer, ErrorResponse) SelectGuardianHostWithCompletion(string? selectedRegionKey)
    {
        SelectedRegion = GetGRDRegionByKey(selectedRegionKey ?? GetRegionForOurTimeZone());

        Logger.LogInformation(
            $"GRDServerManager.SelectGuardianHostWithCompletion: Calling SelectBestHostInRegion for region '{SelectedRegion.RegionName}'");
        var regionHostRecord = SelectBestHostInRegion(SelectedRegion.RegionName);

        return (regionHostRecord, new ErrorResponse());
    }

    #region GRDServerManager private stuff

    private static int _latest = 1;
    private static int Active;
    private static int Standby => Active ^ 1;

    private static readonly Dictionary<int, GRDRegionCache> _geoInfoCaches = new()
    {
        { 0, new GRDRegionCache() },
        { 1, new GRDRegionCache() }
    };


    private static GRDRegionCache Live => _geoInfoCaches[Active];
    private static GRDRegionCache Alternate => _geoInfoCaches[Standby];
    private static DateTime LastUpdateChangeTime;
    private static readonly ManualResetEventSlim RegionHostsRetrievalWaiter = new();

    #endregion

    #region region loading and collections

    // Caller can set the refresh interval. Defaulting to 1 hour.
    public static TimeSpan TimeSpanBetweenEachGeoRefresh { get; set; } =
        new(1, 0, 0);

    public static ManualResetEventSlim InitialGeoInformationLoadComplete = new();

    #region public methods

    public static DateTime GetLastTimeUpdated()
    {
        return LastUpdateChangeTime;
    }

    public static async Task LongRunningRefreshTask(CancellationToken cancellationToken)
    {
        var RefreshMinutesStr =
            RegistrySettings.RetrieveGuardianUserSettings("MinutesBetweenGeoRefreshChecks");
        if (string.IsNullOrEmpty(RefreshMinutesStr))
            TimeSpanBetweenEachGeoRefresh = new TimeSpan(1, 0, 0); // 1 hour default
        else
            TimeSpanBetweenEachGeoRefresh = TimeSpan.FromMinutes(Convert.ToDouble(RefreshMinutesStr));

        await Task.Factory.StartNew(async () =>
        {
            Logger.LogInformation(
                $"GRDServerManager.LongRunningRefreshTask: Kicking off RefreshDataAsync task to run every {TimeSpanBetweenEachGeoRefresh} period");
            do
            {
                await RefreshDataAsync(); // sub-second - no need to pass cancellation token
                Logger.LogInformation("GRDServerManager.LongRunningRefreshTask: RefreshDataAsync completed. ");
                var changed = _geoInfoCaches[Standby].Checksum() != _geoInfoCaches[Active].Checksum();

                // Swap gate: promote the freshly-refreshed cache whenever it
                // differs from the active one. We intentionally do NOT gate on
                // region count — a valid 200 OK that returns an empty region list
                // is still promoted, so the empty result surfaces to the caller as
                // a visible symptom of a server-side fault rather than being masked
                // by retaining stale regions.
                if (changed)
                {
                    Logger.LogInformation(
                        "GRDServerManager.LongRunningRefreshTask: The latest refresh has changes. Toggling ACTIVE to point LIVE to newest data.");
                    Logger.LogInformation(
                        $"Pre-Switch: Latest(on index {Standby}): {Alternate.Checksum()}, Active (on index {Active}): {Live.Checksum()}");
                    SetActiveToLatest();
                    Logger.LogInformation(
                        $"Active Switched (to index {Active}): Latest (now on index {Standby}): {Alternate.Checksum()}, Active: {Live.Checksum()}");
                }

                InitialGeoInformationLoadComplete.Set();
                await Task.Delay(TimeSpanBetweenEachGeoRefresh, cancellationToken);
            } while (!cancellationToken.IsCancellationRequested);
        }, cancellationToken);
    }


    public static async Task RefreshDataAsync()
    {
        var startTime = DateTime.Now;
        InitializeAlternate();
        Logger.LogInformation("RefreshDataAsync: 1. calling RefreshStandbyRegionsList()...");
        var regions = await RefreshStandbyRegionsLists();
        Logger.LogInformation("RefreshDataAsync: 2. calling GetLatestTimeZonesForRegions()...");
        await GetLatestTimeZonesForRegions();
        Logger.LogInformation($"Latest Regions Collection has {Alternate.regionLookup.Count} region records");
        Logger.LogInformation($"Latest Timezone Collection has {Alternate.timezonesLookup.Count} timezone records");

        Alternate.ComputeHash();
        Logger.LogInformation($"Checksum : {_geoInfoCaches[1].Checksum()}");
        var endTime = DateTime.Now;
        Logger.LogInformation($"Region Collection refreshed. Checksum = {Alternate.Checksum()}");
        Logger.LogInformation(
            $"Total GRDServerManager.RefreshDataAsync execution time = {(endTime - startTime).TotalMilliseconds / 1000} seconds");
    }

    public static List<string> GetSortedRegionKeys()
    {
        return Live.RegionKeysByDisplay.Keys.ToList();
    }

    public static void SwapActiveGeoInfoCache()
    {
        Toggle();
        SetActiveToLatest();
        Logger.LogInformation("SwapActiveGeoInfoCache: Swapped active GeoInfoCache to latest.");
    }

    public static bool LookUpRegionIndexForMyTimeZone(string ourTimeZoneId, out string myRegionKey)
    {
        var containingKey = string.Empty;
        myRegionKey = "us-east"; // default

        Logger.LogInformation($"LookUpRegionIndexForMyTimeZone:  Our time zone ID = '{ourTimeZoneId}'");
        _ = "us-east"; // placeholder (formerly ourKey)
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
                    "LookUPRegionIndexForMyTimeZone: Defaulting to 'us-east' as timezone not found in timezonesLookup collection!");
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
        // Resilient lookup — a missing key must NEVER crash the connect flow.
        // A failed/empty regions refresh (e.g. a transient post-disconnect DNS
        // hiccup) can leave regionLookup degenerate, and region auto-pick
        // defaults to "us-east"; an unguarded indexer here threw
        // KeyNotFoundException out of SelectGuardianHostWithCompletion.
        if (!string.IsNullOrEmpty(regionKey) && Live.regionLookup.TryGetValue(regionKey, out var region))
            return region;

        // Try the Standby cache (last-good) for the same key, then any concrete
        // (non-Automatic) region, then Automatic as a final non-throwing default.
        if (!string.IsNullOrEmpty(regionKey) && Alternate.regionLookup.TryGetValue(regionKey, out var alt))
            return alt;

        var fallback = Live.regionLookup.Values.FirstOrDefault(r => r.RegionName != "Automatic")
                       ?? Alternate.regionLookup.Values.FirstOrDefault(r => r.RegionName != "Automatic");
        if (fallback != null)
        {
            Logger.LogWarning(
                $"GetGRDRegionByKey: region key '{regionKey}' not found; falling back to '{fallback.RegionName}'.");
            return fallback;
        }

        Logger.LogWarning(
            $"GetGRDRegionByKey: region key '{regionKey}' not found and no concrete regions loaded; returning Automatic.");
        return Live.regionLookup.TryGetValue("Automatic", out var auto)
            ? auto
            : new GRDRegion { RegionName = "Automatic", DisplayName = "Automatic" };
    }

    /// <summary>
    /// Looks up the region key (internal name) that owns the given hostname
    /// by scanning loaded host caches. Returns null if not found —
    /// typically means the host's region's host list hasn't been loaded
    /// yet. Caller should consider triggering a load first if it's
    /// expecting a result.
    /// </summary>
    public static string? FindRegionKeyForHostname(string hostname)
    {
        if (string.IsNullOrWhiteSpace(hostname)) return null;
        foreach (var kvp in Live._hostLookup)
        {
            if (kvp.Value.Any(h => string.Equals(h.Hostname, hostname, StringComparison.OrdinalIgnoreCase)))
                return kvp.Key;
        }
        return null;
    }

    /// <summary>
    /// Returns the cached GRDSGWServer for the given hostname, or
    /// null if not found across any loaded region's host list. Used by
    /// the WireGuard key-exchange flow to pull DisplayName for an
    /// override host.
    /// </summary>
    public static GRDSGWServer? FindHostRecord(string hostname)
    {
        if (string.IsNullOrWhiteSpace(hostname)) return null;
        foreach (var hosts in Live._hostLookup.Values)
        {
            var match = hosts.FirstOrDefault(h =>
                string.Equals(h.Hostname, hostname, StringComparison.OrdinalIgnoreCase));
            if (match != null) return match;
        }
        return null;
    }

    /// <summary>
    /// Resolves a host record for the given hostname, falling back to the
    /// all-hostnames endpoint when the local cache has no entry.
    /// <para>
    /// The cache is only populated as a side effect of host selection
    /// (SelectGuardianHostWithCompletion → SelectBestHostInRegion →
    /// GetHostsForRegion), which does not run when a connection dials with
    /// already-stored credentials. In a freshly started process that path leaves
    /// the cache empty, so a cache-only lookup returns null for a host we are
    /// actively connecting to. The remote list carries both smart-routing-enabled
    /// and the nested region, so one call recovers the full record.
    /// </para>
    /// Returns null if the hostname is absent from the remote list too.
    /// </summary>
    public static async Task<GRDSGWServer?> FindHostRecordResilient(string hostname)
    {
        if (string.IsNullOrWhiteSpace(hostname)) return null;

        var cached = FindHostRecord(hostname);
        if (cached != null) return cached;

        Logger.LogInformation(
            "FindHostRecordResilient: {Host} not in the local cache; querying all-hostnames.", hostname);
        try
        {
            var all = await GetAllHostnamesAsync();
            var match = all.FirstOrDefault(h =>
                string.Equals(h.Hostname, hostname, StringComparison.OrdinalIgnoreCase));
            if (match == null)
                Logger.LogWarning(
                    "FindHostRecordResilient: {Host} absent from all-hostnames ({Count} records).",
                    hostname, all.Count);
            return match;
        }
        catch (Exception e)
        {
            Logger.LogError(e, "FindHostRecordResilient: all-hostnames lookup for {Host} threw.", hostname);
            return null;
        }
    }

    #endregion

    #region private methods

    private static int Toggle()
    {
        return Interlocked.Exchange(ref _latest, _latest ^ 1);
    }

    private static void SetActiveToLatest()
    {
        Active = _latest;
        Toggle();
        LastUpdateChangeTime = DateTime.Now;
    }

    private static void InitializeAlternate()
    {
        _geoInfoCaches[Standby] = new GRDRegionCache();
    }


    private static async Task<List<GRDRegion>> RefreshStandbyRegionsLists()
    {
        Logger.LogInformation("RefreshStandbyRegionsLists() executing...");
        var errorResponse = new ErrorResponse();
        var errorMessage = string.Empty;
        var responseCode = 0;

        var regionsList = new List<GRDRegion>();
        try
        {
            errorResponse = await GRDHousekeepingAPI.RequestServerRegions();

            // One-time re-solicit. Branch on IsError / ThrownException — the
            // authoritative failure signal — NOT on HttpResponse.IsSuccessStatusCode:
            // a defaulted ErrorResponse carries a 200-OK HttpResponse, which
            // previously masked a failed call (e.g. a transient post-disconnect DNS
            // failure) as a successful EMPTY response. The first attempt can fail on
            // that transient, so retry once before falling back to last-good.
            if (errorResponse.IsError || errorResponse.ThrownException != null)
            {
                Logger.LogWarning(
                    $"RefreshStandbyRegionsLists: regions fetch failed ('{errorResponse.Message}'); re-soliciting once...");
                await Task.Delay(500);
                errorResponse = await GRDHousekeepingAPI.RequestServerRegions();
            }

            if (!errorResponse.IsError && errorResponse.ThrownException == null)
            {
                responseCode = (int)errorResponse.HttpResponse.StatusCode;
                var content = errorResponse.Data?.ToString() ?? string.Empty;
                if (string.IsNullOrEmpty(content))
                {
                    Logger.LogInformation("RefreshStandbyRegionsLists: content returned for regions is empty");
                }
                else
                {
                    Logger.LogInformation(
                        "RefreshStandbyRegionsLists: Successfully retrieved latest regions from backend.");
                    Alternate.contentstrings.Add(content);
                    regionsList =
                        JsonSerializer.Deserialize<List<GRDRegion>>(content,
                            GRDRegionJsonContext.Default.ListGRDRegion);
                    Logger.LogInformation(
                        $"RefreshStandbyRegionsLists: Regions Collection loaded with (ACTUAL) {regionsList?.Count} items");
                }
            }
            else
            {
                // Genuine failure after the re-solicit. Leave regionsList empty and
                // let the carry-forward below preserve the last-good regions rather
                // than building a degenerate cache.
                Logger.LogWarning(
                    $"RefreshStandbyRegionsLists: regions fetch still failing after re-solicit ('{errorResponse.Message}'); will carry forward last-good.");
            }
        }
        catch (Exception ex)
        {
            Logger.LogError(ex,
                $"RefreshStandbyRegionsLists(): Exception thrown processing all-server-regions response: {ex.Message}");
        }

        // If we couldn't fetch real regions (failed call or empty body), don't
        // build a degenerate Standby cache that a later swap would promote over
        // good Live data. That clobber is what corrupted region selection after
        // a post-disconnect DNS hiccup: regionLookup was left with only
        // "Automatic", so the "us-east" auto-pick threw KeyNotFoundException.
        // Carry forward the current Live regions as last-good instead.
        // (GRDRegion.StaticRegions is an empty list, so it is NOT a usable
        // fallback despite the log messages elsewhere.)
        if (regionsList == null || regionsList.Count == 0)
        {
            var carried = Live.regionLookup.Values
                .Where(r => r.RegionName != "Automatic")
                .ToList();
            Logger.LogWarning(
                $"RefreshStandbyRegionsLists: no regions fetched; carrying forward {carried.Count} last-good region(s) from the active cache.");
            regionsList = carried;
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
        Logger.LogInformation(
            $"RefreshStandbyRegionsLists: regionLookup pre-load has {Alternate.regionLookup.Count} items.");
        var rluKeys = string.Join(',', Alternate.regionLookup.Keys);
        Logger.LogDebug($"regionLookup dictionary keys are: '{rluKeys}");
        foreach (var regionRec in (regionsList ?? GRDRegion.StaticRegions).OrderBy(region => region.DisplayName))
        {
            if (!Alternate.regionLookup.TryAdd(regionRec.RegionName, regionRec))
            {
                Logger.LogError(
                    $"GetLatestRegionsList: Failed to add region name/pretty-name to regionlookup dictionary for '{regionRec.RegionName}' using TryAdd");
                try
                {
                    Alternate.regionLookup.Add(regionRec.RegionName, regionRec);
                    Logger.LogInformation(
                        $"GetLatestRegionsList: SUCCESS in adding region '{regionRec.RegionName}' to regionLookup collection.");
                }
                catch (Exception e)
                {
                    Logger.LogCritical(e,
                        $"GetLatestRegionsList: FATAL - Could not add region '{regionRec.RegionName}' object to regionLookup collection!");
                    throw;
                }
            }

            Alternate.RegionKeys.Add(regionRec.RegionName);
            Alternate.RegionKeysByDisplay.TryAdd(regionRec.DisplayName, regionRec.RegionName);
        }

        return regionsList ?? GRDRegion.StaticRegions;
    }

    private static async Task GetLatestTimeZonesForRegions()
    {
        Logger.LogInformation("GetLatestTimeZonesForRegions executing...");
        var errorMessage = string.Empty;

        var geoDataCollection = new List<GeoData>();

        var response = await GRDHousekeepingAPI.RequestLatestTimeZonesForRegions();

        if (response.IsError || response.ThrownException != null)
        {
            // Transient failure — do NOT clobber good timezone data with the empty
            // GeoData.StaticGeoDataCollection (which is what blanked timezonesLookup,
            // forcing every user to the 'us-east' default). Carry forward the
            // current Live timezones as last-good and bail.
            Logger.LogWarning(
                $"GetLatestTimeZonesForRegions: fetch failed ('{response.Message}'); carrying forward {Live.timezonesLookup.Count} last-good timezone entr(ies).");
            Alternate.timezonesLookup = new Dictionary<string, List<string>>(Live.timezonesLookup);
            return;
        }

        Logger.LogInformation(
            "GetLatestTimeZonesForRegions: Successfully retrieved latest timezones for regions from backend.");
        var content = response.Data?.ToString() ?? string.Empty;
        geoDataCollection = string.IsNullOrEmpty(content)
            ? new List<GeoData>()
            : JsonSerializer.Deserialize<List<GeoData>>(content, GeoDataJsonContext.Default.ListGeoData)
              ?? new List<GeoData>();

        if (geoDataCollection.Count == 0)
        {
            // Empty/unparseable payload — carry forward rather than blanking.
            Logger.LogWarning(
                $"GetLatestTimeZonesForRegions: empty/unparseable timezone payload; carrying forward {Live.timezonesLookup.Count} last-good entr(ies).");
            Alternate.timezonesLookup = new Dictionary<string, List<string>>(Live.timezonesLookup);
            return;
        }

        Alternate.contentstrings.Add(content);
        Logger.LogInformation(
            $"GetLatestTimeZonesForRegions: Timezones Collection loaded with (ACTUAL) {geoDataCollection.Count} items; populating timezonesLookup...");
        Alternate.timezonesLookup = new Dictionary<string, List<string>>();
        foreach (var geoRec in geoDataCollection)
        {
            if (!Alternate.timezonesLookup.TryAdd(geoRec.KeyName, geoRec.Timezones))
                Logger.LogWarning(
                    $"GetLatestTimeZonesForRegions: Could not add timezones for region key '{geoRec.KeyName}'");
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

        var response = new HttpResponseMessage();
        var getHostsForRegionUrl = $"https://{Common.DefaultConnectAPIHostname}/api/v1.3/servers/hostnames-for-region";
        var uri = new Uri(getHostsForRegionUrl);
        try
        {
            // v1.3 rather than v1: only v1.3 returns "smart-routing-enabled" and
            // the nested "region" object per host. On v1 both are absent, so
            // GRDSGWServer.SmartProxyRoutingEnabled deserialized to the bool
            // default of false and Smart Routing Proxy could never engage.
            var rip = new RegionInputParameter
            {
                Region = regionRec.RegionName,
                Paid = true,
                FeatureEnvironment = (int)HostRequestFeatureEnvironment,
                BetaCapable = HostRequestBetaCapable,
                RegionPrecision = Common.kRegionPrecisionDefault,
            };

            Logger.LogInformation("About to do GET for Region Hosts collection retrieval");
            var ripSerialized =
                JsonSerializer.Serialize(rip, RegionInputParameterJsonContext.Default.RegionInputParameter);
            HttpContent content = new StringContent(ripSerialized);
            content.Headers.Remove("Content-Type");
            content.Headers.Add("Content-Type", "application/json; charset=utf-8");

            try
            {
                response = HttpUtils.Client?.PostAsync(uri, content).GetAwaiter().GetResult() ??
                           throw new InvalidOperationException();
            }
            catch (HttpRequestException hrex)
            {
                Logger.LogError(hrex,
                    $"FAILURE: HTTP REQUEST EXCEPTION - Failed to get hosts for region {regionKey}. StatusCode={hrex.StatusCode}, Error={hrex.HttpRequestError}");
                throw;
            }
            catch (Exception e)
            {
                Logger.LogError(e, $"Failed to get hosts for region {regionKey}");
                throw;
            }

            if (response.IsSuccessStatusCode)
            {
                var respContent = await response.Content.ReadAsStringAsync();
                var regionHosts = JsonSerializer.Deserialize<List<GRDSGWServer>>(respContent,
                    GRDSGWServerJsonContext.Default.ListGRDSGWServer)
                    ?? new List<GRDSGWServer>();

                // Populate the GRDSGWServer-style region back-ref. The per-region
                // host-list endpoint omits the nested "region" object (region is the
                // query context), so stamp it from the known region key. Use ??= so we
                // never clobber a region the JSON did provide (servers/all-hostnames).
                var region = GetGRDRegionByKey(regionKey);
                foreach (var h in regionHosts) h.Region ??= region;

                if (!Live._hostLookup.ContainsKey(regionKey)) Live._hostLookup.Add(regionKey, null!);
                Live._hostLookup[regionKey] = regionHosts;
                message =
                    $"GRDServerManager.GetHostForRegion: Added {regionHosts?.Count} hosts for region '{regionKey}'";
                Logger.LogInformation(message);
            }
            else
            {
                message =
                    $"GRDServerManager.GetHostForRegion: ResponseCode for getting region hosts for region '{regionKey}': {response.StatusCode}";
                Logger.LogInformation(message);
            }

            var hostCount = Live._hostLookup[regionRec.RegionName].Count;
            message =
                $"GetHostsForRegion(): Getting latest collection of hosts for Region {regionRec.RegionName} - {regionRec.DisplayName}. Number of hosts = {hostCount}";
            Logger.LogInformation(message);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, @"\tERROR {0}", ex.Message);
        }

        RegionHostsRetrievalWaiter.Set();
    }

    public static async Task<List<GRDSGWServer>> GetAllHostnamesAsync()
    {
        var url = $"https://{Common.DefaultConnectAPIHostname}/api/v1.1/servers/all-hostnames";
        var uri = new Uri(url);

        try
        {
            Logger.LogInformation("GetAllHostnamesAsync: GET {Url}", url);
            var response = await (HttpUtils.Client?.GetAsync(uri)
                                  ?? throw new InvalidOperationException("HttpUtils.Client is null"));

            var body = await response.Content.ReadAsStringAsync();
            // Log status + body-length so the caller can tell hung-call (no log)
            // from empty-list (logs 200 + small body) from wrong-path (logs 404)
            // from wrong-shape (logs 200 + body that fails to parse below).
            Logger.LogInformation(
                "GetAllHostnamesAsync: response status={Status}, body length={Len}",
                (int)response.StatusCode, body?.Length ?? 0);

            if (!response.IsSuccessStatusCode)
            {
                // Log first 500 chars of body so error responses (HTML page,
                // JSON error envelope, etc.) are visible.
                var snippet = body is null ? "(null)"
                            : body.Length > 500 ? body[..500] + "…(truncated)"
                            : body;
                Logger.LogError(
                    "GetAllHostnamesAsync: non-success status {Status}. Body: {Body}",
                    response.StatusCode, snippet);
                return new List<GRDSGWServer>();
            }

            List<GRDSGWServer>? list;
            try
            {
                list = JsonSerializer.Deserialize<List<GRDSGWServer>>(
                    body ?? string.Empty, GRDSGWServerJsonContext.Default.ListGRDSGWServer);
            }
            catch (Exception parseEx)
            {
                // Shape mismatch — log the body's first chunk so the caller can
                // see whether it's a wrapper object like {"hosts":[...]} vs a
                // bare array. Surface as failure (empty list).
                var snippet = body is null ? "(null)"
                            : body.Length > 500 ? body[..500] + "…(truncated)"
                            : body;
                Logger.LogError(parseEx,
                    "GetAllHostnamesAsync: response parsed as List<GRDSGWServer> failed. Body: {Body}",
                    snippet);
                return new List<GRDSGWServer>();
            }

            var count = list?.Count ?? 0;
            Logger.LogInformation("GetAllHostnamesAsync: parsed {Count} hosts", count);
            return list ?? new List<GRDSGWServer>();
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "GetAllHostnamesAsync: failed");
            return new List<GRDSGWServer>();
        }
    }

    internal static GRDSGWServer SelectBestHostInRegion(string regionKey)
    {
        RegionHostsRetrievalWaiter.Reset();
        if (!Live._hostLookup.ContainsKey(regionKey) || Live._hostLookup[regionKey].Count == 0)
        {
            Logger.LogInformation(
                $"GRDServerManager.SelectBestHostInRegion: Region '{regionKey}' needs host list refresh... calling GetHostsForRegion to update now");
            _ = Task.Factory.StartNew(async () => { await GetHostsForRegion(regionKey); });
            Logger.LogInformation(
                "RegionUtil.SelectBestHostInRegion: Waiting for GetHostsForRegion to return results...");
            RegionHostsRetrievalWaiter.Wait(5 * 1000);

            Logger.LogInformation(
                $"GRDServerManager.SelectBestHostInRegion: Return from GetHostsForRegion - region '{regionKey}' host list refresh complete.");
        }

        if (!Live._hostLookup.TryGetValue(regionKey, out var myRegionRecord))
            throw new Exception($"Hosts Lookup collection does NOT contain record for region {regionKey}");

        // Do random thing
        var regionHosts = Live._hostLookup[regionKey];
        var lightest = regionHosts.Where(h => h.CapacityScore == 0);
        var lighter = regionHosts.Where(h => h.CapacityScore == 1);

        Logger.LogInformation(
            $"SelectBestHostInRegion: For region '{regionKey}' we have {lightest.Count()} lightest hosts, {lighter.Count()} midrange hosts out of {regionHosts.Count()} total hosts");

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
        var converted = TimeZoneInfo.TryConvertWindowsIdToIanaId(localTimeZoneInfo.Id, out var otzID);
        Logger.LogInformation(
            $"GetLocalTimeZone(): Local time zone is {TimeZoneInfo.Local}. Our Time Zone ID: '{otzID}'");

        return otzID ?? string.Empty;
    }

    private static string GetRegionForOurTimeZone()
    {
        var ourTimeZoneId = GetLocalTimeZone();
        // Let's just tuck away our fixed region where we are
        var timezoneMissing = LookUpRegionIndexForMyTimeZone(ourTimeZoneId, out var regionKeyForOurTimeZone);
        if (timezoneMissing)
        {
            Logger.LogWarning(
                $"Our time zone ID '{ourTimeZoneId}' was NOT FOUND in our Regions' Time Zones Lookup tables!!");
            ourTimeZoneId = "America/New_York";
        }

        return regionKeyForOurTimeZone;
    }

    #endregion - private methods

    #endregion
}