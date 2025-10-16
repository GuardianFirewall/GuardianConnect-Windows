using GuardianConnect.Shared;
using GuardianFirewallService;
using Microsoft.Extensions.Hosting;
using Serilog;
using Win32Calls;

// setup  logging first
var paths = new[] { @"C:\temp", "GuardianFirewallService", "Diagnostics.log" };
const string filler = "##############";

Win32Calls.Class1.foo();

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

// Set up default application fault handlers
Log.Information($"{filler} Program: Calling StartupSetupFaultHandlers... {filler}");
Startup.SetUpFaultHandlers();

Log.Information($"{filler} Program: Calling SetupPowerTransitionHandler... {filler}");
PowerTransitionHandler.SetupPowerTransitionHandler();
Log.Information($"{filler} Program: Return SetupPowerTransitionHandler... {filler}");

Log.Information($"{filler} Program: Calling CreateDefaultBuilder... {filler}");
var hb = Host.CreateDefaultBuilder(args);
Log.Information($"{filler} Program: Return from CreateDefaultBuilder... {filler}");

Log.Information($"{filler} Program: Calling UseWindowService... {filler}");
hb.UseWindowsService();

Log.Information($"{filler} Program: Calling ConfigureServices... {filler}");
hb.ConfigureServices((services) => Startup.ConfigureServices(services));
Log.Information($"{filler} Program: Return from ConfigureServices... {filler}");

var host = hb.Build();
Log.Information($"{filler} Program: Calling host.RunAsync... {filler}");
try
{
    await host.RunAsync();
}
catch (Exception e)
{
    if (e is OperationCanceledException)
    {
        Log.Information("{filler} GuardianFirewall Service operation was administratively cancelled.", filler);
    }
    else
    {
        Log.Error(e, $"{filler} Exception thrown in Fall-thru after host.RunAsync... {e}{filler}");
    }
}
finally
{
    Log.Information($"{filler} GuardianFirewall Service exiting... {filler}");
}
