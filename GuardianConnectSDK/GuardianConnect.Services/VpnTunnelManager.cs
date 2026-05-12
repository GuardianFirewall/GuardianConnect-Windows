using GuardianConnect.Abstractions;
using GuardianConnect.Shared;
using Serilog;
using Win32Calls.WireGuard;

namespace GuardianConnect.Services;

/// <summary>
/// WireGuard transport. Drives a single WireGuardNT adapter via
/// <see cref="WireGuardTunnel"/>. Mirrors <see cref="GuardianConnect.VPNTransports.VPNTransportIKEV2"/>
/// in surface area but owns its adapter lifecycle directly rather than going through RAS.
///
/// Step 4a scope: cryptographic adapter setup only (create / set config / Up).
/// IP / DNS / route configuration is Step 4b; the tunnel created here will not
/// carry traffic until that lands.
/// </summary>
public sealed class VpnTunnelManager : ITransportProvider, IDisposable
{
    private const string AdapterName = "GuardianWireGuard";

    private readonly object _lock = new();
    private WireGuardTunnel? _tunnel;
    private ITransportProvider.VPNProviderStatus _status =
        ITransportProvider.VPNProviderStatus.VPNStatusDisconnected;
    private ITransportProvider.VPNConnectionError _lastError;
    private DateTime _connectedDate = DateTime.MinValue;

    public ITransportProvider.TransportProtocol ProtocolType =>
        ITransportProvider.TransportProtocol.TransportWireGuard;

    public ITransportProvider.VPNProviderStatus VPNStatus
    {
        get { lock (_lock) return _status; }
    }

    public ITransportProvider.VPNConnectionError LastVPNError
    {
        get { lock (_lock) return _lastError; }
    }

    public DateTime ConnectedDate
    {
        get { lock (_lock) return _connectedDate; }
    }

    /// <summary>The adapter's IF_LUID once connected; 0 while disconnected.</summary>
    public ulong AdapterLuid
    {
        get { lock (_lock) return _tunnel?.AdapterLuid ?? 0UL; }
    }

    public Task<(ErrorResponse, bool)> StartVPNTunnelAndReturnError()
    {
        // WireGuard always needs a config payload; there is no equivalent of RAS's
        // saved-phonebook entry. Route everything through StartVPNTunnelWithOptions.
        var err = new ErrorResponse
        {
            IsError = true,
            Message = "WireGuard transport requires VPNCallParameters; use StartVPNTunnelWithOptions."
        };
        return Task.FromResult((err, false));
    }

    public async Task<ErrorResponse> StartVPNTunnelWithOptions(VPNCallParameters options)
    {
        Log.Information("VpnTunnelManager.StartVPNTunnelWithOptions: Entry");

        var configText = await ResolveConfigText(options);
        if (configText is null)
        {
            return new ErrorResponse
            {
                IsError = true,
                Message = "VPNCallParameters did not supply a WireGuard config (set WireGuardConfigText or WireGuardConfigPath)."
            };
        }

        WireGuardConfig config;
        try
        {
            config = WireGuardConfigParser.Parse(configText);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "VpnTunnelManager: failed to parse WireGuard config");
            return ErrorResponse.FromException(ex);
        }

        lock (_lock)
        {
            if (_tunnel is not null)
            {
                return new ErrorResponse { IsError = true, Message = "Tunnel already active." };
            }
            _status = ITransportProvider.VPNProviderStatus.VPNStatusConnecting;
        }

        WireGuardTunnel tunnel = new(AdapterName);
        try
        {
            tunnel.Activate(config);
        }
        catch (Exception ex)
        {
            lock (_lock)
            {
                _status = ITransportProvider.VPNProviderStatus.VPNStatusDisconnected;
                _lastError = ITransportProvider.VPNConnectionError.VPNConnectionErrorConfigurationFailed;
            }
            tunnel.Dispose();
            Log.Error(ex, "VpnTunnelManager: WireGuard tunnel activation failed");
            return ErrorResponse.FromException(ex);
        }

        lock (_lock)
        {
            _tunnel = tunnel;
            _status = ITransportProvider.VPNProviderStatus.VPNStatusConnected;
            _connectedDate = DateTime.UtcNow;
            _lastError = 0;
        }

        Log.Information(
            "VpnTunnelManager: adapter '{Name}' up. LUID={Luid:X16}", AdapterName, tunnel.AdapterLuid);
        return new ErrorResponse();
    }

    public ErrorResponse DisconnectVPNTunnel() => StopVPNTunnel(false);

    public ErrorResponse StopVPNTunnel(bool wasDisconnectPlanned = true)
    {
        WireGuardTunnel? tunnel;
        lock (_lock)
        {
            tunnel = _tunnel;
            _tunnel = null;
            if (tunnel is not null)
                _status = ITransportProvider.VPNProviderStatus.VPNStatusDisconnecting;
        }

        if (tunnel is null)
            return new ErrorResponse();

        try
        {
            tunnel.Dispose();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "VpnTunnelManager: error tearing down WireGuard tunnel");
            lock (_lock)
            {
                _status = ITransportProvider.VPNProviderStatus.VPNStatusDisconnected;
                _connectedDate = DateTime.MinValue;
            }
            return ErrorResponse.FromException(ex);
        }

        lock (_lock)
        {
            _status = ITransportProvider.VPNProviderStatus.VPNStatusDisconnected;
            _connectedDate = DateTime.MinValue;
        }

        Log.Information(
            "VpnTunnelManager: tunnel torn down (wasDisconnectPlanned={Planned})", wasDisconnectPlanned);
        return new ErrorResponse();
    }

    public ErrorResponse FetchLastDisonnectError()
    {
        ITransportProvider.VPNConnectionError err;
        lock (_lock) err = _lastError;
        return err == 0
            ? new ErrorResponse()
            : new ErrorResponse { IsError = true, Message = err.ToString() };
    }

    // Legacy stub methods (not on ITransportProvider). Preserved so any existing
    // callers reflecting on the type don't suddenly miss them, but they just
    // forward to the canonical overloads.
    public Task<ErrorResponse> DisconnectVPNTunnel(string entryName) =>
        Task.FromResult(DisconnectVPNTunnel());

    public ErrorResponse StopVPNTunnel(string entryName) => StopVPNTunnel();

    public void Dispose() => StopVPNTunnel();

    private static async Task<string?> ResolveConfigText(VPNCallParameters options)
    {
        if (!string.IsNullOrWhiteSpace(options.WireGuardConfigText))
            return options.WireGuardConfigText;
        if (!string.IsNullOrWhiteSpace(options.WireGuardConfigPath))
            return await File.ReadAllTextAsync(options.WireGuardConfigPath);
        return null;
    }
}
