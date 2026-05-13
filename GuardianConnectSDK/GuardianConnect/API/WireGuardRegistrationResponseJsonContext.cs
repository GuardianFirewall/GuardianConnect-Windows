using System.Text.Json.Serialization;

namespace GuardianConnect.API;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(GRDGateway.WireGuardRegistrationResponse))]
public partial class WireGuardRegistrationResponseJsonContext : JsonSerializerContext
{
}
