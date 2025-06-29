//#include "pch.h"

#include "NativeRoutines.h"

namespace NativeRoutines
{
    bool Grd::LoadCheck()
    {
        fprintf(stderr, "NATIVE: WE ARE LOADED!");

        return true;
    }

    void Grd::MarshalString(String^ s, std::string& os)
    {
        using namespace System::Runtime::InteropServices;
        const char* chars = (const char*)Marshal::StringToHGlobalAnsi(s).ToPointer();
        os = chars;
    }

    String^ Grd::MarshalToString(std::string& os)
    {
        using namespace System::Runtime::InteropServices;
        String^ s;
        const char* chars = (const char*)(Marshal::StringToHGlobalAnsi(s)).ToPointer();
        os = chars;
        return s;
    }

    String^ Grd::WStoString(const std::wstring ws)
    {
        return gcnew String(ws.c_str());
    }


    String^ Grd::FormatAString(String^ format, ...array<Object^>^ args)
    {
        return String::Format(format, args);
    }
}