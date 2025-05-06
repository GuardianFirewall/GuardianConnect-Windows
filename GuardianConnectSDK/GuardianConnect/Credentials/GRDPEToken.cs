using System.Text;
using GuardianConnect.Shared;
using Newtonsoft.Json;

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

            peToken = JsonConvert.DeserializeObject<GRDPEToken>(petObjectAsText) ?? new GRDPEToken();

            return peToken;
        }

        public int DestroyAllPersisted()
        {
            GRDKeychain.RemoveKeychainItemForAccount(IGRDKeychain.kKeychainStr_PEToken_Object);
            GRDKeychain.RemoveKeychainItemForAccount(IGRDKeychain.kKeychainStr_PEToken_Itself);
            return 0;
        }

        public GRDPEToken InitFromDictionary(Dictionary<string, object> dict)
        {
            if (dict.Count == 0) return new GRDPEToken();
            GRDPEToken peToken = new GRDPEToken();
            if (dict.ContainsKey("Token")) peToken.Token = dict["Token"].ToString() ?? throw new InvalidOperationException();
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
            var jsonOut = JsonConvert.SerializeObject(this);
            var plainTextData = Encoding.UTF8.GetBytes(jsonOut);
            var tempFileNamePath = Path.GetTempFileName();
            // temp for test cases

            //GRDKeychain.StoreData(IGRDKeychain.kKeychainStr_PEToken_Object, plainTextData);
            GRDKeychain.StorePassword(jsonOut, IGRDKeychain.kKeychainStr_PEToken_Object);
        }
    }
}
