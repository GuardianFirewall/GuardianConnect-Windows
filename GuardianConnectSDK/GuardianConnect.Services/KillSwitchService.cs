using GuardianConnect.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Win32Calls;
using Win32Calls.WFP;
using Windows.Win32.Foundation;

namespace GuardianConnect.Services;

/// <summary>
/// Kill switch orchestrator. Owns a single dynamic WFP session and drives filter
/// installation / removal in response to VPN state changes (per the OnConnected
/// behaviour table in the design doc §3.1).
///
/// State machine (v1, OnConnected only):
///
///   Mode=Off                          → filters removed (any state)
///   Mode=OnConnected & VPN CONNECTED  → filters installed with tunnel LUID
///   Mode=OnConnected & VPN CONNECTING → filters NOT installed yet (would block
///                                       the IKEv2 handshake; we wait for CONNECTED)
///   Mode=OnConnected & VPN drop:
///     - WasDisconnectPlanned=false    → filters STAY installed (kill switch
///                                       fulfils its purpose: traffic stays blocked
///                                       until the user explicitly turns it off)
///     - WasDisconnectPlanned=true     → filters removed (user-initiated disconnect
///                                       is a clean exit)
///
/// Always-On (persistent filters across reboots) is §8 Future Experimental and is
/// not implemented here.
/// </summary>
public sealed class KillSwitchService : BackgroundService
{
    private readonly ILogger<KillSwitchService> _logger;
    private readonly object _stateLock = new();

    private KillSwitchMode _mode = KillSwitchMode.Off;
    private bool _allowLan;

    // WFP state (only valid while _isActive)
    private HANDLE _engine = HANDLE.Null;
    private readonly List<ulong> _installedFilterIds = new();
    private bool _isActive;

    // Last observed connected/disconnected state. Polled fresh from ConnectionRoutines.
    // Cannot use NotificationHandler.CurrentConnectionState — that field is only updated
    // by RasConnChangeWaiterTask, which is only spawned if an active RAS connection is
    // present at service startup. With VPN disconnected at boot (the normal case), the
    // watcher never starts and the field stays Uninitialized forever, so observing it
    // never tells us when VPN connects.
    private bool _lastObservedConnected;

    /// <summary>
    /// The currently-running KillSwitchService instance. Set in the constructor; read by
    /// the named-pipe command dispatcher (which is constructed per-thread without DI
    /// parameters and so can't inject the service directly). This is OK because the host
    /// registers KillSwitchService as a singleton hosted service — only one instance ever
    /// exists.
    /// </summary>
    public static KillSwitchService? Current { get; private set; }

    public KillSwitchService(ILogger<KillSwitchService>? logger = null)
    {
        _logger = logger ?? NullLogger<KillSwitchService>.Instance;
        Current = this;
    }

    // -------------------------------------------------------------------------------
    // Public read-only state
    // -------------------------------------------------------------------------------

    public KillSwitchMode Mode { get { lock (_stateLock) return _mode; } }
    public bool           AllowLan { get { lock (_stateLock) return _allowLan; } }
    public bool           IsActive { get { lock (_stateLock) return _isActive; } }

    public KillSwitchStatus GetStatus()
    {
        lock (_stateLock)
        {
            return new KillSwitchStatus { Mode = _mode, AllowLan = _allowLan, IsActive = _isActive };
        }
    }

    // -------------------------------------------------------------------------------
    // Public setters — called via IPC (Phase 3) or directly when in-process.
    // -------------------------------------------------------------------------------

    public void SetMode(KillSwitchMode mode)
    {
        lock (_stateLock)
        {
            if (_mode == mode) return;
            _logger.LogInformation("KillSwitchService.SetMode: {Old} -> {New}", _mode, mode);
            _mode = mode;
            ReevaluateUnsafe();
        }
    }

    public void SetAllowLan(bool allow)
    {
        lock (_stateLock)
        {
            if (_allowLan == allow) return;
            _logger.LogInformation("KillSwitchService.SetAllowLan: {Old} -> {New}", _allowLan, allow);
            _allowLan = allow;
            // If filters are already installed, reinstall to pick up the new LAN setting.
            if (_isActive) ReinstallUnsafe();
        }
    }

