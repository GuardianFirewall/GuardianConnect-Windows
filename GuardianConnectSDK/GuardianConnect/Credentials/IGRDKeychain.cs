namespace GuardianConnect.Credentials
{
    public interface IGRDKeychain
    {
        static readonly string kKeychainStr_EapUsername 			= @"eap-username";
        static readonly string kKeychainStr_EapPassword 			= @"eap-password";
        static readonly string kKeychainStr_AuthToken				= @"auth-token";
        static readonly string kKeychainStr_APIAuthToken 			= @"api-auth-token";
        static readonly string kKeychainStr_SubscriberCredential 	= @"subscriber-credential";
        static readonly string kKeychainStr_PEToken_Object			= @"pe-token-object";
        static readonly string kKeychainStr_PEToken_Itself			= @"pe-token-tokenitself";
        static readonly string kKeychainStr_WireGuardConfig 		= @"kGuardianWireGuardConfig";
        static readonly string kKeychainStr_DayPassAccountingToken = @"kGuardianDayPassAccountingToken";
        static readonly string kGuardianCredentialsList 			= @"GuardianCredentialsList";

        static readonly string kGuardianConnectSubscriberSecret 	= @"kGuardianConnectSubscriberSecret";

        static abstract int StorePassword(string password, string accountKey);
        static abstract int StoreData(string accountKey, byte[] data);

        static abstract string GetPasswordStringForAccount(string accountKey);
        static abstract byte[] GetPasswordRefForAccount(string accountKey);
        static abstract string GetDataForAccount(string accountKey);
        static abstract int RemoveKeychainItemForAccount(string accountKeyStr);
        static abstract int RemoveSubscriberCredentialWithRetries(int retryCount);

        static abstract void RemoveAllKeychainItems();
        static abstract void RemoveGuardianKeychainItems();
    }
}
