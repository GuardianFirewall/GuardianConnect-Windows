using System.Runtime.InteropServices;
using Serilog;
using Microsoft.Win32;

namespace GuardianConnect.Shared;

public static class PowerTransitionMonitor
{
    public delegate uint DEVICE_NOTIFY_CALLBACK_ROUTINE(IntPtr Context, uint Type, IntPtr Setting);

    public const int DEVICE_NOTIFY_CALLBACK = 2; 
    
    [DllImport("Powrprof.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern uint PowerRegisterSuspendResumeNotification(uint Flags, IntPtr Recipient, out IntPtr RegistrationHandle);

    [StructLayout(LayoutKind.Sequential)]
    public struct DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS
    {
        public DEVICE_NOTIFY_CALLBACK_ROUTINE Callback;
        public IntPtr Context;
    }
    
    public static void RegisterForPowerNotifications(DEVICE_NOTIFY_CALLBACK_ROUTINE callback)
    {
        Log.Information("************** PowerTransitionMonitor.RegisterForPowerNotifications: Registering for Power Nofitications...");
        DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS dnsp = new DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS();
        dnsp.Callback = callback;

        IntPtr pDeviceNotify = Marshal.AllocHGlobal(Marshal.SizeOf(dnsp));
        Marshal.StructureToPtr(dnsp, pDeviceNotify, false);
        IntPtr pRegistrationHandle = IntPtr.Zero;
        uint nRet = PowerRegisterSuspendResumeNotification(DEVICE_NOTIFY_CALLBACK, pDeviceNotify, out pRegistrationHandle);
        Log.Information($"************** PowerTransitionMonitor.RegisterForPowerNotifications: return value from PowerRegisterSuspendResumeNotification: {nRet:X8}");
        Marshal.FreeHGlobal(pDeviceNotify);
    }

}