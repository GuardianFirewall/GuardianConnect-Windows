using System.Text.Json.Serialization;
using GuardianConnect.API.Model;

namespace GuardianConnect.API;

// Source-generated (de)serialization for the alerts endpoint. Metadata mode +
// case-insensitive matching keeps it NativeAOT/trim-safe (no reflection).
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(GRDAlert))]
[JsonSerializable(typeof(List<GRDAlert>))]
[JsonSerializable(typeof(GRDGateway.AlertsRequestPayload))]
[JsonSerializable(typeof(string))]
public partial class GRDAlertJsonContext : JsonSerializerContext
{
}
