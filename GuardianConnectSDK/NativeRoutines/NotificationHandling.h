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
        static DWORD CreateListenerNotifyEvents();
        static HANDLE NotificationHandling::VPNClientNotifierHandle;
        static HANDLE NotificationHandling::VPNServiceNotifierHandle;
        static String^ lNameOfEventForVPNStateListeners = L"GRDRASCONNLISTENEREVENT";

//        static DEVICE_NOTIFY_CALLBACK_ROUTINE DeviceNotifyCallbackRoutine;
//        static DWORD RegisterForPowerEvents();
//        static void UnregisterFromPowerNotifications();
        
    internal:
        static void RasConnChangeWaiterThread();
    };
}
