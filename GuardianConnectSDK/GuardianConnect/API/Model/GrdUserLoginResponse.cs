using Newtonsoft.Json;

namespace GuardianConnect.API.Model
{
    public class GrdUserLoginResponse
    {
        [JsonProperty("cancelled-subscription")]
        public bool CancelledSubscription { get; set; }
        
        [JsonProperty("is-sub-user-account")]
        public bool IsSubUserAccount { get; set; }

        [JsonProperty("pe-token")]
        public string? PeToken { get; set; }

        [JsonProperty("pet-expires")]
        public int PetExpires { get; set; }
        
        [JsonProperty("type")]
        public string? SubscriptionType { get; set; }

        [JsonProperty("type-pretty")]
        public string? SubscriptionTypePretty { get; set; }

        public override string ToString()
        {
            var pretty = JsonConvert.SerializeObject(this, Formatting.Indented);
            return pretty;
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

}
