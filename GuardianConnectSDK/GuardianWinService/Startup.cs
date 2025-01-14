using System.Diagnostics;
using System.Reflection;
using GuardianWinService;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;

namespace GuardianWinService;

public class Startup
{
    // This method gets called by the runtime. Use this method to add services to the container.
    // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
    public void ConfigureServices(IServiceCollection services)
    {
        //var ourPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "NULL";
//        var ourPath = AppContext.BaseDirectory;
//        var configuration = new ConfigurationBuilder()
//            .SetBasePath(ourPath)
//            .AddJsonFile("logsettings.json")
//            .Build();

        Log.Logger.Information($"Startup: Past Builder logging");
        
        Log.Logger.Information("Startup: Calling AddWindowsService()...");
        services.AddWindowsService(options =>
        {
            options.ServiceName = "GuardianFirewall Service";
        });
        Log.Logger.Information("Startup: Adding Singleton ConfigurationManager...");
//        services
//            .AddSingleton<IConfiguration>(new ConfigurationManager())
//            .AddSingleton(Log.Logger);
        
        Log.Logger.Information("Startup: Adding HostedService VpnManagerService...");
        services.AddHostedService<VpnManagerService>();

        Log.Logger.Information("Startup: Adding HostedService ClientPipeService ...");
        services.AddHostedService<ClientPipeService>();

        Log.Logger.Information("========= Leaving Startup.ConfigureServices...");
    }
}