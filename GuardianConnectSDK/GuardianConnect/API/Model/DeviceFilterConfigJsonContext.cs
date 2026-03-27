using System.Text.Json.Serialization;

namespace GuardianConnect.API.Model;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata, WriteIndented = true)]
[JsonSerializable(typeof(DeviceFilterConfig))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(bool))]
public partial class DeviceFilterConfigJsonContext : JsonSerializerContext
{
}