using System.Text.Json.Serialization;

namespace GuardianConnect.Credentials;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata, WriteIndented = false,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(GRDLoginCredentials))]
public partial class GRDLoginCredentialsJsonContext : JsonSerializerContext
{
}