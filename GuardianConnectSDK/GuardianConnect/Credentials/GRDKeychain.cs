using System.Diagnostics;
using System.Text;
using GuardianConnect.Shared;
using GuardianConnect.Win32;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;

namespace GuardianConnect.Credentials;

public class GRDKeychain : IGRDKeychain
{
    // Aligned with RegistrySettings.GRDUserKeyPath — see that comment for
    // the rationale. The PETOKEN and other DPAPI-encrypted credentials
    // live under this subtree; CleanupUtil's BACKUP/RESTORE round-trip
    // during MajorUpgrade migrates the encrypted values from the legacy
    // Software\GuardianVPN path verbatim (DPAPI ciphertext round-trips
    // cleanly since the user account doesn't change).
    public const string GRDKeyPath = @"Software\GuardianFirewall\Settings";
    public static ILogger _logger = NullLogger.Instance;
    public static string _entropyData = @"Быстрая, коричневая лиса, перепрыгнула через ленивого пса";

    public static RegistryKey? GRDKey;

    public static ILogger Logger
    {
        get
        {
            if (_logger == NullLogger.Instance) _logger = StaticLoggerFactory.CreateLogger<GRDKeychain>();
            return _logger;
        }
    }

    public static string GetDataForAccount(string accountKey)
    {
        var data = string.Empty;
        var byteData = Array.Empty<byte>();
        var encryptedData = ReadRegistryByteData(accountKey);
        if (encryptedData != null && encryptedData.Length > 0)
            try
            {
                byteData = DPAPI.Decrypt(encryptedData, Encoding.UTF8.GetBytes(_entropyData), out var description);
                data = Encoding.UTF8.GetString(byteData);
            }
            catch (Exception e)
            {
                Logger.LogError(e,
                    "Exception thrown while decrypting accocunt data retrieved from keychain. Setting to empty.");
            }

        return data;
    }

    public static string? GetPasswordStringForAccount(string accountKey)
    {
        string encryptedPassword;
        string password;
        encryptedPassword = ReadRegistryData(accountKey);
        if (string.IsNullOrEmpty(encryptedPassword)) return null;
        password = DPAPI.Decrypt(encryptedPassword);

        return password;
    }

    public static byte[] GetPasswordRefForAccount(string accountKey)
    {
        throw new NotImplementedException();
    }

    public static void RemoveAllKeychainItems()
    {
        Registry.CurrentUser.DeleteSubKeyTree(GRDKeyPath);
    }

    public static void RemoveGuardianKeychainItems()
    {
        var rootGrdKey = Registry.CurrentUser.OpenSubKey(GRDKeyPath, true);
        if (rootGrdKey == null)
        {
            Logger.LogError("RemoveGuardianKeychainItems(): Could not open Guardian Keychain root key.");
            return;
        }

        foreach (var key in Common.GuardianKeychainItemsKeys)
            try
            {
                var o = rootGrdKey.GetValue(key);
                if (o is null)
                {
                    var errmsg = $"DELETING Registry Value with key '{key} found that value is not present.";
                    Logger.LogError(errmsg);
                    Debug.WriteLine(errmsg);
                    continue;
                }

                rootGrdKey.DeleteValue(key);
            }
            catch (Exception e)
            {
                if (e is UnauthorizedAccessException)
                {
                    var errmsg =
                        $"Exception 'UnauthorizedAccessException' thrown when attemnpting deletion of key {key}";
                    Logger.LogError(errmsg);
                    Debug.WriteLine(errmsg);
                }
            }
    }

    public static int RemoveKeychainItemForAccount(string accountKeyStr)
    {
        var rk = Registry.CurrentUser.OpenSubKey(GRDKeyPath, true);
        if (rk != null) rk.DeleteValue(accountKeyStr, false);

        return 0;
    }

    public static int RemoveSubscriberCredentialWithRetries(int retryCount)
    {
        GRDKey = Registry.CurrentUser.CreateSubKey(GRDKeyPath);
        GRDKey.DeleteSubKey(Common.kKeychainStr_SubscriberCredential, false);

        return 0;
    }

    public static int StoreData(string regKeyName, byte[] plainTextData)
    {
        var encryptedData = DPAPI.Encrypt(DPAPI.KeyType.UserKey, plainTextData, Encoding.UTF8.GetBytes(_entropyData),
            "User's Data");
        WriteRegistryData(encryptedData, regKeyName);

        return 0;
    }

    public static int StorePassword(string password, string accountKey)
    {
        var encryptedPassword = DPAPI.Encrypt(password);

        WriteRegistryData(encryptedPassword, accountKey);

        return 0;
    }

