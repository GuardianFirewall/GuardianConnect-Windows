using System.Text.Json.Serialization;

namespace GuardianConnect.Shared
{
    [JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
    [JsonSerializable(typeof(List<string>))]
    [JsonSerializable(typeof(string))]
    public partial class LogLinesJsonContext : System.Text.Json.Serialization.JsonSerializerContext
    {
    }
}
