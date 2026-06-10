using System.Text.Json.Serialization;

namespace GuardianConnect.Credentials;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata, WriteIndented = true)]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(DateTime))]
[JsonSerializable(typeof(byte[]))]
[JsonSerializable(typeof(VPNDeviceResponse))]
[JsonSerializable(typeof(GRDCredential))]
[JsonSerializable(typeof(List<GRDCredential>))]
public partial class GRDCredentialJsonContext : JsonSerializerContext
{
}