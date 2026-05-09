using Microsoft.Win32;

namespace GuardianConnect.Shared;

public static class RegistrySettings
{
    private const string GRDKeyPath = @"Software\GuardianVPN";

    public static string RetrieveGuardianUserSettings(string name)
    {
        var key = Registry.CurrentUser.CreateSubKey(GRDKeyPath);

        var value = (string)key.GetValue(name)!;

        return value;
    }

    public static void UpdateGuardianUserSettings(string name, string value)
    {
        var key = Registry.CurrentUser.CreateSubKey(GRDKeyPath);

        // add any vm variables
        key.SetValue(name, value);

        key.Close();
    }

    public static void CreateAndSetDefaultIfNotPresent(string name, string value)
    {
        var key = Registry.CurrentUser.CreateSubKey(GRDKeyPath);
        if (key.GetValue(name) == null) key.SetValue(name, value);
    }

    public static void ClearGuardianUserLoginSettings()
    {
        var key = Registry.CurrentUser.CreateSubKey(GRDKeyPath);

        // clear out of registry any values saved

        key.Close();
    }

    // Machine-wide values used to share runtime state between the SYSTEM service and
    // the per-user UI (HKCU is per-process, so the service's HKCU is the SYSTEM
    // account's hive — invisible to the UI). HKLM\Software\GuardianVPN is writable
    // by SYSTEM and readable by all users by default. Used for KS status broadcast:
    // service writes on every state change + signals an event, UI reads on event wake.
    public static void UpdateGuardianMachineSetting(string name, string value)
    {
        using var key = Registry.LocalMachine.CreateSubKey(GRDKeyPath);
        key.SetValue(name, value);
    }

    public static string RetrieveGuardianMachineSetting(string name)
    {
        using var key = Registry.LocalMachine.OpenSubKey(GRDKeyPath);
        if (key == null) return string.Empty;
        return (key.GetValue(name) as string) ?? string.Empty;
    }
}