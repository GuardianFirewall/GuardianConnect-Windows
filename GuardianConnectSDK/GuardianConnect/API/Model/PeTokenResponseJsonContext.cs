using System.Text.Json.Serialization;

namespace GuardianConnect.API.Model;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata, WriteIndented = true)]
[JsonSerializable(typeof(PeTokenResponse))]
[JsonSerializable(typeof(List<PeTokenResponse>))]
public partial class PeTokenResponseJsonContext : JsonSerializerContext
{
}