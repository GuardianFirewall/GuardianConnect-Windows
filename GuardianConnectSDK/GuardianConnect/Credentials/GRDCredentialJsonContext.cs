using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;

namespace GuardianConnect.Credentials
{
    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(GRDCredential))]
    [JsonSerializable(typeof(List<GRDCredential>))]
    public partial class GRDCredentialJsonContext : JsonSerializerContext
    {
    }
}
