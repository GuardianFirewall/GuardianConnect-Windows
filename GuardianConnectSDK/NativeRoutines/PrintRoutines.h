#pragma once
#include "NativeRoutines.h"
#include "RasBaseRoutines.h"

namespace NativeRoutines
{
    public ref class PrintRoutines
    {
    public:
        static bool LoggingInitialized;
        static String^ logFilePath;
        static void PrintRasError(DWORD error);
        static void PrintSystemError(DWORD error);
        int PrintConnectionDetails(HRASCONN connection);
        int PrintRoutines::PrintConnections();
        int PrintRoutines::PrintDevices();
        static void PrintRoutines::PrintBytes(LPCWSTR name, LPBYTE bytes, DWORD len);
        static int PrintRoutines::PrintEntryDetails(LPCTSTR phonebookOverride, LPCTSTR entry_name);
        static int PrintRoutines::PrintEntries(System::String^ phonebookOverride);
        static int PrintRoutines::PrintEntries(LPCTSTR phonebookOverride);
        static void Output(System::String^ managedMessage);
        static void SetLoggingPath();
        static String^ PrintRoutines::GetOurExeName();
    };
}