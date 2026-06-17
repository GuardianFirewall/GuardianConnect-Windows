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
            // Connect/Disconnect are strictly paired: refuse a second Connect while a
            // tunnel is genuinely up. Decide that OS-authoritatively, NOT from the
            // static _activeTransport — which can be stale (power-suspend tears the
            // tunnel down outside DisconnectVPNConnection) OR null while a tunnel is up
            // (power-resume reconnects without registering here). IKEv2 lives on a RAS
            // connection, so the OS (IsAnyConnectionActive) is the source of truth even
            // when _activeTransport is null. WG/Wintun is not a RAS connection, so
            // detect it via the tracked transport's status.
            bool ikev2Up = ConnectionRoutines.IsAnyConnectionActive(out _);
            bool wgUp = _activeTransport is { ProtocolType: GRDTransportProtocol.TransportProtocol.TransportWireGuard }
                        && _activeTransport.VPNStatus is ITransportProvider.VPNProviderStatus.VPNStatusConnected
                                                      or ITransportProvider.VPNProviderStatus.VPNStatusConnecting;
            if (ikev2Up || wgUp)
            {
                // RAS up but no tracked transport (resume-reconnect) → report IKEv2.
                var activeProto = _activeTransport?.ProtocolType
                                  ?? GRDTransportProtocol.TransportProtocol.TransportIKEv2;
                Logger.LogWarning(
                    "GuardianNPCommandDispatcher.StartVPNConnection: refused — a VPN is already connected (via {Transport})",
                    activeProto);
                return new ErrorResponse().SetErrorMessage(
                    $"VPN is already connected via {activeProto}. Disconnect first.");
            }

            // Nothing is actually up. If a stale transport reference lingers (tunnel
            // torn down by power-suspend or another external path without
            // DisconnectVPNConnection), reclaim it before starting fresh.
            if (_activeTransport is not null)
            {
                Logger.LogInformation(
                    "GuardianNPCommandDispatcher.StartVPNConnection: clearing stale _activeTransport {Transport} (no live tunnel)",
                    _activeTransport.ProtocolType);
                DisposeActiveTransportUnsafe();
            }

            var transport = SelectTransport(protocolRequest);
            if (transport is null)
            {
                Logger.LogWarning(
                    "GuardianNPCommandDispatcher.StartVPNConnection: refused — no transport specified (Transport={Transport})",
                    protocolRequest.Transport);
                return new ErrorResponse().SetErrorMessage(
                    "No VPN transport was specified in the connection request. " +
                    "The request must explicitly set Transport to IKEv2 or WireGuard.");
            }

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
    /// Explicit protocol selection driven by <see cref="VPNCallParameters.Transport"/>.
    /// The caller must state which transport to start; we never infer it from the
    /// presence of a config payload. An unspecified transport
    /// (<see cref="GRDTransportProtocol.TransportProtocol.TransportUnknown"/>)
    /// returns <c>null</c> so the caller can refuse the request rather than
    /// silently defaulting — a wrong default here would leave the host in a
    /// confusing state (e.g. an IKEv2 tunnel when WireGuard was intended).
    /// </summary>
    private static ITransportProvider? SelectTransport(VPNCallParameters request) =>
        request.Transport switch
        {
            GRDTransportProtocol.TransportProtocol.TransportWireGuard => new GuardianConnect.Services.VpnTunnelManager(),
            GRDTransportProtocol.TransportProtocol.TransportIKEv2     => new VPNTransportIKEV2(),
            _                                                         => null,
        };

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

    /// <summary>
    /// Clears the active-transport reference when a tunnel is torn down OUTSIDE the
    /// normal DisconnectVPNConnection front door — e.g. the power-suspend path
    /// (ServicePowerEventsHandler -> VPNTransportIKEV2.PowerSuspendVPNConnection),
    /// which stops the RAS tunnel via a throwaway instance and never touches this
    /// dispatcher. Without this, _activeTransport goes stale and blocks the next
    /// connect ("VPN is already connected ... disconnect first"). No-op when nothing
    /// is active. Acquires the gate, so never call it while already holding it.
    /// </summary>
    public static void NotifyTransportTornDownExternally()
    {
        _transportGate.Wait();
        try
        {
            if (_activeTransport is not null)
            {
                Logger.LogInformation(
                    "GuardianNPCommandDispatcher.NotifyTransportTornDownExternally: clearing _activeTransport {Transport}",
                    _activeTransport.ProtocolType);
                DisposeActiveTransportUnsafe();
            }
        }
        finally { _transportGate.Release(); }
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