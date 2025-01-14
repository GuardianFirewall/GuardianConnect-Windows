using Newtonsoft.Json;

namespace GuardianConnect.API.Model
{
    public class GrdSubscriberCredentialJwt
    {
        [JsonProperty("subscriber-credential")]
        public string SubscriberCredential { get; set; } = null!;
    }
}
