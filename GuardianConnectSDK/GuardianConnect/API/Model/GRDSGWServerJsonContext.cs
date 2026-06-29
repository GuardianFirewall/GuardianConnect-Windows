using System.Text.Json.Serialization;
using GuardianConnect.API;

namespace GuardianConnect.API.Model;

// IncludeFields = true so the nested GRDRegion (which exposes its values as public
// fields, not properties) deserializes — matching GRDRegionJsonContext.
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata, WriteIndented = true,
    PropertyNameCaseInsensitive = true, IncludeFields = true)]
[JsonSerializable(typeof(GRDSGWServer))]
[JsonSerializable(typeof(List<GRDSGWServer>))]
[JsonSerializable(typeof(GRDRegion))]
public partial class GRDSGWServerJsonContext : JsonSerializerContext
{
}