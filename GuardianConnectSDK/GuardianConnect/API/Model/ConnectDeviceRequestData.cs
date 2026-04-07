using System.Text.Json.Serialization;

namespace GuardianConnect.API.Model;

/*
 * Option A - PE Token only:
 * {
 *     "pe-token": "xxxxxxxoAwQsahAK5RkcHR0W560W0vhQ"
 * }
 *
 * Option B - Identifier + Secret:
 * {
 *     "ep-grd-subscriber-identifier": "xxxxxxx",
 *     "ep-grd-subscriber-secret": "xxxxxxx"
 * }
 *
 * Optional fields (included when set):
 * {
 *     "ep-grd-device-nickname": "My Device",
 *     "accepted-tos": "true"
 * }
 */
public class ConnectDeviceRequestData
{
    private ConnectDeviceRequestData()
    {
    }

    [JsonPropertyName("pe-token")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PeToken { get; set; }

    [JsonPropertyName("ep-grd-subscriber-identifier")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Identifier { get; set; }

    [JsonPropertyName("ep-grd-subscriber-secret")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Secret { get; set; }

    [JsonPropertyName("ep-grd-device-nickname")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Nickname { get; set; }

    [JsonPropertyName("accepted-tos")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? AcceptedTOS { get; set; }

    public static ConnectDeviceRequestData WithPeToken(string peToken)
    {
        return new ConnectDeviceRequestData { PeToken = peToken };
    }

    public static ConnectDeviceRequestData ForNickName(string peToken, string nickname)
    {
        return new ConnectDeviceRequestData { PeToken = peToken, Nickname = nickname };
    }

    public static ConnectDeviceRequestData WithIdentifierAndSecret(string identifier, string secret)
    {
        return new ConnectDeviceRequestData { Identifier = identifier, Secret = secret };
    }
}