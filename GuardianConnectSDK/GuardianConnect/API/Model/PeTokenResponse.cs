using Newtonsoft.Json;

namespace GuardianConnect.API.Model;

public class PeTokenResponse
{
    public int Balance { get; set; }
    
    public string? Dpat { get; set; }

    [JsonProperty("pet-expires")] public int Petexpires { get; set; }
    
    public string? type { get; set; }

    [JsonProperty("type-pretty")] public string? Typepretty { get; set; }
}