using System.Net.NetworkInformation;
using System.Text.Json;
using GuardianConnect.Abstractions;
using GuardianConnect.API;
using GuardianConnect.Shared;
using GuardianConnect.VPNTransports;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;

namespace GuardianConnect.Services;

public static class ServicePowerEventsHandler
{
    private const string ast20 = "********************";
    private const string at20 = "@@@@@@@@@@@@@@@@@@@@";
    private const string dash20 = "--------------------";
    private const string eql20 = "====================";
    private const string pct20 = "%%%%%%%%%%%%%%%%%%%%";
    private const string hash20 = "####################";
    private const string plus20 = "++++++++++++++++++++";

    private static Common.PowerTransitionStates CurrentPowerTransitionState = Common.PowerTransitionStates.Running;

    private static ITransportProvider.VPNProviderStatus VPNStatusAtSuspendTime =
        ITransportProvider.VPNProviderStatus.VPNStatusInvalid;

    private static ILogger _logger = NullLogger.Instance;

    public static ILogger Logger
    {
        get
        {
            if (_logger == NullLogger.Instance) _logger = StaticLoggerFactory.CreateLogger("ServicePowerEventsHandler");
            return _logger;
        }
    }


    internal static bool ConnectedAtSuspendTime()
    {
        return VPNStatusAtSuspendTime == ITransportProvider.VPNProviderStatus.VPNStatusConnected;
    }

    internal static void SetConnectedAtSuspendTime()
    {
        VPNStatusAtSuspendTime = ITransportProvider.VPNProviderStatus.VPNStatusConnected;
        Logger.LogInformation(
            $"SetVPNStateAtSuspendTime() called from Poller... VPNStatusAtSuspendTime now set to {VPNStatusAtSuspendTime}");
    }

    internal static void ResetVpnStatusAtSuspendTime()
    {
        VPNStatusAtSuspendTime = ITransportProvider.VPNProviderStatus.VPNStatusInvalid;
    }

    public static void SetupServicePowerEventsHandler()
    {
        Logger.LogInformation("ServicePowerEventsHandler.SetupServicePowerEventsHandler: TESTING LOG!");
        SystemEvents.PowerModeChanged += SystemEventsOnPowerModeChanged;
        NetworkChange.NetworkAddressChanged += NetworkChangeOnNetworkAddressChanged;
        NetworkChange.NetworkAvailabilityChanged += NetworkChangeOnNetworkAvailabilityChanged;
        PowerTransitionMonitor.RegisterForPowerNotifications(PowerChangeNotifyCallbackRoutine);
        // Add Resume function to VPNTransportIKEV2 delegate for sake of Disconnect recovery
        VPNTransportIKEV2.PowerResumeActions = PerformResumeActions;
        VPNTransportIKEV2.SetVPNStateAtSuspend = SetConnectedAtSuspendTime;
        VPNTransportIKEV2.ResetVPNStateAtSuspend = ResetVpnStatusAtSuspendTime;
    }

    public static void NetworkChangeOnNetworkAddressChanged(object? sender, EventArgs e)
    {
        Logger.LogInformation(
            $"{at20} ServicePowerEventsHandler. NetworkChangeOnNetworkAddressChanged: Network address changed. GetIsNetworkAvailable = {NetworkInterface.GetIsNetworkAvailable()}");
        var adapters = NetworkInterface.GetAllNetworkInterfaces();
        var logLines = new List<string>();
        var byType = new SortedDictionary<string, List<string>>();
        foreach (var n in adapters)
        {
            // Let's skip some we don't care about
            if (n.Description.Contains("Microsoft Wi-Fi Direct Virtual Adapter")) continue;
            if (n.Description.Contains("Microsoft Kernel Debug Network Adapter")) continue;
            if (n.Description.Contains("Bluetooth")) continue;
            if (n.Description.Contains("Teredo")) continue;
            if (n.Description.Contains("6to4")) continue;
            if (n.Description.Contains("IP-HTTPS")) continue;
            if (n.Description.Contains("Software Loopback Interface")) continue;
            if (n.Description.Contains("Network Monitor")) continue;
            var nit = n.NetworkInterfaceType.ToString();
            if (!byType.Keys.Contains(nit)) byType.Add(nit, new List<string>());
            var line = $"{n.OperationalStatus} - '{n.Name}' [Desc: '{n.Description}']";
            byType[nit].Add($"\t{line}");
        }

        logLines.Add(Environment.NewLine);
        foreach (var item in byType)
        {
            logLines.Add($"{item.Key}:");
            item.Value.Sort();
            logLines.AddRange(item.Value);
        }

        Logger.LogInformation(string.Join(Environment.NewLine, logLines));
    }

