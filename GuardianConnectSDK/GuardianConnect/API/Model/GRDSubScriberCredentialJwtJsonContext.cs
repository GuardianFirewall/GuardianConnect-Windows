using System.Text.Json.Serialization;

namespace GuardianConnect.API.Model
{
    [JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata, WriteIndented = true)]
    [JsonSerializable(typeof(GrdSubscriberCredentialJwt))]
    [JsonSerializable(typeof(List<GrdSubscriberCredentialJwt>))]
    public partial class GRDSubScriberCredentialJwtJsonContext: JsonSerializerContext
    {
    }
}
