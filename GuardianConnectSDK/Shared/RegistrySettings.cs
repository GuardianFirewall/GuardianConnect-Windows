using Microsoft.Win32;

namespace GuardianConnect.Shared
{
    public static class RegistrySettings
    {
        private const string GRDKeyPath = @"Software\GuardianVPN"; 

        public static string RetrieveGuardianUserSettings(string name)
        {
            RegistryKey? key = Registry.CurrentUser.CreateSubKey(GRDKeyPath);

            var value = (string)key.GetValue(name)!;

            return value;
        }

        public static void UpdateGuardianUserSettings(string name, string value)
        {
            RegistryKey key = Registry.CurrentUser.CreateSubKey(GRDKeyPath);

            // add any vm variables
            key.SetValue(name, value);
            
            key.Close();
        }

        public static void CreateAndSetDefaultIfNotPresent(string name, string value)
        {
            RegistryKey? key = Registry.CurrentUser.CreateSubKey(GRDKeyPath);
            if (key.GetValue(name) == null)
            {
                key.SetValue(name, value);
            }
        }

        public static void ClearGuardianUserLoginSettings()
        {
            RegistryKey key = Registry.CurrentUser.CreateSubKey(GRDKeyPath);
            
            // clear out of registry any values saved
            
            key.Close();
            
        }
    }
}
