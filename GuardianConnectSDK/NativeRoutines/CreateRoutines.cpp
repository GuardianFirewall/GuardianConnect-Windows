//#include "pch.h"
#include "PrintRoutines.h"
#include "RasBaseRoutines.h"
#include "CreateRoutines.h"
#include "ConnectionRoutines.h"
using namespace System;

// Macro for counting maximum characters that will fit into a buffer
#define CELEMS(x) ((sizeof(x))/(sizeof(x[0])))

namespace NativeRoutines
{

    int CreateRoutines::CreateTheCall(System::String^ phonebookPath,
        System::String^ entryName,
        System::String^ hostName,
        System::String^ userName,
        System::String^ password)
    {
        PrintRoutines::LoggingInitialized = false;
        PrintRoutines::SetLoggingPath();
        System::Console::WriteLine("\nCONSOLE: In CreateTheCall!!");
        PrintRoutines::Output("OUTPUT: In CreateTheCall!!");
        PrintRoutines::Output(Grd::FormatAString("CreateTheCall(): EntryName = {0}", gcnew array<Object^> { entryName } ));
        PrintRoutines::Output(Grd::FormatAString("CreateTheCall(): hostName = {0}", gcnew array<Object^>  {hostName}));
        PrintRoutines::Output(Grd::FormatAString("CreateTheCall(): userName = {0}", gcnew array<Object^> {userName}));
        PrintRoutines::Output(Grd::FormatAString("CreateTheCall(): Password = {0}", gcnew array<Object^> { password }));

        pin_ptr<const wchar_t> pinnedPhoneBook = ::PtrToStringChars(phonebookPath);
        pin_ptr<const wchar_t> pinnedEntryName = ::PtrToStringChars(entryName);
        pin_ptr<const wchar_t> pinnedHostName = ::PtrToStringChars(hostName);
        pin_ptr<const wchar_t> pinnedUserName = ::PtrToStringChars(userName);
        pin_ptr<const wchar_t> pinnedPassword = ::PtrToStringChars(password);




        System::Console::WriteLine("\nCONSOLE: In CreateTheCall: Calling CreateOrUpdateEntry(()...");
        int retVal = CreateOrUpdateEntry(
            (LPCTSTR)pinnedPhoneBook,
            (LPCTSTR)pinnedEntryName,
            (LPCTSTR)pinnedHostName,
            (LPCTSTR)pinnedUserName,
            (LPCTSTR)pinnedPassword);

        System::Console::WriteLine("\nCONSOLE: In CreateTheCall: Back from CreateOrUpdateEntry(().");
        return retVal;
    }

