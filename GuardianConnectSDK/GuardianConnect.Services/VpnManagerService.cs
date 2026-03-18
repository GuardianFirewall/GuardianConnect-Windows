using System.Security.AccessControl;
using System.Security.Principal;
using GuardianConnect.Abstractions;
using GuardianConnect.Shared;
using GuardianConnect.VPNTransports;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Win32Calls;

namespace GuardianConnect.Services;

public class VpnManagerService : BackgroundService
{
    private static ILogger _logger = NullLogger.Instance;
    public VpnManagerService(ILogger<VpnManagerService> logger)
    {
        _logger = logger;
    }
    
    private static void ShowSecurity(EventWaitHandleSecurity security, string who)
    {
        _logger.LogInformation($"\r\nCurrent access rules for {who}:\r\n");

        foreach (EventWaitHandleAccessRule ar in
                 security.GetAccessRules(true, true, typeof(NTAccount)))
        {
            _logger.LogInformation("        User: {0}", ar.IdentityReference);
            _logger.LogInformation("        Type: {0}", ar.AccessControlType);
            _logger.LogInformation("      Rights: {0}", ar.EventWaitHandleRights);
            _logger.LogInformation("");
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("VPNMGR: TESTING LOG");
        _logger.LogInformation("VpnManager running at: {time}", DateTimeOffset.Now);

        stoppingToken.Register(() => _logger.LogInformation("VpnManagerService is stopping."));

        _logger.LogInformation($"VpnManagerService: creating task...stoppingToken.IsCancllationRequestioned = {stoppingToken.IsCancellationRequested}");
        _logger.LogInformation("VpnManagerService: In task...");
        VPNTransportIKEV2 vpnikeInstance = new VPNTransportIKEV2();

        _logger.LogInformation("Creating Change Event for listeners - SERVICE-SIDE and CLIENT-SIDE...");
        _logger.LogInformation($"stoppingToken.IsCancllationRequestioned = {stoppingToken.IsCancellationRequested}");
        // Create VPNChange Event so Service and UI can wait for notification - OURS - not Ras'
        
        var Everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
        EventWaitHandleAccessRule rule = new EventWaitHandleAccessRule(Everyone,
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

        _logger.LogInformation("Checking for active connection...");
        _logger.LogInformation($"stoppingToken.IsCancllationRequestioned = {stoppingToken.IsCancellationRequested}");
        // Let's see if there's an active connection
        if (VPNTransportIKEV2.GetCurrentVPNState() == ITransportProvider.VPNProviderStatus.VPNStatusConnected)
        {
            // Now spawn watcher at native level so we trap CONNECT/DISCONNECT notifications
            _logger.LogInformation("VpnManagerService: Calling StartConnectionStateWatcher...");
            _logger.LogInformation($"stoppingToken.IsCancllationRequestioned = {stoppingToken.IsCancellationRequested}");
            NotificationHandler.StartRasConnectStateWatcher();
        }

        vpnikeInstance.StartMonitoringTask(stoppingToken);

        try
        {
            var heartbeatCounter = 0;
            var priorMessage = $"VpnService is running... Cancellation Request is {stoppingToken.IsCancellationRequested}";
            _logger.LogInformation( $"Going into while() loop. stoppingToken.IsCancllationRequestioned = {stoppingToken.IsCancellationRequested}");
            while (!stoppingToken.IsCancellationRequested)
            {
                var currentMessage =
                    $"VpnService is running... Cancellation Request is {stoppingToken.IsCancellationRequested}";

                if (!currentMessage.Equals(priorMessage))
                {
                    _logger.LogInformation(currentMessage);
                    heartbeatCounter = 0;
                    priorMessage = currentMessage;
                }
                else if (++heartbeatCounter % 5 == 0) _logger.LogInformation("VpnService is running...");
                
                // Do stuff with vpnManager here
                await Task.Delay(60000, stoppingToken);
            }

            _logger.LogInformation(
                $"Past while() loop. stoppingToken.IsCancllationRequestioned = {stoppingToken.IsCancellationRequested}");
        }
        catch (OperationCanceledException oce) when (!oce.CancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("OperationCanceledException");
            // When the stopping token is canceled, for example, a call made from services.msc,
            // we shouldn't exit with a non-zero exit code. In other words, this is expected...

            _logger.LogError(oce, "{Message}", oce.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "{Message}", ex.Message);

            // Terminates this process and returns an exit code to the operating system.
            // This is required to avoid the 'BackgroundServiceExceptionBehavior', which
            // performs one of two scenarios:
            // 1. When set to "Ignore": will do nothing at all, errors cause zombie services.
            // 2. When set to "StopHost": will cleanly stop the host, and log errors.
            //
            // In order for the Windows Service Management system to leverage configured
            // recovery options, we need to terminate the process with a non-zero exit code.
        }

        _logger.LogInformation("VpnManagerService: past Task Creation clause...");
    }
}