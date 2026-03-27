using System.Text.Json.Serialization;

namespace GuardianConnect.Shared;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata, WriteIndented = true,
    PropertyNameCaseInsensitive = true, IncludeFields = true)]
[JsonSerializable(typeof(ConnectionStateEnum))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(CurrentVPNStatus))]
public partial class CurrentVPNStatusJsonConect : JsonSerializerContext
{
}