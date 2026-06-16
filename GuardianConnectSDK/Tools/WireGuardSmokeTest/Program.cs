// Throwaway dev tool — Phase 4b smoke test for the full WireGuard transport.
// Reads a wg-quick .conf, creates the WireGuard adapter, applies WG config, pins
// the adapter's interface metric, attaches Addresses + AllowedIPs as routes, sets
// DNS, and waits. Press Enter to tear everything down.
//
//   cd GuardianConnectSDK/Tools/WireGuardSmokeTest
//   dotnet run -c Debug -p:Platform=x64 -- "C:\path\to\config.conf"
//
// IMPORTANT: must run as Administrator (WireGuardCreateAdapter, route table edits,
// SetInterfaceDnsSettings — all need elevation).
//
// Validates end-to-end:
//   * WireGuardCreateAdapter / SetConfiguration / SetAdapterState(Up) succeed
//   * IP assigned to the adapter (visible in ipconfig)
//   * Routes installed (`Get-NetRoute -InterfaceAlias GuardianWG-Smoke`)
//   * DNS set on the adapter (`Get-DnsClientServerAddress -InterfaceAlias ...`)
//   * `ping 1.1.1.1` should now flow through the tunnel
//   * Teardown destroys the adapter, sweeping away all attached IP/route/DNS state.
//
// Mirrors the orchestration logic in GuardianConnect.Services/VpnTunnelManager.cs
// without depending on the Services project (smoke test stays lightweight).

using System.ComponentModel;
using System.Security.Principal;
using Serilog;
using Win32Calls.WFP;
using Win32Calls.WireGuard;

const string AdapterName = "GuardianWG-Smoke";
const uint TunnelInterfaceMetric = 1;

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
    Log.Error("Must be run as Administrator. WireGuard adapter ops + route/DNS edits require elevation.");
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

    ApplyAdapterConfiguration(tunnel.AdapterLuid, config);

    Log.Information("");
    Log.Information("Tunnel fully configured. Try in another shell:");
    Log.Information("  Get-NetAdapter -Name {Name}", AdapterName);
    Log.Information("  Get-NetRoute -InterfaceAlias {Name}", AdapterName);
    Log.Information("  Get-DnsClientServerAddress -InterfaceAlias {Name}", AdapterName);
    Log.Information("  ping 1.1.1.1");
    Log.Information("  curl https://api.ipify.org   # should show the VPN exit IP");
    Log.Information("");
    Log.Information("Press Enter to tear down ...");
    Console.ReadLine();
}
catch (Exception ex)
{
    Log.Error(ex, "Activation failed");
    return 1;
}

Log.Information("Tearing down ...");
// tunnel.Dispose() via `using` destroys the adapter, which Windows treats as
// "remove all IPs / routes / DNS attached to this interface" automatically.
return 0;

static void ApplyAdapterConfiguration(ulong luid, WireGuardConfig config)
{
    var rv = AdapterIpDnsRoutes.SetInterfaceMetric(luid, TunnelInterfaceMetric);
    if (rv != 0) throw new Win32Exception(rv, $"SetInterfaceMetric({TunnelInterfaceMetric}) failed.");
    Log.Debug("Set interface metric to {Metric}", TunnelInterfaceMetric);

    foreach (var addr in config.Addresses)
    {
        rv = AdapterIpDnsRoutes.AddUnicastAddress(luid, addr.Address, (byte)addr.PrefixLength);
        if (rv != 0) throw new Win32Exception(rv, $"AddUnicastAddress({addr.Address}/{addr.PrefixLength}) failed.");
        Log.Debug("Added unicast address {Address}/{Prefix}", addr.Address, addr.PrefixLength);
    }

    foreach (var network in config.Peer.AllowedIPs)
    {
        rv = AdapterIpDnsRoutes.AddRoute(luid, network.Address, (byte)network.PrefixLength);
        if (rv != 0) throw new Win32Exception(rv, $"AddRoute({network.Address}/{network.PrefixLength}) failed.");
        Log.Debug("Added route {Address}/{Prefix}", network.Address, network.PrefixLength);
    }

    if (config.DnsServers.Count > 0)
    {
        rv = AdapterIpDnsRoutes.SetDnsServers(luid, config.DnsServers);
        if (rv != 0) throw new Win32Exception(rv, "SetDnsServers failed.");
        Log.Debug("Set DNS servers: {Servers}", string.Join(", ", config.DnsServers.Select(s => s.ToString())));
    }
}

static bool IsRunningAsAdmin()
{
    using var identity = WindowsIdentity.GetCurrent();
    var principal = new WindowsPrincipal(identity);
    return principal.IsInRole(WindowsBuiltInRole.Administrator);
}
