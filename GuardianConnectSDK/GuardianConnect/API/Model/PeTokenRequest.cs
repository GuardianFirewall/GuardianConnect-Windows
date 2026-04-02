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
    public PeTokenRequest(string method, string token)
    {
        ValidationMethod = method;
        PeToken = token;
    }

    public PeTokenRequest(string token)
    {
        PeToken = token;
    }

    [JsonPropertyName("validation-method")]
    public string ValidationMethod { get; } = "pe-token";

    [JsonPropertyName("pe-token")] public string PeToken { get; set; }
}