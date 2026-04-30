using System.Net.NetworkInformation;
using System.Text.Json.Serialization;

namespace GuardianConnect.Shared;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata,
    WriteIndented = false,
    PropertyNameCaseInsensitive = true,
    IncludeFields = true)]
[JsonSerializable(typeof(NetworkAvailabilityEventArgs))]
public partial class NetworkAvailabilityEventArgsContext : JsonSerializerContext
{
}
