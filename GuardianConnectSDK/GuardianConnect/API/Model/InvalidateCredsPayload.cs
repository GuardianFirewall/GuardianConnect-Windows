using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace GuardianConnect.API.Model
{

    public class InvalidateCredsPayload
    {
        [JsonPropertyName("api-auth-token")]
        public string ApiToken { get; set; } = string.Empty;

        [JsonPropertyName("subscriber-credential")]
        public string SubscriberCredential { get; set; } = string.Empty;
    }
}
