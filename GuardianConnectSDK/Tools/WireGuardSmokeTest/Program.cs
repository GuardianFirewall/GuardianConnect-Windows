// Throwaway dev tool — Phase 4a smoke test for Win32Calls.WireGuard/WireGuardTunnel.
// Reads a wg-quick .conf, creates the WireGuard adapter, applies config, brings Up.
// Press Enter to bring Down + close.
//
//   cd GuardianConnectSDK/Tools/WireGuardSmokeTest
//   dotnet run -c Debug -p:Platform=x64 -- "C:\path\to\config.conf"
//
// IMPORTANT: must run as Administrator. WireGuard adapter creation requires elevation.
//
// At Step 4a the adapter is cryptographically up but has no IP / DNS / routes assigned.
// `ping` will not flow through it. Step 4b will add the iphlpapi calls for those.
// What this smoke test validates:
//   * WireGuardCreateAdapter succeeds
//   * WireGuardSetConfiguration accepts the buffer we built (struct layouts correct)
//   * WireGuardSetAdapterState(Up) returns true
//   * Get-NetAdapter -Name GuardianWG-Smoke shows the adapter
//   * `wg show GuardianWG-Smoke` (if you have the WG CLI installed) shows the peer
//   * Teardown reverses everything cleanly.
//
// Not in the SDK solution by default — added to it for IDE visibility, but never
// signed or shipped.

using System.Security.Principal;
using Serilog;
using Win32Calls.WireGuard;

const string AdapterName = "GuardianWG-Smoke";

if (args.Length < 1)
{
    Console.Error.WriteLine("Usage: WireGuardSmokeTest <path-to-wg-quick.conf>");
    return 2;
}

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console()
    .CreateLogger();

if (!IsRunningAsAdmin())
{
    Log.Error("Must be run as Administrator. WireGuardCreateAdapter requires elevation.");
    return 1;
}

var configPath = args[0];
Log.Information("Reading WireGuard config from {Path}", configPath);

WireGuardConfig config;
try
{
    var text = await File.ReadAllTextAsync(configPath);
    config = WireGuardConfigParser.Parse(text);
}
catch (Exception ex)
{
    Log.Error(ex, "Failed to read/parse config");
    return 1;
}

Log.Information(
    "Parsed config: endpoint={Endpoint}, allowedIps={AipCount}, addresses={AddrCount}, dns={DnsCount}",
    config.Peer.Endpoint, config.Peer.AllowedIPs.Count, config.Addresses.Count, config.DnsServers.Count);

using var tunnel = new WireGuardTunnel(AdapterName);
try
{
    Log.Information("Activating tunnel adapter '{Name}' ...", AdapterName);
    tunnel.Activate(config);
    Log.Information("Adapter Up. LUID=0x{Luid:X16}", tunnel.AdapterLuid);
    Log.Information("");
    Log.Information("Verify externally (in another shell):");
    Log.Information("  Get-NetAdapter -Name {Name}", AdapterName);
    Log.Information("  wg show {Name}        # if WireGuard CLI is installed", AdapterName);
    Log.Information("");
    Log.Information("Note: no IP / DNS / routes are configured (Step 4b). Traffic won't flow.");
    Log.Information("Press Enter to tear down ...");
    Console.ReadLine();
}
catch (Exception ex)
{
    Log.Error(ex, "Activation failed");
    return 1;
}

Log.Information("Tearing down ...");
// tunnel.Dispose() runs here via the `using` statement.
return 0;

static bool IsRunningAsAdmin()
{
    using var identity = WindowsIdentity.GetCurrent();
    var principal = new WindowsPrincipal(identity);
    return principal.IsInRole(WindowsBuiltInRole.Administrator);
}
