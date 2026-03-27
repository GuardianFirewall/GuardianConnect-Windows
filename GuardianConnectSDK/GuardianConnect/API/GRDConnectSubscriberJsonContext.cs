using System.Text.Json.Serialization;

namespace GuardianConnect.API;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata, WriteIndented = true,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(GRDConnectSubscriber))]
[JsonSerializable(typeof(List<GRDConnectSubscriber>))]
[JsonSerializable(typeof(GRDConnectDevice))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(DateTime))]
public partial class GRDConnectSubscriberJsonContext : JsonSerializerContext
{
}