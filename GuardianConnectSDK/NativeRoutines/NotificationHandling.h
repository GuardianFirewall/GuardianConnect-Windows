#pragma once
#include "NativeRoutines.h"
#include "Utility.h"

namespace NativeRoutines
{
    public ref class NotificationHandling
    {
    public:
        static String^ LastKnownConnectedEntry;
        static void StartConnectionStateWatcher();
        static Utility::CheckConnectionResult GetConnectionState();
        static DWORD WaitForVPNConnectionChange(int millis);
        static DWORD CreateVPNConnectionChangeEvent();
        static void NotificationHandling::ResetVPNConnectionChangeEvent();
        static void NotificationHandling::SetVPNConnectionChangeEvent();
        static HANDLE NotificationHandling::VPNClientNotifierHandle;
        static String^ lNameOfEventForVPNStateListeners = L"GRDRASCONNLISTENEREVENT";

    internal:
        static HRASCONN RasConnectionHandle;
        static void WaiterThread();
        
    };
}
