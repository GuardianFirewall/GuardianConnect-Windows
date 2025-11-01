using System.Net.NetworkInformation;
using System.Text.Json;
using GuardianConnect.Abstractions;
using GuardianConnect.API;
using GuardianConnect.Shared;
using GuardianConnect.VPNTransports;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using Microsoft.Extensions.Logging.Abstractions;

namespace GuardianConnect.Services;

public static class PowerTransitionHandler
{
    private const string ast20 = "********************";
    private const string at20 = "@@@@@@@@@@@@@@@@@@@@";
    private const string dash20 = "--------------------";
    private const string eql20 = "====================";
    private const string pct20 = "%%%%%%%%%%%%%%%%%%%%";
    private const string hash20 = "####################";
    private const string plus20 = "++++++++++++++++++++";
    
    private static Common.PowerTransitionStates CurrentPowerTransitionState = Common.PowerTransitionStates.Running;

    private static ITransportProvider.VPNProviderStatus VPNStatusAtSuspendTime = ITransportProvider.VPNProviderStatus.VPNStatusInvalid;

    private static ILogger _logger;
    
    internal static bool ConnectedAtSuspendTime() => VPNStatusAtSuspendTime == ITransportProvider.VPNProviderStatus.VPNStatusConnected;
    internal static void SetConnectedAtSuspendTime()
    {
        VPNStatusAtSuspendTime = ITransportProvider.VPNProviderStatus.VPNStatusConnected;
        _logger.LogInformation($"SetVPNStateAtSuspendTime() called from Poller... VPNStatusAtSuspendTime now set to {VPNStatusAtSuspendTime}");
    }

    internal static void ResetVpnStatusAtSuspendTime() =>
        VPNStatusAtSuspendTime = ITransportProvider.VPNProviderStatus.VPNStatusInvalid;

    public static void SetupPowerTransitionHandler()
    {
        SystemEvents.PowerModeChanged += SystemEventsOnPowerModeChanged;
        NetworkChange.NetworkAvailabilityChanged += NetworkChangeOnNetworkAvailabilityChanged;
        PowerTransitionMonitor.RegisterForPowerNotifications(PowerChangeNotifyCallbackRoutine);
        // Add Resume function to VPNTransportIKEV2 delegate for sake of Disconnect recovery
        VPNTransportIKEV2.PowerResumeActions = PerformResumeActions;
        VPNTransportIKEV2.SetVPNStateAtSuspend = SetConnectedAtSuspendTime;
        VPNTransportIKEV2.ResetVPNStateAtSuspend = ResetVpnStatusAtSuspendTime;
        //InitPowerEvents();

    }

    private static void NetworkChangeOnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        // Check network availability.
        // If unavailable and we've already set Suspend mode (handled), then ignore, else set Suspend and do PerformSuspend...
        // If available and we were already in Resume or Running, then ignore, else set Resume and do PerformResume...