    // https://docs.microsoft.com/en-us/windows/win32/api/ras/nf-ras-rassetentrypropertiesa
    int CreateRoutines::CreateOrUpdateEntry(LPCTSTR givenPhonebookPath, LPCTSTR entry_name, LPCTSTR hostname, LPCTSTR username, LPCTSTR password) {
        ConnectionRoutines^ con = gcnew ConnectionRoutines();
        PrintRoutines::Output("In CreateOrUpdateEntry.");
        
        RASENTRY entry;
        ZeroMemory(&entry, sizeof(RASENTRY));


        // Issue #198 - Enumerate and remove all of our phonebook entries before creating new one
        DeleteExistingGuardianRasEntries();
        
        // For descriptions of each field (including valid values) see:
        // https://docs.microsoft.com/en-us/previous-versions/windows/desktop/legacy/aa377274(v=vs.85)
        entry.dwSize = sizeof(RASENTRY);
        entry.dwfOptions = RASEO_RemoteDefaultGateway | RASEO_RequireEAP | RASEO_PreviewUserPw | RASEO_PreviewDomain | RASEO_ShowDialingProgress;
        wcscpy_s(entry.szLocalPhoneNumber, 128, hostname);
        entry.dwfNetProtocols = RASNP_Ip | RASNP_Ipv6;
        entry.dwFramingProtocol = RASFP_Ppp;
        wcscpy_s(entry.szDeviceType, 16, RASDT_Vpn);
        wcscpy_s(entry.szDeviceName, 128, TEXT("WAN Miniport (IKEv2)"));
        entry.dwType = RASET_Vpn;
        entry.dwEncryptionType = ET_Optional;
        entry.dwVpnStrategy = VS_Ikev2Only;
        entry.dwfOptions2 = RASEO2_DontNegotiateMultilink | RASEO2_ReconnectIfDropped | RASEO2_IPv6RemoteDefaultGateway | RASEO2_CacheCredentials;
        entry.dwRedialCount = 3;
        entry.dwRedialPause = 60;

        // this maps to "Type of sign-in info" => "User name and password"
        entry.dwCustomAuthKey = 26;

        std::string error_get_phone_book_path;
        DWORD dwRet = RasSetEntryProperties(givenPhonebookPath, entry_name, &entry, entry.dwSize, NULL, NULL);
        if (dwRet != ERROR_SUCCESS) {
            PrintRoutines::PrintRasError(dwRet);
            return dwRet;
        }

        //dwRet = con->SetCredentials(entry_name, username, password);
        // Why aren't we checking dwRet here?
        //if (dwRet != 0)
        // DOING INLINE SETCREDENTIALS HERE
        {
            //PrintRoutines::PrintRasError(dwRet);
            //return dwRet;
            {
                RASCREDENTIALSW credentials;

                ZeroMemory(&credentials, sizeof(RASCREDENTIALSW));
                credentials.dwSize = sizeof(RASCREDENTIALSW);
                credentials.dwMask = RASCM_UserName | RASCM_Password;

                wcscpy_s(credentials.szUserName, 256, username);
                wcscpy_s(credentials.szPassword, 256, password);

                DWORD dwRet = RasSetCredentialsW(givenPhonebookPath, entry_name, &credentials, FALSE);
                if (dwRet != ERROR_SUCCESS)
                {
                    PrintRoutines::Output(L"Got error on inline RasSetCredentials!!");
                    PrintRoutines::PrintRasError(dwRet);

                    if (dwRet == 623)
                    {
                        PrintRoutines::Output(L"Couldn't find entry. Fine. Going to do a Get with same params");
                        // Got 623 - going to try something  - since RasSetEntryProperties used same identical phonebook
                        // path and entry name as the failed RasSetCredentials, going to try now to do a RasGetEntryProperties with said same
                        int pedRet = PrintRoutines::PrintEntryDetails(entry_name, givenPhonebookPath);

                        if (pedRet != 0)
                        {
                            PrintRoutines::Output(L"Call to PrintEntryDetails got error:");
                            PrintRoutines::PrintRasError(pedRet);
                            return pedRet;
                        }
                        else
                        {
                            PrintRoutines::Output(L"Call to PrintEntryDetails SUCCESSFUL!! (???!!) Then why the 623?");
                        }
                    }
                    else
                    {
                        return dwRet;
                    }
                }
            }
        }

        // Policy needs to be set, otherwise you'll see an error like this in `eventvwr`:
        // >> The user DESKTOP - DRCJVG6\brian dialed a connection named BRAVEVPN which has failed.The error code returned on failure is 13868.
        // 
        // I've found you can set this manually via PowerShell using the `Set-VpnConnectionIPsecConfiguration` cmdlet:
        // https://docs.microsoft.com/en-us/powershell/module/vpnclient/set-vpnconnectionipsecconfiguration?view=windowsserver2019-ps
        // 
        // I've used the following parameters via PowerShell:
        // >> AuthenticationTransformConstants: GCMAES256
        // >> CipherTransformConstants : GCMAES256
        // >> DHGroup : ECP384
        // >> IntegrityCheckMethod : SHA256
        // >> PfsGroup : None
        // >> EncryptionMethod : GCMAES256
        //
        // RAS doesn't expose public methods for editing policy. However, the storage is just an INI format file:
        // `%APPDATA%\Microsoft\Network\Connections\Pbk\rasphone.pbk`
        // 
        // The variable being set in this file is similar to the structure `ROUTER_CUSTOM_IKEv2_POLICY0` which was 
        // part of MPR (Multiprotocol Routing). The DWORDs are written out byte by byte in 02d format as `CustomIPSecPolicies`
        // and `NumCustomPolicy` is always being set to 1.
        // 
        // NOTE: *This IKEv2 implementation (due to policy) might only be supported on Windows 8 and above; we need to check that.*
        // 

        // https://docs.microsoft.com/en-us/windows/win32/api/processenv/nf-processenv-expandenvironmentstringsa
        wchar_t AppDataPath[1025] = { 0 };
        dwRet = ExpandEnvironmentStrings(TEXT("%APPDATA%"), AppDataPath, 1024);
        if (dwRet == 0) {
            PrintRoutines::Output(L"Got error on ExpandEnvironmentStrings!!");
            PrintRoutines::PrintRasError(GetLastError());
            // TODO: handle error here
        }

        wchar_t PhonebookPath[2048] = { 0 };
        swprintf(PhonebookPath, 2048, L"%s\\Microsoft\\Network\\Connections\\Pbk\\rasphone.pbk", AppDataPath);

        // https://docs.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-writeprivateprofilestringw
        BOOL wrote_entry = WritePrivateProfileString(
            entry_name,
            L"NumCustomPolicy",
            L"1",
            givenPhonebookPath
        );
        if (!wrote_entry) {
            wprintf(L"ERROR: failed to write \"NumCustomPolicy\" field to `%s`", PhonebookPath);
            // TODO: handle error here
        }

        wrote_entry = WritePrivateProfileString(
            entry_name,
            L"CustomIPSecPolicies",
            L"030000000400000002000000050000000200000000000000",
            givenPhonebookPath
        );
        if (!wrote_entry) {
            //wprintf(L"ERROR: failed to write \"CustomIPSecPolicies\" field to `%s`", PhonebookPath);
            wprintf(L"ERROR: failed to write \"CustomIPSecPolicies\" field to `%s`", givenPhonebookPath);
            // TODO: handle error here
            PrintRoutines::Output("ERROR: failed to write \"CustomIPSecPolicies\" field to givenPhonebookPath");
        }

        return ERROR_SUCCESS;
    }

