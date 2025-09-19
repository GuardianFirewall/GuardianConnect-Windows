using GuardianConnect.Shared;
using Microsoft.Extensions.Hosting;
using NativeRoutines;

namespace GuardianFirewallService
{
    public class ServiceManager : IHostedService
    {
        public ServiceManager() { }

        protected Task ExecuteAsync(CancellationToken stoppingToken)
        {
            Common.Logger.Information("ServiceManager: ExecuteAsync()...");

            Common.Logger.Information("ServiceManager: [1a.5] Calling RegisterForPowerEvents()...");
            NotificationHandling.RegisterForPowerEvents();
            Common.Logger.Information("ServiceManager: [1b.5] Return from RegisterForPowerEvents()...");
            
            Common.Logger.Information("ServiceManager: [2a.5] Calling RegisterForPowerEvents()...");
            var vpnSvc = new VpnManagerService();
            Common.Logger.Information("ServiceManager: [2b.5] Return from RegisterForPowerEvents()...");
            
            Common.Logger.Information("ServiceManager: [3a.5] Calling RegisterForPowerEvents()...");
            var clientSvc = new ClientPipeService();
            Common.Logger.Information("ServiceManager: [3b.5] Return from RegisterForPowerEvents()...");

            Common.Logger.Information("ServiceManager: [4a.5] Calling VPNService.StartAsync.");
            var t1 = vpnSvc.StartAsync(stoppingToken);
            Common.Logger.Information("ServiceManager: [4b.5] Return from VPNService.StartAsync.");
            
            Common.Logger.Information("ServiceManager: [5a.5] Calling RegisterForPowerEvents()...");
            var t2 = clientSvc.StartAsync(stoppingToken);
            Common.Logger.Information("ServiceManager: [5b.5] Return from RegisterForPowerEvents()...");

            Common.Logger.Information("ServiceManager Doing Task.WaitAll()...");
            Task.WaitAll(t1, t2);

            return Task.CompletedTask;

        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            Common.Logger.Information("In ServiceManager's StartAsync()...");
            return ExecuteAsync(cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Common.Logger.Information("In ServiceManager's StopAsync()...");
            //throw new NotImplementedException();
            return Task.CompletedTask;
        }
    }
}
