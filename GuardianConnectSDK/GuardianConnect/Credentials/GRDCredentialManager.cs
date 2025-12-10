using GuardianConnect.API;
using GuardianConnect.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GuardianConnect.Credentials
{
    public static class GRDCredentialManager
    {
        private static Microsoft.Extensions.Logging.ILogger _logger = NullLogger.Instance;
        public static Microsoft.Extensions.Logging.ILogger Logger
        {
            get
            {
                if (_logger == NullLogger.Instance)
                {
                    _logger = StaticLoggerFactory.CreateLogger("GRDCredentialManager");
                }
                return _logger;
            }
        }

        private const string ZERO = "ZERO";

        private static byte[] CredentialsToToData(List<GRDCredential> credentialsList)
        {
            var serializedData = JsonSerializer.Serialize(credentialsList, GRDCredentialJsonContext.Default.ListGRDCredential);
            var binData = Encoding.UTF8.GetBytes(serializedData);

            return binData;
        }

        private static List<GRDCredential> DataToCredentials(string dataFromKeychain)
        {
            var CredentialsList = JsonSerializer.Deserialize<List<GRDCredential>>(dataFromKeychain, GRDCredentialJsonContext.Default.ListGRDCredential) ?? new List<GRDCredential>();
            
            Logger.LogInformation($"GRDCredentialsManager.DataToCredentials(): CredentialsList has {CredentialsList.Count}");

            return CredentialsList;
        }

        public static GRDCredential? GetMainCredentials() => GetCredentialsListFromStorage().Find(c => c.MainCredential);

        public static List<GRDCredential> NonMainFilteredCredentials => GetCredentialsListFromStorage().Where(c => !c.MainCredential).ToList();

        public static int FilteredCredential(string identifier) => GetCredentialsListFromStorage().FindIndex(c => c.Identifer == identifier);

        public static void AddOrUpdateCredential(GRDCredential credential)
        {
            credential.LastUpdated = DateTime.Now;
            var credentialsList = GetCredentialsListFromStorage();
            int foundCredentialIndex = credentialsList.FindIndex(c => c.Identifer == credential.Identifer);
            if (foundCredentialIndex != -1)
            {
                credentialsList[foundCredentialIndex] = credential;
            }
            else
            {
                credentialsList.Insert(0, credential);
            }

            GRDKeychain.StoreData(IGRDKeychain.kGuardianCredentialsList, CredentialsToToData(credentialsList));
        }

        public static List<GRDCredential> GetCredentialsListFromStorage()
        {
            var data = GRDKeychain.GetDataForAccount(IGRDKeychain.kGuardianCredentialsList);
            if (string.IsNullOrEmpty(data))
            {
                data = JsonSerializer.Serialize(new List<GRDCredential>(), GRDCredentialJsonContext.Default.ListGRDCredential);
            }
            var credentials = DataToCredentials(data);
            
            // first time store empty list
            if (data == "[]") GRDKeychain.StoreData(IGRDKeychain.kGuardianCredentialsList, CredentialsToToData(credentials));
            
            string count = credentials.Count == 0 ? ZERO : credentials.Count.ToString();
            Logger.LogInformation( $"LoadCredentialsList(): Number of Credentials loaded from KeyChain is {count}");
            return credentials;
        }

        public static void ClearMainCredentials()
        {
            GRDKeychain.RemoveKeychainItemForAccount(IGRDKeychain.kGuardianCredentialsList);
        }
    }
}
