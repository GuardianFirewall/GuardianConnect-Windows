//using Newtonsoft.Json;
using System.Text.Json.Serialization;

namespace GuardianConnect.API.Model;

/*
 * {
      "validation-method": "pe-token",
      "pe-token": "xxxxxxxoAwQsahAK5RkcHR0W560W0vhQ"
 * }
 */
public class PeTokenRequest
{
    [JsonPropertyName("validation-method")]
    public string ValidationMethod = "pe-token";

    [JsonPropertyName("pe-token")]
    public string LivePeToken;

    public PeTokenRequest(string method, string token)
    {
        ValidationMethod = method;
        LivePeToken = token;
    }
}