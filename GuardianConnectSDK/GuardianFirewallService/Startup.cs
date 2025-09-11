using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace GuardianFirewallService;

public class Startup
{
    // This method gets called by the runtime. Use this method to add services to the container.
    // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
    public void ConfigureServices(IServiceCollection services)
    {
        Log.Information($"Startup: Past Builder logging");
        
        Log.Information("Startup: Calling AddWindowsService()...");
        services.AddWindowsService(options =>
        {
            options.ServiceName = "GuardianFirewall Service";
        });
        
        Log.Information("Startup: Adding HostedService VpnManagerService...");
        services.AddHostedService<VpnManagerService>();

        Log.Information("Startup: Adding HostedService ClientPipeService ...");
        services.AddHostedService<ClientPipeService>();
        
        Log.Information("========= Leaving Startup.ConfigureServices...");
    }
}