using System.Text.Json.Serialization;

namespace GuardianConnect.API.Model;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata, WriteIndented = true)]
[JsonSerializable(typeof(InvalidateCredsPayload))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
internal partial class InvalidateCredsPayloadJsonContext : JsonSerializerContext
{
}