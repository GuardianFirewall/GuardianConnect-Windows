using GuardianConnect.Shared;
using Microsoft.Win32.SafeHandles;
using Serilog;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using Windows.Wdk;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.NetworkManagement.IpHelper;
using Windows.Win32.NetworkManagement.Ndis;
using Windows.Win32.NetworkManagement.Rras;
using Windows.Win32.NetworkManagement.WindowsFilteringPlatform;
using Windows.Win32.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PInvoke = Windows.Win32.PInvoke;

namespace Win32Calls
{
#if FROM_HEADER
{
    public:

//        static DEVICE_NOTIFY_CALLBACK_ROUTINE DeviceNotifyCallbackRoutine;
//        static DWORD RegisterForPowerEvents();
//        static void UnregisterFromPowerNotifications();
        
    internal:
        static void RasConnChangeWaiterThread();
    };
#endif
    public static class NotificationHandler
    {
        private static Microsoft.Extensions.Logging.ILogger _logger = NullLogger.Instance;
        public static Microsoft.Extensions.Logging.ILogger Log
        {
            get
            {
                if (_logger == NullLogger.Instance)
                {
                    _logger = StaticLoggerFactory.CreateLogger("NotificationHandler");
                }
                return _logger;
            }
        }

        public static bool WasDisconnectPlanned = false;
        public static string LastKnownConnectedEntry;
        public static EventWaitHandle? VPNClientNotifierHandle;
        public static EventWaitHandle? VPNServiceNotifierHandle;

        internal static string lNameOfEventForVPNStateListeners = "GRDRASCONNLISTENEREVENT";

        internal static Utility.CheckConnectionResult CurrentConnectionState;
        internal static HANDLE HRasConnState = HANDLE.Null;

        internal static HANDLE hVPNSvrSideEvtHandle;
        internal static HANDLE hVPNCliSideEvtHandle;

        public static unsafe void StartRasConnectStateWatcher()
        {
            HRASCONN handleToActiveConnection = ConnectionRoutines.FindAnyActiveConnection();
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
                SECURITY_ATTRIBUTES sa = new SECURITY_ATTRIBUTES();
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

        internal static unsafe void RasConnChangeWaiterTask()
        {
            Log.LogInformation("RasConnChangedWaiterTask spawned for connection events ...");
            Log.LogInformation("RasConnChangedWaiterTask: Setting listener events so that they can react ...");

            VPNServiceNotifierHandle.Set();
            VPNClientNotifierHandle.Set();

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
                            CurrentConnectionState.ToString());

            VPNServiceNotifierHandle.Set();
            VPNClientNotifierHandle.Set();

            Log.LogInformation("RasConnChangedWaiterTask: Service and Client listeners notified.");
            Log.LogInformation("RasConnChangedWaiterTask: Now exiting this thread ...");
        }

        internal static unsafe void CreateListenerNotifyEvents()
        {
            var users = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
            var rule = new EventWaitHandleAccessRule(users, EventWaitHandleRights.Synchronize | EventWaitHandleRights.Modify, AccessControlType.Allow);
            var security = new EventWaitHandleSecurity();
            security.AddAccessRule(rule);
            VPNServiceNotifierHandle = new EventWaitHandle(false, EventResetMode.ManualReset, Common.VPNEVT_NAME_SVRSIDE);
            VPNServiceNotifierHandle.SetAccessControl(security);
            VPNServiceNotifierHandle = new EventWaitHandle(false, EventResetMode.ManualReset, Common.VPNEVT_NAME_CLIENTSIDE);
            VPNServiceNotifierHandle.SetAccessControl(security);
        }

    }
}
