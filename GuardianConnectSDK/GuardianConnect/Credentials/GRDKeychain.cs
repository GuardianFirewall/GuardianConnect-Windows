using System.Diagnostics;
using System.Text;
using GuardianConnect.Shared;
using GuardianConnect.Win32;
using Microsoft.Win32;
using Serilog;

namespace GuardianConnect.Credentials
{
    public class GRDKeychain : IGRDKeychain
    {
        private const string GRDKeyPath = @"Software\GuardianVPN";
        private static string _entropyData = @"Быстрая, коричневая лиса, перепрыгнула через ленивого пса";

        private static RegistryKey? GRDKey;

        // TODO: Check callers
        private static void WriteRegistryData(byte[] encryptedData, string key)
        {
            try
            {
                RegistryKey rk = Registry.CurrentUser.CreateSubKey(GRDKeyPath);
                rk.SetValue(key, encryptedData, RegistryValueKind.Binary);
            }
            catch (Exception e)
            {
                Log.Error(e, "Exception thrown when writing to registry key: ", key);
                throw;
            }
        }
        
        private static void WriteRegistryData(string encryptedDataAsString, string key)
        {
            try
            {
                RegistryKey rk = Registry.CurrentUser.CreateSubKey(GRDKeyPath);
                rk.SetValue(key, encryptedDataAsString, RegistryValueKind.String);
            }
            catch (Exception e)
            {
                Log.Error(e, "Exception thrown when writing to registry key: ", key);
                throw;
            }
        } 
        private static string ReadRegistryData(string key)
        {
            var defaultValue = string.Empty;
            var encryptedDataString = defaultValue;
            try
            {
                var rk = Registry.CurrentUser.CreateSubKey(GRDKeyPath);
                encryptedDataString = (string)rk.GetValue(key, defaultValue);
            }
            catch (Exception e)
            {
                Log.Error(e, "Exception thrown when reading from registry key: ", key);
            }
            return encryptedDataString;
        }

        private static byte[] ReadRegistryByteData(string key)
        {
            var defaultValue = new byte[0];
            var encryptedDataBytes = defaultValue;
            
            try
            {
                var rk = Registry.CurrentUser.CreateSubKey(GRDKeyPath);
                
                // Stored as ASCII string representing a byte array.
                // So let's retrieve first the string
                var o = rk.GetValue(key, null);
                if (o == null) return defaultValue;
                
                var t = rk.GetValueKind(key);
                Log.Information($"Type of value for key {key} is {t}");
                if (t == RegistryValueKind.String)
                {
                    string s = (string)o;
                    if (!string.IsNullOrEmpty(s))
                    {
                        encryptedDataBytes = Encoding.UTF8.GetBytes(s);
                    }
                }
                else
                {
                    encryptedDataBytes = (byte[])o;
                }
            }
            catch (Exception e)
            {
                Log.Error(e, "Exception thrown when reading from registry key: ", key);
            }            
            
            return encryptedDataBytes;
        }
        
        public static string GetDataForAccount(string accountKey)
        {
            string data = string.Empty;
            byte[] byteData = Array.Empty<byte>();
            byte[] encryptedData = ReadRegistryByteData(accountKey);
            if (encryptedData != null && encryptedData.Length > 0)
            {
                try
                {
                    byteData = DPAPI.Decrypt(encryptedData, Encoding.UTF8.GetBytes(_entropyData), out string description);
                    data = Encoding.UTF8.GetString(byteData);
                }
                catch (Exception e)
                {
                    Log.Error(e, $"Exception thrown while decrypting accocunt data retrieved from keychain. Setting to empty.");
                }
            }

            return data;
        }

        public static string GetPasswordStringForAccount(string accountKey)
        {
            string encryptedPassword;
            string password;
            try
            {
                encryptedPassword = ReadRegistryData(accountKey);
                if (string.IsNullOrEmpty(encryptedPassword))
                {
                    return "";
                }
                // why below throwing exception???
                //password = DPAPI.Decrypt(encryptedPassword.ToString());
                password = DPAPI.Decrypt(encryptedPassword);
            
                return password;
            }
            catch (Exception e)
            {
                System.Diagnostics.Debug.WriteLine(e);
                throw;
            }
        }

        public static byte[] GetPasswordRefForAccount(string accountKey)
        {
            throw new NotImplementedException();
        }

        public static void RemoveAllKeychainItems()
        {
            Registry.CurrentUser.DeleteSubKeyTree(GRDKeyPath);
        }

        /// <summary>
        /// Code from macOs:
        /// + (void)removeGuardianKeychainItems {
        /// NSArray *guardianKeys = @[kKeychainStr_EapUsername,
        ///                           kKeychainStr_EapPassword,
        ///                           kKeychainStr_AuthToken,
        ///                           kKeychainStr_APIAuthToken,
        ///                           kKeychainStr_WireGuardConfig];
        /// [guardianKeys enumerateObjectsUsingBlock:^(id  _Nonnull obj, NSUInteger idx, BOOL * _Nonnull stop) {
        ///     [self removeKeychainItemForAccount:obj];
        /// }];
        /// [GRDCredentialManager clearMainCredentials];
        ///}
        /// </summary>
        public static void RemoveGuardianKeychainItems()
        {
            var rootGrdKey = Registry.CurrentUser.OpenSubKey(GRDKeyPath, true);
            if (rootGrdKey == null)
            {
                Log.Error("RemoveGuardianKeychainItems(): Could not open Guardian Keychain root key.");
                return;
            }

            foreach (string key in Common.GuardianKeychainItemsKeys)
            {
                try
                {
                    object? o = rootGrdKey.GetValue(key);
                    if (o is null)
                    {
                        var errmsg = $"DELETING Registry Value with key '{key} found that value is not present.";
                        Log.Error(errmsg);
                        Debug.WriteLine(errmsg);
                        continue;
                    }
                    rootGrdKey.DeleteValue(key);
                }
                catch (Exception e)
                {
                    if (e is UnauthorizedAccessException)
                    {
                        var errmsg = $"Exception 'UnauthorizedAccessException' thrown when attemnpting deletion of key {key}";
                        Log.Error(errmsg);
                        Debug.WriteLine(errmsg);
                    }
                }
            }
        }

        public static int RemoveKeychainItemForAccount(string accountKeyStr)
        {
            RegistryKey? rk = Registry.CurrentUser.OpenSubKey(GRDKeyPath, true);
            if (rk != null) rk.DeleteValue(accountKeyStr, false);

            return 0;
        }

        public static int RemoveSubscriberCredentialWithRetries(int retryCount)
        {
            GRDKey = Registry.CurrentUser.CreateSubKey(GRDKeyPath);
            GRDKey.DeleteSubKey(Common.kKeychainStr_SubscriberCredential, false);

            return 0;
        }

        public static int StoreData(string accountKey, byte[] plainTextData)
        {
            var encryptedData = DPAPI.Encrypt(DPAPI.KeyType.UserKey, plainTextData, Encoding.UTF8.GetBytes(_entropyData), "User's Data");
            WriteRegistryData(encryptedData, accountKey);

            return 0;
        }

        public static int StorePassword(string password, string accountKey)
        {
            var encryptedPassword = DPAPI.Encrypt(password);
            
            WriteRegistryData(encryptedPassword, accountKey);
            
            return 0;
        }
    }
}
