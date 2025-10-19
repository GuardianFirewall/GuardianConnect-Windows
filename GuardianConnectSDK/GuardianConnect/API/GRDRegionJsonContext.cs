using System.Text.Json.Serialization;

namespace GuardianConnect.API
{
    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(GRDRegion))]
    [JsonSerializable(typeof(List<GRDRegion>))]
    public partial class GRDRegionJsonContext: JsonSerializerContext
    {
    }
}
