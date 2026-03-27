//using Newtonsoft.Json;

using System.Text.Json.Serialization;

namespace GuardianConnect.API.Model;

public class PeTokenResponse
{
    public int Balance { get; set; }

    public string? Dpat { get; set; }

    [JsonPropertyName("pet-expires")] public int Petexpires { get; set; }

    public string? type { get; set; }

    [JsonPropertyName("type-pretty")] public string? Typepretty { get; set; }
}