    public static void NetworkChangeOnNetworkAvailabilityChanged(object? sender, NetworkAvailabilityEventArgs e)
    {
        // Check network availability.
        // If unavailable and we've already set Suspend mode (handled), then ignore, else set Suspend and do PerformSuspend...
        // If available and we were already in Resume or Running, then ignore, else set Resume and do PerformResume...

        Logger.LogInformation($"{at20} Network availability changed to {e.IsAvailable} {at20}");
        if (e.IsAvailable)
        {
            if (CurrentPowerTransitionState == Common.PowerTransitionStates.Running
                || CurrentPowerTransitionState == Common.PowerTransitionStates.Resume)
            {
                Logger.LogInformation(
                    $"{at20} CurentPowerTransitionState is already {CurrentPowerTransitionState} so ignoring NC... {at20}");
                return;
            }

            CurrentPowerTransitionState = Common.PowerTransitionStates.Resume;
            Logger.LogInformation(
                $"{at20} CurrentPowerTransitionState set to 'Resume' - calling PerformResumeActions... {at20}");
            PerformResumeActions();
        }
        else
        {
            if (CurrentPowerTransitionState == Common.PowerTransitionStates.Suspend)
            {
                Logger.LogInformation(
                    $"{at20} Ignoring Network UNAVAILABLE event since we already have CurrentPowerTransitionState set to 'Suspend' {at20}");
                return;
            }

            CurrentPowerTransitionState = Common.PowerTransitionStates.Suspend;
            Logger.LogInformation(
                $"{at20} CurrentPowerTransitionState set to 'Suspend' - calling PerformSuspendActions... {at20}");
            PerformSuspendActions();
        }
    }

    private static uint PowerChangeNotifyCallbackRoutine(IntPtr Context, uint powerNotificationType, IntPtr Setting)
    {
        var settingValue = Setting.ToInt32();
        var contextValue = Context.ToInt32();
        var incomingPowerNotificationType =
            (Common.PowerNotificationTypes)Enum.ToObject(typeof(Common.PowerNotificationTypes), powerNotificationType);
        Logger.LogInformation(
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
                Logger.LogInformation(
                    "*************** SystemEventsOnPowerModeChanged: SYSTEM IS SUSPENDING! - WE WILL SUSPEND ANY BACKGROUND TASKS.");
                if (CurrentPowerTransitionState == Common.PowerTransitionStates.Running)
                {
                    CurrentPowerTransitionState = Common.PowerTransitionStates.Suspend;
                    PerformSuspendActions();
                }

                break;
            case PowerModes.Resume:
                Logger.LogInformation(
                    "*************** SystemEventsOnPowerModeChanged: SYSTEM IS RESUMING! - WE WILL RESUME ANY BACKGROUND TASKS.");
                if (CurrentPowerTransitionState == Common.PowerTransitionStates.Suspend)
                {
                    CurrentPowerTransitionState = Common.PowerTransitionStates.Resume;
                    PerformResumeActions();
                }

                break;
// This is a Battery/AC state change - not going to log this for now - too noisy on laptops
//#if TRACKINGTHIS
            case PowerModes.StatusChange:
                Logger.LogInformation(
                    "*************** SystemEventsOnPowerModeChanged: SYSTEM HAS POWER STATUS CHANGE!!");
                break;
//#endif
        }
    }

