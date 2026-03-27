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
        DISCONNECTED
    }

    public struct RasOperationResult
    {
        public bool Success;

        // If not success, store user friendly error description.
        public string ErrorDescription;
    }
}