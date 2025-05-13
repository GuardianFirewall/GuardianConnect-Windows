using System.Text;
using Newtonsoft.Json;
using Serilog;

namespace GuardianConnect.Credentials
{
    public static class GRDCredentialManager
    {
        public static Serilog.ILogger Logger { get; set; } = Serilog.Log.Logger;

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
            var serializedData = JsonConvert.SerializeObject(_credentialsList);
            var binData = Encoding.UTF8.GetBytes(serializedData);

            return binData;
        }

        private static void DataToCredentials(string dataFromKeychain)
        {
            CredentialsList = JsonConvert.DeserializeObject<List<GRDCredential>>(dataFromKeychain) ?? new List<GRDCredential>();
            Log.Information($"GRDCredentialsManager.DataToCredentials(): CredentialsList has {CredentialsList.Count}");
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
            DataToCredentials(data);
            
            // first time store empty list
            if (data == "[]") GRDKeychain.StoreData(IGRDKeychain.kGuardianCredentialsList, CredentialsToToData());
            
            string count = CredentialsList.Count == 0 ? ZERO : CredentialsList.Count.ToString();
            Logger.Information( $"LoadCredentialsList(): Number of Credentials loaded from KeyChain is {count}");
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
