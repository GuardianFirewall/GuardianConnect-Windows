#pragma once
#include "NativeRoutines.h"
#include "Utility.h"
using namespace System;

namespace NativeRoutines
{
    public ref class ConnectionRoutines
    {
    public:
        static HRASCONN FindAnyActiveConnection();
        static String^ GetEntryNameOfActiveConnection();
        static bool IsAnyConnectionActive(LPCTSTR entryNameOut);
        static HRASCONN ActiveConnectionHandle;
        static wchar_t* ActiveConnectionEntryName;
        static String^ ConnectedEntry;
        static DWORD MakeTheCall(System::String^ givenPhonebookPath, System::String^ entryName);
        static DWORD SetCredentials(LPCTSTR entry_name, LPCTSTR username, LPCTSTR password);
        static DWORD ConnectWithEntry(String^ phoneBookPath, System::String^ entryName);
        static Utility::CheckConnectionResult CheckConnection(System::String^ entry_name);
        static Utility::CheckConnectionResult CheckConnection(System::String^ entry_name, HRASCONN& handle);
        static Utility::CheckConnectionResult GetConnectionState(HRASCONN h_ras_conn);
        static bool DisconnectEntry(System::String^ entryName);
    };
}