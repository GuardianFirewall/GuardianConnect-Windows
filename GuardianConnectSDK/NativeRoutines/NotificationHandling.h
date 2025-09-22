#pragma once
#include "NativeRoutines.h"
#include "Utility.h"
#include <powrprof.h>

namespace NativeRoutines
{
    public ref class NotificationHandling
    {
    public:
        static bool WasDisconnectPlanned = false;
        static String^ LastKnownConnectedEntry;
        static void StartRasConnectStateWatcher();
        static Utility::CheckConnectionResult GetConnectionState();
        static DWORD WaitForVPNConnectionChange(int millis);
        static DWORD CreateClientNotificationEvent();
        static void NotificationHandling::ResetClientNotificationEvent();
        static void NotificationHandling::SetClientNotificationEvent();
        static HANDLE NotificationHandling::VPNClientNotifierHandle;
//        static void NotificationHandling::RasConnChangeWaiterThread(HANDLE event);
        static String^ lNameOfEventForVPNStateListeners = L"GRDRASCONNLISTENEREVENT";

//        static DEVICE_NOTIFY_CALLBACK_ROUTINE DeviceNotifyCallbackRoutine;
//        static DWORD RegisterForPowerEvents();
//        static void UnregisterFromPowerNotifications();
        
    internal:
        static HRASCONN RasConnectionHandle;
        static void RasConnChangeWaiterThread();
    };
}
