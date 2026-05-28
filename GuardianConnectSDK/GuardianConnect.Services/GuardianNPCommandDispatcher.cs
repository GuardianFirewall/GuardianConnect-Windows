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

    // Connect / disconnect / protocol-switch is a single-flight, process-wide
    // operation: only one VPN transport may be active for the whole service at
    // a time, regardless of how many pipe connections a client opens. Each
    // ServerThread creates its own GuardianNPCommandDispatcher instance, so
    // per-instance state would strand a started transport when a follow-up
    // command arrives on a different pipe. Keep the active transport static,
    // and serialize start/stop through a process-wide semaphore (SemaphoreSlim
    // because Start is async and a plain lock can't cross an await).
    private static readonly SemaphoreSlim _transportGate = new(1, 1);
    private static ITransportProvider? _activeTransport;

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
        await _transportGate.WaitAsync().ConfigureAwait(false);
        try
        {
            // Connect/Disconnect are strictly paired. If a transport is already
            // active, refuse the second Connect — the caller must Disconnect
            // first. Silently tearing down would hide a client-side bug and
            // disrupt a working tunnel without the user asking.
            if (_activeTransport is not null)
            {
                Logger.LogWarning(
                    "GuardianNPCommandDispatcher.StartVPNConnection: refused — transport {Transport} already active",
                    _activeTransport.ProtocolType);
                return new ErrorResponse().SetErrorMessage(
                    $"VPN is already connected via {_activeTransport.ProtocolType}. Disconnect first.");
            }

            var transport = SelectTransport(protocolRequest);
            _activeTransport = transport;

            Logger.LogInformation(
                "GuardianNPCommandDispatcher.StartVPNConnection: starting transport {Transport}",
                transport.ProtocolType);

            var result = await transport.StartVPNTunnelWithOptions(protocolRequest).ConfigureAwait(false);
            Logger.LogInformation(
                "GuardianNPCommandDispatcher.StartVPNConnection: transport {Transport} returned IsError={IsError}",
                transport.ProtocolType, result.IsError);

            if (result.IsError)
            {
                // Failure on start means the transport may already have torn itself
                // down (VpnTunnelManager.StartVPNTunnelWithOptions disposes its tunnel
                // on the error path). Drop the reference so a follow-up disconnect
                // doesn't try to use a dead instance.
                DisposeActiveTransportUnsafe();
            }

            return result;
        }
        finally
        {
            _transportGate.Release();
        }
    }

    public ErrorResponse DisconnectVPNConnection()
    {
        _transportGate.Wait();
        try
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
            DisposeActiveTransportUnsafe();
            return result;
        }
        finally
        {
            _transportGate.Release();
        }
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

    // Caller must hold _transportGate.
    private static void DisposeActiveTransportUnsafe()
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

        // WireGuard adapters aren't RAS connections, so IsAnyConnectionActive
        // would miss them. The static _activeTransport is our process-wide
        // source of truth for which transport (if any) is up; consult it first
        // and only fall back to RAS for IKEv2.
        ITransportProvider? active;
        _transportGate.Wait();
        try { active = _activeTransport; }
        finally { _transportGate.Release(); }

        if (active is { ProtocolType: GRDTransportProtocol.TransportProtocol.TransportWireGuard })
        {
            status.ConnectionState = ConnectionStateEnum.Connected;
            status.EntryName = string.IsNullOrEmpty(NotificationHandler.LastKnownConnectedEntry)
                ? "Guardian WireGuard"
                : NotificationHandler.LastKnownConnectedEntry;
            Logger.LogInformation(
                "GuardianNPCommandDispatcher.GetVpnConnectionStatus: WG active, Entry='{Entry}'",
                status.EntryName);
            return status;
        }

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

    public ErrorResponse EnterConnectingMode()
    {
        var svc = KillSwitchService.Current;
        if (svc == null)
        {
            // Service not running. The overlay can't be installed because there's
            // no engine — and there's no filter set to escape from either, so this
            // is benign. Return success.
            Logger.LogInformation("GuardianNPCommandDispatcher.EnterConnectingMode: KillSwitchService.Current is null; no overlay needed.");
            return new ErrorResponse();
        }
        svc.EnterConnectingMode();
        return new ErrorResponse();
    }

    public ErrorResponse ExitConnectingMode()
    {
        var svc = KillSwitchService.Current;
        if (svc == null)
        {
            Logger.LogInformation("GuardianNPCommandDispatcher.ExitConnectingMode: KillSwitchService.Current is null; no overlay to remove.");
            return new ErrorResponse();
        }
        svc.ExitConnectingMode();
        return new ErrorResponse();
    }
}