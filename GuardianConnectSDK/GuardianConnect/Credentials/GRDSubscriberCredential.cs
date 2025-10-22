//using NativeRoutines;

using System.Text;
using System.Text.Json;
using GuardianConnect.Shared;
using System.Text.Json.Serialization;
using Serilog;

namespace GuardianConnect.Credentials
{
    public class GRDSubscriberCredential
    {
        public GRDSubscriberCredential()
        {
        }

        #region json properties
        [JsonPropertyName("jwt")]
        public string Jwt { get; set; } = string.Empty;

        //[JsonPropertyName("sub-type")]
        [JsonPropertyName("subscription-type")]
        public string SubscriptionType { get; set; } = string.Empty;

        [JsonPropertyName("subscription-type-pretty")]
        //[JsonPropertyName("sub-type-pretty")]
        public string SubscriptionTypePretty { get; set; } = string.Empty;

        //[JsonPropertyName("sub-expire-date")]
        [JsonPropertyName("subscription-expiration-date")]
        public long SubscriptionExpirationDateUnixSeconds { get; set; }

        [JsonIgnore]
        public DateTime SubscriptionExpirationDate { get; set; }

        //[JsonPropertyName("token-expire-date")]
        [JsonPropertyName("exp")]
        public long TokenExpirationDateUnixSeconds { get; set; }

        [JsonIgnore]
        public DateTime TokenExpirationDate { get; set; }

        [JsonIgnore]
        public bool IsTokenExpired { get; set; }

        [JsonIgnore]
        public bool IsEmpty => string.IsNullOrEmpty(Jwt);

        #endregion json properties

#if FROMJ2CS
// Root myDeserializedClass = JsonConvert.DeserializeObject<Root>(myJsonResponse);
    public class Root
    {
        [JsonProperty("subscription-type")]
        public string subscriptiontype { get; set; }

        [JsonProperty("subscription-type-pretty")]
        public string subscriptiontypepretty { get; set; }

        [JsonProperty("subscription-expiration-date")]
        public int subscriptionexpirationdate { get; set; }

        [JsonProperty("guardian-employee")]
        public bool guardianemployee { get; set; }

        [JsonProperty("guardian-employee-id")]
        public string guardianemployeeid { get; set; }
        public int exp { get; set; }
        public int iat { get; set; }
    }
#endif
        public static GRDSubscriberCredential GetCurrentStoredSubscriberCredential()
        {
            // CONN#8
            Log.Debug("CONN#8");
            
            var subCredBytes = GRDKeychain.GetDataForAccount(Common.kKeychainStr_SubscriberCredential);
            GRDSubscriberCredential subscriberCredential = new GRDSubscriberCredential("");
            if (subCredBytes.Length > 0)
            {
                subscriberCredential = JsonSerializer.Deserialize<GRDSubscriberCredential>(subCredBytes, GRDSubscriberCredentialJsonContext.Default.GRDSubscriberCredential) ?? throw new InvalidOperationException();
            }

            var sc = new GRDSubscriberCredential(subscriberCredential.Jwt);

            return sc;
        }

        public void Store()
        {
            Log.Information("Storing SubscriberCredentials to keychain...");
            string jsonOut = JsonSerializer.Serialize(this, GRDSubscriberCredentialJsonContext.Default.GRDSubscriberCredential);
            byte[] bytes = Encoding.UTF8.GetBytes(jsonOut);
            GRDKeychain.StoreData(Common.kKeychainStr_SubscriberCredential, bytes);
        }

        public GRDSubscriberCredential(string subscriberCredential)
        {
            if (string.IsNullOrEmpty(subscriberCredential)) return;
            
            Jwt = subscriberCredential;
            ParseSubscriberCredentialString();
        }

        public GRDSubscriberCredential InitWithSubscriberCredential(string subscriberCredential)
        {
            return new GRDSubscriberCredential(subscriberCredential);
        }

        private static GRDSubscriberCredential CurrentSubscriberCredential()
        {
            string subCredString = GRDKeychain.GetPasswordStringForAccount(IGRDKeychain.kKeychainStr_SubscriberCredential);
            return new GRDSubscriberCredential(subCredString);
        }

        public string Description()
        {
            return ToString();
        }

        public override string ToString()
        {
            string expiredString = @"YES";
            if (IsTokenExpired == false)
            {
                expiredString = @"NO";
            }

            return
                $"{GetType()}\nSubscription Type:{SubscriptionType} \nSubscription Expiration Date: {SubscriptionExpirationDate} \nExpired: {expiredString}";
        }

        private void ParseSubscriberCredentialString()
        {
            var jwtSplit = Jwt.Split('.');
            var payloadString = jwtSplit[1];
          
            // Note from CJ:
            // This is Base64 magic that I only partly understand because I am not entirely familiar with
            // the Base64 spec.
            // This just makes sure that the string can be read by removing invalid characters
            payloadString = payloadString.Replace('_', '/').Replace('-', '+');

            int padSizeForB64 = (4 - payloadString.Length % 4);
            
            string base64Payload = payloadString + (padSizeForB64 != 4 ? new string('=', padSizeForB64) : "");

            string payLoad = Common.DecodeFrom64(base64Payload);
            Log.Information($"ParseSubscriberCredentials: jwt payload = '{payLoad}'");
            var subCred = JsonSerializer.Deserialize<GRDSubscriberCredential>(payLoad, GRDSubscriberCredentialJsonContext.Default.GRDSubscriberCredential);

            SubscriptionType = subCred.SubscriptionType ?? string.Empty;
            SubscriptionTypePretty = subCred.SubscriptionTypePretty ?? string.Empty;

            //long expDateTimeSecondsSinceUnixEpoch = long.Parse(SubscriptionExpirationDateUnixSeconds);
            //SubscriptionExpirationDate = Common.DateOnlyFromAppleDTI1970(expDateTimeSecondsSinceUnixEpoch);
            //SubscriptionExpirationDate = DateTimeOffset.FromUnixTimeSeconds((long)gscDict!["subscription-expiration-date"]).DateTime;
            SubscriptionExpirationDate = DateTimeOffset.FromUnixTimeSeconds(subCred.SubscriptionExpirationDateUnixSeconds).DateTime;
            TokenExpirationDate =        DateTimeOffset.FromUnixTimeSeconds(subCred.TokenExpirationDateUnixSeconds).DateTime;
            //SubscriptionTypePretty = (string)gscDict["subscription-type"];
            IsTokenExpired = IsExpired();
            //Jwt = subCred.Jwt;
            SubscriptionExpirationDateUnixSeconds = subCred.SubscriptionExpirationDateUnixSeconds;
            TokenExpirationDateUnixSeconds = subCred.TokenExpirationDateUnixSeconds;
        }
 
        private bool IsExpired()
        {
            var safeExpirationDate = TokenExpirationDate.AddDays(-(Common.FortyEightHoursInSeconds/(3600*24)));
            var expired = (safeExpirationDate < DateTime.Now);
            return expired;
        }
    }
}