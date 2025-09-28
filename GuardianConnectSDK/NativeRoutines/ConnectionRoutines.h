#pragma once
#include "NativeRoutines.h"
#include "Utility.h"
using namespace System;

namespace NativeRoutines
{
    public ref class ConnectionRoutines
    {
    internal:
        static HRASCONN RasConnectionHandle;
    public:
        enum class RasConnState
        {
            OpenPort = 0,
            PortOpened,
            ConnectDevice,
            DeviceConnected,
            AllDevicesConnected,
            Authenticate,
            AuthNotify,
            AuthRetry,
            AuthCallback,
            AuthChangePassword,
            AuthProject,
            AuthLinkSpeed,
            AuthAck,
            ReAuthenticate,
            Authenticated,
            PrepareForCallback,
            WaitForModemReset,
            WaitForCallback,
            Projected,
            SubEntryConnected,
            SubEntryDisconnected,
            ApplySettings,
            Interactive = RASCS_PAUSED,         // You may need to define RASCS_PAUSED as its integer value
            RetryAuthentication,
            CallbackSetByCaller,
            PasswordExpired,
            InvokeEapUI,
            Connected = RASCS_DONE,             // You may need to define RASCS_DONE as its integer value
            Disconnected
        };

        enum class tagRasConnSubState
        {
            RASCSS_None,
            RASCSS_Dormant,
            RASCSS_Reconnecting,
            RASCSS_Reconnected��� = RASCSS_DONE
        };

        ref struct RasTunnEndpointInfo
        {
            int Type; // dwType
            int Id;   // dwId

            RasTunnEndpointInfo() {}
        };

        ref struct RasConnStatusInfo
        {
            tagRASCONNSTATE RasConnState;
            int ErrorCode;
            System::String^ DeviceType;
            System::String^ DeviceName;
            RasTunnEndpointInfo RTEIP4;
            RasTunnEndpointInfo RTEIP6;
            tagRASCONNSUBSTATE RasConnSubState;

            RasConnStatusInfo() {}
        };
        static HRASCONN FindAnyActiveConnection();
        static String^ GetEntryNameOfActiveConnection();
        static bool IsAnyConnectionActive(LPCTSTR entryNameOut);
        static HRASCONN ActiveConnectionHandle;
        static wchar_t* ActiveConnectionEntryName;
        static String^ ConnectedEntry;
        static DWORD MakeTheCall(System::String^ givenPhonebookPath, System::String^ entryName);
//        static DWORD SetCredentials(LPCTSTR entry_name, LPCTSTR username, LPCTSTR password);
        static DWORD ConnectWithEntry(String^ phoneBookPath, System::String^ entryName);
        static Utility::CheckConnectionResult CheckConnection(System::String^ entry_name);
        static Utility::CheckConnectionResult CheckConnection(System::String^ entry_name, HRASCONN& handle);
        static Utility::CheckConnectionResult GetConnectionState(HRASCONN h_ras_conn, LPRASCONNSTATUSW& lp_ras_status);

        static bool DisconnectEntry(System::String^ entryName);

		static Utility::CheckConnectionResult GetConnectionState(IntPtr hRasConn, [System::Runtime::InteropServices::Out] RasConnStatusInfo^% statusInfo);
    };
}