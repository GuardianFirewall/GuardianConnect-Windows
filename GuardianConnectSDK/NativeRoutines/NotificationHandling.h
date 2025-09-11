#pragma once
#include "NativeRoutines.h"
#include "Utility.h"
#include <powrprof.h>

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

        static DEVICE_NOTIFY_CALLBACK_ROUTINE DeviceNotifyCallbackRoutine;
        static DWORD RegisterForPowerEvents();
        static void UnregisterFromPowerNotifications();
        
    internal:
        static HRASCONN RasConnectionHandle;
        static void WaiterThread();
    };
}
