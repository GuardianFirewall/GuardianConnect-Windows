using System.Text.Json.Serialization;

namespace GuardianConnect.API.Model
{
    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(PeTokenResponse))]
    [JsonSerializable(typeof(List<PeTokenResponse>))]
    public partial  class PeTokenResponseJsonContext : JsonSerializerContext
    {
    }
}
