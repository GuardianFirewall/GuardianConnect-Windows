using System.Text.Json.Serialization;
using GuardianConnect.API;
using GuardianConnect.API.Model;

namespace GuardianConnect.Credentials;

// IncludeFields = true for the GRDRegion nested inside GRDCredential.Server, which
// exposes its values as public fields rather than properties — without it the
// region round-trips empty. GRDCredential itself declares no public fields, so its
// own persisted shape is unaffected. Matches GRDSGWServerJsonContext.
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata, WriteIndented = true,
    IncludeFields = true)]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(byte[]))]
[JsonSerializable(typeof(VPNDeviceResponse))]
[JsonSerializable(typeof(GRDSGWServer))]
[JsonSerializable(typeof(GRDRegion))]
[JsonSerializable(typeof(GRDCredential))]
[JsonSerializable(typeof(List<GRDCredential>))]
public partial class GRDCredentialJsonContext : JsonSerializerContext
{
}