    public static void WriteRegistryData(byte[] encryptedData, string key)
    {
        try
        {
            var rk = Registry.CurrentUser.CreateSubKey(GRDKeyPath);
            rk.SetValue(key, encryptedData, RegistryValueKind.Binary);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Exception thrown when writing to registry key: {Key}", key);
            throw;
        }
    }

    public static void WriteRegistryData(string encryptedDataAsString, string key)
    {
        try
        {
            var rk = Registry.CurrentUser.CreateSubKey(GRDKeyPath);
            rk.SetValue(key, encryptedDataAsString, RegistryValueKind.String);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Exception thrown when writing to registry key: {Key}", key);
            throw;
        }
    }

    public static void WriteRegistryData(byte[] encryptedData, RegistryKey registrySubKey, string ValueName)
    {
        try
        {
            registrySubKey.SetValue(ValueName, encryptedData, RegistryValueKind.Binary);
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Exception thrown when writing to registry key: {KeyName}", registrySubKey.Name);
            throw;
        }
    }

    public static string ReadRegistryData(string key)
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
            Logger.LogError(e, "Exception thrown when reading from registry key: {Key}", key);
        }

        return encryptedDataString;
    }

    public static byte[] ReadRegistryByteData(string key)
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
            Logger.LogInformation($"Type of value for key {key} is {t}");
            if (t == RegistryValueKind.String)
            {
                var s = (string)o;
                if (!string.IsNullOrEmpty(s)) encryptedDataBytes = Encoding.UTF8.GetBytes(s);
            }
            else
            {
                encryptedDataBytes = (byte[])o;
            }
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Exception thrown when reading from registry key: {Key}", key);
        }

        return encryptedDataBytes;
    }

    public static byte[] ReadRegistryByteData(RegistryKey registrySubKey, string ValueName)
    {
        var defaultValue = new byte[0];
        var encryptedDataBytes = defaultValue;

        try
        {
            // Stored as ASCII string representing a byte array.
            // So let's retrieve first the string
            var o = registrySubKey.GetValue(ValueName, encryptedDataBytes);
            if (o == null) return defaultValue;

            encryptedDataBytes = (byte[])o;
        }
        catch (Exception e)
        {
            Logger.LogError(e, "Exception thrown when reading from registry key: {ValueName}", ValueName);
        }

        return encryptedDataBytes;
    }

    public static int RemoveSubKeyAndValues(string regKeyName)
    {
        GRDKey = Registry.CurrentUser.CreateSubKey(GRDKeyPath);
        GRDKey.DeleteSubKeyTree(regKeyName, false);

        return 0;
    }

    public static int StoreDictionaryOfObjects(string DictOfObjectsSubKeyName, Dictionary<string, byte[]> dictOfObjects)
    {
        var grdRootKey = Registry.CurrentUser.CreateSubKey(GRDKeyPath);
        var dictKey = grdRootKey.CreateSubKey(DictOfObjectsSubKeyName);

        foreach (var objectKeyName in dictOfObjects.Keys)
            try
            {
                var plainBytes = dictOfObjects[objectKeyName];
                var encryptedData = DPAPI.Encrypt(DPAPI.KeyType.UserKey, plainBytes,
                    Encoding.UTF8.GetBytes(_entropyData), "User's Data");
                WriteRegistryData(encryptedData, dictKey, objectKeyName);
            }
            catch (Exception e)
            {
                Logger.LogError(e,
                    $"Exception thrown when writing to registry key: {DictOfObjectsSubKeyName}/{objectKeyName}");
                throw;
            }

        return 0;
    }

    public static int ReadDictionaryOfObjects(string DictOfObjectsSubKeyName,
        out Dictionary<string, byte[]> dictOfObjects)
    {
        dictOfObjects = new Dictionary<string, byte[]>();
        var grdRootKey = Registry.CurrentUser.CreateSubKey(GRDKeyPath);
        var dictKey = grdRootKey.OpenSubKey(DictOfObjectsSubKeyName);
        if (dictKey == null) return -1;

        var listOfValueNames = dictKey.GetValueNames();
        foreach (var valueName in listOfValueNames)
        {
            var encryptedBytes = ReadRegistryByteData(dictKey, valueName);
            var plainBytes = DPAPI.Decrypt(encryptedBytes, Encoding.UTF8.GetBytes(_entropyData), out var description);
            dictOfObjects.Add(valueName, plainBytes);
        }

        return 0;
    }
}