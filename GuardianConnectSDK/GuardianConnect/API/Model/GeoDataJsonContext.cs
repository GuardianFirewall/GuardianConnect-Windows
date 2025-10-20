using System.Text.Json.Serialization;

namespace GuardianConnect.API.Model
{
    [JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata, WriteIndented = true)]
    [JsonSerializable(typeof(GeoData))]
    [JsonSerializable(typeof(List<GeoData>))]
    [JsonSerializable(typeof(List<string>))]
    public partial class GeoDataJsonContext : JsonSerializerContext
    {
    }
}