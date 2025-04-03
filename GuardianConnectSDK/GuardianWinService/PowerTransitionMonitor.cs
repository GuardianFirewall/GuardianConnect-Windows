using System.Runtime.InteropServices;
using Serilog;
using Microsoft.Win32;
using GuardianConnect.Shared;
using GuardianConnect.VPNTransports;

namespace GuardianWinService;

public static class PowerTransitionMonitor
{
    private static Common.PowerTransitionStates CurrentPowerTransitionState = Common.PowerTransitionStates.Running;
    private static ITransportProvider.VPNProviderStatus VPNStatusAtSuspendTime = ITransportProvider.VPNProviderStatus.VPNStatusInvalid;

    internal static void SetPowerTransitionEventHandler()
    {
        SystemEvents.PowerModeChanged += SystemEventsOnPowerModeChanged;
        Log.Information("*************** POWERMODE STATE CHANGE EVENT HANDLER NOW ACTIVE!!");
        
        // not sure what else to do here - the event handler will act accordingly
        // but - for resumption to work successfully, each successful connect must save parameters
        // so we can resume being connected if that's what was present at time of sleep/hibernate
        // (TODO: handling reboots whether post-unexpected or post-planned)
    }

    private static void SystemEventsOnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        switch (e.Mode)
        {
            case PowerModes.Suspend:
                // Signal for the Updates task to stop or pause
                // Signal for any active VPN to disconnect?
                // - or, in case and assume service is already notified and suspending...
                // let it do the disconnect independently of UI
                Log.Information("*************** SYSTEM IS SHUTTING DOWN! - WE WILL SUSPEND VPN IF CONNECTED AND ANY BACKGROUND TASKS.");

                CurrentPowerTransitionState = Common.PowerTransitionStates.Suspend;
                break;
            case PowerModes.Resume: 
                Log.Information("*************** SYSTEM IS RESUMING! - WE WILL RESUME VPN IF CONNECTED AND ANY BACKGROUND TASKS.");
                CurrentPowerTransitionState = Common.PowerTransitionStates.Resume;
                break;
            case PowerModes.StatusChange:
                Log.Information("*************** SYSTEM POWERSTATE has fired event STATUSCHANGE!");
                break;
        }
    }

#if true
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
    
    internal static void PossibleFixSetup()
    {
        Log.Information("************** PossibleSuspendResume Fix Setup...");
        DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS dnsp = new DEVICE_NOTIFY_SUBSCRIBE_PARAMETERS();
        dnsp.Callback = DeviceNotifyCallbackRoutine;

        IntPtr pDeviceNotify = Marshal.AllocHGlobal(Marshal.SizeOf(dnsp));
        Marshal.StructureToPtr(dnsp, pDeviceNotify, false);
        IntPtr pRegistrationHandle = IntPtr.Zero;
        uint nRet = PowerRegisterSuspendResumeNotification(DEVICE_NOTIFY_CALLBACK, pDeviceNotify, out pRegistrationHandle);
        Log.Information($"************** PossibleFixSetup: return value from PowerRegisterSuspendResumeNotification: {nRet:X8}");
        Marshal.FreeHGlobal(pDeviceNotify);
    }
    
    private static uint DeviceNotifyCallbackRoutine(IntPtr Context, uint powerNotificationType, IntPtr Setting)
    {
        int settingValue = Setting.ToInt32();
        int contextValue = Context.ToInt32();
        Common.PowerNotificationTypes currentPowerNotificationType = (Common.PowerNotificationTypes)Enum.ToObject(typeof(Common.PowerNotificationTypes), powerNotificationType);
        Log.Information($"************** DeviceNotify - powerNotificationType = {currentPowerNotificationType}, Context={contextValue}, Setting={settingValue}");
        
        // Do something per notification
        switch (currentPowerNotificationType)
        {
            case Common.PowerNotificationTypes.PBT_APMSUSPEND:
                // If VPN connected - it will disconnect when network stack collapses - we'll get it on the way up
                VPNStatusAtSuspendTime = VPNTransportIKEV2.GetCurrentVPNState();
                if (VPNStatusAtSuspendTime == ITransportProvider.VPNProviderStatus.VPNStatusConnected)
                {
                    // We need to do a clean disconnect now - filters too ugly - messes up reconnect
                    Log.Information( "************** DeviceNotify - Calling VPNTransportIKEV2.PowerSuspendVPNConnection...");
                    VPNTransportIKEV2.PowerSuspendVPNConnection();
                }
                break;
            case Common.PowerNotificationTypes.PBT_APMRESUMEAUTOMATIC:
                // We don't care if user brought us out or not - we are resuming
                // IF we were connected, then reconnect now
                if (VPNStatusAtSuspendTime == ITransportProvider.VPNProviderStatus.VPNStatusConnected)
                {
                    Log.Information( "************** DeviceNotify - VPN WAS CONNECTED AT SUSPENSION. Calling VPNTransportIKEV2.PowerResumeVPNConnection...");
                    // Let's reconnect
                    var successful = VPNTransportIKEV2.PowerResumeVPNConnection();
                    Log.Information(successful
                        ? "**************** PowerResumeVPNConnection successful!"
                        : "**************** PowerResumeVPNConnection failed!");
                }
                break;
        }
        return 0;
    }
    
#endif
}