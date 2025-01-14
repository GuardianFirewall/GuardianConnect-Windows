#include "pch.h"
#include "ConnectionRoutines.h"
#include <vcclr.h>
#include "NativeRoutines.h"
#include "NotificationHandling.h"
#include "PrintRoutines.h"
#include "ScopedHeapAlloc.h"
#include "Utility.h"
#include "WFM/VpnDnsHandler.h"
using namespace Serilog;
using namespace Serilog::Core;
using namespace Serilog::Configuration;

using namespace System;

namespace NativeRoutines
{
    DWORD ConnectionRoutines::MakeTheCall(String^ givenPhonebookPath, String^ entryName)
    {
        LoggerConfiguration^ logCfg = gcnew LoggerConfiguration();
        
        Logger^ log = logCfg->CreateLogger();

        log->Debug(String::Format("In MakeTheCall()... Entry name = '{0}'", gcnew String(entryName)));

        DWORD retVal = ConnectWithEntry(givenPhonebookPath, entryName);

        return retVal;
    }

    // https://docs.microsoft.com/en-us/windows/win32/api/ras/nf-ras-rassetcredentialsa
    DWORD ConnectionRoutines::SetCredentials(LPCTSTR entry_name, LPCTSTR username, LPCTSTR password)
    {
        RASCREDENTIALS credentials;

        ZeroMemory(&credentials, sizeof(RASCREDENTIALS));
        credentials.dwSize = sizeof(RASCREDENTIALS);
        credentials.dwMask = RASCM_UserName | RASCM_Password;

        wcscpy_s(credentials.szUserName, 256, username);
        wcscpy_s(credentials.szPassword, 256, password);

        std::string error_get_phone_book_path;
        LPCWSTR phone_book_path =
            RasBaseRoutines::GetPhonebookPath(entry_name, &error_get_phone_book_path);
        DWORD dwRet = RasSetCredentials(phone_book_path, entry_name, &credentials, FALSE);
        if (dwRet != ERROR_SUCCESS)
        {
            PrintRoutines::PrintRasError(dwRet);
            return dwRet;
        }

        return ERROR_SUCCESS;
    }

    DWORD ConnectionRoutines::ConnectWithEntry(String^ givenPhonebookPath, String^ entryName)
    {
        PrintRoutines::SetLoggingPath();
        pin_ptr<const wchar_t> given_Phonebook_path = ::PtrToStringChars(givenPhonebookPath);
        pin_ptr<const wchar_t> entry_name = ::PtrToStringChars(entryName);
        
        PrintRoutines::Output(String::Format("In Connect()... Entry name = '{0}'", entryName));
        // Check current state of connections first...
        auto connection_result = CheckConnection(entryName);
        if (connection_result == Utility::CheckConnectionResult::CONNECTING ||
            connection_result == Utility::CheckConnectionResult::CONNECTED) {
            PrintRoutines::Output(Grd::FormatAString("{0}: Don't try to connect when it's in-progress or already connected.",
                gcnew array<String^> { __FUNCTION__ }));
            return ERROR_SUCCESS;
        }

        std::string error_get_phone_book_path;
        LPCWSTR phone_book_path =
            RasBaseRoutines::GetPhonebookPath(entry_name, &error_get_phone_book_path);

        // Continue with making connection
        LPRASDIALPARAMSW lpRasDialParams = nullptr;
        DWORD cb = sizeof(RASDIALPARAMSW);

        lpRasDialParams = (LPRASDIALPARAMSW)HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, cb);
        if (lpRasDialParams == nullptr)
        {
            PrintRoutines::Output(L"HeapAlloc failed!\n");
            return 0;
        }
        
        lpRasDialParams->dwSize = sizeof(RASDIALPARAMSW);
        wcscpy_s(lpRasDialParams->szEntryName, 256, entry_name);
        wcscpy_s(lpRasDialParams->szDomain, 15, L"*");
        
