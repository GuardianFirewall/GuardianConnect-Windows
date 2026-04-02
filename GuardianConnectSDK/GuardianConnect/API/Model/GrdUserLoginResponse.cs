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
/*
 *{
"cancelled-subscription": false,
"Dpat": "dpat_OkE7ygpXLyH4tUCkmbtcukS7U0pN2v93pjdKtMu3IXY4iVq7ZyAuZnwT3deWBj07kKv3iDh9N0kvMivOeBXK2HMiGhd0WbTbgdTIaA5xFru73IwqmzzpFRhEhnMWaAev",
"is-sub-user-account": false,
"pe-token": "6dh0LIPNijpznEAIy7hULXni8HkSyO4H",
"pet-expires": 1735685999,
"Type": "grd_pro_yearly",
"type-pretty": "Pro"
}
 */