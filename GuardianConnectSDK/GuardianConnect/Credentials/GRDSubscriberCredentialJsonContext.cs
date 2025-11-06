using System.Text.Json.Serialization;

namespace GuardianConnect.Credentials
{
    [JsonSourceGenerationOptions(WriteIndented = true, IncludeFields = false)]
    [JsonSerializable(typeof(GRDSubscriberCredential))]
    [JsonSerializable(typeof(List<GRDSubscriberCredential>))]
    [JsonSerializable(typeof(string))]
    [JsonSerializable(typeof(bool))]
    [JsonSerializable(typeof(DateTime))]
    public partial class GRDSubscriberCredentialJsonContext : JsonSerializerContext
    {
    }
}