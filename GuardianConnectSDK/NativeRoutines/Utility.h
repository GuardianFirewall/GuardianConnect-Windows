#pragma once
#include <string>

using namespace System;

namespace NativeRoutines {
    public ref class Utility
    {
    public:
        enum class CheckConnectionResult
        {
            CONNECTED,
            CONNECTING,
            CONNECT_FAILED,
            DISCONNECTING,
            DISCONNECTED,
        };

        value struct RasOperationResult
        {
            bool success;
            // If not success, store user friendly error description.
            System::String^ error_description;
        };
    };
}
