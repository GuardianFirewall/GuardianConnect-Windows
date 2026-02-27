using System.Text;
using System.Text.Json;
using GuardianConnect.Shared;
using System.Text.Json.Serialization;

namespace GuardianConnect.Credentials
{
    public class GRDPEToken : IGRDPEToken
    {
        public GRDPEToken()
        {
            Token = "";
            ConnectAPIEnv = Common.kConnectAPIHostname;
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
            var petObjectAsText = GRDKeychain.GetPasswordStringForAccount(IGRDKeychain.kKeychainStr_PEToken_Object);
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
            if (dict.ContainsKey(Common.kPETokenKey)) peToken.Token = dict[Common.kPETokenKey].ToString() ?? throw new InvalidOperationException();
            
            if (dict.ContainsKey(Common.kGuardianConnectDevicePETExpiresKey)) peToken.ExpirationDateUnix = (long)dict[Common.kGuardianConnectDevicePETExpiresKey];
            if (dict.ContainsKey("expirationDateUnix")) peToken.ExpirationDateUnix = (long)dict["ExpirationDateUnix"];
            if (dict.ContainsKey("ExpirationDate")) peToken.ExpirationDate = DateTime.Parse(dict["ExpirationDate"].ToString() ?? throw new InvalidOperationException());
            if (dict.ContainsKey("ConnectAPIEnv")) peToken.ConnectAPIEnv = dict["ConnectAPIEnv"]?.ToString() ?? Common.kConnectAPIHostname;
            if (dict.ContainsKey("SubscriptionType")) peToken.SubscriptionType = dict["SubscriptionType"].ToString() ?? throw new InvalidOperationException();
            if (dict.ContainsKey("SubscriptionTypePretty")) peToken.SubscriptionTypePretty = dict["SubscriptionTypePretty"].ToString() ?? throw new InvalidOperationException();
            return peToken;
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
            GRDKeychain.StorePassword(jsonOut, IGRDKeychain.kKeychainStr_PEToken_Object);
        }
    }
}
