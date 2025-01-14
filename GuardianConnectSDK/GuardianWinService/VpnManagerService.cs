using NativeRoutines;
using System.Security.AccessControl;
using System.Security.Principal;
using GuardianConnect.Shared;
using GuardianConnect.VPNTransports;
using Microsoft.Extensions.Hosting;


namespace GuardianWinService;

public class VpnManagerService : BackgroundService
{

    public Serilog.ILogger Logger { get; set; }
    
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Logger = Common.Logger;
        VPNTransportIKEV2.Logger = Common.Logger;
        Logger.Information("VPNMGR: TESTING LOG");
        Logger.Information("VpnManager running at: {time}", DateTimeOffset.Now);

        stoppingToken.Register(() => Logger.Information("VpnManagerService is stopping."));

        Logger.Information(
            $"VpnManagerService: creating task...stoppingToken.IsCancllationRequestioned = {stoppingToken.IsCancellationRequested}");
        Logger.Information("VpnManagerService: In task...");
        VPNTransportIKEV2 vpnikeInstance = new VPNTransportIKEV2();

        Logger.Information("Creating Change Event");
        Logger.Information($"stoppingToken.IsCancllationRequestioned = {stoppingToken.IsCancellationRequested}");
        // Create VPNChange Event so Service and UI can wait for notification - OURS - not Ras'
        EventWaitHandleSecurity mSec = new EventWaitHandleSecurity();
        var Everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        EventWaitHandleAccessRule rule = new EventWaitHandleAccessRule(Everyone,
            EventWaitHandleRights.Synchronize | EventWaitHandleRights.Modify, AccessControlType.Allow);
        mSec.AddAccessRule(rule);

        EventWaitHandle VPNStateChangeEventHandle =
            new EventWaitHandle(false, EventResetMode.ManualReset, Common.VPNSTATECHANGE_EVT_NAME);
        VPNStateChangeEventHandle.SetAccessControl(mSec);

        Logger.Information("Checking for active connection...");
        Logger.Information($"stoppingToken.IsCancllationRequestioned = {stoppingToken.IsCancellationRequested}");
        // Let's see if there's an active connection
        if (VPNTransportIKEV2.GetCurrentVPNState() == ITransportProvider.VPNProviderStatus.VPNStatusConnected)
        {
            // Now spawn watcher at native level so we trap CONNECT/DISCONNECT notifications
            Logger.Information("VpnManagerService: Calling StartConnectionStateWatcher...");
            Logger.Information($"stoppingToken.IsCancllationRequestioned = {stoppingToken.IsCancellationRequested}");
            NotificationHandling.StartConnectionStateWatcher();
        }

        vpnikeInstance.StartMonitoringTask();

        try
        {
            Logger.Information(
                $"Going into while() loop. stoppingToken.IsCancllationRequestioned = {stoppingToken.IsCancellationRequested}");
            while (!stoppingToken.IsCancellationRequested)
            {
                Logger.Information(
                    $"VpnService is running... Cancellation Request is {stoppingToken.IsCancellationRequested}");

                // Do stuff with vpnManager here
                await Task.Delay(60000);
            }

            Logger.Information(
                $"Past while() loop. stoppingToken.IsCancllationRequestioned = {stoppingToken.IsCancellationRequested}");
        }
        catch (OperationCanceledException oce) when (!oce.CancellationToken.IsCancellationRequested)
        {
            Logger.Information("OperationCanceledException");
            // When the stopping token is canceled, for example, a call made from services.msc,
            // we shouldn't exit with a non-zero exit code. In other words, this is expected...

            Logger.Error(oce, "{Message}", oce.Message);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "{Message}", ex.Message);

            // Terminates this process and returns an exit code to the operating system.
            // This is required to avoid the 'BackgroundServiceExceptionBehavior', which
            // performs one of two scenarios:
            // 1. When set to "Ignore": will do nothing at all, errors cause zombie services.
            // 2. When set to "StopHost": will cleanly stop the host, and log errors.
            //
            // In order for the Windows Service Management system to leverage configured
            // recovery options, we need to terminate the process with a non-zero exit code.
        }

        Logger.Information("VpnManagerService: past Task Creation clause...");
    }
}