    void CreateRoutines::DeleteExistingGuardianRasEntries()
    {

        LPRASENTRYNAME lpRasEntryName = NULL;
        LPRASENTRYNAME lpTempRasEntryName = NULL;
        DWORD cb = sizeof(RASENTRYNAME);
        DWORD cEntries = 0;
        int nRet = 0;
        DWORD i = 0;
        BOOL fSuccess = FALSE;
        TCHAR           szTempBuf[256] = { 0 };

        lpRasEntryName = (LPRASENTRYNAME)HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, cb);
        if (NULL == lpRasEntryName)
        {
            printf("HeapAlloc failed.\n");
            //return ERROR_OUTOFMEMORY;
            return;
        }

        lpRasEntryName->dwSize = sizeof(RASENTRYNAME);

        // Getting the size required for the RASENTRYNAME buffer

        nRet = RasEnumEntries(NULL, NULL, lpRasEntryName, &cb, &cEntries);

        switch (nRet)
        {
        case ERROR_BUFFER_TOO_SMALL:
            if (HeapFree(GetProcessHeap(), 0, (LPVOID)lpRasEntryName))
            {

                lpRasEntryName = (LPRASENTRYNAME)HeapAlloc(GetProcessHeap(), HEAP_ZERO_MEMORY, cb);
                if (NULL == lpRasEntryName)
                {
                    printf("HeapAlloc failed.\n");
                    //return ERROR_OUTOFMEMORY;
                    return;
                }

                lpRasEntryName->dwSize = sizeof(RASENTRYNAME);

                // Calling RasEnumEntries to enumerate the phonebook entries for a default phonebook	
                nRet = RasEnumEntries(NULL, NULL, lpRasEntryName, &cb, &cEntries);
                if (ERROR_SUCCESS != nRet)
                {
                    printf("RasEnumEntries failed: Error %d\n", nRet);
                    goto done;
                }
                else
                {
                    fSuccess = TRUE;
                }
            }
            else
            {
                printf("HeapFree failed.\n");
                //return GetLastError();
                return;
            }

            break;

        case ERROR_SUCCESS:
            fSuccess = TRUE;
            break;

        default:
            printf("RasEnumEntries failed: Error = %d\n", nRet);
            goto done;
        }


        if (fSuccess)
        {
            printf("Phone book entries in the default phonebook:\n\n");
            lpTempRasEntryName = lpRasEntryName;
            for (i = 0; i < cEntries; i++)
            {
                wprintf(L"found %ls\n", lpTempRasEntryName->szEntryName);
                ZeroMemory((LPVOID)szTempBuf, sizeof(szTempBuf));
                if (wcsncmp(lpTempRasEntryName->szEntryName, L"Guardian Firewall - ", 8) == 0)
                {
					wprintf(L"Deleting %ls\n", lpTempRasEntryName->szEntryName);
					RasDeleteEntryW(NULL, lpTempRasEntryName->szEntryName);
                }
                lpTempRasEntryName++;
            }
        }

    done:
        HeapFree(GetProcessHeap(), 0, (LPVOID)lpRasEntryName);


    }
}

