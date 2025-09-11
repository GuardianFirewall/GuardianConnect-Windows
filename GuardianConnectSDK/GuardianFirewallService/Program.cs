using System.Text;
using GuardianConnect.Shared;
using GuardianFirewallService;
using Microsoft.Extensions.Hosting;
using Serilog;

// setup  logging first
var paths = new[] { @"C:\temp", "GuardianFirewallService", "Diagnostics.log" };
Common.LogFilePath = Path.Combine(paths);
Common.SetUpLogging();

// Checking if running as SYSTEM
Log.Information($"MachineName='{Environment.MachineName}'. Username='{Environment.UserName}. User Domain Name='{Environment.UserDomainName}'. IsPrivileged = {Environment.IsPrivilegedProcess}");
//
if (Environment.UserName != $"{Environment.MachineName}$")
{
    Log.Error("*!*!*!*!*!*!*!*!*!*!*! THIS SERVICE PROCESS IS NOT EXECUTING AS SYSTEM USER. TERMINATING NOW *!*!*!*!*!*!*!*!*!*!*!*!");
    Environment.Exit(-1);
}

PowerTransitionHandler.SetupPowerTransitionHandler();

var hb = Host.CreateDefaultBuilder(args);
hb.UseWindowsService();
hb.ConfigureServices((services) => new Startup().ConfigureServices(services));

// DO WE NEED THIS??
//PowerHandlerService powerHandlerService = new PowerHandlerService();

var host = hb.Build();
await host.RunAsync();