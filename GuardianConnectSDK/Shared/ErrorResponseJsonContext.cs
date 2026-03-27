using System.Text.Json.Serialization;

namespace GuardianConnect.Shared;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata, WriteIndented = true,
    PropertyNameCaseInsensitive = true, IncludeFields = true)]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(Exception))]
public partial class ErrorResponseJsonContext : JsonSerializerContext
{
}