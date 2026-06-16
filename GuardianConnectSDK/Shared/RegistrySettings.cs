using Microsoft.Win32;

namespace GuardianConnect.Shared;

public static class RegistrySettings
{
    private const string GRDUserKeyPath = @"Software\GuardianFirewall\Settings";

    private const string GRDMachineKeyPath = @"Software\GuardianFirewall";

    public static string RetrieveGuardianUserSettings(string name)
    {
        var key = Registry.CurrentUser.CreateSubKey(GRDUserKeyPath);

        var value = (string)key.GetValue(name)!;

        return value;
    }

    public static void UpdateGuardianUserSettings(string name, string value)
    {
        var key = Registry.CurrentUser.CreateSubKey(GRDUserKeyPath);

        // add any vm variables
        key.SetValue(name, value);

        key.Close();
    }

    public static void CreateAndSetDefaultIfNotPresent(string name, string value)
    {
        var key = Registry.CurrentUser.CreateSubKey(GRDUserKeyPath);
        if (key.GetValue(name) == null) key.SetValue(name, value);
    }

    public static void ClearGuardianUserLoginSettings()
    {
        var key = Registry.CurrentUser.CreateSubKey(GRDUserKeyPath);

        // clear out of registry any values saved

        key.Close();
    }

    // Machine-wide values used to share runtime state between the SYSTEM service
    // and the per-user UI. HKCU is per-process — the service's HKCU is the SYSTEM
    // account's hive, NOT the interactive user's — so for service-to-UI broadcast
    // we use HKLM which both processes can see (SYSTEM writes, anyone reads).
    // Today this is just the KS status triplet (IsActive / Mode / AllowLan); the
    // service writes on every state change then signals KSEVT_NAME_STATUSCHANGED
    // and the UI reads on event wake. Path is now GRDMachineKeyPath
    // (Software\GuardianFirewall) — see the constant's comment for the migration
    // note from the old Software\GuardianVPN location.
    public static void UpdateGuardianMachineSetting(string name, string value)
    {
        using var key = Registry.LocalMachine.CreateSubKey(GRDMachineKeyPath);
        key.SetValue(name, value);
    }

    public static string RetrieveGuardianMachineSetting(string name)
    {
        using var key = Registry.LocalMachine.OpenSubKey(GRDMachineKeyPath);
        if (key == null) return string.Empty;
        return (key.GetValue(name) as string) ?? string.Empty;
    }
}