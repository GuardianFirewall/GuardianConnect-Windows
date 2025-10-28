using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace GuardianConnect.Shared
{
    [JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata, WriteIndented = true, PropertyNameCaseInsensitive = true, IncludeFields = true)]
    [JsonSerializable(typeof(ErrorResponse))]
    [JsonSerializable(typeof(string))]
    [JsonSerializable(typeof(Exception))]
    public partial class ErrorResponseJsonContext : JsonSerializerContext
    {
    }
}
