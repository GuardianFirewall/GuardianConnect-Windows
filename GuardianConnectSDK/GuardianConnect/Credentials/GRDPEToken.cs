using System.Text;
using System.Text.Json;
using static GuardianConnect.Shared.Common;

namespace GuardianConnect.Credentials
{
    public class GRDPEToken //: IGRDPEToken
    {
        public GRDPEToken()
        {
            Token = "";
            ConnectAPIEnv = DefaultConnectAPIHostname;
        }

        public string Token { get; set; }

        public string ConnectAPIEnv { get; set; }

        public DateTime ExpirationDate { get; set; }

        public long ExpirationDateUnix { get; set; }

        public string SubscriptionType { get; set; } = string.Empty;
        
        public string SubscriptionTypePretty { get; set; } = string.Empty;

        public static GRDPEToken GetCurrentPEToken()
        {
            GRDPEToken peToken = new GRDPEToken();
            var petObjectAsText = GRDKeychain.GetPasswordStringForAccount(kKeychainStr_PEToken_Object);
            if (string.IsNullOrEmpty(petObjectAsText))
            {
                return peToken;
            }

            peToken = JsonSerializer.Deserialize<GRDPEToken>(petObjectAsText, GRDPETokenJsonContext.Default.GRDPEToken);


            return peToken;
        }

        public static GRDPEToken InitFromDictionary(Dictionary<string, object> dict)
        {
            if (dict.Count == 0) return new GRDPEToken();
            GRDPEToken peToken = new GRDPEToken();
            if (dict.ContainsKey("Token")) peToken.Token = dict["Token"].ToString() ?? throw new InvalidOperationException();
            if (dict.ContainsKey(kPETokenKey)) peToken.Token = dict[kPETokenKey].ToString() ?? throw new InvalidOperationException();
            if (dict.ContainsKey(kGuardianConnectDevicePETExpires)) peToken.ExpirationDateUnix = long.Parse(dict[kGuardianConnectDevicePETExpires].ToString());
            if (dict.ContainsKey("ExpirationDate"))
            {
                var expirationText = dict["ExpirationDate"].ToString();
                peToken.ExpirationDate = string.IsNullOrWhiteSpace(expirationText)
                    ? DateTimeOffset.FromUnixTimeSeconds(peToken.ExpirationDateUnix).DateTime
                    : DateTime.Parse(expirationText);
            }
            else
            {
                peToken.ExpirationDate = DateTimeOffset.FromUnixTimeSeconds(peToken.ExpirationDateUnix).DateTime;
            }
            
            if (dict.ContainsKey("ConnectAPIEnv")) peToken.ConnectAPIEnv = dict["ConnectAPIEnv"]?.ToString() ?? DefaultConnectAPIHostname;
            if (dict.ContainsKey("SubscriptionType")) peToken.SubscriptionType = dict["SubscriptionType"].ToString() ?? throw new InvalidOperationException();
            if (dict.ContainsKey("SubscriptionTypePretty")) peToken.SubscriptionTypePretty = dict["SubscriptionTypePretty"].ToString() ?? throw new InvalidOperationException();
            return peToken;
        }

        // Useful function to update Current PEToken with fields
        public void UpdateFromDict(Dictionary<string, JsonElement> dict)
        {
            if (dict.ContainsKey(kPETokenKey)) Token = dict[kPETokenKey].GetString();
            if (dict.ContainsKey(kGuardianConnectDevicePETExpires))
                ExpirationDateUnix = dict[kGuardianConnectDevicePETExpires].GetInt64();
            if (dict.ContainsKey("ExpirationDate"))
            {
                var expirationText = dict["ExpirationDate"].ToString();
                ExpirationDate = string.IsNullOrWhiteSpace(expirationText)
                    ? DateTimeOffset.FromUnixTimeSeconds(ExpirationDateUnix).DateTime
                    : DateTime.Parse(expirationText);
            }
            else
            {
                ExpirationDate = DateTimeOffset.FromUnixTimeSeconds(ExpirationDateUnix).DateTime;
            }
        }
        
        public bool IsExpired()
        {
            return ExpirationDate < DateTime.Now;
        }

        public bool RequiresValidation()
        {
            if (IsExpired()) return true;

            // TJE - check on extra stuff about validationThreshold
            return false;
        }

        public void Store()
        {
            var jsonOut = JsonSerializer.Serialize(this, GRDPETokenJsonContext.Default.GRDPEToken);
            var plainTextData = Encoding.UTF8.GetBytes(jsonOut);
            var tempFileNamePath = Path.GetTempFileName();
            // temp for test cases

            //GRDKeychain.StoreData(IGRDKeychain.kKeychainStr_PEToken_Object, plainTextData);
            GRDKeychain.StorePassword(jsonOut, kKeychainStr_PEToken_Object);
        }
        
        public void Remove()
        {
            GRDKeychain.RemoveKeychainItemForAccount(kKeychainStr_PEToken_Object);
        }
    }
}
