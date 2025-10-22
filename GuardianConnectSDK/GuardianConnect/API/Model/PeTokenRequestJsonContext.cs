using System.Text.Json.Serialization;

namespace GuardianConnect.API.Model
{
    [JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata, WriteIndented = true)]
    [JsonSerializable(typeof(PeTokenRequest))]
    [JsonSerializable(typeof(string))]
    public partial class PeTokenRequestJsonContext : JsonSerializerContext
    {
    }
}