        // https://docs.microsoft.com/en-us/windows/win32/api/ras/nf-ras-rasgetcredentialsw
        RASCREDENTIALSW credentials;

        ZeroMemory(&credentials, sizeof(RASCREDENTIALSW));
        credentials.dwSize = sizeof(RASCREDENTIALSW);
        credentials.dwMask = RASCM_UserName | RASCM_Password;

        DWORD dwRet = RasGetCredentialsW(given_Phonebook_path, entry_name, &credentials);
        if (dwRet != ERROR_SUCCESS)
        {
            HeapFree(GetProcessHeap(), 0, (LPVOID)lpRasDialParams);
            PrintRoutines::PrintRasError(dwRet);
            return dwRet;
        }
        wcscpy_s(lpRasDialParams->szUserName, 256, credentials.szUserName);
        wcscpy_s(lpRasDialParams->szPassword, 256, credentials.szPassword);

        wprintf(L"Connecting to `%s`...\n", entry_name);
        PrintRoutines::Output(System::String::Format("Connecting to '{0}'", entryName));

        HRASCONN hRasConn = nullptr;
        dwRet = RasDialW(nullptr, given_Phonebook_path, lpRasDialParams, NULL, nullptr, &hRasConn);
        
        if (dwRet != ERROR_SUCCESS)
        {
            HeapFree(GetProcessHeap(), 0, (LPVOID)lpRasDialParams);
            PrintRoutines::PrintRasError(dwRet);
            return dwRet;
        }
        wprintf(L"SUCCESS!\n");
        PrintRoutines::Output("SUCCESS!");

        // store handle if needed, etc
        NotificationHandling::RasConnectionHandle =  hRasConn;
        //..
        // Trigger VPNConnectionEvent for watchers
        //NotificationHandling::SetVPNConnectionChangeEvent();

        HeapFree(GetProcessHeap(), 0, (LPVOID)lpRasDialParams);

        // New - call Brave's routines for adding filtering
        VpnDnsHandler* vpn_dns_handler = new VpnDnsHandler();
        vpn_dns_handler->UpdateFiltersState();

