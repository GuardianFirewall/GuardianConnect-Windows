using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GuardianConnect.Shared;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace GuardianWinService
{
    public class ServiceManager : IHostedService
    {
        public ServiceManager() { }

        protected Task ExecuteAsync(CancellationToken stoppingToken)
        {

            Common.Logger.Information("In ServiceManager's ExecuteAsync()...");
            var vpnSvc = new VpnManagerService();
            var clientSvc = new ClientPipeService();

            var t1 = vpnSvc.StartAsync(stoppingToken);
            var t2 = clientSvc.StartAsync(stoppingToken);

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
            throw new NotImplementedException();
        }
    }
}
