//#include "pch.h"
#include "NotificationHandling.h"

#include "ConnectionRoutines.h"
#include "PrintRoutines.h"
#include <powerbase.h>
#include <powrprof.h>
#include <powersetting.h>

#include "WFM/VpnDnsHandler.h"

namespace NativeRoutines
{

    DWORD WINAPI WaiterThread(LPVOID);
    Utility::CheckConnectionResult CurrentConnectionState;
    static HANDLE HRasConnState = NULL;
    HANDLE HandleOfWaiterThread;
    HPOWERNOTIFY g_hPowerNotify = NULL; // Store the registration handle

    void NotificationHandling::StartRasConnectStateWatcher()
    {
        // Let's do check first
        HRASCONN handleToActiveConnection = ConnectionRoutines::FindAnyActiveConnection();
        if (handleToActiveConnection == nullptr)
        {
            PrintRoutines::Output("StartConnectionStateWatcher(): - no active connections. Next connection will start watcher.");
            return;
        }

        // ... else - we need to set the triggers for connection state change
        RasConnectionHandle = handleToActiveConnection;

        if (HRasConnState != NULL)
        {
            PrintRoutines::Output("StartConnectionStateWatcher(): CreateEventW() already has handle created. Skipping...");
        } else
        {
            HRasConnState = CreateEventW(NULL, false, false, NULL);
            if (HRasConnState == NULL)
            {
                PrintRoutines::Output("StartConnectionStateWatcher(): CreateEventW() returned error:");
                PrintRoutines::Output(Grd::FormatAString("Error:  {0} ...\n",
                    gcnew array<Object^> { GetLastError()}));
            }
        }

        DWORD dwRet = RasConnectionNotificationW(RasConnectionHandle, HRasConnState, RASCN_Connection | RASCN_Disconnection);

        if (dwRet != ERROR_SUCCESS)
        {
            PrintRoutines::Output("StartConnectionStateWatcher(): ERROR returned from RasConnectionNotificationW()!");
            PrintRoutines::PrintRasError(dwRet);
            return;
        }

        // Now spawn thread to wait
        PrintRoutines::Output("StartConnectionStateWatcher(): Spawning WaiterThread...");
        Threading::Thread ^waiterThread = gcnew Threading::Thread(gcnew Threading::ThreadStart(RasConnChangeWaiterThread));
        waiterThread->Start();
    }

    void NotificationHandling::RasConnChangeWaiterThread()
    {
        PrintRoutines::Output("RasConnChangedWaiterThread spawned for connection events ...");
        PrintRoutines::Output("RasConnChangedWaiterThread: Going to CreateEvent for listeners to sit on...");
        
        VPNClientNotifierHandle = OpenEventW(SYNCHRONIZE, true,  VPNEVENT_CLIENTNOTIFIER);
        if (VPNClientNotifierHandle == NULL)
        {
            PrintRoutines::Output("RasConnChangedWaiterThread(): OpenEventW() for listeners returned error:");
            PrintRoutines::Output(Grd::FormatAString("Error:  {0} ...\n",
                gcnew array<Object^> {GetLastError()}));
        }
        
        PrintRoutines::Output(Grd::FormatAString("Thread {0} waiting for write event...",
                gcnew array<Object^> { GetCurrentThreadId() }));
    
        DWORD dwWaitResult = WaitForSingleObject( HRasConnState, INFINITE);

        if (dwWaitResult == 0xffffffff)
        {
            DWORD dwLastError = GetLastError();
            PrintRoutines::Output("RasConnChangedWaiterThread: Error WAIT_FAILED returned from WaitForSingleObject. Error is: ");
            PrintRoutines::PrintSystemError(dwLastError);
            return;
        }

        PrintRoutines::Output("RasConnChangedWaiterThread received indication that the Ras VPN state has changed.");
        CurrentConnectionState =
            ConnectionRoutines::FindAnyActiveConnection() == nullptr
                ? Utility::CheckConnectionResult::DISCONNECTED
                : Utility::CheckConnectionResult::CONNECTED;
        
        PrintRoutines::Output(
            Grd::FormatAString("RasConnChangedWaiterThread: Connection State is NOW {0}.", gcnew array<Object^> { CurrentConnectionState} ));
#if OLDWAY
        BOOL eventSet = SetEvent(VPNClientNotifierHandle);
        if (eventSet == 0)
        {
            DWORD dwLastError = GetLastError();
            PrintRoutines::Output("SetEvent of VPN Listeners Event failed!");
            PrintRoutines::Output(Grd::FormatAString("Error:  {0:X} ...\n",
                gcnew array<Object^> {dwLastError}));
        }
#else
        if (!WasDisconnectPlanned)
        {
            PrintRoutines::Output("RasConnChangedWaiterThread: Post-wait fallthrough for RasConnState, DISCONNECT WAS NOT PLANNED!!");
            // Put Filter reset here?
            VpnDnsHandler* vdh = new VpnDnsHandler();
            PrintRoutines::Output("RasConnChangedWaiterThread: Post-wait fallthrough for RasConnState: REMOVING WFP FILTERS!!");
            vdh->RemoveFilters(ConnectionRoutines::ConnectedEntry);
            PrintRoutines::Output("RasConnChangedWaiterThread: Post-wait fallthrough for RasConnState: SETTING VPNConnectionChangeEvent !!");
            SetClientNotificationEvent(); // Is this correct??
        }
        PrintRoutines::Output("RasConnChangedWaiterThread: Post-wait fallthrough for RasConnState, calling ResetVPNConnnectionChangeEvent() to prime event...");
//        ResetClientNotificationEvent();
#endif
        
        PrintRoutines::Output("Connection Event Waiter thread now exiting...");
    }
    