        return ERROR_SUCCESS;
    }

    Utility::CheckConnectionResult ConnectionRoutines::CheckConnection(String^ entry_name)
    {
        HRASCONN throwawayHandle = NULL;
        return CheckConnection(entry_name, throwawayHandle);
    }

    Utility::CheckConnectionResult ConnectionRoutines::CheckConnection(String^ entryName, HRASCONN& handleOut)
    {
        PrintRoutines::Output(String::Format("Check connection state for {0}", entryName));
        pin_ptr<const wchar_t> entry_name = ::PtrToStringChars(entryName);

        DWORD dw_cb = 0;
        DWORD dw_ret = dw_cb;
        DWORD dw_connections = 0;
        LPRASCONN lp_ras_conn = NULL;

        // Call RasEnumConnections with lp_ras_conn = NULL. dw_cb is returned with the
        // required buffer size and a return code of ERROR_BUFFER_TOO_SMALL
        dw_ret = RasEnumConnections(lp_ras_conn, &dw_cb, &dw_connections);

        // If got success here, it means there is no connected vpn entry.
        if (dw_ret == ERROR_SUCCESS) {
            PrintRoutines::Output("CheckConnection(): There is no active connection.");
            return Utility::CheckConnectionResult::DISCONNECTED;
        }

        // Abnormal situation.
        if (dw_ret != ERROR_BUFFER_TOO_SMALL) {
            return Utility::CheckConnectionResult::DISCONNECTED;
        }

        // Allocate the memory needed for the array of RAS structure(s).
        ScopedHeapAlloc ras_conn(dw_cb);
        lp_ras_conn = reinterpret_cast<LPRASCONN>(ras_conn.lp_alloc_mem());
        if (lp_ras_conn == NULL) {
            PrintRoutines::Output("HeapAlloc failed!");
            return Utility::CheckConnectionResult::DISCONNECTED;
        }

        // The first RASCONN structure in the array must contain the RASCONN
        // structure size
        lp_ras_conn[0].dwSize = sizeof(RASCONN);

        // Call RasEnumConnections to enumerate active connections
        dw_ret = RasEnumConnections(lp_ras_conn, &dw_cb, &dw_connections);

        if (ERROR_SUCCESS != dw_ret) {
            lp_ras_conn = NULL;
            return Utility::CheckConnectionResult::DISCONNECTED;
        }

        // If successful, find connection with |entry_name|.
        Utility::CheckConnectionResult result = Utility::CheckConnectionResult::DISCONNECTED;
        for (DWORD i = 0; i < dw_connections; i++) {
            wchar_t* rasConnEntryName = (lp_ras_conn[i].szEntryName);
            if (!wcscmp(entry_name, rasConnEntryName)) {
                result = GetConnectionState(lp_ras_conn[i].hrasconn);
                handleOut = lp_ras_conn[i].hrasconn;
                break;
            }
        }
        return result;
    }

    Utility::CheckConnectionResult ConnectionRoutines::GetConnectionState(HRASCONN h_ras_conn) {
        DWORD dw_ret = 0;

        RASCONNSTATUS ras_conn_status;
        ZeroMemory(&ras_conn_status, sizeof(RASCONNSTATUS));
        ras_conn_status.dwSize = sizeof(RASCONNSTATUS);

        // Utility::Checking connection status using RasGetConnectStatus
        dw_ret = RasGetConnectStatus(h_ras_conn, &ras_conn_status);
        if (ERROR_SUCCESS != dw_ret) {
            PrintRoutines::Output(System::String::Format("RasGetConnectStatus failed: Error = ", dw_ret));
            return Utility::CheckConnectionResult::DISCONNECTED;
        }

        switch (ras_conn_status.rasconnstate) {
        case RASCS_ConnectDevice:
//            PrintRoutines::Output("Connecting device...");
            return Utility::CheckConnectionResult::CONNECTING;
        case RASCS_Connected:
//            PrintRoutines::Output("Connected");
            return Utility::CheckConnectionResult::CONNECTED;
        case RASCS_Disconnected:
//            PrintRoutines::Output("Disconnected");
            return Utility::CheckConnectionResult::DISCONNECTED;
        default:
            break;
        }

        return Utility::CheckConnectionResult::DISCONNECTED;
    }

    String^ ConnectionRoutines::GetEntryNameOfActiveConnection()
    {
        HRASCONN handle = FindAnyActiveConnection();
        
        //wprintf(L"GENOAC: ActiveConnectionEntryName = '%s'\n", ActiveConnectionEntryName);
        return ConnectedEntry;
    }

    // TJE - this is cut/paste of above except entry name not known. This is
    // used when we have been started and don't known if or what entry has an
    // active connection. So we iterate through and find ANY active connection
    // and return the handle to be used for notification setup.
    // We'll save entryName elsewhere for UI propagation
    HRASCONN ConnectionRoutines::FindAnyActiveConnection()
    {
        DWORD dw_cb = 0;
        DWORD dw_ret = dw_cb;
        DWORD dw_connections = 0;
        LPRASCONN lp_ras_conn = NULL;

        ActiveConnectionEntryName = new wchar_t[2048];
        // Call RasEnumConnections with lp_ras_conn = NULL. dw_cb is returned with the
        // required buffer size and a return code of ERROR_BUFFER_TOO_SMALL
        dw_ret = RasEnumConnections(lp_ras_conn, &dw_cb, &dw_connections);

        // If got success here, it means there is no connected vpn entry.
        if (dw_ret == ERROR_SUCCESS) {
            // TJE - CHECK THIS - do we need to output this every time? Commenting out for now.
            //PrintRoutines::Output("FindAnyActiveConnection: There is no active connection.");
            return nullptr;
        }

        // Abnormal situation.
        if (dw_ret != ERROR_BUFFER_TOO_SMALL) {
            return nullptr;
        }

        // Allocate the memory needed for the array of RAS structure(s).
        ScopedHeapAlloc ras_conn(dw_cb);
        lp_ras_conn = reinterpret_cast<LPRASCONN>(ras_conn.lp_alloc_mem());
        if (lp_ras_conn == NULL) {
            PrintRoutines::Output("HeapAlloc failed!");
            return nullptr;
        }

        // The first RASCONN structure in the array must contain the RASCONN
        // structure size
        lp_ras_conn[0].dwSize = sizeof(RASCONN);

        // Call RasEnumConnections to enumerate active connections
        dw_ret = RasEnumConnections(lp_ras_conn, &dw_cb, &dw_connections);

        if (ERROR_SUCCESS != dw_ret) {
            lp_ras_conn = NULL;
            return nullptr;
        }

        // If successful, find connection with |entry_name|.
        Utility::CheckConnectionResult result = Utility::CheckConnectionResult::DISCONNECTED;
        for (DWORD i = 0; i < dw_connections; i++) {
            result = GetConnectionState(lp_ras_conn[i].hrasconn);
            if (result == Utility::CheckConnectionResult::CONNECTED) {
                wprintf(L"FAAC: szEntryName = '%s'\n", lp_ras_conn[i].szEntryName);
                int len = wcslen(lp_ras_conn[i].szEntryName);
                wprintf(L"len of above is %d\n", len);
                wchar_t activeName[2048] = { 0 };
                wcscpy_s(activeName, len+1, lp_ras_conn[i].szEntryName);
                ActiveConnectionEntryName = activeName;
                ConnectedEntry = gcnew String(activeName);

                wprintf(L"FAAC: ActiveConnectionEntryName = '%s'\n", ActiveConnectionEntryName);
                
                return lp_ras_conn[i].hrasconn;
            }
        }

        ActiveConnectionEntryName = nullptr;
        return nullptr;
    }

    // TJE - TODO: Clean up unused return values
    bool ConnectionRoutines::IsAnyConnectionActive(LPCTSTR entryNameOut)
    {
        bool connected = FindAnyActiveConnection() != nullptr;
        entryNameOut = ActiveConnectionEntryName;
        return connected;
    }

    bool ConnectionRoutines::DisconnectEntry(String^ dummyEntryName)
    {
        bool disconnectResult = false;
        HRASCONN entryHandle = NULL;

        LPCTSTR activeEntryName;
        HRASCONN activeConnectionHandle = FindAnyActiveConnection();
        
        String^ entryName = ConnectedEntry;
        PrintRoutines::Output("FindAnyActiveConnection found entry:");
        PrintRoutines::Output(entryName);
        
        Utility::CheckConnectionResult entryConnectionCheckResult = CheckConnection(entryName, entryHandle);
        if (entryConnectionCheckResult != Utility::CheckConnectionResult::CONNECTED && entryConnectionCheckResult != Utility::CheckConnectionResult::CONNECTING)
        {
            PrintRoutines::Output(Grd::FormatAString("Entry '{0}' is not in a CONNECTED or CONNECTING state. Its state is {1}",
                gcnew array<String^> {
                    entryName,
                    entryConnectionCheckResult.ToString()
                }));
            Console::WriteLine("Entry '{0}' is not in a CONNECTED or CONNECTING state. Its state is {1}",
                entryName, entryConnectionCheckResult);

            return true;
        }

        DWORD dwRet = RasHangUpW(entryHandle);
        PrintRoutines::Output(Grd::FormatAString("RasHangUp returned {0} for Entry '{1}'", gcnew array<Object^> {
            dwRet, entryName }));
        PrintRoutines::PrintRasError(dwRet);

        VpnDnsHandler* vpn_dns_handler = new VpnDnsHandler();
        vpn_dns_handler->UpdateFiltersState();
        return dwRet == ERROR_SUCCESS ? true : false;

    }

}
