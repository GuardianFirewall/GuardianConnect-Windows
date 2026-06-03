using System.Net;
using System.Net.Sockets;
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

    // Connecting-overlay state (wg-alpha.35) — separate ID list so the overlay can be
    // installed / removed independently of the base KS filter set. Watchdog timer
    // auto-exits the overlay if EnterConnectingMode isn't paired with an explicit
    // ExitConnectingMode call (e.g., client process crashes mid-negotiate).
    private readonly List<ulong> _connectingOverlayFilterIds = new();
    private bool _isConnecting;
    private DateTime _connectingDeadlineUtc = DateTime.MaxValue;
    private System.Threading.Timer? _connectingWatchdog;
    private const int ConnectingOverlayTimeoutSeconds = 60;

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
        // Snapshot under lock so what we publish to HKLM matches what we signal about.
        bool isActive;
        KillSwitchMode mode;
        bool allowLan;
        lock (_stateLock)
        {
            isActive = _isActive;
            mode = _mode;
            allowLan = _allowLan;
        }

        // Publish to HKLM so the UI watcher can read state without an IPC call. The
        // service runs as SYSTEM (write access to HKLM); the per-user UI reads it.
        try
        {
            RegistrySettings.UpdateGuardianMachineSetting(Common.kKillSwitchActiveRegValue, isActive ? "1" : "0");
            RegistrySettings.UpdateGuardianMachineSetting(Common.kKillSwitchModeRegValue, mode.ToString());
            RegistrySettings.UpdateGuardianMachineSetting(Common.kKillSwitchAllowLanRegValue, allowLan ? "1" : "0");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "KillSwitchService.SignalStatusChanged: HKLM publish threw");
        }

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
        // Restore user-intent state from HKLM BEFORE the initial logging
        // line so the "Initial mode: X" output reflects what we'll
        // actually operate with — not the field defaults. SignalStatusChanged
        // writes these on every state change, so any prior session's intent
        // survives service restart / install / boot.
        RestorePersistedStateFromHklm();

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
        NotificationHandler.WireGuardConnectionStateChanged += OnWireGuardConnectionStateChanged;

        stoppingToken.Register(() =>
        {
            _logger.LogInformation("KillSwitchService stopping; tearing down filters.");
            NotificationHandler.RasConnectionStateChanged -= OnRasConnectionStateChanged;
            NotificationHandler.WireGuardConnectionStateChanged -= OnWireGuardConnectionStateChanged;
            lock (_stateLock) RemoveFiltersUnsafe();
            try { _statusChangedEvent?.Dispose(); } catch { /* best-effort */ }
            _statusChangedEvent = null;
        });

        // Initial sync: handle the boot-with-VPN-already-connected case.
        try
        {
            var connected = IsAnyTransportConnected();
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

    /// <summary>
    /// WireGuard-side analog of OnRasConnectionStateChanged. Fired by
    /// VpnTunnelManager via NotificationHandler.WireGuardConnectionStateChanged
    /// on tunnel up / tunnel down. Re-evaluates filter state the same way the
    /// RAS handler does.
    /// </summary>
    private void OnWireGuardConnectionStateChanged(bool wgConnected)
    {
        try
        {
            var connected = IsAnyTransportConnected();
            if (connected == _lastObservedConnected) return;

            var planned = NotificationHandler.WasDisconnectPlanned;
            _logger.LogInformation(
                "KillSwitchService observed VPN connected={Old} -> {New} (WG event, wgConnected={Wg}, WasDisconnectPlanned={Planned})",
                _lastObservedConnected, connected, wgConnected, planned);
            _lastObservedConnected = connected;

            lock (_stateLock) ReevaluateUnsafe();
            SignalStatusChanged();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "KillSwitchService WG event handler error");
        }
    }

    private void OnRasConnectionStateChanged(Utility.CheckConnectionResult state)
    {
        try
        {
            // CheckConnectionResult covers more than two values, so resolve to a clean
            // up/down. Use IsAnyTransportConnected so a concurrent WG tunnel still
            // counts as connected when a RAS transition is fired (e.g., a stale
            // RAS notification firing while WG is the active transport).
            var connected = IsAnyTransportConnected();
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

    /// <summary>
    /// Reads the user-intent values (KillSwitchActiveMode + KillSwitchActiveAllowLan)
    /// the service published to HKLM\Software\GuardianFirewall on its last
    /// SignalStatusChanged. Without this, _mode resets to KillSwitchMode.Off
    /// on every service restart (install / reboot / crash recovery) and the
    /// user's Kill Switch silently downgrades to off until they re-toggle.
    /// We only restore intent fields (_mode, _allowLan); _isActive is recomputed
    /// by ReevaluateUnsafe from the current connection state.
    /// </summary>
    private void RestorePersistedStateFromHklm()
    {
        try
        {
            var modeText = RegistrySettings.RetrieveGuardianMachineSetting(Common.kKillSwitchModeRegValue);
            if (!string.IsNullOrEmpty(modeText) && Enum.TryParse<KillSwitchMode>(modeText, out var mode))
            {
                _mode = mode;
                _logger.LogInformation(
                    "KillSwitchService: restored persisted mode '{Mode}' from HKLM", mode);
            }

            var allowLanText = RegistrySettings.RetrieveGuardianMachineSetting(Common.kKillSwitchAllowLanRegValue);
            if (!string.IsNullOrEmpty(allowLanText))
            {
                _allowLan = allowLanText == "1";
                _logger.LogInformation(
                    "KillSwitchService: restored persisted AllowLan '{AllowLan}' from HKLM", _allowLan);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "KillSwitchService: failed to restore persisted state from HKLM");
        }
    }

    /// <summary>
    /// True if either the RAS connection table reports an active connection
    /// (IKEv2 path) OR NotificationHandler.IsWireGuardConnected is set
    /// (WireGuard path). The RAS table never sees Wintun adapters so a bare
    /// IsAnyConnectionActive() returns false when only WG is up; we'd miss
    /// installing kill-switch filters entirely.
    /// </summary>
    private static bool IsAnyTransportConnected()
    {
        return ConnectionRoutines.IsAnyConnectionActive(out _)
            || NotificationHandler.IsWireGuardConnected;
    }

    private void ReevaluateUnsafe()
    {
        if (_mode == KillSwitchMode.Off)
        {
            if (_isActive) RemoveFiltersUnsafe();
            return;
        }

        // Mode == OnConnected. Always read fresh state — _lastObservedConnected
        // can lag reality because the C# events (RasConnectionStateChanged /
        // WireGuardConnectionStateChanged) only fire on transitions AFTER the
        // respective watcher arms. The flow "service starts disconnected →
        // user connects → user toggles KS on" produces no event yet, so
        // without this fresh read SetMode would see stale state and skip
        // the install.
        var connected = IsAnyTransportConnected();
        _lastObservedConnected = connected;

        // Treat power-transition drops (suspend / resume) as planned, even if the RAS
        // notification reports them as unplanned. Without this, the user wakes from sleep
        // with filters still installed, the resume reconnect can't resolve DNS (DNS-block
        // matches outbound on the physical NIC), and they're stuck without internet until
        // they manually disable KS. Filters reinstall once VPN is back up via the normal
        // RasConnectionStateChanged path.
        var wasPlanned = NotificationHandler.WasDisconnectPlanned
                      || ServicePowerEventsHandler.IsInPowerTransition;

        if (connected)
        {
            if (!_isActive)
            {
                InstallFiltersUnsafe();
            }
            else
            {
                // Already-active path: tunnel came up while KS was already
                // engaged from a prior session (unplanned-drop-then-reconnect).
                // The current filter set is scoped to the PRIOR tunnel's LUID,
                // which is now stale (adapter destroyed). The tunnel-LUID-scoped
                // permits match nothing → new tunnel's traffic gets caught by
                // block-all. Rebuild filters with the fresh LUID.
                _logger.LogInformation(
                    "KillSwitchService: tunnel came up while KS already active; rebuilding filter set with fresh tunnel LUID.");
                ReinstallUnsafe();
            }
            // Connecting-overlay (if any) is now redundant — the new filter set's
            // tunnel-LUID-scoped DNS permit handles DNS via the tunnel. Clear it
            // promptly rather than waiting for the watchdog.
            if (_isConnecting) ExitConnectingModeUnsafe();
            return;
        }

        // Disconnected:
        // - If the disconnect was planned (user clicked Disconnect, or
        //   transitive equivalents via transport-switch / region-change /
        //   logout), remove filters. Power-transition suspend is also
        //   treated as planned for this purpose (see comment above).
        // - If unplanned (drop / server outage / network blip), KEEP filters
        //   installed. This is the kill switch doing its job — block all
        //   traffic until the user explicitly opts out (Disconnect / KS off /
        //   service restart / system reboot).
        //
        // The rock-and-hard-place bug (CJ+TJE 2026-05-28) — where the next
        // Connect attempt failed because the DNS-block + stale-LUID DNS-permit
        // left no DNS path for the negotiate call — is solved by the new
        // EnterConnectingMode / ExitConnectingMode overlay below, NOT by
        // tearing down filters on unplanned drop. wg-alpha.34 mistakenly
        // tore down filters; backed out in wg-alpha.35.
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
        // WG adapter: VpnTunnelManager creates it with a deterministic alias.
        // Tried last so the IKEv2 strategies above keep priority when both
        // protocols' adapters happen to coexist briefly (e.g., during a
        // transport-switch handoff).
        tunnelLuid ??= AdapterLuidResolver.FindFirstUpAdapterByAlias("GuardianWireGuard");

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

            // WireGuard carrier permit — the WG analog of the IKE/ESP permits above.
            // WG encrypts in user/kernel mode and sends the encrypted UDP to the server
            // endpoint out the PHYSICAL NIC, where block-all drops it unless permitted.
            // Scope it as tight as possible: UDP to exactly the resolved server IP:port
            // (published by VpnTunnelManager). Without this, KS-on + WG = no internet.
            AddWireGuardCarrierPermitUnsafe();

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

                // ICMP gap closer at OUTBOUND_IPPACKET layer. ALE_AUTH_CONNECT often
                // doesn't fire for stateless ICMP, so the ALE block-all misses ping/
                // traceroute leaks. Block ICMP outbound where local interface != tunnel.
                Track(KillSwitchFilters.AddBlockNonTunnelIcmpOutboundV4(_engine, luid));
                Track(KillSwitchFilters.AddBlockNonTunnelIcmpOutboundV6(_engine, luid));
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

    /// <summary>
    /// Install the WireGuard encrypted-carrier permit when a WG tunnel is active.
    /// Must be called inside the install transaction (uses _engine + Track). No-op
    /// for the IKEv2 transport (no WG endpoint published). Scopes the permit to UDP
    /// to exactly the resolved server IP:port — the only off-tunnel traffic the kill
    /// switch allows is the carrier reaching the VPN server.
    /// </summary>
    private void AddWireGuardCarrierPermitUnsafe()
    {
        var endpoint = NotificationHandler.WireGuardServerEndpoint;
        if (endpoint is null)
        {
            // Expected for IKEv2. But if WG reports connected with no endpoint, the
            // carrier permit is missing and the tunnel will be blocked — loud warning.
            if (NotificationHandler.IsWireGuardConnected)
                _logger.LogWarning(
                    "KillSwitchService: WG is connected but WireGuardServerEndpoint is null; " +
                    "carrier permit NOT installed — tunnel traffic will be blocked. This is a bug.");
            return;
        }

        var port = (ushort)endpoint.Port;
        var addrBytes = endpoint.Address.GetAddressBytes(); // network byte order

        if (endpoint.Address.AddressFamily == AddressFamily.InterNetwork)
        {
            // Host byte order for FWP_V4_ADDR_AND_MASK (matches AddPermitV4Subnet convention).
            uint hostOrder = (uint)((addrBytes[0] << 24) | (addrBytes[1] << 16) |
                                    (addrBytes[2] << 8) | addrBytes[3]);
            Track(KillSwitchFilters.AddPermitWireGuardCarrierOutboundV4(_engine, hostOrder, port));
            _logger.LogInformation(
                "KillSwitchService: installed WireGuard carrier permit (UDP -> {Endpoint}).", endpoint);
        }
        else if (endpoint.Address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            Track(KillSwitchFilters.AddPermitWireGuardCarrierOutboundV6(_engine, addrBytes, port));
            _logger.LogInformation(
                "KillSwitchService: installed WireGuard carrier permit (UDP -> {Endpoint}).", endpoint);
        }
        else
        {
            _logger.LogWarning(
                "KillSwitchService: WireGuardServerEndpoint has unexpected address family {Family}; " +
                "carrier permit NOT installed.", endpoint.Address.AddressFamily);
        }
    }

    private void RemoveFiltersUnsafe()
    {
        if (!_isActive && _installedFilterIds.Count == 0 && _engine == HANDLE.Null) return;

        _logger.LogInformation("KillSwitchService.RemoveFiltersUnsafe: tearing down kill switch.");

        // Overlay rides on top of the base filter set in the same dynamic engine,
        // so closing the engine kills overlay filters too. Clear overlay tracking
        // state explicitly so a subsequent EnterConnectingMode (with KS off) doesn't
        // think there are stale overlay filters to remove.
        if (_isConnecting || _connectingOverlayFilterIds.Count > 0)
        {
            ExitConnectingModeUnsafe();
        }

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

    // -------------------------------------------------------------------------------
    // Connecting-mode overlay (wg-alpha.35) — temporary DNS + HTTPS permits installed
    // during a user-initiated Connect attempt so the credential-negotiate machinery in
    // the client process can resolve Guardian API hostnames and complete HTTP calls
    // while the regular KS filter set (block-all + DNS-block) keeps protecting the
    // rest of traffic.
    //
    // Lifecycle:
    //   - Client calls EnterConnectingMode IPC before its first negotiate HTTP call
    //     (ConnectButtonCommand path in the UI).
    //   - Service installs overlay permits (UDP/TCP/53 + TCP/443 outbound, unscoped)
    //     at WeightSpecificPermit (4), beating the DNS-block (3) and block-all (1).
    //   - Negotiate completes, client sends StartVPNConnection, tunnel comes up.
    //   - ReevaluateUnsafe detects connected=true and the InstallFiltersUnsafe path
    //     rebuilds the base set; ExitConnectingModeUnsafe runs implicitly to remove
    //     overlay since tunnel-LUID permits handle DNS naturally from here.
    //   - On client-side failure (negotiate threw, user closed app, etc.), the
    //     watchdog timer auto-exits the overlay after ConnectingOverlayTimeoutSeconds.
    //   - Client can also call ExitConnectingMode IPC explicitly on error paths for
    //     prompt teardown without waiting for the watchdog.
    //
    // Leak surface during the open window: DNS + HTTPS to any destination via any
    // local interface. Bounded by the negotiate's natural duration (typically
    // seconds) and capped by the watchdog. The user explicitly clicked Connect,
    // so they've signaled "I want connectivity to come back" — consistent with
    // intent. The block-all WFP filter still catches non-DNS, non-443 traffic, so
    // general apps stay blocked.
    // -------------------------------------------------------------------------------

    public void EnterConnectingMode()
    {
        lock (_stateLock)
        {
            EnterConnectingModeUnsafe();
        }
        SignalStatusChanged();
    }

    public void ExitConnectingMode()
    {
        lock (_stateLock)
        {
            ExitConnectingModeUnsafe();
        }
        SignalStatusChanged();
    }

    private void EnterConnectingModeUnsafe()
    {
        // Idempotent: if overlay already installed, refresh the deadline so a fresh
        // EnterConnectingMode call extends the window. (Useful if the client retries
        // the negotiate after a transient failure within the same connect session.)
        _connectingDeadlineUtc = DateTime.UtcNow.AddSeconds(ConnectingOverlayTimeoutSeconds);

        if (_isConnecting)
        {
            _logger.LogInformation("KillSwitchService.EnterConnectingMode: overlay already installed; deadline refreshed to {Deadline:o}.", _connectingDeadlineUtc);
            return;
        }

        // No overlay needed when KS isn't blocking anything anyway.
        if (!_isActive)
        {
            _logger.LogInformation("KillSwitchService.EnterConnectingMode: KS not active; no overlay needed.");
            return;
        }

        if (_engine == HANDLE.Null)
        {
            _logger.LogWarning("KillSwitchService.EnterConnectingMode: _isActive but engine handle is null. Skipping overlay install.");
            return;
        }

        _logger.LogInformation("KillSwitchService.EnterConnectingMode: installing DNS + HTTPS overlay permits (deadline {Deadline:o}).", _connectingDeadlineUtc);

        try
        {
            if (KillSwitchFilters.BeginTransaction(_engine) != 0)
            {
                _logger.LogError("KillSwitchService.EnterConnectingMode: BeginTransaction failed; overlay not installed.");
                return;
            }

            TrackOverlay(KillSwitchFilters.AddPermitDnsUdpAnyOutboundV4(_engine));
            TrackOverlay(KillSwitchFilters.AddPermitDnsTcpAnyOutboundV4(_engine));
            TrackOverlay(KillSwitchFilters.AddPermitDnsUdpAnyOutboundV6(_engine));
            TrackOverlay(KillSwitchFilters.AddPermitDnsTcpAnyOutboundV6(_engine));
            TrackOverlay(KillSwitchFilters.AddPermitHttpsAnyOutboundV4(_engine));
            TrackOverlay(KillSwitchFilters.AddPermitHttpsAnyOutboundV6(_engine));

            if (KillSwitchFilters.CommitTransaction(_engine) != 0)
            {
                _logger.LogError("KillSwitchService.EnterConnectingMode: CommitTransaction failed; overlay aborted.");
                _connectingOverlayFilterIds.Clear();
                return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "KillSwitchService.EnterConnectingMode: exception during overlay install. Aborting.");
            try { KillSwitchFilters.AbortTransaction(_engine); } catch { /* best-effort */ }
            _connectingOverlayFilterIds.Clear();
            return;
        }

        _isConnecting = true;
        _logger.LogInformation("KillSwitchService.EnterConnectingMode: {Count} overlay filters installed.", _connectingOverlayFilterIds.Count);

        // Arm watchdog. Single-shot Timer; we dispose+re-arm on each EnterConnectingMode call so a
        // refreshed deadline (idempotent re-entry) doesn't fire prematurely from a stale schedule.
        _connectingWatchdog?.Dispose();
        _connectingWatchdog = new System.Threading.Timer(WatchdogFire, null,
            TimeSpan.FromSeconds(ConnectingOverlayTimeoutSeconds), Timeout.InfiniteTimeSpan);
    }

    private void ExitConnectingModeUnsafe()
    {
        if (!_isConnecting && _connectingOverlayFilterIds.Count == 0)
        {
            _connectingDeadlineUtc = DateTime.MaxValue;
            return;
        }

        _logger.LogInformation("KillSwitchService.ExitConnectingMode: removing overlay permits ({Count} filters).", _connectingOverlayFilterIds.Count);

        if (_engine != HANDLE.Null && _connectingOverlayFilterIds.Count > 0)
        {
            try
            {
                KillSwitchFilters.DeleteFiltersById(_engine, _connectingOverlayFilterIds);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "KillSwitchService.ExitConnectingMode: DeleteFiltersById threw. Overlay IDs cleared anyway; remaining filters will tear down with the engine.");
            }
        }

        _connectingOverlayFilterIds.Clear();
        _isConnecting = false;
        _connectingDeadlineUtc = DateTime.MaxValue;

        _connectingWatchdog?.Dispose();
        _connectingWatchdog = null;
    }

    private void TrackOverlay(ulong filterId)
    {
        if (filterId != 0) _connectingOverlayFilterIds.Add(filterId);
    }

    private void WatchdogFire(object? state)
    {
        // Timer fires on a thread-pool thread; re-acquire the state lock before
        // touching shared state. If ExitConnectingMode was called in the meantime,
        // _isConnecting is false and ExitConnectingModeUnsafe is a no-op.
        lock (_stateLock)
        {
            if (!_isConnecting) return;
            _logger.LogWarning(
                "KillSwitchService: connecting-overlay watchdog fired after {Timeout}s — no ExitConnectingMode received. Auto-removing overlay.",
                ConnectingOverlayTimeoutSeconds);
            ExitConnectingModeUnsafe();
        }
        SignalStatusChanged();
    }
}
