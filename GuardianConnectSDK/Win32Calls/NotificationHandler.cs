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
                Log.Information("StartRasConnectStateWatcher: No active RAS connection found.");
                return;
            }

            // ... else - we need to set the triggers for connection state change
            if (HRasConnState != HANDLE.Null)
            {
                Log.Information("StartRasConnectStateWatcher: RAS connection state watcher already active.");
            }
            else
            {
                HRasConnState = HANDLE.Null;
                SECURITY_ATTRIBUTES sa = new SECURITY_ATTRIBUTES();
                HRasConnState = PInvoke.CreateEvent(&sa, false, false, null);
                if (HRasConnState == HANDLE.Null)
                {
                    Log.Information("StartRasConnectStateWatcher: Failed to create event for RAS connection state.");
                    return;
                }
            }

            var retVal = PInvoke.RasConnectionNotification(
                handleToActiveConnection,
                HRasConnState,
                PInvoke.RASCN_Connection | PInvoke.RASCN_Disconnection);
            if (retVal != 0)
            {
                Log.Information($"StartRasConnectStateWatcher: RasConnectionNotification failed. Error: {retVal}");
                return;
            }

            Log.Information("StartRasConnectStateWatcher: Successfully set RAS connection state notification.");
            Task.Factory.StartNew(RasConnChangeWaiterTask);
            Log.Information("StartRasConnectStateWatcher: RasConnChangeWaiterTask spawned.");
        }

        internal static unsafe void RasConnChangeWaiterTask()
        {
            Log.Information("RasConnChangedWaiterTask spawned for connection events ...");
            Log.Information("RasConnChangedWaiterTask: Setting listener events so that they can react ...");

            VPNServiceNotifierHandle.Set();
            VPNClientNotifierHandle.Set();

            Log.Information("RasConnChangedWaiterTask: Waiting for RASConnectionNotification event ...");
            var retVal = PInvoke.WaitForSingleObject(HRasConnState, PInvoke.INFINITE);

            if (retVal == WAIT_EVENT.WAIT_FAILED)
            {
                Log.Information("RasConnChangedWaiterThread: Error WAIT_FAILED returned from WaitForSingleObject.");
                return;
            }

            Log.Information("RasConnChangedWaiterTask: RAS connection state change detected.");
            // We need to check the connection state now
            CurrentConnectionState = ConnectionRoutines.CheckConnection(LastKnownConnectedEntry);
            Log.Information("RasConnChangedWaiterTask: State after CheckConnection: " +
                            CurrentConnectionState.ToString());

            VPNServiceNotifierHandle.Set();
            VPNClientNotifierHandle.Set();

            Log.Information("RasConnChangedWaiterTask: Service and Client listeners notified.");
            Log.Information("RasConnChangedWaiterTask: Now exiting this thread ...");
        }

        internal static unsafe void CreateListenerNotifyEvents()
        {
#if WIN32
            SECURITY_DESCRIPTOR sd = new SECURITY_DESCRIPTOR();
            PSECURITY_DESCRIPTOR pSecDesc = new PSECURITY_DESCRIPTOR(new IntPtr(&sd));
            var initOk = PInvoke.InitializeSecurityDescriptor(pSecDesc, PInvoke.SECURITY_DESCRIPTOR_REVISION);

            SECURITY_ATTRIBUTES secAttr;
            secAttr = new SECURITY_ATTRIBUTES();
            secAttr.nLength = (uint)sizeof(SECURITY_ATTRIBUTES);
            secAttr.bInheritHandle = false;
            secAttr.lpSecurityDescriptor = pSecDesc;
            var lpSecAttr = &secAttr;

            // Set NULL DACL so that everyone has access
            ACL Dacl = new ACL();
            var pAcl = &Dacl;

            PInvoke.SetSecurityDescriptorDacl(pSecDesc, true, pAcl, false);
            PInvoke.SetSecurityDescriptorSacl(pSecDesc, false, pAcl, false);

            fixed (char* evtNameClient = Common.VPNEVT_NAME_CLIENTSIDE)
            {
                hVPNCliSideEvtHandle = PInvoke.CreateEvent(lpSecAttr, false, false, evtNameClient);
                //SafeWaitHandle swh = new SafeWaitHandle(cliHandle, false);
                //VPNClientNotifierHandle = new EventWaitHandle(swh, true);
            }

            fixed (char* evtNameSvc = Common.VPNEVT_NAME_SVRSIDE)
            {
                hVPNSvrSideEvtHandle = PInvoke.CreateEvent(lpSecAttr, false, false, evtNameSvc);
                //VPNServiceNotifierHandle = svcHandle.SafeWaitHandle;
            }
            //var svcHandle = PInvoke.CreateEvent(lpSecAttr, false, false, Common.VPNEVT_NAME_SVRSIDE);
            //VPNServiceNotifierHandle = svcHandle.SafeWaitHandle;
#else
            var users = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
            var rule = new EventWaitHandleAccessRule(users, EventWaitHandleRights.Synchronize | EventWaitHandleRights.Modify, AccessControlType.Allow);
            var security = new EventWaitHandleSecurity();
            security.AddAccessRule(rule);
            VPNServiceNotifierHandle = new EventWaitHandle(false, EventResetMode.ManualReset, Common.VPNEVT_NAME_SVRSIDE);
            VPNServiceNotifierHandle.SetAccessControl(security);
            VPNServiceNotifierHandle = new EventWaitHandle(false, EventResetMode.ManualReset, Common.VPNEVT_NAME_CLIENTSIDE);
            VPNServiceNotifierHandle.SetAccessControl(security);
#endif
        }

    }
}
