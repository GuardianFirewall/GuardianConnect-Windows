using System.ComponentModel;
using GuardianConnect.Abstractions;
using GuardianConnect.Shared;
using Serilog;
using Win32Calls;
using Win32Calls.WFP;
using Win32Calls.WireGuard;

namespace GuardianConnect.Services;

/// <summary>
/// WireGuard transport. Drives a single WireGuardNT adapter via
/// <see cref="WireGuardTunnel"/> and configures the adapter's IP / routes /
/// DNS via <see cref="AdapterIpDnsRoutes"/>. Mirrors
/// <see cref="GuardianConnect.VPNTransports.VPNTransportIKEV2"/> in surface
/// area but owns its adapter lifecycle directly rather than going through RAS.
/// </summary>
public sealed class VpnTunnelManager : ITransportProvider, IDisposable
{
    private const string AdapterName = "GuardianWireGuard";

    // Interface metric to set on the WireGuard adapter so its routes are
    // preferred over the physical NIC's. Windows ranks routes by total cost
    // (interface metric + route metric); pinning the WG adapter to 1 beats
    // typical physical adapter metrics (5–25+).
    private const uint TunnelInterfaceMetric = 1;

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
            ApplyAdapterConfiguration(tunnel.AdapterLuid, config);
        }
        catch (Exception ex)
        {
            lock (_lock)
            {
                _status = ITransportProvider.VPNProviderStatus.VPNStatusDisconnected;
                _lastError = ITransportProvider.VPNConnectionError.VPNConnectionErrorConfigurationFailed;
            }
            // Dispose tears down the WG adapter, which sweeps away any IP / DNS / route
            // entries we attached to it before the failure.
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

        // Wake the client watcher. IKEv2 gets this for free via
        // RasConnectionNotification → RasConnChangeWaiterTask, but WG has no RAS
        // path. Without this, the app's GeneralPageViewModel sits on
        // VPNEVT_NAME_CLIENTSIDE forever and the Connect button never flips to
        // "Disconnect." Use the same name LastKnownConnectedEntry the dispatcher
        // reports back to clients.
        NotificationHandler.LastKnownConnectedEntry = options.EntryName ?? AdapterName;
        NotificationHandler.WasDisconnectPlanned = false;
        NotificationHandler.VPNClientNotifierHandle?.Set();
        NotificationHandler.VPNServiceNotifierHandle?.Set();

        Log.Information(
            "VpnTunnelManager: adapter '{Name}' up. LUID={Luid:X16}", AdapterName, tunnel.AdapterLuid);
        return new ErrorResponse();
    }

    /// <summary>
    /// Pin interface metric to 1, then attach Addresses, Routes (one per
    /// AllowedIPs entry), and DNS servers. Throws Win32Exception on first
    /// failure — caller is expected to <see cref="WireGuardTunnel.Dispose"/>
    /// the tunnel, which destroys the adapter and rolls back any partial state.
    /// </summary>
    private static void ApplyAdapterConfiguration(ulong luid, WireGuardConfig config)
    {
        var rv = AdapterIpDnsRoutes.SetInterfaceMetric(luid, TunnelInterfaceMetric);
        if (rv != 0)
            throw new Win32Exception(rv, $"SetInterfaceMetric({TunnelInterfaceMetric}) failed.");

        foreach (var addr in config.Addresses)
        {
            rv = AdapterIpDnsRoutes.AddUnicastAddress(luid, addr.Address, (byte)addr.PrefixLength);
            if (rv != 0)
                throw new Win32Exception(rv, $"AddUnicastAddress({addr.Address}/{addr.PrefixLength}) failed.");
        }

        foreach (var network in config.Peer.AllowedIPs)
        {
            rv = AdapterIpDnsRoutes.AddRoute(luid, network.Address, (byte)network.PrefixLength);
            if (rv != 0)
                throw new Win32Exception(rv, $"AddRoute({network.Address}/{network.PrefixLength}) failed.");
        }

        if (config.DnsServers.Count > 0)
        {
            rv = AdapterIpDnsRoutes.SetDnsServers(luid, config.DnsServers);
            if (rv != 0)
                throw new Win32Exception(rv, "SetDnsServers failed.");
        }
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

        // Wake the client watcher on tear-down too. Mirrors what
        // RasConnChangeWaiterTask does for IKEv2.
        NotificationHandler.WasDisconnectPlanned = wasDisconnectPlanned;
        NotificationHandler.LastKnownConnectedEntry = string.Empty;
        NotificationHandler.VPNClientNotifierHandle?.Set();
        NotificationHandler.VPNServiceNotifierHandle?.Set();

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
