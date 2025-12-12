using GuardianConnect.Shared;

namespace GuardianConnect.Abstractions;

public interface IGuardianNPContract
{


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

    Task<ErrorResponse> StartVPNConnection(VPNCallParameters? protocolRequest);

    ErrorResponse DisconnectVPNConnection();

    CurrentVPNStatus GetCurrentVpnConnectionStatus();

    Task<string> Ping();

    void ShutdownService();

    void ToggleLogging(bool whetherToDeleteLogFiles);

    void SwitchServiceLoggingLevel(Common.LoggingLevels loggingLevel);
}