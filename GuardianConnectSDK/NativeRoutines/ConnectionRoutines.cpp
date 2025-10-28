//#include "pch.h"
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

        PrintRoutines::Output(L"ConnectWithEntry(): Calling RasGetCredentialsW...\n");
        DWORD dwRet = RasGetCredentialsW(given_Phonebook_path, entry_name, &credentials);
        if (dwRet != ERROR_SUCCESS)
        {
            HeapFree(GetProcessHeap(), 0, (LPVOID)lpRasDialParams);
			String^ errorRetCode = gcnew String(std::to_string(dwRet).c_str());
            PrintRoutines::Output(Grd::FormatAString("{0}: **** ERROR **** Return from RasGetCredentials: {1:x}.",
                gcnew array<String^> { __FUNCTION__, errorRetCode}));
            PrintRoutines::PrintRasError(dwRet);
            return dwRet;
        }
        wcscpy_s(lpRasDialParams->szUserName, 256, credentials.szUserName);
        wcscpy_s(lpRasDialParams->szPassword, 256, credentials.szPassword);

        wprintf(L"Connecting to `%s`...\n", entry_name);
        PrintRoutines::Output(System::String::Format("ConnectWithEntry: Connecting to '{0}' with call to RasDial...", entryName));

        HRASCONN hRasConn = nullptr;
        dwRet = RasDialW(nullptr, given_Phonebook_path, lpRasDialParams, NULL, nullptr, &hRasConn);
        
        if (dwRet != ERROR_SUCCESS)
        {
			String^ errorRetCode = gcnew String(std::to_string(dwRet).c_str());
            PrintRoutines::Output(Grd::FormatAString("{0}: **** ERROR **** Return from RasDial: {1:x}.",
                gcnew array<String^> { __FUNCTION__, errorRetCode}));
            HeapFree(GetProcessHeap(), 0, (LPVOID)lpRasDialParams);
            PrintRoutines::PrintRasError(dwRet);
            return dwRet;
        }
        wprintf(L"SUCCESS!\n");
        PrintRoutines::Output("ConnectWithEntry: SUCCESS return from RasDial! [CONNECT#1.1]");
		ConnectedEntry = entryName;

        // store handle if needed, etc
        RasConnectionHandle =  hRasConn;
        //..

        HeapFree(GetProcessHeap(), 0, (LPVOID)lpRasDialParams);

        // New - call Brave's routines for adding filtering
        VpnDnsHandler* vpn_dns_handler = new VpnDnsHandler();
		PrintRoutines::Output("ConnectWithEntry: Calling VpnDnsHandler::UpdateFiltersState()... [CONNECT#1.2]");
        vpn_dns_handler->UpdateFiltersState();

		PrintRoutines::Output("ConnectWithEntry: Back from VpnDnsHandler::UpdateFiltersState()...[CONNECT#1.3]");
        return ERROR_SUCCESS;
    }

    Utility::CheckConnectionResult ConnectionRoutines::CheckConnection(String^ entry_name)
    {
        HRASCONN throwawayHandle = NULL;
        return CheckConnection(entry_name, throwawayHandle);
    }

    Utility::CheckConnectionResult ConnectionRoutines::CheckConnection(String^ entryName, HRASCONN& handleOut)
    {
        PrintRoutines::Output(String::Format("Check connection state for '{0}'", entryName));
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
                LPRASCONNSTATUS lpras_conn_status;
//                result = GetConnectionState(IntPtr(lp_ras_conn[i].hrasconn), lpras_conn_status);
                //
                RasConnStatusInfo^ managedStatusInfo = nullptr; // Create a managed RasConnStatusInfo object
                result = GetConnectionState(lp_ras_conn[i].hrasconn, managedStatusInfo);

                // If needed, convert managedStatusInfo to native lpras_conn_status
                if (managedStatusInfo != nullptr)
                {
                    lpras_conn_status = new RASCONNSTATUSW();
                    lpras_conn_status->dwSize = sizeof(RASCONNSTATUSW);
                    lpras_conn_status->rasconnstate = static_cast<RASCONNSTATE>(managedStatusInfo->RasConnStateValue);
                    lpras_conn_status->dwError = managedStatusInfo->ErrorCode;
                }
                //
                handleOut = lp_ras_conn[i].hrasconn;
                break;
            }
        }
        switch (result)
        {
            case Utility::CheckConnectionResult::DISCONNECTED:
                PrintRoutines::Output("CheckConnectionResult::DISCONNECTED");
                break;
            case Utility::CheckConnectionResult::CONNECTED:
                PrintRoutines::Output("CheckConnectionResult::CONNECTED");
                break;
            default:
                PrintRoutines::Output("CheckConnectionResult::UNKNOWN");
                break;
        }
        return result;
    }

    Utility::CheckConnectionResult ConnectionRoutines::GetRasConnectStatus(HRASCONN h_ras_conn, LPRASCONNSTATUSW& lp_ras_status)
    {
        DWORD dw_ret = 0;
        RASCONNSTATUS ras_conn_status;
        ZeroMemory(&ras_conn_status, sizeof(RASCONNSTATUS));
        ras_conn_status.dwSize = sizeof(RASCONNSTATUS);

        // Utility::Checking connection status using RasGetConnectStatus
        dw_ret = RasGetConnectStatus(h_ras_conn, &ras_conn_status);
        lp_ras_status = &ras_conn_status;
        if (ERROR_SUCCESS != dw_ret) {
            PrintRoutines::Output(System::String::Format("GetRasConnectStatus: RasGetConnectStatus failed: Error = {0}", dw_ret));
            return Utility::CheckConnectionResult::DISCONNECTED;
        }

        PrintRoutines::Output(String::Format("GetRasConnectStatus: RasConnState/RasConnSubState = {0}/{1}",
            (int)ras_conn_status.rasconnstate, (int)ras_conn_status.rasconnsubstate));
        switch (ras_conn_status.rasconnstate) {
        case RASCS_ConnectDevice:
            PrintRoutines::Output("GetRasConnectStatus: Connecting device...");
            return Utility::CheckConnectionResult::CONNECTING;
        case RASCS_Connected:
            PrintRoutines::Output("GetRasConnectStatus: Connected");
            return Utility::CheckConnectionResult::CONNECTED;
        case RASCS_Disconnected:
            PrintRoutines::Output("GetRasConnectStatus: Disconnected");
            return Utility::CheckConnectionResult::DISCONNECTED;
        default:
            break;
        }

        return Utility::CheckConnectionResult::DISCONNECTED;
    }
    // from copilot
    Utility::CheckConnectionResult ConnectionRoutines::GetConnectionState(HRASCONN hRasConn, RasConnStatusInfo^% statusInfo)
    {
        LPRASCONNSTATUSW lpStatus = nullptr;
        auto result = GetRasConnectStatus(hRasConn, lpStatus);

        if (lpStatus != nullptr)
        {
            statusInfo = gcnew RasConnStatusInfo();
            RasConnState managedType = static_cast<RasConnState>(lpStatus->rasconnstate);
            statusInfo->RasConnStateValue = managedType;
            statusInfo->ErrorCode = lpStatus->dwError;
            RasConnSubState rcss = static_cast<RasConnSubState>(lpStatus->rasconnsubstate);
            statusInfo->RasConnSubStateValue = rcss;
        }
        else
        {
            statusInfo = nullptr;
        }

        return result;
    }

    Utility::CheckConnectionResult ConnectionRoutines::GetConnectionState(RasConnStatusInfo^% statusInfo)
    {
        return GetConnectionState(ActiveConnectionHandle, statusInfo);
    }
    
    //

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
            PrintRoutines::Output("FindAnyActiveConnection: There is no active connection.");
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
        LPRASCONNSTATUSW lp_ras_status = NULL;
        for (DWORD i = 0; i < dw_connections; i++) {
            //result = GetConnectionState(IntPtr(lp_ras_conn[i].hrasconn), lp_ras_status);
            //
            RasConnStatusInfo^ managedStatusInfo = nullptr; // Create a managed RasConnStatusInfo object
            result = GetConnectionState(lp_ras_conn[i].hrasconn, managedStatusInfo);

            // If needed, convert managedStatusInfo to native lp_ras_status
            if (managedStatusInfo != nullptr)
            {
                lp_ras_status = new RASCONNSTATUSW();
                lp_ras_status->dwSize = sizeof(RASCONNSTATUSW);
                lp_ras_status->rasconnstate = static_cast<RASCONNSTATE>(managedStatusInfo->RasConnStateValue);
                lp_ras_status->dwError = managedStatusInfo->ErrorCode;
            }
            //
            if (result == Utility::CheckConnectionResult::CONNECTED) {
                wprintf(L"FAAC: szEntryName = '%s'\n", lp_ras_conn[i].szEntryName);
                size_t len = wcslen(lp_ras_conn[i].szEntryName);
                wchar_t activeName[2048] = { 0 };
                wcscpy_s(activeName, len+1, lp_ras_conn[i].szEntryName);
                ActiveConnectionEntryName = activeName;
                ConnectedEntry = gcnew String(activeName);


                PrintRoutines::Output(
                    Grd::FormatAString("FindAnyActiveConnection: Entry '{0}' is in a CONNECTED or CONNECTING state.",
                    ConnectedEntry ));
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

        ActiveConnectionHandle = FindAnyActiveConnection();
        
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
