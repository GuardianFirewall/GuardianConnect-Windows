#include "pch.h"
#include "NotificationHandling.h"

#include "ConnectionRoutines.h"
#include "PrintRoutines.h"

namespace NativeRoutines
{

    DWORD WINAPI WaiterThread(LPVOID);
    Utility::CheckConnectionResult CurrentConnectionState;
    static HANDLE HRasConnState = NULL;
    HANDLE HandleOfWaiterThread;

    void NotificationHandling::StartConnectionStateWatcher()
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
        Threading::Thread ^waiterThread = gcnew Threading::Thread(gcnew Threading::ThreadStart(WaiterThread));
        waiterThread->Start();
    }

    DWORD NotificationHandling::CreateVPNConnectionChangeEvent()
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



        VPNClientNotifierHandle = CreateEventW(lpSecAttr, true, false, VPNSTATECHANGE_EVT_NAME);
        if (VPNClientNotifierHandle == nullptr)
        {
            PrintRoutines::Output("CreateVPNConnectionChangeEvent(): CreateEventW() for listeners returned error:");
            PrintRoutines::Output(Grd::FormatAString("Error:  {0} ...\n", gcnew array<Object^> {GetLastError()}));
            return GetLastError();
        }
        ResetVPNConnectionChangeEvent(); // Just to be sure
        return SUCCESS;
    }
    
    DWORD NotificationHandling::WaitForVPNConnectionChange(int millis)
    {
        PrintRoutines::Output("WaitForVPNConnectionChange() Entry.");
        PrintRoutines::Output(
            "WaitForVPNConnectionChange() About to sit on VPNClientNotiferHandle...");
        HANDLE localVPNClientNotifierHandle = OpenEventW(SYNCHRONIZE, true,  VPNSTATECHANGE_EVT_NAME);
        if (localVPNClientNotifierHandle == nullptr)
        {
            PrintRoutines::Output("WaitForVPNConnectionChange(): OpenEventW() for listeners returned error:");
            PrintRoutines::Output(Grd::FormatAString("Error:  {0} ...\n", gcnew array<Object^> {GetLastError()}));
        }
        
        DWORD dwRet = WaitForSingleObject(localVPNClientNotifierHandle, millis);
        if (dwRet == -1)
        {
            DWORD gleRet = GetLastError();
            PrintRoutines::Output(Grd::FormatAString( "WaitForVPNConnectionChange() Error returned is {0}", gcnew array<Object^> { gleRet }));
            PrintRoutines::PrintSystemError(gleRet);
        }
        PrintRoutines::Output( "WaitForVPNConnectionChange() Back from waiting.");

        return dwRet;
    }

    void NotificationHandling::ResetVPNConnectionChangeEvent()
    {
        PrintRoutines::Output("Resetting VPNConnectionChangeEvent");

        HANDLE localH = OpenEventW(SYNCHRONIZE | EVENT_MODIFY_STATE, true,  VPNSTATECHANGE_EVT_NAME);
        if (localH == NULL)
        {
            PrintRoutines::Output("ResetVPNConnectionChangeEvent(): OpenEventW() for listeners returned error:");
            PrintRoutines::Output(Grd::FormatAString("Error:  {0} ...\n", gcnew array<Object^> {GetLastError()}));
        }
        
        ResetEvent(localH);
    }

    void NotificationHandling::SetVPNConnectionChangeEvent()
    {
        PrintRoutines::Output("Setting VPNConnectionChangeEvent");
        HANDLE localH = OpenEventW(SYNCHRONIZE | EVENT_MODIFY_STATE, true,  VPNSTATECHANGE_EVT_NAME);
        if (localH == NULL)
        {
            PrintRoutines::Output("SetVPNConnectionChangeEvent(): OpenEventW() for listeners returned error:");
            PrintRoutines::Output(Grd::FormatAString("Error:  {0} ...\n", gcnew array<Object^> {GetLastError()}));
        }
        
        SetEvent(localH);
    }

    Utility::CheckConnectionResult NotificationHandling::GetConnectionState()
    {
        return CurrentConnectionState;
    }

    void NotificationHandling::WaiterThread()
    {
        PrintRoutines::Output("WaiterThread spawned for connection events ...");
        PrintRoutines::Output("WaiterThread: Going to CreateEvent for listeners to sit on...");
        
        VPNClientNotifierHandle = OpenEventW(SYNCHRONIZE, true,  VPNSTATECHANGE_EVT_NAME);
        if (VPNClientNotifierHandle == NULL)
        {
            PrintRoutines::Output("WaiterThread(): OpenEventW() for listeners returned error:");
            PrintRoutines::Output(Grd::FormatAString("Error:  {0} ...\n",
                gcnew array<Object^> {GetLastError()}));
        }
        
        PrintRoutines::Output(Grd::FormatAString("Thread {0} waiting for write event...",
                gcnew array<Object^> { GetCurrentThreadId() }));
    
        DWORD dwWaitResult = WaitForSingleObject( HRasConnState, INFINITE);

        if (dwWaitResult == 0xffffffff)
        {
            DWORD dwLastError = GetLastError();
            PrintRoutines::Output("WaiterThread: Error WAIT_FAILED returned from WaitForSingleObject. Error is: ");
            PrintRoutines::PrintSystemError(dwLastError);
            return;
        }

        LPCTSTR entryNameOut;
        PrintRoutines::Output("WaiterThread received indication that the Ras VPN state has changed.");
        CurrentConnectionState =
            ConnectionRoutines::FindAnyActiveConnection() == nullptr
                ? Utility::CheckConnectionResult::DISCONNECTED
                : Utility::CheckConnectionResult::CONNECTED;
        
        PrintRoutines::Output(
            Grd::FormatAString("WaiterThread: Connection State is NOW {0}.", gcnew array<Object^> { CurrentConnectionState} ));
        BOOL eventSet = SetEvent(VPNClientNotifierHandle);
        if (eventSet == 0)
        {
            PrintRoutines::Output("SetEvent of VPN Listeners Event failed");
            GetLastError();
        }
        
        PrintRoutines::Output("Connection Event Waiter thread now exiting...");
        SetVPNConnectionChangeEvent();
    }
}
