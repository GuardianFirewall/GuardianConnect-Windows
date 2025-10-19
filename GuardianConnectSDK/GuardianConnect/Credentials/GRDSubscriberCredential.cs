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
        [JsonPropertyName("jwt")]
        public string Jwt { get; set; } = string.Empty;

        [JsonPropertyName("sub-type")]
        public string SubscriptionType { get; set; } = string.Empty;

        [JsonPropertyName("sub-type-pretty")] public string SubscriptionTypePretty { get; set; } = string.Empty;

        [JsonPropertyName("sub-expire-date")]
        public DateTime SubscriptionExpirationDate { get; set; }

        [JsonPropertyName("token-expire-date")]
        public DateTime TokenExpirationDate { get; set; }

        [JsonIgnore]
        public bool IsTokenExpired { get; set; }

        [JsonIgnore]
        public bool IsEmpty => string.IsNullOrEmpty(Jwt);

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
            var gscDict = JsonSerializer.Deserialize<Dictionary<string, object>>(payLoad);

            SubscriptionType = (string)gscDict!["subscription-type"] ?? string.Empty;
            SubscriptionTypePretty = (string)gscDict!["subscription-type"] ?? string.Empty;
            
            //long expDateTimeSecondsSinceUnixEpoch = (long)gscDict["subscription-expiration-date"];
            //SubscriptionExpirationDate = Common.DateOnlyFromAppleDTI1970(expDateTimeSecondsSinceUnixEpoch);
            SubscriptionExpirationDate = DateTimeOffset.FromUnixTimeSeconds((long)gscDict!["subscription-expiration-date"]).DateTime;
            TokenExpirationDate = DateTimeOffset.FromUnixTimeSeconds((long)gscDict["exp"]).DateTime;
            SubscriptionTypePretty = (string)gscDict["subscription-type"];
            IsTokenExpired = IsExpired();
        }
 
        private bool IsExpired()
        {
            var safeExpirationDate = TokenExpirationDate.AddDays(-(Common.FortyEightHoursInSeconds/(3600*24)));
            var expired = (safeExpirationDate < DateTime.Now);
            return expired;
        }
    }
}