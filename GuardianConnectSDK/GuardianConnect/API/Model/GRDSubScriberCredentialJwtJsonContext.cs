using System.Text.Json.Serialization;

namespace GuardianConnect.API.Model
{
    [JsonSourceGenerationOptions(WriteIndented = true)]
    [JsonSerializable(typeof(GrdSubscriberCredentialJwt))]
    [JsonSerializable(typeof(List<GrdSubscriberCredentialJwt>))]
    public partial class GRDSubScriberCredentialJwtJsonContext: JsonSerializerContext
    {
    }
}
