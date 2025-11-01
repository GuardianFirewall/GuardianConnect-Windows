using GuardianConnect.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace GuardianFirewallService;

internal static class Startup
{
    // This method gets called by the runtime. Use this method to add services to the container.
    // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
    internal static void ConfigureServices(IServiceCollection services)
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
    
    internal static void SetUpFaultHandlers()
    {
        // S.O.P. for .NET application death
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            var errorMessage = $"AppDomain caught unhandled exception: Sender:{s}, Exception:{e}";
            Log.Error((Exception)e.ExceptionObject, errorMessage);
            Environment.Exit(-1);
        };
        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            var errorMessage = $"TaskScheduler threw exception: Sender:{s}, Exception:{e}";
            var innerFlatten = e.Exception.Flatten().Message;
            Log.Error((Exception)e.Exception, $"Error: {errorMessage}, Inner: {innerFlatten}");
            Environment.Exit(-1);
        };
    }

}