using System.Text.Json;
using Microsoft.Win32;
using Serilog;

namespace GuardianConnect.Shared;

/*
 *        Preferences.Set("SelectedRegion", selectedRegion);
 *         Preferences.Default.Set("Region", myPhysicalRegionKey);
 *         string StoredSelectedRegion = Preferences.Get("SelectedRegion", "NOTSET");
 * GET, SET, CLEAR, REMOVE, CONTAINSKEY
 *
 */
public static class Preferences
{
    // Aligned with RegistrySettings.GRDUserKeyPath — see that comment for
    // the rationale. Legacy data at Software\GuardianVPN is migrated by
    // CleanupUtil's BACKUP/RESTORE round-trip during MajorUpgrade.
    private const string GRDKeyPath = @"Software\GuardianFirewall\Settings";

    private static readonly string SettingsPath = "UserPreferences";
    private static bool notLoadedYet = true;

    private static PreferencesStore Store = new();

    public static string Get(string key, string valueIfMissing)
    {
        if (notLoadedYet) LoadPreferences();
        if (!Store.ContainsKey(key)) return valueIfMissing;
        return Store[key];
    }

    public static void Set(string key, string value, bool skipPersistance = false)
    {
        if (notLoadedYet) LoadPreferences();
        Store[key] = value;
        if (!skipPersistance) Save();
    }

    public static void Set(string key, int value, bool skipPersistance = false)
    {
        if (notLoadedYet) LoadPreferences();
        Store[key] = value.ToString();
        if (!skipPersistance) Save();
    }

    public static void Remove(string key)
    {
        if (notLoadedYet) LoadPreferences();
        if (Store.ContainsKey(key)) Store.Remove(key);
        Save();
    }

    private static void LoadPreferences()
    {
        var rk = Registry.CurrentUser.OpenSubKey(GRDKeyPath);
        if (rk == null || rk.GetValue(SettingsPath) == null) return;

        var jsonData = (string)rk.GetValue(SettingsPath)!;
        if (!string.IsNullOrEmpty(jsonData))
            Store = JsonSerializer.Deserialize<PreferencesStore>(jsonData,
                        PreferencesStoreJsonContext.Default.PreferencesStore) ??
                    new PreferencesStore();

        notLoadedYet = false;
    }

    private static void Save()
    {
        try
        {
            var jsonData = JsonSerializer.Serialize(Store, PreferencesStoreJsonContext.Default.PreferencesStore);
            var rk = Registry.CurrentUser.CreateSubKey(GRDKeyPath, true);

            rk.SetValue(SettingsPath, jsonData);
        }
        catch (Exception e)
        {
            Log.Error(e, "Exception thrown when trying to write user settings.");
        }
    }

    public static class Default
    {
        private const string DefaultSettingsPath = "UserDefaults";
        private static bool notLoadedYet = true;

        private static Dictionary<string, string> Store = new();

        public static string Get(string key, string valueIfMissing)
        {
            if (notLoadedYet) LoadPreferences();
            if (!Store.ContainsKey(key)) return valueIfMissing;
            return Store[key];
        }

        public static void Set(string key, string value, bool skipPersistance = false)
        {
            if (notLoadedYet) LoadPreferences();
            Store[key] = value;
            if (!skipPersistance) Save();
        }

        public static void Set(string key, int value, bool skipPersistance = false)
        {
            if (notLoadedYet) LoadPreferences();
            Store[key] = value.ToString();
            if (!skipPersistance) Save();
        }

        private static void LoadPreferences()
        {
            var rk = Registry.CurrentUser.OpenSubKey(GRDKeyPath);
            if (rk == null || rk.GetValue(DefaultSettingsPath) == null) return;

            var jsonData = (string)rk.GetValue(DefaultSettingsPath)!;
            if (!string.IsNullOrEmpty(jsonData))
                Store = JsonSerializer.Deserialize<PreferencesStore>(jsonData,
                            PreferencesStoreJsonContext.Default.PreferencesStore) ??
                        new Dictionary<string, string>();

            notLoadedYet = false;
        }

        private static void Save()
        {
            try
            {
                var jsonData = JsonSerializer.Serialize(Store, PreferencesStoreJsonContext.Default.PreferencesStore);
                var rk = Registry.CurrentUser.CreateSubKey(GRDKeyPath, true);

                rk.SetValue(DefaultSettingsPath, jsonData);
            }
            catch (Exception e)
            {
                Log.Error(e, "Exception thrown when trying to write user settings.");
            }
        }
    }
}