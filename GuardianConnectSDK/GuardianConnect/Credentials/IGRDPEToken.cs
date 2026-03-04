using GuardianConnect.Shared;

namespace GuardianConnect.Credentials
{
    public interface IGRDPEToken
    {
        /// The Password Equivalent Token itself
        string Token { get; }

        /// The GuardianConnect API environment the PET is associated with
        string ConnectAPIEnv { get; }

        /// The PETs expiration date
        DateTime ExpirationDate { get; }

        /// The PETs expiration date as a Unix timestamp
        long ExpirationDateUnix { get; }
        
        string SubscriptionType { get; }
        
        string SubscriptionTypePretty { get; }


        /// Convenience init function to pickup PETs from data returned by the Connect API
        /// - Parameter dict: a dictionary containing key/value pairs that can be parsed to create a GRDPEToken object
        static GRDPEToken InitFromDictionary(Dictionary<string, object> dict)
        {
            if (dict.Count == 0) return new GRDPEToken();
            GRDPEToken peToken = new GRDPEToken();
            if (dict.ContainsKey("Token")) peToken.Token = dict["Token"].ToString() ?? throw new InvalidOperationException();
            if (dict.ContainsKey("expirationDateUnix")) peToken.ExpirationDateUnix = (long)dict["ExpirationDateUnix"];
            if (dict.ContainsKey("ExpirationDate")) peToken.ExpirationDate = DateTime.Parse(dict["ExpirationDate"].ToString() ?? throw new InvalidOperationException());
            if (dict.ContainsKey("ConnectAPIEnv")) peToken.ConnectAPIEnv = dict["ConnectAPIEnv"]?.ToString() ?? Common.DefaultConnectAPIHostname;
            if (dict.ContainsKey("SubscriptionType")) peToken.SubscriptionType = dict["SubscriptionType"].ToString() ?? throw new InvalidOperationException();
            if (dict.ContainsKey("SubscriptionTypePretty")) peToken.SubscriptionTypePretty = dict["SubscriptionTypePretty"].ToString() ?? throw new InvalidOperationException();
            return peToken;
        }

        /// Convenience method to retrieve a reference to the current on device PET. Returns nil if no PET is present
        static abstract GRDPEToken GetCurrentPEToken();

        /// Indicates whether the PET expiration date is in the past
        bool IsExpired();

        /// Indicates whether the PET expiration date + a 7 day buffer added is in the past
        bool RequiresValidation();

        /// Convenience method to properly store a PET as well as the PET expiration date. Returns an error in case either the persistent write into the keychain or NSUserDefaults fails
        void Store();

        /// Convenience method to delete the persistent references of the current PET as well as the token's expiration date

        static void DestroyAllPersisted()
        {
            GRDKeychain.RemoveKeychainItemForAccount(IGRDKeychain.kKeychainStr_PEToken_Object);
            GRDKeychain.RemoveKeychainItemForAccount(IGRDKeychain.kKeychainStr_PEToken_Itself);
        }
    }
}