    // -------------------------------------------------------------------------------
    // BackgroundService loop — polls observed VPN state and reacts to transitions.
    // 1Hz is plenty for kill-switch responsiveness; state changes already happen
    // through RAS event signalling (NotificationHandler updates CurrentConnectionState
    // on RAS connect/disconnect events), so we just sample what's there.
    // -------------------------------------------------------------------------------

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("KillSwitchService running. Initial mode: {Mode}, AllowLan: {AllowLan}",
            Mode, AllowLan);
        stoppingToken.Register(() =>
        {
            _logger.LogInformation("KillSwitchService stopping; tearing down filters.");
            lock (_stateLock) RemoveFiltersUnsafe();
        });

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var connected = ConnectionRoutines.IsAnyConnectionActive(out _);
                if (connected != _lastObservedConnected)
                {
                    var planned = NotificationHandler.WasDisconnectPlanned;
                    _logger.LogInformation(
                        "KillSwitchService observed VPN connected={Old} -> {New} (WasDisconnectPlanned={Planned})",
                        _lastObservedConnected, connected, planned);
                    _lastObservedConnected = connected;

                    lock (_stateLock) ReevaluateUnsafe();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "KillSwitchService poll loop error");
            }

            try { await Task.Delay(1000, stoppingToken); }
            catch (TaskCanceledException) { break; }
        }
    }

    // -------------------------------------------------------------------------------
    // State machine — must be called under _stateLock.
    // -------------------------------------------------------------------------------

    private void ReevaluateUnsafe()
    {
        if (_mode == KillSwitchMode.Off)
        {
            if (_isActive) RemoveFiltersUnsafe();
            return;
        }

        // Mode == OnConnected. Poll fresh state to handle the case where SetMode is
        // called between polling-loop iterations.
        var connected = _lastObservedConnected;
        var wasPlanned = NotificationHandler.WasDisconnectPlanned;

        if (connected)
        {
            if (!_isActive) InstallFiltersUnsafe();
            return;
        }

        // Disconnected:
        // - If the disconnect was planned (user-initiated), remove filters.
        // - If unplanned (drop), keep filters installed — the kill switch is doing its
        //   job: block traffic until the user reconnects or explicitly turns KS off.
        if (_isActive && wasPlanned) RemoveFiltersUnsafe();
    }

    private void ReinstallUnsafe()
    {
        if (!_isActive) return;
        RemoveFiltersUnsafe();
        InstallFiltersUnsafe();
    }

    private void InstallFiltersUnsafe()
    {
        _logger.LogInformation("KillSwitchService.InstallFiltersUnsafe: opening engine and installing kill-switch filter set.");

        _engine = KillSwitchFilters.OpenDynamicEngine();
        if (_engine == HANDLE.Null)
        {
            _logger.LogError("KillSwitchService.InstallFiltersUnsafe: OpenDynamicEngine returned Null. Aborting install.");
            return;
        }

        if (KillSwitchFilters.EnsureDynamicSublayerRegistered(_engine) != 0)
        {
            _logger.LogError("KillSwitchService.InstallFiltersUnsafe: EnsureDynamicSublayerRegistered failed. Aborting install.");
            KillSwitchFilters.CloseEngine(_engine);
            _engine = HANDLE.Null;
            return;
        }

        // Resolve tunnel LUID (best-effort; if missing, install without tunnel permits — the
        // user will lose all connectivity but block-all is honored).
        ulong? tunnelLuid = null;
        var entryName = ConnectionRoutines.ActiveConnectionEntryName;
        if (!string.IsNullOrEmpty(entryName))
        {
            tunnelLuid = AdapterLuidResolver.FindTunnelLuidByEntryName(entryName)
                      ?? AdapterLuidResolver.FindFirstUpAdapterByDescriptionContains("WAN Miniport (IKEv2)");
        }
        if (tunnelLuid == null)
        {
            _logger.LogWarning("KillSwitchService: tunnel LUID not resolved; tunnel-permit filters will be skipped.");
        }

        if (KillSwitchFilters.BeginTransaction(_engine) != 0)
        {
            _logger.LogError("KillSwitchService.InstallFiltersUnsafe: BeginTransaction failed. Aborting install.");
            KillSwitchFilters.CloseEngine(_engine);
            _engine = HANDLE.Null;
            return;
        }

        try
        {
            // Block-all (weight 1)
            Track(KillSwitchFilters.AddBlockAllOutboundV4(_engine));
            Track(KillSwitchFilters.AddBlockAllInboundV4(_engine));
            Track(KillSwitchFilters.AddBlockAllOutboundV6(_engine));
            Track(KillSwitchFilters.AddBlockAllInboundV6(_engine));

            // LAN permits (weight 2) — opt-in
            if (_allowLan)
            {
                _installedFilterIds.AddRange(KillSwitchFilters.AddPermitLanAll(_engine));
            }

            // DNS block (weight 3) — belt-and-suspenders against future app-id permits
            Track(KillSwitchFilters.AddBlockDnsUdpOutboundV4(_engine));
            Track(KillSwitchFilters.AddBlockDnsTcpOutboundV4(_engine));
            Track(KillSwitchFilters.AddBlockDnsUdpOutboundV6(_engine));
            Track(KillSwitchFilters.AddBlockDnsTcpOutboundV6(_engine));

            // Specific permits (weight 4)
            Track(KillSwitchFilters.AddPermitLoopbackOutboundV4(_engine));
            Track(KillSwitchFilters.AddPermitLoopbackInboundV4(_engine));
            Track(KillSwitchFilters.AddPermitLoopbackOutboundV6(_engine));
            Track(KillSwitchFilters.AddPermitLoopbackInboundV6(_engine));

            Track(KillSwitchFilters.AddPermitDhcpOutboundV4(_engine));
            Track(KillSwitchFilters.AddPermitDhcpInboundV4(_engine));

            if (tunnelLuid is { } luid)
            {
                Track(KillSwitchFilters.AddPermitTunnelLuidOutboundV4(_engine, luid));
                Track(KillSwitchFilters.AddPermitTunnelLuidInboundV4(_engine, luid));
                Track(KillSwitchFilters.AddPermitTunnelLuidOutboundV6(_engine, luid));
                Track(KillSwitchFilters.AddPermitTunnelLuidInboundV6(_engine, luid));

                Track(KillSwitchFilters.AddPermitDnsUdpOnTunnelV4(_engine, luid));
                Track(KillSwitchFilters.AddPermitDnsTcpOnTunnelV4(_engine, luid));
                Track(KillSwitchFilters.AddPermitDnsUdpOnTunnelV6(_engine, luid));
                Track(KillSwitchFilters.AddPermitDnsTcpOnTunnelV6(_engine, luid));
            }

            if (KillSwitchFilters.CommitTransaction(_engine) != 0)
            {
                _logger.LogError("KillSwitchService.InstallFiltersUnsafe: CommitTransaction failed. Engine closing without active state.");
                KillSwitchFilters.CloseEngine(_engine);
                _engine = HANDLE.Null;
                _installedFilterIds.Clear();
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "KillSwitchService.InstallFiltersUnsafe: exception during filter set install. Aborting transaction.");
            KillSwitchFilters.AbortTransaction(_engine);
            KillSwitchFilters.CloseEngine(_engine);
            _engine = HANDLE.Null;
            _installedFilterIds.Clear();
            return;
        }

        _isActive = true;
        _logger.LogInformation("KillSwitchService: kill switch ACTIVE. {Count} filters installed.", _installedFilterIds.Count);
    }

    private void RemoveFiltersUnsafe()
    {
        if (!_isActive && _installedFilterIds.Count == 0 && _engine == HANDLE.Null) return;

        _logger.LogInformation("KillSwitchService.RemoveFiltersUnsafe: tearing down kill switch.");

        if (_engine != HANDLE.Null)
        {
            // Closing the dynamic engine is enough — all filters tear down with the session.
            // We still call DeleteFiltersById first for clean per-filter removal in the common
            // path; if the engine close on the next line is the only thing that runs (e.g.,
            // mid-shutdown), the dynamic session lifecycle still cleans them up.
            try
            {
                if (_installedFilterIds.Count > 0)
                    KillSwitchFilters.DeleteFiltersById(_engine, _installedFilterIds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "KillSwitchService.RemoveFiltersUnsafe: DeleteFiltersById threw; closing engine to force cleanup.");
            }

            KillSwitchFilters.CloseEngine(_engine);
            _engine = HANDLE.Null;
        }

        _installedFilterIds.Clear();
        _isActive = false;
        _logger.LogInformation("KillSwitchService: kill switch INACTIVE.");
    }

    private void Track(ulong filterId)
    {
        if (filterId != 0) _installedFilterIds.Add(filterId);
    }
}
