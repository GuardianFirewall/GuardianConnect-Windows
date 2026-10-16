using System.Text.Json.Serialization;

namespace GuardianConnect.Shared;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata, WriteIndented = true,
    PropertyNameCaseInsensitive = true, IncludeFields = true)]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(string))]
public partial class ErrorResponseJsonContext : JsonSerializerContext
{
}