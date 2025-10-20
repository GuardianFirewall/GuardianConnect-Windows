using System.Text.Json.Serialization;

namespace GuardianConnect.Shared
{
    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(VPNCallParameters))]
    [JsonSerializable(typeof(string))]
    public partial class VPNCallParametersJsonContext : JsonSerializerContext
    {
    }
}
