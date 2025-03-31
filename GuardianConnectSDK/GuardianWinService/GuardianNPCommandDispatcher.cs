using GuardianConnect.Shared;
using GuardianConnect.VPNTransports;
using NativeRoutines;

namespace GuardianWinService;

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

    public bool StartVPNConnection(Dictionary<string, object> protocolRequest)
    {
        //return true;
        _vpnTransportIkev2 = new VPNTransportIKEV2();
        var result = _vpnTransportIkev2.StartVPNTunnelWithOptions(protocolRequest).Result;

        return !result.IsError;
    }

    public void DisconnectVPNConnection(string entryName)
    {
        _vpnTransportIkev2 = new VPNTransportIKEV2();
        _vpnTransportIkev2.StopVPNTunnel(entryName);
    }

    public IGuardianNPContract.CurrentVPNStatus GetCurrentVpnConnectionStatus()
    {
        IGuardianNPContract.CurrentVPNStatus status = new IGuardianNPContract.CurrentVPNStatus
        {
            ConnectionState = IGuardianNPContract.ConnectionStateEnum.Disconnected,
            EntryName = "None"
        };
            
        unsafe
        {
            char* entryOut = null;
            bool anyConnectionActive = ConnectionRoutines.IsAnyConnectionActive(entryOut);
            status.ConnectionState = anyConnectionActive
                ? IGuardianNPContract.ConnectionStateEnum.Connected
                : IGuardianNPContract.ConnectionStateEnum.Disconnected;
            status.EntryName = status.ConnectionState == IGuardianNPContract.ConnectionStateEnum.Connected
                ? ConnectionRoutines.ConnectedEntry
                : "None";
        }

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
}