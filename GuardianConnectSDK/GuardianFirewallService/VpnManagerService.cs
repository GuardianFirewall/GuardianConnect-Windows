using NativeRoutines;
using System.Security.AccessControl;
using System.Security.Principal;
using GuardianConnect.Shared;
using GuardianConnect.VPNTransports;
using Microsoft.Extensions.Hosting;
using Serilog;


namespace GuardianFirewallService;

public class VpnManagerService : BackgroundService
{

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Log.Information("VPNMGR: TESTING LOG");
        Log.Information("VpnManager running at: {time}", DateTimeOffset.Now);

        stoppingToken.Register(() => Log.Information("VpnManagerService is stopping."));

        Log.Information(
            $"VpnManagerService: creating task...stoppingToken.IsCancllationRequestioned = {stoppingToken.IsCancellationRequested}");
        Log.Information("VpnManagerService: In task...");
        VPNTransportIKEV2 vpnikeInstance = new VPNTransportIKEV2();

        Log.Information("Creating Change Event");
        Log.Information($"stoppingToken.IsCancllationRequestioned = {stoppingToken.IsCancellationRequested}");
        // Create VPNChange Event so Service and UI can wait for notification - OURS - not Ras'
        EventWaitHandleSecurity mSec = new EventWaitHandleSecurity();
        var Everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        EventWaitHandleAccessRule rule = new EventWaitHandleAccessRule(Everyone,
            EventWaitHandleRights.Synchronize | EventWaitHandleRights.Modify, AccessControlType.Allow);
        mSec.AddAccessRule(rule);

        EventWaitHandle VPNStateChangeEventHandle =
            new EventWaitHandle(false, EventResetMode.ManualReset, Common.VPNSTATECHANGE_EVT_NAME);
        VPNStateChangeEventHandle.SetAccessControl(mSec);

        Log.Information("Checking for active connection...");
        Log.Information($"stoppingToken.IsCancllationRequestioned = {stoppingToken.IsCancellationRequested}");
        // Let's see if there's an active connection
        if (VPNTransportIKEV2.GetCurrentVPNState() == ITransportProvider.VPNProviderStatus.VPNStatusConnected)
        {
            // Now spawn watcher at native level so we trap CONNECT/DISCONNECT notifications
            Log.Information("VpnManagerService: Calling StartConnectionStateWatcher...");
            Log.Information($"stoppingToken.IsCancllationRequestioned = {stoppingToken.IsCancellationRequested}");
            NotificationHandling.StartConnectionStateWatcher();
        }

        vpnikeInstance.StartMonitoringTask();

        try
        {
            var heartbeatCounter = 0;
            var priorMessage =
                    $"VpnService is running... Cancellation Request is {stoppingToken.IsCancellationRequested}";
            Log.Information(
                $"Going into while() loop. stoppingToken.IsCancllationRequestioned = {stoppingToken.IsCancellationRequested}");
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