        _logger.LogInformation($"{at20} Network availability changed to {e.IsAvailable} {at20}");
        if (e.IsAvailable)
        {
            if (CurrentPowerTransitionState == Common.PowerTransitionStates.Running
                || CurrentPowerTransitionState == Common.PowerTransitionStates.Resume)
            {
                _logger.LogInformation($"{at20} CurentPowerTransitionState is already {CurrentPowerTransitionState} so ignoring NC... {at20}");
                return;
            }

            CurrentPowerTransitionState = Common.PowerTransitionStates.Resume;
            _logger.LogInformation($"{at20} CurrentPowerTransitionState set to 'Resume' - calling PerformResumeActions... {at20}");
            PerformResumeActions();    
        }
        else
        {
            if (CurrentPowerTransitionState == Common.PowerTransitionStates.Suspend)
            {
                _logger.LogInformation(
                    $"{at20} Ignoring Network UNAVAILABLE event since we already have CurrentPowerTransitionState set to 'Suspend' {at20}");
                return;
            }

            CurrentPowerTransitionState = Common.PowerTransitionStates.Suspend;
            _logger.LogInformation($"{at20} CurrentPowerTransitionState set to 'Suspend' - calling PerformSuspendActions... {at20}");
            PerformSuspendActions();
        }
    }

    private static uint PowerChangeNotifyCallbackRoutine(IntPtr Context, uint powerNotificationType, IntPtr Setting)
    {
        int settingValue = Setting.ToInt32();
        int contextValue = Context.ToInt32();
        Common.PowerNotificationTypes incomingPowerNotificationType =
            (Common.PowerNotificationTypes)Enum.ToObject(typeof(Common.PowerNotificationTypes), powerNotificationType);
        _logger.LogInformation(
            $"************** PowerChangeNotifyCallbackRoutine - powerNotificationType = {incomingPowerNotificationType}, Context={contextValue}, Setting={settingValue}");

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
                _logger.LogInformation(
                    "*************** SystemEventsOnPowerModeChanged: SYSTEM IS SUSPENDING! - WE WILL SUSPEND ANY BACKGROUND TASKS.");
                if (CurrentPowerTransitionState == Common.PowerTransitionStates.Running)
                {
                    CurrentPowerTransitionState = Common.PowerTransitionStates.Suspend;
                    PerformSuspendActions();
                }

                break;
            case PowerModes.Resume:
                _logger.LogInformation(
                    "*************** SystemEventsOnPowerModeChanged: SYSTEM IS RESUMING! - WE WILL RESUME ANY BACKGROUND TASKS.");
                if (CurrentPowerTransitionState == Common.PowerTransitionStates.Suspend)
                {
                    CurrentPowerTransitionState = Common.PowerTransitionStates.Resume;
                    PerformResumeActions();
                }

                break;
// TJE 082025: This is a Battery/AC state change - not going to log this for now - too noisy on laptops
//#if TRACKINGTHIS
            case PowerModes.StatusChange:
                _logger.LogInformation("*************** SystemEventsOnPowerModeChanged: SYSTEM HAS POWER STATUS CHANGE!!");
                break;
//#endif
        }
    }

    private static void PerformSuspendActions()
    {
        _logger.LogInformation("*************** PerformSuspendActions ...");
        // If VPN connected - it will disconnect when network stack collapses - we'll get it on the way up
        VPNStatusAtSuspendTime = VPNTransportIKEV2.GetCurrentVPNState();
        if (VPNStatusAtSuspendTime == ITransportProvider.VPNProviderStatus.VPNStatusConnected)
        {
            // We need to do a clean disconnect now - filters too ugly - messes up reconnect
            _logger.LogInformation(
                "************** PowerChangeNotifyCallbackRoutine - Calling VPNTransportIKEV1.PowerSuspendVPNConnection...");
            VPNTransportIKEV2.PowerSuspendVPNConnection();
        }
    }

    internal static void PerformResumeActions()
    {
        var successful = false;
        _logger.LogInformation("*************** PerformResumeActions ...");
        // We don't care if user brought us out or not - we are resuming
        // IF we were connected, then reconnect now
        _logger.LogInformation($"*************** PerformResumeActions: VPNStatusAtSuspendTime was '{VPNStatusAtSuspendTime}'");
        if (VPNStatusAtSuspendTime == ITransportProvider.VPNProviderStatus.VPNStatusConnected)
        {
            ErrorResponse errorResponse;
            var defaultRetries = Common.DefaultPowerResumeReconnectAttempts;
            var SavedResumeParemeters =
                RegistrySettings.RetrieveGuardianUserSettings(Common.kVpnCallParametersForReboot);
            var VpnResumeParameters =
                JsonSerializer.Deserialize(SavedResumeParemeters, typeof(Dictionary<string, object>)) as
                    Dictionary<string, object>;

            var host = (string)VpnResumeParameters["hostName"];
            _logger.LogInformation("************** PerformResumeActions - VPN WAS CONNECTED AT SUSPENSION.");
            _logger.LogInformation(
                $"************** Check network stack readiness by attempting a status check of the vpn host '{host}");
            var countValue = RegistrySettings.RetrieveGuardianUserSettings(Common.kServicePowerResumeReconnectAttempts);
            if (string.IsNullOrEmpty(countValue)) countValue = defaultRetries;
            int maxRetriesCount = int.Parse(countValue);
            int readinessCheckCount = maxRetriesCount;

            var header = "PerformResumeActions (waiting for host availability): GetServerStatus returned:";
            do
            {
                _logger.LogInformation(
                    $"Calling status of host '{host}' to verify if network is ready - retry # {maxRetriesCount - --readinessCheckCount}");
                GRDGateway gw = new GRDGateway();
                errorResponse = gw.GetServerStatus(host).Result;
                _logger.LogInformation($"{header}: errorResponse from GetServerStatus: {errorResponse}");
                if (!errorResponse.IsError)
                {
                    _logger.LogInformation(
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
                _logger.LogInformation(
                    $" Calling VPNTransportIKEV2.PowerResumeVPNConnection... attempt #{maxRetriesCount - --connectionAttemptCount}");
                errorResponse = VPNTransportIKEV2.PowerResumeVPNConnection();
                if (errorResponse.IsError) Task.Delay(5000).Wait();
            } while (connectionAttemptCount != 0 && errorResponse.IsError);

            _logger.LogInformation(errorResponse.IsError
                ? "**************** PerformResumeActions failed!"
                : "**************** PerformResumeActions successful!");
        }

        CurrentPowerTransitionState = Common.PowerTransitionStates.Running;
    }
}