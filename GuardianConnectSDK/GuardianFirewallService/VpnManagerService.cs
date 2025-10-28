using System.Security.AccessControl;
using System.Security.Principal;
using GuardianConnect.Shared;
using GuardianConnect.VPNTransports;
using Microsoft.Extensions.Hosting;
using Serilog;
using Win32Calls;
        using System.Security.AccessControl;
        using System.Threading;


namespace GuardianFirewallService;

public class VpnManagerService : BackgroundService
{
    private static void ShowSecurity(EventWaitHandleSecurity security, string who)
    {
        Log.Information($"\r\nCurrent access rules for {who}:\r\n");

        foreach (EventWaitHandleAccessRule ar in
                 security.GetAccessRules(true, true, typeof(NTAccount)))
        {
            Log.Information("        User: {0}", ar.IdentityReference);
            Log.Information("        Type: {0}", ar.AccessControlType);
            Log.Information("      Rights: {0}", ar.EventWaitHandleRights);
            Log.Information("");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Log.Information("VPNMGR: TESTING LOG");
        Log.Information("VpnManager running at: {time}", DateTimeOffset.Now);

        stoppingToken.Register(() => Log.Information("VpnManagerService is stopping."));

        Log.Information($"VpnManagerService: creating task...stoppingToken.IsCancllationRequestioned = {stoppingToken.IsCancellationRequested}");
        Log.Information("VpnManagerService: In task...");
        VPNTransportIKEV2 vpnikeInstance = new VPNTransportIKEV2();

        Log.Information("Creating Change Event for listeners - SERVICE-SIDE and CLIENT-SIDE...");
        Log.Information($"stoppingToken.IsCancllationRequestioned = {stoppingToken.IsCancellationRequested}");
        // Create VPNChange Event so Service and UI can wait for notification - OURS - not Ras'
#if BRAVE
        // From Brave AI

        // ...

        EventWaitHandleSecurity security = new EventWaitHandleSecurity();
        // Add access rules for specific users or groups
        security.AddAccessRule(new EventWaitHandleAccessRule("Everyone", EventWaitHandleRights.FullControl, AccessControlType.Allow));

        EventWaitHandle globalEvent = new EventWaitHandle(false, EventResetMode.ManualReset, "MyGlobalEventName", out bool createdNew, security);
        //
#endif
#if true
        var Everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        EventWaitHandleAccessRule rule = new EventWaitHandleAccessRule(Everyone,
//            EventWaitHandleRights.Synchronize | EventWaitHandleRights.Modify |
            EventWaitHandleRights.FullControl, AccessControlType.Allow);

        EventWaitHandleSecurity mSecSvc = new EventWaitHandleSecurity();
        mSecSvc.AddAccessRule(rule);
        EventWaitHandle H_VPNStateChangeServiceEvent = new EventWaitHandle(false, EventResetMode.ManualReset, Common.VPNEVT_NAME_SVRSIDE);
        var currentAC = H_VPNStateChangeServiceEvent.GetAccessControl();
        H_VPNStateChangeServiceEvent.SetAccessControl(mSecSvc);
        Win32Calls.NotificationHandler.VPNServiceNotifierHandle = H_VPNStateChangeServiceEvent;
        var afterAC = H_VPNStateChangeServiceEvent.GetAccessControl();
        ShowSecurity(afterAC, "Service");

        EventWaitHandleSecurity mSecCli = new EventWaitHandleSecurity();
        mSecCli.AddAccessRule(rule);
        EventWaitHandle H_VPNStateChangeClientEvent = new EventWaitHandle(false, EventResetMode.ManualReset, Common.VPNEVT_NAME_CLIENTSIDE);
        currentAC = H_VPNStateChangeClientEvent.GetAccessControl();
        H_VPNStateChangeClientEvent.SetAccessControl(mSecCli);
        afterAC = H_VPNStateChangeClientEvent.GetAccessControl();
        ShowSecurity(afterAC, "Client");
        Win32Calls.NotificationHandler.VPNClientNotifierHandle = H_VPNStateChangeClientEvent;
#else
        //NotificationHandling.CreateListenerNotifyEvents();
        NotificationHandler.CreateListenerNotifyEvents();
#endif

        Log.Information("Checking for active connection...");
        Log.Information($"stoppingToken.IsCancllationRequestioned = {stoppingToken.IsCancellationRequested}");
        // Let's see if there's an active connection
        if (VPNTransportIKEV2.GetCurrentVPNState() == ITransportProvider.VPNProviderStatus.VPNStatusConnected)
        {
            // Now spawn watcher at native level so we trap CONNECT/DISCONNECT notifications
            Log.Information("VpnManagerService: Calling StartConnectionStateWatcher...");
            Log.Information($"stoppingToken.IsCancllationRequestioned = {stoppingToken.IsCancellationRequested}");
            NotificationHandler.StartRasConnectStateWatcher();
        }

        vpnikeInstance.StartMonitoringTask();

        try
        {
            var heartbeatCounter = 0;
            var priorMessage = $"VpnService is running... Cancellation Request is {stoppingToken.IsCancellationRequested}";
            Log.Information( $"Going into while() loop. stoppingToken.IsCancllationRequestioned = {stoppingToken.IsCancellationRequested}");
            while (!stoppingToken.IsCancellationRequested)
            {
                var currentMessage =
                    $"VpnService is running... Cancellation Request is {stoppingToken.IsCancellationRequested}";

                if (!currentMessage.Equals(priorMessage))
                {
                    Log.Information(currentMessage);
                    heartbeatCounter = 0;
                    priorMessage = currentMessage;
                }
                else if (++heartbeatCounter % 5 == 0) Log.Information("VpnService is running...");
                
                // Do stuff with vpnManager here
                await Task.Delay(60000);
            }

            Log.Information(
                $"Past while() loop. stoppingToken.IsCancllationRequestioned = {stoppingToken.IsCancellationRequested}");
        }
        catch (OperationCanceledException oce) when (!oce.CancellationToken.IsCancellationRequested)
        {
            Log.Information("OperationCanceledException");
            // When the stopping token is canceled, for example, a call made from services.msc,
            // we shouldn't exit with a non-zero exit code. In other words, this is expected...

            Log.Error(oce, "{Message}", oce.Message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "{Message}", ex.Message);

            // Terminates this process and returns an exit code to the operating system.
            // This is required to avoid the 'BackgroundServiceExceptionBehavior', which
            // performs one of two scenarios:
            // 1. When set to "Ignore": will do nothing at all, errors cause zombie services.
            // 2. When set to "StopHost": will cleanly stop the host, and log errors.
            //
            // In order for the Windows Service Management system to leverage configured
            // recovery options, we need to terminate the process with a non-zero exit code.
        }

        Log.Information("VpnManagerService: past Task Creation clause...");
    }
}