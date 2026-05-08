// Throwaway dev tool — Phase 1 smoke test for Win32Calls.WFP/KillSwitchFilters.cs.
// Run as admin. Installs the OnConnected filter set, prompts you to verify with
// `netsh wfp show filters` (look for the Guardian Kill Switch Sublayer), then removes.
//
//   cd GuardianConnectSDK/Tools/KillSwitchSmokeTest
//   dotnet run -c Release -p:Platform=x64
//
// IMPORTANT: by default the test installs full block-all (no LAN exception). If you're
// running this over RDP / SSH / Remote Desktop you WILL be disconnected the moment the
// transaction commits. Pass `--allow-lan` to install the LAN-permit filter set as well
// (RFC1918, link-local, multicast, broadcast on v4; fe80::/10 + fc00::/7 on v6) so RDP
// keeps working:
//
//   dotnet run -c Release -p:Platform=x64 -- --allow-lan
//
// Not part of the SDK solution. Not signed. Not packaged. Don't ship.

using System.Security.Principal;
using Serilog;
using Win32Calls.WFP;
using Windows.Win32.Foundation;

var allowLan = args.Any(a => a.Equals("--allow-lan", StringComparison.OrdinalIgnoreCase));

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console()
    .CreateLogger();

if (!IsRunningAsAdmin())
{
    Log.Error("Must be run as Administrator (FwpmEngineOpen0 needs ADMIN access).");
    return 1;
}

Log.Information("=== Kill Switch smoke test (Phase 1 primitives) ===");
Log.Information("Sublayer GUID: {Guid}", KillSwitchFilters.SublayerDynamicGuid);
Log.Information("LAN exception: {AllowLan} (pass --allow-lan to enable; required if running over RDP)",
                allowLan ? "ON" : "OFF");
if (!allowLan)
    Log.Warning("Block-all is in effect with no LAN exception. Remote-desktop sessions will be severed.");

HANDLE engine = HANDLE.Null;
var filterIds = new List<ulong>();
var inTransaction = false;

try
{
    Log.Information("Opening dynamic engine ...");
    engine = KillSwitchFilters.OpenDynamicEngine();
    if (engine == HANDLE.Null)
    {
        Log.Error("OpenDynamicEngine returned HANDLE.Null. Aborting.");
        return 1;
    }

    Log.Information("Registering dynamic sublayer ...");
    var subResult = KillSwitchFilters.EnsureDynamicSublayerRegistered(engine);
    if (subResult != 0)
    {
        Log.Error("EnsureDynamicSublayerRegistered failed: 0x{Result:X8}", subResult);
        return 1;
    }

    Log.Information("Beginning transaction ...");
    if (KillSwitchFilters.BeginTransaction(engine) != 0)
    {
        Log.Error("BeginTransaction failed.");
        return 1;
    }
    inTransaction = true;

    AddAndTrack(engine, filterIds, "BlockAllOutboundV4", KillSwitchFilters.AddBlockAllOutboundV4);
    AddAndTrack(engine, filterIds, "BlockAllInboundV4",  KillSwitchFilters.AddBlockAllInboundV4);
    AddAndTrack(engine, filterIds, "BlockAllOutboundV6", KillSwitchFilters.AddBlockAllOutboundV6);
    AddAndTrack(engine, filterIds, "BlockAllInboundV6",  KillSwitchFilters.AddBlockAllInboundV6);

    AddAndTrack(engine, filterIds, "PermitLoopbackOutboundV4", KillSwitchFilters.AddPermitLoopbackOutboundV4);
    AddAndTrack(engine, filterIds, "PermitLoopbackInboundV4",  KillSwitchFilters.AddPermitLoopbackInboundV4);
    AddAndTrack(engine, filterIds, "PermitLoopbackOutboundV6", KillSwitchFilters.AddPermitLoopbackOutboundV6);
    AddAndTrack(engine, filterIds, "PermitLoopbackInboundV6",  KillSwitchFilters.AddPermitLoopbackInboundV6);

    AddAndTrack(engine, filterIds, "PermitDhcpOutboundV4", KillSwitchFilters.AddPermitDhcpOutboundV4);
    AddAndTrack(engine, filterIds, "PermitDhcpInboundV4",  KillSwitchFilters.AddPermitDhcpInboundV4);

    if (allowLan)
    {
        Log.Information("Adding LAN permit filters (--allow-lan) ...");
        var lanIds = KillSwitchFilters.AddPermitLanAll(engine);
        foreach (var id in lanIds) Log.Information("  [+] LAN permit filterId={Id}", id);
        filterIds.AddRange(lanIds);
    }

    Log.Information("Committing transaction ...");
    if (KillSwitchFilters.CommitTransaction(engine) != 0)
    {
        Log.Error("CommitTransaction failed.");
        return 1;
    }
    inTransaction = false;

    Log.Information("");
    Log.Information("All {Count} filters installed. Tracked IDs: {Ids}", filterIds.Count, string.Join(", ", filterIds));
    Log.Information("Verify in another admin shell:");
    Log.Information("  netsh wfp show filters");
    Log.Information("Look for the 'Guardian Kill Switch Sublayer' GUID in the output.");
    Log.Information("");
    Log.Information("Press ENTER to remove all filters and exit (or Ctrl+C to leave them in place — they'll");
    Log.Information("disappear when this process exits since they're dynamic-session).");
    Console.ReadLine();

    Log.Information("Removing {Count} filters ...", filterIds.Count);
    var ok = KillSwitchFilters.DeleteFiltersById(engine, filterIds);
    Log.Information("DeleteFiltersById: {Result}", ok ? "all succeeded" : "one or more failed (see errors above)");
    return ok ? 0 : 1;
}
catch (Exception ex)
{
    Log.Error(ex, "Smoke test threw.");
    if (inTransaction && engine != HANDLE.Null) KillSwitchFilters.AbortTransaction(engine);
    return 1;
}
finally
{
    if (engine != HANDLE.Null)
    {
        Log.Information("Closing engine handle (any remaining dynamic filters tear down here).");
        KillSwitchFilters.CloseEngine(engine);
    }
    Log.CloseAndFlush();
}

static void AddAndTrack(HANDLE engine, List<ulong> ids, string label, Func<HANDLE, ulong> addFn)
{
    var id = addFn(engine);
    if (id == 0)
    {
        Log.Warning("  [skipped] {Label} returned filterId=0 (see errors above)", label);
        return;
    }
    ids.Add(id);
    Log.Information("  [+] {Label} filterId={Id}", label, id);
}

static bool IsRunningAsAdmin()
{
    using var identity = WindowsIdentity.GetCurrent();
    var principal = new WindowsPrincipal(identity);
    return principal.IsInRole(WindowsBuiltInRole.Administrator);
}
