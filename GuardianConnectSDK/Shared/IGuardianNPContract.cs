namespace GuardianConnect.Shared;

public interface IGuardianNPContract
{
    public enum ConnectionStateEnum
    {
        Connected,
        Connecting,
        Disconnected,
        Disconnecting
    }

    public struct CurrentVPNStatus
    {
        public string EntryName;
        public ConnectionStateEnum ConnectionState;
    }
    
    class CompositeType
    {
        bool boolValue = true;
        string stringValue = "Hello ";

        public bool BoolValue
        {
            get { return boolValue; }
            set { boolValue = value; }
        }

        public string StringValue
        {
            get { return stringValue; }
            set { stringValue = value; }
        }
    }
    
    public enum NPCommands
    {
        StartVPNConnection,
        DisconnectVPNConnection,
        GetCurrentVpnConnectionStatus,
        GetData,
        GetDataUsingDataContract,
        Ping,
        AdministrativeShutdownRequested,
        UninstallerShutdownOccurring,
        ToggleLogging,
        RequestLogLines,
        SwitchLoggingLevel
    }

    string GetData(int value);

    CompositeType GetDataUsingDataContract(CompositeType composite);
    
    ErrorResponse StartVPNConnection(Dictionary<string, object> protocolRequest);

    void DisconnectVPNConnection(string entryName);

    CurrentVPNStatus GetCurrentVpnConnectionStatus();

    Task<string> Ping();

    void ShutdownService();

    void ToggleLogging(bool whetherToDeleteLogFiles);
    
    void SwitchServiceLoggingLevel(Common.LoggingLevels loggingLevel );
}