using System.Text.Json.Serialization;

namespace GuardianConnect.API;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(GRDGateway.RegisterDevicePayload))]
[JsonSerializable(typeof(List<GRDGateway.RegisterDevicePayload>))]
public partial class RegisterDevicePayloadJsonContext : JsonSerializerContext
{
}