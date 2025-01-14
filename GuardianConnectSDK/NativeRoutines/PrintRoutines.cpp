#include "pch.h"
#include "PrintRoutines.h"
using namespace System;
using namespace Serilog;
using namespace Serilog::Core;
using namespace GuardianConnect::Shared;

namespace NativeRoutines
{
    static bool LoggingInitialized;
    static gcroot<Threading::Mutex^> LogWriteGate = gcnew Threading::Mutex();
    static gcroot<Threading::ReaderWriterLock^> LogWriteLock = gcnew Threading::ReaderWriterLock();
    static gcroot<IO::StreamWriter^> LogWriter;

    void PrintRoutines::SetLoggingPath()
    {
        //Console::WriteLine("THIS IS A TEST WRITING TO CONSOLE!!!");
        //Console::Error->WriteLine("THIS IS A TEST WRITING TO SYSTEM CONSOLE ERROR");
        //logFilePath = L"C:\\temp\\GuardianVPN\\GuardianVPN_DebugLog.wrapper_log";
    }

    void PrintRoutines::Output(String^ managedMessage)
    {
	    Common::GRDLog( Grd::FormatAString("{0}", gcnew array<Object^> { managedMessage }));
    }

    int PrintRoutines::PrintConnectionDetails(HRASCONN connection) {
        DWORD dwCb = 0;
        DWORD dwRet = ERROR_SUCCESS;
        PRAS_PROJECTION_INFO lpProjectionInfo = NULL;

        // https://docs.microsoft.com/en-us/windows/win32/api/ras/nf-ras-rasgetprojectioninfoex
        dwRet = RasGetProjectionInfoEx(connection, lpProjectionInfo, &dwCb);
        if (dwRet == ERROR_BUFFER_TOO_SMALL) {
            lpProjectionInfo = (PRAS_PROJECTION_INFO)HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, dwCb);
            lpProjectionInfo->version = RASAPIVERSION_CURRENT;
            dwRet = RasGetProjectionInfoEx(connection, lpProjectionInfo, &dwCb);
            if (dwRet != ERROR_SUCCESS) {
                PrintRoutines::PrintRasError(dwRet);
                if (lpProjectionInfo) {
                    HeapFree(GetProcessHeap(), 0, lpProjectionInfo);
                    lpProjectionInfo = NULL;
                }
                return dwRet;
            }

            if (lpProjectionInfo->type == PROJECTION_INFO_TYPE_IKEv2) {
                // See _RASIKEV2_PROJECTION_INFO in Ras.h for full list of fields.
                // Fields commented out are not implemented (ex: IPv6).
                wprintf(L"\ttype=PROJECTION_INFO_TYPE_IKEv2");

                // IPv4 Projection Parameters
                wprintf(L"\n\tdwIPv4NegotiationError=%d", lpProjectionInfo->ikev2.dwIPv4NegotiationError);
                wprintf(L"\n\tipv4Address=");
                printf("%s", inet_ntoa(lpProjectionInfo->ikev2.ipv4Address));
                wprintf(L"\n\tipv4ServerAddress=");
                printf("%s", inet_ntoa(lpProjectionInfo->ikev2.ipv4ServerAddress));

                // IPv6 Projection Parameters
                //DWORD         dwIPv6NegotiationError;
                //RASIPV6ADDR   ipv6Address;
                //RASIPV6ADDR   ipv6ServerAddress;
                //DWORD         dwPrefixLength;

                // AUTH
                wprintf(L"\n\tdwAuthenticationProtocol=");
                if (lpProjectionInfo->ikev2.dwAuthenticationProtocol == RASIKEv2_AUTH_MACHINECERTIFICATES) wprintf(L"RASIKEv2_AUTH_MACHINECERTIFICATES");
                else if (lpProjectionInfo->ikev2.dwAuthenticationProtocol == RASIKEv2_AUTH_EAP) wprintf(L"RASIKEv2_AUTH_EAP");
                wprintf(L"\n\tdwEapTypeId=%d", lpProjectionInfo->ikev2.dwEapTypeId);

                // -
                wprintf(L"\n\tdwFlags=");
                if (lpProjectionInfo->ikev2.dwFlags & RASIKEv2_FLAGS_MOBIKESUPPORTED) wprintf(L"RASIKEv2_FLAGS_MOBIKESUPPORTED, ");
                if (lpProjectionInfo->ikev2.dwFlags & RASIKEv2_FLAGS_BEHIND_NAT) wprintf(L"RASIKEv2_FLAGS_BEHIND_NAT, ");
                if (lpProjectionInfo->ikev2.dwFlags & RASIKEv2_FLAGS_SERVERBEHIND_NAT) wprintf(L"RASIKEv2_FLAGS_SERVERBEHIND_NAT");
                wprintf(L"\n\tdwEncryptionMethod=");
                // https://docs.microsoft.com/en-us/windows/win32/api/ipsectypes/ne-ipsectypes-ipsec_cipher_type
                if (lpProjectionInfo->ikev2.dwEncryptionMethod == IPSEC_CIPHER_TYPE_DES) wprintf(L"IPSEC_CIPHER_TYPE_DES");
                else if (lpProjectionInfo->ikev2.dwEncryptionMethod == IPSEC_CIPHER_TYPE_3DES) wprintf(L"IPSEC_CIPHER_TYPE_3DES");
                else if (lpProjectionInfo->ikev2.dwEncryptionMethod == IPSEC_CIPHER_TYPE_AES_128) wprintf(L"IPSEC_CIPHER_TYPE_AES_128");
                else if (lpProjectionInfo->ikev2.dwEncryptionMethod == IPSEC_CIPHER_TYPE_AES_192) wprintf(L"IPSEC_CIPHER_TYPE_AES_192");
                else if (lpProjectionInfo->ikev2.dwEncryptionMethod == IPSEC_CIPHER_TYPE_AES_256) wprintf(L"IPSEC_CIPHER_TYPE_AES_256");
                else wprintf(L"unknown (%d)", lpProjectionInfo->ikev2.dwEncryptionMethod);

                // -
                wprintf(L"\n\tnumIPv4ServerAddresses=%d", lpProjectionInfo->ikev2.numIPv4ServerAddresses);
                wprintf(L"\n\tipv4ServerAddresses=");
                for (DWORD j = 0; j < lpProjectionInfo->ikev2.numIPv4ServerAddresses; j++) {
                    printf("%s", inet_ntoa(lpProjectionInfo->ikev2.ipv4ServerAddresses[j]));
                    if ((j + 1) < lpProjectionInfo->ikev2.numIPv4ServerAddresses) wprintf(L", ");
                }
                wprintf(L"\n\tnumIPv6ServerAddresses=%d", lpProjectionInfo->ikev2.numIPv6ServerAddresses);
                //RASIPV6ADDR* ipv6ServerAddresses;
            }
            else if (lpProjectionInfo->type == PROJECTION_INFO_TYPE_PPP) {
                wprintf(L"\ttype=PROJECTION_INFO_TYPE_PPP");
            }

            HeapFree(GetProcessHeap(), 0, lpProjectionInfo);
            lpProjectionInfo = NULL;
        }
        else {
            wprintf(L"\tError calling RasGetProjectionInfoEx: ");
            PrintRoutines::PrintRasError(dwRet);
        }

