using Newtonsoft.Json;

// ReSharper disable CollectionNeverUpdated.Global
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace GuardianConnect.API.Model
{
#pragma warning disable 0649
    public class GeoData
    {
        [JsonProperty("name")]
        public string KeyName { get; set; }

        [JsonProperty("name-pretty")]
        public string DisplayName { get; set; }
        public string Continent { get; set; }

        [JsonProperty("country-iso-code")]
        public string Countryisocode { get; set; }
        public List<string> Timezones { get; set; }
    }
}
