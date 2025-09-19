using System.Text;
using GuardianConnect.Shared;
using GuardianFirewallService;
using Microsoft.Extensions.Hosting;
using Serilog;

// setup  logging first
var paths = new[] { @"C:\temp", "GuardianFirewallService", "Diagnostics.log" };
const string filler = "##############";

Common.LogFilePath = Path.Combine(paths);
Common.SetUpLogging();

// Checking if running as SYSTEM
Log.Information($"{Environment.NewLine}{Environment.NewLine}{filler} Guardian Firewall Service Started {filler}");
Log.Information($"MachineName='{Environment.MachineName}'. Username='{Environment.UserName}. User Domain Name='{Environment.UserDomainName}'. IsPrivileged = {Environment.IsPrivilegedProcess}");
//
if (Environment.UserName != $"{Environment.MachineName}$")
{
    Log.Error("*!*!*!*!*!*!*!*!*!*!*! THIS SERVICE PROCESS IS NOT EXECUTING AS SYSTEM USER. TERMINATING NOW *!*!*!*!*!*!*!*!*!*!*!*!");
    Environment.Exit(-1);
}

Log.Information($"{filler} Program: Calling SetupPowerTransitionHandler... {filler}");
PowerTransitionHandler.SetupPowerTransitionHandler();
Log.Information($"{filler} Program: Return SetupPowerTransitionHandler... {filler}");

Log.Information($"{filler} Program: Calling CreateDefaultBuilder... {filler}");
var hb = Host.CreateDefaultBuilder(args);
Log.Information($"{filler} Program: Return from CreateDefaultBuilder... {filler}");

Log.Information($"{filler} Program: Calling UseWindowService... {filler}");
hb.UseWindowsService();

Log.Information($"{filler} Program: Calling ConfigureServices... {filler}");
hb.ConfigureServices((services) => new Startup().ConfigureServices(services));
Log.Information($"{filler} Program: Return from ConfigureServices... {filler}");

var host = hb.Build();
Log.Information($"{filler} Program: Calling host.RunAsync... {filler}");
await host.RunAsync();
Log.Information($"{filler} Fall-thru after host.RunAsync... {filler}");
