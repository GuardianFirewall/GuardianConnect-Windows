//using Newtonsoft.Json;

using System.Text.Json.Serialization;

namespace GuardianConnect.API.Model
{
    public class GrdSubscriberCredentialJwt
    {
        [JsonPropertyName("subscriber-credential")]
        public string SubscriberCredential { get; set; } = null!;
    }
}
