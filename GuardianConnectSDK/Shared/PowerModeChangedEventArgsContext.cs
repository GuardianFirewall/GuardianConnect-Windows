using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace GuardianConnect.Shared;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata, WriteIndented = true,
    PropertyNameCaseInsensitive = true, IncludeFields = true)]
[JsonSerializable(typeof(PowerModeChangedEventArgs))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(Exception))]
public partial class PowerModeChangedEventArgsContext : JsonSerializerContext
{
    
}