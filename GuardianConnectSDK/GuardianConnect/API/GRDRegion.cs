using System.Text.Json.Serialization;
using GuardianConnect.API.Model;

namespace GuardianConnect.API;

public class GRDRegion
{
    public static readonly List<GRDRegion> StaticRegions = new();

    public string BestHost = string.Empty; //defaults to nil, is populated upon get server detail completion
    public string BestHostLocation = string.Empty; //defaults to nil, is populated upon get server detail completion

    public string Continent = string.Empty; //continent

    [JsonPropertyName("country-iso-code")] public string CountryISOCode = string.Empty; //country-iso-code

    [JsonPropertyName("name-pretty")] public string DisplayName = string.Empty; //name-pretty

    [JsonPropertyName("name")] public string RegionName = string.Empty; //name

    [JsonPropertyName("country")] public string Country = string.Empty; //country

    [JsonPropertyName("region-precision")]
    public string RegionPrecision = string.Empty; //region-precision

    [JsonPropertyName("latitude")] public double Latitude; //latitude

    [JsonPropertyName("longitude")] public double Longitude; //longitude

    [JsonPropertyName("server-count")] public int ServerCount; //server-count

    [JsonPropertyName("smart-routing-proxy-servers")]
    public int SmartRoutingProxyServers; //smart-routing-proxy-servers

    [JsonPropertyName("smart-routing-proxy-state")]
    public string SmartRoutingProxyState = string.Empty; //smart-routing-proxy-state

    [JsonPropertyName("multihop-entry-enabled-servers")]
    public int MultihopEntryEnabledServers; //multihop-entry-enabled-servers

    [JsonPropertyName("multihop-entry-enabled-state")]
    public string MultihopEntryEnabledState = string.Empty; //multihop-entry-enabled-state

    [JsonPropertyName("multihop-exit-names")]
    public List<string> MultihopExitNames = new(); //multihop-exit-names

    [JsonIgnore]
    public bool SupportsSmartRoutingProxy => SmartRoutingProxyServers > 0;

    [JsonConstructor]
    public GRDRegion()
    {
    }

    /// Convenience method to parse an API response to a GRDRegion object
    /// - Parameter regionDict: the dictionary with Guardian Connect API compatible key/value pairs
    public static GRDRegion InitWithDictionary(Dictionary<string, object> regionDict)
    {
        // fill in
        var self = new GRDRegion();
        self.Continent = (string)regionDict["continent"];
        self.CountryISOCode = (string)regionDict["countryisocode"];
        self.RegionName = (string)regionDict["name"];
        self.DisplayName = (string)regionDict["namepretty"];
        return self;
    }

    public static GRDRegion InitFromGeoDataRecord(GeoData geoDataRec)
    {
        // fill in
        var self = new GRDRegion();
        self.Continent = geoDataRec.Continent;
        self.CountryISOCode = geoDataRec.Countryisocode;
        self.RegionName = geoDataRec.KeyName;
        self.DisplayName = geoDataRec.DisplayName;
        return self;
    }
}