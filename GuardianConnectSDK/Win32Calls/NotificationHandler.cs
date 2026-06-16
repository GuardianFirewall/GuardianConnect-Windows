using System.Net;
using System.Security.AccessControl;
using System.Security.Principal;
using Windows.Win32.Foundation;
using Windows.Win32.NetworkManagement.Rras;
using Windows.Win32.Security;
using GuardianConnect.Shared;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PInvoke = Windows.Win32.PInvoke;

namespace Win32Calls;
public static class NotificationHandler
{
    private static ILogger _logger = NullLogger.Instance;

    public static bool WasDisconnectPlanned = false;
    public static string LastKnownConnectedEntry = string.Empty;
    public static EventWaitHandle? VPNClientNotifierHandle;
    public static EventWaitHandle? VPNServiceNotifierHandle;

    internal static string lNameOfEventForVPNStateListeners = "GRDRASCONNLISTENEREVENT";

    public static Utility.CheckConnectionResult CurrentConnectionState;
    internal static HANDLE HRasConnState = HANDLE.Null;

    /// <summary>
    /// Fired by RasConnChangeWaiterTask after CurrentConnectionState has been
    /// refreshed from a RAS state-change notification. Subscribers receive the
    /// freshly-observed state. Same trigger as VPNServiceNotifierHandle/
    /// VPNClientNotifierHandle, just exposed as a managed event so in-process
    /// consumers (like KillSwitchService) don't have to wrestle with named-event
    /// reset semantics across multiple waiters.
    /// </summary>
    public static event Action<Utility.CheckConnectionResult>? RasConnectionStateChanged;

    /// <summary>
    /// True while a WireGuard tunnel is up. Maintained by VpnTunnelManager
    /// (start/stop flips this flag). Distinct from RAS state because Wintun
    /// adapters don't appear in the RAS connection table — so
    /// ConnectionRoutines.IsAnyConnectionActive can't see WG.
    /// </summary>
    public static bool IsWireGuardConnected;

    /// <summary>
    /// The resolved WireGuard server endpoint (IP + port) of the active tunnel,
    /// or null when no WG tunnel is up. Set by VpnTunnelManager on tunnel up,
    /// cleared on tunnel down.
    ///
    /// KillSwitchService reads this to install a tightly-scoped carrier permit:
    /// WireGuard encrypts its payload and sends the encrypted UDP to the server
    /// FROM THE PHYSICAL NIC, where it hits ALE_AUTH_CONNECT and is dropped by
    /// the kill switch's block-all unless explicitly permitted (the WG analog of
    /// the IKE/ESP/IP-in-IP permits the IKEv2 path already has). Permitting only
    /// UDP to this exact server IP:port keeps the off-tunnel leak surface at zero.
    /// Without it, KS-on + WG blocks the tunnel's own carrier and the user loses
    /// all connectivity 
    /// </summary>
    public static IPEndPoint? WireGuardServerEndpoint;

    /// <summary>
    /// Fired by VpnTunnelManager when the WG tunnel comes up or down. Parallel
    /// to RasConnectionStateChanged for the WG transport. Subscribers that
    /// care about "is any VPN transport up" (e.g., KillSwitchService) must
    /// subscribe to BOTH events because the two transports are mutually
    /// exclusive at runtime but use disjoint OS plumbing.
    /// </summary>
    public static event Action<bool>? WireGuardConnectionStateChanged;

    /// <summary>
    /// Internal hook used by VpnTunnelManager to publish a WG state transition.
    /// Updates the IsWireGuardConnected flag and fans out to subscribers.
    /// Swallows subscriber exceptions so one bad handler doesn't take down
    /// the publisher.
    /// </summary>
    public static void RaiseWireGuardConnectionStateChanged(bool isConnected)
    {
        IsWireGuardConnected = isConnected;
        try
        {
            WireGuardConnectionStateChanged?.Invoke(isConnected);
        }
        catch (Exception ex)
        {
            Log.LogError(ex, "WireGuardConnectionStateChanged subscriber threw");
        }
    }

    internal static HANDLE hVPNSvrSideEvtHandle;
    internal static HANDLE hVPNCliSideEvtHandle;

    public static ILogger Log
    {
        get
        {
            if (_logger == NullLogger.Instance) _logger = StaticLoggerFactory.CreateLogger("NotificationHandler");
            return _logger;
        }
    }

