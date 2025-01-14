using Newtonsoft.Json;

namespace GuardianConnect.API.Model;

/*
 * {
      "validation-method": "pe-token",
      "pe-token": "xxxxxxxoAwQsahAK5RkcHR0W560W0vhQ"
 * }
 */
public class PeTokenRequest
{
    [JsonProperty("validation-method")]
    public string ValidationMethod = "pe-token";

    [JsonProperty("pe-token")]
    public string LivePeToken;

    public PeTokenRequest(string method, string token)
    {
        ValidationMethod = method;
        LivePeToken = token;
    }
}