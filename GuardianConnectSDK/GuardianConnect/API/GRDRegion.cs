using GuardianConnect.API.Model;
using Newtonsoft.Json;

namespace GuardianConnect.API;

public class GRDRegion
{
    public string Continent = string.Empty; 			//continent
    
    [JsonProperty("country-iso-code")]
    public string CountryISOCode = string.Empty; 	    //country-iso-code
    
    [JsonProperty("name")]
    public string RegionName = string.Empty; 		    //name
    
    [JsonProperty("name-pretty")]
    public string DisplayName = string.Empty; 		    //name-pretty
    
    public string BestHost = string.Empty; 			//defaults to nil, is populated upon get server detail completion
    public string BestHostLocation = string.Empty; 	//defaults to nil, is populated upon get server detail completion

    /// Convenience method to parse an API response to a GRDRegion object
    /// - Parameter regionDict: the dictionary with Guardian Connect API compatible key/value pairs
    public static GRDRegion InitWithDictionary(Dictionary<string, object> regionDict)
    {
        // fill in
        GRDRegion self = new GRDRegion();
        self.Continent = (string)regionDict["continent"];
        self.CountryISOCode = (string)regionDict["countryisocode"];
        self.RegionName = (string)regionDict["name"];
        self.DisplayName = (string)regionDict["namepretty"];
        return self;
    }

    public static GRDRegion InitFromGeoDataRecord(GeoData geoDataRec)
    {
        // fill in
        GRDRegion self = new GRDRegion();
        self.Continent = geoDataRec.Continent;
        self.CountryISOCode = geoDataRec.Countryisocode;
        self.RegionName = geoDataRec.KeyName;
        self.DisplayName = geoDataRec.DisplayName;
        return self;
    }
}