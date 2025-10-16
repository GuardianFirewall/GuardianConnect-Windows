using GuardianConnect.Shared;
using GuardianConnect.VPNTransports;
using Serilog;
using Win32Calls;

namespace GuardianFirewallService;

//public class GuardianNPCommandDispatcher :IGuardianNPContract
public class GuardianNPCommandDispatcher :IGuardianNPContract
{
    private VPNTransportIKEV2 _vpnTransportIkev2;
    
    public string GetData(int value)
    {
        return string.Format("You entered: {0}", value);
    }

    public IGuardianNPContract.CompositeType GetDataUsingDataContract(IGuardianNPContract.CompositeType composite)
    {
        if (composite == null)
        {
            throw new ArgumentNullException("composite");
        }
        if (composite.BoolValue)
        {
            composite.StringValue += "Suffix";
        }
        return composite;
    }

    public ErrorResponse StartVPNConnection(Dictionary<string, object> protocolRequest)
    {
        //return true;
        _vpnTransportIkev2 = new VPNTransportIKEV2();
        var result = _vpnTransportIkev2.StartVPNTunnelWithOptions(protocolRequest).Result;

        return result;
    }

    public void DisconnectVPNConnection()
    {
        _vpnTransportIkev2 = new VPNTransportIKEV2();
        Log.Information($"GuardianNPCommandDispatcher.DisconnectVPNConnection: stopping VPN entry '{ConnectionRoutines.ActiveConnectionEntryName}'");
        _vpnTransportIkev2.StopVPNTunnel();
    }

    public IGuardianNPContract.CurrentVPNStatus GetCurrentVpnConnectionStatus()
    {
        IGuardianNPContract.CurrentVPNStatus status = new IGuardianNPContract.CurrentVPNStatus
        {
            ConnectionState = IGuardianNPContract.ConnectionStateEnum.Disconnected,
            EntryName = "None"
        };

        bool anyConnectionActive = ConnectionRoutines.IsAnyConnectionActive(out string entryOut);
        Log.Information($"GuardianNPCommandDispatcher.GetVpnConnectionStatus: IsAnyConnectionActive returned {anyConnectionActive}, Entry: '{entryOut}'");
        status.ConnectionState = anyConnectionActive
            ? IGuardianNPContract.ConnectionStateEnum.Connected
            : IGuardianNPContract.ConnectionStateEnum.Disconnected;
        status.EntryName = status.ConnectionState == IGuardianNPContract.ConnectionStateEnum.Connected
            ? ConnectionRoutines.ActiveConnectionEntryName
            : "None";

        return status;
    }

    public Task<string> Ping()
    {
        throw new NotImplementedException();
    }

    public void ShutdownService()
    {
        throw new NotImplementedException();
    }

    public void ToggleLogging(bool whetherToDeleteLogFiles) {}

    public void SwitchServiceLoggingLevel(Common.LoggingLevels loggingLevel)
    {
        Log.Warning($"Command sent to switch log level from {Common.CurrentMinimumLogLevel} to {loggingLevel}");
        Common.CurrentMinimumLogLevel = loggingLevel;
        Common.SetMinimumLogLevelToCurrentLevel();
    }
}