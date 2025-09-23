#pragma once
#include <string>
#include <wchar.h>
#include <stdio.h>
#include <windows.h>
#include <WinBase.h>
#include <winnt.h>
#include <securitybaseapi.h>
#include <AclAPI.h>
#include <winerror.h>
#include <ras.h>
#include <raserror.h>
#include <ipsectypes.h>
#include <MprApi.h>
#include <string.h>
#include <vcclr.h>
#include <fwptypes.h>
#include <cwchar>
#include <fstream>
#include <fwptypes.h>
#include <iostream>

using namespace System;

namespace NativeRoutines {
#define VPNEVENT_CLIENTNOTIFIER L"Global\\GRDRASCONNLISTENEREVENT"
    
    public class Grd
    {
        public:
            static bool LoadCheck();
            static void MarshalString(String^ s, std::string& os);
            static String^ MarshalToString(std::string& os);
            static String^ WStoString(const std::wstring ws);
            static String^ FormatAString(String^ format, ...array<Object^>^ args);
    };
}
