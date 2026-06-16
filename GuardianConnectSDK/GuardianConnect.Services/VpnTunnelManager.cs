using System.ComponentModel;
using System.Runtime.InteropServices;
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
    // Deterministic Wintun adapter alias. Exposed as internal so the kill
    // switch (KillSwitchService) can resolve this adapter's LUID by the exact
    // same alias instead of duplicating the string literal — a mismatch there
    // would silently break tunnel-permit filtering under block-all.
    internal const string AdapterName = "GuardianFirewall-WireGuard";

    // Interface metric to set on the WireGuard adapter so its routes are
    // preferred over the physical NIC's. Windows ranks routes by total cost
    // (interface metric + route metric); pinning the WG adapter to 1 beats
    // typical physical adapter metrics (5–25+).
    private const uint TunnelInterfaceMetric = 1;

    private readonly object _lock = new();
    private WireGuardTunnel? _tunnel;
    private WireGuardDnsBlockPermit.Installation? _dnsFilters;
    private ITransportProvider.VPNProviderStatus _status =
        ITransportProvider.VPNProviderStatus.VPNStatusDisconnected;
    private ITransportProvider.VPNConnectionError _lastError;
    private DateTime _connectedDate = DateTime.MinValue;

    public GRDTransportProtocol.TransportProtocol ProtocolType =>
        GRDTransportProtocol.TransportProtocol.TransportWireGuard;

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
        WireGuardDnsBlockPermit.Installation? dnsFilters = null;
        try
        {
            tunnel.Activate(config);
            ApplyAdapterConfiguration(tunnel.AdapterLuid, config);

            // DNS-leak protection: block all DNS in the VPN DNS sublayer and
            // permit only DNS leaving on the tunnel adapter's LUID. Without
            // this, Windows' multi-homed DNS sends parallel queries to the
            // physical NIC's resolvers (e.g. ISP DNS) and leaks. The IKEv2
            // path gets a similar effect for free from the RAS PPP connection
            // bumping the physical adapter's metric; Wintun doesn't.
            dnsFilters = WireGuardDnsBlockPermit.Install(tunnel.AdapterLuid);
            if (dnsFilters is null)
                throw new InvalidOperationException(
                    "WireGuardDnsBlockPermit.Install failed; refusing to connect without DNS-leak protection.");
        }
        catch (Exception ex)
        {
            lock (_lock)
            {
                _status = ITransportProvider.VPNProviderStatus.VPNStatusDisconnected;
                _lastError = ITransportProvider.VPNConnectionError.VPNConnectionErrorConfigurationFailed;
            }
            if (dnsFilters is not null) WireGuardDnsBlockPermit.Uninstall(dnsFilters);
            // Dispose tears down the WG adapter, which sweeps away any IP / DNS / route
            // entries we attached to it before the failure.
            tunnel.Dispose();
            Log.Error(ex, "VpnTunnelManager: WireGuard tunnel activation failed");
            return ErrorResponse.FromException(ex);
        }

        lock (_lock)
        {
            _tunnel = tunnel;
            _dnsFilters = dnsFilters;
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
        //
        // Do NOT fire VPNServiceNotifierHandle — its only listener is the IKEv2
        // poller (PollConnectionState), which then queries RAS and, finding no
        // RAS connection up, mis-labels the situation as an "UNPLANNED
        // DISCONNECT" and corrupts VPNStatusAtSuspendTime. WG state is observable
        // to the client via VPNClientNotifierHandle alone.
        NotificationHandler.LastKnownConnectedEntry = options.EntryName ?? AdapterName;
        NotificationHandler.WasDisconnectPlanned = false;
        NotificationHandler.VPNClientNotifierHandle?.Set();

        // Publish the resolved server endpoint BEFORE raising the connected event:
        // KillSwitchService.InstallFiltersUnsafe runs synchronously inside the
        // RaiseWireGuardConnectionStateChanged fan-out and reads this to install the
        // WG carrier permit (UDP to exactly this server IP:port). If it's not set
        // first, KS would block WireGuard's own encrypted carrier and kill all
        // connectivity. See NotificationHandler.WireGuardServerEndpoint.
        NotificationHandler.WireGuardServerEndpoint = config.Peer.Endpoint;

        // Tell KillSwitchService (and anyone else listening) that a WG tunnel
        // came up. RAS-only subscribers (NotificationHandler.RasConnectionStateChanged)
        // never see this transition because Wintun isn't a RAS connection.
        NotificationHandler.RaiseWireGuardConnectionStateChanged(true);

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
        WireGuardDnsBlockPermit.Installation? dnsFilters;
        lock (_lock)
        {
            tunnel = _tunnel;
            dnsFilters = _dnsFilters;
            _tunnel = null;
            _dnsFilters = null;
            if (tunnel is not null)
                _status = ITransportProvider.VPNProviderStatus.VPNStatusDisconnecting;
        }

        if (tunnel is null)
            return new ErrorResponse();

        // Uninstall DNS filters before tearing down the adapter so the
        // LUID-scoped permits don't briefly outlive the tunnel they reference.
        if (dnsFilters is not null) WireGuardDnsBlockPermit.Uninstall(dnsFilters);

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

        // Post-Dispose quiet period: give WireGuardNT (wireguard.sys) time
        // to drain any DPCs (deferred procedure calls) queued by the
        // adapter's packet-processing path before we proceed to subsequent
        // teardown steps (DNS flush, status flip, event notification) or
        // — more importantly — let any new Activate call land that creates
        // a fresh adapter. Without this, rapid teardown+create cycles
        // (transport-switch testing, stale-cred-cleanup-induced reconnects,
        // multi-click radio toggles) can race the driver's DPC drain and
        // trigger bugcheck 0xCE
        // (DRIVER_UNLOADED_WITHOUT_CANCELLING_PENDING_OPERATIONS, arg2=0x10
        // = orphaned DPC pointer) when the kernel later tries to follow a
        // DPC pointer into an image range that's been unmapped. Empirically
        // observed on 0.40.21 + 0.40.22 testing during rapid transport
        // switches. 150ms is a band-aid; root cause is upstream in
        // wireguard.sys and needs minidump-level confirmation (no WinDbg
        // analysis yet at write time).
        System.Threading.Thread.Sleep(150);

        // Flush the Windows DNS resolver cache: when the WG adapter was up
        // wg-quick configured it with DNS = 1.1.1.1 / 1.0.0.1 and Windows
        // stamped those as the system resolvers for the WG adapter. Tearing
        // the Wintun adapter down removes the adapter cleanly, but the DNS
        // Client service's per-process cache and the system resolver state
        // briefly hold negative/positive entries that resolved through the
        // now-gone interface. A rapid reconnect issuing an HTTPS lookup for
        // a different gateway hostname (e.g., miami-2.sgw.guardianapp.com
        // right after disconnecting from miami-4) gets back "No such host
        // is known" because the resolver hasn't yet retried via the physical
        // NIC. DnsFlushResolverCache is the same Win32 API used by
        // `ipconfig /flushdns`; non-blocking, no privilege escalation.
        try
        {
            var flushed = DnsFlushResolverCache();
            Log.Information(
                "VpnTunnelManager.StopVPNTunnel: DnsFlushResolverCache returned {Result}",
                flushed);
        }
        catch (Exception ex)
        {
            Log.Warning(ex,
                "VpnTunnelManager.StopVPNTunnel: DnsFlushResolverCache threw; continuing teardown");
        }

        lock (_lock)
        {
            _status = ITransportProvider.VPNProviderStatus.VPNStatusDisconnected;
            _connectedDate = DateTime.MinValue;
        }

        // Wake the client watcher on tear-down too. Mirrors what
        // RasConnChangeWaiterTask does for IKEv2. Service-side notifier is
        // intentionally NOT signalled — see the matching note in
        // StartVPNTunnelWithOptions.
        NotificationHandler.WasDisconnectPlanned = wasDisconnectPlanned;
        NotificationHandler.LastKnownConnectedEntry = string.Empty;
        NotificationHandler.VPNClientNotifierHandle?.Set();

        // Symmetric with the connect-side hook. KillSwitchService listens for
        // this to evaluate whether filters should stay up (unplanned drop) or
        // be torn down (planned disconnect) on the WG path.
        NotificationHandler.WireGuardServerEndpoint = null;
        NotificationHandler.RaiseWireGuardConnectionStateChanged(false);

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

    // Flushes the system DNS resolver cache — equivalent to running
    // `ipconfig /flushdns`. Called on WG tunnel teardown to drop any
    // entries that resolved while the WG adapter's DNS (1.1.1.1) was the
    // system resolver and that would otherwise leave a window where
    // post-disconnect lookups via the physical NIC return stale results.
    // Returns ERROR_SUCCESS (0) on success, a Win32 error code otherwise.
    [DllImport("dnsapi.dll", EntryPoint = "DnsFlushResolverCache", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DnsFlushResolverCache();

    private static async Task<string?> ResolveConfigText(VPNCallParameters options)
    {
        if (!string.IsNullOrWhiteSpace(options.WireGuardConfigText))
            return options.WireGuardConfigText;
        if (!string.IsNullOrWhiteSpace(options.WireGuardConfigPath))
            return await File.ReadAllTextAsync(options.WireGuardConfigPath);
        return null;
    }
}
