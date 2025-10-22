using System.Text.Json.Serialization;

namespace GuardianConnect.API.Model
{
    [JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata, WriteIndented = true)]
    [JsonSerializable(typeof(RegionalHostRecord))]
    [JsonSerializable(typeof(List<RegionalHostRecord>))]
    public partial  class RegionalHostRecordJsonContext : JsonSerializerContext
    {
    }
}
