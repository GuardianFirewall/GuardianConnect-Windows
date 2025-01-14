namespace GuardianConnect.Credentials
{
    internal interface IGRDPEToken
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
        GRDPEToken InitFromDictionary(Dictionary<string, object> dict);

        /// Convenience method to retrieve a reference to the current on device PET. Returns nil if no PET is present
        static abstract GRDPEToken GetCurrentPEToken();

        /// Indicates whether the PET expiration date is in the past
        bool IsExpired();

        /// Indicates whether the PET expiration date + a 7 day buffer added is in the past
        bool RequiresValidation();

        /// Convenience method to properly store a PET as well as the PET expiration date. Returns an error in case either the persistent write into the keychain or NSUserDefaults fails
        void Store();

        /// Convenience method to delete the persistent references of the current PET as well as the token's expiration date
        int DestroyAllPersisted();
    }
}
