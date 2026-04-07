using System.Text.Json.Serialization;

namespace GuardianConnect.API.Model;

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(GrdUserLoginResponse))]
[JsonSerializable(typeof(List<GrdUserLoginResponse>))]
public partial class GRDUserLoginResponseJsonContext : JsonSerializerContext
{
}