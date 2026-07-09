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
        ExitConnectingMode,
        // APPEND ONLY: the wire format is the hexified ordinal of this enum,
        // so inserting above an existing member breaks mixed-version app/service
        // pairs mid-upgrade — exactly the window ApplyUpdate runs in.
        ApplyUpdate
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
    /// connecting-overlay so my credential-registration HTTP calls can escape
    /// the DNS-block + block-all set". Idempotent; watchdog auto-exits after
    /// 60s if no paired ExitConnectingMode arrives. See KillSwitchService.cs
    /// for full lifecycle notes.
    /// </summary>
    ErrorResponse EnterConnectingMode();

    /// <summary>
    /// Optional explicit teardown of the connecting-overlay (e.g., registration
    /// failed in the client and we don't want to wait for the watchdog).
    /// Idempotent. Normally not needed — the overlay is cleared automatically
    /// when the tunnel comes up via the wgConnected / RasConnected event paths.
    /// </summary>
    ErrorResponse ExitConnectingMode();

    /// <summary>
    /// The client (UI) reports that the user approved applying an available
    /// product update. <paramref name="advertisedVersion"/> is a HINT ONLY —
    /// this contract deliberately carries no update source, URL, or file path
    /// (a pipe client must never be able to point the SYSTEM service at an
    /// artifact to execute). The hosting service process supplies the actual
    /// update behavior by registering
    /// <c>GuardianNPCommandDispatcher.UpdateRequestHandler</c> at startup; the
    /// handler is expected to independently fetch its own update feed, verify
    /// the advertised version is genuinely newer than what is installed,
    /// authenticate the downloaded artifact, and only then apply it. Returns
    /// an error response when no handler is registered ("not supported") or
    /// the handler rejects/fails.
    /// </summary>
    ErrorResponse ApplyUpdate(string advertisedVersion);
}