    public static unsafe void StartRasConnectStateWatcher()
    {
        var handleToActiveConnection = ConnectionRoutines.FindAnyActiveConnection();
        if (handleToActiveConnection == HRASCONN.Null)
        {
            Log.LogInformation("StartRasConnectStateWatcher: No active RAS connection found.");
            return;
        }

        // ... else - we need to set the triggers for connection state change
        if (HRasConnState != HANDLE.Null)
        {
            Log.LogInformation("StartRasConnectStateWatcher: RAS connection state watcher already active.");
        }
        else
        {
            HRasConnState = HANDLE.Null;
            var sa = new SECURITY_ATTRIBUTES();
            HRasConnState = PInvoke.CreateEvent(&sa, false, false, null);
            if (HRasConnState == HANDLE.Null)
            {
                Log.LogInformation("StartRasConnectStateWatcher: Failed to create event for RAS connection state.");
                return;
            }
        }

        var retVal = PInvoke.RasConnectionNotification(
            handleToActiveConnection,
            HRasConnState,
            PInvoke.RASCN_Connection | PInvoke.RASCN_Disconnection);
        if (retVal != 0)
        {
            Log.LogInformation($"StartRasConnectStateWatcher: RasConnectionNotification failed. Error: {retVal}");
            return;
        }

        Log.LogInformation("StartRasConnectStateWatcher: Successfully set RAS connection state notification.");
        Task.Factory.StartNew(RasConnChangeWaiterTask);
        Log.LogInformation("StartRasConnectStateWatcher: RasConnChangeWaiterTask spawned.");
    }

    internal static void RasConnChangeWaiterTask()
    {
        Log.LogInformation("RasConnChangedWaiterTask spawned for connection events ...");
        Log.LogInformation("RasConnChangedWaiterTask: Setting listener events so that they can react ...");

        VPNServiceNotifierHandle?.Set();
        VPNClientNotifierHandle?.Set();

        // Fire the C# event at watcher arm time too, mirroring the named events.
        // Use case: user reconnects VPN while KS is already on. ConnectToVpnLongRunning
        // calls StartRasConnectStateWatcher → this task spawns. Without firing the C#
        // event here, KillSwitchService wouldn't notice the reconnect until the NEXT
        // state change (the watcher is one-shot per arm). Subscribers re-fetch state
        // anyway, so passing CurrentConnectionState (possibly stale) is fine.
        try
        {
            RasConnectionStateChanged?.Invoke(CurrentConnectionState);
        }
        catch (Exception ex)
        {
            Log.LogError(ex, "RasConnChangedWaiterTask: subscriber threw on watcher-arm notification.");
        }

        Log.LogInformation("RasConnChangedWaiterTask: Waiting for RASConnectionNotification event ...");
        var retVal = PInvoke.WaitForSingleObject(HRasConnState, PInvoke.INFINITE);

        if (retVal == WAIT_EVENT.WAIT_FAILED)
        {
            Log.LogInformation("RasConnChangedWaiterThread: Error WAIT_FAILED returned from WaitForSingleObject.");
            return;
        }

        Log.LogInformation("RasConnChangedWaiterTask: RAS connection state change detected.");
        // We need to check the connection state now
        CurrentConnectionState = ConnectionRoutines.CheckConnection(LastKnownConnectedEntry);
        Log.LogInformation("RasConnChangedWaiterTask: State after CheckConnection: " +
                           CurrentConnectionState);

        VPNServiceNotifierHandle?.Set();
        VPNClientNotifierHandle?.Set();

        try
        {
            RasConnectionStateChanged?.Invoke(CurrentConnectionState);
        }
        catch (Exception ex)
        {
            Log.LogError(ex, "RasConnChangedWaiterTask: subscriber threw while handling state change.");
        }

        Log.LogInformation("RasConnChangedWaiterTask: Service and Client listeners notified.");
        Log.LogInformation("RasConnChangedWaiterTask: Now exiting this thread ...");
    }

    internal static void CreateListenerNotifyEvents()
    {
        var users = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        var rule = new EventWaitHandleAccessRule(users,
            EventWaitHandleRights.Synchronize | EventWaitHandleRights.Modify, AccessControlType.Allow);
        var security = new EventWaitHandleSecurity();
        security.AddAccessRule(rule);
        VPNServiceNotifierHandle = new EventWaitHandle(false, EventResetMode.ManualReset, Common.VPNEVT_NAME_SVRSIDE);
        VPNServiceNotifierHandle.SetAccessControl(security);
        VPNServiceNotifierHandle =
            new EventWaitHandle(false, EventResetMode.ManualReset, Common.VPNEVT_NAME_CLIENTSIDE);
        VPNServiceNotifierHandle.SetAccessControl(security);
    }
}