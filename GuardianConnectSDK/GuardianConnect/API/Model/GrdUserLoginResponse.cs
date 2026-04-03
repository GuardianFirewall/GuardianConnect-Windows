using System.Text.Json;
using System.Text.Json.Serialization;

namespace GuardianConnect.API.Model;

public class GrdUserLoginResponse
{
    [JsonPropertyName("cancelled-subscription")]
    public bool CancelledSubscription { get; set; }

    [JsonPropertyName("is-sub-user-account")]
    public bool IsSubUserAccount { get; set; }

    [JsonPropertyName("pe-token")] public string? PeToken { get; set; }

    [JsonPropertyName("pet-expires")] public int PetExpires { get; set; }

    [JsonPropertyName("type")] public string? SubscriptionType { get; set; }

    [JsonPropertyName("type-pretty")] public string? SubscriptionTypePretty { get; set; }

    public override string ToString()
    {
        // Use the generated JsonContext to avoid AOT/trimming issues
        return JsonSerializer.Serialize(this, GRDUserLoginResponseJsonContext.Default.GrdUserLoginResponse);
    }
}
