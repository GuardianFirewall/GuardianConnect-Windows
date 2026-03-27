using System.Text.Json.Serialization;
using GuardianConnect.Credentials;

namespace GuardianConnect;

[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(GRDLoginCredentials))]
[JsonSerializable(typeof(string))]
public partial class CredentialsJsonContext : JsonSerializerContext
{
}