using GuardianConnect.API.Model;
//using Newtonsoft.Json;
using System.Text.Json.Serialization;

namespace GuardianConnect.API;

public class GRDRegion
{
    public string Continent = string.Empty; 			//continent
    
    [JsonPropertyName("country-iso-code")]
    public string CountryISOCode = string.Empty; 	    //country-iso-code
    
    [JsonPropertyName("name")]
    public string RegionName = string.Empty; 		    //name
    
    [JsonPropertyName("name-pretty")]
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

    public static readonly List<GRDRegion> StaticRegions = new List<GRDRegion>
    {
        new GRDRegion { Continent = "Asia", CountryISOCode = "SG", RegionName = "asia-sg", DisplayName = "Singapore" },
        new GRDRegion { Continent = "Asia", CountryISOCode = "JP", RegionName = "asia-jp", DisplayName = "Japan" },
        new GRDRegion { Continent = "Europe", CountryISOCode = "DE", RegionName = "eu-de", DisplayName = "Germany" },
        new GRDRegion { Continent = "Europe", CountryISOCode = "ES", RegionName = "eu-es", DisplayName = "Spain" },
        new GRDRegion
            { Continent = "Europe", CountryISOCode = "GB", RegionName = "eu-en", DisplayName = "United Kingdom" },
        new GRDRegion { Continent = "Europe", CountryISOCode = "FR", RegionName = "eu-fr", DisplayName = "France" },
        new GRDRegion
            { Continent = "Europe", CountryISOCode = "CH", RegionName = "eu-ch", DisplayName = "Switzerland" },
        new GRDRegion
            { Continent = "Europe", CountryISOCode = "NL", RegionName = "eu-nl", DisplayName = "Netherlands" },
        new GRDRegion
        {
            Continent = "North-America", CountryISOCode = "US", RegionName = "us-north-west",
            DisplayName = "USA (Northwest)"
        },
        new GRDRegion
            { Continent = "North-America", CountryISOCode = "US", RegionName = "us-west", DisplayName = "USA (West)" },
        new GRDRegion
            { Continent = "North-America", CountryISOCode = "US", RegionName = "us-east", DisplayName = "USA (East)" },
        new GRDRegion
        {
            Continent = "North-America", CountryISOCode = "US", RegionName = "us-central", DisplayName = "USA (Central)"
        },
        new GRDRegion
            { Continent = "North-America", CountryISOCode = "CA", RegionName = "ca-east", DisplayName = "Canada" },
        new GRDRegion { Continent = "Oceania", CountryISOCode = "AU", RegionName = "au-au", DisplayName = "Australia" },
        new GRDRegion { Continent = "Europe", CountryISOCode = "IT", RegionName = "eu-italy", DisplayName = "Italy" },
        new GRDRegion { Continent = "Europe", CountryISOCode = "HR", RegionName = "eu-cr", DisplayName = "Croatia" },
        new GRDRegion { Continent = "Europe", CountryISOCode = "IE", RegionName = "eu-ie", DisplayName = "Ireland" },
        new GRDRegion { Continent = "Europe", CountryISOCode = "PL", RegionName = "eu-pl", DisplayName = "Poland" },
        new GRDRegion
        {
            Continent = "North-America", CountryISOCode = "US", RegionName = "us-mountain",
            DisplayName = "USA (Mountain)"
        },
        new GRDRegion
            { Continent = "South-America", CountryISOCode = "MX", RegionName = "sa-mexico", DisplayName = "Mexico" },
        new GRDRegion
            { Continent = "South-America", CountryISOCode = "BR", RegionName = "sa-brazil", DisplayName = "Brazil" },
        new GRDRegion
        {
            Continent = "South-America", CountryISOCode = "CO", RegionName = "sa-colombia", DisplayName = "Colombia"
        },
        new GRDRegion { Continent = "Europe", CountryISOCode = "DK", RegionName = "eu-dk", DisplayName = "Denmark" },
        new GRDRegion { Continent = "Europe", CountryISOCode = "PT", RegionName = "eu-pt", DisplayName = "Portugal" },
        new GRDRegion
            { Continent = "South-America", CountryISOCode = "CL", RegionName = "sa-cl", DisplayName = "Chile" },
        new GRDRegion
            { Continent = "Europe", CountryISOCode = "CZ", RegionName = "eu-cz", DisplayName = "Czech-Republic" },
        new GRDRegion { Continent = "Europe", CountryISOCode = "RO", RegionName = "eu-ro", DisplayName = "Romania" },
        new GRDRegion { Continent = "Europe", CountryISOCode = "AT", RegionName = "eu-at", DisplayName = "Austria" },
        new GRDRegion { Continent = "Europe", CountryISOCode = "UA", RegionName = "eu-ua", DisplayName = "Ukraine" },
        new GRDRegion { Continent = "Europe", CountryISOCode = "GR", RegionName = "eu-gr", DisplayName = "Greece" },
        new GRDRegion
            { Continent = "Africa", CountryISOCode = "ZA", RegionName = "af-za", DisplayName = "South Africa" },
        new GRDRegion { Continent = "Europe", CountryISOCode = "SE", RegionName = "eu-sweden", DisplayName = "Sweden" },
        new GRDRegion { Continent = "Europe", CountryISOCode = "NO", RegionName = "eu-no", DisplayName = "Norway" },
        new GRDRegion { Continent = "Asia", CountryISOCode = "IL", RegionName = "asia-il", DisplayName = "Israel" },
        new GRDRegion
            { Continent = "Oceania", CountryISOCode = "NZ", RegionName = "nz-nz", DisplayName = "New Zealand" },
        new GRDRegion { Continent = "Europe", CountryISOCode = "BE", RegionName = "eu-be", DisplayName = "Belgium" }
    };
}
