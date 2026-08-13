using System.Text.Json.Serialization;

namespace GuardianConnect.API;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata, WriteIndented = true,
    PropertyNameCaseInsensitive = true, IncludeFields = true)]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(GRDRegion))]
[JsonSerializable(typeof(List<GRDRegion>))]
public partial class GRDRegionJsonContext : JsonSerializerContext
{
}