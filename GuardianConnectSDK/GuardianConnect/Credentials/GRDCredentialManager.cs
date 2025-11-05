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
        private static List<GRDCredential> _credentialsList = new List<GRDCredential>();

        internal static List<GRDCredential> CredentialsList
        {
            get
            {
                return _credentialsList;
            }

            set
            {
                _credentialsList = value;
            }
        }

        private static byte[] CredentialsToToData()
        {
            var serializedData = JsonSerializer.Serialize(_credentialsList, GRDCredentialJsonContext.Default.ListGRDCredential);
            var binData = Encoding.UTF8.GetBytes(serializedData);

            return binData;
        }

        private static void DataToCredentials(string dataFromKeychain)
        {
            CredentialsList = JsonSerializer.Deserialize<List<GRDCredential>>(dataFromKeychain, GRDCredentialJsonContext.Default.ListGRDCredential) ?? new List<GRDCredential>();
            
            Logger.LogInformation($"GRDCredentialsManager.DataToCredentials(): CredentialsList has {CredentialsList.Count}");
        }

        internal static GRDCredential? MainCredentials => CredentialsList.Find(c => c.MainCredential);

        internal static List<GRDCredential> FilteredCredentials => CredentialsList.Where(c => !c.MainCredential).ToList();

        internal static int FilteredCredential(string identifier) => CredentialsList.FindIndex(c => c.Identifer == identifier);

        internal static void AddOrUpdateCredential(GRDCredential credential)
        {
            credential.LastUpdated = DateTime.Now;
            int foundCredentialIndex = FilteredCredential(credential.Identifer);
            if (foundCredentialIndex != -1)
            {
                _credentialsList[foundCredentialIndex] = credential;
            }
            else
            {
                _credentialsList.Insert(0, credential);
            }

            GRDKeychain.StoreData(IGRDKeychain.kGuardianCredentialsList, CredentialsToToData());
        }

        public static void LoadCredentialsList()
        {
            var data = GRDKeychain.GetDataForAccount(IGRDKeychain.kGuardianCredentialsList);
            if (string.IsNullOrEmpty(data))
            {
                data = JsonSerializer.Serialize(new List<GRDCredential>(), GRDCredentialJsonContext.Default.ListGRDCredential);
            }
            DataToCredentials(data);
            
            // first time store empty list
            if (data == "[]") GRDKeychain.StoreData(IGRDKeychain.kGuardianCredentialsList, CredentialsToToData());
            
            string count = CredentialsList.Count == 0 ? ZERO : CredentialsList.Count.ToString();
            Logger.LogInformation( $"LoadCredentialsList(): Number of Credentials loaded from KeyChain is {count}");
        }

        public static void ClearMainCredentials()
        {
            if (MainCredentials == null) return;
            
            MainCredentials.HostName = string.Empty;
            MainCredentials.ApiAuthToken = string.Empty;
            MainCredentials.Password = string.Empty;
            MainCredentials.UserName = string.Empty;
            AddOrUpdateCredential(MainCredentials);
        }
    }
}
