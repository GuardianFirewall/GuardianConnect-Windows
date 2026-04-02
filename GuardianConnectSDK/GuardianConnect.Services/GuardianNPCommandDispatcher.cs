using GuardianConnect.Abstractions;
using GuardianConnect.Shared;
using GuardianConnect.VPNTransports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Win32Calls;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace GuardianFirewallService;

public class GuardianNPCommandDispatcher : IGuardianNPContract
{
    private static ILogger _logger = NullLogger.Instance;
    private VPNTransportIKEV2 _vpnTransportIkev2;

    public GuardianNPCommandDispatcher()
    {
        _vpnTransportIkev2 = new VPNTransportIKEV2();
    }

    private static ILogger Logger
    {
        get
        {
            if (_logger == NullLogger.Instance)
            {
                _logger = StaticLoggerFactory.CreateLogger("GuardianNPCommandDispatcher");
                _logger.LogInformation("GRDGateway: TEST Log");
            }

            return _logger;
        }
    }

    public string GetData(int value)
    {
        return string.Format("You entered: {0}", value);
    }

    public CompositeType GetDataUsingDataContract(CompositeType composite)
    {
        if (composite == null) throw new ArgumentNullException("composite");
        if (composite.BoolValue) composite.StringValue += "Suffix";
        return composite;
    }

    public async Task<ErrorResponse> StartVPNConnection(VPNCallParameters protocolRequest)
    {
        Logger.LogInformation(
            "GuardianNPCommandDispatcher.StartVPNConnection: Calling VpnTransportIkeV2.StartVPNTunnelWithOptions...");
        _vpnTransportIkev2 = new VPNTransportIKEV2();
        var result = await _vpnTransportIkev2.StartVPNTunnelWithOptions(protocolRequest);
        Logger.LogInformation(
            $"GuardianNPCommandDispatcher.StartVPNConnection: Return from VpnTransportIkeV2.StartVPNTunnelWithOptions. response: {result.IsError}");

        return result;
    }

    public ErrorResponse DisconnectVPNConnection()
    {
        _vpnTransportIkev2 = new VPNTransportIKEV2();
        Logger.LogInformation(
            $"GuardianNPCommandDispatcher.DisconnectVPNConnection: stopping VPN entry '{ConnectionRoutines.ActiveConnectionEntryName}'");
        var result = _vpnTransportIkev2.StopVPNTunnel();
        return result;
    }

    public CurrentVPNStatus GetCurrentVpnConnectionStatus()
    {
        var status = new CurrentVPNStatus
        {
            ConnectionState = ConnectionStateEnum.Disconnected,
            EntryName = "None"
        };

        var anyConnectionActive = ConnectionRoutines.IsAnyConnectionActive(out var entryOut);
        Logger.Log(LogLevel.Information,
            $"GuardianNPCommandDispatcher.GetVpnConnectionStatus: IsAnyConnectionActive returned {anyConnectionActive}, Entry: '{entryOut}'");
        status.ConnectionState = anyConnectionActive
            ? ConnectionStateEnum.Connected
            : ConnectionStateEnum.Disconnected;
        status.EntryName = status.ConnectionState == ConnectionStateEnum.Connected
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

    public void ToggleLogging(bool whetherToDeleteLogFiles)
    {
    }

    public void SwitchServiceLoggingLevel(Common.LoggingLevels loggingLevel)
    {
        Logger.LogWarning($"Command sent to switch log level from {Common.CurrentMinimumLogLevel} to {loggingLevel}");
        Common.CurrentMinimumLogLevel = loggingLevel;
        Common.SetMinimumLogLevelToCurrentLevel();
    }
}