        return dwRet;
    }

    // https://docs.microsoft.com/en-us/windows/win32/api/ras/nf-ras-rasenumconnectionsa
    int PrintRoutines::PrintConnections() {
        DWORD dwCb = 0;
        DWORD dwRet = ERROR_SUCCESS;
        DWORD dwConnections = 0;
        LPRASCONN lpRasConn = NULL;

        // Call RasEnumConnections with lpRasConn = NULL. dwCb is returned with the required buffer size and 
        // a return code of ERROR_BUFFER_TOO_SMALL
        dwRet = RasEnumConnections(lpRasConn, &dwCb, &dwConnections);
        if (dwRet == ERROR_BUFFER_TOO_SMALL) {
            // Allocate the memory needed for the array of RAS structure(s).
            lpRasConn = (LPRASCONN)HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, dwCb);
            if (lpRasConn == NULL) {
                wprintf(L"HeapAlloc failed!\n");
                return 0;
            }
            // The first RASCONN structure in the array must contain the RASCONN structure size
            lpRasConn[0].dwSize = sizeof(RASCONN);

            // Call RasEnumConnections to enumerate active connections
            dwRet = RasEnumConnections(lpRasConn, &dwCb, &dwConnections);

            // If successful, print the names of the active connections.
            if (ERROR_SUCCESS == dwRet) {
                wprintf(L"The following RAS connections are currently active:\n");
                for (DWORD i = 0; i < dwConnections; i++) {
                    wprintf(L"%s\n", lpRasConn[i].szEntryName);
                    PrintConnectionDetails(lpRasConn[i].hrasconn);
                }
            }
            wprintf(L"\n");
            //Deallocate memory for the connection buffer
            HeapFree(GetProcessHeap(), 0, lpRasConn);
            lpRasConn = NULL;
            return 0;
        }

        // There was either a problem with RAS or there are no connections to enumerate    
        if (dwConnections >= 1) {
            wprintf(L"The operation failed to acquire the buffer size.\n\n");
        }
        else {
            wprintf(L"There are no active RAS connections.\n\n");
        }

        return 0;
    }

    // https://docs.microsoft.com/en-us/windows/win32/api/ras/nf-ras-rasenumdevicesa
    int PrintRoutines::PrintDevices() {
        DWORD dwCb = 0;
        DWORD dwRet = ERROR_SUCCESS;
        DWORD dwDevices = 0;
        LPRASDEVINFO lpRasDevInfo = NULL;

        // Call RasEnumDevices with lpRasDevInfo = NULL. dwCb is returned with the required buffer size and 
        // a return code of ERROR_BUFFER_TOO_SMALL
        dwRet = RasEnumDevices(lpRasDevInfo, &dwCb, &dwDevices);

        if (dwRet == ERROR_BUFFER_TOO_SMALL) {
            // Allocate the memory needed for the array of RAS structure(s).
            lpRasDevInfo = (LPRASDEVINFO)HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, dwCb);
            if (lpRasDevInfo == NULL) {
                wprintf(L"HeapAlloc failed!\n");
                return 0;
            }
            // The first RASDEVINFO structure in the array must contain the structure size
            lpRasDevInfo[0].dwSize = sizeof(RASDEVINFO);

            // Call RasEnumDevices to enumerate RAS devices
            dwRet = RasEnumDevices(lpRasDevInfo, &dwCb, &dwDevices);

            // If successful, print the names of the RAS devices
            if (ERROR_SUCCESS == dwRet) {
                wprintf(L"The following RAS devices were found:\n");
                for (DWORD i = 0; i < dwDevices; i++) {
                    wprintf(L"%s\n", lpRasDevInfo[i].szDeviceName);
                }
            }
            wprintf(L"\n");
            //Deallocate memory for the connection buffer
            HeapFree(GetProcessHeap(), 0, lpRasDevInfo);
            lpRasDevInfo = NULL;
            return 0;
        }

        // There was either a problem with RAS or there are no RAS devices to enumerate    
        if (dwDevices >= 1) {
            wprintf(L"The operation failed to acquire the buffer size.\n\n");
        }
        else {
            wprintf(L"There were no RAS devices found.\n\n");
        }

        return 0;
    }

    void PrintRoutines::PrintBytes(LPCWSTR name, LPBYTE bytes, DWORD len) {
        bool next_is_newline = false;
        const int bytes_per_line = 12;
        wprintf(L"\n\t[%s: %d bytes]\n\t\t", name, len);
        for (DWORD i = 0; i < len; i++) {
            if (i > 0 && !next_is_newline) {
                wprintf(L", ");
            }
            wprintf(L"0x%02x", bytes[i]);
            next_is_newline = ((i + 1) % bytes_per_line) == 0;
            if (next_is_newline) {
                wprintf(L"\n\t\t");
            }
        }
        wprintf(L"\n\t[/%s]", name);
    }

    int PrintRoutines::PrintEntryDetails(LPCTSTR entry_name, LPCTSTR phoneBookOverride) {
        DWORD dwCb = 0;
        DWORD dwRet = ERROR_SUCCESS;
        LPRASENTRY lpRasEntry = NULL;

        // Call RasGetEntryProperties with lpRasEntry = NULL. dwCb is returned with the required buffer size and 
        // a return code of ERROR_BUFFER_TOO_SMALL
        //dwRet = RasGetEntryProperties(DEFAULT_PHONE_BOOK, entry_name, lpRasEntry, &dwCb, NULL, NULL);
        dwRet = RasGetEntryProperties(phoneBookOverride, entry_name, lpRasEntry, &dwCb, NULL, NULL);
        if (dwRet == ERROR_BUFFER_TOO_SMALL) {
            lpRasEntry = (LPRASENTRY)HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, dwCb);
            if (lpRasEntry == NULL) {
                wprintf(L"HeapAlloc failed!\n");
                return 0;
            }

            // The first LPRASENTRY structure in the array must contain the structure size
            lpRasEntry[0].dwSize = sizeof(RASENTRY);
            //dwRet = RasGetEntryProperties(DEFAULT_PHONE_BOOK, entry_name, lpRasEntry, &dwCb, NULL, NULL);
            dwRet = RasGetEntryProperties(phoneBookOverride, entry_name, lpRasEntry, &dwCb, NULL, NULL);
            switch (dwRet) {
            case ERROR_INVALID_SIZE:
                wprintf(L"An incorrect structure size was detected.\n");
                break;
            }

            // great place to set debug breakpoint when inspecting existing connections
            //PrintOptions(lpRasEntry->dwfOptions);
            //PrintOptions2(lpRasEntry->dwfOptions2);

            // https://docs.microsoft.com/en-us/windows/win32/api/ras/nf-ras-rasgetcustomauthdataa
            LPBYTE custom_auth_data = NULL;
            //dwRet = RasGetCustomAuthData(DEFAULT_PHONE_BOOK, entry_name, custom_auth_data, &dwCb);
            dwRet = RasGetCustomAuthData(phoneBookOverride, entry_name, custom_auth_data, &dwCb);
            if (dwRet == ERROR_BUFFER_TOO_SMALL && dwCb > 0) {
                custom_auth_data = (LPBYTE)HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, dwCb);
                //dwRet = RasGetCustomAuthData(DEFAULT_PHONE_BOOK, entry_name, custom_auth_data, &dwCb);
                dwRet = RasGetCustomAuthData(phoneBookOverride, entry_name, custom_auth_data, &dwCb);
                if (dwRet != ERROR_SUCCESS) {
                    PrintRoutines::PrintRasError(dwRet);
                    if (custom_auth_data) {
                        HeapFree(GetProcessHeap(), 0, custom_auth_data);
                        custom_auth_data = NULL;
                    }
                    return dwRet;
                }
                PrintRoutines::PrintBytes(L"CustomAuthData", custom_auth_data, dwCb);
                HeapFree(GetProcessHeap(), 0, custom_auth_data);
            }
            else if (dwCb > 0) {
                wprintf(L"\n\tError calling RasGetCustomAuthData: ");
                PrintRoutines::PrintRasError(dwRet);
            }

            // https://docs.microsoft.com/en-us/windows/win32/api/ras/nf-ras-rasgeteapuserdataa
            LPBYTE eap_user_data = NULL;
            //dwRet = RasGetEapUserData(NULL, DEFAULT_PHONE_BOOK, entry_name, eap_user_data, &dwCb);
            dwRet = RasGetEapUserData(NULL, phoneBookOverride, entry_name, eap_user_data, &dwCb);
            if (dwRet == ERROR_BUFFER_TOO_SMALL && dwCb > 0) {
                eap_user_data = (LPBYTE)HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, dwCb);
                //dwRet = RasGetEapUserData(NULL, DEFAULT_PHONE_BOOK, entry_name, eap_user_data, &dwCb);
                dwRet = RasGetEapUserData(NULL, phoneBookOverride, entry_name, eap_user_data, &dwCb);
                if (dwRet != ERROR_SUCCESS) {
                    PrintRoutines::PrintRasError(dwRet);
                    if (eap_user_data) {
                        HeapFree(GetProcessHeap(), 0, eap_user_data);
                        eap_user_data = NULL;
                    }
                    return dwRet;
                }
                PrintRoutines::PrintBytes(L"EapUserData", eap_user_data, dwCb);
                HeapFree(GetProcessHeap(), 0, eap_user_data);
            }
            else if (dwCb > 0) {
                wprintf(L"\n\tError calling RasGetEapUserData: ");
                PrintRoutines::PrintRasError(dwRet);
            }

            // https://docs.microsoft.com/en-us/windows/win32/api/ras/nf-ras-rasgetsubentrypropertiesa
            wprintf(L"\n\tdwSubEntries: %d", lpRasEntry->dwSubEntries);
            if (lpRasEntry->dwSubEntries > 0) {
                for (DWORD i = 0; i < lpRasEntry->dwSubEntries; i++) {
                    LPRASSUBENTRY lpRasSubEntry = NULL;
                    //dwRet = RasGetSubEntryProperties(phonebookOverride, entry_name, i + 1, lpRasSubEntry, &dwCb, NULL, NULL);
                    dwRet = RasGetSubEntryProperties(phoneBookOverride, entry_name, i + 1, lpRasSubEntry, &dwCb, NULL, NULL);
                    if (dwRet == ERROR_BUFFER_TOO_SMALL && dwCb > 0) {
                        lpRasSubEntry = (LPRASSUBENTRY)HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, dwCb);
                        //dwRet = RasGetSubEntryProperties(DEFAULT_PHONE_BOOK, entry_name, i + 1, lpRasSubEntry, &dwCb, NULL, NULL);
                        dwRet = RasGetSubEntryProperties(phoneBookOverride, entry_name, i + 1, lpRasSubEntry, &dwCb, NULL, NULL);
                        if (dwRet != ERROR_SUCCESS) {
                            PrintRoutines::PrintRasError(dwRet);
                            if (lpRasSubEntry) {
                                HeapFree(GetProcessHeap(), 0, lpRasSubEntry);
                                lpRasSubEntry = NULL;
                            }
                            return dwRet;
                        }
                        wprintf(L"\n\t\tdwSize=%d", lpRasSubEntry->dwSize);
                        wprintf(L"\n\t\tdwfFlags=%d", lpRasSubEntry->dwfFlags);
                        wprintf(L"\n\t\tszDeviceType=%s", lpRasSubEntry->szDeviceType);
                        wprintf(L"\n\t\tszDeviceName=%s", lpRasSubEntry->szDeviceName);
                        wprintf(L"\n\t\tszLocalPhoneNumber=%s", lpRasSubEntry->szLocalPhoneNumber);
                        wprintf(L"\n\t\tdwAlternateOffset=%d", lpRasSubEntry->dwAlternateOffset);
                        HeapFree(GetProcessHeap(), 0, lpRasSubEntry);
                        lpRasSubEntry = NULL;
                    }
                    else {
                        wprintf(L"\n\tError calling RasGetSubEntryProperties: ");
                        PrintRoutines::PrintRasError(dwRet);
                    }
                }
            }

            wprintf(L"\n");
            //Deallocate memory for the entry buffer
            HeapFree(GetProcessHeap(), 0, lpRasEntry);
            lpRasEntry = NULL;
            return ERROR_SUCCESS;
        }

        return dwRet;
    }

    int PrintRoutines::PrintEntries(System::String^ phonebookOverride) {
        pin_ptr<const wchar_t> pinnedPhoneBook = ::PtrToStringChars(phonebookOverride);

        return PrintEntries(pinnedPhoneBook);
    }

    // https://docs.microsoft.com/en-us/windows/win32/api/ras/nf-ras-rasenumentriesa
    int PrintRoutines::PrintEntries(LPCTSTR phonebookOverride) {
        DWORD dwCb = 0;
        DWORD dwRet = ERROR_SUCCESS;
        DWORD dwEntries = 0;
        LPRASENTRYNAME lpRasEntryName = NULL;

        // Call RasEnumEntries with lpRasEntryName = NULL. dwCb is returned with the required buffer size and 
        // a return code of ERROR_BUFFER_TOO_SMALL
        dwRet = RasEnumEntries(NULL, NULL, lpRasEntryName, &dwCb, &dwEntries);
        if (dwRet == ERROR_BUFFER_TOO_SMALL) {
            // Allocate the memory needed for the array of RAS entry names.
            lpRasEntryName = (LPRASENTRYNAME)HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, dwCb);
            if (lpRasEntryName == NULL) {
                wprintf(L"HeapAlloc failed!\n");
                return 0;
            }
            // The first RASENTRYNAME structure in the array must contain the structure size
            lpRasEntryName[0].dwSize = sizeof(RASENTRYNAME);

            // Call RasEnumEntries to enumerate all RAS entry names
            dwRet = RasEnumEntries(NULL, phonebookOverride, lpRasEntryName, &dwCb, &dwEntries);

            // If successful, print the RAS entry names 
            if (ERROR_SUCCESS == dwRet) {
                wprintf(L"The following RAS entry names were found:\n");
                for (DWORD i = 0; i < dwEntries; i++) {
                    wprintf(L"%s\n", lpRasEntryName[i].szEntryName);
                    dwRet = PrintEntryDetails(lpRasEntryName[i].szEntryName, phonebookOverride);
                }
            }
            //Deallocate memory for the connection buffer
            HeapFree(GetProcessHeap(), 0, lpRasEntryName);
            lpRasEntryName = NULL;
            return ERROR_SUCCESS;
        }

        // There was either a problem with RAS or there are RAS entry names to enumerate    
        if (dwEntries >= 1) {
            wprintf(L"The operation failed to acquire the buffer size.\n\n");
        }
        else {
            wprintf(L"There were no RAS entry names found:.\n\n");
        }

        return dwRet;
    }

    // https://docs.microsoft.com/en-us/windows/win32/api/ras/nf-ras-rasgeterrorstringa
    void PrintRoutines::PrintRasError(DWORD error)
    {
        //    fprintf(ErrorOutput, "PrintRasError: error is %d\n", error);

        DWORD cBufSize = 512;
        TCHAR lpszErrorString[512];

        if (error > RASBASE && error < RASBASEEND)
        {
            if (RasGetErrorStringW(error, lpszErrorString, cBufSize) == ERROR_SUCCESS)
            {
                wprintf(L"%s\n", lpszErrorString);
                return;
            }
        }

        PrintRoutines::PrintSystemError(error);
    }

    void PrintRoutines::PrintSystemError(DWORD error)
    {
        DWORD cBufSize = 512;
        TCHAR lpszErrorString[512];

        DWORD bufLen = FormatMessage(
            FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS,
            nullptr,
            error,
            MAKELANGID(LANG_NEUTRAL, SUBLANG_DEFAULT),
            lpszErrorString,
            cBufSize, nullptr);
        if (bufLen)
        {
            wprintf(L"%ls", lpszErrorString);
            Output(Grd::FormatAString("{0}", gcnew array<Object^> { gcnew String(lpszErrorString) }));
        }
    }

    String^ PrintRoutines::GetOurExeName()
    {
            TCHAR exepath[MAX_PATH+1];

            if(0 == GetModuleFileName(0, exepath, MAX_PATH+1))
                fprintf(stderr,"Error!");

        String^ exePath = gcnew String(exepath);
        String^ exeFile;
        int lio = exePath->LastIndexOf('\\')+1;
        exeFile = exePath->Substring(lio);

        return exeFile;
    }
}