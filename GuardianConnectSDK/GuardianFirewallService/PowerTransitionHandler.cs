using System.Net;
using GuardianConnect.API;
using GuardianConnect.Helpers;
using GuardianConnect.Shared;
using GuardianConnect.Shared.Extensions;
using GuardianConnect.VPNTransports;
using Serilog;
using Microsoft.Win32;
using Newtonsoft.Json;

namespace GuardianFirewallService;

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
        var successful = false;
        Log.Information("*************** PerformResumeActions ...");
        // We don't care if user brought us out or not - we are resuming
        // IF we were connected, then reconnect now
        Log.Information($"*************** PerformResumeActions: VPNStatusAtSuspendTime was '{VPNStatusAtSuspendTime}'");
        if (VPNStatusAtSuspendTime == ITransportProvider.VPNProviderStatus.VPNStatusConnected)
        {
            ErrorResponse errorResponse;
            var defaultRetries = Common.DefaultPowerResumeReconnectAttempts;
            var SavedResumeParemeters =
                RegistrySettings.RetrieveGuardianUserSettings(Common.kVpnCallParametersForReboot);
            var VpnResumeParameters = JsonConvert.DeserializeObject(SavedResumeParemeters, typeof(Dictionary<string, object>)) as Dictionary<string, object>;
            
            var host = (string)VpnResumeParameters["hostName"];
            Log.Information("************** PerformResumeActions - VPN WAS CONNECTED AT SUSPENSION.");
            Log.Information( $"************** Check network stack readiness by attempting a status check of the vpn host '{host}");
            var countValue = RegistrySettings.RetrieveGuardianUserSettings(Common.kServicePowerResumeReconnectAttempts);
            if (string.IsNullOrEmpty(countValue)) countValue = defaultRetries;
            int maxRetriesCount = int.Parse(countValue);
            int readinessCheckCount = maxRetriesCount;
            
            var header = "PerformResumeActions (waiting for host availability): GetServerStatus returned:";
            do
            {
                Log.Information($"Calling status of host '{host}' to verify if network is ready - retry # {maxRetriesCount - --readinessCheckCount}");
                GRDGateway gw = new GRDGateway();
                errorResponse = gw.GetServerStatus(host).Result;
                Log.Information($"{header}: errorResponse from GetServerStatus: {errorResponse}");
                if (!errorResponse.IsError)
                {
                    Log.Information(
                        $"************** PerformResumeActions - NO error returned from GetServerStatus. Response message = '{errorResponse.Message}. StatusCode={errorResponse.HttpResponse.StatusCode}");
                    break; // ok - not an error - so then let's break and try to connect
                }
                
                Task.Delay(5000).Wait();
            } while (--readinessCheckCount != 0);
            
            // let's fall through and see if we can connect anyway
            // Let's reconnect
            int connectionAttemptCount = maxRetriesCount;
            do
            {
                Log.Information($" Calling VPNTransportIKEV2.PowerResumeVPNConnection... attempt #{maxRetriesCount - --connectionAttemptCount}");
                errorResponse = VPNTransportIKEV2.PowerResumeVPNConnection();
                if (errorResponse.IsError) Task.Delay(5000).Wait();
            } while (connectionAttemptCount != 0 &&  errorResponse.IsError);
            Log.Information(errorResponse.IsError
                ? "**************** PerformResumeActions failed!"
            : "**************** PerformResumeActions successful!");
        }

        CurrentPowerTransitionState = Common.PowerTransitionStates.Running;
    }
}