    // --------------- Section for Notifying client of a change of the state of a VPN connection

    DWORD NotificationHandling::CreateClientNotificationEvent()
    {
        SECURITY_DESCRIPTOR secDesc;
        bool bInitOk = InitializeSecurityDescriptor(&secDesc, SECURITY_DESCRIPTOR_REVISION);
        PSECURITY_DESCRIPTOR pSecDesc = &secDesc;
        PACL pDacl = NULL;
        SetSecurityDescriptorDacl(pSecDesc, true, pDacl, false);
        SetSecurityDescriptorSacl(pSecDesc, false, pDacl, false);

        SECURITY_ATTRIBUTES secAttr;
        LPSECURITY_ATTRIBUTES lpSecAttr = &secAttr;
        secAttr.nLength = sizeof(secAttr);
        secAttr.lpSecurityDescriptor = pSecDesc;



        VPNClientNotifierHandle = CreateEventW(lpSecAttr, true, false, VPNEVENT_CLIENTNOTIFIER);
        if (VPNClientNotifierHandle == nullptr)
        {
            PrintRoutines::Output("CreateVPNConnectionChangeEvent(): CreateEventW() for listeners returned error:");
            PrintRoutines::Output(Grd::FormatAString("Error:  {0} ...\n", gcnew array<Object^> {GetLastError()}));
            return GetLastError();
        }
        ResetClientNotificationEvent(); // Just to be sure
        return SUCCESS;
    }
    
    void NotificationHandling::ResetClientNotificationEvent()
    {
        PrintRoutines::Output("Resetting ClientNotificationEvent");

        HANDLE localH = OpenEventW(SYNCHRONIZE | EVENT_MODIFY_STATE, true,  VPNEVENT_CLIENTNOTIFIER);
        if (localH == NULL)
        {
            PrintRoutines::Output("ResetClientNotificationEvent(): OpenEventW() for listeners returned error:");
            PrintRoutines::Output(Grd::FormatAString("Error:  {0} ...\n", gcnew array<Object^> {GetLastError()}));
        }
        
        ResetEvent(localH);
    }

    void NotificationHandling::SetClientNotificationEvent()
    {
        PrintRoutines::Output("Setting ClientNotificationEvent");
        HANDLE localH = OpenEventW(SYNCHRONIZE | EVENT_MODIFY_STATE, true,  VPNEVENT_CLIENTNOTIFIER);
        if (localH == NULL)
        {
            PrintRoutines::Output("SetClientNotificationEvent(): OpenEventW() for listeners returned error:");
            PrintRoutines::Output(Grd::FormatAString("Error:  {0} ...\n", gcnew array<Object^> {GetLastError()}));
        }
        
        SetEvent(localH);
    }

    Utility::CheckConnectionResult NotificationHandling::GetConnectionState()
    {
        return CurrentConnectionState;
    }

    // Placeholder for callback from RasConnection Notification Event
}
