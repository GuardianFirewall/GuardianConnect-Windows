
using System.Text.Json.Serialization;

// ReSharper disable CollectionNeverUpdated.Global
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.

namespace GuardianConnect.API.Model;
#pragma warning disable 0649
public class GeoData
{
    public static readonly List<GeoData> StaticGeoDataCollection = new();

    [JsonPropertyName("name")] public string KeyName { get; set; }

    [JsonPropertyName("name-pretty")] public string DisplayName { get; set; }
    public string Continent { get; set; }

    [JsonPropertyName("country-iso-code")] public string Countryisocode { get; set; }

    [JsonPropertyName("timezones")] public List<string> Timezones { get; set; }
}