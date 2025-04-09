using GuardianConnect.Shared;
using GuardianConnect.VPNTransports;
using Serilog;
using Microsoft.Win32;

namespace GuardianWinService;

public static class PowerTransitionHandler
{
    private static Common.PowerTransitionStates CurrentPowerTransitionState = Common.PowerTransitionStates.Running;
    private static ITransportProvider.VPNProviderStatus VPNStatusAtSuspendTime = ITransportProvider.VPNProviderStatus.VPNStatusInvalid;

    internal static bool ConnectedAtSuspendTime() =>
        VPNStatusAtSuspendTime == ITransportProvider.VPNProviderStatus.VPNStatusConnected;

    internal static void ResetVpnStatusAtSuspendTime() => VPNStatusAtSuspendTime = ITransportProvider.VPNProviderStatus.VPNStatusInvalid;

    internal static void SetupPowerTransitionHandler()
    {
        SystemEvents.PowerModeChanged += SystemEventsOnPowerModeChanged;
        PowerTransitionMonitor.RegisterForPowerNotifications(PowerChangeNotifyCallbackRoutine);
    }
    
    private static uint PowerChangeNotifyCallbackRoutine(IntPtr Context, uint powerNotificationType, IntPtr Setting)
    {
        int settingValue = Setting.ToInt32();
        int contextValue = Context.ToInt32();
        Common.PowerNotificationTypes incomingPowerNotificationType = (Common.PowerNotificationTypes)Enum.ToObject(typeof(Common.PowerNotificationTypes), powerNotificationType);
        Log.Information($"************** PowerChangeNotifyCallbackRoutine - powerNotificationType = {incomingPowerNotificationType}, Context={contextValue}, Setting={settingValue}");
        
        // Do something per notification
        switch (incomingPowerNotificationType)
        {
            case Common.PowerNotificationTypes.PBT_APMSUSPEND:
                if (CurrentPowerTransitionState == Common.PowerTransitionStates.Running)
                {
                    CurrentPowerTransitionState = Common.PowerTransitionStates.Suspend;
                    PerformSuspendActions();
                }

                break;
            case Common.PowerNotificationTypes.PBT_APMRESUMEAUTOMATIC:
                if (CurrentPowerTransitionState == Common.PowerTransitionStates.Suspend)
                {
                    CurrentPowerTransitionState = Common.PowerTransitionStates.Resume;
                    PerformResumeActions();
                }

                break;
        }

        return 0;
    } 
    
    
    private static void SystemEventsOnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        switch (e.Mode)
        {
            case PowerModes.Suspend:
                Log.Information("*************** SystemEventsOnPowerModeChanged: SYSTEM IS SUSPENDING! - WE WILL SUSPEND ANY BACKGROUND TASKS.");
                if (CurrentPowerTransitionState == Common.PowerTransitionStates.Running)
                {
                    CurrentPowerTransitionState = Common.PowerTransitionStates.Suspend;
                    PerformSuspendActions();
                }
                break;
            case PowerModes.Resume:
                Log.Information("*************** SystemEventsOnPowerModeChanged: SYSTEM IS RESUMING! - WE WILL RESUME ANY BACKGROUND TASKS.");
                if (CurrentPowerTransitionState == Common.PowerTransitionStates.Suspend)
                {
                    CurrentPowerTransitionState = Common.PowerTransitionStates.Resume;
                    PerformResumeActions();
                }
                break;
            case PowerModes.StatusChange:
                Log.Information("*************** SystemEventsOnPowerModeChanged: SYSTEM HAS POWER STATUS CHANGE!!");
                break;
        }
    }

    private static void PerformSuspendActions()
    {
        Log.Information("*************** PerformSuspendActions ...");
        // If VPN connected - it will disconnect when network stack collapses - we'll get it on the way up
        VPNStatusAtSuspendTime = VPNTransportIKEV2.GetCurrentVPNState();
        if (VPNStatusAtSuspendTime == ITransportProvider.VPNProviderStatus.VPNStatusConnected)
        {
            // We need to do a clean disconnect now - filters too ugly - messes up reconnect
            Log.Information(
                "************** PowerChangeNotifyCallbackRoutine - Calling VPNTransportIKEV1.PowerSuspendVPNConnection...");
            VPNTransportIKEV2.PowerSuspendVPNConnection();
        }
    }

    private static void PerformResumeActions()
    {
        Log.Information("*************** PerformResumeActions ...");
        // We don't care if user brought us out or not - we are resuming
        // IF we were connected, then reconnect now
        if (VPNStatusAtSuspendTime == ITransportProvider.VPNProviderStatus.VPNStatusConnected)
        {
            Log.Information(
                "************** PerformResumeActions - VPN WAS CONNECTED AT SUSPENSION. Calling VPNTransportIKEV1.PowerResumeVPNConnection...");
            // Let's reconnect
            var successful = VPNTransportIKEV2.PowerResumeVPNConnection();
            Log.Information(successful
                ? "**************** PerformResumeActions successful!"
                : "**************** PerformResumeActions failed!");
        }

        CurrentPowerTransitionState = Common.PowerTransitionStates.Running;
    }
}