namespace Win32Calls;

public static class Utility
{
    public enum CheckConnectionResult
    {
        Uninitialized,
        CONNECTED,
        CONNECTING,
        CONNECT_FAILED,
        DISCONNECTING,
        DISCONNECTED,
    }

    public struct RasOperationResult
    {
        bool success;
        // If not success, store user friendly error description.
        string error_description;
    }
}