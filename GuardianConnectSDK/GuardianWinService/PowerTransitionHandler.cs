using GuardianConnect.Shared;
using GuardianConnect.VPNTransports;
using Serilog;
using Microsoft.Win32;

namespace GuardianWinService;

public static class PowerTransitionHandler
{
    private static Common.PowerTransitionStates CurrentPowerTransitionState = Common.PowerTransitionStates.Running;
    private static ITransportProvider.VPNProviderStatus VPNStatusAtSuspendTime = ITransportProvider.VPNProviderStatus.VPNStatusInvalid;

    internal static void SetupPowerTransitionHandler()
    {
        PowerTransitionMonitor.RegisterForPowerNotifications(PowerChangeNotifyCallbackRoutine);
    }
    
    private static uint PowerChangeNotifyCallbackRoutine(IntPtr Context, uint powerNotificationType, IntPtr Setting)
    {
        int settingValue = Setting.ToInt32();
        int contextValue = Context.ToInt32();
        Common.PowerNotificationTypes currentPowerNotificationType = (Common.PowerNotificationTypes)Enum.ToObject(typeof(Common.PowerNotificationTypes), powerNotificationType);
        Log.Information($"************** PowerChangeNotifyCallbackRoutine - powerNotificationType = {currentPowerNotificationType}, Context={contextValue}, Setting={settingValue}");
        
        // Do something per notification
        switch (currentPowerNotificationType)
        {
            case Common.PowerNotificationTypes.PBT_APMSUSPEND:
                // If VPN connected - it will disconnect when network stack collapses - we'll get it on the way up
                VPNStatusAtSuspendTime = VPNTransportIKEV2.GetCurrentVPNState();
                if (VPNStatusAtSuspendTime == ITransportProvider.VPNProviderStatus.VPNStatusConnected)
                {
                    // We need to do a clean disconnect now - filters too ugly - messes up reconnect
                    Log.Information( "************** PowerChangeNotifyCallbackRoutine - Calling VPNTransportIKEV1.PowerSuspendVPNConnection...");
                    VPNTransportIKEV2.PowerSuspendVPNConnection();
                }
                break;
            case Common.PowerNotificationTypes.PBT_APMRESUMEAUTOMATIC:
                // We don't care if user brought us out or not - we are resuming
                // IF we were connected, then reconnect now
                if (VPNStatusAtSuspendTime == ITransportProvider.VPNProviderStatus.VPNStatusConnected)
                {
                    Log.Information( "************** PowerChangeNotifyCallbackRoutine - VPN WAS CONNECTED AT SUSPENSION. Calling VPNTransportIKEV1.PowerResumeVPNConnection...");
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
}