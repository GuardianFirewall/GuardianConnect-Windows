
using System.Text.Json.Serialization;

// ReSharper disable CollectionNeverUpdated.Global

namespace GuardianConnect.API.Model;

public class GeoData
{
    public static readonly List<GeoData> StaticGeoDataCollection = new();

    [JsonPropertyName("name")] public string KeyName { get; set; } = string.Empty;

    [JsonPropertyName("name-pretty")] public string DisplayName { get; set; } = string.Empty;
    public string Continent { get; set; } = string.Empty;

    [JsonPropertyName("country-iso-code")] public string Countryisocode { get; set; } = string.Empty;

    [JsonPropertyName("timezones")] public List<string> Timezones { get; set; } = [];
}