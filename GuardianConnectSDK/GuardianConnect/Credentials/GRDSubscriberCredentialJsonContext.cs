using System.Text.Json.Serialization;

namespace GuardianConnect.Credentials
{
    [JsonSourceGenerationOptions(WriteIndented = true, IncludeFields = true)]
    [JsonSerializable(typeof(GRDSubscriberCredential))]
    [JsonSerializable(typeof(List<GRDSubscriberCredential>))]
    [JsonSerializable(typeof(string))]
    [JsonSerializable(typeof(bool))]
    [JsonSerializable(typeof(DateTime))]
    public partial class GRDSubscriberCredentialJsonContext : JsonSerializerContext
    {
    }
}