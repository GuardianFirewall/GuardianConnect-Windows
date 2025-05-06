#pragma once
#include <string>
#include "NativeRoutines.h"
#include "Utility.h"
using namespace System;

namespace NativeRoutines
{
#define DEFAULT_PHONE_BOOK NULL
    public class RasBaseRoutines
    {
    public:
//        static String^ FormatAString(String^ format, ...array<Object^>^ args);
        static String^ GetRasErrorString(DWORD error);
        static std::string GetRasErrorMessage(DWORD error);
        static void GetPhonebookPath(const std::wstring& entry_name, wchar_t* pPhoneBookPath, std::string* error);
        static std::string GetSystemError(DWORD error);
        static Utility::RasOperationResult GetRasSuccessResult();
        static Utility::RasOperationResult GetRasErrorResult(std::string& error, const std::string& caller = {});

    };
}
