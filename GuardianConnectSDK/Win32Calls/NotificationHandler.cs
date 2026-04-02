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

    internal static Utility.CheckConnectionResult CurrentConnectionState;
    internal static HANDLE HRasConnState = HANDLE.Null;

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