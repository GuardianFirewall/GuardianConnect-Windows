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
        SwitchLoggingLevel,
        SendPowerAndNetworkEvents,
        SetKillSwitchMode,
        SetKillSwitchAllowLan,
        GetKillSwitchStatus,
        EnterConnectingMode,
        ExitConnectingMode
    }
    
    public enum SystemEventType
    {
        NotSet,
        PowerChangeNotifyNotificationEvent,
        PowerModeChangeEvent,
        NetworkChangeOnNetworkAddressChanged,
        NetworkChangeOnNetworkAvailabilityChanged
    }

    string GetData(int value);

    CompositeType GetDataUsingDataContract(CompositeType composite);

    Task<ErrorResponse> StartVPNConnection(VPNCallParameters protocolRequest);

    ErrorResponse DisconnectVPNConnection();

    CurrentVPNStatus GetCurrentVpnConnectionStatus();

    Task<string> Ping();

    void ShutdownService();

    void ToggleLogging(bool whetherToDeleteLogFiles);

    void SwitchServiceLoggingLevel(Common.LoggingLevels loggingLevel);

    ErrorResponse SetKillSwitchMode(KillSwitchMode mode);

    ErrorResponse SetKillSwitchAllowLan(bool allow);

    KillSwitchStatus GetKillSwitchStatus();

    /// <summary>
    /// Tell the service "I'm about to attempt a Connect; open the kill-switch
    /// connecting-overlay so my credential-negotiate HTTP calls can escape
    /// the DNS-block + block-all set". Idempotent; watchdog auto-exits after
    /// 60s if no paired ExitConnectingMode arrives. See KillSwitchService.cs
    /// for full lifecycle notes.
    /// </summary>
    ErrorResponse EnterConnectingMode();

    /// <summary>
    /// Optional explicit teardown of the connecting-overlay (e.g., negotiate
    /// failed in the client and we don't want to wait for the watchdog).
    /// Idempotent. Normally not needed — the overlay is cleared automatically
    /// when the tunnel comes up via the wgConnected / RasConnected event paths.
    /// </summary>
    ErrorResponse ExitConnectingMode();
}