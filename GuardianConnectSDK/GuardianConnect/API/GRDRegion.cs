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