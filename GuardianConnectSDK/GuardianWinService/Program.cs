using GuardianConnect.Shared;
using GuardianWinService;
using Microsoft.Extensions.Hosting;

// setup  logging first
var paths = new[] { @"C:\temp", "GuardianFirewallService", "Diagnostics.log" };
Common.LogFilePath = Path.Combine(paths);
Common.SetUpLogging();

var hb = Host.CreateDefaultBuilder(args);
hb.UseWindowsService();
hb.ConfigureServices((services) => new Startup().ConfigureServices(services));

var host = hb.Build();
await host.RunAsync();