    private static void PerformSuspendActions()
    {
        Logger.LogInformation("*************** PerformSuspendActions ...");
        if (VPNStatusAtSuspendTime == ITransportProvider.VPNProviderStatus.VPNStatusConnected)
        {
            // If VPN connected - it will disconnect when network stack collapses - we'll get it on the way up
            var vpnStatusRightNow = VPNTransportIKEV2.GetCurrentVPNState();
            if (vpnStatusRightNow == ITransportProvider.VPNProviderStatus.VPNStatusConnected)
            {
                // We need to do a clean disconnect now - filters too ugly - messes up reconnect
                Logger.LogInformation(
                    "************** PowerChangeNotifyCallbackRoutine - Calling VPNTransportIKEV1.PowerSuspendVPNConnection...");
                VPNTransportIKEV2.PowerSuspendVPNConnection(VPNStatusAtSuspendTime ==
                                                            ITransportProvider.VPNProviderStatus.VPNStatusDisconnected);
            }
        }
    }

    internal static void PerformResumeActions()
    {
        Logger.LogInformation("*************** PerformResumeActions ...");
        // We don't care if user brought us out or not - we are resuming
        // IF we were connected, then reconnect now
        Logger.LogInformation(
            $"*************** PerformResumeActions: VPNStatusAtSuspendTime was '{VPNStatusAtSuspendTime}'");
        if (VPNStatusAtSuspendTime == ITransportProvider.VPNProviderStatus.VPNStatusConnected)
        {
            ErrorResponse errorResponse;
            var defaultRetries = Common.DefaultPowerResumeReconnectAttempts;
            var SavedResumeParemeters =
                RegistrySettings.RetrieveGuardianUserSettings(Common.kVpnCallParametersForReboot);
            var vpnResumeParameters = JsonSerializer.Deserialize<VPNCallParameters>(SavedResumeParemeters,
                VPNCallParametersJsonContext.Default.VPNCallParameters);

            var host = vpnResumeParameters?.VpnHostName ?? string.Empty;
            Logger.LogInformation("************** PerformResumeActions - VPN WAS CONNECTED AT SUSPENSION.");
            Logger.LogInformation(
                $"************** Check network stack readiness by attempting a status check of the vpn host '{host}");
            var countValue = RegistrySettings.RetrieveGuardianUserSettings(Common.kServicePowerResumeReconnectAttempts);
            if (string.IsNullOrEmpty(countValue)) countValue = defaultRetries;
            var maxRetriesCount = int.Parse(countValue);
            var readinessCheckCount = maxRetriesCount;

            var header = "PerformResumeActions (waiting for host availability): GetServerStatus returned:";
            do
            {
                Logger.LogInformation(
                    $"Calling status of host '{host}' to verify if network is ready - retry # {maxRetriesCount - --readinessCheckCount}");
                errorResponse = GRDGateway.GetServerStatus(host).Result;
                Logger.LogInformation($"{header}: errorResponse from GetServerStatus: {errorResponse}");
                if (!errorResponse.IsError)
                {
                    Logger.LogInformation(
                        $"************** PerformResumeActions - NO error returned from GetServerStatus. Response message = '{errorResponse.Message}. StatusCode={errorResponse.HttpResponse.StatusCode}");
                    break; // ok - not an error - so then let's break and try to connect
                }

                Task.Delay(5000).Wait();
            } while (--readinessCheckCount != 0);

            // let's fall through and see if we can connect anyway
            // Let's reconnect
            var connectionAttemptCount = maxRetriesCount;
            do
            {
                Logger.LogInformation(
                    $" Calling VPNTransportIKEV2.PowerResumeVPNConnection... attempt #{maxRetriesCount - --connectionAttemptCount}");
                errorResponse = VPNTransportIKEV2.PowerResumeVPNConnection();
                if (errorResponse.IsError) Task.Delay(5000).Wait();
            } while (connectionAttemptCount != 0 && errorResponse.IsError);

            Logger.LogInformation(errorResponse.IsError
                ? "**************** PerformResumeActions failed!"
                : "**************** PerformResumeActions successful!");
        }

        CurrentPowerTransitionState = Common.PowerTransitionStates.Running;
    }
}