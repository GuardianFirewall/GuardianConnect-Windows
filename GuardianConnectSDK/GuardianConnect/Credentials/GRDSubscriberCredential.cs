//using NativeRoutines;

using GuardianConnect.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GuardianConnect.Credentials
{
    public class GRDSubscriberCredential
    {
        [JsonIgnore]
        private static Microsoft.Extensions.Logging.ILogger _logger = NullLogger.Instance;
        [JsonIgnore]
        public static Microsoft.Extensions.Logging.ILogger Logger
        {
            get
            {
                if (_logger == NullLogger.Instance)
                {
                    _logger = StaticLoggerFactory.CreateLogger("GRDSubscriberCredential");
                }
                return _logger;
            }
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

        public static GRDSubscriberCredential GetCurrentStoredSubscriberCredential()
        {
            // CONN#8
            Logger.LogDebug("CONN#8");
            
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
            Logger.LogInformation("Storing SubscriberCredentials to keychain...");
            string jsonOut = JsonSerializer.Serialize(this, GRDSubscriberCredentialJsonContext.Default.GRDSubscriberCredential);
            byte[] bytes = Encoding.UTF8.GetBytes(jsonOut);
            GRDKeychain.StoreData(Common.kKeychainStr_SubscriberCredential, bytes);
        }

        public GRDSubscriberCredential()
        {
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
            string? subCredString = GRDKeychain.GetPasswordStringForAccount(IGRDKeychain.kKeychainStr_SubscriberCredential);
            return new GRDSubscriberCredential(subCredString ?? string.Empty);
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
            Logger.LogInformation($"ParseSubscriberCredentials: jwt payload = '{payLoad}'");
            var subCred = JsonSerializer.Deserialize<GRDSubscriberCredential>(payLoad, GRDSubscriberCredentialJsonContext.Default.GRDSubscriberCredential)
                          ?? throw new InvalidOperationException("Failed to deserialize subscriber credential payload");

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