using GuardianConnect.Abstractions;
using GuardianConnect.Services;
using GuardianConnect.Shared;
using GuardianConnect.Shared.Extensions;
using GuardianConnect.VPNTransports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Win32Calls;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace GuardianFirewallService;

public class GuardianNPCommandDispatcher : IGuardianNPContract
{
    private static ILogger _logger = NullLogger.Instance;

    // The currently-running transport, if any. IKEv2 doesn't strictly need to
    // be held (its state is in RAS, system-wide) but WireGuard does (the adapter
    // handle lives inside VpnTunnelManager and a fresh instance won't find it).
    // So we hold whichever was started until disconnect.
    private ITransportProvider? _activeTransport;

    private static ILogger Logger
    {
        get
        {
            if (_logger == NullLogger.Instance)
            {
                _logger = StaticLoggerFactory.CreateLogger("GuardianNPCommandDispatcher");
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
        // Tear down any previously-active transport. Defensive — a well-behaved
        // client always disconnects first, but a stale handle here would prevent
        // a fresh adapter from coming up (for WireGuard) or leak resources.
        DisposeActiveTransport();

        var transport = SelectTransport(protocolRequest);
        _activeTransport = transport;

        Logger.LogInformation(
            "GuardianNPCommandDispatcher.StartVPNConnection: starting transport {Transport}",
            transport.ProtocolType);

        var result = await transport.StartVPNTunnelWithOptions(protocolRequest);
        Logger.LogInformation(
            "GuardianNPCommandDispatcher.StartVPNConnection: transport {Transport} returned IsError={IsError}",
            transport.ProtocolType, result.IsError);

        if (result.IsError)
        {
            // Failure on start means the transport may already have torn itself
            // down (VpnTunnelManager.StartVPNTunnelWithOptions disposes its tunnel
            // on the error path). Drop the reference so a follow-up disconnect
            // doesn't try to use a dead instance.
            DisposeActiveTransport();
        }

        return result;
    }

    public ErrorResponse DisconnectVPNConnection()
    {
        Logger.LogInformation(
            "GuardianNPCommandDispatcher.DisconnectVPNConnection: stopping active transport (current entry '{Entry}')",
            ConnectionRoutines.ActiveConnectionEntryName);

        if (_activeTransport is null)
        {
            // Backward-compat: legacy clients call Disconnect without a prior Start
            // in this process (e.g., a fresh service handling a "clean up after
            // ungraceful client exit" disconnect). Fall through to a fresh IKEv2
            // instance which can still find and tear down the RAS connection.
            var ikev2 = new VPNTransportIKEV2();
            return ikev2.StopVPNTunnel();
        }

        var result = _activeTransport.StopVPNTunnel();
        DisposeActiveTransport();
        return result;
    }

    /// <summary>
    /// Implicit protocol detection: if a WireGuard config payload (path or inline
    /// text) is present on the VPNCallParameters, route to VpnTunnelManager;
    /// otherwise default to IKEv2. Future protocols would either add another such
    /// field or warrant an explicit TransportKind enum on VPNCallParameters.
    /// </summary>
    private static ITransportProvider SelectTransport(VPNCallParameters request)
    {
        var hasWireGuardConfig =
            !string.IsNullOrWhiteSpace(request.WireGuardConfigPath)
            || !string.IsNullOrWhiteSpace(request.WireGuardConfigText);

        return hasWireGuardConfig
            ? new GuardianConnect.Services.VpnTunnelManager()
            : new VPNTransportIKEV2();
    }

    private void DisposeActiveTransport()
    {
        if (_activeTransport is IDisposable disposable)
        {
            try { disposable.Dispose(); }
            catch (Exception ex)
            {
                Logger.LogWarning(ex, "GuardianNPCommandDispatcher: error disposing active transport");
            }
        }
        _activeTransport = null;
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

    public ErrorResponse SetKillSwitchMode(KillSwitchMode mode)
    {
        var svc = KillSwitchService.Current;
        if (svc == null)
        {
            Logger.LogError("GuardianNPCommandDispatcher.SetKillSwitchMode: KillSwitchService.Current is null (service not registered?).");
            var resp = new ErrorResponse();
            resp.SetException(new InvalidOperationException("Kill switch service is not running."));
            return resp;
        }
        svc.SetMode(mode);
        return new ErrorResponse();
    }

    public ErrorResponse SetKillSwitchAllowLan(bool allow)
    {
        var svc = KillSwitchService.Current;
        if (svc == null)
        {
            Logger.LogError("GuardianNPCommandDispatcher.SetKillSwitchAllowLan: KillSwitchService.Current is null.");
            var resp = new ErrorResponse();
            resp.SetException(new InvalidOperationException("Kill switch service is not running."));
            return resp;
        }
        svc.SetAllowLan(allow);
        return new ErrorResponse();
    }

    public KillSwitchStatus GetKillSwitchStatus()
    {
        var svc = KillSwitchService.Current;
        if (svc == null)
        {
            // Service not running: return Off/inactive snapshot rather than throwing across the pipe.
            return new KillSwitchStatus { Mode = KillSwitchMode.Off, AllowLan = false, IsActive = false };
        }
        return svc.GetStatus();
    }
}