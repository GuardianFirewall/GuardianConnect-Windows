using System.Security.AccessControl;
using System.Security.Principal;
using GuardianConnect.Abstractions;
using GuardianConnect.Shared;
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

    // Last observed connected/disconnected state. Refreshed by:
    //   - one-shot evaluation at service start (in case VPN is already connected at boot)
    //   - NotificationHandler.RasConnectionStateChanged event on every RAS state change
    private bool _lastObservedConnected;

    // Named EventWaitHandle the UI subscribes to for status auto-refresh. Created with
    // Everyone-FullControl so the desktop client (running as the user) can open it.
    // Set() is called whenever observable KS state changes (install, remove, mode flip,
    // allow-LAN flip); UI loops on WaitOne and re-fetches via GetKillSwitchStatus IPC.
    private EventWaitHandle? _statusChangedEvent;

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
        SignalStatusChanged();
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
        SignalStatusChanged();
    }

    private void SignalStatusChanged()
    {
        try
        {
            _statusChangedEvent?.Set();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "KillSwitchService.SignalStatusChanged: Set() threw");
        }
    }

    // -------------------------------------------------------------------------------
    // BackgroundService entry — event-driven. We subscribe to
    // NotificationHandler.RasConnectionStateChanged (the same signal that drives the
    // UI's connection-state indicator) and react when it fires. No polling.
    //
    // We do an initial state evaluation here too, so that if the service starts with
    // VPN already connected (e.g., reboot-with-tunnel-up scenarios) and KS is set to
    // OnConnected, we install filters immediately rather than waiting for the next
    // RAS state change.
    // -------------------------------------------------------------------------------

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("KillSwitchService running. Initial mode: {Mode}, AllowLan: {AllowLan}",
            Mode, AllowLan);

        // Create the status-changed named event with World access so the user-mode UI
        // can open it. Mirrors the approach in VpnManagerService for the VPN state events.
        try
        {
            var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
            var rule = new EventWaitHandleAccessRule(everyone,
                EventWaitHandleRights.FullControl, AccessControlType.Allow);
            var sec = new EventWaitHandleSecurity();
            sec.AddAccessRule(rule);
            _statusChangedEvent = new EventWaitHandle(false, EventResetMode.ManualReset,
                Common.KSEVT_NAME_STATUSCHANGED);
            _statusChangedEvent.SetAccessControl(sec);
            _logger.LogInformation("KillSwitchService: status-changed event created ({Name}).",
                Common.KSEVT_NAME_STATUSCHANGED);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "KillSwitchService: failed to create status-changed event; UI auto-refresh will not work.");
        }

        NotificationHandler.RasConnectionStateChanged += OnRasConnectionStateChanged;

        stoppingToken.Register(() =>
        {
            _logger.LogInformation("KillSwitchService stopping; tearing down filters.");
            NotificationHandler.RasConnectionStateChanged -= OnRasConnectionStateChanged;
            lock (_stateLock) RemoveFiltersUnsafe();
            try { _statusChangedEvent?.Dispose(); } catch { /* best-effort */ }
            _statusChangedEvent = null;
        });

        // Initial sync: handle the boot-with-VPN-already-connected case.
        try
        {
            var connected = ConnectionRoutines.IsAnyConnectionActive(out _);
            _lastObservedConnected = connected;
            _logger.LogInformation(
                "KillSwitchService initial state: VPN connected={Connected}", connected);
            lock (_stateLock) ReevaluateUnsafe();
            SignalStatusChanged();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "KillSwitchService initial state evaluation failed.");
        }

        // ExecuteAsync would normally hold the host running; with no work loop we just
        // wait on cancellation. The event handler does the actual work.
        return Task.Delay(Timeout.Infinite, stoppingToken).ContinueWith(_ => { },
            TaskContinuationOptions.OnlyOnCanceled | TaskContinuationOptions.ExecuteSynchronously);
    }

    private void OnRasConnectionStateChanged(Utility.CheckConnectionResult state)
    {
        try
        {
            // CheckConnectionResult covers more than two values, so resolve to a clean
            // up/down via IsAnyConnectionActive (cheap; just walks RAS connection table).
            var connected = ConnectionRoutines.IsAnyConnectionActive(out _);
            if (connected == _lastObservedConnected) return;

            var planned = NotificationHandler.WasDisconnectPlanned;
            _logger.LogInformation(
                "KillSwitchService observed VPN connected={Old} -> {New} (state={State}, WasDisconnectPlanned={Planned})",
                _lastObservedConnected, connected, state, planned);
            _lastObservedConnected = connected;

            lock (_stateLock) ReevaluateUnsafe();
            SignalStatusChanged();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "KillSwitchService event handler error");
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

        // Mode == OnConnected. Always read fresh state from RAS — _lastObservedConnected
        // can lag reality because the C# event (RasConnectionStateChanged) only fires on
        // transitions AFTER the watcher arms, and the watcher doesn't arm until either
        // service-startup-with-VPN-connected OR a manual Connect command. The flow
        // "service starts disconnected → user connects → user toggles KS on" produces no
        // C# event yet, so without this fresh read SetMode would see stale state and
        // skip the install.
        var connected = ConnectionRoutines.IsAnyConnectionActive(out _);
        _lastObservedConnected = connected;
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

        // Resolve tunnel LUID. Try multiple strategies in order; if all fail, dump every
        // up adapter to the log so we can diagnose what's actually present.
        var entryName = ConnectionRoutines.ActiveConnectionEntryName;
        _logger.LogInformation("KillSwitchService: resolving tunnel LUID (RAS entry name='{Entry}')", entryName);

        ulong? tunnelLuid = null;
        if (!string.IsNullOrEmpty(entryName))
            tunnelLuid = AdapterLuidResolver.FindTunnelLuidByEntryName(entryName);
        tunnelLuid ??= AdapterLuidResolver.FindFirstUpAdapterByDescriptionContains("WAN Miniport (IKEv2)");
        tunnelLuid ??= AdapterLuidResolver.FindFirstUpPppAdapter();

        if (tunnelLuid == null)
        {
            _logger.LogWarning(
                "KillSwitchService: tunnel LUID not resolved by any strategy. Tunnel-permit filters " +
                "will be skipped — block-all will block ALL traffic including tunnel-bound. " +
                "Diagnostic dump of up adapters follows so we can fix the resolver.");
            _logger.LogWarning(AdapterLuidResolver.DumpUpAdapters());
        }
        else
        {
            _logger.LogInformation("KillSwitchService: using tunnel LUID 0x{Luid:X16}", tunnelLuid.Value);
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

            // IKEv2 transport permits — required for the tunnel itself to stay alive.
            // Without these, keepalives hit block-all and the IPSec SA dies within ~30s.
            Track(KillSwitchFilters.AddPermitIkeOutboundV4(_engine));
            Track(KillSwitchFilters.AddPermitIkeNatTOutboundV4(_engine));

            // IPSec tunnel transport — permit the encrypted carrier packets (IP-in-IP and
            // ESP) outbound on the physical NIC. Native Windows IKEv2 RAS exposes these
            // outer packets to ALE_AUTH_CONNECT_V4 with LOCAL_INTERFACE=physical-NIC, so
            // the tunnel-LUID permit doesn't catch them. Without this, app traffic gets
            // routed through the tunnel adapter, gets encrypted, and then the encrypted
            // packets are blocked on their way out the physical NIC — tunnel transport
            // dies and nothing flows. (Wireguard doesn't need this because Wintun handles
            // encryption in user mode.)
            Track(KillSwitchFilters.AddPermitIpInIpOutboundV4(_engine));
            Track(KillSwitchFilters.AddPermitEspOutboundV4(_engine));

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
