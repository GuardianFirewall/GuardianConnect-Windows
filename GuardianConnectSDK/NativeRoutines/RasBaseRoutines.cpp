//#include "pch.h"
#include "RasBaseRoutines.h"
using namespace System;

namespace NativeRoutines
{
    String^ RasBaseRoutines::GetRasErrorString(DWORD error)
    {
        std::string em = GetRasErrorMessage(error);
        return Grd::MarshalToString(em);
    }

    Utility::RasOperationResult RasBaseRoutines::GetRasSuccessResult() {
        Utility::RasOperationResult result;
        result.success = true;
        return result;
    }

    Utility::RasOperationResult RasBaseRoutines::GetRasErrorResult(std::string& error, const std::string& caller) {
        Utility::RasOperationResult result;
        result.success = false;
        result.error_description = Grd::MarshalToString(error);
        return result;
    }

    // https://docs.microsoft.com/en-us/windows/win32/api/ras/nf-ras-rasgeterrorstringa
    std::string RasBaseRoutines::GetRasErrorMessage(DWORD error) {
        constexpr
            DWORD kBufSize = 512;
        TCHAR lpsz_error_string[kBufSize];

        if (error > RASBASE && error < RASBASEEND) {
            if (RasGetErrorString(error, lpsz_error_string, kBufSize) ==
                ERROR_SUCCESS)
            {
                std::wstring msg(lpsz_error_string);
                std::string arr_s(msg.begin(), msg.end());
                return arr_s;
            }
        }

        return GetSystemError(error);
    }

    // https://docs.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-formatmessage
    std::string RasBaseRoutines::GetSystemError(DWORD error) {
        constexpr
            DWORD kBufSize = 512;
        TCHAR lpsz_error_string[kBufSize];

        DWORD buf_len =
            FormatMessage(FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS,
                NULL, error, MAKELANGID(LANG_NEUTRAL, SUBLANG_DEFAULT),
                lpsz_error_string, kBufSize, NULL);
        if (!buf_len) {
            return "";
        }

        std::wstring msg(lpsz_error_string);
        std::string arr_s(msg.begin(), msg.end());
        return arr_s;
    }

    void RasBaseRoutines::GetPhonebookPath(const std::wstring& entry_name, wchar_t* pbkPath, std::string* error)
    {
        // https://docs.microsoft.com/en-us/windows/win32/api/processenv/nf-processenv-expandenvironmentstringsa
        wchar_t AppDataPath[1025] = { 0 };
        DWORD dwRet = ExpandEnvironmentStrings(TEXT("%APPDATA%"), AppDataPath, 1024);
        if (dwRet == 0) {
            //PrintRoutines::PrintRasError(GetLastError());
            // TODO: handle error here
        }

        wchar_t PhonebookPath[2048] = { 0 };
        swprintf(pbkPath, 2048, L"%s\\Microsoft\\Network\\Connections\\Pbk\\rasphone.pbk", AppDataPath);
    }

}
