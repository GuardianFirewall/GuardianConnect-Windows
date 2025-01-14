namespace GuardianConnect.Shared;

public class State
{
    public enum VpnConnectionState
    {
        CONNECTED,
        CONNECTING,
        CONNECT_FAILED,
        DISCONNECTING,
        DISCONNECTED,
        INDETERMINATE
    }

    public VpnConnectionState CheckConnectionResult;
}