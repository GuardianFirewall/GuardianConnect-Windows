using System;
using System.Text.Json.Serialization;

namespace GuardianConnect.Shared;

[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata,
    WriteIndented = false,
    PropertyNameCaseInsensitive = true,
    IncludeFields = true)]
[JsonSerializable(typeof(Tuple<int, uint, int>))]
public partial class PowerChangeNotifyTupleContext : JsonSerializerContext
{
}
