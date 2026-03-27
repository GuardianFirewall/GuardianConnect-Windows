using System.Text.Json.Serialization;

namespace GuardianConnect.API.Model;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata, WriteIndented = true)]
[JsonSerializable(typeof(RegionInputParameter))]
[JsonSerializable(typeof(string))]
public partial class RegionInputParameterJsonContext : JsonSerializerContext
{
}